using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace StickBuddy
{
    public enum ThingKind { Ball, TV, Lamp, Plant, Chair, Dumbbell, Frame, Teddy }
    public enum ThingState { Home, Held, Loose, Carried, Broken }

    // One of his things (TV, lamp, ball...). At home it is drawn by the house; when it is out of the
    // house it gets its own little window so it can fly around the screen.
    public class Thing
    {
        public ThingKind Kind;
        public ThingState State = ThingState.Home;
        public double HomeX, HomeY, W, Hh;        // house units (centre of the thing, y up)
        public double X, Y, VX, VY, Ang, AV;       // screen pixels
        public bool Breakable;
        public double BreakSpeed, Bounce = 0.25;
        public bool Resting;
        public Support RestOn;
        public Native.RECT RestRect;
        public double BrokenAt, HeldOffX, HeldOffY, LastHitBuddy = -10, Scale = 1;
        public ThingForm Win;
        public Doodle Art;
        public string Name;

        public double Diag { get { return Math.Sqrt(W * W + Hh * Hh); } }

        // How far the thing reaches down from its centre at its current angle.
        public double ExtentY(double s)
        {
            double c = Math.Abs(Math.Cos(Ang)), sn = Math.Abs(Math.Sin(Ang));
            if (Kind == ThingKind.Ball) return W / 2 * s;
            return (Hh / 2 * c + W / 2 * sn) * s;
        }

        public double ExtentX(double s)
        {
            double c = Math.Abs(Math.Cos(Ang)), sn = Math.Abs(Math.Sin(Ang));
            if (Kind == ThingKind.Ball) return W / 2 * s;
            return (W / 2 * c + Hh / 2 * sn) * s;
        }

        // Draws the thing centred on (0,0), u = pixels per house unit.
        public void Draw(Graphics g, float u, double now, bool tvOn)
        {
            Color ink = Color.FromArgb(255, 22, 22, 26);
            using (Pen thin = new Pen(ink, 1.8f * u))
            using (SolidBrush inkB = new SolidBrush(ink))
            {
                thin.LineJoin = LineJoin.Round; thin.StartCap = LineCap.Round; thin.EndCap = LineCap.Round;
                float w = (float)W * u, h = (float)Hh * u;
                switch (Kind)
                {
                    case ThingKind.Ball:
                        {
                            float r = w / 2;
                            using (SolidBrush ob = new SolidBrush(Color.FromArgb(255, 235, 125, 35))) g.FillEllipse(ob, -r, -r, w, w);
                            g.DrawEllipse(thin, -r, -r, w, w);
                            using (Pen s = new Pen(ink, 1.1f * u))
                            {
                                g.DrawLine(s, -r, 0, r, 0);
                                g.DrawArc(s, -r * 0.55f, -r, r * 1.1f, w, -70, 140);
                                g.DrawArc(s, -r * 0.55f, -r, r * 1.1f, w, 110, 140);
                            }
                            break;
                        }
                    case ThingKind.TV:
                        {
                            RectangleF body = new RectangleF(-w / 2, -h / 2 + 7 * u, w, h - 7 * u);
                            g.DrawLine(thin, 0, -h / 2 + 7 * u, -7 * u, -h / 2);
                            g.DrawLine(thin, 0, -h / 2 + 7 * u, 8 * u, -h / 2 + 1 * u);
                            using (GraphicsPath p = Renderer.RoundRect(body, 4 * u))
                            using (SolidBrush cb = new SolidBrush(Color.FromArgb(255, 70, 70, 80)))
                            { g.FillPath(cb, p); g.DrawPath(thin, p); }
                            RectangleF scr = new RectangleF(body.X + 4 * u, body.Y + 4 * u, body.Width - 8 * u, body.Height - 8 * u);
                            if (tvOn)
                            {
                                // a little cartoon: sky, ground and a bouncing ball
                                using (SolidBrush sky = new SolidBrush(Color.FromArgb(255, 120, 190, 250))) g.FillRectangle(sky, scr);
                                using (SolidBrush gr = new SolidBrush(Color.FromArgb(255, 90, 190, 90))) g.FillRectangle(gr, scr.X, scr.Bottom - scr.Height * 0.3f, scr.Width, scr.Height * 0.3f);
                                float bx = scr.X + (float)((Math.Sin(now * 1.7) * 0.5 + 0.5) * (scr.Width - 6 * u)) + 3 * u;
                                float by = scr.Bottom - scr.Height * 0.3f - (float)Math.Abs(Math.Sin(now * 5)) * scr.Height * 0.5f - 3 * u;
                                using (SolidBrush bb = new SolidBrush(Color.FromArgb(255, 240, 70, 70))) g.FillEllipse(bb, bx - 3 * u, by - 3 * u, 6 * u, 6 * u);
                                using (SolidBrush sun = new SolidBrush(Color.FromArgb(255, 255, 220, 60))) g.FillEllipse(sun, scr.Right - 8 * u, scr.Y + 2 * u, 5 * u, 5 * u);
                            }
                            else
                            {
                                using (LinearGradientBrush lg = new LinearGradientBrush(scr, Color.FromArgb(255, 40, 44, 52), Color.FromArgb(255, 90, 96, 110), 45f))
                                    g.FillRectangle(lg, scr);
                            }
                            g.DrawRectangle(thin, scr.X, scr.Y, scr.Width, scr.Height);
                            break;
                        }
                    case ThingKind.Lamp:
                        {
                            g.FillEllipse(inkB, -w / 2, h / 2 - 4 * u, w, 4 * u);
                            g.DrawLine(thin, 0, h / 2 - 2 * u, 0, -h / 2 + 12 * u);
                            PointF[] shade = { new PointF(-4 * u, -h / 2), new PointF(4 * u, -h / 2), new PointF(w / 2, -h / 2 + 13 * u), new PointF(-w / 2, -h / 2 + 13 * u) };
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(255, 255, 210, 80))) g.FillPolygon(sb, shade);
                            g.DrawPolygon(thin, shade);
                            break;
                        }
                    case ThingKind.Plant:
                        {
                            PointF[] pot = { new PointF(-w / 2 + 2 * u, h / 2 - 18 * u), new PointF(w / 2 - 2 * u, h / 2 - 18 * u), new PointF(w / 2 - 5 * u, h / 2), new PointF(-w / 2 + 5 * u, h / 2) };
                            using (SolidBrush leaf = new SolidBrush(Color.FromArgb(255, 70, 160, 70)))
                            {
                                g.FillEllipse(leaf, -w / 2, -h / 2 + 2 * u, w * 0.55f, 14 * u);
                                g.FillEllipse(leaf, -w * 0.05f, -h / 2, w * 0.55f, 16 * u);
                                g.FillEllipse(leaf, -w * 0.3f, -h / 2 + 8 * u, w * 0.6f, 14 * u);
                                using (Pen lp = new Pen(Color.FromArgb(255, 30, 90, 40), 1.2f * u))
                                {
                                    g.DrawEllipse(lp, -w / 2, -h / 2 + 2 * u, w * 0.55f, 14 * u);
                                    g.DrawEllipse(lp, -w * 0.05f, -h / 2, w * 0.55f, 16 * u);
                                }
                            }
                            using (SolidBrush pb = new SolidBrush(Color.FromArgb(255, 200, 100, 60))) g.FillPolygon(pb, pot);
                            g.DrawPolygon(thin, pot);
                            break;
                        }
                    case ThingKind.Chair:
                        {
                            using (Pen wood = new Pen(Color.FromArgb(255, 120, 75, 40), 3f * u))
                            {
                                wood.StartCap = LineCap.Round; wood.EndCap = LineCap.Round;
                                g.DrawLine(wood, -w / 2, 0, w / 2, 0);            // seat
                                g.DrawLine(wood, w / 2 - 1 * u, 0, w / 2 - 1 * u, -h / 2);  // back
                                g.DrawLine(wood, w / 2 - 1 * u, -h / 2 + 6 * u, w / 2 - 1 * u, -h / 2 + 6 * u);
                                g.DrawLine(wood, -w / 2 + 2 * u, 0, -w / 2 + 2 * u, h / 2); // legs
                                g.DrawLine(wood, w / 2 - 2 * u, 0, w / 2 - 2 * u, h / 2);
                            }
                            g.DrawLine(thin, w / 2 - 1 * u, -h / 2 + 8 * u, w / 2 - 1 * u, -h / 2 + 8 * u);
                            break;
                        }
                    case ThingKind.Dumbbell:
                        {
                            g.DrawLine(thin, -w / 2 + 5 * u, 0, w / 2 - 5 * u, 0);
                            using (SolidBrush wb = new SolidBrush(Color.FromArgb(255, 60, 60, 70)))
                            {
                                g.FillRectangle(wb, -w / 2, -h / 2, 7 * u, h);
                                g.FillRectangle(wb, w / 2 - 7 * u, -h / 2, 7 * u, h);
                            }
                            break;
                        }
                    case ThingKind.Frame:
                        {
                            RectangleF fr = new RectangleF(-w / 2, -h / 2, w, h);
                            using (SolidBrush fb = new SolidBrush(Color.FromArgb(255, 165, 110, 55))) g.FillRectangle(fb, fr);
                            RectangleF pic = new RectangleF(fr.X + 4 * u, fr.Y + 4 * u, fr.Width - 8 * u, fr.Height - 8 * u);
                            g.FillRectangle(Brushes.White, pic);
                            if (Art != null) Art.Draw(g, pic, 1);
                            g.DrawRectangle(thin, fr.X, fr.Y, fr.Width, fr.Height);
                            using (Pen sh = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f * u)) g.DrawLine(sh, pic.X + 3 * u, pic.Y + 3 * u, pic.X + 10 * u, pic.Y + 3 * u);
                            break;
                        }
                    case ThingKind.Teddy:
                        {
                            using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 170, 115, 70)))
                            using (SolidBrush lt = new SolidBrush(Color.FromArgb(255, 220, 180, 140)))
                            {
                                g.FillEllipse(tb, -w / 2, -h / 2, 6 * u, 6 * u);
                                g.FillEllipse(tb, w / 2 - 6 * u, -h / 2, 6 * u, 6 * u);
                                g.FillEllipse(tb, -w * 0.4f, -h / 2 + 2 * u, w * 0.8f, 9 * u);
                                g.FillEllipse(tb, -w / 2, -h / 2 + 9 * u, w, h - 9 * u);
                                g.FillEllipse(lt, -w * 0.2f, -h / 2 + 12 * u, w * 0.4f, 6 * u);
                                g.FillEllipse(inkB, -2.5f * u, -h / 2 + 4.5f * u, 1.6f * u, 1.6f * u);
                                g.FillEllipse(inkB, 1f * u, -h / 2 + 4.5f * u, 1.6f * u, 1.6f * u);
                            }
                            break;
                        }
                }
            }
        }

        public Color[] ShardColors()
        {
            switch (Kind)
            {
                case ThingKind.TV: return new Color[] { Color.FromArgb(70, 70, 80), Color.FromArgb(150, 190, 230), Color.FromArgb(40, 40, 45) };
                case ThingKind.Lamp: return new Color[] { Color.FromArgb(255, 210, 80), Color.FromArgb(40, 40, 45), Color.FromArgb(255, 240, 180) };
                case ThingKind.Plant: return new Color[] { Color.FromArgb(200, 100, 60), Color.FromArgb(70, 160, 70), Color.FromArgb(110, 80, 50) };
                case ThingKind.Chair: return new Color[] { Color.FromArgb(120, 75, 40), Color.FromArgb(160, 110, 60) };
                case ThingKind.Frame: return new Color[] { Color.FromArgb(165, 110, 55), Color.FromArgb(220, 235, 245), Color.White };
            }
            return new Color[] { Color.Gray };
        }
    }

    // Keeps track of all his things and moves the ones that are flying around.
    public class ThingManager
    {
        readonly Buddy b;
        public readonly List<Thing> All = new List<Thing>();
        Thing held;
        double heldVX, heldVY;
        Point lastC;
        readonly List<DebrisForm> debris = new List<DebrisForm>();

        public ThingManager(Buddy b) { this.b = b; }

        double S { get { return b.House != null ? b.House.Hu : b.U; } }

        public Thing Get(ThingKind k)
        {
            foreach (Thing t in All) if (t.Kind == k) return t;
            return null;
        }

        void Add(ThingKind k, string name, double hx, double hy, double w, double h, bool breakable, double breakSpeed, double bounce)
        {
            Thing t = new Thing();
            t.Kind = k; t.Name = name; t.HomeX = hx; t.HomeY = hy; t.W = w; t.Hh = h;
            t.Breakable = breakable; t.BreakSpeed = breakSpeed; t.Bounce = bounce;
            All.Add(t);
        }

        public void CreateAll()
        {
            if (All.Count == 0)
            {
                Add(ThingKind.TV, "TV", 195, 34 + 17, 44, 34, true, 950, 0.15);
                Add(ThingKind.Lamp, "lamp", 228, 31, 16, 62, true, 850, 0.2);
                Add(ThingKind.Plant, "plant", 68, 20, 24, 40, true, 900, 0.15);
                Add(ThingKind.Ball, "basketball", 104, 9, 18, 18, false, 0, 0.72);
                Add(ThingKind.Dumbbell, "dumbbell", 134, 5.5, 28, 11, false, 0, 0.1);
                Add(ThingKind.Chair, "chair", 220, House.UpperFloor + 20, 24, 40, true, 1300, 0.2);
                Add(ThingKind.Frame, "picture", 262, House.UpperFloor + 86, 46, 34, true, 900, 0.2);
                Add(ThingKind.Teddy, "teddy bear", 253, House.UpperFloor + 31, 16, 20, false, 0, 0.35);
                Get(ThingKind.Frame).Art = Doodle.Make(b.Rnd);
            }
            if (b.House != null) b.House.Invalidate2();
        }

        public void HideAll()
        {
            foreach (Thing t in All)
                if (t.State != ThingState.Home) { if (t.Win != null) t.Win.Hide(); t.State = ThingState.Home; }
            held = null;
        }

        public void CloseAll()
        {
            foreach (Thing t in All) if (t.Win != null) { t.Win.Close(); t.Win = null; }
            foreach (DebrisForm d in debris) d.Close();
        }

        public void Rescale() { foreach (Thing t in All) if (t.Win != null) t.Win.Resize2(); }

        public void KeepOnTop()
        {
            foreach (Thing t in All)
                if (t.Win != null && t.Win.Visible)
                    Native.SetWindowPos(t.Win.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // Which thing at home is under this house point (house units)?
        public Thing HitHome(double hx, double hy)
        {
            Thing best = null;
            foreach (Thing t in All)
            {
                if (t.State != ThingState.Home) continue;
                double pad = 4;
                if (Math.Abs(hx - t.HomeX) <= t.W / 2 + pad && Math.Abs(hy - t.HomeY) <= t.Hh / 2 + pad)
                    if (best == null || t.W * t.Hh < best.W * best.Hh) best = t;
            }
            return best;
        }

        PointF HomeScreen(Thing t) { return new PointF((float)b.House.Sx(t.HomeX), (float)b.House.Sy(t.HomeY)); }

        void Detach(Thing t)
        {
            PointF p = HomeScreen(t);
            t.X = p.X; t.Y = p.Y; t.Ang = 0; t.AV = 0; t.VX = 0; t.VY = 0; t.Resting = false;
            if (t.Win == null || t.Win.IsDisposed) t.Win = new ThingForm(b, this, t);
            t.Win.Resize2();
            if (!t.Win.Visible) t.Win.Show();
            if (t.Kind == ThingKind.TV) b.House.TvOn = false;
            b.House.Invalidate2();
        }

        public void BeginDragFromHome(Thing t, Point c)
        {
            if (b.House == null) return;
            Detach(t);
            BeginDrag(t, c);
            b.Brain.OnThingTaken(t);
        }

        public void BeginDrag(Thing t, Point c)
        {
            if (t.State == ThingState.Broken) return;
            if (t.State == ThingState.Carried && b.Carrying == t) b.Carrying = null;
            held = t;
            t.State = ThingState.Held;
            t.HeldOffX = t.X - c.X; t.HeldOffY = t.Y - c.Y;
            t.Resting = false;
            heldVX = 0; heldVY = 0; lastC = c;
        }

        // The stickman picks a thing up.
        public void PickUp(Thing t)
        {
            if (t.State == ThingState.Home) Detach(t);
            t.State = ThingState.Carried; t.Resting = false; t.Ang = 0;
            b.Carrying = t;
        }

        public void Drop(Thing t)
        {
            if (t.State == ThingState.Carried) { t.State = ThingState.Loose; t.VX = b.Facing * 60; t.VY = -60; }
            if (b.Carrying == t) b.Carrying = null;
        }

        public void PutHome(Thing t)
        {
            if (b.Carrying == t) b.Carrying = null;
            t.State = ThingState.Home; t.Resting = false;
            if (t.Win != null) t.Win.Hide();
            if (b.House != null) b.House.Invalidate2();
        }

        public void Throw(Thing t, double vx, double vy, double av)
        {
            if (b.Carrying == t) b.Carrying = null;
            if (t.State == ThingState.Home) Detach(t);
            t.State = ThingState.Loose; t.Resting = false;
            t.VX = vx; t.VY = vy; t.AV = av;
            t.LastHitBuddy = b.Now + 2.5; // his own throw can't hit him
        }

        public bool AnyOut()
        {
            foreach (Thing t in All) if (t.State == ThingState.Loose && t.Resting) return true;
            return false;
        }

        public void Update(double now, double dt)
        {
            if (All.Count == 0) return;
            bool left = (Control.MouseButtons & MouseButtons.Left) != 0;
            Point c = Cursor.Position;
            foreach (Thing t in All)
            {
                switch (t.State)
                {
                    case ThingState.Held:
                        heldVX = heldVX * 0.55 + (c.X - lastC.X) / dt * 0.45;
                        heldVY = heldVY * 0.55 + (c.Y - lastC.Y) / dt * 0.45;
                        t.X = c.X + t.HeldOffX; t.Y = c.Y + t.HeldOffY;
                        t.Ang = t.Ang * 0.9 + Math.Max(-0.6, Math.Min(0.6, heldVX / 4000)) * 0.1;
                        if (!left)
                        {
                            held = null;
                            if (b.House != null && b.House.Shown && Math.Abs(b.House.Hx(t.X) - t.HomeX) < 26 && Math.Abs((b.House.GroundY - t.Y) / b.House.Hu - t.HomeY) < 26
                                && Math.Abs(heldVX) + Math.Abs(heldVY) < 900 * b.Dpi)
                            {
                                PutHome(t);
                                break;
                            }
                            t.State = ThingState.Loose;
                            t.VX = Math.Max(-3000 * b.Dpi, Math.Min(3000 * b.Dpi, heldVX));
                            t.VY = Math.Max(-3000 * b.Dpi, Math.Min(2000 * b.Dpi, heldVY));
                            t.AV = t.VX / 300.0;
                        }
                        break;
                    case ThingState.Carried:
                        {
                            Joints j = b.WorldJoints();
                            if (b.Dribbling && t.Kind == ThingKind.Ball && b.CurrentAct == Act.Dribble)
                            {
                                double ph = (b.ActTime % 0.5) / 0.5, k = Math.Abs(Math.Sin(Math.PI * ph));
                                double r = t.W / 2 * S, top = j.HandA.Y + r, floor = b.Y - r;
                                t.X = j.HandA.X; t.Y = top + (floor - top) * k; t.Ang += 0.08;
                            }
                            else if (b.CurrentAct == Act.Curl || b.CurrentAct == Act.Shoot)
                            {
                                t.X = j.HandA.X; t.Y = j.HandA.Y - (b.CurrentAct == Act.Shoot ? t.Hh * S * 0.4 : 0); t.Ang = 0;
                            }
                            else
                            {
                                t.X = (j.HandA.X + j.HandB.X) / 2; t.Y = (j.HandA.Y + j.HandB.Y) / 2 - t.Hh * S * 0.3;
                                t.Ang = 0;
                            }
                            if (b.RagMode) Drop(t);
                            break;
                        }
                    case ThingState.Loose:
                        Physics(t, now, dt);
                        break;
                    case ThingState.Broken:
                        if (now - t.BrokenAt > 45) { t.State = ThingState.Home; if (b.House != null) b.House.Invalidate2(); }
                        break;
                }
                if (t.Win != null && t.Win.Visible)
                {
                    if (t.State == ThingState.Home || t.State == ThingState.Broken) t.Win.Hide();
                    else t.Win.RenderThing(now);
                }
            }
            lastC = c;
            for (int i = debris.Count - 1; i >= 0; i--)
                if (!debris[i].Step(dt)) { debris[i].Close(); debris.RemoveAt(i); }
        }

        void Physics(Thing t, double now, double dt)
        {
            double s = S;
            Rectangle vs = SystemInformation.VirtualScreen;
            if (t.Resting)
            {
                // follow the window it lies on, or fall if it goes away
                if (t.RestOn != null && t.RestOn.IsWindow)
                {
                    IntPtr h = t.RestOn.Hwnd;
                    if (!Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) { t.Resting = false; return; }
                    Native.RECT r = Native.GetVisibleRect(h);
                    t.X += r.Left - t.RestRect.Left; t.Y += r.Top - t.RestRect.Top; t.RestRect = r;
                    if (t.X < r.Left || t.X > r.Right) t.Resting = false;
                }
                else if (t.RestOn != null && t.RestOn.IsHouse)
                {
                    if (b.House == null || !b.House.Shown || !b.House.OnLevel(t.RestOn.Kind, t.X)) t.Resting = false;
                    else t.Y = (t.RestOn.Kind == Plat.Upper ? b.House.UpperY : b.House.RoofY) - t.ExtentY(s);
                }
                return;
            }
            double oldBottom = t.Y + t.ExtentY(s);
            t.VY += 2300 * b.Dpi * dt;
            t.VX *= Math.Pow(0.995, dt * 60);
            t.X += t.VX * dt; t.Y += t.VY * dt; t.Ang += t.AV * dt;
            double ex = t.ExtentX(s), ey = t.ExtentY(s);
            if (t.X - ex < vs.Left) { t.X = vs.Left + ex; if (Hit(t, -t.VX)) return; t.VX = Math.Abs(t.VX) * t.Bounce; t.AV = -t.AV * 0.5; }
            if (t.X + ex > vs.Right) { t.X = vs.Right - ex; if (Hit(t, t.VX)) return; t.VX = -Math.Abs(t.VX) * t.Bounce; t.AV = -t.AV * 0.5; }
            if (t.Y - ey < vs.Top) { t.Y = vs.Top + ey; if (Hit(t, -t.VY)) return; t.VY = Math.Abs(t.VY) * t.Bounce; }
            Plat p = World.LandingOn(t.X, oldBottom, t.Y + ey);
            if (p == null && t.Y + ey > vs.Bottom) p = World.SurfaceBelow(t.X, vs.Bottom - 5);
            if (p != null)
            {
                double impact = t.VY;
                if (Hit(t, impact)) return;
                t.Y = p.Y - ey;
                t.VY = -impact * t.Bounce;
                t.VX *= t.Kind == ThingKind.Ball ? 0.92 : 0.6;
                if (t.Kind == ThingKind.Ball) t.AV = t.VX / (t.W / 2 * s);
                else t.AV *= 0.4;
                if (Math.Abs(t.VY) < 140 * b.Dpi)
                {
                    t.VY = 0;
                    if (t.Kind != ThingKind.Ball)
                    {
                        // tip over onto the nearest flat side
                        double q = Math.Round(t.Ang / (Math.PI / 2)) * (Math.PI / 2);
                        t.Ang = t.Ang + (q - t.Ang) * 0.5;
                        if (Math.Abs(q - t.Ang) < 0.05) t.Ang = q;
                        t.Y = p.Y - t.ExtentY(s);
                    }
                    if (Math.Abs(t.VX) < 25 * b.Dpi && (t.Kind == ThingKind.Ball || Math.Abs(t.Ang - Math.Round(t.Ang / (Math.PI / 2)) * (Math.PI / 2)) < 0.06))
                    {
                        t.VX = 0; t.AV = 0; t.Resting = true;
                        t.RestOn = Support.Of(p);
                        if (t.RestOn.IsWindow) t.RestRect = Native.GetVisibleRect(p.Hwnd);
                    }
                }
            }
            // hit the stickman?
            double sp = Math.Sqrt(t.VX * t.VX + t.VY * t.VY);
            if (sp > 550 * b.Dpi && now - t.LastHitBuddy > 0.6 && !b.Hiding)
            {
                RectangleF body = b.WorldJoints().Bounds();
                body.Inflate((float)(t.Diag * s * 0.35), (float)(t.Diag * s * 0.35));
                if (body.Contains((float)t.X, (float)t.Y))
                {
                    t.LastHitBuddy = now;
                    b.Knock(t.VX * 0.55, t.VY * 0.55 - 250 * b.Dpi);
                    b.Brain.OnHitByThing(t, sp / b.Dpi);
                    t.VX *= -0.3; t.VY *= -0.3;
                }
            }
        }

        // Returns true if the thing broke.
        bool Hit(Thing t, double speed)
        {
            if (!t.Breakable || speed < t.BreakSpeed * b.Dpi) return false;
            Break(t);
            return true;
        }

        public void Break(Thing t)
        {
            t.State = ThingState.Broken;
            t.BrokenAt = b.Now;
            if (t.Win != null) t.Win.Hide();
            if (b.Carrying == t) b.Carrying = null;
            try
            {
                DebrisForm d = new DebrisForm(b, t, S);
                debris.Add(d);
                d.Show();
            }
            catch (Exception ex) { Log.Write("debris: " + ex.Message); }
            b.Brain.OnThingBroke(t);
        }
    }

    // The little window a thing lives in while it is out of the house.
    public class ThingForm : Form
    {
        readonly Buddy b;
        readonly ThingManager m;
        readonly Thing t;
        Bitmap canvas;
        int size;
        double lastX = double.NaN, lastY, lastAng;

        public ThingForm(Buddy b, ThingManager m, Thing t)
        {
            this.b = b; this.m = m; this.t = t;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-10000, -10000);
            Text = "Stick Buddy " + t.Name;
            Resize2();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Resize2()
        {
            double s = b.House != null ? b.House.Hu : b.U;
            size = (int)Math.Ceiling(t.Diag * s + 14 * s);
            if (canvas != null) canvas.Dispose();
            canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            lastX = double.NaN;
        }

        public void RenderThing(double now)
        {
            bool tv = false;
            if (Math.Abs(t.X - lastX) < 0.3 && Math.Abs(t.Y - lastY) < 0.3 && Math.Abs(t.Ang - lastAng) < 0.002) return;
            lastX = t.X; lastY = t.Y; lastAng = t.Ang;
            float s = (float)(b.House != null ? b.House.Hu : b.U);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(3, 128, 128, 128)))
                    g.FillEllipse(hb, size * 0.15f, size * 0.15f, size * 0.7f, size * 0.7f);
                g.TranslateTransform(size / 2f, size / 2f);
                g.RotateTransform((float)(t.Ang * 180 / Math.PI));
                t.Draw(g, s, now, tv);
            }
            Native.UpdateLayered(Handle, canvas, (int)Math.Round(t.X - size / 2.0), (int)Math.Round(t.Y - size / 2.0));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { b.ShowMenu(Cursor.Position); return; }
            if (e.Button == MouseButtons.Left && t.State != ThingState.Broken) m.BeginDrag(t, Cursor.Position);
        }
    }

    // Bits of a broken thing flying everywhere for a moment.
    public class DebrisForm : Form
    {
        class Shard { public double X, Y, VX, VY, A, AV, S; public Color C; public PointF[] Shape; public bool Rest; }
        readonly List<Shard> shards = new List<Shard>();
        readonly Buddy b;
        Bitmap canvas;
        Rectangle area;
        double age;
        double floorY;

        public DebrisForm(Buddy b, Thing t, double s)
        {
            this.b = b;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-10000, -10000);
            Text = "Stick Buddy crash";
            Rectangle vs = SystemInformation.VirtualScreen;
            Plat p = World.SurfaceBelow(t.X, t.Y);
            floorY = p.Y;
            int half = (int)(260 * s);
            area = Rectangle.FromLTRB((int)Math.Max(vs.Left, t.X - half), (int)Math.Max(vs.Top, t.Y - half), (int)Math.Min(vs.Right, t.X + half), (int)Math.Min(vs.Bottom, floorY + 10));
            if (area.Height < 20) area = new Rectangle(area.X, area.Y - 40, area.Width, area.Height + 50);
            canvas = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppArgb);
            Color[] cols = t.ShardColors();
            Random r = b.Rnd;
            int n = 14 + r.Next(6);
            for (int i = 0; i < n; i++)
            {
                Shard sh = new Shard();
                sh.X = t.X + (r.NextDouble() - 0.5) * t.W * s; sh.Y = t.Y + (r.NextDouble() - 0.5) * t.Hh * s;
                double ang = r.NextDouble() * Math.PI * 2, sp = (150 + r.NextDouble() * 450) * b.Dpi;
                sh.VX = Math.Cos(ang) * sp + t.VX * 0.15; sh.VY = Math.Sin(ang) * sp - 250 * b.Dpi;
                sh.AV = (r.NextDouble() - 0.5) * 20; sh.S = (2.5 + r.NextDouble() * 4.5) * s;
                sh.C = cols[r.Next(cols.Length)];
                int k = 3 + r.Next(2);
                sh.Shape = new PointF[k];
                for (int q = 0; q < k; q++) { double a = q * Math.PI * 2 / k + r.NextDouble() * 0.8; double rr = sh.S * (0.6 + r.NextDouble() * 0.6); sh.Shape[q] = new PointF((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr)); }
                shards.Add(sh);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public bool Step(double dt)
        {
            age += dt;
            if (age > 2.2) return false;
            foreach (Shard s in shards)
            {
                if (s.Rest) continue;
                s.VY += 2300 * b.Dpi * dt;
                s.X += s.VX * dt; s.Y += s.VY * dt; s.A += s.AV * dt;
                if (s.Y > floorY - 2) { s.Y = floorY - 2; s.VY = -s.VY * 0.3; s.VX *= 0.6; s.AV *= 0.5; if (Math.Abs(s.VY) < 60) s.Rest = true; }
                if (s.X < area.Left + 3 || s.X > area.Right - 3) s.VX = -s.VX * 0.5;
            }
            int alpha = age < 1.6 ? 255 : (int)(255 * (2.2 - age) / 0.6);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (Shard s in shards)
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform((float)(s.X - area.Left), (float)(s.Y - area.Top));
                    g.RotateTransform((float)(s.A * 57.3));
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), s.C))) g.FillPolygon(br, s.Shape);
                    g.Restore(st);
                }
            }
            Native.UpdateLayered(Handle, canvas, area.Left, area.Top);
            return true;
        }
    }
}
