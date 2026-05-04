#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDir = Split-Path -Parent $scriptDir
$projectPath = Join-Path $solutionDir 'src/ABHive.Web/ABHive.Web.csproj'
$versionFile = Join-Path $solutionDir 'src/version.json'
$outDir = Join-Path $scriptDir 'out'
$distDir = Join-Path $scriptDir 'dist'
$packDir = Join-Path $distDir '.pack'

$rids = @(
    'win-x64',
    'win-arm64',
    'linux-x64',
    'linux-arm64',
    'osx-x64',
    'osx-arm64'
)

if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "Missing version file: $versionFile"
}

$versionManifest = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
$version = [string]$versionManifest.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Missing version value in $versionFile"
}

$semverPattern = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
$versionMatch = [regex]::Match($version, $semverPattern)
if (-not $versionMatch.Success) {
    throw "Version '$version' in $versionFile is not valid SemVer."
}

$assemblyFileVersion = "$($versionMatch.Groups['major'].Value).$($versionMatch.Groups['minor'].Value).$($versionMatch.Groups['patch'].Value).0"
$informationalVersion = $version

Write-Host "[version] SemVer=$version"
Write-Host "[version] AssemblyVersion/FileVersion=$assemblyFileVersion"

if ($null -eq $versionManifest.assets) {
    throw "Missing required 'assets' object in $versionFile"
}

$assetChanges = @()
foreach ($rid in $rids) {
    $parts = $rid.Split('-', 2)
    $ridOs = $parts[0]
    $arch = $parts[1]
    $osLabel = switch ($ridOs) {
        'win' { 'windows' }
        'linux' { 'linux' }
        'osx' { 'macos' }
        default { throw "Unsupported RID OS segment '$ridOs' for RID '$rid'." }
    }

    $expectedAsset = "agenticbeehive-v$version-$osLabel-$arch.zip"
    $assetProp = $versionManifest.assets.PSObject.Properties[$rid]
    if ($null -eq $assetProp) {
        throw "Missing required assets entry '$rid' in $versionFile"
    }

    $currentAsset = [string]$assetProp.Value
    if ($currentAsset -ne $expectedAsset) {
        $assetChanges += [PSCustomObject]@{
            Rid = $rid
            Old = $currentAsset
            New = $expectedAsset
        }
        $assetProp.Value = $expectedAsset
    }
}

if ($assetChanges.Count -gt 0) {
    Write-Host "[version-sync] Synchronizing asset names in $versionFile for version $version..."
    $syncedJson = $versionManifest | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $versionFile -Value $syncedJson -Encoding utf8
    foreach ($change in $assetChanges) {
        Write-Host "[version-sync] $($change.Rid): '$($change.Old)' -> '$($change.New)'"
    }

    $versionManifest = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
    if ($null -eq $versionManifest.assets) {
        throw "Version sync failed. Missing 'assets' object after writing $versionFile"
    }
} else {
    Write-Host "[version-sync] Assets already aligned for version $version."
}

