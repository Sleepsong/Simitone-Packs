/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Content;
using FSO.Content.Codecs;
using FSO.Content.Framework;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using HarmonyLib;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// Writes down what the player's game holds for each cut feature: the objects, their interaction tables with the
    /// marks on each entry, the scripts no table entry leads to, the texts, the sounds and the skins. The second
    /// version also lists the steps of the scripts that matter, reads the shared script files (semi-globals) that
    /// objects like the phones and doors keep most of their code in, and lists objects that are not in the catalog.
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
            "clean skill", "cleaning skill", "garden", "music skill", "literacy", "spooky", "stock", "placeholder",
        };
        /// <summary>Objects reported in full whatever they contain.</summary>
        private static readonly string[] ObjectWords = { "toad", "kiosk", "trainer", "limo", "taxi", "pickpocket" };
        /// <summary>Scripts whose steps are listed.</summary>
        private static readonly Regex ScriptsToList = new Regex(
            @"limo|invest|stock|offer|kiosk|pickpocket|placeholder|random call",
            RegexOptions.IgnoreCase);
        private const int MaxScriptsListed = 300;

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

        private static int Listed;
        private static HashSet<BHAV> ListedScripts = new HashSet<BHAV>();

        public static string Run()
        {
            Listed = 0;
            ListedScripts.Clear();
            var report = new StringBuilder();
            report.AppendLine("Cut Content survey, version 5");
            report.AppendLine();
            report.AppendLine("== What the pack has done ==");
            report.AppendLine("Ghost sting: " + GhostSting.Status);
            report.AppendLine("Information Kiosk: " + Kiosk.Status);
            foreach (var rule in AddedInteractions.Rules) report.AppendLine(rule.Name + ": " + rule.Status);
            foreach (var rule in Interactions.Rules) report.AppendLine(rule.Name + ": " + rule.Status);
            report.AppendLine("Locked doors: " + DoorLocks.Status);
            report.AppendLine("Investment calls: " + Investments.StatusText((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm));

            Section(report, "Files in full", Whole);
            Section(report, "Object files with kiosk or info in the name, and files that failed to load", KioskFiles);
            Section(report, "Sounds", Sounds);
            Section(report, "Skins", Skins);
            Section(report, "The selected Sim's skill numbers", Skills);
            Section(report, "Objects", Objects);
            Section(report, "Shared script files (semi-globals)", SemiGlobals);
            return report.ToString();
        }

        private static void Section(StringBuilder report, string title, Action<StringBuilder> write)
        {
            report.AppendLine();
            report.AppendLine("== " + title + " ==");
            try { write(report); }
            catch (Exception e) { report.AppendLine("This part failed: " + e); }
        }

        /// <summary>Runs one part of a file's report, so a chunk that can't be read doesn't lose the rest of the file.</summary>
        private static T Try<T>(List<string> lines, string what, Func<T> get)
        {
            try { return get(); }
            catch (Exception e) { lines.Add($"  ({what} could not be read: {e.Message})"); return default(T); }
        }

        /// <summary>Files listed in full: every object, table entry, script with its steps, and text.</summary>
        /// <summary>Files listed in full. Surveys 3 and 4 listed the ones the limousine and the investments needed.</summary>
        private static readonly string[] WholeFiles = { };
        /// <summary>Shared script files listed in full, but with only some scripts' steps.</summary>
        private static readonly (string file, Regex steps)[] WholeShared = { };

        private static void Whole(StringBuilder report)
        {
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objects == null) return;
            foreach (var name in WholeFiles)
            {
                var entry = objects.Entries.Values.FirstOrDefault(x => string.Equals(System.IO.Path.GetFileName(x.FileName ?? ""), name, StringComparison.OrdinalIgnoreCase));
                if (entry == null) { report.AppendLine(); report.AppendLine(name + ": not in this game"); continue; }
                GameObjectResource res = null;
                try { res = objects.Get(entry.ID)?.Resource; }
                catch (Exception e) { report.AppendLine(); report.AppendLine($"{name}: could not be loaded ({e.Message})"); continue; }
                if (res != null) Full(report, objects, name, res, res.SemiGlobal, null);
            }
            foreach (var (file, steps) in WholeShared)
            {
                var res = Content.Get().WorldObjectGlobals.Get(file)?.Resource;
                if (res == null) { report.AppendLine(); report.AppendLine(file + ": not in this game"); continue; }
                Full(report, null, file + ".iff", res, null, steps);
            }
        }

        private static void Full(StringBuilder report, TS1ObjectProvider catalog, string fileName, GameIffResource res, GameIffResource semiGlobal, Regex steps)
        {
            var lines = new List<string>();
            report.AppendLine();
            report.AppendLine($"{fileName} in full" + ((semiGlobal != null) ? $" (shares scripts with {semiGlobal.MainIff?.Filename})" : ""));
            foreach (var objd in Try(lines, "objects", () => res.List<OBJD>()) ?? new List<OBJD>())
            {
                report.AppendLine($"  object \"{Clean(objd.ChunkLabel)}\" {objd.GUID:x8}: type {objd.ObjectType}, table {objd.TreeTableID}, " +
                    $"{((catalog?.GetItemByGUID(objd.GUID) != null) ? "in catalog" : "not in catalog")} (category {objd.FunctionFlags:x}, rooms {objd.RoomFlags:x}, price {objd.Price}, part {objd.MasterID}/{objd.SubIndex}, attributes {objd.NumAttributes})");
            }
            foreach (var table in Try(lines, "interaction tables", () => res.List<TTAB>()) ?? new List<TTAB>())
            {
                var strings = Try(lines, $"strings of table {table.ChunkID}", () => res.Get<TTAs>(table.ChunkID));
                foreach (var entry in table.Interactions)
                {
                    lines.Add($"  table {table.ChunkID} entry {entry.TTAIndex} \"{Clean(strings?.GetString((int)entry.TTAIndex))}\": marks {(uint)entry.Flags:x}, " +
                        $"action {entry.ActionFunction} \"{Clean(Find(res, semiGlobal, entry.ActionFunction)?.ChunkLabel)}\", check {entry.TestFunction} \"{Clean(Find(res, semiGlobal, entry.TestFunction)?.ChunkLabel)}\"");
                }
            }
            foreach (var text in Try(lines, "texts", () => res.List<STR>()) ?? new List<STR>())
            {
                for (int i = 0; i < text.Length; i++)
                {
                    var value = text.GetString(i);
                    if (!string.IsNullOrEmpty(value)) lines.Add($"  text {text.ChunkID} \"{Clean(text.ChunkLabel)}\" line {i}: {Clean(value)}");
                }
            }
            foreach (var script in (Try(lines, "scripts", () => res.List<BHAV>()) ?? new List<BHAV>()).OrderBy(x => x.ChunkID))
            {
                if (steps == null || steps.IsMatch(Clean(script.ChunkLabel))) Steps(lines, res, semiGlobal, script);
                else lines.Add($"  script {script.ChunkID} \"{Clean(script.ChunkLabel)}\"");
            }
            foreach (var line in lines) report.AppendLine(line);
        }

        /// <summary>The kiosk is in no object the game lists. Maybe its file is there with no object in it, or failed to load.</summary>
        private static void KioskFiles(StringBuilder report)
        {
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            var provider = AccessTools.Field(typeof(TS1ObjectProvider), "GameObjects")?.GetValue(objects) as TS1SubProvider<FSO.Files.Formats.IFF.IffFile>;
            var names = provider?.GetAllNames().ToList();
            if (names == null) report.AppendLine("(couldn't list the object files)");
            else
            {
                report.AppendLine($"{names.Count} object files.");
                foreach (var name in names.Where(x => Regex.IsMatch(x, "kiosk|info", RegexOptions.IgnoreCase)).OrderBy(x => x)) report.AppendLine("file: " + name);
            }
            foreach (var failed in Content.FailedContentFiles ?? Enumerable.Empty<TS1BCFProvider.FailedFileInfo>())
                report.AppendLine($"failed: {failed.Filename}: {failed.ErrorType}: {failed.ErrorMessage}");

            //The Sims shows an object in buy mode only with its catalog text. Simitone doesn't ask for it, so the kiosk may
            //already be in Simitone's catalog, with no name: list the catalog's objects that have no catalog text
            report.AppendLine("Objects in Simitone's catalog with no catalog text (The Sims would not show these):");
            foreach (var item in objects?.All() ?? new List<FSO.Content.Interfaces.ObjectCatalogItem>())
            {
                try
                {
                    var obj = objects.Get(item.GUID);
                    if (obj?.OBJ == null) continue;
                    var ctss = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID);
                    var name = ctss?.GetString(0);
                    if (string.IsNullOrWhiteSpace(name))
                        report.AppendLine($"  \"{Clean(obj.OBJ.ChunkLabel)}\" {item.GUID:x8} in {obj.Resource.Name}: catalog text {obj.OBJ.CatalogStringsID} {(ctss == null ? "missing" : "empty")}, category {item.Category}, price {item.Price}");
                }
                catch (Exception e) { report.AppendLine($"  {item.GUID:x8}: could not be read ({e.Message})"); }
            }
        }

        private static void Sounds(StringBuilder report)
        {
            {
                var audio1 = Content.Get().Audio as TS1Audio;
                var all = AccessTools.Field(typeof(TS1Audio), "HitlistsById")?.GetValue(audio1) as Dictionary<uint, FSO.Files.HIT.Hitlist>;
                if (all != null && all.TryGetValue(0x31f, out var spooky))
                    report.AppendLine($"list 31f, from all sound files: {string.Join(", ", spooky.IDs.Select(x => x.ToString("x")))}");
                else report.AppendLine("list 31f is in no sound file");
            }
            var audio = Content.Get().Audio as TS1Audio;
            var events = audio?.Events;
            if (events == null) return;
            foreach (var evt in events.OrderBy(x => x.Key))
            {
                if (!evt.Key.Contains("spooky")) continue;
                var track = audio.GetTrack(evt.Value.TrackID, 0, evt.Value.ResGroup);
                var list = (track != null && track.HitlistID != 0) ? audio.GetHitlist(track.HitlistID, evt.Value.ResGroup) : null;
                report.AppendLine($"event {evt.Key}: track {evt.Value.TrackID:x}, track sound {track?.SoundID:x}, subroutine {track?.SubroutineID:x}, " +
                    $"list {track?.HitlistID:x} = {((list == null) ? "none" : string.Join(", ", list.IDs.Select(x => x.ToString("x"))))}");
            }
            var wavs = AccessTools.Field(typeof(TS1Audio), "WAVSounds")?.GetValue(audio) as TS1SubProvider<DecodedSFX>;
            var names = wavs?.GetAllNames().Where(x => x.ToLowerInvariant().Contains("spooky") || x.ToLowerInvariant().Contains("ghost")).OrderBy(x => x).ToList();
            report.AppendLine("Sound files with spooky or ghost in the name: " + ((names == null) ? "(couldn't list)" : string.Join(", ", names)));
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
                GameObjectResource res = null;
                try
                {
                    res = objects.Get(entry.ID)?.Resource;
                }
                catch (Exception e)
                {
                    report.AppendLine($"{entry.FileName}: could not be loaded ({e.Message})");
                    continue;
                }
                if (res == null || !done.Add(res)) continue;
                File(report, objects, entry.FileName, res, res.SemiGlobal);
            }
        }

        private static void SemiGlobals(StringBuilder report)
        {
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objects == null) return;
            var done = new HashSet<object>();
            foreach (var entry in objects.Entries.Values.OrderBy(x => x.FileName))
            {
                GameIffResource sg = null;
                try { sg = objects.Get(entry.ID)?.Resource?.SemiGlobal; } catch { }
                if (sg == null || !done.Add(sg)) continue;
                File(report, null, sg.MainIff?.Filename ?? "(semi-global)", sg, null);
            }
            var global = Content.Get().WorldObjectGlobals.Get("global")?.Resource;
            if (global != null) File(report, null, "global.iff", global, null);
        }

        private static void File(StringBuilder report, TS1ObjectProvider catalog, string fileName, GameIffResource res, GameIffResource semiGlobal)
        {
            var lines = new List<string>();
            var objds = Try(lines, "objects", () => res.List<OBJD>()) ?? new List<OBJD>();
            var tables = Try(lines, "interaction tables", () => res.List<TTAB>()) ?? new List<TTAB>();
            var scripts = Try(lines, "scripts", () => res.List<BHAV>()) ?? new List<BHAV>();
            var texts = Try(lines, "texts", () => res.List<STR>()) ?? new List<STR>();
            var whole = Has(fileName, ObjectWords) || objds.Any(x => Has(x.ChunkLabel, ObjectWords));

            var called = new HashSet<ushort>();
            foreach (var table in tables)
            {
                var strings = Try(lines, $"strings of table {table.ChunkID}", () => res.Get<TTAs>(table.ChunkID));
                foreach (var entry in table.Interactions)
                {
                    called.Add(entry.ActionFunction);
                    called.Add(entry.TestFunction);
                    var label = Clean(strings?.GetString((int)entry.TTAIndex));
                    var action = Find(res, semiGlobal, entry.ActionFunction);
                    var check = Find(res, semiGlobal, entry.TestFunction);
                    if (!whole && !Has(label, Words) && !Has(action?.ChunkLabel, Words) && !Has(check?.ChunkLabel, Words)) continue;
                    var marks = new List<string>();
                    if (entry.Debug) marks.Add("debug only");
                    if (!entry.TS1AllowChild) marks.Add("no children");
                    if (!entry.TS1AllowAdults) marks.Add("no adults");
                    if (entry.TS1AllowVisitors) marks.Add("visitors");
                    lines.Add($"  table {table.ChunkID} entry {entry.TTAIndex} \"{label}\": marks {(uint)entry.Flags:x} [{string.Join(", ", marks)}], " +
                        $"action {entry.ActionFunction} \"{Clean(action?.ChunkLabel)}\", check {entry.TestFunction} \"{Clean(check?.ChunkLabel)}\"");
                }
            }
            var toList = new List<BHAV>();
            foreach (var script in scripts)
            {
                var label = Clean(script.ChunkLabel);
                if (ScriptsToList.IsMatch(label)) toList.Add(script);
                if (!Has(label, Words)) continue;
                lines.Add($"  script {script.ChunkID} \"{label}\"{(called.Contains(script.ChunkID) ? "" : " (no table entry leads straight to it)")}");
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
            foreach (var script in toList)
            {
                if (Listed >= MaxScriptsListed) { lines.Add("  (no more scripts listed: the limit was reached)"); break; }
                if (!ListedScripts.Add(script)) continue;
                Listed++;
                Steps(lines, res, semiGlobal, script);
            }
            if (lines.Count == 0 && !whole) return;

            report.AppendLine();
            report.AppendLine(fileName + ((semiGlobal != null) ? $" (shares scripts with {semiGlobal.MainIff?.Filename})" : ""));
            foreach (var objd in objds)
            {
                var inCatalog = catalog?.GetItemByGUID(objd.GUID) != null;
                report.AppendLine($"  object \"{Clean(objd.ChunkLabel)}\" {objd.GUID:x8}: type {objd.ObjectType}, table {objd.TreeTableID}, " +
                    $"{(inCatalog ? "in catalog" : "not in catalog")} (category {objd.FunctionFlags:x}, build {objd.BuildModeType}, rooms {objd.RoomFlags:x}, " +
                    $"price {objd.Price}, off {objd.Disabled}, graphics {objd.NumGraphics}, part {objd.MasterID}/{objd.SubIndex}, catalog text {objd.CatalogStringsID})");
            }
            foreach (var line in lines) report.AppendLine(line);
        }

        private static BHAV Find(GameIffResource res, GameIffResource semiGlobal, ushort id)
        {
            try
            {
                if (id == 0) return null;
                if (id < 4096) return Content.Get().WorldObjectGlobals.Get("global")?.Resource?.Get<BHAV>(id);
                if (id < 8192) return res.Get<BHAV>(id);
                return (semiGlobal ?? res).Get<BHAV>(id);
            }
            catch { return null; }
        }

        /// <summary>Lists a script's steps: what each does, with its settings, and where it goes next.</summary>
        private static void Steps(List<string> lines, GameIffResource res, GameIffResource semiGlobal, BHAV script)
        {
            lines.Add($"  steps of script {script.ChunkID} \"{Clean(script.ChunkLabel)}\" (arguments {script.Args}, locals {script.Locals}):");
            for (int i = 0; i < script.Instructions.Length; i++)
            {
                var inst = script.Instructions[i];
                string what;
                if (inst.Opcode >= 256)
                {
                    var sub = Find(res, semiGlobal, inst.Opcode);
                    var kind = (inst.Opcode < 4096) ? "global" : (inst.Opcode < 8192) ? "own" : "shared";
                    what = $"call {kind} {inst.Opcode} \"{Clean(sub?.ChunkLabel)}\" with {Hex(inst.Operand)}";
                }
                else
                {
                    var prim = (inst.Opcode < VMContext.Primitives.Length) ? VMContext.Primitives[inst.Opcode] : null;
                    what = $"{prim?.Name ?? ("primitive " + inst.Opcode)} {Operand(prim, inst.Operand)}";
                }
                lines.Add($"    {i}: {what} -> true {Next(inst.TruePointer)}, false {Next(inst.FalsePointer)}");
            }
        }

        private static string Next(byte pointer)
        {
            switch (pointer)
            {
                case 253: return "error";
                case 254: return "return true";
                case 255: return "return false";
                default: return pointer.ToString();
            }
        }

        private static string Hex(byte[] bytes)
        {
            return (bytes == null) ? "" : string.Join(" ", bytes.Select(x => x.ToString("x2")));
        }

        /// <summary>The settings of a step, read by the engine's own reader for that kind of step.</summary>
        private static string Operand(VMPrimitiveRegistration prim, byte[] bytes)
        {
            var raw = "[" + Hex(bytes) + "]";
            if (prim?.OperandModel == null || bytes == null) return raw;
            try
            {
                var operand = (VMPrimitiveOperand)Activator.CreateInstance(prim.OperandModel);
                operand.Read(bytes);
                var parts = new List<string>();
                foreach (var member in prim.OperandModel.GetMembers(BindingFlags.Public | BindingFlags.Instance))
                {
                    object value;
                    if (member is FieldInfo field) value = field.GetValue(operand);
                    else if (member is PropertyInfo prop && prop.GetIndexParameters().Length == 0 && prop.CanRead)
                    {
                        try { value = prop.GetValue(operand); } catch { continue; }
                    }
                    else continue;
                    if (value is byte[] array) value = Hex(array);
                    parts.Add($"{member.Name}={value}");
                }
                return "{" + string.Join(", ", parts) + "} " + raw;
            }
            catch
            {
                return raw;
            }
        }
    }
}
