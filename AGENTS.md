# AGENTS.md — guide for AI coding agents (and new contributors)

A WinForms wrapper around ClamAV for Windows. One ~350 KB portable exe,
**zero dependencies, zero toolchains**: it builds with the `csc.exe` compiler
that ships inside Windows (.NET Framework 4.8). Keep it that way.
Licensed under Apache 2.0 (`LICENSE`).

## Build & test

```powershell
.\build.ps1   # builds ClamAVUI.exe with C:\Windows\Microsoft.NET\...\csc.exe
.\test.ps1    # compiles src\ + tests\ into ClamAVUI.Tests.exe and runs it
```

Run **both** after every change. There is no .sln/.csproj and there must not
be one — both scripts glob `src\*.cs` (+ `tests\*.cs`); a new framework
reference means editing the `/r:` lists in **both** scripts. CI
(`.github/workflows/tests.yml`) runs exactly these two scripts on every PR.

## Hard constraints

- **C# 5 only** — the built-in compiler (v4.0.30319) rejects anything newer.
  No `$"..."` interpolation, no `?.`/`??=`, no `nameof`, no expression-bodied
  members, no `out var`, no pattern matching, no tuples, no auto-property
  initializers. `async/await` is available but the codebase mostly uses
  threads + `BeginInvoke`; anonymous callbacks use `delegate(...) { }` syntax.
- **.NET Framework 4.8 BCL only** — no NuGet, no third-party libraries,
  no image assets (icons are GDI+ vector glyphs in `src/Icons.cs`).
- **UTF-8 sources** (`/codepage:65001`); Ukrainian literals are normal.
- The app always runs **non-elevated**. Install is per-user
  (`%LocalAppData%\Programs`) and needs no admin; the few admin-only actions
  (`--fix-wintemp`, removing a legacy Program Files install via
  `--uninstall`) are separate short-lived relaunches with `Verb = "runas"`
  (see `MainForm.Install.cs`).
- Never commit build outputs, `clamav/`, `settings.ini`, `scans.log`, or
  `quarantine/` (all gitignored).

## Architecture

One `MainForm` class split into partial files by concern:

| File | Concern |
|------|---------|
| `src/MainForm.cs` | state fields, `Main()`, process plumbing, log rendering, autostart |
| `src/MainForm.Ui.cs` | all UI construction, pages, dialogs, `ApplyLanguage()` |
| `src/MainForm.Scan.cs` | scans, progress/ETA, clamd engine, scheduled quick scan |
| `src/MainForm.Updates.cs` | DB updates, ClamAV download, app self-update |
| `src/MainForm.Settings.cs` | locating ClamAV, `settings.ini` load/save |
| `src/MainForm.Quarantine.cs` | neutralized `.quar` storage, index, threat dialog |
| `src/MainForm.Monitor.cs` | FileSystemWatcher monitoring, exclusions |
| `src/MainForm.Install.cs` | per-user install/uninstall, legacy Program Files uninstall, ACL fixes |
| `src/MainForm.Usb.cs` | USB volume-arrival prompt |
| `src/Controls.cs`, `src/Icons.cs`, `src/Theme.cs` | custom-drawn controls, glyphs, dark palette |
| `src/Lang.cs` | the English/Ukrainian string table |

UI is built in code; the settings card uses absolute positions. All state
lives on the UI thread — background work goes through `ThreadPool`/threads
and marshals back with `BeginInvoke` (wrapped in `try/catch` for the
form-already-closed case). Child processes set `SynchronizingObject = this`.

## Working rules — details live in `.claude/skills/`

Follow the matching skill whenever a change touches one of these areas:

- **`localization`** — every user-visible string goes through `Lang.T("key")`,
  added in `src/Lang.cs` with **both** English and Ukrainian; persistent
  controls are re-texted in `ApplyLanguage()`.
- **`settings-key`** — a `settings.ini` key needs a parser line in
  `LoadSettings()`, a writer in `SaveSettings()`, and graceful degradation
  when missing from old files.
- **`testing`** — testable logic is exposed as `internal static` members of
  `MainForm` and covered in `tests/*.cs` (zero-dependency reflection runner).
- **`release`** — the version lives in `src/AssemblyInfo.cs`; merging a bump
  to `main` publishes the GitHub Release the app self-updates from.
- **`verify`** — launching the built exe for a manual check: single-instance
  tray app, both languages, and the Defender-eats-EICAR gotcha.
