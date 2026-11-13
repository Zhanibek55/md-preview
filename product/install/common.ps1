# Shared values for install.ps1 and uninstall.ps1.
$Clsid = '{D6EF0D24-13B8-4841-B4CE-B190114F4B51}'
$DisplayName = 'Markdown Preview (MdPreview)'
$PreviewHandlerShellEx = '{8895b1c6-b41f-4c1c-a562-0d564250836f}'
# Standard 64-bit surrogate (prevhost.exe) for preview handlers.
$PrevhostAppId = '{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}'
$Extensions = @('.md', '.markdown', '.mdown', '.mkd', '.mkdn')

function Stop-PreviewHosts {
    param([string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\MdPreview'))

    # prevhost.exe is Windows' shared preview-host surrogate. A host running our handler keeps the
    # install folder's DLLs locked, and because it runs at low integrity we cannot reliably read its
    # module list to tell it apart. prevhost is ephemeral (Explorer respawns it on the next preview),
    # so stop them all, plus any WebView2 child pointing at our folder.
    Get-Process prevhost -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        "Stopped prevhost.exe ($($_.Id))"
    }

    Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($Target) -or $_.CommandLine -match 'MdPreview') } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            "Stopped msedgewebview2.exe ($($_.ProcessId))"
        }
}

# Replace the install folder, retrying while preview hosts release their file handles.
function Copy-InstallFiles {
    param([string]$Source, [string]$Target)

    New-Item -ItemType Directory -Force $Target | Out-Null
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Copy-Item (Join-Path $Source '*') $Target -Recurse -Force -Exclude '*.pdb' -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 10) { throw }
            Stop-PreviewHosts -Target $Target | Out-Null
            Start-Sleep -Milliseconds 300
        }
    }
}
