# GARbro Plugin Development Documentation

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Core Components](#core-components)
4. [Creating Archive Format Plugins](#creating-archive-format-plugins)
5. [Creating Image Format Plugins](#creating-image-format-plugins)
6. [Creating Audio Format Plugins](#creating-audio-format-plugins)
7. [Creating Video Format Plugins](#creating-video-format-plugins)
8. [Utility Classes and Helpers](#utility-classes-and-helpers)
9. [Settings and Configuration](#settings-and-configuration)
10. [Best Practices](#best-practices)

## Introduction

GARbro is an extensible visual novel resource extraction tool that uses a plugin-based architecture built on .NET's Managed Extensibility Framework (MEF). This documentation covers how to write plugins to support new archive, image, audio, and video formats.

## Architecture Overview

### Plugin System
GARbro uses MEF (Managed Extensibility Framework) for plugin discovery and loading. Plugins are discovered through:
- `[Export]` attributes on plugin classes
- `[ExportMetadata]` for additional plugin information
- Automatic discovery at runtime from assemblies

### Key Namespaces
- `GameRes`             - Core resource interfaces and base classes
- `GameRes.Formats`     - Format implementations and utilities
- `GameRes.Compression` - Compression/decompression utilities
- `GameRes.Utility`     - Helper classes and extensions

## Core Components

### Entry Class
The base class for all file entries in archives:

```csharp
public class Entry
{
        public virtual string      Name { get; set; }  // full path either inside the archive or on the file system
        public virtual string      Type { get; set; }  // File type (archive/image/audio/video)
        public         long      Offset { get; set; }  // File offset in the archive
        public         long        Size { get; set; }  // File size in the archive
        public virtual bool IsEncrypted { get; set; }  // File encryption flag
}
```

### IResource Interface
Base interface for all format handlers:

```csharp
public interface IResource
{
    string         Tag { get; }  // Unique format identifier
    string Description { get; }  // Human-readable description
    uint     Signature { get; }  // File signature (magic number)
    string        Type { get; }  // Resource type (same as file type)
}
```

### ArcView with Memory-Mapped File Access
GARbro uses memory-mapped files for efficient archive access through the `ArcView` class:

```csharp
// ArcView provides memory-mapped file access
public override ArcFile TryOpen (ArcView file)
{
    // Direct memory access without loading entire file
    ushort signature  = file.View.ReadUInt16 (0);
    string name       = file.View.ReadString (0x10, 256);

    using (var stream = file.CreateStream (offset, size))
    {
        var magic = stream.ReadUInt32()
        // ...
    }

    // Frame-based reading for large files
    // `file.View` might only have first 4KB mapped but index is at end of 5GB file
    long indexOffset  = file.MaxOffset - 0x10000; 

    // Create frame specifically for the index area
    using (var indexFrame = file.CreateFrame())
    {
        indexFrame.Reserve(indexOffset, 0x10000);  // Pre-map entire index data region

        uint magic = indexFrame.ReadUInt32(indexOffset);
        uint count = indexFrame.ReadUInt32(indexOffset + 4);

        // Read entire index without remapping
        for (int i = 0; i < count; i++)
        {
            var name = indexFrame.ReadString(indexOffset + 8 + i * 264, 256);
            var offset = indexFrame.ReadUInt64(indexOffset + 8 + i * 264 + 256);
            // ...
        }
    }
    // ...
}
```

### IBinaryStream Interface
Provides convenient binary reading methods:

```csharp
public override ImageData Read (IBinaryStream file, ImageMetaData info)
{
    file.Position = 0x20;

    // Convenient type-safe reading
    uint width    = file.ReadUInt32();
    uint height   = file.ReadUInt32();
    short bpp     = file.ReadInt16();

    // Reading big-endian
    uint value     = file.ReadUInt32BE();

    // Read strings with encoding
    string name   = file.ReadCString (256, Encodings.cp932);

    // Read arrays
    byte[] pixels = file.ReadBytes (width * height * 4);

    // Big-endian support
    uint be_value = file.ReadUInt32BE();

    // ...
}
```

### CowArray - Copy-on-Write Arrays
Efficient array handling for read-heavy operations:

```csharp
var header = file.ReadHeader (0x100);  // Returns CowArray<byte>
// The CowArray just references the internal buffer - no copy yet

// Reading doesn't trigger a copy:
if (!header.AsciiEqual (0, "MAGIC"))  // Just reads from original buffer
    return null;

uint version = header.ToUInt32 (8);  // Still no copy

// But if you modify it:
header[0] = 0xFF;  // Now it copies the data first, then modifies
```

## Creating Archive Format Plugins

### Basic Archive Plugin Structure

```csharp
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes;

[Export (typeof(ArchiveFormat))]
public class MyArchiveFormat : ArchiveFormat
{
    public override string         Tag { get { return "MYARC"; } } // used for serialisation/options/etc
    public override string Description { get { return "My Archive Format"; } }
    public override uint     Signature { get { return  0x54534554; } } // first bytes of the file: "TEST"
    public override bool  IsHierarchic { get { return  false; } } // wheter it's displayed as a simple list or tree
    public override bool      CanWrite { get { return  false; } } // hints if Write function is implemented

    public override ArcFile TryOpen (ArcView file)
    {
        // Verify signature if needed (if Signature above is 0 file is always checked here)
        if (!file.View.AsciiEqual (0, "TESTARCHIVE"))
            return null;

        // Read header
        uint count = file.View.ReadUInt32 (4); // read uint at postion 4
        if (!IsSaneCount (count))
            return null;

        // Read directory (if archive is hierarchic it will be converted into a tree automatically using entry names)
        var dir = new List<Entry>();
        uint index_offset = 8;

        for (uint i = 0; i < count; ++i)
        {
            var entry = new Entry // our archive consists of Entry items list
            {
                Name   = file.View.ReadString (index_offset,  0x100),
                Offset = file.View.ReadUInt32 (index_offset + 0x100),
                Size   = file.View.ReadUInt32 (index_offset + 0x104)
            };

            // Auto-detect file type
            entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);

            dir.Add (entry);
            index_offset += 0x108;
        }

        Comment = "Additional comment to display in preview status";

        return new ArcFile (file, this, dir);
    }
}
```

### Advanced Features

#### Encrypted Archives
```csharp
public class MyEncryptedEntry : Entry
{
    public byte Key { get; set; }
}

public override ArcFile TryOpen (ArcView file)
{
    var dir = new List<Entry>();

    // When reading the archive index
    var entry = new MyEncryptedEntry
    {
        Name        = "file.dat",
        Offset      =  0x100,
        Size        =  0x100,
        IsEncrypted =  true,
        Key         =  0x42
    };
    dir.Add (entry);

    return new ArcFile (file, this, dir);
}

public override Stream OpenEntry (ArcFile arc, Entry entry)
{
    var input = arc.File.CreateStream (entry.Offset, entry.Size);

    if (entry.IsEncrypted) 
    {
        // Apply decryption
        return new XoredStream (input, entry.Key);
    }
    return input; // caller owns this so no need for disposal here
}
```

#### Compressed Archives
```csharp
public override ArcFile TryOpen (ArcView file)
{
    var dir = new List<Entry>();

    // When reading the archive index
    var entry = new PackedEntry
    {
        Name         = "file.dat",
        Offset       =  0x100,
        Size         =  0x100,
        UnpackedSize =  0x1000,
        IsPacked     =  true,
    };
    dir.Add (entry);

    return new ArcFile (file, this, dir);
}

public override Stream OpenEntry (ArcFile arc, Entry entry)
{
    var input = arc.File.CreateStream (entry.Offset, entry.Size);

    if (entry.IsPacked)
    {
        // Unpack it
        var packed = new ZLibStream (input, CompressionMode.Decompress);
        return new SeekableStream (packed);
    }

    return input;
}
```

## Creating Image Format Plugins

### Basic Image Plugin Structure

```csharp
[Export (typeof(ImageFormat))]
public class MyImageFormat : ImageFormat
{
    public override string         Tag { get { return "MIM"; } }
    public override string Description { get { return "My Image Format"; } }
    public override uint     Signature { get { return  0x4D494D00; } }

    public override ImageMetaData ReadMetaData (IBinaryStream file)
    {
        var header = file.ReadHeader (0x10);

        return new ImageMetaData
        {
            Width  = header.ToUInt32 (4),
            Height = header.ToUInt32 (8),
            BPP    = header.ToInt32 (12)
        };
    }

    public override ImageData Read (IBinaryStream file, ImageMetaData info)
    {
        file.Position = 0x10;

        int stride = (int)info.Width * info.BPP / 8;
        var pixels = file.ReadBytes (stride * (int)info.Height);

        PixelFormat format = info.BPP == 32 ? PixelFormats.Bgra32 :
                            info.BPP == 24 ? PixelFormats.Bgr24 :
                            PixelFormats.Gray8;

        return ImageData.Create (info, format, null, pixels);
    }

    public override void Write (Stream file, ImageData image)
    {
        throw new NotImplementedException();
    }
}
```

### Custom Image Decoders

```csharp
public class MyImageDecoder : BinaryImageDecoder
{
    public MyImageDecoder (IBinaryStream input, ImageMetaData info)
        : base (input, info)
    {
    }

    protected override ImageData GetImageData()
    {
        m_input.Position = 0x20; // Skip header

        int stride = (int)Info.Width * 4;
        var pixels = new byte[stride * (int)Info.Height];

        // Custom decoding logic
        for (int y = 0; y < Info.Height; ++y)
        {
            for (int x = 0; x < Info.Width; ++x)
            {
                int dst = y * stride + x * 4;
                pixels[dst  ] = m_input.ReadUInt8();   // B
                pixels[dst+1] = m_input.ReadUInt8();   // G
                pixels[dst+2] = m_input.ReadUInt8();   // R
                pixels[dst+3] = m_input.ReadUInt8();   // A
            }
        }

        return ImageData.Create (Info, PixelFormats.Bgra32, null, pixels);
    }
}
```

## Creating Audio Format Plugins

### Basic Audio Plugin Structure

```csharp
[Export (typeof(AudioFormat))]
public class MyAudioFormat : AudioFormat
{
    public override string         Tag { get { return "MAU"; } }
    public override string Description { get { return "My Audio Format"; } }
    public override uint     Signature { get { return 0x5541414D; } } // "MAAU"

    public override SoundInput TryOpen (IBinaryStream file)
    {
        var header = file.ReadHeader (0x20);
        if (!header.AsciiEqual (0, "MAAU\0"))
            return null;

        var format = new WaveFormat
        {
            FormatTag        = 1,  // PCM
            Channels         = header.ToUInt16 (4),
            SamplesPerSecond = header.ToUInt32 (6),
            BitsPerSample    = header.ToUInt16 (10)
        };
        format.SetBPS();

        uint pcm_size = header.ToUInt32 (16);

        file.Position = 0x20;
        return new RawPcmInput (file.AsStream, format)
        {
            PcmSize = pcm_size
        };
    }
}
```

### Custom Audio Decoder

```csharp
public class MyAudioDecoder : SoundInput
{
    public override string SourceFormat { get { return "mau"; } }
    public override int   SourceBitrate { get { return  128000; } }

    private long m_position;

    public MyAudioDecoder (Stream input) : base (input)
    {
        // Initialize format
        this.Format = new WaveFormat
        {
            FormatTag        = 1,
            Channels         = 2,
            SamplesPerSecond = 44100,
            BitsPerSample    = 16
        };
        this.Format.SetBPS();

        // Calculate PCM size
        this.PcmSize = CalculateDecodedSize();
    }

    public override int Read (byte[] buffer, int offset, int count)
    {
        // Implement decoding logic
        return 0;
    }

    public override long Position
    {
        get { return m_position; }
        set { m_position = value; }
    }

    public override bool CanSeek { get { return false; } }
}
```

## Creating Video Format Plugins

### Basic Video Plugin Structure

```csharp
[Export (typeof(VideoFormat))]
public class MyVideoFormat : VideoFormat
{
    public override string         Tag { get { return "MVD"; } }
    public override string Description { get { return "My Video Format"; } }
    public override uint     Signature { get { return  0x44564D00; } }

    public override VideoMetaData ReadMetaData (IBinaryStream file)
    {
        var header = file.ReadHeader (0x30);
        if (!header.AsciiEqual (0, "MVD\0"))
            return null;

        return new VideoMetaData
        {
            Width           = header.ToUInt32 (4),
            Height          = header.ToUInt32 (8),
            Duration        = header.ToInt64  (12),
            FrameRate       = header.ToDouble (20),
            Codec           = "mvd",
            HasAudio        = header[28] != 0,
            CommonExtension = "mvd"
        };
    }

    public override VideoData Read (IBinaryStream file, VideoMetaData info)
    {
        // Return video data stream
        return new VideoData (file.AsStream, info);
    }
}
```

## Utility Classes and Helpers

### Stream Helpers

#### XoredStream - Simple XOR encryption
```csharp
var encrypted = arc.File.CreateStream (entry.Offset, entry.Size);
var decrypted = new XoredStream (encrypted, 0x5A);
```

#### ByteStringEncryptedStream - Multibyte XOR encryption
```csharp
byte[] key = { 0x12, 0x34, 0x56, 0x78 };
var encrypted = arc.File.CreateStream (entry.Offset, entry.Size);
var decrypted = new ByteStringEncryptedStream(encrypted, key);
```

#### AesStream - AES encryption
```csharp
byte[] key = { /* 16/24/32 bytes */ };
byte[] iv  = { /* 16 bytes */ };
var encrypted = arc.File.CreateStream (entry.Offset, entry.Size);
var decrypted = new AesStream (encrypted, key, iv, CryptoStreamMode.Read);
```

#### LimitStream - Limit stream size
```csharp
var limited = new LimitStream (input, maxBytes);
```

#### PrefixStream - Add header to stream
```csharp
byte[] header  = new byte[] { 0x42, 0x4D }; // Add BMP header
var withHeader = new PrefixStream (header, imageData);
```

### Auto-Detection Helpers

```csharp
// Auto-detect entry type
var name   = string.Format ("{0}#{1:D4}", base_name, i);
var entry  = AutoEntry.Create (file, offset_table[i], name);
entry.Size = size;

// Detect file type by signature
var resource = AutoEntry.DetectFileType (signature);
```

## Settings and Configuration

GARbro has three distinct types of configuration:

### 1. Developer-Configured Defaults (ResourceScheme)
Pre-defined settings that ship in `Formats.dat`:

```csharp
[Serializable]
public class MyFormatScheme : ResourceScheme
{
    // To make the scheme easily modifiable, it's best to avoid classes and 
    // interfaces and only use primitive types, dictionaries, and lists here
    public Dictionary<string, byte[]> KnownKeys;
}

    // In your format class
    static readonly MyFormatScheme DefaultScheme = new MyFormatScheme
    {
        KnownKeys = new Dictionary<string, byte[]>
        {
            // If no Formats.dat scheme is provided we'll take data from here
            { "GameTitle1", new byte[] { 0x12, 0x34, 0x56 } },
            { "GameTitle2", new byte[] { 0xAB, 0xCD, 0xEF } }
        }
    };
    
    public override ResourceScheme Scheme
    {
        get { return DefaultScheme; }
        set { 
            // This setter is ONLY called if:
            // 1. Formats.dat contains a scheme for your format tag
            // 2. The scheme value is not null
            DefaultScheme = (MyFormatScheme)value; 
        }
    }

```

### 2. Persistent User Preferences (IResourceSetting)
Settings that appear in the Preferences dialog (View → Preferences → Format Tag):

```csharp
public class MyArchiveFormat : ArchiveFormat
{
    // These will appear in the Preferences dialog under your format
    LocalResourceSetting DefaultEncoding = new LocalResourceSetting
    {
        Name = "MyFormatEncoding",
        Text = "Default Encoding",
        Description = "Text encoding to use for filenames",
        Value = Encodings.cp932
    };

    // Use in your format
    public override ArcFile TryOpen (ArcView file)
    {
        var encoding = DefaultEncoding.Value as Encoding;
        var name = file.View.ReadString (offset, length, encoding);
        // ...
    }

    // Expose to settings system
    public MyArchiveFormat ()
    {
        Settings = new[] { DefaultEncoding };
    }
}
```

### 3. Per-Operation Options (ResourceOptions)
Options users choose when creating/extracting specific archives:

```csharp
public class MyArchiveOptions : ResourceOptions
{
    public      string Password { get; set; }
    public int CompressionLevel { get; set; }
}

    // Provide GUI for options
    public override object GetAccessWidget ()
    {
        return new GUI.WidgetMyArchive();
    }
    
    // Use options in your format
    public override ArcFile TryOpen (ArcView file)
    {
        // ...
        var options = Query<MyArchiveOptions>(arcStrings.ArcEncryptedNotice);
        if (options == null || string.IsNullOrWhiteSpace (options.Password))
            return null;
        // ...
    }
    
    // Use them during creation
    public override void Create (Stream output, IEnumerable<Entry> list,
                               ResourceOptions options, EntryCallback callback)
    {
        var opts = options as MyArchiveOptions;
        // Use opts.Password, etc.
    }
```

## Best Practices

### 1. Format Validation
Always validate the parsed format using its signature, header data or internal parameters:
```csharp
if (!file.View.AsciiEqual (0, "MYSIGNATURE"))
    return null; // <- skip wrong files
    
uint width  = header.ToUInt32 (4);
uint height  = header.ToUInt32 (8);
if (width > 10000 || height > 10000) 
    return null;
```

### 2. Sanity Checks
Use built-in sanity checks for counts and sizes before enumerating something or creating arrays with them:
```csharp
if (!IsSaneCount (count))
    return null;
```

And verify all parameters:

```csharp
if (offset + size > file.MaxOffset)
    throw new InvalidFormatException(); // in case we are sure we handle the right format 
```

```csharp
if (offset + size > file.MaxOffset)
    return null; // in case the format may be something else
```

### 3. Debugging
Use `System.Diagnostics.Trace` for logging:  
```csharp
Trace.Write($"Processing entry: {entry.Name}", "[MyFormat]");
// ...
if (isOK)
    Trace.WriteLine($"OK", "[MyFormat]");
else
    Trace.WriteLine($"FAILED", "[MyFormat]");
```

### 4. Resource Management
Always properly dispose resources:
```csharp
public override Stream OpenEntry (ArcFile arc, Entry entry)
{
    var input = arc.File.CreateStream (entry.Offset, entry.Size);
    try
    {
        var output = new MyDecoder (input); // <- might throw
        return output; // not "using" here because it's owned by the caller
    }
    catch
    {
        input.Dispose(); // dispose in case of exception
        throw;
    }
}
```

### 5. Other
Handle edge cases:  
```csharp
if (entry.Size == 0)
    return Stream.Null;
```

### 6. Encodings 
Specify encodings explicitly:  
```csharp
// Specify encoding for game-specific text
// Japanese Encoding is built-in as Encodings.cp932
var name = file.View.ReadString (offset, 256, Encodings.cp932);

// Or use format-specific encoding setting
var encoding = MyFormatEncoding.Value as Encoding;
var text = input.ReadCString (encoding);
```

### 7. Callbacks
Use callbacks for long archive creation operations:
```csharp
public override void Create (Stream output, IEnumerable<Entry> list,
                           ResourceOptions options, EntryCallback callback)
{
    int i = 0;
    foreach (var entry in list)
    {
        if (callback != null)
            callback (i++, entry, "Packing");
        // Pack entry
    }
}
```

## Conclusion

For additional examples and reference implementations, examine the existing format plugins in the [GARbro](/../../tree/main/ArcFormats/Formats) source code repository.