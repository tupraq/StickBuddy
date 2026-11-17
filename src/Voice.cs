using System;
using System.Collections.Generic;
using System.Security;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StickBuddy
{
    // His mouth (text-to-speech) and ears (Windows speech recognition). Works offline.
    public class Voice : IDisposable
    {
        readonly Buddy b;
        volatile SpeechSynthesizer synth;
        SpeechRecognitionEngine rec;
        readonly object recLock = new object();
        readonly Dictionary<Prompt, TaskCompletionSource<bool>> waits = new Dictionary<Prompt, TaskCompletionSource<bool>>();
        public string Problem;
        public bool Hearing;
        volatile bool noSsml;
        double quietUntil;
        int recGeneration;

        public Voice(Buddy b) { this.b = b; }

        public void Start()
        {
            // Loading the voice can take a while on slow PCs, so do it in the background.
            Task.Run(delegate
            {
                try
                {
                    SpeechSynthesizer s = new SpeechSynthesizer();
                    s.SetOutputToDefaultAudioDevice();
                    s.SpeakCompleted += OnSpeakCompleted;
                    synth = s;
                    ApplyVoice();
                    Log.Write("voice ready");
                }
                catch (Exception ex) { synth = null; Log.Write("tts: " + ex.Message); }
            });
            StartRecognition();
        }

        public List<string> VoiceNames()
        {
            List<string> list = new List<string>();
            try
            {
                if (synth != null)
                    foreach (InstalledVoice v in synth.GetInstalledVoices())
                        if (v.Enabled) list.Add(v.VoiceInfo.Name);
            }
            catch { }
            return list;
        }

        public void ApplyVoice()
        {
            if (synth == null) return;
            try { if (!string.IsNullOrEmpty(b.S.VoiceName)) synth.SelectVoice(b.S.VoiceName); }
            catch (Exception ex) { Log.Write("voice: " + ex.Message); }
        }

        public bool IsSpeaking
        {
            get
            {
                try { return synth != null && synth.State == SynthesizerState.Speaking; }
                catch { return false; }
            }
        }

        public void StopTalking()
        {
            try { if (synth != null) synth.SpeakAsyncCancelAll(); } catch { }
        }

        static string CleanForSpeech(string t)
        {
            t = t.Replace("*", " ").Replace("×", " times ").Replace("÷", " divided by ").Replace("=", " equals ")
                 .Replace("√", " square root of ").Replace("\"", " ");
            return Regex.Replace(t, "\\s+", " ").Trim();
        }

        public Task Speak(string text, string mood)
        {
            if (synth == null || b.S.Muted)
                return Task.Delay(TimeSpan.FromSeconds(Math.Min(6, 0.8 + text.Length * 0.05)));
            try
            {
                synth.SpeakAsyncCancelAll();
                int rate = b.S.VoiceRate;
                string pitch = b.S.Squeaky ? "high" : "medium";
                if (mood == "happy") { rate += 1; pitch = b.S.Squeaky ? "x-high" : "high"; }
                else if (mood == "angry") { rate += 1; pitch = b.S.Squeaky ? "medium" : "low"; }
                else if (mood == "bored") { rate -= 2; pitch = b.S.Squeaky ? "medium" : "low"; }
                synth.Rate = Math.Max(-10, Math.Min(10, rate));
                string clean = CleanForSpeech(text);
                PromptBuilder pb = new PromptBuilder();
                if (noSsml) pb.AppendText(clean);
                else
                {
                    try { pb.AppendSsmlMarkup("<prosody pitch=\"" + pitch + "\">" + SecurityElement.Escape(clean) + "</prosody>"); }
                    catch { pb = new PromptBuilder(); pb.AppendText(clean); }
                }
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                Prompt p = synth.SpeakAsync(pb);
                lock (waits) waits[p] = tcs;
                return tcs.Task;
            }
            catch (Exception ex)
            {
                Log.Write("speak: " + ex.Message);
                noSsml = true;
                return Task.FromResult(true);
            }
        }

        void OnSpeakCompleted(object sender, SpeakCompletedEventArgs e)
        {
            if (e.Error != null && !e.Cancelled && !(e.Error is OperationCanceledException))
            {
                Log.Write("speak error: " + e.Error.Message);
                noSsml = true;
            }
            TaskCompletionSource<bool> t = null;
            lock (waits)
            {
                if (waits.TryGetValue(e.Prompt, out t)) waits.Remove(e.Prompt);
            }
            quietUntil = b.Now + 0.7;
            if (t != null) t.TrySetResult(true);
        }

        // ---------- ears ----------
        public void StartRecognition()
        {
            StopRecognition();
            if (!b.S.Listening) { Hearing = false; Problem = "listening is turned off"; return; }
            string name = b.S.Name.ToLowerInvariant();
            int gen = ++recGeneration;
            Task.Run(delegate
            {
                try
                {
                    RecognizerInfo info = null;
                    foreach (RecognizerInfo ri in SpeechRecognitionEngine.InstalledRecognizers())
                    {
                        if (ri.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase)) { info = ri; break; }
                        if (info == null && ri.Culture.TwoLetterISOLanguageName == "en") info = ri;
                    }
                    if (info == null)
                        foreach (RecognizerInfo ri in SpeechRecognitionEngine.InstalledRecognizers()) { info = ri; break; }
                    if (info == null) throw new Exception("Windows has no speech recognizer installed");

                    SpeechRecognitionEngine engine = new SpeechRecognitionEngine(info);
                    try { engine.SetInputToDefaultAudioDevice(); }
                    catch { engine.Dispose(); throw new Exception("no microphone found"); }

                    Choices wake = new Choices(name, "hey " + name, "hi " + name, "ok " + name, "okay " + name, "yo " + name);
                    GrammarBuilder gb = new GrammarBuilder(); gb.Culture = info.Culture; gb.Append(wake); gb.AppendDictation();
                    Grammar g1 = new Grammar(gb); g1.Name = "wakecmd";
                    GrammarBuilder gb2 = new GrammarBuilder(); gb2.Culture = info.Culture; gb2.Append(wake);
                    Grammar g2 = new Grammar(gb2); g2.Name = "wake";
                    DictationGrammar g3 = new DictationGrammar(); g3.Name = "dict";
                    engine.LoadGrammar(g1);
                    engine.LoadGrammar(g2);
                    engine.LoadGrammar(g3);
                    engine.SpeechRecognized += OnRecognized;
                    engine.RecognizeCompleted += delegate (object s, RecognizeCompletedEventArgs e)
                    {
                        if (e.Error != null) Log.Write("recognizer stopped: " + e.Error.Message);
                    };
                    lock (recLock)
                    {
                        if (gen != recGeneration) { engine.Dispose(); return; }
                        rec = engine;
                    }
                    engine.RecognizeAsync(RecognizeMode.Multiple);
                    Hearing = true;
                    Problem = null;
                    Log.Write("ears on: " + info.Name);
                }
                catch (Exception ex)
                {
                    Hearing = false;
                    Problem = ex.Message;
                    Log.Write("ears: " + ex);
                    try { b.BeginInvoke((Action)delegate { b.OnVoiceProblem(ex.Message); }); } catch { }
                }
            });
        }

        public void StopRecognition()
        {
            SpeechRecognitionEngine old;
            lock (recLock) { old = rec; rec = null; recGeneration++; }
            Hearing = false;
            if (old != null)
                Task.Run(delegate
                {
                    try { old.RecognizeAsyncCancel(); old.Dispose(); } catch { }
                });
        }

        void OnRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            string text = e.Result.Text;
            float conf = e.Result.Confidence;
            string g = e.Result.Grammar != null ? e.Result.Grammar.Name : "";
            try { b.BeginInvoke((Action)delegate { Heard(text, conf, g); }); } catch { }
        }

        static int Lev(string a, string c)
        {
            int[,] d = new int[a.Length + 1, c.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= c.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= c.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == c[j - 1] ? 0 : 1));
            return d[a.Length, c.Length];
        }

        static bool Similar(string w, string name)
        {
            if (w == name || w == name + "s" || w == name + "'s") return true;
            if (name.Length >= 4 && Lev(w, name) <= 1) return true;
            if (name.Length >= 7 && Lev(w, name) <= 2) return true;
            return false;
        }

        // Finds the wake name in what was heard, and returns the rest of the sentence.
        static bool FindName(string text, string name, out string rest)
        {
            rest = null;
            MatchCollection words = Regex.Matches(text.ToLowerInvariant(), "[a-z']+");
            foreach (Match m in words)
            {
                if (!Similar(m.Value, name)) continue;
                string before = text.Substring(0, m.Index);
                before = Regex.Replace(before, @"\b(hey|hi|ok|okay|yo)[\s,]*$", "", RegexOptions.IgnoreCase);
                string after = text.Substring(m.Index + m.Length);
                rest = (before + " " + after).Trim(' ', ',', '.', '!', '?');
                return true;
            }
            return false;
        }

        void Heard(string text, float conf, string grammar)
        {
            if (IsSpeaking || b.Now < quietUntil) return;
            Log.Write(string.Format("ASR [{0} {1:0.00}] {2}", grammar, conf, text));
            string rest;
            bool named = FindName(text, b.S.Name.ToLowerInvariant(), out rest);
            if (named && grammar == "wake" && conf < 0.45f) named = false;
            if (named && grammar == "dict" && conf < 0.2f) named = false;
            if (named)
            {
                if (grammar == "wake" || rest.Length < 2) b.OnWake();
                else b.Brain.Hear(rest);
                return;
            }
            if (b.Now < b.ListeningUntil || !b.S.NeedName)
            {
                if (!b.S.NeedName && b.Now >= b.ListeningUntil && conf < 0.4f) return;
                if (text.Trim().Length < 2) return;
                b.Brain.Hear(text);
            }
        }

        public void ListenNow()
        {
            if (!Hearing)
            {
                b.ShowBubble("I can't hear right now" + (Problem != null ? " (" + Problem + ")" : "") + ". Double-click me to type!", 6);
                return;
            }
            b.OnWake();
        }

        public void Dispose()
        {
            StopRecognition();
            try { if (synth != null) { synth.SpeakAsyncCancelAll(); synth.Dispose(); } } catch { }
        }
    }
}
