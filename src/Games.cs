using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickBuddy
{
    // You (X) against the stickman (O).
    public class TicTacToeForm : Form
    {
        readonly Buddy b;
        readonly int[] cells = new int[9]; // 0 empty, 1 you, 2 him
        bool userTurn = true, over;
        int[] winLine;
        string status = "Your turn! You are X.";
        readonly int cell, pad, statusH;
        readonly Button again;
        readonly Random rnd = new Random();
        static readonly int[][] Lines =
        {
            new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8}, new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8}, new[] {0,4,8}, new[] {2,4,6}
        };

        public TicTacToeForm(Buddy b)
        {
            this.b = b;
            Text = "Tic Tac Toe with " + b.S.Name;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.White;
            cell = (int)(100 * b.Dpi); pad = (int)(16 * b.Dpi); statusH = (int)(70 * b.Dpi);
            ClientSize = new Size(cell * 3 + pad * 2, cell * 3 + pad * 2 + statusH);
            again = new Button();
            again.Text = "Play again";
            again.Font = new Font("Segoe UI", 10f);
            again.AutoSize = true;
            again.Visible = false;
            again.Click += delegate { Reset(); };
            Controls.Add(again);
            again.Location = new Point(ClientSize.Width - pad - (int)(110 * b.Dpi), cell * 3 + pad * 2 + (int)(18 * b.Dpi));
            MouseClick += OnClickBoard;
            Paint += OnPaintBoard;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max((int)(150 * b.U), (wa.Height - Height) / 2 + (int)(40 * b.Dpi)));
        }

        void Reset()
        {
            for (int i = 0; i < 9; i++) cells[i] = 0;
            over = false; winLine = null; again.Visible = false;
            userTurn = rnd.Next(2) == 0;
            status = userTurn ? "Your turn!" : b.S.Name + " goes first...";
            Invalidate();
            if (!userTurn) BuddyMoveLater();
        }

        void OnClickBoard(object sender, MouseEventArgs e)
        {
            if (!userTurn || over) return;
            int cx = (e.X - pad) / cell, cy = (e.Y - pad) / cell;
            if (e.X < pad || e.Y < pad || cx > 2 || cy > 2) return;
            int c = cy * 3 + cx;
            if (cells[c] != 0) return;
            cells[c] = 1;
            Invalidate();
            if (CheckEnd()) return;
            userTurn = false;
            BuddyMoveLater();
        }

        async void BuddyMoveLater()
        {
            status = b.S.Name + " is thinking...";
            Invalidate();
            b.TttThinking();
            await Task.Delay(700 + rnd.Next(700));
            if (IsDisposed) return;
            cells[ChooseMove()] = 2;
            b.TttMoved();
            Invalidate();
            if (!CheckEnd()) { userTurn = true; status = "Your turn!"; Invalidate(); }
        }

        int Winner()
        {
            foreach (int[] l in Lines)
                if (cells[l[0]] != 0 && cells[l[0]] == cells[l[1]] && cells[l[1]] == cells[l[2]]) { winLine = l; return cells[l[0]]; }
            return 0;
        }

        bool CheckEnd()
        {
            int w = Winner();
            bool full = Array.IndexOf(cells, 0) < 0;
            if (w == 0 && !full) return false;
            over = true;
            again.Visible = true;
            status = w == 1 ? "You win!" : w == 2 ? b.S.Name + " wins!" : "It's a tie!";
            Invalidate();
            b.TttEnded(w);
            return true;
        }

        int FindLine(int who)
        {
            foreach (int[] l in Lines)
            {
                int count = 0, empty = -1;
                foreach (int i in l) { if (cells[i] == who) count++; else if (cells[i] == 0) empty = i; }
                if (count == 2 && empty >= 0) return empty;
            }
            return -1;
        }

        // Pretty good, but not perfect, so you can win sometimes.
        int ChooseMove()
        {
            int m = FindLine(2);
            if (m >= 0) return m;
            m = FindLine(1);
            if (m >= 0 && rnd.NextDouble() < 0.85) return m;
            if (cells[4] == 0 && rnd.NextDouble() < 0.7) return 4;
            List<int> free = new List<int>();
            for (int i = 0; i < 9; i++) if (cells[i] == 0) free.Add(i);
            List<int> corners = free.FindAll(delegate (int i) { return i == 0 || i == 2 || i == 6 || i == 8; });
            if (corners.Count > 0 && rnd.NextDouble() < 0.6) return corners[rnd.Next(corners.Count)];
            return free[rnd.Next(free.Count)];
        }

        void OnPaintBoard(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = (float)b.Dpi;
            using (Pen grid = new Pen(Color.FromArgb(200, 200, 210), 4 * d))
            {
                grid.StartCap = LineCap.Round; grid.EndCap = LineCap.Round;
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(grid, pad + i * cell, pad + 8 * d, pad + i * cell, pad + 3 * cell - 8 * d);
                    g.DrawLine(grid, pad + 8 * d, pad + i * cell, pad + 3 * cell - 8 * d, pad + i * cell);
                }
            }
            float m = cell * 0.22f;
            using (Pen xp = new Pen(Color.FromArgb(30, 136, 229), 9 * d))
            using (Pen op = new Pen(b.S.BodyColor, 9 * d))
            {
                xp.StartCap = LineCap.Round; xp.EndCap = LineCap.Round;
                for (int i = 0; i < 9; i++)
                {
                    float x = pad + (i % 3) * cell, y = pad + (i / 3) * cell;
                    if (cells[i] == 1)
                    {
                        g.DrawLine(xp, x + m, y + m, x + cell - m, y + cell - m);
                        g.DrawLine(xp, x + cell - m, y + m, x + m, y + cell - m);
                    }
                    else if (cells[i] == 2) g.DrawEllipse(op, x + m, y + m, cell - 2 * m, cell - 2 * m);
                }
            }
            if (winLine != null)
            {
                using (Pen wp = new Pen(Color.FromArgb(220, 240, 60, 60), 7 * d))
                {
                    wp.StartCap = LineCap.Round; wp.EndCap = LineCap.Round;
                    PointF a = Center(winLine[0]), c = Center(winLine[2]);
                    g.DrawLine(wp, a, c);
                }
            }
            using (Font f = new Font("Segoe UI", 14f, FontStyle.Bold))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(40, 40, 55)))
                g.DrawString(status, f, tb, pad, cell * 3 + pad * 2 + 18 * d);
        }

        PointF Center(int i) { return new PointF(pad + (i % 3) * cell + cell / 2f, pad + (i / 3) * cell + cell / 2f); }
    }

    // The stickman plays Snake all by himself (and loves it).
    public class SnakeForm : Form
    {
        readonly Buddy b;
        const int N = 20;
        readonly int cs, topH;
        List<Point> snake = new List<Point>();
        Point food, dir = new Point(1, 0);
        readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        int score, best, deaths;
        bool dead, finished;
        readonly Random rnd = new Random();
        static readonly Point[] Dirs = { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };

        public SnakeForm(Buddy b)
        {
            this.b = b;
            Text = b.S.Name + " plays Snake";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(22, 30, 26);
            cs = (int)(18 * b.Dpi);
            topH = (int)(36 * b.Dpi);
            ClientSize = new Size(N * cs, N * cs + topH);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max((int)(150 * b.U), (wa.Height - Height) / 2 + (int)(40 * b.Dpi)));
            Paint += OnPaintGame;
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Space) t.Enabled = !t.Enabled; };
            t.Interval = 85;
            t.Tick += delegate { Step(); };
            NewGame();
            t.Start();
            FormClosed += delegate { t.Stop(); t.Dispose(); };
        }

        void NewGame()
        {
            snake = new List<Point> { new Point(6, 10), new Point(5, 10), new Point(4, 10) };
            dir = new Point(1, 0);
            score = 0; dead = false;
            PlaceFood();
        }

        void PlaceFood()
        {
            do food = new Point(rnd.Next(N), rnd.Next(N)); while (snake.Contains(food));
        }

        static bool Inside(Point p) { return p.X >= 0 && p.Y >= 0 && p.X < N && p.Y < N; }
        static Point Add(Point a, Point c) { return new Point(a.X + c.X, a.Y + c.Y); }

        bool[,] Blocked(bool tailFree)
        {
            bool[,] bl = new bool[N, N];
            for (int i = 0; i < snake.Count - (tailFree ? 1 : 0); i++) bl[snake[i].X, snake[i].Y] = true;
            return bl;
        }

        int FloodSize(Point start, bool[,] bl)
        {
            if (!Inside(start) || bl[start.X, start.Y]) return 0;
            bool[,] seen = new bool[N, N];
            Queue<Point> q = new Queue<Point>();
            q.Enqueue(start); seen[start.X, start.Y] = true;
            int n = 0;
            while (q.Count > 0)
            {
                Point p = q.Dequeue(); n++;
                foreach (Point d in Dirs)
                {
                    Point np = Add(p, d);
                    if (Inside(np) && !bl[np.X, np.Y] && !seen[np.X, np.Y]) { seen[np.X, np.Y] = true; q.Enqueue(np); }
                }
            }
            return n;
        }

        // Finds a path to the food; if that looks dangerous, goes where there is the most room.
        Point ThinkDir()
        {
            bool[,] bl = Blocked(true);
            Point head = snake[0];
            Point[,] from = new Point[N, N];
            bool[,] seen = new bool[N, N];
            Queue<Point> q = new Queue<Point>();
            q.Enqueue(head); seen[head.X, head.Y] = true;
            bool found = false;
            while (q.Count > 0 && !found)
            {
                Point p = q.Dequeue();
                foreach (Point d in Dirs)
                {
                    Point np = Add(p, d);
                    if (!Inside(np) || bl[np.X, np.Y] || seen[np.X, np.Y]) continue;
                    seen[np.X, np.Y] = true; from[np.X, np.Y] = p; q.Enqueue(np);
                    if (np == food) { found = true; break; }
                }
            }
            if (found)
            {
                Point step = food;
                while (from[step.X, step.Y] != head) step = from[step.X, step.Y];
                Point d = new Point(step.X - head.X, step.Y - head.Y);
                if (FloodSize(step, bl) > snake.Count || rnd.NextDouble() < 0.05) return d;
            }
            Point bestD = dir;
            int bestN = -1;
            foreach (Point d in Dirs)
            {
                Point np = Add(head, d);
                int n = FloodSize(np, bl);
                if (n > bestN) { bestN = n; bestD = d; }
            }
            return bestD;
        }

        void Step()
        {
            if (dead || finished) return;
            dir = ThinkDir();
            Point h = Add(snake[0], dir);
            bool eating = h == food;
            bool hit = !Inside(h);
            if (!hit)
                for (int i = 0; i < snake.Count - (eating ? 0 : 1); i++) if (snake[i] == h) { hit = true; break; }
            if (hit) { Die(); return; }
            snake.Insert(0, h);
            if (eating) { score++; if (score > best) best = score; PlaceFood(); b.SnakeAte(score); }
            else snake.RemoveAt(snake.Count - 1);
            Invalidate();
        }

        async void Die()
        {
            dead = true;
            deaths++;
            Invalidate();
            b.SnakeDied(score, deaths);
            await Task.Delay(1800);
            if (IsDisposed) return;
            if (deaths >= 3)
            {
                finished = true;
                b.SnakeDone(best);
                Invalidate();
                return;
            }
            NewGame();
        }

        void OnPaintGame(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = (float)b.Dpi;
            using (SolidBrush top = new SolidBrush(Color.FromArgb(14, 20, 17))) g.FillRectangle(top, 0, 0, ClientSize.Width, topH);
            using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(220, 240, 230)))
            {
                string s = finished ? "Game over! " + b.S.Name + "'s best: " + best : "Score: " + score + "    Best: " + best + "    Lives: " + (3 - deaths);
                g.DrawString(s, f, tb, 8 * d, 7 * d);
            }
            g.TranslateTransform(0, topH);
            using (SolidBrush fb = new SolidBrush(Color.FromArgb(240, 70, 70)))
                g.FillEllipse(fb, food.X * cs + 2, food.Y * cs + 2, cs - 4, cs - 4);
            Color body = dead ? Color.Gray : b.S.BodyColor;
            for (int i = snake.Count - 1; i >= 0; i--)
            {
                Point p = snake[i];
                using (SolidBrush sb = new SolidBrush(i == 0 ? body : Renderer.Shade(body, 0.8 + 0.2 * ((i % 2)))))
                using (GraphicsPath gp = Renderer.RoundRect(new RectangleF(p.X * cs + 1, p.Y * cs + 1, cs - 2, cs - 2), cs * 0.3f))
                    g.FillPath(sb, gp);
            }
            Point h = snake[0];
            using (SolidBrush eye = new SolidBrush(Color.White))
            {
                float ex = h.X * cs + cs / 2f + dir.X * cs * 0.18f, ey = h.Y * cs + cs / 2f + dir.Y * cs * 0.18f;
                float ox = dir.Y * cs * 0.2f, oy = dir.X * cs * 0.2f;
                g.FillEllipse(eye, ex + ox - 2.5f * d, ey + oy - 2.5f * d, 5 * d, 5 * d);
                g.FillEllipse(eye, ex - ox - 2.5f * d, ey - oy - 2.5f * d, 5 * d, 5 * d);
            }
            if (dead && !finished)
                using (Font f = new Font("Segoe UI", 20f, FontStyle.Bold))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("Oops!", f, tb, new RectangleF(0, 0, N * cs, N * cs), sf);
                }
        }
    }
}
