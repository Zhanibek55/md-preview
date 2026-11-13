# Installs the Markdown preview handler for the current user only (no admin rights).
# Copies the build to %LOCALAPPDATA%\Programs\MdPreview and registers it under HKCU.
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\MdPreview\bin\Release'),
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\MdPreview'),
    [switch]$SkipWarmup
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$Source = (Resolve-Path $Source).Path
if (-not (Test-Path (Join-Path $Source 'MdPreview.dll'))) { throw "MdPreview.dll not found in $Source. Build first." }

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
