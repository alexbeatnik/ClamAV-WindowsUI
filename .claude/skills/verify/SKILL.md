---
name: verify
description: Launch and manually verify ClamAV UI after a change — single-instance/tray behavior, what to click through, and the Windows Defender/EICAR gotcha. Use after building when a change needs to be seen working in the real app.
---

# Verifying a change in the running app

Build first (`.\build.ps1`), then launch `.\ClamAVUI.exe`.

## App behavior to know

- **Single instance**: a second launch doesn't open a window — it just
  activates the running one (mutex + broadcast message). To restart after a
  rebuild: `Stop-Process -Name ClamAVUI`, then launch again.
- The close button **minimizes to tray**; actually exit via the tray menu
  (or `Stop-Process`).
- `--tray` starts minimized to the tray (this is what autostart uses).
- A portable ClamAV lives in `clamav/` next to the exe (gitignored,
  ~220 MB); without it most scan paths are disabled but the UI still runs.
- First run without `settings.ini` asks the portable-vs-Program-Files
  question. To test upgrade behavior, keep the existing `settings.ini`; to
  test first-run behavior, move it aside temporarily (it's gitignored).

## What to check after UI changes

- **Both languages**: Settings → switch English/Українська and re-inspect
  the changed controls — `ApplyLanguage()` must re-text them, and the longer
  Ukrainian strings must not clip or overlap.
- **Minimum window size** (900×700): the settings card is
  absolute-positioned; shrink the window fully and confirm nothing collides
  or falls off the card, especially in the right column (x=520+).
- Settings round-trip: toggle the new option, exit via tray, relaunch,
  confirm it stuck (`settings.ini` next to the exe shows the raw value).

## Detection-testing gotcha

Windows Defender intercepts **EICAR** test files before clamscan can see
them — on a machine with Defender active, don't use EICAR to test detection
or quarantine flows. Verify scan plumbing with a clean file instead (the
log/summary/threat-count paths are the same), or use a Defender exclusion
folder you control.
