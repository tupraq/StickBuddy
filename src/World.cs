using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace StickBuddy
{
    // Something he can stand on: the floor, the top of a window, or a floor of his house.
    public class Plat
    {
        public const int Floor = 0, Window = 1, Upper = 2, Roof = 3;
        public double L, R, Y;
        public int Kind;
        public IntPtr Hwnd;

        public Plat(double l, double r, double y, int kind, IntPtr h) { L = l; R = r; Y = y; Kind = kind; Hwnd = h; }
        public double Mid { get { return (L + R) / 2; } }
        public bool Same(Support s) { return s != null && s.Kind == Kind && s.Hwnd == Hwnd; }
    }

    // What he is standing on right now (kept by kind, because the platform list is rebuilt all the time).
    public class Support
    {
        public readonly int Kind;
        public readonly IntPtr Hwnd;
        public Support(int k, IntPtr h) { Kind = k; Hwnd = h; }
        public static readonly Support Floor = new Support(Plat.Floor, IntPtr.Zero);
        public static readonly Support Upper = new Support(Plat.Upper, IntPtr.Zero);
        public static readonly Support Roof = new Support(Plat.Roof, IntPtr.Zero);
        public static Support Window(IntPtr h) { return new Support(Plat.Window, h); }
        public static Support Of(Plat p)
        {
            if (p == null) return Floor;
            switch (p.Kind)
            {
                case Plat.Window: return Window(p.Hwnd);
                case Plat.Upper: return Upper;
                case Plat.Roof: return Roof;
            }
            return Floor;
        }
        public bool IsWindow { get { return Kind == Plat.Window; } }
        public bool IsHouse { get { return Kind == Plat.Upper || Kind == Plat.Roof; } }
    }

    public class WinInfo
    {
        public IntPtr H;
        public Native.RECT R;
        public bool Max;
    }

    // Keeps a list of everything on screen he can land on or climb.
    public static class World
    {
        public static List<Plat> Plats = new List<Plat>();
        public static List<WinInfo> Wins = new List<WinInfo>(); // top-most first
        static double next;
        static readonly int pid = Process.GetCurrentProcess().Id;

        static bool IsShell(string c)
        {
            return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" ||
                   c == "Windows.UI.Core.CoreWindow" || c == "XamlExplorerHostIslandWindow" || c == "TopLevelWindowForOverflowXamlIsland" ||
                   c == "NotifyIconOverflowWindow" || c == "Xaml_WindowedPopupClass";
        }

        // A normal window you can see (not hidden, not a see-through overlay, not ours).
        public static bool RealWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
            if (Native.ProcessId(h) == pid) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return false;
            if (Native.IsCloaked(h)) return false;
            if (Native.Title(h).Length == 0) return false;
            if (IsShell(Native.ClassName(h))) return false;
            Native.RECT r = Native.GetVisibleRect(h);
            return r.Width >= 120 && r.Height >= 60;
        }

        public static void Refresh(Buddy b, double now, bool force)
        {
            if (!force && now < next) return;
            next = now + 0.15;
            List<Plat> ps = new List<Plat>();
            foreach (Screen s in Screen.AllScreens)
                ps.Add(new Plat(s.WorkingArea.Left, s.WorkingArea.Right, s.WorkingArea.Bottom, Plat.Floor, IntPtr.Zero));

            Rectangle houseRect = Rectangle.Empty;
            if (b.House != null && b.House.Shown)
            {
                ps.AddRange(b.House.Platforms());
                houseRect = b.House.StructureRect();
            }

            List<WinInfo> wins = new List<WinInfo>();
            try
            {
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    if (RealWindow(h))
                    {
                        WinInfo w = new WinInfo();
                        w.H = h; w.R = Native.GetVisibleRect(h); w.Max = Native.IsZoomed(h);
                        wins.Add(w);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            for (int i = 0; i < wins.Count; i++)
            {
                WinInfo w = wins[i];
                if (w.Max) continue;
                Rectangle wa = Screen.FromPoint(new Point((w.R.Left + w.R.Right) / 2, w.R.Top + 5)).WorkingArea;
                if (w.R.Top < wa.Top + 100 * b.U || w.R.Top > wa.Bottom - 30 * b.Dpi) continue;
                List<double[]> segs = new List<double[]>();
                segs.Add(new double[] { Math.Max(w.R.Left, wa.Left), Math.Min(w.R.Right, wa.Right) });
                for (int k = 0; k < i; k++)
                {
                    Native.RECT o = wins[k].R;
                    if (o.Top <= w.R.Top + 2 && o.Bottom >= w.R.Top) Subtract(segs, o.Left, o.Right);
                }
                if (!houseRect.IsEmpty && houseRect.Top <= w.R.Top && houseRect.Bottom >= w.R.Top) Subtract(segs, houseRect.Left, houseRect.Right);
                foreach (double[] sg in segs)
                    if (sg[1] - sg[0] > 60 * b.U) ps.Add(new Plat(sg[0], sg[1], w.R.Top, Plat.Window, w.H));
            }
            Plats = ps;
            Wins = wins;
        }

        static void Subtract(List<double[]> segs, double l, double r)
        {
            List<double[]> outp = new List<double[]>();
            foreach (double[] s in segs)
            {
                if (r <= s[0] || l >= s[1]) { outp.Add(s); continue; }
                if (l > s[0]) outp.Add(new double[] { s[0], l });
                if (r < s[1]) outp.Add(new double[] { r, s[1] });
            }
            segs.Clear();
            segs.AddRange(outp);
        }

        // The platform he would land on when falling from yOld to yNew at x.
        public static Plat LandingOn(double x, double yOld, double yNew)
        {
            Plat best = null;
            foreach (Plat p in Plats)
            {
                if (x < p.L || x > p.R) continue;
                if (yOld <= p.Y + 0.5 && yNew >= p.Y && (best == null || p.Y < best.Y)) best = p;
            }
            return best;
        }

        // The first thing below (or at) y.
        public static Plat SurfaceBelow(double x, double y)
        {
            Plat best = null;
            foreach (Plat p in Plats)
                if (x >= p.L && x <= p.R && p.Y >= y - 3 && (best == null || p.Y < best.Y)) best = p;
            if (best == null)
            {
                Screen sc = Screen.FromPoint(new Point((int)x, (int)y));
                best = new Plat(sc.WorkingArea.Left, sc.WorkingArea.Right, sc.WorkingArea.Bottom, Plat.Floor, IntPtr.Zero);
            }
            return best;
        }

        public static Plat Find(Support s, double x)
        {
            Plat best = null;
            foreach (Plat p in Plats)
                if (p.Same(s) && (best == null || Dist(p, x) < Dist(best, x))) best = p;
            return best;
        }

        static double Dist(Plat p, double x) { return x < p.L ? p.L - x : x > p.R ? x - p.R : 0; }

        public static WinInfo Info(IntPtr h)
        {
            foreach (WinInfo w in Wins) if (w.H == h) return w;
            return null;
        }
    }
}
