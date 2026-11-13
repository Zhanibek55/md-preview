using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MdPreview.Warmup
{
    /// <summary>
    /// Renders a warm-up document once through the installed preview handler so the WebView2
    /// profile's on-disk V8 code cache gets seeded with the heavy scripts — Mermaid, KaTeX,
    /// highlight.js. The handler serves those libraries with cacheable headers, so their compiled
    /// bytecode is written to the profile and survives preview-host and browser restarts. After
    /// this, the first real diagram a user opens compiles from cache (~0.3 s) instead of ~4-9 s.
    ///
    /// Runs the handler the way Explorer does (CoCreateInstance into prevhost), off-screen, and
    /// exits once the page reports it rendered (the disk cache is written during the render).
    ///
    /// Usage: MdPreviewWarmup [warmup.md] [--timeout ms]
    /// Exit 0 on success, 1 on timeout.
    /// </summary>
    internal static class Program
    {
        private static readonly Guid Clsid = new Guid("d6ef0d24-13b8-4841-b4ce-b190114f4b51");
        private static readonly Guid IidUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        private const uint ClsctxLocalServer = 0x4;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "MdPreview");

        private static readonly string LogPath = Path.Combine(DataDir, "log.txt");

        // Where WebView2 (Chromium) keeps the compiled-script bytecode for this profile.
        private static readonly string CodeCacheDir = Path.Combine(DataDir, "WebView2", "EBWebView", "Default", "Code Cache");

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        [STAThread]
        private static int Main(string[] args)
        {
            string file = args.Length > 0 && !args[0].StartsWith("--")
                ? Path.GetFullPath(args[0])
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "warmup.md");
            int timeout = 60000;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--timeout") int.TryParse(args[i + 1], out timeout);
            }

            if (!File.Exists(file))
            {
                Console.Error.WriteLine("warmup file not found: " + file);
                return 1;
            }

            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ClientSize = new Size(900, 1000),
                ShowInTaskbar = false,
            };
            form.Show();
            Pump(100);

            long logStart = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;

            Guid clsid = Clsid, iid = IidUnknown;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxLocalServer, ref iid, out object handler) != 0)
            {
                Console.Error.WriteLine("handler not registered");
                return 1;
            }

            var preview = (IPreviewHandler)handler;
            var rect = new RECT { Left = 0, Top = 0, Right = 900, Bottom = 1000 };
            preview.SetWindow(form.Handle, ref rect);

            // Chromium writes a script's compiled bytecode to the disk code cache only on its SECOND
            // execution (its "cache after 2nd run" heuristic). So render the page twice; the second
            // pass is what makes the heavy Mermaid bytecode persist to disk and survive restarts.
            bool rendered = false;
            var clock = Stopwatch.StartNew();
            for (int pass = 1; pass <= 2; pass++)
            {
                long passStart = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
                ((IInitializeWithFile)handler).Initialize(file, 0);
                preview.DoPreview();

                rendered = false;
                var passClock = Stopwatch.StartNew();
                while (passClock.ElapsedMilliseconds < timeout)
                {
                    Pump(50);
                    if (ReadLogFrom(passStart).Contains("rendered " + file)) { rendered = true; break; }
                }

                Pump(500);
                try { preview.Unload(); } catch { }  // navigating away flushes the code cache
                Pump(300);
            }

            // Wait until the compiled bytecode has actually landed on disk before exiting.
            long cacheKb = WaitForCodeCache(400, 10000);

            try { Marshal.FinalReleaseComObject(handler); } catch { }
            form.Close();
            Console.WriteLine(rendered
                ? "warm-up done in " + clock.ElapsedMilliseconds + "ms, code cache " + cacheKb + " KB"
                : "warm-up timed out");
            return rendered ? 0 : 1;
        }

        private static long WaitForCodeCache(long targetKb, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            long size = 0, stable = 0;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                Pump(300);
                long now = CodeCacheKb();
                // Stop once it has reached a healthy size and stopped growing for ~1s.
                if (now >= targetKb && now == size) { if (++stable >= 3) break; }
                else { stable = 0; }
                size = now;
            }

            return size;
        }

        private static long CodeCacheKb()
        {
            try
            {
                if (!Directory.Exists(CodeCacheDir)) return 0;
                long bytes = 0;
                foreach (string f in Directory.GetFiles(CodeCacheDir, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }

                return bytes / 1024;
            }
            catch { return 0; }
        }

        private static void Pump(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        private static string ReadLogFrom(long offset)
        {
            try
            {
                if (!File.Exists(LogPath)) return "";
                using (var s = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (s.Length < offset) offset = 0;
                    s.Seek(offset, SeekOrigin.Begin);
                    using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch (IOException) { return ""; }
        }
    }
}
