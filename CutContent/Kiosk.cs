/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// Superstar's Information Kiosk. What is left of it is in SculptureMovie.iff, among Superstar's film-set objects: its
    /// catalog text, 2005, "Information Kiosk" ("Information is a valuable commodity... the kiosk as provided is information
    /// free, but it does make a lovely addition to any corporate area"), and its picture, 600, with its sprite. The file's
    /// other pictures go with its objects by hundreds (the bust 300, the film camera 400, the TV camera 500), and 600 and
    /// 2005 are the only ones no object uses. What is missing is the object definition that ties them into an object; that
    /// is the data buy mode needs.
    ///
    /// So it is made again, in memory, from its nearest sibling: the TV camera's definition, a single-tile film-set item,
    /// given its own number, the kiosk's picture and catalog text, and a price. It goes into the file (in memory), the
    /// game's list of objects and buy mode, sorted as the film-set bust is: home lots' Decorative > Sculptures and every
    /// community lot (buy mode leaves out of home lots anything with no room sort, as the TV camera has). If the game ever loads the file
    /// again, the kiosk is put back as it is asked for, so lots with a kiosk still load. Without the pack, a saved kiosk is
    /// an object the game doesn't know, and is left out when the lot loads.
    /// </summary>
    public static class Kiosk
    {
        public static string Status = "not looked for yet";
        public const uint Guid = 0x6C7C05C1; //the kiosk's own number, made up: not one the game uses
        public const uint Price = 750;
        private static readonly Regex KioskText = new Regex(@"information kiosk", RegexOptions.IgnoreCase);

        private static OBJD Made; //the kiosk's definition

        public static void Restore()
        {
            var content = Content.Get();
            var objects = content.WorldObjects as TS1ObjectProvider;
            if (objects == null) { Status = "not a Sims 1 game"; return; }
            var byCategory = AccessTools.Field(typeof(TS1ObjectProvider), "ItemsByCategory")?.GetValue(null) as List<ObjectCatalogItem>[];
            var byGUID = AccessTools.Field(typeof(TS1ObjectProvider), "ItemsByGUID")?.GetValue(null) as Dictionary<uint, ObjectCatalogItem>;
            if (byCategory == null || byGUID == null) throw new Exception("the catalog was not found: this pack was made for a different version of Simitone");

            List<GameObjectReference> entries;
            lock (objects.Entries) entries = objects.Entries.Values.ToList();
            if (entries.Any(x => x.ID == Guid)) { Status = $"not added: the number it would have, {Guid:x8}, is taken"; return; }

            var notes = new List<string>();
            var files = new HashSet<object>();
            foreach (var entry in entries.Where(x => Regex.IsMatch(x.FileName ?? "", "sculpture", RegexOptions.IgnoreCase)))
            {
                GameObject first = null;
                try { first = objects.Get(entry.ID); } catch { }
                var res = first?.Resource;
                if (res?.MainIff == null || !files.Add(res)) continue;
                var iff = res.MainIff;
                List<CTSS> texts; List<OBJD> objds; List<DGRP> pictures;
                try { texts = iff.List<CTSS>() ?? new List<CTSS>(); objds = iff.List<OBJD>() ?? new List<OBJD>(); pictures = iff.List<DGRP>() ?? new List<DGRP>(); }
                catch { continue; }

                var text = texts.FirstOrDefault(t => KioskText.IsMatch(t.GetString(0) ?? ""));
                if (text == null) continue;
                var users = objds.Where(o => o.CatalogStringsID == text.ChunkID).ToList();
                if (users.Count > 0)
                {
                    //an object with the kiosk's text after all: only the catalog entry can be missing
                    foreach (var obj in users.Where(o => o.MasterID == 0 || o.SubIndex == -1))
                    {
                        if (byGUID.ContainsKey(obj.GUID)) { notes.Add($"\"{obj.ChunkLabel}\" ({entry.FileName}) is already in the catalog"); continue; }
                        AddToCatalog(byCategory, byGUID, obj, text.GetString(0));
                        notes.Add($"added \"{obj.ChunkLabel}\" ({entry.FileName}) to buy mode");
                    }
                    continue;
                }

                //no object: make one. its picture is the one no object uses, numbered as the others are, by hundreds
                bool Used(DGRP d) => objds.Any(o => d.ChunkID >= o.BaseGraphicID && d.ChunkID < o.BaseGraphicID + Math.Max((ushort)1, o.NumGraphics));
                var singles = objds.Where(o => o.MasterID == 0 && o.NumGraphics > 0).OrderBy(o => o.BaseGraphicID).ToList();
                var picture = pictures.Where(d => !Used(d) && d.ChunkID % 100 == 0 && d.ChunkID > (singles.LastOrDefault()?.BaseGraphicID ?? 0)).OrderBy(d => d.ChunkID).FirstOrDefault();
                var like = singles.Where(o => byGUID.ContainsKey(o.GUID)).OrderByDescending(o => o.CatalogStringsID).FirstOrDefault();
                if (picture == null || like == null)
                {
                    notes.Add($"its catalog text is in {entry.FileName} (text {text.ChunkID}), but {(picture == null ? "no unused picture was found for it" : "no object there to make it from")}");
                    continue;
                }

                Made = MadeObjects.Copy(iff, like);
                Made.GUID = Guid;
                Made.ChunkID = (ushort)(objds.Max(o => o.ChunkID) + 1);
                Made.ChunkLabel = "Sculptures - Information Kiosk (Cut Content)";
                Made.BaseGraphicID = picture.ChunkID;
                Made.NumGraphics = 1;
                Made.CatalogStringsID = text.ChunkID;
                Made.Price = (ushort)Price;
                Made.MasterID = 0;
                Made.SubIndex = 0;
                Made.Disabled = 0;
                //where buy mode shows it: as the film-set piece of the file sold on every kind of lot (the bust: home lots'
                //Decorative > Sculptures, and every community lot). On a home lot buy mode leaves out anything with no room
                //sort, as the TV camera has, so the camera's own sorts would show the kiosk on community lots only
                var sorts = singles.Where(o => o.RoomFlags != 0 && o.FunctionSubsort != 0 && byGUID.ContainsKey(o.GUID)).OrderBy(o => o.BaseGraphicID).FirstOrDefault();
                if (sorts != null)
                {
                    Made.FunctionFlags = sorts.FunctionFlags;
                    Made.RoomFlags = sorts.RoomFlags;
                    Made.FunctionSubsort = sorts.FunctionSubsort;
                    Made.CommunitySubsort = sorts.CommunitySubsort;
                    Made.DTSubsort = sorts.DTSubsort;
                    Made.VacationSubsort = sorts.VacationSubsort;
                    Made.STSubsort = sorts.STSubsort;
                    Made.MTSubsort = sorts.MTSubsort;
                }
                else if (Made.RoomFlags == 0) Made.RoomFlags = 0x40; //miscellaneous
                Made = MadeObjects.Copy(iff, Made); //written out and read in again, so its raw values (which scripts read) agree
                //the film-set objects keep their scripts in a function table under their definition's number: the kiosk
                //needs a copy of the camera's under its own, or placing it closes the game
                var chunks = new List<IffChunk> { Made };
                var functions = MadeObjects.FunctionTable(res, like, Made);
                if (functions != null) chunks.Insert(0, functions);
                else if (like.UsesFnTable != 0) { Made.UsesFnTable = 0; Made = MadeObjects.Copy(iff, Made); } //no table to copy: none at all, rather than one that isn't there
                MadeObjects.Add(res, like.GUID, Made, chunks);
                AddToCatalog(byCategory, byGUID, Made, text.GetString(0));
                notes.Add($"made from its catalog text ({text.ChunkID}) and picture ({picture.ChunkID}) in {entry.FileName}, which no object used, on the model of \"{like.ChunkLabel}\"; " +
                    $"in buy mode for ${Price}, sorted as \"{sorts?.ChunkLabel ?? like.ChunkLabel}\" (home lots: Decorative > Sculptures; community lots: their decorative section)");
                CutContentPack.Log($"kiosk: made object {Guid:x8} in {entry.FileName}: picture {picture.ChunkID}, catalog text {text.ChunkID}, like {like.ChunkLabel} ({like.GUID:x8})");
            }

            Status = notes.Count > 0 ? (Made != null || notes.Any(n => n.StartsWith("added")) ? "restored: " : "not added: ") + string.Join("; ", notes) : "no kiosk found (it needs Superstar)";
            CutContentPack.Log("kiosk: " + Status);
        }

        private static void AddToCatalog(List<ObjectCatalogItem>[] byCategory, Dictionary<uint, ObjectCatalogItem> byGUID, OBJD obj, string name)
        {
            var category = (obj.FunctionFlags > 0) ? (sbyte)Math.Log(obj.FunctionFlags, 2) : (sbyte)6; //6: miscellaneous
            if (category < 0 || category > 7) category = 6;
            var item = new ObjectCatalogItem()
            {
                Category = category,
                RoomSort = (byte)obj.RoomFlags,
                GUID = obj.GUID,
                DisableLevel = 0,
                Price = (obj.Price != 0) ? obj.Price : Price,
                Name = obj.ChunkLabel,
                CatalogName = string.IsNullOrWhiteSpace(name) ? "Information Kiosk" : name,
                Subsort = (byte)obj.FunctionSubsort,
                CommunitySort = (byte)obj.CommunitySubsort,
                DowntownSort = (byte)obj.DTSubsort,
                MagictownSort = (byte)obj.MTSubsort,
                StudiotownSort = (byte)obj.STSubsort,
                VacationSort = (byte)obj.VacationSubsort
            };
            byCategory[item.Category].Add(item);
            byGUID[item.GUID] = item;
        }
    }
}
