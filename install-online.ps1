# MdPreview — one-line installer.
# Downloads the latest GitHub release and installs the Markdown preview handler for the
# current user only (no administrator rights). Source & issues: https://github.com/Zhanibek55/md-preview (MIT)
#
# Inspect this script before running it. Usage, in PowerShell:
#   irm https://raw.githubusercontent.com/Zhanibek55/md-preview/main/install-online.ps1 | iex
#
# To uninstall later: run Uninstall.cmd from the unzipped package, or
#   & "$env:LOCALAPPDATA\Programs\MdPreview\uninstall.ps1"   (if present)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

$repo = 'Zhanibek55/md-preview'
$work = Join-Path $env:TEMP ('MdPreview-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    Write-Host 'MdPreview: finding the latest release...'
    $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'MdPreview-installer' }
    $asset = $rel.assets | Where-Object { $_.name -like 'MdPreview-*.zip' } | Select-Object -First 1
    if (-not $asset) { throw 'No MdPreview-*.zip asset found in the latest release.' }

    $zip = Join-Path $work $asset.name
    Write-Host ("MdPreview: downloading {0} ({1} MB)..." -f $asset.name, [math]::Round($asset.size / 1MB, 1))
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing

    Write-Host 'MdPreview: extracting...'
    Expand-Archive -Path $zip -DestinationPath $work -Force

    $installer = Get-ChildItem -Path $work -Recurse -Filter 'install.ps1' | Select-Object -First 1
    if (-not $installer) { throw 'install.ps1 was not found inside the package.' }

    Write-Host 'MdPreview: installing (per-user, no admin)...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer.FullName
    if ($LASTEXITCODE) { throw "The installer exited with code $LASTEXITCODE." }

    Write-Host ''
    Write-Host 'MdPreview installed. In Explorer press Alt+P, then select a .md file.'
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
