/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Engine.Scopes;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// The parts of the survey of an older copy that read files in the form used before release (see <see cref="Iff10"/>),
    /// such as the 1999 build's. Its person data is numbered differently from 1.0's (its "init traits" sets 1 to 5 and 20),
    /// so the skills are found by the names in its own Behavior.iff, not by 1.0's numbers.
    /// </summary>
    public static partial class OldSurvey
    {
        private static readonly Regex PersonWords = new Regex(@"\bnice|\bneat|outgoing|\bactive|playful|generous|grouch|cook|mechanic|charisma|creativ|logic|\bbody\b|skill|clean|garden|music|litera|\bage\b|gender|money|\bjob\b|salary|\bmood|personality", RegexOptions.IgnoreCase);
        private static readonly Regex SkillName = new Regex(@"skill|clean|cook|garden|music|litera|\bread|writ|charisma|social|mechanic|repair|\bbody\b|physical|logic|creativ|\bart\b|paint", RegexOptions.IgnoreCase);
        private static readonly Regex ShippedSkill = new Regex(@"cook|mechanic|repair|charisma|social|\bbody\b|physical|logic|creativ", RegexOptions.IgnoreCase);
        private const int MaxOldScripts = 60;

        /// <summary>Names from the old Behavior.iff, where it has them: its primitives and its person data.</summary>
        private static string[] OldPrimitives;
        private static Dictionary<int, string> OldPersonData;

        private static List<OldFile> OldOnes(List<OldFile> files) => files.Where(x => x.Old != null).ToList();
        private static OldFile Behavior(List<OldFile> files) => files.FirstOrDefault(x => x.Name.Equals("Behavior.iff", StringComparison.OrdinalIgnoreCase));

        private static List<(STR text, List<string> lines)> Tables(OldFile file)
        {
            return List<STR>(file.Iff).OrderBy(x => x.ChunkID).Select(x => (x, Enumerable.Range(0, x.Length).Select(i => x.GetString(i) ?? "").ToList())).ToList();
        }

        /// <summary>Reads the names the old Behavior.iff gives primitives and person data, for the other parts.</summary>
        private static void LearnNames(List<OldFile> files, StringBuilder report)
        {
            OldPrimitives = null;
            OldPersonData = null;
            var behavior = Behavior(files);
            if (behavior?.Old == null) return;
            var tables = Tables(behavior);

            var prims = tables.FirstOrDefault(x => x.lines.Count > 2 && x.lines[2].IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0);
            if (prims.text != null)
            {
                OldPrimitives = prims.lines.ToArray();
                report.AppendLine($"Primitives are named from Behavior.iff text {prims.text.ChunkID} \"{Survey.Clean(prims.text.ChunkLabel)}\".");
            }
            else report.AppendLine("Behavior.iff has no table of primitives (one whose third line is \"expression\"): they are named as in 1.0.");

            //the person data table begins "idle state" and marks traits and skills "p:" and "s:". Another table lists the same
            //traits and skills as "personality ads" (None, Nice, Grouchy... Cleaning Skill...), numbered differently: not that one
            var person = tables
                .Select(x => (x.text, x.lines, score: x.lines.Count(l => PersonWords.IsMatch(l)) + 3 * x.lines.Count(l => Regex.IsMatch(l, @"^\s*[psi]: ", RegexOptions.IgnoreCase))
                    + (x.lines.Count > 0 && Regex.IsMatch(x.lines[0], @"idle state", RegexOptions.IgnoreCase) ? 100 : 0)
                    + (Regex.IsMatch(x.text.ChunkLabel ?? "", @"person.*data", RegexOptions.IgnoreCase) ? 100 : 0)
                    - (x.lines.Count > 0 && x.lines[0].Trim().Equals("None", StringComparison.OrdinalIgnoreCase) ? 50 : 0)))
                .OrderByDescending(x => x.score).FirstOrDefault();
            if (person.text != null && person.score >= 5)
            {
                OldPersonData = person.lines.Select((l, i) => (l, i)).ToDictionary(x => x.i, x => Survey.Clean(x.l));
                report.AppendLine($"Person data is named from Behavior.iff text {person.text.ChunkID} \"{Survey.Clean(person.text.ChunkLabel)}\".");
            }
            else report.AppendLine("No table in Behavior.iff looks like the names of person data.");
        }

        // ---- what the files hold ----

        private static void Old10Summary(StringBuilder report, List<OldFile> files)
        {
            var old = OldOnes(files);
            var live = new Dictionary<string, int>();
            int deleted = 0, chunks = 0, named = 0;
            var forms = new Dictionary<string, int>();
            var tables = new Dictionary<string, int>();
            var scripts = new Dictionary<string, int>();
            var objects = new Dictionary<string, int>();
            var nameKinds = new Dictionary<string, int>();
            void Count(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1;

            foreach (var file in old)
            {
                foreach (var chunk in file.Old.Chunks)
                {
                    if (chunk.IsNames) continue;
                    if (chunk.Deleted) { deleted++; continue; }
                    chunks++;
                    Count(live, Iff10.Printable(chunk.Type));
                    if (chunk.Label != "") named++;
                    if (chunk.Type == "STR#" || chunk.Type == "TTAs" || chunk.Type == "CTSS") { Iff10.Strings(chunk.Data, out var form); Count(forms, form); }
                    if (chunk.Type == "TTAB") { Iff10.Table(chunk.Data, out var v, out var problem); Count(tables, (v < 0 ? "empty" : v.ToString()) + (problem != null && v < 4 ? " (" + problem + ")" : "")); }
                    if (chunk.Type == "BHAV" && chunk.Data.Length >= 2) Count(scripts, "0x" + Iff10.LE16(chunk.Data, 0).ToString("x4"));
                    if (chunk.Type == "OBJD" && chunk.Data.Length >= 4) Count(objects, BitConverter.ToUInt32(chunk.Data, 0).ToString());
                }
                if (file.Old.NamesFrom.Count == 0) Count(nameKinds, "no table of names");
                foreach (var from in file.Old.NamesFrom) Count(nameKinds, Regex.Replace(from, @", naming \d+ chunks$", ""));
            }
            report.AppendLine($"{old.Count} files in the form used before release were read by the pack's own reader.");
            report.AppendLine("Chunks: " + string.Join(", ", live.OrderByDescending(x => x.Value).Select(x => $"{x.Value} {x.Key}")) + $"; and {deleted} deleted (\"XXXX\").");
            report.AppendLine($"Names: {named} of {chunks} chunks have one. The tables of names:");
            foreach (var kind in nameKinds.OrderByDescending(x => x.Value)) report.AppendLine($"  {kind.Value} files: {kind.Key}");
            report.AppendLine("Text forms: " + string.Join(", ", forms.Select(x => $"{x.Key} ({x.Value})")));
            report.AppendLine("Menu table versions: " + string.Join(", ", tables.Select(x => $"{x.Key} ({x.Value})")));
            report.AppendLine("Script versions: " + string.Join(", ", scripts.Select(x => $"{x.Key} ({x.Value})")));
            report.AppendLine("Object definition versions: " + string.Join(", ", objects.Select(x => $"{x.Key} ({x.Value})")));
            foreach (var file in old.Where(x => x.Old.Problem != null)) report.AppendLine($"{file.Where}: {file.Old.Problem}");
            var labelIds = old.SelectMany(f => f.Old.Chunks.Where(c => !c.IsNames && !c.Deleted)).Select(c => c.LabelId).ToList();
            var numbered = labelIds.Where(x => x != uint.MaxValue).ToList();
            report.AppendLine($"Name numbers in chunks: {labelIds.Count(x => x == uint.MaxValue)} have none (ff ff ff ff); the rest run from " +
                (numbered.Count > 0 ? $"{numbered.Min()} to {numbered.Max()}" : "-") + $", {numbered.Distinct().Count()} different.");
            var unread = old.SelectMany(f => f.Old.Live.Where(c => c.Type == "NAME").Select(c => (f, c))).Take(4).ToList();
            foreach (var (f, c) in unread)
            {
                report.AppendLine($"{f.Where}: \"NAME\" {c.Id}, flags 0x{c.Flags:x4}, name number {c.LabelId:x8}, {c.Data.Length} bytes; the chunk from its 13th byte:");
                Dump(report, c.Body, 0, 128);
                var some = f.Old.Chunks.Where(x => x != c && !x.Deleted).Take(6).Select(x => $"{Iff10.Printable(x.Type)} {x.Id} -> {x.LabelId:x8}");
                report.AppendLine("  name numbers of the file's first chunks: " + string.Join(", ", some));
            }
            var links = old.Select(x => (x.Name, link: SemiGlobalName(x))).Where(x => x.link != null).GroupBy(x => x.link).Select(g => $"{g.Key}: {g.Count()} files");
            report.AppendLine("Shared script files the objects use: " + string.Join(", ", links));
            LearnNames(files, report);
        }

        /// <summary>Every text list in an older Behavior.iff, whole: in this form there's no telling ahead which is which.</summary>
        private static void AllTables(StringBuilder report, OldFile behavior)
        {
            foreach (var (text, lines) in Tables(behavior))
            {
                report.AppendLine($"text {text.ChunkID} \"{Survey.Clean(text.ChunkLabel)}\" ({lines.Count} lines):");
                for (int i = 0; i < lines.Count; i++)
                {
                    var value = Survey.Clean(lines[i]);
                    if (value.Length > 0) report.AppendLine($"  {i}: {value}");
                }
            }
        }

        // ---- scripts ----

        /// <summary>The name of the shared script file an object uses, from its "GLOB" chunk.</summary>
        private static string SemiGlobalName(OldFile file)
        {
            var glob = file.Old?.Of("GLOB").FirstOrDefault();
            if (glob == null || glob.Data.Length == 0) return null;
            var d = glob.Data;
            string name;
            if (d[0] > 0 && d[0] < d.Length && d.Skip(1).Take(d[0]).All(b => b >= 32 && b < 127)) name = Encoding.ASCII.GetString(d, 1, d[0]);
            else name = Encoding.ASCII.GetString(d.TakeWhile(b => b != 0).ToArray());
            return name.Trim();
        }

        private static IEnumerable<OldFile> SemiGlobals(List<OldFile> files, OldFile file)
        {
            var name = SemiGlobalName(file);
            if (name != null)
            {
                var found = files.Where(x => Path.GetFileNameWithoutExtension(x.Name).Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase)).ToList();
                if (found.Count > 0) return found;
            }
            return files.Where(x => x.Where.Replace('\\', '/').IndexOf("global/", StringComparison.OrdinalIgnoreCase) >= 0 && !x.Name.Equals("Global.iff", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The name of the script a step or menu entry runs: an object's own, a global one, or a shared one.</summary>
        private static string ScriptName(List<OldFile> files, OldFile file, int id)
        {
            if (id == 0) return "none";
            IEnumerable<OldFile> where;
            if (id < 4096) where = files.Where(x => x.Name.Equals("Global.iff", StringComparison.OrdinalIgnoreCase) && (x.Old != null) == (file.Old != null));
            else if (id < 8192) where = new[] { file };
            else where = SemiGlobals(files, file);
            var found = where.Select(f => (f, s: List<BHAV>(f.Iff).FirstOrDefault(x => x.ChunkID == id))).Where(x => x.s != null).ToList();
            if (found.Count == 0) return "?";
            if (found.Count == 1) return Survey.Clean(found[0].s.ChunkLabel);
            return string.Join(" or ", found.Select(x => $"{Path.GetFileNameWithoutExtension(x.f.Name)}: {Survey.Clean(x.s.ChunkLabel)}"));
        }

        private static string Squash(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());

        /// <summary>A step in words, with the old copy's own primitive names. Settings are read as 1.0 reads them only where the primitive has the same name.</summary>
        private static string StepText(List<OldFile> files, OldFile file, BHAVInstruction inst)
        {
            if (inst.Opcode >= 256)
            {
                var kind = (inst.Opcode < 4096) ? "global" : (inst.Opcode < 8192) ? "own" : "shared";
                return $"call {kind} {inst.Opcode} \"{ScriptName(files, file, inst.Opcode)}\" with {Survey.Hex(inst.Operand)}";
            }
            var prim = (inst.Opcode < VMContext.Primitives.Length) ? VMContext.Primitives[inst.Opcode] : null;
            var then = (OldPrimitives != null && inst.Opcode < OldPrimitives.Length) ? Survey.Clean(OldPrimitives[inst.Opcode]) : "";
            if (inst.Opcode == 2 && (then == "" || then.IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0)) return "expression " + ExpressionText(inst.Operand);
            if (then == "" || Squash(then) == Squash(prim?.Name)) return $"{prim?.Name ?? ("primitive " + inst.Opcode)} {Survey.Operand(prim, inst.Operand)}";
            return $"{then} (primitive {inst.Opcode}, \"{prim?.Name}\" in 1.0) [{Survey.Hex(inst.Operand)}]";
        }

        /// <summary>An expression in words, with person data named as the old copy names it.</summary>
        private static string ExpressionText(byte[] operand)
        {
            var e = new VMExpressionOperand();
            try { e.Read(operand); } catch { return "[" + Survey.Hex(operand) + "]"; }
            string Side(VMVariableScope owner, short data) =>
                owner == VMVariableScope.Literal ? data.ToString() : PersonScopes.Contains(owner) ? $"{owner}[{Slot(data)}]" : $"{owner}[{data}]";
            return $"{Side(e.LhsOwner, e.LhsData)} {e.Operator} {Side(e.RhsOwner, e.RhsData)}";
        }

        private static void WholeScript(StringBuilder report, List<OldFile> files, OldFile file, BHAV script)
        {
            report.AppendLine($"{file.Name} script {script.ChunkID} \"{Survey.Clean(script.ChunkLabel)}\" ({script.Instructions.Length} steps) [{Now(file, script)}]:");
            for (int i = 0; i < script.Instructions.Length; i++)
            {
                var inst = script.Instructions[i];
                report.AppendLine($"    {i}: {StepText(files, file, inst)} -> true {Survey.Next(inst.TruePointer)}, false {Survey.Next(inst.FalsePointer)}");
            }
        }

        /// <summary>Whether the game being played has a script of the same name in the same file.</summary>
        private static string Now(OldFile file, BHAV script)
        {
            var res = CurrentFile(file.Name);
            if (res == null) return "file not in this game";
            List<BHAV> now = null;
            try { now = res.List<BHAV>(); } catch { }
            var same = now?.FirstOrDefault(x => Survey.Clean(x.ChunkLabel).Equals(Survey.Clean(script.ChunkLabel), StringComparison.OrdinalIgnoreCase));
            return same == null ? "no script of that name in this game" : $"in this game as {same.ChunkID}";
        }

        // ---- person data ----

        private class Use
        {
            public OldFile File;
            public BHAV Script;
            public int Step, Slot;
            public bool Sets;
        }

        private static readonly HashSet<VMVariableScope> PersonScopes = new HashSet<VMVariableScope> { VMVariableScope.MyPersonData, VMVariableScope.StackObjectPersonData, (VMVariableScope)32 };
        private static readonly HashSet<VMExpressionOperator> Compares = new HashSet<VMExpressionOperator> {
            VMExpressionOperator.GreaterThan, VMExpressionOperator.LessThan, VMExpressionOperator.Equals, VMExpressionOperator.IsFlagSet,
            VMExpressionOperator.GreaterThanOrEqualTo, VMExpressionOperator.LessThanOrEqualTo, VMExpressionOperator.NotEqualTo };

        private static string Slot(int i) => (OldPersonData != null && OldPersonData.TryGetValue(i, out var name) && name != "") ? $"{i} \"{name}\"" : i.ToString();

        /// <summary>
        /// The person data the old copy names, every step that reads or sets each, and, for the skills that didn't ship, the
        /// scripts that use them, whole.
        /// </summary>
        private static void PersonData(StringBuilder report, List<OldFile> files)
        {
            if (OldPersonData != null)
            {
                report.AppendLine("Person data, as Behavior.iff names it:");
                foreach (var slot in OldPersonData.Where(x => x.Value != "")) report.AppendLine($"  {slot.Key}: {slot.Value}");
            }
            else report.AppendLine("Behavior.iff doesn't name person data, so the numbers below are only numbers.");

            var uses = new List<Use>();
            foreach (var file in OldOnes(files))
                foreach (var script in List<BHAV>(file.Iff))
                    for (int i = 0; i < script.Instructions.Length; i++)
                    {
                        var inst = script.Instructions[i];
                        if (inst.Opcode != 2 || inst.Operand == null || inst.Operand.Length < 8) continue;
                        var e = new VMExpressionOperand();
                        try { e.Read(inst.Operand); } catch { continue; }
                        if (PersonScopes.Contains(e.LhsOwner)) uses.Add(new Use { File = file, Script = script, Step = i, Slot = e.LhsData, Sets = !Compares.Contains(e.Operator) });
                        if (PersonScopes.Contains(e.RhsOwner)) uses.Add(new Use { File = file, Script = script, Step = i, Slot = e.RhsData, Sets = false });
                    }

            report.AppendLine();
            report.AppendLine("Each slot's use, in steps that read or set a Sim's own or another Sim's person data:");
            foreach (var group in uses.GroupBy(x => x.Slot).OrderBy(x => x.Key))
                report.AppendLine($"  {Slot(group.Key)}: set in {group.Count(x => x.Sets)} steps, read in {group.Count(x => !x.Sets)}, in {group.Select(x => x.Script).Distinct().Count()} scripts");

            var named = OldPersonData ?? new Dictionary<int, string>();
            var marked = named.Where(x => Regex.IsMatch(x.Value, @"^\s*s: ", RegexOptions.IgnoreCase)).Select(x => x.Key).ToList();
            var skills = marked.Count > 0 ? marked : named.Where(x => SkillName.IsMatch(x.Value)).Select(x => x.Key).ToList();
            if (skills.Count == 0)
            {
                report.AppendLine("No slot is named for a skill.");
                return;
            }
            report.AppendLine();
            report.AppendLine("The scripts that use each skill:");
            foreach (var slot in skills)
            {
                var scripts = uses.Where(x => x.Slot == slot).GroupBy(x => (x.File, x.Script)).ToList();
                report.AppendLine($"  {Slot(slot)}: " + (scripts.Count == 0 ? "none" :
                    string.Join("; ", scripts.Select(g => $"{g.Key.File.Name} {g.Key.Script.ChunkID} \"{Survey.Clean(g.Key.Script.ChunkLabel)}\" ({(g.Any(x => x.Sets) ? "sets" : "reads")})"))));
            }

            report.AppendLine();
            report.AppendLine("The scripts that use the skills that didn't ship, whole:");
            var listed = 0;
            foreach (var group in uses.Where(x => skills.Contains(x.Slot) && !ShippedSkill.IsMatch(OldPersonData[x.Slot])).GroupBy(x => (x.File, x.Script)))
            {
                if (listed++ >= MaxOldScripts) { report.AppendLine("(no more: the limit was reached)"); break; }
                WholeScript(report, files, group.Key.File, group.Key.Script);
            }
            if (listed == 0) report.AppendLine("None: no step reads or sets them.");
        }

        // ---- menus ----

        /// <summary>Every entry of every old menu table: its text, the scripts it runs and tests with, and what it advertises.</summary>
        private static void Menus(StringBuilder report, List<OldFile> files)
        {
            var total = 0;
            foreach (var file in OldOnes(files).OrderBy(x => x.Where, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var chunk in file.Old.Of("TTAB").OrderBy(x => x.Id))
                {
                    var entries = Iff10.Table(chunk.Data, out var version, out var problem);
                    if (version >= 4)
                    {
                        TTAB later = null;
                        try { later = file.Iff.Get<TTAB>(chunk.Id); } catch (Exception e) { problem = "the engine could not read it: " + e.Message; }
                        if (later?.Interactions != null)
                        {
                            problem = null;
                            entries = later.Interactions.Select(x => new Iff10.Entry
                            {
                                Action = x.ActionFunction, Test = x.TestFunction, Field = (ushort)x.TTAIndex, Attenuation = x.AttenuationValue,
                                Ads = (x.MotiveEntries ?? new TTABMotiveEntry[0]).Select(m => m.EffectRangeDelta).ToArray(),
                                Flags = (uint)x.Flags, Later = true,
                            }).ToList();
                        }
                    }
                    var texts = file.Old.Live.FirstOrDefault(x => x.Type == "TTAs" && x.Id == chunk.Id) ?? file.Old.Live.FirstOrDefault(x => x.Type == "STR#" && x.Id == chunk.Id);
                    var names = texts != null ? Iff10.Strings(texts.Data, out _) : null;
                    var objects = List<OBJD>(file.Iff).Where(x => x.TreeTableID == chunk.Id).Select(x => $"\"{Survey.Clean(x.ChunkLabel)}\"").ToList();
                    report.AppendLine($"{file.Name} menu {chunk.Id} \"{Survey.Clean(chunk.Label)}\"{(objects.Count > 0 ? " of " + string.Join(", ", objects) : "")}: version {version}, {entries.Count} entries" +
                        (problem != null ? $" ({problem})" : "") + (names == null ? ", no texts with its number" : ""));
                    if (entries.Count == 0 && problem != null)
                    {
                        var dump = new StringBuilder();
                        Dump(dump, chunk.Data, 0, 96);
                        report.Append(dump);
                    }
                    for (int i = 0; i < entries.Count; i++)
                    {
                        total++;
                        var e = entries[i];
                        var ads = e.Ads.Select((v, j) => (v, j)).Where(x => x.v != 0).Select(x => $"{(x.j < 16 ? ((VMMotive)x.j).ToString() : "need " + x.j)} {x.v}").ToList();
                        var name = (names != null && i < names.Count) ? Survey.Clean(names[i]) : "";
                        report.AppendLine($"  {i} \"{name}\": runs {e.Action} \"{ScriptName(files, file, e.Action)}\", test {(e.Test == 0 ? "none" : $"{e.Test} \"{ScriptName(files, file, e.Test)}\"")}" +
                            (ads.Count > 0 ? ", advertises " + string.Join(", ", ads) : "") + $", attenuation {e.Attenuation:0.###}" +
                            (e.Later ? $", text {e.Field}, flags 0x{e.Flags:x}" : (e.Field != 0xa3a3 ? $", unknown field 0x{e.Field:x4}" : "")));
                    }
                }
                //where the menu texts are kept is not known yet: every text list of the file, briefly
                var lists = file.Old.Live.Where(x => x.Type == "STR#" || x.Type == "CATS" || x.Type == "TTAs" || x.Type == "CTSS").OrderBy(x => x.Type).ThenBy(x => x.Id).ToList();
                if (file.Old.Of("TTAB").Any() && lists.Count > 0)
                    report.AppendLine("  texts: " + string.Join("; ", lists.Select(x =>
                    {
                        var t = Iff10.Strings(x.Data, out _) ?? new List<string>();
                        return $"{Iff10.Printable(x.Type)} {x.Id} ({t.Count}): " + string.Join(" | ", t.Take(6).Select(Survey.Clean)) + (t.Count > 6 ? " ..." : "");
                    })));
            }
            report.AppendLine($"{total} entries. Needs are named in 1.0's order, which this copy may not share.");
        }

        // ---- careers ----

        /// <summary>The job and level files the 1999 build has instead of 1.0's career tables: their constants, texts and scripts.</summary>
        private static void CareerFiles(StringBuilder report, List<OldFile> files)
        {
            var jobs = OldOnes(files).Where(x => Regex.IsMatch(Path.GetFileNameWithoutExtension(x.Name), @"^(job|level|career)", RegexOptions.IgnoreCase))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (jobs.Count == 0) { report.AppendLine("There are no job or level files."); return; }
            foreach (var file in jobs)
            {
                var objects = List<OBJD>(file.Iff).Select(x => $"\"{Survey.Clean(x.ChunkLabel)}\" (0x{x.GUID:x8})");
                report.AppendLine($"{file.Where}: {string.Join(", ", objects)}");
                foreach (var chunk in file.Old.Of("BCON").OrderBy(x => x.Id))
                {
                    var d = chunk.Data;
                    if (d.Length < 2) continue;
                    var count = Math.Min(d[0], (d.Length - 2) / 2);
                    var values = Enumerable.Range(0, count).Select(i => (short)Iff10.LE16(d, 2 + i * 2)).ToList();
                    var texts = file.Old.Live.FirstOrDefault(x => (x.Type == "STR#" || x.Type == "TRCN") && x.Id == chunk.Id);
                    var names = texts != null ? Iff10.Strings(texts.Data, out _) : null;
                    report.AppendLine($"  constants {chunk.Id} \"{Survey.Clean(chunk.Label)}\": " +
                        string.Join(", ", values.Select((v, i) => (names != null && i < names.Count && names[i] != "") ? $"{i} {Survey.Clean(names[i])} = {v}" : $"{i} = {v}")));
                }
                foreach (var (text, lines) in Tables(file))
                    report.AppendLine($"  text {text.ChunkID} \"{Survey.Clean(text.ChunkLabel)}\": " + string.Join(" | ", lines.Select((l, i) => $"{i} {Survey.Clean(l)}").Where(x => !x.EndsWith(" "))));
                report.AppendLine("  scripts: " + string.Join("; ", List<BHAV>(file.Iff).OrderBy(x => x.ChunkID).Select(x => $"{x.ChunkID} {Survey.Clean(x.ChunkLabel)}")));
            }
        }

        // ---- every script ----

        private static void AllScripts(StringBuilder report, List<OldFile> files)
        {
            foreach (var file in OldOnes(files).OrderBy(x => x.Where, StringComparer.OrdinalIgnoreCase))
            {
                var objects = List<OBJD>(file.Iff).Select(x => $"\"{Survey.Clean(x.ChunkLabel)}\" (0x{x.GUID:x8}, ${x.Price})").ToList();
                var scripts = List<BHAV>(file.Iff).OrderBy(x => x.ChunkID).Select(x => $"{x.ChunkID} {Survey.Clean(x.ChunkLabel)}").ToList();
                if (objects.Count == 0 && scripts.Count == 0) continue;
                report.AppendLine($"{file.Where}{(objects.Count > 0 ? ": " + string.Join(", ", objects) : "")}{(SemiGlobalName(file) is string g ? $", shares {g}" : "")}");
                var line = new StringBuilder("   ");
                foreach (var script in scripts)
                {
                    if (line.Length + script.Length > 150) { report.AppendLine(line.ToString().TrimEnd(';', ' ')); line.Clear().Append("   "); }
                    line.Append(' ').Append(script).Append(';');
                }
                if (line.Length > 3) report.AppendLine(line.ToString().TrimEnd(';'));
            }
        }
    }
}
