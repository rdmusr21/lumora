using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using LumoraShared;

[assembly: AssemblyTitle("Lumora Setup")]
[assembly: AssemblyProduct("Lumora")]

namespace LumoraSetup
{
    static class Program
    {
        public const string AppName = "Lumora";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lumora";
        // Files embedded as resources; extracted into the install folder
        public static readonly string[] Payload = {
            "Lumora.exe", "lumora.html", "Microsoft.Web.WebView2.Core.dll",
            "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll"
        };

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (mode == "/uninstall")
                Uninstaller.Run(args.Length > 1 && args[1].Equals("/silent", StringComparison.OrdinalIgnoreCase));
            else
                Application.Run(new SetupForm(mode == "/update"));
        }

        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }
        public static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }

        public static string ExistingInstallDir()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k == null ? null : k.GetValue("InstallLocation") as string;
        }

        public static byte[] Embedded(string name)
        {
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var ms = new MemoryStream())
            {
                src.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // Newest lumora.html from the update location, or the one bundled in this setup when offline
        public static byte[] LatestHtml()
        {
            return Online.TryGet("lumora.html", 8000) ?? Embedded("lumora.html");
        }

        public static string DownloadsFolder()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                {
                    var v = k == null ? null : k.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                    if (!string.IsNullOrEmpty(v)) return Environment.ExpandEnvironmentVariables(v);
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    class SetupForm : Form
    {
        readonly TextBox pathBox = new TextBox();
        readonly CheckBox desktopBox = new CheckBox();
        readonly CheckBox launchBox = new CheckBox();
        readonly Button browseBtn = new Button();
        readonly Button installBtn = new Button();
        readonly Button cancelBtn = new Button();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label status = new Label();
        readonly Label heading = new Label();
        readonly Label sub = new Label();
        readonly Label gear = new Label();
        readonly bool update;
        bool done;

        public SetupForm(bool update)
        {
            this.update = update;
            var existing = Program.ExistingInstallDir();
            // Layout below is in 96-DPI pixels; WinForms scales it to the screen on ResumeLayout
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = update ? "Lumora Update" : "Lumora Setup";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 330);
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;

            var banner = new Panel { Dock = DockStyle.Top, Height = 92 };
            banner.Paint += (s, e) =>
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(banner.ClientRectangle,
                    Color.FromArgb(10, 132, 255), Color.FromArgb(255, 55, 95), 0f))
                    e.Graphics.FillRectangle(br, banner.ClientRectangle);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                var k = e.Graphics.DpiX / 96f;
                int sz = (int)(48 * k);
                e.Graphics.DrawIcon(new Icon(Icon, sz, sz), new Rectangle((int)(22 * k), (int)(22 * k), sz, sz));
            };
            heading.Text = update ? "Lumora aktualisieren" : "Lumora installieren";
            heading.Font = new Font("Segoe UI Semibold", 15f);
            heading.ForeColor = Color.White;
            heading.BackColor = Color.Transparent;
            heading.SetBounds(84, 18, 400, 32);
            sub.Text = "Bildeditor  ·  Version " + Config.Version;
            sub.ForeColor = Color.FromArgb(235, 255, 255, 255);
            sub.BackColor = Color.Transparent;
            sub.SetBounds(86, 52, 400, 22);

            gear.Text = ""; // Settings glyph
            gear.Font = new Font("Segoe MDL2 Assets", 13f);
            gear.ForeColor = Color.FromArgb(220, 255, 255, 255);
            gear.BackColor = Color.Transparent;
            gear.TextAlign = ContentAlignment.MiddleCenter;
            gear.SetBounds(520, 8, 32, 32);
            gear.Cursor = Cursors.Hand;
            gear.MouseEnter += (s, e) => gear.ForeColor = Color.White;
            gear.MouseLeave += (s, e) => gear.ForeColor = Color.FromArgb(220, 255, 255, 255);
            gear.Click += (s, e) => { using (var f = new SettingsForm()) f.ShowDialog(this); };
            new ToolTip().SetToolTip(gear, "Einstellungen");

            banner.Controls.Add(heading);
            banner.Controls.Add(sub);
            banner.Controls.Add(gear);
            Controls.Add(banner);

            var lbl = new Label { Text = "Installationsordner:", AutoSize = true, Location = new Point(24, 112) };
            Controls.Add(lbl);

            pathBox.SetBounds(24, 136, 404, 26);
            pathBox.Text = existing ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lumora");
            Controls.Add(pathBox);

            browseBtn.Text = "Durchsuchen…";
            browseBtn.SetBounds(436, 134, 100, 29);
            browseBtn.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Ordner für Lumora auswählen", ShowNewFolderButton = true })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        var p = dlg.SelectedPath;
                        if (!Path.GetFileName(p.TrimEnd('\\')).Equals("Lumora", StringComparison.OrdinalIgnoreCase))
                            p = Path.Combine(p, "Lumora");
                        pathBox.Text = p;
                    }
                }
            };
            Controls.Add(browseBtn);

            desktopBox.Text = "Desktopverknüpfung erstellen";
            // On update keep the user's earlier choice
            desktopBox.Checked = !update || existing == null || File.Exists(Program.DesktopLink);
            desktopBox.AutoSize = true;
            desktopBox.Location = new Point(24, 178);
            Controls.Add(desktopBox);

            launchBox.Text = "Lumora nach der Installation starten";
            launchBox.Checked = true;
            launchBox.AutoSize = true;
            launchBox.Location = new Point(24, 204);
            Controls.Add(launchBox);

            progress.SetBounds(24, 240, 512, 8);
            progress.Visible = false;
            Controls.Add(progress);

            status.SetBounds(24, 252, 512, 22);
            status.ForeColor = Color.DimGray;
            Controls.Add(status);

            var line = new Label { BorderStyle = BorderStyle.Fixed3D, Bounds = new Rectangle(0, 280, 560, 2) };
            Controls.Add(line);

            installBtn.Text = update ? "Aktualisieren" : "Installieren";
            installBtn.SetBounds(332, 290, 100, 30);
            installBtn.Click += (s, e) => { if (done) Finish(); else Install(); };
            Controls.Add(installBtn);
            AcceptButton = installBtn;

            cancelBtn.Text = "Abbrechen";
            cancelBtn.SetBounds(436, 290, 100, 30);
            cancelBtn.Click += (s, e) => Close();
            Controls.Add(cancelBtn);
            ResumeLayout(false);
        }

        void Install()
        {
            var dir = pathBox.Text.Trim();
            try { dir = Path.GetFullPath(dir); }
            catch { MessageBox.Show(this, "Der Pfad ist ungültig.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            foreach (var p in Process.GetProcessesByName("Lumora"))
            {
                try { if (p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) { p.CloseMainWindow(); p.WaitForExit(3000); if (!p.HasExited) p.Kill(); } }
                catch { }
            }

            foreach (Control c in Controls) if (c is TextBox || c is Button || c is CheckBox) c.Enabled = false;
            gear.Enabled = false;
            progress.Visible = true;
            progress.Maximum = Program.Payload.Length + 4;
            progress.Value = 0;

            try
            {
                status.Text = "Suche neueste Version …";
                Application.DoEvents();
                var html = Program.LatestHtml();
                progress.Value++;

                Directory.CreateDirectory(dir);
                foreach (var name in Program.Payload)
                {
                    status.Text = "Kopiere " + name + " …";
                    Application.DoEvents();
                    File.WriteAllBytes(Path.Combine(dir, name), name == "lumora.html" ? html : Program.Embedded(name));
                    progress.Value++;
                }

                status.Text = "Erstelle Verknüpfungen …";
                Application.DoEvents();
                var exe = Path.Combine(dir, "Lumora.exe");
                var uninst = Path.Combine(dir, "Uninstall.exe");
                if (!Application.ExecutablePath.Equals(uninst, StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, uninst, true);
                progress.Value++;

                Shortcuts.Create(Program.StartMenuLink, exe, dir);
                if (desktopBox.Checked) Shortcuts.Create(Program.DesktopLink, exe, dir);
                else if (File.Exists(Program.DesktopLink)) File.Delete(Program.DesktopLink);
                progress.Value++;

                status.Text = "Registriere Programm …";
                using (var k = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
                {
                    k.SetValue("DisplayName", "Lumora");
                    k.SetValue("DisplayVersion", Config.Version);
                    k.SetValue("Publisher", "Lumora");
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /silent");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    long size = 0;
                    foreach (var f in Directory.GetFiles(dir)) size += new FileInfo(f).Length;
                    k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                }
                progress.Value++;
            }
            catch (UnauthorizedAccessException)
            {
                Fail("Kein Schreibzugriff auf diesen Ordner.\nBitte einen anderen Ordner wählen oder Setup als Administrator starten.");
                return;
            }
            catch (Exception ex)
            {
                Fail("Installation fehlgeschlagen:\n" + ex.Message);
                return;
            }

            done = true;
            heading.Text = update ? "Update abgeschlossen" : "Installation abgeschlossen";
            status.Text = update ? "Lumora ist jetzt auf dem neuesten Stand." : "Lumora wurde erfolgreich installiert.";
            installBtn.Text = "Fertig";
            installBtn.Enabled = true;
            launchBox.Enabled = true;
            cancelBtn.Visible = false;
            installBtn.Left = 436;
        }

        void Fail(string msg)
        {
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            foreach (Control c in Controls) c.Enabled = true;
            gear.Enabled = true;
            progress.Visible = false;
            status.Text = "";
        }

        void Finish()
        {
            if (launchBox.Checked)
            {
                var exe = Path.Combine(Path.GetFullPath(pathBox.Text.Trim()), "Lumora.exe");
                try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) }); } catch { }
            }
            Close();
        }
    }

    class SettingsForm : Form
    {
        readonly RadioButton desktopRb = new RadioButton();
        readonly RadioButton downloadsRb = new RadioButton();
        readonly RadioButton otherRb = new RadioButton();
        readonly TextBox otherBox = new TextBox();
        readonly Button otherBtn = new Button();
        readonly Button downloadBtn = new Button();
        readonly Label status = new Label();

        public SettingsForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Einstellungen";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 268);
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;

            var title = new Label { Text = "Ohne Installation", Font = new Font("Segoe UI Semibold", 12f), AutoSize = true, Location = new Point(20, 16) };
            var info = new Label
            {
                Text = "Speichert nur die Datei lumora.html, ohne Installation.\nDie Datei kannst du per Doppelklick im Browser öffnen.",
                ForeColor = Color.DimGray, AutoSize = true, Location = new Point(20, 46)
            };
            Controls.Add(title);
            Controls.Add(info);

            desktopRb.Text = "Desktop";
            desktopRb.Checked = true;
            desktopRb.AutoSize = true;
            desktopRb.Location = new Point(22, 94);
            downloadsRb.Text = "Downloads";
            downloadsRb.AutoSize = true;
            downloadsRb.Location = new Point(22, 120);
            otherRb.Text = "Anderer Ordner:";
            otherRb.AutoSize = true;
            otherRb.Location = new Point(22, 146);
            Controls.Add(desktopRb);
            Controls.Add(downloadsRb);
            Controls.Add(otherRb);

            otherBox.SetBounds(40, 172, 276, 26);
            otherBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            otherBox.TextChanged += (s, e) => otherRb.Checked = true;
            otherBtn.Text = "Durchsuchen…";
            otherBtn.SetBounds(322, 170, 100, 29);
            otherBtn.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Speicherort für lumora.html auswählen", ShowNewFolderButton = true, SelectedPath = otherBox.Text })
                    if (dlg.ShowDialog(this) == DialogResult.OK) { otherBox.Text = dlg.SelectedPath; otherRb.Checked = true; }
            };
            Controls.Add(otherBox);
            Controls.Add(otherBtn);

            status.SetBounds(20, 230, 230, 22);
            status.ForeColor = Color.DimGray;
            Controls.Add(status);

            downloadBtn.Text = "Nur HTML herunterladen";
            downloadBtn.SetBounds(262, 224, 160, 32);
            downloadBtn.Click += (s, e) => Download();
            Controls.Add(downloadBtn);
            AcceptButton = downloadBtn;
            ResumeLayout(false);
        }

        void Download()
        {
            var dir = desktopRb.Checked ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : downloadsRb.Checked ? Program.DownloadsFolder()
                    : otherBox.Text.Trim();
            try
            {
                downloadBtn.Enabled = false;
                status.Text = "Lade herunter …";
                Application.DoEvents();
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "lumora.html");
                File.WriteAllBytes(file, Program.LatestHtml());
                status.Text = "";
                if (MessageBox.Show(this, "Gespeichert unter:\n" + file + "\n\nOrdner jetzt öffnen?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start("explorer.exe", "/select,\"" + file + "\"");
                Close();
            }
            catch (Exception ex)
            {
                status.Text = "";
                MessageBox.Show(this, "Speichern fehlgeschlagen:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { downloadBtn.Enabled = true; }
        }
    }

    static class Shortcuts
    {
        public static void Create(string lnk, string target, string workDir)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = target;
            sc.WorkingDirectory = workDir;
            sc.IconLocation = target + ",0";
            sc.Description = "Lumora – Bildeditor";
            sc.Save();
        }
    }

    static class Uninstaller
    {
        public static void Run(bool silent)
        {
            var dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (!silent && MessageBox.Show("Lumora wirklich deinstallieren?", "Lumora deinstallieren",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (var p in Process.GetProcessesByName("Lumora"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }

            try { if (File.Exists(Program.DesktopLink)) File.Delete(Program.DesktopLink); } catch { }
            try { if (File.Exists(Program.StartMenuLink)) File.Delete(Program.StartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            foreach (var name in Program.Payload)
                try { File.Delete(Path.Combine(dir, name)); } catch { }

            // The running Uninstall.exe cannot delete itself; let cmd do it after we exit
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c timeout /t 2 /nobreak >nul & del /f /q \"" + Application.ExecutablePath + "\" & rmdir \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });

            if (!silent)
                MessageBox.Show("Lumora wurde deinstalliert.", "Lumora deinstallieren", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
