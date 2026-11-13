# MdPreview — Markdown preview for Windows Explorer (prototype)

Select a `.md` file with the preview pane on (Alt+P) and Explorer shows it rendered: tables, task lists,
GitHub alerts, code highlighting (including auto-detect for code blocks with no language tag), Mermaid
diagrams, KaTeX math, emoji shortcodes (`:rocket:` → 🚀), local images, front matter of agent and skill
files, working in-document links, a floating **outline** of the headings with scroll-spy, **find in page**
(Ctrl+F), and light/dark theme. No PowerToys needed. By default the page never touches the network;
remote images are blocked and shown as a placeholder (toggle with `install/set-remote-images.ps1`).

Status: working prototype, installed for the current user on the dev laptop. Not packaged, not signed,
not published.

## Build and install (current user, no admin)

```powershell
cd projects/md-preview/product
./tools/fetch-vendor.ps1                          # once: Mermaid, KaTeX, highlight.js (pinned)
dotnet build src/MdPreview/MdPreview.csproj -c Release
dotnet build tools/Warmup/Warmup.csproj -c Release   # optional: enables the install cache warm-up
./install/install.ps1                             # copies to %LOCALAPPDATA%\Programs\MdPreview, registers in HKCU, warms the cache
./install/uninstall.ps1                           # removes everything
```

Extensions: `.md .markdown .mdown .mkd .mkdn`. Pass `-SkipWarmup` to `install.ps1` to skip the
cache warm-up (the first diagram is then slow once).

### Portable package for another PC (no admin, no dev tools)

`package/build-package.ps1` produces a self-contained zip you copy to another machine — no .NET SDK,
no Node, no administrator rights on the target.

```powershell
cd projects/md-preview/product
./package/build-package.ps1        # -> package/out/MdPreview-<version>.zip  (~4.4 MB)
```

On the target PC: unzip anywhere, double-click **Install.cmd** (a `.cmd` wrapper so the PowerShell
execution policy is not in the way), then press Alt+P in Explorer and select a `.md` file.
`Uninstall.cmd` removes it. The package carries the prebuilt binaries, the bundled libraries, the
warm-up tool, and (best effort) the per-user WebView2 Runtime installer; the only target requirement
is Windows 10 (1903+) / 11, where .NET Framework 4.8 is already present.

Caveat: a work PC whose **Group Policy** forces a PowerShell execution policy can block even the
`-ExecutionPolicy Bypass` wrapper. If `Install.cmd` is refused there, a batch + `.reg` installer (no
PowerShell) or a signed single-exe (Inno Setup, per-user) is the fallback — not built yet.

### Why not Rust / other libraries

The slow part was never the markdown parser (Markdig renders the PRD in 2.7 ms) or .NET — it was
Chromium start-up and the one-time compile of Mermaid's 5.5 MB. Rewriting the handler in Rust would
still use WebView2/Chromium and still compile Mermaid, so it buys nothing here; the fix is caching
(above). .NET Framework 4.8 also ships on every Windows 10/11, which keeps install dependency-free.

## Tests

```powershell
./tests/run-tests.ps1        # builds, installs, renders every sample through prevhost.exe + runs the link test
./tests/explorer-check.ps1   # opens a real Explorer window on a sample (preview pane must be on), screenshots it
```

A borderless window appears on screen for a few seconds per sample; the screenshot is taken from it.
The handler log is `%USERPROFILE%\AppData\LocalLow\MdPreview\log.txt`.

Samples: `features.md` (text, tables, code, alerts), `diagrams.md` (Mermaid, math, local image),
`SKILL.md` (front matter), `toc.md` (table of contents with in-document links), `hostile.md` (scripts,
remote images, iframes, redirects, path traversal), `broken-diagram.md` (Mermaid and math errors), plus
a generated 1.5 MB `big.md`.

The `ClickTest` tool runs the real page (the same `app.js`) in a WebView2 and clicks every in-document
link, checking the page scrolls to the matching heading. It is part of `run-tests.ps1`.

### Speed (i9-13900H, Windows 11, WebView2 157)

