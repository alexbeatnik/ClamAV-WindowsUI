---
name: testing
description: Add or run unit tests for ClamAV UI — the zero-dependency runner in tests/TestFramework.cs, making logic testable via internal static helpers, TempDir usage. Use when writing tests, making code testable, or investigating test failures.
---

# Tests without a test framework

`.\test.ps1` compiles `src\*.cs` + `tests\*.cs` into a console
`ClamAVUI.Tests.exe` (entry point `ClamAVUI.Tests.Program`, picked with
`/main:`) and runs it. Both scripts glob their folders — a new test file is
picked up automatically, no list to edit. CI runs this on every PR.

## Conventions

- Test classes: `static class SomethingTests` (name **must** end in `Tests`),
  in namespace `ClamAVUI.Tests`, discovered by reflection.
- Test methods: `public static void TestWhatever()` — name must start with
  `Test`. No attributes, no setup/teardown.
- Assertions: `Assert.True(cond, "label")`, `Assert.False(...)`,
  `Assert.Equal(expected, actual, "label")` — see `tests/TestFramework.cs`.
  The label is what you'll see on failure; make it say which case failed.
- Filesystem tests: `using (var tmp = new TempDir())` gives an isolated
  folder with `tmp.File("name")` helpers and cleans up after itself.

## Making logic testable

Don't test UI. Extract the pure decision/parsing into an **`internal static`**
member of `MainForm` (or the relevant static class) and test that. Existing
examples: `Quote`, `IsUnder`, `XorCopy`, `CvdVersionFromHeader`,
`RiskyExtensions`, `ScheduledScanDue`, `DriveRootsFromMask`,
`ProgressBarText`. The pattern: UI event handlers gather state and call the
pure helper; the helper takes everything as parameters (including
`DateTime now` — never read `DateTime.Now` inside a testable helper).

Tests can construct fixed dates (`new DateTime(2026, 7, 11, 12, 0, 0)`) and
probe boundaries: exactly-at-threshold, just-under, far-over, degenerate
inputs (MinValue, empty, garbage).

## Remember

C# 5 only in tests too — same compiler. And run **both** `.\build.ps1` and
`.\test.ps1` after every change; green tests with a broken GUI build is
still broken.
