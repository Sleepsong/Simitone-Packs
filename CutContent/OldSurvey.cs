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

            var files = Read(folder, report);
            report.AppendLine($"{files.Count} object and script files read.");
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
                .Where(x => !x.Replace('\\', '/').ToLowerInvariant().Contains("/userdata")) //saved lots and families: not the game's code
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
                                var iff = new IffFile();
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
                    try { files.Add(new OldFile { Name = Path.GetFileName(path), Where = shown, Iff = new IffFile(path) }); }
                    catch (Exception e) { report.AppendLine($"{shown}: could not be read ({e.Message})"); }
                }
            }
            return files;
        }

        private static List<T> List<T>(IffFile iff) where T : IffChunk
        {
            try { return iff.List<T>() ?? new List<T>(); } catch { return new List<T>(); }
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
