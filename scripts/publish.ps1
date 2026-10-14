<#
.SYNOPSIS
    Publishes the BatteryInfo tray app to a per-user folder (default %LOCALAPPDATA%\Programs\BatteryInfo).
.DESCRIPTION
    Asks a running copy published to the same folder to close (it removes its tray icon; it is killed only if
    it does not close in time), publishes a framework-dependent Release build (needs the .NET 10 Desktop
    Runtime), and optionally starts it.
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\BatteryInfo'),
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\BatteryInfo.App\BatteryInfo.App.csproj'
$exe = Join-Path $Destination 'BatteryInfo.exe'

# A killed copy cannot remove its tray icon, so ask it to close through its exit event first. Kill it only when it
# has no such event (a build from before it existed) or does not close in time: its exit can take a few seconds
# while it waits for running device reads.
Get-Process -Name BatteryInfo -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    ForEach-Object {
        $exitRequest = $null
        if ([System.Threading.EventWaitHandle]::TryOpenExisting("Local\BatteryInfo.Exit.$($_.Id)", [ref]$exitRequest)) {
            $exitRequest.Set() | Out-Null
            $exitRequest.Dispose()
            if ($_.WaitForExit(10000)) { return }
        }
        $_.Kill()
        $_.WaitForExit(5000) | Out-Null
    }

dotnet publish $project -c Release -o $Destination --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Write-Host "Published to $Destination"

if ($Start) {
    Start-Process -FilePath $exe -ArgumentList '--tray'
    Write-Host 'Started BatteryInfo in the tray.'
}
