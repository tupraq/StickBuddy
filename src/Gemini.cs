using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace StickBuddy
{
    // Asks Google's Gemini over the internet (for a key made in Google AI Studio).
    // Uses the generateContent REST endpoint directly, with the key in the x-goog-api-key header.
    public static class Gemini
    {
        public const string DefaultModel = "gemini-3.5-flash-lite";
        public static readonly string[] Models = { "gemini-3.5-flash-lite", "gemini-3.6-flash", "gemini-3.8-flash", "gemini-3.1-flash-lite" };
        const string Base = "https://generativelanguage.googleapis.com/v1beta/models/";

        public static async Task<ClaudeReply> Ask(string key, string model, string system, List<Dictionary<string, object>> history, string user)
        {
            if (string.IsNullOrEmpty(key)) throw new Exception("there's no API key in my settings");
            if (string.IsNullOrEmpty(model) || !model.StartsWith("gemini")) model = DefaultModel;
            List<object> contents = new List<object>();
            foreach (Dictionary<string, object> h in history)
            {
                object role, content;
                h.TryGetValue("role", out role); h.TryGetValue("content", out content);
                contents.Add(Turn(role as string == "assistant" ? "model" : "user", content as string ?? ""));
            }
            contents.Add(Turn("user", user));

            string resp = null;
            bool retry = false;
            try { resp = await Post(key, model, Body(system, contents, Claude.Schema(), 2048)); }
            catch (ClaudeHttpException ex)
            {
                // if the JSON schema option isn't accepted, ask again with just "answer in JSON"
                if (ex.Status == 400 && !ex.Message.Contains("key")) retry = true; else throw;
            }
            if (retry) resp = await Post(key, model, Body(system + "\n\nAnswer with one JSON object with these fields: say (string), action (string), target (string), write_to_notepad (true/false), emotion (string), remember (string).", contents, null, 2048));
            return Parse(resp);
        }

        // One question with an optional picture (JPEG, base64) and your own answer shape. Returns the JSON text.
        public static async Task<string> AskRaw(string key, string model, string system, string userText, string jpegB64, Dictionary<string, object> schema, int maxTokens)
        {
            if (string.IsNullOrEmpty(key)) throw new Exception("there's no API key in my settings");
            if (string.IsNullOrEmpty(model) || !model.StartsWith("gemini")) model = DefaultModel;
            List<object> parts = new List<object>();
            if (jpegB64 != null)
            {
                Dictionary<string, object> inline = Claude.Obj("mimeType", "image/jpeg");
                inline["data"] = jpegB64;
                parts.Add(Claude.Obj("inlineData", inline));
            }
            parts.Add(Claude.Obj("text", userText));
            Dictionary<string, object> turn = Claude.Obj("role", "user");
            turn["parts"] = parts.ToArray();
            List<object> contents = new List<object>();
            contents.Add(turn);
            string resp;
            bool retry = false;
            resp = null;
            try { resp = await Post(key, model, Body(system, contents, schema, maxTokens)); }
            catch (ClaudeHttpException ex) { if (ex.Status == 400 && !ex.Message.Contains("key")) retry = true; else throw; }
            if (retry) resp = await Post(key, model, Body(system + "\n\nAnswer with one JSON object only.", contents, null, maxTokens));
            string finish;
            bool blocked;
            string text = ExtractText(resp, out finish, out blocked);
            if (blocked) throw new Exception("Google didn't want to answer that one");
            return text;
        }

        static Dictionary<string, object> Turn(string role, string text)
        {
            Dictionary<string, object> part = Claude.Obj("text", text);
            Dictionary<string, object> t = Claude.Obj("role", role);
            t["parts"] = new object[] { part };
            return t;
        }

        static string Body(string system, List<object> contents, Dictionary<string, object> schema, int maxTokens)
        {
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["responseMimeType"] = "application/json";
            if (schema != null) cfg["responseJsonSchema"] = schema;
            cfg["maxOutputTokens"] = maxTokens;
            Dictionary<string, object> body = new Dictionary<string, object>();
            Dictionary<string, object> sys = new Dictionary<string, object>();
            sys["parts"] = new object[] { Claude.Obj("text", system) };
            body["systemInstruction"] = sys;
            body["contents"] = contents;
            body["generationConfig"] = cfg;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.Serialize(body);
        }

        static async Task<string> Post(string key, string model, string json)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + Uri.EscapeDataString(model) + ":generateContent");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Headers.Add("x-goog-api-key", key.Trim());
            byte[] data = Encoding.UTF8.GetBytes(json);
            using (Stream s = await req.GetRequestStreamAsync())
                await s.WriteAsync(data, 0, data.Length);

            Task<WebResponse> rt = req.GetResponseAsync();
            Task first = await Task.WhenAny(rt, Task.Delay(60000));
            if (first != rt) { req.Abort(); throw new Exception("thinking took too long, please try again"); }

            HttpWebResponse resp = null;
            WebException wex = null;
            try { resp = (HttpWebResponse)await rt; }
            catch (WebException we) { wex = we; }
            if (wex != null)
            {
                HttpWebResponse hr = wex.Response as HttpWebResponse;
                if (hr == null) throw new Exception("I can't reach the internet (" + wex.Status + ")");
                string err;
                using (StreamReader rd = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) err = rd.ReadToEnd();
                int code = (int)hr.StatusCode;
                Log.Write("gemini http " + code + ": " + (err.Length > 600 ? err.Substring(0, 600) : err));
                throw new ClaudeHttpException(code, Friendly(code, err));
            }
            using (resp)
            using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return await rd.ReadToEndAsync();
        }

        static string Friendly(int code, string body)
        {
            string msg = "", status = "";
            try
            {
                Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                Dictionary<string, object> e = root != null && root.ContainsKey("error") ? root["error"] as Dictionary<string, object> : null;
                if (e != null && e.ContainsKey("message")) msg = e["message"] as string ?? "";
                if (e != null && e.ContainsKey("status")) status = e["status"] as string ?? "";
            }
            catch { }
            string low = (msg + " " + body).ToLowerInvariant();
            if (low.Contains("api_key_invalid") || low.Contains("api key not valid") || low.Contains("api key expired"))
                return "Google says that key doesn't work. Check you copied all of it, or make a new key in Google AI Studio";
            if (code == 401 || code == 403)
                return "Google didn't accept the key (" + code + "). A brand-new key can take a minute to start working, so try again soon";
            if (code == 429) return "I asked Google too many questions too fast. Give me a minute";
            if (code == 404) return "Google doesn't have the model I asked for. Pick another one in my settings";
            if (code >= 500) return "Google's servers are busy, try again soon";
            return "error " + code + (msg.Length > 0 ? ": " + msg : "");
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
        }

        static ClaudeReply Parse(string resp)
        {
            ClaudeReply r = new ClaudeReply();
            string finish;
            bool blocked;
            string text = ExtractText(resp, out finish, out blocked);
            if (blocked) { r.Say = "Sorry, I can't talk about that one."; return r; }
            Claude.FillReply(r, text, finish == "MAX_TOKENS");
            return r;
        }

        // The answer text from a generateContent response (skipping "thinking" parts).
        static string ExtractText(string resp, out string finish, out bool blocked)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = js.DeserializeObject(resp) as Dictionary<string, object>;
            blocked = false;
            finish = "";
            Dictionary<string, object> fb = root != null && root.ContainsKey("promptFeedback") ? root["promptFeedback"] as Dictionary<string, object> : null;
            if (fb != null && Str(fb, "blockReason").Length > 0) { blocked = true; return ""; }
            StringBuilder text = new StringBuilder();
            object cands;
            if (root != null && root.TryGetValue("candidates", out cands) && cands is IEnumerable)
            {
                foreach (object c in (IEnumerable)cands)
                {
                    Dictionary<string, object> cd = c as Dictionary<string, object>;
                    if (cd == null) continue;
                    finish = Str(cd, "finishReason");
                    Dictionary<string, object> content = cd.ContainsKey("content") ? cd["content"] as Dictionary<string, object> : null;
                    object parts;
                    if (content != null && content.TryGetValue("parts", out parts) && parts is IEnumerable)
                        foreach (object p in (IEnumerable)parts)
                        {
                            Dictionary<string, object> pd = p as Dictionary<string, object>;
                            if (pd == null) continue;
                            object th;
                            if (pd.TryGetValue("thought", out th) && th is bool && (bool)th) continue;
                            text.Append(Str(pd, "text"));
                        }
                    break;
                }
            }
            if (text.Length == 0 && (finish == "SAFETY" || finish == "PROHIBITED_CONTENT" || finish == "BLOCKLIST")) blocked = true;
            return text.ToString();
        }
    }
}
