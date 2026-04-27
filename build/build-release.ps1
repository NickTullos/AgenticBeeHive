#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDir = Split-Path -Parent $scriptDir
$projectPath = Join-Path $solutionDir 'src/ABHive.Web/ABHive.Web.csproj'
$versionFile = $null
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

foreach ($candidate in @(
    (Join-Path $solutionDir 'src/version.json'),
    (Join-Path $solutionDir 'version.json')
)) {
    if (Test-Path -LiteralPath $candidate) {
        $versionFile = $candidate
        break
    }
}

if ([string]::IsNullOrWhiteSpace([string]$versionFile)) {
    throw "Missing version file. Checked '$solutionDir/src/version.json' and '$solutionDir/version.json'."
}

$versionManifest = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
$version = [string]$versionManifest.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Missing version value in $versionFile"
}

if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
if (Test-Path -LiteralPath $distDir) { Remove-Item -LiteralPath $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null
New-Item -ItemType Directory -Path $distDir | Out-Null
New-Item -ItemType Directory -Path $packDir | Out-Null

foreach ($rid in $rids) {
    $parts = $rid.Split('-', 2)
    $os = $parts[0]
    $arch = $parts[1]
    $publishDir = Join-Path $outDir $rid
    $zipName = "agenticbeehive-v$version-build-$os-$arch.zip"
    $zipBase = [System.IO.Path]::GetFileNameWithoutExtension($zipName)
    $zipPath = Join-Path $distDir $zipName
    $packageRoot = Join-Path $packDir $zipBase

    Write-Host "Publishing $rid..."
    dotnet publish $projectPath `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -o $publishDir

    $unixExecutable = Join-Path $publishDir 'abHive.Web'
    if (Test-Path -LiteralPath $unixExecutable) {
        try {
            & chmod +x $unixExecutable
        } catch { }
    }

    if ($os -ne 'win') {
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

    if ($os -eq 'osx') {
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

    Write-Host "Packaging $zipName..."
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
    'sha256 filename'
)
Set-Content -LiteralPath $manifestPath -Value $header

foreach ($rid in $rids) {
    $parts = $rid.Split('-', 2)
    $os = $parts[0]
    $arch = $parts[1]
    $zipName = "agenticbeehive-v$version-build-$os-$arch.zip"
    $zipPath = Join-Path $distDir $zipName

    if (-not (Test-Path -LiteralPath $zipPath)) {
        throw "Missing expected artifact: $zipPath"
    }

    $expectedAsset = $versionManifest.assets.$rid
    if (-not [string]::IsNullOrWhiteSpace([string]$expectedAsset) -and $expectedAsset -ne $zipName) {
        throw "Asset name mismatch for $rid. version.json has '$expectedAsset' but built '$zipName'."
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    Add-Content -LiteralPath $manifestPath -Value "$hash $zipName"
}

Write-Host "Release build complete."
Write-Host "Artifacts: $distDir"
Write-Host "Manifest: $manifestPath"
if (Test-Path -LiteralPath $packDir) { Remove-Item -LiteralPath $packDir -Recurse -Force }
