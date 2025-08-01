using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using GameRes.Strings;

namespace GameRes
{
    /// <summary>
    /// Basic filesystem entry.
    /// </summary>
    public class Entry
    {
        public virtual string      Name { get; set; }
        public virtual string      Type { get; set; }
        public         long      Offset { get; set; }
        public         long        Size { get; set; }
        public         bool IsEncrypted { get; set; }

        public Entry ()
        {
            Name = null;
            Type = "";
            Offset = -1;
            Size = 0;
            IsEncrypted = false;
        }

        /// <summary>
        /// Check whether entry lies within specified file bound.
        /// </summary>
        public bool CheckPlacement (long max_offset)
        {
            return Offset < max_offset && Size <= max_offset && Offset <= max_offset - Size;
        }

        /// <summary>
        /// Change entry type to the type of resource <paramref name="res"/>.
        /// Entry name extension is changed accordingly.
        /// </summary>
        public void ChangeType (IResource res)
        {
            if (null == res)
                return;
            Type = res.Type;
            foreach (var ext in res.Extensions)
            {
                if (!string.IsNullOrEmpty (ext))
                    Name = Path.ChangeExtension (Name, ext);
                break;
            }
        }

        /// <summary>
        /// Change entry type to the type of <paramref name="res"/>.
        /// Entry name extension is changed accordingly.
        /// </summary>
        public void ChangeType(string entry_type)
        {
            Type = entry_type;
        }
    }

    public class PackedEntry : Entry
    {
        public long PackedSize   { get => Size; } // alias
        public long UnpackedSize { get; set; }
        public bool IsPacked     { get; set; }
    }

    public abstract class IResource
    {
        /// <summary>Short tag sticked to resource (usually filename extension)</summary>
        public abstract string Tag { get; }

        /// <summary>Resource description (its source/inventor)</summary>
        public abstract string Description { get; }

        /// <summary>Resource type (image/archive/script)</summary>
        public abstract string Type { get; }

        /// <summary>
        /// First 4 bytes of the resource file as little-endian 32-bit integer,
        /// or zero if it could vary.
        /// </summary>
        public abstract uint Signature { get; }

        /// <summary>Whether resource creation is supported by implementation.</summary>
        public virtual bool CanWrite { get { return false; } }

        /// <summary>Signatures peculiar to the resource (the one above is also included here).</summary>
        public IEnumerable<uint> Signatures { get; protected set; }

        /// <summary>Filename extensions peculiar to the resource.</summary>
        public IEnumerable<string> Extensions { get; protected set; }

        /// <summary>Persistent resource settings.</summary>
        public IEnumerable<IResourceSetting> Settings { get; protected set; }

        /// <summary>Resource access scheme suitable for serialization.</summary>
        public virtual ResourceScheme Scheme { get; set; }

        protected IResource ()
        {
            Extensions = new string[] { GetDefaultExtension() };
            Signatures = new uint[] { this.Signature };
        }

        protected string GetDefaultExtension ()
        {
            var ext = Tag.ToLowerInvariant();
            int slash = ext.IndexOf ('/');
            if (slash != -1)
                ext = ext.Substring (0, slash);
            return ext;
        }

        public virtual ResourceOptions GetDefaultOptions ()
        {
            return null;
        }

        public virtual ResourceOptions GetOptions (object widget)
        {
            return GetDefaultOptions();
        }

        public virtual object GetCreationWidget ()
        {
            return null;
        }

        public virtual object GetAccessWidget ()
        {
            return null;
        }

        protected OptType GetOptions<OptType> (ResourceOptions res_options) where OptType : ResourceOptions
        {
            var options = res_options as OptType;
            if (null == options)
                options = this.GetDefaultOptions() as OptType;
            return options;
        }

        protected OptType Query<OptType> (string notice) where OptType : ResourceOptions
        {
            var args = new ParametersRequestEventArgs { Notice = notice };
            FormatCatalog.Instance.InvokeParametersRequest (this, args);
            if (!args.InputResult)
                throw new OperationCanceledException();

            return GetOptions<OptType> (args.Options);
        }
    }

    public class ResourceOptions
    {
    }

    [Serializable]
    public class ResourceScheme
    {
    }

    public class ResourceAlias
    {
    }

    /// <summary>
    /// Filename extension mapping to specific resource.
    /// </summary>
    public interface IResourceAliasMetadata
    {
        string Extension { get; }
        string    Target { get; }
        [DefaultValue(null)]
        string      Type { get; }
    }

    public interface IResourceMetadata
    {
        [DefaultValue(0)]
        int Priority { get; }
    }

    public delegate void ParametersRequestEventHandler (object sender, ParametersRequestEventArgs e);

    public class ParametersRequestEventArgs : EventArgs
    {
        /// <summary>
        /// String describing request nature (encryption key etc).
        /// </summary>
        public string Notice        { get; set; }

        /// <summary>
        /// Return value from ShowDialog().
        /// </summary>
        public bool   InputResult   { get; set; }

        /// <summary>
        /// Archive-specific options set by InputWidget.
        /// </summary>
        public ResourceOptions Options { get; set; }
    }

    public class InvalidFormatException : FileFormatException
    {
        public InvalidFormatException() : base(garStrings.MsgInvalidFormat) { }
        public InvalidFormatException (string msg) : base (msg) { }
    }

    public class UnknownEncryptionScheme : Exception
    {
        public UnknownEncryptionScheme() : base(garStrings.MsgUnknownEncryption) { }
        public UnknownEncryptionScheme (string msg) : base (msg) { }
    }

    public class InvalidEncryptionScheme : Exception
    {
        public InvalidEncryptionScheme() : base(garStrings.MsgInvalidEncryption) { }
        public InvalidEncryptionScheme (string msg) : base (msg) { }
    }

    public class FileSizeException : Exception
    {
        public FileSizeException () : base (garStrings.MsgFileTooLarge) { }
        public FileSizeException (string msg) : base (msg) { }
    }

    public class InvalidFileName : Exception
    {
        public string FileName { get; set; }

        public InvalidFileName (string filename)
            : this (filename, garStrings.MsgInvalidFileName)
        {
        }

        public InvalidFileName (string filename, string message)
            : base (message)
        {
            FileName = filename;
        }

        public InvalidFileName (string filename, Exception X)
            : this (filename, garStrings.MsgInvalidFileName, X)
        {
        }

        public InvalidFileName (string filename, string message, Exception X)
            : base (message, X)
        {
            FileName = filename;
        }
    }
}
