# Third-party components

MdPreview itself is under the MIT License (see ../LICENSE). It bundles the components below under
their own licenses. The Microsoft Edge WebView2 Runtime is a Microsoft component and is not part of
this repository.

| Component | Version | License | Where |
|---|---|---|---|
| Markdig | 1.5.1 | BSD-2-Clause | NuGet, `Markdig.dll` |
| Microsoft Edge WebView2 SDK | 1.0.4314.35 | Microsoft WebView2 SDK license (redistributable) | NuGet, `Microsoft.Web.WebView2.*.dll`, `WebView2Loader.dll` |
| System.Memory, System.Buffers, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe | via Markdig | MIT | NuGet |
| Mermaid | 12.2.0 | MIT | npm, `assets/vendor/mermaid` |
| KaTeX | 0.19.1 | MIT | npm, `assets/vendor/katex` (fonts: SIL OFL via KaTeX) |
| highlight.js (`@highlightjs/cdn-assets`) | 11.12.0 | BSD-3-Clause | npm, `assets/vendor/highlight` |

License texts of the npm packages are copied next to the files by `tools/fetch-vendor.ps1`.
The WebView2 Runtime itself is part of Windows 11 and is not shipped with the product.

Before a public release: re-check the KaTeX font license text and the WebView2 SDK redistribution terms.
