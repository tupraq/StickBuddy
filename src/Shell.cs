using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StickBuddy
{
    public class AppEntry
    {
        public string Name, Id;
    }

    // Every app on the computer (the same list as the Start menu's "All apps").
    public static class AppIndex
    {
        static List<AppEntry> apps = new List<AppEntry>();
        static readonly object lk = new object();
        public static volatile bool Loaded;

        public static void LoadAsync()
        {
            Thread t = new Thread(Load);
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static object Call(object o, string name, BindingFlags kind, params object[] args)
        {
            return o.GetType().InvokeMember(name, kind, null, o, args);
        }

        static void Load()
        {
            List<AppEntry> list = new List<AppEntry>();
            try
            {
                Type st = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(st);
                object folder = Call(shell, "NameSpace", BindingFlags.InvokeMethod, "shell:AppsFolder");
                object items = Call(folder, "Items", BindingFlags.InvokeMethod);
                int n = (int)Call(items, "Count", BindingFlags.GetProperty);
                for (int i = 0; i < n; i++)
                {
                    object it = Call(items, "Item", BindingFlags.InvokeMethod, i);
                    string name = Call(it, "Name", BindingFlags.GetProperty) as string;
                    string path = Call(it, "Path", BindingFlags.GetProperty) as string;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                    {
                        AppEntry e = new AppEntry();
                        e.Name = name; e.Id = path;
                        list.Add(e);
                    }
                }
            }
            catch (Exception ex) { Log.Write("apps: " + ex.Message); }
            lock (lk) apps = list;
            Loaded = true;
            Log.Write("apps found: " + list.Count);
        }

        static string Clean(string s) { return Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9 ]", " ").Trim(); }

        public static AppEntry Find(string query)
        {
            string q = Clean(query);
            if (q.Length == 0) return null;
            string[] qw = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            AppEntry best = null;
            int bestScore = 0;
            List<AppEntry> copy;
            lock (lk) copy = apps;
            foreach (AppEntry a in copy)
            {
                string n = Regex.Replace(Clean(a.Name), "\\s+", " ");
                if (Regex.IsMatch(n, @"\b(uninstall|readme|help|documentation|support center|release notes)\b") && !q.Contains("uninstall")) continue;
                int s = 0;
                if (n == q) s = 100;
                else if (n.StartsWith(q + " ")) s = 85;
                else if (Array.IndexOf(n.Split(' '), q) >= 0) s = 75;
                else if (n.Contains(q)) s = 60;
                else
                {
                    bool all = true;
                    foreach (string w in qw) if (!n.Contains(w)) { all = false; break; }
                    if (all) s = 50;
                }
                if (s == 0) continue;
                s = s * 100 - n.Length; // shorter names win ties
                if (s > bestScore) { bestScore = s; best = a; }
            }
            return best;
        }

        public static void Launch(AppEntry a)
        {
            string id = a.Id;
            if (id.StartsWith("http://") || id.StartsWith("https://")) { Process.Start(id); return; }
            if (id.Length > 2 && id[1] == ':' && File.Exists(id)) { Process.Start(id); return; }
            Process.Start("explorer.exe", "shell:AppsFolder\\" + id);
        }
    }

    // Finds things on the Windows taskbar.
    static class Shell
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        public static Rectangle FindStartButton()
        {
            try
            {
                IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) return Rectangle.Empty;
                AutomationElement root = AutomationElement.FromHandle(tray);
                AutomationElement el = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
                if (el == null) return Rectangle.Empty;
                System.Windows.Rect r = el.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width < 4) return Rectangle.Empty;
                return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            }
            catch (Exception ex)
            {
                Log.Write("start button: " + ex.Message);
                return Rectangle.Empty;
            }
        }

        public static Rectangle GuessStartButton(double dpi)
        {
            Screen s = Screen.PrimaryScreen;
            Rectangle b = s.Bounds, wa = s.WorkingArea;
            int size = (int)(48 * dpi);
            bool left = false;
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", null);
                if (v is int && (int)v == 0) left = true;
            }
            catch { }
            int x = left ? b.Left + 4 : b.Left + b.Width / 2 - (int)(200 * dpi);
            if (!left)
            {
                // On a centred taskbar the Start button sits just left of the app icons.
                IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                IntPtr rebar = tray == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
                Native.RECT r;
                if (rebar != IntPtr.Zero && Native.GetWindowRect(rebar, out r) && r.Width > 0)
                    x = Math.Max(b.Left + 4, r.Left - (int)(3.2 * size));
            }
            return new Rectangle(x, wa.Bottom, size, size);
        }
    }
}
