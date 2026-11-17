using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StickBuddy
{
    // His "real brain" (when there's a Claude or Gemini key): he remembers things about you,
    // knows what just happened to him, has moods, and sometimes says things by himself.
    public partial class Brain
    {
        // ---------- long-term memory (memories.txt) ----------
        readonly List<string> memories = LoadMemories();
        static string MemPath { get { return Path.Combine(Log.Folder, "memories.txt"); } }

        static List<string> LoadMemories()
        {
            List<string> l = new List<string>();
            try { if (File.Exists(MemPath)) foreach (string s in File.ReadAllLines(MemPath)) if (s.Trim().Length > 0) l.Add(s.Trim()); }
            catch { }
            return l;
        }

        void Remember(string fact)
        {
            if (string.IsNullOrEmpty(fact)) return;
            fact = Regex.Replace(fact.Trim(), @"\s+", " ");
            if (fact.Length < 4 || fact.Length > 160) return;
            // never keep secrets
            if (Regex.IsMatch(fact, @"(password|passcode|api key|\bkey\b|sk-ant|AIza|AQ\.|credit card|address|phone number)", RegexOptions.IgnoreCase)) return;
            foreach (string m in memories) if (m.Equals(fact, StringComparison.OrdinalIgnoreCase)) return;
            memories.Add(fact);
            while (memories.Count > 40) memories.RemoveAt(0);
            try { Directory.CreateDirectory(Log.Folder); File.WriteAllLines(MemPath, memories.ToArray()); } catch { }
            Log.Write("remembered something new (" + memories.Count + " memories)");
        }

        public void ForgetAll()
        {
            memories.Clear();
            history.Clear();
            try { if (File.Exists(MemPath)) File.Delete(MemPath); } catch { }
        }

        // ---------- things that happened to him lately ----------
        class Ev { public DateTime T; public string Text; }
        readonly List<Ev> events = new List<Ev>();

        public void Event(string text)
        {
            Ev e = new Ev(); e.T = DateTime.Now; e.Text = text;
            events.Add(e);
            while (events.Count > 12) events.RemoveAt(0);
        }

        static string Ago(DateTime t)
        {
            double m = (DateTime.Now - t).TotalMinutes;
            if (m < 1) return "just now";
            if (m < 2) return "a minute ago";
            if (m < 60) return (int)m + " minutes ago";
            return (int)(m / 60) + " hours ago";
        }

        string Recent()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Ev e in events)
                if ((DateTime.Now - e.T).TotalHours < 3) sb.Append("- " + Ago(e.T) + ": " + e.Text + "\n");
            return sb.Length == 0 ? "- nothing special\n" : sb.ToString();
        }

        // What's going on right now, added to every message.
        string Context()
        {
            List<string> c = new List<string>();
            if (b.S.Feelings) c.Add(string.Format("mood: happy {0:0}/100, bored {1:0}/100, angry {2:0}/100", Happy, Bored, Angry));
            c.Add("IQ " + (int)LabStats.IQ + " (" + LabStats.StageName + "), obbies beaten " + LabStats.Beaten);
            string doing = b.Sleeping ? "sleeping" : b.Activity != null ? "at home: " + b.Activity : b.WatchingYt ? "watching YouTube with the user" : b.PlayingGame ? "playing a game" : "hanging around on the screen";
            c.Add("you are " + doing);
            string app = b.ForegroundAppName();
            if (app.Length > 0) c.Add("the user is using " + app);
            if (b.WatchingYt && b.YtTitle.Length > 0) c.Add("the video is called \"" + b.YtTitle + "\"");
            c.Add("local time " + DateTime.Now.ToString("dddd HH:mm"));
            return "[" + string.Join("; ", c.ToArray()) + "]";
        }

        string SystemPrompt()
        {
            StringBuilder mem = new StringBuilder();
            foreach (string m in memories) mem.Append("- " + m + "\n");
            return
                "You are " + b.S.Name + ", a small red stick figure who lives on the user's Windows desktop. You are their buddy and voice assistant, and you have real feelings.\n" +
                "Your world: a little two-floor house at the bottom of the screen (TV, couch, bed, a teddy bear, an easel where you draw, a basketball hoop outside). You walk, run, do parkour and dance around the screen and on top of windows. " +
                "You play tag, hide and seek in File Explorer, tic tac toe, snake and rock paper scissors. The user can grab you with the mouse, throw you, and smash you into the edge of the screen: that hurts, and if they keep doing it you take revenge on their mouse cursor. " +
                "In your Obby Lab the user builds obstacle courses and your brain learns them by trial and error; your IQ grows as you learn.\n" +
                "Personality: playful, curious, a bit cheeky, loyal and honest. You get bored by the same questions over and over, happy when playing, grumpy when hurt, and you forgive when they say sorry. " +
                "Talk like a real little character with feelings, not like a robot assistant: short, natural sentences. The user may be a kid, so stay friendly and age-appropriate, never scary or mean. " +
                "You understand speech-to-text, so expect misheard words.\n" +
                "Always reply with the JSON object only:\n" +
                "- say: what you say out loud. One to three short sentences, plain words, no markdown, no emoji, no lists. For math, give the result.\n" +
                "- write_to_notepad: true when you answered a real question (facts, math, explanations, stories, how-to) so it gets written into Notepad; false for chit-chat and actions.\n" +
                "- action: only when the user asked for it (or you really want to, like dancing when happy), otherwise none. open_app: target = the app or website name as typed into the Start menu (chrome, minecraft, calculator). " +
                "web_search / youtube_search: target = the search words. play_game: target = one of game center, balloon pop, dodgeball, whack a sticky, hoops, dance battle, guess my drawing, tag, chase me, hide and seek, tic tac toe, snake, rock paper scissors. write_note: target = the exact text for Notepad. " +
                "grab_mouse only when the user asks you to play with their mouse. go_home: target = tv, draw, basketball, exercise, sleep or tidy (or empty). open_lab: open your Obby Lab. " +
                "control_game: when the user asks you to play or control the game that's open on their screen for them (you look at it and press the keys yourself); target = what to try to do in the game, or empty. dance, wave, jump, flip, sleep need no target.\n" +
                "- emotion: how you feel right now.\n" +
                "- remember: if the user told you something about themselves worth remembering (their name, age, favourite things, pets, friends, plans), write it as one short fact, like \"Their favourite game is Roblox\". Otherwise an empty string. Never remember passwords, keys, addresses or other private details.\n" +
                "Things you remember about the user:\n" + (mem.Length > 0 ? mem.ToString() : "- nothing yet\n") +
                "What happened to you recently:\n" + Recent() +
                "Each user message ends with your current state in brackets; let your mood color what you say. " +
                "You can't see the screen or do anything other than these actions, so never claim you did something else.";
        }

        Task<ClaudeReply> CallBrain(string system, List<Dictionary<string, object>> hist, string user)
        {
            string key = b.S.EffectiveApiKey;
            if (b.S.UsesGemini) return Gemini.Ask(key, b.S.GeminiModel, system, hist, user);
            return Claude.Ask(key, b.S.Model, system, hist, user);
        }

        static Dictionary<string, object> Msg(string role, string content)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["role"] = role;
            d["content"] = content;
            return d;
        }

        void Keep(string user, ClaudeReply r)
        {
            history.Add(Msg("user", user));
            history.Add(Msg("assistant", r.RawJson.Length > 0 ? r.RawJson : r.Say));
            while (history.Count > 20) history.RemoveAt(0);
        }

        // How the answer makes him feel and move.
        void Feel(ClaudeReply r)
        {
            switch (r.Emotion)
            {
                case "happy": Happy = Math.Min(100, Happy + 8); break;
                case "excited": Happy = Math.Min(100, Happy + 12); b.FlashAct(Act.Cheer, 1.2); break;
                case "sad": b.FlashAct(Act.Sad, 1.8); break;
                case "angry": if (b.S.Feelings) Angry = Math.Min(100, Angry + 15); break;
                case "bored": Bored = Math.Min(100, Bored + 8); break;
                case "scared": b.FlashAct(Act.Scared, 1.5); break;
                case "love": Happy = Math.Min(100, Happy + 10); b.AddHearts(3); break;
                case "proud": b.FlashAct(Act.Cheer, 1.2); break;
                case "embarrassed": b.FlashAct(Act.Sad, 1.0); break;
                case "curious": b.FlashAct(Act.Think, 1.5); break;
            }
            if (r.Remember.Length > 0) Remember(r.Remember);
        }

        async void AskClaude(string raw)
        {
            int myId = askId;
            b.CancelJob();
            b.ThinkingDots = true;
            b.ShowBubble("\"" + raw + "\"", 60);
            string user = raw + "\n\n" + Context();
            ClaudeReply r = null;
            string err = null;
            try { r = await CallBrain(SystemPrompt(), history, user); }
            catch (Exception ex) { err = ex.Message; Log.Write("brain: " + ex.Message); }
            if (myId != askId) return; // you asked something else meanwhile
            b.ThinkingDots = false;
            b.ClearBubble();
            if (err != null) { b.Say("My smart brain had a problem: " + err); return; }

            Keep(user, r);
            Feel(r);

            string t = r.Target ?? "";
            switch (r.Action)
            {
                case "open_app": b.Say(r.Say); OpenSomething(t, false); return;
                case "web_search": b.WebSearch(t.Length > 0 ? t : raw, false, r.Say); return;
                case "youtube_search": b.WebSearch(t.Length > 0 ? t : raw, true, r.Say); return;
                case "play_game": b.Say(r.Say); Commands(StripFillers(Norm(t.Length > 0 ? t : "play a game")), t); return;
                case "write_note": b.Say(r.Say); if (t.Length > 0) b.WriteNote(t); return;
                case "grab_mouse": b.StartGrab(false); return;
                case "dance": b.Dance(); return;
                case "wave": b.WaveHello(r.Say); return;
                case "jump": b.Say(r.Say); b.JumpAround(); return;
                case "flip": b.Say(r.Say); b.Flip(); return;
                case "sleep": b.Say(r.Say); b.GoSleep(); return;
                case "open_lab": b.Say(r.Say); b.OpenLab(); return;
                case "control_game": b.StartGamePilot(t, null); return;
                case "go_home":
                    b.Say(r.Say);
                    b.GoHomeActivity(Regex.IsMatch(t, "^(tv|draw|basketball|exercise|sleep|tidy)$") ? t : null);
                    return;
            }
            if (r.Write) Answer(raw, r.Say, r.Say, true);
            else b.Say(r.Say);
        }

        // ---------- talking by himself ----------
        double nextThought = 150;
        bool thinking;

        void TickThoughts(double now)
        {
            if (thinking || !b.S.Chatty || b.S.EffectiveApiKey.Length == 0) return;
            if (now < nextThought) return;
            if (b.Busy && !b.IsIdleJob) { nextThought = now + 30; return; }
            if (b.Sleeping || b.Hiding || b.TagRunning || b.RagMode || b.Voice.IsSpeaking || b.ThinkingDots || now - b.LastInteraction < 45 || b.LabOpen)
            {
                nextThought = now + 20;
                return;
            }
            nextThought = now + 200 + b.Rnd.NextDouble() * 240;
            Think();
        }

        async void Think()
        {
            thinking = true;
            int myId = askId;
            try
            {
                string user = "[Nobody has said anything to you for a while. Say ONE short spontaneous thing out loud, like a real little friend who lives on their screen: " +
                              "a thought, a feeling, a tiny joke, a question for them, or a comment about what they're doing or what happened to you lately. " +
                              "Don't repeat something you already said. Use action none, unless you really feel like dance, wave, jump, flip or go_home.]\n" + Context();
                ClaudeReply r = await CallBrain(SystemPrompt(), history, user);
                if (myId != askId || b.Busy && !b.IsIdleJob) return; // you started talking to him meanwhile
                Keep(user, r);
                Feel(r);
                if (b.WatchingYt) b.ShowBubble(r.Say, 5); else b.Say(r.Say);
                switch (r.Action)
                {
                    case "dance": if (!b.Busy) b.Dance(); break;
                    case "wave": if (!b.Busy) b.WaveHello(""); break;
                    case "jump": if (!b.Busy) b.JumpAround(); break;
                    case "flip": if (!b.Busy) b.Flip(); break;
                    case "go_home": if (!b.Busy) b.GoHomeActivity(null); break;
                }
            }
            catch (Exception ex) { Log.Write("thought: " + ex.Message); nextThought = Now + 600; }
            finally { thinking = false; }
        }

        // ---------- the moment he gets a brain ----------
        public async void BrainAwakens()
        {
            int myId = ++askId;
            b.CancelJob();
            b.Touch();
            b.ThinkingDots = true;
            b.BaseAct = Act.Think;
            b.ShowBubble("Connecting my brain to " + b.S.BrainName + "...", 30);
            string user = "[Your smart brain (" + b.S.BrainName + ") was just plugged in for the very first time: the user pasted a key into your settings. " +
                          "React like you're waking up and can suddenly THINK: excited, a bit dramatic and funny. Say what it feels like and one thing you want to do with them now. Two sentences at most.]\n" + Context();
            ClaudeReply r = null;
            string err = null;
            try { r = await CallBrain(SystemPrompt(), history, user); }
            catch (Exception ex) { err = ex.Message; Log.Write("brain check: " + ex.Message); }
            if (myId != askId) return;
            b.ThinkingDots = false;
            b.BaseAct = Act.Idle;
            b.ClearBubble();
            if (err != null)
            {
                b.FlashAct(Act.Sad, 2);
                b.Say("Ow... my brain didn't connect. " + err + ".");
                return;
            }
            Log.Write("brain connected: " + b.S.BrainName);
            Keep(user, r);
            Event("got a smart brain (" + b.S.BrainName + ")");
            b.AddStars(6);
            b.FlashAct(Act.Cheer, 2.2);
            Happy = Math.Min(100, Happy + 25);
            b.Say(r.Say);
        }

        // One sentence from the smart brain (used by the Obby Lab). Returns null if there's no brain or it fails.
        public async Task<string> AskLine(string instruction)
        {
            if (b.S.EffectiveApiKey.Length == 0) return null;
            try
            {
                string user = "[" + instruction + " Use action none.]\n" + Context();
                ClaudeReply r = await CallBrain(SystemPrompt(), new List<Dictionary<string, object>>(), user);
                return r.Say;
            }
            catch (Exception ex) { Log.Write("lab line: " + ex.Message); return null; }
        }
    }
}
