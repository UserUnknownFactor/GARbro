using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameRes
{
    /// <summary>
    /// Abstract base class for archive resource implementations.
    /// </summary>
    public abstract class ArchiveFormat : IResource
    {
        public override string Type { get { return "archive"; } }

        /// <summary>
        /// Whether archive file system could contain subdirectories.
        /// </summary>
        public abstract bool IsHierarchic { get; }

        /// <summary>
        /// Tags of formats related to this archive format (could be null).
        /// </summary>
        public IEnumerable<string> ContainedFormats { get; protected set; }

        public abstract ArcFile TryOpen (ArcView view);

        /// <summary>
        /// Create GameRes.Entry corresponding to <paramref name="filename"/> extension.
        /// </summary>
        /// <exception cref="System.ArgumentException">May be thrown if filename contains invalid
        /// characters.</exception>
        public EntryType Create<EntryType> (string filename) where EntryType : Entry, new()
        {
            return new EntryType {
                Name = filename,
                Type = FormatCatalog.Instance.GetTypeFromName (filename, ContainedFormats),
            };
        }

        /// <summary>
        /// Extract file referenced by <paramref name="entry"/> into current directory.
        /// </summary>
        public void Extract (ArcFile file, Entry entry)
        {
            using (var input = OpenEntry (file, entry))
            using (var output = PhysicalFileSystem.CreateFile (entry.Name))
                input.CopyTo (output);
        }

        /// <summary>
        /// Open file referenced by <paramref name="entry"/> as Stream.
        /// </summary>
        public virtual Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size > 0)
                return arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            else
                return Stream.Null;
        }

        /// <summary>
        /// Open <paramref name="entry"> as image. Throws InvalidFormatException if entry is not an image.
        /// </summary>
        public virtual IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            return ImageFormatDecoder.Create (input);
        }

        /// <summary>
        /// Create resource within stream <paramref name="file"/> containing entries from the
        /// supplied <paramref name="list"/> and applying necessary <paramref name="options"/>.
        /// </summary>
        public virtual void Create (Stream file, IEnumerable<Entry> list, ResourceOptions options = null,
                                    EntryCallback callback = null)
        {
            throw new NotImplementedException ("ArchiveFormat.Create is not implemented");
        }

        /// <summary>
        /// Whether <paramref name="count"/> represents legit number of files in archive.
        /// </summary>
        public static bool IsSaneCount (int count)
        {
            return count > 0 && count < 0x40000;
        }

        /// <summary>
        /// Whether <paramref name="name"/> represents a valid archive entry name.
        /// </summary>
        public static bool IsValidEntryName (string name)
        {
            return !string.IsNullOrWhiteSpace (name) && !Path.IsPathRooted (name);
        }
    }

    public enum ArchiveOperation
    {
        Abort,
        Skip,
        Continue,
    }

    public delegate ArchiveOperation EntryCallback (int num, Entry entry, string description);
}
