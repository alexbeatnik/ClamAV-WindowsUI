---
name: settings-key
description: Add a persisted option to ClamAV UI — a settings.ini key with LoadSettings/SaveSettings wiring, migration for old settings files, and a control on the Settings page (absolute-positioned card layout). Use when adding a toggle, selector, or any state that must survive restarts.
---

# Adding a settings.ini key (and its Settings-page control)

`settings.ini` lives next to the exe: plain `key=value` lines, no sections,
written as UTF-8 without BOM. Everything is in `src/MainForm.Settings.cs`.

## Steps

1. **Field** in `src/MainForm.cs` with the other state fields, with a comment
   stating the meaning and the default (e.g. `int schedMode = 2; // 0 = off,
   1 = daily, 2 = weekly (the default)`).

2. **Parser line** in `LoadSettings()`. Follow the existing `else if` chain.
   Two idioms:
   - flags/enums: only parse values that differ from the default
     (`else if (t == "sched=off") schedMode = 0;` — the default needs no line);
   - timestamps/numbers: `t.StartsWith("lastsched=")` + `long.TryParse` on
     the substring; ignore unparsable values (never throw).

3. **Writer line** in `SaveSettings()` — always written, so the file is
   self-documenting.

4. **Degrade gracefully**: an old settings.ini without the key must behave
   sensibly. If the new feature needs one-time initialization on upgrade,
   follow the migration-flag pattern (`watchinit=1/2/3`, `modeasked`) or
   anchor pattern (`if (lastScheduledScan == DateTime.MinValue)
   lastScheduledScan = DateTime.Now;` after the parse loop).

5. **UI control** in `BuildSettingsPage()` (`src/MainForm.Ui.cs`). The card
   uses **absolute positions**:
   - left column: toggles at `x=20`, rows step by 36 px (`y = 56, 92, 128…`),
     created with `MakeCheck(text, x, y)`; wire `CheckedChanged` to
     `SaveSettings()`;
   - right column: `x=520`, ~310 px of width available before the card edge
     at the minimum window size. Radio-style selectors are rows of
     `ModernButton`s with ●/○ markers — copy the perf/sched pattern
     (`SetPerfMode`/`UpdatePerfButtons`, `SetSchedMode`/`UpdateSchedButtons`).
   - Add every new control to `cardSettingsPanel.Controls`.
   - Inserting mid-column means shifting the y of everything below it —
     check the whole column down to the STATUS block.

6. **Localization**: label/status strings go through `Lang.T` and persistent
   controls get re-texted in `ApplyLanguage()` — see the `localization` skill.

7. **Loading order**: `LoadSettings()` runs after `BuildUi()`, so controls
   exist; setting `chk*.Checked` fires `CheckedChanged`, which is why
   handlers early-return on the `loadingSettings` flag via `SaveSettings()`.
   Reflect loaded state of custom selectors explicitly (the
   `UpdatePerfButtons()` / `UpdateSchedButtons()` calls near the end).

## Gotcha

`SaveSettings()` early-returns while `loadingSettings` is true — safe to call
from anywhere. Never write settings.ini directly.
