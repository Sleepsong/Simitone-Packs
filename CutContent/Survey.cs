/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.Client;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CutContent
{
    /// <summary>
    /// Writes down what the player's game holds for each cut feature: the objects, their interaction tables with the
    /// marks on each entry, the scripts no table entry leads to, the texts, the sounds and the skins.
    ///
    /// The restorations that are not done yet need the names and numbers in this report. It reads every object file
    /// in the game, so it takes a while.
    /// </summary>
    public static class Survey
    {
        /// <summary>Words that mark something as belonging to a cut feature. Matched on lower case English text.</summary>
        private static readonly string[] Words =
        {
            "hug", "toad", "lock", "litter", "limo", "invest", "kiosk", "pickpocket", "pick pocket",
            "clean skill", "cleaning skill", "garden", "music skill", "literacy", "spooky",
        };
        /// <summary>Objects reported in full whatever they contain.</summary>
        private static readonly string[] ObjectWords = { "toad", "kiosk", "trainer", "limo", "taxi", "cab", "phone", "pickpocket" };

        private static bool Has(string text, string[] words)
        {
            if (string.IsNullOrEmpty(text)) return false;
            text = text.ToLowerInvariant();
            return words.Any(x => text.Contains(x));
        }

        private static string Clean(string text)
        {
            return (text ?? "").TrimEnd('\0').Replace("\r", " ").Replace("\n", " ");
        }

        public static string Run()
        {
            var report = new StringBuilder();
            report.AppendLine("Cut Content survey");
            report.AppendLine();
            report.AppendLine("== What the pack has done ==");
            report.AppendLine("Ghost sting: " + GhostSting.Status);
            report.AppendLine("Information Kiosk: " + Kiosk.Status);
            foreach (var rule in Interactions.Rules) report.AppendLine(rule.Name + ": " + rule.Status);

            Section(report, "Sounds", Sounds);
            Section(report, "Skins", Skins);
            Section(report, "The selected Sim's skill numbers", Skills);
            Section(report, "Objects", Objects);
            return report.ToString();
        }

        private static void Section(StringBuilder report, string title, Action<StringBuilder> write)
        {
            report.AppendLine();
            report.AppendLine("== " + title + " ==");
            try { write(report); }
            catch (Exception e) { report.AppendLine("This part failed: " + e); }
        }

        private static void Sounds(StringBuilder report)
        {
            var events = Content.Get().Audio?.Events;
            if (events == null) return;
            var groups = new HashSet<FSO.Content.Model.HITResourceGroup>();
            foreach (var evt in events.OrderBy(x => x.Key))
            {
                if (evt.Value.ResGroup != null) groups.Add(evt.Value.ResGroup);
                if (evt.Key.Contains("sting") || evt.Key.Contains("spooky") || evt.Key.Contains("ghost"))
                    report.AppendLine($"event {evt.Key}: type {evt.Value.EventType}, track {evt.Value.TrackID:x}");
            }
            foreach (var group in groups)
            {
                if (group.hot?.Patches == null) continue;
                foreach (var patch in group.hot.Patches)
                {
                    var file = (patch.Value.Filename ?? "").ToLowerInvariant();
                    if (file.Contains("sting") || file.Contains("spooky") || file.Contains("ghost"))
                        report.AppendLine($"patch {patch.Key:x}: {patch.Value.Name} = {patch.Value.Filename}");
                }
            }
        }

        private static void Skins(StringBuilder report)
        {
            var textures = Content.Get().AvatarTextures as TS1AvatarTextureProvider;
            if (textures == null) return;
            var words = new[] { "pick", "burglar", "thief", "robber", "crook" };
            foreach (var name in textures.GetAllNames().OrderBy(x => x))
            {
                if (Has(name, words)) report.AppendLine(name);
            }
        }

        private static void Skills(StringBuilder report)
        {
            var sim = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.SelectedAvatar;
            if (sim == null) { report.AppendLine("No Sim is selected. Run the survey in a lot to see these."); return; }
            report.AppendLine("The game keeps ten skill numbers for a Sim and shows six. The names of the other four below are");
            report.AppendLine("what they are believed to have been, not something read from the game.");
            var slots = new (int index, string name)[]
            {
                (9, "cleaning? (cut)"), (10, "cooking"), (11, "charisma"), (12, "mechanical"), (13, "gardening? (cut)"),
                (14, "music? (cut)"), (15, "creativity"), (16, "literacy? (cut)"), (17, "body"), (18, "logic"),
            };
            foreach (var slot in slots)
                report.AppendLine($"{sim.Name}, number {slot.index} ({slot.name}): {sim.GetPersonData((VMPersonDataVariable)slot.index)}");
        }

        private static void Objects(StringBuilder report)
        {
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objects == null) return;
            var done = new HashSet<object>();
            foreach (var entry in objects.Entries.Values.OrderBy(x => x.FileName).ThenBy(x => x.Name))
            {
                try
                {
                    var obj = objects.Get(entry.ID);
                    var res = obj?.Resource;
                    if (res == null || !done.Add(res)) continue;
                    File(report, objects, entry.FileName, res);
                }
                catch (Exception e)
                {
                    report.AppendLine($"{entry.FileName}: could not be read ({e.Message})");
                }
            }
        }

        private static void File(StringBuilder report, TS1ObjectProvider catalog, string fileName, GameObjectResource res)
        {
            var objds = res.List<OBJD>() ?? new List<OBJD>();
            var tables = res.List<TTAB>() ?? new List<TTAB>();
            var scripts = res.List<BHAV>() ?? new List<BHAV>();
            var texts = res.List<STR>() ?? new List<STR>();
            var whole = Has(fileName, ObjectWords) || objds.Any(x => Has(x.ChunkLabel, ObjectWords));

            var lines = new List<string>();
            var called = new HashSet<ushort>();
            foreach (var table in tables)
            {
                var strings = res.Get<TTAs>(table.ChunkID);
                foreach (var entry in table.Interactions)
                {
                    called.Add(entry.ActionFunction);
                    called.Add(entry.TestFunction);
                    var label = Clean(strings?.GetString((int)entry.TTAIndex));
                    var action = res.Get<BHAV>(entry.ActionFunction);
                    var check = res.Get<BHAV>(entry.TestFunction);
                    if (!whole && !Has(label, Words) && !Has(action?.ChunkLabel, Words) && !Has(check?.ChunkLabel, Words)) continue;
                    var marks = new List<string>();
                    if (entry.Debug) marks.Add("debug only");
                    if (!entry.TS1AllowChild) marks.Add("no children");
                    if (!entry.TS1AllowAdults) marks.Add("no adults");
                    if (entry.TS1AllowVisitors) marks.Add("visitors");
                    if (entry.TestFunction != 0 && check == null && entry.TestFunction >= 4096 && entry.TestFunction < 8192) marks.Add("CHECK SCRIPT MISSING: never shown");
                    if (action == null && entry.ActionFunction >= 4096 && entry.ActionFunction < 8192) marks.Add("ACTION SCRIPT MISSING");
                    lines.Add($"  table {table.ChunkID} entry {entry.TTAIndex} \"{label}\": marks {(uint)entry.Flags:x} [{string.Join(", ", marks)}], " +
                        $"action {entry.ActionFunction} \"{Clean(action?.ChunkLabel)}\", check {entry.TestFunction} \"{Clean(check?.ChunkLabel)}\"");
                }
            }
            foreach (var script in scripts)
            {
                if (!Has(script.ChunkLabel, Words)) continue;
                lines.Add($"  script {script.ChunkID} \"{Clean(script.ChunkLabel)}\"{(called.Contains(script.ChunkID) ? "" : " (no table entry leads straight to it)")}");
            }
            foreach (var text in texts)
            {
                if (text is TTAs) continue;
                for (int i = 0; i < text.Length; i++)
                {
                    var value = text.GetString(i);
                    if (Has(value, Words)) lines.Add($"  text {text.ChunkID} \"{Clean(text.ChunkLabel)}\" line {i}: {Clean(value)}");
                }
            }
            if (lines.Count == 0 && !whole) return;

            report.AppendLine();
            report.AppendLine(fileName);
            foreach (var objd in objds)
            {
                var inCatalog = catalog.GetItemByGUID(objd.GUID) != null;
                report.AppendLine($"  object \"{Clean(objd.ChunkLabel)}\" {objd.GUID:x8}: type {objd.ObjectType}, table {objd.TreeTableID}, " +
                    $"{(inCatalog ? "in catalog" : "not in catalog")} (category {objd.FunctionFlags:x}, build {objd.BuildModeType}, rooms {objd.RoomFlags:x}, " +
                    $"price {objd.Price}, off {objd.Disabled}, graphics {objd.NumGraphics}, part {objd.MasterID}/{objd.SubIndex}, catalog text {objd.CatalogStringsID})");
            }
            foreach (var line in lines) report.AppendLine(line);
        }
    }
}
