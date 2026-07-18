---
name: release
description: Version bumps and releases of ClamAV UI — AssemblyInfo.cs, the release.yml workflow, vX.Y.Z branch flow, and the self-update constraints a release must respect. Use when bumping the version, preparing a release, or changing anything about update/release plumbing.
---

# Releases & versioning

The version lives in `src/AssemblyInfo.cs` (`AssemblyVersion`, four parts;
the UI shows the first three). `AppVersion` in `MainForm.cs` reads it from
the assembly — never hardcode a version string anywhere else.

## Flow

1. Work happens on a `vX.Y.Z` branch (e.g. `v0.0.6`).
2. Bump `AssemblyVersion` **on the branch** as part of the feature work.
3. Merging to `main` triggers `.github/workflows/release.yml`: it builds
   `ClamAVUI.exe` with `build.ps1` and publishes a `vX.Y.Z` GitHub Release
   with the exe attached.
4. The workflow no-ops if that version is already released, and can be run
   manually from the Actions tab.

## Constraints (breaking these breaks users)

- The app **self-updates** from these releases (checked every 4 hours
  against the latest release): never publish a release whose `ClamAVUI.exe`
  asset is missing or renamed, and never delete the latest release's asset.
- Self-update compares versions — a release must always carry a version
  strictly greater than the previous one.
- Old `settings.ini` files must keep working after an update (see the
  `settings-key` skill's migration patterns) — users don't reinstall, they
  get swapped exes.
- Tests (`.github/workflows/tests.yml`) run `build.ps1` + `test.ps1` on
  every PR — both must pass before merging a version bump.

## Release notes / README

When a release adds user-visible features, update `README.md` (the "What it
can do" list and, if the layout changed, the screenshots — retaken by the
`screenshots` skill) in the same branch. Architecture, install layout and
other technical detail belong in `README.DEV.md`, not in `README.md`.
