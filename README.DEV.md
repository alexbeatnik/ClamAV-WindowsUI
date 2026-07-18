# ClamAV Windows UI — developer guide

Technical documentation for contributors. The user-facing overview and quick
start live in [README.md](README.md); AI-agent-specific rules and hard
constraints are in [AGENTS.md](AGENTS.md).

## Building

```powershell
.\build.ps1   # builds ClamAVUI.exe with the csc.exe built into Windows
.\test.ps1    # builds and runs the unit tests (ClamAVUI.Tests.exe)
```

Nothing needs to be installed: the app targets .NET Framework 4.8 and builds
with the C# 5 compiler (`v4.0.30319\csc.exe`) that ships inside Windows. That
imposes the project's hard constraints — **C# 5 syntax only, .NET 4.8 BCL
only, no NuGet, no third-party libraries, no image assets** (icons are GDI+
vector glyphs in `src/Icons.cs`). There is no `.sln`/`.csproj` and there must
not be one: both scripts glob `src\*.cs`, and CI
(`.github/workflows/tests.yml`) runs exactly these two scripts on every PR.

The main window is deliberately **fixed-size** (`FixedSingle`, no maximize):
every page is a hand-tuned layout for 940×720 and the settings card uses
absolute positions.

## Resource & performance profile

* **Executable size:** ~380 KB (single portable exe, zero dependencies)
* **Downloads footprint:** the ClamAV zip (~220 MB) plus the signature
  database (~110 MB), fetched on first run and kept updated by `freshclam`
* **Typical memory profile:**
  * **Idle in the tray:** ~50 MB working set / ~27 MB private bytes (measured
    on Windows 11 — the usual .NET Framework WinForms floor)
  * **While scanning:** the UI process stays in that range; the resident
    ClamAV backend (`clamd`) is the heavy part, since it loads the whole
    signature database into memory (on the order of 1 GB). It is started for
    the scan and shut down immediately afterwards, so that cost is not paid
    while idle.

## Scan architecture & flow

Every scan — manual, quick, full, RAM, and the automatic new-file monitor —
runs over one file list the app builds itself, so the scanner never wastes
time walking gigabytes of video it was going to skip anyway:

```
       Scan input (disk walk / RAM dumps / new-file events)
                              │
                              ▼
        ClamAV signatures — clamd + parallel clamdscan workers
             (automatic fallback to plain clamscan)
                              │
               ┌──────────────┴──────────────┐
          (no match)                     (match)
               │                             │
               ▼                             ▼
       clean, scan ends          threat dialog: quarantine /
                                 delete / exclude — or silent
                                 auto-quarantine

  Quarantined files are stored neutralized: XOR-transformed .quar blobs
  that can't run and don't trip other antiviruses (reversible).
```

Quick scan, full scan and the dedicated **Scan RAM** button all dump running
processes' executable RAM (`MainForm.MemScan.cs`): best-effort
`OpenProcess`/`VirtualQueryEx`/`ReadProcessMemory`, writing executable
non-image regions to a temp folder so clamd sees code that is masked or
absent on disk. Inaccessible (protected / higher-integrity) processes are
skipped, dumps are capped per-region and in total, and cleaned up on every
scan-exit path.

Per-scan state (counters, phase flags, the cancel flag) lives in a
`ScanSession` object replaced wholesale at the start of each scan, so nothing
leaks from one scan into the next; a superseded scan's late writes land in
its own dead object.

Scan size limits are centralized in `ScanLimitsArg(bool skipBig)` (clamscan
args) and mirrored in `WriteClamdConf()` (clamd.conf) — keep the two in sync.
The per-file cap is user-controlled by the "skip large files" toggle (200 MB
when on, unlimited when off); the other limits — recursion, file count, and
especially `--max-scantime=10000` (10 s per object) — always apply and are
what keep even a multi-GB file from hanging a scan.

## How monitoring works

