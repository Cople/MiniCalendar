using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace MiniCalendar.Services
{
    public static class TrayIconHelper
    {
        #region P/Invoke Definitions

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoints, uint cPoints);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, AllocationType flAllocationType, MemoryProtection flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, FreeType dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [Flags]
        private enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x00000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400
        }

        [Flags]
        private enum AllocationType
        {
            Commit = 0x1000,
            Reserve = 0x2000,
            Decommit = 0x4000,
            Release = 0x8000,
            Reset = 0x80000,
            Physical = 0x400000,
            TopDown = 0x100000,
            WriteWatch = 0x200000,
            LargePages = 0x20000000
        }

        [Flags]
        private enum MemoryProtection
        {
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            NoAccess = 0x01,
            ReadOnly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            GuardModifierflag = 0x100,
            NoCacheModifierflag = 0x200,
            WriteCombineModifierflag = 0x400
        }

        [Flags]
        private enum FreeType
        {
            Decommit = 0x4000,
            Release = 0x8000,
        }

        private const uint TB_BUTTONCOUNT = 0x0418;
        private const uint TB_GETBUTTON = 0x0417;
        private const uint TB_GETITEMRECT = 0x041D;

        #endregion

        public static Rect GetTrayIconRect(string toolTipTitle)
        {
            // 1. Try to find in the system tray
            IntPtr hWndTray = FindWindow("Shell_TrayWnd", null);
            if (hWndTray != IntPtr.Zero)
            {
                IntPtr hWndTrayNotify = FindWindowEx(hWndTray, IntPtr.Zero, "TrayNotifyWnd", null);
                if (hWndTrayNotify != IntPtr.Zero)
                {
                    IntPtr hWndSysPager = FindWindowEx(hWndTrayNotify, IntPtr.Zero, "SysPager", null);
                    if (hWndSysPager != IntPtr.Zero)
                    {
                        IntPtr hWndToolbar = FindWindowEx(hWndSysPager, IntPtr.Zero, "ToolbarWindow32", null);
                        if (hWndToolbar != IntPtr.Zero)
                        {
                            var rect = GetTrayIconRectFromToolbar(hWndToolbar, toolTipTitle);
                            if (rect != Rect.Empty) return rect;
                        }
                    }
                    
                    // Windows 11 sometimes puts ToolbarWindow32 directly under TrayNotifyWnd (or different structure)
                    // Let's try searching ToolbarWindow32 directly under TrayNotifyWnd as fallback
                    IntPtr hWndToolbarDirect = FindWindowEx(hWndTrayNotify, IntPtr.Zero, "ToolbarWindow32", null);
                    if (hWndToolbarDirect != IntPtr.Zero)
                    {
                         var rect = GetTrayIconRectFromToolbar(hWndToolbarDirect, toolTipTitle);
                         if (rect != Rect.Empty) return rect;
                    }
                }
            }

            // 2. Try to find in the overflow window (the chevron popup)
            IntPtr hWndOverflow = FindWindow("NotifyIconOverflowWindow", null);
            if (hWndOverflow != IntPtr.Zero)
            {
                IntPtr hWndToolbar = FindWindowEx(hWndOverflow, IntPtr.Zero, "ToolbarWindow32", null);
                if (hWndToolbar != IntPtr.Zero)
                {
                    var rect = GetTrayIconRectFromToolbar(hWndToolbar, toolTipTitle);
                    if (rect != Rect.Empty) return rect;
                }
            }

            return Rect.Empty;
        }

        private static Rect GetTrayIconRectFromToolbar(IntPtr hWndToolbar, string toolTipTitle)
        {
            uint processId;
            GetWindowThreadProcessId(hWndToolbar, out processId);

            IntPtr hProcess = OpenProcess(ProcessAccessFlags.VirtualMemoryOperation | ProcessAccessFlags.VirtualMemoryRead, false, processId);
            if (hProcess == IntPtr.Zero) return Rect.Empty;

            try
            {
                // Determine if 64-bit
                bool is64Bit = Environment.Is64BitOperatingSystem; // Assuming Explorer matches OS bitness usually

                int count = (int)SendMessage(hWndToolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
                
                for (int i = 0; i < count; i++)
                {
                    // Define TBBUTTON size
                    int tbButtonSize = is64Bit ? 32 : 20; // Rough estimation: 64-bit (8+4+1+1+6+8+8=36? No, actually: 8(bitmap)+4(id)+1(state)+1(style)+6(align)+8(data)+8(string) = 36? Wait. 
                    // Let's use Marshal.SizeOf if we had the struct defined, but we'll do manual offset calculation for safety across processes.
                    // 64-bit TBBUTTON:
                    // 0: iBitmap (4 bytes)
                    // 4: idCommand (4 bytes)
                    // 8: fsState (1 byte)
                    // 9: fsStyle (1 byte)
                    // 10: bReserved (6 bytes padding) -> 16
                    // 16: dwData (8 bytes)
                    // 24: iString (8 bytes)
                    // Total: 32 bytes?
                    
                    // 32-bit TBBUTTON:
                    // 0: iBitmap (4 bytes)
                    // 4: idCommand (4 bytes)
                    // 8: fsState (1 byte)
                    // 9: fsStyle (1 byte)
                    // 10: bReserved (2 bytes) -> 12
                    // 12: dwData (4 bytes)
                    // 16: iString (4 bytes)
                    // Total: 20 bytes

                    // We need to allocate memory in the remote process
                    IntPtr lpRemoteBuffer = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)tbButtonSize, AllocationType.Commit, MemoryProtection.ReadWrite);
                    if (lpRemoteBuffer == IntPtr.Zero) continue;

                    try
                    {
                        if (SendMessage(hWndToolbar, TB_GETBUTTON, (IntPtr)i, lpRemoteBuffer) != IntPtr.Zero)
                        {
                            byte[] buffer = new byte[tbButtonSize];
                            int bytesRead;
                            if (ReadProcessMemory(hProcess, lpRemoteBuffer, buffer, buffer.Length, out bytesRead))
                            {
                                IntPtr iStringPtr = IntPtr.Zero;
                                
                                if (is64Bit)
                                {
                                    long ptrVal = BitConverter.ToInt64(buffer, 24);
                                    iStringPtr = (IntPtr)ptrVal;
                                }
                                else
                                {
                                    int ptrVal = BitConverter.ToInt32(buffer, 16);
                                    iStringPtr = (IntPtr)ptrVal;
                                }

                                if (iStringPtr != IntPtr.Zero)
                                {
                                    // Read the string
                                    // First we need to find the length or just read a chunk
                                    byte[] stringBuffer = new byte[512]; // 256 chars unicode
                                    if (ReadProcessMemory(hProcess, iStringPtr, stringBuffer, stringBuffer.Length, out bytesRead))
                                    {
                                        string text = Encoding.Unicode.GetString(stringBuffer);
                                        int nullIndex = text.IndexOf('\0');
                                        if (nullIndex >= 0) text = text.Substring(0, nullIndex);

                                        if (text.Contains(toolTipTitle))
                                        {
                                            // Found it! Get the rect
                                            // Reuse remote buffer for RECT
                                            // SendMessage TB_GETITEMRECT
                                            IntPtr lpRectBuffer = lpRemoteBuffer; // Reuse
                                            
                                            if (SendMessage(hWndToolbar, TB_GETITEMRECT, (IntPtr)i, lpRectBuffer) != IntPtr.Zero)
                                            {
                                                byte[] rectBuffer = new byte[16]; // 4 ints
                                                if (ReadProcessMemory(hProcess, lpRectBuffer, rectBuffer, 16, out bytesRead))
                                                {
                                                    RECT r;
                                                    r.Left = BitConverter.ToInt32(rectBuffer, 0);
                                                    r.Top = BitConverter.ToInt32(rectBuffer, 4);
                                                    r.Right = BitConverter.ToInt32(rectBuffer, 8);
                                                    r.Bottom = BitConverter.ToInt32(rectBuffer, 12);

                                                    // Map points to screen
                                                    POINT pTL = new POINT { X = r.Left, Y = r.Top };
                                                    POINT pBR = new POINT { X = r.Right, Y = r.Bottom };
                                                    
                                                    MapWindowPoints(hWndToolbar, IntPtr.Zero, ref pTL, 1);
                                                    MapWindowPoints(hWndToolbar, IntPtr.Zero, ref pBR, 1);

                                                    return new Rect(pTL.X, pTL.Y, pBR.X - pTL.X, pBR.Y - pTL.Y);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        VirtualFreeEx(hProcess, lpRemoteBuffer, 0, FreeType.Release);
                    }
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }

            return Rect.Empty;
        }
    }
}
