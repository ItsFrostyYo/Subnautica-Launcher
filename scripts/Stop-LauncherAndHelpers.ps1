[CmdletBinding()]
param(
    [switch]$Launcher,
    [switch]$Helpers,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Stop-TargetProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $processes = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if (-not $processes) {
        Write-Host "Not running: $Name"
        return
    }

    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            Write-Host ("Stopped: {0} (PID {1})" -f $Name, $process.Id)
        }
        catch {
            Write-Warning ("Failed to stop {0} (PID {1}): {2}" -f $Name, $process.Id, $_.Exception.Message)
        }
    }
}

$stopLauncher = $Launcher.IsPresent
$stopHelpers = $Helpers.IsPresent

if ($All.IsPresent) {
    $stopLauncher = $true
    $stopHelpers = $true
}

if (-not $stopLauncher -and -not $stopHelpers) {
    Write-Host ""
    Write-Host "Select what to stop:"
    Write-Host "1) Launcher only"
    Write-Host "2) Helpers only"
    Write-Host "3) Launcher + Helpers"
    Write-Host "4) Cancel"
    $choice = Read-Host "Enter 1-4"

    switch ($choice) {
        "1" { $stopLauncher = $true }
        "2" { $stopHelpers = $true }
        "3" { $stopLauncher = $true; $stopHelpers = $true }
        default {
            Write-Host "Cancelled."
            exit 0
        }
    }
}

if ($stopLauncher) {
    Stop-TargetProcess -Name "SubnauticaLauncher"
    Stop-TargetProcess -Name "SNLUpdater"
}

if ($stopHelpers) {
    Stop-TargetProcess -Name "ExplosionResetHelper2018"
    Stop-TargetProcess -Name "ExplosionResetHelper2022"
    Stop-TargetProcess -Name "DepotDownloader"
}

Write-Host "Done."
