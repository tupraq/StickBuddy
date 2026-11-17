using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace StickBuddy
{
    // Hide and seek: he hides behind one of your files in File Explorer (Downloads, Documents, Pictures...).
    // He only shows when that folder is open, the file is scrolled into view, and no other window covers it.
    // He never opens, moves or changes any file.
    public partial class Buddy
    {
        enum HideState { None, Counting, Hidden }
        HideState hideState = HideState.None;
        public bool Hiding { get { return hideState != HideState.None; } }
        string hideFolder, hideFolderName, hideItem, hideItemNoExt;
        volatile bool hideTracking;
        Rectangle hideItemRect = Rectangle.Empty;   // where the file is on screen (from the tracker thread)
        IntPtr hideHwnd = IntPtr.Zero;
        bool hideVisible;
        double hideStart, nextHint, nextVisCheck;
        int hintsGiven;
        Rectangle hideIcon = Rectangle.Empty, hideIconFromUia = Rectangle.Empty;
        readonly object hideLock = new object();

        public void StartHideAndSeek()
        {
            Touch();
            if (Hiding) return;
            RunJob(async delegate (CancellationToken ct)
            {
                await WaitGround(ct);
                string[] pick = PickHidingSpot();
                if (pick == null)
                {
                    await SayWait("Hmm, your folders are all empty, there's nowhere to hide! Let's play tag instead!", ct);
                    StartTag(true);
                    return;
                }
                hideFolder = pick[0]; hideFolderName = pick[1]; hideItem = pick[2];
                hideItemNoExt = Path.GetFileNameWithoutExtension(hideItem);
                Log.Write("hide and seek: hiding in " + hideFolderName);
                BaseAct = Act.Cheer;
                await SayWait("Hide and seek! Close your eyes and count to 10. No peeking! I'll hide somewhere in your files.", ct);
                BaseAct = Act.Idle;
                hideState = HideState.Counting;
                Overlay.Countdown(10, "Close your eyes!  " + S.Name + " is hiding...");
                try
                {
                    if (Environment.GetEnvironmentVariable("STICKBUDDY_TESTHIDE") != null) Process.Start("explorer.exe", "\"" + hideFolder + "\"");
                    else Process.Start("explorer.exe");
                }
                catch { }
                for (int i = 10; i >= 1; i--)
                {
                    Overlay.Countdown(i, i > 3 ? "Close your eyes!  " + S.Name + " is hiding..." : "Almost ready...");
                    await Wait(1, ct);
                }
                Overlay.ClearOverlay();
                hideState = HideState.Hidden;
                hideStart = Now; nextHint = Now + 40; hintsGiven = 0;
                StartHideTracker();
                Say("Ready or not, come find me! I'm hiding in File Explorer. Click me when you see me!");
                double end = Now + 200;
                while (Hiding && Now < end) await Wait(0.25, ct);
                if (Hiding)
                {
                    RevealAfterSeek(false);
                    Say("Time's up! I was hiding in " + hideFolderName + ", behind " + hideItemNoExt + ". I win! Hehe!");
                }
            });
        }

        // Picks a folder and a file to hide behind.
        string[] PickHidingSpot()
        {
            List<string[]> folders = new List<string[]>();
            string dl = Native.KnownFolder(Native.FolderDownloads);
            if (dl != null) { folders.Add(new string[] { dl, "Downloads" }); folders.Add(new string[] { dl, "Downloads" }); }
            folders.Add(new string[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents" });
            folders.Add(new string[] { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures" });
            folders.Add(new string[] { Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music" });
            folders.Add(new string[] { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Videos" });
            folders.Add(new string[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop" });
            for (int tries = 0; tries < 12; tries++)
            {
                string[] f = folders[Rnd.Next(folders.Count)];
                try
                {
                    if (string.IsNullOrEmpty(f[0]) || !Directory.Exists(f[0])) continue;
                    List<string> items = new List<string>();
                    foreach (string e in Directory.GetFileSystemEntries(f[0]))
                    {
                        string n = Path.GetFileName(e);
                        if (n.StartsWith(".") || n.StartsWith("~$") || n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        FileAttributes a = File.GetAttributes(e);
                        if ((a & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        items.Add(n);
                        if (items.Count > 400) break;
                    }
                    if (items.Count < 2) continue;
                    return new string[] { f[0], f[1], items[Rnd.Next(items.Count)] };
                }
                catch { }
            }
            return null;
        }

        // ---------- finding where the file is on screen (runs on its own thread) ----------
        void StartHideTracker()
        {
            hideTracking = true;
            Thread t = new Thread(HideTrackerLoop);
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        static object ComGet(object o, string name)
        {
            return o == null ? null : o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        void HideTrackerLoop()
        {
            object shell = null;
            AutomationElement cached = null;
            IntPtr cachedHwnd = IntPtr.Zero;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); } catch (Exception ex) { Log.Write("hide shell: " + ex.Message); }
            string folder = hideFolder, full = hideItem, noExt = hideItemNoExt;
            int loops = 0;
            while (hideTracking)
            {
                Rectangle found = Rectangle.Empty;
                IntPtr foundHwnd = IntPtr.Zero;
                int winCount = 0;
                try
                {
                    // 1. which Explorer windows show the hiding folder?
                    List<IntPtr> wins = new List<IntPtr>();
                    if (shell != null)
                    {
                        object list = shell.GetType().InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
                        int n = Convert.ToInt32(ComGet(list, "Count"));
                        for (int i = 0; i < n; i++)
                        {
                            try
                            {
                                object w = list.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, list, new object[] { i });
                                if (w == null) continue;
                                object self = ComGet(ComGet(ComGet(w, "Document"), "Folder"), "Self");
                                string path = Convert.ToString(ComGet(self, "Path"));
                                if (string.Equals(path.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                                {
                                    IntPtr h = new IntPtr(Convert.ToInt64(ComGet(w, "HWND")));
                                    if (!wins.Contains(h)) wins.Add(h);
                                }
                            }
                            catch { }
                        }
                    }
                    // 2. where is the file in that window?
                    winCount = wins.Count;
                    foreach (IntPtr h in wins)
                    {
                        if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) continue;
                        AutomationElement item = null;
                        if (cached != null && cachedHwnd == h)
                        {
                            try { System.Windows.Rect test = cached.Current.BoundingRectangle; item = cached; }
                            catch { cached = null; }
                        }
                        if (item == null)
                        {
                            AutomationElement root = AutomationElement.FromHandle(h);
                            Condition byName = new OrCondition(new PropertyCondition(AutomationElement.NameProperty, full), new PropertyCondition(AutomationElement.NameProperty, noExt));
                            item = root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem), byName));
                            cached = item; cachedHwnd = h;
                        }
                        if (item == null) continue;
                        System.Windows.Rect r = item.Current.BoundingRectangle;
                        if (item.Current.IsOffscreen || r.IsEmpty || r.Width < 4 || r.Height < 4) continue;
                        // keep it inside the list area (when half scrolled out)
                        AutomationElement listEl = TreeWalker.ControlViewWalker.GetParent(item);
                        while (listEl != null && listEl.Current.ControlType != ControlType.List) listEl = TreeWalker.ControlViewWalker.GetParent(listEl);
                        if (listEl != null)
                        {
                            System.Windows.Rect lr = listEl.Current.BoundingRectangle;
                            if (r.Top < lr.Top + 2 || r.Bottom > lr.Bottom - 2) continue;
                        }
                        found = new Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height);
                        foundHwnd = h;
                        // the file's name label tells us where the little picture is
                        Rectangle icon = Rectangle.Empty;
                        try
                        {
                            AutomationElement nameEl = item.FindFirst(TreeScope.Descendants, new OrCondition(
                                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)));
                            if (nameEl != null)
                            {
                                System.Windows.Rect nr = nameEl.Current.BoundingRectangle;
                                if (!nr.IsEmpty && nr.Top > r.Top + r.Height * 0.35)
                                {
                                    double ah = nr.Top - r.Top, aw = r.Width;     // picture above the name (icon views)
                                    icon = new Rectangle((int)(r.Left + aw * 0.06), (int)(r.Top + ah * 0.18), (int)(aw * 0.88), (int)(ah * 0.74));
                                }
                                else if (!nr.IsEmpty && nr.Left > r.Left + 6)
                                {
                                    int s = (int)Math.Max(10, r.Height * 0.72);  // picture left of the name (details / list view)
                                    icon = new Rectangle((int)nr.Left - 4 - s, (int)(r.Top + (r.Height - s) / 2), s, s);
                                }
                            }
                        }
                        catch { }
                        lock (hideLock) { hideIconFromUia = icon; }
                        break;
                    }
                }
                catch (Exception ex) { Log.Write("hide tracker: " + ex.Message); cached = null; }
                if (++loops % 20 == 1) Log.Write("hide tracker: folder windows " + winCount + ", file on screen " + !found.IsEmpty);
                lock (hideLock) { hideItemRect = found; hideHwnd = foundHwnd; }
                Thread.Sleep(250);
            }
        }

        // Is this point of the screen showing that window (not covered by another one)?
        bool PointShowsWindow(Point p, IntPtr target)
        {
            bool result = false, done = false;
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (done) return false;
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                if (Native.ProcessId(h) == myPid) return true;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
                if (Native.IsCloaked(h)) return true;
                Native.RECT r = Native.GetVisibleRect(h);
                if (p.X < r.Left || p.X >= r.Right || p.Y < r.Top || p.Y >= r.Bottom) return true;
                result = h == target; done = true;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        void UpdateHideSeek(double now)
        {
            if (hideState != HideState.Hidden) return;
            if (now > nextVisCheck)
            {
                nextVisCheck = now + 0.1;
                Rectangle r, ui; IntPtr h;
                lock (hideLock) { r = hideItemRect; h = hideHwnd; ui = hideIconFromUia; }
                if (r.IsEmpty) { hideVisible = false; hideIcon = Rectangle.Empty; }
                else
                {
                    hideIcon = ui.Width > 4 ? ui : IconRect(r);
                    Point c = new Point(hideIcon.Left + hideIcon.Width / 2, hideIcon.Top + hideIcon.Height / 2);
                    bool vis = PointShowsWindow(c, h);
                    if (vis != hideVisible) Log.Write("hide spot " + (vis ? "visible" : "covered") + " item " + r + " icon " + hideIcon);
                    hideVisible = vis;
                }
            }
            if (now > nextHint)
            {
                nextHint = now + 40;
                hintsGiven++;
                if (hintsGiven == 1) Say("Hint: I'm hiding in your " + hideFolderName + " folder!");
                else if (hintsGiven == 2) Say("Hint: look for a file called " + hideItemNoExt + "! You might have to scroll.");
                else Say(Pick("Hehe! You're getting warmer... maybe!", "I'm still here! Hehe!"));
            }
        }

        // Where the little picture of the file is, inside the file's row/tile.
        Rectangle IconRect(Rectangle r)
        {
            if (r.Width > r.Height * 2.2)
            {
                int s = Math.Max(10, (int)(r.Height * 0.72));
                return new Rectangle(r.Left + (int)(5 * Dpi), r.Top + (r.Height - s) / 2, s, s);
            }
            return new Rectangle(r.Left + (int)(r.Width * 0.06), r.Top + (int)(r.Height * 0.12), (int)(r.Width * 0.88), (int)(r.Height * 0.58));
        }

        // Draws him peeking out from behind the file's icon (the icon covers the rest of him).
        bool RenderHiding(double now)
        {
            if (hideState == HideState.None) return false;
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                if (hideState == HideState.Hidden && hideVisible && !hideIcon.IsEmpty)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float uh = (float)(Math.Min(hideIcon.Height, hideIcon.Width * 1.2) / 52.0);
                    int wx = hideIcon.Left + hideIcon.Width / 2 - W / 2, wy = hideIcon.Bottom + 2 - (int)AnchorY;
                    float ax = W / 2f, ay = AnchorY;
                    Rectangle iconLocal = new Rectangle(hideIcon.Left - wx, hideIcon.Top - wy, hideIcon.Width, hideIcon.Height);
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(3, 128, 128, 128)))
                    {
                        Rectangle hitR = iconLocal; hitR.Inflate((int)(8 * Dpi), (int)(14 * Dpi));
                        g.FillRectangle(hb, hitR);
                    }
                    Pose p = Poses.Get(Act.Peek, now - hideStart, 0);
                    // every now and then he ducks right down
                    double duck = Math.Max(0, Math.Sin((now - hideStart) * 0.7)) > 0.85 ? 1 : 0;
                    p.HipY -= duck * 8;
                    Joints j = Renderer.Compute(p, ax, ay, 1, uh);
                    using (Region clip = new Region(new Rectangle(0, 0, W, H)))
                    {
                        clip.Exclude(iconLocal);
                        g.Clip = clip;
                        Renderer.DrawFigure(g, j, 1, uh, BodyColorNow(now), Prop.None, now, false);
                        g.ResetClip();
                    }
                    Native.UpdateLayered(Handle, canvas, wx, wy);
                    return true;
                }
            }
            Native.UpdateLayered(Handle, canvas, -10000, -10000);
            return true;
        }

        bool HideClick()
        {
            if (hideState == HideState.Counting) return true;
            if (hideState != HideState.Hidden) return false;
            if (hideVisible) FoundMe();
            return true;
        }

        public void FoundMe()
        {
            if (hideState != HideState.Hidden) return;
            RevealAfterSeek(true);
            Brain.Event("the user found you hiding in their " + hideFolderName + " folder (hide and seek)");
            Brain.Happy = Math.Min(100, Brain.Happy + 15);
            Say(Pick("Aww, you found me! Good job!", "Hehe, you found me! Want to play again?", "Nooo! You found me!"));
            FlashAct(Act.Cheer, 2);
        }

        public void GiveUpHideSeek()
        {
            if (hideState != HideState.Hidden) return;
            RevealAfterSeek(false);
            Say("Hehe! I was hiding in " + hideFolderName + ", behind " + hideItemNoExt + "! I win!");
            FlashAct(Act.Cheer, 2);
        }

        // Comes out of hiding: pops out where he was (if you can see it), or drops in from the top.
        void RevealAfterSeek(bool fromSpot)
        {
            hideTracking = false;
            hideState = HideState.None;
            Overlay.ClearOverlay();
            CancelJob();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (fromSpot && !hideIcon.IsEmpty)
            {
                X = hideIcon.Left + hideIcon.Width / 2.0; Y = hideIcon.Bottom;
            }
            else { X = wa.Left + wa.Width * 0.5; Y = wa.Top + 120 * U; }
            OnGround = false; VX = Facing * 120 * Dpi; VY = -500 * Dpi; Sup = Support.Floor;
            RollNextLanding = true;
            RagMode = false;
            lieUntil = 0; kneelUntil = 0;
            shown = Poses.Get(Act.Jump, 0, 0); blendFrom = shown;
            Touch();
        }
    }
}
