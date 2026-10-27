using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StreamCell
{
    // Only call Execute with values resolved from the PC's saved configuration.
    internal static class DesktopActions
    {
        private static readonly object InputLock = new object();
        private static readonly object BrowseLock = new object();
        private const uint KeyUp = 0x0002;
        private const uint ExtendedKey = 0x0001;

        public static void Execute(string type, string target, string arguments, int soundIndex, string keys, string soundpadPath)
        {
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "app":
                    Start(ValidateAppPath(target), ValidateArguments(arguments), true);
                    return;
                case "url":
                    Uri uri;
                    string address = (target ?? "").Trim();
                    if (address.Length > 8192 || address.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                        !Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                        String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo))
                        throw new InvalidOperationException("Configure um endereço válido começando com http:// ou https://, sem credenciais no link.");
                    Start(uri.AbsoluteUri, "", true);
                    return;
                case "soundpad":
                    if (soundIndex < 1) throw new InvalidOperationException("O número do som no Soundpad deve ser maior que zero.");
                    Start(FindSoundpad(soundpadPath), "-rc DoPlaySound(" + soundIndex.ToString(CultureInfo.InvariantCulture) + ")", false);
                    return;
                case "soundpad_stop":
                    Start(FindSoundpad(soundpadPath), "-rc DoStopSound()", false);
                    return;
                case "hotkey":
                    SendKeys(ParseHotkey(keys));
                    return;
                case "media":
                    SendKeys(new List<ushort> { MediaKey(target) });
                    return;
                default:
                    throw new InvalidOperationException("Tipo de ação não reconhecido. Confira este botão no editor do PC.");
            }
        }

        public static List<Dictionary<string, object>> GetApps()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.Programs), paths);
            CollectShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), paths);
            return paths.OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new Dictionary<string, object>
                {
                    { "name", Path.GetFileNameWithoutExtension(path) }, { "path", path }
                }).ToList();
        }

        public static string Browse(string purpose)
        {
            purpose = (purpose ?? "").Trim().ToLowerInvariant();
            if (purpose != "app" && purpose != "soundpad")
                throw new InvalidOperationException("Seleção de arquivo não reconhecida.");
            string selected = "";
            Exception failure = null;
            bool isSoundpad = purpose == "soundpad";
            lock (BrowseLock)
            {
                var thread = new Thread(delegate()
                {
                    try
                    {
                        using (var dialog = new OpenFileDialog())
                        {
                            dialog.Title = isSoundpad ? "Selecionar o Soundpad.exe" : "Selecionar aplicativo ou atalho";
                            dialog.Filter = isSoundpad ? "Soundpad (Soundpad.exe)|Soundpad.exe" : "Aplicativos e atalhos (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url";
                            dialog.CheckFileExists = true;
                            dialog.CheckPathExists = true;
                            dialog.Multiselect = false;
                            dialog.RestoreDirectory = true;
                            dialog.DereferenceLinks = false;
                            if (dialog.ShowDialog() == DialogResult.OK)
                                selected = isSoundpad ? ValidateSoundpadPath(dialog.FileName) : ValidateAppPath(dialog.FileName);
                        }
                    }
                    catch (Exception error) { failure = error; }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            if (failure != null) throw new InvalidOperationException("Não foi possível selecionar o arquivo. Tente novamente no PC.");
            return selected;
        }

        private static void CollectShortcuts(string root, HashSet<string> paths)
        {
            if (String.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        string extension = Path.GetExtension(file);
                        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                            paths.Add(Path.GetFullPath(file));
                    }
                    foreach (string child in Directory.GetDirectories(directory))
                    {
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (SecurityException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
            }
        }

        private static string ValidateAppPath(string value)
        {
            string path = NormalizePath(value);
            string extension = Path.GetExtension(path);
            if ((!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                 !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                 !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) || !File.Exists(path))
                throw new InvalidOperationException("Selecione no PC um aplicativo .exe ou um atalho .lnk/.url existente.");
            return path;
        }

        private static string NormalizePath(string value)
        {
            try
            {
                string path = Environment.ExpandEnvironmentVariables((value ?? "").Trim());
                if (path.Length > 1 && path[0] == '"' && path[path.Length - 1] == '"') path = path.Substring(1, path.Length - 2);
                if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new ArgumentException();
                return Path.GetFullPath(path);
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is NotSupportedException) && !(error is IOException) && !(error is SecurityException)) throw;
                throw new InvalidOperationException("Selecione um caminho completo e válido no computador.");
            }
        }

        private static string ValidateArguments(string arguments)
        {
            arguments = arguments ?? "";
            if (arguments.Length > 8192 || arguments.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new InvalidOperationException("Os argumentos do aplicativo são inválidos ou muito longos.");
            return arguments;
        }

        private static string ValidateSoundpadPath(string value)
        {
            string path = NormalizePath(value);
            if (!Path.GetFileName(path).Equals("Soundpad.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidOperationException("Selecione o arquivo Soundpad.exe instalado no computador.");
            return path;
        }

        private static string FindSoundpad(string configured)
        {
            if (!String.IsNullOrWhiteSpace(configured)) return ValidateSoundpadPath(configured);
            foreach (Process process in Process.GetProcessesByName("Soundpad"))
            {
                using (process)
                {
                    try
                    {
                        string path = process.MainModule.FileName;
                        if (File.Exists(path)) return path;
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (NotSupportedException) { }
                }
            }
            var candidates = new List<string>();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (string root in new[] { programFiles, programFilesX86 })
            {
                if (String.IsNullOrEmpty(root)) continue;
                candidates.Add(Path.Combine(root, "Soundpad", "Soundpad.exe"));
                candidates.Add(Path.Combine(root, "Steam", "steamapps", "common", "Soundpad", "Soundpad.exe"));
            }
            try
            {
                using (RegistryKey steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string steamPath = steam == null ? "" : steam.GetValue("SteamPath", "") as string;
                    if (!String.IsNullOrEmpty(steamPath)) candidates.Add(Path.Combine(steamPath, "steamapps", "common", "Soundpad", "Soundpad.exe"));
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            foreach (string path in candidates)
                if (File.Exists(path)) return path;
            throw new InvalidOperationException("Soundpad não encontrado. Selecione o Soundpad.exe nas configurações do PC.");
        }

        private static void Start(string target, string arguments, bool shell)
        {
            try
            {
                var info = new ProcessStartInfo(target, arguments) { UseShellExecute = shell };
                if (!shell) info.CreateNoWindow = true;
                if (Path.IsPathRooted(target) && File.Exists(target)) info.WorkingDirectory = Path.GetDirectoryName(target);
                using (Process process = Process.Start(info)) { }
            }
            catch (Exception error)
            {
                if (!(error is Win32Exception) && !(error is InvalidOperationException) && !(error is IOException) && !(error is UnauthorizedAccessException) && !(error is SecurityException)) throw;
                throw new InvalidOperationException("O Windows não conseguiu executar esta ação. Confira o aplicativo e suas permissões no PC.");
            }
        }

        private static List<ushort> ParseHotkey(string text)
        {
            if (String.IsNullOrWhiteSpace(text) || text.Length > 120)
                throw new InvalidOperationException("Configure um atalho, por exemplo CTRL+ALT+1.");
            string[] parts = text.ToUpperInvariant().Split('+');
            var result = new List<ushort>();
            ushort primary = 0;
            foreach (string part in parts)
            {
                string token = part.Trim();
                ushort modifier = token == "CTRL" || token == "CONTROL" ? (ushort)0x11 :
                    token == "ALT" ? (ushort)0x12 : token == "SHIFT" ? (ushort)0x10 :
                    token == "WIN" || token == "WINDOWS" || token == "META" ? (ushort)0x5B : (ushort)0;
                if (modifier != 0)
                {
                    if (result.Contains(modifier)) throw new InvalidOperationException("O atalho contém uma tecla modificadora repetida.");
                    result.Add(modifier);
                }
                else
                {
                    if (primary != 0) throw new InvalidOperationException("Use uma tecla principal acompanhada de CTRL, ALT, SHIFT ou WIN.");
                    primary = MainKey(token);
                }
            }
            if (primary == 0) throw new InvalidOperationException("Inclua uma tecla principal no atalho, por exemplo CTRL+ALT+1.");
            if (primary == 0x2E && result.Contains(0x11) && result.Contains(0x12))
                throw new InvalidOperationException("O Windows não permite enviar CTRL+ALT+DELETE por este aplicativo.");
            result.Add(primary);
            return result;
        }

        private static ushort MainKey(string token)
        {
            if (token.Length == 1 && ((token[0] >= 'A' && token[0] <= 'Z') || (token[0] >= '0' && token[0] <= '9')))
                return token[0];
            int function;
            if (token.StartsWith("F", StringComparison.Ordinal) && Int32.TryParse(token.Substring(1), out function) && function >= 1 && function <= 24)
                return (ushort)(0x70 + function - 1);
            switch (token)
            {
                case "ENTER": case "RETURN": return 0x0D;
                case "ESC": case "ESCAPE": return 0x1B;
                case "SPACE": case "ESPACO": case "ESPAÇO": return 0x20;
                case "TAB": return 0x09;
                case "BACKSPACE": return 0x08;
                case "DELETE": case "DEL": return 0x2E;
                case "INSERT": case "INS": return 0x2D;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": case "PGUP": return 0x21;
                case "PAGEDOWN": case "PGDN": return 0x22;
                case "LEFT": return 0x25;
                case "UP": return 0x26;
                case "RIGHT": return 0x27;
                case "DOWN": return 0x28;
                case "PRINTSCREEN": case "PRTSC": return 0x2C;
                case "PAUSE": return 0x13;
                case "CAPSLOCK": return 0x14;
                case "NUMLOCK": return 0x90;
                case "SCROLLLOCK": return 0x91;
                case "PLUS": case "OEMPLUS": return 0xBB;
                case "MINUS": case "OEMMINUS": return 0xBD;
            }
            throw new InvalidOperationException("Tecla não reconhecida. Use letras, números, F1–F24 ou nomes como ENTER e SPACE.");
        }

        private static ushort MediaKey(string target)
        {
            switch ((target ?? "").Trim().ToLowerInvariant())
            {
                case "play_pause": return 0xB3;
                case "next": return 0xB0;
                case "previous": return 0xB1;
                case "volume_up": return 0xAF;
                case "volume_down": return 0xAE;
                case "mute": return 0xAD;
                default: throw new InvalidOperationException("Comando de mídia não reconhecido.");
            }
        }

        private static void SendKeys(List<ushort> keys)
        {
            lock (InputLock)
            {
                var injected = new List<ushort>();
                for (int i = 0; i < keys.Count; i++)
                {
                    // Do not release a modifier that the person is physically holding.
                    if (i < keys.Count - 1 && (GetAsyncKeyState(keys[i]) & 0x8000) != 0) continue;
                    injected.Add(keys[i]);
                }
                var events = new List<INPUT>();
                foreach (ushort key in injected) events.Add(KeyboardInput(key, false));
                for (int i = injected.Count - 1; i >= 0; i--) events.Add(KeyboardInput(injected[i], true));
                INPUT[] input = events.ToArray();
                if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) == input.Length) return;
                // If Windows accepted only part of the sequence, avoid leaving a key down.
                INPUT[] release = injected.AsEnumerable().Reverse().Select(key => KeyboardInput(key, true)).ToArray();
                SendInput((uint)release.Length, release, Marshal.SizeOf(typeof(INPUT)));
                throw new InvalidOperationException("O Windows não aceitou o atalho. Confira se o aplicativo de destino está aberto e usa o mesmo nível de permissão.");
            }
        }

        private static INPUT KeyboardInput(ushort key, bool up)
        {
            bool extended = (key >= 0x21 && key <= 0x28) || key == 0x2C || key == 0x2D || key == 0x2E ||
                key == 0x5B || key == 0x90 || (key >= 0xAD && key <= 0xB3);
            return new INPUT
            {
                type = 1,
                data = new InputUnion { keyboard = new KEYBDINPUT { virtualKey = key, flags = (up ? KeyUp : 0) | (extended ? ExtendedKey : 0) } }
            };
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion data; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort virtualKey; public ushort scan; public uint flags; public uint time; public UIntPtr extra; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int x; public int y; public uint data; public uint flags; public uint time; public UIntPtr extra; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public uint message; public ushort low; public ushort high; }
    }
}
