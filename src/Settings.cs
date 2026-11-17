using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StickBuddy
{
    // Everything you can customise. Saved in %APPDATA%\StickBuddy\settings.ini
    public class Settings
    {
        public static readonly Color DefaultRed = Color.FromArgb(226, 32, 36);
        public string Name = "Sticky";
        public Color BodyColor = DefaultRed;
        public bool ShowHouse = true;
        public bool ImpactFlash = true;
        public bool WatchScreen = true;
        public double HouseX = -1;
        public int Version = 2;
        public bool Rainbow = false;
        public double Size = 1.0;
        public string VoiceName = "";
        public int VoiceRate = 1;
        public bool Squeaky = true;
        public bool Muted = false;
        public bool Listening = true;
        public bool NeedName = true;
        public bool NotepadAnswers = true;
        public bool MouseTricks = true;
        public bool Feelings = true;
        public bool Wander = true;
        public string ApiKey = "";
        public string Model = "claude-opus-5-5";
        public string GeminiModel = Gemini.DefaultModel;
        public bool Chatty = true;

        // Claude keys start with "sk-ant-"; anything else (like a key from Google AI Studio) is treated as Gemini.
        public static bool IsClaudeKey(string k) { return k != null && k.Trim().StartsWith("sk-ant", StringComparison.Ordinal); }
        public bool UsesGemini { get { return EffectiveApiKey.Length > 0 && !IsClaudeKey(EffectiveApiKey); } }
        public string BrainName { get { return UsesGemini ? "Gemini" : "Claude"; } }

        static string FilePath { get { return Path.Combine(Log.Folder, "settings.ini"); } }

        public string EffectiveApiKey
        {
            get
            {
                if (!string.IsNullOrEmpty(ApiKey)) return ApiKey.Trim();
                string env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                if (string.IsNullOrEmpty(env)) env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                return env == null ? "" : env.Trim();
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                s.Version = 1;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "Name": if (v.Length > 0) s.Name = v; break;
                        case "Color": s.BodyColor = ColorTranslator.FromHtml(v); break;
                        case "Rainbow": s.Rainbow = v == "1"; break;
                        case "Size": s.Size = Math.Max(0.5, Math.Min(2.5, double.Parse(v, CultureInfo.InvariantCulture))); break;
                        case "Voice": s.VoiceName = v; break;
                        case "VoiceRate": s.VoiceRate = int.Parse(v); break;
                        case "Squeaky": s.Squeaky = v == "1"; break;
                        case "Muted": s.Muted = v == "1"; break;
                        case "Listening": s.Listening = v == "1"; break;
                        case "NeedName": s.NeedName = v == "1"; break;
                        case "NotepadAnswers": s.NotepadAnswers = v == "1"; break;
                        case "MouseTricks": s.MouseTricks = v == "1"; break;
                        case "Feelings": s.Feelings = v == "1"; break;
                        case "Wander": s.Wander = v == "1"; break;
                        case "Model": if (v.Length > 0) s.Model = v; break;
                        case "ApiKey": s.ApiKey = Unprotect(v); break;
                        case "ShowHouse": s.ShowHouse = v == "1"; break;
                        case "ImpactFlash": s.ImpactFlash = v == "1"; break;
                        case "WatchScreen": s.WatchScreen = v == "1"; break;
                        case "HouseX": s.HouseX = double.Parse(v, CultureInfo.InvariantCulture); break;
                        case "Version": s.Version = int.Parse(v); break;
                        case "GeminiModel": if (v.Length > 0) s.GeminiModel = v; break;
                        case "Chatty": s.Chatty = v == "1"; break;
                    }
                }
                if (s.Version < 2)
                {
                    // version 2: he is red now (unless you picked your own colour)
                    if (s.BodyColor.R == 124 && s.BodyColor.G == 77 && s.BodyColor.B == 255) s.BodyColor = DefaultRed;
                    s.Version = 2;
                    s.Save();
                }
            }
            catch (Exception ex) { Log.Write("settings load: " + ex.Message); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Name=" + Name);
                sb.AppendLine("Color=" + ColorTranslator.ToHtml(Color.FromArgb(BodyColor.R, BodyColor.G, BodyColor.B)));
                sb.AppendLine("Rainbow=" + (Rainbow ? "1" : "0"));
                sb.AppendLine("Size=" + Size.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Voice=" + VoiceName);
                sb.AppendLine("VoiceRate=" + VoiceRate);
                sb.AppendLine("Squeaky=" + (Squeaky ? "1" : "0"));
                sb.AppendLine("Muted=" + (Muted ? "1" : "0"));
                sb.AppendLine("Listening=" + (Listening ? "1" : "0"));
                sb.AppendLine("NeedName=" + (NeedName ? "1" : "0"));
                sb.AppendLine("NotepadAnswers=" + (NotepadAnswers ? "1" : "0"));
                sb.AppendLine("MouseTricks=" + (MouseTricks ? "1" : "0"));
                sb.AppendLine("Feelings=" + (Feelings ? "1" : "0"));
                sb.AppendLine("Wander=" + (Wander ? "1" : "0"));
                sb.AppendLine("Model=" + Model);
                sb.AppendLine("ApiKey=" + Protect(ApiKey));
                sb.AppendLine("ShowHouse=" + (ShowHouse ? "1" : "0"));
                sb.AppendLine("ImpactFlash=" + (ImpactFlash ? "1" : "0"));
                sb.AppendLine("WatchScreen=" + (WatchScreen ? "1" : "0"));
                sb.AppendLine("HouseX=" + HouseX.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Version=" + Version);
                sb.AppendLine("GeminiModel=" + GeminiModel);
                sb.AppendLine("Chatty=" + (Chatty ? "1" : "0"));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception ex) { Log.Write("settings save: " + ex.Message); }
        }

        // The API key is locked with your Windows account so other people can't read it from the file.
        static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }
}