The cost is Chromium start-up and the one-time compile of the bundled scripts (Mermaid is ~5.5 MB).
Markdig parsing is not a factor: 2.7 ms for the 27 KB PRD, 65 ms for a 784 KB file. So the fix is
caching, not a faster parser:

- Bundled libraries are served with cacheable headers, so WebView2 persists their compiled V8
  bytecode to the profile's on-disk **Code Cache**. It survives preview-host **and browser restarts**
  (and reboots), so diagrams stay fast after the first time — not only while a browser process is warm.
- The install **warm-up** (`tools/Warmup`) renders a page with Mermaid + KaTeX + highlighted code
  twice, off-screen, which seeds that disk cache. Chromium writes a script's bytecode on its *second*
  execution, hence two passes. One-time, ~15 s during install, invisible.

| Check | Before | After (cacheable assets + install warm-up) |
|---|---|---|
| First diagram render, empty profile | 9.9 s | — (paid once at install, off-screen) |
| First real render after install | — | **0.86 s** |
| Diagram file, cold prevhost, warm disk cache | 9.9 s each time the browser had exited | **0.5–0.75 s**, survives browser exit/reboot |
| Text file, cold | ~0.8 s | ~0.5–0.85 s |
| Next file in the same pane (warm) | 0.25–0.7 s | 0.1–0.5 s |
| Disk code cache after warm-up | 3 KB (not persisted) | ~1.5 MB, survives browser exit |

| Correctness check | Result |
|---|---|
| 9 harness renders through prevhost.exe (7 cold, 2 warm) | all rendered |
| Real Explorer window, preview pane | rendered (prevhost runs at low integrity) |
| In-document links, `toc.md` and the real 27 KB PRD | every TOC link scrolled to its heading |
| Find in page (`toc.md` "наполнитель" ×147, `rich.md` "preview" ×4) | all matches highlighted via the CSS Highlight API |
| Outline panel (`toc.md`, `rich.md`) | built from headings; clicking an entry scrolls to it |
| `hostile.md` | no script ran, remote image and iframe blocked, auto-redirect blocked and not opened |

## How it works

- `MarkdownPreviewHandler` is a COM preview handler (`IPreviewHandler`, `IInitializeWithFile`), .NET Framework 4.8,
  loaded by Explorer into `prevhost.exe`.
- COM may call it on an MTA thread, so all UI runs on one dedicated STA thread (`UiThread`).
- `MarkdownRenderer` turns the file into HTML with Markdig. `PreviewControl` shows it in WebView2.
- Every request goes to the fake host `https://mdpreview.example/`, served from memory: the page, bundled
  assets, and images from the document's own folder only. A strict Content-Security-Policy blocks inline
  scripts, remote images, frames and forms. Links open in the default browser only on a user click.
- Bundled assets are served cacheable (page and document images stay `no-store`), which is what lets the
  compiled script bytecode persist to disk. `UiThread.Prewarm` also starts the WebView2 environment while
  the first file is still parsing, and Chromium boots with background services trimmed off.
- Explorer runs the handler at low integrity, so the log and the WebView2 profile live in `AppData\LocalLow`.
- `app.js` loads highlight.js, KaTeX and Mermaid only when the document needs them, and builds the outline,
  find bar and (when remote images are off) the image placeholders itself.
- Remote images are gated by `HKCU\Software\MdPreview\AllowRemoteImages` (off by default), which widens the
  page's `img-src` to `https:` when on. `install/set-remote-images.ps1 -On/-Off` flips it.

## Known gaps

- No thumbnails for `.md` files yet (`IThumbnailProvider`).
- No settings *window* yet — options live in the registry (`set-remote-images.ps1`); theme follows the system.
- Find in page needs the CSS Custom Highlight API (Chromium 105+); on an older WebView2 runtime it no-ops.
- Files downloaded from the internet (Mark of the Web) are not previewed by Explorer at all; Windows decides that.
- Inline `<style>` in a Markdown file is allowed (needed by Mermaid); it can only change looks.
- Packaging for Microsoft Store (MSIX or installer), code signing and a licence check are not done.
- Tested on one machine only.
