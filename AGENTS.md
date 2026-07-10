# AGENTS.md — guide for AI coding agents (and new contributors)

A WinForms wrapper around ClamAV for Windows. One ~350 KB portable exe,
**zero dependencies, zero toolchains**: it builds with the `csc.exe` compiler
that ships inside Windows (.NET Framework 4.8). Keep it that way.

## Build & test

```powershell
.\build.ps1   # builds ClamAVUI.exe with C:\Windows\Microsoft.NET\...\csc.exe
.\test.ps1    # compiles src\ + tests\ into ClamAVUI.Tests.exe and runs it
```

Run **both** after every change. There is no .sln/.csproj and there must not
be one — the file lists live in `build.ps1`/`test.ps1`. Adding a new framework
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
- The app always runs **non-elevated**. Anything that needs admin is a
  separate short-lived `--install`/`--uninstall`/`--fix-wintemp` relaunch
  with `Verb = "runas"` (see `MainForm.Install.cs`).

## Architecture

One `MainForm` class split into partial files by concern:

| File | Concern |
|------|---------|
| `src/MainForm.cs` | state fields, `Main()`, process plumbing, log rendering, autostart |
| `src/MainForm.Ui.cs` | all UI construction, pages, dialogs, `ApplyLanguage()` |
| `src/MainForm.Scan.cs` | scans, progress/ETA, clamd engine, parallel clamdscan |
| `src/MainForm.Updates.cs` | DB updates, ClamAV download, app self-update |
| `src/MainForm.Settings.cs` | locating ClamAV, `settings.ini` load/save |
| `src/MainForm.Quarantine.cs` | neutralized `.quar` storage, index, threat dialog |
| `src/MainForm.Monitor.cs` | FileSystemWatcher monitoring, exclusions |
| `src/MainForm.Install.cs` | install/uninstall to Program Files, ACL fixes |
| `src/MainForm.Usb.cs` | USB volume-arrival prompt |
| `src/Controls.cs`, `src/Icons.cs`, `src/Theme.cs` | custom-drawn controls, glyphs, dark palette |
| `src/Lang.cs` | the English/Ukrainian string table |

UI is built in code; the settings card uses absolute positions. All state
lives on the UI thread — background work goes through `ThreadPool`/threads
and marshals back with `BeginInvoke` (wrapped in `try/catch` for the
form-already-closed case). Child processes set `SynchronizingObject = this`.

## Localization — mandatory

Every user-visible string goes through `Lang.T("key")`. Add keys in
`src/Lang.cs` with `A(key, english, ukrainian)` — **always both languages**.
Persistent controls must also be re-texted in `ApplyLanguage()`
(`MainForm.Ui.cs`); dialogs are built on demand and pick the language up
automatically. Format placeholders (`{0}`…) must match the `string.Format`
call sites in both translations.

## Settings

`settings.ini` next to the exe, plain `key=value` lines, no sections.
A new key needs both a parser line in `LoadSettings()` and a writer line in
`SaveSettings()` (`MainForm.Settings.cs`), and must degrade gracefully when
missing — old files from previous versions must keep working (see the
`watchinit`/`modeasked` migration flags for the pattern).

## Tests

`tests/*.cs` + the zero-dependency runner in `tests/TestFramework.cs`:
public static `Test*` methods in `*Tests` classes, discovered by reflection.
Logic meant to be testable is exposed as `internal static` members of
`MainForm` (e.g. `Quote`, `IsUnder`, `XorCopy`, `CvdVersionFromHeader`).
Prefer extending these pure helpers over testing UI.

## Releases & versioning

The version lives in `src/AssemblyInfo.cs` (`AssemblyVersion`).
`.github/workflows/release.yml` publishes a `vX.Y.Z` GitHub Release with the
built exe when `AssemblyVersion` changes on `main`. Work happens on `vX.Y.Z`
branches; bump the version on the branch so the merge triggers the release.
The app self-updates from these releases, so never publish a release whose
`ClamAVUI.exe` asset is missing or renamed.

## Manual verification

`.\ClamAVUI.exe` — single-instance tray app (a second launch just activates
the first; stop it with `Stop-Process -Name ClamAVUI`). `--tray` starts
minimized. A portable ClamAV lives in `clamav/` (gitignored, ~220 MB).
Windows Defender intercepts EICAR test files before clamscan can see them —
don't use EICAR to test detection on a machine with Defender active.

## Don'ts

- Don't introduce NuGet packages, SDK-style projects, or new toolchains.
- Don't use C# 6+ syntax (the build will fail on a stock machine).
- Don't add user-facing strings outside `Lang.cs`.
- Don't make the main app require elevation.
- Don't commit build outputs, `clamav/`, `settings.ini`, `scans.log`,
  or `quarantine/` (all gitignored).
