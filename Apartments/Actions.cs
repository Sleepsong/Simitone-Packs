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
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Everything the player can do with apartments on a lot. The Apartments window and the console both come here, so
    /// they cannot differ. Each action returns what to tell the player (and writes it to the log); none shows anything
    /// itself.
    /// </summary>
    public static class Actions
    {
        public const int MostRent = 1000;
        public const string NoHomeless = "There are none. Make a family first and leave it in the family bin.";
        private const string KeepNote = "Save the lot to keep this.";

        public static TS1GameScreen Screen => GameFacade.Screens.CurrentUIScreen as TS1GameScreen;

        // ---- where we are ----

        /// <summary>
        /// The lot on screen, if it is a house being played by the family that lives there, or one being built with
        /// nobody on it. Otherwise null, and why.
        /// </summary>
        public static VM HomeLot(out int house, out string problem)
        {
            problem = null;
            var screen = Screen;
            var vm = screen?.vm;
            house = (screen == null || screen.Downtown) ? 0 : Neighbours.HomeHouse(vm);
            if (house != 0) return vm;
            problem = "This works on a house you are playing or building, not on a lot your Sims are visiting.";
            return null;
        }

        /// <summary>The lot on screen and its apartment file, if it is an apartment building.</summary>
        public static VM BuildingLot(out int house, out LotFile file, out string problem)
        {
            file = null;
            var vm = HomeLot(out house, out problem);
            if (vm == null) return null;
            file = Neighbours.FileOf(vm);
            if (file != null) return vm;
            problem = "This is an ordinary house. Make it an apartment building first.";
            return null;
        }

        /// <summary>The household being played (null while a lot is being built with nobody on it).</summary>
        public static FAMI Played(VM vm) => vm?.TS1State?.CurrentFamily;

        public static bool IsPlayed(VM vm, FAMI family) => family != null && Played(vm)?.ChunkID == family.ChunkID;

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

        /// <summary>An apartment's size and rent in a few words, from the lot as it is now.</summary>
        public static string SizeText(VM vm, LotFile file, Apartment apartment)
        {
            var rooms = Units.RoomCount(vm, file, apartment);
            if (rooms == 0) return "no rooms yet";
            var area = Units.Area(vm, file, apartment);
            return rooms + " room" + (rooms == 1 ? "" : "s") + ", " + area + " tiles, rent " + Units.DailyRent(area, file.Rate) + " a day";
        }

        /// <summary>An apartment's size and rent as they were when the lot was last saved (for when no lot is loaded).</summary>
        public static string SavedSizeText(LotFile file, Apartment apartment)
        {
            return apartment.Rooms + " room" + (apartment.Rooms == 1 ? "" : "s") + ", " + apartment.Area + " tiles, rent " + Units.DailyRent(apartment.Area, file.Rate) + " a day";
        }

        /// <summary>A household's apartment in a few words, without the household's name.</summary>
        public static string UnitText(VM vm, LotFile file, FAMI family)
        {
            var apartment = file?.ApartmentOf(family.ChunkID);
            if (apartment == null) return "no apartment";
            var owed = file.Rent.TryGetValue(family.ChunkID, out var account) ? account.Owed : 0;
            return apartment.Name + " (" + SizeText(vm, file, apartment) + (owed > 0 ? ", " + owed + " owed" : "") + ")";
        }

        public static string UnitLine(VM vm, LotFile file, FAMI family) => Residency.NameOf(family) + ": " + UnitText(vm, file, family);

        public static string RentLine(LotFile file)
        {
            return file.Rate == 0 ? "There is no rent on this lot."
                : ("Rent is " + file.Rate + " for each tile each day, paid by the household being played. Every room that is in no apartment, and outdoors, is shared.");
        }

        public static string TenantText(int house, Apartment apartment)
        {
            if (apartment.Vacant) return "vacant";
            return Residency.NameOf(Residency.Residents(house).FirstOrDefault(x => x.ChunkID == apartment.Tenant));
        }

        // ---- the building ----

        public static string MakeBuilding()
        {
            var vm = HomeLot(out var house, out var problem);
            if (vm == null) return problem;
            if (Neighbours.FileOf(vm) != null) return "This is already an apartment building.";
            Neighbours.SetBuilding(vm, true);
            AptLog.Write("House " + house + " is now an apartment building.");
            return "This is now an apartment building. Add its apartments next. " + KeepNote;
        }

        public static string MakeOrdinary()
        {
            var vm = BuildingLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            if (Residency.Residents(house).Count > 1) return "More than one household lives here. Move the others out first.";
            Neighbours.SetBuilding(vm, false);
            AptLog.Write("House " + house + " is an ordinary house again.");
            return "This is an ordinary house again. Its apartments are forgotten when you save.";
        }

        /// <summary>Add an apartment with no rooms yet.</summary>
        public static Apartment AddApartment(out string result)
        {
            var vm = BuildingLot(out var house, out var file, out result);
            if (vm == null) return null;
            var apartment = file.Add();
            AptLog.Write(apartment.Name + " added to house " + house + ".");
            result = apartment.Name + " added. " + KeepNote;
            return apartment;
        }

        public static string DeleteApartment(Apartment apartment)
        {
            var vm = BuildingLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            if (!file.Apartments.Remove(apartment)) return "That apartment is already gone.";
            AptLog.Write(apartment.Name + " of house " + house + " deleted" + (apartment.Vacant ? "." : " (family " + apartment.Tenant + " lived there, and now has no apartment)."));
            return apartment.Name + " is gone. Its rooms are shared again"
                + (apartment.Vacant ? ". " : (", and " + TenantText(house, apartment) + " have no apartment. ")) + KeepNote;
        }

        /// <summary>Add the room at a spot to an apartment.</summary>
        public static string AddRoom(Apartment apartment, LotTilePos pos)
        {
            var vm = BuildingLot(out _, out var file, out var problem);
            if (vm == null) return problem;
            if (!file.Apartments.Contains(apartment)) return "That apartment is gone.";
            problem = Units.Claim(vm, file, apartment, pos);
            if (problem != null) return problem;
            AptLog.Write("Room at floor " + pos.Level + ", tile " + pos.TileX + "," + pos.TileY + " added to " + apartment.Name + ".");
            return "Room added to " + apartment.Name + " (" + SizeText(vm, file, apartment) + "). " + KeepNote;
        }

        /// <summary>Take the room at a spot out of whatever apartment it is in.</summary>
        public static string RemoveRoom(LotTilePos pos)
        {
            var vm = BuildingLot(out _, out var file, out var problem);
            if (vm == null) return problem;
            var owner = Units.Unclaim(vm, file, pos);
            if (owner == null) return "That room is not in any apartment.";
            AptLog.Write("Room at floor " + pos.Level + ", tile " + pos.TileX + "," + pos.TileY + " taken out of " + owner.Name + ".");
            return "That room is shared again. It was part of " + owner.Name + ". " + KeepNote;
        }

        /// <summary>Make a household that lives here the tenant of an apartment (null: nobody is).</summary>
        public static string SetTenant(Apartment apartment, FAMI family)
        {
            var vm = BuildingLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            if (!file.Apartments.Contains(apartment)) return "That apartment is gone.";
            if (family != null && family.HouseNumber != house) return Residency.NameOf(family) + " do not live here.";
            if (family == null)
            {
                if (apartment.Vacant) return apartment.Name + " is already vacant.";
                var was = TenantText(house, apartment);
                apartment.Tenant = 0;
                AptLog.Write(apartment.Name + " is vacant (was " + was + "'s).");
                return apartment.Name + " is now vacant. " + was + " still live here, with no apartment. " + KeepNote;
            }
            file.SetTenant(apartment, family.ChunkID);
            AptLog.Write(apartment.Name + " is now the apartment of " + Residency.NameOf(family) + ".");
            return Residency.NameOf(family) + " now live in " + apartment.Name + ". " + KeepNote;
        }

        public static string SetRent(int rate)
        {
            var vm = BuildingLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            rate = Math.Max(0, Math.Min(MostRent, rate));
            file.Rate = rate;
            AptLog.Write("Rent on house " + house + " set to " + rate + " a tile a day.");
            return (rate == 0 ? "There is now no rent on this lot. " : ("Rent is now " + rate + " for each tile each day. ")) + KeepNote;
        }

        // ---- households ----

        /// <summary>A household from the family bin moves in beside the one being played, into an apartment if one is given.</summary>
        public static string MoveIn(FAMI family, Apartment apartment = null)
        {
            var vm = HomeLot(out var house, out var problem);
            if (vm == null) return problem;
            if (Played(vm) == null) return "Nobody lives here yet. Move the first household in from the neighbourhood screen: choose the family, then this house.";
            if (family.HouseNumber != 0) return Residency.NameOf(family) + " already have a house.";
            if (apartment != null && !apartment.Vacant) return apartment.Name + " is taken.";
            var deposit = 0;
            if (apartment != null)
            {
                var file = Neighbours.FileOf(vm);
                deposit = Units.Deposit(Units.Area(vm, file, apartment), file.Rate);
                if (family.Budget < deposit) return Residency.NameOf(family) + " cannot afford to move into " + apartment.Name + ": it costs " + deposit + " (" + Units.DepositDays + " days' rent), and they have " + family.Budget + ".";
                family.Budget -= deposit;
            }
            family.HouseNumber = house; //its own record names the house. the game's table still names the played household
            AptLog.Write(Residency.NameOf(family) + " moved into house " + house + (apartment == null ? "" : ", " + apartment.Name) + ".");
            Neighbours.Reconcile(vm, "moved in"); //(two households make it a building, if it was not one)
            if (apartment != null) Neighbours.FileOf(vm)?.SetTenant(apartment, family.ChunkID);
            Neighbours.Reconcile(vm, "moved in");
            return Residency.NameOf(family) + " now live here too, " + (apartment == null ? "with no apartment yet" : "in " + apartment.Name + ", for a deposit of " + deposit) + ". They are at home, and one of them will come out now and then. " + KeepNote;
        }

        public static string MoveOut(FAMI family)
        {
            var vm = HomeLot(out var house, out var problem);
            if (vm == null) return problem;
            if (IsPlayed(vm, family)) return "To move the household you are playing out, play another first.";
            if (family.HouseNumber != house) return Residency.NameOf(family) + " do not live here.";
            var removed = Neighbours.RemoveFromLot(vm, family);
            family.HouseNumber = 0;
            var apartment = Neighbours.FileOf(vm)?.ApartmentOf(family.ChunkID);
            if (apartment != null) apartment.Tenant = 0;
            AptLog.Write(Residency.NameOf(family) + " moved out of house " + house + " (" + removed + " Sims taken off the lot).");
            Neighbours.Reconcile(vm, "moved out");
            return Residency.NameOf(family) + " moved out, and are back in the family bin. Their things stay" + (apartment == null ? ". " : (", and " + apartment.Name + " is vacant. ")) + KeepNote;
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
                //each step is checked: a half-done switch must stop where it is, say so, and leave the game running
                if (!ApartmentsPack.Guard("saving the lot before switching", () => screen.Save()))
                {
                    Say("Apartments", "The lot could not be saved, so the household was not changed. The log says what went wrong.");
                    return;
                }
                if (!ApartmentsPack.Guard("making " + Residency.NameOf(family) + " the played household", () => Residency.SetPlayed(family)))
                {
                    Say("Apartments", "The household could not be changed. The log says what went wrong.");
                    return;
                }
                AptLog.Write("Saved. Loading the lot again for " + Residency.NameOf(family) + ".");
                if (!ApartmentsPack.Guard("loading the lot for " + Residency.NameOf(family), () =>
                {
                    screen.ActiveFamily = family;
                    screen.InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);
                }))
                {
                    Say("Apartments", "The lot could not be loaded again. Leave to the neighbourhood and come back in: the household has been changed. The log says what went wrong.");
                    return;
                }
                AptLog.Write("=== Now playing " + Residency.NameOf(family) + ".");
            });
            return null;
        }

        /// <summary>The Sim being played goes to knock at a neighbouring household's door (the same as clicking the door and choosing Knock).</summary>
        public static string Knock(FAMI family)
        {
            var vm = BuildingLot(out _, out var file, out var problem);
            if (vm == null) return problem;
            var sim = Screen?.SelectedAvatar;
            if (sim == null) return "Choose one of your Sims first.";
            var apartment = file.ApartmentOf(family.ChunkID);
            if (apartment == null) return Residency.NameOf(family) + " have no apartment to knock at.";
            if (!Doors.CanKnock(vm, sim, apartment)) return "Only a Sim of the household you are playing can knock, and not at their own door.";
            var doors = Doors.DoorsOf(vm, Neighbours.StateOf(vm), apartment);
            if (doors.Count == 0) return apartment.Name + " has no door to knock at: none of its rooms has a door to anywhere outside it.";
            var nearest = doors.OrderBy(x => Math.Abs(x.Outside.x - sim.Position.x) + Math.Abs(x.Outside.y - sim.Position.y) + Math.Abs(x.Outside.Level - sim.Position.Level) * 1000).First();
            Doors.GoAndKnock(vm, sim, apartment, nearest);
            return sim.Name + " is going to knock at " + apartment.Name + ". You are told what happens when they get there.";
        }

        // ---- the console's way of giving rooms: the room the played Sim is standing in ----

        /// <summary>The room the played Sim is in joins a household's apartment (the played one's, if none is given). The household gets an apartment if it has none.</summary>
        public static string Claim(FAMI family)
        {
            var vm = BuildingLot(out var house, out var file, out var problem);
            if (vm == null) return problem;
            family = family ?? Played(vm);
            if (family == null) return "Nobody is being played here. Use the Apartments window to add rooms.";
            if (!StandingAt(out var pos, out problem)) return problem;
            var apartment = file.ApartmentOf(family.ChunkID);
            var made = apartment == null;
            if (made)
            {
                apartment = file.Add();
                apartment.Tenant = family.ChunkID;
            }
            var result = AddRoom(apartment, pos);
            if (made && apartment.Tiles.Count == 0) file.Apartments.Remove(apartment);
            return result;
        }

        public static string Unclaim()
        {
            return StandingAt(out var pos, out var problem) ? RemoveRoom(pos) : problem;
        }

        // ---- settings for trying things out. they last until the game closes ----

        public static void SetOutings(Outings often)
        {
            Neighbours.ComeOut = often;
            AptLog.Write("Setting: outings " + often.ToString().ToLowerInvariant());
        }

        public static void SetDim(bool on)
        {
            Neighbours.Dim = on;
            AptLog.Write("Setting: dim " + (on ? "on" : "off"));
            Neighbours.DimChanged(Screen?.vm);
        }

        /// <summary>Bring a neighbour out of their apartment now (one of this household's, or anyone).</summary>
        public static string BringOut(FAMI family = null)
        {
            var vm = BuildingLot(out _, out _, out var problem);
            return (vm == null) ? problem : Neighbours.BringOut(vm, family);
        }

        /// <summary>Send the neighbours who are out back home.</summary>
        public static string SendHome()
        {
            var vm = BuildingLot(out _, out _, out var problem);
            return (vm == null) ? problem : Neighbours.SendHome(vm);
        }

        public static void SetPrivate(bool on)
        {
            Doors.Private = on;
            AptLog.Write("Setting: private " + (on ? "on" : "off"));
        }

        public static void SetShowVisitors(bool on)
        {
            Doors.ShowVisitors = on;
            AptLog.Write("Setting: visitors to the door " + (on ? "on" : "off"));
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

        /// <summary>Ask the player to choose one of several things, each a button, with Cancel last.</summary>
        public static void Choose(string title, string message, IEnumerable<KeyValuePair<string, Action>> choices, Action cancelled = null)
        {
            UIChoiceDialog.Show(title, message, choices, cancelled);
        }

        public static KeyValuePair<string, Action> Choice(string text, Action action) => new KeyValuePair<string, Action>(text, action);
    }
}
