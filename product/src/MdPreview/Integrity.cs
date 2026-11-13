using System;
using System.Runtime.InteropServices;

namespace MdPreview
{
    /// <summary>Reports the integrity level of this process (for diagnostics).</summary>
    internal static class Integrity
    {
        private const uint TokenQuery = 0x0008;
        private const int TokenIntegrityLevel = 25;

        public static string Current()
        {
            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
                {
                    return "unknown";
                }

                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int size);
                buffer = Marshal.AllocHGlobal(size);
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out size))
                {
                    return "unknown";
                }

                IntPtr sid = Marshal.ReadIntPtr(buffer);
                int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                int rid = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                if (rid < 0x1000) return "untrusted";
                if (rid < 0x2000) return "low";
                if (rid < 0x3000) return "medium";
                if (rid < 0x4000) return "high";
                return "system";
            }
            catch
            {
                return "unknown";
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int length, out int returned);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    }
}
