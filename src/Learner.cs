using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StickBuddy
{
    public enum Tile : byte { Empty, Block, Lava, Bounce, Check, Start, Finish }

    // An obby (obstacle course): a grid of tiles. y goes down, 1 unit = 1 tile.
    public class Level
    {
        public int W, H;
        public Tile[,] T;
        public int[,] Dist;     // how many steps to the finish from each tile (-1 = can't get there)
        public string Name = "My obby";

        public Level(int w, int h) { W = w; H = h; T = new Tile[w, h]; }

        public Tile At(int x, int y) { return x < 0 || y < 0 || x >= W || y >= H ? Tile.Empty : T[x, y]; }

        public bool Solid(int x, int y)
        {
            if (x < 0 || x >= W || y < 0) return true;  // walls and ceiling
            if (y >= H) return false;                   // the bottom is a deadly drop
            Tile t = T[x, y];
            return t == Tile.Block || t == Tile.Bounce;
        }

        public void Set(int x, int y, Tile t)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            if (t == Tile.Start || t == Tile.Finish)
                for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) if (T[i, j] == t) T[i, j] = Tile.Empty;
            T[x, y] = t;
        }

        public bool Find(Tile t, out int fx, out int fy)
        {
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (T[x, y] == t) { fx = x; fy = y; return true; }
            fx = fy = -1;
            return false;
        }

        bool Open(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H && !Solid(x, y) && T[x, y] != Tile.Lava; }

        // Distance (in tiles) from every open tile to the finish, ignoring gravity.
        public void ComputeDist()
        {
            Dist = new int[W, H];
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) Dist[x, y] = -1;
            Queue<int> q = new Queue<int>();
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (T[x, y] == Tile.Finish) { Dist[x, y] = 0; q.Enqueue(x * 1000 + y); }
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (q.Count > 0)
            {
                int v = q.Dequeue(), cx = v / 1000, cy = v % 1000;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + dx[k], ny = cy + dy[k];
                    if (!Open(nx, ny) || Dist[nx, ny] >= 0) continue;
                    if (k >= 4 && (!Open(cx + dx[k], cy) || !Open(cx, cy + dy[k]))) continue;
                    Dist[nx, ny] = Dist[cx, cy] + 1;
                    q.Enqueue(nx * 1000 + ny);
                }
            }
        }

        public int DistAt(int x, int y) { return Dist == null || x < 0 || y < 0 || x >= W || y >= H ? -1 : Dist[x, y]; }

        public string Hash()
        {
            unchecked
            {
                int h = 17;
                for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) h = h * 31 + (int)T[x, y] * (x + 1) * 7 + y;
                return h.ToString("X");
            }
        }

        const string Chars = ".#LBCSF";

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(W + " " + H);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++) sb.Append(Chars[(int)T[x, y]]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static Level Parse(string text, string name)
        {
            string[] lines = text.Replace("\r", "").Split('\n');
            string[] wh = lines[0].Split(' ');
            Level l = new Level(int.Parse(wh[0]), int.Parse(wh[1]));
            l.Name = name;
            for (int y = 0; y < l.H && y + 1 < lines.Length; y++)
                for (int x = 0; x < l.W && x < lines[y + 1].Length; x++)
                {
                    int i = Chars.IndexOf(lines[y + 1][x]);
                    l.T[x, y] = i < 0 ? Tile.Empty : (Tile)i;
                }
            return l;
        }

        // ---- ready-made obbies ----
        public static readonly string[] ExampleNames = { "First steps", "Lava run", "Bounce tower", "Wall climb (needs parkour!)" };

        public static Level Example(int n, int w, int h)
        {
            Level l = new Level(w, h);
            l.Name = ExampleNames[n];
            int g = h - 2; // ground row
            switch (n)
            {
                case 0:
                    Fill(l, 0, g, 12, 2, Tile.Block);
                    Fill(l, 15, g, 8, 2, Tile.Block);
                    Fill(l, 25, g - 2, 6, 4, Tile.Block);
                    Fill(l, 31, g, 3, 2, Tile.Lava);
                    Fill(l, 34, g, w - 34, 2, Tile.Block);
                    l.T[2, g - 1] = Tile.Start; l.T[w - 3, g - 1] = Tile.Finish; l.T[18, g - 1] = Tile.Check;
                    break;
                case 1:
                    Fill(l, 0, g, 6, 2, Tile.Block);
                    Fill(l, 6, g + 1, w - 12, 1, Tile.Lava);
                    for (int i = 0; i < 7; i++) Fill(l, 9 + i * 5, g - (i % 2) * 2, 2, 1, Tile.Block);
                    Fill(l, w - 6, g, 6, 2, Tile.Block);
                    l.T[2, g - 1] = Tile.Start; l.T[w - 3, g - 1] = Tile.Finish;
                    break;
                case 2:
                    Fill(l, 0, g, w, 2, Tile.Block);
                    Fill(l, 12, g - 6, w - 12, 1, Tile.Block);   // high floor
                    Fill(l, 12, g - 5, 1, 5, Tile.Block);        // wall: can't just walk
                    l.T[9, g] = Tile.Bounce;
                    Fill(l, 20, g - 7, 2, 1, Tile.Lava);
                    Fill(l, 30, g - 7, 3, 1, Tile.Lava);
                    l.T[2, g - 1] = Tile.Start; l.T[w - 3, g - 7] = Tile.Finish;
                    break;
                default:
                    Fill(l, 0, g, w, 2, Tile.Block);
                    Fill(l, 16, 3, 1, g - 6, Tile.Block);       // left wall of the shaft (open at the bottom to walk in)
                    Fill(l, 20, 6, 1, g - 6, Tile.Block);       // right wall of the shaft
                    Fill(l, 20, 5, w - 20, 1, Tile.Block);      // top floor
                    Fill(l, 21, g - 1, w - 21, 1, Tile.Lava);
                    l.T[4, g - 1] = Tile.Start; l.T[w - 3, 4] = Tile.Finish;
                    break;
            }
            return l;
        }

        static void Fill(Level l, int x0, int y0, int w, int h, Tile t)
        {
            for (int x = x0; x < x0 + w; x++) for (int y = y0; y < y0 + h; y++) if (x >= 0 && y >= 0 && x < l.W && y < l.H) l.T[x, y] = t;
        }
    }

    // A small neural network: what the stickman "sees" goes in, which buttons to press come out.
    public class Net
    {
        public const int Cols = 7, Rows = 6, NIn = Cols * Rows + 9, NHid = 12, NOut = 3;
        public const int Size = NIn * NHid + NHid + NHid * NOut + NOut;
        public double[] Wt = new double[Size];
        public readonly double[] Hid = new double[NHid], Out = new double[NOut];

        public static Net Random(Random r)
        {
            Net n = new Net();
            for (int i = 0; i < Size; i++) n.Wt[i] = (r.NextDouble() * 2 - 1) * (i < NIn * NHid ? 0.6 : 1.0);
            return n;
        }

        public Net Clone() { Net n = new Net(); Array.Copy(Wt, n.Wt, Size); return n; }

        public void Run(double[] input)
        {
            int k = 0;
            for (int h = 0; h < NHid; h++)
            {
                double s = 0;
                for (int i = 0; i < NIn; i++) s += input[i] * Wt[k++];
                Hid[h] = s;
            }
            for (int h = 0; h < NHid; h++) Hid[h] = Math.Tanh(Hid[h] + Wt[k++]);
            for (int o = 0; o < NOut; o++)
            {
                double s = 0;
                for (int h = 0; h < NHid; h++) s += Hid[h] * Wt[k++];
                Out[o] = s;
            }
            for (int o = 0; o < NOut; o++) Out[o] = Math.Tanh(Out[o] + Wt[k++]);
        }

        public double W1(int i, int h) { return Wt[h * NIn + i]; }
        public double W2(int h, int o) { return Wt[NIn * NHid + NHid + o * NHid + h]; }

        static double Gauss(Random r) { return Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble()); }

        public void Mutate(Random r, double rate, double sigma)
        {
            for (int i = 0; i < Size; i++)
            {
                if (r.NextDouble() < rate) Wt[i] += Gauss(r) * sigma;
                if (r.NextDouble() < rate * 0.05) Wt[i] = r.NextDouble() * 2 - 1;
                if (Wt[i] > 4) Wt[i] = 4; else if (Wt[i] < -4) Wt[i] = -4;
            }
        }

        public static Net Cross(Net a, Net b, Random r)
        {
            Net n = new Net();
            // swap whole neurons so good "ideas" stay together
            for (int h = 0; h < NHid; h++)
            {
                Net src = r.NextDouble() < 0.5 ? a : b;
                for (int i = 0; i < NIn; i++) n.Wt[h * NIn + i] = src.Wt[h * NIn + i];
                n.Wt[NIn * NHid + h] = src.Wt[NIn * NHid + h];
                for (int o = 0; o < NOut; o++) n.Wt[NIn * NHid + NHid + o * NHid + h] = src.Wt[NIn * NHid + NHid + o * NHid + h];
            }
            for (int o = 0; o < NOut; o++) n.Wt[Size - NOut + o] = (r.NextDouble() < 0.5 ? a : b).Wt[Size - NOut + o];
            return n;
        }

        public string Save()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Size; i++) { if (i > 0) sb.Append(','); sb.Append(Wt[i].ToString("0.####", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        public static Net Load(string s)
        {
            string[] p = s.Split(',');
            if (p.Length != Size) return null;
            Net n = new Net();
            for (int i = 0; i < Size; i++) n.Wt[i] = double.Parse(p[i], CultureInfo.InvariantCulture);
            return n;
        }
    }

    // One try at the obby: a little stickman with a brain.
    public class LabAgent
    {
        public Net Brain;
        public double X, Y, VX, VY, T, AnimT, Fitness, LastImprove, WallLock;
        public bool Grounded, Dead, Finished, Done, WallL, WallR, JumpHeld, InLava, Fell;
        public int Facing = 1, BestDist = int.MaxValue, StartDist, Checks;
        public readonly double[] In = new double[Net.NIn];
        public bool KeyL, KeyR, KeyJ;
        public static bool Hint = true;
        readonly HashSet<int> seenChecks = new HashSet<int>();

        public const double HW = 0.3, HT = 1.7, G = 55, MaxFall = 22, Run = 7, AccGround = 70, AccAir = 40, JumpV = 19, BounceV = 27;

        public LabAgent(Net brain, Level l)
        {
            Brain = brain;
            int sx, sy;
            if (!l.Find(Tile.Start, out sx, out sy)) { sx = 1; sy = 1; }
            X = sx + 0.5; Y = sy + 1 - 0.001;
            StartDist = BodyDist(l);
            if (StartDist < 0) StartDist = l.W + l.H;
            BestDist = StartDist;
        }

        int BodyDist(Level l)
        {
            int bx = (int)Math.Floor(X), by = (int)Math.Floor(Y - 0.85);
            int d = l.DistAt(bx, by);
            if (d < 0) d = l.DistAt(bx, by - 1);
            if (d < 0) d = l.DistAt(bx, by + 1);
            return d;
        }

        public double Progress { get { return StartDist <= 0 ? 0 : Math.Max(0, Math.Min(1, (StartDist - BestDist) / (double)StartDist)); } }

        static double Sense(Level l, int x, int y)
        {
            if (y >= l.H) return -1;           // a pit you fall out of
            if (x < 0 || x >= l.W || y < 0) return 1;
            switch (l.T[x, y])
            {
                case Tile.Block: return 1;
                case Tile.Bounce: return 0.6;
                case Tile.Lava: return -1;
                case Tile.Finish: return 0.4;
            }
            return 0;
        }

        void See(Level l, bool canWallJump)
        {
            int fx = (int)Math.Floor(X), fy = (int)Math.Floor(Y + 0.05);
            int k = 0;
            for (int dy = -3; dy <= 2; dy++)
                for (int dx = -2; dx <= 4; dx++)
                    In[k++] = Sense(l, fx + dx, fy + dy);
            // where is the finish? (the way the path goes)
            int bx = (int)Math.Floor(X), by = (int)Math.Floor(Y - 0.85);
            int best = l.DistAt(bx, by), gx = 0, gy = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int d = l.DistAt(bx + dx, by + dy);
                    if (d >= 0 && (best < 0 || d < best)) { best = d; gx = dx; gy = dy; }
                }
            In[k++] = VX / Run; In[k++] = VY / 20; In[k++] = Grounded ? 1 : 0;
            In[k++] = WallL ? 1 : 0; In[k++] = WallR ? 1 : 0;
            // he only gets a hint of where the finish is when it's far up or down (he has to learn the rest himself)
            In[k++] = 0; In[k++] = Hint ? gy : 0;
            In[k++] = canWallJump ? 1 : 0;
            In[k++] = 1;
        }

        bool Hits(Level l, double x0, double y0, double x1, double y1, Func<int, int, bool> test)
        {
            for (int x = (int)Math.Floor(x0); x <= (int)Math.Floor(x1 - 1e-6); x++)
                for (int y = (int)Math.Floor(y0); y <= (int)Math.Floor(y1 - 1e-6); y++)
                    if (test(x, y)) return true;
            return false;
        }

        public void Step(Level l, double dt, bool canWallJump, double maxT)
        {
            if (Done) return;
            T += dt; AnimT += dt;
            See(l, canWallJump);
            Brain.Run(In);
            KeyL = Brain.Out[0] > 0; KeyR = Brain.Out[1] > 0; KeyJ = Brain.Out[2] > 0;
            Control(l, dt, canWallJump);

            // how far along is he?
            int d = BodyDist(l);
            if (d >= 0 && d < BestDist) { BestDist = d; LastImprove = T; }
            if (Fell || InLava) { Dead = true; Done = true; }
            else if (Finished) Done = true;
            else if (T - LastImprove > 2.5 || T > maxT) Done = true;
            if (Done) Fitness = Progress * 1000 + Checks * 60 + (Finished ? 2000 + Math.Max(0, maxT - T) * 40 : 0) - (Dead ? 5 : 0);
        }

        // The movement rules (also used when you watch his best run).
        public void Control(Level l, double dt, bool canWallJump)
        {
            int dir = (KeyR ? 1 : 0) - (KeyL ? 1 : 0);
            if (WallLock > 0) { WallLock -= dt; dir = Math.Sign(VX); }
            double target = dir * Run, acc = Grounded ? AccGround : AccAir;
            if (VX < target) VX = Math.Min(target, VX + acc * dt); else if (VX > target) VX = Math.Max(target, VX - acc * dt);
            if (dir != 0) Facing = dir;

            bool jumpPressed = KeyJ && !JumpHeld;
            JumpHeld = KeyJ;
            if (jumpPressed && Grounded) { VY = -JumpV; Grounded = false; }
            else if (jumpPressed && !Grounded && canWallJump && (WallL || WallR))
            {
                // wall jump: kick off the wall
                VY = -JumpV * 0.88; VX = (WallL ? 1 : -1) * Run * 1.15; WallLock = 0.18; Facing = WallL ? 1 : -1;
            }
            VY = Math.Min(MaxFall, VY + G * dt);
            if (canWallJump && !Grounded && VY > 3 && ((WallL && dir < 0) || (WallR && dir > 0))) VY = 3; // wall slide

            // move sideways
            X += VX * dt;
            if (Hits(l, X - HW, Y - HT, X + HW, Y, l.Solid))
            {
                if (VX > 0) X = Math.Floor(X + HW) - HW - 1e-4; else if (VX < 0) X = Math.Floor(X - HW) + 1 + HW + 1e-4;
                VX = 0;
            }
            // move up/down
            Y += VY * dt;
            Grounded = false;
            if (Hits(l, X - HW, Y - HT, X + HW, Y, l.Solid))
            {
                if (VY > 0)
                {
                    int row = (int)Math.Floor(Y);
                    Y = row - 1e-4;
                    bool bounce = l.At((int)Math.Floor(X), row) == Tile.Bounce || l.At((int)Math.Floor(X - HW + 0.01), row) == Tile.Bounce || l.At((int)Math.Floor(X + HW - 0.01), row) == Tile.Bounce;
                    if (bounce) VY = -BounceV; else { VY = 0; Grounded = true; }
                }
                else { Y = Math.Floor(Y - HT) + 1 + HT + 1e-4; VY = 0; }
            }
            WallL = Hits(l, X - HW - 0.08, Y - HT * 0.75, X - HW, Y - HT * 0.25, l.Solid);
            WallR = Hits(l, X + HW, Y - HT * 0.75, X + HW + 0.08, Y - HT * 0.25, l.Solid);

            // touching things
            double x0 = X - HW + 0.06, x1 = X + HW - 0.06, y0 = Y - HT + 0.1, y1 = Y - 0.05;
            if (Hits(l, x0, y0, x1, y1, delegate (int x, int y) { return l.At(x, y) == Tile.Lava; })) InLava = true;
            if (Hits(l, x0, y0, x1, y1, delegate (int x, int y) { return l.At(x, y) == Tile.Finish; })) Finished = true;
            for (int x = (int)Math.Floor(x0); x <= (int)Math.Floor(x1); x++)
                for (int y = (int)Math.Floor(y0); y <= (int)Math.Floor(y1); y++)
                    if (l.At(x, y) == Tile.Check && seenChecks.Add(x * 1000 + y)) Checks++;
            if (Y - HT > l.H + 0.5) Fell = true;
        }
    }

    // Evolution: many tries at once, the best ones become the parents of the next generation.
    public class Trainer
    {
        public Level L;
        public readonly List<LabAgent> Pop = new List<LabAgent>();
        public int Gen, PopSize = 40, StallGens;
        public double BestFitness = double.MinValue, BestProgress, MaxT = 10;
        public Net BestNet;
        public bool EverFinished;
        public readonly List<double> History = new List<double>();
        public string LastWhat = "";
        readonly Random r = new Random();

        public Trainer(Level l, Net seed)
        {
            L = l;
            L.ComputeDist();
            for (int i = 0; i < PopSize; i++)
            {
                Net n;
                if (seed == null) n = Net.Random(r);
                else if (i == 0) n = seed.Clone();
                else if (i < PopSize * 0.75) { n = seed.Clone(); n.Mutate(r, 0.15, 0.35); }
                else n = Net.Random(r);
                Pop.Add(new LabAgent(n, L));
            }
        }

        public bool CanWallJump { get { return LabStats.IQ >= LabStats.ParkourIQ; } }

        public LabAgent Leader()
        {
            LabAgent best = null;
            foreach (LabAgent a in Pop)
                if (!a.Dead && (best == null || a.Progress > best.Progress || (a.Progress == best.Progress && a.X > best.X))) best = a;
            if (best == null) foreach (LabAgent a in Pop) if (best == null || a.Progress > best.Progress) best = a;
            return best;
        }

        public int Alive { get { int n = 0; foreach (LabAgent a in Pop) if (!a.Done) n++; return n; } }

        // One small time step for everybody. Returns true when the generation is over.
        public bool Step(double dt)
        {
            bool any = false;
            bool wj = CanWallJump;
            foreach (LabAgent a in Pop) { a.Step(L, dt, wj, MaxT); if (!a.Done) any = true; }
            if (any) return false;
            Evolve();
            return true;
        }

        void Evolve()
        {
            Pop.Sort(delegate (LabAgent a, LabAgent b) { return b.Fitness.CompareTo(a.Fitness); });
            LabAgent top = Pop[0];
            LastWhat = top.Finished ? "finished" : top.InLava ? "lava" : top.Fell ? "fell" : "stuck";
            if (top.Fitness > BestFitness) { BestFitness = top.Fitness; BestNet = top.Brain.Clone(); StallGens = 0; }
            else StallGens++;
            BestProgress = Math.Max(BestProgress, top.Progress);
            if (top.Finished) EverFinished = true;
            History.Add(top.Finished ? 1 : top.Progress);
            if (History.Count > 120) History.RemoveAt(0);
            MaxT = Math.Min(45, 10 + 40 * BestProgress);

            double sigma = StallGens > 8 ? 0.8 : 0.4, rate = StallGens > 8 ? 0.15 : 0.1;
            List<Net> next = new List<Net>();
            for (int i = 0; i < 3 && i < Pop.Count; i++) next.Add(Pop[i].Brain.Clone());
            int immigrants = StallGens > 8 ? 4 : 1;
            while (next.Count < PopSize - immigrants)
            {
                Net a = Pick().Brain, b = Pick().Brain;
                Net child = r.NextDouble() < 0.6 ? Net.Cross(a, b, r) : a.Clone();
                child.Mutate(r, rate, sigma);
                next.Add(child);
            }
            while (next.Count < PopSize) next.Add(Net.Random(r));
            Pop.Clear();
            foreach (Net n in next) Pop.Add(new LabAgent(n, L));
            Gen++;
            LabStats.OnGeneration();
        }

        LabAgent Pick()
        {
            LabAgent best = null;
            for (int i = 0; i < 3; i++)
            {
                LabAgent c = Pop[r.Next(Math.Max(1, Pop.Count * 2 / 3))];
                if (best == null || c.Fitness > best.Fitness) best = c;
            }
            return best;
        }
    }

    // His IQ and his learned brain, saved in %APPDATA%\StickBuddy\lab.txt
    public static class LabStats
    {
        public static double IQ;
        public static int Gens, Beaten;
        public static Net Brain;
        static readonly List<string> beatenLevels = new List<string>();
        public const double WordsIQ = 20, ParkourIQ = 50, TalkIQ = 90;
        static string PathFile { get { return System.IO.Path.Combine(Log.Folder, "lab.txt"); } }
        static bool loaded;

        public static string StageName
        {
            get
            {
                Load();
                if (IQ < WordsIQ) return "baby brain";
                if (IQ < ParkourIQ) return "learning to walk";
                if (IQ < TalkIQ) return "parkour";
                return "talking";
            }
        }

        public static int Stage { get { Load(); return IQ < WordsIQ ? 0 : IQ < ParkourIQ ? 1 : IQ < TalkIQ ? 2 : 3; } }

        public static void Load()
        {
            if (loaded || NoSave) return;
            loaded = true;
            try
            {
                if (!File.Exists(PathFile)) return;
                foreach (string line in File.ReadAllLines(PathFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "IQ") IQ = double.Parse(v, CultureInfo.InvariantCulture);
                    else if (k == "Gens") Gens = int.Parse(v);
                    else if (k == "Beaten") Beaten = int.Parse(v);
                    else if (k == "Levels") beatenLevels.AddRange(v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    else if (k == "Brain") Brain = Net.Load(v);
                }
            }
            catch (Exception ex) { Log.Write("lab load: " + ex.Message); }
        }

        public static bool NoSave;

        public static void Save()
        {
            if (NoSave) return;
            try
            {
                Directory.CreateDirectory(Log.Folder);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("IQ=" + IQ.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("Gens=" + Gens);
                sb.AppendLine("Beaten=" + Beaten);
                sb.AppendLine("Levels=" + string.Join(",", beatenLevels.ToArray()));
                if (Brain != null) sb.AppendLine("Brain=" + Brain.Save());
                File.WriteAllText(PathFile, sb.ToString());
            }
            catch (Exception ex) { Log.Write("lab save: " + ex.Message); }
        }

        public static void OnGeneration()
        {
            Load();
            Gens++;
            IQ += 0.45 * (1 - IQ / 220);
            if (Gens % 5 == 0) Save();
        }

        // Returns true the first time this obby is beaten.
        public static bool OnBeaten(Level l, Net brain)
        {
            Load();
            Brain = brain.Clone();
            string h = l.Hash();
            bool first = !beatenLevels.Contains(h);
            if (first)
            {
                beatenLevels.Add(h);
                Beaten++;
                IQ += 14 * (1 - IQ / 260);
            }
            Save();
            return first;
        }

        public static void ResetBrain()
        {
            IQ = 0; Gens = 0; Beaten = 0; Brain = null; beatenLevels.Clear();
            loaded = true;
            Save();
        }
    }
}
