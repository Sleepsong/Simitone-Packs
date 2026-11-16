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
            report.AppendLine("Cut Content survey, version 7");
            report.AppendLine();
            report.AppendLine("== What the pack has done ==");
            report.AppendLine("Ghost sting: " + GhostSting.Status);
            report.AppendLine("Information Kiosk: " + Kiosk.Status);
            foreach (var rule in AddedInteractions.Rules) report.AppendLine(rule.Name + ": " + rule.Status);
            foreach (var rule in Interactions.Rules) report.AppendLine(rule.Name + ": " + rule.Status);
            report.AppendLine("Locked doors: " + DoorLocks.Status);
            report.AppendLine("Investment calls: " + Investments.StatusText((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm));
            report.AppendLine($"Limousine: {Limousine.Status}. This session: {Limousine.Hired} hired.");

            Section(report, "Cut skills: who has them", SkillHolders);
            Section(report, "Cleaning skill: the routine that reads it, and what calls it", Cleaning);
            Section(report, "Sounds", Sounds);
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
            var words = new[] { "pick", "burglar", "thief", "robber", "crook", "vacationma" };
            foreach (var name in textures.GetAllNames().OrderBy(x => x))
            {
                if (Has(name, words)) report.AppendLine(name);
            }
        }

        /// <summary>The four person-data numbers no part of the game shows, and what they are believed to have been.</summary>
        private static readonly (int index, string name)[] CutSkills = { (9, "cleaning"), (13, "gardening"), (14, "music"), (16, "literacy") };

        /// <summary>Every Sim and NPC on the lot, with the four numbers.</summary>
        private static void SkillHolders(StringBuilder report)
        {
            var vm = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm;
            if (vm == null) { report.AppendLine("Not in a lot. Run the survey in a lot to see these."); return; }
            report.AppendLine("Person data 9, 13, 14 and 16 are believed to be the cut cleaning, gardening, music and literacy skills.");
            report.AppendLine("The wiki lists those four, and The Sims: Life Stories used 13, 14 and 16 for gardening, music and literacy.");
            foreach (var sim in vm.Entities.OfType<VMAvatar>())
            {
                var values = string.Join(", ", CutSkills.Select(x => $"{x.name} {sim.GetPersonData((VMPersonDataVariable)x.index)}"));
                var shown = $"cooking {sim.GetPersonData(VMPersonDataVariable.CookingSkill)}, charisma {sim.GetPersonData(VMPersonDataVariable.CharismaSkill)}";
                report.AppendLine($"{sim.Name} ({Clean(sim.Object?.OBJ?.ChunkLabel)}, {sim.Object?.OBJ?.GUID:x8}): {values}; for comparison {shown}");
            }
        }

        /// <summary>
        /// Every expression step in the game that reads or sets one of the four, as a Sim's own person data (scope 18), the
        /// stack object's (19) or a neighbour's (32). Steps that pick the number from a temp at run time can't be told
        /// apart here, so they are only counted.
        /// </summary>
        private static void SkillSteps(StringBuilder report)
        {
            var wanted = new HashSet<int>(CutSkills.Select(x => x.index));
            var names = CutSkills.ToDictionary(x => x.index, x => x.name);
            var scopes = new Dictionary<int, string> { { 18, "my" }, { 19, "stack object's" }, { 32, "neighbour's" } };
            var byTemp = 0;
            var counts = new Dictionary<int, int>();
            foreach (var (fileName, res) in AllFiles())
            {
                List<BHAV> scripts = null;
                try { scripts = res.List<BHAV>(); } catch { }
                if (scripts == null) continue;
                foreach (var script in scripts.OrderBy(x => x.ChunkID))
                {
                    for (int i = 0; i < script.Instructions.Length; i++)
                    {
                        var inst = script.Instructions[i];
                        if (inst.Opcode != 2 || inst.Operand == null || inst.Operand.Length < 8) continue; //expression
                        var e = new FSO.SimAntics.Engine.Primitives.VMExpressionOperand();
                        try { e.Read(inst.Operand); } catch { continue; }
                        var sides = new[] { ((int)e.LhsOwner, (int)e.LhsData), ((int)e.RhsOwner, (int)e.RhsData) };
                        if (sides.Any(x => x.Item1 == 30 || x.Item1 == 31)) byTemp++;
                        var hit = sides.FirstOrDefault(x => scopes.ContainsKey(x.Item1) && wanted.Contains(x.Item2));
                        if (hit.Item1 == 0) continue;
                        counts[hit.Item2] = counts.TryGetValue(hit.Item2, out var c) ? c + 1 : 1;
                        report.AppendLine($"{fileName} script {script.ChunkID} \"{Clean(script.ChunkLabel)}\" step {i}: {scopes[hit.Item1]} {names[hit.Item2]} ({hit.Item2}): " +
                            $"{e.LhsOwner}[{e.LhsData}] {e.Operator} {e.RhsOwner}[{e.RhsData}] -> true {Next(inst.TruePointer)}, false {Next(inst.FalsePointer)}");
                    }
                }
            }
            report.AppendLine("Totals: " + string.Join(", ", CutSkills.Select(x => $"{x.name} {(counts.TryGetValue(x.index, out var c) ? c : 0)}")) +
                $". Steps that pick person data by a temp, which may be any of them: {byTemp}.");
        }

        /// <summary>Every object file, its shared script file and the global file, once each.</summary>
        private static List<(string, GameIffResource)> AllFiles()
        {
            var files = new List<(string, GameIffResource)>();
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objects == null) return files;
            var done = new HashSet<object>();
            foreach (var entry in objects.Entries.Values.OrderBy(x => x.FileName))
            {
                GameObjectResource res = null;
                try { res = objects.Get(entry.ID)?.Resource; } catch { }
                if (res != null && done.Add(res)) files.Add((entry.FileName, res));
                if (res?.SemiGlobal != null && done.Add(res.SemiGlobal)) files.Add((res.SemiGlobal.MainIff?.Filename ?? "(semi-global)", res.SemiGlobal));
            }
            var global = Content.Get().WorldObjectGlobals.Get("global")?.Resource;
            if (global != null && done.Add(global)) files.Add(("global.iff", global));
            return files;
        }

        /// <summary>
        /// Global 304, "clean inc", reads person data 9 to work out how fast a Sim cleans. Its steps, and every step in the
        /// game that calls it, with the steps of the first few callers, to see where a Sim could learn cleaning.
        /// </summary>
        private static void Cleaning(StringBuilder report)
        {
            var global = Content.Get().WorldObjectGlobals.Get("global")?.Resource;
            if (global == null) { report.AppendLine("The global file was not found."); return; }
            var lines = new List<string>();
            var clean = global.Get<BHAV>(304);
            if (clean != null) Steps(lines, global, null, clean);
            else lines.Add("Global 304 was not found.");
            var callers = new List<(string file, GameIffResource res, GameIffResource shared, BHAV script, int step)>();
            foreach (var (fileName, res) in AllFiles())
            {
                List<BHAV> scripts = null;
                try { scripts = res.List<BHAV>(); } catch { }
                if (scripts == null) continue;
                foreach (var script in scripts.OrderBy(x => x.ChunkID))
                    for (int i = 0; i < script.Instructions.Length; i++)
                        if (script.Instructions[i].Opcode == 304) callers.Add((fileName, res, (res as GameObjectResource)?.SemiGlobal, script, i));
            }
            lines.Add($"{callers.Count} steps call it:");
            foreach (var c in callers) lines.Add($"  {c.file} script {c.script.ChunkID} \"{Clean(c.script.ChunkLabel)}\" step {c.step}");
            foreach (var c in callers.GroupBy(x => x.script).Select(x => x.First()).Take(4)) Steps(lines, c.res, c.shared, c.script);
            foreach (var line in lines) report.AppendLine(line);
        }

        private static readonly Regex BodyWords = new Regex("vacation|burglar|thief|pick|mafit|mafat|maskn|crook|robber", RegexOptions.IgnoreCase);

        /// <summary>The skins the game's body files offer, and the meshes, that could carry the pickpocket's textures.</summary>
        private static void Bodies(StringBuilder report)
        {
            var bcf = Content.Get().BCFGlobal;
            if (bcf != null)
            {
                var skins = bcf.SkinHostBCF.Keys.Where(x => BodyWords.IsMatch(x)).OrderBy(x => x).ToList();
                report.AppendLine($"{bcf.SkinHostBCF.Count} skins in the body files; these match: " + string.Join(", ", skins.Select(x => $"{x} (in {bcf.SkinHostBCF[x]})")));
            }
            var meshes = (Content.Get().AvatarMeshes as TS1BMFProvider)?.GetAllNames().Where(x => BodyWords.IsMatch(x)).OrderBy(x => x).ToList();
            report.AppendLine("Meshes that match: " + ((meshes == null) ? "(couldn't list)" : string.Join(", ", meshes)));
        }

        /// <summary>What every character in the game wears: its body strings (text 200), which name a mesh and a texture for each part.</summary>
        private static void Outfits(StringBuilder report)
        {
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objects == null) return;
            var done = new HashSet<object>();
            foreach (var guid in objects.PersonGUIDs.OrderBy(x => x))
            {
                try
                {
                    var obj = objects.Get(guid);
                    if (obj?.Resource == null || !done.Add(obj.Resource)) continue;
                    var texts = obj.Resource.Get<STR>(200);
                    if (texts == null) continue;
                    var lines = Enumerable.Range(0, texts.Length).Select(i => Clean(texts.GetString(i))).Where(x => x.Length > 0).ToList();
                    report.AppendLine($"{obj.Resource.Name} \"{Clean(obj.OBJ.ChunkLabel)}\" {guid:x8}: {string.Join(" | ", lines)}");
                }
                catch (Exception e) { report.AppendLine($"{guid:x8}: could not be read ({e.Message})"); }
            }
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
