using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace StickBuddy
{
    // A see-through window over the whole screen. Used for the black "impact frame" when he smashes
    // into the edge of the screen, and for the hide-and-seek countdown. Clicks go right through it.
    public class OverlayForm : Form
    {
        readonly Buddy b;
        double flashUntil;
        bool showing;
        public bool Flashing { get { return b.Now < flashUntil; } }

        public OverlayForm(Buddy b)
        {
            this.b = b;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-10000, -10000);
            Size = new Size(1, 1);
            Text = "Stick Buddy overlay";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000; // layered, click-through, toolwindow, topmost, no-activate
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        void Put(Bitmap bmp, Rectangle vs)
        {
            if (!Visible) Show();
            Native.UpdateLayered(Handle, bmp, vs.Left, vs.Top);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            showing = true;
        }

        // Black screen, white stickman, for a split second.
        public void Flash(Joints worldJoints, double secs)
        {
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                using (Bitmap bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Joints lj = worldJoints.Offset(-vs.Left, -vs.Top);
                        Renderer.DrawFigure(g, lj, b.Facing, (float)b.U, Color.White, Prop.None, 0, false);
                    }
                    Put(bmp, vs);
                }
                flashUntil = b.Now + secs;
            }
            catch (Exception ex) { Log.Write("flash: " + ex.Message); }
        }

        // The screen goes dark and counts down while he hides.
        public void Countdown(int n, string line)
        {
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                Rectangle pr = Screen.PrimaryScreen.Bounds;
                float d = (float)b.Dpi;
                using (Bitmap bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    using (Font big = new Font("Segoe UI", 170 * d, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Font small = new Font("Segoe UI", 30 * d, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (StringFormat sf = new StringFormat())
                    {
                        g.Clear(Color.FromArgb(232, 12, 12, 18));
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                        RectangleF r = new RectangleF(pr.Left - vs.Left, pr.Top - vs.Top, pr.Width, pr.Height);
                        g.DrawString(n.ToString(), big, Brushes.White, new RectangleF(r.X, r.Y - 60 * d, r.Width, r.Height), sf);
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(220, 230, 230, 240)))
                            g.DrawString(line, small, sb, new RectangleF(r.X, r.Y + 130 * d, r.Width, r.Height), sf);
                    }
                    Put(bmp, vs);
                }
            }
            catch (Exception ex) { Log.Write("countdown: " + ex.Message); }
        }

        public void ClearOverlay()
        {
            if (!showing) return;
            showing = false;
            using (Bitmap bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                Native.UpdateLayered(Handle, bmp, -10000, -10000);
        }

        public void Tick(double now)
        {
            if (showing && flashUntil > 0 && now >= flashUntil) { flashUntil = 0; ClearOverlay(); }
        }
    }
}
