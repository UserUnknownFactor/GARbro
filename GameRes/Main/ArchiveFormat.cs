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


        /// <summary>
        /// Try to open <paramref name="file"/> object as archive.
        /// </summary>
        /// <returns>
        /// <b><see cref="ArcFile"/></b> object if file is opened successfully, <b>null</b> otherwise.
        /// </returns>
        public abstract ArcFile TryOpen (ArcView file);

        /// <summary>
        /// Create <see cref="Entry"/> corresponding to <paramref name="filename"/>'s extension.
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
        /// Extract <paramref name="arc"/>'s file referenced by <paramref name="entry"/> into the <i>current</i> directory.
        /// </summary>
        public void Extract (ArcFile file, Entry entry)
        {
            using (var input = OpenEntry (file, entry))
            using (var output = PhysicalFileSystem.CreateFile (entry.Name))
                input.CopyTo (output);
        }

        /// <summary>
        /// Return <paramref name="arc"/>'s file referenced by <paramref name="entry"/> as <see cref="Stream"/>.
        /// </summary>
        public virtual Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size > 0)
                return arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            else
                return Stream.Null;
        }

        /// <summary>
        /// Open <paramref name="arc"/>'s file referenced by <paramref name="entry" /> as an image.
        /// </summary>
        /// <exception cref="InvalidFormatException">May be thrown if entry is not an image.
        /// characters.</exception>
        public virtual IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            return ImageFormatDecoder.Create (input);
        }

        /// <summary>
        /// Create resource within stream <paramref name="file"/> containing entries from the
        /// supplied <paramref name="list"/> and applying necessary <paramref name="options"/>.
        /// </summary>
        /// <exception cref="NotImplementedException"/>
        public virtual void Create (Stream file, IEnumerable<Entry> list, ResourceOptions options = null,
                                    EntryCallback callback = null)
        {
            throw new NotImplementedException ("ArchiveFormat.Create is not implemented");
        }

        /// <summary>
        /// Checks whether <paramref name="count"/> represents legitimate number of items<br/>
        /// Can be specified with the <paramref name = "possible_max" /> parameter.
        /// </summary>
        public static bool IsSaneCount (int count, int possible_max = 0x40000)
        {
            return count > 0 && count < possible_max;
        }

        /// <inheritdoc cref="IsSaneCount (int, int)" />
        public static bool IsSaneCount(long count, long possible_max = 0x40000)
        {
            return count > 0 && count < possible_max;
        }

        /// <inheritdoc cref="IsSaneCount (int, int)" />
        public static bool IsSaneCount(uint count, uint possible_max = 0x40000)
        {
            return count > 0 && count < possible_max;
        }

        /// <summary>
        /// Checks whether <paramref name="count"/> represents legitimate number of items 
        /// that be specified with the <paramref name = "possible_max" /> parameter.<br/><br/>
        /// Useful when we are 100% sure that the format is correct, but it cannot be parsed:<br/>
        /// can be better than returning <b>null</b> and searching for other formats.</summary>
        /// <exception cref="InvalidFormatException"/>
        public static void IsSaneCountWithException(int count, int possible_max = 0x40000, string comment=null)
        {
            if (count < 0 && count > possible_max)
                throw new InvalidFormatException(comment);
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
