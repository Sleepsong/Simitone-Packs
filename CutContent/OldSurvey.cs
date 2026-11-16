/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.FAR1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// "cutcontent survey old FOLDER": reads an older copy of The Sims straight from its folder, without the game loading
    /// it, so it works for a copy Simitone can't run, such as the base game alone.
    ///
    /// The cut gardening, music and literacy skills would have used person data 13, 14 and 16, which Hot Date later took
    /// for Sims' interests. When it did, anything that still wrote skills into them had to go, so a copy from before Hot
    /// Date is where any such steps would be. This lists, from the old copy:
    ///   - every script step that reads or sets person data 9, 13, 14 or 16, and whether the game being played still has
    ///     it (the same step in the same script), has the script without it, or doesn't have the script;
    ///   - the steps of each old script that touches 13, 14 or 16 and was changed or removed since;
    ///   - every text that mentions a skill, with the whole table where it is a table of names (such as Behavior.iff's).
    /// Only reads the old copy: nothing in it is changed or used by the game.
    /// </summary>
    public static class OldSurvey
    {
        private static readonly (int index, string name)[] Slots = { (9, "cleaning"), (13, "gardening"), (14, "music"), (16, "literacy") };
        private static readonly Dictionary<int, string> Scopes = new Dictionary<int, string> { { 18, "my" }, { 19, "stack object's" }, { 32, "neighbour's" } };
        private static readonly Regex SkillText = new Regex(@"skill|literacy|gardening", RegexOptions.IgnoreCase);
        private const int MaxScriptsListed = 120;

        private class OldFile
        {
            public string Name;   //the file's own name, as the game knows it
            public string Where;  //where it was found
            public IffFile Iff;
        }

        /// <summary>The pack's own folder, where an older copy can be put and found without typing a path.</summary>
        public static string PackFolder;

        /// <summary>
        /// Finds the folder meant by what was typed. Each folder name is matched without minding capitals, as on Windows; "~" is
        /// the home folder; a path that doesn't start at the root is looked for in the pack's folder and then the user
        /// folder; and nothing at all means the folders inside the pack's folder. Returns null, with what was tried and what is
        /// there instead, when nothing matches.
        /// </summary>
        public static string Find(string typed, out string explained)
        {
            var notes = new StringBuilder();
            typed = (typed ?? "").Trim().Trim('"', '\'');
            notes.AppendLine($"Path received: \"{typed}\"");
            if (typed == "")
            {
                if (PackFolder != null && Directory.Exists(PackFolder) && Directory.EnumerateDirectories(PackFolder).Any())
                {
                    explained = notes + $"No path was given, so the folders inside the pack's folder are read: {PackFolder}";
                    return PackFolder;
                }
                explained = notes + $"No path was given, and the pack's folder ({PackFolder}) has no folder inside it. Put the older copy there, or give its path.";
                return null;
            }
            if (typed == "~" || typed.StartsWith("~/")) typed = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + typed.Substring(1);

            var tries = new List<string>();
            if (Path.IsPathRooted(typed)) tries.Add(typed);
            else
            {
                if (PackFolder != null) tries.Add(Path.Combine(PackFolder, typed));
                tries.Add(Path.Combine(FSO.Common.FSOEnvironment.UserDir, typed));
            }
            foreach (var attempt in tries)
            {
                var found = Match(Path.GetFullPath(attempt), notes);
                if (found != null)
                {
                    explained = notes + $"Reading: {found}";
                    return found;
                }
            }
            explained = notes.ToString();
            return null;
        }

        /// <summary>Walks a path one folder at a time, matching each name without minding capitals. Says where it stopped and what is there.</summary>
        private static string Match(string full, StringBuilder notes)
        {
            if (Directory.Exists(full)) return full;
            var root = Path.GetPathRoot(full);
            var parts = full.Substring(root.Length).Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var at = root;
            foreach (var part in parts)
            {
                string[] inside;
                try { inside = Directory.GetDirectories(at); }
                catch (Exception e) { notes.AppendLine($"Could not look inside {at}: {e.Message}"); return null; }
                var next = inside.FirstOrDefault(x => string.Equals(Path.GetFileName(x), part, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                {
                    notes.AppendLine($"Not found: \"{part}\" in {at}");
                    notes.AppendLine("  The folders there are: " + (inside.Length == 0 ? "(none)" : string.Join(", ", inside.Select(x => $"\"{Path.GetFileName(x)}\"").Take(40))));
                    return null;
                }
                at = next;
            }
            return at;
        }

        public static string Run(string typed)
        {
            var report = new StringBuilder();
            report.AppendLine("Cut Content survey of an older copy");
            var folder = Find(typed, out var explained);
            report.Append(explained);
            if (!explained.EndsWith("\n")) report.AppendLine();
            if (folder == null) { report.AppendLine("The folder was not found."); return report.ToString(); }

            Unopened.Clear();
            var files = Read(folder, report);
            report.AppendLine($"{files.Count} object and script files read.");
            Section(report, "Files the engine would not open: what they look like", () => Describe(report));
            Section(report, "What the files are made of, and what the engine could not read", () => Formats(report, files));
            Section(report, "Behavior.iff's tables of names, whole", () => Labels(report, files));
            Section(report, "Career levels that ask for the three unnamed skills (min req 7, 8, 9)", () =>
            {
                int levels = 0, asking = 0;
                foreach (var file in files) SkillLinks.Careers(report, file.Name, List<CARR>(file.Iff), ref levels, ref asking);
                report.AppendLine($"{asking} of {levels} career levels ask for them.");
            });
            Section(report, "Script names that mention a skill, learning, studying or practising", () =>
            {
                foreach (var file in files)
                    foreach (var script in List<BHAV>(file.Iff).Where(x => ScriptWords.IsMatch(x.ChunkLabel ?? "")).OrderBy(x => x.ChunkID))
                        report.AppendLine($"{file.Name} script {script.ChunkID} \"{Survey.Clean(script.ChunkLabel)}\"");
            });
            Section(report, "Menu entries whose pull is scaled by a cut skill", () =>
            {
                var found = 0;
                foreach (var file in files) SkillLinks.Ads(report, file.Name, List<TTAB>(file.Iff), id => file.Iff.Get<TTAs>(id), ref found);
                report.AppendLine($"{found} entries.");
            });
            Section(report, "Objects' skill flags (the skills buy mode says they build)", () =>
                SkillLinks.Flags(report, files.SelectMany(f => List<OBJD>(f.Iff).Select(o => (f.Name, o)))));
            Section(report, "Texts that mention a skill", () => Texts(report, files));
            Section(report, "Script steps that read or set person data 9, 13, 14 or 16", () => Steps(report, files));
            return report.ToString();
        }

        private static void Section(StringBuilder report, string title, Action write)
        {
            report.AppendLine();
            report.AppendLine("== " + title + " ==");
            try { write(); }
            catch (Exception e) { report.AppendLine("This part failed: " + e); }
        }

        // ---- reading the old copy ----

        private static List<OldFile> Read(string folder, StringBuilder report)
        {
            var files = new List<OldFile>();
            var paths = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(x => !Saved(x)) //saved lots and families: not the game's code
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var path in paths)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var shown = Path.GetRelativePath(folder, path);
                if (ext == ".far")
                {
                    FAR1Archive far = null;
                    try
                    {
                        far = new FAR1Archive(path, false);
                        var count = 0;
                        foreach (var entry in far.GetAllFarEntries())
                        {
                            if (!entry.Filename.EndsWith(".iff", StringComparison.OrdinalIgnoreCase)) continue;
                            var name = Path.GetFileName(entry.Filename.Replace('\\', '/'));
                            try
                            {
                                var iff = new IffFile { RetainChunkData = true };
                                using (var stream = new MemoryStream(far.GetEntry(entry))) iff.Read(stream);
                                files.Add(new OldFile { Name = name, Where = shown, Iff = iff });
                                count++;
                            }
                            catch (Exception e) { report.AppendLine($"{shown}/{name}: could not be read ({e.Message})"); }
                        }
                        report.AppendLine($"{shown}: {count} files");
                    }
                    catch (Exception e) { report.AppendLine($"{shown}: not an archive the survey can read ({e.Message})"); }
                    finally { far?.Close(); }
                }
                else if (ext == ".iff")
                {
                    try { files.Add(new OldFile { Name = Path.GetFileName(path), Where = shown, Iff = new IffFile(path, true) }); }
                    catch (Exception e) { report.AppendLine($"{shown}: could not be read ({e.Message})"); Unopened.Add(path); }
                }
            }
            return files;
        }

        private static bool Saved(string path)
        {
            var p = path.Replace('\\', '/').ToLowerInvariant();
            return p.Contains("/userdata") || p.Contains("/neighborhood/");
        }

        /// <summary>Loose files the engine would not open, kept to describe at the end.</summary>
        private static List<string> Unopened = new List<string>();

        private static readonly string[] Tags = { "BHAV", "STR#", "TTAB", "TTAs", "OBJD", "OBJf", "CTSS", "BCON", "GLOB", "CARR", "SLOT", "DGRP", "SPR2", "rsmp" };

        /// <summary>
        /// What the files the engine would not open look like: the text each begins with, a dump of the first bytes of a few,
        /// and where the data tags the game uses (BHAV, STR#, TTAB...) are found, with the bytes after them. The layout of an
        /// older file can be worked out from this, to read it.
        /// </summary>
        private static void Describe(StringBuilder report)
        {
            if (Unopened.Count == 0) { report.AppendLine("Every file was opened."); return; }
            var heads = new Dictionary<string, int>();
            foreach (var path in Unopened)
            {
                try
                {
                    var head = new byte[64];
                    using (var f = File.OpenRead(path)) f.Read(head, 0, head.Length);
                    var text = Printable(head);
                    heads[text] = heads.TryGetValue(text, out var n) ? n + 1 : 1;
                }
                catch { }
            }
            report.AppendLine("The text the files begin with:");
            foreach (var head in heads.OrderByDescending(x => x.Value)) report.AppendLine($"  {head.Value} files: \"{head.Key}\"");

            //a few to look at closely: the label file, the global scripts, and one object
            var picks = new[] { "behavior.iff", "global.iff", "bones.iff", "bookshelfm.iff" }
                .Select(n => Unopened.FirstOrDefault(p => Path.GetFileName(p).Equals(n, StringComparison.OrdinalIgnoreCase)))
                .Where(p => p != null).Distinct().ToList();
            if (picks.Count == 0) picks = Unopened.Take(2).ToList();
            foreach (var path in picks)
            {
                byte[] data;
                try { data = File.ReadAllBytes(path); } catch { continue; }
                report.AppendLine();
                report.AppendLine($"{Path.GetFileName(path)}, {data.Length} bytes. The first 192:");
                Dump(report, data, 0, 192);
                var found = 0;
                foreach (var tag in Tags)
                {
                    var t = System.Text.Encoding.ASCII.GetBytes(tag);
                    var at = new List<int>();
                    for (int i = 0; i + 4 <= data.Length && at.Count < 400; i++)
                        if (data[i] == t[0] && data[i + 1] == t[1] && data[i + 2] == t[2] && data[i + 3] == t[3]) at.Add(i);
                    if (at.Count == 0) continue;
                    report.AppendLine($"  \"{tag}\" {at.Count} times, first at {string.Join(", ", at.Take(6))}");
                    foreach (var i in at.Take(found < 6 ? 2 : 0))
                    {
                        found++;
                        Dump(report, data, i, 96);
                    }
                }
            }
        }

        private static string Printable(byte[] bytes)
        {
            return new string(bytes.Select(b => (b >= 32 && b < 127) ? (char)b : '.').ToArray());
        }

        private static void Dump(StringBuilder report, byte[] data, int from, int count)
        {
            for (int i = from; i < Math.Min(data.Length, from + count); i += 16)
            {
                var line = data.Skip(i).Take(Math.Min(16, data.Length - i)).ToArray();
                report.AppendLine($"    {i:x6}: {string.Join(" ", line.Select(b => b.ToString("x2"))),-47}  {Printable(line)}");
            }
        }

        private static List<T> List<T>(IffFile iff) where T : IffChunk
        {
            try { return iff.List<T>() ?? new List<T>(); } catch { return new List<T>(); }
        }

        private static readonly Regex ScriptWords = new Regex(@"skill|garden|music|literac|learn|study|practi", RegexOptions.IgnoreCase);

        /// <summary>
        /// A copy from before the game came out may keep its data in older forms than the engine reads: menu tables before
        /// version 4, and object definitions other than 138 to 142, are skipped. This counts each kind of data, its versions
        /// read from the raw bytes, and what could not be read, so that "nothing found" can be told apart from "not readable".
        /// </summary>
        private static void Formats(StringBuilder report, List<OldFile> files)
        {
            var kinds = new Dictionary<string, int>();
            var versions = new Dictionary<string, Dictionary<string, int>>();
            var unread = new Dictionary<string, int>();
            int oldTables = 0, oldEntries = 0;
            void Count(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1;
            void Version(string kind, string v) { if (!versions.TryGetValue(kind, out var d)) versions[kind] = d = new Dictionary<string, int>(); Count(d, v); }

            foreach (var file in files)
            {
                foreach (var type in new[] { typeof(BHAV), typeof(TTAB), typeof(OBJD), typeof(STR), typeof(CARR), typeof(TTAs) })
                {
                    System.Collections.IList chunks;
                    try { chunks = (System.Collections.IList)typeof(IffFile).GetMethod("List").MakeGenericMethod(type).Invoke(file.Iff, null); }
                    catch { Count(unread, type.Name + " (a whole file's)"); continue; }
                    if (chunks == null) continue;
                    foreach (IffChunk chunk in chunks)
                    {
                        Count(kinds, type.Name);
                        var raw = chunk.OriginalData;
                        if (raw == null || raw.Length < 4) continue;
                        if (chunk is TTAB table)
                        {
                            var count = BitConverter.ToUInt16(raw, 0);
                            var version = (count == 0) ? "empty" : BitConverter.ToUInt16(raw, 2).ToString();
                            Version("TTAB", version);
                            if (count > 0 && table.Interactions.Length == 0) { oldTables++; oldEntries += count; }
                        }
                        else if (chunk is OBJD objd) Version("OBJD", BitConverter.ToUInt32(raw, 0).ToString());
                        else if (chunk is BHAV) Version("BHAV", "0x" + BitConverter.ToUInt16(raw, 0).ToString("x4"));
                    }
                }
            }
            report.AppendLine("Counted: " + string.Join(", ", kinds.Select(x => $"{x.Value} {x.Key}")));
            foreach (var kind in versions)
                report.AppendLine($"{kind.Key} versions: " + string.Join(", ", kind.Value.OrderBy(x => x.Key).Select(x => $"{x.Key} ({x.Value})")));
            report.AppendLine($"Menu tables the engine skipped as too old: {oldTables}, with {oldEntries} entries the survey could not see.");
            if (unread.Count > 0) report.AppendLine("Could not be read: " + string.Join(", ", unread.Select(x => $"{x.Value} {x.Key}")));
            report.AppendLine("The engine reads object definitions of versions 138 to 142 and menu tables from version 4; scripts 0x8000 to 0x8003.");
        }

        /// <summary>The label tables a modding tool shows, which name person data, objects' fields and the "personality ads".</summary>
        private static readonly int[] LabelTables = { 200, 232, 141, 204 };

        private static void Labels(StringBuilder report, List<OldFile> files)
        {
            var behavior = files.FirstOrDefault(x => x.Name.Equals("Behavior.iff", StringComparison.OrdinalIgnoreCase));
            if (behavior == null) { report.AppendLine("Behavior.iff was not found in the folder."); return; }
            foreach (var id in LabelTables)
            {
                STR text = null;
                try { text = behavior.Iff.Get<STR>((ushort)id); } catch { }
                if (text == null) { report.AppendLine($"text {id}: not there"); continue; }
                report.AppendLine($"text {id} \"{Survey.Clean(text.ChunkLabel)}\":");
                for (int i = 0; i < text.Length; i++)
                {
                    var value = Survey.Clean(text.GetString(i));
                    if (value.Length > 0) report.AppendLine($"  {i}: {value}");
                }
            }
        }

        // ---- texts ----

        private static void Texts(StringBuilder report, List<OldFile> files)
        {
            foreach (var file in files)
            {
                foreach (var text in List<STR>(file.Iff))
                {
                    var lines = Enumerable.Range(0, text.Length).Select(i => (i, value: text.GetString(i) ?? "")).ToList();
                    var hits = lines.Where(x => SkillText.IsMatch(x.value)).ToList();
                    if (hits.Count == 0) continue;
                    //a table of names, such as the labels a modding tool shows: worth seeing whole, with each line's number
                    var whole = hits.Count >= 3 && lines.Count <= 200;
                    report.AppendLine($"{file.Where} {(file.Where.EndsWith(file.Name, StringComparison.OrdinalIgnoreCase) ? "" : file.Name + " ")}text {text.ChunkID} \"{Survey.Clean(text.ChunkLabel)}\"" + (whole ? " (whole table)" : ""));
                    foreach (var (i, value) in whole ? lines.Where(x => x.value.Length > 0) : hits)
                        report.AppendLine($"  {i}: {Survey.Clean(value)}");
                }
            }
        }

        // ---- steps ----

        private class Hit
        {
            public OldFile File;
            public BHAV Script;
            public int Step;
            public int Slot;
            public string Text;
        }

        private static void Steps(StringBuilder report, List<OldFile> files)
        {
            var names = Slots.ToDictionary(x => x.index, x => x.name);
            var hits = new List<Hit>();
            foreach (var file in files)
            {
                foreach (var script in List<BHAV>(file.Iff).OrderBy(x => x.ChunkID))
                {
                    for (int i = 0; i < script.Instructions.Length; i++)
                    {
                        var inst = script.Instructions[i];
                        if (inst.Opcode != 2 || inst.Operand == null || inst.Operand.Length < 8) continue; //expression
                        var e = new VMExpressionOperand();
                        try { e.Read(inst.Operand); } catch { continue; }
                        foreach (var (scope, data) in new[] { ((int)e.LhsOwner, (int)e.LhsData), ((int)e.RhsOwner, (int)e.RhsData) })
                        {
                            if (!Scopes.ContainsKey(scope) || !names.ContainsKey(data)) continue;
                            hits.Add(new Hit { File = file, Script = script, Step = i, Slot = data,
                                Text = $"{Scopes[scope]} {names[data]} ({data}): {e.LhsOwner}[{e.LhsData}] {e.Operator} {e.RhsOwner}[{e.RhsData}] -> true {Survey.Next(inst.TruePointer)}, false {Survey.Next(inst.FalsePointer)}" });
                            break;
                        }
                    }
                }
            }

            report.AppendLine("Totals: " + string.Join(", ", Slots.Select(x => $"{x.name} {hits.Count(h => h.Slot == x.index)}")));
            report.AppendLine("Each step is checked against the game being played: \"still there\" (the same step in the same script),");
            report.AppendLine("\"changed\" (the script is there without that step), \"script gone\", or \"file not in this game\".");
            report.AppendLine();
            var status = new Dictionary<Hit, string>();
            foreach (var hit in hits)
            {
                status[hit] = Compare(hit);
                report.AppendLine($"{hit.File.Name} script {hit.Script.ChunkID} \"{Survey.Clean(hit.Script.ChunkLabel)}\" step {hit.Step}: {hit.Text} [{status[hit]}]");
            }

            //the old scripts that touched the three cut skills and have since changed: their steps, which may show what learning them was
            report.AppendLine();
            report.AppendLine("Steps of the old scripts that touch gardening, music or literacy and are not the same in the game being played:");
            var listed = 0;
            foreach (var group in hits.Where(x => x.Slot != 9 && status[x] != "still there").GroupBy(x => x.Script))
            {
                if (listed++ >= MaxScriptsListed) { report.AppendLine("(no more: the limit was reached)"); break; }
                var hit = group.First();
                report.AppendLine($"{hit.File.Name} script {hit.Script.ChunkID} \"{Survey.Clean(hit.Script.ChunkLabel)}\" (arguments {hit.Script.Args}, locals {hit.Script.Locals}) [{status[hit]}]:");
                for (int i = 0; i < hit.Script.Instructions.Length; i++)
                {
                    var inst = hit.Script.Instructions[i];
                    string what;
                    if (inst.Opcode >= 256)
                    {
                        var kind = (inst.Opcode < 4096) ? "global" : (inst.Opcode < 8192) ? "own" : "shared";
                        var sub = (inst.Opcode >= 4096 && inst.Opcode < 8192) ? List<BHAV>(hit.File.Iff).FirstOrDefault(x => x.ChunkID == inst.Opcode) : null;
                        what = $"call {kind} {inst.Opcode}{(sub != null ? $" \"{Survey.Clean(sub.ChunkLabel)}\"" : "")} with {Survey.Hex(inst.Operand)}";
                    }
                    else
                    {
                        var prim = (inst.Opcode < VMContext.Primitives.Length) ? VMContext.Primitives[inst.Opcode] : null;
                        what = $"{prim?.Name ?? ("primitive " + inst.Opcode)} {Survey.Operand(prim, inst.Operand)}";
                    }
                    report.AppendLine($"    {i}: {what} -> true {Survey.Next(inst.TruePointer)}, false {Survey.Next(inst.FalsePointer)}");
                }
            }
        }

        private static Dictionary<string, GameIffResource> Current = new Dictionary<string, GameIffResource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The same file in the game being played: an object's file, or a shared or global script file.</summary>
        private static GameIffResource CurrentFile(string name)
        {
            if (Current.TryGetValue(name, out var found)) return found;
            GameIffResource res = null;
            try
            {
                var objects = Content.Get().WorldObjects as TS1ObjectProvider;
                var entry = objects?.Entries.Values.FirstOrDefault(x => string.Equals(Path.GetFileName((x.FileName ?? "").Replace('\\', '/')), name, StringComparison.OrdinalIgnoreCase));
                if (entry != null) res = objects.Get(entry.ID)?.Resource;
                if (res == null) res = Content.Get().WorldObjectGlobals.Get(Path.GetFileNameWithoutExtension(name))?.Resource;
            }
            catch { }
            Current[name] = res;
            return res;
        }

        private static string Compare(Hit hit)
        {
            var res = CurrentFile(hit.File.Name);
            if (res == null) return "file not in this game";
            BHAV now = null;
            try { now = res.Get<BHAV>(hit.Script.ChunkID); } catch { }
            if (now == null || Survey.Clean(now.ChunkLabel) != Survey.Clean(hit.Script.ChunkLabel)) return "script gone";
            var old = hit.Script.Instructions[hit.Step];
            return now.Instructions.Any(x => x.Opcode == old.Opcode && x.Operand != null && x.Operand.SequenceEqual(old.Operand)) ? "still there" : "changed";
        }
    }
}
