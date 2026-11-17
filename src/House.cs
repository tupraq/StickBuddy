using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace StickBuddy
{
    // His little two-floor house, drawn like a cut-open doll's house, sitting on the bottom of the screen.
    // House units: x from the left outside wall (0..360), y UP from the ground (0..300).
    public class House : Form
    {
        readonly Buddy b;
        public double HX, GroundY, Hu = 1;
        public const double OuterW = 360, Wall = 7, Slab1 = 130, UpperFloor = 137, Ceil2 = 262, RoofFloor = 269;
        public const double StairX0 = 20, StairX1 = 160, DoorTop = 100;
        public bool Shown;
        public bool TvOn;
        public Doodle Easel;
        public double EaselProgress;
        double door, nextRender, nextTv;
        bool dirty = true;
        Bitmap canvas;
        int W, H;

        // dragging the house around
        bool movingHouse;
        int moveStartX;
        double moveStartHX;

        public House(Buddy b)
        {
            this.b = b;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Stick Buddy house";
            Location = new Point(-10000, -10000);
            Size = new Size(10, 10);
            Rescale();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            HX = b.S.HouseX >= 0 ? b.S.HouseX : wa.Left + 30 * b.Dpi;
            ClampPosition();
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

        public void Rescale()
        {
            Hu = Math.Max(0.8 * b.Dpi, Math.Min(1.6 * b.Dpi, b.U));
            W = (int)(422 * Hu); H = (int)(318 * Hu);
            if (canvas != null) canvas.Dispose();
            canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            dirty = true;
            ClampPosition();
        }

        void ClampPosition()
        {
            Screen sc = Screen.FromPoint(new Point((int)HX + 10, (int)(Screen.PrimaryScreen.WorkingArea.Bottom - 10)));
            Rectangle wa = sc.WorkingArea;
            HX = Math.Max(wa.Left + 2, Math.Min(wa.Right - OuterW * Hu - 50 * Hu, HX));
            GroundY = wa.Bottom;
        }

        public void ShowHouse()
        {
            Shown = true;
            if (!Visible) Show();
            dirty = true;
            Render();
        }

        public void HideHouse()
        {
            Shown = false;
            Hide();
        }

        // ---------- geometry (screen pixels) ----------
        public double Sx(double x) { return HX + x * Hu; }
        public double Sy(double y) { return GroundY - y * Hu; }
        public double Hx(double sx) { return (sx - HX) / Hu; }
        public double UpperY { get { return Sy(UpperFloor); } }
        public double RoofY { get { return Sy(RoofFloor); } }
        public double DoorX { get { return Sx(OuterW); } }

        public List<Plat> Platforms()
        {
            List<Plat> l = new List<Plat>();
            l.Add(new Plat(Sx(StairX1 + 2), Sx(OuterW - Wall - 1), UpperY, Plat.Upper, IntPtr.Zero));
            l.Add(new Plat(Sx(0), Sx(OuterW), RoofY, Plat.Roof, IntPtr.Zero));
            return l;
        }

        public bool OnLevel(int kind, double x)
        {
            if (kind == Plat.Upper) return x >= Sx(StairX1 - 2) && x <= Sx(OuterW - Wall + 2);
            if (kind == Plat.Roof) return x >= Sx(-3) && x <= Sx(OuterW + 3);
            return true;
        }

        public bool InsideX(double x) { return x >= Sx(0) && x <= Sx(OuterW); }

        public Rectangle StructureRect()
        {
            return Rectangle.FromLTRB((int)Sx(0), (int)Sy(276), (int)Sx(OuterW), (int)GroundY);
        }

        // ---------- updating ----------
        public void Tick(double now)
        {
            if (!Shown) return;
            double oldGround = GroundY;
            if (now > nextTv) { ClampPosition(); if (GroundY != oldGround) dirty = true; }
            // the door opens when he comes near it
            bool near = !b.RagMode && b.OnGround && b.Sup.Kind == Plat.Floor && Math.Abs(b.X - DoorX) < 50 * Hu;
            double target = near ? 1 : 0;
            if (Math.Abs(door - target) > 0.001)
            {
                door += Math.Sign(target - door) * Math.Min(Math.Abs(target - door), 0.016 / 0.18);
                dirty = true;
            }
            Thing tv = b.Things != null ? b.Things.Get(ThingKind.TV) : null;
            bool tvLive = TvOn && tv != null && tv.State == ThingState.Home;
            if (tvLive && now > nextTv) { dirty = true; }
            if (now > nextTv) nextTv = now + 0.07;
            if (movingHouse)
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    movingHouse = false;
                    b.S.HouseX = HX; b.S.Save();
                }
                else
                {
                    double old = HX;
                    HX = moveStartHX + (Cursor.Position.X - moveStartX);
                    ClampPosition();
                    if (HX != old) { dirty = true; World.Refresh(b, now, true); }
                }
            }
            if (dirty && now >= nextRender) { nextRender = now + 0.03; Render(); }
        }

        public void Invalidate2() { dirty = true; }

        // ---------- mouse: grab things in the house, or drag the house itself ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point c = Cursor.Position;
            if (e.Button == MouseButtons.Right) { b.ShowMenu(c); return; }
            if (e.Button != MouseButtons.Left) return;
            double hx = Hx(c.X), hy = (GroundY - c.Y) / Hu;
            if (b.Things != null)
            {
                Thing t = b.Things.HitHome(hx, hy);
                if (t != null) { b.Things.BeginDragFromHome(t, c); return; }
            }
            movingHouse = true;
            moveStartX = c.X;
            moveStartHX = HX;
        }

        // ---------- drawing ----------
        float PX(double x) { return (float)((x + 10) * Hu); }
        float PY(double y) { return (float)((310 - y) * Hu); }
        PointF Pt(double x, double y) { return new PointF(PX(x), PY(y)); }
        RectangleF Rc(double x0, double y0, double x1, double y1) { return RectangleF.FromLTRB(PX(x0), PY(y1), PX(x1), PY(y0)); }

        public void Render()
        {
            if (!Shown) return;
            DrawCanvas();
            Native.UpdateLayered(Handle, canvas, (int)Math.Round(Sx(-10)), (int)Math.Round(Sy(310)));
        }

        void DrawCanvas()
        {
            dirty = false;
            float u = (float)Hu;
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color ink = Color.FromArgb(255, 22, 22, 26);
                using (SolidBrush paper = new SolidBrush(Color.FromArgb(246, 255, 255, 255)))
                using (SolidBrush inkB = new SolidBrush(ink))
                using (Pen thick = new Pen(ink, 3.2f * u))
                using (Pen thin = new Pen(ink, 1.8f * u))
                using (Pen hair = new Pen(Color.FromArgb(160, 22, 22, 26), 1.1f * u))
                {
                    thick.LineJoin = LineJoin.Round; thin.LineJoin = LineJoin.Round;
                    thin.StartCap = LineCap.Round; thin.EndCap = LineCap.Round;

                    // inside walls
                    g.FillRectangle(paper, Rc(0, 0, OuterW, RoofFloor));
                    // floors and roof slabs
                    g.FillRectangle(inkB, Rc(0, Ceil2, OuterW, RoofFloor));
                    g.FillRectangle(inkB, Rc(Wall, Slab1, StairX0, UpperFloor));
                    g.FillRectangle(inkB, Rc(StairX1, Slab1, OuterW, UpperFloor));
                    // outside walls (the right one has the door hole)
                    g.FillRectangle(inkB, Rc(0, 0, Wall, RoofFloor));
                    g.FillRectangle(inkB, Rc(OuterW - Wall, DoorTop, OuterW, RoofFloor));
                    // little roof edges + chimney
                    g.FillRectangle(inkB, Rc(-4, RoofFloor - 1, 14, RoofFloor + 7));
                    g.FillRectangle(inkB, Rc(OuterW - 14, RoofFloor - 1, OuterW + 4, RoofFloor + 7));
                    g.FillRectangle(paper, Rc(60, RoofFloor, 84, 300));
                    g.DrawRectangle(thin, PX(60), PY(300), 24 * u, 31 * u);
                    g.FillRectangle(inkB, Rc(57, 296, 87, 301));
                    // ground line
                    g.DrawLine(thick, Pt(-8, 0), Pt(OuterW + 52, 0));

                    // ---- ground floor ----
                    // stairs going up to the right
                    int steps = 9;
                    List<PointF> st = new List<PointF>();
                    st.Add(Pt(StairX0, 0));
                    for (int i = 0; i < steps; i++)
                    {
                        double x = StairX0 + (StairX1 - StairX0) * i / steps, y1 = UpperFloor * (i + 1) / steps;
                        double x2 = StairX0 + (StairX1 - StairX0) * (i + 1) / steps;
                        st.Add(Pt(x, y1)); st.Add(Pt(x2, y1));
                    }
                    st.Add(Pt(StairX1, UpperFloor - 16));
                    st.Add(Pt(StairX0 + 16, 0));
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(255, 232, 232, 236))) g.FillPolygon(sb, st.ToArray());
                    g.DrawPolygon(thin, st.ToArray());

                    // ceiling lamp
                    g.DrawLine(hair, Pt(300, Slab1), Pt(300, 112));
                    using (SolidBrush lb = new SolidBrush(Color.FromArgb(255, 255, 214, 90)))
                    {
                        PointF[] shade = { Pt(292, 112), Pt(308, 112), Pt(313, 104), Pt(287, 104) };
                        g.FillPolygon(lb, shade); g.DrawPolygon(thin, shade);
                    }
                    // clock
                    g.FillEllipse(Brushes.White, PX(192) - 9 * u, PY(100) - 9 * u, 18 * u, 18 * u);
                    g.DrawEllipse(thin, PX(192) - 9 * u, PY(100) - 9 * u, 18 * u, 18 * u);
                    double hr = DateTime.Now.Hour % 12 + DateTime.Now.Minute / 60.0, mn = DateTime.Now.Minute;
                    g.DrawLine(thin, Pt(192, 100), Pt(192 + Math.Sin(hr / 12 * 2 * Math.PI) * 5, 100 + Math.Cos(hr / 12 * 2 * Math.PI) * 5));
                    g.DrawLine(hair, Pt(192, 100), Pt(192 + Math.Sin(mn / 60 * 2 * Math.PI) * 7.5, 100 + Math.Cos(mn / 60 * 2 * Math.PI) * 7.5));
                    // TV table
                    g.FillRectangle(inkB, Rc(170, 31, 220, 34));
                    g.DrawLine(thin, Pt(174, 31), Pt(174, 0)); g.DrawLine(thin, Pt(216, 31), Pt(216, 0));
                    g.DrawLine(hair, Pt(174, 14), Pt(216, 14));
                    // rug
                    using (Pen rug = new Pen(Color.FromArgb(255, 220, 70, 70), 3f * u)) g.DrawLine(rug, Pt(236, 1.5), Pt(342, 1.5));
                    // couch, facing the TV
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(255, 110, 150, 210)))
                    using (SolidBrush cd = new SolidBrush(Color.FromArgb(255, 80, 115, 175)))
                    {
                        RectangleF seat = Rc(248, 8, 342, 24);
                        RectangleF back = Rc(326, 8, 342, 54);
                        RectangleF arm = Rc(244, 8, 258, 34);
                        using (GraphicsPath p1 = Renderer.RoundRect(back, 4 * u)) { g.FillPath(cd, p1); g.DrawPath(thin, p1); }
                        using (GraphicsPath p2 = Renderer.RoundRect(seat, 4 * u)) { g.FillPath(cb, p2); g.DrawPath(thin, p2); }
                        using (GraphicsPath p3 = Renderer.RoundRect(arm, 4 * u)) { g.FillPath(cd, p3); g.DrawPath(thin, p3); }
                        g.DrawLine(hair, Pt(290, 24), Pt(290, 10));
                        g.DrawLine(thin, Pt(252, 8), Pt(252, 0)); g.DrawLine(thin, Pt(338, 8), Pt(338, 0));
                    }

                    // ---- upper floor ----
                    // easel with the picture he is drawing
                    g.DrawLine(thin, Pt(172, UpperFloor), Pt(184, UpperFloor + 76));
                    g.DrawLine(thin, Pt(204, UpperFloor), Pt(192, UpperFloor + 76));
                    g.DrawLine(thin, Pt(196, UpperFloor), Pt(188, UpperFloor + 60));
                    RectangleF canvasR = Rc(166, UpperFloor + 36, 210, UpperFloor + 72);
                    g.FillRectangle(Brushes.White, canvasR);
                    if (Easel != null) Easel.Draw(g, canvasR, EaselProgress);
                    g.DrawRectangle(thin, canvasR.X, canvasR.Y, canvasR.Width, canvasR.Height);
                    g.FillRectangle(inkB, Rc(164, UpperFloor + 33, 212, UpperFloor + 36));
                    // bed
                    using (SolidBrush blanket = new SolidBrush(Color.FromArgb(255, 90, 170, 120)))
                    using (SolidBrush wood = new SolidBrush(Color.FromArgb(255, 150, 100, 60)))
                    {
                        g.FillRectangle(wood, Rc(342, UpperFloor, 350, UpperFloor + 50));
                        g.DrawRectangle(thin, PX(342), PY(UpperFloor + 50), 8 * u, 50 * u);
                        g.FillRectangle(wood, Rc(240, UpperFloor, 246, UpperFloor + 30));
                        g.DrawRectangle(thin, PX(240), PY(UpperFloor + 30), 6 * u, 30 * u);
                        RectangleF mat = Rc(246, UpperFloor + 8, 342, UpperFloor + 20);
                        g.FillRectangle(Brushes.White, mat); g.DrawRectangle(thin, mat.X, mat.Y, mat.Width, mat.Height);
                        RectangleF bl = Rc(246, UpperFloor + 11, 318, UpperFloor + 21);
                        using (GraphicsPath p4 = Renderer.RoundRect(bl, 3 * u)) { g.FillPath(blanket, p4); g.DrawPath(thin, p4); }
                        RectangleF pil = Rc(322, UpperFloor + 20, 340, UpperFloor + 27);
                        using (GraphicsPath p5 = Renderer.RoundRect(pil, 3 * u)) { g.FillPath(Brushes.White, p5); g.DrawPath(thin, p5); }
                    }
                    // window on the back wall
                    RectangleF win = Rc(300, UpperFloor + 65, 338, UpperFloor + 103);
                    using (SolidBrush sky = new SolidBrush(Color.FromArgb(255, 200, 228, 255))) g.FillRectangle(sky, win);
                    g.DrawRectangle(thin, win.X, win.Y, win.Width, win.Height);
                    g.DrawLine(hair, Pt(319, UpperFloor + 65), Pt(319, UpperFloor + 103));
                    g.DrawLine(hair, Pt(300, UpperFloor + 84), Pt(338, UpperFloor + 84));

                    // ---- outside: basketball hoop on the right wall ----
                    RectangleF board = Rc(OuterW, 150, OuterW + 6, 188);
                    g.FillRectangle(Brushes.White, board); g.DrawRectangle(thin, board.X, board.Y, board.Width, board.Height);
                    using (Pen rim = new Pen(Color.FromArgb(255, 235, 90, 30), 2.6f * u))
                    {
                        g.DrawLine(rim, Pt(OuterW + 6, 160), Pt(OuterW + 34, 160));
                    }
                    for (int i = 0; i <= 4; i++)
                        g.DrawLine(hair, Pt(OuterW + 7 + i * 6.5, 160), Pt(OuterW + 12 + i * 4, 140));
                    g.DrawLine(hair, Pt(OuterW + 9, 150), Pt(OuterW + 31, 150));

                    // ---- the door ----
                    double dw = 44 * door;
                    if (door < 0.05)
                    {
                        using (SolidBrush db = new SolidBrush(Color.FromArgb(255, 170, 110, 60))) g.FillRectangle(db, Rc(OuterW - Wall, 0, OuterW, DoorTop));
                        g.DrawRectangle(thin, PX(OuterW - Wall), PY(DoorTop), (float)Wall * u, (float)DoorTop * u);
                    }
                    else
                    {
                        PointF[] dp = { Pt(OuterW, 0), Pt(OuterW + dw, 4 * door), Pt(OuterW + dw, DoorTop - 4 * door), Pt(OuterW, DoorTop) };
                        using (SolidBrush db = new SolidBrush(Color.FromArgb(255, 170, 110, 60))) g.FillPolygon(db, dp);
                        g.DrawPolygon(thin, dp);
                        g.FillEllipse(inkB, PX(OuterW + dw * 0.8) - 2 * u, PY(50) - 2 * u, 4 * u, 4 * u);
                    }

                    // ---- his things that are at home ----
                    if (b.Things != null)
                        foreach (Thing t in b.Things.All)
                            if (t.State == ThingState.Home)
                            {
                                GraphicsState gs = g.Save();
                                g.TranslateTransform(PX(t.HomeX), PY(t.HomeY));
                                t.Draw(g, u, b.Now, TvOn);
                                g.Restore(gs);
                            }
                }
                // a faint hit area so you can grab the inside
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(3, 128, 128, 128))) g.FillRectangle(hb, Rc(0, 0, OuterW, RoofFloor));
            }
        }

        // A picture of the house (for the test render).
        public Bitmap Snapshot() { DrawCanvas(); return (Bitmap)canvas.Clone(); }

        // Where a house point ends up in the snapshot picture.
        public PointF SnapPoint(double x, double y) { return new PointF(PX(x), PY(y)); }
    }

    // A random kid-style drawing he makes on his easel when he's bored.
    public class Doodle
    {
        public class Stroke { public Color C; public float W; public List<PointF> P = new List<PointF>(); public bool Fill; }
        public readonly List<Stroke> Strokes = new List<Stroke>();
        public string Name = "a picture";
        Random r;

        static readonly Color Black = Color.FromArgb(40, 40, 45), Green = Color.FromArgb(60, 170, 70), Yellow = Color.FromArgb(250, 200, 30),
            Blue = Color.FromArgb(60, 130, 230), Red = Color.FromArgb(230, 50, 50), Brown = Color.FromArgb(140, 90, 50),
            Orange = Color.FromArgb(245, 140, 40), Pink = Color.FromArgb(240, 110, 170), Purple = Color.FromArgb(140, 80, 200),
            Gray = Color.FromArgb(130, 130, 140), Sky = Color.FromArgb(120, 190, 240);

        public static Doodle Make(Random r)
        {
            Doodle d = new Doodle();
            d.r = r;
            switch (r.Next(11))
            {
                case 0: d.Name = "a sunny day"; d.Ground(); d.Sun(80, 18, 9); d.Tree(28, 70); d.Flower(58, 70); break;
                case 1: d.Name = "my house"; d.Ground(); d.HouseShape(45, 70); d.Sun(84, 15, 8); d.Cloud(20, 16); break;
                case 2: d.Name = "a cat"; d.Cat(50, 42); d.Heart(84, 18, 7); break;
                case 3: d.Name = "a fish"; d.Water(); d.Fish(48, 40); d.Bubble(70, 22); d.Bubble(76, 12); break;
                case 4: d.Name = "a rocket in space"; d.Rocket(50, 40); d.Star(18, 16, 5); d.Star(82, 60, 4); d.Star(80, 14, 3); break;
                case 5: d.Name = "a rainbow"; d.Rainbow(); d.Cloud(20, 58); d.Cloud(80, 58); break;
                case 6: d.Name = "me!"; d.Ground(); d.Stick(50, 70); d.Sun(82, 16, 8); d.Heart(22, 24, 6); break;
                case 7: d.Name = "mountains"; d.Mountains(); d.Sun(78, 16, 8); d.Cloud(26, 14); break;
                case 8: d.Name = "a boat"; d.Water(); d.Boat(48, 58); d.Sun(82, 15, 8); break;
                case 9: d.Name = "a car"; d.Ground(); d.Car(50, 68); d.Tree(86, 70); break;
                default: d.Name = "flowers"; d.Ground(); d.Flower(22, 70); d.Flower(50, 70); d.Flower(78, 70); d.Sun(50, 14, 7); break;
            }
            return d;
        }

        // ---- for the guessing game: one clear thing per drawing, plus the words that count as right ----
        public string[] Keywords = new string[0];
        public string Hint = "";
        static readonly string[] GuessKinds = { "cat", "fish", "rocket", "rainbow", "house", "car", "boat", "flower", "me", "snowman", "apple", "ice cream", "pizza", "umbrella", "butterfly", "ghost", "star", "heart", "tree", "sun" };

        public static Doodle MakeGuess(Random r, List<string> used)
        {
            List<string> left = new List<string>();
            foreach (string k in GuessKinds) if (!used.Contains(k)) left.Add(k);
            if (left.Count == 0) { used.Clear(); left.AddRange(GuessKinds); }
            string kind = left[r.Next(left.Count)];
            used.Add(kind);
            Doodle d = new Doodle();
            d.r = r;
            d.Name = "a " + kind;
            switch (kind)
            {
                case "cat": d.Cat(50, 44); d.Keywords = new[] { "cat", "kitty", "kitten" }; d.Hint = "It says meow!"; break;
                case "fish": d.Fish(46, 40); d.Bubble(72, 24); d.Keywords = new[] { "fish", "goldfish" }; d.Hint = "It swims and has fins."; break;
                case "rocket": d.Rocket(50, 38); d.Keywords = new[] { "rocket", "spaceship", "space ship", "spacecraft" }; d.Hint = "It flies to space!"; break;
                case "rainbow": d.Rainbow(); d.Name = "a rainbow"; d.Keywords = new[] { "rainbow" }; d.Hint = "It has lots of colors and comes after the rain."; break;
                case "house": d.HouseShape(50, 72); d.Keywords = new[] { "house", "home" }; d.Hint = "People live in it. Like me!"; break;
                case "car": d.Car(50, 62); d.Keywords = new[] { "car", "auto" }; d.Hint = "It has four wheels and drives on roads."; break;
                case "boat": d.Water(); d.Boat(50, 58); d.Keywords = new[] { "boat", "ship", "sailboat", "sail boat" }; d.Hint = "It floats on the water."; break;
                case "flower": d.BigFlower(); d.Keywords = new[] { "flower", "flowers", "daisy" }; d.Hint = "It grows in the garden and smells nice."; break;
                case "me": d.Stick(50, 74); d.Name = "me! A stick figure"; d.Keywords = new[] { "stick figure", "stickman", "stick man", "stick", "you", "sticky", "yourself", "person", "man" }; d.Hint = "It looks a LOT like someone you know. Hehe!"; break;
                case "snowman": d.Snowman(); d.Keywords = new[] { "snowman", "snow man", "frosty" }; d.Hint = "It's made of snow and melts in the sun."; break;
                case "apple": d.Apple(); d.Name = "an apple"; d.Keywords = new[] { "apple" }; d.Hint = "A crunchy red fruit."; break;
                case "ice cream": d.IceCream(); d.Name = "an ice cream"; d.Keywords = new[] { "ice cream", "icecream", "ice-cream", "cone" }; d.Hint = "A cold sweet treat."; break;
                case "pizza": d.Pizza(); d.Name = "a pizza"; d.Keywords = new[] { "pizza" }; d.Hint = "It's cheesy and you eat it in slices."; break;
                case "umbrella": d.Umbrella(); d.Name = "an umbrella"; d.Keywords = new[] { "umbrella" }; d.Hint = "You open it when it rains."; break;
                case "butterfly": d.Butterfly(); d.Keywords = new[] { "butterfly" }; d.Hint = "It used to be a caterpillar!"; break;
                case "ghost": d.Ghost(); d.Keywords = new[] { "ghost", "spirit" }; d.Hint = "Boooo!"; break;
                case "star": d.Star(50, 40, 30); d.Keywords = new[] { "star" }; d.Hint = "It twinkles in the night sky."; break;
                case "heart": d.Heart(50, 44, 30); d.Keywords = new[] { "heart", "love" }; d.Hint = "It means love."; break;
                case "tree": d.BigTree(); d.Keywords = new[] { "tree" }; d.Hint = "It has a trunk and leaves."; break;
                default: d.Sun(50, 40, 20); d.Name = "the sun"; d.Keywords = new[] { "sun" }; d.Hint = "It's hot and bright and up in the sky."; break;
            }
            return d;
        }

        public bool IsRight(string guess)
        {
            string g = " " + System.Text.RegularExpressions.Regex.Replace((guess ?? "").ToLowerInvariant(), "[^a-z ]", " ") + " ";
            foreach (string k in Keywords)
            {
                if (g.Contains(" " + k + " ") || g.Contains(" " + k + "s ") || g.Contains(" " + k.Replace(" ", "") + " ")) return true;
            }
            return false;
        }

        // Where the pencil is when the drawing is this far along (0..100 x 0..80 units).
        public PointF Tip(double progress)
        {
            double total = TotalPoints, budget = total * Math.Max(0, Math.Min(1, progress));
            foreach (Stroke s in Strokes)
            {
                if (budget <= s.P.Count) return s.P[Math.Max(0, Math.Min(s.P.Count - 1, (int)budget))];
                budget -= s.P.Count;
            }
            Stroke last = Strokes[Strokes.Count - 1];
            return last.P[last.P.Count - 1];
        }

        void BigFlower()
        {
            Line(Green, 2.4f, 50, 78, 50, 40); Line(Green, 2f, 50, 62, 60, 54); Poly(Green, true, 60, 54, 68, 50, 62, 58);
            for (int i = 0; i < 6; i++) { double a = i * Math.PI * 2 / 6; Circle(Pink, 1.8f, 50 + Math.Cos(a) * 9, 30 + Math.Sin(a) * 9, 7, 7, true); }
            Circle(Yellow, 1.8f, 50, 30, 6, 6, true);
        }
        void Snowman()
        {
            Color c = Color.FromArgb(110, 140, 190);
            Circle(c, 2f, 50, 62, 15, 14, false); Circle(c, 2f, 50, 38, 11, 10, false); Circle(c, 2f, 50, 20, 8, 8, false);
            Poly(Black, true, 41, 13, 59, 13, 56, 4, 44, 4);
            Poly(Orange, true, 50, 20, 60, 22, 50, 23);
            Circle(Black, 1f, 47, 18, 1.1, 1.1, true); Circle(Black, 1f, 53, 18, 1.1, 1.1, true);
            Line(Brown, 1.8f, 39, 36, 26, 28); Line(Brown, 1.8f, 61, 36, 74, 28);
        }
        void Apple()
        {
            Circle(Red, 2f, 50, 46, 20, 19, true);
            Line(Brown, 2.4f, 50, 28, 52, 17);
            Poly(Green, true, 53, 22, 64, 17, 58, 26);
        }
        void IceCream()
        {
            Poly(Orange, true, 36, 42, 64, 42, 50, 76);
            Line(Brown, 1.2f, 40, 48, 58, 62); Line(Brown, 1.2f, 60, 48, 44, 64);
            Circle(Pink, 2f, 50, 33, 15, 12, true);
            Circle(Red, 1.4f, 50, 19, 3, 3, true);
        }
        void Pizza()
        {
            Poly(Yellow, true, 22, 18, 78, 18, 50, 74);
            Line(Brown, 4f, 22, 18, 78, 18);
            Circle(Red, 1.4f, 42, 30, 4, 4, true); Circle(Red, 1.4f, 57, 32, 4, 4, true); Circle(Red, 1.4f, 50, 47, 4, 4, true);
        }
        void Umbrella()
        {
            List<double> xy = new List<double>();
            for (int i = 0; i <= 20; i++) { double a = Math.PI + i * Math.PI / 20; xy.Add(50 + Math.Cos(a) * 32); xy.Add(38 + Math.Sin(a) * 26); }
            Poly(Blue, true, xy.ToArray());
            Line(Black, 2f, 50, 38, 50, 70);
            Line(Black, 2f, 50, 70, 50, 74, 46, 76, 42, 73);
        }
        void Butterfly()
        {
            Circle(Purple, 2f, 38, 30, 12, 11, true); Circle(Purple, 2f, 62, 30, 12, 11, true);
            Circle(Pink, 2f, 40, 52, 9, 9, true); Circle(Pink, 2f, 60, 52, 9, 9, true);
            Line(Black, 3f, 50, 24, 50, 62);
            Line(Black, 1.2f, 50, 24, 44, 12); Line(Black, 1.2f, 50, 24, 56, 12);
        }
        void Ghost()
        {
            Color c = Color.FromArgb(120, 120, 150);
            Poly(c, false, 32, 70, 32, 34, 36, 22, 44, 15, 56, 15, 64, 22, 68, 34, 68, 70, 62, 64, 56, 70, 50, 64, 44, 70, 38, 64);
            Circle(Black, 1.2f, 43, 34, 3, 4, true); Circle(Black, 1.2f, 57, 34, 3, 4, true);
            Circle(Black, 1.2f, 50, 46, 4, 5, false);
        }
        void BigTree()
        {
            Poly(Brown, true, 46, 78, 54, 78, 53, 48, 47, 48);
            Circle(Green, 2.2f, 50, 32, 22, 19, true);
            Circle(Red, 1.2f, 42, 30, 2.4, 2.4, true); Circle(Red, 1.2f, 58, 36, 2.4, 2.4, true);
        }

        PointF J(double x, double y) { return new PointF((float)(x + (r.NextDouble() - 0.5) * 1.2), (float)(y + (r.NextDouble() - 0.5) * 1.2)); }
        Stroke Add(Color c, float w, bool fill) { Stroke s = new Stroke(); s.C = c; s.W = w; s.Fill = fill; Strokes.Add(s); return s; }
        void Line(Color c, float w, params double[] xy) { Stroke s = Add(c, w, false); for (int i = 0; i + 1 < xy.Length; i += 2) s.P.Add(J(xy[i], xy[i + 1])); }
        void Circle(Color c, float w, double cx, double cy, double rx, double ry, bool fill)
        {
            Stroke s = Add(c, w, fill);
            for (int i = 0; i <= 26; i++) { double a = i * Math.PI * 2 / 26; s.P.Add(J(cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry)); }
        }
        void Poly(Color c, bool fill, params double[] xy) { Stroke s = Add(c, 1.6f, fill); for (int i = 0; i + 1 < xy.Length; i += 2) s.P.Add(J(xy[i], xy[i + 1])); s.P.Add(s.P[0]); }

        void Ground() { Line(Green, 2.4f, 0, 72, 25, 70, 50, 73, 75, 70, 100, 72); }
        void Water() { for (int k = 0; k < 2; k++) Line(Blue, 2f, 0, 66 + k * 8, 12, 63 + k * 8, 25, 66 + k * 8, 38, 63 + k * 8, 50, 66 + k * 8, 62, 63 + k * 8, 75, 66 + k * 8, 88, 63 + k * 8, 100, 66 + k * 8); }
        void Sun(double x, double y, double rad)
        {
            Circle(Yellow, 2f, x, y, rad, rad, true);
            for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(Orange, 1.6f, x + Math.Cos(a) * (rad + 2), y + Math.Sin(a) * (rad + 2), x + Math.Cos(a) * (rad + 7), y + Math.Sin(a) * (rad + 7)); }
        }
        void Cloud(double x, double y) { Circle(Sky, 1.6f, x - 6, y, 7, 5, false); Circle(Sky, 1.6f, x + 3, y - 3, 8, 6, false); Circle(Sky, 1.6f, x + 10, y + 1, 6, 4.5, false); }
        void Tree(double x, double b) { Line(Brown, 3f, x, b, x, b - 22); Circle(Green, 2f, x, b - 30, 12, 11, true); }
        void Flower(double x, double b)
        {
            Line(Green, 1.8f, x, b, x, b - 18); Line(Green, 1.6f, x, b - 8, x + 5, b - 12);
            Color pc = new Color[] { Pink, Red, Purple, Orange }[r.Next(4)];
            for (int i = 0; i < 5; i++) { double a = i * Math.PI * 2 / 5; Circle(pc, 1.4f, x + Math.Cos(a) * 4, b - 22 + Math.Sin(a) * 4, 3, 3, true); }
            Circle(Yellow, 1.4f, x, b - 22, 2.4, 2.4, true);
        }
        void HouseShape(double x, double b)
        {
            Poly(Red, false, x - 16, b, x + 16, b, x + 16, b - 24, x - 16, b - 24);
            Poly(Brown, true, x - 20, b - 24, x, b - 40, x + 20, b - 24);
            Poly(Blue, false, x - 4, b, x + 4, b, x + 4, b - 12, x - 4, b - 12);
            Poly(Sky, true, x + 7, b - 19, x + 13, b - 19, x + 13, b - 13, x + 7, b - 13);
        }
        void Cat(double x, double y)
        {
            Circle(Orange, 2f, x, y, 18, 15, true);
            Poly(Orange, true, x - 15, y - 8, x - 12, y - 24, x - 4, y - 14);
            Poly(Orange, true, x + 15, y - 8, x + 12, y - 24, x + 4, y - 14);
            Circle(Black, 1.4f, x - 6, y - 3, 2, 2.4, true); Circle(Black, 1.4f, x + 6, y - 3, 2, 2.4, true);
            Line(Black, 1.2f, x - 3, y + 5, x, y + 7, x + 3, y + 5);
            Line(Black, 1f, x - 8, y + 3, x - 22, y); Line(Black, 1f, x - 8, y + 6, x - 22, y + 7);
            Line(Black, 1f, x + 8, y + 3, x + 22, y); Line(Black, 1f, x + 8, y + 6, x + 22, y + 7);
        }
        void Fish(double x, double y)
        {
            Circle(Orange, 2f, x, y, 16, 9, true);
            Poly(Orange, true, x + 14, y, x + 26, y - 8, x + 26, y + 8);
            Circle(Black, 1.2f, x - 8, y - 2, 1.6, 1.6, true);
        }
        void Bubble(double x, double y) { Circle(Sky, 1.2f, x, y, 2.6, 2.6, false); }
        void Rocket(double x, double y)
        {
            Poly(Gray, true, x - 7, y + 18, x + 7, y + 18, x + 7, y - 10, x - 7, y - 10);
            Poly(Red, true, x - 7, y - 10, x, y - 24, x + 7, y - 10);
            Poly(Red, true, x - 7, y + 18, x - 14, y + 26, x - 7, y + 8);
            Poly(Red, true, x + 7, y + 18, x + 14, y + 26, x + 7, y + 8);
            Circle(Sky, 1.4f, x, y, 4, 4, true);
            Poly(Orange, true, x - 5, y + 19, x, y + 32, x + 5, y + 19);
        }
        void Star(double x, double y, double rad)
        {
            List<double> xy = new List<double>();
            for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? rad : rad * 0.45; xy.Add(x + Math.Cos(a) * rr); xy.Add(y + Math.Sin(a) * rr); }
            Poly(Yellow, true, xy.ToArray());
        }
        void Heart(double x, double y, double s)
        {
            List<double> xy = new List<double>();
            for (int i = 0; i < 30; i++)
            {
                double t = i * Math.PI * 2 / 30;
                xy.Add(x + s * 0.1 * 16 * Math.Pow(Math.Sin(t), 3));
                xy.Add(y - s * 0.1 * (13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t)));
            }
            Poly(Red, true, xy.ToArray());
        }
        void Rainbow()
        {
            Color[] c = { Red, Orange, Yellow, Green, Blue, Purple };
            for (int k = 0; k < c.Length; k++)
            {
                Stroke s = Add(c[k], 2.6f, false);
                double rad = 40 - k * 3.2;
                for (int i = 0; i <= 24; i++) { double a = Math.PI + i * Math.PI / 24; s.P.Add(J(50 + Math.Cos(a) * rad, 66 + Math.Sin(a) * rad)); }
            }
        }
        void Stick(double x, double b)
        {
            Line(Red, 3f, x, b - 22, x, b - 42); Line(Red, 3f, x, b - 22, x - 8, b); Line(Red, 3f, x, b - 22, x + 8, b);
            Line(Red, 3f, x - 12, b - 40, x, b - 36, x + 12, b - 46);
            Circle(Red, 2f, x, b - 50, 6, 6, true);
        }
        void Mountains()
        {
            Poly(Gray, true, 0, 72, 25, 30, 50, 72);
            Poly(Gray, true, 35, 72, 65, 22, 95, 72);
            Poly(Color.White, true, 59, 31, 65, 22, 71, 31, 65, 34);
            Line(Green, 2.4f, 0, 72, 100, 72);
        }
        void Boat(double x, double b)
        {
            Poly(Brown, true, x - 22, b - 6, x + 22, b - 6, x + 15, b + 4, x - 15, b + 4);
            Line(Black, 1.6f, x, b - 6, x, b - 38);
            Poly(Color.FromArgb(250, 250, 250), true, x + 1, b - 37, x + 1, b - 10, x + 20, b - 10);
        }
        void Car(double x, double b)
        {
            Poly(Blue, true, x - 26, b - 8, x + 26, b - 8, x + 26, b - 20, x + 12, b - 20, x + 6, b - 30, x - 14, b - 30, x - 20, b - 20, x - 26, b - 20);
            Circle(Black, 2f, x - 14, b - 6, 6, 6, true); Circle(Black, 2f, x + 14, b - 6, 6, 6, true);
            Poly(Sky, true, x - 12, b - 21, x - 8, b - 28, x + 3, b - 28, x + 7, b - 21);
        }

        // progress 0..1: how much of the drawing is done
        public void Draw(Graphics g, RectangleF rect, double progress)
        {
            if (Strokes.Count == 0) return;
            double total = 0;
            foreach (Stroke s in Strokes) total += s.P.Count;
            double budget = total * Math.Max(0, Math.Min(1, progress));
            float sx = rect.Width / 100f, sy = rect.Height / 80f, sc = Math.Min(sx, sy);
            GraphicsState st = g.Save();
            g.SetClip(rect);
            foreach (Stroke s in Strokes)
            {
                if (budget <= 1) break;
                int n = (int)Math.Min(s.P.Count, budget);
                budget -= s.P.Count;
                PointF[] pts = new PointF[n];
                for (int i = 0; i < n; i++) pts[i] = new PointF(rect.X + s.P[i].X * sx, rect.Y + s.P[i].Y * sy);
                if (n < 2) continue;
                if (s.Fill && n == s.P.Count)
                    using (SolidBrush fb = new SolidBrush(Color.FromArgb(150, s.C))) g.FillPolygon(fb, pts);
                using (Pen p = Renderer.RoundPen(s.C, Math.Max(0.8f, s.W * sc * 0.55f))) g.DrawLines(p, pts);
            }
            g.Restore(st);
        }

        public int TotalPoints { get { int t = 0; foreach (Stroke s in Strokes) t += s.P.Count; return t; } }
    }
}
