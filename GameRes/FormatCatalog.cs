//! \file       FormatCatalog.cs
//! \date       Wed Sep 16 22:51:11 2015
//! \brief      game resources formats catalog class.
//
// Copyright (C) 2014-2018 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using GameRes.Collections;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using GameRes.Compression;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Reflection;

namespace GameRes
{
    public sealed class FormatCatalog
    {
        private static readonly FormatCatalog m_instance = new FormatCatalog();

        #pragma warning disable 649
        private IEnumerable<ArchiveFormat>  m_arc_formats;
        private IEnumerable<ImageFormat>    m_image_formats;
        private IEnumerable<VideoFormat>    m_video_formats;
        private IEnumerable<AudioFormat>    m_audio_formats;
        [ImportMany(typeof(ScriptFormat))]
        private IEnumerable<ScriptFormat>   m_script_formats;
        [ImportMany(typeof(ISettingsManager))]
        private IEnumerable<ISettingsManager> m_settings_managers;
        #pragma warning restore 649

        private MultiValueDictionary<string, IResource> m_extension_map = new MultiValueDictionary<string, IResource>();
        private MultiValueDictionary<uint, IResource> m_signature_map = new MultiValueDictionary<uint, IResource>();

        private Dictionary<string, string> m_game_map = new Dictionary<string, string>();

        /// <summary> The only instance of this class.</summary>
        public static FormatCatalog       Instance      { get { return m_instance; } }

        public IEnumerable<ArchiveFormat> ArcFormats    { get { return m_arc_formats; } }
        public IEnumerable<ImageFormat>   ImageFormats  { get { return m_image_formats; } }
        public IEnumerable<VideoFormat>   VideoFormats  { get { return m_video_formats; } }
        public IEnumerable<AudioFormat>   AudioFormats  { get { return m_audio_formats; } }
        public IEnumerable<ScriptFormat>  ScriptFormats { get { return m_script_formats; } }

        public IEnumerable<IResource> Formats
        {
            get
            {
                return ((IEnumerable<IResource>)ArcFormats).Concat (ImageFormats).Concat (AudioFormats).Concat (ScriptFormats).Concat(VideoFormats);
            }
        }

        public int CurrentSchemeVersion { get; private set; }
        public string          SchemeID { get { return "GARbroDB"; } }
        public string  AssemblyLocation { get; private set; }
        public string     DataDirectory { get { return m_gamedata_dir.Value; } }

        public Exception LastError { get; set; }

        public event ParametersRequestEventHandler  ParametersRequest;

        private Lazy<string> m_gamedata_dir;

        private FormatCatalog ()
        {
            AssemblyLocation = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location);
            m_gamedata_dir = new Lazy<string> (() => Path.Combine (AssemblyLocation, "GameData"));

            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the Program class
            catalog.Catalogs.Add (new AssemblyCatalog (typeof(FormatCatalog).Assembly));
            //Adds parts matching pattern found in the directory of the assembly
            catalog.Catalogs.Add (new DirectoryCatalog (AssemblyLocation, "Arc*.dll"));

            //Create the CompositionContainer with the parts in the catalog
            using (var container = new CompositionContainer (catalog))
            {
                m_arc_formats = ImportWithPriorities<ArchiveFormat> (container);
                m_image_formats = ImportWithPriorities<ImageFormat> (container);
                m_video_formats = ImportWithPriorities<VideoFormat> (container);
                m_audio_formats = ImportWithPriorities<AudioFormat> (container);

                //Fill the imports of this object
                container.ComposeParts (this);

                AddResourceImpl (m_image_formats, container);
                AddResourceImpl (m_video_formats, container);
                AddResourceImpl (m_audio_formats, container);
                AddResourceImpl (m_script_formats, container);
                AddResourceImpl (m_arc_formats, container);

                AddAliases (container);
            }
        }

        private void AddResourceImpl (IEnumerable<IResource> formats, ICompositionService container)
        {
            foreach (var impl in formats)
            {
                try
                {
                    var part = AttributedModelServices.CreatePart (impl);
                    if (part.ImportDefinitions.Any())
                        container.SatisfyImportsOnce (part);
                }
                catch (Exception X)
                {
                    System.Diagnostics.Trace.WriteLine (X.Message, impl.Tag);
                }
                foreach (var ext in impl.Extensions)
                {
                    m_extension_map.Add (ext.ToUpperInvariant(), impl);
                }
                foreach (var signature in impl.Signatures)
                {
                    m_signature_map.Add (signature, impl);
                }
            }
        }

