# Builds a self-contained, no-admin install package of the Markdown preview handler.
# Output: package/out/MdPreview-<version>.zip  — copy to another PC, unzip, run Install.cmd.
# The target needs only Windows 10/11 (.NET Framework 4.8 is built in) and the WebView2 Runtime
# (shipped with Windows 11 and most Windows 10; the package installs it per-user if missing).
param(
    [switch]$SkipBuild,
    [switch]$NoBootstrapper   # do not bundle the WebView2 installer
)

$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$src = Join-Path $product 'src\MdPreview'
$bin = Join-Path $src 'bin\Release'

function Step($m) { Write-Host "==> $m" }

if (-not $SkipBuild) {
    if (-not (Test-Path (Join-Path $src 'assets\vendor\mermaid\mermaid.min.js'))) {
        Step 'Fetching vendor libraries (Mermaid, KaTeX, highlight.js)'
        & (Join-Path $product 'tools\fetch-vendor.ps1')
    }
    Step 'Building MdPreview (Release)'
    dotnet build (Join-Path $src 'MdPreview.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'MdPreview build failed' }
    Step 'Building Warmup (Release)'
    dotnet build (Join-Path $product 'tools\Warmup\Warmup.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'Warmup build failed' }
}

if (-not (Test-Path (Join-Path $bin 'assets\vendor\mermaid\mermaid.min.js'))) {
    throw "Vendor libraries missing from the build output ($bin\assets\vendor). Run tools/fetch-vendor.ps1 and rebuild."
}

$version = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $bin 'MdPreview.dll')).Version.ToString()
$stageRoot = Join-Path $PSScriptRoot 'staging'
$stage = Join-Path $stageRoot 'MdPreview'
$app = Join-Path $stage 'app'
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

Step 'Staging binaries'
# Handler + its dependencies + assets (incl. vendor) + native WebView2 loaders.
Copy-Item (Join-Path $bin '*') $app -Recurse -Force -Exclude '*.pdb', '*.xml'
# Warm-up tool (seeds the diagram cache during install) and its document.
$warmBin = Join-Path $product 'tools\Warmup\bin\Release'
Copy-Item (Join-Path $warmBin 'MdPreviewWarmup.exe') $app -Force
if (Test-Path (Join-Path $warmBin 'MdPreviewWarmup.exe.config')) { Copy-Item (Join-Path $warmBin 'MdPreviewWarmup.exe.config') $app -Force }
Copy-Item (Join-Path $warmBin 'warmup.md') $app -Force

Step 'Staging install scripts'
foreach ($f in 'install.ps1', 'uninstall.ps1', 'common.ps1', 'set-remote-images.ps1') {
    Copy-Item (Join-Path $product "install\$f") $stage -Force
}

# .cmd wrappers so a double-click works without touching the PowerShell execution policy.
@'
@echo off
rem Installs the Markdown preview for the current user only (no administrator rights needed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
'@ | Set-Content -Encoding ASCII (Join-Path $stage 'Install.cmd')

@'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
pause
'@ | Set-Content -Encoding ASCII (Join-Path $stage 'Uninstall.cmd')

@"
Markdown preview for Windows Explorer — portable install (no administrator rights)
=================================================================================

What it does
  Shows .md files rendered (tables, code, Mermaid, math, emoji, outline, Ctrl+F)
  in the Explorer preview pane (turn it on with Alt+P), and in classic Outlook's
  attachment preview. Everything stays on your PC; the page never goes online.

Install
  1. Unzip this folder anywhere (e.g. your Desktop or Documents).
  2. Double-click  Install.cmd
     (or, in PowerShell:  powershell -ExecutionPolicy Bypass -File install.ps1)
  It installs to  %LOCALAPPDATA%\Programs\MdPreview  and registers under HKCU.
  No administrator rights are used.

Use
  In Explorer, press Alt+P to show the preview pane, then select a .md file.
  First run seeds a cache (about 15 s, invisible); after that it is fast.

Remote images (off by default; previews never fetch from the internet)
  Turn on:   powershell -ExecutionPolicy Bypass -File set-remote-images.ps1 -On
  Turn off:  powershell -ExecutionPolicy Bypass -File set-remote-images.ps1 -Off

Uninstall
  Double-click  Uninstall.cmd

Requirements
  Windows 10 (1903+) or 11. .NET Framework 4.8 is built in. The Microsoft Edge
  WebView2 Runtime is needed; it ships with Windows 11 and most Windows 10. If it
  is missing, the installer sets it up per-user automatically when the bundled
  MicrosoftEdgeWebView2Setup.exe is present.

Version: $version
"@ | Set-Content -Encoding UTF8 (Join-Path $stage 'README-INSTALL.txt')

if (-not $NoBootstrapper) {
    Step 'Bundling the WebView2 per-user installer (best effort)'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' `
            -OutFile (Join-Path $stage 'MicrosoftEdgeWebView2Setup.exe') -UseBasicParsing
        Write-Host '    bundled MicrosoftEdgeWebView2Setup.exe'
    }
    catch {
        Write-Warning "    could not download the WebView2 bootstrapper ($($_.Exception.Message)). The package still works where WebView2 is already present."
    }
}

Step 'Zipping'
$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$zip = Join-Path $outDir "MdPreview-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Step "Package ready: $zip ($sizeMb MB)"
Get-ChildItem $stage -Recurse -File | Measure-Object | ForEach-Object { "    $($_.Count) files staged" }
