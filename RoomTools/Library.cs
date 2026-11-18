/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoomTools
{
    /// <summary>
    /// The library: rooms and lots saved as files of their own, in Library/Rooms and Library/Lots beside Simitone's
    /// saves. A file can be copied to another player's library as it is; the objects in it are found by their ids, so
    /// downloads in it need to be installed there too.
    /// </summary>
    public static class Library
    {
        public class Entry
        {
            public string Path;
            public Blueprint.Header Header;
            public string Name => Header.Name;
            public bool IsLot => Header.IsLot;
        }

        public static string Folder => System.IO.Path.Combine(FSOEnvironment.UserDir, "Library");
        public static string RoomsFolder => System.IO.Path.Combine(Folder, "Rooms");
        public static string LotsFolder => System.IO.Path.Combine(Folder, "Lots");

        private static List<Entry> Cached;
        private static DateTime CachedAt;

        /// <summary>Everything in the library, newest first: rooms, then lots. Files that can't be read are skipped.</summary>
        public static List<Entry> All(bool fresh = false)
        {
            if (!fresh && Cached != null && (DateTime.UtcNow - CachedAt).TotalSeconds < 2) return Cached;
            var result = new List<Entry>();
            foreach (var (dir, ext) in new[] { (RoomsFolder, Blueprint.RoomExtension), (LotsFolder, Blueprint.LotExtension) })
            {
                var found = new List<Entry>();
                try
                {
                    Directory.CreateDirectory(dir);
                    foreach (var file in Directory.GetFiles(dir, "*" + ext))
                    {
                        try
                        {
                            using (var stream = File.OpenRead(file))
                                found.Add(new Entry { Path = file, Header = Blueprint.ReadHeader(stream) });
                        }
                        catch (Exception e)
                        {
                            RoomToolsPack.Log("Skipped " + System.IO.Path.GetFileName(file) + " in the library: " + e.Message);
                        }
                    }
                }
                catch (Exception e)
                {
                    RoomToolsPack.Log("Could not read the library folder " + dir + ": " + e.Message);
                }
                result.AddRange(found.OrderByDescending(x => x.Header.Saved));
            }
            Cached = result;
            CachedAt = DateTime.UtcNow;
            return result;
        }

        public static Blueprint Load(Entry entry)
        {
            using (var stream = File.OpenRead(entry.Path)) return Blueprint.Read(stream);
        }

        /// <summary>Save a room or a lot to the library, under its name. Returns the file.</summary>
        public static string Save(Blueprint b)
        {
            var dir = b.IsLot ? LotsFolder : RoomsFolder;
            Directory.CreateDirectory(dir);
            var stem = new string((b.Name ?? "").Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_').ToArray()).Trim();
            if (stem.Length == 0) stem = b.IsLot ? "Lot" : "Room";
            if (stem.Length > 48) stem = stem.Substring(0, 48);
            var ext = b.IsLot ? Blueprint.LotExtension : Blueprint.RoomExtension;
            var path = System.IO.Path.Combine(dir, stem + ext);
            for (int i = 2; File.Exists(path); i++) path = System.IO.Path.Combine(dir, stem + " " + i + ext);
            var temp = path + ".part";
            using (var stream = File.Create(temp)) b.Write(stream);
            File.Move(temp, path);
            Cached = null;
            return path;
        }

        public static void Delete(Entry entry)
        {
            File.Delete(entry.Path);
            Cached = null;
        }

        /// <summary>A name not yet used in the library, for something new: "Room", "Room 2"...</summary>
        public static string FreshName(string stem, bool lot)
        {
            var names = new HashSet<string>(All(true).Where(x => x.IsLot == lot).Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(stem)) return stem;
            for (int i = 2; ; i++) if (!names.Contains(stem + " " + i)) return stem + " " + i;
        }
    }
}
