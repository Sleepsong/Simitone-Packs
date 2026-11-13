/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model.TS1Platform;
using FSO.SimAntics.Primitives;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Apartments
{
    /// <summary>
    /// Apartments, stage 1: several households on one lot. You play one of them. The others live there too, as
    /// neighbours the game runs by itself, and you can change which one you play.
    ///
    /// The engine keeps one family for a lot (VMTS1LotState.CurrentFamily), and all of the game's own scripts expect
    /// that. This pack leaves it so: there is still exactly one played family, and changing household changes which
    /// one it is. What the pack adds is everything around that, as Harmony patches:
    ///
    ///  1. A neighbourhood has loaded: point the game's one-family-per-house table at the played household of each
    ///     shared house.
    ///  2. The game moves a house's family out: if others live there, the house is not empty.
    ///  3. The game has checked the played family is on the lot: put its members in their apartment, and send
    ///     everyone else who lives here home, off stage.
    ///  4. The lot ticks: now and then bring a neighbour out for a while and walk them home again, take the rent,
    ///     and write down what the neighbours do.
    ///  5. The game takes or gives money: write down who caused it (it always goes to the played household).
    ///  6. The lot is saved: keep the pack's file for the lot with it.
    ///  7. A room's light has been worked out: turn it down if it is another household's apartment.
    ///
    /// This is a prototype whose first purpose is to find out what the game's scripts make of Sims who live on a lot
    /// without being played. Everything it learns goes to Apartments.log. It has no apartments yet: no units, doors,
    /// rent or bills of its own. Those come once this stage shows what has to be worked around.
    /// </summary>
    public class ApartmentsPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            Residency.PackId = pack.Id;
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
            PackManager.RegisterCommand("apt", Command);
            Neighbours.OnRentShortfall = (vm, message) =>
            {
                if (Actions.Screen?.vm == vm) Actions.Say("Rent", message);
            };
            AptLog.Write("Apartments prototype loaded. Console: apt help");
        }

        public void OnLotLoaded(VM vm)
        {
            //normally done already (3). this covers a lot that reached the screen another way
            if (!Neighbours.IsShared(vm)) Guard("looking at the lot that loaded", () => Neighbours.Reconcile(vm, "lot loaded"));
        }

        /// <summary>
        /// Nothing this pack does may take the game down with it. Whatever goes wrong is written to the log, in full,
        /// and the game carries on.
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
                if (++Failures <= MostFailuresLogged) AptLog.Write("Something went wrong while " + doing + ": " + e);
                if (Failures == MostFailuresLogged) AptLog.Write("(No more of these will be written down this session.)");
                return false;
            }
        }

        private const int MostFailuresLogged = 20;
        private static int Failures;

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(ApartmentsPack), name);

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(AfterNeighbourhoodLoad)));
            // 2
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "MoveOut"), postfix: Mine(nameof(AfterMoveOut)));
            // 3
            harmony.Patch(Find(typeof(VMTS1LotState), "VerifyFamily"), prefix: Mine(nameof(BeforeVerifyFamily)), postfix: Mine(nameof(AfterVerifyFamily)));
            // 4
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 5
            harmony.Patch(Find(typeof(VMTS1Budget), "Execute"), prefix: Mine(nameof(BeforeBudget)), postfix: Mine(nameof(AfterBudget)));

            // 7
            harmony.Patch(Find(typeof(VMContext), "RefreshLighting"), postfix: Mine(nameof(AfterRoomLit)));
            // 6. (only calls Simitone makes in every version are relied on, so the pack looks for a save itself)
            harmony.Patch(Find(typeof(TS1GameScreen), "Save"), postfix: Mine(nameof(AfterSave)));

            // the ways into the Apartments window. the pack works without them (the console opens the window too), so
            // a version of Simitone they do not fit costs only that way in
            Optional(() => harmony.Patch(Find(typeof(UILotControl), "Update"), postfix: Mine(nameof(AfterLotControlUpdate))), "the F9 key");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "AddPacksButton"), postfix: Mine(nameof(AfterPacksButton))), "the Apartments button in Options");
        }

        private static void Optional(Action patch, string what)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                AptLog.Write("Could not add " + what + ": " + e.Message + " Use the console instead: apt");
            }
        }

        // ---- the ways into the window ----

        public static void AfterLotControlUpdate(UpdateState state)
        {
            if (state?.NewKeys != null && state.NewKeys.Contains(Keys.F9)) UIApartmentsDialog.Open();
        }

        /// <summary>Simitone has just put its Packs button in the Options panel. Put an Apartments button after it, made the same way.</summary>
        public static void AfterPacksButton(UISubpanel parent)
        {
            try
            {
                int index = 5; //after Packs
                float buttonX = 50 + index * 120f;
                float labelX = buttonX - 27;

                var label = new UILabel();
                label.Caption = "Apartments";
                label.Alignment = TextAlignment.Middle | TextAlignment.Center;
                label.Wrapped = true;
                label.Position = new Vector2(labelX - 77, 106);
                label.Size = new Vector2(120, 1);
                label.CaptionStyle = label.CaptionStyle.Clone();
                label.CaptionStyle.Size = 12;
                label.CaptionStyle.Color = UIStyle.Current.Text;

                //the pack has no artwork of its own: the Packs button's picture, under its own name
                var button = new UICatButton(Content.Get().CustomUI.Get("opt_packs.png").Get(GameFacade.GraphicsDevice));
                button.Position = new Vector2(buttonX - 50, 16);
                button.OnButtonClick += (btn) => UIApartmentsDialog.Open();

                GameFacade.Screens.Tween.To(label, 0.5f, new Dictionary<string, float>() { { "X", labelX } }, TweenQuad.EaseOut);
                GameFacade.Screens.Tween.To(button, 0.5f, new Dictionary<string, float>() { { "X", buttonX } }, TweenQuad.EaseOut);

                parent.Add(label);
                parent.Add(button);
            }
            catch (Exception e)
            {
                AptLog.Write("Could not add the Apartments button to Options: " + e.Message + " Use F9 or the console instead.");
            }
        }

        // ---- 1 ----

        public static void AfterNeighbourhoodLoad(TS1NeighborhoodProvider __instance)
        {
            Guard("reading the shared houses of this neighbourhood", () => Residency.FixTable(__instance));
        }

        // ---- 2 ----

        public static void AfterMoveOut(TS1NeighborhoodProvider __instance, short houseID)
        {
            Guard("moving a household out", () => Residency.AfterMoveOut(__instance, houseID));
        }

        // ---- 3 ----

        public static void BeforeVerifyFamily(VM vm, out HashSet<uint> __state)
        {
            //who is on the lot before the game adds the played family's missing members: the rest are new arrivals
            HashSet<uint> there = null;
            Guard("looking at who is on the lot", () => there = new HashSet<uint>(Neighbours.AvatarsByGuid(vm).Keys));
            __state = there;
        }

        public static void AfterVerifyFamily(VM vm, HashSet<uint> __state)
        {
            Guard("putting the households in their places", () => Neighbours.Reconcile(vm, "family checked", __state));
        }

        // ---- 4 ----

        public static void AfterTick(VM __instance)
        {
            if (__instance.TS1) Guard("looking after the neighbours", () => Neighbours.AfterTick(__instance));
        }

        // ---- 7 ----

        public static void AfterRoomLit(VMContext __instance, ushort room)
        {
            Guard("dimming an apartment", () => Neighbours.AfterRoomLit(__instance, room));
        }

        // ---- 6 ----

        public static void AfterSave(TS1GameScreen __instance)
        {
            Guard("keeping the apartments with the saved lot", () => Neighbours.SaveLot(__instance.vm));
        }

        // ---- 5. the game has one purse, the played household's. this only reports what reaches it ----

        public static void BeforeBudget(VMStackFrame context, out int __state)
        {
            __state = context.VM.TS1State?.CurrentFamily?.Budget ?? 0;
        }

        public static void AfterBudget(VMStackFrame context, int __state)
        {
            var vm = context.VM;
            if (!Neighbours.IsShared(vm)) return;
            Guard("writing down a payment", () => ReportMoney(context, __state));
        }

        private static void ReportMoney(VMStackFrame context, int __state)
        {
            var vm = context.VM;
            var now = vm.TS1State?.CurrentFamily?.Budget ?? 0;
            if (now == __state) return;
            var change = now - __state;
            var who = NameOf(context.Caller);
            var what = NameOf(context.Callee);
            var neighbour = Neighbours.IsNeighbour(vm, context.Caller);
            AptLog.Write("Money: " + (change > 0 ? "+" : "") + change + " for the played household (now " + now + "). By " + who
                + (what != who ? ", using " + what : "") + (neighbour ? ". THAT IS A NEIGHBOUR: the played household paid for them" : "") + ".");
        }

        private static string NameOf(VMEntity entity)
        {
            if (entity == null) return "(nothing)";
            try
            {
                var name = entity.Name;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch
            {
            }
            return entity.Object?.OBJ?.ChunkLabel ?? "(unnamed)";
        }

        // ---- the console. everything it does is also in the Apartments window (UIApartmentsDialog) ----

        private const string Help =
            "apt - open the Apartments window (also F9, or Options > Apartments)\n" +
            "apt who - the households in this house\n" +
            "apt families - households with no house, which can move in\n" +
            "apt movein <number or name> - move a household in beside yours\n" +
            "apt moveout <number or name> - move a neighbouring household out\n" +
            "apt switch <number or name> - save, and play that household\n" +
            "apt claim [number or name] - the room your Sim is in joins your apartment (or that household's)\n" +
            "apt unclaim - the room your Sim is in is shared again\n" +
            "apt units - each household's apartment and rent\n" +
            "apt rent <simoleons per tile per day>|off - the rent on this lot\n" +
            "apt outings never|sometimes|often - how often a neighbour comes out of their apartment\n" +
            "apt out - bring a neighbour out now\n" +
            "apt home - send the neighbours who are out back home\n" +
            "apt dim on|off - dim the other households' apartments\n" +
            "apt needs <-100 to 100>|off - neighbours' needs never fall below this while they are out\n" +
            "apt watch on|off - write down what neighbours do\n" +
            "apt log - where the record is kept";

        private static bool Command(string[] args)
        {
            if (args.Length == 0)
            {
                UIApartmentsDialog.Open();
                return true;
            }
            var verb = args[0].ToLowerInvariant();
            var rest = string.Join(" ", args.Skip(1));
            switch (verb)
            {
                case "help":
                    Actions.Say("Apartments", Help);
                    return true;
                case "log":
                    Actions.Say("Apartments", "The record is kept in:\n" + AptLog.FilePath);
                    return true;
                case "outings":
                    if (!Enum.TryParse<Outings>(rest, true, out var often) || !Enum.IsDefined(typeof(Outings), often)) return false;
                    Actions.SetOutings(often);
                    return true;
                case "out":
                    Actions.Say("Apartments", Actions.BringOut());
                    return true;
                case "home":
                    Actions.Say("Apartments", Actions.SendHome());
                    return true;
                case "dim":
                    if (!OnOff(rest, out var dim)) return false;
                    Actions.SetDim(dim);
                    return true;
                case "watch":
                    if (!OnOff(rest, out var watch)) return false;
                    Actions.SetWatch(watch);
                    return true;
                case "needs":
                    if (rest.ToLowerInvariant() == "off") Actions.SetNeeds(null);
                    else if (int.TryParse(rest, out var floor) && floor >= -100 && floor <= 100) Actions.SetNeeds(floor);
                    else return false;
                    return true;
                case "families":
                    var homeless = Residency.Homeless();
                    Actions.Say("Households with no house", homeless.Count == 0 ? Actions.NoHomeless : string.Join("\n", homeless.Select(Actions.Describe)));
                    return true;
                case "who":
                    {
                        var vm = Actions.HomeLot(out var house, out var problem);
                        if (vm == null) Actions.Say("Apartments", problem);
                        else Actions.Say("House " + house, string.Join("\n", Residency.Residents(house).Select(x => Actions.Describe(x) + (Actions.IsPlayed(vm, x) ? "  (played)" : ""))));
                        return true;
                    }
                case "units":
                    {
                        var vm = Actions.SharedLot(out var house, out var file, out var problem);
                        if (vm == null) Actions.Say("Apartments", problem);
                        else Actions.Say("Apartments in house " + house, string.Join("\n", Residency.Residents(house).Select(x => Actions.UnitLine(vm, file, x))) + "\n\n" + Actions.RentLine(file));
                        return true;
                    }
                case "movein":
                    return Named(rest, Residency.Homeless(), "No household with no house matches \"" + rest + "\". See: apt families", Actions.MoveIn);
                case "moveout":
                    return Named(rest, Actions.NeighboursHere(), "No neighbouring household matches \"" + rest + "\". See: apt who", Actions.MoveOut);
                case "switch":
                    return Named(rest, Actions.NeighboursHere(), "No neighbouring household matches \"" + rest + "\". See: apt who", Actions.Switch);
                case "claim":
                    if (rest == "") Actions.Say("Apartments", Actions.Claim(null));
                    else Named(rest, Actions.ResidentsHere(), "No household here matches \"" + rest + "\". See: apt who", Actions.Claim);
                    return true;
                case "unclaim":
                    Actions.Say("Apartments", Actions.Unclaim());
                    return true;
                case "rent":
                    int rate;
                    if (rest.ToLowerInvariant() == "off") rate = 0;
                    else if (!int.TryParse(rest, out rate) || rate < 0 || rate > Actions.MostRent) return false;
                    Actions.Say("Apartments", Actions.SetRent(rate));
                    return true;
            }
            return false;
        }

        /// <summary>Do something to the household the player named, and say how it went.</summary>
        private static bool Named(string text, List<FAMI> among, string notFound, Func<FAMI, string> action)
        {
            var family = Residency.Find(among, text);
            var result = (family == null) ? notFound : action(family);
            if (result != null) Actions.Say("Apartments", result);
            return true;
        }

        private static bool OnOff(string text, out bool option)
        {
            text = text.ToLowerInvariant();
            option = (text == "" || text == "on");
            return option || text == "off";
        }
    }
}
