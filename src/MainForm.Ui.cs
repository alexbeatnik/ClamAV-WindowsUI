// UI construction: pages, dashboard, quarantine/settings pages, language switching.
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

        public MainForm(bool startInTray)
        {
            Text = AppName;
            Icon = AppIcon;
            MinimumSize = new Size(880, 620); // the taller hero + tiles need the room
            Size = new Size(940, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            Theme.DarkTitleBar(this);

            BuildUi();
            LocateClamAV();
            LoadSettings();
            RefreshDbStatus();
            ShowPage(0);
            EnsureAutostartFirstRun();

            if (startInTray)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }

            // Clean PC: only the exe is present — offer to download ClamAV automatically
            Shown += delegate
            {
                if (clamDir == null && !startInTray) OfferClamAVDownload();
            };
        }

        // ---------- UI construction ----------

        void BuildUi()
        {
            // Header: ClamAV logo + name on the left, horizontal nav tabs on the right.
            // (Not a left icon rail — see the note on NavTab for why.)
            var title = new Panel();
            title.Dock = DockStyle.Top;
            title.Height = 78;
            title.BackColor = Theme.Bg;
            var logo = new PictureBox();
            logo.Image = LogoImage;
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Size = new Size(132, 62);
            logo.Location = new Point(20, 10);
            logo.BackColor = Color.Transparent;
            var titleText = new Label();
            titleText.Text = "ClamAV UI";
            titleText.Font = new Font("Segoe UI Semibold", 17f);
            titleText.ForeColor = Theme.Text;
            titleText.AutoSize = true;
            titleText.Location = new Point(168, 20);
            var verText = new Label();
            verText.Text = "v" + AppVersion;
            verText.Font = new Font("Segoe UI", 9.5f);
            verText.ForeColor = Theme.Muted;
            verText.AutoSize = true;
            verText.Location = new Point(171, 50);
            title.Controls.Add(logo);
            title.Controls.Add(titleText);
            title.Controls.Add(verText);

            var navBar = new FlowLayoutPanel();
            navBar.Dock = DockStyle.Right;
            navBar.FlowDirection = FlowDirection.LeftToRight;
            navBar.WrapContents = false;
            navBar.AutoSize = true;
            navBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            navBar.BackColor = Theme.Bg;
            navBar.Padding = new Padding(0, 17, 24, 0);
            string[] navLabelKeys = { "nav.dashboard", "nav.logs", "nav.quarantine", "nav.settings" };
            IconDraw[] navIcons = { Ico.ShieldIcon, Ico.LogIcon, Ico.Radiation, Ico.Gear };
            navs = new NavTab[4];
            for (int i = 0; i < 4; i++)
            {
                navs[i] = new NavTab(Lang.T(navLabelKeys[i]), navIcons[i]);
                int idx = i;
                navs[i].Click += delegate { ShowPage(idx); };
                navBar.Controls.Add(navs[i]);
            }
            title.Controls.Add(navBar);

            // Status bar at the bottom
            var statusBar = new Panel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 26;
            statusBar.BackColor = Theme.Card;
            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(12, 0, 0, 0);
            statusLabel.Text = Lang.T("status.ready");
            statusBar.Controls.Add(statusLabel);

            progress = new SlimMarquee();
            progress.Dock = DockStyle.Bottom;

            // Page area (full width now that navigation lives in the header)
            var content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(20, 8, 20, 12);
            content.BackColor = Theme.Bg;

            pages = new Panel[4];
            pages[0] = BuildDashboardPage();
            pages[1] = BuildLogsPage();
            pages[2] = BuildQuarantinePage();
            pages[3] = BuildSettingsPage();
            foreach (var p in pages) { p.Visible = false; content.Controls.Add(p); }

            Controls.Add(content);
            Controls.Add(progress);
            Controls.Add(statusBar);
            Controls.Add(title);

            // Debounce timer: wait for a file to finish being written, then scan as a batch
            debounceTimer = new Timer();
            debounceTimer.Interval = 3000;
            debounceTimer.Tick += OnDebounceTick;

            // Auto-update: first check 15s after startup, then the timer ticks hourly.
            // The actual checks are throttled further: database versions once a day
            // (the CDN rate-limits), app releases on GitHub every 4 hours
            autoUpdateTimer = new Timer();
            autoUpdateTimer.Interval = 15000;
            autoUpdateTimer.Tick += delegate
            {
                if (autoUpdateFirstTick) { autoUpdateFirstTick = false; autoUpdateTimer.Interval = 3600000; }
                MaybeAutoUpdate();
                MaybeCheckAppUpdate();
            };
            autoUpdateTimer.Start();

            // Scan heartbeat: guarantees a progress line every 10s even when clamscan
            // is stuck for a long time on a large file/archive and prints nothing.
            scanHeartbeat = new Timer();
            scanHeartbeat.Interval = 10000;
            scanHeartbeat.Tick += delegate { ScanHeartbeatTick(); };

            // Tray
            tray = new NotifyIcon();
            tray.Icon = AppIcon;
            tray.Text = AppName;
            tray.Visible = true;
            tray.DoubleClick += delegate { RestoreFromTray(); };
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            menu.ForeColor = Theme.Text;
            trayOpenItem = menu.Items.Add(Lang.T("tray.open"), null, delegate { RestoreFromTray(); });
            trayExitItem = menu.Items.Add(Lang.T("tray.exit"), null, delegate { reallyClose = true; Close(); });
            tray.ContextMenuStrip = menu;

            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                    ShowInTaskbar = false; // hide to tray
            };
            FormClosing += OnFormClosing;
        }

        Panel BuildDashboardPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;

            // Hero status banner — the commercial-AV centerpiece: a large glowing
            // shield, big headline, and the primary QUICK SCAN action right inside
            // it (the "one big obvious button" every commercial dashboard leads with).
            statusBanner = new StatusBanner();
            statusBanner.Dock = DockStyle.Top;
            statusBanner.Height = 104;
            statusBanner.Width = 880; // pre-size so the Anchor.Right button keeps its margin
            statusBanner.Margin = new Padding(6, 4, 6, 4);
            shield = new ShieldIndicator();
            shield.Size = new Size(66, 66);
            shield.Location = new Point(22, 17);
            shield.BackColor = Theme.Card;
            heroTitle = new Label();
            heroTitle.AutoSize = true;
            heroTitle.Location = new Point(102, 22);
            heroTitle.Font = new Font("Segoe UI Semibold", 15f);
            heroTitle.ForeColor = Theme.Text;
            heroTitle.BackColor = Theme.Card;
            heroSub = new Label();
            heroSub.AutoSize = true;
            heroSub.Location = new Point(104, 56);
            heroSub.Font = Font;
            heroSub.ForeColor = Theme.Muted;
            heroSub.BackColor = Theme.Card;
            dashQuick = MakeButton(Lang.T("btn.quickScan"), 180, Theme.Accent, Theme.AccentHot, Ico.Radar);
            dashQuick.Height = 44;
            dashQuick.Font = new Font("Segoe UI Semibold", 10f);
            dashQuick.BackColor = Theme.Card;
            dashQuick.Location = new Point(statusBanner.Width - dashQuick.Width - 28,
                (statusBanner.Height - dashQuick.Height) / 2);
            dashQuick.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            dashQuick.Click += delegate { RunQuickScan(); };
            // red STOP takes QUICK SCAN's place while a scan/update runs (see SetBusy)
            dashStop = MakeButton(Lang.T("btn.stop"), 180, Theme.Danger, Theme.DangerHot, Ico.StopIcon);
            dashStop.Height = 44;
            dashStop.Font = dashQuick.Font;
            dashStop.BackColor = Theme.Card;
            dashStop.Location = dashQuick.Location;
            dashStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            dashStop.Visible = false;
            dashStop.Click += delegate { StopCurrent(); };
            statusBanner.Controls.Add(shield);
            statusBanner.Controls.Add(heroTitle);
            statusBanner.Controls.Add(heroSub);
            statusBanner.Controls.Add(dashQuick);
            statusBanner.Controls.Add(dashStop);
            scanButtons.Add(dashQuick);

            // Secondary scan tiles below the hero, icon + label like a launcher
            var scanBar = new FlowLayoutPanel();
            scanBar.Dock = DockStyle.Top;
            scanBar.Height = 112;
            scanBar.Padding = new Padding(6, 4, 6, 2);
            scanBar.BackColor = Theme.Bg;
            dashScanFile = MakeCardButton(Lang.T("btn.scanFileDash"), Theme.Card, Theme.CardLine, Theme.Text, Ico.FileIcon);
            dashScanFile.Click += delegate { PickAndScan(false); };
            dashScanFolder = MakeCardButton(Lang.T("btn.scanFolderDash"), Theme.Card, Theme.CardLine, Theme.Text, Ico.FolderIcon);
            dashScanFolder.Click += delegate { PickAndScan(true); };
            dashScanAll = MakeCardButton(Lang.T("btn.scanAll"), Theme.Card, Theme.CardLine, Theme.Text, Ico.Stack);
            dashScanAll.Click += delegate { RunFullScan(); };
            scanBar.Controls.AddRange(new Control[] { dashScanFile, dashScanFolder, dashScanAll });
            scanButtons.Add(dashScanFile);
            scanButtons.Add(dashScanFolder); scanButtons.Add(dashScanAll);

            btnQuarantine = MakeCardButton(Lang.T("btn.openQuarantine"), Theme.Card, Theme.CardLine, Theme.Warn, Ico.Radiation);
            btnQuarantine.Click += delegate { ShowPage(2); };
            scanBar.Controls.Add(btnQuarantine);

            // System card: stats plus the (conditionally visible) update button —
            // folded together instead of a separate "Updates" card
            cardSystem = new CardPanel(Lang.T("card.system"));
            cardSystem.Dock = DockStyle.Top;
            cardSystem.Height = 213;
            cardSystem.Margin = new Padding(6, 4, 6, 4);
            sysNames = new Label();
            sysNames.Dock = DockStyle.Fill;
            sysNames.Font = new Font("Consolas", 9.5f);
            sysNames.ForeColor = Theme.Text;
            sysNames.BackColor = Theme.Card;
            sysNames.Text = Lang.T("sys.labels");
            statsLabel = new Label();
            statsLabel.Dock = DockStyle.Right;
            statsLabel.Width = 150;
            statsLabel.Font = new Font("Consolas", 9.5f);
            statsLabel.ForeColor = Theme.Warn;
            statsLabel.BackColor = Theme.Card;
            statsLabel.TextAlign = ContentAlignment.TopRight;
            btnUpdate = MakeLightButton(Lang.T("btn.updateDb"), Ico.Refresh);
            btnUpdate.BackColor = Theme.Card;
            btnUpdate.Dock = DockStyle.Bottom;
            btnUpdate.Click += delegate { RunFreshclam(); };
            cardSystem.Controls.Add(sysNames);
            cardSystem.Controls.Add(statsLabel);
            cardSystem.Controls.Add(btnUpdate);

            // Last-activity strip: one line instead of a scrollable log card — the
            // full history is one click away via "Open Log File", so the dashboard
            // doesn't need to dedicate a big scrollable panel to it.
            activityRow = new StatusBanner();
            activityRow.Dock = DockStyle.Top;
            activityRow.Height = 56;
            activityRow.Margin = new Padding(6, 4, 6, 4);
            activityRow.Padding = new Padding(20, 2, 10, 8); // bottom inset keeps children off the card shadow
            activityRow.AccentColor = Theme.Accent;
            lastActivityLabel = new Label();
            lastActivityLabel.Dock = DockStyle.Fill;
            lastActivityLabel.AutoEllipsis = true;
            lastActivityLabel.Font = new Font("Consolas", 9f);
            lastActivityLabel.ForeColor = Theme.Muted;
            lastActivityLabel.BackColor = Theme.Card;
            lastActivityLabel.TextAlign = ContentAlignment.MiddleLeft;
            btnScanLog = MakeLightButton(Lang.T("btn.openLog"), Ico.LogIcon);
            btnScanLog.BackColor = Theme.Card;
            btnScanLog.Dock = DockStyle.Right;
            btnScanLog.Margin = new Padding(0, 0, 0, 0);
            btnScanLog.Click += delegate { OpenScanLog(); };
            activityRow.Controls.Add(lastActivityLabel);
            activityRow.Controls.Add(btnScanLog);

            // Dock=Top stacks in reverse add order: hero banner first, then the
            // scan tiles, the system card, and the last-activity strip
            page.Controls.Add(activityRow);
            page.Controls.Add(cardSystem);
            page.Controls.Add(scanBar);
            page.Controls.Add(statusBanner);
            return page;
        }

        Panel BuildLogsPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;
            page.Padding = new Padding(6);

            cardScan = new CardPanel(Lang.T("card.scanning"));
            cardScan.Dock = DockStyle.Fill;
            cardScan.Margin = new Padding(6);

            var buttonsRow = new FlowLayoutPanel();
            buttonsRow.Dock = DockStyle.Top;
            buttonsRow.Height = 42;
            buttonsRow.BackColor = Theme.Card;

            btnStop = MakeButton(Lang.T("btn.stop"), 110, Theme.Danger, Theme.DangerHot, Ico.StopIcon);
            btnStop.Enabled = false;
            btnStop.Click += delegate { StopCurrent(); };

            buttonsRow.Controls.Add(btnStop);

            log = new RichTextBox();
            log.Dock = DockStyle.Fill;
            log.ReadOnly = true;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.LogBg;
            log.ForeColor = Theme.Muted;
            log.Font = new Font("Consolas", 9.5f);
            log.HideSelection = false;

            cardScan.Controls.Add(log);
            cardScan.Controls.Add(buttonsRow);

            page.Controls.Add(cardScan);
            return page;
        }

        Panel BuildQuarantinePage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;
            page.Padding = new Padding(6);

            cardQuar = new CardPanel(Lang.T("card.quarantine"));
            cardQuar.Dock = DockStyle.Fill;
            cardQuar.Margin = new Padding(6);

            quarList = MakeList();
            quarList.Columns.Add(Lang.T("col.file"), 220);
            quarList.Columns.Add(Lang.T("col.origin"), 280);
            quarList.Columns.Add(Lang.T("col.when"), 130);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Height = 50;
            buttons.Padding = new Padding(8);
            buttons.BackColor = Theme.Card;

            btnQuarDelete = MakeButton(Lang.T("btn.deleteForever"), 170, Theme.Danger, Theme.DangerHot, Ico.Trash);
            btnQuarRestore = MakeButton(Lang.T("btn.restore"), 120, Theme.Accent, Theme.AccentHot, Ico.Restore);
            btnQuarToExcl = MakeButton(Lang.T("btn.toExclusions"), 125, Theme.Card, Theme.Bg, Ico.Ban);
            btnQuarOpenFolder = MakeButton(Lang.T("btn.openFolder"), 140, Theme.Card, Theme.Bg, Ico.FolderIcon);
            btnQuarExclusions = MakeButton(Lang.T("btn.exclusions"), 125, Theme.Card, Theme.Bg, Ico.Ban);

            btnQuarRestore.Click += delegate { RestoreSelectedQuarantine(false); };
            btnQuarToExcl.Click += delegate { RestoreSelectedQuarantine(true); };
            btnQuarDelete.Click += delegate { DeleteSelectedQuarantine(); };
            btnQuarOpenFolder.Click += delegate { Process.Start("explorer.exe", "\"" + quarDir + "\""); };
            btnQuarExclusions.Click += delegate { EditExclusions(); };

            buttons.Controls.AddRange(new Control[] { btnQuarRestore, btnQuarToExcl, btnQuarDelete, btnQuarOpenFolder, btnQuarExclusions });

            cardQuar.Controls.Add(quarList);
            cardQuar.Controls.Add(buttons);

            page.Controls.Add(cardQuar);
            return page;
        }

        void ReloadQuarantineList()
        {
            if (quarList == null || quarDir == null) return;
            try { if (!Directory.Exists(quarDir)) Directory.CreateDirectory(quarDir); }
            catch { return; }
            NeutralizeQuarantineFolder(); // pick up legacy/raw files before listing
            quarList.Items.Clear();
            var map = ReadQuarIndex(quarIndex);
            foreach (string f in Directory.GetFiles(quarDir))
            {
                string name = Path.GetFileName(f);
                if (string.Equals(name, "index.txt", StringComparison.OrdinalIgnoreCase)) continue;
                string origin = Lang.T("quarantine.unknownOrigin"), when = "";
                string[] meta;
                if (map.TryGetValue(name, out meta)) { origin = meta[1]; when = meta[2]; }
                // the list shows the original file name; the .quar suffix is an on-disk detail
                string display = name.EndsWith(QuarExt, StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - QuarExt.Length) : name;
                var item = new ListViewItem(new string[] { display, origin, when });
                item.Tag = f;
                quarList.Items.Add(item);
            }
            UpdateStatsUi();
        }

        void RestoreSelectedQuarantine(bool excludeToo)
        {
            if (quarList.SelectedItems.Count == 0) return;
            if (MessageBox.Show(this,
                Lang.T("msg.restoreConfirm"),
                Lang.T("quarantine.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (ListViewItem it in quarList.SelectedItems)
            {
                string path = (string)it.Tag;
                string origin = it.SubItems[1].Text;
                if (origin == Lang.T("quarantine.unknownOrigin"))
                {
                    MessageBox.Show(this, string.Format(Lang.T("msg.unknownOriginPath"), it.Text), Lang.T("quarantine.title"));
                    continue;
                }
                try
                {
                    if (File.Exists(origin))
                    {
                        MessageBox.Show(this, string.Format(Lang.T("msg.fileExists"), origin), Lang.T("quarantine.title"));
                        continue;
                    }
                    if (path.EndsWith(QuarExt, StringComparison.OrdinalIgnoreCase))
                    {
                        // neutralized file: XOR back into the original bytes at the origin path
                        XorCopy(path, origin);
                        File.Delete(path);
                    }
                    else
                        File.Move(path, origin); // raw legacy file — plain move
                    RemoveQuarIndexEntry(quarIndex, Path.GetFileName(path));
                    if (excludeToo) AddExclusion(origin);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("title.error")); }
            }
            if (excludeToo) SaveSettings();
            ReloadQuarantineList();
        }

        void DeleteSelectedQuarantine()
        {
            if (quarList.SelectedItems.Count == 0) return;
            if (MessageBox.Show(this,
                string.Format(Lang.T("msg.deleteConfirm"), quarList.SelectedItems.Count),
                Lang.T("quarantine.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (ListViewItem it in quarList.SelectedItems)
            {
                string path = (string)it.Tag;
                try { File.Delete(path); RemoveQuarIndexEntry(quarIndex, Path.GetFileName(path)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("title.error")); }
            }
            ReloadQuarantineList();
        }

        Panel BuildSettingsPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;
            page.Padding = new Padding(6);

            cardSettingsPanel = new CardPanel(Lang.T("card.settings"));
            cardSettingsPanel.Dock = DockStyle.Fill;
            cardSettingsPanel.Margin = new Padding(6);

            chkMonitor = MakeCheck("", 20, 56);
            chkMonitor.CheckedChanged += delegate { OnMonitorToggled(); };

            btnWatchDirs = MakeLightButton(Lang.T("btn.folders"), Ico.FolderIcon);
            btnWatchDirs.BackColor = Theme.Card;
            btnWatchDirs.SetBounds(430, 50, 130, 30);

            btnWatchDirs.Click += delegate { EditWatchDirs(); };

            chkRiskyOnly = MakeCheck(Lang.T("settings.riskyOnly"), 20, 92);
            chkRiskyOnly.Checked = true;
            chkRiskyOnly.CheckedChanged += delegate { SaveSettings(); };

            chkQuarantine = MakeCheck(Lang.T("settings.autoQuarantine"), 20, 128);
            chkQuarantine.Checked = false;
            chkQuarantine.CheckedChanged += delegate { SaveSettings(); };

            chkAutoUpdate = MakeCheck(Lang.T("settings.autoUpdate"), 20, 164);
            chkAutoUpdate.Checked = true;
            chkAutoUpdate.CheckedChanged += delegate { SaveSettings(); };

            chkFullRisky = MakeCheck(Lang.T("settings.fullRisky"), 20, 200);
            chkFullRisky.Checked = true;
            chkFullRisky.CheckedChanged += delegate { SaveSettings(); };

            chkAutostart = MakeCheck(Lang.T("settings.autostart"), 20, 236);
            chkAutostart.Checked = IsAutostartEnabled();
            chkAutostart.CheckedChanged += delegate { SetAutostart(chkAutostart.Checked); };

            langLabel = new Label();
            langLabel.Text = Lang.T("settings.language");
            langLabel.AutoSize = true;
            langLabel.ForeColor = Theme.Text;
            langLabel.BackColor = Theme.Card;
            langLabel.Location = new Point(20, 286);
            btnLangEn = MakeButton("English", 90, Theme.Btn, Theme.BtnHot);
            btnLangEn.TextColor = Theme.BtnText;
            btnLangEn.BackColor = Theme.Card;
            btnLangEn.SetBounds(180, 280, 90, 30);
            btnLangEn.Click += delegate { SetLanguage(Lang.Language.English); };
            btnLangUk = MakeButton("Українська", 110, Theme.Btn, Theme.BtnHot);
            btnLangUk.TextColor = Theme.BtnText;
            btnLangUk.BackColor = Theme.Card;
            btnLangUk.SetBounds(276, 280, 110, 30);
            btnLangUk.Click += delegate { SetLanguage(Lang.Language.Ukrainian); };
            UpdateLangButtons();

            btnInstall = MakeLightButton(IsInstalled
                ? Lang.T("btn.installedPF") : Lang.T("btn.installPF"), Ico.Download);
            btnInstall.BackColor = Theme.Card;
            btnInstall.SetBounds(20, 326, 290, 30);
            btnInstall.Enabled = !IsInstalled;
            btnInstall.Click += delegate
            {
                if (MessageBox.Show(this,
                    string.Format(Lang.T("msg.installConfirm"), InstallDir),
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    LaunchInstaller();
            };

            // Only relevant if C:\Windows\Temp isn't watchable yet — hidden once fixed
            // (via this button, or automatically after --install elevates and fixes it).
            btnFixWinTemp = MakeLightButton(Lang.T("btn.fixWinTemp"), Ico.Unlock);
            btnFixWinTemp.BackColor = Theme.Card;
            btnFixWinTemp.SetBounds(20, 364, 290, 30);
            btnFixWinTemp.Visible = !CanWatchDirectory(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
            btnFixWinTemp.Click += delegate { FixWinTempAccess(); };

            cardSettingsPanel.Controls.Add(chkMonitor);
            cardSettingsPanel.Controls.Add(btnWatchDirs);
            cardSettingsPanel.Controls.Add(chkRiskyOnly);
            cardSettingsPanel.Controls.Add(chkQuarantine);
            cardSettingsPanel.Controls.Add(chkAutoUpdate);
            cardSettingsPanel.Controls.Add(chkFullRisky);
            cardSettingsPanel.Controls.Add(chkAutostart);
            cardSettingsPanel.Controls.Add(langLabel);
            cardSettingsPanel.Controls.Add(btnLangEn);
            cardSettingsPanel.Controls.Add(btnLangUk);
            cardSettingsPanel.Controls.Add(btnInstall);
            cardSettingsPanel.Controls.Add(btnFixWinTemp);

            page.Controls.Add(cardSettingsPanel);
            return page;
        }

        void ShowPage(int idx)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == idx;
                navs[i].SetActive(i == idx);
            }
            if (idx == 0) { UpdateStatsUi(); RefreshHistory(); }
            if (idx == 2) { ReloadQuarantineList(); }
        }

        // Shows just the last line of scans.log — the full history is one click
        // away via "Open Log File" (see activityRow in BuildDashboardPage).
        void RefreshHistory()
        {
            if (lastActivityLabel == null) return;
            try
            {
                string last = null;
                if (File.Exists(scanLogPath))
                {
                    string[] lines = File.ReadAllLines(scanLogPath);
                    for (int i = lines.Length - 1; i >= 0; i--)
                        if (lines[i].Trim().Length > 0) { last = lines[i]; break; }
                }
                lastActivityLabel.Text = last ?? Lang.T("history.empty");
            }
            catch { }
        }

        void SetScanEnabled(bool on)
        {
            foreach (ModernButton b in scanButtons) if (b != null) b.Enabled = on;
        }

        ModernButton MakeButton(string text, int width, Color back, Color hover, IconDraw icon = null)
        {
            var b = new ModernButton(text, back, hover, Theme.Text);
            b.Width = width;
            b.Icon = icon;
            return b;
        }

        // Light button with dark text
        ModernButton MakeLightButton(string text, IconDraw icon = null)
        {
            var b = new ModernButton(text, Theme.Btn, Theme.BtnHot, Theme.BtnText);
            b.Icon = icon;
            return b;
        }

        // Big square button with the icon centered above the label — dashboard scan actions
        ModernButton MakeCardButton(string text, Color back, Color hover, Color fore, IconDraw icon)
        {
            var b = new ModernButton(text, back, hover, fore);
            b.Icon = icon;
            b.CardStyle = true;
            b.Width = 152;
            b.Height = 104;
            b.Font = new Font("Segoe UI Semibold", 8.5f);
            return b;
        }

        Toggle MakeCheck(string text, int x, int y)
        {
            var c = new Toggle(text);
            c.Location = new Point(x, y);
            c.BackColor = Theme.Card;
            return c;
        }

        // Dark ListView with custom column headers (the system ones are white)
        static ListView MakeList()
        {
            var list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.Dock = DockStyle.Fill;
            list.BackColor = Theme.LogBg;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.None;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.OwnerDraw = true;
            list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, " " + e.Header.Text, ((ListView)s).Font,
                    e.Bounds, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            list.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            return list;
        }

        void SetHero(ShieldState state, string title, string sub)
        {
            shield.SetState(state);
            heroTitle.Text = title;
            heroSub.Text = sub;
            switch (state)
            {
                case ShieldState.Ok: heroTitle.ForeColor = statusBanner.AccentColor = Theme.Good; break;
                case ShieldState.Danger: heroTitle.ForeColor = statusBanner.AccentColor = Theme.Danger; break;
                case ShieldState.Busy: heroTitle.ForeColor = Theme.Text; statusBanner.AccentColor = Theme.Accent; break;
                default: heroTitle.ForeColor = statusBanner.AccentColor = Theme.Warn; break;
            }
            statusBanner.Invalidate();
        }

        // ---------- Interface language ----------

        void SetLanguage(Lang.Language lang)
        {
            if (Lang.Current == lang) return;
            Lang.Current = lang;
            ApplyLanguage();
            SaveSettings();
        }

        void UpdateLangButtons()
        {
            bool en = Lang.Current == Lang.Language.English;
            btnLangEn.Back = en ? Theme.Accent : Theme.Btn;
            btnLangEn.Hover = en ? Theme.AccentHot : Theme.BtnHot;
            btnLangEn.TextColor = en ? Theme.Text : Theme.BtnText;
            btnLangEn.Invalidate();
            btnLangUk.Back = !en ? Theme.Accent : Theme.Btn;
            btnLangUk.Hover = !en ? Theme.AccentHot : Theme.BtnHot;
            btnLangUk.TextColor = !en ? Theme.Text : Theme.BtnText;
            btnLangUk.Invalidate();
        }

        // Re-applies text to every persistent control after a language switch.
        // Dialogs and message boxes need no such handling: they're built fresh
        // each time they're opened and simply pick up the current language.
        void ApplyLanguage()
        {
            navs[0].Text = Lang.T("nav.dashboard");
            navs[1].Text = Lang.T("nav.logs");
            navs[2].Text = Lang.T("nav.quarantine");
            navs[3].Text = Lang.T("nav.settings");

            cardSystem.HeaderText = Lang.T("card.system"); cardSystem.Invalidate();
            if (cardQuar != null) { cardQuar.HeaderText = Lang.T("card.quarantine"); cardQuar.Invalidate(); }
            if (cardScan != null) { cardScan.HeaderText = Lang.T("card.scanning"); cardScan.Invalidate(); }
            cardSettingsPanel.HeaderText = Lang.T("card.settings"); cardSettingsPanel.Invalidate();

            dashQuick.Text = Lang.T("btn.quickScan");
            dashScanFile.Text = Lang.T("btn.scanFileDash");
            dashScanFolder.Text = Lang.T("btn.scanFolderDash");
            dashScanAll.Text = Lang.T("btn.scanAll");
            btnUpdate.Text = Lang.T("btn.updateDb");
            btnScanLog.Text = Lang.T("btn.openLog");
            btnQuarantine.Text = Lang.T("btn.openQuarantine");
            if (btnQuarExclusions != null) btnQuarExclusions.Text = Lang.T("btn.exclusions");
            if (btnQuarDelete != null) btnQuarDelete.Text = Lang.T("btn.deleteForever");
            if (btnQuarRestore != null) btnQuarRestore.Text = Lang.T("btn.restore");
            if (btnQuarToExcl != null) btnQuarToExcl.Text = Lang.T("btn.toExclusions");
            if (btnQuarOpenFolder != null) btnQuarOpenFolder.Text = Lang.T("btn.openFolder");
            if (quarList != null)
            {
                quarList.Columns[0].Text = Lang.T("col.file");
                quarList.Columns[1].Text = Lang.T("col.origin");
                quarList.Columns[2].Text = Lang.T("col.when");
            }
            btnStop.Text = Lang.T("btn.stop");
            dashStop.Text = Lang.T("btn.stop");
            btnWatchDirs.Text = Lang.T("btn.folders");
            sysNames.Text = Lang.T("sys.labels");

            chkRiskyOnly.Text = Lang.T("settings.riskyOnly");
            chkQuarantine.Text = Lang.T("settings.autoQuarantine");
            chkAutoUpdate.Text = Lang.T("settings.autoUpdate");
            chkFullRisky.Text = Lang.T("settings.fullRisky");
            chkAutostart.Text = Lang.T("settings.autostart");
            UpdateMonitorLabel();
            langLabel.Text = Lang.T("settings.language");
            UpdateLangButtons();
            btnInstall.Text = Lang.T(IsInstalled ? "btn.installedPF" : "btn.installPF");
            btnFixWinTemp.Text = Lang.T("btn.fixWinTemp");

            trayOpenItem.Text = Lang.T("tray.open");
            trayExitItem.Text = Lang.T("tray.exit");

            RefreshDbStatus();
            UpdateStatsUi();
            RefreshHistory();
        }

        // A second instance of the app sent a broadcast — bring this window forward
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmShow && WmShow != 0) RestoreFromTray();
            base.WndProc(ref m);
        }

        void RestoreFromTray()
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!reallyClose && e.CloseReason == CloseReason.UserClosing)
            {
                // The close button minimizes to tray; actual exit is via the tray menu
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                return;
            }
            StopWatchers();
            StopCurrent();
            StopClamd();
            tray.Visible = false;
        }

    }
}
