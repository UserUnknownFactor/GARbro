//#define LOG_EVERYTHING 
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.SevenZip
{
    [Export(typeof(ArchiveFormat))]
    public class SevenZipOpener : ArchiveFormat
    {
        public override string         Tag { get { return "7Z/OTHERS"; } }
        public override string Description { get { return "Archive supported by 7-Zip"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public SevenZipOpener()
        {
            Extensions = new[] {
                "7z", "zip", "rar", "tar", "gz", "bz2", "xz", "lzma", "cab",
                "iso", "dmg", "vhd", "wim", "rpm", "deb", "arj", "lzh", "chm",
                "msi", "nsis", "udf", "squashfs", "tgz", "tbz2", "txz"
            };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!Lib7ZipLoader.Load())
                return null;

            var format = GuessFormat(file);
            if (format == SevenZipFormat.Undefined)
                return null;

            try
            {
                object archive;
                var classId     = Formats.FormatGuidMapping[format];
                var interfaceId = typeof(IInArchive).GUID;

                int hr = CreateObject(ref classId, ref interfaceId, out archive);
                if (hr != 0)
                    return null;

                var inArchive = archive as IInArchive;
                if (inArchive == null)
                {
                    Marshal.ReleaseComObject(archive);
                    return null;
                }

                var stream   = new ArcViewStream(file);
                var inStream = new InStreamWrapper(stream);

                ulong maxCheckStartPosition = 32 * 1024;
                hr = inArchive.Open(inStream, ref maxCheckStartPosition, null);

                if (hr != 0)
                {
                    Marshal.ReleaseComObject(inArchive);
                    stream.Dispose();
                    return null;
                }

                var dir = ReadDirectory(inArchive, file.Name);
                if (dir.Count == 0)
                {
                    Marshal.ReleaseComObject(inArchive);
                    stream.Dispose();
                    return null;
                }

                Comment = format.ToString();

                return new SevenZipArchive(file, this, dir, inArchive, inStream, format);
            }
            catch
            {
                return null;
            }
        }

        public override void Create(Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            if (!Lib7ZipLoader.Load())
                return;

            var format = GetFormatFromOptions(options);
            if (!Formats.FormatGuidMapping.ContainsKey(format))
                throw new NotSupportedException($"Format {format} is not supported for writing");

            object archive;
            var classId = Formats.FormatGuidMapping[format];
            var interfaceId = typeof(IOutArchive).GUID;

            int hr = CreateObject(ref classId, ref interfaceId, out archive);
            if (hr != 0)
                throw new COMException($"Failed to create archive interface {interfaceId}", hr);

            var outArchive = archive as IOutArchive;
            if (outArchive == null)
            {
                Marshal.ReleaseComObject(archive);
                throw new InvalidCastException("Failed to cast to IOutArchive");
            }

            var entries = list.ToArray();

            try
            {
                SetCompressionProperties(outArchive, format, options);

                var outStream = new OutStreamWrapper(output);
                var updateCallback = new ArchiveUpdateCallback(entries, callback);

                hr = outArchive.UpdateItems(outStream, (uint)entries.Length, updateCallback);
                if (hr != 0)
                    throw new InvalidOperationException($"Failed to create archive: {hr:X8}");
            }
            finally
            {
                Marshal.ReleaseComObject(outArchive);
            }
        }

        SevenZipFormat GetFormatFromOptions(ResourceOptions options)
        {
            var arcOptions = options as ArchiveOptions;
            if (arcOptions != null && !string.IsNullOrEmpty(arcOptions.ArchiveFormat))
            {
                var format = arcOptions.ArchiveFormat.ToLowerInvariant();
                if (Formats.ExtensionFormatMapping.ContainsKey(format))
                    return Formats.ExtensionFormatMapping[format];
            }

            return SevenZipFormat.SevenZip;
        }

        void SetCompressionProperties(IOutArchive archive, SevenZipFormat format, ResourceOptions options)
        {
            var setProperties = archive as ISetProperties;
            if (setProperties == null)
                return;

            var props = new List<string>();
            var values = new List<object>();

            var arcOptions = options as ArchiveOptions;
            if (arcOptions != null)
            {
                props.Add("x");
                values.Add(arcOptions.CompressionLevel);
            }
            else
            {
                props.Add("x");
                values.Add(5);
            }

            switch (format)
            {
                case SevenZipFormat.SevenZip:
                    props.Add("m");
                    values.Add("LZMA2");
                    props.Add("mt");
                    values.Add("on");
                    break;

                case SevenZipFormat.Zip:
                    props.Add("m");
                    values.Add("Deflate");
                    break;
            }

            if (props.Count > 0)
            {
                setProperties.SetProperties(props.ToArray(), values.ToArray(), props.Count);
            }
        }

        SevenZipFormat GuessFormat(ArcView file)
        {
            var signatures = new Dictionary<byte[], SevenZipFormat>
            {
                { new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C },             SevenZipFormat.SevenZip },
                { new byte[] { 0x50, 0x4B, 0x03, 0x04 },                         SevenZipFormat.Zip },
                { new byte[] { 0x50, 0x4B, 0x05, 0x06 },                         SevenZipFormat.Zip },
                { new byte[] { 0x50, 0x4B, 0x07, 0x08 },                         SevenZipFormat.Zip },
                { new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }, SevenZipFormat.Rar5 },
                { new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 },       SevenZipFormat.Rar },
                { new byte[] { 0x1F, 0x8B },                                     SevenZipFormat.GZip },
                { new byte[] { 0x42, 0x5A, 0x68 },                               SevenZipFormat.BZip2 },
                { new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A },                   SevenZipFormat.XZ },
                { new byte[] { 0x4D, 0x53, 0x43, 0x46 },                         SevenZipFormat.Cab },
                { new byte[] { 0x49, 0x54, 0x53, 0x46 },                         SevenZipFormat.Chm },
                { new byte[] { 0x21, 0x3C, 0x61, 0x72, 0x63, 0x68, 0x3E },       SevenZipFormat.Deb },
                { new byte[] { 0xED, 0xAB, 0xEE, 0xDB },                         SevenZipFormat.Rpm },
                { new byte[] { 0x60, 0xEA },                                     SevenZipFormat.Arj },
            };

            byte[] header = new byte[8];
            file.View.Read(0, header, 0, 8);

            foreach (var sig in signatures)
            {
                if (header.Take(sig.Key.Length).SequenceEqual(sig.Key))
                    return sig.Value;
            }

            if (file.MaxOffset > 512)
            {
                byte[] tarSig = new byte[5];
                file.View.Read(257, tarSig, 0, 5);
                if (tarSig.AsciiEqual("ustar"))
                    return SevenZipFormat.Tar;
            }

            if (file.MaxOffset > 0x8000 + 5)
            {
                byte[] isoSig = new byte[5];
                file.View.Read(0x8001, isoSig, 0, 5);
                if (isoSig.AsciiEqual("CD001"))
                    return SevenZipFormat.Iso;
            }

            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            if (Formats.ExtensionFormatMapping.ContainsKey(ext))
                return Formats.ExtensionFormatMapping[ext];

            return SevenZipFormat.Undefined;
        }

        List<Entry> ReadDirectory(IInArchive archive, string arcName)
        {
            var itemCount = archive.GetNumberOfItems();
            var dir = new List<Entry>((int)itemCount);

            for (uint i = 0; i < itemCount; i++)
            {
                var entry = ReadEntry(archive, i);
                if (entry != null && !entry.IsFolder)
                    dir.Add(entry);
            }

            return dir;
        }

        SevenZipEntry ReadEntry(IInArchive archive, uint index)
        {
            string name    = GetProperty<string>(archive, index, ItemPropId.kpidPath);
            if (string.IsNullOrEmpty(name))
                return null;

            bool isFolder    = GetProperty<bool>(archive, index, ItemPropId.kpidIsFolder);
            if (isFolder)
                return null;

            var size        = GetProperty<ulong>(archive, index, ItemPropId.kpidSize);
            var packedSize  = GetProperty<ulong>(archive, index, ItemPropId.kpidPackedSize);
            var isEncrypted = GetProperty<bool>(archive,  index, ItemPropId.kpidEncrypted);

            var entry = new SevenZipEntry
            {
                Name        = name.Replace('\\', '/'),
                Type        = FormatCatalog.Instance.GetTypeFromName(name),
                Index       = index,
                Size        = (uint)size,
                Offset      = 0,
                IsFolder    = isFolder,
                PackedSize  = packedSize,
                IsEncrypted = isEncrypted
            };

            return entry;
        }

        T GetProperty<T>(IInArchive archive, uint index, ItemPropId propId)
        {
            PropVariant propVariant = new PropVariant();
            archive.GetProperty(index, propId, ref propVariant);
            object value = propVariant.GetObject();

            if (propVariant.VarType == VarEnum.VT_EMPTY)
            {
                propVariant.Clear();
                return default(T);
            }

            propVariant.Clear();

            if (value == null)
            {
                return default(T);
            }

            Type type = typeof(T);
            bool isNullable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
            Type underlyingType = isNullable ? Nullable.GetUnderlyingType(type) : type;

            T result = (T)Convert.ChangeType(value.ToString(), underlyingType);
            return result;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var sarc = arc as SevenZipArchive;
            var sentry = entry as SevenZipEntry;
            if (sarc == null || sentry == null)
                return base.OpenEntry(arc, entry);

            try
            {
                // First, try to extract with the existing archive
                return ExtractEntryInternal(sarc.Archive, sentry);
            }
#pragma warning disable CS0168
            catch (Exception ex)
            {
#if LOG_EVERYTHING 
                System.Diagnostics.Debug.WriteLine($"Initial extraction failed: {ex.Message}"); // this always fails on a first open entry??
#endif

                // Archive might be closed, try to reopen it
                try
                {
                    return ReopenAndExtract(sarc, sentry);
                }
                catch (Exception reopenEx)
                {
#if LOG_EVERYTHING 
                    System.Diagnostics.Debug.WriteLine($"Reopen and extract failed: {reopenEx.Message}");
#endif
                    throw new InvalidOperationException("Failed to extract entry after reopening archive", reopenEx);
                }
            }
#pragma warning restore CS0168
        }

        // Helper method to extract entry
        private Stream ExtractEntryInternal(IInArchive archive, SevenZipEntry entry)
        {
            // Verify archive is accessible
            uint numItems = archive.GetNumberOfItems();
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"Archive has {numItems} items");
#endif
            if (entry.Index >= numItems)
                throw new InvalidOperationException($"Entry index {entry.Index} is out of range (max: {numItems - 1})");

            var stream = new MemoryStream();
            var callback = new StreamExtractCallback(entry.Index, stream);

            int hr = archive.Extract(new[] { entry.Index }, 1, 0, callback);
            if (hr != 0)
            {
                stream.Dispose();
                throw new InvalidOperationException($"Failed to extract entry: 0x{hr:X8}");
            }

            stream.Position = 0;
            return stream;
        }

        private Stream ReopenAndExtract(SevenZipArchive arc, SevenZipEntry entry)
        {
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"Reopening archive for format: {arc.Format}");
#endif

            if (!Lib7ZipLoader.Load())
                return null;

            object archive;
            var classId = Formats.FormatGuidMapping[arc.Format];
            var interfaceId = typeof(IInArchive).GUID;

            int hr = CreateObject(ref classId, ref interfaceId, out archive);
            if (hr != 0)
                throw new COMException($"Failed to create archive interface: 0x{hr:X8}", hr);

            var inArchive = archive as IInArchive;
            if (inArchive == null)
            {
                Marshal.ReleaseComObject(archive);
                throw new InvalidCastException("Failed to cast to IInArchive");
            }

            var stream = new ArcViewStream(arc.File);
            var inStream = new InStreamWrapper(stream);

            try
            {
                ulong maxCheckStartPosition = 32 * 1024;
                hr = inArchive.Open(inStream, ref maxCheckStartPosition, null);
                if (hr != 0)
                    throw new InvalidOperationException($"Failed to reopen archive: 0x{hr:X8}");

                var extractedStream = ExtractEntryInternal(inArchive, entry);
                
                var archiveProperty = typeof(SevenZipArchive).GetProperty("Archive");
                var streamProperty = typeof(SevenZipArchive).GetProperty("Stream");
                if (archiveProperty != null && streamProperty != null)
                {
                    archiveProperty.SetValue(arc, inArchive);
                    streamProperty.SetValue(arc, inStream);
                }

                return extractedStream;
            }
            catch
            {
                inStream.Dispose();
                Marshal.ReleaseComObject(inArchive);
                throw;
            }
        }

        [DllImport("7z.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int CreateObject(ref Guid classID, ref Guid interfaceID,
            [MarshalAs(UnmanagedType.Interface)] out object outObject);
    }

    public class ArchiveOptions : ResourceOptions
    {
        public string ArchiveFormat { get; set; }
        public int CompressionLevel { get; set; }
        public Dictionary<string, object> FormatOptions { get; set; }

        public ArchiveOptions()
        {
            ArchiveFormat = "7z";
            CompressionLevel = 5;
            FormatOptions = new Dictionary<string, object>();
        }
    }

    internal class SevenZipArchive : ArcFile
    {
        public IInArchive Archive { get; private set; }
        public InStreamWrapper Stream { get; private set; }
        public SevenZipFormat Format { get; private set; }
        private GCHandle archiveHandle;

        public SevenZipArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
            IInArchive archive, InStreamWrapper stream, SevenZipFormat format)
            : base(arc, impl, dir)
        {
            Archive = archive;
            Stream = stream;
            Format = format;

            archiveHandle = GCHandle.Alloc(archive, GCHandleType.Normal);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Archive != null)
                {
                    if (archiveHandle.IsAllocated)
                        archiveHandle.Free();
                    try
                    {
                        Archive.Close();
                    }
                    catch { }
                    Marshal.ReleaseComObject(Archive);
                    Archive = null;
                }
                if (Stream != null)
                {
                    Stream.Dispose();
                    Stream = null;
                }
            }
            base.Dispose(disposing);
        }
    }

    internal class SevenZipEntry : Entry
    {
        public uint Index { get; set; }
        public bool IsFolder { get; set; }
        public ulong PackedSize { get; set; }
    }

    internal class ArchiveUpdateCallback : IArchiveUpdateCallback, ISequentialInStream, ICryptoGetTextPassword2
    {
        private       Entry[] m_entries;
        private EntryCallback m_callback;
        private           int m_current_index = -1;
        private        Stream m_current_stream;
        private          long m_total_size;
        private          long m_completed_size;

        public ArchiveUpdateCallback(Entry[] entries, EntryCallback callback)
        {
            m_entries    = entries;
            m_callback   = callback;
            m_total_size = entries.Sum(e => (long)e.Size);
        }

        public void SetTotal(ulong total) { }
        public void SetCompleted(ref ulong completeValue) { }

        public int GetUpdateItemInfo(uint index, ref int newData, ref int newProperties, ref uint indexInArchive)
        {
            newData = 1;
            newProperties = 1;
            indexInArchive = uint.MaxValue;
            return 0;
        }

        public int GetProperty(uint index, ItemPropId propID, ref PropVariant value)
        {
            var entry = m_entries[index];

            switch (propID)
            {
            case ItemPropId.kpidPath:
                value.SetString(entry.Name);
                break;
            case ItemPropId.kpidIsFolder:
                value.SetBool(false);
                break;
            case ItemPropId.kpidSize:
                value.SetULong((ulong)entry.Size);
                break;
            case ItemPropId.kpidAttributes:
                value.SetUInt(0x20);
                break;
            case ItemPropId.kpidCreationTime:
            case ItemPropId.kpidLastAccessTime:
            case ItemPropId.kpidLastWriteTime:
                value.SetFileTime(DateTime.Now);
                break;
            }

            return 0;
        }

        public int GetStream(uint index, out ISequentialInStream inStream)
        {
            m_current_index = (int)index;
            var entry = m_entries[index];

            if (m_callback != null)
            {
                int progress = (int)((m_completed_size * 100) / m_total_size);
                m_callback(progress, entry, "Adding file...");
            }

            m_current_stream = File.OpenRead(entry.Name);
            inStream = this;

            return 0;
        }

        public int SetOperationResult(int operationResult)
        {
            if (m_current_stream != null)
            {
                m_completed_size += m_current_stream.Length;
                m_current_stream.Dispose();
                m_current_stream = null;
            }
            return 0;
        }

        public uint Read(byte[] data, uint size)
        {
            if (m_current_stream == null)
                return 0;
            return (uint)m_current_stream.Read(data, 0, (int)size);
        }

        public int CryptoGetTextPassword2(ref int passwordIsDefined, out string password)
        {
            passwordIsDefined = 0;
            password = "";
            return 0;
        }
    }

    internal class StreamExtractCallback : IArchiveExtractCallback
    {
        private readonly uint m_index;
        private readonly Stream m_stream;
        private OutStreamWrapper m_streamWrapper;

        public StreamExtractCallback(uint index, Stream stream)
        {
            m_index = index;
            m_stream = stream;
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"StreamExtractCallback created for index {index}");
#endif
        }

        public void SetTotal(ulong total)
        {
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"SetTotal called: {total} bytes");
#endif
        }

        public void SetCompleted(ref ulong completeValue)
        {
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"SetCompleted called: {completeValue} bytes");
#endif
        }

        public int GetStream(uint index, out ISequentialOutStream outStream, AskMode askExtractMode)
        {
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"GetStream called: index={index}, askExtractMode={askExtractMode}");
#endif

            if ((index != m_index) || (askExtractMode != AskMode.kExtract))
            {
                outStream = null;
#if LOG_EVERYTHING 
                System.Diagnostics.Debug.WriteLine("GetStream returning null stream");
#endif
                return 0;
            }

            try
            {
                m_streamWrapper = new OutStreamWrapper(m_stream);
                outStream = m_streamWrapper;
#if LOG_EVERYTHING 
                System.Diagnostics.Debug.WriteLine("GetStream created output stream successfully");
#endif
                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetStream exception: {ex}");
                outStream = null;
                return unchecked((int)0x80004005); // E_FAIL
            }
        }

        public void PrepareOperation(AskMode askExtractMode)
        {
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"PrepareOperation called: {askExtractMode}");
#endif
        }

        public void SetOperationResult(OperationResult resultEOperationResult)
        {
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine($"SetOperationResult called: {resultEOperationResult}");
#endif

            if (resultEOperationResult != OperationResult.kOK)
            {
                string error;
                switch (resultEOperationResult)
                {
                case OperationResult.kUnSupportedMethod: error = "Unsupported compression method"; break;
                case OperationResult.kDataError: error = "Data error"; break;
                case OperationResult.kCRCError: error = "CRC error"; break;
                default: error = "Unknown error"; break;
                }
                throw new InvalidOperationException($"Extraction failed: {error}");
            }
        }
    }

    internal class ArcViewStream : Stream
    {
        private ArcView m_view;
        private long m_position;

        public ArcViewStream(ArcView view)
        {
            m_view = view;
            m_position = 0;
        }

        public override bool  CanRead => true;
        public override bool  CanSeek => true;
        public override bool CanWrite => false;
        public override long   Length => m_view.MaxOffset;
        public override long Position
        {
            get => m_position;
            set => m_position = value;
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, Length - m_position);
            if (read > 0)
            {
                m_view.View.Read(m_position, buffer, offset, (uint)read);
                m_position += read;
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    m_position = offset;
                    break;
                case SeekOrigin.Current:
                    m_position += offset;
                    break;
                case SeekOrigin.End:
                    m_position = Length + offset;
                    break;
            }
            return m_position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    internal static class Lib7ZipLoader
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

        static bool loaded = false;
        static bool failed = false;

        const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
        const uint LOAD_LIBRARY_SEARCH_SYSTEM32     = 0x00000800;

        public static bool Load()
        {
            if (loaded)
                return true;

            if (failed)
                return false; // no need to retry

            var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var arch = IntPtr.Size == 4 ? "x86" : "x64";

            string[] searchPaths = {
                Path.Combine(folder, arch, "7z.dll"),
                Path.Combine(folder, $"7z-{arch}.dll"),
                Path.Combine(folder, "7z.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.dll")
            };

            foreach (var path in searchPaths)
            {
                if (!File.Exists(path)) continue;

                var handle = LoadLibraryEx(path, IntPtr.Zero,
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
                if (handle != IntPtr.Zero)
                {
                    loaded = true;
                    return true;
                }
            }

            failed = true;
#if LOG_EVERYTHING 
            System.Diagnostics.Debug.WriteLine("7z.dll not found in any expected location");
#endif
            return false;
        }
    }

    #region COM interfaces

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600800000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOutArchive
    {
        [PreserveSig]
        int UpdateItems(
            [MarshalAs(UnmanagedType.Interface)] ISequentialOutStream outStream,
            uint numItems,
            [MarshalAs(UnmanagedType.Interface)] IArchiveUpdateCallback updateCallback);

        void GetFileTimeType(out uint type);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600820000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IArchiveUpdateCallback
    {
        void SetTotal(ulong total);
        void SetCompleted([In] ref ulong completeValue);

        [PreserveSig]
        int GetUpdateItemInfo(uint index,
            ref int newData,
            ref int newProperties,
            ref uint indexInArchive);

        [PreserveSig]
        int GetProperty(uint index, ItemPropId propID, ref PropVariant value);

        [PreserveSig]
        int GetStream(uint index, out ISequentialInStream inStream);

        [PreserveSig]
        int SetOperationResult(int operationResult);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600030000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISetProperties
    {
        [PreserveSig]
        int SetProperties(
            [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 2)] string[] names,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] values,
            int numProps);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000500110000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICryptoGetTextPassword2
    {
        [PreserveSig]
        int CryptoGetTextPassword2(
            ref int passwordIsDefined,
            [MarshalAs(UnmanagedType.BStr)] out string password);
    }

    // Extended PropVariant for setting values
    internal partial struct PropVariant
    {
        public void SetString(string value)
        {
            this.vt = (ushort)VarEnum.VT_BSTR;
            this.pointerValue = Marshal.StringToBSTR(value);
        }

        public void SetBool(bool value)
        {
            this.vt = (ushort)VarEnum.VT_BOOL;
            this.byteValue = (byte)(value ? 255 : 0);
        }

        public void SetULong(ulong value)
        {
            this.vt = (ushort)VarEnum.VT_UI8;
            this.longValue = (long)value;
        }

        public void SetUInt(uint value)
        {
            this.vt = (ushort)VarEnum.VT_UI4;
            this.longValue = value;
        }

        public void SetFileTime(DateTime value)
        {
            this.vt = (ushort)VarEnum.VT_FILETIME;
            this.longValue = value.ToFileTime();
        }
    }

    #region Enums

    public enum SevenZipFormat
    {
        Undefined = 0,
        SevenZip,
        Arj,
        BZip2,
        Cab,
        Chm,
        Compound,
        Cpio,
        Deb,
        GZip,
        Iso,
        Lzh,
        Lzma,
        Nsis,
        Rar,
        Rar5,
        Rpm,
        Split,
        Tar,
        Wim,
        Lzw,
        Zip,
        Udf,
        Xar,
        Mub,
        Hfs,
        Dmg,
        XZ,
        Mslz,
        Flv,
        Swf,
        PE,
        Elf,
        Msi,
        Vhd,
        SquashFS,
        Lzma86,
        Ppmd,
        TE,
        UEFIc,
        UEFIs,
        CramFS,
        APM,
        Swfc,
        Ntfs,
        Fat,
        Mbr,
        MachO
    }

    internal enum ItemPropId : uint
    {
        kpidNoProperty = 0,
        kpidHandlerItemIndex = 2,
        kpidPath,
        kpidName,
        kpidExtension,
        kpidIsFolder,
        kpidSize,
        kpidPackedSize,
        kpidAttributes,
        kpidCreationTime,
        kpidLastAccessTime,
        kpidLastWriteTime,
        kpidSolid,
        kpidCommented,
        kpidEncrypted,
        kpidSplitBefore,
        kpidSplitAfter,
        kpidDictionarySize,
        kpidCRC,
        kpidType,
        kpidIsAnti,
        kpidMethod,
        kpidHostOS,
        kpidFileSystem,
        kpidUser,
        kpidGroup,
        kpidBlock,
        kpidComment,
        kpidPosition,
        kpidPrefix,
        kpidTotalSize = 0x1100,
        kpidFreeSpace,
        kpidClusterSize,
        kpidVolumeName,
        kpidLocalName = 0x1200,
        kpidProvider,
        kpidUserDefined = 0x10000
    }

    internal enum AskMode : int
    {
        kExtract = 0,
        kTest,
        kSkip,
        kReadExternal
    }

    internal enum OperationResult : int
    {
        kOK = 0,
        kUnSupportedMethod,
        kDataError,
        kCRCError
    }

    #endregion

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600600000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInArchive
    {
        [PreserveSig]
        int Open(
            IInStream stream,
            [In] ref ulong maxCheckStartPosition,
            [MarshalAs(UnmanagedType.Interface)] IArchiveOpenCallback openArchiveCallback);

        void Close();

        uint GetNumberOfItems();

        void GetProperty(
            uint index,
            ItemPropId propID, // PROPID
            ref PropVariant value); // PROPVARIANT

        [PreserveSig]
        int Extract(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] uint[] indices,
            uint numItems,
            int testMode,
            [MarshalAs(UnmanagedType.Interface)] IArchiveExtractCallback extractCallback);

        void GetArchiveProperty(
            uint propID, // PROPID
            ref PropVariant value); // PROPVARIANT

        uint GetNumberOfProperties();

        void GetPropertyInfo(
            uint index,
            [MarshalAs(UnmanagedType.BStr)] out string name,
            out ItemPropId propID, // PROPID
            out ushort varType); //VARTYPE

        uint GetNumberOfArchiveProperties();

        void GetArchivePropertyInfo(
            uint index,
            [MarshalAs(UnmanagedType.BStr)] string name,
            ref uint propID, // PROPID
            ref ushort varType); //VARTYPE
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600200000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IArchiveExtractCallback
    {
        // IProgress methods
        void SetTotal(ulong total);
        void SetCompleted([In] ref ulong completeValue);

        // IArchiveExtractCallback methods
        [PreserveSig]
        int GetStream(
            uint index,
            [MarshalAs(UnmanagedType.Interface)] out ISequentialOutStream outStream,
            AskMode askExtractMode);

        void PrepareOperation(AskMode askExtractMode);
        void SetOperationResult(OperationResult resultEOperationResult);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300010000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISequentialInStream
    {
        uint Read(
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
            uint size);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300020000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISequentialOutStream
    {
        [PreserveSig]
        int Write(
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
            uint size,
            IntPtr processedSize);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300030000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInStream
    {
        uint Read(
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
            uint size);

        void Seek(
            long offset,
            uint seekOrigin,
            IntPtr newPosition);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300040000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOutStream
    {
        [PreserveSig]
        int Write(
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
            uint size,
            IntPtr processedSize);

        void Seek(
            long offset,
            uint seekOrigin,
            IntPtr newPosition);

        [PreserveSig]
        int SetSize(long newSize);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600100000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IArchiveOpenCallback
    {
        void SetTotal(IntPtr files, IntPtr bytes);
        void SetCompleted(IntPtr files, IntPtr bytes);
    }

    // Structures
    [StructLayout(LayoutKind.Explicit)]
    internal partial struct PropVariant
    {
        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
        [FieldOffset(8)] public byte byteValue;
        [FieldOffset(8)] public long longValue;
        [FieldOffset(8)] public System.Runtime.InteropServices.ComTypes.FILETIME filetime;

        public VarEnum VarType
        {
            get { return (VarEnum)this.vt; }
        }

        public void Clear()
        {
            switch (this.VarType)
            {
                case VarEnum.VT_EMPTY:
                case VarEnum.VT_NULL:
                case VarEnum.VT_I2:
                case VarEnum.VT_I4:
                case VarEnum.VT_R4:
                case VarEnum.VT_R8:
                case VarEnum.VT_CY:
                case VarEnum.VT_DATE:
                case VarEnum.VT_ERROR:
                case VarEnum.VT_BOOL:
                case VarEnum.VT_I1:
                case VarEnum.VT_UI1:
                case VarEnum.VT_UI2:
                case VarEnum.VT_UI4:
                case VarEnum.VT_I8:
                case VarEnum.VT_UI8:
                case VarEnum.VT_INT:
                case VarEnum.VT_UINT:
                case VarEnum.VT_HRESULT:
                case VarEnum.VT_FILETIME:
                    this.vt = 0;
                    break;
                case VarEnum.VT_BSTR:
                    Marshal.FreeBSTR(this.pointerValue);
                    this.vt = 0;
                    break;
                default:
                    PropVariantClear(ref this);
                    break;
            }
        }

        public object GetObject()
        {
            switch (this.VarType)
            {
                case VarEnum.VT_EMPTY:
                    return null;

                case VarEnum.VT_FILETIME:
                    return DateTime.FromFileTime(this.longValue);

                default:
                    GCHandle PropHandle = GCHandle.Alloc(this, GCHandleType.Pinned);
                    try
                    {
                        return Marshal.GetObjectForNativeVariant(PropHandle.AddrOfPinnedObject());
                    }
                    finally
                    {
                        PropHandle.Free();
                    }
            }
        }
    }

    #endregion

    #region Helper classes

    internal class StreamWrapper : IDisposable
    {
        protected Stream BaseStream;

        protected StreamWrapper(Stream baseStream)
        {
            this.BaseStream = baseStream;
        }

        public void Dispose()
        {
            this.BaseStream.Close();
        }

        public virtual void Seek(long offset, uint seekOrigin, IntPtr newPosition)
        {
            long Position = this.BaseStream.Seek(offset, (SeekOrigin)seekOrigin);
            if (newPosition != IntPtr.Zero)
            {
                Marshal.WriteInt64(newPosition, Position);
            }
        }
    }

    internal class InStreamWrapper : StreamWrapper, ISequentialInStream, IInStream
    {
        public InStreamWrapper(Stream baseStream) : base(baseStream)
        {
        }

        public uint Read(byte[] data, uint size)
        {
            return (uint)this.BaseStream.Read(data, 0, (int)size);
        }
    }

    internal class OutStreamWrapper : StreamWrapper, ISequentialOutStream, IOutStream
    {
        public OutStreamWrapper(Stream baseStream) : base(baseStream)
        {
        }

        public int SetSize(long newSize)
        {
            this.BaseStream.SetLength(newSize);
            return 0;
        }

        public int Write(byte[] data, uint size, IntPtr processedSize)
        {
            this.BaseStream.Write(data, 0, (int)size);
            if (processedSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(processedSize, (int)size);
            }
            return 0;
        }
    }

    #endregion

    #region Format mappings

    internal static class Formats
    {
        internal static readonly Dictionary<string, SevenZipFormat> ExtensionFormatMapping = new Dictionary<string, SevenZipFormat>
        {
            {"7z",   SevenZipFormat.SevenZip},
            {"gz",   SevenZipFormat.GZip},
            {"tar",  SevenZipFormat.Tar},
            {"rar",  SevenZipFormat.Rar},
            {"zip",  SevenZipFormat.Zip},
            {"lzma", SevenZipFormat.Lzma},
            {"lzh",  SevenZipFormat.Lzh},
            {"arj",  SevenZipFormat.Arj},
            {"bz2",  SevenZipFormat.BZip2},
            {"cab",  SevenZipFormat.Cab},
            {"chm",  SevenZipFormat.Chm},
            {"deb",  SevenZipFormat.Deb},
            {"iso",  SevenZipFormat.Iso},
            {"rpm",  SevenZipFormat.Rpm},
            {"wim",  SevenZipFormat.Wim},
            {"udf",  SevenZipFormat.Udf},
            {"mub",  SevenZipFormat.Mub},
            {"xar",  SevenZipFormat.Xar},
            {"hfs",  SevenZipFormat.Hfs},
            {"dmg",  SevenZipFormat.Dmg},
            {"z",    SevenZipFormat.Lzw},
            {"xz",   SevenZipFormat.XZ},
            {"flv",  SevenZipFormat.Flv},
            {"swf",  SevenZipFormat.Swf},
            {"exe",  SevenZipFormat.PE},
            {"dll",  SevenZipFormat.PE},
            {"vhd",  SevenZipFormat.Vhd},
            {"msi",  SevenZipFormat.Msi},
            {"nsis", SevenZipFormat.Nsis},
            {"squashfs", SevenZipFormat.SquashFS},
            {"tgz",  SevenZipFormat.GZip},
            {"tbz2", SevenZipFormat.BZip2},
            {"txz",  SevenZipFormat.XZ}
        };

        internal static Dictionary<SevenZipFormat, Guid> FormatGuidMapping = new Dictionary<SevenZipFormat, Guid>
        {
            {SevenZipFormat.SevenZip, new Guid("23170f69-40c1-278a-1000-000110070000")},
            {SevenZipFormat.Arj,      new Guid("23170f69-40c1-278a-1000-000110040000")},
            {SevenZipFormat.BZip2,    new Guid("23170f69-40c1-278a-1000-000110020000")},
            {SevenZipFormat.Cab,      new Guid("23170f69-40c1-278a-1000-000110080000")},
            {SevenZipFormat.Chm,      new Guid("23170f69-40c1-278a-1000-000110e90000")},
            {SevenZipFormat.Compound, new Guid("23170f69-40c1-278a-1000-000110e50000")},
            {SevenZipFormat.Cpio,     new Guid("23170f69-40c1-278a-1000-000110ed0000")},
            {SevenZipFormat.Deb,      new Guid("23170f69-40c1-278a-1000-000110ec0000")},
            {SevenZipFormat.GZip,     new Guid("23170f69-40c1-278a-1000-000110ef0000")},
            {SevenZipFormat.Iso,      new Guid("23170f69-40c1-278a-1000-000110e70000")},
            {SevenZipFormat.Lzh,      new Guid("23170f69-40c1-278a-1000-000110060000")},
            {SevenZipFormat.Lzma,     new Guid("23170f69-40c1-278a-1000-0001100a0000")},
            {SevenZipFormat.Nsis,     new Guid("23170f69-40c1-278a-1000-000110090000")},
            {SevenZipFormat.Rar,      new Guid("23170f69-40c1-278a-1000-000110030000")},
            {SevenZipFormat.Rar5,     new Guid("23170f69-40c1-278a-1000-000110CC0000")},
            {SevenZipFormat.Rpm,      new Guid("23170f69-40c1-278a-1000-000110eb0000")},
            {SevenZipFormat.Split,    new Guid("23170f69-40c1-278a-1000-000110ea0000")},
            {SevenZipFormat.Tar,      new Guid("23170f69-40c1-278a-1000-000110ee0000")},
            {SevenZipFormat.Wim,      new Guid("23170f69-40c1-278a-1000-000110e60000")},
            {SevenZipFormat.Lzw,      new Guid("23170f69-40c1-278a-1000-000110050000")},
            {SevenZipFormat.Zip,      new Guid("23170f69-40c1-278a-1000-000110010000")},
            {SevenZipFormat.Udf,      new Guid("23170f69-40c1-278a-1000-000110E00000")},
            {SevenZipFormat.Xar,      new Guid("23170f69-40c1-278a-1000-000110E10000")},
            {SevenZipFormat.Mub,      new Guid("23170f69-40c1-278a-1000-000110E20000")},
            {SevenZipFormat.Hfs,      new Guid("23170f69-40c1-278a-1000-000110E30000")},
            {SevenZipFormat.Dmg,      new Guid("23170f69-40c1-278a-1000-000110E40000")},
            {SevenZipFormat.XZ,       new Guid("23170f69-40c1-278a-1000-0001100C0000")},
            {SevenZipFormat.Mslz,     new Guid("23170f69-40c1-278a-1000-000110D50000")},
            {SevenZipFormat.PE,       new Guid("23170f69-40c1-278a-1000-000110DD0000")},
            {SevenZipFormat.Elf,      new Guid("23170f69-40c1-278a-1000-000110DE0000")},
            {SevenZipFormat.Swf,      new Guid("23170f69-40c1-278a-1000-000110D70000")},
            {SevenZipFormat.Vhd,      new Guid("23170f69-40c1-278a-1000-000110DC0000")},
            {SevenZipFormat.Flv,      new Guid("23170f69-40c1-278a-1000-000110D60000")},
            {SevenZipFormat.SquashFS, new Guid("23170f69-40c1-278a-1000-000110D20000")},
            {SevenZipFormat.Lzma86,   new Guid("23170f69-40c1-278a-1000-0001100B0000")},
            {SevenZipFormat.Ppmd,     new Guid("23170f69-40c1-278a-1000-0001100D0000")},
            {SevenZipFormat.TE,       new Guid("23170f69-40c1-278a-1000-000110CF0000")},
            {SevenZipFormat.UEFIc,    new Guid("23170f69-40c1-278a-1000-000110D00000")},
            {SevenZipFormat.UEFIs,    new Guid("23170f69-40c1-278a-1000-000110D10000")},
            {SevenZipFormat.CramFS,   new Guid("23170f69-40c1-278a-1000-000110D30000")},
            {SevenZipFormat.APM,      new Guid("23170f69-40c1-278a-1000-000110D40000")},
            {SevenZipFormat.Swfc,     new Guid("23170f69-40c1-278a-1000-000110D80000")},
            {SevenZipFormat.Ntfs,     new Guid("23170f69-40c1-278a-1000-000110D90000")},
            {SevenZipFormat.Fat,      new Guid("23170f69-40c1-278a-1000-000110DA0000")},
            {SevenZipFormat.Mbr,      new Guid("23170f69-40c1-278a-1000-000110DB0000")},
            {SevenZipFormat.MachO,    new Guid("23170f69-40c1-278a-1000-000110DF0000")},
            {SevenZipFormat.Msi,      new Guid("23170f69-40c1-278a-1000-000110e50000")}
        };
    }

    #endregion

    internal static class ByteArrayExtensions
    {
        public static bool AsciiEqual(this byte[] data, string text)
        {
            if (data.Length < text.Length)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (data[i] != (byte)text[i])
                    return false;
            }
            return true;
        }

        public static int ToInt24(this byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        }
    }
}