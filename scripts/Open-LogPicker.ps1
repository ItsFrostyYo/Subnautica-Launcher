[CmdletBinding()]
param(
    [ValidateSet("launcher", "updater", "reset")]
    [string]$Log
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$logsRoot = Join-Path $workspaceRoot "bin\Debug\net8.0-windows\logs"

$logMap = [ordered]@{
    "1" = @{
        Key = "launcher"
        Name = "Launcher Log"
        Path = (Join-Path $logsRoot "launcher.log")
    }
    "2" = @{
        Key = "updater"
        Name = "Updater Log"
        Path = (Join-Path $logsRoot "updater.log")
    }
    "3" = @{
        Key = "reset"
        Name = "Reset Macro Log"
        Path = (Join-Path $logsRoot "reset-macro\subnautica-reset-macro.log")
    }
}

function Open-SelectedLog {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Entry
    )

    if (-not (Test-Path -LiteralPath $Entry.Path)) {
        Write-Warning ("Log file not found: {0}" -f $Entry.Path)
        Write-Host "Tip: Run the launcher/updater/macro once so it creates the log."
        return
    }

    Start-Process -FilePath "notepad.exe" -ArgumentList @($Entry.Path)
    Write-Host ("Opened {0}: {1}" -f $Entry.Name, $Entry.Path)
}

if ($PSBoundParameters.ContainsKey("Log")) {
    $entry = $logMap.Values | Where-Object { $_.Key -eq $Log } | Select-Object -First 1
    if ($null -eq $entry) {
        Write-Error ("Unknown log key: {0}" -f $Log)
        exit 1
    }

    Open-SelectedLog -Entry $entry
    exit 0
}

Write-Host ""
Write-Host "Select which log to open:"
Write-Host ("1) Launcher Log ({0})" -f $logMap["1"].Path)
Write-Host ("2) Updater Log ({0})" -f $logMap["2"].Path)
Write-Host ("3) Reset Macro Log ({0})" -f $logMap["3"].Path)
Write-Host "4) Cancel"

$choice = Read-Host "Enter 1-4"

switch ($choice) {
    "1" { Open-SelectedLog -Entry $logMap["1"] }
    "2" { Open-SelectedLog -Entry $logMap["2"] }
    "3" { Open-SelectedLog -Entry $logMap["3"] }
    default { Write-Host "Cancelled." }
}
