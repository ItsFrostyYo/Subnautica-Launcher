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
$packageDataRoot = Join-Path $packageRoot "Data"
$releaseDataRoot = Join-Path $releaseRoot "Data"
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

function Get-LatestManifestVersion([string]$manifestPath) {
    [xml]$document = Get-Content -LiteralPath $manifestPath
    $latest = @($document.updates.update) |
        Sort-Object { [version]$_.version } -Descending |
        Select-Object -First 1
    if ($null -eq $latest) { throw "No update entries were found in $manifestPath." }
    return [version]$latest.version
}

function Copy-ManifestVerified([string]$sourcePath, [string]$destinationDirectory) {
    $destinationPath = Join-Path $destinationDirectory (Split-Path -Leaf $sourcePath)
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    $destinationHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "The release manifest was not replaced correctly: $destinationPath"
    }
}

function New-IntegrityManifest([string]$packageDirectory, [string]$outputPath, [string]$version) {
    $outputFullPath = [System.IO.Path]::GetFullPath($outputPath)
    $packagePrefix = [System.IO.Path]::GetFullPath($packageDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $files = @(
        Get-ChildItem -LiteralPath $packageDirectory -File -Recurse |
            Where-Object { [System.IO.Path]::GetFullPath($_.FullName) -ne $outputFullPath } |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    path = [System.IO.Path]::GetFullPath($_.FullName).Substring($packagePrefix.Length).Replace('\', '/')
                    size = $_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                }
            }
    )
    $manifest = [ordered]@{
        version = $version
        files = $files
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($outputPath, $json, [System.Text.UTF8Encoding]::new($false))
}

Remove-RepoDirectory $publishRoot
Remove-RepoDirectory $packageRoot
Remove-RepoDirectory $releaseRoot
New-Item -ItemType Directory -Path $launcherPublishRoot, $updaterPublishRoot, $packageRoot, $packageDataRoot, $releaseRoot, $releaseDataRoot -Force | Out-Null

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
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $releaseRoot -Force
$dependencyArchive = Join-Path $releaseRoot "Dependencies.zip"
Compress-Archive -Path (Join-Path $releaseRoot "Dependencies\*") -DestinationPath $dependencyArchive -CompressionLevel Optimal -Force

$launcherVersion = [version](Get-Item -LiteralPath (Join-Path $releaseRoot "SubnauticaLauncher.exe")).VersionInfo.FileVersion
$updaterVersion = [version](Get-Item -LiteralPath (Join-Path $releaseRoot "SNLUpdater.exe")).VersionInfo.FileVersion
$launcherManifestVersion = Get-LatestManifestVersion $launcherManifest
$updaterManifestVersion = Get-LatestManifestVersion $updaterManifest
if ($launcherManifestVersion -ne $launcherVersion) {
    throw "The latest SNLUpdates.xml version ($launcherManifestVersion) does not match SubnauticaLauncher.exe ($launcherVersion). Add or update the launcher manifest entry before packaging."
}
if ($updaterManifestVersion -ne $updaterVersion) {
    throw "The latest SNLUUpdates.xml version ($updaterManifestVersion) does not match SNLUpdater.exe ($updaterVersion). Add or update the updater manifest entry before packaging."
}

Set-ManifestFiles $launcherManifest @("SubnauticaLauncher.exe", "Dependencies.zip", "Data/IntegrityManifest.json", "Data/SNLUpdates.xml", "Data/SNLUUpdates.xml", "README.md")
Set-ManifestFiles $updaterManifest @("SNLUpdater.exe")
Copy-ManifestVerified $launcherManifest $releaseDataRoot
Copy-ManifestVerified $updaterManifest $releaseDataRoot
# Keep release-root feed mirrors so installed 4.0.3 clients can discover the
# one-time move to Data. Packaged 4.0.4 installations use only Data afterward.
Copy-ManifestVerified $launcherManifest $releaseRoot
Copy-ManifestVerified $updaterManifest $releaseRoot

Copy-Item -LiteralPath (Join-Path $releaseRoot "SubnauticaLauncher.exe"), (Join-Path $releaseRoot "SNLUpdater.exe") -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $releaseRoot "Dependencies") -Destination (Join-Path $packageRoot "Dependencies") -Recurse -Force
Copy-ManifestVerified $launcherManifest $packageDataRoot
Copy-ManifestVerified $updaterManifest $packageDataRoot
Copy-Item -LiteralPath (Join-Path $releaseRoot "README.md") -Destination $packageRoot -Force
New-Item -ItemType Directory -Path (Join-Path $packageRoot "Plugins"), (Join-Path $packageRoot "Logs") -Force | Out-Null

$integrityManifest = Join-Path $packageDataRoot "IntegrityManifest.json"
New-IntegrityManifest $packageRoot $integrityManifest $launcherVersion.ToString()
Copy-Item -LiteralPath $integrityManifest -Destination $releaseDataRoot -Force

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
