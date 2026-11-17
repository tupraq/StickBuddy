using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // Dance Battle: arrows float up, press the matching arrow key (or W A S D) when they reach the top.
    // Sticky dances on top of the window: hit the beat and he busts a move, miss and he stumbles.
    public class DanceForm : Form
    {
        class View : Panel { public View() { DoubleBuffered = true; } }
        class Note { public double T; public int Lane; public bool Done, Hit; }
        enum St { Menu, Play, Over }

        readonly Buddy b;
        readonly View view;
        readonly System.Windows.Forms.Timer timer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly float d;
        St st = St.Menu;
        int diff = 1;
        List<Note> notes = new List<Note>();
        double songStart, travel = 1.6;
        const double Bpm = 120, Length = 62;
        static byte[] song;
        SoundPlayer music;
        int score, combo, maxCombo, perfect, great, good, missed;
        double health = 60;
        string judge = "";
        double judgeAt = -10;
        readonly double[] flash = new double[4];
        bool failed, record;
        readonly Random rnd = new Random();
        static readonly Color[] LaneCol = { Color.FromArgb(200, 90, 255), Color.FromArgb(40, 200, 255), Color.FromArgb(80, 230, 120), Color.FromArgb(255, 80, 110) };
        static readonly float[] LaneRot = { -90, 180, 0, 90 };
        static readonly string[] DiffName = { "EASY", "NORMAL", "HARD" };

        double Now { get { return clock.Elapsed.TotalSeconds; } }

        public DanceForm(Buddy b)
        {
            this.b = b;
            d = (float)b.Dpi;
            Text = "Dance Battle with " + b.S.Name;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size((int)(440 * d), (int)(600 * d));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Renderer.MakeIcon(b.S.BodyColor); } catch { }
            view = new View();
            view.Dock = DockStyle.Fill;
            view.Paint += Draw;
            view.MouseDown += Click2;
            Controls.Add(view);
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += delegate { Tick(); view.Invalidate(); };
            timer.Start();
        }

        float LaneX(int i) { return (60 + i * 106) * d; }
        float TargetY { get { return 92 * d; } }
        float BottomY { get { return ClientSize.Height + 30 * d; } }

        // ---------- playing ----------
        void Start(int level)
        {
            diff = level;
            travel = level == 0 ? 1.9 : level == 1 ? 1.6 : 1.3;
            notes = new List<Note>();
            double beat = 60 / Bpm;
            int last = -1;
            for (int k = 4; k < (int)((Length - 3) / beat); k++)
            {
                double pOn = level == 0 ? 0.55 : level == 1 ? 0.8 : 0.95, pOff = level == 0 ? 0 : level == 1 ? 0.22 : 0.45;
                if (k % 16 >= 14) pOn *= 0.5; // a little rest at the end of each phrase
                if (rnd.NextDouble() < pOn) last = AddNote(k * beat, last);
                if (rnd.NextDouble() < pOff) last = AddNote(k * beat + beat / 2, last);
                if (level == 2 && rnd.NextDouble() < 0.1) AddNote(k * beat, last); // two at once
            }
            score = combo = maxCombo = perfect = great = good = missed = 0;
            health = 60; failed = false; record = false; judge = "";
            try
            {
                if (song == null) song = Sfx.DanceSong(Bpm, (int)Math.Ceiling(Length / 2));
                if (music != null) music.Stop();
                if (!b.S.Muted) { music = new SoundPlayer(new MemoryStream(song)); music.Load(); music.Play(); }
            }
            catch (Exception ex) { Log.Write("dance music: " + ex.Message); }
            songStart = Now + 0.12;
            st = St.Play;
            b.DanceStart();
        }

        int AddNote(double t, int last)
        {
            int lane = rnd.Next(4);
            if (lane == last && rnd.NextDouble() < 0.6) lane = (lane + 1 + rnd.Next(3)) % 4;
            foreach (Note n in notes) if (Math.Abs(n.T - t) < 0.01 && n.Lane == lane) return last;
            Note nn = new Note(); nn.T = t; nn.Lane = lane;
            notes.Add(nn);
            return lane;
        }

        void Tick()
        {
            if (st != St.Play) return;
            double t = Now - songStart;
            foreach (Note n in notes)
            {
                if (n.Done || t - n.T < 0.19) continue;
                n.Done = true; missed++; combo = 0;
                health -= 7;
                Judge("MISS");
                b.DanceMiss();
            }
            if (health <= 0) { failed = true; End(); }
            else if (t > Length + 0.5) End();
        }

        void End()
        {
            st = St.Over;
            if (music != null) { try { music.Stop(); } catch { } }
            record = Scores.Submit("dance", score);
            b.DanceDone(score, Grade(), failed, record);
        }

        string Grade()
        {
            int total = perfect + great + good + missed;
            if (failed || total == 0) return "F";
            double acc = (perfect + great * 0.75 + good * 0.4) / total;
            return acc > 0.93 ? "S" : acc > 0.82 ? "A" : acc > 0.68 ? "B" : acc > 0.5 ? "C" : "D";
        }

        void Judge(string j) { judge = j; judgeAt = Now; }

        void Press(int lane)
        {
            flash[lane] = Now;
            if (st != St.Play) return;
            double t = Now - songStart;
            Note best = null;
            foreach (Note n in notes)
                if (!n.Done && n.Lane == lane && Math.Abs(n.T - t) < 0.19 && (best == null || Math.Abs(n.T - t) < Math.Abs(best.T - t))) best = n;
            if (best == null) return;
            double dt = Math.Abs(best.T - t);
            best.Done = true; best.Hit = true;
            int pts;
            string q;
            if (dt < 0.05) { q = "PERFECT!"; pts = 300; perfect++; }
            else if (dt < 0.1) { q = "GREAT!"; pts = 200; great++; }
            else { q = "GOOD"; pts = 100; good++; }
            combo++;
            maxCombo = Math.Max(maxCombo, combo);
            score += pts * (1 + Math.Min(3, combo / 10));
            health = Math.Min(100, health + (pts >= 200 ? 3 : 1.5));
            Judge(q);
            b.DanceHit(lane, pts >= 300, combo);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left: case Keys.A: Press(0); return true;
                case Keys.Down: case Keys.S: Press(1); return true;
                case Keys.Up: case Keys.W: Press(2); return true;
                case Keys.Right: case Keys.D: Press(3); return true;
                case Keys.D1: case Keys.NumPad1: if (st != St.Play) Start(0); return true;
                case Keys.D2: case Keys.NumPad2: if (st != St.Play) Start(1); return true;
                case Keys.D3: case Keys.NumPad3: if (st != St.Play) Start(2); return true;
                case Keys.Enter: case Keys.Space: if (st != St.Play) Start(diff); return true;
                case Keys.Escape:
                    if (st == St.Play) { failed = true; End(); } else Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        RectangleF[] DiffButtons()
        {
            RectangleF[] r = new RectangleF[3];
            for (int i = 0; i < 3; i++) r[i] = new RectangleF(70 * d, (330 + i * 64) * d, ClientSize.Width - 140 * d, 50 * d);
            return r;
        }

        void Click2(object sender, MouseEventArgs e)
        {
            if (st == St.Play) return;
            RectangleF[] r = DiffButtons();
            for (int i = 0; i < 3; i++) if (r[i].Contains(e.Location)) { Start(i); return; }
        }

        // ---------- drawing ----------
        static PointF[] ArrowShape(float s)
        {
            return new[] { new PointF(0, -s), new PointF(s, 0), new PointF(s * 0.42f, 0), new PointF(s * 0.42f, s * 0.9f), new PointF(-s * 0.42f, s * 0.9f), new PointF(-s * 0.42f, 0), new PointF(-s, 0) };
        }

        void DrawArrow(Graphics g, int lane, float x, float y, float s, Color fill, Color edge, float pen)
        {
            GraphicsState gs = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(LaneRot[lane]);
            PointF[] pts = ArrowShape(s);
            if (fill.A > 0) using (SolidBrush b2 = new SolidBrush(fill)) g.FillPolygon(b2, pts);
            using (Pen p = new Pen(edge, pen)) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, pts); }
            g.Restore(gs);
        }

        void Draw(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Rectangle cr = view.ClientRectangle;
            double now = Now, t = Now - songStart;
            using (LinearGradientBrush bg = new LinearGradientBrush(cr, Color.FromArgb(40, 20, 70), Color.FromArgb(10, 10, 25), 90f)) g.FillRectangle(bg, cr);
            // stage lights
            for (int i = 0; i < 3; i++)
            {
                double ph = now * 0.8 + i * 2.1;
                float lx = (float)(cr.Width * (0.5 + 0.45 * Math.Sin(ph)));
                using (SolidBrush lb = new SolidBrush(Color.FromArgb(25, Buddy.FromHsv((now * 40 + i * 120) % 360, 0.8, 1))))
                    g.FillPolygon(lb, new[] { new PointF(cr.Width / 2f, 0), new PointF(lx - 60 * d, cr.Height), new PointF(lx + 60 * d, cr.Height) });
            }
            using (Font title = new Font("Segoe UI", 30 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font f = new Font("Segoe UI", 15 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", 12.5f * d, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat c = new StringFormat())
            {
                c.Alignment = StringAlignment.Center; c.LineAlignment = StringAlignment.Center;
                if (st == St.Menu)
                {
                    g.DrawString("DANCE BATTLE", title, Brushes.White, new RectangleF(0, 90 * d, cr.Width, 50 * d), c);
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(200, 210, 220, 255)))
                        g.DrawString("Press the arrow keys (or W A S D)\nwhen the arrows reach the top.\n" + b.S.Name + " dances on top of this window!\nBest: " + Scores.Best("dance"), small, gb, new RectangleF(0, 150 * d, cr.Width, 130 * d), c);
                    for (int i = 0; i < 4; i++) DrawArrow(g, i, LaneX(i), 300 * d, 16 * d, LaneCol[i], Color.White, 2 * d);
                    DrawButtons(g, f, c);
                    return;
                }
                if (st == St.Over)
                {
                    g.DrawString(failed ? "TOO MANY MISSES!" : "SONG CLEAR!", f, Brushes.White, new RectangleF(0, 60 * d, cr.Width, 30 * d), c);
                    using (Font huge = new Font("Segoe UI", 90 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush gold = new SolidBrush(Color.FromArgb(255, 210, 70)))
                        g.DrawString(Grade(), huge, gold, new RectangleF(0, 90 * d, cr.Width, 120 * d), c);
                    string info = "Score " + score + (record ? "   NEW RECORD!" : "") + "\nMax combo " + maxCombo + "\nPerfect " + perfect + "   Great " + great + "   Good " + good + "   Miss " + missed;
                    g.DrawString(info, small, Brushes.White, new RectangleF(0, 215 * d, cr.Width, 90 * d), c);
                    DrawButtons(g, f, c);
                    return;
                }
                // lanes and targets
                for (int i = 0; i < 4; i++)
                {
                    using (SolidBrush lane = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillRectangle(lane, LaneX(i) - 40 * d, TargetY - 40 * d, 80 * d, cr.Height);
                    double fl = Math.Max(0, 1 - (now - flash[i]) / 0.15);
                    DrawArrow(g, i, LaneX(i), TargetY, 30 * d, Color.FromArgb((int)(160 * fl), LaneCol[i]), Color.FromArgb(200, 255, 255, 255), 3 * d);
                }
                // notes
                foreach (Note n in notes)
                {
                    if (n.Done) continue;
                    double dtn = n.T - t;
                    if (dtn > travel) continue;
                    float y = TargetY + (float)(dtn / travel) * (BottomY - TargetY);
                    DrawArrow(g, n.Lane, LaneX(n.Lane), y, 28 * d, LaneCol[n.Lane], Color.White, 2.5f * d);
                }
                // score, combo, judgement
                g.DrawString(score.ToString(), f, Brushes.White, new RectangleF(0, 8 * d, cr.Width - 14 * d, 26 * d), new StringFormat { Alignment = StringAlignment.Far });
                g.DrawString(DiffName[diff], small, Brushes.Gray, 12 * d, 10 * d);
                if (now - judgeAt < 0.6)
                {
                    int al = (int)(255 * (1 - (now - judgeAt) / 0.6));
                    Color jc = judge == "MISS" ? Color.FromArgb(al, 255, 90, 90) : judge.StartsWith("PERFECT") ? Color.FromArgb(al, 255, 220, 70) : Color.FromArgb(al, 140, 230, 255);
                    using (Font jf = new Font("Segoe UI", 28 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush jb = new SolidBrush(jc))
                        g.DrawString(judge, jf, jb, new RectangleF(0, cr.Height * 0.45f, cr.Width, 40 * d), c);
                }
                if (combo >= 5)
                    using (Font cf = new Font("Segoe UI", 18 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                        g.DrawString(combo + " COMBO" + (combo >= 10 ? "  x" + (1 + Math.Min(3, combo / 10)) : ""), cf, Brushes.White, new RectangleF(0, cr.Height * 0.45f + 40 * d, cr.Width, 30 * d), c);
                // health and song progress
                RectangleF hb = new RectangleF(12 * d, cr.Height - 22 * d, cr.Width - 24 * d, 10 * d);
                using (SolidBrush bk = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(bk, hb);
                using (SolidBrush hp = new SolidBrush(health > 30 ? Color.FromArgb(90, 220, 120) : Color.FromArgb(255, 90, 90))) g.FillRectangle(hp, hb.X, hb.Y, (float)(hb.Width * health / 100), hb.Height);
                using (SolidBrush pr = new SolidBrush(Color.FromArgb(200, 255, 210, 70))) g.FillRectangle(pr, 0, 0, (float)(cr.Width * Math.Max(0, Math.Min(1, t / Length))), 3 * d);
            }
        }

        void DrawButtons(Graphics g, Font f, StringFormat c)
        {
            RectangleF[] r = DiffButtons();
            string[] labels = { "1  EASY", "2  NORMAL", "3  HARD" };
            for (int i = 0; i < 3; i++)
            {
                using (GraphicsPath gp = Renderer.RoundRect(r[i], 12 * d))
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(i == diff ? 210 : 140, LaneCol[i + 1])))
                { g.FillPath(bb, gp); }
                g.DrawString(labels[i], f, Brushes.White, r[i], c);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            if (music != null) { try { music.Stop(); } catch { } }
            base.OnFormClosed(e);
        }
    }

    public partial class Buddy
    {
        DanceForm danceForm;

        public void StartDanceBattle()
        {
            Touch();
            if (danceForm == null || danceForm.IsDisposed) danceForm = new DanceForm(this);
            danceForm.Show();
            BringForward(danceForm);
            DanceForm f = danceForm;
            RunJob(async delegate (CancellationToken ct)
            {
                BeginGame("dance", false);
                try
                {
                    Say("Dance Battle! Pick easy, normal or hard, then hit the arrows to the beat. I'll dance on top!");
                    await Wait(0.5, ct);
                    if (!f.IsDisposed)
                    {
                        Native.RECT r = Native.GetVisibleRect(f.Handle);
                        await StandOnWindow(f.Handle, r.Width / 2.0, ct);
                    }
                    Facing = 1;
                    while (!f.IsDisposed && f.Visible) await Wait(0.3, ct);
                }
                finally { BaseAct = Act.Idle; EndGame(); }
            });
        }

        // Puts a game window in front of everything for a moment.
        public static async void BringForward(Form f)
        {
            f.TopMost = true;
            Native.ForceForeground(f.Handle);
            f.Activate();
            await Task.Delay(1500);
            if (!f.IsDisposed) f.TopMost = false;
        }

        public void DanceStart() { BaseAct = Act.DanceGroove; ShowBubble(Pick("Let's go!", "Hit it!", "Music!"), 1.2); }

        public void DanceHit(int lane, bool perfect, int combo)
        {
            Act[] moves = { Act.DanceMoon, Act.Squat, Act.Cheer, Act.DanceDisco };
            Act m = moves[lane];
            if (combo >= 20) m = combo % 2 == 0 ? Act.DanceRobot : Act.DanceRunning;
            BaseAct = m;
            if (lane == 0 || lane == 3) Facing = lane == 0 ? -1 : 1;
            if (combo > 0 && combo % 10 == 0) { AddNotes(3); ShowBubble(combo + " combo! Woo!", 1.2); }
            else if (perfect && Rnd.NextDouble() < 0.06) AddNotes(1);
        }

        public void DanceMiss()
        {
            BaseAct = Act.Land;
            if (Rnd.NextDouble() < 0.2) ShowBubble(Pick("Whoops!", "Ack!", "Off beat!"), 0.8);
        }

        public void DanceDone(int score, string grade, bool failed, bool record)
        {
            BaseAct = failed ? Act.Sad : Act.Cheer;
            Brain.Event("you had a Dance Battle with the user: grade " + grade + ", score " + score);
            Brain.Happy = Math.Min(100, Brain.Happy + 15);
            if (record) { Sfx.Play("cheer"); AddStars(5); }
            Say(failed ? "Aww, we lost the beat! Try again?" :
                grade == "S" || grade == "A" ? "WOW! Grade " + grade + "! We're dance legends!" + (record ? " New record!" : "") :
                "Grade " + grade + "! Nice dancing!" + (record ? " New record!" : " Want to go again?"));
        }
    }
}
