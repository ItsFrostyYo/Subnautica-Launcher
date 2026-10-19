[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SubnauticaLauncher.csproj"
$updaterProject = Join-Path $repoRoot "Updater\SubnauticaLauncherUpdater.csproj"
$artifactsRoot = Join-Path $repoRoot "Artifacts"
$publishRoot = Join-Path $artifactsRoot "Publish\Release"
$launcherPublishRoot = Join-Path $publishRoot "Launcher"
$updaterPublishRoot = Join-Path $publishRoot "Updater"
$packageRoot = Join-Path $artifactsRoot "Package"
$releaseRoot = Join-Path $repoRoot "Release"
$zipPath = Join-Path $releaseRoot "SubnauticaLauncher.zip"
$launcherManifest = Join-Path $repoRoot "Updates\SNLUpdates.xml"
$updaterManifest = Join-Path $repoRoot "Updates\SNLUUpdates.xml"

function Remove-RepoDirectory([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $resolved = [System.IO.Path]::GetFullPath($path)
    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    if (-not $resolved.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the repository: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Set-ManifestFiles([string]$manifestPath, [string[]]$relativePaths) {
    [xml]$document = Get-Content -LiteralPath $manifestPath
    $entries = @($document.updates.update)
    $latest = $entries |
        Sort-Object { [version]$_.version } -Descending |
        Select-Object -First 1
    if ($null -eq $latest) { throw "No update entries were found in $manifestPath." }
    foreach ($entry in $entries) {
        $filesNode = $entry.SelectSingleNode("files")
        if ([version]$entry.version -ne [version]$latest.version -and $null -ne $filesNode) {
            [void]$entry.RemoveChild($filesNode)
        }
    }
    $files = $latest.SelectSingleNode("files")
    if ($null -eq $files) {
        $files = $document.CreateElement("files")
        [void]$latest.AppendChild($files)
    } else {
        $files.RemoveAll()
    }
    foreach ($relativePath in $relativePaths | Sort-Object -Unique) {
        $file = $document.CreateElement("file")
        $file.SetAttribute("path", "Release/" + $relativePath.Replace('\', '/'))
        $file.SetAttribute("status", "changed")
        [void]$files.AppendChild($file)
    }
    $document.Save($manifestPath)
}

Remove-RepoDirectory $publishRoot
Remove-RepoDirectory $packageRoot
Remove-RepoDirectory $releaseRoot
New-Item -ItemType Directory -Path $launcherPublishRoot, $updaterPublishRoot, $packageRoot, $releaseRoot -Force | Out-Null

& dotnet publish $project --configuration Release --runtime $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded --output $launcherPublishRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "Build-NativeImGuiHost.ps1") -OutputDirectory $launcherPublishRoot

$dependenciesSource = Join-Path $repoRoot "Dependencies"
if (Test-Path -LiteralPath $dependenciesSource) {
    Get-ChildItem -LiteralPath $dependenciesSource -Force |
        Where-Object { $_.Name -ne "LiveSplit" } |
        Copy-Item -Destination (Join-Path $launcherPublishRoot "Dependencies") -Recurse -Force
}

& dotnet publish $updaterProject --configuration Release --runtime $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded --output $updaterPublishRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $launcherPublishRoot "SubnauticaLauncher.exe") -Destination $releaseRoot -Force
Copy-Item -LiteralPath (Join-Path $updaterPublishRoot "SNLUpdater.exe") -Destination $releaseRoot -Force
Copy-Item -LiteralPath (Join-Path $launcherPublishRoot "Dependencies") -Destination (Join-Path $releaseRoot "Dependencies") -Recurse -Force
$dependencyArchive = Join-Path $releaseRoot "Dependencies.zip"
Compress-Archive -Path (Join-Path $releaseRoot "Dependencies\*") -DestinationPath $dependencyArchive -CompressionLevel Optimal -Force

Set-ManifestFiles $launcherManifest @("SubnauticaLauncher.exe", "Dependencies.zip", "SNLUpdates.xml", "SNLUUpdates.xml")
Set-ManifestFiles $updaterManifest @("SNLUpdater.exe")
Copy-Item -LiteralPath $launcherManifest, $updaterManifest -Destination $releaseRoot -Force

Copy-Item -LiteralPath (Join-Path $releaseRoot "SubnauticaLauncher.exe"), (Join-Path $releaseRoot "SNLUpdater.exe") -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $releaseRoot "Dependencies") -Destination (Join-Path $packageRoot "Dependencies") -Recurse -Force
Copy-Item -LiteralPath $launcherManifest, $updaterManifest -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $packageRoot -Force
New-Item -ItemType Directory -Path (Join-Path $packageRoot "Data"), (Join-Path $packageRoot "Plugins"), (Join-Path $packageRoot "Logs") -Force | Out-Null

foreach ($manifestPath in @($launcherManifest, $updaterManifest)) {
    [xml]$manifest = Get-Content -LiteralPath $manifestPath
    $latest = $manifest.updates.update | Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
    foreach ($file in $latest.files.file) {
        $payload = Join-Path $repoRoot $file.path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) {
            throw "The update manifest references a missing release payload: $($file.path)"
        }
    }
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force
Remove-RepoDirectory $publishRoot
Remove-RepoDirectory $packageRoot
Write-Host "Created release folder: $releaseRoot"
Write-Host "Created release package: $zipPath"
