using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StickBuddy
{
    // Understands what you say, keeps track of his feelings, and decides what to do.
    public partial class Brain
    {
        readonly Buddy b;
        public double Happy = 60, Bored = 0, Angry = 0;
        double lastQuestion = -100, pokeWindow = -100, throwWindow = -100, rpsUntil;
        int pokes, throws, askId;
        bool toldAboutKey;
        public string LastQuestion = "", LastAnswer = "";
        readonly List<Dictionary<string, object>> history = new List<Dictionary<string, object>>();

        public Brain(Buddy b) { this.b = b; }

        string Pick(params string[] s) { return b.Pick(s); }
        double Now { get { return b.Now; } }

        // ---------- feelings ----------
        public void Tick(double now, double dt)
        {
            TickThoughts(now);
            if (!b.S.Feelings) { Angry = 0; Bored = 0; Happy = 60; return; }
            Angry = Math.Max(0, Angry - 1.4 * dt);
            if (now - lastQuestion > 25) Bored = Math.Max(0, Bored - 0.9 * dt);
            Happy += (55 - Happy) * Math.Min(1, 0.02 * dt);
            if (b.PlayingGame) { Happy = Math.Min(100, Happy + 4 * dt); Bored = 0; }
        }

        public string VoiceMood()
        {
            if (!b.S.Feelings) return "neutral";
            if (Angry > 50) return "angry";
            if (Bored > 60) return "bored";
            if (Happy > 75 || b.PlayingGame) return "happy";
            return "neutral";
        }

        string MoodLine()
        {
            if (!b.S.Feelings) return "mood: normal";
            return string.Format("your mood: happy {0:0}/100, bored {1:0}/100, angry {2:0}/100", Happy, Bored, Angry);
        }

        public void GameStarted()
        {
            Bored = 0;
            Happy = Math.Min(100, Happy + 25);
            Angry = Math.Max(0, Angry - 30);
        }

        public void OnPoke()
        {
            if (b.FunMode) { b.ShowBubble(Pick("Hehe!", "Grab me and throw me!", "Boop!"), 1); return; }
            if (Now - pokeWindow > 6) { pokes = 0; pokeWindow = Now; }
            pokes++;
            if (b.Sleeping) { WakeFromSleep(false); return; }
            if (!b.S.Feelings) { b.Say("Yes?"); b.ListeningUntil = Now + 8; return; }
            if (pokes == 1) { b.Say(Pick("Yes?", "Hi there!", "You called?")); b.ListeningUntil = Now + 8; }
            else if (pokes == 2) b.Say(Pick("Hehe, that tickles!", "Hey!"));
            else if (pokes == 3) { Angry += 15; b.Say("Stop poking me!"); }
            else if (pokes >= 4)
            {
                Angry = Math.Min(100, Angry + 30);
                pokes = 0;
                ReactToAnger(true);
            }
        }

        // ---------- being picked up, thrown and smashed ----------
        double grabWindow = -100, lastTierAction = -100, lastHurtLine = -100;
        int grabs;

        public void OnPickedUp()
        {
            if (b.FunMode) { if (b.Rnd.NextDouble() < 0.5) b.ShowBubble(Pick("Wheee!", "Aim well!", "Throw me!", "Ready!"), 1); return; }
            if (Now - grabWindow > 25) { grabs = 0; grabWindow = Now; }
            grabs++;
            if (b.Sleeping) return; // he's fast asleep and floppy
            if (b.S.Feelings && grabs >= 3)
            {
                Angry = Math.Min(100, Angry + 5);
                if (Angry >= 25) { b.ShowBubble(Pick("Stop grabbing me!", "Put me DOWN!", "Hey! I'm warning you!", "Not again!"), 1.8); return; }
            }
            if (b.Rnd.NextDouble() < 0.7) b.ShowBubble(Pick("Whoa!", "Hey, put me down!", "Wheee!", "Where are we going?"), 1.8);
        }

        public void OnThrown(bool hard)
        {
            if (b.FunMode) { if (hard) b.ShowBubble(Pick("WHEEEE!", "Yahoooo!", "To infinity!"), 1); return; }
            if (!hard || b.Sleeping) return;
            if (Now - throwWindow > 20) { throws = 0; throwWindow = Now; }
            throws++;
            b.ShowBubble(Pick("Wheeeee!", "Aaaaah!", "I can fly!", "Nooooo!"), 1.5);
        }

        // He hit the floor really hard.
        public void OnSmashed(double speed)
        {
            if (b.Sleeping || b.FunMode) return; // the wake-up happens when he lands
            Event("the user smashed you on the ground");
            if (!b.S.Feelings) return;
            Angry = Math.Min(100, Angry + (speed > 2200 ? 15 : 9));
            Happy = Math.Max(0, Happy - 5);
            if (Now - lastHurtLine > 1.2) { lastHurtLine = Now; b.ShowBubble(Pick("OOF!", "OW!", "Ouch!"), 1.0); }
        }

        // He smashed into the edge of the screen.
        public void OnSlammed(double speed)
        {
            if (b.FunMode) { b.ShowBubble(Pick("Oof! Hehe!", "SPLAT! Again!"), 1); return; }
            Event("the user threw you SPLAT into the edge of the screen");
            if (b.S.Feelings) Angry = Math.Min(100, Angry + 16);
            Happy = Math.Max(0, Happy - 8);
            lastHurtLine = Now;
            b.ShowBubble(Pick("OOF!!", "SPLAT!", "OWWW!"), 1.2);
        }

        public void OnFlungOff()
        {
            b.ShowBubble(Pick("WHOA!", "Hey! I was standing there!"), 1.5);
            if (b.S.Feelings) Angry = Math.Min(100, Angry + 6);
        }

        public void OnShakenOff()
        {
            b.ShowBubble(Pick("Waaah!", "Whoa whoa whoa!", "No fair!"), 1.3);
        }

        // Called when he gets back on his feet (or lands normally).
        public void OnLanded(double impact, bool wasThrown, bool hard)
        {
            if (b.FunMode) return;
            if (b.Sleeping) { WakeFromSleep(hard || impact > 1300); return; }
            if (!hard && impact < 1300) return;
            if (!b.S.Feelings) { b.Say(Pick("Ouch!", "Oof!")); return; }
            ReactToAnger(false);
        }

        void WakeFromSleep(bool startled)
        {
            b.Sleeping = false;
            b.StopSitting();
            if (b.Busy && b.IsIdleJob) b.CancelJob();
            b.Touch();
            string hi = b.Introduced ? "" : " I'm " + b.S.Name + "! Say \"" + b.S.Name + "\" and tell me what to do.";
            b.Introduced = true;
            if (startled)
            {
                b.Say(Pick("WAAAH! I'm up! I'm up!", "WHOA! What happened?!", "AAH! Okay, okay, I'm awake!") + hi);
                b.FlashAct(Act.Scared, 1.2);
            }
            else b.Say(Pick("*yaaawn*... Oh, hi!", "Mmh... is it morning already?", "*yawn* Hello!") + hi);
        }

        // What he does when you hurt him, depending on how angry he is.
        void ReactToAnger(bool fromWords)
        {
            if (Now - lastTierAction < 2.5 || b.Hiding) return;
            lastTierAction = Now;
            if (Angry >= 75) { b.StompCursor(); return; }
            if (Angry >= 50) { if (b.Rnd.NextDouble() < 0.5) b.ThrowCursor(); else b.HangOnCursor(); return; }
            if (Angry >= 25)
            {
                b.Warn(Pick("Stop it! I mean it!", "Hey!! That HURT!", "Do that again and you'll be sorry!", "I'm warning you! Stop smashing me!"));
                return;
            }
            if (fromWords) b.Tantrum(Pick("Hey! That's not nice!", "Hmph!"));
            else if (Now - lastHurtLine > 1.5) b.Say(Pick("Ouch!", "Oof!", "Owww... dizzy..."));
        }

        public void TestReact() { lastTierAction = -100; ReactToAnger(false); }

        public void Vent(double amount) { Angry = Math.Max(0, Angry - amount); }

        // ---------- his things ----------
        double lastThingLine = -100;
        public void OnThingTaken(Thing t)
        {
            if (Now - lastThingLine < 4 || b.Hiding) return;
            lastThingLine = Now;
            b.ShowBubble(Pick("Hey! That's my " + t.Name + "!", "Careful with my " + t.Name + "!", "Where are you taking my " + t.Name + "?"), 2);
        }

        public void OnThingBroke(Thing t)
        {
            lastThingLine = Now;
            Event("the user broke your " + t.Name);
            if (b.Sleeping || b.Hiding) return;
            if (b.S.Feelings) { Angry = Math.Min(100, Angry + 12); Happy = Math.Max(0, Happy - 10); }
            b.Say(Pick("NOOO! My " + t.Name + "!", "You broke my " + t.Name + "!", "My " + t.Name + "! It's in pieces!"));
            if (!b.Busy && !b.RagMode) b.FlashAct(Act.Sad, 2);
        }

        public void OnHitByThing(Thing t, double speed)
        {
            if (b.FunMode) { b.ShowBubble(Pick("Hey! Hehe!", "Bonk!"), 1); return; }
            Event("the user threw your " + t.Name + " at you");
            if (b.S.Feelings) Angry = Math.Min(100, Angry + 8);
            lastHurtLine = Now;
            b.ShowBubble(Pick("OW! Who threw that?!", "Hey! Ow!", "BONK!"), 1.5);
        }

        public void NiceTry() { b.ShowBubble(Pick("Ha! Nice try!", "Nope! It's mine!", "Hehe, can't catch it!"), 1.5); }

        public void OnEscapeGrab()
        {
            Angry = Math.Max(0, Angry - 30);
            b.CancelJob();
            b.Say(Pick("Okay okay, here's your mouse.", "Fine! Take it."));
        }

        // ---------- hearing ----------
        public void Hear(string raw)
        {
            if (raw == null) return;
            raw = raw.Trim();
            if (raw.Length == 0) return;
            Log.Write("HEARD: " + raw);
            askId++;
            b.Touch();
            if (b.Sleeping) { b.Sleeping = false; b.StopSitting(); b.Introduced = true; }
            b.ListeningUntil = 0;
            try { Process(raw); }
            catch (Exception ex) { Log.Write("process: " + ex); b.Say("Oops, my brain got confused!"); }
        }

        static string Norm(string s)
        {
            s = s.ToLowerInvariant().Replace('’', '\'');
            s = s.Replace("what's", "what is").Replace("whats", "what is").Replace("let's", "lets").Replace("it's", "it is")
                 .Replace("you're", "you are").Replace("i'm", "i am").Replace("don't", "do not").Replace("can't", "can not")
                 .Replace("who's", "who is").Replace("how's", "how is").Replace("that's", "that is");
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch) || " +-*/.%^()=".IndexOf(ch) >= 0) sb.Append(ch);
                else if (ch == '×') sb.Append('*');
                else if (ch == '÷') sb.Append('/');
                else sb.Append(' ');
            }
            string r = " " + Regex.Replace(sb.ToString(), "\\s+", " ").Trim() + " ";
            // chat-style short words
            r = r.Replace(" u ", " you ").Replace(" ur ", " your ").Replace(" r ", " are ").Replace(" pls ", " please ")
                 .Replace(" plz ", " please ").Replace(" wat ", " what ").Replace(" wut ", " what ").Replace(" thx ", " thanks ")
                 .Replace(" ty ", " thank you ").Replace(" bc ", " because ").Replace(" whats ", " what is ").Replace(" im ", " i am ");
            r = r.Replace(" u ", " you ");
            return r.Trim().TrimEnd('.', ' ');
        }

        static string StripFillers(string c)
        {
            string prev;
            do
            {
                prev = c;
                c = Regex.Replace(c, @"^(hey|hi|ok|okay|yo|um|uh|so|and|please|can you|could you|would you|will you|i want you to|i need you to|go ahead and|lets|let us|now)\s+", "");
                c = Regex.Replace(c, @"\s+(please|for me|now|right now)$", "");
            } while (c != prev);
            return c.Trim();
        }

        static bool Has(string c, string pattern) { return Regex.IsMatch(c, pattern); }

        void Process(string raw)
        {
            string c = StripFillers(Norm(raw));
            if (c.Length == 0) { b.Say("Yes?"); b.ListeningUntil = Now + 8; return; }

            if (Now < rpsUntil && TryRps(c)) return;
            if (b.GameHear != null && !Has(c, @"^(stop|cancel|quit|exit|stop the game)$") && b.GameHear(c)) return;

            if (Has(c, @"\b(sorry|apologi[sz]e|my bad|forgive me)\b")) { Apology(); return; }
            if (IsInsult(c)) { Insulted(); return; }
            if (b.S.Feelings && Angry >= 70 && !Has(c, @"^(stop|exit|quit)$")) { Grumpy(); return; }
            if (IsCompliment(c) && c.Split(' ').Length <= 7) { Complimented(); return; }

            if (Commands(c, raw)) return;

            string display, spoken;
            if (Mathy.TryAnswer(c, out display, out spoken)) { Answer(raw, display, spoken, true); return; }
            if (Facts.TryAnswer(c, out display)) { Answer(raw, display, display, true); return; }

            if (b.S.EffectiveApiKey.Length > 0) { AskClaude(raw); return; }
            DontKnow(raw);
        }

        // ---------- commands ----------
        bool Commands(string c, string raw)
        {
            Match m;

            if (Has(c, @"^(stop|cancel|never ?mind|be quiet|quiet|shush|stop it|enough)$"))
            {
                b.CancelJob(); b.Voice.StopTalking(); b.ShowBubble("Okay!", 1.5); return true;
            }
            if (Has(c, @"^(exit|quit|close yourself|turn yourself off|go away forever|close stick ?buddy|exit stick ?buddy)$")) { b.ExitApp(); return true; }
            if (Has(c, @"^(shut ?down|restart|turn off)( the| my)? (computer|pc|laptop)")) { b.Say("I'm not allowed to turn off your computer. You can do that yourself!"); return true; }
            if (Has(c, @"^(help|what can you do|what can i say|commands|show commands|what do you do)")) { b.ShowHelp(); b.Say("Here's a list of things I can do!"); return true; }
            if (Has(c, @"^(your settings|settings|customi[sz]e( you| yourself)?|change your (look|settings)|options)$")) { b.OpenSettings(); b.Say("Customise me however you like!"); return true; }

            m = Regex.Match(c, @"(?:change|set) your name to (\w+)|(?:call yourself|your new name is|your name is now|your name is|i will call you|i am going to call you|rename yourself(?: to)?) (\w+)$");
            if (m.Success) { Rename(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value); return true; }
            if (Has(c, @"(what is your name|who are you|what are you called)")) { b.Say("I'm " + b.S.Name + ", your stick buddy! I live on your screen."); return true; }
            if (Has(c, @"(who made you|who created you|what are you)")) { b.Say("I'm a little stick figure who lives on your desktop. You made me, with a bit of help from Claude!"); return true; }

            m = Regex.Match(c, @"(?:colou?r(?: to)?|turn|be|make yourself|become|go) (red|orange|yellow|green|blue|purple|pink|black|white|gray|grey|brown|gold|cyan|lime|teal|navy|violet|magenta|rainbow)\b");
            if (m.Success) { SetColor(m.Groups[1].Value); return true; }

            if (Has(c, @"^(get |be |make yourself |grow )?(bigger|larger|huge)$|^grow( up)?$")) { SetSize(b.S.Size * 1.25, "Look how big I am!"); return true; }
            if (Has(c, @"^(get |be |make yourself )?(smaller|tiny|little)$|^shrink$")) { SetSize(b.S.Size / 1.25, "Tiny me!"); return true; }
            if (Has(c, @"normal size")) { SetSize(1.0, "Back to normal!"); return true; }

            if (Has(c, @"(mute yourself|stop talking out loud|no voice|silent mode|do not talk)")) { b.S.Muted = true; b.S.Save(); b.ShowBubble("Okay, I'll only use bubbles now.", 3); return true; }
            if (Has(c, @"(unmute yourself|talk again|use your voice|speak again)")) { b.S.Muted = false; b.S.Save(); b.Say("Yay, I can talk again!"); return true; }

            if (Has(c, @"(volume up|turn (it|the volume|the sound|the music) up|louder|increase (the )?volume|raise (the )?volume)")) { b.MediaKey(Native.VK_VOLUME_UP, 5, "Louder!"); return true; }
            if (Has(c, @"(volume down|turn (it|the volume|the sound|the music) down|quieter|lower (the )?volume|decrease (the )?volume)")) { b.MediaKey(Native.VK_VOLUME_DOWN, 5, "Quieter."); return true; }
            if (Has(c, @"^(mute|unmute)( the| my)?( sound| volume| computer| pc| audio)?$")) { b.MediaKey(Native.VK_VOLUME_MUTE, 1, null); return true; }
            if (Has(c, @"(next song|skip (this )?(song|track)|next track)")) { b.MediaKey(Native.VK_MEDIA_NEXT, 1, "Next song!"); return true; }
            if (Has(c, @"(previous song|last song|previous track|go back a song)")) { b.MediaKey(Native.VK_MEDIA_PREV, 1, "Going back!"); return true; }
            if (Has(c, @"^(pause|resume|unpause|play)( the)? (music|song|video)$|^(pause|resume|unpause)$")) { b.MediaKey(Native.VK_MEDIA_PLAY_PAUSE, 1, null); return true; }

            if (Has(c, @"(screenshot|screen shot|print screen|capture (the |my )?screen)")) { b.Screenshot(); return true; }

            if (Has(c, @"(what time|time is it|the time\b|current time)")) { Answer(raw, "It's " + DateTime.Now.ToString("h:mm tt") + ".", "It's " + DateTime.Now.ToString("h:mm tt") + ".", false); return true; }
            if (Has(c, @"(what day|the date|today s date|todays date|what month|what year)")) { string d = "Today is " + DateTime.Now.ToString("dddd, MMMM d, yyyy") + "."; Answer(raw, d, d, false); return true; }

            if (Has(c, @"\b(joke|jokes|make me laugh|something funny)\b")) { TellJoke(); return true; }
            if (Has(c, @"(how are you|how do you feel|are you ok|are you okay|how is it going|are you (happy|sad|bored|angry|mad))")) { MoodReport(); return true; }

            if (Has(c, @"(give (me )?(back )?my (mouse|cursor)|let go of my (mouse|cursor)|^let go$)")) { b.ReleaseGrab(); b.Say("Okay, here it is!"); return true; }
            if (Has(c, @"(move|grab|take|steal|play with|control|hold|catch) (my |the )?(mouse|cursor)")) { b.StartGrab(false); return true; }

            // the arcade
            if (Has(c, @"(game center|games menu|all (the |your )?games|show (me )?(the |your )?games|list (the |your )?games)")) { b.OpenGameCenter(); return true; }
            if (Has(c, @"balloon")) { b.StartBalloonPop(); return true; }
            if (Has(c, @"dodge ?ball|throw (balls|things|stuff) at me")) { b.StartDodgeball(); return true; }
            if (Has(c, @"\bwhack|whack a (mole|sticky)")) { b.StartWhack(); return true; }
            if (Has(c, @"\bhoops\b|shoot ?out|basketball (game|with me|against me)|play basketball with me|\bhorse\b")) { b.StartHoops(); return true; }
            if (Has(c, @"dance (battle|off|game|contest)|rhythm game|dance dance")) { b.StartDanceBattle(); return true; }
            if (Has(c, @"guess (my|the|your|what) (drawing|picture)|pictionary|drawing game|guess what (you|i) (draw|drew)")) { b.StartGuessDrawing(); return true; }

            if (b.Hiding)
            {
                if (Has(c, @"(give up|where are you|i surrender|come out|i quit)")) { b.GiveUpHideSeek(); return true; }
                if (Has(c, @"(found you|i see you|got you)")) { b.Say("Hehe, then click on me!"); return true; }
            }
            if (Has(c, @"\b(obby|obbies|obstacle course|obby lab|the lab|your lab|creator (place|mode)|level editor|train yourself|learn parkour)\b")) { b.OpenLab(); return true; }
            if (Has(c, @"(what is your iq|how smart are you|your iq|how much iq)"))
            {
                b.Say("My IQ is " + (int)LabStats.IQ + "! I'm at the " + LabStats.StageName + " stage. I've beaten " + LabStats.Beaten + (LabStats.Beaten == 1 ? " obby" : " obbies") + ". Build me more in my Obby Lab!");
                return true;
            }
            if (Has(c, @"^(forget everything|forget me|forget all|clear your memory|reset your memory)$")) { ForgetAll(); b.Say("Okay... everything is forgotten. Who are you again? Hehe, just kidding. But my memory is empty now."); return true; }
            if (Has(c, @"hide (and|n|an) (go )?seek|play hide")) { b.StartHideAndSeek(); return true; }

            // "play this game for me", "play roblox for me and collect coins", "take over"
            string full = Norm(raw);
            if (Has(full, @"\b(play|beat|control|take over|finish|win)\b") &&
                (Has(full, @"\bfor me\b|\byourself\b|\btake over\b|\byou play\b|\bcontrol (the|this|my) game\b") || Has(c, @"^(play|beat) (this|the) (game|level)$")))
            {
                double minutes = 5;
                Match mins = Regex.Match(full, @"\bfor (\d+|one|two|three|five|ten|fifteen|twenty|thirty) (minute|minutes|min|mins)\b");
                if (mins.Success)
                {
                    string w = mins.Groups[1].Value;
                    int n;
                    if (!int.TryParse(w, out n)) n = w == "one" ? 1 : w == "two" ? 2 : w == "three" ? 3 : w == "five" ? 5 : w == "ten" ? 10 : w == "fifteen" ? 15 : w == "twenty" ? 20 : 30;
                    minutes = n;
                    full = full.Replace(mins.Value, " ").Replace("  ", " ").Trim();
                }
                Match g = Regex.Match(full, @"\band (.+)$");
                string goal = g.Success ? g.Groups[1].Value : "";
                string name = null;
                Match nm = Regex.Match(full, @"\b(?:play|beat|win) ([a-z0-9][a-z0-9 ]{1,30}?) (?:for me|yourself|by yourself)\b");
                if (nm.Success && !Has(nm.Groups[1].Value, @"^(this|the|my|this game|the game|my game|it|a game|games?|this level|the level)$")) name = nm.Groups[1].Value;
                b.StartGamePilot(goal, name, minutes);
                return true;
            }
            if (Has(c, @"(you are it|catch me|chase me|tag me|come get me|try to catch me)")) { b.StartTag(false); return true; }
            if (Has(c, @"(i am it|i ll catch you|i will catch you|i ll chase you|i will chase you|run away from me)")) { b.StartTag(true); return true; }
            if (Has(c, @"\b(play )?tag\b")) { bool userIt = b.Rnd.NextDouble() < 0.5; b.StartTag(userIt); return true; }
            if (Has(c, @"tic ?tac ?toe|tick ?tack ?toe|noughts")) { b.StartTicTacToe(); return true; }
            if (Has(c, @"\bsnake\b")) { b.StartSnake(); return true; }
            if (Has(c, @"rock ?paper ?scissors?|rock paper")) { StartRps(); return true; }

            // his house
            if (Has(c, @"basket ?ball|shoot (some )?hoops")) { b.GoHomeActivity("basketball"); return true; }
            if (Has(c, @"\b(exercise|work ?out|push ?ups?|sit ?ups?|sports?|get strong|train)\b")) { b.GoHomeActivity("exercise"); return true; }
            if (Has(c, @"\b(draw|paint)\b")) { b.GoHomeActivity("draw"); return true; }
            if (Has(c, @"watch (the )?(tv|television|cartoons)")) { b.GoHomeActivity("tv"); return true; }
            if (Has(c, @"(clean up|tidy|put your (stuff|things) back)")) { b.GoHomeActivity("tidy"); return true; }
            if (Has(c, @"(show|bring back|get) (me )?(your|the) house")) { if (b.House == null || !b.House.Shown) b.ToggleHouse(); else b.Say("My house is right there!"); return true; }
            if (Has(c, @"(hide|pack away|remove|put away) (your|the) house")) { if (b.House != null && b.House.Shown) b.ToggleHouse(); return true; }
            if (Has(c, @"^(go )?(home|to your house|in your house|go inside)$|go home")) { b.GoHomeActivity(null); return true; }

            if (Has(c, @"(play (a |some )?games?|^lets play$|^play$|play with me|wanna play|want to play|i am bored|play something)"))
            {
                GameStarted();
                b.OpenGameCenter();
                return true;
            }

            if (Has(c, @"^(write|type|note|put)( it| that)?( down)?( in notepad)?$|^(write|put) (it|that) (down|in notepad)$"))
            {
                if (LastAnswer.Length == 0) b.Say("There's nothing to write yet. Ask me something first!");
                else b.WriteNote("Q: " + LastQuestion + "\nA: " + LastAnswer + "\n");
                return true;
            }
            m = Regex.Match(raw.Trim(), @"(?:^|\s)(?:write down|write|type|take a note|make a note|note down)\s*[:,]?\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success && Has(c, @"^(write|type|take a note|make a note|note down)")) { b.WriteNote(m.Groups[1].Value.Trim()); return true; }

            if (Has(c, @"\b(dance|boogie|groove)\b")) { b.Dance(); return true; }
            if (Has(c, @"\b(back ?flip|flip|somersault)\b")) { b.Flip(); return true; }
            if (Has(c, @"\bjump\b")) { b.JumpAround(); return true; }
            if (Has(c, @"^(wave|say hi|say hello|hi|hello|hey|yo|hiya|good morning|good evening|good afternoon|sup|what is up|wassup)( there)?$"))
            {
                b.WaveHello(Pick("Hi there!", "Hello!", "Hey, buddy!", "Hiya!")); return true;
            }
            if (Has(c, @"^(bye|goodbye|see you|see ya|bye bye)( later)?$")) { b.WaveHello(Pick("Bye! I'll be right here.", "See you later!")); return true; }
            if (Has(c, @"(go to sleep|take a nap|^sleep$|go to bed|good ?night)")) { b.GoSleep(); return true; }
            if (Has(c, @"(wake up|get up|stand up)")) { b.WakeUp(); return true; }
            if (Has(c, @"\b(sit|sit down|take a seat)\b")) { b.SitDown(); return true; }
            if (Has(c, @"(come here|come to me|come back|follow (me|my mouse))")) { b.ComeHere(); return true; }
            if (Has(c, @"^(go |walk |run |move )?(to the )?left$")) { b.GoDirection(-1); return true; }
            if (Has(c, @"^(go |walk |run |move )?(to the )?right$")) { b.GoDirection(1); return true; }
            if (Has(c, @"^(be angry|get angry|act angry|be mad|get mad)$")) { Angry = 65; b.Tantrum("GRRRR! Like this?!"); return true; }
            if (Has(c, @"^(be happy|cheer up|smile)$")) { Happy = 95; Angry = 0; Bored = 0; b.FlashAct(Act.Cheer, 2); b.Say("Yay! I'm happy!"); b.AddHearts(3); return true; }
            if (Has(c, @"^(yes|no|yeah|nope|okay|ok|cool|nice|wow|awesome)$")) { b.Say(Pick("Cool!", "Okay!", "Hehe!")); return true; }

            // --- opening, searching and closing things (checked last) ---
            m = Regex.Match(c, @"^(?:search|google|look up|search for|look for|find)(?: for)? (.+?)(?: on (google|youtube|the internet|the web|internet))?$");
            if (m.Success)
            {
                bool yt = m.Groups[2].Value == "youtube";
                b.WebSearch(m.Groups[1].Value, yt, Pick("Let me search for that!", "Searching!", "Looking it up!"));
                return true;
            }
            m = Regex.Match(c, @"^(?:play|watch|show me|find) (.+?) on youtube$|^(?:search )?youtube (?:for )?(.+)$");
            if (m.Success)
            {
                string q = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value;
                b.WebSearch(q, true, "Let's find it on YouTube!");
                return true;
            }

            if (Has(c, @"(show (me )?(the |my )?desktop|minimi[sz]e (all|everything))")) { b.ShowDesktop(); return true; }
            m = Regex.Match(c, @"^minimi[sz]e\s*(?:the |this |that |my )?(.*)$");
            if (m.Success) { b.CloseWindow(IsThis(m.Groups[1].Value) ? "" : m.Groups[1].Value, true); return true; }
            m = Regex.Match(c, @"^(?:close|shut|exit|quit)\s+(?:the |this |that |my )?(.*)$");
            if (m.Success) { b.CloseWindow(IsThis(m.Groups[1].Value) ? "" : m.Groups[1].Value, false); return true; }

            m = Regex.Match(c, @"^(?:open|start|launch|run|load|go to|bring up)\s+(?:up\s+)?(?:the\s+|my\s+|a\s+)?(.+)$");
            if (m.Success) { OpenSomething(m.Groups[1].Value.Trim(), true); return true; }
            // "play minecraft" = open the game Minecraft
            m = Regex.Match(c, @"^play (?:the game )?([a-z0-9][a-z0-9 ]{1,30})$");
            if (m.Success && !Has(m.Groups[1].Value, @"^(it|that|this|music|a song|song|video|something|with .*)$")) { OpenSomething(m.Groups[1].Value.Trim(), true); return true; }
            return false;
        }

        static bool IsThis(string s)
        {
            s = s.Trim();
            return s.Length == 0 || Regex.IsMatch(s, @"^(window|this|it|that|app|program|this window|that window|the window)$");
        }

        static readonly Dictionary<string, string> Sites = new Dictionary<string, string>
        {
            { "youtube", "https://www.youtube.com" }, { "google", "https://www.google.com" },
            { "gmail", "https://mail.google.com" }, { "netflix", "https://www.netflix.com" },
            { "wikipedia", "https://www.wikipedia.org" }, { "twitch", "https://www.twitch.tv" },
            { "tiktok", "https://www.tiktok.com" }, { "google maps", "https://maps.google.com" },
            { "maps", "https://maps.google.com" }, { "the internet", "https://www.google.com" },
            { "internet", "https://www.google.com" }, { "browser", "https://www.google.com" },
            { "web browser", "https://www.google.com" }, { "claude", "https://claude.ai" },
        };

        static readonly Dictionary<string, string> Apps = new Dictionary<string, string>
        {
            { "file explorer", "file explorer" }, { "files", "file explorer" }, { "explorer", "file explorer" },
            { "my files", "file explorer" }, { "folders", "file explorer" }, { "documents", "documents" },
            { "calculator", "calculator" }, { "calc", "calculator" }, { "paint", "paint" },
            { "windows settings", "settings" }, { "computer settings", "settings" }, { "pc settings", "settings" }, { "settings", "settings" },
            { "chrome", "chrome" }, { "google chrome", "chrome" }, { "edge", "edge" }, { "microsoft edge", "edge" },
            { "firefox", "firefox" }, { "task manager", "task manager" }, { "word", "word" }, { "excel", "excel" },
            { "powerpoint", "powerpoint" }, { "camera", "camera" }, { "store", "microsoft store" },
            { "microsoft store", "microsoft store" }, { "command prompt", "command prompt" }, { "terminal", "terminal" },
        };

        public void OpenSomething(string target, bool announce)
        {
            string t = target.Trim().TrimEnd('.');
            Match sm = Regex.Match(t, @"^(.+?) and (?:search|look up|google)(?: for)? (.+)$");
            if (sm.Success)
            {
                bool yt = sm.Groups[1].Value.Contains("youtube");
                b.WebSearch(sm.Groups[2].Value, yt, announce ? "Opening " + sm.Groups[1].Value + " and searching!" : null);
                return;
            }
            if (Regex.IsMatch(t, @"^(a |some )?games?$")) { Commands("play a game", "play a game"); return; }
            if (Regex.IsMatch(t, @"^notepad$"))
            {
                b.RunJob(async delegate (System.Threading.CancellationToken ct)
                {
                    if (announce) b.Say("Opening Notepad!");
                    IntPtr h = await b.EnsureNotepad(ct);
                    await b.StandOnWindow(h, 90 * b.U, ct);
                    await b.Do(Act.Wave, 1.2, ct);
                });
                return;
            }
            string url;
            if (Sites.TryGetValue(t, out url)) { b.OpenUrl(url, announce ? "Opening " + t + "!" : null); return; }
            Match dm = Regex.Match(t, @"^([a-z0-9\-]+)(?: dot |\.)(com|org|net|nl|io|tv|co\.uk|de|be|edu|gov)$");
            if (dm.Success) { b.OpenUrl("https://" + dm.Groups[1].Value + "." + dm.Groups[2].Value, announce ? "Going to " + t + "!" : null); return; }
            string query;
            if (!Apps.TryGetValue(t, out query)) query = t;
            b.OpenApp(query, t, announce);
        }

        // ---------- answering questions ----------
        public void Answer(string question, string display, string spoken, bool writeIt)
        {
            if (b.S.Feelings)
            {
                if (Bored >= 88 && b.Rnd.NextDouble() < 0.5)
                {
                    b.CancelJob();
                    b.Say(Pick("Ugh, I'm sooo bored of questions... Can we play a game first? Say: play a game!",
                               "*yawn* More questions? Let's play something instead!"));
                    Bored = Math.Min(100, Bored + 3);
                    return;
                }
                Bored = Math.Min(100, Bored + (Now - lastQuestion < 60 ? 14 : 8));
                lastQuestion = Now;
                if (Bored >= 60) spoken = Pick("*sigh* ", "Ugh, fine. ", "Yawn... ", "Again? Okay. ") + spoken;
            }
            LastQuestion = question; LastAnswer = display;
            b.DeliverAnswer(question, display, spoken, writeIt);
        }

        void DontKnow(string raw)
        {
            string tip = "";
            if (!toldAboutKey) { toldAboutKey = true; tip = " (Tip: put a Claude API key in my settings and I can answer almost anything!)"; }
            b.FlashAct(Act.Shrug, 1.5);
            b.WebSearch(raw, false, Pick("Hmm, I don't know that one... Let me look it up!", "Good question! Let me search the internet.") + tip);
        }

        void TellJoke()
        {
            string[] jokes =
            {
                "Why did the stick figure go to school? To get a little sharper!",
                "What do you call a sleeping dinosaur? A dino-snore!",
                "Why don't skeletons fight each other? They don't have the guts!",
                "What do you call a bear with no teeth? A gummy bear!",
                "Why was the computer cold? It left its Windows open!",
                "What do you call cheese that isn't yours? Nacho cheese!",
                "Why did the math book look sad? It had too many problems!",
                "How does a penguin build its house? Igloos it together!",
                "Why can't a bicycle stand up by itself? It's two tired!",
                "What did the ocean say to the beach? Nothing, it just waved!",
                "Why did the cookie go to the doctor? Because it felt crummy!",
                "What has ears but cannot hear? A cornfield!",
                "Why do bees have sticky hair? Because they use honeycombs!",
                "What do you call a fish with no eyes? A fsh!",
            };
            Happy = Math.Min(100, Happy + 5);
            b.Say(jokes[b.Rnd.Next(jokes.Length)]);
            b.FlashAct(Act.Cheer, 1.2);
        }

        void MoodReport()
        {
            if (!b.S.Feelings) { b.Say("I'm good, thanks for asking!"); return; }
            if (Angry > 50) b.Say("I'm still mad at you! Hmph. Say sorry!");
            else if (Bored > 60) b.Say("Honestly? I'm bored. Let's play a game!");
            else if (Happy > 75) { b.Say("I'm super duper happy!"); b.AddHearts(2); }
            else b.Say("I'm good! Ready to help.");
        }

        // ---------- feelings from words ----------
        static bool IsInsult(string c)
        {
            return Regex.IsMatch(c, @"\b(stupid|dumb|idiot|idiots|shut up|useless|ugly|loser|trash|garbage|noob|annoying|you suck|worst|dummy|moron|i hate you|hate you|you are bad|bad bot|bad boy|lame)\b");
        }

        static bool IsCompliment(string c)
        {
            return Regex.IsMatch(c, @"\b(good job|well done|thank you|thanks|thank|love you|you are the best|best buddy|awesome|amazing|so cool|you are cool|smart|great job|good boy|good girl|cute|nice job|you rock)\b");
        }

        void Insulted()
        {
            Event("the user said something mean to you");
            if (!b.S.Feelings) { b.Say("That's not very nice."); return; }
            Angry = Math.Min(100, Angry + 30);
            Happy = Math.Max(0, Happy - 20);
            lastTierAction = -100;
            ReactToAnger(true);
        }

        void Apology()
        {
            Event("the user said sorry");
            if (Angry > 20) { Angry = 0; Happy = Math.Min(100, Happy + 10); b.Say(Pick("Okay... I forgive you.", "Fine. We're friends again!")); b.AddHearts(3); }
            else b.Say(Pick("No worries! We're good.", "It's okay!"));
        }

        void Grumpy()
        {
            b.Tantrum(Pick("Hmph! I'm not doing anything until you say sorry!", "I'm still mad! Say sorry first."));
        }

        void Complimented()
        {
            Happy = Math.Min(100, Happy + 20);
            Angry = Math.Max(0, Angry - 25);
            b.Say(Pick("Aww, thank you!", "You're the best!", "Hehe, thanks!", "That makes me so happy!"));
            b.AddHearts(4);
            b.FlashAct(Act.Cheer, 1.5);
        }

        // ---------- customising ----------
        void Rename(string n)
        {
            n = Regex.Replace(n, "[^A-Za-z]", "");
            if (n.Length < 2) { b.Say("That name is too short!"); return; }
            n = char.ToUpper(n[0]) + n.Substring(1).ToLowerInvariant();
            b.S.Name = n;
            b.ApplySettings(true);
            b.Say("Okay! From now on, call me " + n + "!");
            b.FlashAct(Act.Cheer, 1.5);
        }

        void SetColor(string name)
        {
            if (name == "rainbow") b.S.Rainbow = true;
            else
            {
                b.S.Rainbow = false;
                if (name == "grey") name = "gray";
                Color c = Color.FromName(name);
                if (name == "white") c = Color.FromArgb(245, 245, 250);
                if (name == "black") c = Color.FromArgb(25, 25, 30);
                if (c.A == 0 && c.R == 0 && c.G == 0 && c.B == 0) { b.Say("I don't know that color."); return; }
                b.S.BodyColor = Color.FromArgb(c.R, c.G, c.B);
            }
            b.ApplySettings(false);
            b.Say(Pick("Ta-da! I'm " + name + " now!", "How do I look in " + name + "?"));
            b.FlashAct(Act.Cheer, 1.5);
        }

        void SetSize(double size, string line)
        {
            b.S.Size = Math.Max(0.5, Math.Min(2.5, size));
            b.ApplySettings(false);
            b.Say(line);
        }

        // ---------- rock paper scissors ----------
        public void StartRps()
        {
            GameStarted();
            rpsUntil = Now + 15;
            b.ListeningUntil = Now + 15;
            b.Say("Rock, paper, scissors! Say rock, paper, or scissors!");
            b.FlashAct(Act.Shrug, 1.0);
        }

        bool TryRps(string c)
        {
            int you = Regex.IsMatch(c, @"\b(rock|stone)\b") ? 0 : Regex.IsMatch(c, @"\bpaper\b") ? 1 : Regex.IsMatch(c, @"\bscissors?\b") ? 2 : -1;
            if (you < 0) return false;
            string[] n = { "rock", "paper", "scissors" };
            int me = b.Rnd.Next(3);
            int res = (3 + you - me) % 3;
            if (res == 0) { b.Say("I picked " + n[me] + " too! It's a tie! Again? Say rock, paper, or scissors!"); b.FlashAct(Act.Shrug, 1.5); }
            else if (res == 1) { b.Say("I picked " + n[me] + ". Aww, you win! Again? Say rock, paper, or scissors!"); b.FlashAct(Act.Sad, 1.5); }
            else { b.Say("I picked " + n[me] + "! I win! Hehe! Again? Say rock, paper, or scissors!"); b.FlashAct(Act.Cheer, 1.8); Happy = Math.Min(100, Happy + 8); }
            rpsUntil = Now + 14;
            b.ListeningUntil = Now + 14;
            return true;
        }

    }

    // ---------- offline math ----------
    public static class Mathy
    {
        static readonly Dictionary<string, int> Words = new Dictionary<string, int>
        {
            {"zero",0},{"one",1},{"two",2},{"three",3},{"four",4},{"five",5},{"six",6},{"seven",7},{"eight",8},{"nine",9},
            {"ten",10},{"eleven",11},{"twelve",12},{"thirteen",13},{"fourteen",14},{"fifteen",15},{"sixteen",16},
            {"seventeen",17},{"eighteen",18},{"nineteen",19},{"twenty",20},{"thirty",30},{"forty",40},{"fifty",50},
            {"sixty",60},{"seventy",70},{"eighty",80},{"ninety",90}
        };

        static string WordsToNumbers(string s)
        {
            string[] w = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> outp = new List<string>();
            long cur = 0, total = 0;
            bool inNum = false;
            for (int i = 0; i < w.Length; i++)
            {
                string x = w[i];
                bool nextIsNum = i + 1 < w.Length && (Words.ContainsKey(w[i + 1]) || w[i + 1] == "hundred" || w[i + 1] == "thousand" || w[i + 1] == "million");
                if (Words.ContainsKey(x)) { cur += Words[x]; inNum = true; }
                else if (x == "hundred" && inNum) cur = (cur == 0 ? 1 : cur) * 100;
                else if (x == "thousand" && inNum) { total += (cur == 0 ? 1 : cur) * 1000; cur = 0; }
                else if (x == "million" && inNum) { total += (cur == 0 ? 1 : cur) * 1000000; cur = 0; }
                else if (x == "a" && i + 1 < w.Length && (w[i + 1] == "hundred" || w[i + 1] == "thousand" || w[i + 1] == "million")) { cur = 1; inNum = true; }
                else if (x == "and" && inNum && nextIsNum) { }
                else
                {
                    if (inNum) { outp.Add((total + cur).ToString()); total = 0; cur = 0; inNum = false; }
                    outp.Add(x);
                }
            }
            if (inNum) outp.Add((total + cur).ToString());
            return string.Join(" ", outp);
        }

        public static bool TryAnswer(string c, out string display, out string spoken)
        {
            display = null; spoken = null;
            string s = " " + c + " ";
            s = Regex.Replace(s, @"\b(what is|how much is|how much|calculate|compute|solve|tell me|what does|is equal to|equal to|equals|equal|the answer to|do you know|can you tell me|what are)\b", " ");
            s = " " + WordsToNumbers(s) + " ";
            Match pm = Regex.Match(s, @"^\s*(\d+(?:\.\d+)?)\s*(?:%|percent)\s*of\s*(\d+(?:\.\d+)?)\s*$");
            if (pm.Success)
            {
                double pv = double.Parse(pm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0 *
                            double.Parse(pm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                display = pm.Groups[1].Value + "% of " + pm.Groups[2].Value + " = " + Fmt(pv);
                spoken = pm.Groups[1].Value + " percent of " + pm.Groups[2].Value + " is " + Fmt(pv) + "!";
                return true;
            }
            s = Regex.Replace(s, @"(\d+(?:\.\d+)?)\s*(?:%|percent)\s*of\s*(\d+(?:\.\d+)?)", "($1/100*$2)");
            s = Regex.Replace(s, @"square root of", " sqrt ");
            s = s.Replace(" plus ", " + ").Replace(" add ", " + ").Replace(" minus ", " - ").Replace(" take away ", " - ")
                 .Replace(" times ", " * ").Replace(" multiplied by ", " * ").Replace(" x ", " * ").Replace(" divided by ", " / ")
                 .Replace(" over ", " / ").Replace(" to the power of ", " ^ ").Replace(" squared ", " ^ 2 ").Replace(" cubed ", " ^ 3 ")
                 .Replace(" mod ", " % ");
            s = Regex.Replace(s, @"(\d)\s*x\s*(\d)", "$1*$2");
            s = s.Replace("=", " ").Trim();
            if (!Regex.IsMatch(s, @"\d")) return false;
            if (!Regex.IsMatch(s, @"^[\d\s\.\+\-\*/\^\(\)%]*(sqrt[\d\s\.\+\-\*/\^\(\)%]*)*$")) return false;
            if (!Regex.IsMatch(s, @"[\+\-\*/\^%]|sqrt")) return false;
            double v;
            try { v = new ExprParser(s).Parse(); }
            catch (DivideByZeroException)
            {
                display = "You can't divide by zero!";
                spoken = "You can't divide by zero! Even stick figures know that.";
                return true;
            }
            catch { return false; }
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            string pretty = Regex.Replace(s, @"\s+", "");
            pretty = Regex.Replace(pretty, @"([\+\-\*/\^%])", " $1 ").Replace("*", "×").Replace("/", "÷").Replace("sqrt", "√");
            pretty = Regex.Replace(pretty, @"\s+", " ").Trim();
            string res = Fmt(v);
            display = pretty + " = " + res;
            string sayExpr = pretty.Replace("×", "times").Replace("÷", "divided by").Replace("^", "to the power of")
                                   .Replace("√", "the square root of ").Replace("+", "plus").Replace(" - ", " minus ").Replace("%", "mod");
            spoken = sayExpr + " is " + res + "!";
            return true;
        }

        static string Fmt(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15)
                return ((long)Math.Round(v)).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
            return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }

        class ExprParser
        {
            readonly string s;
            int i;
            public ExprParser(string s) { this.s = s.Replace(" ", ""); }

            public double Parse()
            {
                double v = Expr();
                if (i != s.Length) throw new FormatException("extra");
                return v;
            }

            double Expr()
            {
                double v = Term();
                while (i < s.Length && (s[i] == '+' || s[i] == '-'))
                {
                    char op = s[i++];
                    double r = Term();
                    v = op == '+' ? v + r : v - r;
                }
                return v;
            }

            double Term()
            {
                double v = Power();
                while (i < s.Length && (s[i] == '*' || s[i] == '/' || s[i] == '%'))
                {
                    char op = s[i++];
                    double r = Power();
                    if ((op == '/' || op == '%') && r == 0) throw new DivideByZeroException();
                    v = op == '*' ? v * r : op == '/' ? v / r : v % r;
                }
                return v;
            }

            double Power()
            {
                double v = Unary();
                if (i < s.Length && s[i] == '^') { i++; v = Math.Pow(v, Power()); }
                return v;
            }

            double Unary()
            {
                if (i < s.Length && s[i] == '-') { i++; return -Unary(); }
                if (i < s.Length && s[i] == '+') { i++; return Unary(); }
                return Primary();
            }

            double Primary()
            {
                if (i < s.Length && s[i] == '(')
                {
                    i++;
                    double v = Expr();
                    if (i >= s.Length || s[i] != ')') throw new FormatException(")");
                    i++;
                    return v;
                }
                if (string.CompareOrdinal(s, i, "sqrt", 0, 4) == 0) { i += 4; return Math.Sqrt(Unary()); }
                int st = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (st == i) throw new FormatException("number");
                return double.Parse(s.Substring(st, i - st), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    // ---------- offline facts (used when there is no Claude key) ----------
    public static class Facts
    {
        static readonly string[][] Table =
        {
            new[] { @"(animal|creature|thing).*(lived|lives|live|living|life).*(longest|most|oldest|long)|oldest (animal|creature)|(longest|oldest) living (animal|creature)|animal.*(oldest|longest)",
                    "The oldest animal ever found was a clam named Ming. It was 507 years old! Greenland sharks can also live for about 400 years." },
            new[] { @"(biggest|largest) animal", "The blue whale is the biggest animal ever. It can be 30 meters long, as long as 3 buses!" },
            new[] { @"fastest animal", "The peregrine falcon is the fastest: it dives at about 390 kilometers per hour! On land, the cheetah wins at about 110." },
            new[] { @"tallest animal", "The giraffe is the tallest animal. It can be about 5 and a half meters tall!" },
            new[] { @"smallest animal", "One of the smallest animals is a tiny frog from Papua New Guinea, only about 8 millimeters long!" },
            new[] { @"(biggest|largest) planet", "Jupiter is the biggest planet. More than 1,300 Earths could fit inside it!" },
            new[] { @"how many planets", "There are 8 planets in our solar system." },
            new[] { @"hottest planet", "Venus is the hottest planet, about 465 degrees Celsius. Even hotter than Mercury!" },
            new[] { @"(tallest|highest|biggest) mountain", "Mount Everest is the tallest mountain on Earth, about 8,849 meters high." },
            new[] { @"longest river", "The Nile in Africa is usually called the longest river, about 6,650 kilometers long. The Amazon is almost as long!" },
            new[] { @"(biggest|largest) country", "Russia is the biggest country in the world." },
            new[] { @"(biggest|largest) ocean", "The Pacific Ocean is the biggest ocean." },
            new[] { @"speed of light|how fast is light", "Light travels about 300,000 kilometers every second!" },
            new[] { @"how far.*moon|distance.*moon", "The Moon is about 384,400 kilometers away from Earth." },
            new[] { @"how far.*sun|distance.*sun", "The Sun is about 150 million kilometers away from Earth." },
            new[] { @"how many (days|weeks|months) (are )?in a year", "A year has 365 days, 52 weeks and 12 months. A leap year has 366 days." },
            new[] { @"how many bones", "Grown-ups have 206 bones. Babies are born with about 300, and some join together!" },
            new[] { @"how many continents", "There are 7 continents: Africa, Antarctica, Asia, Australia, Europe, North America and South America." },
            new[] { @"how old is the (universe)", "The universe is about 13.8 billion years old." },
            new[] { @"how old is the earth", "The Earth is about 4.5 billion years old." },
            new[] { @"(biggest|largest) dinosaur", "Some of the biggest dinosaurs were titanosaurs like Argentinosaurus, maybe 35 meters long!" },
            new[] { @"why is the sky blue", "The sky is blue because air scatters blue sunlight more than the other colors." },
        };

        static readonly Dictionary<string, string> Capitals = new Dictionary<string, string>
        {
            {"france","Paris"},{"germany","Berlin"},{"the netherlands","Amsterdam"},{"netherlands","Amsterdam"},{"holland","Amsterdam"},
            {"spain","Madrid"},{"italy","Rome"},{"england","London"},{"the uk","London"},{"uk","London"},{"united kingdom","London"},
            {"america","Washington, D.C."},{"the usa","Washington, D.C."},{"usa","Washington, D.C."},{"united states","Washington, D.C."},
            {"japan","Tokyo"},{"china","Beijing"},{"turkey","Ankara"},{"russia","Moscow"},{"canada","Ottawa"},{"australia","Canberra"},
            {"brazil","Brasilia"},{"egypt","Cairo"},{"india","New Delhi"},{"mexico","Mexico City"},{"belgium","Brussels"},
            {"poland","Warsaw"},{"sweden","Stockholm"},{"norway","Oslo"},{"greece","Athens"},{"portugal","Lisbon"},{"morocco","Rabat"},
            {"ireland","Dublin"},{"scotland","Edinburgh"},{"denmark","Copenhagen"},{"finland","Helsinki"},{"austria","Vienna"},
            {"switzerland","Bern"},{"south korea","Seoul"},{"korea","Seoul"},{"argentina","Buenos Aires"},{"nigeria","Abuja"},
            {"south africa","Pretoria"},{"pakistan","Islamabad"},{"indonesia","Jakarta"},{"iran","Tehran"},{"saudi arabia","Riyadh"},
        };

        public static bool TryAnswer(string c, out string answer)
        {
            answer = null;
            Match m = Regex.Match(c, @"capital (?:city )?of (?:the )?([a-z ]+)$");
            if (m.Success)
            {
                string k = m.Groups[1].Value.Trim();
                string cap;
                if (Capitals.TryGetValue(k, out cap) || Capitals.TryGetValue("the " + k, out cap))
                {
                    answer = "The capital of " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(k) + " is " + cap + ".";
                    return true;
                }
            }
            foreach (string[] row in Table)
                if (Regex.IsMatch(c, row[0])) { answer = row[1]; return true; }
            return false;
        }
    }
}