        private IEnumerable<Format> ImportWithPriorities<Format> (ExportProvider provider)
        {
            return provider.GetExports<Format, IResourceMetadata>()
                    .OrderByDescending (f => f.Metadata.Priority)
                    .Select (f => f.Value)
                    .ToArray();
        }

        private void AddAliases (ExportProvider provider)
        {
            foreach (var alias in provider.GetExports<ResourceAlias, IResourceAliasMetadata>())
            {
                var metadata = alias.Metadata;
                IEnumerable<IResource> target_list;
                if (string.IsNullOrEmpty (metadata.Type))
                    target_list = Formats;
                else if ("archive" == metadata.Type)
                    target_list = ArcFormats;
                else if ("image" == metadata.Type)
                    target_list = ImageFormats;
                else if ("video" == metadata.Type)
                    target_list = VideoFormats;
                else if ("audio" == metadata.Type)
                    target_list = AudioFormats;
                else if ("script" == metadata.Type)
                    target_list = ScriptFormats;
                else
                {
                    System.Diagnostics.Trace.WriteLine ("Unknown resource type specified", metadata.Extension);
                    continue;
                }
                var ext    = metadata.Extension;
                var target = metadata.Target;
                if (!string.IsNullOrEmpty (ext) && !string.IsNullOrEmpty (target))
                {
                    var target_res = target_list.FirstOrDefault (f => f.Tag == target);
                    if (target_res != null)
                        m_extension_map.Add (ext.ToUpperInvariant(), target_res);
                }
            }
        }

        public void UpgradeSettings ()
        {
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
            foreach (var mgr in m_settings_managers)
            {
                mgr.UpgradeSettings();
            }
        }

        public void SaveSettings ()
        {
            Properties.Settings.Default.Save();
            foreach (var mgr in m_settings_managers)
            {
                mgr.SaveSettings();
            }
        }

        /// <summary>
        /// Look up filename in format registry by filename extension and return corresponding interfaces.
        /// if no formats available, return empty range.
        /// </summary>
        public IEnumerable<IResource> LookupFileName (string filename)
        {
            string ext = Path.GetExtension (filename);
            if (string.IsNullOrEmpty (ext))
                return Enumerable.Empty<IResource>();
            return LookupExtension (ext.TrimStart ('.'));
        }

        public IEnumerable<IResource> LookupExtension (string ext)
        {
            return m_extension_map.GetValues (ext.ToUpperInvariant(), true);
        }

        public IEnumerable<Type> LookupExtension<Type> (string ext) where Type : IResource
        {
            return LookupExtension (ext).OfType<Type>();
        }

        public IEnumerable<IResource> LookupSignature (uint signature)
        {
            return m_signature_map.GetValues (signature, true);
        }

        public IEnumerable<Type> LookupSignature<Type> (uint signature) where Type : IResource
        {
            return LookupSignature (signature).OfType<Type>();
        }

        /// <summary>
        /// Enumerate resources matching specified <paramref name="signature"/> and filename extension.
        /// </summary>
        internal IEnumerable<ResourceType> FindFormats<ResourceType> (string filename, uint signature) where ResourceType : IResource
        {
            var ext = new Lazy<string> (() => Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant(), false);
            var tried = Enumerable.Empty<ResourceType>();
            IEnumerable<string> preferred = null;
            if (VFS.IsVirtual)
            {
                var arc_fs = VFS.Top as ArchiveFileSystem;
                if (arc_fs != null)
                    preferred = arc_fs.Source.ContainedFormats;
            }
            for (;;)
            {
                var range = LookupSignature<ResourceType> (signature);
                if (tried.Any())
                    range = range.Except (tried);
                // check formats that match filename extension first
                if (range.Skip (1).Any()) // if range.Count() > 1
                    range = range.OrderByDescending (f => f.Extensions.Any (e => e == ext.Value));
                if (preferred != null && preferred.Any())
                    range = range.OrderByDescending (f => preferred.Contains (f.Tag));
                foreach (var impl in range)
                {
                    yield return impl;
                }
                if (0 == signature)
                    break;
                signature = 0;
                tried = range;
            }
        }

