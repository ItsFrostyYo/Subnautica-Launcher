[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot "External Research\dear-imgui"
$hostSource = Join-Path $repoRoot "NativeImGui\SubnauticaLauncherImGuiHost.cpp"
$fontSource = Join-Path $repoRoot "Assets\Subnautica\aller.regular.ttf"
$backgroundSource = Join-Path $repoRoot "Assets\Backgrounds"
$iconSource = Join-Path $repoRoot "Assets\Icons"
$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
$hostOutput = Join-Path $OutputDirectory "Dependencies"
$objectOutput = Join-Path ([System.IO.Path]::GetTempPath()) "SubnauticaLauncher-NativeImGui"

if (-not (Test-Path -LiteralPath $vcvars)) { throw "Visual C++ Build Tools were not found at $vcvars." }
if (-not (Test-Path -LiteralPath $sourceRoot)) { throw "Dear ImGui source was not found at $sourceRoot." }
New-Item -ItemType Directory -Path $hostOutput -Force | Out-Null
New-Item -ItemType Directory -Path $objectOutput -Force | Out-Null

$objects = @(
    $hostSource,
    (Join-Path $sourceRoot "imgui.cpp"),
    (Join-Path $sourceRoot "imgui_draw.cpp"),
    (Join-Path $sourceRoot "imgui_tables.cpp"),
    (Join-Path $sourceRoot "imgui_widgets.cpp"),
    (Join-Path $sourceRoot "backends\imgui_impl_win32.cpp"),
    (Join-Path $sourceRoot "backends\imgui_impl_dx11.cpp")
) | ForEach-Object { '"' + $_ + '"' }
$output = Join-Path $hostOutput "SubnauticaLauncher.ImGuiHost.exe"
$command = 'call "' + $vcvars + '" >nul && cl /nologo /std:c++20 /O2 /EHsc /I"' + $sourceRoot + '" /Fo"' + $objectOutput + '\\" ' + ($objects -join ' ') + ' /Fe:"' + $output + '" /link /SUBSYSTEM:WINDOWS d3d11.lib dxgi.lib dwmapi.lib shell32.lib ole32.lib crypt32.lib user32.lib windowscodecs.lib'
cmd.exe /c $command
if ($LASTEXITCODE -ne 0) { throw "Native Dear ImGui host build failed." }

# The host is the only runtime artifact. Keep compiler intermediates out of the launcher folder.
Get-ChildItem -LiteralPath $hostOutput -Filter '*.obj' -File -ErrorAction SilentlyContinue | Remove-Item -Force
$assetsOutput = Join-Path $hostOutput 'Assets'
New-Item -ItemType Directory -Path $assetsOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $assetsOutput 'Fonts') -Force | Out-Null
Copy-Item -LiteralPath $fontSource -Destination (Join-Path $assetsOutput 'Fonts\aller.regular.ttf') -Force
Copy-Item -LiteralPath $backgroundSource -Destination (Join-Path $assetsOutput 'Backgrounds') -Recurse -Force
Copy-Item -LiteralPath $iconSource -Destination (Join-Path $assetsOutput 'Icons') -Recurse -Force