A `FileSystemWatcher` watches the configured folders (the **"Folders…"**
button). New files are queued with a 3 s debounce (so a file has time to
finish being written; temporary extensions like `.crdownload`/`.part` are
ignored until they're renamed), then scanned together in one `clamscan`
batch. If a threat is found, the window is restored and a warning is shown.

`C:\Windows\Temp` is only watched if it is actually readable: on hardened
systems a non-elevated process can be denied even read access, so the app
checks first and skips it instead of letting the watcher fail. Settings
offers a one-click fix (`--fix-wintemp`, a short-lived elevated relaunch)
that restores access, after which it is watched automatically.

## Tests

```powershell
.\test.ps1
```

Unit tests use the same zero-toolchain approach: `tests\*.cs` contains a tiny
reflection-based runner (no NuGet, no xUnit) compiled together with `src\*.cs`
into a console `ClamAVUI.Tests.exe`. Testable logic is exposed as
`internal static` members of `MainForm`. 150 tests currently cover the
quarantine XOR transform and index, `.cvd` header parsing, path/quoting
helpers, the risky-extension filter, the language table, scheduled-scan due
dates, database staleness and date formatting, protection pause, stale temp
sweeping, per-scan session state, recent-activity formatting, the self-update
swap, restore paths, memory-scan helpers and performance/USB helpers. CI runs
them on every pull request.

## Install layout & migration

The first run asks once how to use the app; the choice is remembered:

- **Install for this user** (no admin rights, no UAC): the exe is copied to
  `%LocalAppData%\Programs\ClamAV UI` together with anything already next to
  it (ClamAV, database, quarantine, settings), the engine is downloaded there
  if missing, Start Menu and Desktop shortcuts are created, and the app
  appears in "Apps" with an uninstall entry. The folder is private to the
  current user, so the binaries can't be replaced by other non-admin users.
- **Portable mode**: the current folder becomes the root — ClamAV, the
  signature database, quarantine and `settings.ini` all live next to the exe,
  leaving no traces on the system.

Installing later is one button in Settings. Old `settings.ini` files must keep
working across updates (users get swapped exes, they don't reinstall), so
every new settings key needs graceful degradation when missing.

> Versions before 0.0.8 installed to `C:\Program Files\ClamAV UI`. Such
> installs keep working and self-updating; to migrate one, run
> `ClamAVUI.exe --install` from the old folder (database and quarantine are
> carried over). Uninstalling removes every trace — the per-user install
> **and** any leftover Program Files copy (that part asks for admin rights
> once).

## Releases

`.github/workflows/release.yml` builds `ClamAVUI.exe` and publishes it as a
GitHub Release whenever `AssemblyVersion` in `src/AssemblyInfo.cs` changes on
`main` (a `vX.Y.Z` tag and a release with the exe attached appear
automatically). It no-ops if that version was already released and can also be
triggered manually from the Actions tab.

The app **self-updates** from these releases (checked on every launch, then
once a day while it keeps running), so:
never publish a release whose `ClamAVUI.exe` asset is missing or renamed,
never delete the latest release's asset, and always publish a version
strictly greater than the previous one.

## Project structure

```
src/                       — the application (WinForms, C# 5), compiled into one exe
  AssemblyInfo.cs          — assembly metadata (the version lives here)
  Theme.cs                 — dark palette, rounded corners, dark title bar interop
  Lang.cs                  — English/Ukrainian string table
  Icons.cs                 — vector glyphs drawn with GDI+ (no image assets)
  Controls.cs              — custom-drawn buttons, toggles, cards, nav tabs
  ScanSession.cs           — per-scan state (counters, phases, cancel flag)
  MainForm.cs              — state fields, entry point, process plumbing, autostart
  MainForm.Ui.cs           — pages and UI construction, language switching
  MainForm.Scan.cs         — scans, progress/ETA, clamd engine, scheduled scan
  MainForm.MemScan.cs      — process-memory dumping (RAM regions → temp → clamd)
  MainForm.Monitor.cs      — folder monitoring, exclusions
  MainForm.Quarantine.cs   — quarantine storage, index, threat dialog
  MainForm.Updates.cs      — DB updates, ClamAV download, app self-update
  MainForm.Settings.cs     — locating ClamAV, settings load/save
  MainForm.Pause.cs        — tray "pause protection" and auto-resume
  MainForm.Install.cs      — per-user install/uninstall (+ legacy), ACL fixes
  MainForm.Usb.cs          — USB drive detection, scan-on-connect prompt
tests/                     — unit tests + the zero-dependency test runner
.claude/skills/            — playbooks for AI coding agents (localization,
                             settings keys, testing, releases, verification,
                             screenshots)
AGENTS.md                  — build/test/style guide for agents and contributors
build.ps1 / test.ps1       — zero-toolchain build and test scripts
clamav.ico / logo.png      — app icon and header logo (embedded into the exe)
settings.ini               — settings and statistics (created automatically)
quarantine/                — neutralized (.quar) files + index.txt
clamav/                    — portable ClamAV (not in git, downloaded)
ClamAVUI.exe               — build output (not in git)
```

> **Note:** files under `quarantine/` are stored neutralized — every byte is
> XOR-ed with 0xFF and the file gets a `.quar` extension, so nothing there can
> run and other antiviruses won't react to the folder. Still, don't copy files
> out of it by hand; restore or delete them via the UI.

A more detailed per-file architecture map (which partial class owns which
concern, threading rules, settings-key conventions, localization rules) is
maintained in [AGENTS.md](AGENTS.md).
