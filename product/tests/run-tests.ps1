# Builds the handler and the harness, installs the handler for the current user, then
# renders every sample through prevhost.exe exactly as Explorer would and saves screenshots.
# A window appears on screen for a few seconds per sample: the screenshot is taken from it.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'out'),
    [switch]$SkipBuild,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$samples = Join-Path $PSScriptRoot 'samples'

if (-not $SkipBuild) {
    dotnet build (Join-Path $product 'src\MdPreview\MdPreview.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'handler build failed' }
    dotnet build (Join-Path $PSScriptRoot 'Harness\Harness.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'harness build failed' }
    dotnet build (Join-Path $PSScriptRoot 'ClickTest\ClickTest.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'click-test build failed' }
    dotnet build (Join-Path $product 'tools\Warmup\Warmup.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'warm-up build failed' }
}

if (-not $SkipInstall) {
    & (Join-Path $product 'install\install.ps1')
}

# The warm-up has written the V8 code cache to disk, so retiring its leftover preview host now is
# safe and keeps the first timed render below from reusing a host that is still tearing down.
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Run the real app.js in a WebView2: in-document links, find-in-page, and the outline.
$clickExe = Join-Path $PSScriptRoot 'ClickTest\bin\Release\MdPreviewClickTest.exe'
Write-Host '--- Links + find + outline (toc.md)'
& $clickExe (Join-Path $samples 'toc.md') --find 'наполнитель'
$clickFailed = $LASTEXITCODE -ne 0
Write-Host '--- Find + outline (rich.md)'
& $clickExe (Join-Path $samples 'rich.md') --find 'preview'
if ($LASTEXITCODE -ne 0) { $clickFailed = $true }

New-Item -ItemType Directory -Force $OutDir | Out-Null

# A big generated file to measure speed on long documents (~1.5 MB).
$big = Join-Path $OutDir 'big.md'
$sb = [System.Text.StringBuilder]::new()
for ($i = 1; $i -le 3000; $i++) {
    [void]$sb.AppendLine("## Section $i").AppendLine().AppendLine("Paragraph $i with **bold** text, a [link](https://example.com/$i) and ``code``. " * 3).AppendLine()
    if ($i % 50 -eq 0) { [void]$sb.AppendLine('```js').AppendLine("const value$i = compute($i);").AppendLine('```').AppendLine() }
    if ($i % 100 -eq 0) { [void]$sb.AppendLine('| a | b |').AppendLine('|---|---|').AppendLine("| $i | $($i * 2) |").AppendLine() }
}
[IO.File]::WriteAllText($big, $sb.ToString(), [Text.UTF8Encoding]::new($false))

$harness = Join-Path $PSScriptRoot 'Harness\bin\Release\MdPreviewHarness.exe'
$cases = @(
    # The first case also previews more files with the same handler, like Explorer when you
    # move the selection; it measures the warm (already started) path.
    @{ File = (Join-Path $samples 'features.md');       Theme = 'light'; Then = @((Join-Path $samples 'diagrams.md'), (Join-Path $samples 'SKILL.md')) },
    @{ File = (Join-Path $samples 'features.md');       Theme = 'dark' },
    @{ File = (Join-Path $samples 'diagrams.md');       Theme = 'dark' },
    @{ File = (Join-Path $samples 'SKILL.md');          Theme = 'dark' },
    @{ File = (Join-Path $samples 'hostile.md');        Theme = 'light' },
    @{ File = (Join-Path $samples 'broken-diagram.md'); Theme = 'light' },
    @{ File = $big;                                     Theme = 'light' }
)

$results = foreach ($case in $cases) {
    $name = [IO.Path]::GetFileNameWithoutExtension($case.File) + '-' + $case.Theme
    $png = Join-Path $OutDir "$name.png"
    $extra = @()
    foreach ($t in @($case.Then | Where-Object { $_ })) { $extra += @('--then', $t) }
    $output = & $harness $case.File $png --theme $case.Theme @extra 2>&1
    $code = $LASTEXITCODE
    "=== $name (exit $code)"
    $output
    [pscustomobject]@{ Case = $name; Exit = $code; Screenshot = $png }
}

$results | Where-Object { $_ -is [string] }
$table = $results | Where-Object { $_ -isnot [string] }
$table | Format-Table -AutoSize | Out-String -Width 200
$renderFailed = [bool]($table | Where-Object Exit -ne 0)
if ($renderFailed) { Write-Host 'Some renders failed.' }
if ($clickFailed) { Write-Host 'Link-navigation test failed.' }
if ($renderFailed -or $clickFailed) { exit 1 }
