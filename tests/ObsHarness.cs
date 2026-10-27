using System;
using System.Diagnostics;
using System.Threading;
using StreamCell;
class ObsTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Wait(ObsBridge bridge, Func<ObsSnapshot, bool> predicate, string message) {
        for (int i=0;i<100;i++) { if (predicate(bridge.GetSnapshot())) return; Thread.Sleep(50); }
        throw new Exception(message + ": " + bridge.GetSnapshot().error);
    }
    static int Main(string[] args) {
        try {
            using (var bridge = new ObsBridge()) {
                int port = Int32.Parse(args[0]);
                bridge.Configure(port, "test-password");
                Wait(bridge, s => s.connected, "connect");
                var state = bridge.GetSnapshot();
                Check(state.currentScene == "Main", "scene list");
                Check(state.inputs.Count == 2 && state.inputs.Contains("Mic") && state.inputs.Contains("Desktop"), "audio filtering and special inputs");
                state.scenes.Clear();
                state.muted.Clear();
                Check(bridge.GetSnapshot().scenes.Count == 2 && bridge.GetSnapshot().muted.Count == 2, "snapshot clone");
                bridge.Execute("obs_scene", "Game");
                Wait(bridge, s => s.currentScene == "Game", "scene action");
                bridge.Execute("obs_mute", "Mic");
                Wait(bridge, s => s.muted.ContainsKey("Mic") && s.muted["Mic"], "mute action");
                bridge.Execute("obs_mute", "Mic");
                Wait(bridge, s => s.muted.ContainsKey("Mic") && !s.muted["Mic"], "unmute action");
                bridge.Execute("obs_stream", "");
                Wait(bridge, s => s.streaming, "start stream");
                bridge.Execute("obs_stream", "");
                Wait(bridge, s => !s.streaming, "stop stream");
                bridge.Execute("obs_record", "");
                Wait(bridge, s => s.recording, "start recording");
                bridge.Execute("obs_record", "");
                Wait(bridge, s => !s.recording, "stop recording");
                try { bridge.Execute("obs_scene", "private-target"); throw new Exception("missing rejection"); }
                catch (InvalidOperationException e) { Check(!e.Message.Contains("private-target"), "target leaked"); }
                Check(bridge.GetSnapshot().connected, "request error disconnected");
                bridge.Configure(port, "wrong");
                Wait(bridge, s => !s.connected && s.error.Contains("Senha"), "wrong password");
                bridge.Configure(port, "test-password");
                Wait(bridge, s => s.connected, "reconnect after configure");
                var watch = Stopwatch.StartNew();
                try { bridge.Execute("obs_scene", "Timeout"); throw new Exception("missing timeout"); }
                catch (InvalidOperationException e) { Check(!e.Message.Contains("Timeout"), "raw failure leaked"); }
                Check(watch.ElapsedMilliseconds < 4500, "timeout too slow");
                Wait(bridge, s => s.connected, "reconnect after timeout");
            }
            Console.WriteLine("PASS: auth, fragmented frames, snapshot cloning, inventory filtering, scene/mute/stream/record actions, safe errors, timeout and reconnection.");
            return 0;
        } catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
    }
}