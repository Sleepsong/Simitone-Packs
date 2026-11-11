/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CutContent
{
    /// <summary>
    /// One cut interaction: which objects it is on, and what its menu entry is called.
    /// Both are matched on English text, so these only work with the game in English.
    /// </summary>
    public class InteractionRule
    {
        public string Name;
        /// <summary>Any of these in the object's name or file name. Empty means any object.</summary>
        public string[] Objects = new string[0];
        /// <summary>Any of these in the interaction's menu text.</summary>
        public string[] Labels = new string[0];
        /// <summary>The interaction was made for children: lift a "no children" mark as well.</summary>
        public bool ForChildren;

        public int Found;
        public int Changed;
        public List<string> Seen = new List<string>();

        public string Status
        {
            get
            {
                if (Found == 0) return "not seen yet";
                var where = string.Join(", ", Seen.Take(3)) + ((Seen.Count > 3) ? ", ..." : "");
                if (Changed == 0) return $"found ({where}), but not switched off by a mark this pack can lift";
                return $"switched on ({where})";
            }
        }

        public bool MatchesObject(string name)
        {
            return Objects.Length == 0 || Objects.Any(x => name.Contains(x));
        }

        public bool MatchesLabel(string label)
        {
            return Labels.Any(x => label.Contains(x));
        }
    }

    /// <summary>
    /// Switches cut interactions back on.
    ///
    /// The game can leave an interaction in an object's table and still keep it off the menu in three ways this
    /// pack knows of: a "debug only" mark, an age mark, or a check script that always says no. The first two are
    /// lifted here, in memory, the first time anything asks the object for an interaction. The third is only
    /// overridden with "cutcontent force on", because a check that says no can also be the game's own rule for
    /// when the interaction is allowed.
    ///
    /// An interaction that was taken out of the table altogether is not brought back here. The survey lists the
    /// scripts left behind, which is what is needed to add those by hand.
    /// </summary>
    public static class Interactions
    {
        public static bool Force;

        public static readonly InteractionRule[] Rules =
        {
            new InteractionRule { Name = "Hug a pine or palm tree (Vacation)", Objects = new[] { "tree", "palm", "pine" }, Labels = new[] { "hug" } },
            new InteractionRule { Name = "Children kiss toads (Makin' Magic)", Objects = new[] { "toad" }, Labels = new[] { "kiss" }, ForChildren = true },
            new InteractionRule { Name = "Lock and unlock doors (Superstar, Makin' Magic)", Objects = new[] { "door" }, Labels = new[] { "lock" } },
            new InteractionRule { Name = "Sell a litter to the Pet Trainer (Unleashed)", Objects = new[] { "trainer" }, Labels = new[] { "litter", "sell" } },
            new InteractionRule { Name = "Hire a limousine (Hot Date)", Labels = new[] { "limo" } },
            new InteractionRule { Name = "Investment offers by telephone", Labels = new[] { "invest" } },
        };

        private class Match
        {
            public TTABInteraction Entry;
            public ushort Check;
        }

        private static HashSet<TTAB> Tables = new HashSet<TTAB>();
        private static List<Match> Matches = new List<Match>();
        private static bool Forced;

        /// <summary>Before the engine looks an interaction up in an object's table.</summary>
        public static void BeforeGetAction(VMEntity __instance, VMContext context, bool global)
        {
            if (global || context?.VM == null || !context.VM.TS1) return;
            try
            {
                var table = __instance.TreeTable;
                if (table != null && Tables.Add(table)) Search(__instance, table);
                if (Forced != Force)
                {
                    Forced = Force;
                    foreach (var match in Matches) match.Entry.TestFunction = Forced ? (ushort)0 : match.Check;
                }
            }
            catch (Exception e)
            {
                CutContentPack.Log("couldn't look through the interactions of " + __instance + ": " + e);
            }
        }

        private static void Search(VMEntity entity, TTAB table)
        {
            var strings = entity.TreeTableStrings;
            if (strings == null) return;
            var name = (((entity.Object?.OBJ?.ChunkLabel) ?? "") + " " + ((entity.Object?.Resource?.Name) ?? "")).ToLowerInvariant();
            foreach (var rule in Rules)
            {
                if (!rule.MatchesObject(name)) continue;
                foreach (var entry in table.Interactions)
                {
                    var label = strings.GetString((int)entry.TTAIndex);
                    if (string.IsNullOrEmpty(label) || !rule.MatchesLabel(label.ToLowerInvariant())) continue;

                    var before = entry.Flags;
                    entry.Flags &= ~TTABFlags.Debug;
                    if (rule.ForChildren) entry.Flags &= ~(TTABFlags.TS1NoChild | TTABFlags.TS1NoDemoChild);

                    rule.Found++;
                    var what = $"\"{label}\" on {entity.Object?.OBJ?.ChunkLabel}";
                    if (!rule.Seen.Contains(what)) rule.Seen.Add(what);
                    if (entry.Flags != before) rule.Changed++;
                    Matches.Add(new Match { Entry = entry, Check = entry.TestFunction });
                    if (Forced) entry.TestFunction = 0;
                    CutContentPack.Log($"{rule.Name}: {what}, marks {(uint)before:x} -> {(uint)entry.Flags:x}, check script {entry.TestFunction}");
                }
            }
        }
    }
}
