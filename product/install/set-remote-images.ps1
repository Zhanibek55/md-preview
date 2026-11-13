# Toggles whether the Markdown preview loads images from the internet.
# Off by default: remote images are blocked and shown as a small placeholder, so previewing a file
# never silently fetches from outside hosts (which would leak your IP). Turn on only if you want them.
#
#   ./set-remote-images.ps1 -On
#   ./set-remote-images.ps1 -Off
#   ./set-remote-images.ps1          # shows the current value
param(
    [switch]$On,
    [switch]$Off
)

$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Software\MdPreview'
New-Item -Path $key -Force | Out-Null

if ($On -and $Off) { throw 'Pass only one of -On / -Off.' }

if ($On -or $Off) {
    Set-ItemProperty -Path $key -Name 'AllowRemoteImages' -Value ([int]$On.IsPresent) -Type DWord
    # Preview hosts cache the setting for the current file; retire them so the change takes effect.
    Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

$val = (Get-ItemProperty -Path $key -Name 'AllowRemoteImages' -ErrorAction SilentlyContinue).AllowRemoteImages
"Remote images: $(if ($val) { 'ON (previews load internet images)' } else { 'OFF (blocked, shown as a placeholder)' })"
