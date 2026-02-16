param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts/release",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherProject = Join-Path $repoRoot "SubnauticaLauncher.csproj"
$updaterProject = Join-Path $repoRoot "tools\SNLUpdater\SNLUpdater.csproj"
$depotDownloaderZipUrl = "https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-windows-x64.zip"

if (-not (Test-Path $launcherProject)) {
    throw "Launcher project not found: $launcherProject"
}

if (-not (Test-Path $updaterProject)) {
    throw "Updater project not found: $updaterProject"
}

$tempRoot = Join-Path $repoRoot "artifacts\_package-temp"
$launcherPublishDir = Join-Path $tempRoot "launcher"
$updaterPublishDir = Join-Path $tempRoot "updater"

if ($Clean -and (Test-Path $tempRoot)) {
    Remove-Item -Path $tempRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $launcherPublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $updaterPublishDir -Force | Out-Null

function Invoke-Publish {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$Output
    )

    & dotnet publish $Project `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:SkipUpdaterBuild=true `
        -p:EnableDebugTelemetry=false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $Output

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project"
    }
}

function Get-LauncherVersion {
    param([Parameter(Mandatory = $true)][string]$ProjectFile)

    [xml]$xml = Get-Content -Path $ProjectFile
    foreach ($group in $xml.Project.PropertyGroup) {
        if ($group.Version -and -not [string]::IsNullOrWhiteSpace($group.Version)) {
            return $group.Version.Trim()
        }
    }

    throw "Could not find <Version> in $ProjectFile"
}

function Install-DepotDownloaderToTools {
    param([Parameter(Mandatory = $true)][string]$ToolsDir)

    $zipPath = Join-Path $tempRoot "DepotDownloader.zip"

    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    Write-Host "Downloading latest DepotDownloader..."
    Invoke-WebRequest -Uri $depotDownloaderZipUrl -OutFile $zipPath

    Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
    Remove-Item -Path $zipPath -Force

    $flatExe = Join-Path $ToolsDir "DepotDownloader.exe"
    $nestedExe = Join-Path $ToolsDir "DepotDownloader\DepotDownloader.exe"

    if (-not (Test-Path $flatExe) -and -not (Test-Path $nestedExe)) {
        throw "DepotDownloader.exe was not found after extraction."
    }
}

Write-Host "Publishing launcher..."
Invoke-Publish -Project $launcherProject -Output $launcherPublishDir

Write-Host "Publishing updater..."
Invoke-Publish -Project $updaterProject -Output $updaterPublishDir

$launcherExe = Join-Path $launcherPublishDir "SubnauticaLauncher.exe"
$updaterExe = Join-Path $updaterPublishDir "SNLUpdater.exe"
$toolsDir = Join-Path $launcherPublishDir "tools"

if (-not (Test-Path $launcherExe)) {
    throw "Missing published launcher executable: $launcherExe"
}

if (-not (Test-Path $updaterExe)) {
    throw "Missing published updater executable: $updaterExe"
}

if (-not (Test-Path $toolsDir)) {
    throw "Missing tools folder in publish output: $toolsDir"
}

$helperExeCount = (Get-ChildItem -Path $toolsDir -Filter "ExplosionResetHelper*.exe" -File -ErrorAction Stop).Count
if ($helperExeCount -eq 0) {
    throw "No ExplosionResetHelper executables found in published tools folder."
}

$version = Get-LauncherVersion -ProjectFile $launcherProject
$outputBase = Join-Path $repoRoot $OutputRoot
$packageName = "SubnauticaLauncher.v$version`_$RuntimeIdentifier"
$packageDir = Join-Path $outputBase $packageName
$zipPath = Join-Path $outputBase "$packageName.zip"

New-Item -ItemType Directory -Path $outputBase -Force | Out-Null

if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Copy-Item -Path $launcherExe -Destination (Join-Path $packageDir "SubnauticaLauncher.exe") -Force
Copy-Item -Path $updaterExe -Destination (Join-Path $packageDir "SNLUpdater.exe") -Force
Copy-Item -Path $toolsDir -Destination (Join-Path $packageDir "tools") -Recurse -Force

$packageToolsDir = Join-Path $packageDir "tools"
Install-DepotDownloaderToTools -ToolsDir $packageToolsDir

$runtimeDirs = @("data", "logs")
foreach ($dirName in $runtimeDirs) {
    $dir = Join-Path $packageDir $dirName
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Set-Content -Path (Join-Path $dir ".keep") -Value ""
}

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Release package created:"
Write-Host "  Folder: $packageDir"
Write-Host "  Zip:    $zipPath"
Write-Host "  SHA256: $zipHash"