        /// <summary>
        /// Create GameRes.Entry corresponding to <paramref name="filename"/> extension.
        /// </summary>
        /// <exception cref="System.ArgumentException">May be thrown if filename contains invalid
        /// characters.</exception>
        public EntryType Create<EntryType> (string filename) where EntryType : Entry, new()
        {
            return new EntryType {
                Name = filename,
                Type = GetTypeFromName (filename),
            };
        }

        public string GetTypeFromName (string filename, IEnumerable<string> preferred_formats = null)
        {
            var formats = LookupFileName (filename);
            if (!formats.Any())
                return "";
            if (preferred_formats != null && preferred_formats.Any())
                formats = formats.OrderByDescending (f => preferred_formats.Contains (f.Tag));
            return formats.First().Type;
        }

        public void InvokeParametersRequest (object source, ParametersRequestEventArgs args)
        {
            if (null != ParametersRequest)
                ParametersRequest (source, args);
        }

        /// <summary>
        /// Read first 4 bytes from stream and return them as 32-bit signature.
        /// </summary>
        public static uint ReadSignature (Stream file)
        {
            file.Position = 0;
            uint signature = (byte)file.ReadByte();
            signature |= (uint)file.ReadByte() << 8;
            signature |= (uint)file.ReadByte() << 16;
            signature |= (uint)file.ReadByte() << 24;
            return signature;
        }

        /// <summary>
        /// Look up game title based on archive <paramref name="arc_name"/> and files matching
        /// <paramref name="pattern"/> in the same directory as archive.
        /// </summary>
        /// <returns>Game title, or null if no match was found.</returns>
        public string LookupGame (string arc_name, string pattern = "*.exe")
        {
            string title;
            if (m_game_map.TryGetValue (Path.GetFileName (arc_name), out title))
                return title;
            pattern = VFS.CombinePath (VFS.GetDirectoryName (arc_name), pattern);
            foreach (var file in VFS.GetFiles (pattern).Select (e => Path.GetFileName (e.Name)))
            {
                if (m_game_map.TryGetValue (file, out title))
                    return title;
            }
            return null;
        }

        public void DeserializeScheme (Stream input)
        {
            int version = GetSerializedSchemeVersion (input);
            if (version <= CurrentSchemeVersion)
                return;
            using (var zs = new ZLibStream (input, CompressionMode.Decompress, true))
            {
                var bin = new BinaryFormatter();
                var db = (SchemeDataBase)bin.Deserialize (zs);

                foreach (var format in Formats)
                {
                    ResourceScheme scheme;
                    if (db.SchemeMap.TryGetValue (format.Tag, out scheme))
                        format.Scheme = scheme;
                }
                CurrentSchemeVersion = db.Version;
                if (db.GameMap != null)
                    m_game_map = db.GameMap;
            }
        }

        public void SerializeScheme (Stream output)
        {
            var db = new SchemeDataBase {
                Version = CurrentSchemeVersion,
                SchemeMap = new Dictionary<string, ResourceScheme>(),
                GameMap = m_game_map,
            };
            foreach (var format in Formats)
            {
                var scheme = format.Scheme;
                if (null != scheme)
                    db.SchemeMap[format.Tag] = scheme;
            }
            SerializeScheme (output, db);
        }

        public void SerializeScheme (Stream output, SchemeDataBase db)
        {
            using (var writer = new BinaryWriter (output, System.Text.Encoding.UTF8, true))
            {
                writer.Write (SchemeID.ToCharArray());
                writer.Write (db.Version);
            }
            var bin = new BinaryFormatter();
            using (var zs = new ZLibStream (output, CompressionMode.Compress, true))
                bin.Serialize (zs, db);
        }

