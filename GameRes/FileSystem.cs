//! \file       FileSystem.cs
//! \date       Fri Jun 05 15:32:27 2015
//! \brief      Gameres file system abstraction.
//
// Copyright (C) 2015 by morkt
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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameRes.Strings;

namespace GameRes
{
    public interface IFileSystem : IDisposable
    {
        /// <summary>
        /// Returns entry corresponding to the given filename within filesystem.
        /// </summary>
        /// <exception cref="FileNotFoundException">File is not found.</exception>
        Entry FindFile (string filename);

        /// <summary>
        /// Open file for reading as stream.
        /// </summary>
        Stream OpenStream (Entry entry);

        Stream OpenSeekableStream (Entry entry);

        /// <summary>
        /// Open file for reading as memory-mapped view.
        /// </summary>
        ArcView OpenView (Entry entry);

        /// <summary>
        /// Enumerates subdirectories and files in current directory.
        /// </summary>
        IEnumerable<Entry> GetFiles ();

        /// <summary>
        /// Recursively enumerates files in the current directory and its subdirectories.
        /// Subdirectory entries are omitted from resulting set.
        /// </summary>
        IEnumerable<Entry> GetFilesRecursive ();

        string CurrentDirectory { get; set; }
    }

    public class SubDirEntry : Entry
    {
        public override string Type  { get { return "directory"; } }

        public SubDirEntry (string name)
        {
            Name = name;
            Size = 0;
        }
    }

    public sealed class PhysicalFileSystem : IFileSystem
    {
        public string CurrentDirectory
        {
            get { return Directory.GetCurrentDirectory(); }
            set { Directory.SetCurrentDirectory (value); }
        }

        public Entry FindFile (string filename)
        {
            var attr = File.GetAttributes (filename);
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                return new SubDirEntry (filename);
            else
                return EntryFromFileInfo (new FileInfo (filename));
        }

