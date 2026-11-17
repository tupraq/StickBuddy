using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // Desktop games: Balloon Pop, Dodgeball, Whack-a-Sticky and Hoops.
    public partial class Buddy
    {
        public string ActiveGame;
        public bool FunMode;          // it's a game: being thrown around doesn't make him angry
        public bool GameAllowsDrag;   // grabbing him is part of the game (don't stop it)
        public bool BodyFrozen;       // a game moves him itself
        public Func<double, bool> SpecialRender;
        public Action<Point> SpecialClick;
        public Func<string, bool> GameHear;
        HudForm hud;

        public HudForm Hud { get { if (hud == null || hud.IsDisposed) hud = new HudForm(Dpi); return hud; } }
        void HideHud() { if (hud != null && !hud.IsDisposed) hud.Hide(); }

        static string Mmss(double secs) { secs = Math.Max(0, secs); return ((int)secs / 60) + ":" + ((int)secs % 60).ToString("00"); }

        void BeginGame(string name, bool allowDrag)
        {
            ActiveGame = name; FunMode = true; GameAllowsDrag = allowDrag; PlayingGame = true;
            Sfx.Off = S.Muted;
            Brain.GameStarted();
            Touch();
        }

        void EndGame()
        {
            HideHud();
            FunMode = false; GameAllowsDrag = false; ActiveGame = null; PlayingGame = false; GameHear = null;
        }

        // The end of a game: score, record, and how he feels about it.
        void GameOver(string game, string title, int score, string line, bool heWon)
        {
            bool record = Scores.Submit(game, score);
            Brain.Event("you played " + title + " with the user (their score: " + score + (heWon ? ", you won" : ", they won") + ")");
            Brain.Happy = Math.Min(100, Brain.Happy + 15);
            Brain.Bored = 0;
            if (record) { Sfx.Play("cheer"); AddStars(5); }
            Say(line + (record ? " And that's a NEW RECORD: " + score + "!" : " Your best is " + Scores.Best(game) + "."));
            FlashAct(heWon ? Act.Cheer : (record ? Act.Cheer : Act.Shrug), 2.2);
        }

        // ---------------- sprites ----------------
        Sprite NewSprite(int kind, double x, double y, double r, Color c, bool gold)
        {
            Sprite s = new Sprite();
            s.Kind = kind; s.X = x; s.Y = y; s.BaseY = y; s.R = r; s.Col = c; s.Gold = gold; s.Phase = Rnd.NextDouble() * 6.28;
            int w = (int)Math.Ceiling(kind == Sprite.Balloon ? r * 2.3 : r * 2.6 + 4), h = (int)Math.Ceiling(kind == Sprite.Balloon ? r * 4.2 : r * 2.6 + 4);
            s.Win = new SpriteForm(w, h, false);
            s.Win.Show();
            s.Win.Draw(s, Now);
            return s;
        }

        static void KillSprite(Sprite s)
        {
            s.Dead = true;
            if (s.Win != null) { try { s.Win.Close(); } catch { } s.Win = null; }
        }

        // ================= Balloon Pop =================
        static readonly Color[] BalloonColors = { Color.FromArgb(240, 70, 80), Color.FromArgb(70, 150, 245), Color.FromArgb(80, 200, 110), Color.FromArgb(250, 190, 40), Color.FromArgb(170, 90, 230), Color.FromArgb(255, 130, 200) };

        Sprite SpawnBalloon(Rectangle wa)
        {
            double r = 26 * Dpi * (0.9 + Rnd.NextDouble() * 0.35);
            double x = 0, y = 0;
            for (int tries = 0; tries < 15; tries++)
            {
                x = wa.Left + 70 * Dpi + Rnd.NextDouble() * (wa.Width - 140 * Dpi);
                y = wa.Top + 110 * Dpi + Rnd.NextDouble() * (wa.Height - 360 * Dpi);
                if (House == null || !House.Shown) break;
                Rectangle hr = House.StructureRect(); hr.Inflate((int)(60 * Dpi), (int)(60 * Dpi));
                if (!hr.Contains((int)x, (int)y)) break;
            }
            Sprite s = NewSprite(Sprite.Balloon, x, y, r, BalloonColors[Rnd.Next(BalloonColors.Length)], Rnd.NextDouble() < 0.15);
            s.VX = (Rnd.NextDouble() < 0.5 ? -1 : 1) * (15 + Rnd.NextDouble() * 45) * Dpi;
            return s;
        }

        public void StartBalloonPop()
        {
            RunJob(async delegate (CancellationToken ct)
            {
                BeginGame("balloons", true);
                List<Sprite> balloons = new List<Sprite>();
                int score = 0, combo = 0;
                double lastPop = -10;
                try
                {
                    await WaitGround(ct);
                    Say("Balloon Pop! Grab me and THROW me into the balloons! Gold ones are worth 3. Go go go!");
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    for (int i = 0; i < 6; i++) balloons.Add(SpawnBalloon(wa));
                    double end = Now + 45, last = Now;
                    while (Now < end)
                    {
                        await Task.Delay(16, ct);
                        double now = Now, dt = Math.Min(0.05, now - last);
                        last = now;
                        Joints j = WorldJoints();
                        PointF[] pts = j.ToArray();
                        for (int i = 0; i < balloons.Count; i++)
                        {
                            Sprite s = balloons[i];
                            if (s.Dead) { if (now > s.Life) balloons[i] = SpawnBalloon(wa); continue; }
                            s.Age += dt;
                            s.X += s.VX * dt;
                            if (s.X < wa.Left + s.R * 1.5 || s.X > wa.Right - s.R * 1.5) s.VX = -s.VX;
                            s.Y = s.BaseY + Math.Sin(now * 1.7 + s.Phase) * 9 * Dpi;
                            if (RagMode)
                            {
                                bool popped = false;
                                for (int k = 0; k < pts.Length && !popped; k++)
                                {
                                    double reach = s.R + (k == Ragdoll.Head ? j.HeadR : 5 * U);
                                    double dx = pts[k].X - s.X, dy = pts[k].Y - s.Y;
                                    if (dx * dx + dy * dy < reach * reach) popped = true;
                                }
                                if (popped)
                                {
                                    KillSprite(s);
                                    s.Life = now + 1.3;
                                    int pts1 = s.Gold ? 3 : 1;
                                    combo = now - lastPop < 1.2 ? combo + 1 : 1;
                                    lastPop = now;
                                    score += pts1 + (combo >= 2 ? combo - 1 : 0);
                                    Sfx.Play("pop");
                                    for (int q = 0; q < 10; q++)
                                    {
                                        double a = Rnd.NextDouble() * Math.PI * 2, sp = (100 + Rnd.NextDouble() * 250) * Dpi;
                                        AddParticle(4, s.X, s.Y, Math.Cos(a) * sp, Math.Sin(a) * sp - 100, 0.7, 4, s.Gold ? Color.Gold : s.Col);
                                    }
                                    ShowBubble(combo >= 3 ? combo + "x COMBO!" : combo == 2 ? "Double pop!" : s.Gold ? "GOLD! +3" : Pick("POP!", "Wheee!", "Got one!", "Boom!"), 1);
                                    continue;
                                }
                            }
                            s.Win.Draw(s, now);
                        }
                        Hud.Set("Balloon Pop", score.ToString(), Mmss(end - now) + " left");
                    }
                }
                finally
                {
                    foreach (Sprite s in balloons) KillSprite(s);
                    EndGame();
                }
                await WaitGround(ct);
                GameOver("balloons", "Balloon Pop", score,
                    score >= 15 ? "WOW! You popped " + score + " with my face! I'm so dizzy... but that was awesome!" :
                    score >= 6 ? "Phew! " + score + " points! You're a good thrower." : "Only " + score + "? Throw me harder next time! Hehe.", false);
            });
        }

        // ================= Dodgeball =================
        static string Hearts(int n) { return n <= 0 ? "x_x" : new string('♥', n); }

        public void StartDodgeball()
        {
            RunJob(async delegate (CancellationToken ct)
            {
                BeginGame("dodgeball", false);
                List<Sprite> balls = new List<Sprite>();
                int lives = 3, dodged = 0;
                const double length = 40;
                try
                {
                    await GoToFloor(ct);
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    Point c0 = Cursor.Position;
                    await MoveTo(c0.X < wa.Left + wa.Width / 2 ? wa.Right - 180 * U : wa.Left + 180 * U, true, ct);
                    Say("Dodgeball! I throw balls at your mouse pointer, you dodge them. 3 lives. Survive 40 seconds!");
                    for (int i = 3; i >= 1; i--) { Hud.Set("Dodgeball", Hearts(lives), "starting in " + i); ShowBubble(i + "...", 0.9); await Wait(1, ct); }
                    ShowBubble("Dodge THIS!", 1);
                    double start = Now, end = Now + length, nextThrow = Now + 0.2, release = -1, nextMove = Now + 6, last = Now;
                    double g = 900 * Dpi;
                    Rectangle vs = SystemInformation.VirtualScreen;
                    while (Now < end && lives > 0)
                    {
                        await Task.Delay(16, ct);
                        double now = Now, dt = Math.Min(0.05, now - last), prog = (now - start) / length;
                        last = now;
                        Point cur = Cursor.Position;
                        PointF lead = LeadCursor(new PointF(cur.X, cur.Y), 0.22);
                        bool ready = OnGround && !RagMode && !targetX.HasValue && script == null && !jumping;
                        if (now > nextMove && release < 0 && ready)
                        {
                            nextMove = now + 4 + Rnd.NextDouble() * 3;
                            double nx = wa.Left + 120 * U + Rnd.NextDouble() * (wa.Width - 240 * U);
                            MoveTo(nx, true, ct, 1.2).ContinueWith(delegate (Task tt) { var ig = tt.Exception; });
                            ready = false;
                        }
                        if (now > nextThrow && release < 0 && ready)
                        {
                            Facing = cur.X >= X ? 1 : -1;
                            BaseAct = Act.Throw;
                            release = now + 0.36;
                        }
                        if (release > 0 && now >= release)
                        {
                            release = -1;
                            PointF h = HandWorld();
                            BaseAct = Act.Idle;
                            double speed = (900 + 750 * prog) * Dpi;
                            double dx = lead.X - h.X, dy = lead.Y - h.Y, dist = Math.Sqrt(dx * dx + dy * dy), t = Math.Max(0.18, dist / speed);
                            Sprite b = NewSprite(Sprite.Ball, h.X, h.Y, 13 * Dpi, Color.FromArgb(225, 50, 50), false);
                            b.VX = dx / t; b.VY = dy / t - 0.5 * g * t;
                            balls.Add(b);
                            Sfx.Play("whoosh");
                            nextThrow = now + Math.Max(0.5, 1.45 - 0.95 * prog) + Rnd.NextDouble() * 0.3;
                            if (Rnd.NextDouble() < 0.25) ShowBubble(Pick("Hyah!", "Catch!", "Dodge this!", "Hehe!", "Too slow!"), 0.8);
                        }
                        foreach (Sprite b in balls)
                        {
                            if (b.Dead) continue;
                            b.Age += dt;
                            b.VY += g * dt; b.X += b.VX * dt; b.Y += b.VY * dt;
                            double floor = FloorAt(b.X);
                            if (b.Y + b.R > floor) { b.Y = floor - b.R; b.VY = -Math.Abs(b.VY) * 0.55; b.VX *= 0.8; b.Bounces++; }
                            if (b.X < vs.Left + b.R) { b.X = vs.Left + b.R; b.VX = Math.Abs(b.VX) * 0.7; }
                            if (b.X > vs.Right - b.R) { b.X = vs.Right - b.R; b.VX = -Math.Abs(b.VX) * 0.7; }
                            double cx = b.X - cur.X, cy = b.Y - cur.Y;
                            if (!b.Hit && cx * cx + cy * cy < Math.Pow(b.R + 9 * Dpi, 2))
                            {
                                b.Hit = true; lives--;
                                b.VX = -b.VX * 0.4; b.VY = -450 * Dpi;
                                Sfx.Play("buzz");
                                ShowBubble(lives > 0 ? Pick("Gotcha!", "Hit!", "Bonk!") : "GOTCHA! That's three!", 1.2);
                            }
                            if (b.Age > 2.8 || b.Bounces > 3) { if (!b.Hit) dodged++; KillSprite(b); }
                            else b.Win.Draw(b, now);
                        }
                        balls.RemoveAll(delegate (Sprite s) { return s.Dead; });
                        Hud.Set("Dodgeball", Hearts(lives), "dodged " + dodged + "   " + Mmss(end - now));
                    }
                }
                finally
                {
                    foreach (Sprite b in balls) KillSprite(b);
                    BaseAct = Act.Idle;
                    EndGame();
                }
                bool survived = lives > 0;
                GameOver("dodgeball", "Dodgeball", dodged + (survived ? 10 : 0),
                    survived ? "You survived! You dodged " + dodged + " balls. How are you so fast?!" : "I got you three times! I win! You dodged " + dodged + " though.", !survived);
            });
        }

        // ================= Whack-a-Sticky =================
        class PopSpot { public double X, EdgeY; }
        PopSpot pop;
        Act popAct;
        bool popGold, popHit;
        double popStart, popStay, popHitAt;
        int whackScore, whackMisses;

        PopSpot PickSpot()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            PopSpot s = new PopSpot();
            List<Plat> wins = new List<Plat>();
            foreach (Plat p in World.Plats) if (p.Kind == Plat.Window && p.R - p.L > 90 * U && p.Y > wa.Top + 130 * U) wins.Add(p);
            double r = Rnd.NextDouble();
            if (wins.Count > 0 && r < 0.5)
            {
                Plat p = wins[Rnd.Next(wins.Count)];
                s.X = p.L + 35 * U + Rnd.NextDouble() * (p.R - p.L - 70 * U); s.EdgeY = p.Y;
            }
            else if (House != null && House.Shown && r < 0.65)
            {
                s.X = House.Sx(40 + Rnd.NextDouble() * 280); s.EdgeY = House.RoofY;
            }
            else
            {
                s.X = wa.Left + 60 * U + Rnd.NextDouble() * (wa.Width - 120 * U); s.EdgeY = Screen.PrimaryScreen.Bounds.Bottom;
            }
            return s;
        }

        double PopAmount(double now)
        {
            if (pop == null) return 0;
            if (popHit) return Math.Max(0, 1 - (now - popHitAt) / 0.12);
            double t = now - popStart;
            if (t < 0.16) return t / 0.16;
            if (t < 0.16 + popStay) return 1;
            return Math.Max(0, 1 - (t - 0.16 - popStay) / 0.16);
        }

        bool WhackRender(double now)
        {
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                double up = PopAmount(now);
                if (pop == null || up <= 0.001)
                {
                    Native.UpdateLayered(Handle, canvas, -10000, -10000);
                    return true;
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float edge = (float)(H - 130 * U);
                int wx = (int)Math.Round(pop.X - W / 2.0), wy = (int)Math.Round(pop.EdgeY - edge);
                float ground = edge + (float)((1 - up) * 115 * U + 28 * U);
                Pose p = Poses.Get(popAct, now - popStart, 0);
                Joints j = Renderer.Compute(p, W / 2f, ground, Facing, (float)U);
                g.SetClip(new RectangleF(0, 0, W, edge));
                RectangleF hit = j.Bounds(); hit.Inflate((float)(10 * U), (float)(10 * U));
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(3, 128, 128, 128))) g.FillRectangle(hb, hit);
                Color col = popGold ? FromHsv((now * 300) % 360, 0.8, 1) : BodyColorNow(now);
                if (popGold)
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, 255, 230, 120)))
                        g.FillEllipse(glow, j.Head.X - 28 * (float)U, j.Head.Y - 28 * (float)U, 56 * (float)U, 56 * (float)U);
                Renderer.DrawFigure(g, j, Facing, (float)U, col, Prop.None, now, true);
                g.ResetClip();
                if (popHit)
                {
                    using (Font f = new Font("Segoe UI", (float)(22 * Dpi), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush yb = new SolidBrush(Color.FromArgb(255, 220, 40)))
                        g.DrawString(popGold ? "+3!" : "BONK!", f, yb, j.Head.X + 12 * (float)U, edge - 70 * (float)U);
                }
            }
            Native.UpdateLayered(Handle, canvas, (int)Math.Round(pop.X - W / 2.0), (int)Math.Round(pop.EdgeY - (H - 130 * U)));
            return true;
        }

        void WhackClick(Point pt)
        {
            if (pop == null || popHit || PopAmount(Now) < 0.3) return;
            popHit = true; popHitAt = Now;
            whackScore += popGold ? 3 : 1;
            Sfx.Play("whack");
        }

        public void StartWhack()
        {
            RunJob(async delegate (CancellationToken ct)
            {
                BeginGame("whack", false);
                whackScore = 0; whackMisses = 0;
                const double length = 30;
                try
                {
                    await WaitGround(ct);
                    Say("Whack-a-Sticky! I'll pop up all over your screen. Click me before I hide! Gold me is worth 3!");
                    await Wait(3, ct);
                    BodyFrozen = true; SpecialRender = WhackRender; SpecialClick = WhackClick;
                    Act[] acts = { Act.Wave, Act.Cheer, Act.DanceGroove, Act.Laugh, Act.Point, Act.Shrug };
                    double start = Now, end = Now + length;
                    while (Now < end)
                    {
                        double prog = (Now - start) / length;
                        pop = PickSpot(); popGold = Rnd.NextDouble() < 0.12; popHit = false; popStart = Now;
                        popStay = 1.15 - 0.6 * prog + Rnd.NextDouble() * 0.25;
                        popAct = acts[Rnd.Next(acts.Length)];
                        Facing = Rnd.NextDouble() < 0.5 ? 1 : -1;
                        while (Now - popStart < 0.32 + popStay && !popHit)
                        {
                            Hud.Set("Whack-a-Sticky", whackScore.ToString(), Mmss(end - Now) + " left");
                            await Task.Delay(16, ct);
                        }
                        if (!popHit) whackMisses++;
                        else await Wait(0.2, ct);
                        pop = null;
                        Hud.Set("Whack-a-Sticky", whackScore.ToString(), Mmss(end - Now) + " left");
                        await Wait(0.12 + Rnd.NextDouble() * 0.45 * (1 - prog), ct);
                    }
                }
                finally
                {
                    pop = null; SpecialRender = null; SpecialClick = null; BodyFrozen = false;
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    X = Clamp(X, wa.Left + 60 * U, wa.Right - 60 * U); Y = wa.Top + 150 * U;
                    RagMode = false; OnGround = false; VX = 0; VY = 0; Sup = Support.Floor; RollNextLanding = true;
                    EndGame();
                }
                await WaitGround(ct);
                GameOver("whack", "Whack-a-Sticky", whackScore,
                    whackScore >= 20 ? "Ow ow ow! " + whackScore + " whacks! My head is spinning!" : whackScore >= 10 ? "You got me " + whackScore + " times! Good reflexes!" : "Hehe, only " + whackScore + "! I'm too quick for you!", whackScore < 10);
            });
        }

        // ================= Hoops =================
        // Sticky shoots at his hoop. accuracy 0..1 = chance of a good aim.
        async Task<bool> StickyShot(Thing ball, double accuracy, CancellationToken ct)
        {
            double rimX = House.Sx(House.OuterW + 20), rimY = House.Sy(160);
            Facing = -1;
            BaseAct = Act.Dribble; Dribbling = true;
            await Wait(1.0 + Rnd.NextDouble() * 0.8, ct);
            Dribbling = false;
            Facing = -1;
            BaseAct = Act.Shoot;
            await Wait(0.5, ct);
            bool aimGood = Rnd.NextDouble() < accuracy;
            double tx = rimX + (aimGood ? (Rnd.NextDouble() - 0.5) * 6 : (Rnd.NextDouble() < 0.5 ? -1 : 1) * (12 + Rnd.NextDouble() * 10)) * House.Hu;
            double T = 0.95, gg = 2300 * Dpi;
            double vx = (tx - ball.X) / T, vy = (rimY - 6 * House.Hu - ball.Y - 0.5 * gg * T * T) / T;
            Things.Throw(ball, vx, vy, -8);
            bool scored = await WatchShot(ball, rimX, rimY, ct);
            BaseAct = Act.Idle;
            return scored;
        }

        async Task<bool> GetBall(Thing ball, CancellationToken ct)
        {
            if (await FetchThing(ball, ct)) return true;
            // it ended up somewhere he can't reach: grab the spare one from home
            Things.PutHome(ball);
            return await FetchThing(ball, ct);
        }

        public void StartHoops()
        {
            Touch();
            if (House == null || !House.Shown || Things == null || Things.Get(ThingKind.Ball) == null)
            {
                Say("I need my house for that, it has the hoop! Say \"show your house\" first.");
                return;
            }
            Thing ball = Things.Get(ThingKind.Ball);
            RunJob(async delegate (CancellationToken ct)
            {
                BeginGame("hoops", false);
                int you = 0, me = 0, rounds = 5;
                double rimX = House.Sx(House.OuterW + 20), rimY = House.Sy(160);
                try
                {
                    await GoToFloor(ct);
                    Say("Hoops! We take turns: 5 shots each. Grab the ball and throw it into my hoop. You go first!");
                    for (int round = 1; round <= rounds; round++)
                    {
                        double spot = House.Sx(House.OuterW + 200);
                        // ---- your shot ----
                        Hud.Set("Hoops", you + " - " + me, "round " + round + ": YOUR shot");
                        if (!await GetBall(ball, ct)) { Say("I lost the ball! Game over."); break; }
                        await MoveTo(spot - 40 * U, false, ct);
                        Facing = 1;
                        Things.Throw(ball, 260 * Dpi, -650 * Dpi, 4);
                        await MoveTo(spot + 200 * U, true, ct);
                        Facing = -1;
                        ShowBubble("Your turn! Grab the ball and shoot!", 3);
                        bool tries = true, held = false, shot = false;
                        double waitUntil = Now + 30;
                        while (tries)
                        {
                            tries = false;
                            while (Now < waitUntil)
                            {
                                if (ball.State == ThingState.Held) held = true;
                                else if (held && ball.State == ThingState.Loose) { shot = true; break; }
                                else if (ball.State == ThingState.Home || ball.State == ThingState.Carried) held = false;
                                await Task.Delay(30, ct);
                            }
                            if (shot)
                            {
                                double rdx = ball.X - rimX, rdy = ball.Y - rimY;
                                if (rdx * rdx + rdy * rdy < Math.Pow(110 * Dpi, 2))
                                {
                                    Say("Hey, no dunking! Shoot from further away. Try again!");
                                    await Task.Delay(1500, ct);
                                    held = false; shot = false; tries = true; waitUntil = Now + 30;
                                }
                            }
                        }
                        if (!shot) Say("Too slow! You lose that shot.");
                        else
                        {
                            bool scored = await WatchShot(ball, rimX, rimY, ct);
                            if (scored) { you++; Sfx.Play("swish"); Say(Pick("Wow, nice shot!", "Swish! Nice one!", "Whoa, you're good!")); }
                            else { Sfx.Play("buzz"); ShowBubble(Pick("Missed! Hehe.", "Ooh, so close!", "Brick!"), 2); }
                        }
                        Hud.Set("Hoops", you + " - " + me, "round " + round + ": my shot");
                        await Wait(1.2, ct);
                        // ---- my shot ----
                        if (!await GetBall(ball, ct)) { Say("I lost the ball! Game over."); break; }
                        await MoveTo(spot, false, ct);
                        bool mine = await StickyShot(ball, 0.55, ct);
                        if (mine) { me++; Sfx.Play("swish"); ShowBubble(Pick("Swish! Hehe!", "Nothing but net!", "Yes!"), 2); FlashAct(Act.Cheer, 1.2); }
                        else { ShowBubble(Pick("Aww!", "Nooo!", "My hands slipped!"), 2); }
                        Hud.Set("Hoops", you + " - " + me, "round " + round + " done");
                        await Wait(1.2, ct);
                        if (round == rounds && you == me && rounds < 8) { rounds++; Say("It's a tie! Sudden death!"); }
                    }
                }
                finally
                {
                    Dribbling = false; BaseAct = Act.Idle;
                    EndGame();
                }
                if (Carrying == ball) Things.Drop(ball);
                GameOver("hoops", "Hoops", you,
                    you > me ? "You win " + you + " to " + me + "! You're a basketball star!" : you < me ? "I win " + me + " to " + you + "! Hehe, rematch?" : "A tie! " + you + " to " + me + "!", me > you);
            });
        }
    }
}
