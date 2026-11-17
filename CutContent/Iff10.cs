/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CutContent
{
    /// <summary>
    /// Reads files in the form Maxis used before The Sims came out, which the engine doesn't open. They begin
    /// "IFF FILE 1.0:TYPE FOLLOWED BY SIZE. JAMIE DOORNBOS &amp; MAXIS 1996." (64 bytes); every file of the 1999 build is one.
    ///
    /// Worked out from the 1999 build's own files:
    ///   - after the 64-byte header come the chunks, one after another;
    ///   - a chunk starts with 16 bytes: its type (4 letters), its size counting these 16 bytes, its number and flags
    ///     (2 bytes each) and the number of its name (4 bytes), all with the largest byte first; its data follows;
    ///   - names are not in the chunks but in a table of them: a chunk holding, from its 13th byte, the number of a name
    ///     (4 bytes, smallest byte first) and the name (a length byte, then the letters), again and again;
    ///   - "XXXX" chunks are deleted ones, left in the file with their old data;
    ///   - text lists start with how many texts there are (2 bytes, largest byte first), then each text (a length byte,
    ///     then the letters); some are already in the later forms (FF FF, FE FF), which the engine reads;
    ///   - scripts are already in the engine's oldest form (0x8000); menu tables are version 2, which it doesn't read.
    /// The data the engine can read is passed to it rewritten in the later form, with the names put back in each chunk;
    /// the menu tables are read here.
    /// </summary>
    public static class Iff10
    {
        public class Chunk
        {
            public string Type;
            public ushort Id, Flags;
            public uint LabelId;
            public string Label = "";
            public int Offset;
            public byte[] Data; //after the 16-byte start
            public byte[] Body; //after the type, size, number and flags: the name's number and the data
            public bool IsNames;
            public bool Deleted => Type == "XXXX";
        }

        public class File
        {
            public List<Chunk> Chunks = new List<Chunk>();
            public Dictionary<uint, string> Names = new Dictionary<uint, string>();
            public List<string> NamesFrom = new List<string>();
            public string Problem;
            public IEnumerable<Chunk> Live => Chunks.Where(x => !x.Deleted && !x.IsNames);
            public IEnumerable<Chunk> Of(string type) => Live.Where(x => x.Type == type);
        }

        public static bool Is(byte[] data) => data != null && data.Length >= 64 && Encoding.ASCII.GetString(data, 0, 13) == "IFF FILE 1.0:";

        public static File Read(byte[] data)
        {
            var file = new File();
            var at = 64;
            while (at + 12 <= data.Length)
            {
                var type = Encoding.ASCII.GetString(data, at, 4);
                var size = (long)BE32(data, at + 4);
                if (size < 12 || at + size > data.Length)
                {
                    file.Problem = $"the chunk at byte {at} (\"{Printable(type)}\") says it is {size} bytes long, which goes past the end of the file ({data.Length} bytes): the rest was not read";
                    break;
                }
                var n = (int)size;
                file.Chunks.Add(new Chunk
                {
                    Type = type,
                    Id = BE16(data, at + 8),
                    Flags = BE16(data, at + 10),
                    LabelId = n >= 16 ? BE32(data, at + 12) : uint.MaxValue,
                    Offset = at,
                    Body = Slice(data, at + 12, n - 12),
                    Data = n >= 16 ? Slice(data, at + 16, n - 16) : new byte[0],
                });
                at += n;
            }
            if (file.Problem == null && at < data.Length) file.Problem = $"the last {data.Length - at} bytes are not a whole chunk";

            //the tables of names: deleted ones first, so that a live table's names win
            foreach (var deleted in new[] { true, false })
                foreach (var chunk in file.Chunks.Where(x => x.Deleted == deleted && !ForEngine.Contains(x.Type)))
                {
                    var names = Names(chunk.Body);
                    if (names == null) continue;
                    chunk.IsNames = true;
                    foreach (var name in names) file.Names[name.Key] = name.Value;
                    file.NamesFrom.Add($"\"{Printable(chunk.Type)}\" {chunk.Id}{(deleted ? " (deleted)" : "")}, {names.Count} names");
                }
            foreach (var chunk in file.Chunks)
                if (file.Names.TryGetValue(chunk.LabelId, out var label)) chunk.Label = label;
            return file;
        }

        /// <summary>A table of names: (number, smallest byte first; a length byte; the letters), filling the chunk exactly.</summary>
        private static Dictionary<uint, string> Names(byte[] body)
        {
            var names = new Dictionary<uint, string>();
            int at = 0, empty = 0;
            while (at < body.Length)
            {
                if (at + 5 > body.Length) return null;
                var id = BitConverter.ToUInt32(body, at);
                var length = body[at + 4];
                at += 5;
                if (at + length > body.Length) return null;
                for (int i = at; i < at + length; i++) if (body[i] < 32 || body[i] == 127) return null;
                if (length == 0) empty++;
                names[id] = Latin1(body, at, length);
                at += length;
            }
            if (names.Count < 2 || empty * 2 > names.Count) return null;
            return names;
        }

        // ---- texts ----

        /// <summary>
        /// The texts of a list, in any of its forms: the old one (how many, largest byte first, then each with a length
        /// byte), or the later ones the engine reads (00 00, FF FF, FE FF, FD FF). Null when it is none of them.
        /// </summary>
        public static List<string> Strings(byte[] d, out string form)
        {
            form = "empty";
            if (d == null || d.Length < 2) return new List<string>();
            var code = (short)(d[0] | (d[1] << 8));
            List<string> list;
            if (code == -1 || code == -2 || code == -3)
            {
                form = code == -1 ? "FF FF" : code == -2 ? "FE FF" : "FD FF";
                if (d.Length < 4) return new List<string>();
                return Terminated(d, LE16(d, 2), code);
            }
            if (Pascal(d, 2, BE16(d, 0), true, out list)) { form = "old"; return list; }
            if (code == 0 && d.Length >= 4 && Pascal(d, 4, LE16(d, 2), true, out list)) { form = "00 00"; return list; }
            if (Pascal(d, 2, BE16(d, 0), false, out list)) { form = "old, with bytes left over"; return list; }
            form = $"unknown ({d[0]:x2} {d[1]:x2})";
            return null;
        }

        private static bool Pascal(byte[] d, int at, int count, bool exact, out List<string> list)
        {
            list = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (at >= d.Length) return false;
                var length = d[at++];
                if (at + length > d.Length) return false;
                list.Add(Latin1(d, at, length));
                at += length;
            }
            return !exact || d.Length - at <= 1;
        }

        private static List<string> Terminated(byte[] d, int count, short code)
        {
            var list = new List<string>();
            var at = 4;
            string Next()
            {
                var end = at;
                while (end < d.Length && d[end] != 0) end++;
                var s = Latin1(d, at, end - at);
                at = Math.Min(d.Length, end + 1);
                return s;
            }
            for (int i = 0; i < count && at < d.Length; i++)
            {
                if (code == -3) at++; //language
                var value = Next();
                if (code != -1) Next(); //the note that goes with it
                list.Add(value);
            }
            return list;
        }

        // ---- menu tables ----

        public class Entry
        {
            public ushort Action, Test, Field;
            public float Attenuation;
            public short[] Ads;
        }

        /// <summary>
        /// A menu table older than the engine reads. Version 2 (the 1999 build's): how many entries and the version (2 bytes
        /// each), then per entry the script it runs and the one that tests whether it shows (2 bytes each), how many needs it
        /// advertises to (2 bytes), 2 bytes not yet known, the attenuation (a 4-byte number), and what it advertises to each
        /// need (2 bytes each), in the order of the needs' numbers. There is no "personality" scaling yet.
        /// </summary>
        public static List<Entry> Table(byte[] d, out int version, out string problem)
        {
            version = -1;
            problem = null;
            var list = new List<Entry>();
            if (d.Length < 2) return list;
            var count = LE16(d, 0);
            if (count == 0) return list;
            if (d.Length < 4) { problem = "too short"; return list; }
            version = LE16(d, 2);
            var at = 4;
            for (int i = 0; i < count; i++)
            {
                if (at + 12 > d.Length) { problem = $"entry {i} would go past the end"; return list; }
                var entry = new Entry
                {
                    Action = LE16(d, at),
                    Test = LE16(d, at + 2),
                    Field = LE16(d, at + 6),
                    Attenuation = BitConverter.ToSingle(d, at + 8),
                };
                var ads = LE16(d, at + 4);
                at += 12;
                if (ads > 64 || at + ads * 2 > d.Length) { problem = $"entry {i} says it advertises to {ads} needs, which doesn't fit"; return list; }
                entry.Ads = Enumerable.Range(0, ads).Select(j => (short)LE16(d, at + j * 2)).ToArray();
                at += ads * 2;
                list.Add(entry);
            }
            if (at != d.Length) problem = $"{d.Length - at} bytes left over";
            return list;
        }

        // ---- for the engine ----

        /// <summary>The kinds of data the engine can read once they are in the later form.</summary>
        private static readonly HashSet<string> ForEngine = new HashSet<string> { "BHAV", "STR#", "BCON", "OBJD", "TTAB", "TTAs", "CTSS", "CARR" };

        /// <summary>
        /// The file rewritten in the later form (IFF 2.5), for the engine's own reader: each chunk with its name put back in,
        /// and old text lists rewritten in a form the engine reads. Only scripts, texts, constants, objects and menu tables are
        /// kept; deleted chunks and the tables of names are left out.
        /// </summary>
        public static byte[] ToIff25(File file)
        {
            using (var stream = new MemoryStream())
            {
                var header = new byte[64];
                Encoding.ASCII.GetBytes("IFF FILE 2.5:TYPE FOLLOWED BY SIZE\0 JAMIE DOORNBOS & MAXIS 1").CopyTo(header, 0);
                stream.Write(header, 0, header.Length); //the last 4 bytes: no resource map
                foreach (var chunk in file.Live.Where(x => ForEngine.Contains(x.Type)))
                {
                    var data = chunk.Data;
                    if (chunk.Type == "STR#" || chunk.Type == "CTSS" || chunk.Type == "TTAs") data = Rewrite(data);
                    var start = new byte[76];
                    Encoding.ASCII.GetBytes(chunk.Type).CopyTo(start, 0);
                    PutBE32(start, 4, (uint)(76 + data.Length));
                    start[8] = (byte)(chunk.Id >> 8); start[9] = (byte)chunk.Id;
                    start[10] = (byte)(chunk.Flags >> 8); start[11] = (byte)chunk.Flags;
                    var label = Encoding.ASCII.GetBytes(new string((chunk.Label ?? "").Select(c => c < 128 ? c : '?').Take(63).ToArray()));
                    label.CopyTo(start, 12);
                    stream.Write(start, 0, start.Length);
                    stream.Write(data, 0, data.Length);
                }
                return stream.ToArray();
            }
        }

        /// <summary>An old text list in the later FF FF form (each text ending in a zero byte); the later forms are kept as they are.</summary>
        private static byte[] Rewrite(byte[] d)
        {
            var list = Strings(d, out var form);
            if (list == null || !form.StartsWith("old")) return d;
            using (var stream = new MemoryStream())
            {
                stream.WriteByte(0xFF); stream.WriteByte(0xFF);
                stream.WriteByte((byte)list.Count); stream.WriteByte((byte)(list.Count >> 8));
                foreach (var s in list)
                {
                    var bytes = Encoding.UTF8.GetBytes(s);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.WriteByte(0);
                }
                return stream.ToArray();
            }
        }

        // ---- bytes ----

        public static ushort BE16(byte[] d, int at) => (ushort)((d[at] << 8) | d[at + 1]);
        public static uint BE32(byte[] d, int at) => ((uint)d[at] << 24) | ((uint)d[at + 1] << 16) | ((uint)d[at + 2] << 8) | d[at + 3];
        public static ushort LE16(byte[] d, int at) => (ushort)(d[at] | (d[at + 1] << 8));
        private static void PutBE32(byte[] d, int at, uint v) { d[at] = (byte)(v >> 24); d[at + 1] = (byte)(v >> 16); d[at + 2] = (byte)(v >> 8); d[at + 3] = (byte)v; }
        private static byte[] Slice(byte[] d, int at, int count) { var r = new byte[Math.Max(0, count)]; Array.Copy(d, at, r, 0, r.Length); return r; }
        private static string Latin1(byte[] d, int at, int count) => Encoding.Latin1.GetString(d, at, count);
        public static string Printable(string s) => new string((s ?? "").Select(c => (c >= 32 && c < 127) ? c : '.').ToArray());
    }
}
