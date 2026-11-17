using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace StickBuddy
{
    // A flying game object (balloon, dodgeball...) that lives in its own little see-through window.
    public class Sprite
    {
        public const int Balloon = 0, Ball = 1;
        public int Kind;
        public double X, Y, VX, VY, R, Phase, Age, Life = 999, BaseY;
        public Color Col;
        public bool Gold, Dead, Hit;
        public int Bounces;
        public SpriteForm Win;
    }

    public class SpriteForm : Form
    {
        readonly bool clickable;
        Bitmap canvas;
        int w, h;

        public SpriteForm(int width, int height, bool clickable)
        {
            this.clickable = clickable;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-10000, -10000);
            Text = "Stick Buddy game";
            w = width; h = height;
            canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000;
                if (!clickable) cp.ExStyle |= 0x20;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Draw(Sprite s, double now)
        {
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                Paint(g, s, now, w, h);
            }
            int wy = s.Kind == Sprite.Balloon ? (int)Math.Round(s.Y - s.R * 1.15) : (int)Math.Round(s.Y - h / 2.0);
            Native.UpdateLayered(Handle, canvas, (int)Math.Round(s.X - w / 2.0), wy);
        }

        // Draws a balloon or a ball into a w x h picture.
        public static void Paint(Graphics g, Sprite s, double now, int w, int h)
        {
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = w / 2f, cy = s.Kind == Sprite.Balloon ? (float)(s.R * 1.15) : h / 2f, r = (float)s.R;
                if (s.Kind == Sprite.Balloon)
                {
                    Color c = s.Gold ? Buddy.FromHsv((now * 120) % 360, 0.55, 1) : s.Col;
                    using (Pen str = new Pen(Color.FromArgb(160, 80, 80, 90), Math.Max(1, r / 18)))
                    {
                        PointF[] pts = new PointF[8];
                        for (int i = 0; i < 8; i++) pts[i] = new PointF(cx + (float)Math.Sin(now * 3 + s.Phase + i * 0.7) * r * 0.12f, cy + r + i * r * 0.22f);
                        g.DrawCurve(str, pts);
                    }
                    RectangleF body = new RectangleF(cx - r * 0.86f, cy - r, r * 1.72f, r * 2);
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddEllipse(body);
                        using (PathGradientBrush pg = new PathGradientBrush(gp))
                        {
                            pg.CenterPoint = new PointF(cx - r * 0.3f, cy - r * 0.35f);
                            pg.CenterColor = Color.FromArgb(255, Math.Min(255, c.R + 90), Math.Min(255, c.G + 90), Math.Min(255, c.B + 90));
                            pg.SurroundColors = new[] { c };
                            g.FillPath(pg, gp);
                        }
                    }
                    using (SolidBrush kb = new SolidBrush(c))
                        g.FillPolygon(kb, new[] { new PointF(cx - r * 0.14f, cy + r * 1.08f), new PointF(cx + r * 0.14f, cy + r * 1.08f), new PointF(cx, cy + r * 0.95f) });
                    using (SolidBrush hl = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                        g.FillEllipse(hl, cx - r * 0.5f, cy - r * 0.7f, r * 0.32f, r * 0.45f);
                    if (s.Gold)
                        using (Font f = new Font("Segoe UI", r * 0.7f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("x3", f, Brushes.White, new RectangleF(cx - r, cy - r, r * 2, r * 2), sf);
                        }
                }
                else
                {
                    using (SolidBrush sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(sh, cx - r + 2, cy - r + 3, r * 2, r * 2);
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        using (PathGradientBrush pg = new PathGradientBrush(gp))
                        {
                            pg.CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.35f);
                            pg.CenterColor = Color.FromArgb(255, 255, 140, 140);
                            pg.SurroundColors = new[] { s.Col };
                            g.FillPath(pg, gp);
                        }
                    }
                    using (Pen st = new Pen(Color.FromArgb(120, 255, 255, 255), r / 6))
                        g.DrawArc(st, cx - r * 0.75f, cy - r * 0.75f, r * 1.5f, r * 1.5f, (float)(s.Age * 400) % 360, 140);
                }
            }
        }
    }

    // The scoreboard at the top of the screen during a game (you can click through it).
    public class HudForm : Form
    {
        Bitmap canvas;
        readonly float d;
        string title = "", main = "", side = "";

        public HudForm(double dpi)
        {
            d = (float)dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-10000, -10000);
            Text = "Stick Buddy score";
            canvas = new Bitmap((int)(460 * d), (int)(70 * d), PixelFormat.Format32bppArgb);
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000; return cp; }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Set(string t, string m, string s)
        {
            if (t == title && m == main && s == side && Visible) return;
            title = t; main = m; side = s;
            if (!Visible) Show();
            using (Graphics g = Graphics.FromImage(canvas))
            using (Font tf = new Font("Segoe UI", 13 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font mf = new Font("Segoe UI", 26 * d, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font sf = new Font("Segoe UI", 13 * d, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat c = new StringFormat())
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                RectangleF r = new RectangleF(2, 2, canvas.Width - 4, canvas.Height - 4);
                using (GraphicsPath gp = Renderer.RoundRect(r, 16 * d))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(225, 24, 26, 36)))
                using (Pen bp = new Pen(Color.FromArgb(200, 255, 210, 70), 2 * d))
                { g.FillPath(bg, gp); g.DrawPath(bp, gp); }
                c.LineAlignment = StringAlignment.Center;
                g.DrawString(title, tf, Brushes.White, new RectangleF(16 * d, 0, 150 * d, canvas.Height), c);
                c.Alignment = StringAlignment.Center;
                using (SolidBrush gold = new SolidBrush(Color.FromArgb(255, 210, 70))) g.DrawString(main, mf, gold, new RectangleF(140 * d, 0, 180 * d, canvas.Height), c);
                c.Alignment = StringAlignment.Far;
                using (SolidBrush grey = new SolidBrush(Color.FromArgb(220, 225, 240))) g.DrawString(side, sf, grey, new RectangleF(300 * d, 0, 144 * d, canvas.Height), c);
            }
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Native.UpdateLayered(Handle, canvas, wa.Left + (wa.Width - canvas.Width) / 2, wa.Top + (int)(10 * d));
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    // Best scores, saved in %APPDATA%\StickBuddy\scores.txt
    public static class Scores
    {
        static Dictionary<string, int> best;
        static string PathFile { get { return Path.Combine(Log.Folder, "scores.txt"); } }

        static void Load()
        {
            if (best != null) return;
            best = new Dictionary<string, int>();
            try
            {
                if (File.Exists(PathFile))
                    foreach (string line in File.ReadAllLines(PathFile))
                    {
                        int eq = line.IndexOf('=');
                        int v;
                        if (eq > 0 && int.TryParse(line.Substring(eq + 1), out v)) best[line.Substring(0, eq)] = v;
                    }
            }
            catch { }
        }

        public static int Best(string game) { Load(); int v; return best.TryGetValue(game, out v) ? v : 0; }

        // Returns true for a new record.
        public static bool Submit(string game, int score)
        {
            Load();
            if (score <= Best(game)) return false;
            best[game] = score;
            try
            {
                Directory.CreateDirectory(Log.Folder);
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in best) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(PathFile, sb.ToString());
            }
            catch { }
            return true;
        }
    }

    // Little sound effects and the dance music, made with maths (no sound files needed).
    public static class Sfx
    {
        const int Rate = 22050;
        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        static SoundPlayer last;
        static readonly Random rnd = new Random(7);
        public static bool Off;

        public static void Play(string name)
        {
            if (Off) return;
            try
            {
                byte[] w;
                if (!cache.TryGetValue(name, out w)) { w = Wav(Make(name)); cache[name] = w; }
                SoundPlayer p = new SoundPlayer(new MemoryStream(w));
                p.Play();
                last = p;
            }
            catch (Exception ex) { Log.Write("sfx: " + ex.Message); }
        }

        // For the test: makes a sound (without playing it) and returns its size.
        public static int MakeSize(string name) { return Wav(Make(name)).Length; }

        static float[] Make(string name)
        {
            switch (name)
            {
                case "pop": return Mix(Noise(0.09, 0.9, 40), Chirp(900, 300, 0.08, 0.5));
                case "whack": return Mix(Chirp(180, 60, 0.18, 0.9), Noise(0.03, 0.5, 60));
                case "ding": return Mix(Tone(988, 0.12, 0.4), Delay(Tone(1319, 0.3, 0.45), 0.1));
                case "buzz": return Square(140, 0.3, 0.25);
                case "whoosh": return Noise(0.25, 0.35, 8);
                case "cheer": return Mix(Tone(523, 0.12, 0.35), Delay(Tone(659, 0.12, 0.35), 0.12), Delay(Tone(784, 0.3, 0.4), 0.24));
                case "swish": return Mix(Noise(0.2, 0.3, 14), Delay(Tone(1568, 0.2, 0.25), 0.05));
            }
            return Tone(440, 0.1, 0.3);
        }

        static float[] Tone(double f, double sec, double amp)
        {
            int n = (int)(sec * Rate);
            float[] s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)(Math.Sin(2 * Math.PI * f * i / Rate) * amp * Math.Exp(-4.0 * i / n));
            return s;
        }

        static float[] Square(double f, double sec, double amp)
        {
            int n = (int)(sec * Rate);
            float[] s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)((Math.Sin(2 * Math.PI * f * i / Rate) >= 0 ? 1 : -1) * amp * (1 - (double)i / n));
            return s;
        }

        static float[] Chirp(double f0, double f1, double sec, double amp)
        {
            int n = (int)(sec * Rate);
            float[] s = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double f = f0 + (f1 - f0) * i / n;
                ph += 2 * Math.PI * f / Rate;
                s[i] = (float)(Math.Sin(ph) * amp * Math.Exp(-3.0 * i / n));
            }
            return s;
        }

        static float[] Noise(double sec, double amp, double decay)
        {
            int n = (int)(sec * Rate);
            float[] s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)((rnd.NextDouble() * 2 - 1) * amp * Math.Exp(-decay * i / Rate));
            return s;
        }

        static float[] Delay(float[] a, double sec)
        {
            int off = (int)(sec * Rate);
            float[] s = new float[a.Length + off];
            Array.Copy(a, 0, s, off, a.Length);
            return s;
        }

        static float[] Mix(params float[][] parts)
        {
            int n = 0;
            foreach (float[] p in parts) n = Math.Max(n, p.Length);
            float[] s = new float[n];
            foreach (float[] p in parts) for (int i = 0; i < p.Length; i++) s[i] += p[i];
            return s;
        }

        public static byte[] Wav(float[] s)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + s.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(s.Length * 2);
                foreach (float v in s) w.Write((short)(Math.Max(-1, Math.Min(1, v)) * 30000));
                w.Flush();
                return ms.ToArray();
            }
        }

        // A happy dance song: drums, bass and a little tune over C - Am - F - G.
        public static byte[] DanceSong(double bpm, int bars)
        {
            double beat = 60 / bpm, eighth = beat / 2;
            int n = (int)(bars * 4 * beat * Rate);
            float[] s = new float[n];
            double[][] chords = { new[] { 523.25, 659.25, 783.99, 659.25 }, new[] { 440.0, 523.25, 659.25, 523.25 }, new[] { 349.23, 440.0, 523.25, 440.0 }, new[] { 392.0, 493.88, 587.33, 493.88 } };
            double[] roots = { 65.41, 55.0, 43.65, 49.0 };
            Random r = new Random(3);
            for (int k = 0; k < bars * 8; k++)
            {
                int start = (int)(k * eighth * Rate);
                int bar = k / 8, inBar = k % 8, chord = bar % 4;
                // hi-hat
                for (int i = 0; i < (int)(0.03 * Rate) && start + i < n; i++) s[start + i] += (float)((r.NextDouble() * 2 - 1) * 0.08 * Math.Exp(-120.0 * i / Rate));
                // kick on 1 and 3, snare on 2 and 4
                if (inBar == 0 || inBar == 4)
                {
                    double ph = 0;
                    for (int i = 0; i < (int)(0.15 * Rate) && start + i < n; i++) { double f = 110 - 70.0 * i / (0.15 * Rate); ph += 2 * Math.PI * f / Rate; s[start + i] += (float)(Math.Sin(ph) * 0.75 * Math.Exp(-14.0 * i / Rate)); }
                }
                if (inBar == 2 || inBar == 6)
                    for (int i = 0; i < (int)(0.12 * Rate) && start + i < n; i++) s[start + i] += (float)(((r.NextDouble() * 2 - 1) * 0.3 + Math.Sin(2 * Math.PI * 190 * i / Rate) * 0.15) * Math.Exp(-25.0 * i / Rate));
                // bass: root, octave, root, octave...
                double bf = roots[chord] * (k % 2 == 0 ? 2 : 4);
                for (int i = 0; i < (int)(eighth * Rate * 0.9) && start + i < n; i++)
                    s[start + i] += (float)((Math.Sin(2 * Math.PI * bf * i / Rate) >= 0 ? 1 : -1) * 0.09 * Math.Exp(-5.0 * i / Rate));
                // little tune
                double lf = chords[chord][inBar % 4] * (bar % 8 >= 4 && inBar >= 4 ? 2 : 1);
                for (int i = 0; i < (int)(eighth * Rate * 0.8) && start + i < n; i++)
                {
                    double t = (double)i / Rate;
                    double tri = 2 * Math.Abs(2 * (t * lf - Math.Floor(t * lf + 0.5))) - 1;
                    s[start + i] += (float)(tri * 0.09 * Math.Exp(-6.0 * t));
                }
            }
            return Wav(s);
        }
    }
}
