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
