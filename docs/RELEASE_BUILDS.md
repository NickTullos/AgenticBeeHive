# Release Builds & Versioning

This guide explains how to produce standalone release artifacts and how the app checks for updates.

## What This System Does

- Builds self-contained standalone binaries for common OS/CPU targets.
- Packages each target as a GitHub-upload-ready `.zip`.
- Uses `solution/src/version.json` as the release source of truth.
- Exposes `/api/version/check` so the UI can show `new update is available` at startup.

## Files Involved

- `solution/src/version.json` — app version manifest used by build + update check
- `solution/build/build-release.sh` — macOS/Linux release build script
- `solution/build/build-release.ps1` — PowerShell release build script
- `solution/build/out/` — publish output per RID
- `solution/build/dist/` — OS-specific artifact folders + `release-manifest.txt`

## Prerequisites

- .NET 7 SDK installed
- For `build-release.sh`: `bash`, `zip`, and `sha256sum` (or `shasum`)
- For `build-release.ps1`: PowerShell 7+

## Configure `version.json`

Update these fields before building:

- `version` — SemVer value (example: `0.1.0`)
- `updateManifestUrl` — raw GitHub URL to your remote `version.json`
- `releaseNotesUrl` — your GitHub Releases page/tag URL
- `publishedAtUtc` — UTC publish timestamp
- `assets` — optional; auto-synced by build scripts to match `version`

Build scripts now auto-sync `assets` to the current `version` using the active naming format before publishing.
If entries are out of sync, the script updates `solution/src/version.json`, logs each change, and then continues.

`solution/src/version.json` is the canonical version source for:
- Release artifact naming
- API `appVersion` used by the UI header
- Update-check current version comparisons

`CurrentVersion` in `appsettings.json` is now treated as a legacy fallback only if `version.json` is missing or invalid at runtime.

## Run Release Builds

From the repository root:

### macOS/Linux

```bash
cd solution
./build/build-release.sh
```

### PowerShell (Windows/macOS/Linux)

```powershell
cd solution
./build/build-release.ps1
```

## Output Naming

Each artifact is named:

`agenticbeehive-v<version>-<os>-<arch>.zip`

Examples:

- `agenticbeehive-v0.1.0-windows-x64.zip`
- `agenticbeehive-v0.1.0-linux-arm64.zip`
- `agenticbeehive-v0.1.0-macos-arm64.zip`

OS labels are user-facing:

- `windows`
- `linux`
- `macos`

Each zip extracts into a same-name top-level folder. Example:

- `agenticbeehive-v0.1.0-linux-arm64.zip` ⟶ `agenticbeehive-v0.1.0-linux-arm64/`

## Output Layout

Artifacts are grouped by OS:

- `solution/build/dist/windows/`
- `solution/build/dist/linux/`
- `solution/build/dist/macos/`

The root manifest remains at:

- `solution/build/dist/release-manifest.txt`

Manifest entries use relative paths, for example:

- `windows/agenticbeehive-v0.1.0-windows-x64.zip`
- `macos/agenticbeehive-v0.1.0-macos-arm64.zip`

## Target Matrix

Default targets:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

All targets are published with:

- `-c Release`
- `--self-contained true`
- `-p:PublishSingleFile=true`
- `-p:PublishTrimmed=false`
- `-p:Version=<full semver>` (for example `1.2.3-beta.1`)
- `-p:InformationalVersion=<full semver>`
- `-p:AssemblyVersion=<major>.<minor>.<patch>.0`
- `-p:FileVersion=<major>.<minor>.<patch>.0`
- `chmod +x abHive.Web` applied before zipping (when present)

The build packages include the full `workflowtypes/` tree from the project.
Runtime `logs/` files are excluded from publish artifacts and release zips.

## Verifying Output

After script completion:

1. Check `solution/build/dist/windows/`, `solution/build/dist/linux/`, and `solution/build/dist/macos/` for the 6 zip files.
2. Open `solution/build/dist/release-manifest.txt`.
3. Confirm each zip has a SHA-256 checksum entry with an OS-relative path.

## Running the Published App

After unzipping, run the web executable from inside the extracted folder:

- macOS/Linux: `./abHive.Web`
- Windows: `abHive.Web.exe`

Do not run `abHive`; it is not the web host executable.

Convenience launchers are also included:

- macOS/Linux: `start-abhive.sh`
- macOS Finder double-click: `start-abhive.command`

## Update Check Behavior

- On startup, the web client calls `/api/version/check`.
- The server loads local `version.json`.
- It fetches remote `version.json` from `updateManifestUrl`.
- If remote SemVer is newer, the UI shows `new update is available` next to `Agentic BeeHive`.
- Failures/timeouts are non-blocking (app still starts).

## GitHub Release Workflow (Recommended)

1. Bump `solution/src/version.json` (`version`, `publishedAtUtc`, URLs).
   - `assets` are auto-synced during build.
2. Run release script (`.sh` or `.ps1`).
3. Upload zips from `solution/build/dist/windows/`, `solution/build/dist/linux/`, and `solution/build/dist/macos/` to a GitHub release.
4. Commit/publish the updated remote `version.json` at your raw GitHub URL.
