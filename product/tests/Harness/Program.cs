using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MdPreview.Harness
{
    /// <summary>
    /// Hosts the registered preview handler the way Explorer does: creates it out of process
    /// (prevhost.exe), hands it a file and a window, waits for the page to report that it has
    /// rendered, then saves a screenshot of the window.
    ///
    /// Usage: MdPreviewHarness file.md out.png [--theme dark|light] [--width 900] [--height 1000]
    ///        [--timeout 20000] [--inproc]
    /// Exit codes: 0 rendered, 1 timed out, 2 handler could not be created.
    /// </summary>
    internal static class Program
    {
        private static readonly Guid Clsid = new Guid("d6ef0d24-13b8-4841-b4ce-b190114f4b51");
        private static readonly Guid IidUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        private const uint ClsctxInprocServer = 0x1;
        private const uint ClsctxLocalServer = 0x4;

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "MdPreview", "log.txt");

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        private static int Main(string[] args)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2

            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: MdPreviewHarness file.md out.png [--theme dark|light] [--width N] [--height N] [--timeout ms] [--inproc]");
                return 2;
            }

            string file = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            string theme = Option(args, "--theme", "light");
            int width = int.Parse(Option(args, "--width", "900"));
            int height = int.Parse(Option(args, "--height", "1000"));
            int timeout = int.Parse(Option(args, "--timeout", "20000"));
            bool inproc = Array.IndexOf(args, "--inproc") >= 0;
            bool dark = theme == "dark";

            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(30, 30),
                ClientSize = new Size(width, height),
                TopMost = true,
                ShowInTaskbar = false,
                BackColor = dark ? Color.FromArgb(0x19, 0x19, 0x19) : Color.White,
            };
            form.Show();
            Pump(200);

            long logStart = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
            var clock = Stopwatch.StartNew();

            Guid clsid = Clsid;
            Guid iid = IidUnknown;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, inproc ? ClsctxInprocServer : ClsctxLocalServer, ref iid, out object handler);
            if (hr != 0)
            {
                Console.Error.WriteLine("CoCreateInstance failed: 0x" + hr.ToString("X8"));
                return 2;
            }

            var preview = (IPreviewHandler)handler;
            var rect = new RECT { Left = 0, Top = 0, Right = form.ClientSize.Width, Bottom = form.ClientSize.Height };

            // Files after the first one reuse the same handler, as Explorer does when the selection changes.
            var files = new System.Collections.Generic.List<string> { file };
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--then")
                {
                    files.Add(Path.GetFullPath(args[i + 1]));
                }
            }

            bool allRendered = true;
            for (int n = 0; n < files.Count; n++)
            {
                if (n > 0)
                {
                    preview.Unload();
                    clock.Restart();
                }

                string shot = n == 0 ? output : Path.Combine(Path.GetDirectoryName(output),
                    Path.GetFileNameWithoutExtension(output) + "-next" + n + ".png");
                logStart = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;

                ((IInitializeWithFile)handler).Initialize(files[n], 0);
                preview.SetWindow(form.Handle, ref rect);
                if (handler is IPreviewHandlerVisuals visuals)
                {
                    visuals.SetBackgroundColor(dark ? 0x191919u : 0xFFFFFFu);
                }

                preview.SetRect(ref rect);
                preview.DoPreview();

                bool rendered = WaitAndCapture(form, files[n], shot, logStart, clock, timeout, out string newLog);
                allRendered &= rendered;
                Console.WriteLine((rendered ? "RENDERED" : "TIMEOUT") + " " + Path.GetFileName(files[n]) + " in " + clock.ElapsedMilliseconds +
                                  " ms (" + (inproc ? "in-process" : "prevhost") + ", " + theme + (n > 0 ? ", warm" : ", cold") + ")");
                foreach (string line in newLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Console.WriteLine("  log: " + line);
                }
            }

            preview.Unload();
            Marshal.FinalReleaseComObject(handler);
            form.Close();
            return allRendered ? 0 : 1;
        }

        private static bool WaitAndCapture(Form form, string file, string output, long logStart, Stopwatch clock, int timeout, out string newLog)
        {
            string marker = "rendered " + file;
            newLog = string.Empty;
            bool rendered = false;
            while (clock.ElapsedMilliseconds < timeout)
            {
                Pump(25);
                newLog = ReadLogFrom(logStart);
                if (newLog.Contains(marker))
                {
                    rendered = true;
                    break;
                }
            }

            clock.Stop();
            Pump(600); // let the last frame reach the screen

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
                bitmap.Save(output, ImageFormat.Png);
            }

            return rendered;
        }

        private static void Pump(int milliseconds)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static string ReadLogFrom(long offset)
        {
            try
            {
                if (!File.Exists(LogPath))
                {
                    return string.Empty;
                }

                using (var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length < offset)
                    {
                        offset = 0; // log was rotated
                    }

                    stream.Seek(offset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        private static string Option(string[] args, string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
