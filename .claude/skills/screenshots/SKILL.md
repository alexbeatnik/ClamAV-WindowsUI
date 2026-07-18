---
name: screenshots
description: Retake the four README screenshots (dashboard, logs, quarantine, settings) by driving the running ClamAV UI app through UI Automation. Use when asked for new/updated README screenshots or to capture the app's pages.
---

# README screenshots

Run the ready-made script — it drives the app end-to-end and writes the four
PNGs to `screenshots\` (the paths README.md embeds):

```powershell
# full flow: clear log → real quick scan (~2-5 min) → capture all four pages
& .claude\skills\screenshots\capture.ps1

# just capture the pages as they are (no scan)
& .claude\skills\screenshots\capture.ps1 -NoScan
```

Run it **in the background** (it moves the mouse) and check its output for
`MISS:` lines (a UIA name lookup failed) and `saved:` lines. Warn the user
first that the mouse will move on its own. Review every PNG with the Read tool
before calling the job done — a stray modal (threat dialog, MessageBox,
ClamAV-not-found prompt) photobombs silently.

## Preconditions

- **ClamAV must be present** (`clamav\clamscan.exe` + a `database\*.cvd` next to
  the exe), or the dashboard shows the red "ClamAV not found" hero and a setup
  dialog blocks the capture. No local engine? Junction the sibling AV repo's
  copy: `New-Item -ItemType Junction -Path clamav -Target ..\AV\clamav` (then
  remove the junction afterwards). Set `autoupdate=0` in `settings.ini` while
  capturing so a startup freshclam doesn't touch that shared database.
- The UI must be in **English** (`lang=en` in `settings.ini` next to the exe
  that runs) — the script clicks controls by their UIA Name, the visible text.
- Display scaling must be 100% for crisp 1:1 pixels.
- For a lived-in dashboard (LAST SCAN date, SCANS/FILES counters, RECENT
  ACTIVITY list) either run a real scan (the default flow) or seed `scans.log`
  and the `lastscan=`/`totalScans=`/`totalFiles=`/`totalFound=` keys in
  `settings.ini` before launch, then capture with `-NoScan`.

## How it works — hard-won facts, don't rediscover them

- **Single instance**: launching `ClamAVUI.exe` again does NOT start a second
  copy — it broadcasts a "show yourself" message to the running one and exits.
  That is exactly how the script summons the window from the tray.
- **`Process.MainWindowHandle` lies**: it stays `0` for a form restored from
  the tray. The script finds the window by `EnumWindows`. The main window's
  caption text is painted invisible (the dark title bar hides it) but
  `Form.Text` stays `ClamAV UI`, so `GetWindowText` still matches on it.
- **Custom controls have no UIA Invoke pattern** (they're owner-drawn `Control`
  subclasses), but every WinForms control has its own HWND, so its `Text` shows
  up as the UIA `Name`. Click targets: nav tabs `Dashboard`, `Logs`,
  `Quarantine`, `Settings`; buttons `QUICK SCAN`, `CLEAR`. Clicks are real
  mouse events at the element's center (`SetCursorPos` + `mouse_event`).
- **Scan completion** is detected by a new `quick scan` summary line appended
  to `scans.log`.
- **Capture** = `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` +
  `Graphics.CopyFromScreen` from a DPI-aware process — window-only pixels, no
  drop shadow. The window is fixed-size (FixedSingle), so no maximize is needed.
- **The monitor watches `%TEMP%`** (and Downloads/Desktop/…): any file another
  tool writes there mid-run becomes an `auto-check of new files` line in the
  log page being photographed. Don't run other commands while it works.
- **Don't plant a live EICAR file** to stage a detection — Defender's real-time
  protection eats it before ClamAV sees it. The quarantine page's empty state
  ("No files in quarantine") is the honest default.
