using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LumoraShared;

[assembly: System.Reflection.AssemblyTitle("Lumora")]
[assembly: System.Reflection.AssemblyProduct("Lumora")]

namespace Lumora
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    class MainForm : Form
    {
        readonly WebView2 web = new WebView2();
        readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumora");
        string StateFile { get { return Path.Combine(dataDir, "window.txt"); } }

        public MainForm()
        {
            Text = "Lumora";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = Color.FromArgb(18, 18, 22);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1400, 900);
            MinimumSize = new Size(800, 560);
            LoadWindowState();

            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(18, 18, 22);
            Controls.Add(web);

            Load += async (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(dataDir);
                    var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDir, "WebView2"));
                    await web.EnsureCoreWebView2Async(env);
                }
                catch (Exception ex)
                {
                    var r = MessageBox.Show(this,
                        "Lumora benötigt die Microsoft Edge WebView2 Runtime.\n\nJetzt die Download-Seite öffnen?\n\n(" + ex.Message + ")",
                        "Lumora", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                    if (r == DialogResult.Yes) Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                    Close();
                    return;
                }

                var core = web.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("lumora.app", appDir, CoreWebView2HostResourceAccessKind.Allow);
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.DocumentTitleChanged += (s2, e2) =>
                {
                    var t = core.DocumentTitle;
                    Text = string.IsNullOrEmpty(t) || t.StartsWith("https://") ? "Lumora" : t;
                };
                // External links open in the default browser instead of a new WebView window
                core.NewWindowRequested += (s2, e2) =>
                {
                    e2.Handled = true;
                    OpenExternal(e2.Uri);
                };
                core.NavigationStarting += (s2, e2) =>
                {
                    if (e2.Uri.StartsWith("http") && !e2.Uri.StartsWith("https://lumora.app/"))
                    {
                        e2.Cancel = true;
                        OpenExternal(e2.Uri);
                    }
                };
                core.Navigate("https://lumora.app/lumora.html");
                CheckForUpdate();
            };

            FormClosing += (s, e) => SaveWindowState();
        }

        // Update when version.txt online is newer than this EXE, or the online lumora.html differs from the installed one
        void CheckForUpdate()
        {
            if (!Online.Enabled) return;
            Task.Run(() =>
            {
                var ver = Online.TryGet("version.txt", 8000);
                var exeUpdate = ver != null && Online.IsNewer(Encoding.UTF8.GetString(ver), Config.Version);
                var htmlUpdate = false;
                if (!exeUpdate)
                {
                    var remote = Online.TryGet("lumora.html", 20000);
                    try { htmlUpdate = remote != null && !remote.SequenceEqual(File.ReadAllBytes(Path.Combine(appDir, "lumora.html"))); }
                    catch { }
                }
                if (exeUpdate || htmlUpdate)
                    BeginInvoke(new Action(() => OfferUpdate(exeUpdate)));
            });
        }

        void OfferUpdate(bool exeUpdate)
        {
            if (MessageBox.Show(this, "Ein neues Update ist verfügbar. Herunterladen?", "Lumora Update",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            var oldTitle = Text;
            Text = "Lumora – Update wird geladen …";
            UseWaitCursor = true;
            Task.Run(() =>
            {
                // Newest setup from the update location; for HTML-only updates the installed copy also works
                string setup = null;
                var bytes = Online.TryGet("Lumora-Setup.exe", 120000);
                if (bytes != null && bytes.Length > 100000)
                {
                    try
                    {
                        setup = Path.Combine(Path.GetTempPath(), "Lumora-Setup-Update.exe");
                        File.WriteAllBytes(setup, bytes);
                    }
                    catch { setup = null; }
                }
                var local = Path.Combine(appDir, "Uninstall.exe");
                if (setup == null && !exeUpdate && File.Exists(local)) setup = local;

                BeginInvoke(new Action(() =>
                {
                    UseWaitCursor = false;
                    Text = oldTitle;
                    if (setup == null)
                    {
                        MessageBox.Show(this, "Das Update konnte nicht heruntergeladen werden. Bitte später erneut versuchen.",
                            "Lumora Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    try
                    {
                        Process.Start(new ProcessStartInfo(setup, "/update") { WorkingDirectory = Path.GetTempPath() });
                        Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Das Update konnte nicht gestartet werden:\n" + ex.Message,
                            "Lumora Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }));
            });
        }

        static void OpenExternal(string uri)
        {
            if (uri.StartsWith("http://") || uri.StartsWith("https://"))
                try { Process.Start(uri); } catch { }
        }

        void LoadWindowState()
        {
            try
            {
                var p = File.ReadAllText(StateFile).Split(',');
                var b = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                if (Screen.FromRectangle(b).WorkingArea.IntersectsWith(b))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = b;
                }
                if (p[4] == "1") WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        void SaveWindowState()
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                File.WriteAllText(StateFile, string.Format("{0},{1},{2},{3},{4}",
                    b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized ? "1" : "0"));
            }
            catch { }
        }
    }
}
