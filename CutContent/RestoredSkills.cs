/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// The gardening, music and literacy skills.
    ///
    /// What is known of them: in the build from 1999 (the earliest copy surveyed) every skill was a fixed number from 0 to 10
    /// a character started with, and only work could be learned (studying at the computer, which "Sims won't do unless
    /// they're in a good mood"). Gardening changed how Sims took to plants: above 5, a Sim whistled happily while watering,
    /// liked looking at flowers, and watered a flower after replanting it; below, they sang or sighed with boredom. 1.0's
    /// flowers still ask for gardening above 550 before the liking sound. Music and literacy had no objects at all. Hot Date
    /// then gave all three of their places in person data (13, 14, 16) to interests.
    ///
    /// So, restored where there is something to restore and made up where there isn't:
    ///   - a household Sim learns a skill by doing what it is about, at the object, and not while walking there: gardening by
    ///     tending plants and flowers, music by playing an instrument, literacy by reading or studying. As with the 1999
    ///     computer, not in a bad mood. A point a minute to level 5, then more slowly, up to level 10 (1000 points);
    ///   - from level 5, as the 1999 gardeners did, a Sim enjoys the skill's activities: a little fun each minute, and for
    ///     gardening that includes looking at flowers.
    /// The skills are kept by the pack, not in person data: the scan of every installed pack's scripts found only one of the
    /// slots a Sim's record keeps unused (38), and three are needed. They are kept per neighbourhood, by each Sim's neighbour
    /// number, written when the game saves a lot and read when one loads, so unsaved progress goes when the game's does.
    /// </summary>
    public static class RestoredSkills
    {
        public enum Skill { Gardening, Music, Literacy }
        public static readonly string[] Names = { "Gardening", "Music", "Literacy" };

        public const int Max = 1000;
        public static string PackId;
        public static string Status = "not seen yet (a lot has to load)";
        public static int Minutes; //minutes of learning this session

        /// <summary>
        /// What teaches each skill: the object (its name or file) and what the Sim is doing with it (the menu text).
        /// "Enjoy" is done for fun only: it pleases a skilled Sim but teaches nothing.
        /// </summary>
        private static readonly (Skill skill, Regex what, Regex learn, Regex enjoy)[] Activities =
        {
            (Skill.Gardening,
                new Regex(@"flower|plant|garden|shrub|hedge|bush|topiar|vegetable|tomato|pumpkin|rose|tulip|daisy|daff|cactus|bonsai|orchid|lily|bloom|herb|greenhouse", RegexOptions.IgnoreCase),
                new Regex(@"water|weed|tend|plant|harvest|prune|trim|replant|fertili|spray|clip|pick(?!\s*up)|sow|grow|mulch|compost", RegexOptions.IgnoreCase),
                new Regex(@"look|smell|admire|view|sniff", RegexOptions.IgnoreCase)),
            (Skill.Music,
                new Regex(@"piano|guitar|drum|bass|violin|cello|harp|trumpet|sax|keyboard|organ|bongo|accordion|banjo|xylophone|tuba|flute|harmonica|instrument|synth|lute|mandolin", RegexOptions.IgnoreCase),
                new Regex(@"play|practi|jam|perform|rock|compose|serenade|tune", RegexOptions.IgnoreCase),
                new Regex(@"listen|watch", RegexOptions.IgnoreCase)),
            (Skill.Literacy,
                new Regex(@"book|shelf|librar|newspaper|paper|magazine|novel|journal|tome|spellbook|bard", RegexOptions.IgnoreCase),
                new Regex(@"read|study|browse|learn|research", RegexOptions.IgnoreCase),
                null),
        };

        /// <summary>The skills of each Sim of the neighbourhood that is open, by neighbour number.</summary>
        private static Dictionary<short, short[]> Book = new Dictionary<short, short[]>();
        private static string BookKey;

        /// <summary>What was seen teaching, once each, to show what matched in this game.</summary>
        public static readonly List<string> Seen = new List<string>();

        private class Clock { public int LastMinute = -1; }
        private static readonly ConditionalWeakTable<VM, Clock> Clocks = new ConditionalWeakTable<VM, Clock>();
        private static readonly ConditionalWeakTable<VMAvatar, Clock> Paces = new ConditionalWeakTable<VMAvatar, Clock>();

        public static int Get(VMAvatar sim, Skill skill)
        {
            var id = sim.GetPersonData(VMPersonDataVariable.NeighborId);
            return (id > 0 && Book.TryGetValue(id, out var skills)) ? skills[(int)skill] : 0;
        }

        public static void Set(VMAvatar sim, Skill skill, int value)
        {
            var id = sim.GetPersonData(VMPersonDataVariable.NeighborId);
            if (id <= 0) return;
            if (!Book.TryGetValue(id, out var skills)) Book[id] = skills = new short[Names.Length];
            skills[(int)skill] = (short)Math.Max(0, Math.Min(Max, value));
        }

        // ---- keeping them ----

        /// <summary>
        /// A name for the neighbourhood a lot belongs to, to keep the skills under: every lot of a neighbourhood, home,
        /// downtown or away, is a file in the same folder, so the folder above it stands for the neighbourhood.
        /// </summary>
        private static string NeighbourhoodOf(string lotPath)
        {
            if (string.IsNullOrEmpty(lotPath)) return null;
            var houses = Path.GetDirectoryName(Path.GetFullPath(lotPath));
            var top = Path.GetDirectoryName(houses) ?? houses;
            return Path.Combine(top, "Neighborhood.iff");
        }

        /// <summary>A lot has loaded: read the neighbourhood's skills as last saved, dropping anything learned and not saved.</summary>
        public static void OnLotLoaded(VM vm, string lotPath)
        {
            var key = NeighbourhoodOf(lotPath);
            if (key == null) return;
            BookKey = key;
            Book = new Dictionary<short, short[]>();
            var data = LotPackData.Read(PackId, key);
            if (data != null)
            {
                foreach (var line in Encoding.UTF8.GetString(data).Split('\n'))
                {
                    var parts = line.Trim().Split(' ');
                    if (parts.Length < 1 + Names.Length || !short.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                    var skills = new short[Names.Length];
                    for (int i = 0; i < Names.Length; i++) short.TryParse(parts[1 + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out skills[i]);
                    Book[id] = skills;
                }
            }
            Status = "restored and improvised: household Sims learn gardening by tending plants, music by playing instruments, literacy by reading";
        }

        /// <summary>A lot is saved: keep the neighbourhood's skills with it.</summary>
        public static void OnLotSaved(VM vm, string lotPath)
        {
            var key = NeighbourhoodOf(lotPath);
            if (key == null || key != BookKey) return;
            var text = new StringBuilder();
            foreach (var sim in Book.Where(x => x.Value.Any(v => v > 0)).OrderBy(x => x.Key))
                text.Append(sim.Key.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(string.Join(" ", sim.Value.Select(v => v.ToString(CultureInfo.InvariantCulture)))).Append('\n');
            LotPackData.Write(PackId, key, text.Length == 0 ? null : Encoding.UTF8.GetBytes(text.ToString()));
        }

        // ---- learning ----

        /// <summary>Once a game minute: each household Sim doing a skill's activity at its object learns, or enjoys it.</summary>
        public static void AfterTick(VM __instance)
        {
            var vm = __instance;
            if (!vm.TS1 || BookKey == null) return;
            var clock = Clocks.GetOrCreateValue(vm);
            var minute = vm.Context.Clock.Hours * 60 + vm.Context.Clock.Minutes;
            if (minute == clock.LastMinute) return;
            clock.LastMinute = minute;
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return;
            try
            {
                foreach (var sim in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
                {
                    if (sim.IsPet || sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != family.ChunkID) continue;
                    var action = sim.Thread?.ActiveAction;
                    var target = action?.Callee;
                    if (target == null || target == sim || action.Name == null) continue;
                    if (sim.Thread.Stack.LastOrDefault() is VMRoutingFrame) continue; //still on the way
                    if (target.Position.Level != sim.Position.Level || LotTilePos.Distance(target.Position, sim.Position) > 16 * 3) continue;
                    var what = (target.Object?.OBJ?.ChunkLabel ?? "") + " " + Path.GetFileName(target.Object?.Resource?.MainIff?.Filename ?? "");
                    foreach (var activity in Activities)
                    {
                        if (!activity.what.IsMatch(what)) continue;
                        var learning = activity.learn.IsMatch(action.Name);
                        var enjoying = learning || (activity.enjoy?.IsMatch(action.Name) ?? false);
                        if (!enjoying) continue;
                        Note(activity.skill, learning, action.Name, target.Object?.OBJ?.ChunkLabel);
                        var skill = Get(sim, activity.skill);
                        if (learning && sim.GetMotiveData(VMMotive.Mood) > -25 && skill < Max && Paced(sim, skill))
                        {
                            Set(sim, activity.skill, skill + 1);
                            Minutes++;
                        }
                        if (skill >= 500) sim.SetMotiveData(VMMotive.Fun, (short)Math.Min(100, sim.GetMotiveData(VMMotive.Fun) + 1));
                        break;
                    }
                }
            }
            catch (Exception e) { CutContentPack.Log("skills: " + e.Message); }
        }

        /// <summary>A point a minute to level 5, every second minute to 8, then every third.</summary>
        private static bool Paced(VMAvatar sim, int skill)
        {
            var every = skill < 500 ? 1 : skill < 800 ? 2 : 3;
            var pace = Paces.GetOrCreateValue(sim);
            pace.LastMinute = (pace.LastMinute + 1) % every;
            return pace.LastMinute == 0;
        }

        private static void Note(Skill skill, bool learning, string doing, string what)
        {
            var line = $"{Names[(int)skill].ToLowerInvariant()} {(learning ? "learned" : "enjoyed")}: \"{doing}\" on \"{what}\"";
            if (Seen.Contains(line) || Seen.Count >= 40) return;
            Seen.Add(line);
            CutContentPack.Log("skills: " + line);
        }

        // ---- the skills panel ----

        private static readonly ConditionalWeakTable<UIJobSubpanel, UISkillDisplay[]> Bars = new ConditionalWeakTable<UIJobSubpanel, UISkillDisplay[]>();

        /// <summary>Bars under and beside Cleaning's: gardening below it, music and literacy in a column of their own.</summary>
        public static void AfterPanel(UIJobSubpanel __instance)
        {
            try
            {
                var places = new[] { (334 + 3 * 140, 1), (334 + 4 * 140, 0), (334 + 4 * 140, 1) };
                var bars = new UISkillDisplay[Names.Length];
                for (int i = 0; i < Names.Length; i++)
                {
                    var (x, row) = places[i];
                    var label = new UILabel { Caption = Names[i], Position = new Vector2(x - 2, 11 + 60 * row) };
                    label.CaptionStyle = label.CaptionStyle.Clone();
                    label.CaptionStyle.Color = UIStyle.Current.Text;
                    label.CaptionStyle.Size = 15;
                    __instance.Add(label);
                    bars[i] = new UISkillDisplay { Position = new Vector2(x, 35 + 60 * row) };
                    __instance.Add(bars[i]);
                }
                Bars.AddOrUpdate(__instance, bars);
            }
            catch (Exception e) { CutContentPack.Log("skills: couldn't add the bars: " + e.Message); }
        }

        public static void AfterPanelUpdate(UIJobSubpanel __instance)
        {
            if (!Bars.TryGetValue(__instance, out var bars)) return;
            var sim = __instance.Game?.SelectedAvatar;
            if (sim == null) return;
            for (int i = 0; i < bars.Length; i++) bars[i].Value = Get(sim, (Skill)i) / 100;
        }

        // ---- commands ----

        /// <summary>
        /// cutcontent skills                  the household's gardening, music and literacy, and what has taught them
        /// cutcontent skills SKILL POINTS     set the selected Sim's skill (0 to 1000; a level is 100), to try things out
        /// </summary>
        public static bool Command(string[] args, VMAvatar selected, VM vm)
        {
            if (args.Length >= 2)
            {
                var index = Array.FindIndex(Names, x => x.StartsWith(args[0], StringComparison.OrdinalIgnoreCase));
                if (index < 0 || !int.TryParse(args[1], out var points) || selected == null) return false;
                Set(selected, (Skill)index, points);
                return true;
            }
            var text = new StringBuilder(Status + $". {Book.Count(x => x.Value.Any(v => v > 0))} Sims of this neighbourhood have some.\n\n");
            var family = vm?.TS1State?.CurrentFamily;
            foreach (var sim in vm?.Context.ObjectQueries.Avatars.OfType<VMAvatar>() ?? Enumerable.Empty<VMAvatar>())
            {
                if (sim.IsPet || family == null || sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != family.ChunkID) continue;
                text.AppendLine($"{sim.Name}: " + string.Join(", ", Names.Select((n, i) => $"{n.ToLowerInvariant()} {Get(sim, (Skill)i)}")));
            }
            text.AppendLine();
            text.AppendLine(Seen.Count == 0 ? "Nothing has taught a skill yet this session." : "Seen this session:\n" + string.Join("\n", Seen));
            CutContentPack.Show("Gardening, music and literacy", text.ToString());
            return true;
        }
    }
}
