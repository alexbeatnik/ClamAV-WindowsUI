// ClamAV Windows UI — a lightweight wrapper around clamscan/freshclam.
// Dark theme, sidebar navigation, card-based dashboard.
// Build: .\build.ps1 (uses the csc.exe compiler built into Windows, .NET Framework 4.8)
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

[assembly: AssemblyTitle("ClamAV UI")]
[assembly: AssemblyProduct("ClamAV UI")]
[assembly: AssemblyVersion("0.0.1.0")]
[assembly: AssemblyFileVersion("0.0.1.0")]

namespace ClamAVUI
{
    static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xffff;
        [DllImport("user32.dll")]
        public static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    // Dark theme palette
    static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(23, 24, 28);    // window background
        public static readonly Color Card      = Color.FromArgb(35, 37, 43);    // cards
        public static readonly Color CardLine  = Color.FromArgb(52, 55, 63);    // thin card border
        public static readonly Color LogBg     = Color.FromArgb(16, 17, 20);    // log/list background
        public static readonly Color Text      = Color.FromArgb(230, 232, 236);
        public static readonly Color Muted     = Color.FromArgb(154, 161, 173);
        public static readonly Color Accent    = Color.FromArgb(59, 130, 246);  // blue
        public static readonly Color AccentHot = Color.FromArgb(106, 161, 248);
        public static readonly Color Good      = Color.FromArgb(35, 165, 90);   // green shield
        public static readonly Color Warn      = Color.FromArgb(232, 197, 71);  // yellow (values)
        public static readonly Color Danger    = Color.FromArgb(239, 68, 68);
        public static readonly Color DangerHot = Color.FromArgb(248, 113, 113);
        public static readonly Color Disabled  = Color.FromArgb(92, 97, 108); // clearly gray, not just faded
        public static readonly Color Btn       = Color.FromArgb(216, 219, 226); // light buttons
        public static readonly Color BtnHot    = Color.FromArgb(233, 235, 240);
        public static readonly Color BtnText   = Color.FromArgb(51, 54, 62);
        public const int Radius = 10; // card corner radius

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Dark window title bar (Win10 1903+); without it the frame stays white
        public static void DarkTitleBar(Form f)
        {
            EventHandler apply = delegate
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(f.Handle, 19, ref on, 4); // older Win10 builds
                }
                catch { }
            };
            if (f.IsHandleCreated) apply(null, EventArgs.Empty);
            else f.HandleCreated += apply;
        }
    }

    // Two-language string table (English default, Ukrainian alternative).
    // Lang.Current selects the active language; Lang.T(key) resolves a string,
    // falling back to English and then to the key itself if nothing matches.
    static class Lang
    {
        public enum Language { English, Ukrainian }
        public static Language Current = Language.English;

        static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        static readonly Dictionary<string, string> Uk = new Dictionary<string, string>();

        public static string T(string key)
        {
            var dict = Current == Language.Ukrainian ? Uk : En;
            string v;
            if (dict.TryGetValue(key, out v)) return v;
            if (En.TryGetValue(key, out v)) return v;
            return key;
        }

        static void A(string key, string en, string uk) { En[key] = en; Uk[key] = uk; }

        static Lang()
        {
            // Install / uninstall
            A("install.title", "ClamAV UI — Setup", "ClamAV UI — встановлення");
            A("install.installing", "Installing ClamAV UI to Program Files…", "Встановлюю ClamAV UI у Program Files…");
            A("install.failed", "Installation failed:\r\n", "Не вдалося встановити:\r\n");
            A("uninstall.confirm", "Remove ClamAV UI along with ClamAV, the signature database and quarantine?", "Видалити ClamAV UI разом із ClamAV, базами сигнатур і карантином?");
            A("uninstall.done", "ClamAV UI has been removed.", "ClamAV UI видалено.");
            A("uninstall.error", "Removal error: ", "Помилка видалення: ");

            // Main window chrome
            A("status.ready", "Ready.", "Готово.");
            A("tray.open", "Open", "Відкрити");
            A("tray.exit", "Exit", "Вихід");

            // Top nav tabs
            A("nav.dashboard", "Dashboard", "Огляд");
            A("nav.scanner", "Scanner", "Сканер");
            A("nav.settings", "Settings", "Налаштування");

            // Cards
            A("card.system", "System", "Система");
            A("card.quarantine", "Quarantine", "Карантин");
            A("card.scanning", "Scanning", "Сканування");
            A("card.settings", "Settings", "Налаштування");

            // Buttons
            A("btn.quickScan", "QUICK SCAN", "ШВИДКИЙ СКАН");
            A("btn.scanFileDash", "SCAN FILE", "СКАНУВАТИ ФАЙЛ");
            A("btn.scanFolderDash", "SCAN FOLDER", "СКАНУВАТИ ПАПКУ");
            A("btn.scanAll", "FULL PC", "ВЕСЬ ПК");
            A("btn.updateDb", "UPDATE DATABASE", "ОНОВИТИ БАЗИ");
            A("btn.openLog", "OPEN LOG FILE", "ВІДКРИТИ ФАЙЛ ЖУРНАЛУ");
            A("btn.openQuarantine", "OPEN QUARANTINE", "ВІДКРИТИ КАРАНТИН");
            A("btn.exclusions", "EXCLUSIONS", "ВИКЛЮЧЕННЯ");
            A("btn.quick", "QUICK", "ШВИДКИЙ");
            A("btn.file", "FILE", "ФАЙЛ");
            A("btn.folder", "FOLDER", "ПАПКА");
            A("btn.stop", "STOP", "ЗУПИНИТИ");
            A("btn.folders", "FOLDERS…", "ПАПКИ…");
            A("btn.installedPF", "INSTALLED TO PROGRAM FILES", "ВСТАНОВЛЕНО В PROGRAM FILES");
            A("btn.installPF", "INSTALL TO PROGRAM FILES", "ВСТАНОВИТИ В PROGRAM FILES");
            A("btn.fixWinTemp", "FIX C:\\WINDOWS\\TEMP ACCESS", "ВІДНОВИТИ ДОСТУП ДО C:\\WINDOWS\\TEMP");
            A("btn.close", "Close", "Закрити");
            A("btn.toExclusions", "To exclusions", "У виключення");
            A("btn.delete", "Delete", "Видалити");
            A("btn.toQuarantine", "To quarantine", "В карантин");
            A("btn.deleteForever", "Delete permanently", "Видалити назавжди");
            A("btn.restore", "Restore", "Відновити");
            A("btn.openFolder", "Open folder", "Відкрити папку");
            A("btn.deleteFile", "Delete file", "Видалити файл");
            A("btn.removeFromList", "Remove from list", "Прибрати зі списку");
            A("btn.addFile", "Add file…", "Додати файл…");
            A("btn.addFolder", "Add folder…", "Додати папку…");
            A("btn.cancel", "Cancel", "Скасувати");

            // Settings page
            A("settings.monitorLabel", "Auto-check new files in folders ({0})", "Автоперевірка нових файлів у папках ({0})");
            A("settings.riskyOnly", "Check risky file types only (exe, scripts, archives, documents)", "Перевіряти лише небезпечні типи (exe, скрипти, архіви, документи)");
            A("settings.autoQuarantine", "Auto-quarantine (don't ask what to do with detections)", "Автоматично в карантин (не питати, що робити зі знайденим)");
            A("settings.autoUpdate", "Update database automatically (once a day)", "Оновлювати бази автоматично (раз на добу)");
            A("settings.fullRisky", "Full scan: risky file types only (much faster)", "У повному скані перевіряти лише небезпечні типи (набагато швидше)");
            A("settings.autostart", "Start with Windows (in tray)", "Запускати разом з Windows (у треї)");
            A("settings.language", "Interface language:", "Мова інтерфейсу:");
            A("msg.installConfirm", "The app will be copied to {0} together with ClamAV and the database,\r\nStart Menu/Desktop shortcuts will be created, and it will be registered in \"Apps\".\r\nAdministrator rights are required. Continue?",
                "Програма скопіюється в {0} разом із ClamAV і базами,\r\nз'являться ярлики в Пуску, на робочому столі та запис у «Програмах».\r\nПотрібні права адміністратора. Продовжити?");
            A("msg.fixWinTempConfirm", "On this PC, C:\\Windows\\Temp is locked down so even reading its contents is denied "
                + "to regular apps — a common malware drop point can't be monitored as a result.\r\n\r\n"
                + "This will restore the default Windows read permission on that one folder only "
                + "(via a one-time administrator prompt). The app itself keeps running without admin rights "
                + "afterwards. Continue?",
                "На цьому ПК доступ до C:\\Windows\\Temp обмежено настільки, що звичайні програми не можуть "
                + "навіть прочитати її вміст — тож типове місце для дропу малваре лишається поза наглядом.\r\n\r\n"
                + "Зараз буде відновлено стандартний Windows-дозвіл на читання лише для цієї папки "
                + "(через одноразовий запит адміністратора). Сама програма й надалі працюватиме без прав адміністратора. Продовжити?");
            A("status.fixWinTempCancelled", "C:\\Windows\\Temp access fix cancelled.", "Відновлення доступу до C:\\Windows\\Temp скасовано.");
            A("status.fixWinTempFailed", "Could not restore access to C:\\Windows\\Temp (blocked by policy?).", "Не вдалося відновити доступ до C:\\Windows\\Temp (можливо, заблоковано політикою).");
            A("status.fixWinTempDone", "Access restored — C:\\Windows\\Temp is now monitored.", "Доступ відновлено — C:\\Windows\\Temp тепер під наглядом.");

            // Threat dialog
            A("threat.title", "Threats found — what to do?", "Знайдено загрози — що робити?");
            A("col.file", "File", "Файл");
            A("col.threat", "Threat", "Загроза");
            A("threat.hint", "The action applies to the selected files (none selected = all).", "Дія застосовується до виділених файлів (нічого не виділено = до всіх).");
            A("msg.deleteConfirm", "Permanently delete {0} file(s)?", "Видалити назавжди {0} файл(ів)?");
            A("title.deletion", "Deletion", "Видалення");
            A("title.error", "Error", "Помилка");
            A("status.exclusionsCount", "Exclusions: {0}.", "Виключень: {0}.");

            // Quarantine dialog
            A("quarantine.title", "Quarantine", "Карантин");
            A("col.origin", "Origin", "Звідки");
            A("col.when", "When", "Коли");
            A("quarantine.unknownOrigin", "(unknown)", "(невідомо)");
            A("msg.restoreConfirm", "WARNING: this file was flagged as infected. Really restore it?", "УВАГА: файл було позначено як заражений. Точно відновити?");
            A("msg.unknownOriginPath", "Unknown original path for {0}.\r\nOpen the quarantine folder and retrieve the file manually.", "Невідомий вихідний шлях для {0}.\r\nВідкрий папку карантину і забери файл вручну.");
            A("msg.fileExists", "File already exists: {0}", "Файл уже існує: {0}");
            A("msg.quarantineMoveFailed", "Failed to move to quarantine:\r\n{0}", "Не вдалося перемістити в карантин:\r\n{0}");

            // Exclusions dialog
            A("excl.title", "Scan exclusions", "Виключення зі сканування");
            A("col.path", "Path", "Шлях");
            A("col.type", "Type", "Тип");
            A("type.file", "file", "файл");
            A("type.folder", "folder", "папка");
            A("type.missing", "missing", "відсутній");
            A("msg.onlyExistingToQuarantine", "Only an existing file can be quarantined: {0}", "В карантин можна перемістити лише наявний файл: {0}");
            A("msg.deleteFromDiskConfirm", "Permanently delete {0} file(s) from disk and remove from the list?", "Видалити з диска {0} файл(ів) і прибрати зі списку?");

            // Watch dirs / path list editor
            A("watch.editTitle", "Folders to monitor (one per line)", "Папки для моніторингу (одна на рядок)");
            A("log.folderNotFound", "Folder not found, skipping: {0}\r\n", "Папку не знайдено, пропускаю: {0}\r\n");
            A("log.pathNotFound", "Path not found, skipping: {0}\r\n", "Шлях не знайдено, пропускаю: {0}\r\n");

            // Monitoring
            A("status.monitorOn", "Monitoring enabled: new files will be checked automatically.", "Моніторинг увімкнено: нові файли перевірятимуться автоматично.");
            A("status.monitorOff", "Monitoring disabled.", "Моніторинг вимкнено.");
            A("log.watchFailed", "Failed to watch {0}: {1}\r\n", "Не вдалося стежити за {0}: {1}\r\n");
            A("log.watchingFolders", "Monitoring: {0} folder(s).\r\n", "Моніторинг: {0} папок.\r\n");

            // Scanning: pickers and generic
            A("dlg.pickFolder", "Choose a folder to scan", "Вибери папку для сканування");
            A("dlg.pickFile", "Choose a file to scan", "Вибери файл для сканування");
            A("log.scanning", "Scanning: {0}\r\n", "Сканую: {0}\r\n");
            A("log.buildingList", "Building file list…\r\n", "Складаю список файлів…\r\n");
            A("status.scanning", "Scanning…", "Сканування…");
            A("log.listCreateFailedInline", "Could not create the file list ({0}), passing files on the command line.\r\n", "Не вдалося створити список файлів ({0}), передаю в рядку.\r\n");
            A("log.newFilesHeader", "\r\n[{0:HH:mm:ss}] New files ({1}) — auto-check:\r\n", "\r\n[{0:HH:mm:ss}] Нові файли ({1}) — автоперевірка:\r\n");
            A("status.autoCheck", "Auto-checking new files: {0}…", "Автоперевірка нових файлів: {0}…");
            A("log.filesToCheck", "Files to check: {0}", "Файлів для перевірки: {0}");
            A("desc.autoCheck", "auto-check of new files", "автоперевірка нових файлів");

            // Progress / ETA
            A("status.progress", "Scanned {0} of {1} ({2:0}%){3}, threats: {4}", "Скановано {0} із {1} ({2:0}%){3}, загроз: {4}");
            A("eta.remainingPrefix", ", remaining ", ", залишилось ");
            A("eta.estimating", ", estimating time…", ", оцінюю час…");
            A("time.hm", "{0:0}h {1:0}m", "{0:0} год {1:0} хв");
            A("time.ms", "{0:0}m {1:0}s", "{0:0} хв {1:0} с");
            A("time.s", "{0:0}s", "{0:0} с");

            // Heartbeat
            A("log.hbListing", "[{0:HH:mm:ss}] Building file list… found {1}, elapsed {2}\r\n", "[{0:HH:mm:ss}] Складаю список файлів… знайдено {1}, минуло {2}\r\n");
            A("log.hbEngineLoading", "[{0:HH:mm:ss}] Engine is loading the database into memory… elapsed {1}\r\n", "[{0:HH:mm:ss}] Рушій вантажить бази в пам'ять… минуло {1}\r\n");
            A("log.hbRunning", "[{0:HH:mm:ss}] Running… scanned {1}, elapsed {2}\r\n", "[{0:HH:mm:ss}] Триває… проскановано {1}, минуло {2}\r\n");
            A("log.hbBigFile", "[{0:HH:mm:ss}] Scanning a large file… scanned {1} of {2} ({3:0}%), elapsed {4}\r\n", "[{0:HH:mm:ss}] Сканую великий файл… проскановано {1} із {2} ({3:0}%), минуло {4}\r\n");
            A("log.hbProgress", "[{0:HH:mm:ss}] Scanned {1} of {2} ({3:0}%), {4} files remaining{5}{6}\r\n", "[{0:HH:mm:ss}] Проскановано {1} із {2} ({3:0}%), залишилось {4} файлів{5}{6}\r\n");
            A("log.threatsSuffix", ", threats: {0}", ", загроз: {0}");

            // Log file / history
            A("history.empty", "No scans yet.", "Ще не було жодного сканування.");
            A("log.emptyLogFile", "The log is empty — no scans yet.", "Журнал поки порожній — ще не було жодного сканування.");

            // Auto-update
            A("status.dbUpToDate", "Signature database is up to date.", "Бази сигнатур актуальні.");
            A("tray.dbUpdateDownloading", "A database update is available — downloading…", "Вийшло оновлення баз сигнатур — завантажую…");
            A("log.dbNewerAutoDownload", "\r\n[{0:HH:mm}] A newer database is available — downloading automatically…\r\n", "\r\n[{0:HH:mm}] Доступні новіші бази — завантажую автоматично…\r\n");
            A("hero.dbUpdateAvailable", "A signature database update is available", "Доступне оновлення баз сигнатур");
            A("status.dbUpdateAvailablePress", "A database update is available — press \"Update Database\".", "Доступне оновлення баз — натисни «Оновити бази».");
            A("tray.dbUpdateAvailablePress", "A signature database update is available — press \"Update Database\".", "Доступне оновлення баз сигнатур — натисни «Оновити бази».");

            // Full scan
            A("msg.fullScanRiskyWarn", "Full scan ({0}): risky file types only — exe, scripts,\r\narchives, documents. This is much faster than checking everything\r\n(all files can be enabled in Settings). Continue?",
                "Повне сканування ({0}): небезпечні типи файлів — exe, скрипти,\r\nархіви, документи. Це набагато швидше, ніж перевіряти все підряд\r\n(усі файли можна ввімкнути в налаштуваннях). Продовжити?");
            A("msg.fullScanAllWarn", "Full scan ({0}) of ALL files can take many hours.\r\nContinue?", "Повне сканування ({0}) УСІХ файлів може тривати багато годин.\r\nПродовжити?");
            A("title.fullScan", "Full PC Scan", "Сканування всього ПК");
            A("desc.fullScan", "full scan ({0})", "повне сканування ({0})");
            A("log.fullScanRisky", "Full scan (risky types): {0}\r\n", "Повне сканування (небезпечні типи): {0}\r\n");
            A("log.fullScanAll", "Full scan (all files): {0}\r\n", "Повне сканування (усі файли): {0}\r\n");
            A("status.fullScanRunning", "Full PC scan…", "Повне сканування ПК…");

            // Quick scan
            A("desc.quickScan", "quick scan", "швидке сканування");
            A("log.quickScanHeader", "Quick scan: risky file types in common infection points.\r\n", "Швидке сканування: небезпечні типи файлів у типових місцях зараження.\r\n");
            A("log.quickScanProcesses", "  + executables of running processes\r\n\r\n", "  + виконувані файли запущених процесів\r\n\r\n");
            A("status.quickScanRunning", "Quick scan…", "Швидке сканування…");

            // File listing
            A("status.buildingListFound", "Building file list… found {0}", "Складаю список файлів… знайдено {0}");
            A("status.scanCancelled", "Scan cancelled.", "Сканування скасовано.");
            A("log.cancelled", "Cancelled.\r\n", "Скасовано.\r\n");
            A("status.noFiles", "No files to check.", "Немає файлів для перевірки.");
            A("log.noFiles", "No files to check.\r\n", "Немає файлів для перевірки.\r\n");
            A("status.listCreateFailed", "Could not create the file list.", "Не вдалося створити список файлів.");
            A("log.listCreateFailed", "Could not create the file list: {0}\r\n", "Не вдалося створити список файлів: {0}\r\n");

            // clamd engine
            A("status.engineStarting", "Starting scan engine (loading database into memory)…", "Запускаю рушій сканування (бази вантажаться в пам'ять)…");
            A("log.engineStarting", "Starting the clamd engine (loading database into memory, ~30 seconds)…\r\n", "Запускаю рушій clamd (бази вантажаться в пам'ять, ~пів хвилини)…\r\n");
            A("log.daemonFallback", "clamd failed to start ({0}) — falling back to clamscan.\r\n", "clamd не запустився ({0}) — сканую через clamscan.\r\n");
            A("log.scanningThreads", "Scanning using {0} parallel process(es).\r\n", "Сканую у {0} потоки.\r\n");
            A("log.clamdscanStartFailed", "Failed to start clamdscan: {0}\r\n", "Не вдалося запустити clamdscan: {0}\r\n");
            A("err.daemonExited", "clamd exited unexpectedly", "clamd несподівано завершився");
            A("err.daemonTimeout", "clamd did not respond within 3 minutes", "clamd не відповів за 3 хвилини");

            // Scan output / results
            A("status.scannedFound", "Scanned: {0}, threats found: {1}", "Скановано: {0}, знайдено загроз: {1}");
            A("log.summary", "\r\nSummary: scanned {0} files in {1}, threats found: {2}\r\n", "\r\nПідсумок: проскановано {0} файлів за {1}, знайдено загроз: {2}\r\n");
            A("status.doneClean", "Done. Scanned: {0}. No threats found.", "Готово. Скановано: {0}. Загроз не знайдено.");
            A("log.newFilesClean", "New files are clean ✔\r\n", "Нові файли чисті ✔\r\n");
            A("tray.newFilesClean", "Checked new files: {0} — clean.", "Перевірено нових файлів: {0} — чисто.");
            A("log.noThreatsFound", "\r\nNo threats found ✔\r\n", "\r\nЗагроз не знайдено ✔\r\n");
            A("tray.scanDoneClean", "Scan complete: no threats found.", "Сканування завершено: загроз не знайдено.");
            A("status.threatsFound", "Scanned: {0}. THREATS FOUND: {1}{2}", "Скановано: {0}. ЗНАЙДЕНО ЗАГРОЗ: {1}{2}");
            A("status.quarantinedSuffix", ", quarantined: {0}", ", у карантині: {0}");
            A("hero.threatsFoundTitle", "Threats found!", "Знайдено загрози!");
            A("hero.threatsFoundSub", "Infected files: {0}{1} — check the log", "Заражених файлів: {0}{1} — перевір лог");
            A("log.threatsFound", "\r\nTHREATS FOUND: {0}{1}\r\n", "\r\nЗНАЙДЕНО ЗАГРОЗ: {0}{1}\r\n");
            A("tray.threatsFoundWarn", "WARNING: threats found: {0}{1}", "УВАГА: знайдено загроз: {0}{1}");
            A("status.scanInterrupted", "Scan interrupted or an error occurred (code {0}).", "Сканування перервано або сталася помилка (код {0}).");

            // Install / download ClamAV
            A("status.installCancelled", "Installation cancelled.", "Встановлення скасовано.");
            A("msg.offerInstallChoice", "ClamAV was not found next to the program. How do you want to set it up?\r\n\r\n"
                + "YES — install to Program Files: the app copies itself there, downloads ClamAV\r\n"
                + "(~220 MB) and the database, and adds Start Menu/Desktop shortcuts and an \"Apps\" entry.\r\n"
                + "Administrator rights are required.\r\n\r\n"
                + "NO — portable mode: download ClamAV into the current folder.",
                "ClamAV не знайдено поруч з програмою. Як налаштувати?\r\n\r\n"
                + "ТАК — встановити в Program Files: програма скопіюється туди, скачає ClamAV\r\n"
                + "(~220 МБ) і бази, додасть ярлики в Пуск, на робочий стіл та в «Програми».\r\n"
                + "Потрібні права адміністратора.\r\n\r\n"
                + "НІ — портативний режим: скачати ClamAV у поточну папку.");
            A("msg.offerPortableDownload", "ClamAV was not found next to the program.\r\n\r\n"
                + "Download it automatically from GitHub (~220 MB) and install it into the \"clamav\" folder?\r\n"
                + "The signature database will be downloaded automatically afterwards.",
                "ClamAV не знайдено поруч з програмою.\r\n\r\n"
                + "Завантажити його автоматично з GitHub (~220 МБ) і встановити в папку \"clamav\"?\r\n"
                + "Після цього автоматично завантажаться бази сигнатур.");
            A("status.foundArchiveExtracting", "Found a downloaded archive — extracting…", "Знайдено завантажений архів — розпаковую…");
            A("hero.installingClamAV", "Installing ClamAV", "Установлення ClamAV");
            A("hero.extractingArchive", "Extracting archive…", "Розпаковую архів…");
            A("status.findingLatestClamAV", "Looking up the latest ClamAV release on GitHub…", "Шукаю останню версію ClamAV на GitHub…");
            A("hero.findingLatestRelease", "Looking up the latest release…", "Шукаю останній реліз…");
            A("log.downloading", "Downloading {0}\r\n", "Завантажую {0}\r\n");
            A("status.downloadingClamAV", "Downloading ClamAV: {0} of {1} MB", "Завантаження ClamAV: {0} з {1} МБ");
            A("status.clamAVDownloadCancelled", "ClamAV download cancelled.", "Завантаження ClamAV скасовано.");
            A("hero.installCancelled", "Installation cancelled", "Установлення скасовано");
            A("hero.pressUpdateRetry", "Press \"Update Database\" to try again", "Натисни «Оновити бази», щоб спробувати ще раз");
            A("status.clamAVDownloadFailed", "Failed to download ClamAV: {0}", "Не вдалося завантажити ClamAV: {0}");
            A("hero.downloadError", "Download error", "Помилка завантаження");
            A("hero.checkConnectionRetry", "Check your internet connection and press \"Update Database\" again", "Перевір інтернет-з'єднання і натисни «Оновити бази» ще раз");
            A("status.extractingClamAV", "Extracting ClamAV…", "Розпаковую ClamAV…");
            A("err.noClamscanInArchive", "clamscan.exe not found in the archive", "В архіві немає clamscan.exe");
            A("status.clamAVInstallError", "ClamAV installation error: {0}", "Помилка встановлення ClamAV: {0}");
            A("hero.installError", "Installation error", "Помилка встановлення");
            A("status.clamAVInstalled", "ClamAV installed.", "ClamAV встановлено.");
            A("log.clamAVInstalled", "ClamAV installed ✔\r\n", "ClamAV встановлено ✔\r\n");

            // DB update
            A("msg.cooldownWarn", "The update server has temporarily rate-limited downloads from this address (HTTP 429 —\r\n"
                + "too many requests). This will resolve on its own, expected around {0}.\r\n"
                + "Retrying may extend the block. Try anyway?",
                "Сервер оновлень тимчасово обмежив завантаження з цієї адреси (HTTP 429 —\r\n"
                + "забагато запитів). Це мине саме собою, орієнтовно після {0}.\r\n"
                + "Повторні спроби можуть подовжити блокування. Спробувати все одно?");
            A("log.updatingDbFirstTime", "Updating the signature database (first time is ~200 MB, please wait)…\r\n\r\n", "Оновлюю бази сигнатур (перший раз це ~200 МБ, зачекай)…\r\n\r\n");
            A("log.autoUpdating", "\r\n[{0:HH:mm}] Auto-updating database…\r\n", "\r\n[{0:HH:mm}] Автооновлення баз…\r\n");
            A("status.autoUpdatingDb", "Auto-updating database…", "Автооновлення баз…");
            A("status.updatingDb", "Updating database…", "Оновлення баз…");
            A("hero.updatingDb", "Updating database", "Оновлення баз");
            A("hero.downloadingSignatures", "Downloading signatures from database.clamav.net…", "Завантажую сигнатури з database.clamav.net…");
            A("log.dbAlreadyCurrent", "{0}: already up to date (version {1})\r\n", "{0}: вже актуальна (версія {1})\r\n");
            A("status.dbUpdated", "Database updated.", "Бази оновлено.");
            A("status.dbAlreadyCurrent2", "Database already up to date.", "Бази вже актуальні.");
            A("log.dbUpdated", "\r\nDatabase updated ✔\r\n", "\r\nБази оновлено ✔\r\n");
            A("log.dbAlreadyCurrentLog", "\r\nDatabase already up to date ✔\r\n", "\r\nБази вже актуальні ✔\r\n");
            A("status.updateCancelled", "Update cancelled.", "Оновлення скасовано.");
            A("log.updateCancelled", "\r\nUpdate cancelled.\r\n", "\r\nОновлення скасовано.\r\n");
            A("status.serverRateLimited", "Update server temporarily rate-limited us — will retry later.", "Сервер оновлень тимчасово обмежив завантаження — спробую пізніше.");
            A("log.rateLimitedExplain", "\r\nUpdate server responded 429: too many downloads from this address.\r\n"
                + "This is a temporary server-side limit — your internet connection is fine. The current database still works.\r\n",
                "\r\nСервер оновлень відповів 429: забагато завантажень з цієї адреси.\r\n"
                + "Це тимчасове обмеження сервера, з інтернетом усе гаразд. Наявні бази працюють.\r\n");
            A("log.nextAttempt", "Next automatic attempt: after {0:HH:mm}.\r\n", "Наступна автоматична спроба — після {0:HH:mm}.\r\n");
            A("status.updateError", "Database update error.", "Помилка оновлення баз.");
            A("log.updateErrorDetail", "\r\nUpdate error: {0}\r\nCheck your internet connection.\r\n", "\r\nПомилка оновлення: {0}\r\nПеревір інтернет-з'єднання.\r\n");
            A("err.notADatabaseFile", "{0}: the downloaded file doesn't look like a ClamAV database", "{0}: завантажений файл не схожий на базу ClamAV");
            A("status.downloadingDb", "Downloading {0}: {1:0} / {2:0} MB ({3:0}%)", "Завантаження {0}: {1:0} / {2:0} МБ ({3:0}%)");
            A("log.dbFileDownloaded", "{0} downloaded ✔\r\n", "{0} завантажено ✔\r\n");

            // Misc / process / autostart
            A("log.processStartFailed", "Failed to start {0}: {1}\r\n", "Не вдалося запустити {0}: {1}\r\n");
            A("status.startError", "Startup error.", "Помилка запуску.");
            A("hero.scanningTitle", "Scanning…", "Сканування…");
            A("hero.scanningSub", "This may take a while", "Це може зайняти якийсь час");
            A("status.autostartOn", "Autostart enabled.", "Автозапуск увімкнено.");
            A("status.autostartOff", "Autostart disabled.", "Автозапуск вимкнено.");
            A("log.clamscanNotFound", "clamscan.exe not found. Press \"Update Database\" to download ClamAV automatically.\r\n", "clamscan.exe не знайдено. Натисни «Оновити бази», щоб завантажити ClamAV автоматично.\r\n");
            A("log.clamAVPath", "ClamAV: {0}\r\n", "ClamAV: {0}\r\n");
            A("hero.clamAVNotFound", "ClamAV not found", "ClamAV не знайдено");
            A("hero.putPortableClamAV", "Place a portable ClamAV build in the \"clamav\" folder next to the program", "Поклади portable ClamAV у папку \"clamav\" поруч з програмою");
            A("hero.protected", "Protected", "Захищено");
            A("hero.dbFrom", "Signature database from {0}", "Бази сигнатур від {0}");
            A("hero.dbNeeded", "Signature database needed", "Потрібні бази сигнатур");
            A("hero.pressUpdateFirstTime", "Press \"Update Database\" — first download is ~250 MB", "Натисни «Оновити бази» — перший раз завантажиться ~250 МБ");
            A("tray.appUpdateInstalling", "Updating ClamAV UI to {0} — the app will restart in a few seconds…", "Оновлюю ClamAV UI до {0} — програма перезапуститься за кілька секунд…");
            A("stats.neverScanned", "never", "ще не було");
            A("sys.labels", "ClamAV:\r\nDB date:\r\nLast scan:\r\nScans:\r\nFiles scanned:\r\nThreats:\r\nQuarantined:", "ClamAV:\r\nБази від:\r\nОстанній скан:\r\nПеревірок:\r\nФайлів проскановано:\r\nЗагроз:\r\nУ карантині:");
        }
    }

    // Rounded button with hover/pressed states instead of the system Button.
    // Implements IButtonControl so it can act as AcceptButton/CancelButton.
    delegate void IconDraw(Graphics g, RectangleF r, Color c);

    // Small vector glyphs drawn with GDI+ strokes (same technique as ShieldIndicator/
    // NavIcon) so buttons can carry an icon without shipping any image assets.
    static class Ico
    {
        static Pen P(Color c, RectangleF r, float thicknessFactor)
        {
            var pen = new Pen(c, Math.Max(1.3f, Math.Min(r.Width, r.Height) * thicknessFactor));
            pen.StartCap = pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        // Concentric sweep — quick/full scan
        public static void Radar(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = Math.Min(r.Width, r.Height) / 2f;
            using (var pen = P(c, r, 0.11f))
            {
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                g.DrawEllipse(pen, cx - rad * 0.58f, cy - rad * 0.58f, rad * 1.16f, rad * 1.16f);
                g.DrawLine(pen, cx, cy, cx + rad * 0.82f, cy - rad * 0.48f);
            }
            using (var b = new SolidBrush(c))
                g.FillEllipse(b, cx - rad * 0.13f, cy - rad * 0.13f, rad * 0.26f, rad * 0.26f);
        }

        // Document/page — scan a single file
        public static void FileIcon(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height, fold = w * 0.3f;
            var p = new GraphicsPath();
            p.AddLine(r.X + w * 0.2f, r.Y, r.X + w - fold, r.Y);
            p.AddLine(r.X + w - fold, r.Y, r.X + w * 0.82f, r.Y + fold * 0.75f);
            p.AddLine(r.X + w * 0.82f, r.Y + fold * 0.75f, r.X + w * 0.82f, r.Y + h);
            p.AddLine(r.X + w * 0.82f, r.Y + h, r.X + w * 0.2f, r.Y + h);
            p.CloseFigure();
            using (var pen = P(c, r, 0.1f)) g.DrawPath(pen, p);
        }

        // Folder — scan a folder / pick a directory
        public static void FolderIcon(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            var p = new GraphicsPath();
            p.AddLine(r.X, r.Y + h * 0.16f, r.X + w * 0.4f, r.Y + h * 0.16f);
            p.AddLine(r.X + w * 0.4f, r.Y + h * 0.16f, r.X + w * 0.5f, r.Y + h * 0.32f);
            p.AddLine(r.X + w * 0.5f, r.Y + h * 0.32f, r.X + w, r.Y + h * 0.32f);
            p.AddLine(r.X + w, r.Y + h * 0.32f, r.X + w, r.Y + h * 0.92f);
            p.AddLine(r.X + w, r.Y + h * 0.92f, r.X, r.Y + h * 0.92f);
            p.CloseFigure();
            using (var pen = P(c, r, 0.11f)) g.DrawPath(pen, p);
        }

        // Stacked drives — full-PC / system scan
        public static void Stack(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height, bh = h * 0.24f, gap = h * 0.12f;
            using (var pen = P(c, r, 0.09f))
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Y + i * (bh + gap);
                    using (var path = Theme.Round(new RectangleF(r.X, y, w, bh), bh * 0.28f))
                        g.DrawPath(pen, path);
                }
            }
            using (var b = new SolidBrush(c))
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Y + i * (bh + gap);
                    g.FillEllipse(b, r.X + w * 0.1f, y + bh / 2f - w * 0.045f, w * 0.09f, w * 0.09f);
                }
        }

        // Circular refresh arrow — update database
        public static void Refresh(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = Math.Min(r.Width, r.Height) / 2f * 0.82f;
            using (var pen = P(c, r, 0.12f))
                g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, -30, 300);
            using (var b = new SolidBrush(c))
            {
                float ax = cx + rad * (float)Math.Cos(-30 * Math.PI / 180), ay = cy + rad * (float)Math.Sin(-30 * Math.PI / 180);
                var tri = new PointF[] {
                    new PointF(ax, ay - rad * 0.42f), new PointF(ax + rad * 0.48f, ay), new PointF(ax - rad * 0.05f, ay + rad * 0.3f)
                };
                g.FillPolygon(b, tri);
            }
        }

        // Outline shield — quarantine
        public static void ShieldIcon(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            var p = new GraphicsPath();
            p.AddBezier(r.X + w * .50f, r.Y, r.X + w * .70f, r.Y + h * .08f, r.X + w * .85f, r.Y + h * .10f, r.X + w * .95f, r.Y + h * .10f);
            p.AddLine(r.X + w * .95f, r.Y + h * .10f, r.X + w * .95f, r.Y + h * .48f);
            p.AddBezier(r.X + w * .95f, r.Y + h * .48f, r.X + w * .95f, r.Y + h * .74f, r.X + w * .70f, r.Y + h * .90f, r.X + w * .50f, r.Y + h);
            p.AddBezier(r.X + w * .50f, r.Y + h, r.X + w * .30f, r.Y + h * .90f, r.X + w * .05f, r.Y + h * .74f, r.X + w * .05f, r.Y + h * .48f);
            p.AddLine(r.X + w * .05f, r.Y + h * .48f, r.X + w * .05f, r.Y + h * .10f);
            p.AddBezier(r.X + w * .05f, r.Y + h * .10f, r.X + w * .15f, r.Y + h * .10f, r.X + w * .30f, r.Y + h * .08f, r.X + w * .50f, r.Y);
            p.CloseFigure();
            using (var pen = P(c, r, 0.09f)) g.DrawPath(pen, p);
        }

        // No-entry circle — exclusions
        public static void Ban(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = Math.Min(r.Width, r.Height) / 2f * 0.86f;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                float d = rad * 0.68f;
                g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
            }
        }

        // Lines on a page — log file
        public static void LogIcon(Graphics g, RectangleF r, Color c)
        {
            using (var pen = P(c, r, 0.09f))
            using (var path = Theme.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), r.Width * 0.14f))
                g.DrawPath(pen, path);
            using (var pen = P(c, r, 0.08f))
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Y + r.Height * (0.3f + i * 0.22f);
                    g.DrawLine(pen, r.X + r.Width * 0.2f, y, r.X + r.Width * 0.8f, y);
                }
        }

        // Filled square — stop a running scan
        public static void StopIcon(Graphics g, RectangleF r, Color c)
        {
            float m = Math.Min(r.Width, r.Height) * 0.2f;
            using (var b = new SolidBrush(c))
            using (var path = Theme.Round(new RectangleF(r.X + m, r.Y + m, r.Width - m * 2, r.Height - m * 2), 2))
                g.FillPath(b, path);
        }

        // Trash can — permanent delete
        public static void Trash(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawLine(pen, r.X + w * 0.18f, r.Y + h * 0.28f, r.X + w * 0.82f, r.Y + h * 0.28f);
                g.DrawLine(pen, r.X + w * 0.36f, r.Y + h * 0.28f, r.X + w * 0.4f, r.Y + h * 0.12f);
                g.DrawLine(pen, r.X + w * 0.4f, r.Y + h * 0.12f, r.X + w * 0.6f, r.Y + h * 0.12f);
                g.DrawLine(pen, r.X + w * 0.6f, r.Y + h * 0.12f, r.X + w * 0.64f, r.Y + h * 0.28f);
                using (var path = Theme.Round(new RectangleF(r.X + w * 0.24f, r.Y + h * 0.28f, w * 0.52f, h * 0.62f), w * 0.06f))
                    g.DrawPath(pen, path);
            }
        }

        // Curved undo arrow — restore from quarantine
        public static void Restore(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = Math.Min(r.Width, r.Height) / 2f * 0.82f;
            using (var pen = P(c, r, 0.11f))
            {
                g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, 130, 260);
                double ang = 130 * Math.PI / 180;
                float ax = cx + rad * (float)Math.Cos(ang), ay = cy + rad * (float)Math.Sin(ang);
                g.DrawLine(pen, ax, ay, ax + rad * 0.5f, ay - rad * 0.1f);
                g.DrawLine(pen, ax, ay, ax + rad * 0.15f, ay + rad * 0.45f);
            }
        }

        // Downward arrow into a tray — install
        public static void Download(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height, cx = r.X + w / 2f;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawLine(pen, cx, r.Y, cx, r.Y + h * 0.62f);
                g.DrawLine(pen, cx, r.Y + h * 0.62f, cx - w * 0.22f, r.Y + h * 0.4f);
                g.DrawLine(pen, cx, r.Y + h * 0.62f, cx + w * 0.22f, r.Y + h * 0.4f);
                g.DrawLine(pen, r.X, r.Y + h * 0.86f, r.X + w, r.Y + h * 0.86f);
            }
        }

        // X mark — close/cancel
        public static void Close(Graphics g, RectangleF r, Color c)
        {
            using (var pen = P(c, r, 0.12f))
            {
                float m = Math.Min(r.Width, r.Height) * 0.2f;
                g.DrawLine(pen, r.X + m, r.Y + m, r.X + r.Width - m, r.Y + r.Height - m);
                g.DrawLine(pen, r.X + r.Width - m, r.Y + m, r.X + m, r.Y + r.Height - m);
            }
        }

        // Checkmark — confirm/OK
        public static void Check(Graphics g, RectangleF r, Color c)
        {
            using (var pen = P(c, r, 0.13f))
            {
                g.DrawLine(pen, r.X + r.Width * 0.16f, r.Y + r.Height * 0.52f, r.X + r.Width * 0.42f, r.Y + r.Height * 0.78f);
                g.DrawLine(pen, r.X + r.Width * 0.42f, r.Y + r.Height * 0.78f, r.X + r.Width * 0.86f, r.Y + r.Height * 0.24f);
            }
        }

        // Small "+" badge over the bottom-right corner of a base glyph
        static void PlusBadge(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height) * 0.4f;
            var br = new RectangleF(r.Right - s, r.Bottom - s, s, s);
            using (var pen = P(c, br, 0.16f))
            {
                float cx = br.X + br.Width / 2f, cy = br.Y + br.Height / 2f, h = br.Width * 0.32f;
                g.DrawLine(pen, cx - h, cy, cx + h, cy);
                g.DrawLine(pen, cx, cy - h, cx, cy + h);
            }
        }

        public static void FilePlus(Graphics g, RectangleF r, Color c) { FileIcon(g, r, c); PlusBadge(g, r, c); }
        public static void FolderPlus(Graphics g, RectangleF r, Color c) { FolderIcon(g, r, c); PlusBadge(g, r, c); }

        // Magnifying glass — scanner nav tab
        public static void Search(Graphics g, RectangleF r, Color c)
        {
            float rad = Math.Min(r.Width, r.Height) * 0.32f;
            float cx = r.X + r.Width * 0.42f, cy = r.Y + r.Height * 0.42f;
            using (var pen = P(c, r, 0.13f))
            {
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                float hx = cx + rad * 0.72f, hy = cy + rad * 0.72f;
                g.DrawLine(pen, hx, hy, r.X + r.Width * 0.92f, r.Y + r.Height * 0.92f);
            }
        }

        // Gear — settings nav tab
        public static void Gear(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float rOuter = Math.Min(r.Width, r.Height) * 0.46f, rInner = rOuter * 0.55f;
            using (var pen = P(c, r, 0.11f))
            {
                g.DrawEllipse(pen, cx - rOuter * 0.62f, cy - rOuter * 0.62f, rOuter * 1.24f, rOuter * 1.24f);
                g.DrawEllipse(pen, cx - rInner * 0.5f, cy - rInner * 0.5f, rInner, rInner);
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI / 4 * i + Math.PI / 8;
                    float x1 = cx + (float)Math.Cos(a) * rOuter * 0.62f, y1 = cy + (float)Math.Sin(a) * rOuter * 0.62f;
                    float x2 = cx + (float)Math.Cos(a) * rOuter, y2 = cy + (float)Math.Sin(a) * rOuter;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
        }

        // Open padlock — restoring a folder permission
        public static void Unlock(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            var body = new RectangleF(r.X + w * 0.12f, r.Y + h * 0.46f, w * 0.76f, h * 0.46f);
            using (var pen = P(c, r, 0.12f))
            {
                var shackle = new RectangleF(r.X + w * 0.16f, r.Y, w * 0.5f, h * 0.5f);
                g.DrawArc(pen, shackle, 180, 195);
                g.DrawLine(pen, shackle.X, shackle.Y + shackle.Height / 2f, shackle.X, body.Y + body.Height * 0.2f);
            }
            using (var b = new SolidBrush(c))
            using (var path = Theme.Round(body, w * 0.1f))
                g.FillPath(b, path);
        }
    }

    class ModernButton : Control, IButtonControl
    {
        public Color Back, Hover, TextColor;
        public IconDraw Icon;   // optional glyph; null = text-only button
        public bool CardStyle;  // icon centered above the text, for the big dashboard actions
        bool over, down;
        DialogResult dialogResult = DialogResult.None;

        public ModernButton(string text, Color back, Color hover, Color fore)
        {
            Text = text;
            Back = back; Hover = hover; TextColor = fore;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 34;
            Width = 150;
            Font = new Font("Segoe UI Semibold", 9f);
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 4, 8, 4);
            MouseEnter += delegate { over = true; Invalidate(); };
            MouseLeave += delegate { over = false; down = false; Invalidate(); };
            MouseDown += delegate { down = true; Invalidate(); };
            MouseUp += delegate { down = false; Invalidate(); };
        }

        public DialogResult DialogResult
        {
            get { return dialogResult; }
            set { dialogResult = value; }
        }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            var f = FindForm();
            if (dialogResult != DialogResult.None && f != null && f.Modal) f.DialogResult = dialogResult;
            base.OnClick(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor); // background behind the rounded corners (card color when on a card)
            Color c = !Enabled ? Theme.Disabled : (down ? Back : (over ? Hover : Back));
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), CardStyle ? 12 : 8))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);

            Color fg = Enabled ? TextColor : Theme.Muted;
            if (Icon == null)
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            if (CardStyle)
            {
                float iconSize = Math.Min(Width * 0.32f, 34f);
                var iconRect = new RectangleF((Width - iconSize) / 2f, 16, iconSize, iconSize);
                Icon(g, iconRect, fg);
                var textRect = new Rectangle(4, (int)(iconRect.Bottom + 8), Width - 8, Height - (int)(iconRect.Bottom + 8) - 6);
                TextRenderer.DrawText(g, Text, Font, textRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            }
            else
            {
                const int iconBox = 16, gap = 7;
                Size textSize = TextRenderer.MeasureText(Text, Font);
                int totalW = iconBox + gap + textSize.Width;
                int startX = Math.Max(6, (Width - totalW) / 2);
                var iconRect = new RectangleF(startX, (Height - iconBox) / 2f, iconBox, iconBox);
                Icon(g, iconRect, fg);
                var textRect = new Rectangle(startX + iconBox + gap, 0, Width - (startX + iconBox + gap) - 4, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Animated toggle switch, used instead of the system CheckBox
    class Toggle : Control
    {
        public event EventHandler CheckedChanged;
        bool isOn;
        float knob; // 0..1 — animated knob position
        readonly Timer anim = new Timer();

        public Toggle(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 26;
            Cursor = Cursors.Hand;
            Text = text;
            anim.Interval = 15;
            anim.Tick += delegate
            {
                float target = isOn ? 1f : 0f;
                knob += (target - knob) * 0.35f;
                if (Math.Abs(target - knob) < 0.03f) { knob = target; anim.Stop(); }
                Invalidate();
            };
            Click += delegate { Checked = !Checked; };
        }

        public bool Checked
        {
            get { return isOn; }
            set
            {
                if (isOn == value) return;
                isOn = value;
                if (IsHandleCreated && Visible) anim.Start();
                else { knob = isOn ? 1f : 0f; Invalidate(); }
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        void FitWidth()
        {
            Width = 64 + TextRenderer.MeasureText(Text, Font).Width;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
        // the form's font arrives after the constructor runs — re-measure width then
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); FitWidth(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int tw = 40, th = 20; // track
            int ty = (Height - th) / 2;
            Color track = isOn ? Theme.Accent : Color.FromArgb(75, 80, 92);
            using (var path = Theme.Round(new RectangleF(0, ty, tw, th), th / 2f))
            using (var b = new SolidBrush(track))
                g.FillPath(b, path);
            float kx = 3 + knob * (tw - th); // ranges 3..23
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, ty + 3, th - 6, th - 6);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tw + 10, 0, Width - tw - 10, Height),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // Dark colors for the tray context menu (the system default is stark white)
    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.LogBg; } }
        public override Color ImageMarginGradientBegin { get { return Theme.LogBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.LogBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.LogBg; } }
        public override Color MenuItemSelected { get { return Theme.Card; } }
        public override Color MenuItemBorder { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.CardLine; } }
    }

    enum ShieldState { Ok, Warning, Danger, Busy }

    // Large filled shield icon, drawn with anti-aliasing
    class ShieldIndicator : Control
    {
        public ShieldState State = ShieldState.Warning;

        public ShieldIndicator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(96, 96);
        }

        public void SetState(ShieldState s) { State = s; Invalidate(); }

        static GraphicsPath ShieldPath(float w, float h)
        {
            var p = new GraphicsPath();
            p.AddBezier(w * .50f, h * .04f, w * .68f, h * .10f, w * .80f, h * .12f, w * .88f, h * .12f);
            p.AddLine(w * .88f, h * .12f, w * .88f, h * .48f);
            p.AddBezier(w * .88f, h * .48f, w * .88f, h * .72f, w * .68f, h * .88f, w * .50f, h * .96f);
            p.AddBezier(w * .50f, h * .96f, w * .32f, h * .88f, w * .12f, h * .72f, w * .12f, h * .48f);
            p.AddLine(w * .12f, h * .48f, w * .12f, h * .12f);
            p.AddBezier(w * .12f, h * .12f, w * .20f, h * .12f, w * .32f, h * .10f, w * .50f, h * .04f);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color c;
            switch (State)
            {
                case ShieldState.Ok: c = Theme.Good; break;
                case ShieldState.Danger: c = Theme.Danger; break;
                case ShieldState.Busy: c = Theme.Accent; break;
                default: c = Theme.Warn; break;
            }

            float w = Width, h = Height;
            using (var path = ShieldPath(w, h))
            {
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(70, Color.Black), 2f)) g.DrawPath(pen, path);
            }

            using (var pen = new Pen(Color.White, w * 0.08f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (State == ShieldState.Ok)
                {
                    g.DrawLine(pen, w * .32f, h * .50f, w * .45f, h * .63f);
                    g.DrawLine(pen, w * .45f, h * .63f, w * .68f, h * .36f);
                }
                else if (State == ShieldState.Busy)
                {
                    using (var b = new SolidBrush(Color.White))
                    {
                        float r = w * 0.045f;
                        g.FillEllipse(b, w * .32f - r, h * .48f - r, r * 2, r * 2);
                        g.FillEllipse(b, w * .50f - r, h * .48f - r, r * 2, r * 2);
                        g.FillEllipse(b, w * .68f - r, h * .48f - r, r * 2, r * 2);
                    }
                }
                else
                {
                    g.DrawLine(pen, w * .50f, h * .28f, w * .50f, h * .55f);
                    using (var b = new SolidBrush(Color.White))
                    {
                        float r = w * 0.05f;
                        g.FillEllipse(b, w * .50f - r, h * .66f - r, r * 2, r * 2);
                    }
                }
            }
        }
    }

    // Sidebar navigation icon: shield / crosshair / gear
    // Horizontal top-bar nav tab: icon + label, active state = accent underline.
    // Deliberately not a left icon rail (that shape reads as a clone of the reference
    // Synology-style AV UIs this project used to imitate) — top tabs with text instead.
    class NavTab : Control
    {
        public IconDraw Icon;
        public bool Active;
        bool hover;

        public NavTab(string text, IconDraw icon)
        {
            Text = text;
            Icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI Semibold", 9.5f);
            Height = 44;
            Cursor = Cursors.Hand;
            Margin = new Padding(2, 0, 2, 0);
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }

        public void SetActive(bool a) { Active = a; Invalidate(); }

        void FitWidth() { Width = 46 + TextRenderer.MeasureText(Text, Font).Width; }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            if (hover && !Active) // soft hover highlight, no highlight once active (underline reads active)
                using (var path = Theme.Round(new RectangleF(2, 5, Width - 4, Height - 12), 8))
                using (var b = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                    g.FillPath(b, path);
            Color c = Active ? Theme.Text : (hover ? Theme.Text : Theme.Muted);
            var iconRect = new RectangleF(14, (Height - 8 - 18) / 2f, 18, 18);
            if (Icon != null) Icon(g, iconRect, c);
            var textRect = new Rectangle((int)iconRect.Right + 8, 0, Width - (int)iconRect.Right - 16, Height - 8);
            TextRenderer.DrawText(g, Text, Font, textRect, c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (Active)
                using (var b = new SolidBrush(Theme.Accent))
                using (var path = Theme.Round(new RectangleF(10, Height - 3, Width - 20, 3), 1.5f))
                    g.FillPath(b, path);
        }
    }

    // Wide, short status banner (protection state) — deliberately NOT another square
    // card in a 3-across grid, which is the part of the old layout that read as a
    // direct copy of the reference AV UIs. A colored left bar reflects the state.
    class StatusBanner : Panel
    {
        public Color AccentColor = Theme.Good;

        public StatusBanner()
        {
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.Radius))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.CardLine)) g.DrawPath(pen, path);
            }
            using (var b = new SolidBrush(AccentColor))
            using (var path = Theme.Round(new RectangleF(0, Height * 0.22f, 4, Height * 0.56f), 2))
                g.FillPath(b, path);
        }
    }

    // Card with rounded corners, a thin border, and an UPPERCASE header
    class CardPanel : Panel
    {
        public string HeaderText = "";

        public CardPanel(string header)
        {
            HeaderText = header;
            BackColor = Theme.Bg; // corners show the page background through them
            Padding = new Padding(16, 44, 16, 14);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.Radius))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.CardLine)) g.DrawPath(pen, path);
            }
            using (var f = new Font("Segoe UI Semibold", 9.5f))
            using (var b = new SolidBrush(Theme.Muted))
                g.DrawString(HeaderText.ToUpperInvariant(), f, b, 16, 15);
        }
    }

    // Thin progress bar: marquee (while the total is unknown) or a real percentage
    class SlimMarquee : Control
    {
        readonly Timer timer = new Timer();
        float pos;
        double fraction = -1; // -1 = indeterminate mode (marquee)

        public SlimMarquee()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 3;
            timer.Interval = 16;
            timer.Tick += delegate { pos = (pos + 0.012f) % 1.3f; Invalidate(); };
            Visible = false;
        }

        public void Start() { fraction = -1; Visible = true; timer.Start(); }
        public void Stop() { timer.Stop(); Visible = false; fraction = -1; }

        public void SetFraction(double f)
        {
            fraction = Math.Max(0, Math.Min(1, f));
            timer.Stop();
            if (!Visible) Visible = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            using (var b = new SolidBrush(Theme.Accent))
            {
                if (fraction >= 0)
                {
                    e.Graphics.FillRectangle(b, 0, 0, (int)(Width * fraction), Height);
                }
                else
                {
                    int w = (int)(Width * 0.3f);
                    int x = (int)(Width * pos) - w;
                    e.Graphics.FillRectangle(b, x, 0, w, Height);
                }
            }
        }
    }

    public class MainForm : Form
    {
        const string AppName = "ClamAV UI";
        const string AppVersion = "0.0.1";
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
        readonly HashSet<string> pendingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        ModernButton btnScanFile, btnScanFolder, btnScanAll, btnStop, btnUpdate, btnWatchDirs, btnQuarantine, btnExclusions, btnScanLog;
        ModernButton dashQuick, dashScanFile, dashScanFolder, dashScanAll, btnQuickScan, btnInstall, btnLangEn, btnLangUk, btnFixWinTemp;
        readonly List<ModernButton> scanButtons = new List<ModernButton>(); // all buttons that start a scan (both pages)
        RichTextBox log;
        Label statusLabel, heroTitle, heroSub, statsLabel, sysNames, quarCountLabel, langLabel, lastActivityLabel;
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

        // ---------- Install to Program Files ----------

        static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV UI"); }
        }

        static bool IsInstalled
        {
            get { return Application.ExecutablePath.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase); }
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
        static bool CanWatchDirectory(string dir)
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

        public MainForm(bool startInTray)
        {
            Text = AppName;
            Icon = AppIcon;
            MinimumSize = new Size(860, 580);
            Size = new Size(920, 640);
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
            string[] navLabelKeys = { "nav.dashboard", "nav.scanner", "nav.settings" };
            IconDraw[] navIcons = { Ico.ShieldIcon, Ico.Search, Ico.Gear };
            navs = new NavTab[3];
            for (int i = 0; i < 3; i++)
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

            pages = new Panel[3];
            pages[0] = BuildDashboardPage();
            pages[1] = BuildScannerPage();
            pages[2] = BuildSettingsPage();
            foreach (var p in pages) { p.Visible = false; content.Controls.Add(p); }

            Controls.Add(content);
            Controls.Add(progress);
            Controls.Add(statusBar);
            Controls.Add(title);

            // Debounce timer: wait for a file to finish being written, then scan as a batch
            debounceTimer = new Timer();
            debounceTimer.Interval = 3000;
            debounceTimer.Tick += OnDebounceTick;

            // Auto-update: first check 15s after startup, then the timer ticks hourly,
            // but the actual version check runs no more than once a day
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

            // Scan action cards — always visible on the dashboard, icon + label like a launcher
            var scanBar = new FlowLayoutPanel();
            scanBar.Dock = DockStyle.Top;
            scanBar.Height = 112;
            scanBar.Padding = new Padding(6, 4, 6, 2);
            scanBar.BackColor = Theme.Bg;
            dashQuick = MakeCardButton(Lang.T("btn.quickScan"), Theme.Accent, Theme.AccentHot, Theme.Text, Ico.Radar);
            dashQuick.Click += delegate { RunQuickScan(); };
            dashScanFile = MakeCardButton(Lang.T("btn.scanFileDash"), Theme.Card, Theme.CardLine, Theme.Text, Ico.FileIcon);
            dashScanFile.Click += delegate { PickAndScan(false); };
            dashScanFolder = MakeCardButton(Lang.T("btn.scanFolderDash"), Theme.Card, Theme.CardLine, Theme.Text, Ico.FolderIcon);
            dashScanFolder.Click += delegate { PickAndScan(true); };
            dashScanAll = MakeCardButton(Lang.T("btn.scanAll"), Theme.Card, Theme.CardLine, Theme.Text, Ico.Stack);
            dashScanAll.Click += delegate { RunFullScan(); };
            scanBar.Controls.AddRange(new Control[] { dashQuick, dashScanFile, dashScanFolder, dashScanAll });
            scanButtons.Add(dashQuick); scanButtons.Add(dashScanFile);
            scanButtons.Add(dashScanFolder); scanButtons.Add(dashScanAll);

            // Status banner: wide strip instead of a square card, shield + headline
            // + subtitle laid out horizontally, with a state-colored accent bar.
            statusBanner = new StatusBanner();
            statusBanner.Dock = DockStyle.Top;
            statusBanner.Height = 78;
            statusBanner.Margin = new Padding(6, 4, 6, 4);
            shield = new ShieldIndicator();
            shield.Size = new Size(48, 48);
            shield.Location = new Point(20, 15);
            shield.BackColor = Theme.Card;
            heroTitle = new Label();
            heroTitle.AutoSize = true;
            heroTitle.Location = new Point(84, 15);
            heroTitle.Font = new Font("Segoe UI Semibold", 12.5f);
            heroTitle.ForeColor = Theme.Text;
            heroTitle.BackColor = Theme.Card;
            heroSub = new Label();
            heroSub.AutoSize = true;
            heroSub.Location = new Point(85, 42);
            heroSub.Font = Font;
            heroSub.ForeColor = Theme.Muted;
            heroSub.BackColor = Theme.Card;
            statusBanner.Controls.Add(shield);
            statusBanner.Controls.Add(heroTitle);
            statusBanner.Controls.Add(heroSub);

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
            activityRow.Padding = new Padding(20, 0, 8, 0);
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

            page.Controls.Add(activityRow);
            page.Controls.Add(cardSystem);
            page.Controls.Add(statusBanner);
            page.Controls.Add(scanBar);
            return page;
        }

        Panel BuildScannerPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 1;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));

            // Quarantine card
            cardQuar = new CardPanel(Lang.T("card.quarantine"));
            cardQuar.Dock = DockStyle.Fill;
            cardQuar.Margin = new Padding(6);
            quarCountLabel = new Label();
            quarCountLabel.Dock = DockStyle.Fill;
            quarCountLabel.TextAlign = ContentAlignment.MiddleCenter;
            quarCountLabel.Font = new Font("Segoe UI Semibold", 34f);
            quarCountLabel.ForeColor = Theme.Text;
            quarCountLabel.BackColor = Theme.Card;
            quarCountLabel.Text = "0";
            btnQuarantine = MakeLightButton(Lang.T("btn.openQuarantine"), Ico.ShieldIcon);
            btnQuarantine.BackColor = Theme.Card;
            btnQuarantine.Dock = DockStyle.Bottom;
            btnQuarantine.Click += delegate { ShowQuarantine(); };
            btnExclusions = MakeLightButton(Lang.T("btn.exclusions"), Ico.Ban);
            btnExclusions.BackColor = Theme.Card;
            btnExclusions.Dock = DockStyle.Bottom;
            btnExclusions.Click += delegate { EditExclusions(); };
            cardQuar.Controls.Add(quarCountLabel);
            cardQuar.Controls.Add(btnQuarantine);
            cardQuar.Controls.Add(btnExclusions);

            // Scanning card
            cardScan = new CardPanel(Lang.T("card.scanning"));
            cardScan.Dock = DockStyle.Fill;
            cardScan.Margin = new Padding(6);

            var buttonsRow = new FlowLayoutPanel();
            buttonsRow.Dock = DockStyle.Top;
            buttonsRow.Height = 42;
            buttonsRow.BackColor = Theme.Card;

            btnQuickScan = MakeLightButton(Lang.T("btn.quick"), Ico.Radar);
            btnQuickScan.Width = 105;
            btnScanFile = MakeLightButton(Lang.T("btn.file"), Ico.FileIcon);
            btnScanFile.Width = 90;
            btnScanFolder = MakeLightButton(Lang.T("btn.folder"), Ico.FolderIcon);
            btnScanFolder.Width = 100;
            btnScanAll = MakeLightButton(Lang.T("btn.scanAll"), Ico.Stack);
            btnScanAll.Width = 105;
            btnStop = MakeButton(Lang.T("btn.stop"), 110, Theme.Danger, Theme.DangerHot, Ico.StopIcon);
            btnStop.Enabled = false;

            btnQuickScan.Click += delegate { RunQuickScan(); };
            btnScanFile.Click += delegate { PickAndScan(false); };
            btnScanFolder.Click += delegate { PickAndScan(true); };
            btnScanAll.Click += delegate { RunFullScan(); };
            btnStop.Click += delegate { StopCurrent(); };
            scanButtons.Add(btnQuickScan); scanButtons.Add(btnScanFile);
            scanButtons.Add(btnScanFolder); scanButtons.Add(btnScanAll);

            buttonsRow.Controls.AddRange(new Control[] { btnQuickScan, btnScanFile, btnScanFolder, btnScanAll, btnStop });

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

            grid.Controls.Add(cardQuar, 0, 0);
            grid.Controls.Add(cardScan, 1, 0);

            page.Controls.Add(grid);
            return page;
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
            navs[1].Text = Lang.T("nav.scanner");
            navs[2].Text = Lang.T("nav.settings");

            cardSystem.HeaderText = Lang.T("card.system"); cardSystem.Invalidate();
            cardQuar.HeaderText = Lang.T("card.quarantine"); cardQuar.Invalidate();
            cardScan.HeaderText = Lang.T("card.scanning"); cardScan.Invalidate();
            cardSettingsPanel.HeaderText = Lang.T("card.settings"); cardSettingsPanel.Invalidate();

            dashQuick.Text = Lang.T("btn.quickScan");
            dashScanFile.Text = Lang.T("btn.scanFileDash");
            dashScanFolder.Text = Lang.T("btn.scanFolderDash");
            dashScanAll.Text = Lang.T("btn.scanAll");
            btnUpdate.Text = Lang.T("btn.updateDb");
            btnScanLog.Text = Lang.T("btn.openLog");
            btnQuarantine.Text = Lang.T("btn.openQuarantine");
            btnExclusions.Text = Lang.T("btn.exclusions");
            btnQuickScan.Text = Lang.T("btn.quick");
            btnScanFile.Text = Lang.T("btn.file");
            btnScanFolder.Text = Lang.T("btn.folder");
            btnScanAll.Text = Lang.T("btn.scanAll");
            btnStop.Text = Lang.T("btn.stop");
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

        // ---------- Locating ClamAV ----------

        void LocateClamAV()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "clamav"),
                baseDir,
                Path.Combine(baseDir, @"..\clamav"),
                @"C:\Program Files\ClamAV"
            };
            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(Path.Combine(full, "clamscan.exe")))
                {
                    clamDir = full;
                    break;
                }
            }
            if (clamDir == null)
            {
                // clean up remnants of an interrupted extraction (keep the zip itself for recovery)
                try
                {
                    string tmp = Path.Combine(baseDir, "clamav-tmp");
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                }
                catch { }
                AppendLog(Lang.T("log.clamscanNotFound"), Theme.Warn);
                SetScanEnabled(false);
                return; // btnUpdate stays enabled: it will trigger the install
            }
            dbDir = Path.Combine(clamDir, "database");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            EnsureFreshclamConf();
            FetchClamVersion();
            AppendLog(string.Format(Lang.T("log.clamAVPath"), clamDir), Theme.Muted);
        }

        void FetchClamVersion()
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(clamDir, "clamscan.exe"), "--version");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (var p = Process.Start(psi))
                {
                    string line = p.StandardOutput.ReadLine(); // "ClamAV 1.5.3/27710/..."
                    p.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(line))
                        clamVersion = line.Replace("ClamAV ", "").Split('/')[0];
                }
            }
            catch { }
        }

        void EnsureFreshclamConf()
        {
            string conf = Path.Combine(clamDir, "freshclam.conf");
            if (File.Exists(conf)) return;
            File.WriteAllText(conf,
                "DatabaseDirectory " + dbDir + "\r\n" +
                "DatabaseMirror database.clamav.net\r\n",
                new UTF8Encoding(false));
        }

        bool DbExists()
        {
            if (dbDir == null || !Directory.Exists(dbDir)) return false;
            return Directory.GetFiles(dbDir, "*.cvd").Length > 0
                || Directory.GetFiles(dbDir, "*.cld").Length > 0;
        }

        string DbDateString()
        {
            if (!DbExists()) return "—";
            DateTime newest = DateTime.MinValue;
            foreach (string f in Directory.GetFiles(dbDir))
            {
                DateTime t = File.GetLastWriteTime(f);
                if (t > newest) newest = t;
            }
            return newest.ToString("dd.MM.yyyy HH:mm");
        }

        void RefreshDbStatus()
        {
            if (clamDir == null)
            {
                btnUpdate.Visible = true; // here the button triggers ClamAV installation
                SetHero(ShieldState.Danger, Lang.T("hero.clamAVNotFound"),
                    Lang.T("hero.putPortableClamAV"));
                return;
            }
            if (DbExists())
            {
                // the update button is visible only when the server actually has a newer database
                btnUpdate.Visible = updateAvailable;
                SetHero(ShieldState.Ok, Lang.T("hero.protected"), string.Format(Lang.T("hero.dbFrom"), DbDateString()));
                SetScanEnabled(!scanRunning && !updateRunning);
            }
            else
            {
                btnUpdate.Visible = true;
                SetHero(ShieldState.Warning, Lang.T("hero.dbNeeded"),
                    Lang.T("hero.pressUpdateFirstTime"));
                SetScanEnabled(false);
            }
            UpdateStatsUi();
        }

        // ---------- Settings ----------

        bool loadingSettings;

        void LoadSettings()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            settingsPath = Path.Combine(baseDir, "settings.ini");
            scanLogPath = Path.Combine(baseDir, "scans.log");
            quarDir = Path.Combine(baseDir, "quarantine");
            quarIndex = Path.Combine(quarDir, "index.txt");
            if (!Directory.Exists(quarDir)) Directory.CreateDirectory(quarDir);

            loadingSettings = true;
            bool monitor = false, quarantine = false, autoUpdate = true, riskyOnly = true, fullRisky = true;
            if (File.Exists(settingsPath))
            {
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    string t = line.Trim();
                    if (t.StartsWith("watch=", StringComparison.OrdinalIgnoreCase))
                    {
                        string d = t.Substring(6).Trim();
                        if (d.Length > 0 && !watchDirs.Contains(d)) watchDirs.Add(d);
                    }
                    else if (t.StartsWith("exclude=", StringComparison.OrdinalIgnoreCase))
                    {
                        string d = t.Substring(8).Trim();
                        if (d.Length > 0 && !exclusions.Contains(d)) exclusions.Add(d);
                    }
                    else if (t == "monitor=1") monitor = true;
                    else if (t == "quarantine=1") quarantine = true;
                    else if (t == "autoupdate=0") autoUpdate = false;
                    else if (t == "riskyonly=0") riskyOnly = false;
                    else if (t == "fullrisky=0") fullRisky = false;
                    else if (t == "autostartinit=1") autostartInitialized = true;
                    else if (t == "watchinit=1") watchInitialized = true;
                    else if (t == "watchinit=2") { watchInitialized = true; watchDefaultsV2 = true; }
                    else if (t == "watchinit=3") { watchInitialized = true; watchDefaultsV2 = true; watchDefaultsV3 = true; }
                    else if (t == "lang=uk") Lang.Current = Lang.Language.Ukrainian;
                    else if (t.StartsWith("lastscan="))
                    {
                        string v = t.Substring(9);
                        lastScanInfo = v == "ще не було" ? "" : v; // legacy sentinel from older versions
                    }
                    else if (t.StartsWith("dbcooldown="))
                    {
                        long ticks;
                        if (long.TryParse(t.Substring(11), out ticks) && ticks > 0)
                            dbCooldownUntil = new DateTime(ticks);
                    }
                    else if (t.StartsWith("lastdbcheck="))
                    {
                        long ticks;
                        if (long.TryParse(t.Substring(12), out ticks) && ticks > 0)
                            lastDbCheck = new DateTime(ticks);
                    }
                    else if (t.StartsWith("lastappcheck="))
                    {
                        long ticks;
                        if (long.TryParse(t.Substring(13), out ticks) && ticks > 0)
                            lastAppUpdateCheck = new DateTime(ticks);
                    }
                    else if (t.StartsWith("totalScans=")) long.TryParse(t.Substring(11), out totalScans);
                    else if (t.StartsWith("totalFiles=")) long.TryParse(t.Substring(11), out totalFilesScanned);
                    else if (t.StartsWith("totalFound=")) long.TryParse(t.Substring(11), out totalFound);
                    else if (t.StartsWith("totalMoved=")) long.TryParse(t.Substring(11), out totalMoved);
                }
            }
            // First run: add default folders (Downloads, Desktop, Program Files) and
            // enable monitoring right away. Done only once — if the user changes it
            // afterwards, we don't force it back on.
            if (!watchInitialized)
            {
                watchInitialized = true;
                foreach (string d in DefaultWatchDirs())
                    if (!watchDirs.Contains(d)) watchDirs.Add(d);
                monitor = true;
            }
            else if (!watchDefaultsV2)
            {
                // migrating older settings: add only the new folders (Temp, Roaming)
                // once, without restoring anything the user has since removed
                foreach (string d in TempWatchDirs())
                    if (!watchDirs.Contains(d)) watchDirs.Add(d);
            }
            if (!watchDefaultsV3)
            {
                // v2 added C:\Windows\Temp unconditionally, but a non-elevated process
                // can't watch it on a hardened system (FileSystemWatcher fails there).
                // Self-healing check instead of a blanket removal: keep/add it if it's
                // actually watchable (stock Windows default, or already fixed via
                // --install / the "Fix" button), drop it otherwise.
                string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (CanWatchDirectory(winTemp)) { if (!watchDirs.Contains(winTemp)) watchDirs.Add(winTemp); }
                else watchDirs.RemoveAll(delegate(string d) { return string.Equals(d, winTemp, StringComparison.OrdinalIgnoreCase); });
            }
            watchDefaultsV2 = true;
            watchDefaultsV3 = true;
            if (watchDirs.Count == 0)
                foreach (string d in DefaultWatchDirs())
                    if (!watchDirs.Contains(d)) watchDirs.Add(d);
            chkQuarantine.Checked = quarantine;
            chkAutoUpdate.Checked = autoUpdate;
            chkRiskyOnly.Checked = riskyOnly;
            chkFullRisky.Checked = fullRisky;
            chkMonitor.Checked = monitor; // CheckedChanged will start the watchers itself
            loadingSettings = false;
            ApplyLanguage(); // picks up the loaded language setting and re-texts the UI
            SaveSettings(); // persist the default migration
        }

        // Default monitored folders: typical infection vectors
        List<string> DefaultWatchDirs()
        {
            var list = new List<string>();
            Action<string> add = delegate(string p)
            {
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !list.Contains(p)) list.Add(p);
            };
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
            foreach (string d in TempWatchDirs()) add(d);
            return list;
        }

        // Hot spots added in defaults v2: temp folders and Roaming — the places
        // droppers get extracted to and where malware commonly persists. C:\Windows\Temp
        // is only included if it's actually watchable (see CanWatchDirectory) — on some
        // hardened machines a non-elevated process can't even list it; --install fixes
        // the ACL automatically, or the user can run the fix from Settings.
        static List<string> TempWatchDirs()
        {
            var list = new List<string>();
            Action<string> add = delegate(string p)
            {
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !list.Contains(p)) list.Add(p);
            };
            try { add(Path.GetTempPath().TrimEnd('\\')); } catch { }        // the user's %TEMP%
            add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)); // Roaming
            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (CanWatchDirectory(winTemp)) add(winTemp);
            return list;
        }

        void SaveSettings()
        {
            if (loadingSettings) return;
            var sb = new StringBuilder();
            sb.AppendLine("monitor=" + (chkMonitor.Checked ? "1" : "0"));
            sb.AppendLine("quarantine=" + (chkQuarantine.Checked ? "1" : "0"));
            sb.AppendLine("autoupdate=" + (chkAutoUpdate.Checked ? "1" : "0"));
            sb.AppendLine("riskyonly=" + (chkRiskyOnly.Checked ? "1" : "0"));
            sb.AppendLine("fullrisky=" + (chkFullRisky.Checked ? "1" : "0"));
            sb.AppendLine("autostartinit=" + (autostartInitialized ? "1" : "0"));
            sb.AppendLine("watchinit=" + (watchInitialized ? "3" : "0"));
            sb.AppendLine("lang=" + (Lang.Current == Lang.Language.Ukrainian ? "uk" : "en"));
            sb.AppendLine("lastscan=" + lastScanInfo);
            sb.AppendLine("dbcooldown=" + dbCooldownUntil.Ticks);
            sb.AppendLine("lastdbcheck=" + lastDbCheck.Ticks);
            sb.AppendLine("lastappcheck=" + lastAppUpdateCheck.Ticks);
            sb.AppendLine("totalScans=" + totalScans);
            sb.AppendLine("totalFiles=" + totalFilesScanned);
            sb.AppendLine("totalFound=" + totalFound);
            sb.AppendLine("totalMoved=" + totalMoved);
            foreach (string d in watchDirs) sb.AppendLine("watch=" + d);
            foreach (string d in exclusions) sb.AppendLine("exclude=" + d);
            try { File.WriteAllText(settingsPath, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { AppendLog("Failed to save settings: " + ex.Message + "\r\n", Theme.Danger); }
        }

        // ---------- Quarantine and statistics ----------

        int QuarantineCount()
        {
            if (quarDir == null || !Directory.Exists(quarDir)) return 0;
            int n = 0;
            foreach (string f in Directory.GetFiles(quarDir))
                if (!string.Equals(Path.GetFileName(f), "index.txt", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        void UpdateStatsUi()
        {
            int q = QuarantineCount();
            statsLabel.Text = string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}\r\n{5}\r\n{6}",
                clamVersion, DbExists() ? DbDateString() : "—",
                lastScanInfo.Length == 0 ? Lang.T("stats.neverScanned") : lastScanInfo,
                totalScans, totalFilesScanned, totalFound, q);
            quarCountLabel.Text = q.ToString();
            quarCountLabel.ForeColor = q > 0 ? Theme.Warn : Theme.Text;
        }

        // Moves a file into quarantine manually (without clamscan --move), writes the index
        bool QuarantineFile(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                string dest = Path.Combine(quarDir, name);
                int i = 1;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(quarDir,
                        Path.GetFileNameWithoutExtension(name) + "(" + i + ")" + Path.GetExtension(name));
                    i++;
                }
                File.Move(path, dest);
                File.AppendAllText(quarIndex,
                    Path.GetFileName(dest) + "|" + path + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n",
                    new UTF8Encoding(false));
                totalMoved++;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Lang.T("msg.quarantineMoveFailed"), ex.Message), Lang.T("quarantine.title"));
                return false;
            }
        }

        void AddExclusion(string path)
        {
            if (!exclusions.Contains(path)) exclusions.Add(path);
        }

        // Asks the user what to do with each detected threat
        void ShowThreatDialog()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Lang.T("threat.title");
                dlg.Size = new Size(760, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.BackColor = Theme.Bg;
                dlg.ForeColor = Theme.Text;
                dlg.Font = Font;
                Theme.DarkTitleBar(dlg);

                var list = MakeList();
                list.Columns.Add(Lang.T("col.file"), 400);
                list.Columns.Add(Lang.T("col.threat"), 280);

                foreach (string[] f in foundFiles)
                {
                    if (!File.Exists(f[0])) continue; // already moved or gone
                    var item = new ListViewItem(new string[] { f[0], f[1] });
                    item.Tag = f[0];
                    list.Items.Add(item);
                }
                if (list.Items.Count == 0) return;

                var hint = new Label();
                hint.Dock = DockStyle.Top;
                hint.Height = 30;
                hint.Padding = new Padding(10, 8, 10, 0);
                hint.ForeColor = Theme.Muted;
                hint.Text = Lang.T("threat.hint");

                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Height = 50;
                buttons.Padding = new Padding(8);
                buttons.BackColor = Theme.Bg;

                var close = MakeButton(Lang.T("btn.close"), 90, Theme.Card, Theme.Bg, Ico.Close);
                close.DialogResult = DialogResult.Cancel;
                var excl = MakeButton(Lang.T("btn.toExclusions"), 125, Theme.Card, Theme.Bg, Ico.Ban);
                var del = MakeButton(Lang.T("btn.delete"), 100, Theme.Danger, Theme.DangerHot, Ico.Trash);
                var quar = MakeButton(Lang.T("btn.toQuarantine"), 110, Theme.Accent, Theme.AccentHot, Ico.ShieldIcon);

                Func<List<ListViewItem>> picked = delegate
                {
                    var items = new List<ListViewItem>();
                    if (list.SelectedItems.Count > 0)
                        foreach (ListViewItem it in list.SelectedItems) items.Add(it);
                    else
                        foreach (ListViewItem it in list.Items) items.Add(it);
                    return items;
                };
                Action maybeClose = delegate { if (list.Items.Count == 0) dlg.Close(); };

                quar.Click += delegate
                {
                    foreach (var it in picked())
                        if (QuarantineFile((string)it.Tag)) { movedCount++; list.Items.Remove(it); }
                    SaveSettings();
                    UpdateStatsUi();
                    maybeClose();
                };
                del.Click += delegate
                {
                    var items = picked();
                    if (MessageBox.Show(dlg, string.Format(Lang.T("msg.deleteConfirm"), items.Count),
                        Lang.T("title.deletion"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    foreach (var it in items)
                    {
                        try { File.Delete((string)it.Tag); list.Items.Remove(it); }
                        catch (Exception ex) { MessageBox.Show(dlg, ex.Message, Lang.T("title.error")); }
                    }
                    maybeClose();
                };
                excl.Click += delegate
                {
                    foreach (var it in picked())
                    {
                        AddExclusion((string)it.Tag);
                        list.Items.Remove(it);
                    }
                    SaveSettings();
                    statusLabel.Text = string.Format(Lang.T("status.exclusionsCount"), exclusions.Count);
                    maybeClose();
                };

                buttons.Controls.Add(close);
                buttons.Controls.Add(excl);
                buttons.Controls.Add(del);
                buttons.Controls.Add(quar);

                dlg.Controls.Add(list);
                dlg.Controls.Add(hint);
                dlg.Controls.Add(buttons);
                dlg.CancelButton = close;
                dlg.ShowDialog(this);
            }
            UpdateStatsUi();
        }

        Dictionary<string, string[]> ReadQuarIndex()
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(quarIndex)) return map;
            foreach (string line in File.ReadAllLines(quarIndex))
            {
                string[] parts = line.Split('|');
                if (parts.Length >= 3) map[parts[0]] = parts;
            }
            return map;
        }

        void RemoveQuarIndexEntry(string fileName)
        {
            if (!File.Exists(quarIndex)) return;
            var keep = new List<string>();
            foreach (string line in File.ReadAllLines(quarIndex))
                if (!line.StartsWith(fileName + "|", StringComparison.OrdinalIgnoreCase))
                    keep.Add(line);
            File.WriteAllLines(quarIndex, keep.ToArray());
        }

        void ShowQuarantine()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Lang.T("quarantine.title");
                dlg.Size = new Size(720, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.BackColor = Theme.Bg;
                dlg.ForeColor = Theme.Text;
                dlg.Font = Font;
                Theme.DarkTitleBar(dlg);

                var list = MakeList();
                list.Columns.Add(Lang.T("col.file"), 220);
                list.Columns.Add(Lang.T("col.origin"), 280);
                list.Columns.Add(Lang.T("col.when"), 130);

                Action reload = delegate
                {
                    list.Items.Clear();
                    var map = ReadQuarIndex();
                    foreach (string f in Directory.GetFiles(quarDir))
                    {
                        string name = Path.GetFileName(f);
                        if (string.Equals(name, "index.txt", StringComparison.OrdinalIgnoreCase)) continue;
                        string origin = Lang.T("quarantine.unknownOrigin"), when = "";
                        string[] meta;
                        if (map.TryGetValue(name, out meta)) { origin = meta[1]; when = meta[2]; }
                        var item = new ListViewItem(new string[] { name, origin, when });
                        item.Tag = f;
                        list.Items.Add(item);
                    }
                    UpdateStatsUi();
                };

                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Height = 50;
                buttons.Padding = new Padding(8);
                buttons.BackColor = Theme.Bg;

                var close = MakeButton(Lang.T("btn.close"), 90, Theme.Card, Theme.Bg, Ico.Close);
                close.DialogResult = DialogResult.Cancel;
                var del = MakeButton(Lang.T("btn.deleteForever"), 170, Theme.Danger, Theme.DangerHot, Ico.Trash);
                var restore = MakeButton(Lang.T("btn.restore"), 120, Theme.Accent, Theme.AccentHot, Ico.Restore);
                var toExcl = MakeButton(Lang.T("btn.toExclusions"), 125, Theme.Card, Theme.Bg, Ico.Ban);

                // Restores a file to its original location; excludeToo also adds it to
                // exclusions so future scans leave it alone
                Action<bool> restoreSelected = delegate(bool excludeToo)
                {
                    if (list.SelectedItems.Count == 0) return;
                    if (MessageBox.Show(dlg,
                        Lang.T("msg.restoreConfirm"),
                        Lang.T("quarantine.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    foreach (ListViewItem it in list.SelectedItems)
                    {
                        string path = (string)it.Tag;
                        string origin = it.SubItems[1].Text;
                        if (origin == Lang.T("quarantine.unknownOrigin"))
                        {
                            MessageBox.Show(dlg, string.Format(Lang.T("msg.unknownOriginPath"), it.Text), Lang.T("quarantine.title"));
                            continue;
                        }
                        try
                        {
                            if (File.Exists(origin))
                            {
                                MessageBox.Show(dlg, string.Format(Lang.T("msg.fileExists"), origin), Lang.T("quarantine.title"));
                                continue;
                            }
                            File.Move(path, origin);
                            RemoveQuarIndexEntry(Path.GetFileName(path));
                            if (excludeToo) AddExclusion(origin);
                        }
                        catch (Exception ex) { MessageBox.Show(dlg, ex.Message, Lang.T("title.error")); }
                    }
                    if (excludeToo) SaveSettings();
                    reload();
                };

                restore.Click += delegate { restoreSelected(false); };
                toExcl.Click += delegate { restoreSelected(true); };

                del.Click += delegate
                {
                    if (list.SelectedItems.Count == 0) return;
                    if (MessageBox.Show(dlg,
                        string.Format(Lang.T("msg.deleteConfirm"), list.SelectedItems.Count),
                        Lang.T("quarantine.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    foreach (ListViewItem it in list.SelectedItems)
                    {
                        string path = (string)it.Tag;
                        try { File.Delete(path); RemoveQuarIndexEntry(Path.GetFileName(path)); }
                        catch (Exception ex) { MessageBox.Show(dlg, ex.Message, Lang.T("title.error")); }
                    }
                    reload();
                };

                var openDir = MakeButton(Lang.T("btn.openFolder"), 140, Theme.Card, Theme.Bg, Ico.FolderIcon);
                openDir.Click += delegate { Process.Start("explorer.exe", "\"" + quarDir + "\""); };

                buttons.Controls.Add(close);
                buttons.Controls.Add(del);
                buttons.Controls.Add(restore);
                buttons.Controls.Add(toExcl);
                buttons.Controls.Add(openDir);

                dlg.Controls.Add(list);
                dlg.Controls.Add(buttons);
                dlg.CancelButton = close;

                reload();
                dlg.ShowDialog(this);
            }
            UpdateStatsUi();
        }

        // ---------- Path lists ----------

        void UpdateMonitorLabel()
        {
            chkMonitor.Text = string.Format(Lang.T("settings.monitorLabel"), watchDirs.Count);
        }

        void EditWatchDirs()
        {
            if (EditPathList(Lang.T("watch.editTitle"), watchDirs, true))
            {
                UpdateMonitorLabel();
                SaveSettings();
                if (chkMonitor.Checked) StartWatchers();
            }
        }

        // Restores read access to C:\Windows\Temp (see FixWinTempAcl) so it can be
        // monitored, then adds it to the watch list. No UAC prompt at all if it's
        // already accessible (e.g. stock Windows, or already fixed via --install).
        void FixWinTempAccess()
        {
            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (!CanWatchDirectory(winTemp))
            {
                if (MessageBox.Show(this, Lang.T("msg.fixWinTempConfirm"), AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--fix-wintemp");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    using (var p = Process.Start(psi)) p.WaitForExit();
                }
                catch
                {
                    statusLabel.Text = Lang.T("status.fixWinTempCancelled");
                    return;
                }
            }
            if (!CanWatchDirectory(winTemp))
            {
                statusLabel.Text = Lang.T("status.fixWinTempFailed");
                return;
            }
            if (!watchDirs.Contains(winTemp)) watchDirs.Add(winTemp);
            UpdateMonitorLabel();
            SaveSettings();
            if (chkMonitor.Checked) StartWatchers();
            btnFixWinTemp.Visible = false;
            statusLabel.Text = Lang.T("status.fixWinTempDone");
        }

        void EditExclusions()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Lang.T("excl.title");
                dlg.Size = new Size(760, 440);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.BackColor = Theme.Bg;
                dlg.ForeColor = Theme.Text;
                dlg.Font = Font;
                Theme.DarkTitleBar(dlg);

                var list = MakeList();
                list.Columns.Add(Lang.T("col.path"), 520);
                list.Columns.Add(Lang.T("col.type"), 120);

                Action reload = delegate
                {
                    list.Items.Clear();
                    foreach (string p in exclusions)
                    {
                        string kind = File.Exists(p) ? Lang.T("type.file") : (Directory.Exists(p) ? Lang.T("type.folder") : Lang.T("type.missing"));
                        var item = new ListViewItem(new string[] { p, kind });
                        item.Tag = p;
                        list.Items.Add(item);
                    }
                };

                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Height = 50;
                buttons.Padding = new Padding(8);
                buttons.BackColor = Theme.Bg;

                var close = MakeButton(Lang.T("btn.close"), 90, Theme.Card, Theme.Bg, Ico.Close);
                close.DialogResult = DialogResult.Cancel;
                var delFile = MakeButton(Lang.T("btn.deleteFile"), 140, Theme.Danger, Theme.DangerHot, Ico.Trash);
                var toQuar = MakeButton(Lang.T("btn.toQuarantine"), 110, Theme.Accent, Theme.AccentHot, Ico.ShieldIcon);
                var remove = MakeButton(Lang.T("btn.removeFromList"), 165, Theme.Card, Theme.Bg, Ico.Close);
                var addFile = MakeButton(Lang.T("btn.addFile"), 135, Theme.Card, Theme.Bg, Ico.FilePlus);
                var addDir = MakeButton(Lang.T("btn.addFolder"), 140, Theme.Card, Theme.Bg, Ico.FolderPlus);

                remove.Click += delegate
                {
                    foreach (ListViewItem it in list.SelectedItems)
                        exclusions.Remove((string)it.Tag);
                    SaveSettings();
                    reload();
                };

                toQuar.Click += delegate
                {
                    foreach (ListViewItem it in list.SelectedItems)
                    {
                        string p = (string)it.Tag;
                        if (!File.Exists(p))
                        {
                            MessageBox.Show(dlg, string.Format(Lang.T("msg.onlyExistingToQuarantine"), p), Lang.T("quarantine.title"));
                            continue;
                        }
                        if (QuarantineFile(p)) exclusions.Remove(p);
                    }
                    SaveSettings();
                    UpdateStatsUi();
                    reload();
                };

                delFile.Click += delegate
                {
                    if (list.SelectedItems.Count == 0) return;
                    if (MessageBox.Show(dlg,
                        string.Format(Lang.T("msg.deleteFromDiskConfirm"), list.SelectedItems.Count),
                        Lang.T("title.deletion"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    foreach (ListViewItem it in list.SelectedItems)
                    {
                        string p = (string)it.Tag;
                        try
                        {
                            if (File.Exists(p)) File.Delete(p);
                            exclusions.Remove(p);
                        }
                        catch (Exception ex) { MessageBox.Show(dlg, ex.Message, Lang.T("title.error")); }
                    }
                    SaveSettings();
                    reload();
                };

                addFile.Click += delegate
                {
                    using (var f = new OpenFileDialog())
                        if (f.ShowDialog(dlg) == DialogResult.OK) { AddExclusion(f.FileName); SaveSettings(); reload(); }
                };
                addDir.Click += delegate
                {
                    using (var f = new FolderBrowserDialog())
                        if (f.ShowDialog(dlg) == DialogResult.OK) { AddExclusion(f.SelectedPath); SaveSettings(); reload(); }
                };

                buttons.Controls.Add(close);
                buttons.Controls.Add(delFile);
                buttons.Controls.Add(toQuar);
                buttons.Controls.Add(remove);
                buttons.Controls.Add(addFile);
                buttons.Controls.Add(addDir);

                dlg.Controls.Add(list);
                dlg.Controls.Add(buttons);
                dlg.CancelButton = close;

                reload();
                dlg.ShowDialog(this);
            }
            statusLabel.Text = string.Format(Lang.T("status.exclusionsCount"), exclusions.Count);
            UpdateStatsUi();
        }

        // Generic path-list editor; returns true if OK was pressed
        bool EditPathList(string title, List<string> target, bool requireDir)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.Size = new Size(560, 340);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.BackColor = Theme.Bg;
                dlg.ForeColor = Theme.Text;
                dlg.Font = Font;
                Theme.DarkTitleBar(dlg);

                var box = new TextBox();
                box.Multiline = true;
                box.ScrollBars = ScrollBars.Vertical;
                box.Dock = DockStyle.Fill;
                box.Font = new Font("Consolas", 9.5f);
                box.BackColor = Theme.LogBg;
                box.ForeColor = Theme.Text;
                box.BorderStyle = BorderStyle.None;
                box.Text = string.Join(Environment.NewLine, target.ToArray());

                var boxWrap = new Panel();
                boxWrap.Dock = DockStyle.Fill;
                boxWrap.Padding = new Padding(12);
                boxWrap.BackColor = Theme.Bg;
                boxWrap.Controls.Add(box);

                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Height = 48;
                buttons.Padding = new Padding(8);
                buttons.BackColor = Theme.Bg;
                var ok = MakeButton("OK", 90, Theme.Accent, Theme.AccentHot, Ico.Check);
                ok.DialogResult = DialogResult.OK;
                var cancel = MakeButton(Lang.T("btn.cancel"), 100, Theme.Card, Theme.Bg, Ico.Close);
                cancel.DialogResult = DialogResult.Cancel;
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                dlg.Controls.Add(boxWrap);
                dlg.Controls.Add(buttons);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                if (dlg.ShowDialog(this) != DialogResult.OK) return false;

                target.Clear();
                foreach (string line in box.Lines)
                {
                    string d = line.Trim();
                    if (d.Length == 0) continue;
                    if (requireDir && !Directory.Exists(d))
                    {
                        AppendLog(string.Format(Lang.T("log.folderNotFound"), d), Theme.Warn);
                        continue;
                    }
                    if (!requireDir && !Directory.Exists(d) && !File.Exists(d))
                    {
                        AppendLog(string.Format(Lang.T("log.pathNotFound"), d), Theme.Warn);
                        continue;
                    }
                    if (!target.Contains(d)) target.Add(d);
                }
                return true;
            }
        }

        // ---------- New-file monitoring ----------

        void OnMonitorToggled()
        {
            if (chkMonitor.Checked) StartWatchers();
            else StopWatchers();
            SaveSettings();
            statusLabel.Text = chkMonitor.Checked
                ? Lang.T("status.monitorOn")
                : Lang.T("status.monitorOff");
        }

        void StartWatchers()
        {
            StopWatchers();
            foreach (string d in watchDirs)
            {
                if (!Directory.Exists(d)) continue;
                var w = new FileSystemWatcher(d);
                w.IncludeSubdirectories = true;
                w.NotifyFilter = NotifyFilters.FileName;
                w.InternalBufferSize = 65536; // large trees (Program Files) during installs
                w.SynchronizingObject = this; // events fire on the UI thread
                w.Created += delegate(object s, FileSystemEventArgs e) { QueueNewFile(e.FullPath); };
                w.Renamed += delegate(object s, RenamedEventArgs e) { QueueNewFile(e.FullPath); };
                try { w.EnableRaisingEvents = true; watchers.Add(w); }
                catch (Exception ex)
                {
                    AppendLog(string.Format(Lang.T("log.watchFailed"), d, ex.Message), Theme.Danger);
                    w.Dispose();
                }
            }
            AppendLog(string.Format(Lang.T("log.watchingFolders"), watchers.Count), Theme.Muted);
        }

        void StopWatchers()
        {
            foreach (var w in watchers) w.Dispose();
            watchers.Clear();
            debounceTimer.Stop();
            pendingFiles.Clear();
        }

        void QueueNewFile(string path)
        {
            if (Directory.Exists(path)) return; // files inside it will arrive as separate events
            if (IsExcluded(path)) return;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            // by default only potentially dangerous types are checked
            if (chkRiskyOnly != null && chkRiskyOnly.Checked && !RiskyExtensions.Contains(ext)) return;
            foreach (string t in TempExtensions)
                if (ext == t) return; // file still downloading: wait for the rename
            pendingFiles.Add(path);
            debounceTimer.Stop();
            debounceTimer.Start(); // restart: scan once the stream of new files settles down
        }

        void OnDebounceTick(object sender, EventArgs e)
        {
            if (scanRunning || updateRunning || !DbExists()) return; // try again on the next tick
            var ready = new List<string>();
            var stillLocked = new List<string>();
            foreach (string f in pendingFiles)
            {
                if (!File.Exists(f)) continue;
                if (IsFileLocked(f)) { stillLocked.Add(f); continue; }
                ready.Add(f);
            }
            pendingFiles.Clear();
            foreach (string f in stillLocked) pendingFiles.Add(f);
            if (pendingFiles.Count == 0) debounceTimer.Stop();
            if (ready.Count > 0) ScanFileBatch(ready);
        }

        static bool IsFileLocked(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        void ScanFileBatch(List<string> files)
        {
            ResetScanState(Lang.T("desc.autoCheck"));
            monitorScan = true;
            countGen++; // the total is known upfront, no background counting needed
            totalToScan = files.Count;
            AppendLog(string.Format(Lang.T("log.newFilesHeader"), DateTime.Now, files.Count), Theme.Text);
            SetBusy(true, string.Format(Lang.T("status.autoCheck"), files.Count));

            // Paths are passed via --file-list: hundreds of files on the command line
            // (e.g. installing to Program Files) exceed the ~32K character limit and Process.Start fails
            var args = new StringBuilder();
            args.Append("-r --stdout -d ").Append(Quote(dbDir)).Append(MoveArg()).Append(ExcludeArg()).Append(ScanLimitsArg());
            try
            {
                string lp = Path.Combine(Path.GetTempPath(), "clamui-batch.txt");
                File.WriteAllLines(lp, files.ToArray(), new UTF8Encoding(false));
                batchListPaths.Add(lp);
                args.Append(" --file-list=").Append(Quote(lp));
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Lang.T("log.listCreateFailedInline"), ex.Message), Theme.Warn);
                foreach (string f in files)
                    args.Append(" ").Append(Quote(f));
            }
            StartProcess(Path.Combine(clamDir, "clamscan.exe"), args.ToString(), OnScanLine, OnScanExit);
        }
        readonly List<string> batchListPaths = new List<string>(); // temporary lists for --file-list

        void CleanupBatchLists()
        {
            foreach (string p in batchListPaths) TryDelete(p);
            batchListPaths.Clear();
        }

        // ---------- Manual scanning ----------

        void PickAndScan(bool folder)
        {
            string target = null;
            if (folder)
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = Lang.T("dlg.pickFolder");
                    if (dlg.ShowDialog(this) == DialogResult.OK) target = dlg.SelectedPath;
                }
            }
            else
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = Lang.T("dlg.pickFile");
                    if (dlg.ShowDialog(this) == DialogResult.OK) target = dlg.FileName;
                }
            }
            if (target != null) RunClamscan(target);
        }

        void RunClamscan(string target)
        {
            if (scanRunning || updateRunning) return;
            ResetScanState(target);
            log.Clear();
            AppendLog(string.Format(Lang.T("log.scanning"), target), Theme.Text);
            AppendLog(Lang.T("log.buildingList"), Theme.Muted);
            SetBusy(true, Lang.T("status.scanning"));
            BeginListScan(new List<string> { target }, false);
        }

        string MoveArg()
        {
            return chkQuarantine.Checked ? " --move=" + Quote(quarDir) : "";
        }

        // Limits so clamscan doesn't bog down on gigabyte-sized files and deep archives —
        // those used to make a full scan "hang" on a single file for minutes. Malware that
        // ClamAV catches is almost always small, so this barely affects detection.
        static string ScanLimitsArg()
        {
            return " --max-filesize=50M --max-scansize=100M --max-recursion=6 --max-files=5000"
                 + " --max-scantime=20000"; // no more than 20s per object (skips "heavy" files faster)
        }

        // ---------- Progress, log, auto-update ----------

        static string FormatSpan(TimeSpan t)
        {
            if (t.TotalHours >= 1) return string.Format(Lang.T("time.hm"), Math.Floor(t.TotalHours), t.Minutes);
            if (t.TotalMinutes >= 1) return string.Format(Lang.T("time.ms"), Math.Floor(t.TotalMinutes), t.Seconds);
            return string.Format(Lang.T("time.s"), t.TotalSeconds);
        }

        void UpdateScanProgress()
        {
            if (totalToScan <= 0 || scannedCount <= 0) return;
            double f = Math.Min(1.0, (double)scannedCount / totalToScan);
            progress.SetFraction(f);
            // ETA via a moving window (rate over the last ~seconds), not the whole elapsed
            // time — otherwise a slow start (loading the DB + counting files on C:\, which
            // hammers the disk) inflates the estimate to hundreds of hours.
            string eta = "";
            if (scannedCount < totalToScan)
            {
                if (rateWinTime == DateTime.MinValue) { rateWinTime = DateTime.Now; rateWinCount = scannedCount; }
                double winElapsed = (DateTime.Now - rateWinTime).TotalSeconds;
                int winScanned = scannedCount - rateWinCount;
                if (winElapsed >= 5 && winScanned >= 20)
                {
                    double rate = winScanned / winElapsed; // files/s over the last window
                    double sec = (totalToScan - scannedCount) / rate;
                    lastEta = "~" + FormatSpan(TimeSpan.FromSeconds(sec));
                }
                // window not mature yet — keep the previous estimate, no "estimating…" flicker
                eta = lastEta.Length > 0 ? Lang.T("eta.remainingPrefix") + lastEta : Lang.T("eta.estimating");
                if (winElapsed >= 30) { rateWinTime = DateTime.Now; rateWinCount = scannedCount; } // shift the window
            }
            statusLabel.Text = string.Format(Lang.T("status.progress"),
                scannedCount, totalToScan, f * 100, eta, foundCount);

            if (monitorScan) return;
            if (!loggedTotal)
            {
                loggedTotal = true;
                AppendLog(string.Format(Lang.T("log.filesToCheck"), totalToScan) + "\r\n", Theme.Text);
            }
        }
        string lastEta = ""; // last time estimate ("~5m"), also shown by the heartbeat

        // Called by a timer every 10s during a scan — guarantees a log line even when
        // clamscan is stuck for a long time on a large file and prints nothing.
        void ScanHeartbeatTick()
        {
            if (!scanRunning || monitorScan) return;
            string elapsed = FormatSpan(DateTime.Now - scanStart);
            if (listingFiles)
            {
                AppendLog(string.Format(Lang.T("log.hbListing"), DateTime.Now, listedCount, elapsed), Theme.Muted);
                return;
            }
            if (startingEngine)
            {
                AppendLog(string.Format(Lang.T("log.hbEngineLoading"), DateTime.Now, elapsed), Theme.Muted);
                return;
            }
            bool stalled = (DateTime.Now - lastScanOutput).TotalSeconds >= 9; // no new output
            if (totalToScan <= 0)
            {
                AppendLog(string.Format(Lang.T("log.hbRunning"), DateTime.Now, scannedCount, elapsed), Theme.Muted);
                return;
            }
            double f = Math.Min(1.0, (double)scannedCount / totalToScan);
            int left = Math.Max(0, totalToScan - scannedCount);
            if (stalled)
                AppendLog(string.Format(Lang.T("log.hbBigFile"), DateTime.Now, scannedCount, totalToScan, f * 100, elapsed), Theme.Muted);
            else
                AppendLog(string.Format(Lang.T("log.hbProgress"), DateTime.Now, scannedCount, totalToScan, f * 100, left,
                    lastEta.Length > 0 ? " (" + lastEta + ")" : "",
                    foundCount > 0 ? string.Format(Lang.T("log.threatsSuffix"), foundCount) : ""), Theme.Muted);
        }

        void AppendScanLog()
        {
            try
            {
                File.AppendAllText(scanLogPath, string.Format(
                    "{0:yyyy-MM-dd HH:mm}  {1}  scanned: {2}, threats: {3}, quarantined: {4}, duration: {5}\r\n",
                    DateTime.Now, currentScanDesc, scannedCount, foundCount, movedCount,
                    FormatSpan(DateTime.Now - scanStart)), new UTF8Encoding(false));
            }
            catch { }
            if (pages != null && pages[0].Visible) RefreshHistory();
        }

        void OpenScanLog()
        {
            if (File.Exists(scanLogPath)) Process.Start("notepad.exe", Quote(scanLogPath));
            else statusLabel.Text = Lang.T("log.emptyLogFile");
        }

        // Once a day, quietly checks database versions (3 requests of 512 bytes). If a
        // newer database is out, shows the "Update Database" button, notifies via the
        // tray, and if auto-update is on, downloads right away — nothing for the user to click.
        void MaybeAutoUpdate()
        {
            if (scanRunning || updateRunning || clamDir == null) return;
            if (DateTime.Now < dbCooldownUntil) return; // server asked for a pause (429)
            if (!DbExists()) { if (chkAutoUpdate.Checked) RunFreshclam(true); return; }
            if (checkingDb || (DateTime.Now - lastDbCheck).TotalHours < 24) return;
            checkingDb = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool newer = false;
                foreach (string url in DbUrls)
                {
                    string dest = Path.Combine(dbDir, url.Substring(url.LastIndexOf('/') + 1));
                    long local = LocalCvdVersion(dest);
                    long remote = RemoteCvdVersion(url);
                    if (remote > 0 && remote > local) { newer = true; break; }
                }
                bool fresh = newer;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        checkingDb = false;
                        lastDbCheck = DateTime.Now;
                        SaveSettings();
                        updateAvailable = fresh;
                        RefreshDbStatus(); // shows/hides the update button
                        if (!fresh)
                        {
                            statusLabel.Text = Lang.T("status.dbUpToDate");
                            return;
                        }
                        if (chkAutoUpdate.Checked)
                        {
                            tray.ShowBalloonTip(5000, AppName,
                                Lang.T("tray.dbUpdateDownloading"), ToolTipIcon.Info);
                            AppendLog(string.Format(Lang.T("log.dbNewerAutoDownload"), DateTime.Now), Theme.Muted);
                            RunFreshclam(true);
                        }
                        else
                        {
                            heroSub.Text = Lang.T("hero.dbUpdateAvailable");
                            statusLabel.Text = Lang.T("status.dbUpdateAvailablePress");
                            tray.ShowBalloonTip(5000, AppName,
                                Lang.T("tray.dbUpdateAvailablePress"), ToolTipIcon.Info);
                        }
                    });
                }
                catch { checkingDb = false; } // the form is already closed
            });
        }
        bool checkingDb;       // a version check is already running
        DateTime lastDbCheck;  // time of the last daily check (persisted)
        bool updateAvailable;  // the server has a newer database — show the update button

        // ---------- Self-update: check GitHub Releases for a newer ClamAVUI.exe ----------

        const string UpdateApiUrl = "https://api.github.com/repos/alexbeatnik/ClamAV-WindowsUI/releases/latest";
        DateTime lastAppUpdateCheck; // time of the last daily check (persisted)
        bool checkingAppUpdate;      // a check/download is already in flight

        void MaybeCheckAppUpdate()
        {
            if (checkingAppUpdate || scanRunning || updateRunning) return;
            if ((DateTime.Now - lastAppUpdateCheck).TotalHours < 24) return;
            checkingAppUpdate = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { AppUpdateWorker(); });
        }

        // Checks the latest GitHub release and, if it's newer than AppVersion, downloads
        // its ClamAVUI.exe asset next to the current one. Runs entirely off the UI thread;
        // network failures are silent (retried on the next daily check).
        void AppUpdateWorker()
        {
            string downloadedExe = null, version = null;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                string json;
                using (var api = new System.Net.WebClient())
                {
                    api.Headers.Add("User-Agent", "ClamAVUI");
                    json = api.DownloadString(UpdateApiUrl);
                }
                var vm = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([\\d.]+)\"");
                var um = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*ClamAVUI\\.exe)\"");
                if (vm.Success && um.Success && new Version(vm.Groups[1].Value) > new Version(AppVersion))
                {
                    version = vm.Groups[1].Value;
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string updatePath = Path.Combine(baseDir, "ClamAVUI.update.exe");
                    TryDelete(updatePath); // leftover from an earlier interrupted attempt
                    using (var wc = new System.Net.WebClient())
                    {
                        wc.Headers.Add("User-Agent", "ClamAVUI");
                        wc.DownloadFile(um.Groups[1].Value, updatePath);
                    }
                    // sanity check: a real build is ~300 KB, an error page/HTML redirect is not
                    if (File.Exists(updatePath) && new FileInfo(updatePath).Length > 50 * 1024)
                        downloadedExe = updatePath;
                    else
                        TryDelete(updatePath);
                }
            }
            catch { } // offline, rate-limited, or no releases yet — try again tomorrow

            string fp = downloadedExe, fv = version;
            try { BeginInvoke((Action)delegate { OnAppUpdateChecked(fp, fv); }); }
            catch { }
        }

        void OnAppUpdateChecked(string updatePath, string version)
        {
            checkingAppUpdate = false;
            lastAppUpdateCheck = DateTime.Now;
            SaveSettings();
            if (updatePath == null) return;
            if (scanRunning || updateRunning) { TryDelete(updatePath); return; } // busy — retried tomorrow
            ApplyAppUpdate(updatePath, version);
        }

        // Swaps in the downloaded build and relaunches: a detached cmd.exe helper waits
        // for this process to exit (so the exe file is no longer locked), moves the new
        // build over the current one, then starts it again in the same tray state.
        void ApplyAppUpdate(string updatePath, string version)
        {
            try
            {
                tray.ShowBalloonTip(4000, AppName,
                    string.Format(Lang.T("tray.appUpdateInstalling"), version), ToolTipIcon.Info);
            }
            catch { }
            string exePath = Application.ExecutablePath;
            bool trayNext = WindowState == FormWindowState.Minimized || !ShowInTaskbar;
            var psi = new ProcessStartInfo("cmd.exe",
                "/c timeout /t 3 /nobreak >nul & move /y \"" + updatePath + "\" \"" + exePath + "\""
                + " & start \"\" \"" + exePath + "\"" + (trayNext ? " --tray" : ""));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try
            {
                Process.Start(psi);
                reallyClose = true;
                Close(); // releases the exe file lock so the helper script can replace it
            }
            catch { TryDelete(updatePath); }
        }

        // Quotes a command-line argument; a trailing \ before the quote must be doubled
        static string Quote(string path)
        {
            if (path.EndsWith("\\")) path += "\\";
            return "\"" + path + "\"";
        }

        // --exclude/--exclude-dir built from user exclusions + quarantine + ClamAV's own folder
        string ExcludeArg()
        {
            var all = new List<string>(exclusions);
            if (quarDir != null) all.Add(quarDir);
            if (clamDir != null) all.Add(clamDir);
            var sb = new StringBuilder();
            foreach (string d in all)
            {
                // (\\|$): matches only the path itself or what's inside it,
                // otherwise ^C:\...\quarantine would also match C:\...\quarantine2
                string rx = "(?i)^" + Regex.Escape(d.TrimEnd('\\')) + "(\\\\|$)";
                sb.Append(" --exclude-dir=\"").Append(rx).Append("\"");
                sb.Append(" --exclude=\"").Append(rx).Append("\"");
            }
            return sb.ToString();
        }

        // True if path is root itself or lies inside it. A plain StartsWith would not
        // work: "C:\Program Files" would also match "C:\Program Files (x86)".
        static bool IsUnder(string path, string root)
        {
            root = root.TrimEnd('\\');
            if (root.Length == 0) return false;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            return path.Length == root.Length || path[root.Length] == '\\';
        }

        bool IsExcluded(string path)
        {
            foreach (string d in exclusions)
                if (IsUnder(path, d)) return true;
            if (quarDir != null && IsUnder(path, quarDir)) return true;
            // ClamAV's own folder (database, binaries) — don't scan ourselves
            if (clamDir != null && IsUnder(path, clamDir)) return true;
            return false;
        }

        void RunFullScan()
        {
            if (scanRunning || updateRunning) return;
            var targets = new List<string>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
                if (d.DriveType == DriveType.Fixed && d.IsReady) targets.Add(d.RootDirectory.FullName);
            if (targets.Count == 0) return;
            string drives = string.Join(", ", targets.ToArray());
            bool risky = chkFullRisky.Checked;
            string warn = string.Format(risky ? Lang.T("msg.fullScanRiskyWarn") : Lang.T("msg.fullScanAllWarn"), drives);
            if (MessageBox.Show(this, warn, Lang.T("title.fullScan"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            ResetScanState(string.Format(Lang.T("desc.fullScan"), drives));
            log.Clear();
            AppendLog(string.Format(risky ? Lang.T("log.fullScanRisky") : Lang.T("log.fullScanAll"), drives), Theme.Text);
            AppendLog(Lang.T("log.buildingList"), Theme.Muted);
            SetBusy(true, Lang.T("status.fullScanRunning"));
            BeginListScan(targets, risky);
        }

        // Shared counter reset before a manual scan
        void ResetScanState(string desc)
        {
            scanRunning = true;
            monitorScan = false;
            scannedCount = 0;
            foundCount = 0;
            movedCount = 0;
            foundFiles.Clear();
            scanStart = DateTime.Now;
            loggedTotal = false;
            lastEta = "";
            rateWinTime = DateTime.MinValue;
            currentScanDesc = desc;
        }

        // Quick scan: risky file types in common infection points (downloads, desktop,
        // temp, AppData, startup) + running processes' executables. Minutes instead of
        // the hours a full scan takes.
        void RunQuickScan()
        {
            if (scanRunning || updateRunning || clamDir == null || !DbExists()) return;
            ResetScanState(Lang.T("desc.quickScan"));
            log.Clear();
            var roots = QuickScanRoots();
            AppendLog(Lang.T("log.quickScanHeader"), Theme.Text);
            foreach (string r in roots) AppendLog("  " + r + "\r\n", Theme.Muted);
            AppendLog(Lang.T("log.quickScanProcesses"), Theme.Muted);
            AppendLog(Lang.T("log.buildingList"), Theme.Muted);
            roots.AddRange(RunningProcessFiles());
            SetBusy(true, Lang.T("status.quickScanRunning"));
            BeginListScan(roots, true);
        }

        // Common places malware ends up. Nested paths are merged so the same
        // location isn't scanned twice (e.g. Temp inside AppData\Local).
        List<string> QuickScanRoots()
        {
            var list = new List<string>();
            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                try { p = Path.GetFullPath(p); } catch { return; }
                if (!Directory.Exists(p)) return;
                foreach (string e in list)
                    if (IsUnder(p, e)) return; // already covered by a broader root
                list.RemoveAll(delegate(string e) { return IsUnder(e, p); });
                list.Add(p);
            };
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));       // Roaming
            add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));  // Local (contains Temp)
            add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)); // ProgramData
            add(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            add(Path.GetTempPath());
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
            add(Environment.GetEnvironmentVariable("PUBLIC"));
            return list;
        }

        // Paths of running processes' executables — checks what's currently running
        static List<string> RunningProcessFiles()
        {
            var res = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string f = p.MainModule.FileName;
                    if (seen.Add(f) && File.Exists(f)) res.Add(f);
                }
                catch { } // system/protected processes — no access
                finally { try { p.Dispose(); } catch { } }
            }
            return res;
        }

        volatile bool cancelScanListing; // "Stop" pressed while building the file list
        volatile int listedCount;        // how many files are in the list so far (for the heartbeat)
        volatile bool listingFiles;      // the list is being built, clamscan hasn't started yet

        // Builds the file list in the background (applying type filters and exclusions)
        // and starts clamscan with --file-list. This way the scanner doesn't waste time
        // on gigabyte-sized videos/images, and progress knows the exact workload upfront.
        void BeginListScan(List<string> roots, bool riskyOnly)
        {
            int gen = ++countGen;
            cancelScanListing = false;
            listedCount = 0;
            listingFiles = true;
            // snapshot of exclusions: the background thread must not read the live list
            var skip = new List<string>(exclusions);
            if (quarDir != null) skip.Add(quarDir);
            if (clamDir != null) skip.Add(clamDir);
            var rootsCopy = new List<string>(roots);

            var th = new System.Threading.Thread(delegate()
            {
                Func<string, bool> excluded = delegate(string p)
                {
                    foreach (string s in skip)
                        if (IsUnder(p, s)) return true;
                    return false;
                };
                var files = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var stack = new Stack<string>();
                foreach (string r in rootsCopy)
                {
                    if (File.Exists(r)) { if (!excluded(r) && seen.Add(r)) files.Add(r); }
                    else if (Directory.Exists(r)) stack.Push(r);
                }
                DateTime lastUi = DateTime.MinValue;
                while (stack.Count > 0 && gen == countGen && !cancelScanListing)
                {
                    string d = stack.Pop();
                    if (excluded(d)) continue;
                    try
                    {
                        foreach (string f in Directory.GetFiles(d))
                        {
                            if (excluded(f)) continue;
                            if (riskyOnly && !RiskyExtensions.Contains(Path.GetExtension(f))) continue;
                            if (seen.Add(f)) files.Add(f);
                        }
                        foreach (string sub in Directory.GetDirectories(d))
                            stack.Push(sub);
                    }
                    catch { } // no access — skip
                    listedCount = files.Count;
                    if ((DateTime.Now - lastUi).TotalMilliseconds > 500)
                    {
                        lastUi = DateTime.Now;
                        int n = files.Count;
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                if (gen == countGen && listingFiles)
                                    statusLabel.Text = string.Format(Lang.T("status.buildingListFound"), n);
                            });
                        }
                        catch { }
                    }
                }
                bool cancelled = cancelScanListing;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (gen != countGen) return; // a different scan started — this UI update is stale
                        listingFiles = false;
                        if (cancelled)
                        {
                            scanRunning = false;
                            SetBusy(false, Lang.T("status.scanCancelled"));
                            AppendLog(Lang.T("log.cancelled"), Theme.Warn);
                            RefreshDbStatus();
                            return;
                        }
                        if (files.Count == 0)
                        {
                            scanRunning = false;
                            SetBusy(false, Lang.T("status.noFiles"));
                            AppendLog(Lang.T("log.noFiles"), Theme.Text);
                            RefreshDbStatus();
                            return;
                        }
                        totalToScan = files.Count;
                        loggedTotal = true;
                        AppendLog(string.Format(Lang.T("log.filesToCheck"), files.Count) + "\r\n\r\n", Theme.Text);
                        StartDaemonScan(files);
                    });
                }
                catch { } // the form is already closed
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---------- clamd engine: resident in memory only while scanning ----------

        const int ClamdPort = 3310;
        const string CancelledMarker = "__CANCELLED__"; // language-independent internal sentinel
        Process clamdProc;                 // the daemon we started
        volatile bool clamdStopping;       // the daemon is still shutting down (releasing the port)
        volatile bool startingEngine;      // clamd is loading the database, the scan hasn't started yet
        readonly List<Process> scanProcs = new List<Process>(); // parallel clamdscan processes
        int scanProcsLeft;                 // how many clamdscan processes are still running
        int scanAggExit;                   // aggregated exit code across chunks

        void WriteClamdConf()
        {
            File.WriteAllText(Path.Combine(clamDir, "clamd.conf"),
                "TCPSocket " + ClamdPort + "\r\n" +
                "TCPAddr 127.0.0.1\r\n" +
                "MaxThreads 8\r\n" +
                "DatabaseDirectory \"" + dbDir + "\"\r\n" +
                // same limits as ScanLimitsArg uses for clamscan
                "MaxScanSize 100M\r\nMaxFileSize 50M\r\nMaxRecursion 6\r\n" +
                "MaxFiles 5000\r\nMaxScanTime 20000\r\n" +
                "IdleTimeout 300\r\nForeground yes\r\n",
                new UTF8Encoding(false));
        }

        // clamd only opens its port AFTER loading the database,
        // so PING→PONG is a reliable readiness signal
        static bool ClamdPing()
        {
            return ClamdCommand("PING").StartsWith("PONG");
        }

        static string ClamdCommand(string cmd)
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    c.Connect("127.0.0.1", ClamdPort);
                    using (var s = c.GetStream())
                    {
                        byte[] b = Encoding.ASCII.GetBytes("n" + cmd + "\n");
                        s.Write(b, 0, b.Length);
                        s.ReadTimeout = 3000;
                        var buf = new byte[64];
                        int n = s.Read(buf, 0, buf.Length);
                        return n > 0 ? Encoding.ASCII.GetString(buf, 0, n) : "";
                    }
                }
            }
            catch { return ""; }
        }

        // Starts clamd and waits for it to become ready in the background;
        // onReady/onFail are invoked on the UI thread
        void EnsureClamd(Action onReady, Action<string> onFail)
        {
            startingEngine = true;
            var th = new System.Threading.Thread(delegate()
            {
                string err = null;
                try
                {
                    // a previous daemon is still shutting down — give it time to free the port
                    DateTime stopWait = DateTime.Now.AddSeconds(10);
                    while (clamdStopping && DateTime.Now < stopWait)
                        System.Threading.Thread.Sleep(300);
                    WriteClamdConf();
                    if (cancelScanListing) err = CancelledMarker;
                    else if (!ClamdPing())
                    {
                        var psi = new ProcessStartInfo(Path.Combine(clamDir, "clamd.exe"),
                            "-c " + Quote(Path.Combine(clamDir, "clamd.conf")));
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.WorkingDirectory = clamDir;
                        var p = Process.Start(psi);
                        string lastLine = "";
                        p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                            { if (!string.IsNullOrEmpty(e.Data)) lastLine = e.Data; };
                        p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                            { if (!string.IsNullOrEmpty(e.Data)) lastLine = e.Data; };
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        clamdProc = p;
                        DateTime deadline = DateTime.Now.AddSeconds(180);
                        bool ready = false;
                        while (DateTime.Now < deadline)
                        {
                            if (cancelScanListing) { err = CancelledMarker; break; }
                            if (p.HasExited)
                            {
                                err = Lang.T("err.daemonExited")
                                    + (lastLine.Length > 0 ? ": " + lastLine : "");
                                break;
                            }
                            if (ClamdPing()) { ready = true; break; }
                            System.Threading.Thread.Sleep(1000);
                        }
                        if (err == null && !ready) err = Lang.T("err.daemonTimeout");
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                string fe = err;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        startingEngine = false;
                        if (fe == null) onReady(); else onFail(fe);
                    });
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        // Gracefully stops the daemon (SHUTDOWN), falling back to Kill if needed — in the
        // background, so the UI isn't blocked. A foreign clamd on the same port is left alone.
        void StopClamd()
        {
            var p = clamdProc;
            clamdProc = null;
            if (p == null) return;
            clamdStopping = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (!p.HasExited)
                    {
                        ClamdCommand("SHUTDOWN");
                        if (!p.WaitForExit(5000)) p.Kill();
                    }
                }
                catch { try { p.Kill(); } catch { } }
                try { p.Dispose(); } catch { }
                clamdStopping = false;
            });
        }

        // Writes the full list + chunks, starts clamd, and launches parallel clamdscan
        // processes. If clamd is missing or fails to start, falls back to plain clamscan.
        void StartDaemonScan(List<string> files)
        {
            CleanupBatchLists();
            string tmp = Path.GetTempPath();
            string fullList = Path.Combine(tmp, "clamui-list.txt");
            try
            {
                File.WriteAllLines(fullList, files.ToArray(), new UTF8Encoding(false));
                batchListPaths.Add(fullList);
            }
            catch (Exception ex)
            {
                scanRunning = false;
                SetBusy(false, Lang.T("status.listCreateFailed"));
                AppendLog(string.Format(Lang.T("log.listCreateFailed"), ex.Message), Theme.Danger);
                RefreshDbStatus();
                return;
            }

            bool haveDaemon = File.Exists(Path.Combine(clamDir, "clamd.exe"))
                && File.Exists(Path.Combine(clamDir, "clamdscan.exe"));
            if (!haveDaemon)
            {
                StartProcess(Path.Combine(clamDir, "clamscan.exe"),
                    "--stdout -d " + Quote(dbDir) + MoveArg() + ScanLimitsArg()
                    + " --file-list=" + Quote(fullList), OnScanLine, OnScanExit);
                return;
            }

            // as many list chunks as parallel clamdscan processes
            int n = files.Count >= 200 ? Math.Max(2, Math.Min(4, Environment.ProcessorCount)) : 1;
            var chunks = new List<string>();
            if (n > 1)
            {
                int per = (files.Count + n - 1) / n;
                for (int i = 0; i < n && i * per < files.Count; i++)
                {
                    var slice = files.GetRange(i * per, Math.Min(per, files.Count - i * per));
                    string cp = Path.Combine(tmp, "clamui-list" + (i + 1) + ".txt");
                    try { File.WriteAllLines(cp, slice.ToArray(), new UTF8Encoding(false)); }
                    catch { chunks.Clear(); break; } // failed — fall back to a single process
                    chunks.Add(cp);
                    batchListPaths.Add(cp);
                }
            }
            if (chunks.Count == 0) chunks.Add(fullList);

            statusLabel.Text = Lang.T("status.engineStarting");
            AppendLog(Lang.T("log.engineStarting"), Theme.Muted);
            EnsureClamd(
                delegate { StartClamdscanChunks(chunks); },
                delegate(string msg)
                {
                    StopClamd();
                    if (msg == CancelledMarker)
                    {
                        scanRunning = false;
                        SetBusy(false, Lang.T("status.scanCancelled"));
                        AppendLog(Lang.T("log.cancelled"), Theme.Warn);
                        RefreshDbStatus();
                        CleanupBatchLists();
                        return;
                    }
                    AppendLog(string.Format(Lang.T("log.daemonFallback"), msg), Theme.Warn);
                    StartProcess(Path.Combine(clamDir, "clamscan.exe"),
                        "--stdout -d " + Quote(dbDir) + MoveArg() + ScanLimitsArg()
                        + " --file-list=" + Quote(fullList), OnScanLine, OnScanExit);
                });
        }

        void StartClamdscanChunks(List<string> chunks)
        {
            if (chunks.Count > 1)
                AppendLog(string.Format(Lang.T("log.scanningThreads"), chunks.Count), Theme.Muted);
            lastScanOutput = DateTime.Now;
            scanProcsLeft = chunks.Count;
            scanAggExit = 0;
            string conf = Quote(Path.Combine(clamDir, "clamd.conf"));
            foreach (string cp in chunks)
                StartScanChild(Path.Combine(clamDir, "clamdscan.exe"),
                    "-c " + conf + " --stdout --no-summary" + MoveArg() + " --file-list=" + Quote(cp));
        }

        // One of the parallel clamdscan processes; OnScanExit fires when the last one finishes
        void StartScanChild(string exe, string args)
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
            p.SynchronizingObject = this; // events and counters run on the UI thread
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { OnScanLine(e.Data); };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { OnScanLine(e.Data); };
            p.Exited += delegate
            {
                int code = 2;
                try { code = p.ExitCode; } catch { }
                scanProcs.Remove(p);
                try { p.Dispose(); } catch { }
                OnScanChildDone(code);
            };
            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                scanProcs.Add(p);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Lang.T("log.clamdscanStartFailed"), ex.Message), Theme.Danger);
                OnScanChildDone(2);
            }
        }

        void OnScanChildDone(int code)
        {
            // 1 (threats found) outranks 2 (error); 0 only if everything came back clean
            if (code == 1) scanAggExit = 1;
            else if (code != 0 && scanAggExit != 1) scanAggExit = 2;
            scanProcsLeft--;
            if (scanProcsLeft <= 0) OnScanExit(scanAggExit);
        }

        void OnScanLine(string line)
        {
            if (line == null) return;
            lastScanOutput = DateTime.Now; // clamscan produced output — it's not stuck
            if (line.Contains(": moved to '"))
            {
                RecordQuarantineMove(line);
                AppendLog(line + "\r\n", Theme.Warn);
                return;
            }
            if (line.EndsWith(" FOUND"))
            {
                foundCount++;
                scannedCount++;
                int sep = line.LastIndexOf(": ");
                if (sep > 0)
                {
                    string path = line.Substring(0, sep);
                    string sig = line.Substring(sep + 2, line.Length - sep - 2 - 6); // strip " FOUND"
                    foundFiles.Add(new string[] { path, sig });
                }
                AppendLog(line + "\r\n", Theme.Danger);
                if (totalToScan > 0) UpdateScanProgress();
                else statusLabel.Text = string.Format(Lang.T("status.scannedFound"), scannedCount, foundCount);
            }
            else if (line.EndsWith(": OK"))
            {
                scannedCount++;
                if (monitorScan) AppendLog(line + "\r\n", Theme.Muted);
                if (scannedCount % 10 == 0 || scannedCount == totalToScan)
                {
                    if (totalToScan > 0) UpdateScanProgress();
                    else statusLabel.Text = string.Format(Lang.T("status.scannedFound"), scannedCount, foundCount);
                }
            }
            else if (!monitorScan && line.Trim().Length > 0)
            {
                AppendLog(line + "\r\n", Theme.Muted);
            }
        }

        // Parses a clamscan line "C:\path: moved to 'C:\quarantine\file'"
        void RecordQuarantineMove(string line)
        {
            int idx = line.LastIndexOf(": moved to '");
            if (idx < 0 || !line.EndsWith("'")) return;
            string original = line.Substring(0, idx);
            string moved = line.Substring(idx + 12, line.Length - idx - 13);
            movedCount++;
            try
            {
                File.AppendAllText(quarIndex,
                    Path.GetFileName(moved) + "|" + original + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        void OnScanExit(int exitCode)
        {
            bool wasMonitor = monitorScan;
            scanRunning = false;
            monitorScan = false;
            countGen++; // stop the background file counter
            totalToScan = 0;
            StopClamd(); // the daemon lives only for the duration of the scan
            CleanupBatchLists();
            SetBusy(false, null);
            if (!wasMonitor && (exitCode == 0 || exitCode == 1))
                AppendLog(string.Format(Lang.T("log.summary"),
                    scannedCount, FormatSpan(DateTime.Now - scanStart), foundCount),
                    foundCount > 0 ? Theme.Danger : Theme.Text);
            if (exitCode == 0 || exitCode == 1)
            {
                totalScans++;
                totalFilesScanned += scannedCount;
                totalFound += foundCount;
                totalMoved += movedCount;
                lastScanInfo = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                SaveSettings();
                UpdateStatsUi();
                AppendScanLog();
            }
            if (exitCode == 0)
            {
                statusLabel.Text = string.Format(Lang.T("status.doneClean"), scannedCount);
                RefreshDbStatus(); // returns to the green "Protected" state
                if (wasMonitor)
                {
                    AppendLog(Lang.T("log.newFilesClean"), Theme.Good);
                    tray.ShowBalloonTip(4000, AppName,
                        string.Format(Lang.T("tray.newFilesClean"), scannedCount), ToolTipIcon.Info);
                }
                else
                {
                    AppendLog(Lang.T("log.noThreatsFound"), Theme.Good);
                    tray.ShowBalloonTip(4000, AppName, Lang.T("tray.scanDoneClean"), ToolTipIcon.Info);
                }
            }
            else if (exitCode == 1)
            {
                string movedInfo = movedCount > 0
                    ? string.Format(Lang.T("status.quarantinedSuffix"), movedCount) : "";
                statusLabel.Text = string.Format(Lang.T("status.threatsFound"),
                    scannedCount, foundCount, movedInfo);
                SetHero(ShieldState.Danger, Lang.T("hero.threatsFoundTitle"),
                    string.Format(Lang.T("hero.threatsFoundSub"), foundCount, movedInfo));
                AppendLog(string.Format(Lang.T("log.threatsFound"), foundCount, movedInfo), Theme.Danger);
                tray.ShowBalloonTip(8000, AppName,
                    string.Format(Lang.T("tray.threatsFoundWarn"), foundCount, movedInfo), ToolTipIcon.Warning);
                RestoreFromTray();
                ShowThreatDialog(); // shows nothing if every file was already quarantined
            }
            else
            {
                statusLabel.Text = string.Format(Lang.T("status.scanInterrupted"), exitCode);
                RefreshDbStatus();
            }
            // New files may have appeared while the scan was running
            if (pendingFiles.Count > 0) { debounceTimer.Stop(); debounceTimer.Start(); }
        }

        // ---------- Database updates ----------

        void RunFreshclam()
        {
            RunFreshclam(false);
        }

        // ---------- Automatic ClamAV installation (for a clean PC) ----------

        void OfferClamAVDownload()
        {
            if (!IsInstalled)
            {
                DialogResult r = MessageBox.Show(this,
                    Lang.T("msg.offerInstallChoice"),
                    AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) LaunchInstaller();
                else if (r == DialogResult.No) StartClamAVDownload();
                return;
            }
            if (MessageBox.Show(this,
                Lang.T("msg.offerPortableDownload"),
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                StartClamAVDownload();
        }

        void LaunchInstaller()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--install");
                psi.UseShellExecute = true;
                psi.Verb = "runas"; // UAC
                Process.Start(psi);
                reallyClose = true;
                Close(); // the installed copy will launch itself after copying
            }
            catch // user declined the UAC prompt
            {
                statusLabel.Text = Lang.T("status.installCancelled");
            }
        }

        void StartClamAVDownload()
        {
            if (scanRunning || updateRunning) return;
            updateRunning = true;

            // If the archive was already downloaded (interrupted install), extract it instead of re-downloading
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string existingZip = Path.Combine(baseDir, "clamav-download.zip");
            if (File.Exists(existingZip) && new FileInfo(existingZip).Length > 50 * 1048576)
            {
                SetBusy(true, Lang.T("status.foundArchiveExtracting"));
                SetHero(ShieldState.Busy, Lang.T("hero.installingClamAV"), Lang.T("hero.extractingArchive"));
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { ExtractClamZip(baseDir, existingZip); });
                return;
            }

            SetBusy(true, Lang.T("status.findingLatestClamAV"));
            SetHero(ShieldState.Busy, Lang.T("hero.installingClamAV"), Lang.T("hero.findingLatestRelease"));
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string url = null;
                try
                {
                    using (var api = new System.Net.WebClient())
                    {
                        api.Headers.Add("User-Agent", "ClamAVUI");
                        string json = api.DownloadString(
                            "https://api.github.com/repos/Cisco-Talos/clamav/releases/latest");
                        var m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+win\\.x64\\.zip)\"");
                        if (m.Success) url = m.Groups[1].Value;
                    }
                }
                catch { } // API unavailable — fall back to a known version below
                if (url == null)
                    url = "https://github.com/Cisco-Talos/clamav/releases/download/clamav-1.5.3/clamav-1.5.3.win.x64.zip";
                try { BeginInvoke((Action<string>)DownloadClamZip, url); }
                catch { }
            });
        }

        void DownloadClamZip(string url)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string zipPath = Path.Combine(baseDir, "clamav-download.zip");
            AppendLog(string.Format(Lang.T("log.downloading"), url), Theme.Muted);

            var wc = new System.Net.WebClient();
            clamZipClient = wc; // so "Stop" can cancel the download
            wc.Headers.Add("User-Agent", "ClamAVUI");
            wc.DownloadProgressChanged += delegate(object s, System.Net.DownloadProgressChangedEventArgs e)
            {
                if (e.TotalBytesToReceive > 0)
                {
                    progress.SetFraction((double)e.BytesReceived / e.TotalBytesToReceive);
                    statusLabel.Text = string.Format(Lang.T("status.downloadingClamAV"),
                        e.BytesReceived / 1048576, e.TotalBytesToReceive / 1048576);
                }
            };
            wc.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs e)
            {
                clamZipClient = null;
                wc.Dispose();
                if (e.Cancelled)
                {
                    updateRunning = false;
                    TryDelete(zipPath); // a partial archive isn't usable for recovery
                    SetBusy(false, Lang.T("status.clamAVDownloadCancelled"));
                    SetHero(ShieldState.Warning, Lang.T("hero.installCancelled"),
                        Lang.T("hero.pressUpdateRetry"));
                    return;
                }
                if (e.Error != null)
                {
                    updateRunning = false;
                    SetBusy(false, string.Format(Lang.T("status.clamAVDownloadFailed"), e.Error.Message));
                    SetHero(ShieldState.Danger, Lang.T("hero.downloadError"),
                        Lang.T("hero.checkConnectionRetry"));
                    return;
                }
                statusLabel.Text = Lang.T("status.extractingClamAV");
                SetHero(ShieldState.Busy, Lang.T("hero.installingClamAV"), Lang.T("hero.extractingArchive"));
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { ExtractClamZip(baseDir, zipPath); });
            };
            wc.DownloadFileAsync(new Uri(url), zipPath);
        }

        void ExtractClamZip(string baseDir, string zipPath)
        {
            string err = null;
            try
            {
                string tmp = Path.Combine(baseDir, "clamav-tmp");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tmp);
                // inside the zip is a clamav-x.y.z.win.x64 folder
                string src = tmp;
                if (!File.Exists(Path.Combine(src, "clamscan.exe")))
                    foreach (string d in Directory.GetDirectories(tmp))
                        if (File.Exists(Path.Combine(d, "clamscan.exe"))) { src = d; break; }
                string dst = Path.Combine(baseDir, "clamav");
                if (!File.Exists(Path.Combine(src, "clamscan.exe")))
                    throw new Exception(Lang.T("err.noClamscanInArchive"));
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                Directory.Move(src, dst);
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                err = ex.Message;
                // clean up a corrupt/partial archive, otherwise every retry just trips
                // over it again instead of re-downloading
                if (ex is InvalidDataException) TryDelete(zipPath);
            }

            try
            {
                BeginInvoke((Action)delegate
                {
                    updateRunning = false;
                    if (err != null)
                    {
                        SetBusy(false, string.Format(Lang.T("status.clamAVInstallError"), err));
                        SetHero(ShieldState.Danger, Lang.T("hero.installError"), err);
                        return;
                    }
                    SetBusy(false, Lang.T("status.clamAVInstalled"));
                    AppendLog(Lang.T("log.clamAVInstalled"), Theme.Good);
                    LocateClamAV();
                    RefreshDbStatus();
                    if (clamDir != null && !DbExists()) RunFreshclam(); // fetch the database right away
                });
            }
            catch { }
        }

        // Downloads the database directly instead of via freshclam: its libcurl reliably
        // hangs when fetching main.cvd from Cloudflare, while .NET downloads it fine.
        static readonly string[] DbUrls = new string[]
        {
            "https://database.clamav.net/main.cvd",
            "https://database.clamav.net/daily.cvd",
            "https://database.clamav.net/bytecode.cvd"
        };
        volatile bool cancelUpdate;
        System.Net.WebClient clamZipClient; // the active ClamAV archive download
        DateTime dbCooldownUntil;           // the CDN returned 429 — don't hit the server again until this time
        int cooldown429Sec;                 // Retry-After from the 429 response (for the UI thread)

        void RunFreshclam(bool auto)
        {
            if (scanRunning || updateRunning) return;
            if (clamDir == null) { StartClamAVDownload(); return; } // clean PC
            // the server rate-limited us (429) — don't hammer it, that would extend the block
            if (DateTime.Now < dbCooldownUntil)
            {
                if (auto) return;
                if (MessageBox.Show(this,
                    string.Format(Lang.T("msg.cooldownWarn"), dbCooldownUntil.ToString("HH:mm")),
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            updateRunning = true;
            cancelUpdate = false;
            if (!auto)
            {
                log.Clear();
                AppendLog(Lang.T("log.updatingDbFirstTime"), Theme.Text);
            }
            else
                AppendLog(string.Format(Lang.T("log.autoUpdating"), DateTime.Now), Theme.Muted);
            SetBusy(true, auto ? Lang.T("status.autoUpdatingDb") : Lang.T("status.updatingDb"));
            SetHero(ShieldState.Busy, Lang.T("hero.updatingDb"), Lang.T("hero.downloadingSignatures"));

            System.Threading.ThreadPool.QueueUserWorkItem(delegate { DbUpdateWorker(); });
        }

        void DbUpdateWorker()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            string err = null;
            int updated = 0;
            try
            {
                foreach (string url in DbUrls)
                {
                    if (cancelUpdate) throw new Exception(CancelledMarker);
                    string name = url.Substring(url.LastIndexOf('/') + 1);
                    string dest = Path.Combine(dbDir, name);
                    long localVer = LocalCvdVersion(dest);
                    long remoteVer = RemoteCvdVersion(url);
                    if (localVer > 0 && remoteVer > 0 && localVer >= remoteVer)
                    {
                        UiLog(string.Format(Lang.T("log.dbAlreadyCurrent"), name, localVer), Theme.Muted);
                        continue;
                    }
                    DownloadCvd(url, dest, name);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                // 429 Too Many Requests: the CDN rate-limited our address — back off
                var we = ex as System.Net.WebException;
                var resp = we != null ? we.Response as System.Net.HttpWebResponse : null;
                if (resp != null && (int)resp.StatusCode == 429)
                {
                    int waitSec = 6 * 3600; // if the server didn't say how long — 6 hours
                    string ra = resp.Headers["Retry-After"];
                    int v;
                    if (!string.IsNullOrEmpty(ra) && int.TryParse(ra, out v) && v > 0)
                        waitSec = Math.Max(900, Math.Min(v, 24 * 3600));
                    cooldown429Sec = waitSec;
                    err = "429";
                }
            }

            string fe = err;
            int fu = updated;
            try { BeginInvoke((Action)delegate { OnDbUpdateDone(fe, fu); }); }
            catch { }
        }

        void OnDbUpdateDone(string err, int updated)
        {
            updateRunning = false;
            SetBusy(false, null);
            FetchClamVersion();
            if (err == null) updateAvailable = false; // updated — the button is no longer needed
            RefreshDbStatus();
            if (err == null)
            {
                statusLabel.Text = updated > 0 ? Lang.T("status.dbUpdated") : Lang.T("status.dbAlreadyCurrent2");
                AppendLog(updated > 0 ? Lang.T("log.dbUpdated") : Lang.T("log.dbAlreadyCurrentLog"), Theme.Good);
            }
            else if (err == CancelledMarker)
            {
                statusLabel.Text = Lang.T("status.updateCancelled");
                AppendLog(Lang.T("log.updateCancelled"), Theme.Warn);
            }
            else if (err == "429")
            {
                dbCooldownUntil = DateTime.Now.AddSeconds(cooldown429Sec);
                SaveSettings();
                statusLabel.Text = Lang.T("status.serverRateLimited");
                AppendLog(Lang.T("log.rateLimitedExplain")
                    + string.Format(Lang.T("log.nextAttempt"), dbCooldownUntil),
                    Theme.Warn);
            }
            else
            {
                statusLabel.Text = Lang.T("status.updateError");
                AppendLog(string.Format(Lang.T("log.updateErrorDetail"), err), Theme.Danger);
            }
        }

        // Database version from the 512-byte CVD header ("ClamAV-VDB:date:version:...")
        static long CvdVersionFromHeader(byte[] head, int len)
        {
            try
            {
                string s = Encoding.ASCII.GetString(head, 0, Math.Min(len, 512));
                string[] parts = s.Split(':');
                if (parts.Length >= 3) { long v; if (long.TryParse(parts[2], out v)) return v; }
            }
            catch { }
            return 0;
        }

        static long LocalCvdVersion(string path)
        {
            if (!File.Exists(path)) return 0;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var buf = new byte[512];
                    int n = fs.Read(buf, 0, 512);
                    return CvdVersionFromHeader(buf, n);
                }
            }
            catch { return 0; }
        }

        static long RemoteCvdVersion(string url)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.UserAgent = "ClamAV/1.5.3";
                req.Timeout = 30000;
                req.AddRange(0, 511);
                using (var resp = req.GetResponse())
                using (var rs = resp.GetResponseStream())
                {
                    var buf = new byte[512];
                    int total = 0, r;
                    while (total < 512 && (r = rs.Read(buf, total, 512 - total)) > 0) total += r;
                    return CvdVersionFromHeader(buf, total);
                }
            }
            catch { return 0; }
        }

        void DownloadCvd(string url, string dest, string name)
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "ClamAV/1.5.3";
            req.Timeout = 30000;
            string part = dest + ".part";
            using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (var rs = resp.GetResponseStream())
            using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write))
            {
                rs.ReadTimeout = 45000; // abort a stalled connection instead of waiting forever
                long total = resp.ContentLength;
                long got = 0;
                var buf = new byte[65536];
                int read;
                DateTime lastUi = DateTime.MinValue;
                while ((read = rs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelUpdate) throw new Exception(CancelledMarker);
                    fs.Write(buf, 0, read);
                    got += read;
                    if ((DateTime.Now - lastUi).TotalMilliseconds > 250)
                    {
                        lastUi = DateTime.Now;
                        long g = got, t = total;
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                if (t > 0) progress.SetFraction((double)g / t);
                                statusLabel.Text = string.Format(Lang.T("status.downloadingDb"),
                                    name, g / 1048576.0, t / 1048576.0, t > 0 ? g * 100.0 / t : 0);
                            });
                        }
                        catch { }
                    }
                }
            }
            // the server might have returned an error page instead of the database — check the CVD header
            if (LocalCvdVersion(part) <= 0)
            {
                TryDelete(part);
                throw new Exception(string.Format(Lang.T("err.notADatabaseFile"), name));
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            UiLog(string.Format(Lang.T("log.dbFileDownloaded"), name), Theme.Good);
        }

        // Thread-safe logging from a background thread
        void UiLog(string text, Color color)
        {
            try { BeginInvoke((Action)delegate { AppendLog(text, color); }); }
            catch { }
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
