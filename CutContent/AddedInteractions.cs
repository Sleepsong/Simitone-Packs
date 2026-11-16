/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CutContent
{
    /// <summary>
    /// An interaction whose script Maxis finished, or nearly, but never put in the object's menu.
    /// The object is recognised by having a script with this exact name.
    /// </summary>
    public class AddedInteraction
    {
        public string Name;
        /// <summary>The name of the script the interaction runs, as it is in the object's file.</summary>
        public string Action;
        /// <summary>The name of the script that says whether it is allowed, or null for none.</summary>
        public string Check;
        /// <summary>What the menu shows. A check script can change it (the doors' says Lock or Unlock).</summary>
        public string Label;
        /// <summary>Marks for the entry, as the game's own entries have them.</summary>
        public TTABFlags Flags;

        public int Objects;
        public List<string> Seen = new List<string>();

        public string Status
        {
            get
            {
                if (Objects == 0) return "not seen yet";
                return $"added to the menu ({string.Join(", ", Seen.Take(4))}{((Seen.Count > 4) ? ", ..." : "")})";
            }
        }
    }

    /// <summary>
    /// Puts interactions back in objects' menus. The survey found these scripts in the game's files with no menu
    /// entry leading to them. The game's files are not changed: each object gets a copy of its menu table with the
    /// entry added, in memory, when it is created.
    /// </summary>
    public static class AddedInteractions
    {
        public static readonly AddedInteraction[] Rules =
        {
            // trees2.iff (palm) and Trees4.iff (fir, spruce, pine): the script plays the treehugger animations. adults only, as only adult animations exist
            new AddedInteraction { Name = "Hug a tree (Vacation)", Action = "Tree Hug", Label = "Hug",
                Flags = TTABFlags.AllowVisitors | TTABFlags.TS1NoChild | TTABFlags.TS1NoDemoChild },
            // Doors3_SP.iff, Doors3SP2.iff, Doors4.iff, DoorsUnleashed.iff and others: the check names the entry Lock or Unlock.
            // only the household can lock their doors
            new AddedInteraction { Name = "Lock and unlock doors (Superstar, Makin' Magic)", Action = "Interaction Lock/unlock", Check = "Interaction Lock/unlock TEST", Label = "Lock" },
        };

        private class Added
        {
            public TTAB Table;
            public TTAs Strings;
        }

        /// <summary>For each object file, its tables with the entries added. Keyed on the file so they go when it does.</summary>
        private static ConditionalWeakTable<GameIffResource, Dictionary<object, Added>> Done = new ConditionalWeakTable<GameIffResource, Dictionary<object, Added>>();
        private static readonly object NoTable = new object();

        /// <summary>After an object is created, and after a part of a larger object takes its leader's table.</summary>
        public static void AfterSetup(VMEntity __instance)
        {
            try
            {
                Apply(__instance);
            }
            catch (Exception e)
            {
                CutContentPack.Log("couldn't add interactions to " + __instance?.Object?.OBJ?.ChunkLabel + ": " + e);
            }
        }

        private static void Apply(VMEntity entity)
        {
            var res = entity?.Object?.Resource;
            if (res == null || entity is VMAvatar) return;
            //only an object with a menu, or the leader of a larger one. its parts take its table
            if (entity.Object.OBJ.TreeTableID == 65535 && entity.TreeTable == null) return;

            var found = Done.GetOrCreateValue(res);
            var key = (object)entity.TreeTable ?? NoTable;
            if (!found.TryGetValue(key, out var added))
            {
                added = Make(entity, res);
                found[key] = added;
            }
            if (added == null) return;
            entity.TreeTable = added.Table;
            entity.TreeTableStrings = added.Strings;
        }

        private static Added Make(VMEntity entity, GameIffResource res)
        {
            var scripts = res.List<BHAV>();
            if (scripts == null || scripts.Count == 0) return null;
            var byName = new Dictionary<string, BHAV>();
            foreach (var script in scripts)
            {
                var label = (script.ChunkLabel ?? "").TrimEnd('\0');
                if (!byName.ContainsKey(label)) byName[label] = script;
            }

            var rules = new List<(AddedInteraction rule, BHAV action, BHAV check)>();
            foreach (var rule in Rules)
            {
                if (!byName.TryGetValue(rule.Action, out var action)) continue;
                BHAV check = null;
                if (rule.Check != null && !byName.TryGetValue(rule.Check, out check)) continue;
                //already in the menu?
                if (entity.TreeTable != null && entity.TreeTable.Interactions.Any(x => x.ActionFunction == action.ChunkID)) continue;
                rules.Add((rule, action, check));
            }
            if (rules.Count == 0) return null;

            var table = Copy(entity.TreeTable, out var motives);
            var strings = Copy(entity.TreeTableStrings);
            foreach (var (rule, action, check) in rules)
            {
                //the next free number that both the table and its strings agree on. menus count interactions in a byte
                uint index = (uint)Math.Max(strings.Length, table.Interactions.Length == 0 ? 0 : (int)table.Interactions.Max(x => x.TTAIndex) + 1);
                while (table.InteractionByIndex.ContainsKey(index)) index++;
                if (index > 255) { CutContentPack.Log($"{rule.Name}: no room left in the menu of {entity.Object.OBJ.ChunkLabel}"); continue; }

                SetString(strings, (int)index, rule.Label);
                table.InsertInteraction(new TTABInteraction
                {
                    ActionFunction = action.ChunkID,
                    TestFunction = check?.ChunkID ?? 0,
                    MotiveEntries = new TTABMotiveEntry[motives], //no motive effects: Sims never choose it on their own
                    Flags = rule.Flags,
                    TTAIndex = index,
                    AttenuationCode = 0,
                    AttenuationValue = 0,
                    AutonomyThreshold = 0,
                    JoiningIndex = -1,
                }, table.Interactions.Length);

                rule.Objects++;
                var what = (entity.Object.OBJ.ChunkLabel ?? "").TrimEnd('\0');
                if (!rule.Seen.Contains(what)) rule.Seen.Add(what);
                CutContentPack.Log($"{rule.Name}: added \"{rule.Label}\" to {what} ({entity.Object.Resource.MainIff?.Filename}), entry {index}, script {action.ChunkID}, check {check?.ChunkID ?? 0}");
            }
            return new Added { Table = table, Strings = strings };
        }

        private static TTAB Copy(TTAB original, out int motives)
        {
            var copy = new TTAB();
            motives = 16;
            if (original == null) return copy;
            copy.ChunkID = original.ChunkID;
            copy.ChunkLabel = original.ChunkLabel;
            copy.Interactions = original.Interactions.ToArray();
            copy.InteractionByIndex = new Dictionary<uint, TTABInteraction>(original.InteractionByIndex);
            copy.InitAutoInteractions();
            if (original.Interactions.Length > 0) motives = original.Interactions[0].MotiveEntries.Length;
            return copy;
        }

        private static TTAs Copy(TTAs original)
        {
            var copy = new TTAs();
            if (original == null) return copy;
            copy.ChunkID = original.ChunkID;
            copy.ChunkLabel = original.ChunkLabel;
            for (int i = 0; i < copy.LanguageSets.Length && i < original.LanguageSets.Length; i++)
            {
                var set = original.LanguageSets[i];
                copy.LanguageSets[i] = new STRLanguageSet { Strings = (set?.Strings ?? new STRItem[0]).ToArray() };
            }
            return copy;
        }

        /// <summary>Sets one string in every language that has strings, as the game falls back to English for the rest.</summary>
        private static void SetString(TTAs strings, int index, string value)
        {
            for (int i = 0; i < strings.LanguageSets.Length; i++)
            {
                var set = strings.LanguageSets[i];
                if (set == null || (i > 0 && set.Strings.Length == 0)) continue;
                var list = set.Strings.ToList();
                while (list.Count <= index) list.Add(new STRItem("") { LanguageCode = (byte)(i + 1) });
                list[index] = new STRItem(value) { LanguageCode = (byte)(i + 1) };
                set.Strings = list.ToArray();
            }
        }
    }
}
