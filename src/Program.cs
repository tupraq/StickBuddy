using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace StickBuddy
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--render-test")
            {
                RenderTest.Run(args.Length > 1 ? args[1] : "render-test.png");
                return;
            }
            if (args.Length > 0 && args[0] == "--math-test")
            {
                MathTest(args.Length > 1 ? args[1] : "math-test.txt");
                return;
            }
            if (args.Length > 0 && args[0] == "--lab-test")
            {
                LabTest(args.Length > 1 ? args[1] : "lab-test.txt", args.Length > 2 ? int.Parse(args[2]) : 150);
                return;
            }
            if (args.Length > 0 && args[0] == "--lab-render")
            {
                // pictures of the lab, drawn off-screen: a baby brain, then a smart one doing parkour
                Buddy.TestMode = true;
                LabStats.NoSave = true;
                Buddy bud = new Buddy();
                string outp = args.Length > 1 ? args[1] : "lab.png";
                LabStats.IQ = 0;
                using (ObbyLab lab = new ObbyLab(bud))
                {
                    using (Bitmap b1 = lab.Snapshot(1600, 900, 0, 200)) b1.Save(outp.Replace(".png", "_baby.png"), ImageFormat.Png);
                    LabStats.IQ = 95;
                    using (Bitmap b2 = lab.Snapshot(1600, 900, 3, 60 * 60)) b2.Save(outp.Replace(".png", "_smart.png"), ImageFormat.Png);
                }
                return;
            }
            if (args.Length > 0 && args[0] == "--arcade-test")
            {
                ArcadeTest(args.Length > 1 ? args[1] : ".");
                return;
            }
            if (args.Length > 0 && args[0] == "--pilot-test")
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("schema: " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(GameBrain.Schema()));
                GameTurn t = GameBrain.Parse("{\"thought\":\"A coin on the right\",\"say\":\"Coin!\",\"actions\":[{\"type\":\"hold\",\"key\":\"d\",\"x\":0,\"y\":0,\"ms\":700},{\"type\":\"press\",\"key\":\"space\",\"x\":0,\"y\":0,\"ms\":0},{\"type\":\"press\",\"key\":\"enter\",\"x\":0,\"y\":0,\"ms\":0}],\"hold_keys\":[\"w\",\"enter\",\"shift\"],\"done\":false}");
                sb.AppendLine("parsed: thought=" + t.Thought + " say=" + t.Say + " moves=" + t.Moves.Count + " done=" + t.Done + " keeps holding=" + string.Join("+", t.HoldKeys.ToArray()));                foreach (GameMove m in t.Moves) sb.AppendLine("  " + m.Type + " " + m.Key + " allowed=" + (m.Type == "press" || m.Type == "hold" ? GameBrain.Keys.ContainsKey(m.Key).ToString() : "-"));
                string img;
                using (Bitmap bm = new Bitmap(64, 48))
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    using (Graphics g = Graphics.FromImage(bm)) g.Clear(Color.SkyBlue);
                    bm.Save(ms, ImageFormat.Jpeg);
                    img = Convert.ToBase64String(ms.ToArray());
                }
                try
                {
                    string r = Gemini.AskRaw("AQ.not-a-real-key-just-testing", Gemini.DefaultModel, "Test.", "What color is this?", img, GameBrain.Schema(), 256).Result;
                    sb.AppendLine("gemini: OK " + r);
                }
                catch (Exception ex) { sb.AppendLine("gemini (fake key, error expected): " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
                System.IO.File.WriteAllText(args.Length > 1 ? args[1] : "pilot-test.txt", sb.ToString());
                return;
            }
            if (args.Length > 0 && args[0] == "--gemini-test")
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                string outp = args.Length > 1 ? args[1] : "gemini-test.txt";
                string res;
                try
                {
                    ClaudeReply r = Gemini.Ask("AQ.not-a-real-key-just-testing", Gemini.DefaultModel, "Reply in JSON.", new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>(), "Say hi").Result;
                    res = "OK: " + r.Say;
                }
                catch (Exception ex) { res = "ERROR (expected with a fake key): " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }
                System.IO.File.WriteAllText(outp, res);
                return;
            }
            string startCommand = args.Length > 1 && args[0] == "--command" ? args[1] : null;

            bool created;
            using (Mutex m = new Mutex(true, "StickBuddy_OneAtATime", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Stick Buddy is already running! Look for him on your screen (or in the tray, bottom right).", "Stick Buddy");
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("ui error: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("crash: " + e.ExceptionObject); };
                Log.Write("---- Stick Buddy started ----");
                Buddy buddy = new Buddy();
                buddy.StartCommand = startCommand;
                Application.Run(buddy);
            }
        }

        // Pictures of the new games, drawn off-screen (nothing shows up, no sounds play).
        static void ArcadeTest(string dir)
        {
            Buddy.TestMode = true;
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            Buddy b = new Buddy();
            b.S.Muted = true;           // not saved: just no music in the test
            Sfx.Off = true;

            // all the guessing-game drawings
            using (Bitmap bmp = new Bitmap(5 * 200, 4 * 190))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.White);
                List<string> used = new List<string>();
                Random r = new Random(5);
                for (int i = 0; i < 20; i++)
                {
                    Doodle dd = Doodle.MakeGuess(r, used);
                    RectangleF rr = new RectangleF((i % 5) * 200 + 10, (i / 5) * 190 + 10, 180, 144);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(250, 248, 240)), rr);
                    dd.Draw(g, rr, 1);
                    g.DrawRectangle(Pens.Gray, rr.X, rr.Y, rr.Width, rr.Height);
                    g.DrawString(dd.Name + "  [" + dd.Keywords[0] + "]", f, Brushes.Black, rr.X, rr.Bottom + 4);
                    log.AppendLine(dd.Name + ": 'is it a " + dd.Keywords[0] + "' right=" + dd.IsRight("is it a " + dd.Keywords[0]) + ", 'banana' right=" + dd.IsRight("banana"));
                }
                bmp.Save(System.IO.Path.Combine(dir, "arcade_doodles.png"), ImageFormat.Png);
            }
            // balloons and a dodgeball
            using (Bitmap bmp = new Bitmap(360, 160))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(200, 225, 250));
                Sprite s1 = new Sprite(); s1.Kind = Sprite.Balloon; s1.R = 30; s1.Col = Color.FromArgb(240, 70, 80);
                Sprite s2 = new Sprite(); s2.Kind = Sprite.Balloon; s2.R = 30; s2.Gold = true;
                Sprite s3 = new Sprite(); s3.Kind = Sprite.Ball; s3.R = 16; s3.Col = Color.FromArgb(225, 50, 50);
                using (Bitmap a = new Bitmap(70, 130)) { using (Graphics ga = Graphics.FromImage(a)) SpriteForm.Paint(ga, s1, 1, 70, 130); g.DrawImage(a, 10, 10); }
                using (Bitmap a = new Bitmap(70, 130)) { using (Graphics ga = Graphics.FromImage(a)) SpriteForm.Paint(ga, s2, 1, 70, 130); g.DrawImage(a, 110, 10); }
                using (Bitmap a = new Bitmap(48, 48)) { using (Graphics ga = Graphics.FromImage(a)) SpriteForm.Paint(ga, s3, 1, 48, 48); g.DrawImage(a, 230, 50); }
                bmp.Save(System.IO.Path.Combine(dir, "arcade_sprites.png"), ImageFormat.Png);
            }
            // sounds (made, not played)
            foreach (string sn in new[] { "pop", "whack", "ding", "buzz", "whoosh", "cheer", "swish" })
                log.AppendLine("sound " + sn + ": " + Sfx.MakeSize(sn) + " bytes");
            byte[] song = Sfx.DanceSong(120, 32);
            log.AppendLine("dance song: " + song.Length + " bytes (" + (song.Length / 2 / 22050.0).ToString("0.0") + " s)");

            // the windows, opened far off the screen and photographed
            Shoot(new GameCenterForm(b), System.IO.Path.Combine(dir, "arcade_center.png"), 0);
            DanceForm df = new DanceForm(b);
            Shoot(df, System.IO.Path.Combine(dir, "arcade_dance_menu.png"), 0);
            df = new DanceForm(b);
            df.StartPosition = FormStartPosition.Manual; df.Location = new Point(-5000, -5000); df.ShowInTaskbar = false;
            df.Show();
            SendKeysTo(df, Keys.D2);
            Shoot(df, System.IO.Path.Combine(dir, "arcade_dance_play.png"), 3.2);
            Shoot(new GuessForm(b), System.IO.Path.Combine(dir, "arcade_guess.png"), 7);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "arcade_test.txt"), log.ToString());
        }

        static void SendKeysTo(DanceForm f, Keys k)
        {
            System.Reflection.MethodInfo m = typeof(DanceForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Message msg = new Message();
            m.Invoke(f, new object[] { msg, k });
        }

        static void Shoot(Form f, string path, double waitSecs)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-5000, -5000);
            f.ShowInTaskbar = false;
            if (!f.Visible) f.Show();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < waitSecs + 0.3) { Application.DoEvents(); Thread.Sleep(15); }
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            f.Close();
        }

        // Lets a brand-new brain learn each example obby with no screen, and writes down how it went.
        static void LabTest(string path, int maxGens)
        {
            LabStats.NoSave = true;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int n = 0; n < Level.ExampleNames.Length; n++)
            {
                LabStats.IQ = n == 3 ? 60 : 0; // the wall climb needs parkour
                Level l = Level.Example(n, 48, 20);
                Trainer t = new Trainer(l, null);
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                int solvedAt = -1;
                while (t.Gen < maxGens && solvedAt < 0)
                {
                    if (t.Step(1 / 60.0) && t.LastWhat == "finished") solvedAt = t.Gen;
                }
                sb.AppendLine(l.Name + ": " + (solvedAt >= 0 ? "SOLVED at generation " + solvedAt : "not solved in " + maxGens + " generations") +
                              ", best " + (int)(t.BestProgress * 100) + "%, " + sw.ElapsedMilliseconds + " ms");
                StringBuilderAppendCurve(sb, t);
            }
            System.IO.File.WriteAllText(path, sb.ToString());
        }

        static void StringBuilderAppendCurve(System.Text.StringBuilder sb, Trainer t)
        {
            sb.Append("   per generation: ");
            for (int i = 0; i < t.History.Count; i += Math.Max(1, t.History.Count / 25)) sb.Append((int)(t.History[i] * 100) + " ");
            sb.AppendLine();
        }

        static void MathTest(string path)
        {
            string[] qs =
            {
                "what is 1+1", "what is 1 + 1", "whats one plus one", "what is 12 times 7", "12 x 7", "what is 100 divided by 4",
                "what is twenty five minus seven", "square root of 81", "what is 2 to the power of 10", "10 percent of 50",
                "what is 5 divided by 0", "what is 3.5 * 2", "what is (2+3)*4", "what animal lived the most",
                "what animal lived the longest", "what is the capital of france", "whats the biggest planet",
                "how many bones are in the human body", "open chrome", "what is a hundred and five plus one", "7 squared"
            };
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string q in qs)
            {
                string c = q.ToLowerInvariant().Replace("whats", "what is");
                string d, s;
                if (Mathy.TryAnswer(c, out d, out s)) sb.AppendLine(q + "  =>  MATH  " + d + "   |  says: " + s);
                else if (Facts.TryAnswer(c, out d)) sb.AppendLine(q + "  =>  FACT  " + d);
                else sb.AppendLine(q + "  =>  (not math/fact)");
            }
            System.IO.File.WriteAllText(path, sb.ToString());
        }
    }

    // Draws every pose, the ragdoll, the house and some drawings into one picture,
    // so everything can be checked without running the app.
    static class RenderTest
    {
        public static void Run(string path)
        {
            Buddy.TestMode = true;
            Act[] acts = (Act[])Enum.GetValues(typeof(Act));
            int cols = 10, cw = 160, ch = 190, rows = (acts.Length + cols - 1) / cols;
            Buddy b = new Buddy();
            House h = new House(b);
            b.House = h;
            b.Things = new ThingManager(b);
            b.Things.CreateAll();
            h.Shown = true;
            h.Easel = Doodle.Make(new Random(3)); h.EaselProgress = 0.65; h.TvOn = true;
            Bitmap house = h.Snapshot();
            Color red = Settings.DefaultRed;
            int ragTop = rows * ch, ragH = 300, houseTop = ragTop + ragH, doodTop = houseTop + house.Height + 30;
            int width = Math.Max(cols * cw, house.Width * 2 + 60);
            using (Bitmap bmp = new Bitmap(width, doodTop + 170, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(236, 238, 243));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < acts.Length; i++)
                {
                    Act a = acts[i];
                    float ox = (i % cols) * cw + cw / 2f, oy = (i / cols) * ch + ch - 34;
                    Pose p = Poses.Get(a, a == Act.Throw ? 0.25 : 0.37, 0);
                    Joints j = Renderer.Compute(p, ox, oy, 1, 1.15f);
                    Prop prop = a == Act.Write || a == Act.DrawSit ? Prop.Pencil : a == Act.Play ? Prop.Controller : a == Act.Watch ? Prop.Popcorn : Prop.None;
                    using (Pen gp = new Pen(Color.FromArgb(180, 180, 190), 1)) g.DrawLine(gp, ox - 70, oy, ox + 70, oy);
                    Renderer.DrawFigure(g, j, 1, 1.15f, red, prop, 0.37, true);
                    g.DrawString(a.ToString(), f, Brushes.Black, ox - cw / 2f + 4, (i / cols) * ch + 4);
                }

                // ragdoll: dropped and thrown at a wall, a picture every few frames
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(225, 228, 236))) g.FillRectangle(dark, 0, ragTop, width, ragH);
                g.DrawString("ragdoll: thrown to the right into a wall, then lands (left to right = time)", f, Brushes.Black, 6, ragTop + 4);
                Ragdoll r = new Ragdoll(1.0, 2300);
                double floorY = ragTop + ragH - 20;
                List<Plat> plats = new List<Plat>();
                plats.Add(new Plat(0, width, floorY, Plat.Floor, IntPtr.Zero));
                Rectangle area = new Rectangle(0, ragTop, 260, ragH);
                r.SetJoints(Renderer.Compute(Poses.Get(Act.Idle, 0, 0), 90, (float)floorY - 120, 1, 1f), 1300, -500, 1 / 60.0);
                double maxWall = 0;
                for (int k = 0; k < 150; k++)
                {
                    RagHit hit = r.Step(1 / 60.0, plats, area);
                    maxWall = Math.Max(maxWall, hit.WallSpeed);
                    if (k % 15 == 0)
                    {
                        Joints rj = r.GetJoints().Offset((k / 15) * 150, 0);
                        Renderer.DrawFigure(g, rj, 1, 1f, red, Prop.None, 0, true);
                    }
                }
                g.DrawString("wall hit speed: " + (int)maxWall, f, Brushes.DimGray, 6, ragTop + 22);

                // the house, plus him on the couch and in bed
                g.DrawImage(house, 20, houseTop);
                PointF couch = h.SnapPoint(292, 0), bed = h.SnapPoint(292, House.UpperFloor + 14), chair = h.SnapPoint(220, House.UpperFloor);
                float u = (float)h.Hu;
                Renderer.DrawFigure(g, Renderer.Compute(Poses.Get(Act.CouchSit, 0.3, 0), 20 + couch.X, houseTop + couch.Y, -1, u), -1, u, red, Prop.None, 0, false);
                Renderer.DrawFigure(g, Renderer.Compute(Poses.Get(Act.Sleep, 0.3, 0), 20 + bed.X, houseTop + bed.Y, -1, u), -1, u, red, Prop.None, 0, false);
                Bitmap house2 = h.Snapshot();
                g.DrawImage(house2, 40 + house.Width, houseTop);
                Renderer.DrawFigure(g, Renderer.Compute(Poses.Get(Act.DrawSit, 0.3, 0), 40 + house.Width + chair.X, houseTop + chair.Y, -1, u), -1, u, red, Prop.Pencil, 0, false);
                PointF stairs = h.SnapPoint(90, (90 - House.StairX0) / (House.StairX1 - House.StairX0) * House.UpperFloor);
                Renderer.DrawFigure(g, Renderer.Compute(Poses.Get(Act.Walk, 0.3, 0), 40 + house.Width + stairs.X, houseTop + stairs.Y, 1, u), 1, u, red, Prop.None, 0, false);

                // some of his drawings
                for (int i = 0; i < 8; i++)
                {
                    RectangleF rr = new RectangleF(20 + i * 150, doodTop, 130, 104);
                    g.FillRectangle(Brushes.White, rr);
                    Doodle d = Doodle.Make(new Random(i * 7 + 1));
                    d.Draw(g, rr, 1);
                    g.DrawRectangle(Pens.Gray, rr.X, rr.Y, rr.Width, rr.Height);
                    g.DrawString(d.Name, f, Brushes.Black, rr.X, rr.Bottom + 4);
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
