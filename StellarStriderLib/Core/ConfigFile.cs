using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StellarStriderLib.Core
{
    internal sealed class ConfigFile
    {
        private readonly string _path;
        private readonly string _legacyPath;
        private readonly Action<string> _warn;
        private bool _preserveOriginal;

        internal JObject Document { get; private set; }
        internal bool CanSaveDefaults => !_preserveOriginal;
        internal bool NeedsMigration { get; private set; }

        internal ConfigFile(string path, Action<string> warn, string legacyPath)
        {
            _path = path;
            _legacyPath = legacyPath;
            _warn = warn;
            Document = Read();
        }

        internal static void CheckModId(string modId)
        {
            Keys.Identifier(modId);
            if (modId.EndsWith(".", StringComparison.Ordinal) ||
                modId.IndexOfAny(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }) >= 0)
            {
                throw new ArgumentException("Mod ID must be a valid directory name.", nameof(modId));
            }

            foreach (var character in modId)
            {
                if (char.IsControl(character))
                    throw new ArgumentException("Mod ID must be a valid directory name.", nameof(modId));
            }

            var name = modId.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) ||
                                      name.StartsWith("LPT", StringComparison.Ordinal)) &&
                 "123456789¹²³".Contains(name[3])))
            {
                throw new ArgumentException("Mod ID is a reserved directory name.", nameof(modId));
            }
        }

        private JObject Read()
        {
            var document = ReadDocument(_path, out _preserveOriginal);
            if (document != null) return document;

            if (_legacyPath != null)
            {
                document = ReadDocument(_legacyPath, out var failed);
                NeedsMigration = document != null && !failed;
            }

            return document ?? new JObject();

            JObject ReadDocument(string path, out bool failed)
            {
                failed = false;
                try
                {
                    using var stream = File.OpenText(path);
                    using var reader = new JsonTextReader(stream) { DateParseHandling = DateParseHandling.None };
                    var result = JObject.Load(reader,
                        new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment) throw new JsonReaderException("Expected one JSON object.");
                    }
                    return result;
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                {
                    return null;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    failed = true;
                    _warn($"Could not read {path}. Using defaults. {error.Message}");
                    return new JObject();
                }
            }
        }

        internal void Write(JObject document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var writeId = Guid.NewGuid().ToString("N");
            var temporary = _path + "." + writeId + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                    {
                        writer.WriteLine(document.ToString(Formatting.Indented));
                    }
                    stream.Flush(true);
                }

                if (File.Exists(_path))
                {
                    var backup = _preserveOriginal
                        ? Path.Combine(Path.GetDirectoryName(_path), "config.invalid-" + writeId + ".json")
                        : null;
                    File.Replace(temporary, _path, backup);
                }
                else
                {
                    File.Move(temporary, _path);
                }

                Document = document;
                _preserveOriginal = false;
                NeedsMigration = false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _warn($"Could not remove temporary config file {temporary}. {error.Message}");
                }
            }
        }
    }
}
