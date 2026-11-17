using System;
using System.Collections.Generic;
using System.Drawing;

namespace StickBuddy
{
    // What happened during one ragdoll step: how hard he hit things.
    public class RagHit
    {
        public double FloorSpeed, WallSpeed, CeilSpeed, MaxSpeed;
        public double WallX, WallY, FloorX, FloorY;
        public int WallSide;
        public bool Supported;
    }

    // A floppy stick figure: 11 points held together by sticks (Verlet physics).
    // Used when you pick him up, throw him, or when he falls over.
    public class Ragdoll
    {
        public const int Hip = 0, Neck = 1, Head = 2, ElbowA = 3, HandA = 4, ElbowB = 5, HandB = 6, KneeA = 7, FootA = 8, KneeB = 9, FootB = 10, N = 11;

        public readonly double[] X = new double[N], Y = new double[N], PX = new double[N], PY = new double[N];
        public readonly double[] FX = new double[N], FY = new double[N];
        readonly double[] ox = new double[N], oy = new double[N], rad = new double[N];
        static readonly double[] Inv = { 1 / 3.0, 1 / 2.4, 1 / 1.5, 1 / 0.7, 1 / 0.5, 1 / 0.7, 1 / 0.5, 1 / 1.0, 1 / 0.8, 1 / 1.0, 1 / 0.8 };

        class Stick { public int A, B; public double Min, Max, K; }
        readonly List<Stick> sticks = new List<Stick>();

        public double U, Gravity;
        public int Pin1 = -1, Pin2 = -1;
        public double PinX, PinY;
        double pinFromX, pinFromY;
        bool pinInit;
        public bool Limp;

        public Ragdoll(double u, double gravity)
        {
            U = u; Gravity = gravity;
            Rigid(Hip, Neck, Poses.SP); Rigid(Neck, Head, Poses.NK);
            Rigid(Neck, ElbowA, Poses.UA); Rigid(ElbowA, HandA, Poses.FA);
            Rigid(Neck, ElbowB, Poses.UA); Rigid(ElbowB, HandB, Poses.FA);
            Rigid(Hip, KneeA, Poses.TH); Rigid(KneeA, FootA, Poses.SH);
            Rigid(Hip, KneeB, Poses.TH); Rigid(KneeB, FootB, Poses.SH);
            // softer sticks that stop him folding up like a piece of string
            Soft(Hip, Head, (Poses.SP + Poses.NK) * 0.88, Poses.SP + Poses.NK, 0.5);
            Soft(Hip, FootA, (Poses.TH + Poses.SH) * 0.45, Poses.TH + Poses.SH, 1);
            Soft(Hip, FootB, (Poses.TH + Poses.SH) * 0.45, Poses.TH + Poses.SH, 1);
            Soft(Neck, HandA, (Poses.UA + Poses.FA) * 0.3, Poses.UA + Poses.FA, 1);
            Soft(Neck, HandB, (Poses.UA + Poses.FA) * 0.3, Poses.UA + Poses.FA, 1);
            Soft(Neck, KneeA, 20, 60, 0.5);
            Soft(Neck, KneeB, 20, 60, 0.5);
            Soft(KneeA, KneeB, 0, 40, 0.3);
            for (int i = 0; i < N; i++) rad[i] = 3.2 * u;
            rad[Head] = Poses.HR * u;
        }

        void Rigid(int a, int b, double len) { Stick s = new Stick(); s.A = a; s.B = b; s.Min = len * U; s.Max = len * U; s.K = 1; sticks.Add(s); }
        void Soft(int a, int b, double min, double max, double k) { Stick s = new Stick(); s.A = a; s.B = b; s.Min = min * U; s.Max = max * U; s.K = k; sticks.Add(s); }

        public void SetJoints(Joints j, double vx, double vy, double dt)
        {
            PointF[] a = j.ToArray();
            for (int i = 0; i < N; i++)
            {
                X[i] = a[i].X; Y[i] = a[i].Y;
                PX[i] = X[i] - vx * dt; PY[i] = Y[i] - vy * dt;
            }
            pinInit = false;
        }

        public Joints GetJoints()
        {
            PointF[] a = new PointF[N];
            for (int i = 0; i < N; i++) a[i] = new PointF((float)X[i], (float)Y[i]);
            return Joints.FromArray(a, (float)(Poses.HR * U));
        }

