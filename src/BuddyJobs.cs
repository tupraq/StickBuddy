using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // Things the stickman can do on your computer.
    public partial class Buddy
    {
        Rectangle startRect = Rectangle.Empty;
        double startRectTime = -100;
        IntPtr notepad = IntPtr.Zero;

        // ---------- the Start button ----------
        public async Task<Rectangle> GetStartButton()
        {
            if (Now - startRectTime < 30 && startRect.Width > 0) return startRect;
            Task<Rectangle> t = Task.Run(delegate { return Shell.FindStartButton(); });
            Task done = await Task.WhenAny(t, Task.Delay(2500));
            Rectangle r = Rectangle.Empty;
            if (done == t && t.Status == TaskStatus.RanToCompletion) r = t.Result;
            if (r.Width <= 0) r = Shell.GuessStartButton(Dpi);
            startRect = r;
            startRectTime = Now;
            return r;
        }

        // Runs to the Start button and stomps on it.
        public async Task StompStart(CancellationToken ct)
        {
            Rectangle sb = await GetStartButton();
            ct.ThrowIfCancellationRequested();
            double sx = sb.Left + sb.Width / 2.0;
            await RunTo(sx, ct);
            Facing = 1;
            await JumpTo(sx, FloorAt(sx), Support.Floor, true, 0.42, 55 * U, ct, Act.Jump, 0.1);
            landUntil = 0;
            BaseAct = Act.Stomp;
            AddPow(X, Y);
        }

        // Opens the Start menu and types, like a person would.
        static bool IsStartMenu(IntPtr h)
        {
            return h != IntPtr.Zero && Native.ClassName(h) == "Windows.UI.Core.CoreWindow";
        }

        // Stomps on Start, opens the Start menu and types the app name, like a person would.
        // He never presses Enter there (Windows Search can turn that into a web search);
        // he closes the menu and starts the app himself, which always works.
        // Letters are only typed while the Start menu is really in front.
        public async Task StartMenuShow(string query, CancellationToken ct)
        {
            await StompStart(ct);
            Native.Tap(Native.VK_LWIN);
            IntPtr menuWin = await WaitForForeground(IsStartMenu, 3, ct);
            if (menuWin == IntPtr.Zero) { BaseAct = Act.Idle; return; }
            await Wait(0.4, ct);
            BaseAct = Act.Type;
            foreach (char c in query)
            {
                ct.ThrowIfCancellationRequested();
                if (Native.GetForegroundWindow() != menuWin) { BaseAct = Act.Idle; return; }
                Native.TypeChar(c);
                await Wait(0.06 + Rnd.NextDouble() * 0.07, ct);
            }
            await Wait(0.6, ct);
            if (Native.GetForegroundWindow() == menuWin) Native.Tap(Native.VK_ESCAPE);
            BaseAct = Act.Idle;
        }

        public void OpenApp(string query, string display, bool announce)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                double until = Now + 4;
                while (!AppIndex.Loaded && Now < until) await Wait(0.2, ct);
                AppEntry app = AppIndex.Find(query);
                if (app == null)
                {
                    FlashAct(Act.Shrug, 1.5);
                    await SayWait("Hmm, I can't find " + display + " on this computer. Let me look for it on the internet!", ct);
                    WebSearch(display, false, null);
                    return;
                }
                if (announce) Say(Pick("Opening " + display + "!", "On it! Opening " + display + ".", "Okay, let me open " + display + "!"));
                await StartMenuShow(query, ct);
                AppIndex.Launch(app);
                Log.Write("launched: " + app.Name + " (" + app.Id + ")");
                await Wait(0.6, ct);
                await Do(Act.Cheer, 1.2, ct);
            });
        }

        public void OpenUrl(string url, string speech)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                if (!string.IsNullOrEmpty(speech)) Say(speech);
                await StompStart(ct);
                Process.Start(url);
                await Wait(0.4, ct);
                BaseAct = Act.Idle;
                await Do(Act.Point, 1.2, ct);
            });
        }

        public void WebSearch(string query, bool youtube, string speech)
        {
            string url = youtube
                ? "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query)
                : "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
            OpenUrl(url, speech);
        }

        // ---------- Notepad ----------
        static bool IsNotepad(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            if (Native.ClassName(h) == "Notepad") return true;
            string p = Native.ProcessName(h);
            return p != null && p.Equals("notepad", StringComparison.OrdinalIgnoreCase) && Native.IsWindowVisible(h);
        }

        async Task<IntPtr> WaitForForeground(Func<IntPtr, bool> test, double secs, CancellationToken ct)
        {
            double end = Now + secs;
            while (Now < end)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (test(fg)) return fg;
                await Wait(0.15, ct);
            }
            return IntPtr.Zero;
        }

        static List<IntPtr> NotepadWindows()
        {
            List<IntPtr> list = new List<IntPtr>();
            foreach (IntPtr h in Native.TopWindows()) if (Native.ClassName(h) == "Notepad") list.Add(h);
            return list;
        }

        static bool LooksNew(IntPtr h)
        {
            string t = Native.Title(h).TrimStart('*');
            return t.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase);
        }

        // Waits for the Notepad he just opened: a brand new window, or a new "Untitled" tab in front.
        async Task<IntPtr> WaitForNewNotepad(List<IntPtr> before, double secs, CancellationToken ct)
        {
            double end = Now + secs;
            while (Now < end)
            {
                foreach (IntPtr h in NotepadWindows()) if (!before.Contains(h)) return h;
                IntPtr fg = Native.GetForegroundWindow();
                if (Native.ClassName(fg) == "Notepad" && LooksNew(fg)) return fg;
                await Wait(0.2, ct);
            }
            return IntPtr.Zero;
        }

        string notepadTitle = "";

        public async Task<IntPtr> EnsureNotepad(CancellationToken ct)
        {
            // Only reuse the Notepad he opened himself (and only if it still shows his page),
            // so he never scribbles in your own files.
            if (notepad != IntPtr.Zero && Native.IsWindow(notepad) && Native.IsWindowVisible(notepad) && Native.Title(notepad) == notepadTitle)
            {
                Native.ForceForeground(notepad);
                await Wait(0.3, ct);
                return notepad;
            }
            List<IntPtr> before = NotepadWindows();
            await StartMenuShow("notepad", ct);
            Process.Start("notepad.exe");
            BaseAct = Act.Think;
            IntPtr h = await WaitForNewNotepad(before, 25, ct);
            BaseAct = Act.Idle;
            if (h == IntPtr.Zero) throw new Exception("Notepad didn't open.");
            Native.ForceForeground(h);
            await Wait(0.6, ct);
            notepad = h;
            return h;
        }

        // Walks to Notepad, climbs on it and scribbles the text in, letter by letter.
        public async Task WriteInNotepad(string text, CancellationToken ct)
        {
            IntPtr h = await EnsureNotepad(ct);
            IntPtr edit = IntPtr.Zero;
            for (int i = 0; i < 20 && edit == IntPtr.Zero; i++) { edit = Native.FindEditChild(h); if (edit == IntPtr.Zero) await Wait(0.25, ct); }
            if (edit == IntPtr.Zero) throw new Exception("I couldn't find where to write in Notepad.");
            await StandOnWindow(h, 90 * U, ct);
            Facing = 1;
            BaseAct = Act.Write;
            Native.CaretToEnd(edit);
            int n = 0;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            for (int i = 0; i < text.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!Native.IsWindow(edit))
                {
                    BaseAct = Act.Idle;
                    Say("Hey! Who closed my Notepad?");
                    notepad = IntPtr.Zero;
                    return;
                }
                char c = text[i];
                string unit = c.ToString();
                if (char.IsHighSurrogate(c) && i + 1 < text.Length) { unit = text.Substring(i, 2); i++; }
                Native.InsertText(edit, unit);
                if (++n % 3 == 0) AddDust();
                double d = 0.025 + Rnd.NextDouble() * 0.035;
                if (c == ' ') d += 0.02;
                if (c == '\n') d += 0.15;
                await Wait(d, ct);
            }
            await Wait(0.3, ct);
            notepadTitle = Native.Title(h);
            BaseAct = Act.Idle;
        }

        public void WriteNote(string text)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(Pick("Okay, I'll write that down!", "Writing it down!", "Let me write that!"));
                await WriteInNotepad(text + "\n", ct);
                ShowBubble("Done!", 2);
            });
        }

        public void DeliverAnswer(string question, string answer, string spoken, bool write)
        {
            Touch();
            if (!write || !S.NotepadAnswers)
            {
                CancelJob();
                Say(spoken);
                return;
            }
            RunJob(async delegate (CancellationToken ct)
            {
                Say(spoken);
                await WriteInNotepad("Q: " + question + "\nA: " + answer + "\n\n", ct);
                if (!Voice.IsSpeaking) ShowBubble("All written down!", 2.5);
            });
        }

        // ---------- mouse tricks ----------
        public void StartGrab(bool angry)
        {
            Touch();
            if (!S.MouseTricks)
            {
                if (angry) Tantrum(Pick("Grrr! You're lucky mouse tricks are turned off!", "Hmph!"));
                else Say("Mouse tricks are turned off in my settings.");
                return;
            }
            RunJob(async delegate (CancellationToken ct)
            {
                GrabAngry = angry;
                Say(angry ? Pick("That's it! I'm taking your mouse!", "Grrr! Your mouse is MINE now!")
                          : Pick("Wheee! I got your mouse!", "Let me borrow your mouse!"));
                await GoToFloor(ct);
                Point c = Cursor.Position;
                double reach = 108 * U;
                double fx = c.X, floor = FloorAt(fx);
                double fy = Math.Min(floor, c.Y + reach);
                if (fy < floor - 10) await JumpTo(fx, fy, null, false, 0.55, 40 * U, ct);
                else await MoveTo(fx, true, ct);
                Grabbing = true;
                GrabEnd = Now + (angry ? 6 : 4.5);
                await WaitGround(ct);
                Rectangle wa = Screen.FromPoint(new Point((int)X, (int)Y - 10)).WorkingArea;
                while (Now < GrabEnd && Grabbing)
                {
                    double tx = wa.Left + 80 * U + Rnd.NextDouble() * (wa.Width - 160 * U);
                    if (Rnd.NextDouble() < 0.3) await JumpTo(tx, FloorAt(tx), Support.Floor, true, 0.6, 90 * U, ct);
                    else await MoveTo(tx, true, ct, angry ? 1.3 : 1.0);
                }
                if (Grabbing)
                {
                    Grabbing = false;
                    Say(angry ? Pick("Hmph! Fine, you can have it back.", "There. Be nice next time!") : Pick("Here you go, it's yours again!", "That was fun! Here's your mouse back."));
                }
            });
        }

        public void ReleaseGrab()
        {
            if (Grabbing) { Grabbing = false; CancelJob(); }
        }

        public void Tantrum(string line)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(line);
                await Do(Act.Tantrum, 2.4, ct);
            });
        }

        // ---------- fun moves ----------
        public void Dance()
        {
            Touch();
            Brain.Happy = Math.Min(100, Brain.Happy + 10);
            RunJob(async delegate (CancellationToken ct)
            {
                Say(Pick("Let's dance!", "Dance party!", "Watch my moves!"));
                AddNotes(3);
                await DanceMoves(3, ct);
            });
        }

        // A real little dance routine: a few different moves, each for a couple of bars, then a finish.
        public async Task DanceMoves(int moves, CancellationToken ct)
        {
            await WaitGround(ct);
            List<Act> pool = new List<Act> { Act.DanceGroove, Act.DanceDisco, Act.DanceRunning, Act.DanceRobot, Act.DanceWave, Act.DanceMoon, Act.DanceSpin };
            for (int i = 0; i < moves && pool.Count > 0; i++)
            {
                Act m = pool[Rnd.Next(pool.Count)];
                pool.Remove(m);
                if (m == Act.DanceMoon)
                {
                    // glide backwards
                    double back = X - Facing * 140 * U;
                    Plat p = World.Find(Sup, X);
                    if (p != null && Sup.Kind != Plat.Floor) back = Clamp(back, p.L + 20 * U, p.R - 20 * U);
                    Task mv = MoveTo(back, Act.DanceMoon, ct, 0.75, true);
                    await Task.WhenAny(mv, Task.Delay(3200, ct));
                    ct.ThrowIfCancellationRequested();
                    continue;
                }
                if (m == Act.DanceSpin)
                {
                    BaseAct = m;
                    for (int k = 0; k < 12; k++) { Facing = -Facing; await Wait(0.13, ct); }
                    BaseAct = Act.Idle;
                    await JumpTo(X, Y, Sup, true, 0.4, 30 * U, ct);
                    continue;
                }
                await Do(m, 3.0, ct);
            }
            ShowBubble(Pick("Ta-da!", "Yeah!", "Woo!"), 1.4);
            await Do(Act.Cheer, 1.2, ct);
        }

        public void Flip()
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(Pick("Watch this!", "Backflip!", "Hup!"));
                await WaitGround(ct);
                Spin(0.75, 55, -1);
                flipStart = Now + 0.12;
                await JumpTo(X, Y, Sup, true, 0.8, 120 * U, ct, Act.Jump, 0.12);
                await Do(Act.Cheer, 1.2, ct);
            });
        }

        public void JumpAround()
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(Pick("Boing!", "Jump jump jump!"));
                for (int i = 0; i < 3; i++)
                {
                    await WaitGround(ct);
                    double tx = X + Facing * 60 * U;
                    Plat p = World.Find(Sup, X);
                    if (p != null && Sup.Kind != Plat.Floor) tx = Clamp(tx, p.L + 20 * U, p.R - 20 * U);
                    await JumpTo(tx, Y, Sup, true, 0.5, 70 * U, ct, Act.Jump, 0.08);
                }
            });
        }

        public void WaveHello(string line)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(line);
                await Do(Act.Wave, 2.2, ct);
            });
        }

        public void SitDown()
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(Pick("Ahh, a little break.", "Okay, sitting down."));
                await Do(Act.Sit, 12, ct);
            });
        }

        public void GoSleep()
        {
            CancelJob();
            Say(Pick("Okay... good night! Zzz...", "Nap time! Zzz..."));
            if (House != null && House.Shown) { GoHomeActivity("sleep"); return; }
            Sleeping = true;
        }

        public void WakeUp()
        {
            Sleeping = false;
            StopSitting();
            Touch();
            Say(Pick("I'm up! I'm up!", "*yawn* Good morning!"));
        }

        public void ComeHere()
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                Say(Pick("Coming!", "On my way!"));
                await RunTo(Cursor.Position.X, ct);
                await Do(Act.Wave, 1.2, ct);
            });
        }

        public void GoDirection(int dir)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                await RunTo(X + dir * 400 * Dpi, ct);
            });
        }

        public void Screenshot()
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                await SayWait("Say cheese!", ct);
                BaseAct = Act.Cheer;
                await Wait(0.5, ct);
                Native.KeyDown(Native.VK_LWIN);
                Native.Tap(Native.VK_SNAPSHOT);
                Native.KeyUp(Native.VK_LWIN);
                await Wait(0.8, ct);
                BaseAct = Act.Idle;
                Say("Click! It's saved in your Pictures, in the Screenshots folder.");
            });
        }

        public async Task SayWait(string text, CancellationToken ct)
        {
            Task t = Say(text);
            await Task.WhenAny(t, Task.Delay(-1, ct));
            ct.ThrowIfCancellationRequested();
        }

        public void MediaKey(ushort vk, int times, string line)
        {
            for (int i = 0; i < times; i++) Native.Tap(vk);
            if (line != null) Say(line);
        }

        public void ShowDesktop()
        {
            Native.Combo(Native.VK_LWIN, Native.VK_D);
            Say("Ta-da! Here's your desktop.");
        }

        // ---------- closing / minimising windows ----------
        static bool IsShellWindow(IntPtr h)
        {
            string c = Native.ClassName(h);
            return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
        }

        public IntPtr FindWindowByName(string name)
        {
            name = name.ToLowerInvariant();
            foreach (IntPtr h in Native.TopWindows())
            {
                if (Native.ProcessId(h) == myPid || IsShellWindow(h)) continue;
                string t = Native.Title(h).ToLowerInvariant();
                string p = (Native.ProcessName(h) ?? "").ToLowerInvariant();
                if (t.Contains(name) || p.Contains(name.Replace(" ", ""))) return h;
            }
            return IntPtr.Zero;
        }

        public void CloseWindow(string name, bool minimize)
        {
            Touch();
            IntPtr h = string.IsNullOrEmpty(name) ? LastExternalWindow : FindWindowByName(name);
            if (h == IntPtr.Zero || !Native.IsWindow(h) || IsShellWindow(h))
            {
                Say(string.IsNullOrEmpty(name) ? "There's no window for me to close." : "I can't find a window called " + name + ".");
                return;
            }
            string title = Native.Title(h);
            RunJob(async delegate (CancellationToken ct)
            {
                Say(minimize ? "Shrinking it!" : Pick("Closing it!", "Bye bye, window!"));
                Native.RECT r = Native.GetVisibleRect(h);
                await StandOnWindow(h, r.Width - 40 * U, ct);
                Facing = 1;
                await Do(Act.Stomp, 0.35, ct);
                AddPow(X, Y);
                if (minimize) Native.ShowWindow(h, Native.SW_MINIMIZE);
                else Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                await Wait(0.5, ct);
                Log.Write((minimize ? "minimized: " : "closed: ") + title);
            });
        }

        // ---------- games ----------
        public void StartTicTacToe()
        {
            Touch();
            Brain.GameStarted();
            TicTacToeForm game = new TicTacToeForm(this);
            game.Show();
            RunJob(async delegate (CancellationToken ct)
            {
                PlayingGame = true;
                try
                {
                    Say("Tic tac toe! You're X, I'm O. You go first!");
                    await Wait(0.3, ct);
                    Native.RECT r = Native.GetVisibleRect(game.Handle);
                    await StandOnWindow(game.Handle, r.Width - 45 * U, ct);
                    Facing = -1;
                    while (!game.IsDisposed && game.Visible) await Wait(0.25, ct);
                }
                finally { PlayingGame = false; }
                Say(Pick("Thanks for playing!", "That was fun!"));
            });
        }

        public void TttThinking() { BaseAct = Act.Think; ThinkingDots = true; }
        public void TttMoved() { ThinkingDots = false; BaseAct = Act.Idle; FlashAct(Act.Point, 0.8); }

        public void TttEnded(int result)
        {
            ThinkingDots = false;
            if (result == 2) { Say(Pick("I win! Hehe!", "Yes! Three in a row!")); FlashAct(Act.Cheer, 2.2); Brain.Happy = Math.Min(100, Brain.Happy + 10); }
            else if (result == 1) { Say(Pick("Aww, you beat me! Good game!", "Nooo! You win!")); FlashAct(Act.Sad, 2); }
            else { Say("It's a tie! Want to play again?"); FlashAct(Act.Shrug, 1.6); }
        }

        public void StartSnake()
        {
            Touch();
            Brain.GameStarted();
            SnakeForm game = new SnakeForm(this);
            game.Show();
            RunJob(async delegate (CancellationToken ct)
            {
                PlayingGame = true;
                try
                {
                    Say(Pick("Ooh, snake! Watch me play!", "Snake time! I'm really good at this!"));
                    await Wait(0.3, ct);
                    await StandOnWindow(game.Handle, 60 * U, ct);
                    Facing = 1;
                    BaseAct = Act.Play;
                    while (!game.IsDisposed && game.Visible) await Wait(0.25, ct);
                }
                finally { PlayingGame = false; }
            });
        }

        public void SnakeAte(int score)
        {
            if (Rnd.NextDouble() < 0.35) ShowBubble(Pick("Yum!", "Nom nom!", "Got it!", "Hehe!", "Tasty!"), 1.2);
            Brain.Happy = Math.Min(100, Brain.Happy + 2);
        }

        public void SnakeDied(int score, int deaths)
        {
            Say(deaths < 3 ? Pick("Nooo! Again!", "Oops! One more time!", "Aww! Let me try again!") : "That was so much fun!");
            FlashAct(Act.Sad, 1.3);
        }

        public void SnakeDone(int best)
        {
            Say("My best score was " + best + "! I'm so happy!");
            FlashAct(Act.Cheer, 2.5);
            Brain.Happy = 100;
        }

        public void StartCatchMe() { StartTag(true); }
    }
}
