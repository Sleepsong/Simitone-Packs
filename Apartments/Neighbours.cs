/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.TSOPlatform;
using FSO.SimAntics.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Apartments
{
    /// <summary>How the households that are not played are presented to the game's scripts.</summary>
    public enum Treatment
    {
        /// <summary>As visitors (person type 1): what the game makes of any Sim who is not in the played family.</summary>
        Visitor,
        /// <summary>As family (person type 0), though their family number is not the played one's. An experiment.</summary>
        Family
    }

    /// <summary>
    /// The households that live on the lot but are not being played: putting them on the lot, keeping them there, and
    /// writing down what the game does with them.
    /// </summary>
    public static class Neighbours
    {
        // ---- settings (console: apt keep / needs / treat / watch). they last until the game closes ----

        /// <summary>Put a neighbour back when the game sends them off the lot.</summary>
        public static bool KeepHome = true;
        /// <summary>Neighbours' needs never fall below this (-100 to 100), so an unplayed household cannot starve. Null to leave them alone.</summary>
        public static int? NeedsFloor = 50;
        public static Treatment Treat = Treatment.Visitor;
        /// <summary>Write down what each neighbour is doing whenever it changes.</summary>
        public static bool Watch = true;

        private const int WatchEvery = 90; //ticks: about 3 seconds at normal speed

        private static readonly VMMotive[] Needs = {
            VMMotive.Energy, VMMotive.Comfort, VMMotive.Hunger, VMMotive.Hygiene, VMMotive.Bladder, VMMotive.Social, VMMotive.Fun
        };

        private class Seen
        {
            public bool Present;
            public string Doing;
            public string Name;
            public int Returned;
        }

        private class LotState
        {
            public int House;
            public int Ticks;
            public Dictionary<uint, Seen> Sims = new Dictionary<uint, Seen>();
            /// <summary>The pack's file for the lot, as it will be written when the lot is saved.</summary>
            public LotFile File;
            /// <summary>Whose each room is, as of the last look.</summary>
            public Dictionary<ushort, int> RoomOwners = new Dictionary<ushort, int>();
            public HashSet<Tile> ReportedLost = new HashSet<Tile>();
        }

        /// <summary>Lots being played that are shared. A lot that is not here costs one lookup a tick.</summary>
        private static ConditionalWeakTable<VM, LotState> Lots = new ConditionalWeakTable<VM, LotState>();

        public static bool IsShared(VM vm) => vm != null && Lots.TryGetValue(vm, out _);

        /// <summary>The pack's file for a shared lot being played (null if the lot is not shared).</summary>
        public static LotFile FileOf(VM vm) => (vm != null && Lots.TryGetValue(vm, out var state)) ? state.File : null;

        /// <summary>The lot has been saved: keep the pack's file with it.</summary>
        public static void SaveLot(VM vm)
        {
            var house = HomeHouse(vm);
            if (house == 0) return;
            var file = FileOf(vm) ?? Residency.Load(house);
            var played = vm.TS1State.CurrentFamily.ChunkID;
            file.Played = played;
            //nothing falls due for a household while it is a neighbour: its rent starts again from the day it is next played
            foreach (var account in file.Rent.Where(x => x.Key != played)) account.Value.Day = 0;
            Residency.Save(house, file);
        }

        // ---- who is who ----

        /// <summary>The house being played, if the played family is at home in it (not downtown, on holiday, or visiting).</summary>
        public static int HomeHouse(VM vm)
        {
            if (vm == null || !vm.TS1) return 0;
            var family = vm.TS1State?.CurrentFamily;
            if (family == null || family.HouseNumber <= 0) return 0;
            return (vm.GetGlobalValue(10) == family.HouseNumber) ? family.HouseNumber : 0;
        }

        /// <summary>The other households of the house being played.</summary>
        public static List<FAMI> Others(VM vm)
        {
            var house = HomeHouse(vm);
            if (house == 0) return new List<FAMI>();
            var played = vm.TS1State.CurrentFamily.ChunkID;
            return Residency.Residents(house).Where(x => x.ChunkID != played).ToList();
        }

        private static uint GuidOf(VMAvatar avatar) => avatar.Object.OBJ.GUID;

        private static Dictionary<uint, VMAvatar> AvatarsByGuid(VM vm)
        {
            var result = new Dictionary<uint, VMAvatar>();
            foreach (var entity in vm.Entities)
            {
                if (entity is VMAvatar avatar && !avatar.Dead) result[GuidOf(avatar)] = avatar;
            }
            return result;
        }

        public static bool IsNeighbour(VM vm, VMEntity entity)
        {
            if (!(entity is VMAvatar avatar) || !IsShared(vm)) return false;
            var guid = GuidOf(avatar);
            return Others(vm).Any(x => x.FamilyGUIDs.Contains(guid));
        }

        // ---- putting everyone in place ----

        /// <summary>
        /// Make the lot agree with who lives there: every member of the other households is on it, and each Sim is
        /// marked as played or not. Safe to call again at any time.
        /// </summary>
        public static void Reconcile(VM vm, string why)
        {
            var house = HomeHouse(vm);
            if (house == 0) return;
            var others = Others(vm);
            if (others.Count == 0)
            {
                Lots.Remove(vm);
                return;
            }
            var state = Lots.GetValue(vm, x => new LotState());
            state.House = house;

            var played = vm.TS1State.CurrentFamily;
            if (state.File == null)
            {
                state.File = Residency.Load(house);
                //rent starts from today for a household that was not the one played last: while it was a neighbour, nothing fell due
                var account = state.File.AccountOf(played.ChunkID);
                if (state.File.Played != played.ChunkID || account.Day <= 0) account.Day = Units.Today(vm.Context.Clock);
                state.File.Played = played.ChunkID;
            }
            state.RoomOwners = Units.RoomOwners(vm, state.File);
            var present = AvatarsByGuid(vm);
            AptLog.Write("--- House " + house + " (" + why + "). Played: " + Residency.NameOf(played) + ". Neighbours: " + string.Join(", ", others.Select(Residency.NameOf)) + ". Treated as " + Treat.ToString().ToLowerInvariant() + "s.");

            //the played family: the game put any missing members on the lot itself. those who were neighbours until now are still marked as such
            foreach (var guid in played.FamilyGUIDs)
            {
                if (!present.TryGetValue(guid, out var sim)) continue;
                Mark(vm, sim, played, true);
            }

            foreach (var family in others)
            {
                foreach (var guid in family.FamilyGUIDs)
                {
                    if (!present.TryGetValue(guid, out var sim))
                    {
                        sim = Spawn(vm, guid, family);
                        if (sim == null) continue;
                        AptLog.Write("  Put " + sim.Name + " of " + Residency.NameOf(family) + " on the lot.");
                    }
                    Mark(vm, sim, family, false);
                    Remember(state, sim);
                }
            }
        }

        /// <summary>Put one Sim on the lot, by the mailbox, as the game does for a played family's missing members.</summary>
        private static VMAvatar Spawn(VM vm, uint guid, FAMI family)
        {
            try
            {
                var group = vm.Context.CreateObjectInstance(guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH);
                var sim = group?.Objects?.FirstOrDefault() as VMAvatar;
                if (sim == null)
                {
                    AptLog.Write("  Could not create Sim " + guid.ToString("x8") + " of " + Residency.NameOf(family) + " (is its file missing?).");
                    return null;
                }
                sim.SetPersonData(VMPersonDataVariable.TS1FamilyNumber, (short)family.ChunkID);
                if (!PlaceAtHome(vm, sim, family))
                {
                    //no apartment yet, or no free spot in it: by the mailbox, as the game does
                    var mailbox = vm.Entities.FirstOrDefault(x => x.Object.OBJ.GUID == 0xEF121974 || x.Object.OBJ.GUID == 0x1D95C9B0);
                    if (mailbox == null || !VMFindLocationFor.FindLocationFor(sim, mailbox, vm.Context, VMPlaceRequestFlags.Default))
                    {
                        AptLog.Write("  No room by the mailbox for " + sim.Name + ".");
                    }
                }
                vm.Scheduler.RescheduleInterrupt(sim);
                return sim;
            }
            catch (Exception e)
            {
                AptLog.Write("  Could not put Sim " + guid.ToString("x8") + " on the lot: " + e.Message);
                return null;
            }
        }

        /// <summary>Stand a Sim in their household's apartment: on a free tile of one of its rooms, near the tile that marks it.</summary>
        private static bool PlaceAtHome(VM vm, VMAvatar sim, FAMI family)
        {
            var file = FileOf(vm);
            if (file == null || !file.Units.TryGetValue(family.ChunkID, out var unit)) return false;
            foreach (var tile in unit)
            {
                var room = Units.IndoorRoomAt(vm, tile.ToPos());
                if (room == 0) continue;
                for (int reach = 0; reach <= 3; reach++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        for (int dy = -reach; dy <= reach; dy++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != reach) continue; //only the ring at this distance
                            var pos = new Tile(tile.Level, (short)(tile.X + dx), (short)(tile.Y + dy)).ToPos();
                            if (Units.IndoorRoomAt(vm, pos) != room) continue;
                            if (sim.SetPosition(pos, Direction.NORTH, vm.Context, VMPlaceRequestFlags.Default).Status == VMPlacementError.Success) return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Tell the game's scripts what this Sim is. A Sim whose kind changes starts their script again, since it was in the middle of being the other kind.</summary>
        private static void Mark(VM vm, VMAvatar sim, FAMI family, bool isPlayed)
        {
            short type = (short)((isPlayed || Treat == Treatment.Family) ? 0 : 1);
            var was = sim.GetPersonData(VMPersonDataVariable.PersonType);
            if (was > 1) return; //one of the game's own characters (a maid, a pet's type...): leave it be
            sim.SetPersonData(VMPersonDataVariable.TS1FamilyNumber, (short)family.ChunkID);
            sim.AvatarState.Permissions = isPlayed ? VMTSOAvatarPermissions.Owner : VMTSOAvatarPermissions.Visitor;
            if (was == type) return;
            sim.SetPersonData(VMPersonDataVariable.PersonType, type);
            try
            {
                sim.Reset(vm.Context);
            }
            catch (Exception e)
            {
                AptLog.Write("  Could not restart " + sim.Name + "'s script: " + e.Message);
            }
            AptLog.Write("  " + sim.Name + " of " + Residency.NameOf(family) + ": person type " + was + " -> " + type + (isPlayed ? " (now played)" : " (now a neighbour)") + ".");
        }

        /// <summary>Take a household's Sims off the lot (it has moved out).</summary>
        public static int RemoveFromLot(VM vm, FAMI family)
        {
            var count = 0;
            foreach (var pair in AvatarsByGuid(vm))
            {
                if (!family.FamilyGUIDs.Contains(pair.Key)) continue;
                pair.Value.Delete(true, vm.Context);
                count++;
            }
            if (Lots.TryGetValue(vm, out var state))
            {
                foreach (var guid in family.FamilyGUIDs) state.Sims.Remove(guid);
            }
            return count;
        }

        // ---- watching ----

        private static string Describe(VM vm, VMAvatar sim, LotState state, FAMI family)
        {
            string doing = "idle";
            try
            {
                var thread = sim.Thread;
                var action = thread?.ActiveAction ?? ((thread?.Queue != null && thread.Queue.Count > 0) ? thread.Queue[0] : null);
                if (action != null && !string.IsNullOrWhiteSpace(action.Name)) doing = action.Name.Trim();
            }
            catch
            {
            }
            var where = "off the map";
            if (sim.Position != LotTilePos.OUT_OF_WORLD)
            {
                var room = vm.Context.GetObjectRoom(sim);
                where = "floor " + sim.Position.Level + ", room " + room;
                if (state.File != null && state.File.Units.Count > 0)
                {
                    if (!state.RoomOwners.TryGetValue(room, out var owner)) where += ", shared space";
                    else if (owner == family.ChunkID) where += ", their own apartment";
                    else where += ", THE APARTMENT OF " + Residency.NameOf(Residency.Residents(state.House).FirstOrDefault(x => x.ChunkID == owner));
                }
            }
            return doing + " [" + where + ", type " + sim.GetPersonData(VMPersonDataVariable.PersonType)
                + ", greeted " + sim.GetPersonData(VMPersonDataVariable.GreetStatus) + "]";
        }

        private static void Remember(LotState state, VMAvatar sim)
        {
            if (!state.Sims.TryGetValue(GuidOf(sim), out var seen)) state.Sims[GuidOf(sim)] = seen = new Seen();
            seen.Present = true;
            seen.Name = sim.Name;
        }

        /// <summary>After every tick of a lot. Does nothing on a lot that is not shared, and little on one that is.</summary>
        public static void AfterTick(VM vm)
        {
            if (!Lots.TryGetValue(vm, out var state)) return;
            if (++state.Ticks % WatchEvery != 0) return;
            if (HomeHouse(vm) != state.House) return;

            state.RoomOwners = Units.RoomOwners(vm, state.File);
            ReportLostRooms(vm, state);
            CollectRent(vm, state);

            var present = AvatarsByGuid(vm);
            foreach (var family in Others(vm))
            {
                foreach (var guid in family.FamilyGUIDs)
                {
                    if (!state.Sims.TryGetValue(guid, out var seen)) state.Sims[guid] = seen = new Seen();
                    if (present.TryGetValue(guid, out var sim))
                    {
                        seen.Present = true;
                        seen.Name = sim.Name;
                        TopUp(sim);
                        if (!Watch) continue;
                        var doing = Describe(vm, sim, state, family);
                        if (doing != seen.Doing)
                        {
                            seen.Doing = doing;
                            AptLog.Write(sim.Name + ": " + doing);
                        }
                    }
                    else
                    {
                        if (seen.Present)
                        {
                            seen.Present = false;
                            AptLog.Write((seen.Name ?? guid.ToString("x8")) + " of " + Residency.NameOf(family) + " is no longer on the lot. Last seen: " + (seen.Doing ?? "(not seen)"));
                        }
                        if (!KeepHome) continue;
                        sim = Spawn(vm, guid, family);
                        if (sim == null) continue;
                        Mark(vm, sim, family, false);
                        seen.Present = true;
                        seen.Name = sim.Name;
                        seen.Doing = null;
                        seen.Returned++;
                        AptLog.Write("  Brought " + sim.Name + " back (" + seen.Returned + " time" + (seen.Returned == 1 ? "" : "s") + " so far).");
                    }
                }
            }
        }

        // ---- apartments ----

        /// <summary>Say so, once, when a wall coming down (or anything else) leaves part of an apartment marking no room.</summary>
        private static void ReportLostRooms(VM vm, LotState state)
        {
            if (state.File == null) return;
            foreach (var family in state.File.Units.Keys.ToList())
            {
                foreach (var tile in Units.LostTiles(vm, state.File, family))
                {
                    if (!state.ReportedLost.Add(tile)) continue;
                    AptLog.Write("Part of the apartment of family " + family + " is no longer a room (floor " + tile.Level + ", tile " + tile.X + "," + tile.Y + "): it is open to the outdoors, or gone. It counts as shared until it is a room again.");
                }
            }
        }

        /// <summary>Called when rent is taken, with a message for the player if it could not all be paid.</summary>
        public static Action<VM, string> OnRentShortfall;

        /// <summary>
        /// Rent for the played household's apartment, once for each game day that has passed. Only the played household
        /// pays: the others do not progress. What cannot be paid is owed, and taken when there is money again.
        /// </summary>
        private static void CollectRent(VM vm, LotState state)
        {
            var file = state.File;
            var played = vm.TS1State?.CurrentFamily;
            if (file == null || played == null) return;
            var account = file.AccountOf(played.ChunkID);
            var today = Units.Today(vm.Context.Clock);
            if (account.Day <= 0 || today < account.Day) account.Day = today; //never set, or the clock went backwards
            var days = Units.DaysDue(account.Day, today);
            if (days == 0) return;
            account.Day = today;

            var area = Units.Area(vm, file, played.ChunkID);
            var due = Units.DailyRent(area, file.Rate) * days;
            if (due == 0 && account.Owed == 0) return;

            var budget = played.Budget;
            var owed = account.Owed;
            var paid = Units.Pay(ref budget, ref owed, due);
            played.Budget = budget;
            account.Owed = owed;
            AptLog.Write("Rent: " + Residency.NameOf(played) + " paid " + paid + " (" + days + " day" + (days == 1 ? "" : "s") + " at " + Units.DailyRent(area, file.Rate)
                + " a day for " + area + " tiles" + (owed > 0 ? "; " + owed + " still owed" : "") + "). Money now " + budget + ".");
            if (owed > 0) OnRentShortfall?.Invoke(vm, "The rent could not be paid in full. " + owed + " simoleons are owed, and will be taken when there is money.");
        }

        /// <summary>An unplayed household does not progress: its needs are held up, so nobody starves or collapses while you play another.</summary>
        private static void TopUp(VMAvatar sim)
        {
            if (NeedsFloor == null) return;
            var floor = (short)NeedsFloor.Value;
            foreach (var need in Needs)
            {
                if (sim.GetMotiveData(need) < floor) sim.SetMotiveData(need, floor);
            }
        }
    }
}
