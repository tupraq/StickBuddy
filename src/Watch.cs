using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // Notices what you are doing: watching YouTube (he sits down and watches with you)
    // and new downloads (he says something about them). Nothing leaves your computer.
    public partial class Buddy
    {
        // ---------------- YouTube ----------------
        double nextWatchCheck, ytLostAt;
        public bool WatchingYt;
        string ytTitle = "";
        IntPtr ytWindow;

        static bool IsBrowser(string p)
        {
            if (p == null) return false;
            p = p.ToLowerInvariant();
            return p == "chrome" || p == "msedge" || p == "firefox" || p == "brave" || p == "opera" || p == "vivaldi" || p == "iexplore";
        }

        // "(3) Funny cats - YouTube - Google Chrome" -> "Funny cats"
        static string VideoTitle(string title)
        {
            int i = title.IndexOf(" - YouTube", StringComparison.Ordinal);
            if (i <= 0) return null;
            string t = title.Substring(0, i).Trim();
            t = Regex.Replace(t, @"^\(\d+\)\s*", "");
            if (t.Length == 0 || t.Equals("YouTube", StringComparison.OrdinalIgnoreCase)) return null;
            return t;
        }

        double ytFakeUntil;

        void UpdateWatching(double now)
        {
            if (now < ytFakeUntil) return;
            if (ytFakeUntil > 0) { ytFakeUntil = 0; WatchingYt = false; }
            if (now < nextWatchCheck) return;
            nextWatchCheck = now + 1;
            if (!S.WatchScreen) { WatchingYt = false; return; }
            IntPtr fg = Native.GetForegroundWindow();
            string title = Native.Title(fg);
            string video = title.Contains("YouTube") && IsBrowser(Native.ProcessName(fg)) ? VideoTitle(title) : null;
            if (video != null)
            {
                ytLostAt = 0;
                ytWindow = fg;
                if (!WatchingYt)
                {
                    bool free = (!Busy || IsIdleJob) && !Sleeping && !Hiding && !RagMode && !dragging && CursorMode == CursorTrick.None && Brain.Angry < 50;
                    if (free)
                    {
                        WatchingYt = true; ytTitle = video;
                        RunIdleJob(WatchYoutube);
                    }
                }
                else if (video != ytTitle)
                {
                    ytTitle = video;
                    CommentOnVideo(video, false);
                }
            }
            else if (WatchingYt)
            {
                if (ytLostAt == 0) ytLostAt = now;
                else if (now - ytLostAt > 4) WatchingYt = false;
            }
        }

        async Task WatchYoutube(CancellationToken ct)
        {
            try
            {
                await GoToFloor(ct);
                Native.RECT r = Native.GetVisibleRect(ytWindow);
                Rectangle wa = Screen.FromHandle(ytWindow).WorkingArea;
                double spot = Clamp(r.Left + 90 * U, wa.Left + 60 * U, wa.Right - 60 * U);
                if (House != null && House.Shown && House.InsideX(spot)) spot = House.Sx(House.OuterW + 70);
                await MoveTo(spot, false, ct);
                Facing = (r.Left + r.Right) / 2.0 >= X ? 1 : -1;
                BaseAct = Act.Watch;
                HeldProp = Prop.Popcorn;
                CommentOnVideo(ytTitle, true);
                Brain.Event("you sat down to watch a YouTube video with the user: \"" + ytTitle + "\"");
                double nextReact = Now + 20 + Rnd.NextDouble() * 25;
                while (WatchingYt)
                {
                    await Wait(0.5, ct);
                    if (Now > nextReact)
                    {
                        nextReact = Now + 25 + Rnd.NextDouble() * 35;
                        double k = Rnd.NextDouble();
                        if (k < 0.35) { ShowBubble(Pick("Hahaha!", "Hehe!", "LOL!"), 1.6); BaseAct = Act.Laugh; await Wait(1.6, ct); BaseAct = Act.Watch; }
                        else if (k < 0.7) ShowBubble(Pick("Ooh!", "Whoa!", "*munch munch*", "This is so good!", "No way!"), 1.6);
                        else ShowBubble(Pick("*munch*", "Can we watch another one after this?"), 2);
                    }
                }
                BaseAct = Act.Idle; HeldProp = Prop.None;
                ShowBubble(Pick("That was a good video!", "Aww, is it over?", "Fun!"), 2);
                await Wait(1.5, ct);
            }
            finally { HeldProp = Prop.None; if (!WatchingYt) { } }
        }

        void CommentOnVideo(string video, bool first)
        {
            string v = video.ToLowerInvariant();
            string shortT = video.Length > 40 ? video.Substring(0, 38) + "..." : video;
            string line;
            if (Regex.IsMatch(v, @"minecraft")) line = "Minecraft! I love building stuff!";
            else if (Regex.IsMatch(v, @"roblox")) line = "Roblox! Can I play next?";
            else if (Regex.IsMatch(v, @"stick ?(man|figure)|animat")) line = "Hey! That one looks like me!";
            else if (Regex.IsMatch(v, @"\b(music|song|lyrics|official video|remix|beat)\b")) { line = "Ooh, music! *dances in my seat*"; AddNotes(3); }
            else if (Regex.IsMatch(v, @"\b(funny|meme|fail|prank|lol|comedy)\b")) line = "Ooh, this is gonna be funny!";
            else if (Regex.IsMatch(v, @"\b(cat|cats|dog|dogs|puppy|kitten|animal|animals)\b")) { line = "Awww, so cute!"; AddHearts(3); }
            else if (Regex.IsMatch(v, @"\b(scary|horror|creepy|ghost)\b")) { line = "Eek! I'm scared! Hold me!"; FlashAct(Act.Scared, 2); }
            else if (Regex.IsMatch(v, @"\b(how to|tutorial|learn|explained)\b")) line = "Ooh, learning stuff! I'm taking notes.";
            else if (Regex.IsMatch(v, @"\b(game|gaming|gameplay|lets play|speedrun)\b")) line = "Ooh, gaming! Who's winning?";
            else line = first ? Pick("Ooh, YouTube! Can I watch too?", "Movie time! I'll get the popcorn!") : Pick("Ooh, a new one: \"" + shortT + "\"!", "Next video!");
            // bubbles only, so he doesn't talk over your video
            ShowBubble(line, 4);
        }

        // ---------------- downloads ----------------
        FileSystemWatcher dlWatcher;
        readonly Dictionary<string, double> dlPending = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> dlSize = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, double> dlSeen = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        void StartWatchers()
        {
            try
            {
                string dir = Native.KnownFolder(Native.FolderDownloads);
                if (dir == null) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(dir)) return;
                dlWatcher = new FileSystemWatcher(dir);
                dlWatcher.IncludeSubdirectories = false;
                dlWatcher.NotifyFilter = NotifyFilters.FileName;
                dlWatcher.Created += delegate (object s, FileSystemEventArgs e) { QueueDownload(e.FullPath); };
                dlWatcher.Renamed += delegate (object s, RenamedEventArgs e) { QueueDownload(e.FullPath); };
                dlWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("download watcher: " + ex.Message); }
        }

        void StopWatchers()
        {
            try { if (dlWatcher != null) { dlWatcher.EnableRaisingEvents = false; dlWatcher.Dispose(); } } catch { }
        }

        static bool TempFile(string name)
        {
            string e = Path.GetExtension(name).ToLowerInvariant();
            return e == ".crdownload" || e == ".tmp" || e == ".part" || e == ".partial" || e == ".download" || e == ".opdownload" ||
                   name.StartsWith("~") || name.StartsWith(".") || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                   Regex.IsMatch(name, @"^Unconfirmed \d+", RegexOptions.IgnoreCase);
        }

        void QueueDownload(string path)
        {
            try
            {
                if (TempFile(Path.GetFileName(path))) return;
                BeginInvoke(new Action(delegate { dlPending[path] = Now + 1.5; }));
            }
            catch { }
        }

        void UpdateDownloads(double now)
        {
            if (dlPending.Count == 0) return;
            List<string> ready = new List<string>();
            foreach (KeyValuePair<string, double> kv in dlPending) if (now >= kv.Value) ready.Add(kv.Key);
            foreach (string path in ready)
            {
                dlPending.Remove(path);
                try
                {
                    if (!File.Exists(path)) continue;
                    long size = new FileInfo(path).Length;
                    long old;
                    if (!dlSize.TryGetValue(path, out old) || old != size) { dlSize[path] = size; dlPending[path] = now + 1.5; continue; } // still growing
                    dlSize.Remove(path);
                    double seenAt;
                    if (dlSeen.TryGetValue(path, out seenAt) && now - seenAt < 60) continue;
                    dlSeen[path] = now;
                    if (S.WatchScreen) ReactToDownload(path, size);
                }
                catch { }
            }
        }

        static string NiceSize(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " gigabytes";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0") + " megabytes";
            return (b / 1024.0).ToString("0") + " kilobytes";
        }

        void ReactToDownload(string path, long size)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string shortN = name.Length > 32 ? name.Substring(0, 30) + "..." : name;
            string n = name.ToLowerInvariant();
            string line;
            Act act = Act.Point;
            switch (ext)
            {
                case ".exe": case ".msi": case ".msix": case ".appx": case ".msixbundle":
                    if (Regex.IsMatch(n, @"roblox|minecraft|steam|epic|game|fortnite|launcher"))
                        line = "Ooh, you downloaded " + shortN + "! Is that a game? Can I watch you play?";
                    else line = "You downloaded a program called " + shortN + ". Remember: only open programs from websites you trust!";
                    act = Act.Think;
                    break;
                case ".zip": case ".rar": case ".7z": case ".tar": case ".gz":
                    line = "A zip file! I wonder what's packed inside " + shortN + "."; act = Act.Think; break;
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".webp": case ".bmp": case ".heic":
                    line = Pick("A new picture! " + shortN + ". Is it a nice one?", "Ooh, you saved a picture!"); break;
                case ".mp4": case ".mov": case ".avi": case ".mkv": case ".webm":
                    line = "A video! Let's watch it together!"; act = Act.Cheer; break;
                case ".mp3": case ".wav": case ".ogg": case ".flac": case ".m4a":
                    line = "Music! Play it, play it!"; act = Act.DanceGroove; break;
                case ".pdf": case ".doc": case ".docx": case ".txt": case ".ppt": case ".pptx": case ".xls": case ".xlsx":
                    line = "A document called " + shortN + ". Is it homework?"; act = Act.Think; break;
                case ".jar": case ".mcworld": case ".mcpack": case ".mcaddon":
                    line = "Minecraft stuff?! So cool!"; act = Act.Cheer; break;
                case ".apk":
                    line = "That's an app for a phone!"; break;
                case ".iso": case ".img":
                    line = "Whoa, a whole disk image!"; break;
                default:
                    line = "You downloaded " + shortN + "!"; break;
            }
            if (size > 500L << 20) line += " And it's huge: " + NiceSize(size) + "!";
            else if (size < 10 * 1024) line += " It's a tiny one.";
            Log.Write("download: " + ext + " " + size);
            Brain.Event("the user downloaded a " + (ext.Length > 0 ? ext.TrimStart('.') + " " : "") + "file");
            if (WatchingYt || Hiding || (Busy && !IsIdleJob)) { ShowBubble(line, 5); return; }
            Touch();
            Act a = act;
            RunJob(async delegate (CancellationToken ct)
            {
                await WaitGround(ct);
                Rectangle wa = Screen.FromPoint(new Point((int)X, (int)Y - 10)).WorkingArea;
                Facing = wa.Right - X < X - wa.Left ? 1 : -1;
                Say(line);
                await Do(a, a == Act.DanceGroove ? 4 : 1.8, ct);
            });
        }
    }
}
