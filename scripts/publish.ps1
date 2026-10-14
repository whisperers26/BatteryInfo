<#
.SYNOPSIS
    Publishes the BatteryInfo tray app to a per-user folder (default %LOCALAPPDATA%\Programs\BatteryInfo).
.DESCRIPTION
    Stops a running copy published to the same folder, publishes a framework-dependent Release build
    (needs the .NET 10 Desktop Runtime), and optionally starts it.
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\BatteryInfo'),
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\BatteryInfo.App\BatteryInfo.App.csproj'
$exe = Join-Path $Destination 'BatteryInfo.exe'

Get-Process -Name BatteryInfo -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }

dotnet publish $project -c Release -o $Destination --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Write-Host "Published to $Destination"

if ($Start) {
    Start-Process -FilePath $exe -ArgumentList '--tray'
    Write-Host 'Started BatteryInfo in the tray.'
}
