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
    // What his smart brain (Claude or Gemini) answered.
    public class ClaudeReply
    {
        public string Say = "", Action = "none", Target = "", Emotion = "neutral", Remember = "", RawJson = "";
        public bool Write;
    }

    public class ClaudeHttpException : Exception
    {
        public int Status;
        public ClaudeHttpException(int status, string msg) : base(msg) { Status = status; }
    }

    // Asks Claude (Anthropic's AI) a question over the internet. Needs an API key in Settings.
    // Uses the Messages API directly over HTTPS, because this app is built with the C# compiler
    // that comes with Windows (no NuGet packages).
    public static class Claude
    {
        const string Url = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2026-10-21";
        const string FallbackBeta = "server-side-fallback-2026-07-01";

        static readonly string[] Actions = { "none", "open_app", "web_search", "youtube_search", "play_game", "write_note", "grab_mouse", "dance", "wave", "jump", "flip", "sleep", "go_home", "open_lab", "control_game" };
        static readonly string[] Emotions = { "neutral", "happy", "excited", "sad", "angry", "bored", "thinking", "scared", "love", "proud", "embarrassed", "curious" };

        // The shape of every answer (used by Claude and Gemini).
        public static Dictionary<string, object> Schema()
        {
            Dictionary<string, object> props = new Dictionary<string, object>();
            props["say"] = Obj("type", "string");
            Dictionary<string, object> act = Obj("type", "string"); act["enum"] = Actions; props["action"] = act;
            props["target"] = Obj("type", "string");
            props["write_to_notepad"] = Obj("type", "boolean");
            Dictionary<string, object> emo = Obj("type", "string"); emo["enum"] = Emotions; props["emotion"] = emo;
            props["remember"] = Obj("type", "string");
            Dictionary<string, object> schema = Obj("type", "object");
            schema["properties"] = props;
            schema["required"] = new[] { "say", "action", "target", "write_to_notepad", "emotion", "remember" };
            schema["additionalProperties"] = false;
            return schema;
        }

        static bool SupportsFallbacks(string model)
        {
            return model.StartsWith("claude-opus-5") || model.StartsWith("claude-fable") || model.StartsWith("claude-sonnet-5-5");
        }

        public static async Task<ClaudeReply> Ask(string key, string model, string system, List<Dictionary<string, object>> history, string user)
        {
            if (string.IsNullOrEmpty(key)) throw new Exception("there's no API key in my settings");
            List<object> msgs = new List<object>();
            foreach (Dictionary<string, object> h in history) msgs.Add(h);
            Dictionary<string, object> um = new Dictionary<string, object>();
            um["role"] = "user"; um["content"] = user;
            msgs.Add(um);

            bool fb = SupportsFallbacks(model);
            string resp = null;
            bool retry = false;
            try { resp = await Post(key, Body(model, system, msgs, fb), fb); }
            catch (ClaudeHttpException ex)
            {
                // if the fallback option isn't accepted for this model, try once more without it
                if (fb && ex.Status == 400) retry = true; else throw;
            }
            if (retry) resp = await Post(key, Body(model, system, msgs, false), false);
            return Parse(resp);
        }

        // One question with an optional picture (JPEG, base64) and your own answer shape. Returns the JSON text.
        public static async Task<string> AskRaw(string key, string model, string system, string userText, string jpegB64, Dictionary<string, object> schema)
        {
            if (string.IsNullOrEmpty(key)) throw new Exception("there's no API key in my settings");
            List<object> content = new List<object>();
            if (jpegB64 != null)
            {
                Dictionary<string, object> src = Obj("type", "base64");
                src["media_type"] = "image/jpeg";
                src["data"] = jpegB64;
                Dictionary<string, object> img = Obj("type", "image");
                img["source"] = src;
                content.Add(img);
            }
            Dictionary<string, object> txt = Obj("type", "text");
            txt["text"] = userText;
            content.Add(txt);
            Dictionary<string, object> um = Obj("role", "user");
            um["content"] = content;
            List<object> msgs = new List<object>();
            msgs.Add(um);
            bool fb = SupportsFallbacks(model);
            string resp = null;
            bool retry = false;
            try { resp = await Post(key, Body(model, system, msgs, fb, schema), fb); }
            catch (ClaudeHttpException ex) { if (fb && ex.Status == 400) retry = true; else throw; }
            if (retry) resp = await Post(key, Body(model, system, msgs, false, schema), false);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = js.DeserializeObject(resp) as Dictionary<string, object>;
            if (Str(root, "stop_reason") == "refusal") throw new Exception("Claude didn't want to do that one");
            StringBuilder text = new StringBuilder();
            object blocks;
            if (root != null && root.TryGetValue("content", out blocks) && blocks is IEnumerable)
                foreach (object o in (IEnumerable)blocks)
                {
                    Dictionary<string, object> blk = o as Dictionary<string, object>;
                    if (blk != null && Str(blk, "type") == "text") text.Append(Str(blk, "text"));
                }
            return text.ToString();
        }

        static string Body(string model, string system, List<object> msgs, bool fallbacks, Dictionary<string, object> schema = null)
        {
            Dictionary<string, object> format = Obj("type", "json_schema");
            format["schema"] = schema ?? Schema();
            Dictionary<string, object> outCfg = Obj("effort", "low");
            outCfg["format"] = format;

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = model;
            body["max_tokens"] = 16000;
            body["system"] = system;
            body["messages"] = msgs;
            body["output_config"] = outCfg;
            if (fallbacks) body["fallbacks"] = "default";
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.Serialize(body);
        }

        public static Dictionary<string, object> Obj(string k, object v)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d[k] = v;
            return d;
        }

        static async Task<string> Post(string key, string json, bool beta)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Headers.Add("x-api-key", key);
            req.Headers.Add("anthropic-version", ApiVersion);
            if (beta) req.Headers.Add("anthropic-beta", FallbackBeta);
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
                Log.Write("claude http " + code + ": " + err);
                throw new ClaudeHttpException(code, Friendly(code, err));
            }
            using (resp)
            using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return await rd.ReadToEndAsync();
        }

        static string Friendly(int code, string body)
        {
            string msg = "";
            try
            {
                Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                Dictionary<string, object> e = root != null && root.ContainsKey("error") ? root["error"] as Dictionary<string, object> : null;
                if (e != null && e.ContainsKey("message")) msg = e["message"] as string;
            }
            catch { }
            if (code == 401) return "the API key in my settings doesn't work";
            if (code == 429) return "too many questions too fast, wait a moment";
            if (code >= 500) return "Claude's servers are busy, try again soon";
            return "error " + code + (string.IsNullOrEmpty(msg) ? "" : ": " + msg);
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
        }

        static ClaudeReply Parse(string resp)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = js.DeserializeObject(resp) as Dictionary<string, object>;
            ClaudeReply r = new ClaudeReply();
            string stop = Str(root, "stop_reason");
            if (stop == "refusal") { r.Say = "Sorry, I can't help with that one."; return r; }
            StringBuilder text = new StringBuilder();
            object content;
            if (root != null && root.TryGetValue("content", out content) && content is IEnumerable)
            {
                foreach (object o in (IEnumerable)content)
                {
                    Dictionary<string, object> blk = o as Dictionary<string, object>;
                    if (blk != null && Str(blk, "type") == "text") text.Append(Str(blk, "text"));
                }
            }
            FillReply(r, text.ToString(), stop == "max_tokens");
            return r;
        }

        // Reads the JSON answer text into a reply (shared with Gemini).
        public static void FillReply(ClaudeReply r, string json, bool cutOff)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            json = (json ?? "").Trim();
            if (json.StartsWith("```"))
            {
                int a = json.IndexOf('{'), b = json.LastIndexOf('}');
                if (a >= 0 && b > a) json = json.Substring(a, b - a + 1);
            }
            r.RawJson = json;
            try
            {
                Dictionary<string, object> d = js.DeserializeObject(r.RawJson) as Dictionary<string, object>;
                if (d != null)
                {
                    r.Say = Str(d, "say");
                    r.Action = Str(d, "action");
                    r.Target = Str(d, "target");
                    r.Emotion = Str(d, "emotion");
                    r.Remember = Str(d, "remember");
                    object w;
                    r.Write = d.TryGetValue("write_to_notepad", out w) && w is bool && (bool)w;
                }
            }
            catch { r.Say = r.RawJson; r.RawJson = ""; }
            if (r.Say.Length == 0) r.Say = cutOff ? "Oops, my answer got too long!" : "Hmm, I'm not sure what to say.";
            if (r.Action.Length == 0) r.Action = "none";
        }
    }
}
