using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace MdPreview
{
    /// <summary>
    /// prevhost.exe has no config file of ours, so dependency versions cannot be redirected.
    /// Resolve any dependency by name from the folder this assembly lives in.
    /// </summary>
    internal static class DependencyResolver
    {
        private static int _installed;

        public static readonly string BaseDir = Path.GetDirectoryName(typeof(DependencyResolver).Assembly.Location);

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) == 1)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string name = new AssemblyName(args.Name).Name;
                    string candidate = Path.Combine(BaseDir, name + ".dll");
                    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                }
                catch
                {
                    return null;
                }
            };
        }
    }

    /// <summary>
    /// Per-user options under HKCU\Software\MdPreview. There is no settings UI yet; the install
    /// folder ships set-remote-images.ps1 to toggle these.
    /// </summary>
    internal static class Settings
    {
        private const string Key = @"Software\MdPreview";

        public static bool AllowRemoteImages()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key))
                {
                    return key?.GetValue("AllowRemoteImages") is int v && v != 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Theme
    {
        public static bool IsDark(uint? colorRef)
        {
            if (colorRef.HasValue)
            {
                Color c = FromColorRef(colorRef.Value);
                double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return luminance < 0.5;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") is int value)
                    {
                        return value == 0;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static Color Background(uint? colorRef, bool dark)
        {
            if (colorRef.HasValue)
            {
                return FromColorRef(colorRef.Value);
            }

            return dark ? Color.FromArgb(0x19, 0x19, 0x19) : Color.White;
        }

        public static string ToCss(Color c)
        {
            return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");
        }

        private static Color FromColorRef(uint value)
        {
            return Color.FromArgb((int)(value & 0xFF), (int)((value >> 8) & 0xFF), (int)((value >> 16) & 0xFF));
        }
    }

    /// <summary>
    /// Writable data folder. Explorer may run prevhost.exe at low integrity, where only
    /// %USERPROFILE%\AppData\LocalLow is writable, so everything goes there.
    /// </summary>
    internal static class Paths
    {
        private static readonly Guid LocalAppDataLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");

        public static readonly string DataDir = Path.Combine(LocalLow(), "MdPreview");

        private static string LocalLow()
        {
            try
            {
                Guid id = LocalAppDataLow;
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out IntPtr ptr) == 0)
                {
                    try
                    {
                        return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(ptr);
                    }
                }
            }
            catch
            {
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    }

    internal static class Log
    {
        private static readonly object Gate = new object();
        private static readonly string Dir = Paths.DataDir;
        public static readonly string FilePath = Path.Combine(Dir, "log.txt");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string where, Exception ex) => Write("ERROR", where + ": " + ex);

        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        File.Delete(FilePath);
                    }

                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + Process.GetCurrentProcess().Id + "] " + level + " " + message + Environment.NewLine;
                    File.AppendAllText(FilePath, line);
                }
            }
            catch
            {
            }
        }
    }
}
