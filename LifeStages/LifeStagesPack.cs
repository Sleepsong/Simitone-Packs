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
using Simitone.Client.UI.Panels.LiveSubpanels;
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
    ///  1. A lot has ticked: watch its clock for midnight (a day lived) and the evening (birthdays).
    ///  2. The game has loaded a neighbourhood: forget the ages of the one before.
    ///  3. The game has saved: keep the ages with it.
    ///
    /// and more which the pack can do without: a lot is left, so that days played and not saved are forgotten with
    /// it; the live panel changes what it shows, so that the Personality panel can be given the age display
    /// (UIAgeDisplay.cs); and Create a Sim, so that it can make teens and elders (CreateASim.cs).
    ///
    /// Of Simitone's own pack calls it uses only OnLoad, the first there was, so it runs on any Simitone that can
    /// load a pack with code.
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
            Log.Write(Looks.Load(pack.Folder));
            Life.Announce = (vm, message, choosers) =>
            {
                if (Screen?.vm != vm) return;
                //the news first, then each new adult and elder chooses their head, one after another
                Say("Birthday", message, () => PickInTurn(vm, choosers, 0));
            };
            Life.ChooseLook = (vm, avatar, kind, closed) => OpenPicker(vm, avatar, kind, closed);
            PackManager.RegisterCommand("age", Command);
            Log.Write("Life Stages loaded. Console: age help");
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
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(AfterNeighbourhoodLoad)));
            // 3
            harmony.Patch(Find(typeof(TS1GameScreen), "Save"), postfix: Mine(nameof(AfterSave)));

            // what the pack can do without. a version of Simitone one of these does not fit costs only that
            Optional(() => harmony.Patch(Find(typeof(TS1GameScreen), "ExitLot"), postfix: Mine(nameof(AfterExitLot))),
                "Days played on a lot that is left without saving are kept until Simitone is closed");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "SetSubpanel"), postfix: Mine(nameof(AfterSetSubpanel))),
                "The age display is not available (the console still is: age)");

            // teens and elders in Create a Sim: all of it or none, in a Harmony of its own so it can be taken off whole
            var cas = new Harmony(harmony.Id + ".cas");
            Optional(() =>
            {
                try
                {
                    CreateASim.Apply(cas);
                }
                catch
                {
                    cas.UnpatchAll(cas.Id);
                    throw;
                }
            }, "Create a Sim makes only children and adults");
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

        private static void AfterSave()
        {
            Guard("saving the ages", Life.Saved);
        }

        private static void AfterExitLot()
        {
            Life.Forget();
        }

        /// <summary>The live panel has changed what it shows: the Personality panel gets the age display.</summary>
        private static void AfterSetSubpanel(UIMainPanel __instance, UISubpanel sub)
        {
            if (!(sub is UIPersonalitySubpanel)) return;
            //the panel's own contents start 334 across: the display has the space before that
            Guard("adding the age display", () => sub.Add(new UIAgeDisplay(__instance.Game) { Position = new Microsoft.Xna.Framework.Vector2(14, 8) }));
        }

        private static void AfterTick(VM __instance)
        {
            Guard("counting the days", () => Life.AfterTick(__instance));
        }

        private static void AfterNeighbourhoodLoad()
        {
            Life.Forget();
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

        // ---- choosing a head and an outfit ----

        /// <summary>
        /// Each Sim who has just grown up chooses, one after another: their head, and then their everyday outfit.
        /// </summary>
        private static void PickInTurn(VM vm, List<VMAvatar> sims, int index)
        {
            if (sims == null || index >= sims.Count) return;
            var sim = sims[index];
            var elder = (Life.RecordOf(sim)?.Stage ?? Stage.Adult) == Stage.Elder;
            var now = sim.Name + " is now " + (elder ? "an elder" : "an adult");
            Action next = () => PickInTurn(vm, sims, index + 1);
            Action outfit = () =>
            {
                if (!OpenPicker(vm, sim, LookKind.Body, next, now + ": choose an outfit")) next();
            };
            if (!OpenPicker(vm, sim, LookKind.Head, outfit, now + ": choose a head")) outfit();
        }

        /// <summary>
        /// The head or outfit picker for a grown Sim on the lot being played: what they have now first, then those
        /// for their stage in their skin colour. False if it cannot be shown.
        /// </summary>
        public static bool OpenPicker(VM vm, VMAvatar avatar, LookKind kind, Action closed, string title = null)
        {
            var opened = false;
            Guard("opening the " + (kind == LookKind.Head ? "head" : "outfit") + " picker", () =>
            {
                if (vm == null || Screen?.vm != vm || !VM.UseWorld || avatar == null || avatar.Dead) return;
                var body = Life.BodyStrings(avatar);
                if (body == null || body.Length <= 22) return;
                var stage = Life.RecordOf(avatar)?.Stage ?? Stage.Adult;
                var skin = Body.SkinColour(body.GetString(14), avatar.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.SkinColor));
                var textures = ((TS1AvatarTextureProvider)FSO.Content.Content.Get().AvatarTextures).GetAllNames().ToList();
                List<Look> looks;
                if (kind == LookKind.Head)
                {
                    var worn = Body.Worn(body.GetString(2));
                    looks = Body.Choices(worn.Mesh, worn.Tex, Life.AdultHeads(avatar), stage, skin, textures).Select(x => new Look(x.Mesh, x.Tex)).ToList();
                }
                else looks = Body.Outfits(body.GetString(1), body.GetString(17), Life.AdultBodies(avatar), stage, skin, textures);
                if (looks.Count == 0) return;
                title ??= (kind == LookKind.Head ? "Choose a head for " : "Choose an outfit for ") + avatar.Name;
                var picker = new UILookPicker(avatar, vm, kind, looks, title, closed);
                GameThread.NextUpdate(x => UIScreen.GlobalShowDialog(picker, true));
                opened = true;
            });
            return opened;
        }

        // ---- the console ----

        private static TS1GameScreen Screen => GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;

        private const string Help =
            "age - everyone in your household: stage, day, and days left\n" +
            "age up [name] - move a Sim to the next stage now\n" +
            "age look [name] - choose another head for a grown Sim\n" +
            "age outfit [name] - choose another everyday outfit for a grown Sim\n" +
            "age day [number] [name] - put a Sim on a day of their stage\n" +
            "age days [child|teen|adult] [number] - how many days a stage lasts\n" +
            "age on / age off - whether days count\n" +
            "age teens on|off - whether children become teens first\n" +

            "Without a name, a command is for the selected Sim.";

        private static bool Command(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the age command", () => answer = Run(words, out understood))) answer = "That went wrong. LifeStages.log says how.";
            if (answer != null) Say("Life Stages", answer);
            return understood;
        }

        /// <summary>
        /// The Sim a command is for: the one named, or else the selected one. Null, with the reason, if there is no
        /// such Sim of the played household on the lot.
        /// </summary>
        private static VMAvatar Who(VM vm, IEnumerable<string> nameWords, out string problem)
        {
            problem = null;
            var household = Life.Household(vm);
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

        /// <summary>Does what was typed after "age", and returns what to tell the player.</summary>
        public static string Run(string[] words, out bool understood)
        {
            understood = true;
            var ledger = Life.Ledger;
            if (ledger == null) return "There is no neighbourhood loaded.";
            var settings = ledger.Settings;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            var vm = Screen?.vm;
            const string NeedsLot = "This works while you are in a lot.";
            string problem;

            switch (word)
            {
                case "":
                case "list":
                    if (vm == null) return NeedsLot;
                    var people = Life.Household(vm);
                    var atHome = Life.AtHome(vm);
                    var lines = people.Select(x =>
                    {
                        var view = AgeView.For(Life.RecordOf(x), settings, atHome, vm.Context.Clock.Hours, UIAgeDisplay.BarWidth);
                        return x.Name + ": " + view.Title + ", " + char.ToLowerInvariant(view.Counter[0]) + view.Counter.Substring(1) + ". " + view.Status + ".";
                    }).ToList();
                    if (lines.Count == 0) lines.Add("Nobody of a household you play is on this lot.");
                    if (!settings.Ageing) lines.Add("Ageing is off. To switch it on: age on");
                    else if (people.Count > 0 && !atHome) lines.Add("Days only count on the household's own lot.");
                    return string.Join("\n", lines);

                case "help":
                    return Help;

                case "on":
                case "off":
                    settings.Ageing = word == "on";
                    Life.SettingsChanged();
                    return settings.Ageing ? "Ageing is on." : "Ageing is off. Your Sims stay as they are, and their days are remembered.";

                case "teens":
                    if (words.Length > 1 && (words[1].ToLowerInvariant() == "on" || words[1].ToLowerInvariant() == "off"))
                    {
                        settings.Teens = words[1].ToLowerInvariant() == "on";
                        Life.SettingsChanged();
                        if (vm != null) Life.Reconcile(vm);
                    }
                    else if (words.Length > 1) { understood = false; return null; }
                    return settings.Teens ? "Children become teens, then adults." : "Children grow straight into adults. A Sim who is a teen now stays one until grown.";

                case "days":
                    const string AllLengths = "To change one: age days adult 40";
                    if (words.Length == 1)
                        return "A child for " + settings.ChildDays + " days, a teen for " + settings.TeenDays + ", an adult for " + settings.AdultDays + ". An elder stays an elder.\n" + AllLengths;
                    if (!Stages.TryParse(words[1], out var stage)) { understood = false; return null; }
                    if (words.Length > 2)
                    {
                        if (!(int.TryParse(words[2], out var days) && settings.SetLength(stage, days)))
                            return stage == Stage.Elder ? "An elder's days are not numbered: nobody dies of old age yet." : "A stage lasts from 1 to " + Settings.LongestStage + " days.";
                        Life.SettingsChanged();
                    }
                    var length = Stages.Length(stage, settings);
                    return length == 0 ? "An elder stays an elder." : "A Sim is " + (stage == Stage.Adult ? "an " : "a ") + Stages.Name(stage) + " for " + length + " days.";

                case "up":
                    if (vm == null) return NeedsLot;
                    var grower = Who(vm, words.Skip(1), out problem);
                    if (grower == null) return problem;
                    var news = new List<string>();
                    var choosers = new List<VMAvatar>();
                    Life.GrowUp(vm, grower, news, choosers);
                    if (choosers.Count > 0)
                    {
                        Say("Life Stages", string.Join("\n", news), () => PickInTurn(vm, choosers, 0));
                        return null;
                    }
                    return string.Join("\n", news);

                case "look":
                    if (vm == null) return NeedsLot;
                    var looker = Who(vm, words.Skip(1), out problem);
                    return looker == null ? problem : Life.NewLook(vm, looker, LookKind.Head);

                case "outfit":
                case "clothes":
                    if (vm == null) return NeedsLot;
                    var dresser = Who(vm, words.Skip(1), out problem);
                    return dresser == null ? problem : Life.NewLook(vm, dresser, LookKind.Body);

                case "day":
                    if (vm == null) return NeedsLot;
                    if (words.Length < 2 || !int.TryParse(words[1], out var day) || day < 1) return "Which day? For the fifth day of a Sim's stage: age day 5";
                    var dated = Who(vm, words.Skip(2), out problem);
                    return dated == null ? problem : Life.SetDay(dated, day);

                default:
                    understood = false;
                    return null;
            }
        }

        // ---- telling the player ----

        /// <summary>A message with an OK button; then, once it is closed, what follows, if anything.</summary>
        public static void Say(string title, string message, Action then = null)
        {
            if (string.IsNullOrEmpty(message)) { if (then != null) Guard("after a message", then); return; }
            Log.Write(title + ": " + message.Replace("\n", " | "));
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) =>
                    {
                        UIScreen.RemoveDialog(alert);
                        if (then != null) Guard("after a message", then);
                    })
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
