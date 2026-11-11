/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Content.TS1;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CutContent
{
    /// <summary>
    /// Superstar's Information Kiosk is in the game's files, but without the catalog details that would put it in
    /// buy mode. This adds a catalog entry for it, in memory, filling in only the details that are missing:
    /// Miscellaneous if it has no category, every room and lot type if it has no sort, 500 Simoleons if it has no price.
    /// </summary>
    public static class Kiosk
    {
        public static string Status = "not looked for yet";

        public static void Restore()
        {
            var content = Content.Get();
            var objects = content.WorldObjects as TS1ObjectProvider;
            if (objects == null) { Status = "not a Sims 1 game"; return; }

            var byCategory = AccessTools.Field(typeof(TS1ObjectProvider), "ItemsByCategory")?.GetValue(null) as List<ObjectCatalogItem>[];
            var byGUID = AccessTools.Field(typeof(TS1ObjectProvider), "ItemsByGUID")?.GetValue(null) as Dictionary<uint, ObjectCatalogItem>;
            if (byCategory == null || byGUID == null) throw new Exception("the catalog was not found: this pack was made for a different version of Simitone");

            var added = new List<string>();
            var kiosks = objects.Entries.Values.Where(x => (x.Name ?? "").ToLowerInvariant().Contains("kiosk")).ToList();
            foreach (var entry in kiosks)
            {
                var name = entry.Name.ToLowerInvariant();
                if (!name.Contains("info") || byGUID.ContainsKey((uint)entry.ID)) continue;
                var obj = objects.Get(entry.ID)?.OBJ;
                if (obj == null || (obj.MasterID != 0 && obj.SubIndex != -1)) continue; //only the object itself, not the tiles of a larger one

                var category = (obj.FunctionFlags > 0) ? (sbyte)Math.Log(obj.FunctionFlags, 2) : (sbyte)6; //6: miscellaneous
                if (category < 0 || category > 7) category = 6;
                var item = new ObjectCatalogItem()
                {
                    Category = category,
                    RoomSort = (byte)((obj.RoomFlags != 0) ? obj.RoomFlags : 0xFF),
                    GUID = obj.GUID,
                    DisableLevel = 0,
                    Price = (obj.Price != 0) ? obj.Price : 500u,
                    Name = obj.ChunkLabel,
                    Subsort = Sort(obj.FunctionSubsort),
                    CommunitySort = Sort(obj.CommunitySubsort),
                    DowntownSort = Sort(obj.DTSubsort),
                    MagictownSort = Sort(obj.MTSubsort),
                    StudiotownSort = Sort(obj.STSubsort),
                    VacationSort = Sort(obj.VacationSubsort)
                };
                byCategory[item.Category].Add(item);
                byGUID[item.GUID] = item;
                added.Add($"{entry.Name} ({entry.FileName})");
                CutContentPack.Log($"kiosk: added {entry.Name} from {entry.FileName} to the catalog. it had category {obj.FunctionFlags:x}, rooms {obj.RoomFlags:x}, price {obj.Price}, off {obj.Disabled}, graphics {obj.NumGraphics}");
            }

            if (added.Count > 0) Status = "added to buy mode: " + string.Join(", ", added);
            else if (kiosks.Count > 0) Status = "no Information Kiosk that is missing from the catalog. Kiosks in the game: " + string.Join(", ", kiosks.Select(x => x.Name).Distinct().Take(6));
            else Status = "no kiosk object found (it needs Superstar)";
            CutContentPack.Log("kiosk: " + Status);
        }

        private static byte Sort(ushort value)
        {
            return (byte)((value != 0) ? value : 0xFF);
        }
    }
}
