using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StreamCell
{
    internal sealed class LocalUser
    {
        public string id = "";
        public string username = "";
        public string salt = "";
        public string passwordHash = "";
        public string role = "user";
        public DateTime createdUtc = DateTime.UtcNow;
    }

    internal sealed class AccountStore
    {
        private const int Iterations = 210000;
        private readonly object gate = new object();
        private readonly string path;
        private List<LocalUser> users = new List<LocalUser>();

        public AccountStore(string directory)
        {
            path = Path.Combine(directory, "users.json");
            if (!File.Exists(path)) return;
            try
            {
                var parsed = Codec.Json().Deserialize<List<LocalUser>>(File.ReadAllText(path, Encoding.UTF8));
                if (parsed == null || parsed.Any(u => u == null || String.IsNullOrEmpty(u.id) || String.IsNullOrEmpty(u.username) || String.IsNullOrEmpty(u.salt) || String.IsNullOrEmpty(u.passwordHash)))
                    throw new InvalidDataException();
                users = parsed;
            }
            catch { throw new InvalidOperationException("Não foi possível ler os usuários locais. O arquivo users.json foi preservado."); }
        }

        public bool HasAdmin { get { lock (gate) return users.Any(u => u.role == "admin"); } }

        public object List()
        {
            lock (gate) return users.OrderBy(u => u.username, StringComparer.CurrentCultureIgnoreCase).Select(u => new {
                u.id, u.username, role = u.role, created = u.createdUtc.ToLocalTime().ToString("yyyy-MM-dd")
            }).ToArray();
        }

        public bool Create(string username, string password, out object created)
        {
            username = (username ?? "").Trim().Normalize(NormalizationForm.FormKC);
            if (username.Length < 3 || username.Length > 32 || !Regex.IsMatch(username, @"^[\p{L}\p{N}_.-]+$"))
                throw new ArgumentException("O nome de usuário aceita 3 a 32 letras, números, ponto, hífen ou sublinhado.");
            if (password == null || password.Length < 10 || password.Length > 256)
                throw new ArgumentException("A senha deve ter entre 10 e 256 caracteres.");
            lock (gate)
            {
                if (users.Any(u => String.Equals(u.username, username, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Este nome de usuário já existe neste PC.");
                bool firstAdmin = !users.Any(u => u.role == "admin");
                string salt = Codec.RandomToken(16);
                var user = new LocalUser { id = Codec.RandomToken(16).ToLowerInvariant(), username = username, salt = salt,
                    passwordHash = Hash(password, FromHex(salt)), role = firstAdmin ? "admin" : "user", createdUtc = DateTime.UtcNow };
                users.Add(user); Save(); created = new { user.id, user.username, role = user.role, created = user.createdUtc.ToLocalTime().ToString("yyyy-MM-dd") };
                return firstAdmin;
            }
        }

        public LocalUser Authenticate(string username, string password)
        {
            username = (username ?? "").Trim().Normalize(NormalizationForm.FormKC);
            lock (gate)
            {
                var user = users.FirstOrDefault(u => String.Equals(u.username, username, StringComparison.OrdinalIgnoreCase));
                string salt = user == null ? new string('0', 32) : user.salt;
                string actual = Hash(password ?? "", FromHex(salt));
                string expected = user == null ? new string('0', actual.Length) : user.passwordHash;
                if (user == null || !Codec.Equal(actual, expected)) throw new ApiError(401, "Usuário ou senha incorretos.");
                return user;
            }
        }

        public bool Exists(string id) { lock (gate) return users.Any(u => u.id == id); }

        public void Remove(string id)
        {
            lock (gate)
            {
                var user = users.FirstOrDefault(u => u.id == id);
                if (user == null) throw new ApiError(404, "Usuário não encontrado.");
                if (user.role == "admin" && users.Count(u => u.role == "admin") <= 1)
                    throw new InvalidOperationException("Crie outro administrador antes de remover este.");
                users.Remove(user); Save();
            }
        }

        private static byte[] FromHex(string value)
        {
            byte[] data = new byte[value.Length / 2];
            for (int i = 0; i < data.Length; i++) data[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
            return data;
        }
        private static string Hash(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return BitConverter.ToString(derive.GetBytes(32)).Replace("-", "").ToLowerInvariant();
        }

        private void Save()
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, Codec.Encode(users), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
