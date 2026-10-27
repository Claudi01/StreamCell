using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("StreamCell")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
namespace StreamCell
{
    internal sealed class Request
    {
        public string Method, Path, Body;
        public IPAddress Remote;
        public Dictionary<string,string> Headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public string Header(string key) { string value; return Headers.TryGetValue(key, out value) ? value : ""; }
    }
    internal sealed class ApiError : Exception
    {
        public int Status;
        public ApiError(int status, string message) : base(message) { Status = status; }
    }
    internal sealed class PhoneSession
    {
        public DateTime LastSeen = DateTime.UtcNow;
        public DateTime LastAction = DateTime.MinValue;
        public string UserId = "";
        public string Username = "";
        public bool IsAdmin;
    }
    internal sealed class PairAttempts
    {
        public int Count;
        public DateTime Start = DateTime.UtcNow;
    }
    internal static class Program
    {
        internal const string Version = "0.3.0";
        private static int port = 8765;
        private static string adminToken = Codec.RandomToken(32);
        private static string pairCode = Codec.RandomToken(4);
        private static ConfigStore store;
        private static AccountStore accounts;
        private static ObsBridge obs;
        private static readonly object sessionsGate = new object();
        private static readonly object actionGate = new object();
        private static readonly Dictionary<string,PhoneSession> sessions = new Dictionary<string,PhoneSession>(StringComparer.Ordinal);
        private static readonly Dictionary<string,PairAttempts> attempts = new Dictionary<string,PairAttempts>();
        private static readonly Semaphore clients = new Semaphore(24,24);
        private static readonly HashSet<string> hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static List<string> addresses = new List<string>();
        private static volatile bool running = true;
        [STAThread]
        private static int Main(string[] args)
        {
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamCell");
            bool noBrowser = false;
            bool explicitDataDirectory = false;
            try
            {
                for(int i = 0; i < args.Length; i++)
                {
                    if(args[i] == "--no-browser") noBrowser = true;
                    else if(args[i] == "--port" && i + 1 < args.Length) port = Int32.Parse(args[++i]);
                    else if(args[i] == "--data-dir" && i + 1 < args.Length) { dataDir = Path.GetFullPath(args[++i]); explicitDataDirectory = true; }
                    else throw new ArgumentException("Argumento desconhecido.");
                }
                if(port < 1024 || port > 65535) throw new ArgumentException("Porta inválida.");
                store = new ConfigStore(dataDir);
                accounts = new AccountStore(dataDir);
                obs = new ObsBridge();
                var config = store.Snapshot(); obs.Configure(config.obs.port, ConfigStore.Password(config));
                RefreshAddresses();
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start(32);
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; running = false; listener.Stop(); };
                string adminUrl = "http://127.0.0.1:" + port + "/admin#" + adminToken;
                if(noBrowser)
                {
                    if(explicitDataDirectory) File.WriteAllText(Path.Combine(dataDir,"test-info.json"),Codec.Encode(new {adminToken=adminToken,pairCode=pairCode}),new UTF8Encoding(false));
                    Console.WriteLine("StreamCell test server ready.");
                    Console.WriteLine("Editor token: " + adminToken);
                    Console.WriteLine("Código de pareamento: " + pairCode);
                    while(running)
                    {
                        TcpClient client;
                        try { client = listener.AcceptTcpClient(); } catch(SocketException) { if(!running) break; throw; }
                        if(!clients.WaitOne(0)) { client.Close(); continue; }
                        ThreadPool.QueueUserWorkItem(delegate { try { Handle(client); } finally { client.Close(); clients.Release(); } });
                    }
                }
                else
                {
                    Thread acceptThread = new Thread(delegate() { AcceptLoop(listener); });
                    acceptThread.IsBackground = true; acceptThread.Name = "StreamCell Local Network"; acceptThread.Start();
                    try { DesktopHost.RegisterResolver(); DesktopHost.Run(adminUrl, dataDir); }
                    finally { running = false; listener.Stop(); acceptThread.Join(2000); obs.Dispose(); }
                }
                return 0;
            }
            catch(SocketException ex) { if(noBrowser && explicitDataDirectory) File.WriteAllText(Path.Combine(dataDir,"startup-error.txt"),ex.ToString()); else MessageBox.Show("A porta está ocupada. Feche outra instância do StreamCell ou use --port 8766.","StreamCell",MessageBoxButtons.OK,MessageBoxIcon.Warning); return 1; }
            catch(Exception ex) { if(noBrowser && explicitDataDirectory) File.WriteAllText(Path.Combine(dataDir,"startup-error.txt"),ex.ToString()); else MessageBox.Show("Não foi possível iniciar o StreamCell: " + ex.Message,"StreamCell",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
        }
        private static void AcceptLoop(TcpListener listener)
        {
            while(running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch(SocketException) { if(!running) break; continue; }
                if(!clients.WaitOne(0)) { client.Close(); continue; }
                ThreadPool.QueueUserWorkItem(delegate { try { Handle(client); } finally { client.Close(); clients.Release(); } });
            }
        }
        private static void RefreshAddresses()
        {
            hosts.Add("localhost"); hosts.Add("127.0.0.1");
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if(adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach(var info in adapter.GetIPProperties().UnicastAddresses)
                {
                    var ip = info.Address;
                    if(ip.AddressFamily != AddressFamily.InterNetwork || !LocalAddress(ip)) continue;
                    hosts.Add(ip.ToString()); addresses.Add("http://" + ip + ":" + port + "/");
                }
            }
            addresses = addresses.Distinct().ToList();
        }
        private static bool LocalAddress(IPAddress ip)
        {
            if(IPAddress.IsLoopback(ip)) return true;
            byte[] bytes = ip.GetAddressBytes();
            return bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >=16 && bytes[1] <=31) || (bytes[0] == 192 && bytes[1] ==168) || (bytes[0] ==169 && bytes[1] ==254));
        }
        private static void Handle(TcpClient client)
        {
            using(var stream = client.GetStream())
            {
                try
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
                    var remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                    if(!LocalAddress(remote)) throw new ApiError(403,"Acesso permitido somente na rede local.");
                    var req = ReadRequest(stream); req.Remote = remote;
                    ValidateHost(req);
                    Route(stream, req);
                }
                catch(ApiError ex) { TryError(stream, ex.Status, ex.Message); }
                catch(ArgumentException ex) { TryError(stream, 400, ex.Message); }
                catch(InvalidOperationException ex) { TryError(stream, 409, ex.Message); }
                catch(Exception ex) { Console.Error.WriteLine("Falha interna: " + ex.GetType().Name); TryError(stream, 500, "Não foi possível concluir a ação. Revise a configuração no PC."); }
            }
        }
        private static Request ReadRequest(NetworkStream stream)
        {
            List<byte> head = new List<byte>();
            while(true)
            {
                int b = stream.ReadByte(); if(b < 0) throw new ApiError(400,"Requisição incompleta.");
                head.Add((byte)b);
                int n=head.Count;
                if(n > 16384) throw new ApiError(431,"Cabeçalho muito grande.");
                if(n>=4 && head[n-4]==13 && head[n-3]==10 && head[n-2]==13 && head[n-1]==10) break;
            }
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
            string[] first=lines[0].Split(' ');
            if(first.Length!=3 || first[2]!="HTTP/1.1" || !first[1].StartsWith("/") || first[1].StartsWith("//")) throw new ApiError(400,"Requisição inválida.");
            var req=new Request {Method=first[0],Path=first[1].Split('?')[0]};
            if(req.Method!="GET" && req.Method!="POST") throw new ApiError(405,"Método não permitido.");
            foreach(string line in lines.Skip(1))
            {
                if(line.Length==0) continue;
                int colon=line.IndexOf(':'); if(colon<=0 || Char.IsWhiteSpace(line[0])) throw new ApiError(400,"Cabeçalho inválido.");
                string key=line.Substring(0,colon), value=line.Substring(colon+1).Trim();
                if(req.Headers.ContainsKey(key)) throw new ApiError(400,"Cabeçalho repetido.");
                req.Headers.Add(key,value);
            }
            if(req.Headers.ContainsKey("Transfer-Encoding")) throw new ApiError(400,"Formato de transferência não suportado.");
            int length=0;
            if(req.Headers.ContainsKey("Content-Length") && (!Int32.TryParse(req.Header("Content-Length"),out length) || length<0)) throw new ApiError(400,"Tamanho inválido.");
            if(length>33554432) throw new ApiError(413,"Configuração muito grande.");
            if(req.Method=="POST" && !req.Header("Content-Type").StartsWith("application/json",StringComparison.OrdinalIgnoreCase)) throw new ApiError(415,"Use JSON.");
            var body = new byte[length]; int offset=0;
            while(offset<length) {int n=stream.Read(body,offset,length-offset); if(n==0) throw new ApiError(400,"Corpo incompleto."); offset+=n;}
            req.Body=Encoding.UTF8.GetString(body);
            return req;
        }
        private static void ValidateHost(Request req)
        {
            Uri host;
            if(!Uri.TryCreate("http://"+req.Header("Host"),UriKind.Absolute,out host) || host.UserInfo.Length!=0 || host.Port!=port || host.AbsolutePath!="/" || !hosts.Contains(host.Host)) throw new ApiError(403,"Endereço não autorizado.");
            string origin=req.Header("Origin");
            if(origin.Length>0 && !String.Equals(origin,"http://"+req.Header("Host"),StringComparison.OrdinalIgnoreCase)) throw new ApiError(403,"Origem não autorizada.");
        }
        private static void RequireAdmin(Request req)
        {
            Uri host = new Uri("http://"+req.Header("Host"));
            if(!IPAddress.IsLoopback(req.Remote) || (host.Host!="127.0.0.1" && host.Host!="localhost") || !Codec.Equal(req.Header("X-StreamCell-Admin"),adminToken)) throw new ApiError(403,"Abra o editor pelo endereço exibido no PC.");
        }
        private static PhoneSession RequirePhone(Request req)
        {
            lock(sessionsGate)
            {
                PhoneSession session;
                if(!sessions.TryGetValue(req.Header("X-StreamCell-Token"),out session) || session.LastSeen<DateTime.UtcNow.AddDays(-30) || !accounts.Exists(session.UserId)) throw new ApiError(401,"Pareie este celular novamente.");
                session.LastSeen=DateTime.UtcNow; return session;
            }
        }
        private static Dictionary<string,object> Body(Request req)
        {
            try { var value=Codec.Json().Deserialize<Dictionary<string,object>>(req.Body); if(value==null) throw new Exception(); return value; }
            catch {throw new ApiError(400,"JSON inválido.");}
        }
        private static string Text(Dictionary<string,object> body,string key)
        {
            object value;return body.TryGetValue(key,out value) && value is string ? (string)value : "";
        }
        private static void Route(NetworkStream stream, Request req)
        {
            if(req.Path.StartsWith("/api/admin/",StringComparison.Ordinal))
            {
                RequireAdmin(req);
                if(req.Method=="GET" && req.Path=="/api/admin/users") {SendJson(stream,new {users=accounts.List()});return;}
                if(req.Method=="POST" && req.Path=="/api/admin/users/create")
                {
                    var body=Body(req); object created; bool admin=accounts.Create(Text(body,"username"),Text(body,"password"),out created);
                    SendJson(stream,new {user=created,firstAdmin=admin});return;
                }
                if(req.Method=="POST" && req.Path=="/api/admin/users/remove")
                {
                    string id=Text(Body(req),"id");accounts.Remove(id);
                    lock(sessionsGate) foreach(var expired in sessions.Where(x=>x.Value.UserId==id).Select(x=>x.Key).ToList()) sessions.Remove(expired);
                    SendJson(stream,new {users=accounts.List()});return;
                }
                if(req.Method=="GET" && req.Path=="/api/admin/config") { SendJson(stream,store.AdminView());return; }
                if(req.Method=="POST" && req.Path=="/api/admin/config")
                {
                    var c=store.Save(req.Body);obs.Configure(c.obs.port,ConfigStore.Password(c));SendJson(stream,store.AdminView());return;
                }
                if(req.Method=="GET" && req.Path=="/api/admin/info") {SendJson(stream,AdminInfo());return;}
                if(req.Method=="GET" && req.Path=="/api/admin/apps") {SendJson(stream,new{apps=DesktopActions.GetApps()});return;}
                if(req.Method=="GET" && req.Path=="/api/admin/obs") {SendJson(stream,obs.GetSnapshot());return;}
                if(req.Method=="POST" && req.Path=="/api/admin/browse") {SendJson(stream,new{path=DesktopActions.Browse(Text(Body(req),"purpose"))});return;}
                if(req.Method=="POST" && req.Path=="/api/admin/revoke")
                {
                    lock(sessionsGate) {sessions.Clear();pairCode=Codec.RandomToken(4);attempts.Clear();}
                    SendJson(stream,AdminInfo());return;
                }
            }
            if(req.Method=="POST" && req.Path=="/api/pair") {Pair(stream,req);return;}
            if(req.Method=="GET" && req.Path=="/api/deck") {RequirePhone(req);SendJson(stream,MobileDeck());return;}
            if(req.Method=="POST" && req.Path=="/api/execute") {Execute(stream,req);return;}
            if(req.Path.StartsWith("/api/",StringComparison.Ordinal)) throw new ApiError(404,"Ação não encontrada.");
            if(req.Method!="GET") throw new ApiError(405,"Método não permitido.");
            string asset=req.Path=="/" ? "index.html" : req.Path=="/admin" || req.Path=="/admin/" ? "admin.html" : req.Path.TrimStart('/');
            if(asset.StartsWith("admin",StringComparison.Ordinal) && !IPAddress.IsLoopback(req.Remote)) throw new ApiError(403,"A configuração é feita somente no PC.");
            string[] assets={"index.html","admin.html","app.js","admin.js","shared.js","styles.css","manifest.webmanifest","icon.svg","qrcode.js"};
            if(!assets.Contains(asset)) throw new ApiError(404,"Página não encontrada.");
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("StreamCell.Assets."+asset))
            {
                if(resource==null) throw new ApiError(404,"Página não encontrada.");
                using(var memory=new MemoryStream()) {resource.CopyTo(memory);Write(stream,200,Mime(asset),memory.ToArray());}
            }
        }
        private static object AdminInfo()
        {
            lock(sessionsGate) return new { version=Version,pairCode,addresses,pairLinks=addresses.Select(a=>new { address=a,url="streamcell://pair?server="+Uri.EscapeDataString(a)+"&code="+pairCode }).ToArray(),phoneCount=sessions.Values.Count(s=>s.LastSeen>DateTime.UtcNow.AddMinutes(-2)),warning=store.LoadWarning };
        }
        private static void Pair(NetworkStream stream,Request req)
        {
            var body=Body(req);
            if(body.Keys.Any(k=>k!="code" && k!="username" && k!="password")) throw new ApiError(400,"Campos de conexão inválidos.");
            string code=Text(body,"code").Replace("-","").Replace(" ","").ToUpperInvariant();
            lock(sessionsGate)
            {
                string key=req.Remote.ToString();PairAttempts attempt;
                if(!attempts.TryGetValue(key,out attempt) || attempt.Start<DateTime.UtcNow.AddMinutes(-1)) {attempt=new PairAttempts();attempts[key]=attempt;}
                if(++attempt.Count>10) throw new ApiError(429,"Muitas tentativas. Aguarde um minuto.");
                if(!Codec.Equal(code,pairCode)) throw new ApiError(401,"Código incorreto. Confira o editor no PC.");
            }
            var user=accounts.Authenticate(Text(body,"username"),Text(body,"password"));
            lock(sessionsGate)
            {
                if(!Codec.Equal(code,pairCode)) throw new ApiError(401,"O código mudou. Leia o QR novamente.");
                foreach(var expired in sessions.Where(x=>x.Value.LastSeen<DateTime.UtcNow.AddDays(-30)).Select(x=>x.Key).ToList()) sessions.Remove(expired);
                if(sessions.Count>=32) throw new ApiError(409,"Limite de dispositivos. Revogue os acessos pelo PC.");
                string token=Codec.RandomToken(32);
                sessions.Add(token,new PhoneSession { UserId=user.id, Username=user.username, IsAdmin=user.role=="admin" });
                SendJson(stream,new { token,username=user.username });
            }
        }
        private static object MobileDeck()
        {
            var c=store.Snapshot();var status=obs.GetSnapshot();
            return new {c.title,c.columns,c.revision,pages=c.pages.Select(p=>new{p.id,p.name,p.columns,p.rows,buttons=p.buttons.Select(b=>new {
                b.id,b.slot,b.label,b.icon,b.color,b.image,b.type,
                requiresConfirmation=b.confirm || b.type=="obs_stream",
                active=IsActive(b,status),available=!b.type.StartsWith("obs_",StringComparison.Ordinal) || status.connected
            }).ToArray()}).ToArray(), status=new{obsConnected=status.connected}};
        }
        private static bool IsActive(DeckButton b,ObsSnapshot s)
        {
            bool muted;
            if(b.type=="obs_scene") return s.currentScene==b.target;
            if(b.type=="obs_stream") return s.streaming;
            if(b.type=="obs_record") return s.recording;
            return b.type=="obs_mute" && s.muted!=null && s.muted.TryGetValue(b.target,out muted) && muted;
        }
        private static void Execute(NetworkStream stream,Request req)
        {
            var session=RequirePhone(req);var body=Body(req);string id=Text(body,"id");
            if(body.Keys.Any(k=>k!="id" && k!="confirmed")) throw new ApiError(400,"Envie somente o identificador do botão.");
            lock(actionGate)
            {
                if(session.LastAction>DateTime.UtcNow.AddMilliseconds(-180)) throw new ApiError(429,"Aguarde antes de repetir.");
                var c=store.Snapshot();var b=c.pages.SelectMany(p=>p.buttons).FirstOrDefault(x=>x.id==id);
                if(b==null) throw new ApiError(404,"Este botão não está mais configurado.");
                object confirmed;
                if((b.confirm || b.type=="obs_stream") && (!body.TryGetValue("confirmed",out confirmed) || !(confirmed is bool) || !(bool)confirmed)) throw new ApiError(409,"Confirme esta ação no celular.");
                session.LastAction=DateTime.UtcNow;
                if(b.type.StartsWith("obs_",StringComparison.Ordinal)) obs.Execute(b.type,b.target);
                else DesktopActions.Execute(b.type,b.target,b.arguments,b.soundIndex,b.keys,c.soundpadPath);
                SendJson(stream,new{ok=true,message="Comando enviado ao PC."});
            }
        }
        private static string Mime(string file)
        {
            if(file.EndsWith(".html")) return "text/html; charset=utf-8";
            if(file.EndsWith(".js")) return "text/javascript; charset=utf-8";
            if(file.EndsWith(".css")) return "text/css; charset=utf-8";
            if(file.EndsWith(".svg")) return "image/svg+xml";
            return "application/manifest+json; charset=utf-8";
        }
        private static void SendJson(NetworkStream stream,object obj) {Write(stream,200,"application/json; charset=utf-8",Encoding.UTF8.GetBytes(Codec.Encode(obj)));}
        private static void TryError(NetworkStream stream,int status,string message) {try{Write(stream,status,"application/json; charset=utf-8",Encoding.UTF8.GetBytes(Codec.Encode(new{error=message})));}catch{}}
        private static void Write(NetworkStream stream,int status,string type,byte[] data)
        {
            string header="HTTP/1.1 "+status+" "+(status==200 ? "OK" : "Error")+"\r\nContent-Type: "+type+"\r\nContent-Length: "+data.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'\r\n\r\n";
            var bytes=Encoding.ASCII.GetBytes(header);stream.Write(bytes,0,bytes.Length);stream.Write(data,0,data.Length);
        }
    }
}
