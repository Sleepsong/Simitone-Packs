/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.SimAntics;
using FSO.SimAntics.Primitives;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Chemistry
{
    /// <summary>
    /// Turn-ons, turn-offs and chemistry, after The Sims 2: Nightlife. Each grown Sim has two turn-ons and a turn-off;
    /// how one Sim feels about another follows from them, in lightning bolts; and that changes how much each social
    /// raises how they feel. It changes the balance of the game, so it is a pack: nothing changes until it is switched
    /// on, and switching it off puts the game's own gains back at the next start.
    ///
    /// The rules know nothing of the game (Traits, Attraction). The game is met in three places:
    ///  1. The Relationship primitive, which every change to a score goes through: gains are scaled (Gains). Needed.
    ///  2. The relationship panel: how the selected Sim feels about each Sim, under their bars. Optional.
    ///  3. The cheat console: chemistry, to see and choose turn-ons.
    /// </summary>
    public class ChemistryPack : ISimitonePack
    {
        public const string Command = "chemistry";

        public void OnLoad(Pack pack)
        {
            Gains.Tuning = Tuning.Read(pack?.Folder, out var problem);
            if (problem != null) Log.Write(Tuning.FileName + " could not be read, so the pack's own numbers are used: " + problem);

            var harmony = new Harmony("pack." + (pack?.Id ?? "sample.chemistry"));
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id);
                throw;
            }
            PackManager.RegisterCommand(Command, Run);
            Log.Write("Chemistry loaded. Gains: " + Gains.Tuning + ". Console: chemistry help");
        }

        public void OnLotSaved(VM vm, string lotPath)
        {
            if (!LikesStore.SaveIfChanged()) Log.Write("The turn-ons could not be saved.");
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(ChemistryPack), name);
        private static HarmonyMethod Theirs(string name) => new HarmonyMethod(typeof(Gains), name);

        private static MethodBase Found(MethodBase method, string what)
        {
            return method ?? throw new Exception(what + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Found(AccessTools.Method(typeof(VMRelationship), "Execute"), "VMRelationship.Execute"),
                prefix: Theirs(nameof(Gains.BeforeRelationship)), postfix: Theirs(nameof(Gains.AfterRelationship)));
            // 2
            Optional(() =>
            {
                harmony.Patch(Found(AccessTools.Constructor(typeof(UIRelationshipDisplay), new[] { typeof(int), typeof(int), typeof(VM) }), "UIRelationshipDisplay's constructor"),
                    postfix: Mine(nameof(AfterRelationshipDisplay)));
                harmony.Patch(Found(AccessTools.Method(typeof(UIRelationshipDisplay), "Update"), "UIRelationshipDisplay.Update"),
                    postfix: Mine(nameof(AfterRelationshipDisplayUpdate)));
            }, "chemistry in the relationship panel");
        }

        private static void Optional(Action patch, string what)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                Log.Write("Could not add " + what + ": " + e.Message + " The console still tells it: " + Command);
            }
        }

        // ---- nothing the pack does may take the game down ----

        private const int MostFailuresLogged = 20;
        private static int Failures;

        public static void Failed(string doing, Exception e)
        {
            if (++Failures <= MostFailuresLogged) Log.Write("Something went wrong while " + doing + ": " + e);
        }

        public static bool Guard(string doing, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Failed(doing, e);
                return false;
            }
        }

        // ---- 2. under a Sim's bars in the relationship panel: how the Sim whose panel it is feels about them ----

        private class Badge
        {
            public UILabel Label;
            public int From, To;
            public VM VM;
            public int Wait;
        }

        private static readonly ConditionalWeakTable<UIRelationshipDisplay, Badge> Badges = new ConditionalWeakTable<UIRelationshipDisplay, Badge>();

        /// <summary>Updates between one look at chemistry and the next: what a Sim wears or how clean they are can change.</summary>
        private const int UpdatesBetweenLooks = 30;

        public static void AfterRelationshipDisplay(UIRelationshipDisplay __instance, int nidFrom, int nid, VM curVM)
        {
            Guard("adding chemistry to the relationship panel", () =>
            {
                var label = new UILabel();
                label.CaptionStyle = label.CaptionStyle.Clone();
                label.CaptionStyle.Color = UIStyle.Current.Text;
                label.CaptionStyle.Size = 9;
                label.Size = new Vector2(90, 12);
                label.Position = new Vector2(0, 114);
                label.Alignment = TextAlignment.Middle | TextAlignment.Center;
                label.Caption = "";
                __instance.Add(label);
                Badges.Add(__instance, new Badge() { Label = label, From = nidFrom, To = nid, VM = curVM });
            });
        }

        public static void AfterRelationshipDisplayUpdate(UIRelationshipDisplay __instance)
        {
            if (!Badges.TryGetValue(__instance, out var badge) || --badge.Wait > 0) return;
            badge.Wait = UpdatesBetweenLooks;
            if (!Guard("showing chemistry in the relationship panel", () =>
            {
                var neighbourhood = Content.Get()?.Neighborhood;
                var level = Sims.Between(neighbourhood?.GetNeighborByID((short)badge.From), neighbourhood?.GetNeighborByID((short)badge.To), badge.VM);
                badge.Label.Caption = level == Level.None ? "" : Attraction.Words(level);
            })) badge.Wait = int.MaxValue; //once it has gone wrong for a Sim, stop trying for them
        }

        // ---- 3. the console ----

        private static TS1GameScreen Screen => GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;

        public const string Help =
            "chemistry - the selected Sim's turn-ons, and how they feel about every grown Sim here\n" +
            "chemistry set [turn-on] [turn-on] [turn-off] - choose the selected Sim's\n" +
            "chemistry traits - the words for turn-ons\n" +
            "chemistry gains - how much each level of chemistry changes a gain\n" +
            "chemistry log on|off - write every changed gain to the terminal\n" +
            "A name after chemistry, or after chemistry set and the three traits, is for that Sim instead of the selected one.";

        private static bool Run(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the chemistry command", () => answer = Answer(words, Screen?.vm, Screen?.SelectedAvatar, out understood)))
                answer = "That went wrong. The terminal says how, in lines starting [chemistry].";
            if (answer != null) Say(answer);
            return understood;
        }

        /// <summary>Does what was typed after "chemistry", and returns what to tell the player.</summary>
        public static string Answer(string[] words, VM vm, VMAvatar selected, out bool understood)
        {
            understood = true;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            const string NeedsLot = "This works while you are in a lot.";
            string problem;
            switch (word)
            {
                case "":
                case "list":
                case "show":
                    {
                        if (vm == null) return NeedsLot;
                        var who = Who(vm, selected, words.Skip(1), out problem);
                        if (who == null) return problem;
                        return Describe(vm, who);
                    }
                case "set":
                    {
                        if (vm == null) return NeedsLot;
                        if (words.Length < 4) return "Give two turn-ons and a turn-off: chemistry set good-cook outgoing stink\nThe words: " + Traits.AllWords;
                        var traits = new Trait[3];
                        for (int i = 0; i < 3; i++)
                        {
                            if (!Traits.TryParse(words[i + 1], out traits[i])) return "\"" + words[i + 1] + "\" is not a turn-on. The words: " + Traits.AllWords;
                        }
                        var likes = new Likes(traits[0], traits[1], traits[2]);
                        var why = likes.Problem();
                        if (why != null) return why;
                        var who = Who(vm, selected, words.Skip(4), out problem);
                        if (who == null) return problem;
                        LikesStore.Set(Sims.Guid(who), likes);
                        var saved = LikesStore.SaveIfChanged();
                        return Sims.Name(who) + " is now " + likes + "." + (saved ? "" : "\n(It could not be written down, and will be lost when Simitone closes.)");
                    }
                case "traits":
                    return "Turn-ons and turn-offs: " + string.Join(", ", Traits.All.Select(Traits.Word)) + ".\n" +
                           "Personality counts from 7 points (neat, outgoing...) or at 3 and under (sloppy, shy...); a skill from 5 points; stink is hygiene at a quarter or less.";
                case "gains":
                    return "A gain from a social is multiplied by: turned off x" + Gains.Tuning.TurnedOff + ", no chemistry x" + Gains.Tuning.None + ", 1 bolt x" + Gains.Tuning.OneBolt +
                           ", 2 bolts x" + Gains.Tuning.TwoBolts + ", 3 bolts x" + Gains.Tuning.ThreeBolts + ". Losses are as the game has them.\n" +
                           "The numbers are in " + Tuning.FileName + " in the pack's folder, read when Simitone starts. Gains changed so far: " + Gains.Changed + ".";
                case "log":
                    if (words.Length > 1 && (words[1].ToLowerInvariant() == "on" || words[1].ToLowerInvariant() == "off")) Gains.Verbose = words[1].ToLowerInvariant() == "on";
                    else if (words.Length > 1) { understood = false; return null; }
                    return Gains.Verbose ? "Every gain chemistry changes is written to the terminal." : "Changed gains are not written to the terminal.";
                case "help":
                    return Help;
                default:
                    {
                        //chemistry Bella: the same as chemistry list Bella
                        var named = vm == null ? null : Who(vm, null, words, out problem);
                        if (named == null) { understood = false; return null; }
                        return Describe(vm, named);
                    }
            }
        }

        /// <summary>The Sim a command is for: the grown Sim on the lot with this name, or else the selected one.</summary>
        private static VMAvatar Who(VM vm, VMAvatar selected, IEnumerable<string> nameWords, out string problem)
        {
            problem = null;
            var grown = Sims.GrownOnLot(vm);
            var name = string.Join(" ", nameWords).Trim();
            if (name != "")
            {
                var exact = grown.Where(x => string.Equals(Sims.Name(x), name, StringComparison.OrdinalIgnoreCase)).ToList();
                var starts = grown.Where(x => Sims.Name(x).StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var found = exact.Count > 0 ? exact : starts;
                if (found.Count == 1) return found[0];
                problem = found.Count == 0
                    ? "No grown Sim here is called " + name + "."
                    : "More than one Sim here is called that: " + string.Join(", ", found.Select(Sims.Name)) + ". Select the one you mean and leave the name out.";
                return null;
            }
            if (selected == null) { problem = "Select a Sim, or give a name."; return null; }
            if (!Sims.CanFeel(selected)) { problem = Sims.Name(selected) + " is not grown up: only grown Sims have turn-ons."; return null; }
            return selected;
        }

        public static string Describe(VM vm, VMAvatar who)
        {
            var lines = new List<string>();
            var likes = LikesStore.For(Sims.Guid(who));
            lines.Add(Sims.Name(who) + " is " + likes + ".");
            var others = Sims.GrownOnLot(vm).Where(x => x != who).ToList();
            foreach (var other in others)
            {
                var mine = Sims.Between(who, other);
                var theirs = Sims.Between(other, who);
                lines.Add(Sims.Name(other) + ": " + Attraction.Words(mine).ToLowerInvariant() + " (gains x" + Gains.Tuning.Multiplier(mine) + "); "
                    + Attraction.Words(theirs).ToLowerInvariant() + " the other way.");
            }
            if (others.Count == 0) lines.Add("There is no other grown Sim on the lot.");
            LikesStore.SaveIfChanged();
            return string.Join("\n", lines);
        }

        private static void Say(string message)
        {
            Log.Write(message.Replace("\n", " | "));
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Chemistry",
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
