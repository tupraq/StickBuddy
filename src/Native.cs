using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StickBuddy
{
    // Talks to Windows directly: see-through window, keyboard presses, window positions.
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; public SIZE(int w, int h) { cx = w; cy = h; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left; public int Top; public int Right; public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        public const uint WM_CHAR = 0x0102, EM_SETSEL = 0x00B1;

        // The text box inside Notepad (works for the new Windows 11 Notepad and the old one).
        public static IntPtr FindEditChild(IntPtr parent)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, delegate (IntPtr h, IntPtr l)
            {
                string c = ClassName(h);
                if ((c.StartsWith("RichEdit") || c == "Edit") && IsWindowVisible(h)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
        const uint EM_REPLACESEL = 0x00C2;

        // Puts a letter straight into a text box (at the cursor). It doesn't matter which window
        // is in front, so letters can never end up in the wrong app.
        public static void InsertText(IntPtr edit, string s)
        {
            if (s == "\n") s = "\r";
            IntPtr r;
            SendMessageTimeout(edit, EM_REPLACESEL, (IntPtr)1, s, 2 /* abort if hung */, 1500, out r);
        }

        public static void CaretToEnd(IntPtr edit)
        {
            SendMessage(edit, EM_SETSEL, (IntPtr)int.MaxValue, (IntPtr)int.MaxValue);
        }

        public const int SW_RESTORE = 9, SW_MINIMIZE = 6;
        public const uint WM_CLOSE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;

        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetInt(IntPtr hwnd, int attr, out int v, int size);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

        // Windows that are "open" but hidden by Windows itself (other desktops, suspended apps).
        public static bool IsCloaked(IntPtr h)
        {
            int v;
            return DwmGetInt(h, 14, out v, 4) == 0 && v != 0;
        }

        public static string KnownFolder(Guid id)
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out p) != 0) return null;
                string s = Marshal.PtrToStringUni(p);
                CoTaskMemFree(p);
                return s;
            }
            catch { return null; }
        }

        public static readonly Guid FolderDownloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        public const ushort VK_RETURN = 0x0D, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_ESCAPE = 0x1B,
            VK_END = 0x23, VK_LWIN = 0x5B, VK_SNAPSHOT = 0x2C, VK_D = 0x44,
            VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF,
            VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;

        // ---------- keyboard ----------
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public int mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;

        static INPUT Key(ushort vk, ushort scan, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.wVk = vk; i.u.ki.wScan = scan; i.u.ki.dwFlags = flags;
            return i;
        }

        static uint Ext(ushort vk)
        {
            switch (vk)
            {
                case 0x5B: case 0x5C: case 0x21: case 0x22: case 0x23: case 0x24:
                case 0x25: case 0x26: case 0x27: case 0x28: case 0x2D: case 0x2E:
                    return KEYEVENTF_EXTENDEDKEY;
            }
            return 0;
        }

        static void Send(params INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }

        public static void KeyDown(ushort vk) { Send(Key(vk, 0, Ext(vk))); }
        public static void KeyUp(ushort vk) { Send(Key(vk, 0, KEYEVENTF_KEYUP | Ext(vk))); }
        public static void Tap(ushort vk) { Send(Key(vk, 0, Ext(vk)), Key(vk, 0, KEYEVENTF_KEYUP | Ext(vk))); }
        public static void Combo(ushort mod, ushort vk) { KeyDown(mod); Tap(vk); KeyUp(mod); }

        // Types one letter, no matter what keyboard layout the PC uses.
        public static void TypeChar(char c)
        {
            if (c == '\r') return;
            if (c == '\n') { Tap(VK_RETURN); return; }
            if (c == '\t') { Tap(0x09); return; }
            Send(Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        // ---------- game input ----------
        // Games usually read the key's "scan code" (its place on the keyboard), so send that.
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        const uint KEYEVENTF_SCANCODE = 8;
        const uint MOUSEEVENTF_MOVE = 1, MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, MOUSEEVENTF_RIGHTDOWN = 8, MOUSEEVENTF_RIGHTUP = 0x10;

        public static void GameKey(ushort vk, bool down)
        {
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            uint fl = KEYEVENTF_SCANCODE | (down ? 0 : KEYEVENTF_KEYUP) | Ext(vk);
            Send(Key(vk, scan, fl));
        }

        static INPUT Mouse(int dx, int dy, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 0;
            i.u.mi.dx = dx; i.u.mi.dy = dy; i.u.mi.dwFlags = flags;
            return i;
        }

        public static void MouseButton(bool right, bool down)
        {
            uint fl = right ? (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP) : (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
            Send(Mouse(0, 0, fl));
        }

        // Moves the mouse by an amount (games use this to turn the camera).
        public static void MouseNudge(int dx, int dy) { Send(Mouse(dx, dy, MOUSEEVENTF_MOVE)); }

        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);

        // The inside of a window (without its title bar and borders), in screen pixels.
        public static Rectangle ClientScreenRect(IntPtr h)
        {
            RECT r;
            if (!GetClientRect(h, out r)) return Rectangle.Empty;
            POINT p = new POINT(0, 0);
            ClientToScreen(h, ref p);
            return new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        // ---------- windows ----------
        public static string ClassName(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string Title(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ProcessName(IntPtr h)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch { return null; }
        }

        public static int ProcessId(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return (int)pid;
        }

        // The rectangle you can actually see (Windows 10/11 add invisible borders).
        public static RECT GetVisibleRect(IntPtr h)
        {
            RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Width > 0) return r;
            GetWindowRect(h, out r);
            return r;
        }

        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            IntPtr fg = GetForegroundWindow();
            if (fg == h) return;
            uint dummy;
            uint fgThread = GetWindowThreadProcessId(fg, out dummy);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(h);
            BringWindowToTop(h);
            if (attached) AttachThreadInput(me, fgThread, false);
        }

        public static List<IntPtr> TopWindows()
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (IsWindowVisible(h) && !IsIconic(h) && Title(h).Length > 0) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static int ScreenDpi()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            int dpi = GetDeviceCaps(dc, 88);
            ReleaseDC(IntPtr.Zero, dc);
            return dpi <= 0 ? 96 : dpi;
        }

        // Puts a picture with see-through parts on the screen.
        public static void UpdateLayered(IntPtr hwnd, Bitmap bmp, int x, int y)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBmp);
                SIZE size = new SIZE(bmp.Width, bmp.Height);
                POINT src = new POINT(0, 0);
                POINT pos = new POINT(x, y);
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0; blend.BlendFlags = 0; blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                UpdateLayeredWindow(hwnd, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBmp != IntPtr.Zero) { SelectObject(memDc, old); DeleteObject(hBmp); }
                DeleteDC(memDc);
            }
        }
    }

    static class Log
    {
        static readonly object locker = new object();
        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickBuddy"); }
        }

        public static void Write(string s)
        {
            try
            {
                lock (locker)
                {
                    Directory.CreateDirectory(Folder);
                    string path = Path.Combine(Folder, "log.txt");
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 2000000) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss ") + s + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
