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
using HarmonyLib;
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
    ///  3. The game has checked the played family is on the lot: put the other households on it too, and mark who
    ///     is played.
    ///  4. The lot ticks: hold the neighbours' needs up, bring back any the game sent away, and write down what
    ///     they do.
    ///  5. The game takes or gives money: write down who caused it (it always goes to the played household).
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
                if (Screen?.vm == vm) Say("Rent", message);
            };
            AptLog.Write("Apartments prototype loaded. Console: apt help");
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            //normally done already (3). this covers a lot that reached the screen another way
            if (lotPath != null && !Neighbours.IsShared(vm)) Neighbours.Reconcile(vm, "lot loaded");
        }

        public void OnLotSaved(VM vm, string lotPath)
        {
            Neighbours.SaveLot(vm);
        }

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
            harmony.Patch(Find(typeof(VMTS1LotState), "VerifyFamily"), postfix: Mine(nameof(AfterVerifyFamily)));
            // 4
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 5
            harmony.Patch(Find(typeof(VMTS1Budget), "Execute"), prefix: Mine(nameof(BeforeBudget)), postfix: Mine(nameof(AfterBudget)));
        }

        // ---- 1 ----

        public static void AfterNeighbourhoodLoad(TS1NeighborhoodProvider __instance)
        {
            try
            {
                Residency.FixTable(__instance);
            }
            catch (Exception e)
            {
                AptLog.Write("Could not read the shared houses of this neighbourhood: " + e.Message);
            }
        }

        // ---- 2 ----

        public static void AfterMoveOut(TS1NeighborhoodProvider __instance, short houseID)
        {
            Residency.AfterMoveOut(__instance, houseID);
        }

        // ---- 3 ----

        public static void AfterVerifyFamily(VM vm)
        {
            Neighbours.Reconcile(vm, "family checked");
        }

        // ---- 4 ----

        public static void AfterTick(VM __instance)
        {
            if (__instance.TS1) Neighbours.AfterTick(__instance);
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

        // ---- the console ----

        private const string Help =
            "apt who - the households in this house\n" +
            "apt families - households with no house, which can move in\n" +
            "apt movein <number or name> - move a household in beside yours\n" +
            "apt moveout <number or name> - move a neighbouring household out\n" +
            "apt switch <number or name> - save, and play that household\n" +
            "apt claim [number or name] - the room your Sim is in joins your apartment (or that household's)\n" +
            "apt unclaim - the room your Sim is in is shared again\n" +
            "apt units - each household's apartment and rent\n" +
            "apt rent <simoleons per tile per day>|off - the rent on this lot\n" +
            "apt keep on|off - bring neighbours back when the game sends them away\n" +
            "apt needs <-100 to 100>|off - neighbours' needs never fall below this\n" +
            "apt treat visitor|family - what the game's scripts are told neighbours are\n" +
            "apt watch on|off - write down what neighbours do\n" +
            "apt log - where the record is kept";

        private static bool Command(string[] args)
        {
            var verb = (args.Length == 0) ? "help" : args[0].ToLowerInvariant();
            var rest = string.Join(" ", args.Skip(1));
            switch (verb)
            {
                case "help":
                    Say("Apartments", Help);
                    return true;
                case "log":
                    Say("Apartments", "The record is kept in:\n" + AptLog.FilePath);
                    return true;
                case "keep":
                    if (!OnOff(rest, ref Neighbours.KeepHome)) return false;
                    AptLog.Write("Setting: keep " + (Neighbours.KeepHome ? "on" : "off"));
                    return true;
                case "watch":
                    if (!OnOff(rest, ref Neighbours.Watch)) return false;
                    AptLog.Write("Setting: watch " + (Neighbours.Watch ? "on" : "off"));
                    return true;
                case "needs":
                    if (rest.ToLowerInvariant() == "off") Neighbours.NeedsFloor = null;
                    else if (int.TryParse(rest, out var floor) && floor >= -100 && floor <= 100) Neighbours.NeedsFloor = floor;
                    else return false;
                    AptLog.Write("Setting: needs " + (Neighbours.NeedsFloor?.ToString() ?? "off"));
                    return true;
                case "treat":
                    if (!Enum.TryParse<Treatment>(rest, true, out var treat) || !Enum.IsDefined(typeof(Treatment), treat)) return false;
                    Neighbours.Treat = treat;
                    AptLog.Write("Setting: treat " + treat.ToString().ToLowerInvariant());
                    var lot = Screen?.vm;
                    if (lot != null) Neighbours.Reconcile(lot, "treatment changed");
                    return true;
                case "families":
                    var homeless = Residency.Homeless();
                    Say("Households with no house", homeless.Count == 0 ? "There are none. Make a family first and leave it in the family bin." : string.Join("\n", homeless.Select(Describe)));
                    return true;
                case "who":
                    return Who();
                case "movein":
                    return MoveIn(rest);
                case "moveout":
                    return MoveOut(rest);
                case "switch":
                    return Switch(rest);
                case "claim":
                    return Claim(rest);
                case "unclaim":
                    return Unclaim();
                case "units":
                    return ShowUnits();
                case "rent":
                    return SetRent(rest);
            }
            return false;
        }

        private static bool OnOff(string text, ref bool option)
        {
            text = text.ToLowerInvariant();
            if (text == "" || text == "on") option = true;
            else if (text == "off") option = false;
            else return false;
            return true;
        }

        private static TS1GameScreen Screen => GameFacade.Screens.CurrentUIScreen as TS1GameScreen;

        private static string Describe(FAMI family)
        {
            return Residency.NameOf(family) + ": " + family.FamilyGUIDs.Length + " Sim" + (family.FamilyGUIDs.Length == 1 ? "" : "s") + ", " + family.Budget + " simoleons";
        }

        /// <summary>The lot being played, if its family is at home there. Says why not otherwise.</summary>
        private static VM HomeLot(out int house)
        {
            var screen = Screen;
            var vm = screen?.vm;
            house = (screen == null || screen.Downtown) ? 0 : Neighbours.HomeHouse(vm);
            if (house != 0) return vm;
            Say("Apartments", "This works while you are playing a household in its own house.");
            return null;
        }

        private static bool Who()
        {
            var vm = HomeLot(out var house);
            if (vm == null) return true;
            var played = vm.TS1State.CurrentFamily;
            var lines = Residency.Residents(house).Select(x => Describe(x) + (x.ChunkID == played.ChunkID ? "  (played)" : ""));
            Say("House " + house, string.Join("\n", lines));
            return true;
        }

        private static bool MoveIn(string text)
        {
            var vm = HomeLot(out var house);
            if (vm == null) return true;
            var family = Residency.Find(Residency.Homeless(), text);
            if (family == null)
            {
                Say("Apartments", "No household with no house matches \"" + text + "\". See: apt families");
                return true;
            }
            family.HouseNumber = house; //its own record names the house. the game's table still names the played household
            AptLog.Write(Residency.NameOf(family) + " moved into house " + house + ".");
            Neighbours.Reconcile(vm, "moved in");
            Say("Apartments", Residency.NameOf(family) + " now live here too. They are by the mailbox.\n\nSave the lot to keep this.");
            return true;
        }

        private static bool MoveOut(string text)
        {
            var vm = HomeLot(out var house);
            if (vm == null) return true;
            var family = Residency.Find(Neighbours.Others(vm), text);
            if (family == null)
            {
                Say("Apartments", "No neighbouring household matches \"" + text + "\". See: apt who\n\nTo move the household you are playing out, play another first (apt switch).");
                return true;
            }
            var removed = Neighbours.RemoveFromLot(vm, family);
            family.HouseNumber = 0;
            AptLog.Write(Residency.NameOf(family) + " moved out of house " + house + " (" + removed + " Sims taken off the lot).");
            Neighbours.Reconcile(vm, "moved out");
            Say("Apartments", Residency.NameOf(family) + " moved out, and are back in the family bin. Their things stay.\n\nSave the lot to keep this.");
            return true;
        }

        private static bool Switch(string text)
        {
            var vm = HomeLot(out var house);
            if (vm == null) return true;
            var family = Residency.Find(Neighbours.Others(vm), text);
            if (family == null)
            {
                Say("Apartments", "No neighbouring household matches \"" + text + "\". See: apt who");
                return true;
            }
            //not from inside the console's own event: the whole lot is about to be replaced
            GameThread.NextUpdate(x =>
            {
                var screen = Screen;
                if (screen?.vm != vm) return;
                AptLog.Write("=== Switching house " + house + " to " + Residency.NameOf(family) + ". Saving first.");
                screen.Save();
                Residency.SetPlayed(family);
                screen.ActiveFamily = family;
                screen.InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);
            });
            return true;
        }

        // ---- apartments ----

        /// <summary>A shared lot being played and its file, and where the Sim being played is standing. Says why not otherwise.</summary>
        private static VM SharedLot(out int house, out LotFile file)
        {
            file = null;
            var vm = HomeLot(out house);
            if (vm == null) return null;
            file = Neighbours.FileOf(vm);
            if (file != null) return vm;
            Say("Apartments", "Only one household lives here. Move another in first: apt movein");
            return null;
        }

        private static bool StandingAt(VM vm, out FSO.LotView.Model.LotTilePos pos)
        {
            pos = FSO.LotView.Model.LotTilePos.OUT_OF_WORLD;
            var sim = Screen?.SelectedAvatar;
            if (sim == null || sim.Position == FSO.LotView.Model.LotTilePos.OUT_OF_WORLD)
            {
                Say("Apartments", "Choose one of your Sims and stand them in the room first.");
                return false;
            }
            pos = sim.Position;
            return true;
        }

        private static bool Claim(string text)
        {
            var vm = SharedLot(out var house, out var file);
            if (vm == null) return true;
            var residents = Residency.Residents(house);
            var family = (text == "") ? vm.TS1State.CurrentFamily : Residency.Find(residents, text);
            if (family == null)
            {
                Say("Apartments", "No household here matches \"" + text + "\". See: apt who");
                return true;
            }
            if (!StandingAt(vm, out var pos)) return true;
            var problem = Units.Claim(vm, file, family.ChunkID, pos, id => Residency.NameOf(residents.FirstOrDefault(x => x.ChunkID == id)));
            if (problem != null)
            {
                Say("Apartments", problem);
                return true;
            }
            AptLog.Write("Room at floor " + pos.Level + ", tile " + pos.TileX + "," + pos.TileY + " claimed for " + Residency.NameOf(family) + ".");
            Say("Apartments", "This room is now part of the apartment of " + Residency.NameOf(family) + ".\n\n" + UnitLine(vm, file, family) + "\n\nSave the lot to keep this.");
            return true;
        }

        private static bool Unclaim()
        {
            var vm = SharedLot(out var house, out var file);
            if (vm == null) return true;
            if (!StandingAt(vm, out var pos)) return true;
            var owner = Units.Unclaim(vm, file, pos);
            if (owner == 0)
            {
                Say("Apartments", "This room is not part of anyone's apartment.");
                return true;
            }
            var family = Residency.Residents(house).FirstOrDefault(x => x.ChunkID == owner);
            AptLog.Write("Room at floor " + pos.Level + ", tile " + pos.TileX + "," + pos.TileY + " is shared again (was " + Residency.NameOf(family) + "'s).");
            Say("Apartments", "This room is shared again. It was part of the apartment of " + Residency.NameOf(family) + ".\n\nSave the lot to keep this.");
            return true;
        }

        private static string UnitLine(VM vm, LotFile file, FAMI family)
        {
            var rooms = Units.RoomCount(vm, file, family.ChunkID);
            if (rooms == 0) return Residency.NameOf(family) + ": no apartment yet";
            var area = Units.Area(vm, file, family.ChunkID);
            var owed = file.Rent.TryGetValue(family.ChunkID, out var account) ? account.Owed : 0;
            return Residency.NameOf(family) + ": " + rooms + " room" + (rooms == 1 ? "" : "s") + ", " + area + " tiles, rent " + Units.DailyRent(area, file.Rate) + " a day"
                + (owed > 0 ? ", " + owed + " owed" : "");
        }

        private static bool ShowUnits()
        {
            var vm = SharedLot(out var house, out var file);
            if (vm == null) return true;
            var lines = Residency.Residents(house).Select(x => UnitLine(vm, file, x)).ToList();
            lines.Add("");
            lines.Add(file.Rate == 0 ? "There is no rent on this lot." : ("Rent is " + file.Rate + " for each tile each day, paid by the household being played. Every other room, and outdoors, is shared."));
            Say("Apartments in house " + house, string.Join("\n", lines));
            return true;
        }

        private static bool SetRent(string text)
        {
            int rate;
            if (text.ToLowerInvariant() == "off") rate = 0;
            else if (!int.TryParse(text, out rate) || rate < 0 || rate > 1000) return false;
            var vm = SharedLot(out var house, out var file);
            if (vm == null) return true;
            file.Rate = rate;
            AptLog.Write("Rent on house " + house + " set to " + rate + " a tile a day.");
            Say("Apartments", (rate == 0 ? "There is now no rent on this lot." : ("Rent is now " + rate + " for each tile each day.")) + "\n\nSave the lot to keep this.");
            return true;
        }

        private static void Say(string title, string message)
        {
            AptLog.Write(title + ": " + message.Replace("\n", " | "));
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
