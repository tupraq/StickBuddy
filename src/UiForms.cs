using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace StickBuddy
{
    // Little box for typing to the stickman.
    public class InputBox : Form
    {
        readonly Buddy b;
        readonly TextBox box;

        public InputBox(Buddy b)
        {
            this.b = b;
            Text = "Talk to " + b.S.Name;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 11f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;

            TableLayoutPanel panel = new TableLayoutPanel();
            panel.ColumnCount = 3; panel.RowCount = 2; panel.AutoSize = true; panel.Padding = new Padding(8);
            box = new TextBox();
            box.Width = (int)(330 * b.Dpi);
            Button send = new Button(); send.Text = "Send"; send.AutoSize = true;
            Button mic = new Button(); mic.Text = "Listen"; mic.AutoSize = true;
            Label hint = new Label();
            hint.Text = "Try: open chrome  |  what is 12 x 7  |  play tag  |  hide and seek  |  draw a picture  |  dance";
            hint.AutoSize = true; hint.ForeColor = Color.Gray; hint.Font = new Font("Segoe UI", 9f);
            panel.Controls.Add(box, 0, 0);
            panel.Controls.Add(send, 1, 0);
            panel.Controls.Add(mic, 2, 0);
            panel.Controls.Add(hint, 0, 1);
            panel.SetColumnSpan(hint, 3);
            Controls.Add(panel);
            AcceptButton = send;
            send.Click += delegate { Submit(); };
            mic.Click += delegate { Hide(); b.Voice.ListenNow(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
        }

        void Submit()
        {
            string t = box.Text;
            box.Text = "";
            Hide();
            b.Brain.Hear(t);
        }

        public void OpenNear(Point p)
        {
            Show();
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(wa.Right - Width, p.X - Width / 2));
            int y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, p.Y - Height));
            Location = new Point(x, y);
            Activate();
            box.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }

    // Customise the stickman.
    public class SettingsForm : Form
    {
        readonly Buddy b;
        readonly TextBox name, key;
        readonly Button colorBtn;
        readonly CheckBox rainbow, squeaky, muted, listen, needName, notepad, mouse, feelings, wander, house, flash, watch, chatty;
        Label brainLabel;
        bool modelsForGemini;

        // The model list matches the kind of key you pasted.
        void FillModels(bool first)
        {
            string k = key.Text.Trim();
            bool gem = k.Length > 0 && !Settings.IsClaudeKey(k);
            if (!first && gem == modelsForGemini) { UpdateBrainLabel(); return; }
            modelsForGemini = gem;
            model.Items.Clear();
            if (gem) { model.Items.AddRange(Gemini.Models); model.Text = b.S.GeminiModel; }
            else { model.Items.AddRange(new object[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" }); model.Text = b.S.Model; }
            UpdateBrainLabel();
        }

        void UpdateBrainLabel()
        {
            if (brainLabel == null) return;
            string k = key.Text.Trim();
            brainLabel.Text = k.Length == 0 ? "No key: I use my little built-in brain." : Settings.IsClaudeKey(k) ? "That's a Claude key." : "That looks like a Google Gemini key.";
        }
        readonly TrackBar size, rate;
        readonly ComboBox voice, model;
        Color chosen;
        readonly TableLayoutPanel grid;

        public SettingsForm(Buddy b)
        {
            this.b = b;
            Settings s = b.S;
            Text = "Customise " + s.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TopMost = true;

            grid = new TableLayoutPanel();
            grid.ColumnCount = 2; grid.AutoSize = true; grid.Padding = new Padding(12);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Header("Looks");
            name = new TextBox(); name.Text = s.Name; name.Width = (int)(220 * b.Dpi);
            Row("Name (say it to wake me up):", name);
            chosen = s.BodyColor;
            colorBtn = new Button(); colorBtn.Text = "Pick a color"; colorBtn.AutoSize = true; colorBtn.BackColor = chosen;
            colorBtn.ForeColor = Renderer.Lum(chosen) < 0.5 ? Color.White : Color.Black;
            colorBtn.Click += delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = chosen; cd.FullOpen = true;
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        chosen = cd.Color; colorBtn.BackColor = chosen;
                        colorBtn.ForeColor = Renderer.Lum(chosen) < 0.5 ? Color.White : Color.Black;
                        rainbow.Checked = false;
                    }
                }
            };
            Row("Body color:", colorBtn);
            rainbow = Check("Rainbow mode!", s.Rainbow);
            Row("", rainbow);
            size = new TrackBar(); size.Minimum = 50; size.Maximum = 250; size.TickFrequency = 25; size.Width = (int)(220 * b.Dpi);
            size.Value = (int)Math.Max(50, Math.Min(250, s.Size * 100));
            Row("Size:", size);

            Header("Voice");
            voice = new ComboBox(); voice.DropDownStyle = ComboBoxStyle.DropDownList; voice.Width = (int)(220 * b.Dpi);
            voice.Items.Add("(default)");
            foreach (string v in b.Voice.VoiceNames()) voice.Items.Add(v);
            voice.SelectedIndex = Math.Max(0, voice.Items.IndexOf(s.VoiceName));
            Row("Voice:", voice);
            rate = new TrackBar(); rate.Minimum = -5; rate.Maximum = 5; rate.Value = Math.Max(-5, Math.Min(5, s.VoiceRate)); rate.Width = (int)(220 * b.Dpi);
            Row("Talking speed:", rate);
            squeaky = Check("Squeaky cartoon voice", s.Squeaky);
            Row("", squeaky);
            muted = Check("Don't talk out loud (bubbles only)", s.Muted);
            Row("", muted);
            Button test = new Button(); test.Text = "Test voice"; test.AutoSize = true;
            test.Click += delegate { Apply(false); b.Say("Hi! This is how I sound now!"); };
            Row("", test);

            Header("Listening");
            listen = Check("Listen to my microphone", s.Listening);
            Row("", listen);
            needName = Check("Only listen after you say my name", s.NeedName);
            Row("", needName);

            Header("Behaviour");
            feelings = Check("Has feelings (gets bored, angry, happy)", s.Feelings);
            Row("", feelings);
            mouse = Check("Allowed to grab your mouse (press Esc to get it back)", s.MouseTricks);
            Row("", mouse);
            notepad = Check("Write answers in Notepad", s.NotepadAnswers);
            Row("", notepad);
            wander = Check("Walk around by himself", s.Wander);
            Row("", wander);
            house = Check("Show his house (bottom of the screen)", s.ShowHouse);
            Row("", house);
            flash = Check("Black flash when he smashes into the edge of the screen", s.ImpactFlash);
            Row("", flash);
            watch = Check("Watch YouTube with you and talk about your downloads", s.WatchScreen);
            Row("", watch);

            Header("Smart brain (optional)");
            key = new TextBox(); key.UseSystemPasswordChar = true; key.Width = (int)(260 * b.Dpi); key.Text = s.ApiKey;
            Row("API key:", key);
            model = new ComboBox(); model.DropDownStyle = ComboBoxStyle.DropDown; model.Width = (int)(260 * b.Dpi);
            FillModels(true);
            Row("Model:", model);
            brainLabel = new Label(); brainLabel.AutoSize = true; brainLabel.ForeColor = Color.SeaGreen;
            Row("", brainLabel);
            key.TextChanged += delegate { FillModels(false); b.OnKeyTyped(key.Text); };
            LinkLabel link = new LinkLabel(); link.Text = "Get a Gemini key (aistudio.google.com)"; link.AutoSize = true;
            link.LinkClicked += delegate { try { Process.Start("https://aistudio.google.com/apikey"); } catch { } };
            Row("", link);
            LinkLabel link2 = new LinkLabel(); link2.Text = "Get a Claude key (console.anthropic.com)"; link2.AutoSize = true;
            link2.LinkClicked += delegate { try { Process.Start("https://console.anthropic.com/settings/keys"); } catch { } };
            Row("", link2);
            chatty = Check("Says things by himself sometimes (needs a key)", s.Chatty);
            Row("", chatty);
            Label note = new Label(); note.AutoSize = true; note.ForeColor = Color.Gray; note.MaximumSize = new Size((int)(440 * b.Dpi), 0);
            note.Text = "Paste a Gemini key (from Google AI Studio) or a Claude key (starts with sk-ant-). Without a key I can still do math, open apps, play games and answer some questions. " +
                        "With a key I can talk about almost anything, remember things about you, and say things by myself. What you say to me, my mood and the name of the app you're using " +
                        "get sent to Google or Anthropic to make my answers. Treat the key like a password.";
            Row("", note);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true; buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Dock = DockStyle.Fill;
            Button save = new Button(); save.Text = "Save"; save.AutoSize = true;
            Button cancel = new Button(); cancel.Text = "Cancel"; cancel.AutoSize = true;
            save.Click += delegate { if (Apply(true)) Close(); };
            cancel.Click += delegate { Close(); };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            grid.Controls.Add(buttons, 0, grid.RowCount);
            grid.SetColumnSpan(buttons, 2);
            grid.RowCount++;
            AcceptButton = save; CancelButton = cancel;
            Controls.Add(grid);
        }

        void Header(string text)
        {
            Label l = new Label(); l.Text = text; l.AutoSize = true; l.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            l.Margin = new Padding(3, 10, 3, 3);
            grid.Controls.Add(l, 0, grid.RowCount);
            grid.SetColumnSpan(l, 2);
            grid.RowCount++;
        }

        void Row(string label, Control c)
        {
            Label l = new Label(); l.Text = label; l.AutoSize = true; l.Anchor = AnchorStyles.Left; l.Margin = new Padding(3, 7, 3, 3);
            grid.Controls.Add(l, 0, grid.RowCount);
            grid.Controls.Add(c, 1, grid.RowCount);
            grid.RowCount++;
        }

        static CheckBox Check(string text, bool on)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.Checked = on; c.AutoSize = true;
            return c;
        }

        bool Apply(bool save)
        {
            Settings s = b.S;
            string n = name.Text.Trim();
            if (n.Length < 2 || n.Contains(" "))
            {
                MessageBox.Show(this, "Please use one word with at least 2 letters for my name.", "Name");
                return false;
            }
            bool restartEars = n != s.Name || listen.Checked != s.Listening;
            s.Name = n;
            s.BodyColor = Color.FromArgb(chosen.R, chosen.G, chosen.B);
            s.Rainbow = rainbow.Checked;
            s.Size = size.Value / 100.0;
            s.VoiceName = voice.SelectedIndex <= 0 ? "" : voice.SelectedItem.ToString();
            s.VoiceRate = rate.Value;
            s.Squeaky = squeaky.Checked;
            s.Muted = muted.Checked;
            s.Listening = listen.Checked;
            s.NeedName = needName.Checked;
            s.Feelings = feelings.Checked;
            s.MouseTricks = mouse.Checked;
            s.NotepadAnswers = notepad.Checked;
            s.Wander = wander.Checked;
            s.ImpactFlash = flash.Checked;
            s.WatchScreen = watch.Checked;
            if (house.Checked != (b.House != null && b.House.Shown)) b.ToggleHouse();
            string newKey = key.Text.Trim();
            bool keyChanged = newKey != s.ApiKey;
            s.ApiKey = newKey;
            s.Chatty = chatty.Checked;
            string m = model.Text.Trim();
            if (newKey.Length > 0 && !Settings.IsClaudeKey(newKey)) s.GeminiModel = m.StartsWith("gemini") ? m : Gemini.DefaultModel;
            else s.Model = m.StartsWith("claude") ? m : "claude-opus-5-5";
            b.ApplySettings(restartEars);
            if (keyChanged && newKey.Length > 0) b.Brain.BrainAwakens();
            if (save) Text = "Customise " + s.Name;
            return true;
        }
    }

    public class HelpForm : Form
    {
        public HelpForm(Buddy b)
        {
            string n = b.S.Name;
            Text = "Things you can say to " + n;
            Size = new Size((int)(560 * b.Dpi), (int)(640 * b.Dpi));
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            TextBox t = new TextBox();
            t.Multiline = true; t.ReadOnly = true; t.ScrollBars = ScrollBars.Vertical; t.Dock = DockStyle.Fill;
            t.Font = new Font("Segoe UI", 10.5f); t.BackColor = Color.White;
            t.Text =
"Say \"" + n + "\" first, then what you want. Example: \"" + n + ", open chrome\".\r\n" +
"Or say just \"" + n + "\", wait for \"Yes?\", then talk.\r\n" +
"Typing works too: double-click " + n + " or press Ctrl+Shift+Space.\r\n" +
"Ctrl+Shift+L = listen without saying the name.\r\n\r\n" +
"OPEN THINGS\r\n" +
"  open chrome / open notepad / open calculator / open minecraft\r\n" +
"  open youtube / go to wikipedia.org\r\n" +
"  search for cute cats / play minecraft music on youtube\r\n" +
"  close this window / minimize this / show desktop\r\n" +
"  volume up / volume down / mute / next song / pause music\r\n" +
"  take a screenshot\r\n\r\n" +
"QUESTIONS (answers get written in Notepad)\r\n" +
"  what is 1 + 1 / what's 12 times 7 / square root of 81\r\n" +
"  what animal lived the longest / capital of France\r\n" +
"  what time is it / what's the date / tell me a joke\r\n" +
"  write down buy milk / write it down\r\n" +
"  (with a Claude API key in settings he can answer almost anything)\r\n\r\n" +
"GAME CENTER (say \"games\" or right-click > Game Center)\r\n" +
"  balloon pop      = grab him and THROW him into balloons\r\n" +
"  dodgeball        = he throws balls at your mouse pointer, dodge them!\r\n" +
"  whack a sticky   = click him when he pops up behind windows\r\n" +
"  hoops            = basketball shootout at his house, throw the ball with the mouse\r\n" +
"  dance battle     = rhythm game: arrow keys (or W A S D) to the beat\r\n" +
"  guess my drawing = he draws, you type or say what it is\r\n" +
"  Best scores are saved. He never gets angry about being thrown in a game.\r\n\r\n" +
"GAMES (make him happy!)\r\n" +
"  I'm it  = he runs away with parkour, click him 5 times in 60 seconds\r\n" +
"  chase me / catch me  = he chases your mouse and dives at it (one tag and he wins)\r\n" +
"  play tag  = one of the two, he picks\r\n" +
"  hide and seek  = the screen counts to 10, then find him hiding behind a file in\r\n" +
"     File Explorer (Downloads, Documents, Pictures...). Click him when you see him.\r\n" +
"     Say \"I give up\" if you can't find him.\r\n" +
"  play tic tac toe / play snake / rock paper scissors\r\n" +
"  play with my mouse  (press Esc to get your mouse back)\r\n\r\n" +
"HE PLAYS YOUR GAME (needs a smart-brain key)\r\n" +
"  Open your game, click on it, then say: play this game for me\r\n" +
"  or: play roblox for me and collect coins   (or right-click > Play the game I'm in)\r\n" +
"  He looks at the game, thinks, and presses game keys and clicks by himself.\r\n" +
"  He keeps running while he thinks, knows Roblox and Minecraft controls, and writes\r\n" +
"  himself tips after each game so he gets better. Add \"for 10 minutes\" to play longer.\r\n" +
"  Press Esc, say stop, or move your mouse to take over. He stops after 5 minutes.\r\n" +
"  He can't type in chat or press Enter. Watch him while he plays!\r\n\r\n" +
"OBBY LAB (say \"obby\" or right-click > Obby Lab)\r\n" +
"  Build an obstacle course with blocks, lava, bounce pads, checkpoints, start and finish.\r\n" +
"  Press \"Teach him!\": 40 little " + n + "s try at once with a real neural-network brain.\r\n" +
"  The best ones pass their brain on (evolution). He starts at IQ 0 and babbles,\r\n" +
"  then talks in words (IQ 20), learns parkour wall jumps (IQ 50), and talks properly (IQ 90).\r\n" +
"  what's your IQ / forget everything\r\n\r\n" +
"SMART BRAIN\r\n" +
"  Paste a Gemini key (Google AI Studio) or a Claude key in Settings. Then he can talk\r\n" +
"  about anything, remembers things you tell him, knows what happened to him,\r\n" +
"  and sometimes says things by himself.\r\n\r\n" +
"HIS HOUSE\r\n" +
"  go home / watch tv / draw a picture / play basketball / exercise / clean up\r\n" +
"  show your house / hide your house\r\n" +
"  Drag the house by its walls to move it. Grab his things (TV, lamp, ball...) and\r\n" +
"  throw them around. Throw them too hard and they break!\r\n\r\n" +
"MOVES\r\n" +
"  dance / do a flip / jump / wave / sit down / go to sleep / wake up\r\n" +
"  come here / go left / go right\r\n\r\n" +
"CUSTOMISE\r\n" +
"  turn red / turn rainbow / get bigger / get smaller\r\n" +
"  change your name to Bob / settings\r\n" +
"  mute yourself / talk again\r\n\r\n" +
"FEELINGS\r\n" +
"  Too many questions in a row and " + n + " gets bored.\r\n" +
"  Leave him alone and he goes home to play, draw, or nap.\r\n" +
"  Keep grabbing and smashing him and he gets angry:\r\n" +
"    1. he warns you\r\n" +
"    2. he grabs your cursor and throws it away, or hangs on it so it gets heavy\r\n" +
"    3. he jumps on your cursor to stomp it - wiggle the mouse hard to shake him off!\r\n" +
"  Press Esc to stop him any time, or say sorry.\r\n" +
"  Games, jokes and compliments make " + n + " happy.\r\n\r\n" +
"MOUSE\r\n" +
"  Drag " + n + " around and throw him! Smash him into the edge of the screen... ouch.\r\n" +
"  Click once = poke. Right-click = menu. When he sleeps, pick him up and drop him to wake him.\r\n";
            Controls.Add(t);
        }
    }
}
