/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content.TS1;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Primitives;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Moodlets
{
    /// <summary>
    /// Moodlets, after The Sims 3, a first stage: things that happen give a Sim a mood boost or a mood penalty for a
    /// while.
    ///
    /// The engine is not changed. The pack hangs on it with Harmony patches, four that it needs:
    ///
    ///  1. The game has worked out a Sim's mood: add their moodlets to it (Moods.AfterDecay).
    ///  2. A lot has ticked: count the time moodlets have left, and look at the Sims' jobs.
    ///  3. The game has loaded a neighbourhood: forget the moodlets of the one before.
    ///  4. The game has saved: keep the moodlets with it.
    ///
    /// and more that it can do without, each costing only what it does if this version of Simitone does not fit:
    /// a lot is left (so what happened there and was not saved is forgotten); the live panel shows the Needs panel
    /// (the moodlets are shown beside the needs); and the things that set moodlets off, other than jobs (Events.cs).
    ///
    /// Of Simitone's own pack calls it uses only OnLoad, the first there was, so it runs on any Simitone that can
    /// load a pack with code.
    /// </summary>
    public class MoodletsPack : ISimitonePack
    {
        public const string Command = "moodlet";
        private static string TuningPath;

        public void OnLoad(Pack pack)
        {
            Moods.PackId = pack.Id;
            TuningPath = Path.Combine(pack.Folder, "moodlets.json");
            Moods.Tuning = Tuning.Read(TuningPath, out var problem);
            if (problem != null) Log.Write(problem);
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
            PackManager.RegisterCommand(Command, Run);
            Log.Write("Moodlets loaded: " + Moods.Tuning.Moodlets.Count + " kinds, together at most " + Rules.Signed(Moods.Tuning.Cap) + " or -" + Moods.Tuning.Cap + " to mood. Console: moodlet help");
        }

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(MoodletsPack), name);
        private static HarmonyMethod Theirs(Type type, string name) => new HarmonyMethod(type, name);

        private static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            return (parameters.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, parameters))
                ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Find(typeof(VMTS1MotiveDecay), "Tick", typeof(VMAvatar), typeof(VMContext)),
                prefix: Theirs(typeof(Moods), nameof(Moods.BeforeDecay)), postfix: Theirs(typeof(Moods), nameof(Moods.AfterDecay)));
            // 2
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 3
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(AfterNeighbourhoodLoad)));
            // 4
            harmony.Patch(Find(typeof(TS1GameScreen), "Save"), postfix: Mine(nameof(AfterSave)));

            // what the pack can do without
            Optional(() => harmony.Patch(Find(typeof(TS1GameScreen), "ExitLot"), postfix: Mine(nameof(AfterExitLot))),
                "Moodlets from a lot that is left without saving are kept until Simitone is closed");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "SetSubpanel"), postfix: Mine(nameof(AfterSetSubpanel))),
                "Moodlets are not shown beside the needs (the console still has them: moodlet)");
            Optional(() => harmony.Patch(Find(typeof(VMGenericTS1Call), "Execute"),
                prefix: Theirs(typeof(Events), nameof(Events.BeforeGameCall)), postfix: Theirs(typeof(Events), nameof(Events.AfterGameCall))),
                "No Just Married or Lost a Loved One");
            Optional(() =>
            {
                harmony.Patch(Find(typeof(VMCreateObjectInstance), "Execute"),
                    prefix: Theirs(typeof(Events), nameof(Events.BeforeCreateObject)), finalizer: Theirs(typeof(Events), nameof(Events.AfterCreateObject)));
                harmony.Patch(Find(typeof(VMContext), "CreateObjectInstance", typeof(uint), typeof(LotTilePos), typeof(Direction), typeof(short), typeof(short), typeof(bool)),
                    postfix: Theirs(typeof(Events), nameof(Events.AfterObjectMade)));
            }, "No New Baby");
            Optional(() => harmony.Patch(Find(typeof(VMBurn), "Execute"), postfix: Theirs(typeof(Events), nameof(Events.AfterBurn))),
                "No Fire! or Burnt Food");
        }

        private static void Optional(Action patch, string without)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                Log.Write(without + ": " + e.Message);
            }
        }

        private static void AfterTick(VM __instance)
        {
            Guard("counting moodlets", () => Moods.AfterTick(__instance));
        }

        private static void AfterNeighbourhoodLoad()
        {
            Moods.Forget();
        }

        private static void AfterSave()
        {
            Guard("saving the moodlets", Moods.Saved);
        }

        private static void AfterExitLot()
        {
            Moods.Forget();
        }

        /// <summary>The live panel has changed what it shows: the Needs panel gets the moodlets, to the right of the needs.</summary>
        private static void AfterSetSubpanel(UIMainPanel __instance, UISubpanel sub)
        {
            if (!(sub is UIMotiveSubpanel)) return;
            Guard("adding the moodlets to the Needs panel", () => sub.Add(new UIMoodletPanel(__instance.Game, sub)));
        }

        /// <summary>
        /// Nothing this pack does may take the game down with it. Whatever goes wrong is written to the log and the
        /// game carries on.
        /// </summary>
        public static bool Guard(string doing, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (++Failures <= MostFailuresLogged) Log.Write("Something went wrong while " + doing + ": " + e);
                if (Failures == MostFailuresLogged) Log.Write("(No more of these will be written down this session.)");
                return false;
            }
        }

        private const int MostFailuresLogged = 20;
        private static int Failures;

        // ---- the console ----

        private static TS1GameScreen Screen => GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;

        private const string Help =
            "moodlet - everyone in your household: their moodlets, and what they add to mood\n" +
            "moodlet kinds - every moodlet there is, how strong, and for how long\n" +
            "moodlet give <kind> [name] - give a Sim a moodlet, to see what it does\n" +
            "moodlet clear [kind] [name] - take a Sim's moodlets away, or one kind\n" +
            "moodlet tune <kind> <mood> [hours] - make a kind stronger, weaker, longer or shorter\n" +
            "moodlet cap <number> - the most all of a Sim's moodlets together add or take\n" +
            "moodlet on / moodlet off - whether moodlets count\n" +
            "Without a name, a command is for the selected Sim. Tuning is written to moodlets.json in the pack's folder.";

        private static bool Run(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the moodlet command", () => answer = Answer(words, out understood))) answer = "That went wrong. Moodlets.log says how.";
            if (answer != null) Say("Moodlets", answer);
            return understood;
        }

        /// <summary>
        /// The Sim a command is for: the one named, or else the selected one. Null, with the reason, if there is no
        /// such Sim of the played household on the lot.
        /// </summary>
        private static VMAvatar Who(VM vm, IEnumerable<string> nameWords, out string problem)
        {
            problem = null;
            var household = Moods.Household(vm);
            var name = string.Join(" ", nameWords).Trim();
            if (name != "")
            {
                var exact = household.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                var starts = household.Where(x => x.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var found = exact.Count > 0 ? exact : starts;
                if (found.Count == 1) return found[0];
                problem = found.Count == 0
                    ? "Nobody of your household here is called " + name + "."
                    : "More than one Sim here is called that: " + string.Join(", ", found.Select(x => x.Name)) + ". Select the one you mean and leave the name out.";
                return null;
            }
            var selected = Screen?.SelectedAvatar;
            var who = household.FirstOrDefault(x => x == selected);
            if (who == null) problem = "Select a Sim of your household, or give a name.";
            return who;
        }

        /// <summary>One Sim's moodlets, in a line.</summary>
        public static string Describe(string name, IEnumerable<Moodlet> moodlets, Tuning tuning)
        {
            var list = Rules.Ordered(moodlets, tuning);
            if (list.Count == 0) return name + ": no moodlets.";
            var parts = list.Select(x =>
            {
                var kind = tuning.Find(x.Id);
                return (kind?.Name ?? x.Id) + " " + Rules.Signed(kind?.Mood ?? 0) + " (" + Rules.ShortTime(x.Minutes) + ")";
            });
            var sum = Rules.Sum(list, tuning);
            var total = Rules.Total(list, tuning);
            return name + ": " + string.Join(", ", parts) + ". " + Rules.Signed(total) + " to mood" + (sum != total ? " (" + Rules.Signed(sum) + ", held to the cap)" : "") + ".";
        }

        public static string Kinds(Tuning tuning)
        {
            var lines = tuning.Moodlets.Values.OrderByDescending(x => x.Mood).Select(k =>
                k.Id + ": " + k.Name + ", " + Rules.Signed(k.Mood) + " for " + Rules.Plural(k.Hours, "hour") + (k.Group != "" ? " (" + k.Group + ")" : "")).ToList();
            lines.Add("All of a Sim's moodlets together add at most +" + tuning.Cap + " to mood, and take at most -" + tuning.Cap + ".");
            return string.Join("\n", lines);
        }

        /// <summary>Does what was typed after "moodlet", and returns what to tell the player.</summary>
        public static string Answer(string[] words, out bool understood)
        {
            understood = true;
            var ledger = Moods.Ledger;
            if (ledger == null) return "There is no neighbourhood loaded.";
            var tuning = Moods.Tuning;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            var vm = Screen?.vm;
            const string NeedsLot = "This works while you are in a lot.";
            string problem;

            switch (word)
            {
                case "":
                case "list":
                    if (vm == null) return NeedsLot;
                    var people = Moods.Household(vm);
                    var lines = people.Select(x => Describe(x.Name, Moods.Of(x), tuning)).ToList();
                    if (lines.Count == 0) lines.Add("Nobody of a household you play is on this lot.");
                    if (!ledger.Settings.On) lines.Add("Moodlets are off: they add nothing to mood. To switch them on: moodlet on");
                    return string.Join("\n", lines);

                case "help":
                    return Help;

                case "kinds":
                    return Kinds(tuning);

                case "on":
                case "off":
                    ledger.Settings.On = word == "on";
                    Moods.SettingsChanged();
                    return ledger.Settings.On ? "Moodlets are on." : "Moodlets are off. They add nothing to mood and none are given, and those your Sims have wait until they are on again.";

                case "give":
                {
                    if (vm == null) return NeedsLot;
                    var kind = words.Length > 1 ? tuning.Lookup(words[1]) : null;
                    if (kind == null) return "Which moodlet? For example: moodlet give promoted. Every kind: moodlet kinds";
                    var sim = Who(vm, words.Skip(2), out problem);
                    if (sim == null) return problem;
                    if (!Moods.Give(sim, kind.Id, "Given from the console.")) return "Moodlets are off. To switch them on: moodlet on";
                    return sim.Name + " has " + kind.Name + ", " + Rules.Signed(kind.Mood) + " for " + Rules.Plural(kind.Hours, "hour") + ". Their mood takes it in within two minutes of game time.\n" + Describe(sim.Name, Moods.Of(sim), tuning);
                }

                case "clear":
                {
                    if (vm == null) return NeedsLot;
                    var kind = words.Length > 1 ? tuning.Lookup(words[1]) : null;
                    var sim = Who(vm, words.Skip(kind == null ? 1 : 2), out problem);
                    if (sim == null) return problem;
                    var gone = Moods.Clear(sim, kind?.Id);
                    return gone == 0 ? sim.Name + " had no " + (kind == null ? "moodlets" : kind.Name) + "." : sim.Name + " has lost " + Rules.Plural(gone, "moodlet") + ".";
                }

                case "tune":
                {
                    var kind = words.Length > 1 ? tuning.Lookup(words[1]) : null;
                    if (kind == null || words.Length < 3 || !int.TryParse(words[2], out var mood))
                        return "To tune a moodlet: moodlet tune promoted 15, or with its hours: moodlet tune promoted 15 36";
                    var hours = kind.Hours;
                    if (words.Length > 3 && !int.TryParse(words[3], out hours)) return "Hours are a whole number: moodlet tune " + kind.Id + " " + mood + " 36";
                    if (Math.Abs(mood) > Tuning.MostMood || hours < 1 || hours > Tuning.LongestHours)
                        return "Mood is from -" + Tuning.MostMood + " to " + Tuning.MostMood + ", and hours from 1 to " + Tuning.LongestHours + ".";
                    kind.Mood = mood;
                    kind.Hours = hours;
                    Moods.Retuned();
                    return kind.Name + " is now " + Rules.Signed(mood) + " for " + Rules.Plural(hours, "hour") + ". " + Keep(tuning)
                        + "\nSims who have it keep the time they had left.";
                }

                case "cap":
                    if (words.Length < 2) return "All of a Sim's moodlets together add at most +" + tuning.Cap + " to mood, and take at most -" + tuning.Cap + ". To change it: moodlet cap 30";
                    if (!int.TryParse(words[1], out var cap) || cap < 0 || cap > Tuning.MostMood) return "The cap is from 0 to " + Tuning.MostMood + ".";
                    tuning.Cap = cap;
                    Moods.Retuned();
                    return "All of a Sim's moodlets together now add at most +" + cap + " to mood, and take at most -" + cap + ". " + Keep(tuning);

                default:
                    understood = false;
                    return null;
            }
        }

        /// <summary>Write the tuning back to moodlets.json, and say whether it was.</summary>
        private static string Keep(Tuning tuning)
        {
            if (TuningPath == null) return "It is for this session only.";
            return tuning.Write(TuningPath, out var problem)
                ? "Written to moodlets.json."
                : "It could not be written to moodlets.json (" + problem + "), so it is for this session only.";
        }

        // ---- telling the player ----

        /// <summary>A message with an OK button.</summary>
        public static void Say(string title, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Log.Write(title + ": " + message.Replace("\n", " | "));
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
