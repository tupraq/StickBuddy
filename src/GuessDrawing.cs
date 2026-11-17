using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // Guess My Drawing: he draws something slowly, you type (or say) what it is. Faster = more points.
    public class GuessForm : Form
    {
        class Paper : Panel { public Paper() { DoubleBuffered = true; } }

        readonly Buddy b;
        readonly Paper paper;
        readonly TextBox box;
        readonly Label info;
        readonly System.Windows.Forms.Timer timer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random rnd = new Random();
        readonly List<string> used = new List<string>();
        readonly float d;
        Doodle cur;
        double start, revealAt = -1, nextAt = -1;
        const double DrawTime = 14, Rounds = 5;
        int round, score, right;
        bool solved, hinted, over;
        string lastWrong = "";

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        double Progress { get { return cur == null ? 0 : Math.Min(1, (Now - start) / DrawTime); } }

        public GuessForm(Buddy b)
        {
            this.b = b;
            d = (float)b.Dpi;
            Text = "Guess My Drawing";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(560 * d), (int)(560 * d));
            Font = new Font("Segoe UI", 12f);
            try { Icon = Renderer.MakeIcon(b.S.BodyColor); } catch { }

            paper = new Paper();
            paper.SetBounds((int)(12 * d), (int)(12 * d), (int)(536 * d), (int)(420 * d));
            paper.Paint += DrawPaper;
            Controls.Add(paper);

            box = new TextBox();
            box.SetBounds((int)(12 * d), (int)(444 * d), (int)(420 * d), (int)(34 * d));
            box.Font = new Font("Segoe UI", 15f);
            box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Guess(box.Text); box.Text = ""; }
            };
            Controls.Add(box);
            Button go = new Button(); go.Text = "Guess!"; go.SetBounds((int)(440 * d), (int)(443 * d), (int)(108 * d), (int)(36 * d));
            go.Click += delegate { Guess(box.Text); box.Text = ""; box.Focus(); };
            Controls.Add(go);
            info = new Label();
            info.SetBounds((int)(12 * d), (int)(488 * d), (int)(536 * d), (int)(60 * d));
            info.Font = new Font("Segoe UI", 11.5f);
            Controls.Add(info);
            AcceptButton = null;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 30;
            timer.Tick += delegate { Tick(); paper.Invalidate(); };
            timer.Start();
            NextRound();
        }

        void NextRound()
        {
            round++;
            if (round > Rounds) { Finish(); return; }
            cur = Doodle.MakeGuess(rnd, used);
            start = Now; solved = false; hinted = false; revealAt = -1; nextAt = -1; lastWrong = "";
            UpdateInfo("Round " + round + " of " + Rounds + ". What am I drawing? Type it or say it!");
            b.GuessDrawing(round);
            box.Focus();
        }

        void UpdateInfo(string s) { info.Text = s + "\nScore: " + score + "   (best " + Scores.Best("guess") + ")"; }

        void Tick()
        {
            if (over || cur == null) return;
            double now = Now;
            if (!solved)
            {
                if (!hinted && Progress > 0.55) { hinted = true; b.GuessHint(cur.Hint); UpdateInfo("Hint: " + cur.Hint); }
                if (Progress >= 1 && revealAt < 0) revealAt = now + 7;
                if (revealAt > 0 && now > revealAt)
                {
                    solved = true;
                    b.GuessReveal(cur.Name);
                    UpdateInfo("It was " + cur.Name + "!");
                    nextAt = now + 3;
                }
            }
            if (nextAt > 0 && now > nextAt) { nextAt = -1; NextRound(); }
        }

        // Your guess (typed, or said out loud). Returns true when it was about this game.
        public bool Guess(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { Guess(text); })); return true; }
            text = (text ?? "").Trim();
            if (over || solved || cur == null || text.Length == 0) return false;
            if (cur.IsRight(text))
            {
                solved = true; right++;
                int pts = Math.Max(20, 100 - (int)(Progress * 80));
                score += pts;
                Sfx.Play("ding");
                b.GuessRight(cur.Name, pts);
                UpdateInfo("YES! It's " + cur.Name + "! +" + pts + " points");
                nextAt = Now + 3;
            }
            else
            {
                lastWrong = text;
                b.GuessWrong(text);
                UpdateInfo("\"" + text + "\"? Nope! Keep guessing.");
            }
            return true;
        }

        void Finish()
        {
            over = true; cur = null;
            bool record = Scores.Submit("guess", score);
            UpdateInfo("All done! You got " + right + " of " + (int)Rounds + " right. Close this window or play again from the Game Center.");
            b.GuessDone(score, right, (int)Rounds, record);
        }

        void DrawPaper(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Rectangle r = paper.ClientRectangle;
            g.Clear(Color.FromArgb(250, 248, 240));
            using (Pen lines = new Pen(Color.FromArgb(40, 80, 120, 200)))
                for (float y = 30 * d; y < r.Height; y += 30 * d) g.DrawLine(lines, 0, y, r.Width, y);
            if (over)
            {
                using (Font f = new Font("Segoe UI", 36 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat c = new StringFormat())
                {
                    c.Alignment = StringAlignment.Center; c.LineAlignment = StringAlignment.Center;
                    g.DrawString(score + " points!", f, Brushes.DarkSlateBlue, r, c);
                }
                return;
            }
            if (cur == null) return;
            RectangleF area = new RectangleF(30 * d, 20 * d, r.Width - 60 * d, r.Height - 50 * d);
            double p = solved ? 1 : Progress;
            cur.Draw(g, area, p);
            if (!solved && p < 1)
            {
                // the pencil
                PointF tip = cur.Tip(p);
                float tx = area.X + tip.X * area.Width / 100f, ty = area.Y + tip.Y * area.Height / 80f;
                using (Pen pe = new Pen(Color.FromArgb(255, 200, 40), 7 * d)) g.DrawLine(pe, tx + 6 * d, ty - 6 * d, tx + 34 * d, ty - 34 * d);
                using (Pen pt = Renderer.RoundPen(Color.FromArgb(60, 50, 40), 3 * d)) g.DrawLine(pt, tx, ty, tx + 6 * d, ty - 6 * d);
                using (Pen pk = new Pen(Color.FromArgb(240, 130, 150), 7 * d)) g.DrawLine(pk, tx + 34 * d, ty - 34 * d, tx + 40 * d, ty - 40 * d);
            }
            // time bar
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(120, 70, 130, 230))) g.FillRectangle(tb, 0, r.Height - 8 * d, (float)(r.Width * (1 - Progress)), 8 * d);
            if (solved)
                using (Font f = new Font("Segoe UI", 22 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(60, 160, 80)))
                    g.DrawString(cur.Name.ToUpperInvariant(), f, gb, 14 * d, 8 * d);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            b.GuessClosed();
            base.OnFormClosed(e);
        }
    }

    public partial class Buddy
    {
        GuessForm guessForm;

        public void StartGuessDrawing()
        {
            Touch();
            if (guessForm != null && !guessForm.IsDisposed) guessForm.Close();
            guessForm = new GuessForm(this);
            guessForm.Show();
            BringForward(guessForm);
            GuessForm f = guessForm;
            RunJob(async delegate (CancellationToken ct)
            {
                BeginGame("guess", false);
                GameHear = delegate (string heard)
                {
                    // spoken guesses: "is it a cat", "a cat!", "cat"
                    string g = System.Text.RegularExpressions.Regex.Replace(heard, @"^(is it|it is|it s|that s|thats|i think it s|i think it is|maybe)\s+", "");
                    return !f.IsDisposed && f.Guess(g);
                };
                try
                {
                    await Wait(0.5, ct);
                    if (!f.IsDisposed)
                    {
                        Native.RECT r = Native.GetVisibleRect(f.Handle);
                        await StandOnWindow(f.Handle, r.Width * 0.75, ct);
                    }
                    Facing = -1;
                    while (!f.IsDisposed && f.Visible) await Wait(0.3, ct);
                }
                finally { BaseAct = Act.Idle; EndGame(); }
            });
        }

        public void GuessDrawing(int round)
        {
            BaseAct = Act.Write;
            Say(round == 1 ? "Guess My Drawing! I'll draw something. Type or say what it is. The faster you guess, the more points!" : Pick("Okay, next one!", "Ooh, this one's tricky!", "Watch closely..."));
        }

        public void GuessHint(string hint) { Say("Hint: " + hint); }

        public void GuessWrong(string text)
        {
            if (Rnd.NextDouble() < 0.5) Say(Pick("Nope!", "Not quite!", "Hehe, no!", "Good guess, but no!"));
            else ShowBubble(Pick("Nope!", "Not " + text + "!", "Try again!"), 1.5);
        }

        public void GuessRight(string name, int pts)
        {
            BaseAct = Act.Cheer;
            AddStars(3);
            Say(Pick("Yes! It's " + name + "!", "You got it! " + name + "!", "Correct! Plus " + pts + "!"));
        }

        public void GuessReveal(string name) { BaseAct = Act.Shrug; Say("Time's up! It was " + name + "!"); }

        public void GuessDone(int score, int right, int rounds, bool record)
        {
            BaseAct = Act.Cheer;
            Brain.Event("you played Guess My Drawing with the user: they guessed " + right + " of " + rounds + " drawings");
            Brain.Happy = Math.Min(100, Brain.Happy + 15);
            if (record) { Sfx.Play("cheer"); AddStars(5); }
            Say("You guessed " + right + " of " + rounds + " and got " + score + " points!" + (record ? " That's a new record!" : "") + (right >= 4 ? " You can read my mind!" : ""));
        }

        public void GuessClosed() { if (ActiveGame == "guess") CancelJob(); }
    }
}