        /// <summary>
        /// Serialize scheme database to JSON format.
        /// </summary>
        public void SerializeSchemeJson (Stream output, SchemeDataBase db)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Auto,
                Converters = new List<JsonConverter> { new ResourceSchemeJsonConverter() }
            };

            var json = JsonConvert.SerializeObject(db, settings);
            var bytes = Encoding.UTF8.GetBytes(json);
            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Deserialize scheme database from JSON format.
        /// </summary>
        public void DeserializeSchemeJson (Stream input)
        {
            using (var reader = new StreamReader(input, Encoding.UTF8, true, 1024, true))
            {
                var json = reader.ReadToEnd();

                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters = new List<JsonConverter> { new ResourceSchemeJsonConverter() }
                };

                var db = JsonConvert.DeserializeObject<SchemeDataBase>(json, settings);

                if (db.Version <= CurrentSchemeVersion)
                    return;

                foreach (var format in Formats)
                {
                    ResourceScheme scheme;
                    if (db.SchemeMap.TryGetValue (format.Tag, out scheme))
                        format.Scheme = scheme;
                }
                CurrentSchemeVersion = db.Version;
                if (db.GameMap != null)
                    m_game_map = db.GameMap;
            }
        }

        /// <summary>
        /// Helper method to convert ResourceScheme to JSON-serializable format.
        /// </summary>
        private JsonResourceScheme ConvertToJsonScheme(ResourceScheme scheme)
        {
            // Serialize the scheme object to binary, then convert to base64
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, scheme);
                return new JsonResourceScheme
                {
                    TypeName = scheme.GetType().AssemblyQualifiedName,
                    Data = Convert.ToBase64String(ms.ToArray())
                };
            }
        }

        /// <summary>
        /// Helper method to convert JSON-serializable format back to ResourceScheme.
        /// </summary>
        private ResourceScheme ConvertFromJsonScheme(JsonResourceScheme jsonScheme)
        {
            var bytes = Convert.FromBase64String(jsonScheme.Data);
            using (var ms = new MemoryStream(bytes))
            {
                var formatter = new BinaryFormatter();
                return (ResourceScheme)formatter.Deserialize(ms);
            }
        }

        public int GetSerializedSchemeVersion (Stream input)
        {
            using (var reader = new BinaryReader (input, System.Text.Encoding.UTF8, true))
            {
                var header = reader.ReadChars (SchemeID.Length);
                if (!header.SequenceEqual (SchemeID))
                    throw new FormatException ("Invalid serialization file");
                return reader.ReadInt32();
            }
        }

        /// <summary>
        /// Read text file <paramref name="filename"/> from data directory, performing <paramref name="process_line"/> action on each non-empty line.
        /// </summary>
        public void ReadFileList (string filename, Action<string> process_line)
        {
            var lst_file = Path.Combine (DataDirectory, filename);
            if (!File.Exists (lst_file))
                return;
            using (var input = new StreamReader (lst_file))
            {
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    if (line.Length > 0)
                    {
                        process_line (line);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Lazily initialized wrapper for resource instances.
    /// </summary>
    public class ResourceInstance<T> where T : IResource
    {
        T           m_format;
        Func<T>     m_resolver;

        public ResourceInstance (string tag)
        {
            var t = typeof(T);
            if (typeof(ImageFormat) == t || t.IsSubclassOf (typeof(ImageFormat)))
                m_resolver = () => ImageFormat.FindByTag (tag) as T;
            else if (typeof(ArchiveFormat) == t || t.IsSubclassOf (typeof(ArchiveFormat)))
                m_resolver = () => FormatCatalog.Instance.ArcFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else if (typeof(AudioFormat) == t || t.IsSubclassOf (typeof(AudioFormat)))
                m_resolver = () => FormatCatalog.Instance.AudioFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else if (typeof(VideoFormat) == t || t.IsSubclassOf (typeof(VideoFormat)))
                m_resolver = () => FormatCatalog.Instance.VideoFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else if (typeof(ScriptFormat) == t || t. IsSubclassOf (typeof(ScriptFormat)))
                m_resolver = () => FormatCatalog.Instance.ScriptFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else
                throw new ApplicationException ("Invalid resource type specified for ResourceInstance<T>");
        }

        public T Value { get { return LazyInitializer.EnsureInitialized (ref m_format, m_resolver); } }
    }

    [Serializable]
    public class SchemeDataBase
    {
        public int Version;

        public Dictionary<string, ResourceScheme>   SchemeMap;
        public Dictionary<string, string>           GameMap;
    }

    /// <summary>
    /// JSON-serializable version of SchemeDataBase
    /// </summary>
    public class JsonSchemeDataBase
    {
        public int Version { get; set; }
        public Dictionary<string, JsonResourceScheme> SchemeMap { get; set; }
        public Dictionary<string, string> GameMap { get; set; }
    }

    /// <summary>
    /// JSON-serializable wrapper for ResourceScheme objects
    /// </summary>
    public class JsonResourceScheme
    {
        public string TypeName { get; set; }
        public string Data { get; set; }  // Base64-encoded binary data
    }
}

/// <summary>
/// Custom JSON converter for ResourceScheme objects that intelligently handles serialization
/// </summary>
public class ResourceSchemeJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(GameRes.ResourceScheme).IsAssignableFrom(objectType);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var schemeType = value.GetType();
        var obj = new JObject();
        obj["$type"] = schemeType.AssemblyQualifiedName;

        // Try to serialize properties normally first
        var properties = schemeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = schemeType.GetFields(BindingFlags.Public | BindingFlags.Instance);

        var data = new JObject();
        bool hasNonSerializableData = false;

        // Serialize properties
        foreach (var prop in properties.Where(p => p.CanRead && p.CanWrite))
        {
            try
            {
                var propValue = prop.GetValue(value);
                if (IsJsonSerializable(propValue))
                {
                    data[prop.Name] = JToken.FromObject(propValue, serializer);
                }
                else
                {
                    hasNonSerializableData = true;
                }
            }
            catch
            {
                hasNonSerializableData = true;
            }
        }

        // Serialize fields
        foreach (var field in fields)
        {
            try
            {
                var fieldValue = field.GetValue(value);
                if (IsJsonSerializable(fieldValue))
                {
                    data[field.Name] = JToken.FromObject(fieldValue, serializer);
                }
                else
                {
                    hasNonSerializableData = true;
                }
            }
            catch
            {
                hasNonSerializableData = true;
            }
        }

        obj["data"] = data;

        // If there's non-serializable data, also include a binary fallback
        if (hasNonSerializableData)
        {
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, value);
                obj["binaryData"] = Convert.ToBase64String(ms.ToArray());
            }
        }

        obj.WriteTo(writer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);

        var typeName = obj["$type"]?.Value<string>();
        if (string.IsNullOrEmpty(typeName))
            return null;

        var type = Type.GetType(typeName);
        if (type == null)
            return null;

        // First try to deserialize from binary data if present
        var binaryData = obj["binaryData"]?.Value<string>();
        if (!string.IsNullOrEmpty(binaryData))
        {
            var bytes = Convert.FromBase64String(binaryData);
            using (var ms = new MemoryStream(bytes))
            {
                var formatter = new BinaryFormatter();
                return formatter.Deserialize(ms);
            }
        }

        // Otherwise, try to reconstruct from JSON data
        var data = obj["data"] as JObject;
        if (data != null)
        {
            var instance = Activator.CreateInstance(type);

            // Set properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties.Where(p => p.CanWrite))
            {
                var propData = data[prop.Name];
                if (propData != null)
                {
                    try
                    {
                        var value = propData.ToObject(prop.PropertyType, serializer);
                        prop.SetValue(instance, value);
                    }
                    catch { }
                }
            }

            // Set fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var fieldData = data[field.Name];
                if (fieldData != null)
                {
                    try
                    {
                        var value = fieldData.ToObject(field.FieldType, serializer);
                        field.SetValue(instance, value);
                    }
                    catch { }
                }
            }

            return instance;
        }

        return null;
    }

    private bool IsJsonSerializable(object value)
    {
        return true;
        if (value == null)
            return true;

        var type = value.GetType();

        // Primitive types and strings are always serializable
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || 
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
            type == typeof(Guid))
            return true;

        // Arrays and lists of serializable types
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return IsJsonSerializableType(elementType);
        }

        // Common generic collections
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(List<>) || genericDef == typeof(Dictionary<,>) || 
                genericDef == typeof(HashSet<>) || genericDef == typeof(Queue<>) || 
                genericDef == typeof(Stack<>))
            {
                return type.GetGenericArguments().All(IsJsonSerializableType);
            }
        }

        // Check if type has DataContract or is a simple POCO
        return type.IsSerializable || type.GetCustomAttribute<DataContractAttribute>() != null;
    }

    private bool IsJsonSerializableType(Type type)
    {
        return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || 
               type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
               type == typeof(Guid) || type.IsEnum;
    }
}

/*
using (var fileStream = File.Create("scheme.json"))
{
    var db = new SchemeDataBase 
    {
        Version = CurrentSchemeVersion,
        SchemeMap = schemeMap,
        GameMap = gameMap
    };
    catalog.SerializeSchemeJson(fileStream, db);
}
using (var fileStream = File.OpenRead("scheme.json"))
{
    catalog.DeserializeSchemeJson(fileStream);
}
*/