/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content.Model;
using FSO.Content.TS1;
using FSO.SimAntics;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LifeStages
{
    /// <summary>
    /// Life Stages, a first stage: the Sims you play grow older. Child, teen, adult, elder.
    ///
    /// The engine is not changed. The pack hangs on it in three places, each a Harmony patch:
    ///
    ///  1. A lot has ticked: watch its clock for midnight (a day lived) and six in the evening (birthdays).
    ///  2. The game looks up a skin texture: answer for the grey copies of heads that elders wear.
    ///  3. The game has loaded a neighbourhood: forget the ages of the one before.
    ///
    /// The rules are in Stages.cs, what is remembered in Ledger.cs, and what is done to a Sim in Life.cs.
    /// </summary>
    public class LifeStagesPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            Life.PackId = pack.Id;
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
            Life.Announce = (vm, message) =>
            {
                if (Screen?.vm == vm) Say("Birthday", message);
            };
            PackManager.RegisterCommand("age", Command);
            Log.Write("Life Stages loaded. Console: age help");
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            Guard("looking at the lot that loaded", () => Life.LotLoaded(vm));
        }

        public void OnLotSaved(VM vm, string lotPath)
        {
            Guard("saving the ages", Life.Saved);
        }

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(LifeStagesPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            return (parameters.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, parameters))
                ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 2
            harmony.Patch(Find(typeof(TS1AvatarTextureProvider), "Get", typeof(string)), postfix: Mine(nameof(AfterTextureLookup)));
            // 3
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(AfterNeighbourhoodLoad)));
        }

        private static void AfterTick(VM __instance)
        {
            Guard("counting the days", () => Life.AfterTick(__instance));
        }

        private static void AfterTextureLookup(TS1AvatarTextureProvider __instance, string name, ref ITextureRef __result)
        {
            if (__result != null || name == null || !name.Contains(GreyHair.Marker)) return;
            try
            {
                __result = GreyHair.Find(name, __instance.Get);
            }
            catch (Exception e)
            {
                Guard("finding a grey head", () => throw e);
            }
        }

        private static void AfterNeighbourhoodLoad()
        {
            Life.NeighbourhoodLoaded();
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
            "age - your household's ages\n" +
            "age on / age off - whether days count\n" +
            "age up [name] - move a Sim to the next stage now (the selected Sim, if no name)\n" +
            "age days child|teen|adult [number] - how many days a stage lasts\n" +
            "age teens on|off - whether children become teens first\n" +
            "age grey on|off - whether elders have grey hair";

        private static bool Command(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the age command", () => answer = Run(words, out understood))) answer = "That went wrong. LifeStages.log says how.";
            if (answer != null) Say("Life Stages", answer);
            return understood;
        }

        /// <summary>Does what was typed after "age", and returns what to tell the player.</summary>
        public static string Run(string[] words, out bool understood)
        {
            understood = true;
            var ledger = Life.Ledger;
            if (ledger == null) return "There is no neighbourhood loaded.";
            var settings = ledger.Settings;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            var vm = Screen?.vm;

            switch (word)
            {
                case "":
                case "list":
                    if (vm == null) return "This works while you are in a lot.";
                    var people = Life.Household(vm);
                    var lines = people.Select(x => x.Name + ": " + Stages.Describe(Life.RecordOf(x), settings)).ToList();
                    if (lines.Count == 0) lines.Add("Nobody of a household you play is on this lot.");
                    if (!settings.Ageing) lines.Add("Ageing is off (age on).");
                    else if (people.Count > 0 && !Life.AtHome(vm)) lines.Add("Days only count on the household's own lot.");
                    return string.Join("\n", lines);

                case "help":
                    return Help;

                case "on":
                case "off":
                    settings.Ageing = word == "on";
                    return settings.Ageing ? "Ageing is on." : "Ageing is off. Your Sims stay as they are, and their days are remembered.";

                case "teens":
                case "grey":
                case "gray":
                    var teens = word == "teens";
                    if (words.Length > 1 && (words[1].ToLowerInvariant() == "on" || words[1].ToLowerInvariant() == "off"))
                    {
                        var on = words[1].ToLowerInvariant() == "on";
                        if (teens) settings.Teens = on; else settings.GreyHair = on;
                        if (vm != null) Life.Reconcile(vm);
                    }
                    else if (words.Length > 1) { understood = false; return null; }
                    if (teens) return settings.Teens ? "Children become teens, then adults." : "Children grow straight into adults. A Sim who is a teen now stays one until grown.";
                    return settings.GreyHair ? "Elders have grey hair." : "Elders keep the hair they had.";

                case "days":
                    if (words.Length == 1)
                        return "A child for " + settings.ChildDays + " days, a teen for " + settings.TeenDays + ", an adult for " + settings.AdultDays + ". An elder stays an elder.";
                    if (!Stages.TryParse(words[1], out var stage)) { understood = false; return null; }
                    if (words.Length > 2 && !(int.TryParse(words[2], out var days) && settings.SetLength(stage, days)))
                        return stage == Stage.Elder ? "An elder's days are not numbered: nobody dies of old age yet." : "A stage lasts from 1 to " + Settings.LongestStage + " days.";
                    var length = Stages.Length(stage, settings);
                    return length == 0 ? "An elder stays an elder." : "A Sim is " + (stage == Stage.Adult ? "an " : "a ") + Stages.Name(stage) + " for " + length + " days.";

                case "up":
                    if (vm == null) return "This works while you are in a lot.";
                    var household = Life.Household(vm);
                    VMAvatar who;
                    if (words.Length > 1)
                    {
                        var name = string.Join(" ", words.Skip(1));
                        who = household.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                            ?? household.FirstOrDefault(x => x.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                        if (who == null) return "Nobody of your household here is called " + name + ".";
                    }
                    else
                    {
                        var selected = Screen?.SelectedAvatar;
                        who = household.FirstOrDefault(x => x == selected);
                        if (who == null) return "Select a Sim of your household, or give a name: age up Bella";
                    }
                    var news = new List<string>();
                    Life.GrowUp(vm, who, news);
                    return string.Join("\n", news);

                default:
                    understood = false;
                    return null;
            }
        }

        // ---- telling the player ----

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
