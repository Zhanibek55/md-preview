# Installs the Markdown preview handler for the current user only (no admin rights).
# Copies the binaries to %LOCALAPPDATA%\Programs\MdPreview and registers them under HKCU.
# Works both from the dev tree and from a packaged folder (binaries in .\app next to this script).
param(
    [string]$Source,
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\MdPreview'),
    [switch]$SkipWarmup
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $Source) {
    $packaged = Join-Path $PSScriptRoot 'app'
    $dev = Join-Path $PSScriptRoot '..\src\MdPreview\bin\Release'
    $Source = if (Test-Path (Join-Path $packaged 'MdPreview.dll')) { $packaged } else { $dev }
}
$Source = (Resolve-Path $Source).Path
if (-not (Test-Path (Join-Path $Source 'MdPreview.dll'))) { throw "MdPreview.dll not found in $Source. Build first." }

# The handler needs the Microsoft Edge WebView2 Runtime. It ships with Windows 11 and most Windows 10
# (via Edge); if it is missing, install it per-user (no admin) from the bundled bootstrapper.
function Get-WebView2Version {
    foreach ($p in @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')) {
        $v = (Get-ItemProperty $p -ErrorAction SilentlyContinue).pv
        if ($v) { return $v }
    }
    return $null
}

$wv = Get-WebView2Version
if ($wv) {
    "WebView2 Runtime: $wv"
}
else {
    $bootstrap = Join-Path $PSScriptRoot 'MicrosoftEdgeWebView2Setup.exe'
    if (Test-Path $bootstrap) {
        'WebView2 Runtime missing - installing per-user (no admin)...'
        & $bootstrap '/silent' '/install' | Out-Null
        Start-Sleep -Seconds 2
        $wv = Get-WebView2Version
        "WebView2 Runtime: $(if ($wv) { $wv } else { 'install may need a moment; re-run if the preview is blank' })"
    }
    else {
        Write-Warning 'WebView2 Runtime not found. Install it (no admin) from https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen Bootstrapper), then re-run.'
    }
}

Stop-PreviewHosts -Target $Target
Copy-InstallFiles -Source $Source -Target $Target

$dll = Join-Path $Target 'MdPreview.dll'
$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
# Same form RegAsm writes: unescaped. The CLR does not decode %20, so paths with spaces break with AbsoluteUri.
$codeBase = 'file:///' + ($dll -replace '\\', '/')

$clsidKey = "HKCU:\Software\Classes\CLSID\$Clsid"
New-Item -Path $clsidKey -Force | Out-Null
Set-Item -Path $clsidKey -Value $DisplayName
New-ItemProperty -Path $clsidKey -Name 'DisplayName' -Value $DisplayName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $clsidKey -Name 'AppID' -Value $PrevhostAppId -PropertyType String -Force | Out-Null
# Asks for a normal-integrity prevhost. Explorer ignores this for per-user (HKCU) registrations and
# runs the handler at low integrity anyway; the handler keeps its data in LocalLow so it works both ways.
New-ItemProperty -Path $clsidKey -Name 'DisableLowILProcessIsolation' -Value 1 -PropertyType DWord -Force | Out-Null

foreach ($inproc in @("$clsidKey\InprocServer32", "$clsidKey\InprocServer32\$($assemblyName.Version)")) {
    New-Item -Path $inproc -Force | Out-Null
    if ($inproc -eq "$clsidKey\InprocServer32") {
        Set-Item -Path $inproc -Value 'mscoree.dll'
        New-ItemProperty -Path $inproc -Name 'ThreadingModel' -Value 'Apartment' -PropertyType String -Force | Out-Null
    }
    New-ItemProperty -Path $inproc -Name 'Class' -Value 'MdPreview.MarkdownPreviewHandler' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $inproc -Name 'Assembly' -Value $assemblyName.FullName -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $inproc -Name 'RuntimeVersion' -Value 'v4.0.30319' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $inproc -Name 'CodeBase' -Value $codeBase -PropertyType String -Force | Out-Null
}

foreach ($ext in $Extensions) {
    $shellex = "HKCU:\Software\Classes\$ext\shellex\$PreviewHandlerShellEx"
    New-Item -Path $shellex -Force | Out-Null
    Set-Item -Path $shellex -Value $Clsid
}

$handlers = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers'
New-Item -Path $handlers -Force | Out-Null
New-ItemProperty -Path $handlers -Name $Clsid -Value $DisplayName -PropertyType String -Force | Out-Null

"Installed to $Target"
"Registered for: $($Extensions -join ', ')"

# Seed the WebView2 code cache so the first real diagram renders fast (not ~9 s).
if (-not $SkipWarmup) {
    $warmup = @(
        (Join-Path $Target 'MdPreviewWarmup.exe'),
        (Join-Path $PSScriptRoot '..\tools\Warmup\bin\Release\MdPreviewWarmup.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($warmup) {
        Stop-PreviewHosts -Target $Target | Out-Null
        & $warmup | Out-Null
        $warmOk = $LASTEXITCODE -eq 0
        "Warm-up: $(if ($warmOk) { 'done (diagram cache seeded)' } else { 'skipped/timed out' })"
    }
    else {
        "Warm-up exe not found; first diagram will be slow once. Build tools/Warmup to enable it."
    }
}
