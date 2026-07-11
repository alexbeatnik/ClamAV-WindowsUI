---
name: localization
description: Add or change user-visible text in ClamAV UI — Lang.cs keys, English+Ukrainian translations, ApplyLanguage re-texting. Use whenever any UI label, button, log line, tray balloon, status message, or dialog string is added or edited.
---

# Adding / changing user-visible strings

Every user-visible string goes through `Lang.T("key")` — never hardcode text
in the UI, logs, tray balloons, or dialogs.

## Steps

1. **Add the key** in `src/Lang.cs` inside the static constructor, grouped
   with related keys (the file is organized by feature area — find the
   nearest comment banner):

   ```csharp
   A("settings.schedule", "Scheduled quick scan:", "Плановий швидкий скан:");
   ```

   `A(key, english, ukrainian)` — **both languages, always**. English is the
   default; Ukrainian is the alternative. Missing Ukrainian silently falls
   back to English, which is a bug, not a feature.

2. **Persistent controls** (created once in `BuildUi`/`Build*Page` and kept
   alive) must also be re-texted in `ApplyLanguage()` in
   `src/MainForm.Ui.cs` — otherwise the language switch leaves them stale.
   Selector buttons with ●/○ markers have their own `Update*Buttons()`
   helper that `ApplyLanguage()` calls (see `UpdatePerfButtons`,
   `UpdateSchedButtons`).

3. **Dialogs and MessageBoxes need nothing extra** — they are built fresh
   each time they open and pick up the current language automatically.

## Gotchas

- Format placeholders (`{0}`, `{1:0}`…) must match the `string.Format` call
  site **in both translations** — `tests/LangTests.cs` has a test that
  cross-checks placeholders between En and Uk; it will fail the build if
  they diverge.
- Log lines that end paragraphs carry their own `\r\n` inside the string —
  match the neighbors.
- `ToolStripMenuItem` treats `&` as a mnemonic marker: when reusing a string
  in a menu, escape it (`Lang.T(...).Replace("&", "&&")`).
- Key naming convention: `area.name` — `btn.*`, `settings.*`, `status.*`,
  `log.*`, `tray.*`, `hero.*`, `msg.*`, `sstat.*`/`sval.*` (settings STATUS
  block), `col.*` (list columns), `title.*`, `err.*`.
- Sources are UTF-8 (`/codepage:65001`); Ukrainian literals are normal —
  don't escape them.
