using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    public enum CursorTrick { None, Hold, Fly, Hang, Ride }

    // What he does to your mouse when you make him angry.
    // Tier 1: warnings. Tier 2: grabs your cursor and throws it away, or hangs on it so it gets heavy.
    // Tier 3: tries to "break" your cursor by stomping on it; wiggle the mouse to shake him off.
    // Esc (or saying sorry) always stops him.
    public partial class Buddy
    {
        public CursorTrick CursorMode = CursorTrick.None;
        public bool AngerJob;
        double hangDown;
        double trickEnd, wiggle, hangPull, flyT, flyDur, rideT, rideHop, ridePushLeft, rideVX, rideVY;
        PointF flyPos, flyVel, ridePush;
        Point lastSet;
        bool haveSet;

        void SetCursor(double x, double y)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            int ix = (int)Clamp(x, vs.Left, vs.Right - 1), iy = (int)Clamp(y, vs.Top, vs.Bottom - 1);
            Native.SetCursorPos(ix, iy);
            lastSet = new Point(ix, iy); haveSet = true;
        }

        public void StopCursorTricks(bool timeUp)
        {
            CursorTrick was = CursorMode;
            CursorMode = CursorTrick.None;
            haveSet = false;
            if (was == CursorTrick.Hang && RagMode) { Rag.Pin1 = -1; Rag.Pin2 = -1; ragStart = Now; }
        }

        void UpdateCursorModes(double now, double dt)
        {
            if (CursorMode == CursorTrick.None) return;
            bool esc = (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0;
            if (esc || now > trickEnd || !S.MouseTricks)
            {
                CursorTrick was = CursorMode;
                StopCursorTricks(true);
                if (was == CursorTrick.Ride) { EnterRagdoll(0, -200 * Dpi); }
                if (esc) { CancelJob(); Brain.OnEscapeGrab(); }
                return;
            }
            Point c = Cursor.Position;
            double moved = haveSet ? Math.Sqrt(Math.Pow(c.X - lastSet.X, 2) + Math.Pow(c.Y - lastSet.Y, 2)) : 0;
            wiggle = wiggle * Math.Exp(-dt / 0.35) + moved;
            double userVX = haveSet ? (c.X - lastSet.X) / dt : 0, userVY = haveSet ? (c.Y - lastSet.Y) / dt : 0;
            Rectangle vs = SystemInformation.VirtualScreen;

            switch (CursorMode)
            {
                case CursorTrick.Hold:
                    {
                        PointF h = HandWorld();
                        SetCursor(h.X, h.Y);
                        break;
                    }
                case CursorTrick.Fly:
                    {
                        flyT += dt;
                        flyVel.Y += (float)(2600 * Dpi * dt);
                        flyPos.X += (float)(flyVel.X * dt); flyPos.Y += (float)(flyVel.Y * dt);
                        if (flyPos.X < vs.Left + 2) { flyPos.X = vs.Left + 2; flyVel.X = Math.Abs(flyVel.X) * 0.5f; }
                        if (flyPos.X > vs.Right - 3) { flyPos.X = vs.Right - 3; flyVel.X = -Math.Abs(flyVel.X) * 0.5f; }
                        if (flyPos.Y < vs.Top + 2) { flyPos.Y = vs.Top + 2; flyVel.Y = Math.Abs(flyVel.Y) * 0.5f; }
                        double fl = FloorAt(flyPos.X) - 4;
                        if (flyPos.Y > fl) { flyPos.Y = (float)fl; flyVel.Y = -Math.Abs(flyVel.Y) * 0.45f; flyVel.X *= 0.7f; }
                        SetCursor(flyPos.X, flyPos.Y);
                        if (flyT >= flyDur) StopCursorTricks(false);
                        break;
                    }
                case CursorTrick.Hang:
                    {
                        // he hangs off your cursor and it gets heavier and heavier
                        bool feetDown = Rag.LowestY() >= FloorAt(c.X) - 3;
                        if (feetDown) hangDown += dt; else hangDown = 0;
                        if (hangDown > 0.5)
                        {
                            // pulled your mouse all the way down: let go
                            StopCursorTricks(false);
                            ShowBubble(Pick("Hmph! Heavy, huh?", "Hehe! Your mouse is on the floor now!"), 2);
                            break;
                        }
                        hangPull = Math.Min(hangPull + 300 * Dpi * dt, 560 * Dpi);
                        double ny = feetDown ? c.Y : c.Y + hangPull * dt;
                        SetCursor(c.X, ny);
                        Rag.PinX = c.X; Rag.PinY = ny + 3;
                        if (wiggle > 800 * Dpi)
                        {
                            StopCursorTricks(false);
                            Rag.AddVelocity(userVX * 0.3, userVY * 0.3, 1 / 60.0);
                            Brain.OnShakenOff();
                        }
                        break;
                    }
                case CursorTrick.Ride:
                    {
                        // standing on top of your cursor, stomping it around
                        if (wiggle > 560 * Dpi)
                        {
                            StopCursorTricks(false);
                            X = c.X; Y = c.Y;
                            EnterRagdoll(userVX * 0.6, userVY * 0.6 - 400 * Dpi);
                            Brain.OnShakenOff();
                            return;
                        }
                        double nx = c.X, ny = c.Y;
                        if (ridePushLeft > 0)
                        {
                            ridePushLeft -= dt;
                            nx += ridePush.X * dt; ny += ridePush.Y * dt;
                        }
                        rideT += dt;
                        double period = 0.6, ph = (rideT % period) / period;
                        rideHop = Math.Sin(Math.Min(1, ph / 0.8) * Math.PI) * 45 * U;
                        if (ph >= 0.8 && ph - dt / period < 0.8)
                        {
                            // SLAM
                            double a = Rnd.NextDouble() * Math.PI - Math.PI / 2; // mostly sideways / down
                            double sp = (1400 + Rnd.NextDouble() * 900) * Dpi;
                            ridePush = new PointF((float)(Math.Cos(a) * sp * (Rnd.NextDouble() < 0.5 ? -1 : 1)), (float)(Math.Abs(Math.Sin(a)) * sp * 0.8));
                            ridePushLeft = 0.13;
                            AddPow(nx, ny);
                            if (Rnd.NextDouble() < 0.3) ShowBubble(Pick("BREAK!", "Take THAT!", "Hyah!", "Grr!"), 0.8);
                        }
                        double fl = FloorAt(nx) - 3;
                        if (ny > fl) ny = fl;
                        SetCursor(nx, ny);
                        X = nx; Y = ny - rideHop;
                        Facing = ridePush.X >= 0 ? 1 : -1;
                        break;
                    }
            }
        }

        Act RideAct()
        {
            double ph = (rideT % 0.6) / 0.6;
            return ph < 0.8 ? (ph < 0.4 ? Act.Jump : Act.Fall) : Act.Stomp;
        }

        // ---------- tier 1 ----------
        public void Warn(string line)
        {
            Touch();
            RunJob(async delegate (CancellationToken ct)
            {
                await WaitGround(ct);
                Facing = Cursor.Position.X >= X ? 1 : -1;
                Say(line);
                await Do(Act.Warn, 1.8, ct);
                await Do(Act.Angry, 1.2, ct);
            });
        }

        bool TricksAllowed(string fallback)
        {
            if (S.MouseTricks) return true;
            Tantrum(fallback);
            return false;
        }

        async Task RunAngry(Func<CancellationToken, Task> body, CancellationToken ct)
        {
            AngerJob = true;
            try { await body(ct); }
            finally { AngerJob = false; StopCursorTricks(false); }
        }

        // ---------- tier 2: grab your cursor and throw it away ----------
        public void ThrowCursor()
        {
            Touch();
            if (!TricksAllowed("GRRR! I'd throw your mouse away if mouse tricks were on!")) return;
            RunJob(delegate (CancellationToken ct0)
            {
                return RunAngry(async delegate (CancellationToken ct)
                {
                    await WaitGround(ct);
                    Say(Pick("THAT'S IT! Give me that mouse!", "You want to throw things? Let's throw YOUR mouse!"));
                    bool got = await Approach(CursorPoint, 30 * U, 10, false, ct);
                    Log.Write("tier 2 throw: reached cursor = " + got);
                    if (!got) { await SayWait("Grrr! Come down here!", ct); return; }
                    CursorMode = CursorTrick.Hold; trickEnd = Now + 4; wiggle = 0;
                    ShowBubble("Got it!", 1);
                    await WaitGround(ct);
                    Rectangle vs = SystemInformation.VirtualScreen;
                    Facing = (X - vs.Left > vs.Right - X) ? -1 : 1; // throw it to the far side
                    BaseAct = Act.Throw;
                    await Wait(0.42, ct);
                    PointF h = HandWorld();
                    flyPos = h; flyVel = new PointF((float)(Facing * (2200 + Rnd.NextDouble() * 900) * Dpi), (float)(-1100 * Dpi));
                    flyT = 0; flyDur = 0.9; trickEnd = Now + 2;
                    CursorMode = CursorTrick.Fly;
                    ShowBubble("YEET!", 1.0);
                    await Wait(0.6, ct);
                    BaseAct = Act.Dust;
                    await SayWait(Pick("And STAY there!", "Hmph! Go fetch!"), ct);
                    BaseAct = Act.Idle;
                    Brain.Vent(25);
                }, ct0);
            });
        }

        // ---------- tier 2 (other version): hang on your cursor so it gets heavy ----------
        public void HangOnCursor()
        {
            Touch();
            if (!TricksAllowed("GRRR! Lucky for you mouse tricks are off!")) return;
            RunJob(delegate (CancellationToken ct0)
            {
                return RunAngry(async delegate (CancellationToken ct)
                {
                    await WaitGround(ct);
                    Say(Pick("Fine! Let's see you carry ME around!", "Hmph! I'm gonna hang on your mouse!"));
                    bool got = await Approach(CursorPoint, 34 * U, 10, false, ct);
                    Log.Write("tier 2 hang: reached cursor = " + got);
                    if (!got) { await SayWait("Grrr! Come down here!", ct); return; }
                    Point c = Cursor.Position;
                    EnterRagdoll(0, 0);
                    Rag.Pin1 = Ragdoll.HandA; Rag.Pin2 = Ragdoll.HandB;
                    Rag.PinX = c.X; Rag.PinY = c.Y + 3;
                    CursorMode = CursorTrick.Hang; trickEnd = Now + 7; hangPull = 120 * Dpi; wiggle = 0; haveSet = false; hangDown = 0;
                    ShowBubble(Pick("Heavy, huh?!", "Hehehe! Pull! PULL!"), 2);
                    while (CursorMode == CursorTrick.Hang) await Wait(0.1, ct);
                    Rag.Pin1 = -1; Rag.Pin2 = -1;
                    await WaitGround(ct);
                    Brain.Vent(25);
                }, ct0);
            });
        }

        // ---------- tier 3: stomp on your cursor ----------
        public void StompCursor()
        {
            Touch();
            if (!TricksAllowed("GRAAAH! I'm SO angry! Good thing mouse tricks are off!")) return;
            RunJob(delegate (CancellationToken ct0)
            {
                return RunAngry(async delegate (CancellationToken ct)
                {
                    await WaitGround(ct);
                    Say(Pick("GRAAAAH! I'm gonna BREAK your mouse!", "THAT'S IT! Your mouse is going DOWN!"));
                    BaseAct = Act.Tantrum;
                    await Wait(0.9, ct);
                    BaseAct = Act.Idle;
                    double end = Now + 16;
                    int rounds = 0;
                    while (Now < end && Brain.Angry > 35)
                    {
                        bool near = await Approach(CursorPoint, 70 * U, 8, false, ct);
                        if (!near) break;
                        await WaitGround(ct);
                        // jump onto it
                        Point c = Cursor.Position;
                        Task jt = JumpTo(c.X, c.Y, null, false, 0.4, 50 * U, ct, Act.Jump, 0.05);
                        await jt;
                        c = Cursor.Position;
                        if (Math.Abs(c.X - X) > 70 * U || Math.Abs(c.Y - Y) > 80 * U) continue;
                        CursorMode = CursorTrick.Ride; trickEnd = Now + 6; rideT = 0; wiggle = 0; haveSet = false; ridePushLeft = 0;
                        OnGround = true;
                        rounds++;
                        Log.Write("tier 3: riding the cursor, round " + rounds);
                        while (CursorMode == CursorTrick.Ride) await Wait(0.1, ct);
                        await WaitGround(ct);
                        if (Now < end && Brain.Angry > 35) { Say(Pick("GRRR! Come back here!", "You can't escape me!", "I'm not done with you!")); await Wait(0.4, ct); }
                    }
                    StopCursorTricks(false);
                    await WaitGround(ct);
                    Brain.Vent(30);
                    await SayWait(Pick("Hmph... I'm tired now. Don't do that again!", "Huff... puff... fine. Truce."), ct);
                }, ct0);
            });
        }
    }
}
