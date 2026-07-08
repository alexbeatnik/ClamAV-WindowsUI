// MainForm: constants, state fields, entry point, process launching, autostart.
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
        const string AppName = "ClamAV UI";
        const string AppVersion = "0.0.3";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "ClamAVUI";

        string clamDir;      // folder containing clamscan.exe / freshclam.exe
        string dbDir;        // signature database folder
        string settingsPath; // settings.ini next to the exe
        string quarDir;      // quarantine folder next to the exe
        string quarIndex;    // quarantine\index.txt: "file|original path|date"
        string clamVersion = "—";
        string lastScanInfo = ""; // empty = never scanned yet
        Process currentProc; // running clamscan or freshclam
        int scannedCount, foundCount, movedCount;
        long totalScans, totalFound, totalMoved, totalFilesScanned; // cumulative statistics
        readonly List<string[]> foundFiles = new List<string[]>(); // {path, threat name}
        bool scanRunning, updateRunning;
        bool monitorScan;    // true if the current scan was triggered by the monitor, not the user
        bool reallyClose;    // true = exit, false = minimize to tray
        bool autostartInitialized; // whether autostart was already auto-enabled on first run

        // Progress: total file count (computed in the background), generation to cancel counting
        int totalToScan;
        int countGen;
        DateTime scanStart;
        DateTime rateWinTime = DateTime.MinValue;     // start of the moving window used to estimate rate
        int rateWinCount;                             // scannedCount at the start of the window
        bool loggedTotal;            // whether "Files to check: N" was already logged
        DateTime lastScanOutput;     // last time clamscan produced output (detects "stuck on a file")
        Timer scanHeartbeat;         // logs progress every N seconds even when clamscan is silent
        string currentScanDesc = "";
        string scanLogPath;   // scans.log next to the exe
        Timer autoUpdateTimer;
        bool autoUpdateFirstTick = true;

        // Exclusions: paths that are not scanned
        readonly List<string> exclusions = new List<string>();

        // New-file monitoring
        readonly List<string> watchDirs = new List<string>();
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly Dictionary<string, int> pendingFiles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Timer debounceTimer;
        bool watchInitialized; // whether the default monitored folders were already set up
        bool watchDefaultsV2;  // whether the v2 defaults (Temp, Roaming) were already added
        bool watchDefaultsV3;  // whether C:\Windows\Temp was already dropped (v2 mistake — unwatchable non-elevated)
        static readonly string[] TempExtensions = new string[]
            { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload" };

        // Potentially dangerous types: executables, scripts, installers, archives,
        // and documents with macros — where real-world malware actually shows up.
        static readonly HashSet<string> RiskyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // executables and libraries
            ".exe", ".dll", ".sys", ".com", ".scr", ".cpl", ".ocx", ".drv", ".efi",
            ".msi", ".msix", ".msp", ".mst", ".appx",
            // scripts and shortcuts
            ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf",
            ".wsh", ".hta", ".lnk", ".scf", ".reg", ".jar", ".py", ".ahk",
            // documents with macros / exploits
            ".doc", ".docm", ".dot", ".dotm", ".xls", ".xlsm", ".xlsb", ".xlm",
            ".ppt", ".pptm", ".potm", ".rtf", ".pdf",
            // archives and disk images (can contain anything)
            ".zip", ".rar", ".7z", ".cab", ".arj", ".gz", ".tar", ".iso", ".img", ".vhd"
        };

        // UI
        ModernButton btnStop, btnUpdate, btnWatchDirs, btnQuarantine, btnScanLog;
        ModernButton dashQuick, dashScanFile, dashScanFolder, dashScanAll, btnInstall, btnLangEn, btnLangUk, btnFixWinTemp;
        ModernButton btnQuarDelete, btnQuarRestore, btnQuarToExcl, btnQuarOpenFolder, btnQuarExclusions;
        ListView quarList;
        readonly List<ModernButton> scanButtons = new List<ModernButton>(); // all buttons that start a scan (both pages)
        RichTextBox log;
        Label statusLabel, heroTitle, heroSub, statsLabel, sysNames, langLabel, lastActivityLabel;
        ShieldIndicator shield;
        Toggle chkAutostart, chkMonitor, chkQuarantine, chkAutoUpdate, chkRiskyOnly, chkFullRisky;
        SlimMarquee progress;
        NotifyIcon tray;
        ToolStripItem trayOpenItem, trayExitItem;
        StatusBanner statusBanner, activityRow;
        CardPanel cardSystem, cardQuar, cardScan, cardSettingsPanel;
        Panel[] pages;
        NavTab[] navs;
        static Image logoImage;
        static Icon appIcon;

        // Logo and icon are embedded in the exe as resources (see build.ps1)
        static Image LogoImage
        {
            get
            {
                if (logoImage == null)
                {
                    try
                    {
                        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
                        if (s != null) logoImage = Image.FromStream(s);
                    }
                    catch { }
                }
                return logoImage;
            }
        }

        static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("clamav.ico");
                        if (s != null) appIcon = new Icon(s);
                    }
                    catch { }
                }
                return appIcon ?? SystemIcons.Shield;
            }
        }

        // One instance per user session + a "show window" message to the existing instance
        static System.Threading.Mutex singleInstanceMutex;
        internal static readonly int WmShow = NativeMethods.RegisterWindowMessage("ClamAVUI_Show_7f3a1c");

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool startInTray = false;
            foreach (string a in args)
            {
                if (a == "--tray") startInTray = true;
                if (a == "--install") { RunInstallMode(); return; }
                if (a == "--uninstall") { RunUninstallMode(); return; }
                if (a == "--fix-wintemp") { RunFixWinTempMode(); return; }
            }

            // Single instance only: if already running, ask that instance to show itself and exit
            bool createdNew;
            singleInstanceMutex = new System.Threading.Mutex(true, "Local\\ClamAVUI_SingleInstance_v1", out createdNew);
            if (!createdNew)
            {
                NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, WmShow, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            try { Application.Run(new MainForm(startInTray)); }
            finally { GC.KeepAlive(singleInstanceMutex); }
        }

        // ---------- Process launching ----------

        void StartProcess(string exe, string args, Action<string> onLine, Action<int> onExit)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = clamDir;

            var p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            p.SynchronizingObject = this; // events fire on the UI thread
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { onLine(e.Data); };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { onLine(e.Data); };
            p.Exited += delegate
            {
                int code = 0;
                try { code = p.ExitCode; } catch { }
                currentProc = null;
                onExit(code);
                p.Dispose();
            };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                currentProc = p;
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Lang.T("log.processStartFailed"), Path.GetFileName(exe), ex.Message), Theme.Danger);
                scanRunning = updateRunning = monitorScan = false;
                SetBusy(false, Lang.T("status.startError"));
            }
        }

        void StopCurrent()
        {
            cancelUpdate = true;      // interrupts a database download in progress, if any
            cancelScanListing = true; // interrupts building the file list for a scan
            var wc = clamZipClient;
            if (wc != null) { try { wc.CancelAsync(); } catch { } }
            foreach (var sp in scanProcs.ToArray()) { try { sp.Kill(); } catch { } }
            var p = currentProc;
            if (p == null) return;
            try { p.Kill(); } catch { }
        }

        void SetBusy(bool busy, string status)
        {
            SetScanEnabled(!busy && DbExists());
            btnUpdate.Enabled = !busy; // without ClamAV this button triggers its installation
            btnStop.Enabled = busy;
            if (busy)
            {
                progress.Start();
                if (scanRunning) SetHero(ShieldState.Busy, Lang.T("hero.scanningTitle"), Lang.T("hero.scanningSub"));
                if (scanRunning && !monitorScan)
                {
                    ShowPage(1); // manual scan — switch to the scanner page
                    lastScanOutput = DateTime.Now;
                    scanHeartbeat.Start();
                }
            }
            else { progress.Stop(); scanHeartbeat.Stop(); }
            if (status != null) statusLabel.Text = status;
        }

        void AppendLog(string text, Color color)
        {
            log.SelectionStart = log.TextLength;
            log.SelectionColor = color;
            log.AppendText(text);
            log.SelectionColor = log.ForeColor;
            log.ScrollToCaret();
        }

        // ---------- Autostart ----------

        bool IsAutostartEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                return key != null && key.GetValue(RunValueName) != null;
            }
        }

        void SetAutostart(bool enable)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enable)
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --tray");
                else
                    key.DeleteValue(RunValueName, false);
            }
            statusLabel.Text = enable ? Lang.T("status.autostartOn") : Lang.T("status.autostartOff");
        }

        // Autostart is enabled automatically on first run. A flag is set so that if the
        // user later disables it, we don't turn it back on.
        void EnsureAutostartFirstRun()
        {
            if (autostartInitialized) return;
            autostartInitialized = true;
            if (!IsAutostartEnabled())
                chkAutostart.Checked = true; // the CheckedChanged handler will write the registry
            SaveSettings();
        }
    }
}
