[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$RuntimeIdentifier = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SubnauticaLauncher.csproj"
$outputPath = Join-Path $repoRoot "Artifacts\Build\$Configuration\$RuntimeIdentifier"
$updaterProject = Join-Path $repoRoot "Updater\SubnauticaLauncherUpdater.csproj"

if (Test-Path -LiteralPath $outputPath) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($outputPath)
    $resolvedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'Artifacts\Build'))
    if (-not $resolvedOutput.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an output path outside Artifacts\\Build: $resolvedOutput"
    }
    # Data and logs belong to the user. Rebuild only replaces generated runtime files.
    foreach ($generatedPath in @('SubnauticaLauncher.exe', 'SNLUpdater.exe', 'Dependencies')) {
        $target = Join-Path $outputPath $generatedPath
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
    }
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

& dotnet publish $project --configuration $Configuration --runtime $RuntimeIdentifier --self-contained true --output $outputPath -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot "Build-NativeImGuiHost.ps1") -OutputDirectory $outputPath

$dependenciesSource = Join-Path $repoRoot 'Dependencies'
if (Test-Path -LiteralPath $dependenciesSource) {
    Get-ChildItem -LiteralPath $dependenciesSource -Force |
        Copy-Item -Destination (Join-Path $outputPath 'Dependencies') -Recurse -Force
}

# User-owned state, plugins, and logs deliberately survive rebuilds.
New-Item -ItemType Directory -Path (Join-Path $outputPath 'Data'), (Join-Path $outputPath 'Plugins'), (Join-Path $outputPath 'Logs') -Force | Out-Null

& dotnet publish $updaterProject --configuration $Configuration --runtime $RuntimeIdentifier --self-contained true --output $outputPath -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Created single-file launcher: $(Join-Path $outputPath 'SubnauticaLauncher.exe')"