        public int Nearest(double x, double y)
        {
            int best = Hip; double bd = double.MaxValue;
            for (int i = 0; i < N; i++)
            {
                double dx = X[i] - x, dy = Y[i] - y, d = dx * dx + dy * dy;
                if (i == Hip || i == Neck) d *= 0.7; // easier to grab by the body
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public void AddVelocity(double vx, double vy, double dt)
        {
            for (int i = 0; i < N; i++) { PX[i] -= vx * dt; PY[i] -= vy * dt; }
        }

        bool Pinned(int i) { return i == Pin1 || i == Pin2; }

        public RagHit Step(double dt, List<Plat> plats, Rectangle vs)
        {
            RagHit hit = new RagHit();
            int n = Math.Max(1, (int)Math.Ceiling(dt / (1 / 120.0)));
            double h = dt / n;
            if (!pinInit) { pinFromX = PinX; pinFromY = PinY; pinInit = true; }
            for (int s = 0; s < n; s++)
            {
                double k = (s + 1.0) / n;
                double px = pinFromX + (PinX - pinFromX) * k, py = pinFromY + (PinY - pinFromY) * k;
                for (int i = 0; i < N; i++)
                {
                    ox[i] = X[i]; oy[i] = Y[i];
                    double vx = (X[i] - PX[i]) * 0.999, vy = (Y[i] - PY[i]) * 0.999;
                    PX[i] = X[i]; PY[i] = Y[i];
                    if (Pinned(i)) { X[i] = px; Y[i] = py; continue; }
                    X[i] += vx + FX[i] * h * h;
                    Y[i] += vy + (Gravity + FY[i]) * h * h;
                }
                for (int it = 0; it < 8; it++)
                {
                    foreach (Stick st in sticks)
                    {
                        double dx = X[st.B] - X[st.A], dy = Y[st.B] - Y[st.A];
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < 1e-6) { dx = 0.01; d = 0.01; }
                        double target = d < st.Min ? st.Min : d > st.Max ? st.Max : d;
                        if (target == d) continue;
                        double wa = Pinned(st.A) ? 0 : Inv[st.A], wb = Pinned(st.B) ? 0 : Inv[st.B];
                        if (wa + wb <= 0) continue;
                        double diff = (d - target) / d * st.K;
                        X[st.A] += dx * diff * wa / (wa + wb); Y[st.A] += dy * diff * wa / (wa + wb);
                        X[st.B] -= dx * diff * wb / (wa + wb); Y[st.B] -= dy * diff * wb / (wa + wb);
                    }
                }
                Collide(h, plats, vs, hit);
            }
            pinFromX = PinX; pinFromY = PinY;
            for (int i = 0; i < N; i++)
            {
                FX[i] = 0; FY[i] = 0;
                double sp = Math.Sqrt((X[i] - PX[i]) * (X[i] - PX[i]) + (Y[i] - PY[i]) * (Y[i] - PY[i])) / h;
                if (sp > hit.MaxSpeed) hit.MaxSpeed = sp;
            }
            return hit;
        }

        void Collide(double h, List<Plat> plats, Rectangle vs, RagHit hit)
        {
            for (int i = 0; i < N; i++)
            {
                if (Pinned(i)) continue;
                double r = rad[i];
                // platforms: the floor, window tops, the floors of his house (you only land on them from above)
                foreach (Plat p in plats)
                {
                    if (X[i] < p.L || X[i] > p.R) continue;
                    double surf = p.Y - r;
                    if (oy[i] <= surf + 0.6 && Y[i] > surf)
                    {
                        double vimp = Y[i] - PY[i];
                        Y[i] = surf;
                        PY[i] = Y[i] + vimp * 0.22;
                        PX[i] = X[i] - (X[i] - PX[i]) * 0.82;
                        double sp = vimp / h;
                        if (sp > hit.FloorSpeed) { hit.FloorSpeed = sp; hit.FloorX = X[i]; hit.FloorY = p.Y; }
                        hit.Supported = true;
                    }
                    else if (Math.Abs(Y[i] - surf) < 1.2) hit.Supported = true;
                }
                if (Y[i] > vs.Bottom - r) { Y[i] = vs.Bottom - r; PY[i] = Y[i]; hit.Supported = true; }
                if (X[i] < vs.Left + r)
                {
                    double vimp = X[i] - PX[i];
                    X[i] = vs.Left + r; PX[i] = X[i] + vimp * 0.3;
                    double sp = -vimp / h;
                    if (sp > hit.WallSpeed) { hit.WallSpeed = sp; hit.WallSide = -1; hit.WallX = vs.Left; hit.WallY = Y[i]; }
                }
                else if (X[i] > vs.Right - r)
                {
                    double vimp = X[i] - PX[i];
                    X[i] = vs.Right - r; PX[i] = X[i] + vimp * 0.3;
                    double sp = vimp / h;
                    if (sp > hit.WallSpeed) { hit.WallSpeed = sp; hit.WallSide = 1; hit.WallX = vs.Right; hit.WallY = Y[i]; }
                }
                if (Y[i] < vs.Top + r)
                {
                    double vimp = Y[i] - PY[i];
                    Y[i] = vs.Top + r; PY[i] = Y[i] + vimp * 0.3;
                    double sp = -vimp / h;
                    if (sp > hit.CeilSpeed) { hit.CeilSpeed = sp; hit.WallX = X[i]; hit.WallY = vs.Top; hit.WallSide = 0; }
                }
            }
        }

        public double ComX() { double s = 0; for (int i = 0; i < N; i++) s += X[i]; return s / N; }
        public double ComY() { double s = 0; for (int i = 0; i < N; i++) s += Y[i]; return s / N; }

        public double LowestY()
        {
            double m = double.MinValue;
            for (int i = 0; i < N; i++) m = Math.Max(m, Y[i] + rad[i]);
            return m;
        }
    }
}
