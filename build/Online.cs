using System;
using System.Net;

namespace LumoraShared
{
    // Reads files from the update location (Config.UpdateBaseUrl). Returns null when offline or not configured.
    static class Online
    {
        public static bool Enabled { get { return Config.UpdateBaseUrl.Length > 0; } }

        public static byte[] TryGet(string file, int timeoutMs)
        {
            if (!Enabled) return null;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                var req = (HttpWebRequest)WebRequest.Create(Config.UpdateBaseUrl + file + "?t=" + DateTime.UtcNow.Ticks);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Lumora/" + Config.Version;
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new System.IO.MemoryStream())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return null;
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        public static bool IsNewer(string remote, string local)
        {
            Version r, l;
            return Version.TryParse((remote ?? "").Trim(), out r) && Version.TryParse(local, out l) && r > l;
        }
    }
}
