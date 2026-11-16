/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CutContent
{
    /// <summary>
    /// Where objects still say which skill they were meant for. Shared by the survey and the survey of an older copy.
    ///
    /// The 1.0 base game's Behavior.iff lists, as "personality ads", what an interaction's pull on a Sim can be scaled by:
    /// 1 to 12 the personality traits, then ten skills, 13 Cleaning, 14 Cooking, 15 Social, 16 Repair, 17 Gardening,
    /// 18 Music, 19 Creative, 20 Literacy, 21 Physical, 22 Logic, in the same order as person data 9 to 18. Each menu entry
    /// gives each need it advertises one of these. An entry scaled by 13, 17, 18 or 20 was meant for a cut skill.
    /// Objects also carry RatingSkillFlags, the skills buy mode says they build.
    /// </summary>
    public static class SkillLinks
    {
        public static readonly Dictionary<int, string> Cut = new Dictionary<int, string> { { 13, "cleaning" }, { 17, "gardening" }, { 18, "music" }, { 20, "literacy" } };

        /// <summary>Menu entries whose pull is scaled by a cut skill.</summary>
        public static void Ads(StringBuilder report, string fileName, IEnumerable<TTAB> tables, Func<ushort, TTAs> strings, ref int found)
        {
            foreach (var table in tables)
            {
                TTAs labels = null;
                try { labels = strings(table.ChunkID); } catch { }
                foreach (var entry in table.Interactions)
                {
                    if (entry.MotiveEntries == null) continue;
                    var linked = entry.MotiveEntries.Select((m, i) => (m, i)).Where(x => Cut.ContainsKey(x.m.PersonalityModifier)).ToList();
                    if (linked.Count == 0) continue;
                    found++;
                    var label = Survey.Clean(labels?.GetString((int)entry.TTAIndex));
                    var ads = string.Join(", ", linked.Select(x => $"{(VMMotive)x.i} {x.m.EffectRangeMinimum}+{x.m.EffectRangeDelta} scaled by {Cut[x.m.PersonalityModifier]}"));
                    report.AppendLine($"{fileName} table {table.ChunkID} entry {entry.TTAIndex} \"{label}\" (script {entry.ActionFunction}): {ads}");
                }
            }
        }

        /// <summary>
        /// Career levels that ask for the unnamed skills. 1.0's Behavior.iff names a level's requirements "min cook skill",
        /// "min repair skill", "min social skill", "min body skill", "min logic skill", "min creative skill", then "min req 7",
        /// "min req 8" and "min req 9": room for three more skills. A level that fills one in says how a cut skill was to be used.
        /// </summary>
        public static void Careers(StringBuilder report, string fileName, IEnumerable<CARR> careers, ref int levels, ref int asking)
        {
            foreach (var career in careers)
            {
                if (career.JobLevels == null) continue;
                for (int i = 0; i < career.JobLevels.Length; i++)
                {
                    var level = career.JobLevels[i];
                    levels++;
                    if (level?.MinRequired == null || level.MinRequired.Length < 10) continue;
                    if (level.MinRequired[7] == 0 && level.MinRequired[8] == 0 && level.MinRequired[9] == 0) continue;
                    asking++;
                    report.AppendLine($"{fileName} career {career.ChunkID} \"{Survey.Clean(career.Name ?? career.ChunkLabel)}\" level {i} \"{Survey.Clean(level.JobName)}\": " +
                        $"req 7 {level.MinRequired[7]}, req 8 {level.MinRequired[8]}, req 9 {level.MinRequired[9]} " +
                        $"(and cooking {level.MinRequired[1]}, repair {level.MinRequired[2]}, social {level.MinRequired[3]}, body {level.MinRequired[4]}, logic {level.MinRequired[5]}, creative {level.MinRequired[6]})");
                }
            }
        }

        /// <summary>Objects with skill flags, grouped by the flags, so a flag no shipped skill uses stands out.</summary>
        public static void Flags(StringBuilder report, IEnumerable<(string file, OBJD objd)> objects)
        {
            var groups = objects.Where(x => x.objd.RatingSkillFlags != 0).GroupBy(x => x.objd.RatingSkillFlags).OrderBy(x => x.Key);
            foreach (var group in groups)
            {
                var bits = string.Join("+", Enumerable.Range(0, 16).Where(b => (group.Key & (1 << b)) != 0).Select(b => "bit " + b));
                var names = group.Select(x => $"{Survey.Clean(x.objd.ChunkLabel)} ({x.file})").Distinct().ToList();
                report.AppendLine($"{bits}: {names.Count} objects: {string.Join(", ", names.Take(10))}{(names.Count > 10 ? ", ..." : "")}");
            }
        }
    }
}
