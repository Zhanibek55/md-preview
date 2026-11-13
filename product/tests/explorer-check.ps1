# Real-Explorer check: opens a new Explorer window with a Markdown file selected, waits for
# the preview handler to log that it rendered, saves a screenshot of the window, closes it.
# It does not change any Explorer setting: the preview pane must already be on (Alt+P).
param(
    [string]$File = (Join-Path $PSScriptRoot 'samples\diagrams.md'),
    [string]$Out = (Join-Path $PSScriptRoot 'out\explorer.png'),
    [int]$TimeoutSeconds = 25
)

$ErrorActionPreference = 'Stop'
$File = (Resolve-Path $File).Path
$log = Join-Path $env:USERPROFILE 'AppData\LocalLow\MdPreview\log.txt'
$logStart = if (Test-Path $log) { (Get-Item $log).Length } else { 0 }

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class W {
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
}
'@
[W]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

$shell = New-Object -ComObject Shell.Application
$before = @($shell.Windows() | ForEach-Object { $_.HWND })
Start-Process explorer.exe "/select,`"$File`""

$window = $null
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $window -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $window = $shell.Windows() | Where-Object { $before -notcontains $_.HWND -and $_.FullName -like '*explorer.exe' } | Select-Object -First 1
}
if (-not $window) { throw 'Explorer window did not open' }

$rendered = $false
while (-not $rendered -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $fs = [IO.FileStream]::new($log, 'Open', 'Read', 'ReadWrite, Delete')
    $fs.Seek($logStart, 'Begin') | Out-Null
    $text = [IO.StreamReader]::new($fs).ReadToEnd(); $fs.Dispose()
    $rendered = $text.Contains("rendered $File")
}
Start-Sleep -Milliseconds 800

$hwnd = [IntPtr]::new([long]$window.HWND)
[W]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400
$r = New-Object W+R
[W]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.Rt - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[W]::PrintWindow($hwnd, $dc, 2) | Out-Null   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($dc); $g.Dispose()
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

$window.Quit()
if ($rendered) { "RENDERED in Explorer: $Out" } else { "NOT RENDERED within $TimeoutSeconds s (is the preview pane on?): $Out" }
$text -split "`r?`n" | Where-Object { $_ } | ForEach-Object { "  log: $_" }
