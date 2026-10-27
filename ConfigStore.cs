using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace StreamCell
{
    public sealed class DeckButton
    {
        public string id = "";
        public int slot;
        public string label = "Botão";
        public string icon = "grid";
        public string color = "#8576ff";
        public string type = "media";
        public string target = "";
        public string arguments = "";
        public int soundIndex = 1;
        public string keys = "";
        public bool confirm;
        public string image = "";
    }
    public sealed class DeckPage
    {
        public string id = "main";
        public string name = "Principal";
        public int columns = 5;
        public int rows = 3;
        public List<DeckButton> buttons = new List<DeckButton>();
    }
    public sealed class ObsSettings
    {
        public int port = 4455;
        public string protectedPassword = "";
        public string password;
        public bool clearPassword;
    }
    public sealed class DeckConfig
    {
        public string title = "Meu StreamCell";
        public int columns = 5;
        public long revision = 1;
        public ObsSettings obs = new ObsSettings();
        public string soundpadPath = "";
        public List<DeckPage> pages = new List<DeckPage> { new DeckPage() };
    }
    internal static class Codec
    {
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 30 }; }
        public static string Encode(object value) { return Json().Serialize(value); }
        public static string RandomToken(int bytes)
        {
            byte[] data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return BitConverter.ToString(data).Replace("-", "");
        }
        public static bool Equal(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int result = 0; for (int i = 0; i < a.Length; i++) result |= a[i] ^ b[i];
            return result == 0;
        }
    }
    internal sealed class ConfigStore
    {
        private readonly object gate = new object();
        private readonly string path;
        private DeckConfig current;
        public string LoadWarning { get; private set; }
        public ConfigStore(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "config.json");
            current = new DeckConfig();
            if (File.Exists(path))
            {
                try { current = Parse(File.ReadAllText(path, Encoding.UTF8)); Password(current); }
                catch { current = new DeckConfig(); LoadWarning = "Não foi possível ler a configuração. O arquivo foi preservado. Restaure config.json.bak ou corrija o arquivo antes de salvar."; }
            }
        }
        public DeckConfig Snapshot() { lock (gate) return Codec.Json().Deserialize<DeckConfig>(Codec.Encode(current)); }
        public object AdminView()
        {
            var c = Snapshot();
            return new { c.title, c.columns, c.revision, obs = new { c.obs.port, passwordSet = !String.IsNullOrEmpty(c.obs.protectedPassword) }, c.soundpadPath, c.pages };
        }
        public static string Password(DeckConfig c)
        {
            if (String.IsNullOrEmpty(c.obs.protectedPassword)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(c.obs.protectedPassword), null, DataProtectionScope.CurrentUser));
        }
        public DeckConfig Save(string body)
        {
            var next = Parse(body);
            lock (gate)
            {
                if (LoadWarning != null) throw new InvalidOperationException(LoadWarning);
                next.obs.protectedPassword = current.obs.protectedPassword;
                if (next.obs.clearPassword) next.obs.protectedPassword = "";
                else if (!String.IsNullOrEmpty(next.obs.password)) next.obs.protectedPassword = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(next.obs.password), null, DataProtectionScope.CurrentUser));
                next.obs.password = null; next.obs.clearPassword = false;
                next.revision = current.revision + 1;
                string temp = path + ".tmp";
                File.WriteAllText(temp, Codec.Encode(next), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                current = next;
                return Snapshot();
            }
        }
        private static string Clean(string text, int max, string field)
        {
            text = text ?? "";
            if (text.Length > max || text.Any(ch => Char.IsControl(ch))) throw new ArgumentException("Campo inválido: " + field + ".");
            return text.Trim();
        }
        private static void Identifier(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"^[a-zA-Z0-9_-]{1,64}$")) throw new ArgumentException("Identificador inválido.");
        }
        private static DeckConfig Parse(string body)
        {
            DeckConfig c;
            try { c = Codec.Json().Deserialize<DeckConfig>(body); }
            catch { throw new ArgumentException("Configuração JSON inválida."); }
            if (c == null || c.obs == null || c.pages == null || c.pages.Count < 1 || c.pages.Count > 20) throw new ArgumentException("Use entre 1 e 20 páginas.");
            c.title = Clean(c.title, 60, "nome do deck");
            if (c.title.Length == 0) c.title = "Meu StreamCell";
            c.columns = 5;
            c.soundpadPath = Clean(c.soundpadPath, 1024, "Soundpad");
            if (c.obs.port < 1 || c.obs.port > 65535 || (c.obs.password != null && c.obs.password.Length > 512)) throw new ArgumentException("Configuração OBS inválida.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            int imageCount = 0;
            string[] types = { "app", "url", "soundpad", "soundpad_stop", "hotkey", "media", "obs_scene", "obs_mute", "obs_stream", "obs_record" };
            foreach (var p in c.pages)
            {
                if (p == null || p.buttons == null || p.buttons.Count > 48) throw new ArgumentException("Cada página aceita até 48 botões.");
                if (p.columns < 2 || p.columns > 8 || p.rows < 2 || p.rows > 6 || p.columns * p.rows > 48) throw new ArgumentException("Escolha uma grade de 2 a 8 colunas e 2 a 6 linhas.");
                Identifier(p.id); if (!ids.Add(p.id)) throw new ArgumentException("Identificador repetido.");
                p.name = Clean(p.name, 32, "página");
                if (p.name.Length == 0) throw new ArgumentException("Dê um nome à página.");
                var slots = new HashSet<int>();
                foreach (var b in p.buttons)
                {
                    if (b == null) throw new ArgumentException("Botão inválido.");
                    Identifier(b.id); if (!ids.Add(b.id)) throw new ArgumentException("Identificador repetido.");
                    if (b.slot < 0 || b.slot >= p.columns * p.rows || !slots.Add(b.slot)) throw new ArgumentException("Posição de botão inválida ou repetida.");
                    b.label = Clean(b.label, 48, "nome do botão"); b.icon = Clean(b.icon, 40, "ícone");
                    b.target = Clean(b.target, 2048, "destino"); b.arguments = Clean(b.arguments, 2048, "argumentos"); b.keys = Clean(b.keys, 120, "atalho");
                    b.image = Clean(b.image, 220000, "imagem da tecla");
                    if (b.image.Length > 0) { imageCount++; if (!Regex.IsMatch(b.image, @"^data:image/(png|jpeg|webp);base64,[A-Za-z0-9+/]+={0,2}$")) throw new ArgumentException("Use uma imagem PNG, JPG ou WebP válida."); }
                    if (b.label.Length == 0 || !types.Contains(b.type) || b.color == null || !Regex.IsMatch(b.color, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Revise nome, tipo e cor do botão.");
                    if (b.type == "soundpad" && (b.soundIndex < 1 || b.soundIndex > 100000)) throw new ArgumentException("O índice do som deve ser maior que zero.");
                    if ((b.type == "app" || b.type == "url" || b.type == "obs_scene" || b.type == "obs_mute") && b.target.Length == 0) throw new ArgumentException("Escolha o destino do botão.");
                    if (b.type == "url") { Uri uri; if (!Uri.TryCreate(b.target, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new ArgumentException("Use um endereço http ou https."); }
                    if (b.type == "app" && (!Path.IsPathRooted(b.target) || !new[] { ".exe", ".lnk", ".url" }.Contains(Path.GetExtension(b.target).ToLowerInvariant()))) throw new ArgumentException("Escolha um executável ou atalho do Windows.");
                    if (b.type == "hotkey" && b.keys.Length == 0) throw new ArgumentException("Informe as teclas do atalho.");
                    if (b.type == "media" && !new[] { "play_pause", "next", "previous", "volume_up", "volume_down", "mute" }.Contains(b.target)) throw new ArgumentException("Comando de mídia inválido.");
                }
            }
            if (imageCount > 48) throw new ArgumentException("Use imagens em até 48 teclas por deck para manter as atualizações rápidas.");
            return c;
        }
    }
}
