// Per-user install/uninstall (%LocalAppData%\Programs), legacy Program Files
// uninstall, C:\Windows\Temp ACL fix, shortcuts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClamAVUI
{
    public partial class MainForm : Form
    {
        // ---------- Install (per-user, no admin rights) ----------

        // %LocalAppData%\Programs\ClamAV UI — writable by the owning user only.
        // Binaries can't be tampered with by other local users, and installing
        // and self-updating need no admin rights or UAC prompts. (Pre-0.0.8
        // installs went to Program Files and granted Users:Modify on the whole
        // tree, exe included — that model is retired.)
        static string InstallDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\ClamAV UI");
            }
        }

        // Where pre-0.0.8 versions installed. Still recognized so an existing
        // setup keeps behaving as installed; --uninstall from there elevates.
        static string LegacyInstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV UI"); }
        }

        static bool IsInstalled
        {
            // IsUnder, not StartsWith: "...\ClamAV UI Beta" must not count
            get
            {
                return IsUnder(Application.ExecutablePath, InstallDir)
                    || IsUnder(Application.ExecutablePath, LegacyInstallDir);
            }
        }

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static void RunInstallMode()
        {
            var f = new Form();
            f.Text = Lang.T("install.title");
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MinimizeBox = f.MaximizeBox = false;
            f.Size = new Size(440, 130);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.BackColor = Theme.Bg;
            Theme.DarkTitleBar(f);
            var l = new Label();
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.ForeColor = Theme.Text;
            l.Text = Lang.T("install.installing");
            f.Controls.Add(l);
            f.Shown += delegate
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = null;
                    try { DoInstall(); }
                    catch (Exception ex) { err = ex.Message; }
                    try
                    {
                        f.BeginInvoke((Action)delegate
                        {
                            f.Hide();
                            if (err != null)
                                MessageBox.Show(Lang.T("install.failed") + err, AppName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            else
                            {
                                try { Process.Start(Path.Combine(InstallDir, "ClamAVUI.exe")); }
                                catch { }
                            }
                            Application.ExitThread();
                        });
                    }
                    catch { }
                });
            };
            Application.Run(f);
        }

        static void DoInstall()
        {
            // the instance that launched --install is still shutting down and holds
            // the single-instance mutex — give it a moment, otherwise the installed
            // copy started below would just signal it and exit
            System.Threading.Thread.Sleep(1500);

            string srcDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string dst = InstallDir;
            Directory.CreateDirectory(dst);

            string dstExe = Path.Combine(dst, "ClamAVUI.exe");
            if (!string.Equals(Application.ExecutablePath, dstExe, StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, dstExe, true);

            // carry over whatever is already next to the exe so it isn't downloaded again
            bool samePlace = string.Equals(srcDir, dst, StringComparison.OrdinalIgnoreCase);
            if (!samePlace)
            {
                foreach (string sub in new string[] { "clamav", "quarantine" })
                {
                    string s = Path.Combine(srcDir, sub);
                    if (Directory.Exists(s)) CopyDir(s, Path.Combine(dst, sub));
                }
                foreach (string fn in new string[] { "settings.ini", "scans.log" })
                {
                    string s = Path.Combine(srcDir, fn);
                    if (File.Exists(s)) File.Copy(s, Path.Combine(dst, fn), true);
                }
            }

            // shortcuts: Start Menu and Desktop (both per-user)
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ClamAV UI.lnk"), dstExe, dst);
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ClamAV UI.lnk"), dstExe, dst);

            // register in "Apps" (per-user entry)
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClamAVUI"))
            {
                k.SetValue("DisplayName", "ClamAV UI");
                k.SetValue("DisplayVersion", AppVersion);
                k.SetValue("Publisher", "ClamAV UI");
                k.SetValue("DisplayIcon", dstExe);
                k.SetValue("InstallLocation", dst);
                k.SetValue("UninstallString", "\"" + dstExe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 600000, RegistryValueKind.DWord); // KB, including the database
            }

            // if autostart was enabled from the old location, repoint it to the new one
            using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                if (k != null && k.GetValue(RunValueName) != null)
                    k.SetValue(RunValueName, "\"" + dstExe + "\" --tray");
        }

        static void RunUninstallMode()
        {
            // Uninstall removes EVERY trace: the per-user install and, if present,
            // a pre-0.0.8 copy in Program Files (leftover after a migration, or
            // the copy we're running from). Touching Program Files — its folder,
            // the HKLM entry, the all-users shortcut — needs admin; a per-user
            // copy alone does not, so elevation is requested only when needed.
            bool legacy = Directory.Exists(LegacyInstallDir);
            if (legacy && !IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--uninstall");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch { } // user declined the UAC prompt
                return;
            }
            if (MessageBox.Show(
                Lang.T("uninstall.confirm"),
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                if (legacy)
                {
                    TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "ClamAV UI.lnk"));
                    Registry.LocalMachine.DeleteSubKeyTree(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClamAVUI", false);
                }
                TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ClamAV UI.lnk"));
                TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ClamAV UI.lnk"));
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClamAVUI", false);
                using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    if (k != null) k.DeleteValue(RunValueName, false);
                MessageBox.Show(Lang.T("uninstall.done"), AppName);
                // The folders themselves are removed after exit, since our exe is still
                // running from one of them. Launched AFTER the MessageBox: otherwise rd
                // runs while the window is still open and can't delete the locked exe.
                // rd on a folder that doesn't exist is a harmless no-op.
                string sweep = "rd /s /q \"" + InstallDir + "\"";
                if (legacy) sweep += " & rd /s /q \"" + LegacyInstallDir + "\"";
                var rm = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 3 /nobreak >nul & " + sweep);
                rm.CreateNoWindow = true;
                rm.UseShellExecute = false;
                Process.Start(rm);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("uninstall.error") + ex.Message, AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ---------- C:\Windows\Temp access fix ----------
        // On some hardened machines Users can't even list C:\Windows\Temp (a Group
        // Policy/security baseline strips the normally-default read permission), so
        // FileSystemWatcher on it fails for our always-non-elevated process. Rather than
        // running the whole app elevated (bigger attack surface, breaks non-admin users,
        // fights autostart), we fix the one thing that actually needs admin: the ACL
        // itself, once, via a UAC prompt — the app stays unprivileged afterwards.

        static void RunFixWinTempMode()
        {
            if (!IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--fix-wintemp");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch { } // user declined the UAC prompt
                return;
            }
            FixWinTempAcl();
        }

        static void FixWinTempAcl()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            // strip any explicit Deny for Users/Everyone first — an Allow we add below
            // can't override a Deny, so without this the grant could silently no-op
            RunHidden("icacls", "\"" + dir + "\" /remove:d *S-1-5-32-545 *S-1-1-0");
            RunHidden("icacls", "\"" + dir + "\" /grant *S-1-5-32-545:(RX)");
        }

        // Cheap capability probe: FileSystemWatcher needs at least list access to the
        // directory. Used both to decide whether C:\Windows\Temp is worth adding to the
        // default watch list, and to check whether FixWinTempAcl actually took effect.
        internal static bool CanWatchDirectory(string dir)
        {
            try { Directory.GetFiles(dir); return true; }
            catch { return false; }
        }

        static void RunHidden(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (var p = Process.Start(psi)) p.WaitForExit(30000);
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // .lnk via WScript.Shell (COM, no extra dependencies)
        static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { lnkPath });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }
}
