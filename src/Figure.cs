using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace StickBuddy
{
    public enum Act
    {
        Idle, Walk, Run, Jump, Fall, Wave, Think, Talk, Listen, Write, Type, Stomp, Angry, Tantrum,
        Sit, Yawn, Sleep, Land, GrabIdle, GrabRun, Play, Cheer, Sad, Point, Shrug,
        Lie, Kneel, Crouch, Roll, Slide, Climb, Dive, PushUp, Squat, JumpJack, Dribble, Shoot,
        DrawSit, Watch, CouchSit, Peek, Throw, Dust, Laugh, Scared, Curl, Warn,
        DanceGroove, DanceDisco, DanceRunning, DanceRobot, DanceWave, DanceMoon, DanceSpin
    }

    public enum Prop { None, Pencil, Controller, Popcorn }

    // One body position. Angles are in degrees: 0 = pointing down, positive = towards where he faces,
    // 180 = straight up. Spine 0 = standing straight. Arm and leg angles are measured against the world,
    // not against the parent bone.
    public class Pose
    {
        public double Spine, Head, AU, AF, BU, BF, LAT, LAS, LBT, LBS, Lift;
        public double HipY = 44;      // how high the hip is above the ground (in body units)
        public bool Grounded = true;  // true = work out HipY so the lowest foot touches the ground

        public Pose Clone() { return (Pose)MemberwiseClone(); }

        public static Pose Lerp(Pose a, Pose b, double t)
        {
            Pose p = new Pose();
            p.Spine = L(a.Spine, b.Spine, t); p.Head = L(a.Head, b.Head, t);
            p.AU = L(a.AU, b.AU, t); p.AF = L(a.AF, b.AF, t);
            p.BU = L(a.BU, b.BU, t); p.BF = L(a.BF, b.BF, t);
            p.LAT = L(a.LAT, b.LAT, t); p.LAS = L(a.LAS, b.LAS, t);
            p.LBT = L(a.LBT, b.LBT, t); p.LBS = L(a.LBS, b.LBS, t);
            p.Lift = L(a.Lift, b.Lift, t); p.HipY = L(a.HipY, b.HipY, t);
            p.Grounded = false;
            return p;
        }

        static double L(double a, double b, double t) { return a + (b - a) * t; }

        // Moves every angle of "a" to the same turn as "near" (so -170 and 190 count as the same angle).
        public static void Unwrap(Pose a, Pose near)
        {
            a.Spine = U(a.Spine, near.Spine); a.Head = U(a.Head, near.Head);
            a.AU = U(a.AU, near.AU); a.AF = U(a.AF, near.AF); a.BU = U(a.BU, near.BU); a.BF = U(a.BF, near.BF);
            a.LAT = U(a.LAT, near.LAT); a.LAS = U(a.LAS, near.LAS); a.LBT = U(a.LBT, near.LBT); a.LBS = U(a.LBS, near.LBS);
        }

        static double U(double v, double near)
        {
            while (v - near > 180) v -= 360;
            while (v - near < -180) v += 360;
            return v;
        }
    }

    // All the animations, made from sine waves.
    public static class Poses
    {
        public const double SP = 32, NK = 13, HR = 10.5, UA = 17, FA = 16, TH = 22, SH = 22;

        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double Sm(double t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
        static double Lp(double a, double b, double t) { return a + (b - a) * t; }
        static double Deg(double rad) { return rad * 180 / Math.PI; }

        // "param" is extra info for a few poses (how fast he is being swung, how far a move has got).
        public static Pose Get(Act a, double t, double param)
        {
            Pose p = new Pose();
            double br = Math.Sin(t * 2.1), sway = Math.Sin(t * 0.55);
            p.Spine = 2 + br * 0.8 + sway * 1.2; p.Head = -1 + br * 1.0;
            p.AU = 9 + br * 2.5; p.AF = 16 + br * 3.5; p.BU = -9 - br * 2.5; p.BF = -4 - br * 2.5;
            p.LAT = 7 + sway * 2.5; p.LAS = 2 + sway * 1.5; p.LBT = -7 + sway * 2.5; p.LBS = -3 + sway * 1.5;

            switch (a)
            {
                case Act.Walk:
                    {
                        double ph = t * Math.PI * 2 * 1.75, s = Math.Sin(ph), c = Math.Cos(ph);
                        p.LAT = 27 * s; p.LAS = p.LAT - 48 * Math.Max(0, c) - 3;
                        p.LBT = -27 * s; p.LBS = p.LBT - 48 * Math.Max(0, -c) - 3;
                        p.AU = -22 * s; p.AF = p.AU + 20 + 12 * Math.Max(0, -s);
                        p.BU = 22 * s; p.BF = p.BU + 20 + 12 * Math.Max(0, s);
                        p.Spine = 5 + 1.5 * Math.Abs(c); p.Head = -3 - 1.5 * Math.Abs(c);
                        break;
                    }
                case Act.Run:
                case Act.GrabRun:
                    {
                        double ph = t * Math.PI * 2 * 2.7, s = Math.Sin(ph), c = Math.Cos(ph);
                        p.LAT = 48 * s + 8; p.LAS = p.LAT - 30 - 75 * Math.Max(0, c);
                        p.LBT = -48 * s + 8; p.LBS = p.LBT - 30 - 75 * Math.Max(0, -c);
                        p.AU = -55 * s; p.AF = p.AU + 95; p.BU = 55 * s; p.BF = p.BU + 95;
                        p.Spine = 18 + 3 * Math.Abs(c); p.Head = -14;
                        p.Lift = 4 * Math.Abs(c);
                        if (a == Act.GrabRun) { p.AU = 172; p.AF = 178; p.BU = 160 + 6 * s; p.BF = 182; p.Spine = 4; p.Head = -20; }
                        break;
                    }
                case Act.Jump:
                    p.Grounded = false; p.HipY = 46;
                    p.LAT = 78; p.LAS = -8; p.LBT = 48; p.LBS = -38;
                    p.AU = 150 + 8 * Math.Sin(t * 8); p.AF = 166; p.BU = 128; p.BF = 150;
                    p.Spine = 6; p.Head = -8;
                    break;
                case Act.Fall:
                    p.Grounded = false; p.HipY = 46;
                    p.LAT = 25; p.LAS = 4; p.LBT = -8; p.LBS = -22;
                    p.AU = 115 + 14 * Math.Sin(t * 9); p.AF = 140 + 14 * Math.Sin(t * 9 + 1);
                    p.BU = -110 + 14 * Math.Sin(t * 8 + 2); p.BF = -135 + 14 * Math.Sin(t * 8 + 3);
                    p.Spine = -2; p.Head = -6;
                    break;
                case Act.Land:
                    p.LAT = 75; p.LAS = -32; p.LBT = 52; p.LBS = -52; p.Spine = 30; p.Head = -12;
                    p.AU = 55; p.AF = 75; p.BU = -35; p.BF = -25;
                    break;
                case Act.Wave:
                    p.AU = 150; p.AF = 168 + 22 * Math.Sin(t * 10); p.Head = 6 + 3 * Math.Sin(t * 5);
                    p.Spine = -2 + 2 * Math.Sin(t * 5);
                    break;
                case Act.Think:
                    p.AU = 15; p.AF = 170; p.BU = -35; p.BF = 50; p.Head = -12; p.Spine = -2;
                    break;
                case Act.Talk:
                    p.AU = 35 + 12 * Math.Sin(t * 3); p.AF = 80 + 18 * Math.Sin(t * 3 + 1);
                    p.BU = -12 + 5 * Math.Sin(t * 2.3); p.BF = 10 + 10 * Math.Sin(t * 2.3 + 1);
                    p.Head = 3 * Math.Sin(t * 4.1);
                    break;
                case Act.Listen:
                    p.BU = -40; p.BF = -172; p.Head = -8; p.Spine = 4;
                    break;
                case Act.Write:
                    p.LAT = 65; p.LAS = -25; p.LBT = 40; p.LBS = -45;
                    p.Spine = 38; p.Head = 18;
                    p.AU = 45 + 8 * Math.Sin(t * 21); p.AF = 15 + 18 * Math.Sin(t * 16);
                    p.BU = 20; p.BF = 70;
                    break;
                case Act.Type:
                    p.Spine = 10; p.Head = 12;
                    p.AU = 50 + 7 * Math.Sin(t * 19); p.AF = 85 + 12 * Math.Sin(t * 23 + 1);
                    p.BU = 42 + 7 * Math.Sin(t * 21 + 2); p.BF = 82 + 12 * Math.Sin(t * 25 + 3);
                    break;
                case Act.Stomp:
                    p.LAT = 40; p.LAS = -20; p.LBT = 20; p.LBS = -40; p.Spine = 28; p.Head = 12;
                    p.AU = 15; p.AF = 5; p.BU = -40; p.BF = -60;
                    break;
                case Act.Angry:
                    {
                        double tap = Math.Max(0, Math.Sin(t * 13));
                        p.AU = 30; p.AF = -80; p.BU = -25; p.BF = 80;
                        p.LAT = 12 + 12 * tap; p.LAS = 12 - 8 * tap; p.LBT = -6; p.LBS = -2;
                        p.Spine = 2 * Math.Sin(t * 37); p.Head = 8 + 2 * Math.Sin(t * 30);
                        break;
                    }
                case Act.Warn:
                    {
                        // finger wagging: "don't you dare!"
                        p.AU = 120; p.AF = 165 + 18 * Math.Sin(t * 14); p.BU = -30; p.BF = 70;
                        p.Spine = 8; p.Head = 6; p.LAT = 18; p.LAS = 8; p.LBT = -10; p.LBS = -4;
                        break;
                    }
                case Act.Tantrum:
                    {
                        double st = Math.Sin(t * Math.PI * 2 * 2.5);
                        p.LAT = 50 * Math.Max(0, st); p.LAS = 0;
                        p.LBT = 50 * Math.Max(0, -st); p.LBS = 0;
                        p.AU = 150 + 15 * Math.Sin(t * 31); p.AF = 175; p.BU = 140 + 15 * Math.Sin(t * 29); p.BF = 170;
                        p.Spine = 6; p.Head = 12;
                        break;
                    }
                case Act.Sit:
                case Act.Yawn:
                    p.Grounded = false; p.HipY = 9.5;
                    p.LAT = 118; p.LAS = 42; p.LBT = 110; p.LBS = 38;
                    p.Spine = -10 + 1.5 * br; p.Head = 4 + 3 * Math.Sin(t * 0.7);
                    p.AU = 40; p.AF = 80; p.BU = 30; p.BF = 72;
                    if (a == Act.Yawn) { p.Head = -22; p.AU = 60; p.AF = 165; p.Spine = -14; }
                    break;
                case Act.Sleep:
                    {
                        double b2 = Math.Sin(t * 1.6);
                        p.Grounded = false; p.HipY = 6;
                        p.Spine = -82 + b2 * 1.2; p.Head = 2;
                        p.LAT = 80; p.LAS = 90; p.LBT = 84; p.LBS = 88;
                        p.AU = 95; p.AF = 60 + b2 * 3; p.BU = 85; p.BF = 105;
                        break;
                    }
                case Act.GrabIdle:
                    p.AU = 172; p.AF = 178; p.BU = 165; p.BF = 182; p.Head = -18;
                    p.Lift = Math.Abs(Math.Sin(t * 5)) * 2;
                    break;
                case Act.Play:
                    p.AU = 55; p.AF = 95 + 7 * Math.Sin(t * 14); p.BU = 45; p.BF = 92 + 7 * Math.Sin(t * 13 + 1);
                    p.Spine = 8; p.Head = 6; p.Lift = Math.Abs(Math.Sin(t * 4)) * 2.5;
                    break;
                case Act.Cheer:
                    {
                        double ph = t * Math.PI * 2 * 1.4, k = Math.Abs(Math.Sin(ph));
                        p.Lift = k * 16;
                        p.AU = 160 + 10 * Math.Sin(ph * 2); p.AF = 172; p.BU = -160 - 10 * Math.Sin(ph * 2); p.BF = -172;
                        p.LAT = 8 + 35 * k; p.LAS = 3 - 20 * k; p.LBT = -8 + 25 * k; p.LBS = -3 - 30 * k;
                        p.Head = -10;
                        break;
                    }
                case Act.Sad:
                    p.Spine = 14 + br; p.Head = 26; p.AU = 4; p.AF = 2; p.BU = -4; p.BF = -2;
                    p.LAT = 4; p.LAS = 2; p.LBT = -4; p.LBS = -2;
                    break;
                case Act.Point:
                    p.AU = 88; p.AF = 92; p.Head = -4;
                    break;
                case Act.Shrug:
                    p.AU = 40; p.AF = 125; p.BU = -40; p.BF = -125; p.Head = 12 * Math.Sin(t * 2); p.Spine = 0;
                    break;

                // ---------- falling over and getting up ----------
                case Act.Lie:
                    p.Grounded = false; p.HipY = 6;
                    p.Spine = -82 + 0.8 * br; p.Head = -10;
                    p.LAT = 80; p.LAS = 92; p.LBT = 72; p.LBS = 96;
                    p.AU = -95; p.AF = -100; p.BU = 60; p.BF = 85;
                    break;
                case Act.Kneel:
                    p.LAT = 80; p.LAS = 0; p.LBT = 5; p.LBS = -88;
                    p.Spine = 25; p.Head = -8; p.AU = 45; p.AF = 25; p.BU = -20; p.BF = 10;
                    break;
                case Act.Crouch:
                    p.LAT = 85; p.LAS = -30; p.LBT = 70; p.LBS = -50; p.Spine = 38; p.Head = -18;
                    p.AU = 70; p.AF = 50; p.BU = 40; p.BF = 20;
                    break;

                // ---------- parkour ----------
                case Act.Roll:
                    p.Grounded = false; p.HipY = 24;
                    p.Spine = 75; p.Head = 20; p.LAT = 135; p.LAS = 30; p.LBT = 125; p.LBS = 20;
                    p.AU = 110; p.AF = 150; p.BU = 100; p.BF = 140;
                    break;
                case Act.Slide:
                    p.Spine = -38; p.Head = 20; p.LAT = 84; p.LAS = 90; p.LBT = 78; p.LBS = -82;
                    p.AU = -50; p.AF = -10; p.BU = 135; p.BF = 165;
                    break;
                case Act.Climb:
                    {
                        double s = Math.Sin(t * Math.PI * 2 * 1.5);
                        p.Grounded = false; p.HipY = 40;
                        p.Spine = 6; p.Head = -12;
                        p.AU = 155 + 22 * s; p.AF = 172 + 8 * s; p.BU = 155 - 22 * s; p.BF = 172 - 8 * s;
                        p.LAT = 52 - 18 * s; p.LAS = -8 - 12 * s; p.LBT = 52 + 18 * s; p.LBS = -8 + 12 * s;
                        break;
                    }
                case Act.Dive:
                    p.Grounded = false; p.HipY = 30;
                    p.Spine = 84; p.Head = -6; p.AU = 95; p.AF = 98; p.BU = 88; p.BF = 94;
                    p.LAT = -82; p.LAS = -88; p.LBT = -94; p.LBS = -98;
                    break;

                // ---------- sport ----------
                case Act.PushUp:
                    {
                        double k = 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI / 1.1);
                        p.Grounded = false;
                        p.HipY = 21 - 10 * k;
                        p.Spine = 76 + 7 * k;
                        double neckH = p.HipY + SP * Math.Cos(R(p.Spine));
                        double an = Deg(Math.Acos(Clamp(neckH / (UA + FA), -1, 1)));
                        p.AU = -an + 8; p.AF = an + 8; p.BU = -an + 4; p.BF = an + 4;
                        double l = Deg(Math.Acos(Clamp(p.HipY / (TH + SH), -1, 1)));
                        p.LAT = -l; p.LAS = -l; p.LBT = -l - 3; p.LBS = -l - 3;
                        p.Head = -8;
                        break;
                    }
                case Act.Squat:
                    {
                        double k = 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI / 1.4);
                        p.LAT = 8 + 82 * k; p.LAS = 2 - 28 * k; p.LBT = p.LAT - 6; p.LBS = p.LAS - 4;
                        p.Spine = 4 + 30 * k; p.Head = -4 - 10 * k;
                        p.AU = 20 + 70 * k; p.AF = p.AU + 5; p.BU = p.AU - 8; p.BF = p.AF - 8;
                        break;
                    }
                case Act.JumpJack:
                    {
                        double ph = t * 2 * Math.PI / 0.8, k = 0.5 - 0.5 * Math.Cos(ph);
                        p.AU = 10 + 160 * k; p.AF = 15 + 165 * k; p.BU = -(10 + 160 * k); p.BF = -(15 + 165 * k);
                        p.LAT = 4 + 20 * k; p.LAS = 2 + 18 * k; p.LBT = -(4 + 20 * k); p.LBS = -(2 + 18 * k);
                        p.Lift = 8 * Math.Abs(Math.Sin(ph)); p.Spine = 0; p.Head = -4;
                        break;
                    }
                case Act.Curl:
                    {
                        double k = 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI / 1.2);
                        p.AU = 12; p.AF = 12 + 140 * k; p.BU = -10; p.BF = -6;
                        p.Spine = -2 - 3 * k; p.Head = -4 - 4 * k;
                        p.LAT = 10; p.LAS = 4; p.LBT = -10; p.LBS = -6;
                        break;
                    }
                case Act.Dribble:
                    {
                        double hd = 0.5 + 0.5 * Math.Cos(t * 2 * Math.PI / 0.5);
                        p.Spine = 14; p.Head = -6; p.LAT = 30; p.LAS = -12; p.LBT = -12; p.LBS = -28;
                        p.AU = 30 + 12 * (1 - hd); p.AF = 60 + 30 * (1 - hd); p.BU = -25; p.BF = 20;
                        break;
                    }
                case Act.Shoot:
                    {
                        double q = t / 0.9;
                        if (q < 0.35)
                        {
                            double k = Sm(q / 0.35);
                            p.LAT = 10 + 45 * k; p.LAS = 5 - 30 * k; p.LBT = p.LAT - 8; p.LBS = p.LAS - 6;
                            p.Spine = 4 + 14 * k; p.AU = 60 + 40 * k; p.AF = 120 + 30 * k; p.BU = p.AU - 10; p.BF = p.AF - 10;
                        }
                        else if (q < 0.6)
                        {
                            double k = Sm((q - 0.35) / 0.25);
                            p.LAT = 55 - 50 * k; p.LAS = -25 + 25 * k; p.LBT = p.LAT - 8; p.LBS = p.LAS - 6;
                            p.Lift = 18 * Math.Sin(k * Math.PI / 2); p.Spine = 18 - 18 * k;
                            p.AU = 100 + 65 * k; p.AF = 150 + 25 * k; p.BU = p.AU - 8; p.BF = p.AF - 6;
                        }
                        else
                        {
                            double k = Sm((q - 0.6) / 0.4);
                            p.Lift = 18 * (1 - k); p.LAT = 5; p.LAS = 0; p.LBT = -5; p.LBS = -6;
                            p.AU = 165; p.AF = 175 - 60 * k; p.BU = 160; p.BF = 170 - 50 * k; p.Spine = 0;
                        }
                        p.Head = -14;
                        break;
                    }

                // ---------- at home ----------
                case Act.DrawSit:
                    p.Grounded = false; p.HipY = 21;
                    p.Spine = 12; p.Head = 8;
                    p.LAT = 92; p.LAS = 4; p.LBT = 86; p.LBS = -2;
                    p.AU = 72 + 5 * Math.Sin(t * 9); p.AF = 100 + 14 * Math.Sin(t * 13);
                    p.BU = 25; p.BF = 70;
                    break;
                case Act.Watch:
                    {
                        p.Grounded = false; p.HipY = 7;
                        p.Spine = -14 + br * 0.8; p.Head = -22;
                        p.LAT = 120; p.LAS = 32; p.LBT = 112; p.LBS = 26;
                        p.BU = 55; p.BF = 85;
                        // a handful of popcorn every few seconds
                        double cyc = t % 3.2, e = cyc < 0.9 ? Math.Sin(cyc / 0.9 * Math.PI) : 0;
                        p.AU = 45 + 85 * e; p.AF = 15 + 160 * e;
                        break;
                    }
                case Act.CouchSit:
                    p.Grounded = false; p.HipY = 24;
                    p.Spine = -18 + br; p.Head = -6;
                    p.LAT = 96; p.LAS = 10; p.LBT = 92; p.LBS = 4;
                    p.AU = 30; p.AF = 70; p.BU = -70; p.BF = -30;
                    break;
                case Act.Peek:
                    p.Spine = 12; p.Head = 8 + 6 * Math.Sin(t * 1.3);
                    p.LAT = 100; p.LAS = -40; p.LBT = 92; p.LBS = -48;
                    p.AU = 140; p.AF = 95; p.BU = 128; p.BF = 90;
                    break;
                case Act.Throw:
                    {
                        if (t < 0.32)
                        {
                            double k = Sm(t / 0.32);
                            p.Spine = -14 * k; p.AU = 300 - 100 * k; p.AF = 270 - 80 * k; p.BU = 60 * k; p.BF = 80 * k;
                            p.LAT = 25 * k; p.LAS = -5; p.LBT = -20 * k; p.LBS = -25 * k;
                        }
                        else
                        {
                            double k = Sm((t - 0.32) / 0.2);
                            p.Spine = -14 + 44 * k; p.AU = 200 - 100 * k; p.AF = 190 - 105 * k; p.BU = 60 - 100 * k; p.BF = 80 - 100 * k;
                            p.LAT = 25 + 15 * k; p.LAS = -5 - 10 * k; p.LBT = -20 - 15 * k; p.LBS = -25 - 25 * k;
                        }
                        p.Head = -6;
                        break;
                    }
                case Act.Dust:
                    p.AU = 45 + 12 * Math.Sin(t * 22); p.AF = 95 + 10 * Math.Sin(t * 22);
                    p.BU = 40 - 12 * Math.Sin(t * 22); p.BF = 90; p.Spine = 4; p.Head = 10;
                    break;
                case Act.Laugh:
                    p.Spine = -10 + 8 * Math.Sin(t * 12); p.Head = -15 + 6 * Math.Sin(t * 12 + 0.5);
                    p.AU = 30; p.AF = 100; p.BU = 25; p.BF = 95;
                    p.Lift = 2 * Math.Abs(Math.Sin(t * 12));
                    break;
                case Act.Scared:
                    {
                        double sh = Math.Sin(t * 40) * 2;
                        p.Spine = 30 + sh; p.Head = 20; p.LAT = 60; p.LAS = -20; p.LBT = 45; p.LBS = -35;
                        p.AU = 150; p.AF = 230; p.BU = 140; p.BF = 220;
                        break;
                    }

                // ---------- dancing (2 beats a second) ----------
                case Act.DanceGroove:
                    {
                        double k = 0.5 + 0.5 * Math.Cos(t * 2 * Math.PI * 2);
                        double s = Math.Sin(t * Math.PI * 2);
                        p.LAT = 12 + 22 * k; p.LAS = -2 - 22 * k; p.LBT = 2 + 22 * k; p.LBS = -10 - 22 * k;
                        p.Spine = 6 + 8 * k; p.Head = -6 + 14 * k;
                        p.AU = 55 + 45 * s; p.AF = p.AU + 70;
                        p.BU = 55 - 45 * s; p.BF = p.BU + 70;
                        break;
                    }
                case Act.DanceDisco:
                    {
                        double u = 0.5 + 0.5 * Math.Sin(t * Math.PI * 2);
                        double e = Sm((u - 0.2) / 0.6);
                        double sw = Math.Sin(t * Math.PI * 2);
                        p.AU = Lp(25, 150, e); p.AF = Lp(-25, 160, e);
                        p.BU = -35; p.BF = 55;
                        p.Spine = 8 * sw; p.Head = -8 * e + 6;
                        p.LAT = 10 + 8 * sw; p.LAS = 4; p.LBT = -10 + 8 * sw; p.LBS = -6;
                        p.Lift = 2 * (1 - Math.Abs(sw));
                        break;
                    }
                case Act.DanceRunning:
                    {
                        double ph = t * Math.PI * 2, s = Math.Sin(ph), c = Math.Cos(ph);
                        double ka = Math.Max(0, s), kb = Math.Max(0, -s);
                        p.LAT = 70 * ka - 25 * kb; p.LAS = p.LAT - 85 * ka - 10 * kb;
                        p.LBT = 70 * kb - 25 * ka; p.LBS = p.LBT - 85 * kb - 10 * ka;
                        p.AU = 40 - 45 * s; p.AF = p.AU + 90; p.BU = 40 + 45 * s; p.BF = p.BU + 90;
                        p.Spine = 10; p.Head = -4 + 6 * Math.Abs(c); p.Lift = 3 * Math.Abs(c);
                        break;
                    }
                case Act.DanceRobot:
                    {
                        double[,] K =
                        {
                            { 90, 180, 0, 90, 15 }, { 90, 90, 0, 0, 0 }, { 0, 90, 90, 180, -15 }, { 90, 0, 90, 180, 0 },
                            { 180, 90, 0, -90, 15 }, { 90, 90, 90, 90, 0 }, { 45, 135, -45, 45, -10 }, { 90, 180, 90, 0, 10 }
                        };
                        double q = t * 4;
                        int k1 = (int)Math.Floor(q) % 8, k0 = (k1 + 7) % 8;
                        double e = Sm((q - Math.Floor(q)) / 0.25);
                        p.AU = Lp(K[k0, 0], K[k1, 0], e); p.AF = Lp(K[k0, 1], K[k1, 1], e);
                        p.BU = Lp(K[k0, 2], K[k1, 2], e); p.BF = Lp(K[k0, 3], K[k1, 3], e);
                        p.Head = Lp(K[k0, 4], K[k1, 4], e); p.Spine = 0;
                        p.LAT = 4; p.LAS = 0; p.LBT = -4; p.LBS = -2;
                        if (k1 == 4 || k1 == 5) { p.LAT = 22; p.LAS = -10; p.LBT = 12; p.LBS = -18; }
                        break;
                    }
                case Act.DanceWave:
                    {
                        double ph = t * Math.PI * 2 * 0.9;
                        p.AU = 92 + 22 * Math.Sin(ph); p.AF = 92 + 32 * Math.Sin(ph - 0.9);
                        p.BU = -92 + 22 * Math.Sin(ph - 1.8); p.BF = -92 + 32 * Math.Sin(ph - 2.7);
                        p.Head = 14 * Math.Sin(ph - 3.2); p.Spine = 9 * Math.Sin(ph - 3.8);
                        double k = 0.5 + 0.5 * Math.Sin(ph - 4.4);
                        p.LAT = 8 + 20 * k; p.LAS = -4 - 18 * k; p.LBT = -4 + 20 * k; p.LBS = -10 - 18 * k;
                        break;
                    }
                case Act.DanceMoon:
                    {
                        double s = Math.Sin(t * Math.PI * 2);
                        double ka = Math.Max(0, s), kb = Math.Max(0, -s);
                        p.LAT = 4 + 26 * ka - 10 * kb; p.LAS = p.LAT - 4 - 40 * ka;
                        p.LBT = 4 + 26 * kb - 10 * ka; p.LBS = p.LBT - 4 - 40 * kb;
                        p.Spine = 4; p.Head = -4; p.AU = 20 + 10 * s; p.AF = 40; p.BU = -20 + 10 * s; p.BF = -5;
                        break;
                    }
                case Act.DanceSpin:
                    p.AU = 95; p.AF = 120; p.BU = -95; p.BF = -120;
                    p.LAT = 3; p.LAS = 0; p.LBT = -3; p.LBS = 0; p.Lift = 2 + 2 * Math.Sin(t * 12);
                    p.Head = -10; p.Spine = 0;
                    break;
            }
            Resolve(p);
            return p;
        }

        // Works out how high the hip must be so the lowest foot touches the floor.
        public static void Resolve(Pose p)
        {
            if (!p.Grounded) return;
            double fa = Math.Cos(R(p.LAT)) * TH + Math.Cos(R(p.LAS)) * SH;
            double fb = Math.Cos(R(p.LBT)) * TH + Math.Cos(R(p.LBS)) * SH;
            p.HipY = Math.Max(fa, fb);
            p.Grounded = false;
        }

        public static double R(double deg) { return deg * Math.PI / 180.0; }

        static double LimbAngle(PointF a, PointF b, int f) { return Deg(Math.Atan2(f * (b.X - a.X), b.Y - a.Y)); }

        // The opposite of Renderer.Compute: turns joint positions back into angles.
        // Used when he stops being a floppy ragdoll and has to get up from wherever he landed.
        public static Pose FromJoints(Joints j, int f, double groundY, double u)
        {
            Pose p = new Pose();
            p.Grounded = false;
            p.HipY = (groundY - j.Hip.Y) / u;
            p.Spine = Deg(Math.Atan2(f * (j.Neck.X - j.Hip.X), -(j.Neck.Y - j.Hip.Y)));
            double hs = Deg(Math.Atan2(f * (j.Head.X - j.Neck.X), -(j.Head.Y - j.Neck.Y)));
            p.Head = hs - p.Spine;
            while (p.Head > 180) p.Head -= 360;
            while (p.Head < -180) p.Head += 360;
            p.AU = LimbAngle(j.Neck, j.ElbowA, f); p.AF = LimbAngle(j.ElbowA, j.HandA, f);
            p.BU = LimbAngle(j.Neck, j.ElbowB, f); p.BF = LimbAngle(j.ElbowB, j.HandB, f);
            p.LAT = LimbAngle(j.Hip, j.KneeA, f); p.LAS = LimbAngle(j.KneeA, j.FootA, f);
            p.LBT = LimbAngle(j.Hip, j.KneeB, f); p.LBS = LimbAngle(j.KneeB, j.FootB, f);
            return p;
        }
    }

    public class Joints
    {
        public PointF Hip, Neck, Head, ElbowA, HandA, ElbowB, HandB, KneeA, FootA, KneeB, FootB;
        public float HeadR;

        public PointF[] ToArray() { return new PointF[] { Hip, Neck, Head, ElbowA, HandA, ElbowB, HandB, KneeA, FootA, KneeB, FootB }; }

        public static Joints FromArray(PointF[] a, float headR)
        {
            Joints j = new Joints();
            j.Hip = a[0]; j.Neck = a[1]; j.Head = a[2]; j.ElbowA = a[3]; j.HandA = a[4]; j.ElbowB = a[5]; j.HandB = a[6];
            j.KneeA = a[7]; j.FootA = a[8]; j.KneeB = a[9]; j.FootB = a[10]; j.HeadR = headR;
            return j;
        }

        public Joints Offset(float dx, float dy)
        {
            PointF[] a = ToArray();
            for (int i = 0; i < a.Length; i++) a[i] = new PointF(a[i].X + dx, a[i].Y + dy);
            return FromArray(a, HeadR);
        }

        public RectangleF Bounds()
        {
            PointF[] pts = { Hip, Neck, ElbowA, HandA, ElbowB, HandB, KneeA, FootA, KneeB, FootB };
            float x0 = Head.X - HeadR, x1 = Head.X + HeadR, y0 = Head.Y - HeadR, y1 = Head.Y + HeadR;
            foreach (PointF p in pts)
            {
                x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
            }
            return RectangleF.FromLTRB(x0, y0, x1, y1);
        }
    }

    public static class Renderer
    {
        static PointF P(double x, double y) { return new PointF((float)x, (float)y); }

        static PointF Limb(PointF from, double deg, double len, int f, float u)
        {
            double a = Poses.R(deg);
            return P(from.X + Math.Sin(a) * f * len * u, from.Y + Math.Cos(a) * len * u);
        }

        // (ox, oy) = the spot on the ground under the stickman.
        public static Joints Compute(Pose p, float ox, float oy, int f, float u)
        {
            Joints j = new Joints();
            double hx = ox, hy = oy - (p.HipY + p.Lift) * u;
            j.Hip = P(hx, hy);
            double s = Poses.R(p.Spine);
            j.Neck = P(hx + Math.Sin(s) * f * Poses.SP * u, hy - Math.Cos(s) * Poses.SP * u);
            double hs = Poses.R(p.Spine + p.Head);
            j.Head = P(j.Neck.X + Math.Sin(hs) * f * Poses.NK * u, j.Neck.Y - Math.Cos(hs) * Poses.NK * u);
            j.HeadR = (float)(Poses.HR * u);
            j.ElbowA = Limb(j.Neck, p.AU, Poses.UA, f, u); j.HandA = Limb(j.ElbowA, p.AF, Poses.FA, f, u);
            j.ElbowB = Limb(j.Neck, p.BU, Poses.UA, f, u); j.HandB = Limb(j.ElbowB, p.BF, Poses.FA, f, u);
            j.KneeA = Limb(j.Hip, p.LAT, Poses.TH, f, u); j.FootA = Limb(j.KneeA, p.LAS, Poses.SH, f, u);
            j.KneeB = Limb(j.Hip, p.LBT, Poses.TH, f, u); j.FootB = Limb(j.KneeB, p.LBS, Poses.SH, f, u);
            return j;
        }

        public static Pen RoundPen(Color c, float w)
        {
            Pen pen = new Pen(c, w);
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            return pen;
        }

        public static Color Shade(Color c, double k)
        {
            return Color.FromArgb(c.A, (int)Math.Min(255, c.R * k), (int)Math.Min(255, c.G * k), (int)Math.Min(255, c.B * k));
        }

        public static double Lum(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

        // A plain stick figure: round head, thick round limbs, no face.
        public static void DrawFigure(Graphics g, Joints j, int f, float u, Color color, Prop prop, double t, bool outline)
        {
            float lw = 6.4f * u;
            Color back = Shade(color, 0.8);

            if (outline)
            {
                // a faint light edge so he still shows up on dark windows
                using (Pen o = RoundPen(Color.FromArgb(80, 255, 255, 255), lw + 2.6f * u))
                using (SolidBrush ob = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                {
                    g.DrawLines(o, new PointF[] { j.Neck, j.ElbowB, j.HandB });
                    g.DrawLines(o, new PointF[] { j.Hip, j.KneeB, j.FootB });
                    g.DrawLine(o, j.Hip, j.Neck);
                    g.DrawLines(o, new PointF[] { j.Hip, j.KneeA, j.FootA });
                    g.DrawLines(o, new PointF[] { j.Neck, j.ElbowA, j.HandA });
                    float r = j.HeadR + 1.3f * u;
                    g.FillEllipse(ob, j.Head.X - r, j.Head.Y - r, r * 2, r * 2);
                }
            }

            using (Pen pb = RoundPen(back, lw))
            {
                g.DrawLines(pb, new PointF[] { j.Neck, j.ElbowB, j.HandB });
                g.DrawLines(pb, new PointF[] { j.Hip, j.KneeB, j.FootB });
            }
            if (prop == Prop.Popcorn) DrawPopcorn(g, j.HandB, u);
            using (Pen pf = RoundPen(color, lw))
            {
                g.DrawLine(pf, j.Hip, j.Neck);
                g.DrawLines(pf, new PointF[] { j.Hip, j.KneeA, j.FootA });
            }
            using (SolidBrush hb = new SolidBrush(color))
                g.FillEllipse(hb, j.Head.X - j.HeadR, j.Head.Y - j.HeadR, j.HeadR * 2, j.HeadR * 2);

            if (prop == Prop.Controller)
            {
                PointF m = P((j.HandA.X + j.HandB.X) / 2, (j.HandA.Y + j.HandB.Y) / 2);
                using (GraphicsPath gp = RoundRect(new RectangleF(m.X - 8 * u, m.Y - 4 * u, 16 * u, 8 * u), 3 * u))
                using (SolidBrush cb = new SolidBrush(Color.FromArgb(60, 60, 70)))
                    g.FillPath(cb, gp);
                using (SolidBrush r = new SolidBrush(Color.FromArgb(240, 70, 70)))
                    g.FillEllipse(r, m.X + 3 * u, m.Y - 2 * u, 2.4f * u, 2.4f * u);
                using (SolidBrush bl = new SolidBrush(Color.FromArgb(80, 160, 255)))
                    g.FillEllipse(bl, m.X + 5.2f * u, m.Y - 0.2f * u, 2.4f * u, 2.4f * u);
            }

            using (Pen pf = RoundPen(color, lw))
                g.DrawLines(pf, new PointF[] { j.Neck, j.ElbowA, j.HandA });

            if (prop == Prop.Pencil)
            {
                double dx = j.HandA.X - j.ElbowA.X, dy = j.HandA.Y - j.ElbowA.Y, len = Math.Sqrt(dx * dx + dy * dy) + 0.001;
                dx /= len; dy /= len;
                double ca = Math.Cos(0.6), sa = Math.Sin(0.6) * f;
                double px = dx * ca - dy * sa, py = dx * sa + dy * ca;
                PointF a = P(j.HandA.X - px * 3 * u, j.HandA.Y - py * 3 * u);
                PointF bpt = P(j.HandA.X + px * 9 * u, j.HandA.Y + py * 9 * u);
                PointF tip = P(j.HandA.X + px * 12 * u, j.HandA.Y + py * 12 * u);
                using (Pen pe = new Pen(Color.FromArgb(255, 200, 40), 3f * u)) g.DrawLine(pe, a, bpt);
                using (Pen pt = RoundPen(Color.FromArgb(60, 50, 40), 1.6f * u)) g.DrawLine(pt, bpt, tip);
                using (Pen pk = new Pen(Color.FromArgb(240, 130, 150), 3f * u)) g.DrawLine(pk, a, P(a.X - px * 2 * u, a.Y - py * 2 * u));
            }
        }

        static void DrawPopcorn(Graphics g, PointF hand, float u)
        {
            float w = 13 * u, h = 15 * u, x = hand.X - w / 2, y = hand.Y - h * 0.7f;
            PointF[] cup = { P(x, y), P(x + w, y), P(x + w * 0.82, y + h), P(x + w * 0.18, y + h) };
            using (SolidBrush puff = new SolidBrush(Color.FromArgb(255, 245, 205)))
                for (int i = 0; i < 4; i++) g.FillEllipse(puff, x + i * w / 4 - 1 * u, y - 4.5f * u + (i % 2) * 1.5f * u, 6 * u, 6 * u);
            using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPolygon(wb, cup);
            using (Pen rp = new Pen(Color.FromArgb(230, 40, 40), 2.2f * u))
            {
                g.DrawLine(rp, x + w * 0.3f, y, x + w * 0.36f, y + h);
                g.DrawLine(rp, x + w * 0.7f, y, x + w * 0.64f, y + h);
            }
            using (Pen ep = new Pen(Color.FromArgb(150, 40, 40, 40), 1f * u)) g.DrawPolygon(ep, cup);
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath gp = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 0.5f) { gp.AddRectangle(r); return gp; }
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        // Speech bubble with a little tail pointing at his head.
        public static void DrawBubble(Graphics g, RectangleF r, PointF tail, string text, Font font, float dpi, int alpha)
        {
            float rad = 12 * dpi;
            using (GraphicsPath gp = RoundRect(r, rad))
            {
                float tx = Math.Max(r.Left + rad + 4 * dpi, Math.Min(r.Right - rad - 4 * dpi, tail.X));
                PointF[] tri = { new PointF(tx - 9 * dpi, r.Bottom - 1), new PointF(tx + 9 * dpi, r.Bottom - 1), tail };
                using (GraphicsPath tp = new GraphicsPath())
                {
                    tp.AddPolygon(tri);
                    using (Region reg = new Region(gp))
                    {
                        reg.Union(tp);
                        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(alpha / 6, 0, 0, 0)))
                        {
                            g.TranslateTransform(2 * dpi, 3 * dpi);
                            g.FillRegion(shadow, reg);
                            g.TranslateTransform(-2 * dpi, -3 * dpi);
                        }
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha * 248 / 255, 255, 255, 255)))
                            g.FillRegion(fill, reg);
                    }
                    using (Pen border = new Pen(Color.FromArgb(alpha * 200 / 255, 40, 40, 55), 2f * dpi))
                    {
                        border.LineJoin = LineJoin.Round;
                        g.DrawPath(border, gp);
                        g.DrawLine(border, tri[0], tri[2]);
                        g.DrawLine(border, tri[1], tri[2]);
                    }
                    using (SolidBrush cover = new SolidBrush(Color.FromArgb(alpha * 248 / 255, 255, 255, 255)))
                        g.FillRectangle(cover, tx - 8 * dpi, r.Bottom - 2.5f * dpi, 16 * dpi, 4 * dpi);
                }
            }
            using (StringFormat sf = new StringFormat())
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(alpha, 25, 25, 35)))
            {
                sf.Trimming = StringTrimming.EllipsisWord;
                RectangleF tr = new RectangleF(r.X + 11 * dpi, r.Y + 7 * dpi, r.Width - 20 * dpi, r.Height - 12 * dpi);
                g.DrawString(text, font, tb, tr, sf);
            }
        }

        // The "I'm angry" mark from comics: four little curved strokes.
        public static void DrawAngerMark(Graphics g, float cx, float cy, float s, int alpha)
        {
            using (Pen p = RoundPen(Color.FromArgb(alpha, 220, 20, 30), 2.2f * s / 10))
            using (Pen w = RoundPen(Color.FromArgb(alpha, 255, 255, 255), 4.4f * s / 10))
            {
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < 4; i++)
                    {
                        float ang = i * 90 + 45;
                        double a = ang * Math.PI / 180;
                        float ox = cx + (float)Math.Cos(a) * s * 0.55f, oy = cy + (float)Math.Sin(a) * s * 0.55f;
                        g.DrawArc(pass == 0 ? w : p, ox - s * 0.45f, oy - s * 0.45f, s * 0.9f, s * 0.9f, ang + 90, 90);
                    }
            }
        }

        public static Icon MakeIcon(Color c)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Pose p = Poses.Get(Act.Wave, 0.1, 0);
                    Joints j = Compute(p, 15, 31, 1, 0.27f);
                    DrawFigure(g, j, 1, 0.27f, c, Prop.None, 0, false);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
