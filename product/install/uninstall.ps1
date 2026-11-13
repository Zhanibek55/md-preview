# Removes the per-user registration and the installed files.
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\MdPreview')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

foreach ($ext in $Extensions) {
    $shellex = "HKCU:\Software\Classes\$ext\shellex\$PreviewHandlerShellEx"
    if ((Test-Path $shellex) -and ((Get-Item $shellex).GetValue('') -eq $Clsid)) {
        Remove-Item $shellex -Force
    }
}

$handlers = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers'
if (Test-Path $handlers) { Remove-ItemProperty -Path $handlers -Name $Clsid -ErrorAction SilentlyContinue }

$clsidKey = "HKCU:\Software\Classes\CLSID\$Clsid"
if (Test-Path $clsidKey) { Remove-Item $clsidKey -Recurse -Force }

Stop-PreviewHosts
if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
"Uninstalled"
