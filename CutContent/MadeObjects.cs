/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CutContent
{
    /// <summary>
    /// Objects the pack makes in memory (the Information Kiosk, the pickpocket): an object definition and anything else it
    /// needs (scripts, texts) added to one of the game's own files. The game may load that file again later, from the disk,
    /// without them: when it then looks the object up and doesn't find it, they are put back.
    /// </summary>
    public static class MadeObjects
    {
        private class Made
        {
            public uint Guid, LikeGuid; //the object, and one of the file's own, to find the file by
            public OBJD Definition;
            public List<IffChunk> Chunks;
        }

        private static readonly Dictionary<uint, Made> ByGuid = new Dictionary<uint, Made>();

        /// <summary>Adds the chunks to the file, and remembers them to put back.</summary>
        public static void Add(GameObjectResource res, uint likeGuid, OBJD definition, List<IffChunk> chunks)
        {
            var made = new Made { Guid = definition.GUID, LikeGuid = likeGuid, Definition = definition, Chunks = chunks };
            PutBack(res, made);
            ByGuid[made.Guid] = made;
            if (Content.Get().WorldObjects is FSO.Content.Interfaces.AbstractObjectProvider objects)
                lock (objects.Entries) if (!objects.Entries.ContainsKey(made.Guid)) objects.AddObject(res.MainIff, definition);
        }

        /// <summary>After the game looks up an object: if it is one of these and its file was loaded again without it, put it back.</summary>
        public static void AfterGet(ulong id, ref GameObject __result)
        {
            if (__result != null || !ByGuid.TryGetValue((uint)id, out var made)) return;
            try
            {
                var res = Content.Get().WorldObjects.Get(made.LikeGuid)?.Resource;
                if (res?.MainIff == null) return;
                PutBack(res, made);
                __result = new GameObject { GUID = made.Guid, OBJ = made.Definition, Resource = res };
                CutContentPack.Log($"made objects: {made.Definition.ChunkLabel} put back in {res.MainIff.Filename}");
            }
            catch (Exception e) { CutContentPack.Log("made objects: could not put one back: " + e.Message); }
        }

        private static void PutBack(GameObjectResource res, Made made)
        {
            var iff = res.MainIff;
            foreach (var chunk in made.Chunks)
            {
                bool there;
                switch (chunk)
                {
                    case OBJD o: there = (iff.List<OBJD>() ?? new List<OBJD>()).Any(x => x.GUID == o.GUID); break;
                    case BHAV b: there = iff.Get<BHAV>(b.ChunkID) == b; break;
                    case STR s: there = iff.Get<STR>(s.ChunkID) == s; break;
                    default: there = false; break;
                }
                if (there) continue;
                iff.AddChunk(chunk);
                if (chunk is BHAV bhav) Scripts.Reassemble(res, bhav);
            }
        }

        /// <summary>A copy of an object definition, written out and read in again, so its raw values (which scripts read) agree with its fields.</summary>
        public static OBJD Copy(IffFile iff, OBJD from)
        {
            byte[] bytes;
            using (var stream = new MemoryStream()) { from.Write(iff, stream); bytes = stream.ToArray(); }
            var copy = new OBJD { ChunkID = from.ChunkID, ChunkLabel = from.ChunkLabel, ChunkType = "OBJD", ChunkFlags = from.ChunkFlags };
            using (var stream = new MemoryStream(bytes)) copy.Read(iff, stream);
            copy.ChunkProcessed = true;
            copy.AddedByPatch = true;
            return copy;
        }

        /// <summary>A text list of the pack's own, ready to add to a file.</summary>
        public static STR Texts(ushort id, string label, IEnumerable<string> lines)
        {
            var text = new STR { ChunkID = id, ChunkLabel = label, ChunkType = "STR#", ChunkProcessed = true, AddedByPatch = true };
            text.LanguageSets[0].Strings = lines.Select(x => new STRItem { Value = x, Comment = "" }).ToArray();
            return text;
        }

        /// <summary>A text list number the file doesn't use, from the one given up.</summary>
        public static ushort FreeTextID(GameObjectResource res, ushort from)
        {
            var id = from;
            while (res.Get<STR>(id) != null) id++;
            return id;
        }
    }
}
