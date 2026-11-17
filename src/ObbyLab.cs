using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // The Obby Lab: build an obstacle course, then watch his brain learn it, generation by generation.
    // He starts with a random brain (IQ 0) and learns by evolution: the best tries become the parents of the next ones.
    public class ObbyLab : Form
    {
        enum Mode { Build, Train, Replay }

        class LabCanvas : Panel
        {
            public LabCanvas() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.FromArgb(236, 238, 243); }
        }

        const int LW = 48, LH = 20;
        readonly Buddy b;
        readonly LabCanvas canvas;
        readonly System.Windows.Forms.Timer timer;
        readonly float d;
        Level level;
        Trainer trainer;
        Mode mode = Mode.Build;
        LabAgent replay;
        double replayDone;
        Tile tool = Tile.Block;
        int speed = 1;
        readonly Stopwatch clock = Stopwatch.StartNew();
        string speech = "";
        double speechUntil, lastSpoken = -100, lastAsked = -100;
        bool celebrated;
        readonly Random rnd = new Random();
        readonly List<ToolStripButton> toolButtons = new List<ToolStripButton>(), speedButtons = new List<ToolStripButton>();
        ToolStripButton buildBtn, trainBtn, replayBtn;
        ToolStripTextBox nameBox;
        RectangleF levelRect, brainRect;
        float ts;
        bool painting, erasing;
        Net previewNet;

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        static string Folder { get { return Path.Combine(Log.Folder, "obbies"); } }

        public ObbyLab(Buddy b)
        {
            this.b = b;
            d = (float)b.Dpi;
            Text = b.S.Name + "'s Obby Lab";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size((int)(1280 * d), (int)(780 * d));
            MinimumSize = new Size((int)(900 * d), (int)(560 * d));
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Renderer.MakeIcon(b.S.BodyColor); } catch { }
            LabStats.Load();

            canvas = new LabCanvas();
            canvas.Dock = DockStyle.Fill;
            canvas.Paint += PaintCanvas;
            canvas.MouseDown += CanvasDown;
            canvas.MouseMove += CanvasMove;
            canvas.MouseUp += delegate { painting = false; };
            Controls.Add(canvas);

            ToolStrip tools = new ToolStrip(); tools.GripStyle = ToolStripGripStyle.Hidden; tools.Padding = new Padding(4);
            ToolStrip top = new ToolStrip(); top.GripStyle = ToolStripGripStyle.Hidden; top.Padding = new Padding(4);
            top.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            buildBtn = Btn(top, "Build", delegate { SetMode(Mode.Build); });
            trainBtn = Btn(top, "Teach him!", delegate { SetMode(Mode.Train); });
            replayBtn = Btn(top, "Watch his best run", delegate { SetMode(Mode.Replay); });
            top.Items.Add(new ToolStripSeparator());
            top.Items.Add(new ToolStripLabel("Speed:"));
            foreach (int sp in new[] { 1, 5, 20, 0 })
            {
                int s2 = sp;
                ToolStripButton sb = Btn(top, sp == 0 ? "MAX" : sp + "x", delegate { speed = s2; UpdateButtons(); });
                sb.Tag = sp;
                speedButtons.Add(sb);
            }
            top.Items.Add(new ToolStripSeparator());
            ToolStripButton reset = Btn(top, "Reset his brain", delegate { ResetBrain(); });
            reset.Alignment = ToolStripItemAlignment.Right;

            AddTool(tools, "Block", Tile.Block);
            AddTool(tools, "Lava", Tile.Lava);
            AddTool(tools, "Bounce pad", Tile.Bounce);
            AddTool(tools, "Checkpoint", Tile.Check);
            AddTool(tools, "Start", Tile.Start);
            AddTool(tools, "Finish", Tile.Finish);
            AddTool(tools, "Eraser", Tile.Empty);
            tools.Items.Add(new ToolStripSeparator());
            Btn(tools, "Clear", delegate { if (MessageBox.Show(this, "Clear the whole obby?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes) { NewLevel(new Level(LW, LH)); level.Name = "My obby"; nameBox.Text = level.Name; } });
            ToolStripDropDownButton ex = new ToolStripDropDownButton("Examples");
            for (int i = 0; i < Level.ExampleNames.Length; i++)
            {
                int n = i;
                ex.DropDownItems.Add(Level.ExampleNames[i], null, delegate { NewLevel(Level.Example(n, LW, LH)); nameBox.Text = level.Name; });
            }
            tools.Items.Add(ex);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("Name:"));
            nameBox = new ToolStripTextBox(); nameBox.Width = (int)(150 * d);
            tools.Items.Add(nameBox);
            Btn(tools, "Save", delegate { SaveLevel(); });
            ToolStripDropDownButton open = new ToolStripDropDownButton("Open");
            open.DropDownOpening += delegate
            {
                open.DropDownItems.Clear();
                try
                {
                    if (Directory.Exists(Folder))
                        foreach (string f in Directory.GetFiles(Folder, "*.obby"))
                        {
                            string path = f;
                            open.DropDownItems.Add(Path.GetFileNameWithoutExtension(f), null, delegate { LoadLevel(path); });
                        }
                }
                catch { }
                if (open.DropDownItems.Count == 0) open.DropDownItems.Add("(no saved obbies yet)").Enabled = false;
            };
            tools.Items.Add(open);

            Controls.Add(tools);
            Controls.Add(top);

            NewLevel(Level.Example(0, LW, LH));
            nameBox.Text = level.Name;
            UpdateButtons();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += Tick;
            timer.Start();
            Say(StageLine("hello"), true);
        }

        ToolStripButton Btn(ToolStrip s, string text, EventHandler click)
        {
            ToolStripButton bt = new ToolStripButton(text);
            bt.Click += click;
            bt.Margin = new Padding(2, 1, 2, 1);
            s.Items.Add(bt);
            return bt;
        }

        void AddTool(ToolStrip s, string text, Tile t)
        {
            ToolStripButton bt = Btn(s, text, delegate { tool = t; if (mode != Mode.Build) SetMode(Mode.Build); UpdateButtons(); });
            bt.Tag = t;
            bt.Image = TileIcon(t);
            toolButtons.Add(bt);
        }

        Bitmap TileIcon(Tile t)
        {
            Bitmap bm = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bm))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawTile(g, t, new RectangleF(1, 1, 14, 14), 0);
            }
            return bm;
        }

        void UpdateButtons()
        {
            foreach (ToolStripButton bt in toolButtons) bt.Checked = (Tile)bt.Tag == tool;
            foreach (ToolStripButton bt in speedButtons) bt.Checked = (int)bt.Tag == speed;
            buildBtn.Checked = mode == Mode.Build; trainBtn.Checked = mode == Mode.Train; replayBtn.Checked = mode == Mode.Replay;
        }

        // ---------- obbies ----------
        void NewLevel(Level l)
        {
            level = l;
            level.ComputeDist();
            trainer = null; replay = null; celebrated = false;
            if (mode != Mode.Build) { mode = Mode.Build; UpdateButtons(); }
            canvas.Invalidate();
        }

        void SaveLevel()
        {
            string n = Regex.Replace(nameBox.Text.Trim(), @"[^\w \-]", "");
            if (n.Length == 0) n = "My obby";
            try
            {
                Directory.CreateDirectory(Folder);
                level.Name = n;
                File.WriteAllText(Path.Combine(Folder, n + ".obby"), level.Serialize());
                Say("Saved \"" + n + "\"!", false);
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save: " + ex.Message, Text); }
        }

        void LoadLevel(string path)
        {
            try
            {
                Level l = Level.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
                if (l.W != LW || l.H != LH)
                {
                    Level fit = new Level(LW, LH);
                    for (int x = 0; x < Math.Min(LW, l.W); x++) for (int y = 0; y < Math.Min(LH, l.H); y++) fit.T[x, y] = l.T[x, y];
                    fit.Name = l.Name; l = fit;
                }
                NewLevel(l);
                nameBox.Text = l.Name;
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't open it: " + ex.Message, Text); }
        }

        void ResetBrain()
        {
            if (MessageBox.Show(this, "Reset " + b.S.Name + "'s brain back to IQ 0? He forgets everything he learned in the lab.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            LabStats.ResetBrain();
            trainer = null; replay = null; celebrated = false;
            SetMode(Mode.Build);
            Say(StageLine("hello"), true);
        }

        void SetMode(Mode m)
        {
            if (m == Mode.Train)
            {
                int sx, sy, fx, fy;
                if (!level.Find(Tile.Start, out sx, out sy) || !level.Find(Tile.Finish, out fx, out fy))
                {
                    MessageBox.Show(this, "Put a Start and a Finish in your obby first!", Text);
                    return;
                }
                level.ComputeDist();
                if (trainer == null || trainer.L != level) { trainer = new Trainer(level, LabStats.Brain); celebrated = false; }
                Say(StageLine("start"), true);
            }
            if (m == Mode.Replay)
            {
                Net best = trainer != null && trainer.BestNet != null ? trainer.BestNet : LabStats.Brain;
                if (best == null) { MessageBox.Show(this, "He hasn't learned anything yet. Press \"Teach him!\" first.", Text); return; }
                level.ComputeDist();
                replay = new LabAgent(best, level); replayDone = 0;
            }
            mode = m;
            UpdateButtons();
            canvas.Invalidate();
        }

        // ---------- the clock ----------
        void Tick(object sender, EventArgs e)
        {
            try
            {
                if (mode == Mode.Train && trainer != null)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    int steps = speed == 0 ? int.MaxValue : speed;
                    for (int i = 0; i < steps; i++)
                    {
                        if (trainer.Step(1 / 60.0)) { GenerationOver(); if (mode != Mode.Train) break; }
                        if (speed == 0 && sw.ElapsedMilliseconds > 12) break;
                    }
                }
                else if (mode == Mode.Replay && replay != null)
                {
                    if (!replay.Done) replay.Step(level, 1 / 60.0, LabStats.IQ >= LabStats.ParkourIQ, 60);
                    else if (replayDone == 0) replayDone = Now;
                    else if (Now - replayDone > 2) { replay = new LabAgent(replay.Brain, level); replayDone = 0; }
                }
                canvas.Invalidate();
            }
            catch (Exception ex) { Log.Write("lab tick: " + ex); }
        }

        void GenerationOver()
        {
            string what = trainer.LastWhat;
            double p = trainer.History.Count > 0 ? trainer.History[trainer.History.Count - 1] : 0;
            if (what == "finished" && !celebrated)
            {
                celebrated = true;
                bool first = LabStats.OnBeaten(level, trainer.BestNet);
                b.Brain.Event("you learned the obby \"" + level.Name + "\" in your lab after " + trainer.Gen + " generations");
                b.Brain.Happy = Math.Min(100, b.Brain.Happy + 20);
                Say(StageLine("won"), true);
                if (first) b.AddStars(5);
                b.FlashAct(Act.Cheer, 2.5);
                // show off the winning run
                mode = Mode.Replay;
                replay = new LabAgent(trainer.BestNet.Clone(), level); replayDone = 0;
                UpdateButtons();
                AskSmartLine("You just beat the obby \"" + level.Name + "\" in your Obby Lab after " + trainer.Gen + " generations of learning by trial and error! Celebrate in one short sentence.");
                return;
            }
            if (trainer.Gen % 3 == 1 || Now - lastSpoken > 12)
            {
                string kind = what == "lava" ? "lava" : what == "fell" ? "fell" : p > 0.05 && trainer.StallGens == 0 ? "better" : "stuck";
                Say(StageLine(kind), Now - lastSpoken > 7);
                if (LabStats.Stage >= 3 && Now - lastAsked > 25 && trainer.Gen % 4 == 0)
                    AskSmartLine("You're learning the obstacle course \"" + level.Name + "\" in your Obby Lab with an evolving neural-network brain. Generation " + trainer.Gen +
                                 ": your best try got " + (int)(p * 100) + "% of the way and then " + (what == "lava" ? "touched lava" : what == "fell" ? "fell down a hole" : "got stuck") + ". Say one short, funny line about how it's going.");
            }
        }

        async void AskSmartLine(string instruction)
        {
            if (LabStats.Stage < 3 || b.S.EffectiveApiKey.Length == 0) return;
            lastAsked = Now;
            string line = await b.Brain.AskLine(instruction);
            if (!IsDisposed && !string.IsNullOrEmpty(line)) Say(line, true);
        }

        void Say(string text, bool voice)
        {
            speech = text;
            speechUntil = Now + 4 + text.Length * 0.05;
            if (voice && !b.S.Muted && !Buddy.TestMode) { lastSpoken = Now; b.Say(text); }
        }

        // What he says depends on how smart he is: babbling, then words, then sentences.
        string StageLine(string kind)
        {
            int st = LabStats.Stage;
            string[] baby = { "ba ba!", "goo?", "ga ga!", "bleh!", "wawa!", "eee!", "buh?", "da!" };
            switch (st)
            {
                case 0:
                    if (kind == "lava") return Pick("waaah!", "ow ow!", "eee! hot!");
                    if (kind == "won") return Pick("YAAA! ba ba!", "goo goo YAY!");
                    return Pick(baby);
                case 1:
                    switch (kind)
                    {
                        case "hello": return Pick("hi! lab!", "me learn!", "jump? jump!");
                        case "start": return Pick("go go!", "me try!", "run!");
                        case "lava": return Pick("hot!", "ouchie!", "lava bad!");
                        case "fell": return Pick("uh oh!", "fall down!", "whoops!");
                        case "better": return Pick("more! more!", "far!", "good!");
                        case "won": return Pick("flag! yay!", "me did it!", "win! win!");
                    }
                    return Pick("hmm?", "stuck...", "again!");
                case 2:
                    switch (kind)
                    {
                        case "hello": return Pick("My lab! Build me something hard!", "I can wall jump now. Try me!");
                        case "start": return Pick("Okay, let me try.", "Watch me learn!");
                        case "lava": return Pick("Lava is hot. Must jump!", "Ouch, lava again.");
                        case "fell": return Pick("That gap is big...", "I fell in the hole.");
                        case "better": return Pick("I got further!", "I'm getting it!");
                        case "won": return Pick("I did it! I learned it!", "Parkour master!");
                    }
                    return Pick("Hmm, I'm stuck.", "Let me think...");
                default:
                    switch (kind)
                    {
                        case "hello": return Pick("Welcome to my lab! Build an obby and I'll figure it out.", "Ready to learn. Make it tricky!");
                        case "start": return Pick("Alright, brain, let's do this.", "Starting from what I already know...");
                        case "lava": return "Generation " + Gen + ". Note to self: lava is still hot.";
                        case "fell": return "Generation " + Gen + ". Gravity wins again. I'll jump earlier.";
                        case "better": return "Generation " + Gen + ": " + (int)(Best * 100) + "% of the way. Getting smarter!";
                        case "won": return Pick("Yes! Course complete! My brain is growing!", "Done it! That one took " + Gen + " generations.");
                    }
                    return "Generation " + Gen + ". Hmm, I keep getting stuck. Trying something new.";
            }
        }

        int Gen { get { return trainer != null ? trainer.Gen : 0; } }
        double Best { get { return trainer != null ? trainer.BestProgress : 0; } }
        string Pick(params string[] s) { return s[rnd.Next(s.Length)]; }

        // ---------- editing ----------
        Point Cell(Point p)
        {
            if (ts <= 0) return new Point(-1, -1);
            return new Point((int)Math.Floor((p.X - levelRect.X) / ts), (int)Math.Floor((p.Y - levelRect.Y) / ts));
        }

        void CanvasDown(object sender, MouseEventArgs e)
        {
            if (!levelRect.Contains(e.Location)) return;
            if (mode != Mode.Build) SetMode(Mode.Build);
            painting = true;
            erasing = e.Button == MouseButtons.Right;
            PaintAt(e.Location);
        }

        void CanvasMove(object sender, MouseEventArgs e)
        {
            if (painting && (e.Button & (MouseButtons.Left | MouseButtons.Right)) != 0) PaintAt(e.Location);
        }

        void PaintAt(Point p)
        {
            Point c = Cell(p);
            if (c.X < 0 || c.Y < 0 || c.X >= LW || c.Y >= LH) return;
            Tile t = erasing ? Tile.Empty : tool;
            if (level.T[c.X, c.Y] == t) return;
            level.Set(c.X, c.Y, t);
            level.ComputeDist();
            trainer = null; replay = null; celebrated = false;
            canvas.Invalidate();
        }

        // ---------- drawing ----------
        static void DrawTile(Graphics g, Tile t, RectangleF r, double time)
        {
            switch (t)
            {
                case Tile.Block:
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(55, 58, 68))) g.FillRectangle(br, r);
                    using (Pen p = new Pen(Color.FromArgb(90, 95, 110), Math.Max(1, r.Width / 12))) g.DrawRectangle(p, r.X + r.Width * 0.08f, r.Y + r.Height * 0.08f, r.Width * 0.84f, r.Height * 0.84f);
                    break;
                case Tile.Lava:
                    using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(255, 200, 40), Color.FromArgb(230, 60, 20), 90f))
                        g.FillRectangle(lg, r);
                    using (Pen p = new Pen(Color.FromArgb(160, 255, 240, 160), Math.Max(1, r.Width / 10)))
                    {
                        float w = (float)Math.Sin(time * 3 + r.X * 0.3) * r.Height * 0.12f;
                        g.DrawBezier(p, r.X, r.Y + r.Height * 0.3f + w, r.X + r.Width * 0.33f, r.Y + r.Height * 0.15f, r.X + r.Width * 0.66f, r.Y + r.Height * 0.45f, r.Right, r.Y + r.Height * 0.3f - w);
                    }
                    break;
                case Tile.Bounce:
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(55, 58, 68))) g.FillRectangle(br, r.X, r.Y + r.Height * 0.4f, r.Width, r.Height * 0.6f);
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(60, 200, 90))) g.FillRectangle(gb, r.X, r.Y + r.Height * 0.15f, r.Width, r.Height * 0.3f);
                    using (Pen p = new Pen(Color.White, Math.Max(1, r.Width / 9)))
                    {
                        g.DrawLine(p, r.X + r.Width / 2, r.Y + r.Height * 0.9f, r.X + r.Width / 2, r.Y + r.Height * 0.5f);
                        g.DrawLine(p, r.X + r.Width * 0.3f, r.Y + r.Height * 0.68f, r.X + r.Width / 2, r.Y + r.Height * 0.5f);
                        g.DrawLine(p, r.X + r.Width * 0.7f, r.Y + r.Height * 0.68f, r.X + r.Width / 2, r.Y + r.Height * 0.5f);
                    }
                    break;
                case Tile.Check:
                case Tile.Finish:
                case Tile.Start:
                    {
                        Color c = t == Tile.Check ? Color.FromArgb(60, 140, 240) : t == Tile.Start ? Color.FromArgb(60, 190, 90) : Color.Black;
                        using (Pen pole = new Pen(Color.FromArgb(90, 90, 100), Math.Max(1, r.Width / 9)))
                            g.DrawLine(pole, r.X + r.Width * 0.25f, r.Bottom, r.X + r.Width * 0.25f, r.Y);
                        RectangleF flag = new RectangleF(r.X + r.Width * 0.28f, r.Y + r.Height * 0.02f, r.Width * 0.62f, r.Height * 0.45f);
                        if (t == Tile.Finish)
                        {
                            float cw = flag.Width / 3, ch = flag.Height / 2;
                            for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++)
                                    g.FillRectangle((i + j) % 2 == 0 ? Brushes.Black : Brushes.White, flag.X + i * cw, flag.Y + j * ch, cw, ch);
                            g.DrawRectangle(Pens.Black, flag.X, flag.Y, flag.Width, flag.Height);
                        }
                        else using (SolidBrush fb = new SolidBrush(c)) g.FillRectangle(fb, flag);
                        break;
                    }
            }
        }

        void PaintCanvas(object sender, PaintEventArgs e) { Draw(e.Graphics, canvas.ClientSize); }

        // For testing: train a little with no window on screen and return a picture of the lab.
        public Bitmap Snapshot(int w, int h, int example, int steps)
        {
            NewLevel(Level.Example(example, LW, LH));
            SetMode(Mode.Train);
            for (int i = 0; i < steps && trainer != null; i++)
                if (trainer.Step(1 / 60.0)) { GenerationOver(); if (mode != Mode.Train) break; }
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(236, 238, 243));
                Draw(g, new Size(w, h));
            }
            return bmp;
        }

        void Draw(Graphics g, Size sz)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float panelW = 330 * d, pad = 10 * d;
            ts = Math.Min((sz.Width - panelW - pad * 3) / LW, (sz.Height - pad * 2) / LH);
            if (ts < 4) ts = 4;
            levelRect = new RectangleF(pad, pad + Math.Max(0, (sz.Height - pad * 2 - ts * LH) / 2), ts * LW, ts * LH);
            brainRect = new RectangleF(levelRect.Right + pad, pad, sz.Width - levelRect.Right - pad * 2, sz.Height - pad * 2);
            double time = Now;

            // sky + grid
            using (LinearGradientBrush sky = new LinearGradientBrush(levelRect, Color.FromArgb(205, 230, 255), Color.FromArgb(250, 252, 255), 90f)) g.FillRectangle(sky, levelRect);
            using (Pen grid = new Pen(Color.FromArgb(28, 0, 0, 60)))
            {
                for (int x = 0; x <= LW; x++) g.DrawLine(grid, levelRect.X + x * ts, levelRect.Y, levelRect.X + x * ts, levelRect.Bottom);
                for (int y = 0; y <= LH; y++) g.DrawLine(grid, levelRect.X, levelRect.Y + y * ts, levelRect.Right, levelRect.Y + y * ts);
            }
            for (int x = 0; x < LW; x++)
                for (int y = 0; y < LH; y++)
                    if (level.T[x, y] != Tile.Empty) DrawTile(g, level.T[x, y], new RectangleF(levelRect.X + x * ts, levelRect.Y + y * ts, ts, ts), time);
            using (Pen border = new Pen(Color.FromArgb(40, 40, 50), 2 * d)) g.DrawRectangle(border, levelRect.X, levelRect.Y, levelRect.Width, levelRect.Height);

            // the stickmen
            LabAgent leader = null;
            float u = (float)(ts * LabAgent.HT / 100.0);
            Color red = b.S.BodyColor;
            if (mode == Mode.Train && trainer != null)
            {
                leader = trainer.Leader();
                foreach (LabAgent a in trainer.Pop)
                    if (a != leader && !a.Dead) DrawAgent(g, a, Color.FromArgb(70, red), u, false);
                if (leader != null) DrawAgent(g, leader, red, u, true);
            }
            else if (mode == Mode.Replay && replay != null) { leader = replay; DrawAgent(g, replay, red, u, true); }
            else
            {
                int sx, sy;
                if (level.Find(Tile.Start, out sx, out sy))
                {
                    if (previewNet == null) previewNet = Net.Random(rnd);
                    LabAgent ghost = new LabAgent(previewNet, level);
                    ghost.Grounded = true;
                    leader = ghost;
                    DrawAgent(g, ghost, red, u, true);
                }
            }
            if (leader != null && speech.Length > 0 && time < speechUntil)
            {
                float hx = levelRect.X + (float)leader.X * ts, hy = levelRect.Y + (float)(leader.Y - LabAgent.HT) * ts - 8 * d;
                using (Font f = new Font("Segoe UI", 12 * d, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    SizeF tsz = g.MeasureString(speech, f, (int)(240 * d));
                    float bw = tsz.Width + 22 * d, bh = tsz.Height + 14 * d;
                    float bx = Math.Max(levelRect.X + 2, Math.Min(levelRect.Right - bw - 2, hx - bw / 2));
                    float by = Math.Max(levelRect.Y + 2, hy - bh - 10 * d);
                    Renderer.DrawBubble(g, new RectangleF(bx, by, bw, bh), new PointF(hx, hy), speech, f, d * 0.75f, 255);
                }
            }

            if (mode == Mode.Build)
            {
                using (Font f = new Font("Segoe UI", 11 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(150, 30, 30, 50)))
                    g.DrawString("BUILD MODE: left-click to place, right-click to erase. Then press \"Teach him!\"", f, tb, levelRect.X + 8 * d, levelRect.Bottom - 22 * d);
            }
            DrawBrain(g, leader);
        }

        void DrawAgent(Graphics g, LabAgent a, Color c, float u, bool main)
        {
            Act act = a.Finished ? Act.Cheer : !a.Grounded ? (a.VY < 0 ? Act.Jump : Act.Fall) : Math.Abs(a.VX) > 1.5 ? Act.Run : Act.Idle;
            if (!a.Grounded && (a.WallL || a.WallR) && LabStats.IQ >= LabStats.ParkourIQ) act = Act.Climb;
            Pose p = Poses.Get(act, a.AnimT, 0);
            float px = levelRect.X + (float)a.X * ts, py = levelRect.Y + (float)a.Y * ts;
            int face = a.Facing;
            if (act == Act.Climb) face = a.WallR ? 1 : -1;
            Joints j = Renderer.Compute(p, px, py, face, u);
            Renderer.DrawFigure(g, j, face, u, c, Prop.None, a.AnimT, main);
        }

        void DrawBrain(Graphics g, LabAgent who)
        {
            RectangleF r = brainRect;
            using (GraphicsPath gp = Renderer.RoundRect(r, 12 * d))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 30, 40)))
                g.FillPath(bg, gp);
            float x = r.X + 16 * d, y = r.Y + 14 * d, w = r.Width - 32 * d;
            using (Font title = new Font("Segoe UI", 15 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font big = new Font("Segoe UI", 34 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font f = new Font("Segoe UI", 12 * d, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", 10.5f * d, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush grey = new SolidBrush(Color.FromArgb(170, 175, 195)))
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(255, 210, 70)))
            {
                g.DrawString(b.S.Name + "'s brain", title, white, x, y); y += 26 * d;
                g.DrawString("IQ", f, grey, x, y + 14 * d);
                g.DrawString(((int)LabStats.IQ).ToString(), big, accent, x + 22 * d, y - 2 * d);
                g.DrawString("Stage: " + LabStats.StageName, f, white, x + 110 * d, y + 6 * d);
                g.DrawString(LabStats.Beaten + " obbies beaten, " + LabStats.Gens + " generations", small, grey, x + 110 * d, y + 24 * d);
                y += 52 * d;

                // progress to the next stage
                double[] marks = { 0, LabStats.WordsIQ, LabStats.ParkourIQ, LabStats.TalkIQ, 200 };
                string[] next = { "talks in words at IQ 20", "learns parkour (wall jumps) at IQ 50", "talks in full sentences at IQ 90", "keeps getting smarter" };
                int st = LabStats.Stage;
                double k = Math.Max(0, Math.Min(1, (LabStats.IQ - marks[st]) / (marks[st + 1] - marks[st])));
                RectangleF bar = new RectangleF(x, y, w, 10 * d);
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(60, 64, 80))) g.FillRectangle(bb, bar);
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(120, 200, 120))) g.FillRectangle(fb, bar.X, bar.Y, (float)(bar.Width * k), bar.Height);
                y += 14 * d;
                g.DrawString("Next: " + next[st], small, grey, x, y); y += 22 * d;

                // this run
                string state = mode == Mode.Train ? "Learning" : mode == Mode.Replay ? "Showing his best run" : "Waiting for you to build";
                g.DrawString(state, f, white, x, y); y += 20 * d;
                if (trainer != null)
                {
                    g.DrawString("Generation " + trainer.Gen + "    trying now: " + trainer.Alive + "/" + trainer.PopSize, small, grey, x, y); y += 17 * d;
                    g.DrawString("Best so far: " + (trainer.EverFinished ? "FINISHED!" : (int)(trainer.BestProgress * 100) + "% of the way"), small, trainer.EverFinished ? accent : grey, x, y); y += 22 * d;
                    // graph of the best try per generation
                    RectangleF gr = new RectangleF(x, y, w, 70 * d);
                    using (SolidBrush gbk = new SolidBrush(Color.FromArgb(40, 44, 58))) g.FillRectangle(gbk, gr);
                    List<double> h = trainer.History;
                    int n = Math.Min(60, h.Count);
                    if (n > 1)
                    {
                        PointF[] pts = new PointF[n];
                        for (int i = 0; i < n; i++) pts[i] = new PointF(gr.X + gr.Width * i / (n - 1), gr.Bottom - (float)h[h.Count - n + i] * (gr.Height - 4 * d) - 2 * d);
                        using (Pen lp = new Pen(Color.FromArgb(120, 200, 255), 2 * d)) g.DrawLines(lp, pts);
                    }
                    g.DrawString("how far the best try got, each generation", small, grey, gr.X + 4 * d, gr.Y + 2 * d);
                    y += 78 * d;
                }
                else
                {
                    g.DrawString("Each generation " + 40 + " little " + b.S.Name + "s try the obby.\nThe ones who get furthest pass their brain on.\nLittle changes (mutations) make new ideas.", small, grey, x, y);
                    y += 60 * d;
                }

                // the neural network itself
                if (who != null && who.Brain != null && y + 150 * d < r.Bottom) DrawNet(g, who, new RectangleF(x, y + 4 * d, w, Math.Min(r.Bottom - y - 30 * d, 230 * d)), small, grey);
                g.DrawString(LabStats.IQ >= LabStats.ParkourIQ ? "Parkour unlocked: he can wall jump!" : "Wall jumps are locked until IQ 50", small, LabStats.IQ >= LabStats.ParkourIQ ? accent : grey, x, r.Bottom - 22 * d);
            }
        }

        void DrawNet(Graphics g, LabAgent a, RectangleF r, Font small, Brush grey)
        {
            Net n = a.Brain;
            g.DrawString("What he sees  ->  his brain  ->  what he presses", small, grey, r.X, r.Y);
            float top = r.Y + 18 * d, cell = Math.Min(13 * d, (r.Height - 30 * d) / (Net.Rows + 2));
            // inputs: the tiles around him
            PointF[] inPos = new PointF[Net.NIn];
            int k = 0;
            for (int row = 0; row < Net.Rows; row++)
                for (int col = 0; col < Net.Cols; col++)
                {
                    RectangleF c = new RectangleF(r.X + col * cell, top + row * cell, cell - 1, cell - 1);
                    double v = a.In[k];
                    Color cc = v >= 0.9 ? Color.FromArgb(160, 165, 180) : v <= -0.9 ? Color.FromArgb(255, 110, 40) : v > 0.5 ? Color.FromArgb(70, 210, 100) : v > 0.3 ? Color.FromArgb(255, 220, 60) : Color.FromArgb(60, 64, 80);
                    using (SolidBrush cb = new SolidBrush(cc)) g.FillRectangle(cb, c);
                    if (row == 3 && col == 2) using (Pen me = new Pen(Color.FromArgb(230, 40, 40), 1.5f * d)) g.DrawRectangle(me, c.X, c.Y - cell, c.Width, c.Height);
                    inPos[k++] = new PointF(c.Right, c.Y + c.Height / 2);
                }
            for (int i = k; i < Net.NIn; i++) inPos[i] = new PointF(r.X + Net.Cols * cell, top + Net.Rows * cell + (i - k) * 2);
            float hx = r.X + r.Width * 0.58f, ox = r.Right - 40 * d;
            float hs = (r.Height - 30 * d) / Net.NHid;
            PointF[] hPos = new PointF[Net.NHid];
            for (int h = 0; h < Net.NHid; h++) hPos[h] = new PointF(hx, top + h * hs + hs / 2);
            string[] names = { "<-", "->", "jump" };
            PointF[] oPos = new PointF[Net.NOut];
            for (int o = 0; o < Net.NOut; o++) oPos[o] = new PointF(ox, top + (o + 0.5f) * (r.Height - 30 * d) / Net.NOut);

            // strongest connections only, so it stays readable
            List<KeyValuePair<double, int>> strong = new List<KeyValuePair<double, int>>();
            for (int h = 0; h < Net.NHid; h++) for (int i = 0; i < Net.NIn; i++) strong.Add(new KeyValuePair<double, int>(Math.Abs(n.W1(i, h) * a.In[i]), h * Net.NIn + i));
            strong.Sort(delegate (KeyValuePair<double, int> p, KeyValuePair<double, int> q) { return q.Key.CompareTo(p.Key); });
            for (int s = 0; s < Math.Min(45, strong.Count); s++)
            {
                int h = strong[s].Value / Net.NIn, i = strong[s].Value % Net.NIn;
                double wv = n.W1(i, h) * a.In[i];
                int al = (int)Math.Min(200, 40 + strong[s].Key * 90);
                using (Pen p = new Pen(wv > 0 ? Color.FromArgb(al, 90, 220, 120) : Color.FromArgb(al, 255, 90, 90), 1f * d)) g.DrawLine(p, inPos[i], hPos[h]);
            }
            for (int h = 0; h < Net.NHid; h++)
                for (int o = 0; o < Net.NOut; o++)
                {
                    double wv = n.W2(h, o) * n.Hid[h];
                    int al = (int)Math.Min(220, 30 + Math.Abs(wv) * 120);
                    using (Pen p = new Pen(wv > 0 ? Color.FromArgb(al, 90, 220, 120) : Color.FromArgb(al, 255, 90, 90), 1.2f * d)) g.DrawLine(p, hPos[h], oPos[o]);
                }
            for (int h = 0; h < Net.NHid; h++)
            {
                int v = (int)(127 + n.Hid[h] * 120);
                using (SolidBrush nb = new SolidBrush(Color.FromArgb(v, v, 255))) g.FillEllipse(nb, hPos[h].X - 5 * d, hPos[h].Y - 5 * d, 10 * d, 10 * d);
            }
            bool[] on = { a.KeyL, a.KeyR, a.KeyJ };
            for (int o = 0; o < Net.NOut; o++)
            {
                using (SolidBrush ob = new SolidBrush(on[o] ? Color.FromArgb(255, 210, 70) : Color.FromArgb(70, 74, 90))) g.FillEllipse(ob, oPos[o].X - 8 * d, oPos[o].Y - 8 * d, 16 * d, 16 * d);
                g.DrawString(names[o], small, on[o] ? Brushes.White : grey, oPos[o].X + 10 * d, oPos[o].Y - 8 * d);
            }
        }

        public void TestTrain(int sp) { speed = sp; SetMode(Mode.Train); UpdateButtons(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            LabStats.Save();
            base.OnFormClosed(e);
        }
    }

    // ---------- the stickman side of the lab, and a few helpers for his brain ----------
    public partial class Buddy
    {
        ObbyLab lab;
        public bool LabOpen { get { return lab != null && !lab.IsDisposed && lab.Visible; } }
        public void TestLabTrain(int sp) { if (LabOpen) lab.TestTrain(sp); }
        public string YtTitle { get { return WatchingYt ? ytTitle : ""; } }

        public void OpenLab()
        {
            Touch();
            if (lab == null || lab.IsDisposed) lab = new ObbyLab(this);
            lab.Show();
            lab.WindowState = FormWindowState.Normal;
            // Windows doesn't let background apps jump to the front by themselves, so nudge it
            BringLabForward(lab);
            ObbyLab myLab = lab;
            RunJob(async delegate (CancellationToken ct)
            {
                await Wait(0.6, ct);
                if (myLab.IsDisposed) return;
                Native.RECT r = Native.GetVisibleRect(myLab.Handle);
                await StandOnWindow(myLab.Handle, r.Width - 90 * U, ct);
                Facing = -1;
                BaseAct = Act.Watch;
                while (!myLab.IsDisposed && myLab.Visible) await Wait(0.5, ct);
                BaseAct = Act.Idle;
            });
        }

        static async void BringLabForward(ObbyLab l)
        {
            l.TopMost = true;
            Native.ForceForeground(l.Handle);
            l.Activate();
            await Task.Delay(1500);
            if (!l.IsDisposed) l.TopMost = false;
        }

        // The app you're using, by its friendly name (only the app, never the window title).
        public string ForegroundAppName()
        {
            IntPtr h = LastExternalWindow;
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return "";
            string p = (Native.ProcessName(h) ?? "").ToLowerInvariant();
            switch (p)
            {
                case "chrome": return "Google Chrome";
                case "msedge": return "Microsoft Edge";
                case "firefox": return "Firefox";
                case "explorer": return "File Explorer";
                case "notepad": return "Notepad";
                case "code": return "VS Code";
                case "robloxplayerbeta": return "Roblox";
                case "javaw": return "Minecraft (probably)";
                case "minecraft.windows": return "Minecraft";
                case "spotify": return "Spotify";
                case "discord": return "Discord";
                case "winword": return "Word";
                case "excel": return "Excel";
                case "powerpnt": return "PowerPoint";
                case "steam": return "Steam";
                case "": return "";
            }
            return p;
        }

        // You're pasting a key into his settings: he notices!
        double lastKeyReact = -100;
        public void OnKeyTyped(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length < 25 || key == S.ApiKey || Now - lastKeyReact < 8) return;
            lastKeyReact = Now;
            Touch();
            string kind = Settings.IsClaudeKey(key) ? "Claude" : "Gemini";
            RunJob(async delegate (CancellationToken ct)
            {
                await WaitGround(ct);
                Facing = Cursor.Position.X >= X ? 1 : -1;
                BaseAct = Act.Scared;
                Say(Pick("Whoa whoa whoa! Is that... a BRAIN?! A " + kind + " brain?!", "Wait... what's that? A " + kind + " key?! For ME?!", "Is that a " + kind + " key? My head is tingling!"));
                AddStars(3);
                await Wait(1.6, ct);
                BaseAct = Act.Cheer;
                await Wait(1.2, ct);
                BaseAct = Act.Idle;
                ShowBubble("Press Save and plug it into my head!", 4);
            });
        }
    }
}
