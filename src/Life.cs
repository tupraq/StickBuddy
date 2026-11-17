using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // His life at home: what he does when you start him, when he's bored, and in his house.
    public partial class Buddy
    {
        public string Activity;           // what he's doing at home right now (or null)
        public bool Dribbling;
        public bool Introduced;
        double nextIdle = 8;
        double boredAt = 70;

        public Act CurrentAct { get { return curAct; } }
        public double ActTime { get { return Now - actStart; } }

        public void StopSitting()
        {
            SitSpot = null;
        }

        // ================= every frame =================
        void UpdateLife(double now, double dt)
        {
            UpdateWatching(now);
            UpdateDownloads(now);
            UpdateHideSeek(now);

            if (Activity == "sleep" && Sleeping && House != null && SitSpot.HasValue)
                SitSpot = new Point((int)X, (int)(House.UpperY - 14 * U));
            if (!Sleeping && Activity == "sleep") { SitSpot = null; }

            // Esc always stops an angry stickman
            if (AngerJob && (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0)
            {
                CancelJob(); StopCursorTricks(false); Brain.OnEscapeGrab();
            }
            // Esc stops him playing your game, even while he's still thinking
            if (PilotActive && (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0)
            {
                CancelJob();
                ReleasePilotInput();
                PilotActive = false;
                Say("Okay, you take over!");
            }

            if (dragging || !OnGround || jumping || Grabbing || RagMode || Hiding || BodyFrozen || script != null || CursorMode != CursorTrick.None) return;
            if (Busy && !IsIdleJob) { nextIdle = Math.Max(nextIdle, now + 4); return; }

            if (!Busy && !targetX.HasValue && !Sleeping && (curAct == Act.Idle || curAct == Act.Talk || curAct == Act.Listen || curAct == Act.Angry))
            {
                double dx = Cursor.Position.X - X;
                if (Math.Abs(dx) > 90 * Dpi) Facing = Math.Sign(dx);
            }

            if (now < nextIdle || Busy) return;
            nextIdle = now + 4 + Rnd.NextDouble() * 7;
            if (Sleeping || Voice.IsSpeaking || Quitting) return;
            bool house = House != null && House.Shown;
            if (S.Feelings && now - LastInteraction > 240)
            {
                if (house) RunIdleJob(delegate (CancellationToken ct) { return HomeActivity("sleep", ct); });
                else { Sleeping = true; StopSitting(); }
                return;
            }
            if (!S.Wander) return;
            if (S.Feelings && Brain.Angry > 55) return;

            // bored of waiting for you: go home and play
            if (house && now - LastInteraction > boredAt)
            {
                boredAt = 50 + Rnd.NextDouble() * 50;
                LastInteraction = now - 20; // so he doesn't go straight back
                string what = Things != null && Things.AnyOut() && Rnd.NextDouble() < 0.5 ? "tidy" : Pick("tv", "draw", "draw", "basketball", "exercise");
                Brain.Bored = Math.Min(100, Brain.Bored + 10);
                RunIdleJob(delegate (CancellationToken ct) { return HomeActivity(what, ct); });
                return;
            }
            if (Sup.IsWindow && Rnd.NextDouble() < 0.5) { RunIdleJob(GoToFloor); return; }
            if (S.Feelings && Brain.Bored > 65) return;

            double r = Rnd.NextDouble();
            if (r < 0.34)
            {
                Rectangle wa = Screen.FromPoint(new Point((int)X, (int)Y - 10)).WorkingArea;
                double tx = Clamp(X + (Rnd.NextDouble() * 2 - 1) * 420 * Dpi, wa.Left + 60 * U, wa.Right - 60 * U);
                Plat cur = World.Find(Sup, X);
                if (cur != null && Sup.Kind != Plat.Floor) tx = Clamp(tx, cur.L + 25 * U, cur.R - 25 * U);
                RunIdleJob(delegate (CancellationToken ct) { return MoveTo(tx, false, ct); });
            }
            else if (r < 0.44) RunIdleJob(JumpOnNearbyWindow);
            else if (r < 0.50) RunIdleJob(delegate (CancellationToken ct) { return Do(Act.Wave, 1.6, ct); });
            else if (r < 0.56 && Brain.Happy > 70) RunIdleJob(delegate (CancellationToken ct) { return DanceMoves(1, ct); });
            else if (r < 0.63) Facing = -Facing;
            else if (r < 0.69) RunIdleJob(delegate (CancellationToken ct) { return Do(Act.Think, 2.2, ct); });
            else if (r < 0.75) RunIdleJob(Hop);
        }

        // Curious: hops onto a window that's near him (like when you make Chrome smaller).
        async Task JumpOnNearbyWindow(CancellationToken ct)
        {
            if (Sup.Kind != Plat.Floor) return;
            Plat best = null;
            foreach (Plat p in World.Plats)
            {
                if (p.Kind != Plat.Window) continue;
                double dy = Y - p.Y;
                if (dy < 60 * U || dy > 560 * U) continue;
                double dx = Math.Abs(Clamp(X, p.L, p.R) - X);
                if (dx > 600 * Dpi) continue;
                if (best == null || dx < Math.Abs(Clamp(X, best.L, best.R) - X)) best = p;
            }
            if (best == null) return;
            double standX = Clamp(X, best.L + 40 * U, best.R - 40 * U);
            double launch = standX + (X < standX ? -140 : 140) * U;
            if (launch < best.L || launch > best.R) { } else launch = best.L - 110 * U;
            await MoveTo(launch, true, ct);
            double dyy = Y - best.Y;
            if (dyy > 260 * U) { ShowBubble(Pick("Hup!", "Parkour!", "Watch this!"), 1.2); Spin(0.6, 55, -1); flipStart = Now + 0.14; }
            await JumpTo(standX, best.Y, Support.Window(best.Hwnd), true, 0.55 + dyy / (900 * U), 50 * U, ct, Act.Jump, 0.14);
            await Wait(0.3, ct);
            if (Rnd.NextDouble() < 0.5) await Do(Act.Cheer, 1.0, ct);
        }

        // ================= starting up =================
        async Task StartupInHouse()
        {
            await Task.Delay(300);
            string what = Pick("sleep", "tv", "exercise", "basketball", "draw");
            if (Environment.GetEnvironmentVariable("STICKBUDDY_START") != null) what = Environment.GetEnvironmentVariable("STICKBUDDY_START");
            Log.Write("startup activity: " + what);
            PlaceAt(what);
            if (what == "sleep")
            {
                RunIdleJob(delegate (CancellationToken ct) { return HomeActivity("sleep", ct); });
                ShowBubble("Zzz...", 3);
                return;
            }
            RunIdleJob(delegate (CancellationToken ct) { return HomeActivity(what, ct, 9 + Rnd.NextDouble() * 4); });
            double until = Now + 14;
            while (Now < until && Busy && IsIdleJob) await Task.Delay(250);
            if (Busy && !IsIdleJob) return; // you already asked him something
            if (RagMode || dragging) return;
            RunJob(async delegate (CancellationToken ct)
            {
                await GoToFloor(ct);
                await MoveTo(House.Sx(House.OuterW + 80), false, ct);
                Facing = 1;
                Introduced = true;
                BaseAct = Act.Wave;
                await SayWait("Hi! I'm " + S.Name + "! Say \"" + S.Name + "\" and tell me what to do. Or double-click me to type!", ct);
                BaseAct = Act.Idle;
            });
        }

        // Puts him straight into a spot in the house (no walking).
        void PlaceAt(string what)
        {
            OnGround = true; RagMode = false; VX = 0; VY = 0;
            shown = Poses.Get(Act.Idle, 0, 0); blendFrom = shown; curAct = Act.Idle;
            switch (what)
            {
                case "sleep":
                case "draw":
                    X = House.Sx(what == "sleep" ? 292 : 222); Y = House.UpperY; Sup = Support.Upper; Facing = -1;
                    break;
                case "tv":
                    X = House.Sx(292); Y = House.GroundY; Sup = Support.Floor; Facing = -1;
                    break;
                case "basketball":
                    X = House.Sx(House.OuterW + 150); Y = House.GroundY; Sup = Support.Floor; Facing = -1;
                    Thing ball = Things.Get(ThingKind.Ball);
                    if (ball != null) Things.PickUp(ball);
                    break;
                default:
                    X = House.Sx(House.OuterW + 90); Y = House.GroundY; Sup = Support.Floor; Facing = 1;
                    break;
            }
            lastHouseX = House.HX;
        }

        // ================= getting around the house =================
        public async Task StairsUp(CancellationToken ct)
        {
            if (Sup.Kind == Plat.Upper) return;
            await GoToFloor(ct);
            await MoveTo(House.Sx(House.StairX0 + 2), false, ct);
            double k = 0;
            await RunScript(delegate (double dt)
            {
                double len = Math.Sqrt(Math.Pow((House.StairX1 - House.StairX0) * House.Hu, 2) + Math.Pow(House.UpperFloor * House.Hu, 2));
                k = Math.Min(1, k + 135 * Dpi * dt / len);
                X = House.Sx(House.StairX0 + (House.StairX1 + 4 - House.StairX0) * k);
                Y = House.Sy(House.UpperFloor * k);
                Facing = 1;
                return k >= 1;
            }, Act.Walk, Support.Upper, ct);
        }

        public async Task StairsDown(CancellationToken ct)
        {
            if (Sup.Kind != Plat.Upper) return;
            await MoveTo(House.Sx(House.StairX1 + 6), false, ct);
            double k = 1;
            await RunScript(delegate (double dt)
            {
                double len = Math.Sqrt(Math.Pow((House.StairX1 - House.StairX0) * House.Hu, 2) + Math.Pow(House.UpperFloor * House.Hu, 2));
                k = Math.Max(0, k - 150 * Dpi * dt / len);
                X = House.Sx(House.StairX0 + (House.StairX1 + 4 - House.StairX0) * k);
                Y = House.Sy(House.UpperFloor * k);
                Facing = -1;
                return k <= 0;
            }, Act.Walk, Support.Floor, ct);
        }

        async Task GoUpstairsTo(double hx, CancellationToken ct)
        {
            await StairsUp(ct);
            await MoveTo(House.Sx(hx), false, ct);
        }

        async Task GoDownstairsTo(double hx, CancellationToken ct)
        {
            await GoToFloor(ct);
            await MoveTo(House.Sx(hx), false, ct);
        }

        // ================= things to do at home =================
        public void GoHomeActivity(string what)
        {
            Touch();
            if (House == null || !House.Shown) { Say("My house is packed away! Right-click me, then At home, then Show my house."); return; }
            RunJob(delegate (CancellationToken ct) { return HomeActivity(what, ct); });
        }

        Task HomeActivity(string what, CancellationToken ct) { return HomeActivity(what, ct, 0); }

        async Task HomeActivity(string what, CancellationToken ct, double maxTime)
        {
            if (House == null || !House.Shown) return;
            if (what == null) what = Pick("tv", "draw", "basketball", "exercise");
            Activity = what;
            double t = maxTime > 0 ? maxTime : 18 + Rnd.NextDouble() * 22;
            try
            {
                switch (what)
                {
                    case "tv": await WatchTv(t, ct); break;
                    case "draw": await DrawPicture(ct); break;
                    case "basketball": await Basketball(t, ct); break;
                    case "exercise": await Exercise(ct); break;
                    case "sleep": await SleepInBed(ct); break;
                    case "tidy": await TidyUp(ct); break;
                }
            }
            finally
            {
                Activity = null; SitSpot = null; HeldProp = Prop.None; Dribbling = false;
                if (House != null) House.TvOn = false;
                Thing ball = Things != null ? Things.Get(ThingKind.Ball) : null;
                if (ball != null && Carrying == ball) Things.Drop(ball);
            }
        }

        async Task WatchTv(double secs, CancellationToken ct)
        {
            Thing tv = Things.Get(ThingKind.TV);
            await GoDownstairsTo(292, ct);
            Facing = -1;
            if (tv == null || tv.State != ThingState.Home)
            {
                FlashAct(Act.Sad, 2);
                await SayWait(tv != null && tv.State == ThingState.Broken ? "Aww... my TV is broken." : "Hey, where did my TV go?", ct);
                return;
            }
            House.TvOn = true;
            BaseAct = Act.CouchSit;
            double end = Now + secs;
            while (Now < end)
            {
                await Wait(4 + Rnd.NextDouble() * 4, ct);
                if (tv.State != ThingState.Home) { House.TvOn = false; BaseAct = Act.Idle; await SayWait("HEY! I was watching that!", ct); return; }
                double r = Rnd.NextDouble();
                if (r < 0.3) { ShowBubble(Pick("Hahaha!", "Hehe!", "LOL!"), 1.5); BaseAct = Act.Laugh; await Wait(1.6, ct); BaseAct = Act.CouchSit; }
                else if (r < 0.45) ShowBubble(Pick("Ooh!", "Whoa!", "No way!"), 1.4);
            }
            House.TvOn = false;
            BaseAct = Act.Idle;
        }

        async Task SleepInBed(CancellationToken ct)
        {
            await GoUpstairsTo(292, ct);
            Facing = -1;
            Sleeping = true;
            SitSpot = new Point((int)X, (int)(House.UpperY - 14 * U));
            while (Sleeping) await Wait(0.3, ct);
            SitSpot = null;
        }

        async Task DrawPicture(CancellationToken ct)
        {
            await GoUpstairsTo(222, ct);
            Facing = -1;
            Thing chair = Things.Get(ThingKind.Chair);
            bool sit = chair != null && chair.State == ThingState.Home;
            Doodle d = Doodle.Make(Rnd);
            House.Easel = d; House.EaselProgress = 0; House.Invalidate2();
            BaseAct = sit ? Act.DrawSit : Act.Write;
            if (!sit) { X = House.Sx(214); }
            double dur = 7 + Rnd.NextDouble() * 4, start = Now;
            while (Now - start < dur)
            {
                await Wait(0.08, ct);
                House.EaselProgress = (Now - start) / dur;
                House.Invalidate2();
                if (Rnd.NextDouble() < 0.3) AddDust();
            }
            House.EaselProgress = 1; House.Invalidate2();
            BaseAct = Act.Idle;
            Facing = 1;
            Brain.Happy = Math.Min(100, Brain.Happy + 8);
            Brain.Bored = Math.Max(0, Brain.Bored - 15);
            Say(Pick("Ta-da! I drew " + d.Name + "!", "Look! It's " + d.Name + "!", "Done! I made " + d.Name + "."));
            Brain.Event("you drew " + d.Name + " on your easel and hung it on the wall");
            await Do(Act.Cheer, 1.4, ct);
            Thing frame = Things.Get(ThingKind.Frame);
            if (frame != null && frame.State == ThingState.Home)
            {
                await MoveTo(House.Sx(262), false, ct);
                BaseAct = Act.Point;
                frame.Art = d; House.Invalidate2();
                await Wait(1.2, ct);
                BaseAct = Act.Idle;
                ShowBubble("I'll hang it on my wall!", 2.5);
            }
            await Wait(2, ct);
        }

        async Task Exercise(CancellationToken ct)
        {
            await GoToFloor(ct);
            await MoveTo(House.Sx(House.OuterW + 95), false, ct);
            Facing = Rnd.NextDouble() < 0.5 ? 1 : -1;
            Say(Pick("Workout time!", "Gotta stay strong!", "Exercise time! One, two!"));
            BaseAct = Act.PushUp;
            for (int i = 1; i <= 5; i++) { await Wait(1.1, ct); ShowBubble(i.ToString(), 0.7); }
            BaseAct = Act.Squat;
            for (int i = 1; i <= 4; i++) { await Wait(1.4, ct); ShowBubble(i.ToString(), 0.8); }
            BaseAct = Act.JumpJack;
            await Wait(4.8, ct);
            Thing db = Things.Get(ThingKind.Dumbbell);
            if (db != null && (db.State == ThingState.Home || (db.State == ThingState.Loose && db.Resting && db.RestOn.Kind == Plat.Floor)))
            {
                BaseAct = Act.Idle;
                double hx = db.State == ThingState.Home ? House.Sx(db.HomeX) : db.X;
                await MoveTo(hx, false, ct);
                await Do(Act.Crouch, 0.35, ct);
                Things.PickUp(db);
                await MoveTo(House.Sx(House.OuterW + 95), false, ct);
                BaseAct = Act.Curl;
                for (int i = 1; i <= 4; i++) { await Wait(1.2, ct); ShowBubble(i.ToString(), 0.8); }
                BaseAct = Act.Idle;
                await MoveTo(House.Sx(db.HomeX), false, ct);
                await Do(Act.Crouch, 0.35, ct);
                Things.PutHome(db);
            }
            BaseAct = Act.Idle;
            Say(Pick("Phew! I'm so strong now!", "Done! Feel these muscles!"));
            await Do(Act.Cheer, 1.5, ct);
        }

        async Task Basketball(double secs, CancellationToken ct)
        {
            Thing ball = Things.Get(ThingKind.Ball);
            if (ball == null) return;
            if (Carrying != ball)
            {
                if (!await FetchThing(ball, ct)) { await SayWait("Where's my ball?", ct); return; }
            }
            double spot = House.Sx(House.OuterW + 130 + Rnd.NextDouble() * 70);
            await GoToFloor(ct);
            await MoveTo(spot, false, ct);
            double end = Now + secs;
            int made = 0, shots = 0;
            while (Now < end)
            {
                Facing = -1;
                BaseAct = Act.Dribble; Dribbling = true;
                await Wait(1.5 + Rnd.NextDouble() * 1.5, ct);
                Dribbling = false;
                Facing = -1;
                BaseAct = Act.Shoot;
                await Wait(0.5, ct);
                // shoot at the hoop
                double rimX = House.Sx(House.OuterW + 20), rimY = House.Sy(160);
                bool aimGood = Rnd.NextDouble() < 0.6;
                double tx = rimX + (aimGood ? (Rnd.NextDouble() - 0.5) * 6 : (Rnd.NextDouble() < 0.5 ? -1 : 1) * (12 + Rnd.NextDouble() * 10)) * House.Hu;
                double T = 0.95, g = 2300 * Dpi;
                Thing bl = ball;
                double vx = (tx - bl.X) / T, vy = (rimY - 6 * House.Hu - bl.Y - 0.5 * g * T * T) / T;
                Things.Throw(bl, vx, vy, -8);
                shots++;
                bool scored = await WatchShot(bl, rimX, rimY, ct);
                BaseAct = Act.Idle;
                if (scored) { made++; Say(Pick("Swish!", "Nothing but net!", "Yes! Score!")); BaseAct = Act.Cheer; Brain.Happy = Math.Min(100, Brain.Happy + 6); }
                else { ShowBubble(Pick("Aww!", "So close!", "Missed!"), 1.5); }
                await Wait(1.0, ct);
                BaseAct = Act.Idle;
                if (!await FetchThing(ball, ct)) { await SayWait("Oops, I lost my ball!", ct); return; }
                await MoveTo(spot, false, ct);
            }
            if (shots > 0) ShowBubble("I made " + made + " out of " + shots + "!", 3);
            await Wait(0.5, ct);
            if (Carrying == ball)
            {
                await MoveTo(House.Sx(ball.HomeX), false, ct);
                await Do(Act.Crouch, 0.3, ct);
                Things.PutHome(ball);
            }
        }

        // Watches the ball fly; bounces it off the board and says if it went in.
        async Task<bool> WatchShot(Thing ball, double rimX, double rimY, CancellationToken ct)
        {
            double prevY = ball.Y, end = Now + 2.5;
            double board = House.Sx(House.OuterW + 6), r = ball.W / 2 * House.Hu;
            bool scored = false;
            while (Now < end && ball.State == ThingState.Loose)
            {
                await Wait(0.012, ct);
                if (ball.X - r < board && ball.X > board - 30 * House.Hu && ball.Y > House.Sy(190) && ball.Y < House.Sy(150) && ball.VX < 0)
                {
                    ball.X = board + r; ball.VX = Math.Abs(ball.VX) * 0.5;
                }
                if (!scored && prevY < rimY && ball.Y >= rimY && ball.VY > 0 && Math.Abs(ball.X - rimX) < 11 * House.Hu)
                {
                    scored = true;
                    ball.VX *= 0.15; ball.X = rimX; ball.VY *= 0.5;
                }
                else if (!scored && prevY < rimY && ball.Y >= rimY && ball.VY > 0 && Math.Abs(ball.X - rimX) < 18 * House.Hu)
                {
                    ball.VY = -Math.Abs(ball.VY) * 0.45; ball.VX += (ball.X < rimX ? -1 : 1) * 160 * Dpi; // clanks off the rim
                }
                prevY = ball.Y;
                if (ball.Resting) break;
            }
            return scored;
        }

        // Walks to a thing and picks it up (if he can reach it).
        async Task<bool> FetchThing(Thing t, CancellationToken ct)
        {
            double end = Now + 3;
            while (t.State == ThingState.Loose && !t.Resting && Now < end) await Wait(0.1, ct);
            if (t.State == ThingState.Broken || t.State == ThingState.Held) return false;
            if (t.State == ThingState.Carried) return Carrying == t;
            if (t.State == ThingState.Home)
            {
                if (t.HomeY >= House.UpperFloor) await GoUpstairsTo(t.HomeX, ct);
                else await GoDownstairsTo(t.HomeX, ct);
            }
            else if (t.RestOn == null || t.RestOn.Kind == Plat.Floor) { await GoToFloor(ct); await MoveTo(t.X, false, ct); }
            else if (t.RestOn.Kind == Plat.Upper) { await StairsUp(ct); await MoveTo(t.X, false, ct); }
            else if (t.RestOn.IsWindow)
            {
                Native.RECT r = Native.GetVisibleRect(t.RestOn.Hwnd);
                await StandOnWindow(t.RestOn.Hwnd, t.X - r.Left, ct);
                if (!(Sup.IsWindow && Sup.Hwnd == t.RestOn.Hwnd)) return false;
                await MoveTo(t.X, false, ct);
            }
            else return false;
            if (t.State == ThingState.Held || t.State == ThingState.Broken) return false;
            await Do(Act.Crouch, 0.3, ct);
            Things.PickUp(t);
            return true;
        }

        async Task TidyUp(CancellationToken ct)
        {
            int n = 0;
            foreach (Thing t in Things.All)
            {
                if (t.State != ThingState.Loose || !t.Resting) continue;
                if (n == 0) Say(Pick("Who made this mess? Time to tidy up!", "Let me put my things back."));
                if (!await FetchThing(t, ct)) continue;
                if (t.HomeY >= House.UpperFloor) await GoUpstairsTo(t.HomeX, ct);
                else await GoDownstairsTo(t.HomeX, ct);
                await Do(Act.Crouch, 0.3, ct);
                Things.PutHome(t);
                n++;
            }
            if (n > 0) { Say("All tidy!"); await Do(Act.Cheer, 1.2, ct); }
        }

        // ================= test hooks (used with --command "test:...") =================
        void RunTest(string what)
        {
            Log.Write("TEST: " + what);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (what.StartsWith("dropat:"))
            {
                string[] xy = what.Substring(7).Split(',');
                CancelJob(); X = double.Parse(xy[0]); Y = double.Parse(xy[1]); EnterRagdoll(0, 0);
                return;
            }
            switch (what)
            {
                case "throwwall":
                    CancelJob(); EnterRagdoll(0, 0); Rag.AddVelocity(3200 * Dpi, -900 * Dpi, 1 / 120.0); break;
                case "throwleft":
                    CancelJob(); EnterRagdoll(0, 0); Rag.AddVelocity(-3200 * Dpi, -700 * Dpi, 1 / 120.0); break;
                case "smash":
                    CancelJob(); Y -= 300 * U; EnterRagdoll(0, 2600 * Dpi); break;
                case "drop":
                    CancelJob(); Y -= 200 * U; EnterRagdoll(0, 0); break;
                case "breaktv":
                    { Thing t = Things.Get(ThingKind.TV); if (t != null) Things.Throw(t, 900 * Dpi, -1500 * Dpi, 6); break; }
                case "throwball":
                    { Thing t = Things.Get(ThingKind.Ball); if (t != null) Things.Throw(t, 1200 * Dpi, -1200 * Dpi, 6); break; }
                case "tidy": GoHomeActivity("tidy"); break;
                case "draw": GoHomeActivity("draw"); break;
                case "tv": GoHomeActivity("tv"); break;
                case "basketball": GoHomeActivity("basketball"); break;
                case "exercise": GoHomeActivity("exercise"); break;
                case "sleep": GoHomeActivity("sleep"); break;
                case "dance": Dance(); break;
                case "dump": dumpEvery = 0.12; break;
                case "dumpslow": dumpEvery = 0.5; break;
                case "window": RunJob(JumpOnNearbyWindow); break;
                case "tier1": Brain.Angry = 30; Brain.TestReact(); break;
                case "tier2throw": Brain.Angry = 60; ThrowCursor(); break;
                case "tier2hang": Brain.Angry = 60; Native.SetCursorPos((int)(X + 150 * U), (int)(Y - 330 * U)); HangOnCursor(); break;
                case "tier3": Brain.Angry = 85; StompCursor(); break;
                case "calm": Brain.Angry = 0; Brain.Bored = 0; break;
                case "lab": OpenLab(); break;
                case "labtrain": TestLabTrain(1); break;
                case "labfast": TestLabTrain(5); break;
                case "hold":
                case "holdhead":
                    {
                        CancelJob();
                        Joints j0 = WorldJoints();
                        EnterRagdoll(0, 0);
                        Rag.Pin1 = what == "holdhead" ? Ragdoll.Head : Ragdoll.Neck;
                        PointF pp = what == "holdhead" ? j0.Head : j0.Neck;
                        TestHoldX = pp.X; TestHoldY = pp.Y;
                        Rag.PinX = pp.X; Rag.PinY = pp.Y;
                        TestHoldStart = Now; TestHoldUntil = Now + 4.5;
                        break;
                    }
                case "ytfake":
                    ytFakeUntil = Now + 14; WatchingYt = true; ytTitle = "Funny cats compilation"; ytWindow = LastExternalWindow;
                    RunIdleJob(WatchYoutube); break;
                case "dlfake": ReactToDownload(@"C:\fake\minecraft_installer.exe", 3 * 1024 * 1024); break;
                case "dlfake2": ReactToDownload(@"C:\fake\summer song.mp3", 5 * 1024 * 1024); break;
                case "flash": ImpactFrame(); break;
                case "tagme": StartTag(true); break;
                case "tagyou": StartTag(false); break;
                case "hide": StartHideAndSeek(); break;
                case "found": FoundMe(); break;
            }
        }
    }
}
