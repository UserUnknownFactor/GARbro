using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes
{
    /// <summary>
    /// Represents a single line in a script file
    /// </summary>
    public class ScriptLine
    {
        public uint Id { get; set; }
        public string Text { get; set; }
        public string Speaker { get; set; }
        public Dictionary<string, object> Metadata { get; set; }

        public ScriptLine(uint id, string text, string speaker = null)
        {
            Id = id;
            Text = text ?? string.Empty;
            Speaker = speaker;
            Metadata = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Types of script content
    /// </summary>
    public enum ScriptType
    {
        Unknown,
        PlainText,
        Dialogue,
        TextData,
        BinaryScript,
        LuaScript,
        JsonScript,
        XmlScript
    }

    /// <summary>
    /// Container for script data
    /// </summary>
    public class ScriptData
    {
        public string RawText { get; private set; }
        public ScriptType Type { get; set; }
        public Encoding Encoding { get; set; }
        public string NewLineFormat { get; set; }
        public IList<ScriptLine> TextLines { get { return m_text; } }
        public Dictionary<string, object> Metadata { get; private set; }

        protected List<ScriptLine> m_text;

        public ScriptData()
        {
            RawText = string.Empty;
            Type = ScriptType.Unknown;
            Encoding = Encoding.UTF8;
            m_text = new List<ScriptLine>();
            Metadata = new Dictionary<string, object>();
        }

        public ScriptData(string text, ScriptType type = ScriptType.Unknown)
        {
            RawText = text ?? string.Empty;
            Type = type;
            Encoding = Encoding.UTF8;
            m_text = new List<ScriptLine>();
            Metadata = new Dictionary<string, object>();

            // Detect newline format
            DetectNewLineFormat(text);

            if (!string.IsNullOrEmpty(text) && type == ScriptType.PlainText)
            {
                ParsePlainText(text);
            }
        }

        public ScriptData(IEnumerable<ScriptLine> lines, ScriptType type = ScriptType.Dialogue)
        {
            m_text = new List<ScriptLine>(lines);
            Type = type;
            Encoding = Encoding.UTF8;
            NewLineFormat = Environment.NewLine; // Default to system newline
            Metadata = new Dictionary<string, object>();
            RawText = string.Join(NewLineFormat, m_text.Select(l => l.Text));
        }

        /// <summary>
        /// Detects the newline format used in the text
        /// </summary>
        protected virtual void DetectNewLineFormat(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                NewLineFormat = Environment.NewLine;
                return;
            }

            // Count occurrences of different newline formats
            int crlfCount = 0;
            int lfCount = 0;
            int crCount = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlfCount++;
                        i++; // Skip the \n
                    }
                    else
                    {
                        crCount++;
                    }
                }
                else if (text[i] == '\n')
                {
                    lfCount++;
                }
            }

            // Determine the predominant newline format
            if (crlfCount >= lfCount && crlfCount >= crCount)
                NewLineFormat = "\r\n";
            else if (lfCount >= crCount)
                NewLineFormat = "\n";
            else if (crCount > 0)
                NewLineFormat = "\r";
            else
                NewLineFormat = Environment.NewLine; // Default if no newlines found
        }

        protected virtual void ParsePlainText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            string[] lines;
            if (!string.IsNullOrEmpty(NewLineFormat) && text.Contains(NewLineFormat))
            {
                lines = text.Split(new[] { NewLineFormat }, StringSplitOptions.None);
            }
            else
            {
                lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            }

            uint id = 0;
            foreach (var line in lines)
            {
                m_text.Add(new ScriptLine(id++, line));
            }
        }

        public virtual void Serialize(Stream output)
        {
            using (var writer = new StreamWriter(output, Encoding, 1024, true))
            {
                if (m_text.Count > 0)
                {
                    for (int i = 0; i < m_text.Count; i++)
                    {
                        writer.Write(m_text[i].Text);

                        // Don't add newline after the last line unless original had it
                        if (i < m_text.Count - 1)
                        {
                            writer.Write(NewLineFormat);
                        }
                        else if (RawText.EndsWith(NewLineFormat) || RawText.EndsWith("\n") || RawText.EndsWith("\r"))
                        {
                            writer.Write(NewLineFormat);
                        }
                    }
                }
                else
                {
                    writer.Write(RawText);
                }
            }
        }

        public virtual void Deserialize(Stream input)
        {
            using (var reader = new StreamReader(input, Encoding, true, 1024, true))
            {
                RawText = reader.ReadToEnd();
                DetectNewLineFormat(RawText);
                ParsePlainText(RawText);
            }
        }

        /// <summary>
        /// Gets information about the newline format
        /// </summary>
        public string GetNewLineInfo()
        {
            switch (NewLineFormat)
            {
                case "\r\n":
                    return "CRLF (Windows)";
                case "\n":
                    return "LF (Unix/Linux)";
                case "\r":
                    return "CR (Classic Mac)";
                default:
                    return "Unknown";
            }
        }
    }

    /// <summary>
    /// Base class for script format handlers
    /// </summary>
    public abstract class ScriptFormat : IResource
    {
        public override string Type { get { return "script"; } }

        /// <summary>
        /// Determines if the file is a valid script of this format
        /// </summary>
        public abstract bool IsScript(IBinaryStream file);

        /// <summary>
        /// Converts script from game format to readable format
        /// </summary>
        public abstract Stream ConvertFrom(IBinaryStream file);

        /// <summary>
        /// Converts script from readable format back to game format
        /// </summary>
        public abstract Stream ConvertBack(IBinaryStream file);

        /// <summary>
        /// Reads and parses script data
        /// </summary>
        public abstract ScriptData Read(string name, Stream file);

        /// <summary>
        /// Writes script data to stream
        /// </summary>
        public abstract void Write(Stream file, ScriptData script);

        /// <summary>
        /// Detects encoding of text data
        /// </summary>
        protected virtual Encoding DetectEncoding(byte[] data, int length = -1)
        {
            if (length < 0)
                length = data.Length;

            if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8;
            if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode;
            if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode;
            if (length >= 4 && data[0] == 0xFF && data[1] == 0xFE && data[2] == 0 && data[3] == 0)
                return Encoding.UTF32;

            if (IsUtf8(data, length))
                return Encoding.UTF8;

            if (IsShiftJis(data, length))
                return Encoding.GetEncoding(932); // Shift-JIS

            // Default
            return Encoding.Default;
        }

        private bool IsUtf8(byte[] data, int length)
        {
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.GetCharCount(data, 0, length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsShiftJis(byte[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                if (b >= 0x81 && b <= 0x9F || b >= 0xE0 && b <= 0xFC)
                {
                    if (i + 1 < length)
                    {
                        byte b2 = data[i + 1];
                        if (b2 >= 0x40 && b2 <= 0xFC && b2 != 0x7F)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Finds appropriate script format handler for the given file
        /// </summary>
        public static ScriptFormat FindFormat(IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<ScriptFormat>(file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    if (impl.IsScript(file))
                        return impl;
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Base implementation for generic script formats
    /// </summary>
    public abstract class GenericScriptFormat : ScriptFormat
    {
        public override bool IsScript(IBinaryStream file)
        {
            // Check extension
            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            return Extensions != null && Extensions.Contains(ext);
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            return file.AsStream;
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            return file.AsStream;
        }

        public override ScriptData Read(string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding(data);
            var text = encoding.GetString(data);

            // Remove BOM if present
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);

            var scriptData = new ScriptData(text, GetScriptType(name)) { Encoding = encoding };
            return scriptData;
        }

        public override void Write(Stream file, ScriptData script)
        {
            script.Serialize(file);
        }

        protected virtual ScriptType GetScriptType(string filename)
        {
            var ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "txt":
                    return ScriptType.PlainText;
                case "json":
                    return ScriptType.JsonScript;
                case "xml":
                    return ScriptType.XmlScript;
                case "lua":
                    return ScriptType.LuaScript;
                default:
                    return ScriptType.TextData;
            }
        }
    }

    [Export(typeof(ScriptFormat))]
    public class TextScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "TXT"; } }
        public override string Description { get { return "Plain text file"; } }
        public override uint     Signature { get { return 0; } }

        public TextScriptFormat()
        {
            Extensions = new[] { "txt", "text", "log" };
        }
    }

    [Export(typeof(ScriptFormat))]
    public class JsonScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "JSON"; } }
        public override string Description { get { return "JSON script file"; } }
        public override uint     Signature { get { return 0; } }

        public JsonScriptFormat()
        {
            Extensions = new[] { "json" };
        }
    }

    [Export(typeof(ScriptFormat))]
    public class XmlScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "XML"; } }
        public override string Description { get { return "XML script file"; } }
        public override uint     Signature { get { return 0; } }

        public XmlScriptFormat()
        {
            Extensions = new[] { "xml" };
        }
    }

    [Export(typeof(ScriptFormat))]
    public class LuaScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "LUA"; } }
        public override string Description { get { return "Lua script file"; } }
        public override uint     Signature { get { return 0; } }

        public LuaScriptFormat()
        {
            Extensions = new[] { "lua" };
        }
    }

    [Export(typeof(ScriptFormat))]
    public class BinScriptFormat : ScriptFormat
    {
        public override string         Tag { get { return "SCR"; } }
        public override string Description { get { return "Binary script format"; } }
        public override uint     Signature { get { return 0; } }

        public BinScriptFormat()
        {
            Extensions = new[] { "scr", "bin", "dat" };
        }

        public override bool IsScript(IBinaryStream file)
        {
            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            return Extensions != null && Extensions.Contains(ext);
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            throw new NotSupportedException("Binary script conversion not implemented");
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            throw new NotSupportedException("Binary script conversion not implemented");
        }

        public override ScriptData Read(string name, Stream file)
        {
            throw new NotSupportedException("Binary script reading not implemented");
        }

        public override void Write(Stream file, ScriptData script)
        {
            throw new NotSupportedException("Binary script writing not implemented");
        }
    }
}