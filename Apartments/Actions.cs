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
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Everything the player can do with apartments. The Apartments window and the console both come here, so they
    /// cannot differ. Each action returns what to tell the player (and writes it to the log); none shows anything
    /// itself.
    /// </summary>
    public static class Actions
    {
        public const int MostRent = 1000;
        public const string NoHomeless = "There are none. Make a family first and leave it in the family bin.";
        private const string KeepNote = "Save the lot to keep this.";

        public static TS1GameScreen Screen => GameFacade.Screens.CurrentUIScreen as TS1GameScreen;

        // ---- where we are ----

        /// <summary>The lot being played, if its family is at home there. Otherwise null, and why.</summary>
        public static VM HomeLot(out int house, out string problem)
        {
            problem = null;
            var screen = Screen;
            var vm = screen?.vm;
            house = (screen == null || screen.Downtown) ? 0 : Neighbours.HomeHouse(vm);
            if (house != 0) return vm;
            problem = "This works while you are playing a household in its own house.";
            return null;
        }

        /// <summary>The lot being played and its apartment file, if more than one household lives there.</summary>
        public static VM SharedLot(out int house, out LotFile file, out string problem)
        {
            file = null;
            var vm = HomeLot(out house, out problem);
            if (vm == null) return null;
            file = Neighbours.FileOf(vm);
            if (file != null) return vm;
            problem = "Only one household lives here. Move another in first.";
            return null;
        }

        public static bool IsPlayed(VM vm, FAMI family) => vm?.TS1State?.CurrentFamily?.ChunkID == family?.ChunkID;

        public static List<FAMI> ResidentsHere()
        {
            return (HomeLot(out var house, out _) == null) ? new List<FAMI>() : Residency.Residents(house);
        }

        public static List<FAMI> NeighboursHere()
        {
            var vm = HomeLot(out _, out _);
            return (vm == null) ? new List<FAMI>() : Neighbours.Others(vm);
        }

        /// <summary>Where the Sim being played is standing, or why that cannot be told.</summary>
        private static bool StandingAt(out LotTilePos pos, out string problem)
        {
            problem = null;
            pos = LotTilePos.OUT_OF_WORLD;
            var sim = Screen?.SelectedAvatar;
            if (sim == null || sim.Position == LotTilePos.OUT_OF_WORLD)
            {
                problem = "Choose one of your Sims and stand them in the room first.";
                return false;
            }
            pos = sim.Position;
            return true;
        }

        // ---- descriptions ----

        public static string Describe(FAMI family)
        {
            return Residency.NameOf(family) + ": " + family.FamilyGUIDs.Length + " Sim" + (family.FamilyGUIDs.Length == 1 ? "" : "s") + ", " + family.Budget + " simoleons";
        }

        public static string UnitLine(VM vm, LotFile file, FAMI family)
        {
            return Residency.NameOf(family) + ": " + UnitText(vm, file, family);
        }

        /// <summary>A household's apartment in a few words, without its name.</summary>
        public static string UnitText(VM vm, LotFile file, FAMI family)
        {
            var rooms = (file == null) ? 0 : Units.RoomCount(vm, file, family.ChunkID);
            if (rooms == 0) return "no apartment yet";
            var area = Units.Area(vm, file, family.ChunkID);
            var owed = file.Rent.TryGetValue(family.ChunkID, out var account) ? account.Owed : 0;
            return rooms + " room" + (rooms == 1 ? "" : "s") + ", " + area + " tiles, rent " + Units.DailyRent(area, file.Rate) + " a day"
                + (owed > 0 ? ", " + owed + " owed" : "");
        }

        public static string RentLine(LotFile file)
        {
            return file.Rate == 0 ? "There is no rent on this lot."
                : ("Rent is " + file.Rate + " for each tile each day, paid by the household being played. Every other room, and outdoors, is shared.");
        }

        /// <summary>What the room the played Sim is standing in is: whose, or shared, or not a room. Null owner means shared or none.</summary>
        public static string RoomHere(out FAMI owner, out bool isRoom)
        {
            owner = null;
            isRoom = false;
            var vm = SharedLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            if (!StandingAt(out var pos, out problem)) return problem;
            var room = Units.IndoorRoomAt(vm, pos);
            if (room == 0) return "Your Sim is not in a room (outdoors, or a room open to the outdoors). That is shared space.";
            isRoom = true;
            if (!Units.RoomOwners(vm, file).TryGetValue(room, out var id)) return "The room your Sim is in is shared: it is nobody's apartment.";
            owner = Residency.Residents(house).FirstOrDefault(x => x.ChunkID == id);
            return "The room your Sim is in is part of the apartment of " + Residency.NameOf(owner) + ".";
        }

        // ---- households ----

        public static string MoveIn(FAMI family)
        {
            var vm = HomeLot(out var house, out var problem);
            if (vm == null) return problem;
            if (family.HouseNumber != 0) return Residency.NameOf(family) + " already have a house.";
            family.HouseNumber = house; //its own record names the house. the game's table still names the played household
            AptLog.Write(Residency.NameOf(family) + " moved into house " + house + ".");
            Neighbours.Reconcile(vm, "moved in");
            return Residency.NameOf(family) + " now live here too. They are by the mailbox. " + KeepNote;
        }

        public static string MoveOut(FAMI family)
        {
            var vm = HomeLot(out var house, out var problem);
            if (vm == null) return problem;
            if (IsPlayed(vm, family)) return "To move the household you are playing out, play another first.";
            if (family.HouseNumber != house) return Residency.NameOf(family) + " do not live here.";
            var removed = Neighbours.RemoveFromLot(vm, family);
            family.HouseNumber = 0;
            AptLog.Write(Residency.NameOf(family) + " moved out of house " + house + " (" + removed + " Sims taken off the lot).");
            Neighbours.Reconcile(vm, "moved out");
            return Residency.NameOf(family) + " moved out, and are back in the family bin. Their things stay. " + KeepNote;
        }

        /// <summary>Save, then play another of the house's households. The lot is replaced, a moment from now. Returns a problem, or null.</summary>
        public static string Switch(FAMI family)
        {
            var vm = HomeLot(out var house, out var problem);
            if (vm == null) return problem;
            if (IsPlayed(vm, family)) return "You are already playing " + Residency.NameOf(family) + ".";
            if (family.HouseNumber != house) return Residency.NameOf(family) + " do not live here.";
            //not from inside whatever asked (a console line, a button): the whole lot is about to be replaced
            GameThread.NextUpdate(x =>
            {
                var screen = Screen;
                if (screen?.vm != vm) return;
                UIApartmentsDialog.CloseIfOpen();
                AptLog.Write("=== Switching house " + house + " to " + Residency.NameOf(family) + ". Saving first.");
                screen.Save();
                Residency.SetPlayed(family);
                screen.ActiveFamily = family;
                screen.InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);
            });
            return null;
        }

        // ---- apartments ----

        /// <summary>The room the played Sim is in joins a household's apartment (the played one's, if none is given).</summary>
        public static string Claim(FAMI family)
        {
            var vm = SharedLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            family = family ?? vm.TS1State.CurrentFamily;
            if (!StandingAt(out var pos, out problem)) return problem;
            var residents = Residency.Residents(house);
            problem = Units.Claim(vm, file, family.ChunkID, pos, id => Residency.NameOf(residents.FirstOrDefault(x => x.ChunkID == id)));
            if (problem != null) return problem;
            AptLog.Write("Room at floor " + pos.Level + ", tile " + pos.TileX + "," + pos.TileY + " claimed for " + Residency.NameOf(family) + ".");
            return "This room is now part of the apartment of " + Residency.NameOf(family) + " (" + UnitText(vm, file, family) + "). " + KeepNote;
        }

        /// <summary>The room the played Sim is in is shared again.</summary>
        public static string Unclaim()
        {
            var vm = SharedLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            if (!StandingAt(out var pos, out problem)) return problem;
            var owner = Units.Unclaim(vm, file, pos);
            if (owner == 0) return "This room is not part of anyone's apartment.";
            var family = Residency.Residents(house).FirstOrDefault(x => x.ChunkID == owner);
            AptLog.Write("Room at floor " + pos.Level + ", tile " + pos.TileX + "," + pos.TileY + " is shared again (was " + Residency.NameOf(family) + "'s).");
            return "This room is shared again. It was part of the apartment of " + Residency.NameOf(family) + ". " + KeepNote;
        }

        public static string SetRent(int rate)
        {
            var vm = SharedLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            rate = System.Math.Max(0, System.Math.Min(MostRent, rate));
            file.Rate = rate;
            AptLog.Write("Rent on house " + house + " set to " + rate + " a tile a day.");
            return (rate == 0 ? "There is now no rent on this lot. " : ("Rent is now " + rate + " for each tile each day. ")) + KeepNote;
        }

        // ---- settings for trying things out. they last until the game closes ----

        public static void SetKeep(bool on)
        {
            Neighbours.KeepHome = on;
            AptLog.Write("Setting: keep " + (on ? "on" : "off"));
        }

        public static void SetWatch(bool on)
        {
            Neighbours.Watch = on;
            AptLog.Write("Setting: watch " + (on ? "on" : "off"));
        }

        public static void SetNeeds(int? floor)
        {
            Neighbours.NeedsFloor = floor;
            AptLog.Write("Setting: needs " + (floor?.ToString() ?? "off"));
        }

        public static void SetTreat(Treatment treat)
        {
            Neighbours.Treat = treat;
            AptLog.Write("Setting: treat " + treat.ToString().ToLowerInvariant());
            var vm = Screen?.vm;
            if (vm != null) Neighbours.Reconcile(vm, "treatment changed");
        }

        // ---- telling the player ----

        public static void Say(string title, string message)
        {
            if (message == null) return;
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