if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
if (Test-Path -LiteralPath $distDir) { Remove-Item -LiteralPath $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null
New-Item -ItemType Directory -Path $distDir | Out-Null
New-Item -ItemType Directory -Path $packDir | Out-Null

foreach ($rid in $rids) {
    $parts = $rid.Split('-', 2)
    $ridOs = $parts[0]
    $arch = $parts[1]
    $osLabel = switch ($ridOs) {
        'win' { 'windows' }
        'linux' { 'linux' }
        'osx' { 'macos' }
        default { throw "Unsupported RID OS segment '$ridOs' for RID '$rid'." }
    }
    $publishDir = Join-Path $outDir $rid
    $distOsDir = Join-Path $distDir $osLabel
    $zipName = "agenticbeehive-v$version-$osLabel-$arch.zip"
    $zipBase = [System.IO.Path]::GetFileNameWithoutExtension($zipName)
    $zipPath = Join-Path $distOsDir $zipName
    $packageRoot = Join-Path $packDir $zipBase

    Write-Host "Publishing $rid..."
    dotnet publish $projectPath `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:Version=$version `
        -p:InformationalVersion=$informationalVersion `
        -p:AssemblyVersion=$assemblyFileVersion `
        -p:FileVersion=$assemblyFileVersion `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -o $publishDir

    $unixExecutable = Join-Path $publishDir 'abHive.Web'
    if (Test-Path -LiteralPath $unixExecutable) {
        try {
            & chmod +x $unixExecutable
        } catch { }
    }

    if ($ridOs -ne 'win') {
        $startShPath = Join-Path $publishDir 'start-abhive.sh'
        $startSh = @'
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "${SCRIPT_DIR}"

if [[ ! -x "./abHive.Web" && -f "./abHive.Web" ]]; then
  chmod +x "./abHive.Web" 2>/dev/null || true
fi

exec ./abHive.Web
'@
        Set-Content -LiteralPath $startShPath -Value $startSh -NoNewline
        try {
            & chmod +x $startShPath
        } catch { }
    }

    if ($ridOs -eq 'osx') {
        $startCommandPath = Join-Path $publishDir 'start-abhive.command'
        $startCommand = @'
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "${SCRIPT_DIR}"

if [[ ! -x "./abHive.Web" && -f "./abHive.Web" ]]; then
  chmod +x "./abHive.Web" 2>/dev/null || true
fi

./abHive.Web
EXIT_CODE=$?
echo
echo "ABHive exited with code ${EXIT_CODE}. Press Enter to close."
read -r _
exit ${EXIT_CODE}
'@
        Set-Content -LiteralPath $startCommandPath -Value $startCommand -NoNewline
        try {
            & chmod +x $startCommandPath
        } catch { }
    }

    $logsPath = Join-Path $publishDir 'logs'
    if (Test-Path -LiteralPath $logsPath) {
        Remove-Item -LiteralPath $logsPath -Recurse -Force
    }
    $schedulePath = Join-Path $publishDir 'schedule'
    if (Test-Path -LiteralPath $schedulePath) {
        Remove-Item -LiteralPath $schedulePath -Recurse -Force
    }
    $schedulePathUpper = Join-Path $publishDir 'Schedule'
    if (Test-Path -LiteralPath $schedulePathUpper) {
        Remove-Item -LiteralPath $schedulePathUpper -Recurse -Force
    }
    Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter '.DS_Store' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    Write-Host "Packaging $zipName..."
    New-Item -ItemType Directory -Path $distOsDir -Force | Out-Null
    if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $packageRoot | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $packageRoot -Recurse -Force
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
}

$manifestPath = Join-Path $distDir 'release-manifest.txt'
$header = @(
    'appName=Agentic BeeHive'
    "version=$version"
    "generatedAtUtc=$([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    ''
    'sha256 path'
)
Set-Content -LiteralPath $manifestPath -Value $header

foreach ($rid in $rids) {
    $parts = $rid.Split('-', 2)
    $ridOs = $parts[0]
    $arch = $parts[1]
    $osLabel = switch ($ridOs) {
        'win' { 'windows' }
        'linux' { 'linux' }
        'osx' { 'macos' }
        default { throw "Unsupported RID OS segment '$ridOs' for RID '$rid'." }
    }
    $zipName = "agenticbeehive-v$version-$osLabel-$arch.zip"
    $zipPath = Join-Path (Join-Path $distDir $osLabel) $zipName
    $relativePath = "$osLabel/$zipName"

    if (-not (Test-Path -LiteralPath $zipPath)) {
        throw "Missing expected artifact: $zipPath"
    }

    $expectedAsset = $versionManifest.assets.$rid
    if (-not [string]::IsNullOrWhiteSpace([string]$expectedAsset) -and $expectedAsset -ne $zipName) {
        throw "Asset name mismatch for $rid. version.json has '$expectedAsset' but built '$zipName'."
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    Add-Content -LiteralPath $manifestPath -Value "$hash $relativePath"
}

Write-Host "Release build complete."
Write-Host "Artifacts: $distDir"
Write-Host "Manifest: $manifestPath"
if (Test-Path -LiteralPath $packDir) { Remove-Item -LiteralPath $packDir -Recurse -Force }
