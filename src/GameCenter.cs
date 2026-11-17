using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace StickBuddy
{
    // All his games in one window, with pictures and your best scores.
    public class GameCenterForm : Form
    {
        class Card : Panel
        {
            public string Id, Title, Desc;
            public Action Play;
            public bool Hover;
            public Card() { DoubleBuffered = true; Cursor = Cursors.Hand; }
        }

        readonly Buddy b;
        readonly float d;

        public GameCenterForm(Buddy b)
        {
            this.b = b;
            d = (float)b.Dpi;
            Text = b.S.Name + "'s Game Center";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(1050 * d), (int)(700 * d));
            BackColor = Color.FromArgb(28, 30, 40);
            try { Icon = Renderer.MakeIcon(b.S.BodyColor); } catch { }

            Label head = new Label();
            head.Text = "  Pick a game! Click a card to play.";
            head.Dock = DockStyle.Top; head.Height = (int)(54 * d);
            head.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
            head.ForeColor = Color.White; head.TextAlign = ContentAlignment.MiddleLeft;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill; flow.AutoScroll = true; flow.Padding = new Padding((int)(12 * d));
            Add(flow, "balloons", "Balloon Pop", "Grab me and THROW me into balloons!", delegate { b.StartBalloonPop(); });
            Add(flow, "dodgeball", "Dodgeball", "I throw balls at your mouse. Dodge!", delegate { b.StartDodgeball(); });
            Add(flow, "whack", "Whack-a-Sticky", "Click me when I pop up!", delegate { b.StartWhack(); });
            Add(flow, "hoops", "Hoops", "Basketball shootout at my house.", delegate { b.StartHoops(); });
            Add(flow, "dance", "Dance Battle", "Hit the arrows to the beat!", delegate { b.StartDanceBattle(); });
            Add(flow, "guess", "Guess My Drawing", "Guess what I'm drawing, fast!", delegate { b.StartGuessDrawing(); });
            Add(flow, "tagme", "Tag: you're it", "Catch me 5 times. I do parkour!", delegate { b.StartTag(true); });
            Add(flow, "tagyou", "Tag: I'm it", "Run from me with your mouse!", delegate { b.StartTag(false); });
            Add(flow, "hide", "Hide and Seek", "Find me hiding in your files.", delegate { b.StartHideAndSeek(); });
            Add(flow, "ttt", "Tic Tac Toe", "Can you beat me?", delegate { b.StartTicTacToe(); });
            Add(flow, "snake", "Snake", "Watch me play Snake!", delegate { b.StartSnake(); });
            Add(flow, "rps", "Rock Paper Scissors", "Say rock, paper or scissors!", delegate { b.Brain.StartRps(); });
            Add(flow, "lab", "Obby Lab", "Build obbies, teach my brain.", delegate { b.OpenLab(); });
            Add(flow, "pilot", "I play YOUR game", "Open a game, I play it (needs a key).", delegate { b.StartGamePilot("", null); });
            Controls.Add(flow);
            Controls.Add(head);
        }

        void Add(FlowLayoutPanel flow, string id, string title, string desc, Action play)
        {
            Card c = new Card();
            c.Id = id; c.Title = title; c.Desc = desc; c.Play = play;
            c.Size = new Size((int)(228 * d), (int)(196 * d));
            c.Margin = new Padding((int)(8 * d));
            c.Paint += PaintCard;
            c.MouseEnter += delegate { c.Hover = true; c.Invalidate(); };
            c.MouseLeave += delegate { c.Hover = false; c.Invalidate(); };
            c.Click += delegate { Hide(); c.Play(); Close(); };
            flow.Controls.Add(c);
        }

        void PaintCard(object sender, PaintEventArgs e)
        {
            Card c = (Card)sender;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF r = new RectangleF(1, 1, c.Width - 3, c.Height - 3);
            g.Clear(BackColor);
            using (GraphicsPath gp = Renderer.RoundRect(r, 14 * d))
            using (SolidBrush bg = new SolidBrush(c.Hover ? Color.FromArgb(60, 64, 86) : Color.FromArgb(44, 47, 62)))
            using (Pen bp = new Pen(c.Hover ? Color.FromArgb(255, 210, 70) : Color.FromArgb(70, 74, 95), 2 * d))
            { g.FillPath(bg, gp); g.DrawPath(bp, gp); }
            RectangleF art = new RectangleF(10 * d, 10 * d, c.Width - 20 * d, 108 * d);
            using (GraphicsPath ap = Renderer.RoundRect(art, 10 * d))
            using (LinearGradientBrush sky = new LinearGradientBrush(art, Color.FromArgb(170, 210, 255), Color.FromArgb(240, 246, 255), 90f))
                g.FillPath(sky, ap);
            GraphicsState gs = g.Save();
            g.SetClip(art);
            DrawArt(g, c.Id, art, Environment.TickCount / 1000.0);
            g.Restore(gs);
            using (Font tf = new Font("Segoe UI", 14 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font sf = new Font("Segoe UI", 11.5f * d, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush grey = new SolidBrush(Color.FromArgb(190, 195, 215)))
            using (SolidBrush gold = new SolidBrush(Color.FromArgb(255, 210, 70)))
            {
                g.DrawString(c.Title, tf, Brushes.White, 12 * d, 124 * d);
                g.DrawString(c.Desc, sf, grey, new RectangleF(12 * d, 146 * d, c.Width - 24 * d, 30 * d));
                int best = Scores.Best(c.Id);
                if (best > 0) g.DrawString("Best: " + best, sf, gold, 12 * d, 174 * d);
            }
        }

        void Figure(Graphics g, Act a, float x, float y, float u, int face, Prop prop)
        {
            Pose p = Poses.Get(a, 0.3, 0);
            Joints j = Renderer.Compute(p, x, y, face, u);
            Renderer.DrawFigure(g, j, face, u, b.S.Rainbow ? Color.FromArgb(226, 32, 36) : b.S.BodyColor, prop, 0, false);
        }

        void DrawArt(Graphics g, string id, RectangleF a, double t)
        {
            float u = 0.75f * d, gx = a.X + a.Width / 2, gy = a.Bottom - 8 * d;
            using (Pen ground = new Pen(Color.FromArgb(120, 90, 90, 110), 2 * d)) g.DrawLine(ground, a.X, gy, a.Right, gy);
            switch (id)
            {
                case "balloons":
                    for (int i = 0; i < 4; i++)
                        using (SolidBrush bb = new SolidBrush(new[] { Color.Tomato, Color.DodgerBlue, Color.Gold, Color.MediumPurple }[i]))
                            g.FillEllipse(bb, a.X + (20 + i * 48) * d, a.Y + (12 + (i % 2) * 22) * d, 22 * d, 28 * d);
                    Figure(g, Act.Dive, gx - 30 * d, gy - 20 * d, u, 1, Prop.None);
                    break;
                case "dodgeball":
                    Figure(g, Act.Throw, a.X + 50 * d, gy, u, 1, Prop.None);
                    using (SolidBrush rb = new SolidBrush(Color.FromArgb(225, 50, 50))) g.FillEllipse(rb, a.X + 130 * d, a.Y + 30 * d, 20 * d, 20 * d);
                    {
                        // a mouse pointer running away
                        float px = a.Right - 40 * d, py = a.Y + 30 * d;
                        PointF[] ptr = { new PointF(px, py), new PointF(px, py + 22 * d), new PointF(px + 6 * d, py + 17 * d), new PointF(px + 10 * d, py + 26 * d), new PointF(px + 14 * d, py + 24 * d), new PointF(px + 10 * d, py + 15 * d), new PointF(px + 17 * d, py + 15 * d) };
                        g.FillPolygon(Brushes.White, ptr);
                        using (Pen pp = new Pen(Color.Black, 1.5f * d)) g.DrawPolygon(pp, ptr);
                    }
                    break;
                case "whack":
                    using (SolidBrush wb = new SolidBrush(Color.FromArgb(70, 74, 95))) g.FillRectangle(wb, a.X, gy - 30 * d, a.Width, 40 * d);
                    { GraphicsState s = g.Save(); g.SetClip(new RectangleF(a.X, a.Y, a.Width, gy - 30 * d - a.Y)); Figure(g, Act.Cheer, gx, gy, u, 1, Prop.None); g.Restore(s); }
                    using (Font f = new Font("Segoe UI", 16 * d, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("BONK!", f, Brushes.DarkOrange, gx + 20 * d, a.Y + 10 * d);
                    break;
                case "hoops":
                    using (Pen hp = new Pen(Color.OrangeRed, 3 * d)) g.DrawLine(hp, a.X + 20 * d, a.Y + 40 * d, a.X + 50 * d, a.Y + 40 * d);
                    g.FillRectangle(Brushes.White, a.X + 14 * d, a.Y + 18 * d, 6 * d, 34 * d);
                    using (SolidBrush ob = new SolidBrush(Color.FromArgb(235, 125, 35))) g.FillEllipse(ob, a.X + 70 * d, a.Y + 18 * d, 16 * d, 16 * d);
                    Figure(g, Act.Shoot, a.Right - 60 * d, gy, u, -1, Prop.None);
                    break;
                case "dance":
                    using (SolidBrush db = new SolidBrush(Color.FromArgb(60, 30, 90))) g.FillRectangle(db, a);
                    string[] ar = { "←", "↓", "↑", "→" };
                    using (Font f = new Font("Segoe UI Symbol", 18 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                        for (int i = 0; i < 4; i++) g.DrawString(ar[i], f, Brushes.White, a.X + (16 + i * 26) * d, a.Y + (10 + (i * 37) % 50) * d);
                    Figure(g, Act.DanceDisco, a.Right - 50 * d, gy, u, 1, Prop.None);
                    break;
                case "guess":
                    g.FillRectangle(Brushes.White, a.X + 20 * d, a.Y + 12 * d, 90 * d, 70 * d);
                    using (Pen cp = new Pen(Color.Orange, 2 * d)) { g.DrawEllipse(cp, a.X + 45 * d, a.Y + 30 * d, 40 * d, 34 * d); g.DrawLine(cp, a.X + 50 * d, a.Y + 34 * d, a.X + 52 * d, a.Y + 22 * d); }
                    using (Font f = new Font("Segoe UI", 26 * d, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("?", f, Brushes.MediumPurple, a.Right - 44 * d, a.Y + 6 * d);
                    Figure(g, Act.Point, a.Right - 70 * d, gy, u, -1, Prop.None);
                    break;
                case "tagme":
                case "tagyou":
                    Figure(g, id == "tagme" ? Act.Slide : Act.Dive, gx, gy, u, id == "tagme" ? 1 : -1, Prop.None);
                    using (Pen sp = new Pen(Color.FromArgb(120, 0, 0, 0), 2 * d)) for (int i = 0; i < 3; i++) g.DrawLine(sp, gx - (60 + i * 8) * d, gy - (20 + i * 12) * d, gx - (30 + i * 8) * d, gy - (20 + i * 12) * d);
                    break;
                case "hide":
                    using (SolidBrush fb = new SolidBrush(Color.FromArgb(250, 200, 60))) g.FillRectangle(fb, gx - 22 * d, gy - 40 * d, 44 * d, 34 * d);
                    { GraphicsState s = g.Save(); using (Region rg = new Region(new RectangleF(a.X, a.Y, a.Width, a.Height))) { rg.Exclude(new RectangleF(gx - 22 * d, gy - 40 * d, 44 * d, 34 * d)); g.Clip = rg; Figure(g, Act.Peek, gx, gy - 6 * d, u * 0.8f, 1, Prop.None); } g.Restore(s); }
                    break;
                case "ttt":
                    using (Pen tp = new Pen(Color.FromArgb(60, 60, 80), 2.5f * d))
                    {
                        float x0 = a.X + 20 * d, y0 = a.Y + 14 * d, s = 26 * d;
                        for (int i = 1; i < 3; i++) { g.DrawLine(tp, x0 + i * s, y0, x0 + i * s, y0 + 3 * s); g.DrawLine(tp, x0, y0 + i * s, x0 + 3 * s, y0 + i * s); }
                    }
                    Figure(g, Act.Think, a.Right - 60 * d, gy, u, -1, Prop.None);
                    break;
                case "snake":
                    using (Pen sk = new Pen(Color.FromArgb(70, 190, 90), 8 * d)) { sk.StartCap = LineCap.Round; sk.EndCap = LineCap.Round; g.DrawLines(sk, new[] { new PointF(a.X + 20 * d, a.Y + 30 * d), new PointF(a.X + 80 * d, a.Y + 30 * d), new PointF(a.X + 80 * d, a.Y + 60 * d), new PointF(a.X + 40 * d, a.Y + 60 * d) }); }
                    Figure(g, Act.Play, a.Right - 60 * d, gy, u, -1, Prop.Controller);
                    break;
                case "rps":
                    Figure(g, Act.Shrug, gx, gy, u, 1, Prop.None);
                    using (Font f = new Font("Segoe UI Symbol", 22 * d, GraphicsUnit.Pixel)) g.DrawString("✊ ✋ ✌", f, Brushes.DimGray, a.X + 10 * d, a.Y + 6 * d);
                    break;
                case "lab":
                    using (SolidBrush kb = new SolidBrush(Color.FromArgb(55, 58, 68))) { g.FillRectangle(kb, a.X + 20 * d, gy - 14 * d, 50 * d, 14 * d); g.FillRectangle(kb, a.X + 100 * d, gy - 40 * d, 40 * d, 14 * d); }
                    using (SolidBrush lv = new SolidBrush(Color.OrangeRed)) g.FillRectangle(lv, a.X + 70 * d, gy - 6 * d, 30 * d, 6 * d);
                    Figure(g, Act.Jump, a.X + 85 * d, gy - 30 * d, u, 1, Prop.None);
                    break;
                default:
                    g.FillRectangle(Brushes.DimGray, a.X + 20 * d, a.Y + 14 * d, 100 * d, 66 * d);
                    using (SolidBrush sc = new SolidBrush(Color.FromArgb(120, 200, 120))) g.FillRectangle(sc, a.X + 24 * d, a.Y + 18 * d, 92 * d, 58 * d);
                    Figure(g, Act.Play, a.Right - 55 * d, gy, u, -1, Prop.Controller);
                    break;
            }
        }
    }

    public partial class Buddy
    {
        GameCenterForm gameCenter;

        public void OpenGameCenter()
        {
            Touch();
            if (gameCenter == null || gameCenter.IsDisposed) gameCenter = new GameCenterForm(this);
            gameCenter.Show();
            BringForward(gameCenter);
            Say(Pick("Yay, games! Pick one!", "Game time! What do you want to play?", "Ooh, which one? Which one?"));
            FlashAct(Act.Cheer, 1.5);
        }
    }
}