        public IEnumerable<Entry> GetFiles ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var subdir in info.EnumerateDirectories())
            {
                if (0 != (subdir.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                yield return new SubDirEntry (subdir.FullName);
            }
            foreach (var file in info.EnumerateFiles())
            {
                if (0 != (file.Attributes & FileAttributes.System))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        public IEnumerable<Entry> GetFilesRecursive ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var file in info.EnumerateFiles ("*", SearchOption.AllDirectories))
            {
                if (0 != (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        private Entry EntryFromFileInfo (FileInfo file)
        {
            var entry = FormatCatalog.Instance.Create<Entry> (file.FullName);
            entry.Size = (uint)Math.Min (file.Length, uint.MaxValue);
            return entry;
        }

        public Stream OpenStream (Entry entry)
        {
            return File.OpenRead (entry.Name);
        }

        public Stream OpenSeekableStream (Entry entry)
        {
            return OpenStream (entry);
        }

        public ArcView OpenView (Entry entry)
        {
            return new ArcView (entry.Name);
        }

        public void Dispose ()
        {
            GC.SuppressFinalize (this);
        }
    }

    public class FlatArchiveFileSystem : IFileSystem
    {
        protected readonly ArcFile                      m_arc;
        protected readonly Dictionary<string, Entry>    m_dir;

        public ArcFile Source { get { return m_arc; } }

        public virtual string CurrentDirectory
        {
            get { return ""; }
            set
            {
                if (string.IsNullOrEmpty (value))
                    return;
                if (".." == value || "." == value)
                    return;
                if ("\\" == value || "/" == value)
                    return;
                throw new DirectoryNotFoundException();
            }
        }

        public FlatArchiveFileSystem (ArcFile arc)
        {
            m_arc = arc;
            m_dir = new Dictionary<string, Entry> (arc.Dir.Count, StringComparer.InvariantCultureIgnoreCase);
            foreach (var entry in arc.Dir)
            {
                m_dir.Add (entry.Name, entry);
            }
        }

        public Stream OpenStream (Entry entry)
        {
            return m_arc.OpenEntry (entry);
        }

        public Stream OpenSeekableStream (Entry entry)
        {
            return m_arc.OpenSeekableEntry (entry);
        }

        public ArcView OpenView (Entry entry)
        {
            return m_arc.OpenView (entry);
        }

        public virtual Entry FindFile (string filename)
        {
            Entry entry = null;
            if (!m_dir.TryGetValue (filename, out entry))
                throw new FileNotFoundException();
            return entry;
        }

        public virtual IEnumerable<Entry> GetFiles ()
        {
            return m_arc.Dir;
        }

        public virtual IEnumerable<Entry> GetFilesRecursive ()
        {
            return m_arc.Dir;
        }

        #region IDisposable Members
        bool _arc_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_arc_disposed)
            {
                if (disposing)
                {
                    m_arc.Dispose();
                }
                _arc_disposed = true;
            }
        }
        #endregion
    }

    public class ArchiveFileSystem : FlatArchiveFileSystem
    {
        private string  m_cwd;

        private string PathDelimiter { get; set; }

        private static readonly char[] m_path_delimiters = { '/', '\\' };

        public ArchiveFileSystem (ArcFile arc) : base (arc)
        {
            m_cwd = "";
            PathDelimiter = "/";
        }

        public override string CurrentDirectory
        {
            get { return m_cwd; }
            set { ChDir (value); }
        }

        public override Entry FindFile (string filename)
        {
            Entry entry = null;
            if (m_dir.TryGetValue (filename, out entry))
                return entry;
            if (m_dir.Keys.Any (n => n.StartsWith (filename + PathDelimiter)))
                return new SubDirEntry (filename);
            throw new FileNotFoundException();
        }

        static readonly Regex path_re = new Regex (@"\G[/\\]?([^/\\]+)([/\\])");

        public override IEnumerable<Entry> GetFiles ()
        {
            var root_dir = "";
            IEnumerable<Entry> dir = m_arc.Dir;
            if (!string.IsNullOrEmpty (m_cwd))
            {
                root_dir = m_cwd+PathDelimiter;
                dir = from entry in dir
                      where entry.Name.StartsWith (root_dir)
                      select entry;
                if (!dir.Any())
                {
                    throw new DirectoryNotFoundException();
                }
            }
            var subdirs = new HashSet<string>();
            foreach (var entry in dir)
            {
                var match = path_re.Match (entry.Name, root_dir.Length);
                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    if (subdirs.Add (name))
                    {
                        PathDelimiter = match.Groups[2].Value;
                        yield return new SubDirEntry (root_dir+name);
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }

        public override IEnumerable<Entry> GetFilesRecursive ()
        {
            if (0 == m_cwd.Length)
                return m_arc.Dir;
            else
                return from file in m_arc.Dir
                       where file.Name.StartsWith (m_cwd + PathDelimiter)
                       select file;
        }

        private void ChDir (string path)
        {
            if (string.IsNullOrEmpty (path))
                return;
            var cur_dir = new List<string>();
            if (-1 != Array.IndexOf (m_path_delimiters, path[0]))
            {
                path = path.TrimStart (m_path_delimiters);
            }
            else if (!string.IsNullOrEmpty (m_cwd))
            {
                cur_dir.AddRange (m_cwd.Split (m_path_delimiters));
            }
            var path_list = path.Split (m_path_delimiters);
            foreach (var dir in path_list)
            {
                if ("." == dir)
                {
                    continue;
                }
                else if (".." == dir)
                {
                    if (0 == cur_dir.Count)
                        continue;
                    cur_dir.RemoveAt (cur_dir.Count-1);
                }
                else
                {
                    cur_dir.Add (dir);
                }
            }
            string new_path = string.Join (PathDelimiter, cur_dir);
            if (0 != new_path.Length)
            {
                var entry = m_arc.Dir.FirstOrDefault (e => e.Name.StartsWith (new_path + PathDelimiter));
                if (null == entry)
                    throw new DirectoryNotFoundException();
            }
            m_cwd = new_path;
        }
    }

    public sealed class FileSystemStack : IDisposable
    {
        Stack<IFileSystem> m_fs_stack = new Stack<IFileSystem>();
        Stack<string> m_arc_name_stack = new Stack<string>();

        public IEnumerable<IFileSystem> All { get { return m_fs_stack; } }

        public IFileSystem Top { get { return m_fs_stack.Peek(); } }
        public int       Count { get { return m_fs_stack.Count; } }
        public IEnumerable<string> ArcStack { get { return m_arc_name_stack; } }

        public ArcFile      CurrentArchive { get; private set; }
        private IFileSystem LastVisitedArc { get; set; }
        private string     LastVisitedPath { get; set; }

        public FileSystemStack ()
        {
            m_fs_stack.Push (new PhysicalFileSystem());
        }

        public void ChDir (Entry entry)
        {
            if (entry is SubDirEntry)
            {
                if (1 == m_fs_stack.Count)
                {
                    Top.CurrentDirectory = entry.Name;
                    return;
                }
                if (".." == entry.Name && string.IsNullOrEmpty (Top.CurrentDirectory))
                {
                    Pop();
                    return;
                }
                Top.CurrentDirectory = entry.Name;
                return;
            }
            if (entry.Name == LastVisitedPath && null != LastVisitedArc)
            {
                Push (LastVisitedPath, LastVisitedArc);
                if (LastVisitedArc is FlatArchiveFileSystem)
                    CurrentArchive = (LastVisitedArc as FlatArchiveFileSystem).Source;
                return;
            }
            Flush();
            var arc = ArcFile.TryOpen (entry.Name);
            if (null == arc)
                throw new UnknownFormatException();
            try
            {
                Push (entry.Name, arc.CreateFileSystem());
                CurrentArchive = arc;
            }
            catch
            {
                arc.Dispose();
                throw;
            }
        }

        private void Push (string path, IFileSystem fs)
        {
            m_fs_stack.Push (fs);
            m_arc_name_stack.Push (path);
        }

        private void Pop ()
        {
            if (m_fs_stack.Count > 1)
            {
                Flush();
                LastVisitedArc = m_fs_stack.Pop();
                LastVisitedPath = m_arc_name_stack.Pop();
                if (m_fs_stack.Count > 1 && m_fs_stack.Peek() is FlatArchiveFileSystem)
                    CurrentArchive = (m_fs_stack.Peek() as FlatArchiveFileSystem).Source;
                else
                    CurrentArchive = null;
            }
        }

        public void Flush ()
        {
            if (LastVisitedArc != null)
            {
                LastVisitedArc.Dispose();
                LastVisitedArc = null;
                LastVisitedPath = null;
            }
        }

        private bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                Flush();
                foreach (var fs in m_fs_stack.Reverse())
                    fs.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }

    public static class VFS
    {
        private static FileSystemStack m_vfs = new FileSystemStack();

        public static IFileSystem Top { get { return m_vfs.Top; } }
        public static bool  IsVirtual { get { return m_vfs.Count > 1; } }
        public static  int      Count { get { return m_vfs.Count; } }

        public static ArcFile CurrentArchive { get { return m_vfs.CurrentArchive; } }

        private static string[] m_top_path = new string[1];

        public static IEnumerable<string> FullPath
        {
            get
            {
                m_top_path[0] = Top.CurrentDirectory;
                if (1 == Count)
                    return m_top_path;
                else
                    return m_vfs.ArcStack.Reverse().Concat (m_top_path);
            }
            set
            {
                if (!value.Any())
                    return;
                var new_vfs = new FileSystemStack();
                var desired = value.ToArray();
                for (int i = 0; i < desired.Length-1; ++i)
                    new_vfs.ChDir (new_vfs.Top.FindFile (desired[i]));
                new_vfs.Top.CurrentDirectory = desired.Last();
                m_vfs.Dispose();
                m_vfs = new_vfs;
            }
        }

        public static Entry FindFile (string filename)
        {
            if (".." == filename)
                return new SubDirEntry ("..");
            return m_vfs.Top.FindFile (filename);
        }

        public static Stream OpenStream (Entry entry)
        {
            return m_vfs.Top.OpenStream (entry);
        }

        public static Stream OpenSeekableStream (Entry entry)
        {
            return m_vfs.Top.OpenSeekableStream (entry);
        }

        public static ArcView OpenView (Entry entry)
        {
            return m_vfs.Top.OpenView (entry);
        }

        public static void ChDir (Entry entry)
        {
            m_vfs.ChDir (entry);
        }

        public static void ChDir (string path)
        {
            m_vfs.ChDir (FindFile (path));
        }

        public static void Flush ()
        {
            m_vfs.Flush();
        }
        
        public static IEnumerable<Entry> GetFiles ()
        {
            return m_vfs.Top.GetFiles();
        }
    }

    public class UnknownFormatException : FileFormatException
    {
        public UnknownFormatException () : base (garStrings.MsgUnknownFormat) { }
    }
}
