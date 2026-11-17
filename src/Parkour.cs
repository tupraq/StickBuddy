using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // Parkour moves (run, slide, climb walls, launch jumps, rolls, dives) and the tag games.
    public partial class Buddy
    {
        public bool TagRunning, TagUserIsIt;
        int tagHits;
        double tagInvulnUntil, nextTaunt;

        double RunSpeedMul { get { return Parkour ? 1.35 : 1.0; } }

        // ================= moves =================
        // Runs towards x, but gives up after maxSec so he can change his mind.
        async Task RunFor(double x, double maxSec, CancellationToken ct)
        {
            Task t = MoveTo(x, Act.Run, ct, RunSpeedMul, false);
            Task done = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(maxSec), ct));
            ct.ThrowIfCancellationRequested();
            if (done == t && t.IsCanceled) ct.ThrowIfCancellationRequested();
        }

        double SupportY(Support s, double x, double fallback)
        {
            if (s == null) return fallback;
            if (s.IsWindow) { Native.RECT r = Native.GetVisibleRect(s.Hwnd); return r.Top; }
            if (s.Kind == Plat.Upper && House != null) return House.UpperY;
            if (s.Kind == Plat.Roof && House != null) return House.RoofY;
            return FloorAt(x);
        }

        // A forward roll to soak up a big landing.
        public Task Roll(CancellationToken ct)
        {
            double x0 = X, t = 0, dur = 0.45, dist = 85 * U * Facing;
            Support sup = Sup;
            Spin(dur, 24, 1);
            return RunScript(delegate (double dt)
            {
                t += dt;
                double k = Math.Min(1, t / dur);
                X = x0 + dist * (1 - (1 - k) * (1 - k));
                Y = SupportY(sup, X, Y);
                return k >= 1;
            }, Act.Roll, sup, ct);
        }

        void AutoRoll()
        {
            if (script != null) return;
            AddFootDust(5);
            Roll(CancellationToken.None).ContinueWith(delegate (Task tt) { var ignore = tt.Exception; });
        }

        // A baseball slide along the ground.
        public Task Slide(double dist, CancellationToken ct)
        {
            double x0 = X, t = 0, dur = 0.6;
            Support sup = Sup;
            Facing = Math.Sign(dist) == 0 ? Facing : Math.Sign(dist);
            Plat p = World.Find(sup, X);
            if (p != null && sup.Kind != Plat.Floor) dist = Clamp(x0 + dist, p.L + 10 * U, p.R - 10 * U) - x0;
            Rectangle vs = SystemInformation.VirtualScreen;
            dist = Clamp(x0 + dist, vs.Left + 25 * U, vs.Right - 25 * U) - x0;
            return RunScript(delegate (double dt)
            {
                t += dt;
                double k = Math.Min(1, t / dur);
                X = x0 + dist * (1 - (1 - k) * (1 - k));
                Y = SupportY(sup, X, Y);
                if (Rnd.NextDouble() < 0.5) AddFootDust(1);
                return k >= 1;
            }, Act.Slide, sup, ct);
        }

        // Climbs straight up a wall (the side of a window, or the edge of the screen).
        // He stays hanging there when he gets to the top; the next move takes him off.
        public Task ClimbTo(double x, int face, double topY, CancellationToken ct)
        {
            X = x; Facing = face;
            return RunScript(delegate (double dt)
            {
                X = x; Facing = face;
                Y = Math.Max(topY, Y - 300 * U * dt);
                return Y <= topY + 0.5;
            }, Act.Climb, null, ct);
        }

        // Where his hands are, measured from his feet, in the dive pose.
        PointF DiveHandOffset() { return new PointF((float)(64 * U * Facing), (float)(-37 * U)); }

        // A flying dive at a point. Returns true if hit() said yes while flying.
        async Task<bool> Dive(PointF target, Func<bool> hit, CancellationToken ct)
        {
            Facing = target.X >= X ? 1 : -1;
            PointF off = DiveHandOffset();
            double ex = target.X - off.X, ey = target.Y - off.Y;
            double dist = Math.Sqrt((ex - X) * (ex - X) + (ey - Y) * (ey - Y));
            double dur = Clamp(dist / (1250 * Dpi), 0.2, 0.5);
            ShowBubble(Pick("Hyaaa!", "Gotcha!", "Hup!"), 0.8);
            Task jt = JumpTo(ex, ey, null, false, dur, 20 * U, ct, Act.Dive, 0.05);
            while (!jt.IsCompleted)
            {
                if (hit()) return true;
                await Task.Delay(12, ct);
            }
            if (hit()) return true;
            return false;
        }

        // ================= finding a way to a point on the screen =================
        Plat CurrentPlat()
        {
            Plat p = World.Find(Sup, X);
            if (p == null || (Sup.Kind != Plat.Floor && (X < p.L - 8 || X > p.R + 8))) p = World.SurfaceBelow(X, Y - 3);
            return p;
        }

        // One move towards the goal (run a bit, jump onto something, climb, drop down).
        async Task PlanStep(PointF goal, CancellationToken ct)
        {
            await WaitGround(ct);
            Rectangle vs = SystemInformation.VirtualScreen;
            Plat cur = CurrentPlat();
            double gx = goal.X, gy = goal.Y;
            bool goalHigh = gy < Y - 170 * U;
            bool offPlatform = cur.Kind != Plat.Floor && (gx < cur.L - 10 * U || gx > cur.R + 10 * U || gy > Y + 40 * U);

            if (offPlatform && !(goalHigh && gx >= cur.L && gx <= cur.R))
            {
                // jump down off this platform towards the goal
                double dir = gx < X ? -1 : 1;
                double edge = dir < 0 ? cur.L : cur.R;
                double landX = edge + dir * (60 + Rnd.NextDouble() * 120) * U;
                if (dir < 0 ? gx < landX : gx > landX) landX = Clamp(gx, Math.Min(edge, X - 400 * U), Math.Max(edge, X + 400 * U));
                landX = Clamp(landX, vs.Left + 30 * U, vs.Right - 30 * U);
                Plat below = World.SurfaceBelow(landX, Y + 8);
                await RunFor(edge - dir * 20 * U, 1.2, ct);
                double drop = below.Y - Y;
                await JumpTo(landX, below.Y, Support.Of(below), true, 0.45 + Math.Max(0, drop) / (2400 * U), 45 * U, ct, Act.Jump, 0.05);
                return;
            }
            if (!goalHigh)
            {
                double lo = cur.Kind == Plat.Floor ? vs.Left + 30 * U : cur.L + 15 * U;
                double hi = cur.Kind == Plat.Floor ? vs.Right - 30 * U : cur.R - 15 * U;
                await RunFor(Clamp(gx, lo, hi), 0.3, ct);
                return;
            }

            // goal is up high: find something to climb onto
            Plat best = null; double bestScore = double.MaxValue;
            foreach (Plat p in World.Plats)
            {
                if (p.Y >= Y - 40 * U) continue;
                if (p.Y < gy - 30 * U) continue;
                double px = Clamp(gx, p.L + 20 * U, p.R - 20 * U);
                double score = Math.Abs(p.Y - (gy + 70 * U)) + Math.Abs(px - gx) * 0.7 + Math.Abs(px - X) * 0.25;
                if (score < bestScore) { bestScore = score; best = p; }
            }
            if (best == null || bestScore > 700 * U)
            {
                // nothing to stand on up there: climb the edge of the screen and leap
                await GoToFloor(ct);
                int side = gx < (vs.Left + vs.Right) / 2.0 ? -1 : 1;
                double wallX = side < 0 ? vs.Left + 16 * U : vs.Right - 16 * U;
                await RunFor(wallX, 3, ct);
                if (Math.Abs(X - wallX) > 20 * U) return;
                await ClimbTo(wallX, side, Math.Max(vs.Top + 120 * U, gy + 50 * U), ct);
                Facing = -side;
                return; // caller decides to dive or jump off
            }
            double dy = Y - best.Y;
            double target = Clamp(gx, best.L + 30 * U, best.R - 30 * U);
            bool under = X > best.L - 20 * U && X < best.R + 20 * U;
            if (dy <= 210 * U || (dy <= 400 * U && best.Kind != Plat.Window))
            {
                double side = (X < best.Mid) ? -1 : 1;
                double launch = under ? (side < 0 ? best.L - 110 * U : best.R + 110 * U) : X;
                if (!under && Math.Abs(X - (side < 0 ? best.L : best.R)) > 260 * U) launch = side < 0 ? best.L - 150 * U : best.R + 150 * U;
                launch = Clamp(launch, vs.Left + 30 * U, vs.Right - 30 * U);
                await RunFor(launch, 2.5, ct);
                double land = side < 0 ? best.L + 40 * U : best.R - 40 * U;
                bool big = dy > 210 * U;
                if (big) Spin(0.6, 55, -1);
                await JumpTo(land, best.Y, Support.Of(best), true, big ? 0.8 : 0.55, big ? 70 * U : 45 * U, ct, Act.Jump, 0.08);
                return;
            }
            if (dy <= 400 * U)
            {
                // launch jump: big run-up, big leap
                double side = (X < best.Mid) ? -1 : 1;
                double launch = side < 0 ? best.L - 170 * U : best.R + 170 * U;
                launch = Clamp(launch, vs.Left + 30 * U, vs.Right - 30 * U);
                await RunFor(launch, 2.5, ct);
                Spin(0.7, 55, -1);
                await JumpTo(side < 0 ? best.L + 45 * U : best.R - 45 * U, best.Y, Support.Of(best), true, 0.85, 80 * U, ct, Act.Jump, 0.1);
                return;
            }
            // too high to jump: climb the side of the window and vault on top
            {
                await GoToFloor(ct);
                int side = X < best.Mid ? -1 : 1;
                double wallX = side < 0 ? best.L - 14 * U : best.R + 14 * U;
                wallX = Clamp(wallX, vs.Left + 16 * U, vs.Right - 16 * U);
                await RunFor(wallX, 3, ct);
                if (Math.Abs(X - wallX) > 20 * U) return;
                await ClimbTo(wallX, -side, best.Y + 30 * U, ct);
                await JumpTo(side < 0 ? best.L + 35 * U : best.R - 35 * U, best.Y, Support.Of(best), true, 0.32, 40 * U, ct, Act.Jump, 0);
            }
        }

        double HandDist(PointF p)
        {
            Joints j = WorldJoints();
            double a = Math.Sqrt(Math.Pow(j.HandA.X - p.X, 2) + Math.Pow(j.HandA.Y - p.Y, 2));
            double b2 = Math.Sqrt(Math.Pow(j.HandB.X - p.X, 2) + Math.Pow(j.HandB.Y - p.Y, 2));
            return Math.Min(a, b2);
        }

        bool BodyTouches(PointF p, double pad)
        {
            RectangleF r = WorldJoints().Bounds();
            r.Inflate((float)pad, (float)pad);
            return r.Contains(p);
        }

        // Gets close enough to a moving point (usually your mouse) to touch it.
        public async Task<bool> Approach(Func<PointF> goalFn, double reach, double timeout, bool dive, CancellationToken ct)
        {
            double end = Now + timeout;
            while (Now < end)
            {
                ct.ThrowIfCancellationRequested();
                PointF gp = goalFn();
                if (!RagMode && (HandDist(gp) < reach || BodyTouches(gp, reach * 0.3))) return true;
                await WaitGround(ct);
                if (script != null) await Task.Delay(15, ct);
                gp = goalFn();
                double bx = X, by = Y - 50 * U;
                double d = Math.Sqrt((gp.X - bx) * (gp.X - bx) + (gp.Y - by) * (gp.Y - by));
                bool hanging = !OnGround && script == null && curAct == Act.Climb;
                if (dive && (d < 270 * U || (CurAct == Act.Climb && d < 480 * U)) && gp.Y > Y - 260 * U && gp.Y < Y + 40 * U)
                {
                    PointF lead = LeadCursor(gp, 0.18);
                    if (await Dive(lead, delegate { return HandDist(goalFn()) < reach || BodyTouches(goalFn(), 4 * U); }, ct)) return true;
                    continue;
                }
                if (!dive && Math.Abs(gp.X - X) < 140 * U && gp.Y < Y - 40 * U && gp.Y > Y - 250 * U && OnGround)
                {
                    // jump up to grab it
                    double need = (Y - 105 * U) - gp.Y + 25 * U;
                    Task jt = JumpTo(gp.X - Facing * 10 * U, Y, Sup, true, 0.62, Math.Max(20 * U, need), ct, Act.Jump, 0.06);
                    Facing = gp.X >= X ? 1 : -1;
                    while (!jt.IsCompleted)
                    {
                        if (HandDist(goalFn()) < reach * 1.3) return true;
                        await Task.Delay(12, ct);
                    }
                    continue;
                }
                await PlanStep(gp, ct);
                if (CurAct == Act.Climb && script == null && !OnGround)
                {
                    // finished climbing an edge of the screen: leap at the goal
                    PointF lead = LeadCursor(goalFn(), 0.2);
                    if (dive) { if (await Dive(lead, delegate { return HandDist(goalFn()) < reach || BodyTouches(goalFn(), 4 * U); }, ct)) return true; }
                    else
                    {
                        Task jt = JumpTo(lead.X - Facing * 30 * U, lead.Y + 100 * U, null, false, 0.45, 40 * U, ct);
                        while (!jt.IsCompleted) { if (HandDist(goalFn()) < reach * 1.3) return true; await Task.Delay(12, ct); }
                    }
                }
            }
            return false;
        }

        Act CurAct { get { return curAct; } }

        // Guesses where your mouse is going.
        PointF lastCur; double lastCurT; double curVX, curVY;
        PointF LeadCursor(PointF c, double secs)
        {
            double now = Now, dt = now - lastCurT;
            if (dt > 0.01 && dt < 0.5) { curVX = curVX * 0.5 + (c.X - lastCur.X) / dt * 0.5; curVY = curVY * 0.5 + (c.Y - lastCur.Y) / dt * 0.5; }
            lastCur = c; lastCurT = now;
            Rectangle vs = SystemInformation.VirtualScreen;
            return new PointF((float)Clamp(c.X + curVX * secs, vs.Left, vs.Right), (float)Clamp(c.Y + curVY * secs, vs.Top, vs.Bottom));
        }

        static PointF CursorPoint() { Point p = Cursor.Position; return new PointF(p.X, p.Y); }

        // ================= tag =================
        public void StartTag(bool userIsIt)
        {
            Touch();
            Brain.GameStarted();
            RunJob(async delegate (CancellationToken ct)
            {
                PlayingGame = true;
                Parkour = true;
                TagRunning = true; TagUserIsIt = userIsIt; tagHits = 0; tagInvulnUntil = 0;
                try
                {
                    await WaitGround(ct);
                    if (userIsIt) await TagFlee(ct);
                    else await TagChase(ct);
                }
                finally { TagRunning = false; Parkour = false; PlayingGame = false; }
            });
        }

        public void EndTag() { TagRunning = false; }

        // You click on him while he is running away.
        bool TagClick()
        {
            if (!TagRunning) return false;
            if (!TagUserIsIt) return true; // he's chasing you, clicking him does nothing
            if (Now < tagInvulnUntil) return true;
            tagHits++;
            tagInvulnUntil = Now + 0.7;
            AddStars(3);
            ShowBubble(Pick("Ow!", "Hey!", "Eek!", "No fair!") + " " + tagHits + "/5", 1.4);
            return true;
        }

        async Task TagFlee(CancellationToken ct)
        {
            await SayWait("Tag, you're it! Click me 5 times in 60 seconds. Good luck, I'm fast!", ct);
            double end = Now + 60;
            PointF goal = new PointF((float)X, (float)Y);
            double nextGoal = 0;
            while (Now < end && tagHits < 5)
            {
                ct.ThrowIfCancellationRequested();
                await WaitGround(ct);
                PointF c = CursorPoint();
                double bx = X, by = Y - 50 * U;
                double d = Math.Sqrt((c.X - bx) * (c.X - bx) + (c.Y - by) * (c.Y - by));
                if (d < 150 * U)
                {
                    await Evade(c, ct);
                    nextGoal = 0;
                    continue;
                }
                if (Now > nextGoal || Dist(goal, c) < 250 * U)
                {
                    goal = EscapeSpot(c);
                    nextGoal = Now + 1.4;
                }
                if (Dist(goal, new PointF((float)X, (float)Y)) < 40 * U)
                {
                    // safe for now: tease you
                    if (Now > nextTaunt)
                    {
                        nextTaunt = Now + 2.5 + Rnd.NextDouble() * 3;
                        Facing = c.X >= X ? 1 : -1;
                        ShowBubble(Pick("Can't catch me!", "Nyah nyah!", "Too slow!", "Over here!", "Hehe!"), 1.4);
                        BaseAct = Rnd.NextDouble() < 0.5 ? Act.DanceGroove : (Rnd.NextDouble() < 0.5 ? Act.Wave : Act.DanceRunning);
                    }
                    await Wait(0.1, ct);
                    continue;
                }
                BaseAct = Act.Idle;
                await PlanStep(goal, ct);
            }
            BaseAct = Act.Idle;
            await WaitGround(ct);
            Brain.Event(tagHits >= 5 ? "the user beat you at tag (they caught you 5 times)" : "you beat the user at tag (they only caught you " + tagHits + " times)");
            if (tagHits >= 5) { Say("Okay okay, you got me 5 times! You win!"); await Do(Act.Shrug, 1.2, ct); await Do(Act.Cheer, 1.5, ct); }
            else { Say("Time's up! You only tagged me " + tagHits + " times. I win! Hehe!"); Brain.Happy = Math.Min(100, Brain.Happy + 15); await DanceMoves(1, ct); }
        }

        static double Dist(PointF a, PointF b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        // The best place to run to: far from your mouse.
        PointF EscapeSpot(PointF c)
        {
            List<PointF> cand = new List<PointF>();
            foreach (Plat p in World.Plats)
            {
                if (p.Kind == Plat.Floor)
                    for (int i = 1; i < 10; i++) cand.Add(new PointF((float)(p.L + (p.R - p.L) * i / 10.0), (float)p.Y));
                else
                {
                    cand.Add(new PointF((float)(p.L + 30 * U), (float)p.Y));
                    cand.Add(new PointF((float)p.Mid, (float)p.Y));
                    cand.Add(new PointF((float)(p.R - 30 * U), (float)p.Y));
                }
            }
            PointF me = new PointF((float)X, (float)Y);
            PointF best = me; double bs = double.MinValue;
            foreach (PointF q in cand)
            {
                double s = Dist(q, c) - 0.25 * Dist(q, me) + Rnd.NextDouble() * 60 * U;
                // don't run through the mouse
                if ((q.X - X) * (c.X - X) > 0 && Math.Abs(c.X - X) < Math.Abs(q.X - X) && Math.Abs(c.Y - q.Y) < 200 * U) s -= 500 * U;
                if (s > bs) { bs = s; best = q; }
            }
            return best;
        }

        // Dodges your mouse: slides under it, jumps over it, or climbs a wall.
        async Task Evade(PointF c, CancellationToken ct)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            int dirToCursor = c.X >= X ? 1 : -1;
            bool nearWall = X - vs.Left < 90 * U || vs.Right - X < 90 * U;
            Plat cur = CurrentPlat();
            if (cur.Kind != Plat.Floor && Rnd.NextDouble() < 0.6)
            {
                double dir = -dirToCursor;
                double landX = Clamp(X + dir * 260 * U, vs.Left + 30 * U, vs.Right - 30 * U);
                Plat below = World.SurfaceBelow(landX, Y + 8);
                if (landX < cur.L || landX > cur.R)
                {
                    await JumpTo(landX, below.Y, Support.Of(below), true, 0.55, 50 * U, ct, Act.Jump, 0);
                    return;
                }
            }
            if (nearWall && dirToCursor == (X - vs.Left < 90 * U ? 1 : -1) && cur.Kind == Plat.Floor && Rnd.NextDouble() < 0.6)
            {
                // cornered: run up the wall, then leap over the mouse
                int side = X - vs.Left < 90 * U ? -1 : 1;
                double wallX = side < 0 ? vs.Left + 16 * U : vs.Right - 16 * U;
                X = wallX;
                await ClimbTo(wallX, side, Math.Max(vs.Top + 150 * U, Y - 260 * U), ct);
                Facing = -side;
                double land = Clamp(X - side * 420 * U, vs.Left + 30 * U, vs.Right - 30 * U);
                Spin(0.6, 55, -1);
                await JumpTo(land, FloorAt(land), Support.Floor, true, 0.75, 60 * U, ct, Act.Jump, 0);
                return;
            }
            if (c.Y < Y - 55 * U && c.Y > Y - 140 * U && cur.Kind == Plat.Floor)
            {
                // mouse at chest height: slide right under it
                await Slide(dirToCursor * 300 * U, ct);
                return;
            }
            if (c.Y >= Y - 55 * U)
            {
                // mouse down low: jump over it
                double land = Clamp(X + dirToCursor * 260 * U, cur.Kind == Plat.Floor ? vs.Left + 30 * U : cur.L + 20 * U, cur.Kind == Plat.Floor ? vs.Right - 30 * U : cur.R - 20 * U);
                Spin(0.55, 55, -1);
                await JumpTo(land, Y, Sup, true, 0.55, 110 * U, ct, Act.Jump, 0);
                return;
            }
            // mouse above: just run away
            double away = Clamp(X - dirToCursor * 350 * U, cur.Kind == Plat.Floor ? vs.Left + 30 * U : cur.L + 15 * U, cur.Kind == Plat.Floor ? vs.Right - 30 * U : cur.R - 15 * U);
            await RunFor(away, 0.5, ct);
        }

        async Task TagChase(CancellationToken ct)
        {
            Say("I'm it! I only need to tag you ONCE. Run!");
            for (int i = 3; i >= 1; i--) { ShowBubble(i + "...", 0.9); await Wait(1, ct); }
            ShowBubble("Here I come!", 1.2);
            bool got = await Approach(CursorPoint, 34 * U, 45, true, ct);
            await WaitGround(ct);
            Brain.Event(got ? "you chased the user's mouse and tagged it" : "the user's mouse escaped you in tag");
            if (got)
            {
                AddStars(3);
                Say(Pick("TAG! Got you! I win!", "Tagged you! Hehe, I win!"));
                Brain.Happy = Math.Min(100, Brain.Happy + 20);
                await Do(Act.Cheer, 2, ct);
            }
            else
            {
                Say("Phew... you're too fast! You win!");
                await Do(Act.Sad, 1.5, ct);
            }
        }
    }
}
