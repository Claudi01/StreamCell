using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StreamCell
{
    internal static class DesktopHost
    {
        private const string RuntimeResourcePrefix = "StreamCell.Runtime.";
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        private static extern bool SetDllDirectory(string path);
        [DllImport("shell32.dll", CharSet=CharSet.Auto)]
        private static extern IntPtr ShellExecute(IntPtr window, string operation, string file, string parameters, string directory, int showCommand);

        public static void RegisterResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
            string resource = RuntimeResourcePrefix + name + ".dll";
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (input == null) return null;
                using (MemoryStream output = new MemoryStream()) { input.CopyTo(output); return Assembly.Load(output.ToArray()); }
            }
        }

        private static void PrepareNativeLoader(string dataDirectory)
        {
            string runtime = Path.Combine(dataDirectory, "Runtime");
            Directory.CreateDirectory(runtime);
            string loader = Path.Combine(runtime, "WebView2Loader.dll");
            if (!File.Exists(loader))
            {
                using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(RuntimeResourcePrefix + "WebView2Loader.dll"))
                {
                    if (input == null) throw new InvalidOperationException("O runtime WebView2 não está incluído nesta versão.");
                    using (FileStream output = File.Create(loader)) input.CopyTo(output);
                }
            }
            SetDllDirectory(runtime);
        }

        public static void Run(string url, string dataDirectory)
        {
            RegisterResolver();
            PrepareNativeLoader(dataDirectory);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new Form())
            using (var browser = new WebView2())
            {
                form.Text = "StreamCell · Editor do deck";
                form.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                form.MinimumSize = new System.Drawing.Size(1000, 680);
                form.Size = new System.Drawing.Size(1440, 900);
                form.StartPosition = FormStartPosition.CenterScreen;
                browser.Dock = DockStyle.Fill;
                form.Controls.Add(browser);
                form.Shown += async delegate
                {
                    try
                    {
                        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDirectory, "WebView2Data"));
                        await browser.EnsureCoreWebView2Async(environment);
                        browser.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args) { Uri target; if(Uri.TryCreate(args.Uri,UriKind.Absolute,out target) && target.Scheme=="https" && target.Host=="github.com" && target.AbsolutePath.StartsWith("/Claudi01/StreamCell/releases",StringComparison.OrdinalIgnoreCase)) { args.Handled=true; ShellExecute(IntPtr.Zero,"open",args.Uri,null,null,1); } };
                        browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                        browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                        browser.CoreWebView2.Settings.IsZoomControlEnabled = true;
                        browser.CoreWebView2.Navigate(url);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(form, "Não foi possível abrir o editor integrado. Instale o Microsoft Edge WebView2 Runtime e tente novamente.\n\n" + ex.Message,
                            "StreamCell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        form.Close();
                    }
                };
                Application.Run(form);
            }
        }
    }
}