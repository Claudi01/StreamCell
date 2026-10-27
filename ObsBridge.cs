using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace StreamCell
{
    internal sealed class ObsSnapshot
    {
        public bool connected;
        public string error = "";
        public string currentScene = "";
        public bool streaming;
        public bool recording;
        public List<string> scenes = new List<string>();
        public List<string> inputs = new List<string>();
        public Dictionary<string, bool> muted = new Dictionary<string, bool>(StringComparer.Ordinal);
    }

    // Only this PC talks to OBS. No OBS credentials are exposed by the snapshot.
    internal sealed class ObsBridge : IDisposable
    {
        private const int IoTimeoutMs = 2200;
        private const string OfflineMessage = "OBS desconectado. Abra o OBS e ative o servidor WebSocket no computador.";
        private readonly object ioLock = new object();
        private readonly object snapshotLock = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1048576 };
        private ObsSnapshot snapshot = new ObsSnapshot { error = "Configure a conexão com o OBS no computador." };
        private volatile ClientWebSocket socket;
        private Thread worker;
        private volatile bool disposed;
        private int port = 4455;
        private string password = "";
        private long requestNumber;
        private DateTime nextInventory = DateTime.MinValue;
        private List<string> audioInputs = new List<string>();

        public void Configure(int newPort, string newPassword)
        {
            if (newPort < 1 || newPort > 65535)
                throw new ArgumentException("A porta do OBS deve estar entre 1 e 65535.");
            lock (ioLock)
            {
                ThrowIfDisposed();
                newPassword = newPassword ?? "";
                if (port != newPort || password != newPassword)
                {
                    port = newPort;
                    password = newPassword;
                    Disconnect();
                    audioInputs.Clear();
                    Publish(new ObsSnapshot { error = OfflineMessage });
                }
                if (worker == null)
                {
                    worker = new Thread(PollLoop);
                    worker.IsBackground = true;
                    worker.Name = "StreamCell OBS";
                    worker.Start();
                }
                wake.Set();
            }
        }

        public ObsSnapshot GetSnapshot()
        {
            lock (snapshotLock) return Copy(snapshot);
        }

        public void Execute(string type, string target)
        {
            if (type != "obs_scene" && type != "obs_mute" && type != "obs_stream" && type != "obs_record")
                throw new ArgumentException("Ação do OBS desconhecida.");
            if ((type == "obs_scene" || type == "obs_mute") && String.IsNullOrWhiteSpace(target))
                throw new ArgumentException("Configure o destino desse botão no computador.");

            lock (ioLock)
            {
                ThrowIfDisposed();
                try
                {
                    EnsureConnected();
                    if (type == "obs_scene")
                    {
                        Request("SetCurrentProgramScene", Data("sceneName", target));
                    }
                    else if (type == "obs_mute")
                    {
                        bool muted = Bool(Request("GetInputMute", Data("inputName", target)), "inputMuted");
                        Request("SetInputMute", new Dictionary<string, object> {
                            { "inputName", target }, { "inputMuted", !muted }
                        });
                    }
                    else if (type == "obs_stream")
                    {
                        bool active = Bool(Request("GetStreamStatus", null), "outputActive");
                        Request(active ? "StopStream" : "StartStream", null);
                    }
                    else
                    {
                        bool active = Bool(Request("GetRecordStatus", null), "outputActive");
                        Request(active ? "StopRecord" : "StartRecord", null);
                    }
                    // An acknowledged command must not be reported as failed if a later
                    // status refresh fails. The worker refreshes immediately after this call.
                    wake.Set();
                }
                catch (ObsRequestException)
                {
                    throw new InvalidOperationException("O OBS recusou a ação. Confira a configuração do botão e o estado do OBS no computador.");
                }
                catch (Exception ex)
                {
                    string message = SafeConnectionMessage(ex);
                    Disconnect();
                    MarkDisconnected(message);
                    throw new InvalidOperationException(message);
                }
            }
        }

        private void PollLoop()
        {
            while (!disposed)
            {
                lock (ioLock)
                {
                    if (disposed) break;
                    try
                    {
                        EnsureConnected();
                        RefreshSnapshot();
                    }
                    catch (Exception ex)
                    {
                        Disconnect();
                        MarkDisconnected(SafeConnectionMessage(ex));
                    }
                }
                if (!disposed) wake.WaitOne(2000);
            }
        }

        private void EnsureConnected()
        {
            ThrowIfDisposed();
            if (socket != null && socket.State == WebSocketState.Open) return;
            Disconnect();
            var candidate = new ClientWebSocket();
            candidate.Options.Proxy = null;
            candidate.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            socket = candidate;
            using (var timeout = new CancellationTokenSource(IoTimeoutMs))
            {
                candidate.ConnectAsync(new Uri("ws://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture)), timeout.Token).GetAwaiter().GetResult();
                Dictionary<string, object> hello = Receive(timeout.Token);
                if (Int(hello, "op") != 0)
                    throw new ObsConnectionException("O servidor não usa o protocolo OBS WebSocket 5. Atualize o OBS no computador.");
                Dictionary<string, object> helloData = Object(hello, "d");
                if (Int(helloData, "rpcVersion") < 1)
                    throw new ObsConnectionException("A versão do OBS WebSocket não é compatível. Atualize o OBS no computador.");
                var identify = new Dictionary<string, object> { { "rpcVersion", 1 }, { "eventSubscriptions", 0 } };
                object authentication;
                if (helloData.TryGetValue("authentication", out authentication) && authentication != null)
                {
                    var auth = authentication as Dictionary<string, object>;
                    if (auth == null) throw new ObsConnectionException("O OBS enviou uma autenticação inválida.");
                    identify["authentication"] = Hash(Hash(password + Text(auth, "salt")) + Text(auth, "challenge"));
                }
                Send(new Dictionary<string, object> { { "op", 1 }, { "d", identify } }, timeout.Token);
                Dictionary<string, object> identified = Receive(timeout.Token);
                if (Int(identified, "op") != 2)
                    throw new ObsConnectionException("Não foi possível autenticar a conexão com o OBS.");
            }
            nextInventory = DateTime.MinValue;
        }

        private void RefreshSnapshot()
        {
            var result = new ObsSnapshot { connected = true };
            Dictionary<string, object> sceneData = Request("GetSceneList", null);
            result.currentScene = Text(sceneData, "currentProgramSceneName");
            foreach (object item in Array(sceneData, "scenes"))
            {
                var scene = item as Dictionary<string, object>;
                if (scene != null) AddUnique(result.scenes, Text(scene, "sceneName"));
            }
            result.streaming = Bool(Request("GetStreamStatus", null), "outputActive");
            result.recording = Bool(Request("GetRecordStatus", null), "outputActive");

            bool inventoryDue = DateTime.UtcNow >= nextInventory;
            List<string> candidates = audioInputs;
            if (inventoryDue)
            {
                candidates = new List<string>();
                Dictionary<string, object> list = Request("GetInputList", null);
                foreach (object item in Array(list, "inputs"))
                {
                    var input = item as Dictionary<string, object>;
                    if (input != null) AddUnique(candidates, Text(input, "inputName"));
                }
                Dictionary<string, object> special = Request("GetSpecialInputs", null);
                foreach (string name in new[] { "desktop1", "desktop2", "mic1", "mic2", "mic3", "mic4" })
                    AddUnique(candidates, Text(special, name));
            }
            var found = new List<string>();
            bool removed = false;
            foreach (string input in candidates)
            {
                ThrowIfDisposed();
                try
                {
                    bool mute = Bool(Request("GetInputMute", Data("inputName", input)), "inputMuted");
                    found.Add(input);
                    result.muted[input] = mute;
                }
                catch (ObsRequestException)
                {
                    // Non-audio sources are intentionally absent from the mute selector.
                    removed = true;
                }
            }
            audioInputs = found;
            result.inputs = new List<string>(found);
            if (inventoryDue) nextInventory = DateTime.UtcNow.AddSeconds(10);
            else if (removed) nextInventory = DateTime.MinValue;
            Publish(result);
        }

        private Dictionary<string, object> Request(string requestType, Dictionary<string, object> requestData)
        {
            ThrowIfDisposed();
            string id = (++requestNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var data = new Dictionary<string, object> { { "requestType", requestType }, { "requestId", id } };
            if (requestData != null) data["requestData"] = requestData;
            using (var timeout = new CancellationTokenSource(IoTimeoutMs))
            {
                Send(new Dictionary<string, object> { { "op", 6 }, { "d", data } }, timeout.Token);
                while (true)
                {
                    Dictionary<string, object> response = Receive(timeout.Token);
                    if (Int(response, "op") != 7) continue;
                    Dictionary<string, object> body = Object(response, "d");
                    if (Text(body, "requestId") != id) continue;
                    Dictionary<string, object> status = Object(body, "requestStatus");
                    if (!Bool(status, "result")) throw new ObsRequestException();
                    object responseData;
                    if (body.TryGetValue("responseData", out responseData) && responseData != null)
                    {
                        var responseObject = responseData as Dictionary<string, object>;
                        if (responseObject == null) throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
                        return responseObject;
                    }
                    return new Dictionary<string, object>();
                }
            }
        }

        private void Send(Dictionary<string, object> message, CancellationToken cancellation)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(message));
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation).GetAwaiter().GetResult();
        }

        private Dictionary<string, object> Receive(CancellationToken cancellation)
        {
            byte[] buffer = new byte[8192];
            using (var body = new MemoryStream())
            {
                WebSocketReceiveResult frame;
                do
                {
                    frame = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation).GetAwaiter().GetResult();
                    if (frame.MessageType == WebSocketMessageType.Close)
                    {
                        int code = frame.CloseStatus.HasValue ? (int)frame.CloseStatus.Value : 0;
                        if (code == 4009) throw new ObsConnectionException("Senha do OBS incorreta. Atualize a conexão no computador.");
                        if (code == 4010) throw new ObsConnectionException("A versão do OBS WebSocket não é compatível. Atualize o OBS no computador.");
                        throw new ObsConnectionException(OfflineMessage);
                    }
                    if (frame.MessageType != WebSocketMessageType.Text || body.Length + frame.Count > 1048576)
                        throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
                    body.Write(buffer, 0, frame.Count);
                }
                while (!frame.EndOfMessage);
                var value = json.DeserializeObject(Encoding.UTF8.GetString(body.ToArray())) as Dictionary<string, object>;
                if (value == null) throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
                return value;
            }
        }

        private void Disconnect()
        {
            ClientWebSocket previous = socket;
            socket = null;
            if (previous != null)
            {
                try { previous.Abort(); } catch (ObjectDisposedException) { }
                previous.Dispose();
            }
            nextInventory = DateTime.MinValue;
        }

        private void MarkDisconnected(string message)
        {
            ObsSnapshot offline = GetSnapshot();
            offline.connected = false;
            offline.error = message;
            offline.currentScene = "";
            offline.streaming = false;
            offline.recording = false;
            offline.muted.Clear();
            Publish(offline);
        }

        private void Publish(ObsSnapshot value)
        {
            lock (snapshotLock) snapshot = value;
        }

        private static ObsSnapshot Copy(ObsSnapshot source)
        {
            return new ObsSnapshot {
                connected = source.connected, error = source.error, currentScene = source.currentScene,
                streaming = source.streaming, recording = source.recording,
                scenes = new List<string>(source.scenes), inputs = new List<string>(source.inputs),
                muted = new Dictionary<string, bool>(source.muted, StringComparer.Ordinal)
            };
        }

        private static string SafeConnectionMessage(Exception exception)
        {
            var known = exception as ObsConnectionException;
            if (known != null) return known.Message;
            if (exception is ObsRequestException) return "O OBS não está pronto. Verifique a conexão no computador.";
            return OfflineMessage;
        }

        private static Dictionary<string, object> Data(string key, object value)
        {
            return new Dictionary<string, object> { { key, value } };
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is Dictionary<string, object>))
                throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
            return (Dictionary<string, object>)value;
        }

        private static IEnumerable Array(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is object[]))
                throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
            return (object[])value;
        }

        private static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value is string ? (string)value : "";
        }

        private static bool Bool(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is bool))
                throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
            return (bool)value;
        }

        private static int Int(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is int))
                throw new ObsConnectionException("O OBS enviou uma resposta inválida.");
            return (int)value;
        }

        private static void AddUnique(List<string> names, string value)
        {
            if (!String.IsNullOrWhiteSpace(value) && !names.Contains(value)) names.Add(value);
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("ObsBridge");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            wake.Set();
            // Abort can run alongside ReceiveAsync and prevents Dispose from waiting on I/O.
            ClientWebSocket active = socket;
            if (active != null)
            {
                try { active.Abort(); } catch (ObjectDisposedException) { }
            }
            Thread currentWorker = worker;
            if (currentWorker != null && currentWorker != Thread.CurrentThread)
                currentWorker.Join(IoTimeoutMs + 1000);
            lock (ioLock)
            {
                Disconnect();
                password = "";
            }
            // Leave the small wait handle to GC if an active worker could still touch it.
            if (currentWorker == null || !currentWorker.IsAlive) wake.Dispose();
        }

        private sealed class ObsRequestException : Exception { }
        private sealed class ObsConnectionException : Exception
        {
            public ObsConnectionException(string message) : base(message) { }
        }
    }
}