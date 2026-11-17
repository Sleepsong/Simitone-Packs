/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// Superstar's Information Kiosk is in the game's files, but without what buy mode needs to show it. Its catalog text,
    /// "Information Kiosk" ("Information is a valuable commodity... the kiosk as provided is information free, but it does
    /// make a lovely addition to any corporate area"), is text 2005 of SculptureMovie.iff, among Superstar's film-set
    /// sculptures; the object that uses it has no "kiosk" in its own name. So it is found by its catalog text: any object
    /// whose catalog text is called "Information Kiosk", in a file of sculptures (or with "kiosk" in its name), that buy
    /// mode leaves out is given a catalog entry, in memory, filling in only the details that are missing: Miscellaneous if
    /// it has no category, every room and lot type if it has no sort, 500 Simoleons if it has no price.
    /// </summary>
    public static class Kiosk
    {
        public static string Status = "not looked for yet";
        private static readonly Regex KioskText = new Regex(@"information kiosk", RegexOptions.IgnoreCase);

        public static void Restore()
        {
            var content = Content.Get();
            var objects = content.WorldObjects as TS1ObjectProvider;
            if (objects == null) { Status = "not a Sims 1 game"; return; }

            var byCategory = AccessTools.Field(typeof(TS1ObjectProvider), "ItemsByCategory")?.GetValue(null) as List<ObjectCatalogItem>[];
            var byGUID = AccessTools.Field(typeof(TS1ObjectProvider), "ItemsByGUID")?.GetValue(null) as Dictionary<uint, ObjectCatalogItem>;
            if (byCategory == null || byGUID == null) throw new Exception("the catalog was not found: this pack was made for a different version of Simitone");

            var added = new List<string>();
            var notes = new List<string>();
            var files = new HashSet<object>();
            var candidates = objects.Entries.Values.Where(x => Regex.IsMatch(x.FileName ?? "", "sculpture", RegexOptions.IgnoreCase) || (x.Name ?? "").IndexOf("kiosk", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var entry in candidates)
            {
                GameObject first = null;
                try { first = objects.Get(entry.ID); } catch { }
                var res = first?.Resource;
                if (res == null || !files.Add(res)) continue;
                List<CTSS> texts = null;
                List<OBJD> objds = null;
                try { texts = res.List<CTSS>(); objds = res.List<OBJD>(); } catch { }
                if (texts == null || objds == null) continue;

                var kioskTexts = texts.Where(t => KioskText.IsMatch(t.GetString(0) ?? "")).ToList();
                var named = objds.Where(o => (o.ChunkLabel ?? "").IndexOf("kiosk", StringComparison.OrdinalIgnoreCase) >= 0 && (o.ChunkLabel ?? "").IndexOf("info", StringComparison.OrdinalIgnoreCase) >= 0);
                var kiosks = objds.Where(o => kioskTexts.Any(t => t.ChunkID == o.CatalogStringsID)).Union(named).ToList();
                if (kioskTexts.Count > 0 && kiosks.Count == 0)
                    notes.Add($"its catalog text is in {entry.FileName} (text {string.Join(", ", kioskTexts.Select(t => t.ChunkID))}), but no object there uses that text");

                foreach (var obj in kiosks)
                {
                    var where = $"\"{obj.ChunkLabel}\" ({entry.FileName})";
                    if (obj.MasterID != 0 && obj.SubIndex != -1) { notes.Add($"{where} is one tile of a larger object (part {obj.MasterID}/{obj.SubIndex})"); continue; }
                    if (byGUID.ContainsKey(obj.GUID)) { notes.Add($"{where} is already in the catalog"); continue; }

                    var category = (obj.FunctionFlags > 0) ? (sbyte)Math.Log(obj.FunctionFlags, 2) : (sbyte)6; //6: miscellaneous
                    if (category < 0 || category > 7) category = 6;
                    var text = texts.FirstOrDefault(t => t.ChunkID == obj.CatalogStringsID)?.GetString(0);
                    var item = new ObjectCatalogItem()
                    {
                        Category = category,
                        RoomSort = (byte)((obj.RoomFlags != 0) ? obj.RoomFlags : 0xFF),
                        GUID = obj.GUID,
                        DisableLevel = 0,
                        Price = (obj.Price != 0) ? obj.Price : 500u,
                        Name = obj.ChunkLabel,
                        CatalogName = string.IsNullOrWhiteSpace(text) ? "Information Kiosk" : text,
                        Subsort = Sort(obj.FunctionSubsort),
                        CommunitySort = Sort(obj.CommunitySubsort),
                        DowntownSort = Sort(obj.DTSubsort),
                        MagictownSort = Sort(obj.MTSubsort),
                        StudiotownSort = Sort(obj.STSubsort),
                        VacationSort = Sort(obj.VacationSubsort)
                    };
                    byCategory[item.Category].Add(item);
                    byGUID[item.GUID] = item;
                    added.Add(where);
                    CutContentPack.Log($"kiosk: added {where} to the catalog. it had category {obj.FunctionFlags:x}, build type {obj.BuildModeType}, rooms {obj.RoomFlags:x}, price {obj.Price}, off {obj.Disabled}, graphics {obj.NumGraphics}, multi-tile {obj.IsMultiTile}");
                }
            }

            if (added.Count > 0) Status = "added to buy mode (Miscellaneous unless it says otherwise): " + string.Join(", ", added) + (notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
            else if (notes.Count > 0) Status = "not added: " + string.Join("; ", notes);
            else Status = "no kiosk object found (it needs Superstar)";
            CutContentPack.Log("kiosk: " + Status);
        }

        private static byte Sort(ushort value)
        {
            return (byte)((value != 0) ? value : 0xFF);
        }
    }
}
