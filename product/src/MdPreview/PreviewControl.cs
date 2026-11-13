using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MdPreview
{
    /// <summary>
    /// Child window placed inside Explorer's preview pane. Hosts WebView2 and serves the
    /// rendered page, the bundled assets and the document's own images from memory.
    /// The page has no network access: every request goes to a fake host handled here.
    /// </summary>
    internal sealed class PreviewControl : UserControl
    {
        internal const string Host = "mdpreview.example";
        internal const string Origin = "https://" + Host;
        internal const string PagePath = "/__mdp_page__.html";
        internal const string AssetsPrefix = "/__mdp_assets__/";

        private const int MaxResourceBytes = 25 * 1024 * 1024;

        private static readonly string AssetsDir = Path.Combine(DependencyResolver.BaseDir, "assets");

        private static string ContentSecurityPolicy(bool allowRemoteImages)
        {
            string img = allowRemoteImages ? Origin + "/ data: https:" : Origin + "/ data:";
            return "default-src 'none'; " +
                "script-src " + Origin + AssetsPrefix + "; " +
                "style-src " + Origin + AssetsPrefix + " 'unsafe-inline'; " +
                "img-src " + img + "; " +
                "font-src " + Origin + AssetsPrefix + " data:; " +
                "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        }

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico", ".avif",
        };

        private static readonly HashSet<string> MenuItemsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "copy", "selectAll", "copyLinkLocation", "copyImage", "copyImageLocation",
        };

        [ThreadStatic]
        private static Task<CoreWebView2Environment> t_environment;

        private static int s_navigationCounter;

        private readonly IntPtr _parentHwnd;
        private readonly bool _dark;
        private readonly WebView2 _web;
        private readonly Label _message;

        private Task _initialization;
        private string _docPath;
        private string _docDir;
        private byte[] _pageBytes;
        private string _pageUrl;
        private Stopwatch _clock;
        private readonly bool _allowRemoteImages;

        public PreviewControl(IntPtr parentHwnd, bool dark, Color background)
        {
            _parentHwnd = parentHwnd;
            _dark = dark;
            _allowRemoteImages = Settings.AllowRemoteImages();
            BackColor = background;

            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = background,
            };

            _message = new Label
            {
                Dock = DockStyle.Fill,
                Visible = false,
                BackColor = background,
                ForeColor = dark ? Color.Gainsboro : Color.DimGray,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = SystemFonts.MessageBoxFont,
            };

            Controls.Add(_web);
            Controls.Add(_message);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Parent = _parentHwnd;
                cp.Style |= Native.WS_CHILD | Native.WS_CLIPCHILDREN | Native.WS_CLIPSIBLINGS;
                return cp;
            }
        }

        public void FocusContent()
        {
            try
            {
                _web.Focus();
            }
            catch
            {
            }
        }

        public async void Display(string path)
        {
            _clock = Stopwatch.StartNew();
            try
            {
                _docPath = Path.GetFullPath(path);
                _docDir = Path.GetDirectoryName(_docPath);
                _pageBytes = Encoding.UTF8.GetBytes(MarkdownRenderer.RenderFile(_docPath, _dark, BackColor, _allowRemoteImages));
                long parseMs = _clock.ElapsedMilliseconds;

                await EnsureWebViewAsync();
                if (IsDisposed || _web.CoreWebView2 == null)
                {
                    return;
                }

                _pageUrl = Origin + PagePath + "?v=" + Interlocked.Increment(ref s_navigationCounter);
                _web.CoreWebView2.Navigate(_pageUrl);
                Log.Info("navigate " + _docPath + " parse=" + parseMs + "ms ready=" + _clock.ElapsedMilliseconds + "ms");
            }
            catch (Exception ex)
            {
                Log.Error("Display " + path, ex);
                ShowMessage("Markdown preview is not available for this file.\r\n\r\n" + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _web.Dispose();
                }
                catch
                {
                }
            }

            base.Dispose(disposing);
        }

        private void ShowMessage(string text)
        {
            try
            {
                _message.Text = text;
                _message.Visible = true;
                _web.Visible = false;
            }
            catch
            {
            }
        }

        private Task EnsureWebViewAsync()
        {
            if (_initialization == null || _initialization.IsFaulted)
            {
                _initialization = InitializeWebViewAsync();
            }

            return _initialization;
        }

        private async Task InitializeWebViewAsync()
        {
            CoreWebView2Environment environment = await GetEnvironmentAsync();
            var controllerClock = Stopwatch.StartNew();
            await _web.EnsureCoreWebView2Async(environment);
            Log.Info("controller ready in " + controllerClock.ElapsedMilliseconds + "ms");

            CoreWebView2 core = _web.CoreWebView2;
            CoreWebView2Settings settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsWebMessageEnabled = true;
            settings.IsZoomControlEnabled = true;

            core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ContextMenuRequested += OnContextMenuRequested;
            core.NavigationCompleted += (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    Log.Error("navigation failed: " + e.WebErrorStatus + " " + _docPath);
                }
            };
        }

        /// <summary>
        /// Starts building the WebView2 environment before it is needed, so Chromium boots while
        /// the handler is still parsing. Safe to call more than once; the first call wins.
        /// </summary>
        internal static void Prewarm()
        {
            try { GetEnvironmentAsync(); } catch { }
        }

        private static Task<CoreWebView2Environment> GetEnvironmentAsync()
        {
            if (t_environment == null || t_environment.IsFaulted || t_environment.IsCanceled)
            {
                try
                {
                    CoreWebView2Environment.SetLoaderDllFolderPath(LoaderDirectory());
                }
                catch (Exception ex)
                {
                    // Throws if the loader was already set in this process; harmless.
                    Log.Info("loader path: " + ex.Message);
                }

                string userData = Path.Combine(Paths.DataDir, "WebView2");
                Directory.CreateDirectory(userData);
                Log.Info("create environment: thread=" + Thread.CurrentThread.ManagedThreadId +
                         " apartment=" + Thread.CurrentThread.GetApartmentState() +
                         " integrity=" + Integrity.Current() + " data=" + userData);

                // Trim Chromium startup: skip background services we never use. These only affect
                // boot cost, not rendering. The on-disk V8 code cache in userData persists across
                // prevhost processes, so heavy scripts (Mermaid) compile fast after the first time.
                var options = new CoreWebView2EnvironmentOptions(
                    "--disable-background-networking --disable-component-update --disable-sync " +
                    "--no-first-run --disable-breakpad --disable-domain-reliability " +
                    "--disable-features=Translate,OptimizationHints,MediaRouter,msSmartScreenProtection");
                options.AllowSingleSignOnUsingOSPrimaryAccount = false;

                var clock = Stopwatch.StartNew();
                Task<CoreWebView2Environment> create = CoreWebView2Environment.CreateAsync(null, userData, options);
                create.ContinueWith(t => Log.Info("environment ready in " + clock.ElapsedMilliseconds + "ms" +
                    (t.IsFaulted ? " FAULTED: " + t.Exception?.GetBaseException().Message : "")),
                    TaskScheduler.Default);
                t_environment = create;
            }

            return t_environment;
        }

        private static string LoaderDirectory()
        {
            string arch;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.Arm64: arch = "win-arm64"; break;
                case Architecture.X86: arch = "win-x86"; break;
                default: arch = "win-x64"; break;
            }

            return Path.Combine(DependencyResolver.BaseDir, "runtimes", arch, "native");
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                CoreWebView2Environment environment = _web.CoreWebView2.Environment;
                var uri = new Uri(e.Request.Uri);
                if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string path = Uri.UnescapeDataString(uri.AbsolutePath);
                if (path == PagePath)
                {
                    e.Response = environment.CreateWebResourceResponse(
                        new MemoryStream(_pageBytes ?? new byte[0]), 200, "OK", PageHeaders(_allowRemoteImages));
                    return;
                }

                string file = null;
                bool bundledAsset = false;
                if (path.StartsWith(AssetsPrefix, StringComparison.Ordinal))
                {
                    file = SafeCombine(AssetsDir, path.Substring(AssetsPrefix.Length));
                    bundledAsset = true;
                }
                else if (_docDir != null)
                {
                    file = SafeCombine(_docDir, path.TrimStart('/'));
                    if (file != null && !ImageExtensions.Contains(Path.GetExtension(file)))
                    {
                        file = null;
                    }
                }

                if (file == null || !File.Exists(file) || new FileInfo(file).Length > MaxResourceBytes)
                {
                    e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "Cache-Control: no-store");
                    return;
                }

                // Bundled libraries never change for a build, so serve them cacheable. This lets
                // WebView2 persist the compiled V8 bytecode for the big scripts (Mermaid) to the
                // profile's on-disk code cache, so later cold renders don't recompile from scratch.
                var info = new FileInfo(file);
                if (bundledAsset)
                {
                    string etag = e.Request.Headers.Contains("If-None-Match") ? e.Request.Headers.GetHeader("If-None-Match") : null;
                    string tag = "\"" + info.Length.ToString("x") + "-" + info.LastWriteTimeUtc.Ticks.ToString("x") + "\"";
                    if (etag == tag)
                    {
                        e.Response = environment.CreateWebResourceResponse(null, 304, "Not Modified", AssetHeaders(MimeType(file), tag));
                        return;
                    }

                    byte[] assetBytes = File.ReadAllBytes(file);
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(assetBytes), 200, "OK", AssetHeaders(MimeType(file), tag));
                    return;
                }

                byte[] bytes;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }

                e.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", Headers(MimeType(file), false));
            }
            catch (Exception ex)
            {
                Log.Error("resource " + e.Request.Uri, ex);
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == _pageUrl || (_pageUrl != null && e.Uri.StartsWith(_pageUrl + "#", StringComparison.Ordinal)))
            {
                return;
            }

            e.Cancel = true;
            Log.Info("blocked navigation (user=" + e.IsUserInitiated + "): " + e.Uri);
            if (e.IsUserInitiated)
            {
                OpenExternal(e.Uri);
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            Log.Info("blocked new window (user=" + e.IsUserInitiated + "): " + e.Uri);
            if (e.IsUserInitiated)
            {
                OpenExternal(e.Uri);
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = e.WebMessageAsJson ?? string.Empty;
            if (json.Length > 2000)
            {
                json = json.Substring(0, 2000);
            }

            if (json.Contains("\"type\":\"rendered\""))
            {
                Log.Info("rendered " + _docPath + " total=" + (_clock?.ElapsedMilliseconds ?? -1) + "ms " + json);
            }
            else if (json.Contains("\"type\":\"anchor\""))
            {
                Log.Info("anchor " + _docPath + " " + json);
            }
            else
            {
                Log.Error("page " + _docPath + " " + json);
            }
        }

        private static void OnContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            IList<CoreWebView2ContextMenuItem> items = e.MenuItems;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                CoreWebView2ContextMenuItem item = items[i];
                if (item.Kind == CoreWebView2ContextMenuItemKind.Separator || !MenuItemsToKeep.Contains(item.Name))
                {
                    items.RemoveAt(i);
                }
            }
        }

        private static void OpenExternal(string uri)
        {
            try
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) &&
                    (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeMailto) &&
                    !string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log.Error("open " + uri, ex);
            }
        }

        private static string SafeCombine(string root, string relative)
        {
            relative = relative.Replace('/', '\\');
            if (relative.Length == 0 || relative.IndexOf(':') >= 0 || relative.StartsWith("\\", StringComparison.Ordinal))
            {
                return null;
            }

            string full = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = root.EndsWith("\\", StringComparison.Ordinal) ? root : root + "\\";
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        private static string Headers(string contentType, bool isPage)
        {
            var sb = new StringBuilder();
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("X-Content-Type-Options: nosniff");
            if (isPage)
            {
                sb.Append("\r\nContent-Security-Policy: ").Append(ContentSecurityPolicy(false));
            }

            return sb.ToString();
        }

        private static string PageHeaders(bool allowRemoteImages)
        {
            return "Content-Type: text/html; charset=utf-8\r\n" +
                   "Cache-Control: no-store\r\n" +
                   "X-Content-Type-Options: nosniff\r\n" +
                   "Content-Security-Policy: " + ContentSecurityPolicy(allowRemoteImages);
        }

        // Cacheable headers for the bundled libraries. The ETag is content-derived, so a rebuilt
        // asset (new length or timestamp) busts the cache and recompiles; otherwise WebView2 keeps
        // the response and the compiled bytecode on disk across preview-host restarts.
        private static string AssetHeaders(string contentType, string etag)
        {
            return "Content-Type: " + contentType + "\r\n" +
                   "Cache-Control: public, max-age=31536000\r\n" +
                   "ETag: " + etag + "\r\n" +
                   "X-Content-Type-Options: nosniff";
        }

        private static string MimeType(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                case ".ttf": return "font/ttf";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                case ".bmp": return "image/bmp";
                case ".ico": return "image/x-icon";
                case ".avif": return "image/avif";
                default: return "application/octet-stream";
            }
        }
    }
}
