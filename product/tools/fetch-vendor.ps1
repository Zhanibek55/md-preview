# Downloads pinned browser libraries from the npm registry and copies the files the
# preview needs into src/MdPreview/assets/vendor. Nothing from the packages is executed
# here: `npm pack` only downloads the tarball, it does not run install scripts.
param(
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) ("mdpreview-vendor-" + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'

$packages = [ordered]@{
    'mermaid'                 = '12.2.0'
    'katex'                   = '0.19.1'
    '@highlightjs/cdn-assets' = '11.12.0'
}

$product = Split-Path $PSScriptRoot -Parent
$vendor = Join-Path $product 'src\MdPreview\assets\vendor'

New-Item -ItemType Directory -Force $WorkDir | Out-Null
$extracted = @{}
foreach ($name in $packages.Keys) {
    $version = $packages[$name]
    $dir = Join-Path $WorkDir (($name -replace '[@/]', '_') + "-$version")
    New-Item -ItemType Directory -Force $dir | Out-Null
    $tgz = (npm pack "$name@$version" --pack-destination $dir --silent --ignore-scripts) | Select-Object -Last 1
    tar -xzf (Join-Path $dir $tgz) -C $dir
    $extracted[$name] = Join-Path $dir 'package'
}

if (Test-Path $vendor) { Remove-Item $vendor -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $vendor 'mermaid'), (Join-Path $vendor 'katex\fonts'), (Join-Path $vendor 'highlight') | Out-Null

Copy-Item (Join-Path $extracted['mermaid'] 'dist\mermaid.min.js') (Join-Path $vendor 'mermaid')
Copy-Item (Join-Path $extracted['mermaid'] 'LICENSE') (Join-Path $vendor 'mermaid\LICENSE')

Copy-Item (Join-Path $extracted['katex'] 'dist\katex.min.js') (Join-Path $vendor 'katex')
Copy-Item (Join-Path $extracted['katex'] 'dist\katex.min.css') (Join-Path $vendor 'katex')
Copy-Item (Join-Path $extracted['katex'] 'dist\fonts\*.woff2') (Join-Path $vendor 'katex\fonts')
Copy-Item (Join-Path $extracted['katex'] 'LICENSE') (Join-Path $vendor 'katex\LICENSE')

$hl = $extracted['@highlightjs/cdn-assets']
Copy-Item (Join-Path $hl 'highlight.min.js') (Join-Path $vendor 'highlight')
Copy-Item (Join-Path $hl 'styles\github.min.css') (Join-Path $vendor 'highlight')
Copy-Item (Join-Path $hl 'styles\github-dark.min.css') (Join-Path $vendor 'highlight')
Copy-Item (Join-Path $hl 'LICENSE') (Join-Path $vendor 'highlight\LICENSE')

$packages | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $vendor 'versions.json')

Get-ChildItem $vendor -Recurse -File | Measure-Object Length -Sum | ForEach-Object {
    "vendor: {0} files, {1:N0} KB" -f $_.Count, ($_.Sum / 1KB)
}
