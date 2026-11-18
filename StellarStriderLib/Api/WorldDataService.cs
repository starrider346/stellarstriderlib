using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StellarStriderLib
{
    public static class WorldDataService
    {
        private sealed class Provider
        {
            internal Func<string> Capture;
            internal Action<string> Restore;
        }
        private static readonly SortedDictionary<string, Provider> Providers = new(StringComparer.Ordinal);
        public static void Register(string modId, Func<string> capture, Action<string> restore)
        {
            StriderRegistry.MainThread();
            if (string.IsNullOrWhiteSpace(modId) || capture == null || restore == null || Providers.ContainsKey(modId))
                throw new ArgumentException("Invalid or duplicate world-data provider.");
            Providers.Add(modId, new Provider { Capture = capture, Restore = restore });
        }
        internal static bool Ready
        {
            get { foreach (var provider in Providers.Values) if (provider.Capture() == null) return false; return true; }
        }
        internal static JObject Capture()
        {
            var result = new JObject();
            foreach (var pair in Providers)
            {
                var value = pair.Value.Capture();
                if (value == null || System.Text.Encoding.UTF8.GetByteCount(value) > 16384)
                    throw new InvalidOperationException("World descriptor unavailable or exceeds 16 KiB: " + pair.Key);
                result[pair.Key] = value;
            }
            return result;
        }
        internal static void Restore(JObject document)
        {
            if (document == null && Providers.Count != 0) throw new InvalidOperationException("Missing mod world descriptors.");
            if (document != null) foreach (var property in document.Properties())
                if (!Providers.ContainsKey(property.Name)) throw new InvalidOperationException("Missing world-data mod: " + property.Name);
            foreach (var pair in Providers)
            {
                var token = document?[pair.Key];
                if (token?.Type != JTokenType.String || System.Text.Encoding.UTF8.GetByteCount((string)token) > 16384)
                    throw new InvalidOperationException("Missing/invalid world descriptor: " + pair.Key);
                pair.Value.Restore((string)token);
            }
        }
    }
}
