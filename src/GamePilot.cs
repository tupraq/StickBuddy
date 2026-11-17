using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickBuddy
{
    public class GameMove { public string Type = "", Key = ""; public double X, Y, Ms; }

    public class GameTurn
    {
        public string Thought = "", Say = "";
        public bool Done;
        public readonly List<GameMove> Moves = new List<GameMove>();
        public readonly List<string> HoldKeys = new List<string>();
    }

    // What he knows about a kind of game: how its controls work, plus tips.
    public class GameProfile
    {
        public string Name = "this game", File = "game", Tips = "";
        public bool RightDragLook;   // the camera only turns while the right mouse button is held (Roblox)
        public bool LockedCursor;    // the game holds the mouse in the middle (Minecraft, shooters)

        public static GameProfile Detect(IntPtr h)
        {
            GameProfile p = new GameProfile();
            string proc = (Native.ProcessName(h) ?? "").ToLowerInvariant();
            string title = Native.Title(h);
            if (proc.Contains("roblox") || title.IndexOf("Roblox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                p.Name = "Roblox"; p.File = "roblox"; p.RightDragLook = true;
                p.Tips = "This is Roblox. Move with W A S D (W walks the way the camera looks), jump with space. To cross a gap, hold w and press space just before the edge. " +
                         "Turn the camera with look (it drags with the right mouse button): positive x turns right. Zoom with i and o. " +
                         "In obbies, stay on the platforms, never touch red or glowing kill bricks, and line up straight before jumping. " +
                         "Never open the chat, the menu, the Robux store, the avatar shop or any buy button.";
            }
            else if (proc == "javaw" || proc.Contains("minecraft"))
            {
                p.Name = "Minecraft"; p.File = "minecraft"; p.LockedCursor = true;
                p.Tips = "This is Minecraft. W A S D to move, space to jump, shift to sneak, look turns your head. Click (left) to break blocks or hit, right_click to place or use. " +
                         "1 to 9 pick hotbar slots. Avoid e (inventory) unless you need it. Don't dig straight down.";
            }
            else if (proc == "chrome" || proc == "msedge" || proc == "firefox" || proc == "brave" || proc == "opera")
            {
                string t = Regex.Replace(title, @"\s[-|]\s(Google Chrome|Microsoft.*Edge|Mozilla Firefox|Brave|Opera).*$", "");
                p.Name = t.Length > 0 ? "the browser game \"" + t + "\"" : "a browser game";
                p.File = "web-" + Regex.Replace(t.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                p.Tips = "This game runs in a web browser. Only click inside the game itself, never on the address bar, tabs, ads or links around it. " +
                         "Browser games often use the arrow keys or W A S D, space to jump or shoot, and clicking.";
            }
            else
            {
                p.Name = title.Length > 0 ? "\"" + (title.Length > 40 ? title.Substring(0, 40) : title) + "\"" : "this game";
                p.File = "app-" + Regex.Replace(proc, @"[^a-z0-9]+", "-");
            }
            if (p.File.Length > 50) p.File = p.File.Substring(0, 50);
            return p;
        }

        // ---- tips he wrote himself after playing (%APPDATA%\StickBuddy\games\*.txt) ----
        static string Dir { get { return Path.Combine(Log.Folder, "games"); } }

        public List<string> LoadLearned()
        {
            List<string> l = new List<string>();
            try
            {
                string f = Path.Combine(Dir, File + ".txt");
                if (System.IO.File.Exists(f)) foreach (string s in System.IO.File.ReadAllLines(f)) if (s.Trim().Length > 0) l.Add(s.Trim());
            }
            catch { }
            return l;
        }

        public void SaveLearned(List<string> tips)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                while (tips.Count > 12) tips.RemoveAt(0);
                System.IO.File.WriteAllLines(Path.Combine(Dir, File + ".txt"), tips.ToArray());
            }
            catch { }
        }
    }

    // The "eyes and thumbs" brain: looks at a picture of the game and decides which keys to press.
    public static class GameBrain
    {
        // Only game keys. No Enter, no "/", no letters for typing: he can never chat or type in other apps.
        public static readonly Dictionary<string, ushort> Keys = new Dictionary<string, ushort>
        {
            { "w", 0x57 }, { "a", 0x41 }, { "s", 0x53 }, { "d", 0x44 },
            { "up", 0x26 }, { "down", 0x28 }, { "left", 0x25 }, { "right", 0x27 },
            { "space", 0x20 }, { "shift", 0x10 }, { "e", 0x45 }, { "f", 0x46 }, { "q", 0x51 }, { "r", 0x52 },
            { "z", 0x5A }, { "x", 0x58 }, { "c", 0x43 }, { "i", 0x49 }, { "o", 0x4F },
            { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 }, { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
        };

        static Dictionary<string, object> O(string k, object v) { return Claude.Obj(k, v); }

        public static Dictionary<string, object> Schema()
        {
            Dictionary<string, object> mp = new Dictionary<string, object>();
            Dictionary<string, object> type = O("type", "string");
            type["enum"] = new[] { "press", "hold", "click", "right_click", "move", "look", "wait" };
            mp["type"] = type;
            mp["key"] = O("type", "string");
            mp["x"] = O("type", "number");
            mp["y"] = O("type", "number");
            mp["ms"] = O("type", "number");
            Dictionary<string, object> move = O("type", "object");
            move["properties"] = mp;
            move["required"] = new[] { "type", "key", "x", "y", "ms" };
            move["additionalProperties"] = false;
            Dictionary<string, object> moves = O("type", "array");
            moves["items"] = move;
            Dictionary<string, object> holds = O("type", "array");
            holds["items"] = O("type", "string");

            Dictionary<string, object> props = new Dictionary<string, object>();
            props["thought"] = O("type", "string");
            props["say"] = O("type", "string");
            props["actions"] = moves;
            props["hold_keys"] = holds;
            props["done"] = O("type", "boolean");
            Dictionary<string, object> s = O("type", "object");
            s["properties"] = props;
            s["required"] = new[] { "thought", "say", "actions", "hold_keys", "done" };
            s["additionalProperties"] = false;
            return s;
        }

        static string Prompt(string name, string goal, GameProfile g, List<string> learned)
        {
            return
                "You are " + name + ", a little stick figure buddy who is playing " + g.Name + " on the user's computer for them, using the keyboard and mouse. " +
                "You see a new screenshot of the game window every second or two. The game keeps running while you think, so play like a real player with slow reactions: keep moving, and steer.\n" +
                "Goal from the user: " + (string.IsNullOrEmpty(goal) ? "play the game and do well" : goal) + "\n" +
                (g.Tips.Length > 0 ? g.Tips + "\n" : "") +
                (learned.Count > 0 ? "Tips you wrote yourself after playing this game before:\n- " + string.Join("\n- ", learned.ToArray()) + "\n" : "") +
                "Each turn you answer with:\n" +
                "- hold_keys: the keys to KEEP holding down until your next turn (for example [\"w\"] to keep running forward, [\"w\",\"shift\"] to sprint, [] to stand still). This is how you move smoothly.\n" +
                "- actions: quick things to do right now, in order (0 to 5):\n" +
                "  press: tap a key once (like space to jump). hold: hold a key for ms milliseconds (100 to 800).\n" +
                "  click / right_click: click at x,y. move: move the mouse pointer to x,y. x and y go from 0 to 1000 across the screenshot (0,0 is the top left).\n" +
                "  look: turn the camera: x and y say how far (-500 to 500; positive x turns right, positive y looks down; about 250 is a quarter turn).\n" +
                "  wait: wait ms milliseconds (up to 1000).\n" +
                "Allowed keys only: w a s d up down left right space shift e f q r z x c i o 1 2 3 4 5 6 7 8 9. Use an empty key and 0 for fields you don't need.\n" +
                "Hard rules: never type or chat. Never click anything that buys or spends Robux, coins, gems or money, opens a shop, store, chat, friends, settings, report, log out, delete or leave. " +
                "If a purchase, payment, login or chat screen is showing, or this isn't a game, or the game needs the user, set done to true and say why.\n" +
                "Learn from what happened in your last turns: if you keep falling or getting stuck, try something different.\n" +
                "thought: what you see and your plan, one short sentence. say: a short fun line for your speech bubble, or an empty string most of the time. done: true only when you should stop.";
        }

        static Task<string> Ask(Settings s, string system, string user, string jpeg, Dictionary<string, object> schema, int maxTokens)
        {
            string key = s.EffectiveApiKey;
            return s.UsesGemini
                ? Gemini.AskRaw(key, s.GeminiModel, system, user, jpeg, schema, maxTokens)
                : Claude.AskRaw(key, s.Model, system, user, jpeg, schema);
        }

        public static async Task<GameTurn> Decide(Settings s, string jpegB64, string goal, GameProfile g, List<string> learned, List<string> notes, int turn, string holding)
        {
            string user = "Turn " + turn + ". You are holding: " + (holding.Length > 0 ? holding : "nothing") + ".\n" +
                          (notes.Count > 0 ? "Your last turns:\n" + string.Join("\n", notes.ToArray()) : "This is your first look at the game.") + "\nWhat now?";
            string json = await Ask(s, Prompt(s.Name, goal, g, learned), user, jpegB64, Schema(), 600);
            return Parse(json);
        }

        // After playing: he writes himself a few tips for next time.
        public static async Task<List<string>> Reflect(Settings s, GameProfile g, List<string> notes, List<string> learned)
        {
            Dictionary<string, object> props = new Dictionary<string, object>();
            Dictionary<string, object> arr = O("type", "array");
            arr["items"] = O("type", "string");
            props["tips"] = arr;
            Dictionary<string, object> schema = O("type", "object");
            schema["properties"] = props;
            schema["required"] = new[] { "tips" };
            schema["additionalProperties"] = false;
            string user = "You just finished playing " + g.Name + ". Your notes from the last turns:\n" + string.Join("\n", notes.ToArray()) +
                          (learned.Count > 0 ? "\nTips you already had:\n- " + string.Join("\n- ", learned.ToArray()) : "") +
                          "\nWrite 1 to 3 NEW short, practical tips for yourself for next time you play this game (controls that worked, mistakes to avoid). No repeats.";
            string json = await Ask(s, "You are " + s.Name + ", a stick figure learning to play video games. Reply with JSON only.", user, null, schema, 400);
            List<string> tips = new List<string>();
            try
            {
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(Trim(json)) as Dictionary<string, object>;
                object t;
                if (d != null && d.TryGetValue("tips", out t) && t is IEnumerable)
                    foreach (object o in (IEnumerable)t) if (o != null && o.ToString().Trim().Length > 3) tips.Add(o.ToString().Trim());
            }
            catch { }
            return tips;
        }

        static string Trim(string json)
        {
            json = (json ?? "").Trim();
            int a = json.IndexOf('{'), b = json.LastIndexOf('}');
            return a >= 0 && b > a ? json.Substring(a, b - a + 1) : json;
        }

        static double Num(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
        }

        public static GameTurn Parse(string json)
        {
            GameTurn t = new GameTurn();
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(Trim(json)) as Dictionary<string, object>;
            if (d == null) { t.Done = true; t.Say = "Hmm, my brain got confused."; return t; }
            t.Thought = Str(d, "thought"); t.Say = Str(d, "say");
            object done;
            t.Done = d.TryGetValue("done", out done) && done is bool && (bool)done;
            object acts;
            if (d.TryGetValue("actions", out acts) && acts is IEnumerable)
                foreach (object o in (IEnumerable)acts)
                {
                    Dictionary<string, object> m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    GameMove gm = new GameMove();
                    gm.Type = Str(m, "type").ToLowerInvariant(); gm.Key = Str(m, "key").Trim().ToLowerInvariant();
                    gm.X = Num(m, "x"); gm.Y = Num(m, "y"); gm.Ms = Num(m, "ms");
                    t.Moves.Add(gm);
                    if (t.Moves.Count >= 6) break;
                }
            object holds;
            if (d.TryGetValue("hold_keys", out holds) && holds is IEnumerable && !(holds is string))
                foreach (object o in (IEnumerable)holds)
                {
                    string k = (o ?? "").ToString().Trim().ToLowerInvariant();
                    if (Keys.ContainsKey(k) && !t.HoldKeys.Contains(k) && t.HoldKeys.Count < 3) t.HoldKeys.Add(k);
                }
            return t;
        }
    }

    // Sticky plays the game that's in front, by himself. Esc stops him straight away.
    public partial class Buddy
    {
        public bool PilotActive;
        readonly HashSet<ushort> pilotKeys = new HashSet<ushort>();      // quick key presses in progress
        readonly HashSet<string> pilotHeld = new HashSet<string>();      // keys he keeps holding (walking, running)
        bool pilotLeft, pilotRight;
        Point pilotCursor;
        bool pilotLockedCursor;

        static bool EscDown() { return (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0; }

        bool PilotWindowOk(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
            if (Native.ProcessId(h) == myPid) return false;
            string c = Native.ClassName(h);
            return !(c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "CabinetWClass" || c == "Windows.UI.Core.CoreWindow");
        }

        public void StartGamePilot(string goal, string windowName) { StartGamePilot(goal, windowName, 5); }

        public void StartGamePilot(string goal, string windowName, double minutes)
        {
            Touch();
            if (S.EffectiveApiKey.Length == 0)
            {
                Say("I need my smart brain to see the game! Put a Gemini key in my settings first.");
                return;
            }
            if (!S.MouseTricks)
            {
                Say("I'm not allowed to use your mouse and keyboard. Turn on mouse tricks in my settings first.");
                return;
            }
            IntPtr h = string.IsNullOrEmpty(windowName) ? LastExternalWindow : FindWindowByName(windowName);
            if (!PilotWindowOk(h))
            {
                if (!string.IsNullOrEmpty(windowName))
                {
                    // not open yet: open it, then you pick a game/level
                    OpenApp(windowName, windowName, false);
                    ShowBubble("Opening " + windowName + "! Pick a game, click on it, then say \"play this game for me\".", 8);
                }
                else Say("Open your game first and click on it, then ask me again!");
                return;
            }
            minutes = Clamp(minutes, 1, 30);
            GameProfile prof = GameProfile.Detect(h);
            RunJob(async delegate (CancellationToken ct)
            {
                PilotActive = true;
                PlayingGame = true;
                pilotLockedCursor = prof.LockedCursor;
                Brain.GameStarted();
                List<string> notes = new List<string>(), allNotes = new List<string>();
                List<string> learned = prof.LoadLearned();
                int turns = 0;
                string stopLine = null;
                try
                {
                    Say(learned.Count > 0
                        ? Pick("Ooh, " + prof.Name + " again! I remember some tricks. Press Esc to take over.", "My turn! I've practised this one. Esc stops me.")
                        : Pick("Ooh, let me play " + prof.Name + "! Press Esc any time to take over.", "My turn! Watch this. Press Esc to stop me."));
                    Log.Write("game pilot: start (" + prof.File + ", " + learned.Count + " tips, " + minutes + " min)");
                    Native.RECT wr = Native.GetVisibleRect(h);
                    if (!Native.IsZoomed(h) && wr.Top - 110 * U > Screen.FromHandle(h).WorkingArea.Top) await StandOnWindow(h, wr.Width - 70 * U, ct);
                    Facing = -1;
                    BaseAct = Act.Play;
                    Native.ForceForeground(h);
                    await Wait(0.4, ct);
                    pilotCursor = Cursor.Position;
                    double end = Now + minutes * 60, lostSince = -1;
                    while (Now < end)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (EscDown()) { stopLine = "Okay, you take over!"; break; }
                        if (!Native.IsWindow(h)) { stopLine = "The game closed!"; break; }
                        if (Native.GetForegroundWindow() != h)
                        {
                            ReleasePilotInput();
                            if (lostSince < 0) { lostSince = Now; ShowBubble("Paused! Click on the game so I can keep playing.", 4); }
                            if (Now - lostSince > 15) { stopLine = "You switched away, so I stopped playing."; break; }
                            await Wait(0.3, ct);
                            pilotCursor = Cursor.Position;
                            continue;
                        }
                        lostSince = -1;

                        Rectangle client = Native.ClientScreenRect(h);
                        if (client.Width < 50 || client.Height < 50) { stopLine = "I can't see the game window."; break; }
                        string jpeg = CaptureJpeg(client);
                        ThinkingDots = true;
                        Task<GameTurn> ask = GameBrain.Decide(S, jpeg, goal, prof, learned, notes, ++turns, string.Join(" + ", new List<string>(pilotHeld).ToArray()));
                        // he keeps moving while he thinks, but never "blind" for long
                        double asked = Now;
                        string why = null;
                        while (!ask.IsCompleted)
                        {
                            if (EscDown()) { why = "Okay, you take over!"; break; }
                            if (Native.GetForegroundWindow() != h) { ReleasePilotInput(); break; }
                            if (!pilotLockedCursor && MovedByYou()) { why = "Oh, you're playing now? Okay, your turn!"; break; }
                            if (Now - asked > 3.0 && pilotHeld.Count > 0) ReleaseHeld();
                            await Task.Delay(25, ct);
                        }
                        ThinkingDots = false;
                        if (why != null) { stopLine = why; break; }
                        if (!ask.IsCompleted) continue;
                        if (ask.IsFaulted)
                        {
                            Exception err = ask.Exception.InnerException ?? ask.Exception;
                            Log.Write("game pilot: " + err.Message);
                            ReleasePilotInput();
                            ClaudeHttpException he = err as ClaudeHttpException;
                            if (he != null && he.Status == 429) { ShowBubble("Phew, my brain needs a little breather...", 6); await Wait(20, ct); pilotCursor = Cursor.Position; continue; }
                            stopLine = "My smart brain had a problem: " + err.Message;
                            break;
                        }
                        if (ask.IsCanceled) continue;
                        GameTurn t = ask.Result;
                        ct.ThrowIfCancellationRequested();
                        if (t.Say.Length > 0) ShowBubble(t.Say, 3);
                        List<string> did = new List<string>();
                        foreach (GameMove m in t.Moves)
                        {
                            if (!await DoMove(h, client, m, prof, ct)) break;
                            did.Add(Describe(m));
                        }
                        SetHeld(t.HoldKeys);
                        string note = "Turn " + turns + ": " + (t.Thought.Length > 0 ? t.Thought : "(no thought)") + " | did: " + (did.Count > 0 ? string.Join(", ", did.ToArray()) : "nothing") +
                                      (t.HoldKeys.Count > 0 ? " | kept holding " + string.Join("+", t.HoldKeys.ToArray()) : "");
                        notes.Add(note); allNotes.Add(note);
                        while (notes.Count > 6) notes.RemoveAt(0);
                        while (allNotes.Count > 30) allNotes.RemoveAt(0);
                        if (EscDown()) { stopLine = "Okay, you take over!"; break; }
                        if (t.Done) { stopLine = t.Say.Length > 0 ? t.Say : "I'll stop here. Your turn!"; break; }
                    }
                    if (stopLine == null) stopLine = Pick("Phew, that was fun! Your turn now.", "Okay, I played enough. Your turn!");
                }
                finally
                {
                    ReleasePilotInput();
                    PilotActive = false;
                    PlayingGame = false;
                    ThinkingDots = false;
                    Log.Write("game pilot: stop after " + turns + " turns");
                    Brain.Event("you played " + prof.Name + " for the user (" + turns + " turns)");
                }
                BaseAct = Act.Idle;
                Say(stopLine);
                if (turns >= 4) LearnFromGame(prof, allNotes, learned);
            });
        }

        // Writes down what he learned, so he plays better next time.
        async void LearnFromGame(GameProfile prof, List<string> notes, List<string> learned)
        {
            try
            {
                List<string> tips = await GameBrain.Reflect(S, prof, notes, learned);
                if (tips.Count == 0) return;
                List<string> all = new List<string>(learned);
                all.AddRange(tips);
                prof.SaveLearned(all);
                Log.Write("game pilot: learned " + tips.Count + " new tips for " + prof.File);
                ShowBubble("I learned something! " + tips[0], 6);
            }
            catch (Exception ex) { Log.Write("game learn: " + ex.Message); }
        }

        static string Describe(GameMove m)
        {
            switch (m.Type)
            {
                case "press": return "pressed " + m.Key;
                case "hold": return "held " + m.Key + " " + (int)m.Ms + "ms";
                case "click": return "clicked " + (int)m.X + "," + (int)m.Y;
                case "right_click": return "right-clicked " + (int)m.X + "," + (int)m.Y;
                case "move": return "moved mouse to " + (int)m.X + "," + (int)m.Y;
                case "look": return "looked " + (int)m.X + "," + (int)m.Y;
                case "wait": return "waited";
            }
            return m.Type;
        }

        bool MovedByYou()
        {
            Point c = Cursor.Position;
            return Math.Abs(c.X - pilotCursor.X) + Math.Abs(c.Y - pilotCursor.Y) > 120 * Dpi;
        }

        // Is it still okay to touch the keyboard and mouse? (the game is in front, no Esc, you aren't moving the mouse)
        bool StillOk(IntPtr h)
        {
            if (EscDown() || Native.GetForegroundWindow() != h) return false;
            if (!pilotLockedCursor && MovedByYou()) return false;
            return true;
        }

        async Task<bool> Hold(IntPtr h, double ms, CancellationToken ct)
        {
            double end = Now + ms / 1000.0;
            while (Now < end)
            {
                if (!StillOk(h)) return false;
                await Task.Delay(25, ct);
            }
            return true;
        }

        Point MapToClient(Rectangle client, double x, double y)
        {
            x = Clamp(x, 0, 1000); y = Clamp(y, 0, 1000);
            return new Point(client.Left + 2 + (int)((client.Width - 4) * x / 1000.0), client.Top + 2 + (int)((client.Height - 4) * y / 1000.0));
        }

        // Keys he keeps holding between turns (like W to keep running).
        void SetHeld(List<string> keys)
        {
            foreach (string k in new List<string>(pilotHeld))
                if (!keys.Contains(k)) { Native.GameKey(GameBrain.Keys[k], false); pilotHeld.Remove(k); }
            foreach (string k in keys)
                if (!pilotHeld.Contains(k)) { Native.GameKey(GameBrain.Keys[k], true); pilotHeld.Add(k); }
        }

        void ReleaseHeld() { SetHeld(new List<string>()); }

        async Task<bool> DoMove(IntPtr h, Rectangle client, GameMove m, GameProfile prof, CancellationToken ct)
        {
            if (!StillOk(h)) return false;
            ushort vk;
            switch (m.Type)
            {
                case "press":
                    if (!GameBrain.Keys.TryGetValue(m.Key, out vk)) return true;
                    Native.GameKey(vk, true); pilotKeys.Add(vk);
                    await Task.Delay(70, ct);
                    if (!pilotHeld.Contains(m.Key)) Native.GameKey(vk, false);
                    pilotKeys.Remove(vk);
                    await Task.Delay(30, ct);
                    return true;
                case "hold":
                    {
                        if (!GameBrain.Keys.TryGetValue(m.Key, out vk)) return true;
                        Native.GameKey(vk, true); pilotKeys.Add(vk);
                        bool ok = await Hold(h, Clamp(m.Ms, 80, 800), ct);
                        if (!pilotHeld.Contains(m.Key)) Native.GameKey(vk, false);
                        pilotKeys.Remove(vk);
                        return ok;
                    }
                case "click":
                case "right_click":
                    {
                        bool right = m.Type == "right_click";
                        if (!prof.LockedCursor)
                        {
                            Point p = MapToClient(client, m.X, m.Y);
                            Native.SetCursorPos(p.X, p.Y); pilotCursor = p;
                            await Task.Delay(40, ct);
                        }
                        if (!StillOk(h)) return false;
                        Native.MouseButton(right, true);
                        if (right) pilotRight = true; else pilotLeft = true;
                        await Task.Delay(60, ct);
                        Native.MouseButton(right, false);
                        if (right) pilotRight = false; else pilotLeft = false;
                        await Task.Delay(60, ct);
                        return true;
                    }
                case "move":
                    {
                        if (prof.LockedCursor) return true;
                        Point p = MapToClient(client, m.X, m.Y);
                        Native.SetCursorPos(p.X, p.Y); pilotCursor = p;
                        await Task.Delay(50, ct);
                        return true;
                    }
                case "look":
                    {
                        int dx = (int)Clamp(m.X, -500, 500), dy = (int)Clamp(m.Y, -500, 500);
                        if (prof.RightDragLook)
                        {
                            // Roblox: hold the right mouse button and drag to turn the camera
                            Native.MouseButton(true, true); pilotRight = true;
                            await Task.Delay(30, ct);
                            for (int i = 1; i <= 5; i++) { Native.MouseNudge(dx / 5, dy / 5); await Task.Delay(12, ct); }
                            Native.MouseButton(true, false); pilotRight = false;
                        }
                        else
                        {
                            for (int i = 1; i <= 5; i++) { Native.MouseNudge(dx / 5, dy / 5); await Task.Delay(12, ct); }
                        }
                        await Task.Delay(50, ct);
                        pilotCursor = Cursor.Position;
                        return true;
                    }
                case "wait":
                    return await Hold(h, Clamp(m.Ms, 0, 1000), ct);
            }
            return true;
        }

        // Lets go of every key and mouse button he might still be holding.
        void ReleasePilotInput()
        {
            foreach (ushort vk in pilotKeys) Native.GameKey(vk, false);
            pilotKeys.Clear();
            ReleaseHeld();
            if (pilotLeft) { Native.MouseButton(false, false); pilotLeft = false; }
            if (pilotRight) { Native.MouseButton(true, false); pilotRight = false; }
        }

        // A small JPEG picture of just the game window, for his brain to look at.
        static string CaptureJpeg(Rectangle r)
        {
            using (Bitmap full = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(full)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
                double k = Math.Min(1.0, Math.Min(800.0 / r.Width, 600.0 / r.Height));
                int w = Math.Max(1, (int)(r.Width * k)), hh = Math.Max(1, (int)(r.Height * k));
                using (Bitmap small = new Bitmap(w, hh, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g2 = Graphics.FromImage(small))
                    {
                        g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g2.DrawImage(full, 0, 0, w, hh);
                    }
                    ImageCodecInfo jpg = null;
                    foreach (ImageCodecInfo ci in ImageCodecInfo.GetImageEncoders()) if (ci.MimeType == "image/jpeg") jpg = ci;
                    using (EncoderParameters ep = new EncoderParameters(1))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 68L);
                        small.Save(ms, jpg, ep);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }
    }
}
