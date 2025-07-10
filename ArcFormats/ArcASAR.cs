//! \file       ArcASAR.cs
//! \date       2025 Jul 10
//! \brief      Electron ASAR archive format.
//
// Copyright (C) 2015-2025 by morkt and others
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

using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Web.Script.Serialization;
using GameRes.Utility;

namespace GameRes.Formats.Electron
{
    internal class AsarEntry : Entry
    {
        public bool IsUnpacked { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class AsarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASAR"; } }
        public override string Description { get { return "Electron ASAR archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AsarOpener ()
        {
            Extensions = new string[] { "asar" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 24)
                return null;

            int header_size = file.View.ReadInt32 (0);
            if (header_size != 4)
                return null;

            uint json_length = file.View.ReadUInt32(4) - 8;
            var json_data = file.View.ReadBytes (16, json_length);

            byte zeros = 0;
            while (json_length > zeros + 1 && json_data[json_length - zeros - 1] == 0)
                zeros++;

            string json_string;
            try
            {
                json_string = Encoding.UTF8.GetString (json_data, 0, (int)json_length - zeros);
            }
            catch
            {
                return null;
            }

            IDictionary root;
            try
            {
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = (int)json_length * 2;
                root = serializer.DeserializeObject (json_string) as IDictionary;
            }
            catch
            {
                return null;
            }

            if (root == null || !root.Contains ("files"))
                return null;

            var dir = new List<Entry>();
            long base_offset = 16 + json_length;
            var files_obj = root["files"] as IDictionary;
            if (files_obj == null)
                return null;

            ParseDirectory (files_obj, "", dir, base_offset);
            if (dir.Count == 0)
                return null;

            return new ArcFile (file, this, dir);
        }

        private void ParseDirectory (IDictionary files, string path, List<Entry> dir, long base_offset)
        {
            foreach (DictionaryEntry item in files)
            {
                string name = item.Key as string;
                if (name == null) continue;

                var value = item.Value as IDictionary;
                if (value == null) continue;

                string full_path = string.IsNullOrEmpty (path) ? name : path + "/" + name;

                if (value.Contains ("files"))
                {
                    var subfiles = value["files"] as IDictionary;
                    if (subfiles != null)
                        ParseDirectory (subfiles, full_path, dir, base_offset);
                }
                else // it's a file, yep
                {
                    var entry = new AsarEntry { Name = full_path };

                    if (value.Contains ("size"))
                    {
                        entry.Size = Convert.ToUInt32 (value["size"]);
                    }

                    if (value.Contains ("offset"))
                    {
                        string offset_str = value["offset"] as string;
                        entry.Offset = base_offset + Convert.ToInt64 (offset_str != null ? offset_str : value["offset"]);
                    }

                    if (value.Contains ("unpacked"))
                    {
                        entry.IsUnpacked = Convert.ToBoolean (value["unpacked"]);
                    }

                    entry.Type = FormatCatalog.Instance.GetTypeFromName (full_path);

                    dir.Add (entry);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var asar_entry = entry as AsarEntry;
            if (asar_entry != null && asar_entry.IsUnpacked)
            {
                string unpacked_path = arc.File.Name + ".unpacked";
                string file_path = Path.Combine (unpacked_path, entry.Name.Replace ('/', Path.DirectorySeparatorChar));
                if (File.Exists (file_path))
                {
                    return new FileStream (file_path, FileMode.Open, FileAccess.Read);
                }
                else
                {
                    throw new FileNotFoundException ($"Unpacked file not found: {file_path}");
                }
            }

            return base.OpenEntry (arc, entry);
        }
    }
}
