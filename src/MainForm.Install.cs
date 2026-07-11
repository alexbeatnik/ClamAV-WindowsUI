// Install to Program Files, uninstall, C:\Windows\Temp ACL fix, shortcuts.
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
        // ---------- Install to Program Files ----------

        static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV UI"); }
        }

        static bool IsInstalled
        {
            // IsUnder, not StartsWith: "C:\Program Files\ClamAV UI Beta" must not count
            get { return IsUnder(Application.ExecutablePath, InstallDir); }
        }

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static void RunInstallMode()
        {
            if (!IsAdmin())
            {
                // relaunch ourselves with administrator rights
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--install");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch { } // user declined the UAC prompt
                return;
            }

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
                                // launch the installed copy WITHOUT administrator rights (via explorer)
                                try { Process.Start("explorer.exe", "\"" + Path.Combine(InstallDir, "ClamAVUI.exe") + "\""); }
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
            string srcDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string dst = InstallDir;
            Directory.CreateDirectory(dst);

            // Grant Users write access so the DB/quarantine/settings can update without admin rights
            RunHidden("icacls", "\"" + dst + "\" /grant *S-1-5-32-545:(OI)(CI)M /T");

            // While we're elevated anyway, also restore the default Windows read
            // permission on C:\Windows\Temp if something hardened it away — lets the
            // (always non-elevated) app monitor it too. See FixWinTempAcl for details.
            FixWinTempAcl();

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

            // shortcuts: Start Menu (all users) and Desktop
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "ClamAV UI.lnk"), dstExe, dst);
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ClamAV UI.lnk"), dstExe, dst);

            // register in "Programs and Features" (Apps)
            using (var k = Registry.LocalMachine.CreateSubKey(
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
            if (!IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--uninstall");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch { }
                return;
            }
            if (MessageBox.Show(
                Lang.T("uninstall.confirm"),
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "ClamAV UI.lnk"));
                TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ClamAV UI.lnk"));
                Registry.LocalMachine.DeleteSubKeyTree(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClamAVUI", false);
                using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    if (k != null) k.DeleteValue(RunValueName, false);
                MessageBox.Show(Lang.T("uninstall.done"), AppName);
                // The folder itself is removed after exit, since our exe is still running.
                // We launch this AFTER the MessageBox: otherwise rd runs while the window
                // is still open and can't delete the locked exe.
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 3 /nobreak >nul & rd /s /q \"" + InstallDir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
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
