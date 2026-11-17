using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    public class Particle
    {
        public double X, Y, VX, VY, Life, Max, Size, Phase;
        public int Kind; // 0 steam, 1 Z, 2 heart, 3 note, 4 dust, 5 orbit star, 6 pow, 7 sweat, 8 crack
        public Color Col;
    }

    // The stickman himself: a see-through, always-on-top window that moves around the screen.
    public partial class Buddy : Form
    {
        public Settings S;
        public Brain Brain;
        public Voice Voice;
        public House House;
        public ThingManager Things;
        public OverlayForm Overlay;

        System.Windows.Forms.Timer timer;
        readonly Stopwatch sw = Stopwatch.StartNew();
        double lastT;
        bool firstTick;
        public double Now { get { return sw.Elapsed.TotalSeconds; } }
        public double Dpi = 1, U = 1;
        int W, H;
        Bitmap canvas;
        Font bubbleFont, symbolFont;
        public Random Rnd = new Random();

        // body (when he is moving by himself)
        public double X, Y, VX, VY;
        public bool OnGround;
        public Support Sup = Support.Floor;
        Native.RECT supportRect;
        double lastHouseX = double.NaN;
        public int Facing = 1;
        public Act BaseAct = Act.Idle;
        Act curAct = Act.Fall;
        double actStart;
        Pose shown, blendFrom;
        double blendStart = -10, blendDur = 0.18;
        double landUntil, dizzyUntil, flipStart = -10, flipDur = 0.8, flipCenter = 55;
        int flipSpin = -1;
        bool thrown, afterDive;
        public bool RollNextLanding;
        double lieUntil, kneelUntil;
        public bool Parkour;      // in tag games he rolls instead of falling over
        public double LastDrop;   // how far the last jump fell (for rolling)

        // body (when he is a floppy ragdoll)
        public Ragdoll Rag;
        public bool RagMode;
        double ragSettle, ragStart, lastSlam = -10, lastSmash = -10, ragMaxImpact, ragComVX, ragComVY;
        bool ragSmashed;
        public double FreezeUntil;

        // moving
        double? targetX;
        double moveSpeed;
        bool moveRun, moveKeepFacing;
        Act moveAct = Act.Walk;
        TaskCompletionSource<bool> moveTcs, jumpTcs, groundTcs, scriptTcs;
        bool jumping, jLand;
        double jx0, jy0, jx1, jy1, jT, jDur, jH, jCrouch;
        Support jSup;
        Act jAct = Act.Jump;
        Func<double, bool> script;
        Act scriptAct;
        Support scriptEnd;

        // talking
        string bubble;
        double bubbleUntil;
        public bool ThinkingDots;
        public double ListeningUntil;

        readonly List<Particle> parts = new List<Particle>();
        double nextSteam, nextZ, nextNote;

        // mouse
        bool mouseDown, dragging, pendingPoke;
        Point downPos, lastCursor;
        double dragVX, dragVY, lastClick = -10, pokeAt;
        public bool Grabbing, GrabAngry;
        public double GrabEnd;
        public bool PlayingGame;

        CancellationTokenSource jobCts;
        public bool Busy { get { return jobCts != null; } }
        bool idleJob;
        double nextTopmost;
        public double LastInteraction;
        public bool Sleeping;
        public IntPtr LastExternalWindow = IntPtr.Zero;
        readonly int myPid = Process.GetCurrentProcess().Id;
        public Prop HeldProp = Prop.None;
        public Thing Carrying;
        public Point? SitSpot; // where he sits when he is on furniture (couch, bed, chair)

        NotifyIcon tray;
        ContextMenuStrip menu;
        InputBox input;
        public bool Quitting;

        public Buddy()
        {
            S = Settings.Load();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Stick Buddy";
            Dpi = Native.ScreenDpi() / 96.0;
            bubbleFont = new Font("Segoe UI", (float)(16 * Dpi), FontStyle.Regular, GraphicsUnit.Pixel);
            symbolFont = new Font("Segoe UI Symbol", (float)(16 * Dpi), FontStyle.Bold, GraphicsUnit.Pixel);
            ComputeSize();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            X = wa.Left + wa.Width * 0.72;
            Y = wa.Top - 40;
            OnGround = false;
            Location = new Point(-5000, -5000);
            Size = new Size(W, H);
            shown = Poses.Get(Act.Fall, 0, 0);
            blendFrom = shown;
            Brain = new Brain(this);
            Voice = new Voice(this);
            Rag = new Ragdoll(U, 2300 * Dpi);
            BuildMenu();
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += OnTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000; // layered, toolwindow, topmost, no-activate
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Native.RegisterHotKey(Handle, 1, 0x2 | 0x4 | 0x4000, 0x20); // Ctrl+Shift+Space = type
            Native.RegisterHotKey(Handle, 2, 0x2 | 0x4 | 0x4000, 0x4C); // Ctrl+Shift+L = listen
            Overlay = new OverlayForm(this);
            Things = new ThingManager(this);
            if (S.ShowHouse) { House = new House(this); House.ShowHouse(); Things.CreateAll(); }
            lastT = Now;
            World.Refresh(this, Now, true);
            timer.Start();
            Voice.Start();
            AppIndex.LoadAsync();
            StartWatchers();
            Greet();
        }

        public string StartCommand;
        public static bool TestMode;

        async void Greet()
        {
            if (House != null && House.Shown) await StartupInHouse();
            else
            {
                await Task.Delay(1500);
                Task hi = Say("Hi! I'm " + S.Name + "! Say \"" + S.Name + "\" and tell me what to do. Or double-click me to type!");
            }
            LastInteraction = Now;
            if (!string.IsNullOrEmpty(StartCommand))
            {
                // test mode: commands separated by | run one after another
                await Task.Delay(3000);
                foreach (string cmd in StartCommand.Split('|'))
                {
                    if (cmd.StartsWith("wait:")) { await Task.Delay(TimeSpan.FromSeconds(double.Parse(cmd.Substring(5), System.Globalization.CultureInfo.InvariantCulture))); continue; }
                    if (cmd.StartsWith("test:")) { RunTest(cmd.Substring(5)); await Task.Delay(500); continue; }
                    Brain.Hear(cmd);
                    await Task.Delay(1000);
                    double until = Now + 60;
                    while ((Busy || Voice.IsSpeaking) && Now < until) await Task.Delay(250);
                    await Task.Delay(1500);
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32();
                if (id == 1) ShowInput();
                else if (id == 2) Voice.ListenNow();
                return;
            }
            base.WndProc(ref m);
        }

        public void ComputeSize()
        {
            U = S.Size * Dpi;
            W = (int)Math.Max(440 * Dpi, 210 * U);
            H = (int)(240 * Dpi + 215 * U);
            if (canvas != null) canvas.Dispose();
            canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            if (Rag != null) Rag = new Ragdoll(U, 2300 * Dpi);
        }

        float AnchorY { get { return (float)(H - 14 * U); } }
        float RagAnchorY { get { return (float)(H - 125 * U); } }

        public static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public string Pick(params string[] s) { return s[Rnd.Next(s.Length)]; }
        public void Touch() { LastInteraction = Now; }

        // ================= main loop =================
        void OnTick(object sender, EventArgs e)
        {
            double now = Now;
            double dt = now - lastT;
            lastT = now;
            if (dt > 0.05) dt = 0.05;
            if (dt <= 0) dt = 0.001;
            if (!firstTick) { firstTick = true; Log.Write("first frame"); }
            try
            {
                TrackForeground();
                World.Refresh(this, now, false);
                UpdateMouse(now, dt);
                if (now >= FreezeUntil)
                {
                    UpdatePhysics(now, dt);
                    UpdateCursorModes(now, dt);
                }
                UpdateGrab(now);
                Brain.Tick(now, dt);
                UpdateLife(now, dt);
                UpdateParticles(now, dt);
                if (Things != null) Things.Update(now, dt);
                if (House != null) House.Tick(now);
                Render(now);
                if (Overlay != null) Overlay.Tick(now);
                if (groundTcs != null && ReadyOnGround(now)) Complete(ref groundTcs);
                if (now > nextTopmost)
                {
                    nextTopmost = now + 2.5;
                    KeepOnTop();
                }
            }
            catch (Exception ex) { Log.Write("tick: " + ex); }
        }

        public void KeepOnTop()
        {
            uint fl = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
            if (House != null && House.Shown) Native.SetWindowPos(House.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, fl);
            if (Things != null) Things.KeepOnTop();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, fl);
        }

        void TrackForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && Native.ProcessId(fg) != myPid) LastExternalWindow = fg;
        }

        // ================= mouse =================
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (HideClick()) return;
            if (SpecialClick != null) { if (e.Button == MouseButtons.Left) SpecialClick(Cursor.Position); return; }
            if (Grabbing || CursorMode != CursorTrick.None) { Brain.NiceTry(); if (CursorMode != CursorTrick.Ride) return; }
            if (e.Button == MouseButtons.Left)
            {
                if (TagClick()) return;
                mouseDown = true;
                downPos = Cursor.Position;
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowMenu(Cursor.Position);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (dragging) { EndDrag(); return; }
            if (!mouseDown) return;
            mouseDown = false;
            double now = Now;
            if (now - lastClick < 0.4) { lastClick = -10; pendingPoke = false; ShowInput(); }
            else { lastClick = now; pendingPoke = true; pokeAt = now + 0.4; }
        }

        void UpdateMouse(double now, double dt)
        {
            bool left = (Control.MouseButtons & MouseButtons.Left) != 0;
            Point c = Cursor.Position;
            if (mouseDown && !dragging)
            {
                if (!left) mouseDown = false;
                else if (Math.Abs(c.X - downPos.X) + Math.Abs(c.Y - downPos.Y) > 8 * Dpi) StartDrag();
            }
            if (dragging)
            {
                dragVX = dragVX * 0.55 + (c.X - lastCursor.X) / dt * 0.45;
                dragVY = dragVY * 0.55 + (c.Y - lastCursor.Y) / dt * 0.45;
                Rag.PinX = c.X; Rag.PinY = c.Y;
                if (!left) EndDrag();
            }
            lastCursor = c;
            if (pendingPoke && now >= pokeAt)
            {
                pendingPoke = false;
                Touch();
                Brain.OnPoke();
            }
        }

        void StartDrag()
        {
            dragging = true; mouseDown = false; pendingPoke = false;
            if (!GameAllowsDrag) CancelJob();
            StopCursorTricks(false);
            StopSitting();
            Touch();
            Point c = Cursor.Position;
            if (!RagMode) EnterRagdoll(VX, VY);
            Rag.Pin1 = Rag.Nearest(c.X, c.Y); Rag.Pin2 = -1;
            Rag.PinX = c.X; Rag.PinY = c.Y;
            Rag.Limp = Sleeping;
            dragVX = 0; dragVY = 0; lastCursor = c;
            Brain.OnPickedUp();
        }

        void EndDrag()
        {
            dragging = false; mouseDown = false;
            Rag.Pin1 = -1; Rag.Pin2 = -1;
            double sp = Math.Sqrt(dragVX * dragVX + dragVY * dragVY);
            thrown = sp > 900 * Dpi;
            ragStart = Now;
            Brain.OnThrown(thrown);
        }

        public bool Dragging { get { return dragging; } }

        // ================= ragdoll =================
        public Joints WorldJoints()
        {
            if (RagMode) return Rag.GetJoints();
            if (SitSpot.HasValue) return Renderer.Compute(shown, SitSpot.Value.X, SitSpot.Value.Y, Facing, (float)U);
            return Renderer.Compute(shown, (float)X, (float)Y, Facing, (float)U);
        }

        public void EnterRagdoll(double vx, double vy)
        {
            Joints j = WorldJoints();
            CancelMotion();
            Rag.SetJoints(j, vx, vy, 1 / 60.0);
            Rag.Pin1 = -1; Rag.Pin2 = -1; Rag.Limp = Sleeping;
            RagMode = true;
            OnGround = false;
            ragSettle = 0; ragStart = Now; ragMaxImpact = 0; ragSmashed = false; ragComVX = vx; ragComVY = vy;
            landUntil = 0; flipStart = -10; lieUntil = 0; kneelUntil = 0;
            SitSpot = null;
            if (Carrying != null) { Things.Drop(Carrying); Carrying = null; }
        }

        // Something hit him (a thrown object, or his own anger): he goes flying.
        public void Knock(double vx, double vy)
        {
            if (Hiding) return;
            if (!RagMode) { CancelJob(); StopCursorTricks(false); EnterRagdoll(0, 0); }
            Rag.AddVelocity(vx, vy, 1 / 120.0);
            ragStart = Now;
        }

        void CancelMotion()
        {
            jumping = false; targetX = null; script = null;
            if (moveTcs != null) { TaskCompletionSource<bool> m = moveTcs; moveTcs = null; m.TrySetCanceled(); }
            if (jumpTcs != null) { TaskCompletionSource<bool> m = jumpTcs; jumpTcs = null; m.TrySetCanceled(); }
            if (scriptTcs != null) { TaskCompletionSource<bool> m = scriptTcs; scriptTcs = null; m.TrySetCanceled(); }
        }

        // test only: a pretend hand holding him, so the dangling can be checked without touching the real mouse
        public double TestHoldUntil, TestHoldX, TestHoldY, TestHoldStart;

        void UpdateRagdoll(double now, double dt)
        {
            if (now < TestHoldUntil)
            {
                double t = now - TestHoldStart;
                Rag.PinX = TestHoldX + Math.Sin(t * 2.2) * 260 * Dpi;
                Rag.PinY = TestHoldY - 220 * Dpi * Math.Min(1, t / 0.6) + Math.Sin(t * 4.4) * 50 * Dpi;
                Struggle(now);
            }
            else if (TestHoldUntil > 0) { TestHoldUntil = 0; Rag.Pin1 = -1; ragStart = now; }
            if (dragging || CursorMode == CursorTrick.Hang) Struggle(now);
            double cx0 = Rag.ComX(), cy0 = Rag.ComY();
            RagHit hit = Rag.Step(dt, World.Plats, SystemInformation.VirtualScreen);
            Joints j = Rag.GetJoints();
            X = j.Hip.X; Y = j.Hip.Y;
            // how fast his body was going just before it hit something
            double wallImpact = hit.WallSpeed > 300 * Dpi ? Math.Max(Math.Abs(ragComVX), hit.WallSpeed * 0.5) : 0;
            double ceilImpact = hit.CeilSpeed > 300 * Dpi ? Math.Max(-ragComVY, 0) : 0;
            double floorImpact = hit.FloorSpeed > 300 * Dpi ? Math.Max(ragComVY, 0) : 0;
            ragComVX = (Rag.ComX() - cx0) / dt; ragComVY = (Rag.ComY() - cy0) / dt;

            if (wallImpact > 1100 * Dpi && now - lastSlam > 0.5) { lastSlam = now; WallSlam(wallImpact, hit.WallX, hit.WallY, hit.WallSide); }
            else if (ceilImpact > 1100 * Dpi && now - lastSlam > 0.5) { lastSlam = now; WallSlam(ceilImpact, hit.WallX, hit.WallY, 0); }
            if (floorImpact > ragMaxImpact) ragMaxImpact = floorImpact;
            if (floorImpact > 1300 * Dpi && now - lastSmash > 0.5)
            {
                lastSmash = now;
                ragSmashed = true;
                AddPow(hit.FloorX, hit.FloorY);
                Log.Write("ground smash " + (int)(floorImpact / Dpi));
                if (floorImpact > 2400 * Dpi && S.ImpactFlash && !FunMode && now - lastSlam > 0.5) { lastSlam = now; ImpactFrame(); }
                Brain.OnSmashed(floorImpact / Dpi);
            }

            if (!dragging && CursorMode != CursorTrick.Hang && TestHoldUntil == 0)
            {
                if (hit.Supported && hit.MaxSpeed < 70 * Dpi) ragSettle += dt; else ragSettle = 0;
                if (ragSettle > 0.3 || (now - ragStart > 7 && hit.Supported)) GetUpFromRagdoll(now);
            }
        }

        void Struggle(double now)
        {
            if (Rag.Limp) return;
            double k = (Brain.Angry > 40 ? 1.7 : 1.0) * 4800 * U;
            double burst = 0.35 + 0.65 * Math.Max(0, Math.Sin(now * 2.3));
            k *= burst;
            Rag.FY[Ragdoll.FootA] += Math.Sin(now * 13) * k; Rag.FX[Ragdoll.FootA] += Math.Cos(now * 11) * k * 0.7;
            Rag.FY[Ragdoll.FootB] += Math.Sin(now * 13 + 2.6) * k; Rag.FX[Ragdoll.FootB] += Math.Cos(now * 11 + 2.2) * k * 0.7;
            Rag.FX[Ragdoll.HandA] += Math.Sin(now * 9 + 1) * k * 0.6; Rag.FY[Ragdoll.HandA] += Math.Cos(now * 10) * k * 0.5;
            Rag.FX[Ragdoll.HandB] += Math.Sin(now * 9 + 3) * k * 0.6; Rag.FY[Ragdoll.HandB] += Math.Cos(now * 10 + 2) * k * 0.5;
        }

        void WallSlam(double speed, double wx, double wy, int side)
        {
            Log.Write("wall slam " + (int)(speed / Dpi) + " side " + side);
            ragSmashed = true;
            if (S.ImpactFlash && !FunMode) ImpactFrame();
            AddCrack(wx, wy, side);
            AddStars(5);
            dizzyUntil = Now + 2.2;
            Brain.OnSlammed(speed / Dpi);
        }

        // The "he got hit SO hard" moment: everything goes black, he goes white, time stops for a blink.
        void ImpactFrame()
        {
            double secs = 0.2;
            string slow = Environment.GetEnvironmentVariable("STICKBUDDY_FLASH");
            if (slow != null) double.TryParse(slow, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs);
            FreezeUntil = Now + secs;
            if (Overlay != null) Overlay.Flash(WorldJoints(), secs);
        }

        void GetUpFromRagdoll(double now)
        {
            Joints j = Rag.GetJoints();
            Plat p = World.SurfaceBelow(j.Hip.X, Rag.LowestY() - 6 * U);
            RagMode = false;
            X = j.Hip.X; Y = p.Y; Sup = Support.Of(p); OnGround = true; VX = 0; VY = 0;
            if (p.Kind == Plat.Window) supportRect = Native.GetVisibleRect(p.Hwnd);
            bool upright = j.Neck.Y < j.Hip.Y - 18 * U && j.Hip.Y < Math.Min(j.FootA.Y, j.FootB.Y) - 12 * U;
            if (!upright)
            {
                double fx = (j.FootA.X + j.FootB.X) / 2 - j.Head.X;
                if (Math.Abs(fx) > 3) Facing = Math.Sign(fx);
            }
            Pose cur = Poses.FromJoints(j, Facing, Y, U);
            Pose target = Poses.Get(upright ? Act.Land : Act.Lie, 0, 0);
            Pose.Unwrap(cur, target);
            shown = cur; blendFrom = cur; blendStart = now; blendDur = upright ? 0.2 : 0.35;
            curAct = upright ? Act.Land : Act.Lie; actStart = now;
            bool hard = ragSmashed || ragMaxImpact > 1350 * Dpi;
            if (upright) { landUntil = now + 0.25; lieUntil = 0; kneelUntil = now + 0.25; }
            else { lieUntil = now + (hard ? 1.3 : 0.45); kneelUntil = lieUntil + 0.5; }
            if (hard) { dizzyUntil = Math.Max(dizzyUntil, now + 1.6); if (now - lastSlam > 1) AddStars(4); }
            bool wasThrown = thrown;
            thrown = false;
            double impact = ragMaxImpact;
            Log.Write("get up: " + (upright ? "on feet" : "from lying") + ", impact " + (int)(impact / Dpi) + ", on " + p.Kind + ", angry " + (int)Brain.Angry);
            ragMaxImpact = 0; ragSmashed = false;
            Brain.OnLanded(impact / Dpi, wasThrown, hard);
        }

        public bool Recovering { get { return Now < kneelUntil || RagMode; } }

        bool ReadyOnGround(double now)
        {
            return OnGround && !RagMode && !jumping && script == null && now >= kneelUntil && !dragging;
        }

        // ================= physics =================
        public double FloorAt(double x)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            double y = Math.Max(vs.Top + 1, Math.Min(Y, vs.Bottom - 1)) - 5;
            Screen sc = Screen.FromPoint(new Point((int)x, (int)y));
            return sc.WorkingArea.Bottom;
        }

        static void Complete(ref TaskCompletionSource<bool> t)
        {
            TaskCompletionSource<bool> x = t;
            t = null;
            if (x != null) x.TrySetResult(true);
        }

        public void Fall() { OnGround = false; Sup = Support.Floor; VX = 0; VY = 0; StopSitting(); }

        void UpdatePhysics(double now, double dt)
        {
            if (Hiding || BodyFrozen) return;
            if (RagMode) { UpdateRagdoll(now, dt); return; }
            if (CursorMode == CursorTrick.Ride) return;
            double oldY = Y;
            if (script != null)
            {
                bool done = false;
                try { done = script(dt); } catch (Exception ex) { Log.Write("script: " + ex.Message); done = true; }
                if (done)
                {
                    script = null;
                    if (scriptEnd != null) { OnGround = true; Sup = scriptEnd; if (Sup.IsWindow) supportRect = Native.GetVisibleRect(Sup.Hwnd); }
                    else { OnGround = false; VX = 0; VY = 0; }
                    Complete(ref scriptTcs);
                }
                return;
            }
            if (jumping)
            {
                jT += dt;
                if (jT < jCrouch) return;
                double s = Math.Min(1, (jT - jCrouch) / jDur);
                X = jx0 + (jx1 - jx0) * s;
                Y = jy0 + (jy1 - jy0) * s - 4 * jH * s * (1 - s);
                VY = ((jy1 - jy0) - 4 * jH * (1 - 2 * s)) / jDur;
                VX = (jx1 - jx0) / jDur;
                if (s >= 1)
                {
                    jumping = false;
                    double peak = Math.Min(jy0, jy1);
                    if (jH > 0) { double sp = Clamp(0.5 + (jy0 - jy1) / (8 * jH), 0, 1); peak = jy0 + (jy1 - jy0) * sp - 4 * jH * sp * (1 - sp); }
                    LastDrop = jy1 - peak;
                    if (jLand)
                    {
                        OnGround = true;
                        Sup = jSup ?? Support.Of(World.SurfaceBelow(X, Y - 2));
                        if (Sup.IsWindow) supportRect = Native.GetVisibleRect(Sup.Hwnd);
                        landUntil = now + (LastDrop > 150 * U ? 0.3 : 0.18);
                        VX = 0; VY = 0;
                        if (Parkour && LastDrop > 190 * U) AutoRoll();
                    }
                    else { OnGround = false; VX *= 0.4; VY = Math.Max(0, VY); afterDive = jAct == Act.Dive; }
                    Complete(ref jumpTcs);
                }
                return;
            }
            if (!OnGround)
            {
                VY += 2500 * Dpi * dt;
                X += VX * dt; Y += VY * dt;
                Rectangle vs = SystemInformation.VirtualScreen;
                double half = 22 * U;
                if (X < vs.Left + half) { X = vs.Left + half; VX = Math.Abs(VX) * 0.45; }
                if (X > vs.Right - half) { X = vs.Right - half; VX = -Math.Abs(VX) * 0.45; }
                Plat p = World.LandingOn(X, oldY, Y);
                if (p == null && Y > vs.Bottom) p = World.SurfaceBelow(X, vs.Bottom - 5);
                if (p != null)
                {
                    double impact = VY;
                    if (RollNextLanding)
                    {
                        RollNextLanding = false;
                        Y = p.Y; VX = 0; VY = 0; OnGround = true; Sup = Support.Of(p);
                        if (Sup.IsWindow) supportRect = Native.GetVisibleRect(Sup.Hwnd);
                        AutoRoll();
                        return;
                    }
                    if (impact > 1900 * Dpi && !Parkour)
                    {
                        // a really hard landing: he crumples like a ragdoll and has to get up
                        Y = p.Y - 1;
                        EnterRagdoll(VX, VY);
                        return;
                    }
                    Y = p.Y; VX = 0; VY = 0; OnGround = true; Sup = Support.Of(p);
                    if (Sup.IsWindow) supportRect = Native.GetVisibleRect(Sup.Hwnd);
                    landUntil = now + 0.25;
                    if (Parkour && (impact > 1100 * Dpi || afterDive)) AutoRoll();
                    afterDive = false;
                    Brain.OnLanded(impact / Dpi, thrown, false);
                    thrown = false;
                }
                return;
            }

            // standing on something: move with it
            switch (Sup.Kind)
            {
                case Plat.Window:
                    {
                        IntPtr h = Sup.Hwnd;
                        if (!Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsZoomed(h)) { Fall(); return; }
                        Native.RECT r = Native.GetVisibleRect(h);
                        double dx = r.Left - supportRect.Left, dy = r.Top - supportRect.Top;
                        if (dx != 0 || dy != 0)
                        {
                            // if you yank the window away really fast, he gets flung off
                            double speed = Math.Sqrt(dx * dx + dy * dy) / dt;
                            supportRect = r;
                            if (speed > 2600 * Dpi && Rnd.NextDouble() < 0.7)
                            {
                                X += dx; Y = r.Top;
                                CancelJob();
                                EnterRagdoll(dx / dt * 0.8, dy / dt * 0.8 - 300 * Dpi);
                                Brain.OnFlungOff();
                                return;
                            }
                            X += dx;
                        }
                        Y = r.Top;
                        if (X < r.Left - 5 || X > r.Right + 5) { Fall(); return; }
                        break;
                    }
                case Plat.Upper:
                case Plat.Roof:
                    {
                        if (House == null || !House.Shown) { Fall(); return; }
                        Y = Sup.Kind == Plat.Upper ? House.UpperY : House.RoofY;
                        if (!House.OnLevel(Sup.Kind, X)) { Fall(); return; }
                        break;
                    }
                default:
                    Y = FloorAt(X);
                    break;
            }
            if (House != null && House.Shown)
            {
                if (!double.IsNaN(lastHouseX) && House.HX != lastHouseX && (Sup.IsHouse || (Sup.Kind == Plat.Floor && House.InsideX(X))))
                    X += House.HX - lastHouseX;
                lastHouseX = House.HX;
            }

            if (targetX.HasValue)
            {
                double dx = targetX.Value - X, step = moveSpeed * dt;
                if (Math.Abs(dx) <= step) { X = targetX.Value; targetX = null; Complete(ref moveTcs); }
                else { X += Math.Sign(dx) * step; if (!moveKeepFacing) Facing = Math.Sign(dx); }
            }
        }

        // ================= movement you can "await" =================
        public Task MoveTo(double x, bool run, CancellationToken ct, double speedMul = 1)
        {
            return MoveTo(x, run ? Act.Run : Act.Walk, ct, speedMul, false);
        }

        public Task MoveTo(double x, Act act, CancellationToken ct, double speedMul, bool keepFacing)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            x = Clamp(x, vs.Left + 30 * U, vs.Right - 30 * U);
            if (moveTcs != null) moveTcs.TrySetCanceled();
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            moveTcs = tcs;
            targetX = x;
            moveAct = act;
            moveRun = act == Act.Run;
            moveKeepFacing = keepFacing;
            double baseSpeed = act == Act.Run ? 430 : act == Act.Walk ? 150 : 90;
            moveSpeed = baseSpeed * Dpi * Math.Max(0.75, Math.Sqrt(S.Size)) * speedMul;
            StopSitting();
            ct.Register(delegate
            {
                if (moveTcs == tcs) { targetX = null; moveTcs = null; }
                tcs.TrySetCanceled();
            });
            return tcs.Task;
        }

        public Task JumpTo(double x, double y, Support sup, bool land, double dur, double height, CancellationToken ct)
        {
            return JumpTo(x, y, sup, land, dur, height, ct, Act.Jump, 0);
        }

        public Task JumpTo(double x, double y, Support sup, bool land, double dur, double height, CancellationToken ct, Act air, double crouch)
        {
            if (jumpTcs != null) jumpTcs.TrySetCanceled();
            targetX = null;
            StopSitting();
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            jumpTcs = tcs;
            jumping = true;
            jx0 = X; jy0 = Y; jx1 = x; jy1 = y; jT = 0; jDur = dur; jCrouch = crouch;
            jH = height + Math.Max(0, jy0 - jy1) * 0.5;
            OnGround = false; jSup = sup; jLand = land; jAct = air;
            if (Math.Abs(x - X) > 2) Facing = Math.Sign(x - X);
            ct.Register(delegate
            {
                if (jumpTcs == tcs) { jumping = false; jumpTcs = null; }
                tcs.TrySetCanceled();
            });
            return tcs.Task;
        }

        // Moves him by a little program (stairs, climbing, sliding...). step returns true when done.
        public Task RunScript(Func<double, bool> step, Act act, Support end, CancellationToken ct)
        {
            if (scriptTcs != null) scriptTcs.TrySetCanceled();
            targetX = null; jumping = false;
            StopSitting();
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            scriptTcs = tcs;
            script = step; scriptAct = act; scriptEnd = end;
            OnGround = false;
            ct.Register(delegate
            {
                if (scriptTcs == tcs)
                {
                    script = null; scriptTcs = null;
                    if (end != null) { OnGround = true; Sup = end; }
                }
                tcs.TrySetCanceled();
            });
            return tcs.Task;
        }

        public Task WaitGround(CancellationToken ct)
        {
            if (ReadyOnGround(Now)) return Task.FromResult(true);
            if (groundTcs == null) groundTcs = new TaskCompletionSource<bool>();
            TaskCompletionSource<bool> t = groundTcs;
            ct.Register(delegate { t.TrySetCanceled(); });
            return t.Task;
        }

        public Task Wait(double sec, CancellationToken ct) { return Task.Delay(TimeSpan.FromSeconds(Math.Max(0, sec)), ct); }

        public async Task Do(Act a, double sec, CancellationToken ct)
        {
            BaseAct = a;
            try { await Wait(sec, ct); }
            finally { if (BaseAct == a) BaseAct = Act.Idle; }
        }

        // Shows a pose for a moment without stopping what he is doing.
        public async void FlashAct(Act a, double sec)
        {
            Act prev = BaseAct;
            BaseAct = a;
            await Task.Delay(TimeSpan.FromSeconds(sec));
            if (BaseAct == a) BaseAct = prev == a ? Act.Idle : prev;
        }

        public void Spin(double dur, double centerHeight, int dir)
        {
            flipStart = Now; flipDur = dur; flipCenter = centerHeight; flipSpin = dir;
        }

        public async Task GoToFloor(CancellationToken ct)
        {
            await WaitGround(ct);
            if (Sup.IsWindow)
            {
                Native.RECT r = supportRect;
                double side = (X - r.Left < r.Right - X) ? r.Left - 40 * U : r.Right + 40 * U;
                Rectangle vs = SystemInformation.VirtualScreen;
                if (side < vs.Left + 30 * U) side = r.Right + 40 * U;
                if (side > vs.Right - 30 * U) side = r.Left - 40 * U;
                await JumpTo(side, FloorAt(side), Support.Floor, true, 0.6, 50 * U, ct);
                await WaitGround(ct);
            }
            else if (Sup.Kind == Plat.Upper) await StairsDown(ct);
            else if (Sup.Kind == Plat.Roof && House != null)
            {
                double side = House.Sx(House.OuterW + 45);
                await JumpTo(side, FloorAt(side), Support.Floor, true, 0.75, 40 * U, ct);
                await WaitGround(ct);
            }
        }

        public async Task RunTo(double x, CancellationToken ct)
        {
            await GoToFloor(ct);
            await MoveTo(x, true, ct);
        }

        public Task Hop(CancellationToken ct)
        {
            return JumpTo(X, Y, Sup, true, 0.42, 45 * U, ct);
        }

        // Jumps onto the top edge of a window (or stands next to it if there is no room above).
        public async Task StandOnWindow(IntPtr h, double fromLeft, CancellationToken ct)
        {
            Native.RECT r = Native.GetVisibleRect(h);
            Rectangle wa = Screen.FromHandle(h).WorkingArea;
            double standX = Clamp(r.Left + fromLeft, r.Left + 30 * U, r.Right - 30 * U);
            if (Sup.IsWindow && Sup.Hwnd == h && OnGround) { await MoveTo(standX, false, ct); return; }
            if (r.Top - 110 * U > wa.Top && r.Width > 120 * U && !Native.IsZoomed(h))
            {
                double launch = Clamp(standX + (X < standX ? -130 : 130) * U, wa.Left + 30 * U, wa.Right - 30 * U);
                await RunTo(launch, ct);
                await JumpTo(standX, r.Top, Support.Window(h), true, 0.75, 60 * U, ct, Act.Jump, 0.12);
                supportRect = Native.GetVisibleRect(h);
            }
            else
            {
                await RunTo(Clamp(standX, wa.Left + 30 * U, wa.Right - 30 * U), ct);
            }
        }

        // ================= jobs (one thing at a time) =================
        public void RunJob(Func<CancellationToken, Task> job) { RunJobInner(job, false); }
        public void RunIdleJob(Func<CancellationToken, Task> job) { RunJobInner(job, true); }

        async void RunJobInner(Func<CancellationToken, Task> job, bool idle)
        {
            CancelJob();
            CancellationTokenSource cts = new CancellationTokenSource();
            jobCts = cts;
            idleJob = idle;
            try { await job(cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Write("job: " + ex);
                if (!cts.IsCancellationRequested) Say("Oops! Something went wrong: " + ex.Message);
            }
            finally
            {
                if (jobCts == cts)
                {
                    jobCts = null; BaseAct = Act.Idle; Grabbing = false; ThinkingDots = false; idleJob = false;
                    HeldProp = Prop.None; Parkour = false;
                }
            }
        }

        public void CancelJob()
        {
            CancellationTokenSource c = jobCts;
            jobCts = null;
            if (c != null) c.Cancel();
            BaseAct = Act.Idle; Grabbing = false; ThinkingDots = false; idleJob = false; HeldProp = Prop.None; Parkour = false;
            targetX = null;
            if (moveTcs != null) { TaskCompletionSource<bool> m = moveTcs; moveTcs = null; m.TrySetCanceled(); }
            EndTag();
            if (House != null) House.TvOn = false;
        }

        public bool IsIdleJob { get { return idleJob; } }

        // ================= talking =================
        public Task Say(string text) { return Say(text, 0); }

        public Task Say(string text, double extra)
        {
            if (string.IsNullOrEmpty(text)) return Task.FromResult(true);
            bubble = text;
            bubbleUntil = Now + Math.Min(16, 2.6 + text.Length * 0.07) + extra;
            Log.Write("SAY: " + text);
            if (Hiding) return Voice.Speak(text, Brain.VoiceMood());
            return Voice.Speak(text, Brain.VoiceMood());
        }

        public void ShowBubble(string text, double secs)
        {
            bubble = text;
            bubbleUntil = Now + secs;
        }

        public void ClearBubble() { bubbleUntil = 0; }

        public void OnWake()
        {
            Touch();
            if (Sleeping) Sleeping = false;
            if (Busy && IsIdleJob) CancelJob();
            Say(Pick("Yes?", "I'm listening!", "What's up?", "Yeah?"));
            ListeningUntil = Now + 9;
        }

        public void OnVoiceProblem(string msg)
        {
            ShowBubble("I can't hear you right now (" + msg + "). Double-click me to type instead!", 9);
        }

        // ================= deciding the animation =================
        Act DecideAct(double now)
        {
            if (now < lieUntil) return Act.Lie;
            if (now < kneelUntil && curAct != Act.Land) return Act.Kneel;
            if (CursorMode == CursorTrick.Ride) return RideAct();
            if (script != null) return scriptAct;
            if (jumping)
            {
                if (jT < jCrouch) return Act.Crouch;
                if (jAct != Act.Jump) return jAct;
                return VY < 0 ? Act.Jump : Act.Fall;
            }
            if (!OnGround) return Grabbing ? Act.GrabIdle : (VY < -50 ? Act.Jump : Act.Fall);
            if (now < landUntil) return Act.Land;
            if (targetX.HasValue) return Grabbing ? Act.GrabRun : moveAct;
            if (Grabbing) return Act.GrabIdle;
            Act a = BaseAct;
            if (a != Act.Idle) return a;
            if (Sleeping) return Act.Sleep;
            bool feel = S.Feelings;
            if (feel && Brain.Angry > 55) return Act.Angry;
            if (feel && Brain.Bored > 65) return (Math.Sin(now * 0.45) > 0.93) ? Act.Yawn : Act.Sit;
            if (Voice.IsSpeaking) return Act.Talk;
            if (now < ListeningUntil) return Act.Listen;
            if (ThinkingDots) return Act.Think;
            return Act.Idle;
        }

        Color BodyColorNow(double now)
        {
            Color c = S.BodyColor;
            if (S.Rainbow) c = FromHsv((now * 70) % 360, 0.75, 0.95);
            if (S.Feelings && Brain.Angry > 30)
            {
                // angry = darker and redder
                double k = Math.Min(0.6, (Brain.Angry - 30) / 80.0);
                c = Color.FromArgb((int)(c.R + (170 - c.R) * k), (int)(c.G * (1 - k)), (int)(c.B * (1 - k)));
            }
            return c;
        }

        public static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        static double Smooth(double t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        // ================= drawing =================
        void Render(double now)
        {
            if (SpecialRender != null && SpecialRender(now)) return;
            if (RenderHiding(now)) return;

            Act a = DecideAct(now);
            if (a != curAct)
            {
                blendDur = (a == Act.Kneel || curAct == Act.Kneel || a == Act.Lie) ? 0.32 : 0.18;
                if (a == Act.Roll || curAct == Act.Roll || a == Act.Dive) blendDur = 0.1;
                if (a == Act.DanceRobot || curAct == Act.DanceSpin) blendDur = 0.12;
                blendFrom = shown; blendStart = now; curAct = a; actStart = now;
                Pose.Unwrap(blendFrom, Poses.Get(curAct, 0, 0));
            }
            Pose target = Poses.Get(curAct, now - actStart, 0);
            if (Carrying != null && !RagMode) { target.AU = 75; target.AF = 105; target.BU = 65; target.BF = 100; }
            double bt = (now - blendStart) / blendDur;
            shown = bt < 1 ? Pose.Lerp(blendFrom, target, Smooth(bt)) : target;

            Joints jw;
            int wx, wy;
            if (RagMode)
            {
                jw = Rag.GetJoints();
                wx = (int)Math.Round(X - W / 2.0); wy = (int)Math.Round(Y - RagAnchorY);
            }
            else
            {
                double sy = SitSpot.HasValue ? SitSpot.Value.Y : Y;
                double sx = SitSpot.HasValue ? SitSpot.Value.X : X;
                jw = Renderer.Compute(shown, (float)sx, (float)sy, Facing, (float)U);
                wx = (int)Math.Round(sx - W / 2.0); wy = (int)Math.Round(sy - AnchorY);
            }
            Joints j = jw.Offset(-wx, -wy);
            Color col = BodyColorNow(now);
            bool flash = Overlay != null && Overlay.Flashing;

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                if (!flash)
                {
                    GraphicsState gs = g.Save();
                    if (!RagMode && now - flipStart < flipDur)
                    {
                        float ang = (float)(360 * Smooth((now - flipStart) / flipDur)) * flipSpin * Facing;
                        float ax = (float)(X - wx), cy = (float)(Y - wy - flipCenter * U);
                        g.TranslateTransform(ax, cy); g.RotateTransform(ang); g.TranslateTransform(-ax, -cy);
                    }
                    RectangleF hit = j.Bounds();
                    hit.Inflate((float)(9 * U), (float)(9 * U));
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(3, 128, 128, 128))) g.FillRectangle(hb, hit);

                    Prop prop = HeldProp != Prop.None ? HeldProp : curAct == Act.Write ? Prop.Pencil : curAct == Act.Play ? Prop.Controller : Prop.None;
                    if (curAct == Act.DrawSit) prop = Prop.Pencil;
                    Renderer.DrawFigure(g, j, Facing, (float)U, col, prop, now, true);
                    g.Restore(gs);

                    DrawParticles(g, wx, wy, now);
                    DrawOverlays(g, j, now, wx, wy);
                }
            }
            if (dumpDir != null && now > nextDump && dumpN < 400)
            {
                nextDump = now + dumpEvery;
                try { canvas.Save(System.IO.Path.Combine(dumpDir, "frame" + (dumpN++).ToString("000") + "_" + now.ToString("0.0") + "_" + (RagMode ? "Rag" : curAct.ToString()) + ".png")); } catch { }
            }
            Native.UpdateLayered(Handle, canvas, wx, wy);
        }

        // For testing: set STICKBUDDY_DUMP to a folder and pictures of him get saved there.
        readonly string dumpDir = Environment.GetEnvironmentVariable("STICKBUDDY_DUMP");
        double nextDump, dumpEvery = 0.5;
        int dumpN;

        void DrawOverlays(Graphics g, Joints j, double now, int wx, int wy)
        {
            float d = (float)Dpi, u = (float)U;
            float headTop = j.Head.Y - j.HeadR;

            // listening: little sound waves next to his head
            if (now < ListeningUntil)
            {
                float cx = j.Head.X - Facing * 14 * u, cy = j.Head.Y;
                for (int i = 0; i < 3; i++)
                {
                    double ph = (now * 2.5 + i * 0.33) % 1.0;
                    float r = (float)(4 + ph * 12) * u;
                    int al = (int)(220 * (1 - ph));
                    using (Pen p = new Pen(Color.FromArgb(al, 40, 170, 255), 2.2f * u))
                    {
                        float start = Facing > 0 ? 135 : -45;
                        g.DrawArc(p, cx - r, cy - r, r * 2, r * 2, start, 90);
                    }
                }
            }

            // angry mark next to his head
            if (S.Feelings && Brain.Angry > 40 && !Sleeping)
            {
                float pulse = 1 + 0.15f * (float)Math.Sin(now * 9);
                Renderer.DrawAngerMark(g, j.Head.X + Facing * 13 * u, headTop - 2 * u, 11 * u * pulse, 235);
            }

            // thinking: thought bubbles with dots
            if (ThinkingDots)
            {
                using (SolidBrush wb = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                using (Pen bp = new Pen(Color.FromArgb(200, 40, 40, 55), 1.6f * d))
                {
                    float bx = j.Head.X + Facing * 12 * u, by = headTop - 6 * u;
                    g.FillEllipse(wb, bx - 3 * d, by - 3 * d, 6 * d, 6 * d); g.DrawEllipse(bp, bx - 3 * d, by - 3 * d, 6 * d, 6 * d);
                    float cx2 = bx + Facing * 9 * d, cy2 = by - 12 * d;
                    g.FillEllipse(wb, cx2 - 5 * d, cy2 - 5 * d, 10 * d, 10 * d); g.DrawEllipse(bp, cx2 - 5 * d, cy2 - 5 * d, 10 * d, 10 * d);
                    RectangleF cloud = new RectangleF(cx2 + Facing * 10 * d - 30 * d, cy2 - 40 * d, 60 * d, 28 * d);
                    using (GraphicsPath gp = Renderer.RoundRect(cloud, 13 * d)) { g.FillPath(wb, gp); g.DrawPath(bp, gp); }
                    using (SolidBrush dot = new SolidBrush(Color.FromArgb(40, 40, 55)))
                        for (int i = 0; i < 3; i++)
                        {
                            float jump = (float)Math.Max(0, Math.Sin(now * 6 - i * 0.8)) * 4 * d;
                            g.FillEllipse(dot, cloud.X + 14 * d + i * 13 * d, cloud.Y + 11 * d - jump, 6 * d, 6 * d);
                        }
                }
            }

            // speech bubble
            if (Voice.IsSpeaking && bubble != null) bubbleUntil = Math.Max(bubbleUntil, now + 0.8);
            if (bubble != null && now < bubbleUntil && !(ThinkingDots && !Voice.IsSpeaking && bubbleUntil - now > 20))
            {
                float maxW = 300 * d;
                SizeF sz = g.MeasureString(bubble, bubbleFont, (int)maxW);
                float bw = Math.Min(maxW, sz.Width) + 24 * d, bh = sz.Height + 16 * d;
                float top = Math.Min(headTop, j.Bounds().Top);
                float bottom = top - (ThinkingDots ? 58 * d : 14 * d);
                Rectangle vs = SystemInformation.VirtualScreen;
                float screenTop = vs.Top - wy + 4;
                float maxH = bottom - Math.Max(4 * d, screenTop);
                if (maxH < 30 * d) maxH = 30 * d;
                if (bh > maxH) bh = maxH;
                if (bottom - bh < screenTop) bottom = screenTop + bh;
                float bx = j.Head.X - bw / 2;
                float lo = Math.Max(2, vs.Left - wx + 4), hi = Math.Min(W - bw - 2, vs.Right - wx - 4 - bw);
                if (bx < lo) bx = lo;
                if (bx > hi) bx = hi;
                RectangleF br = new RectangleF(bx, bottom - bh, bw, bh);
                double left = bubbleUntil - now;
                int alpha = left < 0.35 ? (int)(255 * left / 0.35) : 255;
                if (alpha > 5) Renderer.DrawBubble(g, br, new PointF(j.Head.X, headTop - 4 * d), bubble, bubbleFont, d, alpha);
            }
        }

        // ================= particles =================
        public void AddParticle(int kind, double x, double y, double vx, double vy, double life, double size, Color c)
        {
            Particle p = new Particle();
            p.Kind = kind; p.X = x; p.Y = y; p.VX = vx; p.VY = vy; p.Life = life; p.Max = life; p.Size = size; p.Col = c;
            p.Phase = Rnd.NextDouble() * 6.28;
            parts.Add(p);
            if (parts.Count > 140) parts.RemoveAt(0);
        }

        public PointF HeadWorld() { return WorldJoints().Head; }
        public PointF HandWorld() { return WorldJoints().HandA; }

        public void AddStars(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Particle p = new Particle();
                p.Kind = 5; p.Life = 2.0; p.Max = 2.0; p.Size = 14; p.Phase = i * 6.283 / n;
                p.Col = Color.FromArgb(255, 210, 40);
                parts.Add(p);
            }
        }

        public void AddHearts(int n)
        {
            PointF h = HeadWorld();
            for (int i = 0; i < n; i++)
                AddParticle(2, h.X + (Rnd.NextDouble() - 0.5) * 30 * U, h.Y - 10 * U, (Rnd.NextDouble() - 0.5) * 40, -60 - Rnd.NextDouble() * 50, 1.6, 15 + Rnd.Next(8), Color.FromArgb(255, 80, 130));
        }

        public void AddNotes(int n)
        {
            PointF h = HeadWorld();
            for (int i = 0; i < n; i++)
                AddParticle(3, h.X + Facing * 14 * U, h.Y - 6 * U, Facing * (20 + Rnd.NextDouble() * 30), -50 - Rnd.NextDouble() * 30, 1.8, 16, FromHsv(Rnd.Next(360), 0.7, 0.9));
        }

        public void AddDust()
        {
            PointF hand = HandWorld();
            for (int i = 0; i < 2; i++)
                AddParticle(4, hand.X + Facing * 10 * U, hand.Y + 6 * U, (Rnd.NextDouble() - 0.5) * 80, -40 - Rnd.NextDouble() * 60, 0.5, 2.5, Color.FromArgb(70, 70, 80));
        }

        public void AddFootDust(int n)
        {
            for (int i = 0; i < n; i++)
                AddParticle(4, X + (Rnd.NextDouble() - 0.5) * 20 * U, Y - 2, -Facing * (40 + Rnd.NextDouble() * 120), -30 - Rnd.NextDouble() * 70, 0.45, 3, Color.FromArgb(150, 140, 130));
        }

        public void AddPow(double x, double y)
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                AddParticle(6, x, y - 2, Math.Cos(a) * 160 * Dpi, Math.Sin(a) * 90 * Dpi - 40, 0.35, 5, Color.FromArgb(255, 200, 40));
            }
        }

        public void AddCrack(double x, double y, int side)
        {
            AddParticle(8, x, y, side, 0, 2.6, 60 * U, Color.FromArgb(40, 40, 45));
        }

        void UpdateParticles(double now, double dt)
        {
            if (S.Feelings && Brain.Angry > 55 && now > nextSteam && !dragging && !Hiding)
            {
                nextSteam = now + 0.22;
                PointF h = HeadWorld();
                AddParticle(0, h.X + (Rnd.NextDouble() - 0.5) * 14 * U, h.Y - 10 * U, (Rnd.NextDouble() - 0.5) * 50, -80 - Rnd.NextDouble() * 40, 0.9, 5 * U, Color.White);
            }
            if ((curAct == Act.Sleep || (Sleeping && RagMode)) && now > nextZ)
            {
                nextZ = now + 1.1;
                PointF h = HeadWorld();
                AddParticle(1, h.X, h.Y - 12 * U, 25, -35, 2.4, 12 + Rnd.Next(6), Color.FromArgb(90, 110, 200));
            }
            if ((curAct == Act.Play || curAct.ToString().StartsWith("Dance")) && now > nextNote)
            {
                nextNote = now + 0.5;
                AddNotes(1);
            }
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle p = parts[i];
                p.Life -= dt;
                if (p.Life <= 0) { parts.RemoveAt(i); continue; }
                if (p.Kind == 5 || p.Kind == 8) continue;
                p.X += p.VX * dt; p.Y += p.VY * dt;
                if (p.Kind == 4 || p.Kind == 7) p.VY += 400 * dt;
                if (p.Kind == 1 || p.Kind == 3) p.X += Math.Sin(now * 3 + p.Phase) * 20 * dt;
                if (p.Kind == 0) p.Size += 10 * dt * U;
            }
        }

        void DrawParticles(Graphics g, int wx, int wy, double now)
        {
            PointF head = PointF.Empty;
            bool needHead = false;
            foreach (Particle p in parts) if (p.Kind == 5) { needHead = true; break; }
            if (needHead) head = HeadWorld();
            foreach (Particle p in parts)
            {
                float a = (float)Clamp(p.Life / p.Max, 0, 1);
                int al = (int)(255 * Math.Min(1, a * 1.6));
                float x = (float)(p.X - wx), y = (float)(p.Y - wy);
                switch (p.Kind)
                {
                    case 0:
                        using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(170 * a), 235, 235, 240)))
                            g.FillEllipse(b, x - (float)p.Size, y - (float)p.Size, (float)p.Size * 2, (float)p.Size * 2);
                        break;
                    case 1:
                        using (Font f = new Font("Segoe UI", (float)(p.Size * Dpi), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(al, p.Col)))
                            g.DrawString("z", f, b, x, y);
                        break;
                    case 2:
                    case 3:
                        using (Font f = new Font("Segoe UI Symbol", (float)(p.Size * Dpi), FontStyle.Regular, GraphicsUnit.Pixel))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(al, p.Col)))
                            g.DrawString(p.Kind == 2 ? "♥" : "♪", f, b, x, y);
                        break;
                    case 4:
                    case 6:
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(al, p.Col)))
                            g.FillEllipse(b, x - (float)p.Size * (float)Dpi / 2, y - (float)p.Size * (float)Dpi / 2, (float)p.Size * (float)Dpi, (float)p.Size * (float)Dpi);
                        break;
                    case 5:
                        {
                            double ang = now * 4 + p.Phase;
                            float sx = (float)(head.X - wx + Math.Cos(ang) * 16 * U), sy = (float)(head.Y - wy - 12 * U + Math.Sin(ang) * 5 * U);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(al, p.Col)))
                                g.DrawString("★", symbolFont, b, sx - 8 * (float)Dpi, sy - 10 * (float)Dpi);
                            break;
                        }
                    case 8:
                        DrawCrack(g, x, y, (int)p.VX, (float)p.Size, (int)(230 * Math.Min(1, a * 2)), p.Phase);
                        break;
                }
            }
        }

        // Cracks in the edge of the screen where he smashed into it.
        static void DrawCrack(Graphics g, float x, float y, int side, float size, int alpha, double seed)
        {
            Random r = new Random((int)(seed * 1000));
            using (Pen p = new Pen(Color.FromArgb(alpha, 30, 30, 35), Math.Max(1.2f, size / 30)))
            using (Pen w = new Pen(Color.FromArgb(alpha / 2, 255, 255, 255), Math.Max(1.2f, size / 30) + 1.5f))
            {
                p.LineJoin = LineJoin.Round; w.LineJoin = LineJoin.Round;
                double baseAng = side < 0 ? 0 : side > 0 ? Math.PI : Math.PI / 2;
                for (int i = 0; i < 8; i++)
                {
                    double ang = baseAng + (i / 7.0 - 0.5) * Math.PI * 0.95;
                    List<PointF> pts = new List<PointF>();
                    float cx = x, cy = y;
                    pts.Add(new PointF(cx, cy));
                    double len = size * (0.5 + r.NextDouble() * 0.7);
                    int segs = 4;
                    for (int k = 0; k < segs; k++)
                    {
                        double a2 = ang + (r.NextDouble() - 0.5) * 0.7;
                        cx += (float)(Math.Cos(a2) * len / segs); cy += (float)(Math.Sin(a2) * len / segs);
                        pts.Add(new PointF(cx, cy));
                    }
                    g.DrawLines(w, pts.ToArray());
                    g.DrawLines(p, pts.ToArray());
                }
                for (int ring = 1; ring <= 2; ring++)
                {
                    float rr = size * 0.22f * ring;
                    float start = side < 0 ? -90 : side > 0 ? 90 : 0;
                    g.DrawArc(p, x - rr, y - rr, rr * 2, rr * 2, start, 180);
                }
            }
        }

        // ================= mouse grabbing (the playful kind) =================
        void UpdateGrab(double now)
        {
            if (!Grabbing) return;
            bool esc = (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0;
            if (esc || now > GrabEnd + 2.5)
            {
                Grabbing = false;
                if (esc) Brain.OnEscapeGrab();
                return;
            }
            PointF h = HandWorld();
            Native.SetCursorPos((int)h.X, (int)h.Y);
        }

        // ================= menu / tray =================
        void BuildMenu()
        {
            menu = new ContextMenuStrip();
            menu.Items.Add("Talk to me  (Ctrl+Shift+Space)", null, delegate { ShowInput(); });
            menu.Items.Add("Listen now  (Ctrl+Shift+L)", null, delegate { Voice.ListenNow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Game Center (all games)", null, delegate { OpenGameCenter(); });
            ToolStripMenuItem games = new ToolStripMenuItem("Play a game");
            games.DropDownItems.Add("Balloon Pop (throw me!)", null, delegate { StartBalloonPop(); });
            games.DropDownItems.Add("Dodgeball (dodge with your mouse)", null, delegate { StartDodgeball(); });
            games.DropDownItems.Add("Whack-a-Sticky", null, delegate { StartWhack(); });
            games.DropDownItems.Add("Hoops (basketball shootout)", null, delegate { StartHoops(); });
            games.DropDownItems.Add("Dance Battle", null, delegate { StartDanceBattle(); });
            games.DropDownItems.Add("Guess My Drawing", null, delegate { StartGuessDrawing(); });
            games.DropDownItems.Add(new ToolStripSeparator());
            games.DropDownItems.Add("Tag - you're it! (click me 5 times)", null, delegate { StartTag(true); });
            games.DropDownItems.Add("Tag - I'm it! (run away from me)", null, delegate { StartTag(false); });
            games.DropDownItems.Add("Hide and seek (in File Explorer)", null, delegate { StartHideAndSeek(); });
            games.DropDownItems.Add("Tic Tac Toe", null, delegate { StartTicTacToe(); });
            games.DropDownItems.Add("Snake (I play!)", null, delegate { StartSnake(); });
            games.DropDownItems.Add("Rock Paper Scissors", null, delegate { Brain.StartRps(); });
            menu.Items.Add(games);
            ToolStripMenuItem home = new ToolStripMenuItem("At home");
            home.DropDownItems.Add("Go home", null, delegate { GoHomeActivity(null); });
            home.DropDownItems.Add("Draw a picture", null, delegate { GoHomeActivity("draw"); });
            home.DropDownItems.Add("Play basketball", null, delegate { GoHomeActivity("basketball"); });
            home.DropDownItems.Add("Exercise", null, delegate { GoHomeActivity("exercise"); });
            home.DropDownItems.Add("Watch TV", null, delegate { GoHomeActivity("tv"); });
            home.DropDownItems.Add("Take a nap", null, delegate { GoHomeActivity("sleep"); });
            home.DropDownItems.Add("Tidy up my things", null, delegate { GoHomeActivity("tidy"); });
            home.DropDownItems.Add(new ToolStripSeparator());
            home.DropDownItems.Add("Show / hide my house", null, delegate { ToggleHouse(); });
            menu.Items.Add(home);
            menu.Items.Add("Obby Lab (build obbies, teach me!)", null, delegate { OpenLab(); });
            menu.Items.Add("Play the game I'm in (Esc to stop)", null, delegate { StartGamePilot("", null); });
            menu.Items.Add("Dance!", null, delegate { Dance(); });
            menu.Items.Add("Come here", null, delegate { ComeHere(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings / customise...", null, delegate { OpenSettings(); });
            menu.Items.Add("What can I say?", null, delegate { ShowHelp(); });
            menu.Items.Add("Hide / show", null, delegate { ToggleHidden(); });
            menu.Items.Add("Exit", null, delegate { ExitApp(); });

            if (TestMode) return;
            tray = new NotifyIcon();
            tray.Icon = Renderer.MakeIcon(S.BodyColor);
            tray.Text = "Stick Buddy";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowInput(); };
        }

        public void ShowMenu(Point p)
        {
            Native.SetForegroundWindow(Handle);
            menu.Show(p);
        }

        public void ToggleHidden()
        {
            if (Visible) { CancelJob(); Hide(); }
            else { Show(); Touch(); }
        }

        public void ToggleHouse()
        {
            if (House != null && House.Shown)
            {
                if (Sup.IsHouse) Fall();
                House.HideHouse();
                if (Things != null) Things.HideAll();
                S.ShowHouse = false;
                Say("Okay, I packed my house away.");
            }
            else
            {
                if (House == null) House = new House(this);
                House.ShowHouse();
                if (Things != null) Things.CreateAll();
                S.ShowHouse = true;
                Say("Ta-da! My house is back!");
            }
            S.Save();
            World.Refresh(this, Now, true);
        }

        public void ShowInput()
        {
            Touch();
            if (Sleeping) { Sleeping = false; StopSitting(); }
            if (input == null || input.IsDisposed) input = new InputBox(this);
            input.Text = "Talk to " + S.Name;
            input.OpenNear(new Point((int)X, (int)(Y - 150 * U)));
        }

        SettingsForm settingsForm;
        public void OpenSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(this);
            settingsForm.Show();
            settingsForm.Activate();
        }

        HelpForm helpForm;
        public void ShowHelp()
        {
            if (helpForm == null || helpForm.IsDisposed) helpForm = new HelpForm(this);
            helpForm.Show();
            helpForm.Activate();
        }

        public void ApplySettings(bool restartEars)
        {
            ComputeSize();
            S.Save();
            Voice.ApplyVoice();
            if (restartEars) Voice.StartRecognition();
            try { tray.Icon = Renderer.MakeIcon(S.BodyColor); } catch { }
            if (House != null) House.Rescale();
            if (Things != null) Things.Rescale();
        }

        public void ExitApp()
        {
            if (Quitting) return;
            Quitting = true;
            CancelJob();
            RunJob(async delegate (CancellationToken ct)
            {
                BaseAct = Act.Wave;
                Task t = Say("Bye bye!");
                await Task.WhenAny(t, Task.Delay(2500));
                Close();
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                timer.Stop();
                StopCursorTricks(false);
                Native.UnregisterHotKey(Handle, 1);
                Native.UnregisterHotKey(Handle, 2);
                tray.Visible = false;
                tray.Dispose();
                Voice.Dispose();
                StopWatchers();
                if (House != null) House.Close();
                if (Things != null) Things.CloseAll();
                if (Overlay != null) Overlay.Close();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
