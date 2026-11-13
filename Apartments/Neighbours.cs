/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
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
    /// <summary>How often a neighbour comes out of their apartment.</summary>
    public enum Outings
    {
        Never,
        Sometimes,
        Often
    }

    /// <summary>
    /// The households that live on the lot but are not being played.
    ///
    /// They are at home, and home is off stage: while a neighbour is in their apartment they are not on the lot at all,
    /// and the apartment is dimmed. Now and then one comes out. They appear in their apartment, walk to somewhere
    /// shared, spend a while there as any visiting Sim would, then walk home and are gone again.
    ///
    /// The first version kept every neighbour on the lot the whole time. The game took them for visitors who had just
    /// arrived: each walked to the front of the lot, rang the bell, waited, gave up and left, and was put back to do it
    /// again. A Sim who is already marked as greeted does none of that, so a neighbour who comes out is marked so.
    ///
    /// A Sim's skills, job and the rest live on the Sim itself while it is on a lot, and Simitone saves them with the
    /// lot. So before a neighbour goes off stage they are written to the Sim's record in the neighbourhood, which is
    /// where a Sim put on a lot gets them from.
    /// </summary>
    public static class Neighbours
    {
        // ---- settings (the window's Testing page, or the console). they last until the game closes ----

        public static Outings ComeOut = Outings.Sometimes;
        /// <summary>A neighbour's needs never fall below this while they are out (-100 to 100). Null to leave them alone.</summary>
        public static int? NeedsFloor = 50;
        /// <summary>Write down what each neighbour is doing whenever it changes.</summary>
        public static bool Watch = true;
        /// <summary>Dim the apartments of the households that are not played.</summary>
        public static bool Dim = true;

        private const int WatchEvery = 90; //ticks: about 3 seconds at normal speed

        //all in game minutes
        private const int FirstHour = 8, LastHour = 22;           //neighbours come out between these hours
        private const int OutingShortest = 45, OutingLongest = 120;
        private const int WalkHomeLimit = 30;                     //after this long walking home, they are simply taken in
        private const int MostOutAtOnce = 2;

        //what the game calls a visitor who has been let in. (worked out from play: a neighbour with this stayed where
        //they were; one without it went to ring the bell)
        private const short Greeted = 1;

        private static readonly VMMotive[] Needs = {
            VMMotive.Energy, VMMotive.Comfort, VMMotive.Hunger, VMMotive.Hygiene, VMMotive.Bladder, VMMotive.Social, VMMotive.Fun
        };

        private static readonly Random Dice = new Random();

        /// <summary>A neighbour who is out of their apartment.</summary>
        private class Outing
        {
            public uint Guid;
            public int Family;
            public string Name;
            public long Ends;            //when they turn for home
            public long HomewardSince = -1;
            public string Doing;
        }

        private class LotState
        {
            public int House;
            public int Ticks;
            /// <summary>The pack's file for the lot, as it will be written when the lot is saved.</summary>
            public LotFile File;
            /// <summary>Whose each room is, as of the last look.</summary>
            public Dictionary<ushort, int> RoomOwners = new Dictionary<ushort, int>();
            /// <summary>The rooms to draw dimmed (by the room their light is worked out with).</summary>
            public HashSet<ushort> DimRooms = new HashSet<ushort>();
            public HashSet<Tile> ReportedLost = new HashSet<Tile>();
            public Dictionary<uint, Outing> Out = new Dictionary<uint, Outing>();
            public long NextOuting = -1;
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
            var played = vm.TS1State?.CurrentFamily?.ChunkID ?? 0;
            if (played != 0) file.Played = played;
            //nothing falls due for a household while it is a neighbour: its rent starts again from the day it is next played
            foreach (var account in file.Rent.Where(x => x.Key != played)) account.Value.Day = 0;
            if (file.Building && vm.Context?.RoomInfo != null) Units.NoteSizes(vm, file);
            Residency.Save(house, file);
        }

        /// <summary>
        /// Make the lot being played or built an apartment building (or an ordinary house again). Returns its file.
        /// </summary>
        public static LotFile SetBuilding(VM vm, bool building)
        {
            var house = HomeHouse(vm);
            if (house == 0) return null;
            if (building)
            {
                var state = Lots.GetValue(vm, x => new LotState());
                state.House = house;
                if (state.File == null) state.File = Residency.Load(house);
                state.File.Building = true;
                Reconcile(vm, "made an apartment building");
                return state.File;
            }
            if (Lots.TryGetValue(vm, out var old))
            {
                old.File.Building = false;
                old.DimRooms.Clear();
                Lots.Remove(vm);
                Relight(vm);
                return old.File;
            }
            return null;
        }

        // ---- who is who ----

        /// <summary>
        /// The house this lot is, if it is one the pack has business with: a house being played by the family that
        /// lives in it (not a lot they are visiting), or a house with nobody on it, being built.
        /// </summary>
        public static int HomeHouse(VM vm)
        {
            if (vm == null || !vm.TS1) return 0;
            int house = vm.GetGlobalValue(10);
            if (house <= 0) return 0;
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return house; //nobody is being played: the lot is being built
            return (family.HouseNumber == house) ? house : 0;
        }

        /// <summary>The households of this house other than the one being played (all of them, if none is).</summary>
        public static List<FAMI> Others(VM vm)
        {
            var house = HomeHouse(vm);
            if (house == 0) return new List<FAMI>();
            var played = vm.TS1State?.CurrentFamily?.ChunkID ?? 0;
            return Residency.Residents(house).Where(x => x.ChunkID != played).ToList();
        }

        private static uint GuidOf(VMAvatar avatar) => avatar.Object.OBJ.GUID;

        public static Dictionary<uint, VMAvatar> AvatarsByGuid(VM vm)
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

        /// <summary>The game's clock as one number of minutes that only goes up.</summary>
        private static long Now(VM vm)
        {
            var clock = vm.Context.Clock;
            return (long)Units.Today(clock) * 1440 + clock.Hours * 60 + clock.Minutes;
        }

        // ---- putting everyone in place ----

        /// <summary>
        /// Make the lot agree with who lives there. The played household is on the lot, in its apartment if it was just
        /// put there. Everyone else is at home, off stage. Safe to call again at any time.
        /// </summary>
        /// <param name="alreadyThere">The Sims who were on the lot before the game put the played family's missing members on it (null if not known).</param>
        public static void Reconcile(VM vm, string why, HashSet<uint> alreadyThere = null)
        {
            var house = HomeHouse(vm);
            if (house == 0) return;
            var others = Others(vm);
            var played = vm.TS1State?.CurrentFamily; //null while the lot is being built with nobody on it

            Lots.TryGetValue(vm, out var state);
            var file = state?.File ?? Residency.Load(house);
            if (others.Count + (played == null ? 0 : 1) >= 2) file.Building = true; //two households share it: it is one
            if (!file.Building)
            {
                if (state != null)
                {
                    //an ordinary house again: nothing is dimmed any more
                    state.DimRooms.Clear();
                    Lots.Remove(vm);
                    Relight(vm);
                }
                return;
            }
            if (state == null)
            {
                state = Lots.GetValue(vm, x => new LotState());
                state.File = file;
                if (played != null)
                {
                    //rent starts from today for a household that was not the one played last: while it was a neighbour, nothing fell due
                    var account = file.AccountOf(played.ChunkID);
                    if (file.Played != played.ChunkID || account.Day <= 0) account.Day = Units.Today(vm.Context.Clock);
                    file.Played = played.ChunkID;
                }
            }
            state.House = house;
            state.RoomOwners = Units.RoomOwners(vm, state.File);
            var present = AvatarsByGuid(vm);
            AptLog.Write("--- House " + house + " (" + why + "). " + (played == null ? "Nobody is being played. Households: " : ("Played: " + Residency.NameOf(played) + ". Neighbours: "))
                + (others.Count == 0 ? "none" : string.Join(", ", others.Select(Residency.NameOf))) + ". Apartments: "
                + (state.File.Apartments.Count == 0 ? "none" : string.Join(", ", state.File.Apartments.Select(x => x.Name + (x.Vacant ? " (vacant)" : " (family " + x.Tenant + ")")))) + ".");

            foreach (var guid in (played == null) ? new uint[0] : played.FamilyGUIDs)
            {
                if (!present.TryGetValue(guid, out var sim)) continue;
                state.Out.Remove(guid);
                Mark(vm, sim, played, true);
                if (alreadyThere != null && !alreadyThere.Contains(guid))
                {
                    //the game has just put them by the mailbox. they were at home: that is where they are
                    RestoreNeeds(state, sim);
                    if (PlaceAtHome(vm, sim, played)) AptLog.Write("  " + sim.Name + " is at home, in their apartment.");
                }
            }

            //anyone else who lives here and is standing on the lot goes in (the household played until now, a neighbour
            //who was out when the lot was saved, or neighbours from when they were kept on the lot all the time)
            //(not while nobody is played: then this is a lot being built or looked at, and its Sims are left as they are)
            foreach (var family in (played == null) ? new List<FAMI>() : others)
            {
                foreach (var guid in family.FamilyGUIDs)
                {
                    if (!present.TryGetValue(guid, out var sim)) continue;
                    if (state.Out.ContainsKey(guid)) continue; //out on purpose, just now
                    GoOffStage(vm, state, sim, family, "went in");
                }
            }

            SetDimRooms(vm, state);
            if (state.NextOuting < 0) ScheduleOuting(vm, state);
        }

        /// <summary>Put one Sim on the lot: in their apartment if they have one, or else by the mailbox, as the game does.</summary>
        private static VMAvatar Spawn(VM vm, LotState state, uint guid, FAMI family)
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
                //no apartment yet, or no free spot in it
                var mailbox = vm.Entities.FirstOrDefault(x => x.Object.OBJ.GUID == 0xEF121974 || x.Object.OBJ.GUID == 0x1D95C9B0);
                if (mailbox == null || !VMFindLocationFor.FindLocationFor(sim, mailbox, vm.Context, VMPlaceRequestFlags.Default))
                {
                    AptLog.Write("  Nowhere to put " + sim.Name + ": no apartment, and no room by the mailbox.");
                    sim.Delete(true, vm.Context);
                    return null;
                }
            }
            RestoreNeeds(state, sim);
            vm.Scheduler.RescheduleInterrupt(sim);
            return sim;
        }

        /// <summary>Stand a Sim in their household's apartment: on a free tile of one of its rooms, near the tile that marks it.</summary>
        private static bool PlaceAtHome(VM vm, VMAvatar sim, FAMI family)
        {
            var file = FileOf(vm);
            var unit = file?.ApartmentOf(family.ChunkID)?.Tiles;
            if (unit == null) return false;
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
            short type = (short)(isPlayed ? 0 : 1);
            var was = sim.GetPersonData(VMPersonDataVariable.PersonType);
            if (was > 1) return; //one of the game's own characters: leave it be
            sim.SetPersonData(VMPersonDataVariable.TS1FamilyNumber, (short)family.ChunkID);
            sim.AvatarState.Permissions = isPlayed ? VMTSOAvatarPermissions.Owner : VMTSOAvatarPermissions.Visitor;
            if (!isPlayed) sim.SetPersonData(VMPersonDataVariable.GreetStatus, Greeted);
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
            if (!isPlayed) sim.SetPersonData(VMPersonDataVariable.GreetStatus, Greeted);
            AptLog.Write("  " + sim.Name + " of " + Residency.NameOf(family) + ": person type " + was + " -> " + type + (isPlayed ? " (now played)" : " (now a neighbour)") + ".");
        }

        // ---- on and off stage ----

        /// <summary>
        /// A neighbour goes home, which is off the lot. What they have become while on it (skills, job, personality,
        /// needs) is kept for when they are next put on a lot.
        /// </summary>
        private static void GoOffStage(VM vm, LotState state, VMAvatar sim, FAMI family, string how)
        {
            var guid = GuidOf(sim);
            var name = sim.Name;
            Keep(state, sim, family);
            sim.Delete(true, vm.Context);
            state.Out.Remove(guid);
            AptLog.Write("  " + name + " of " + Residency.NameOf(family) + " " + how + ".");
        }

        /// <summary>Write a Sim's own data back to their record in the neighbourhood, and their needs to the pack's file.</summary>
        private static void Keep(LotState state, VMAvatar sim, FAMI family)
        {
            var needs = new short[16];
            for (int i = 0; i < needs.Length; i++) needs[i] = sim.GetMotiveData((VMMotive)i);
            state.File.Needs[GuidOf(sim)] = needs;

            var hood = Content.Get().Neighborhood;
            var id = hood.GetNeighborIDForGUID(GuidOf(sim));
            var record = (id == null) ? null : hood.GetNeighborByID(id.Value);
            if (record?.PersonData == null) return;
            //the record holds the first 88 values, and a Sim put on a lot takes exactly those from it
            var count = Math.Min(record.PersonData.Length, 88);
            for (int i = 0; i < count; i++) record.PersonData[i] = sim.GetPersonData((VMPersonDataVariable)i);
            //what they were in the middle of does not go with them
            Set(record.PersonData, VMPersonDataVariable.Posture, 0);
            Set(record.PersonData, VMPersonDataVariable.PersonType, 0);
            Set(record.PersonData, VMPersonDataVariable.GreetStatus, 0);
            Set(record.PersonData, VMPersonDataVariable.TS1FamilyNumber, (short)family.ChunkID);
        }

        private static void Set(short[] data, VMPersonDataVariable index, short value)
        {
            if ((int)index < data.Length) data[(int)index] = value;
        }

        /// <summary>Give a Sim who has just been put on the lot the needs they went home with.</summary>
        private static void RestoreNeeds(LotState state, VMAvatar sim)
        {
            if (state.File == null || !state.File.Needs.TryGetValue(GuidOf(sim), out var needs)) return;
            foreach (var need in Needs)
            {
                if ((int)need < needs.Length) sim.SetMotiveData(need, needs[(int)need]);
            }
        }

        /// <summary>Take a household's Sims off the lot for good (it has moved out).</summary>
        public static int RemoveFromLot(VM vm, FAMI family)
        {
            Lots.TryGetValue(vm, out var state);
            var count = 0;
            foreach (var pair in AvatarsByGuid(vm))
            {
                if (!family.FamilyGUIDs.Contains(pair.Key)) continue;
                if (state?.File != null) Keep(state, pair.Value, family);
                pair.Value.Delete(true, vm.Context);
                count++;
            }
            if (state != null)
            {
                foreach (var guid in family.FamilyGUIDs) state.Out.Remove(guid);
            }
            return count;
        }

        // ---- coming out ----

        private static void ScheduleOuting(VM vm, LotState state)
        {
            //on average, one neighbour comes out every three hours of the game's day, or every 45 minutes
            var every = (ComeOut == Outings.Often) ? 45 : 180;
            state.NextOuting = Now(vm) + every / 2 + Dice.Next(every);
        }

        /// <summary>The neighbours who are out of their apartments, by name.</summary>
        public static List<string> OutNow(VM vm, FAMI family = null)
        {
            if (!Lots.TryGetValue(vm, out var state)) return new List<string>();
            return state.Out.Values.Where(x => family == null || x.Family == family.ChunkID).Select(x => x.Name).ToList();
        }

        /// <summary>Bring a neighbour out now (one of this household, or anyone). Returns what happened, for the player.</summary>
        public static string BringOut(VM vm, FAMI family = null)
        {
            if (!Lots.TryGetValue(vm, out var state) || vm.TS1State?.CurrentFamily == null) return "Neighbours come out while you are playing a household here.";
            var present = AvatarsByGuid(vm);
            var choices = new List<Tuple<FAMI, uint>>();
            foreach (var other in Others(vm))
            {
                if (family != null && other.ChunkID != family.ChunkID) continue;
                foreach (var guid in other.FamilyGUIDs)
                {
                    if (!present.ContainsKey(guid)) choices.Add(Tuple.Create(other, guid));
                }
            }
            if (choices.Count == 0) return "Everyone who could come out already has.";
            var pick = choices[Dice.Next(choices.Count)];
            var sim = StartOuting(vm, state, pick.Item1, pick.Item2);
            return (sim == null) ? "They could not be brought out. The log says why." : (sim.Name + " of " + Residency.NameOf(pick.Item1) + " has come out.");
        }

        /// <summary>Send every neighbour who is out back home. Returns what happened, for the player.</summary>
        public static string SendHome(VM vm)
        {
            if (!Lots.TryGetValue(vm, out var state) || state.Out.Count == 0) return "No neighbours are out.";
            var now = Now(vm);
            foreach (var outing in state.Out.Values) outing.Ends = Math.Min(outing.Ends, now);
            return "The neighbours who are out are heading home.";
        }

        private static VMAvatar StartOuting(VM vm, LotState state, FAMI family, uint guid)
        {
            var sim = Spawn(vm, state, guid, family);
            if (sim == null) return null;
            var outing = new Outing() { Guid = guid, Family = family.ChunkID, Name = sim.Name };
            outing.Ends = Now(vm) + OutingShortest + Dice.Next(OutingLongest - OutingShortest + 1);
            state.Out[guid] = outing;
            Mark(vm, sim, family, false);

            var where = SharedSpot(vm, state);
            var walking = where != null && WalkTo(vm, sim, where.Value);
            AptLog.Write(sim.Name + " of " + Residency.NameOf(family) + " came out for " + (outing.Ends - Now(vm)) + " minutes"
                + (where == null ? ". There is nowhere shared to walk to." : (walking ? ", walking to floor " + where.Value.Level + ", tile " + where.Value.X + "," + where.Value.Y + "." : ". They could not be told where to walk, so they are left to themselves.")));
            return sim;
        }

        /// <summary>Somewhere a neighbour can go: a tile in an indoor room that is nobody's apartment, or else outdoors near the mailbox.</summary>
        private static Tile? SharedSpot(VM vm, LotState state)
        {
            var arch = vm.Context.Architecture;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                var tile = new Tile((sbyte)(1 + Dice.Next(Math.Max(1, arch.Stories))), (short)Dice.Next(arch.Width), (short)Dice.Next(arch.Height));
                var room = Units.IndoorRoomAt(vm, tile.ToPos());
                if (room == 0 || state.RoomOwners.ContainsKey(room)) continue;
                if (vm.Context.RoomInfo[room].Room.Area < 4) continue;
                return tile;
            }
            var mailbox = vm.Entities.FirstOrDefault(x => x.Object.OBJ.GUID == 0xEF121974 || x.Object.OBJ.GUID == 0x1D95C9B0);
            if (mailbox == null || mailbox.Position == LotTilePos.OUT_OF_WORLD) return null;
            return new Tile(mailbox.Position.Level, (short)(mailbox.Position.TileX - 2 + Dice.Next(5)), (short)(mailbox.Position.TileY - 2 + Dice.Next(5)));
        }

        private static Tile? HomeSpot(VM vm, LotState state, int family)
        {
            var unit = state.File.ApartmentOf(family)?.Tiles;
            if (unit == null) return null;
            foreach (var tile in unit)
            {
                if (Units.IndoorRoomAt(vm, tile.ToPos()) != 0) return tile;
            }
            return null;
        }

        /// <summary>
        /// Have a Sim walk to a tile, the way the player's "go here" does it: the game's own go-here marker is put on
        /// the tile, and the Sim is given its first interaction.
        /// </summary>
        private static bool WalkTo(VM vm, VMAvatar sim, Tile tile)
        {
            const uint GoHereMarker = 0x000007C4;
            try
            {
                var marker = vm.Context.CreateObjectInstance(GoHereMarker, tile.ToPos(), Direction.NORTH)?.Objects?.FirstOrDefault();
                if (marker == null) return false;
                if (marker.Position == LotTilePos.OUT_OF_WORLD)
                {
                    marker.Delete(true, vm.Context);
                    return false;
                }
                var menu = marker.GetPieMenu(vm, sim, true, false);
                if (menu == null || menu.Count == 0)
                {
                    marker.Delete(true, vm.Context);
                    return false;
                }
                marker.PushUserInteraction(menu[0].ID, sim, vm.Context, false, new short[] { menu[0].Param0, 0, 0, 0 });
                return true;
            }
            catch (Exception e)
            {
                AptLog.Write("  Could not send " + sim.Name + " walking: " + e.Message);
                return false;
            }
        }

        // ---- every few seconds ----

        private static string Describe(VM vm, VMAvatar sim, LotState state, int family)
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
                if (state.File != null && state.File.Apartments.Count > 0)
                {
                    if (!state.RoomOwners.TryGetValue(room, out var owner)) where += ", shared space";
                    else if (owner == family) where += ", their own apartment";
                    else where += ", THE APARTMENT OF " + Residency.NameOf(Residency.Residents(state.House).FirstOrDefault(x => x.ChunkID == owner));
                }
            }
            return doing + " [" + where + ", greeted " + sim.GetPersonData(VMPersonDataVariable.GreetStatus) + "]";
        }

        /// <summary>After every tick of a lot. Does nothing on a lot that is not shared, and little on one that is.</summary>
        public static void AfterTick(VM vm)
        {
            if (!Lots.TryGetValue(vm, out var state)) return;
            if (++state.Ticks % WatchEvery != 0) return;
            if (HomeHouse(vm) != state.House) return;

            var owners = Units.RoomOwners(vm, state.File);
            var changed = owners.Count != state.RoomOwners.Count || owners.Any(x => !state.RoomOwners.TryGetValue(x.Key, out var was) || was != x.Value);
            state.RoomOwners = owners;
            if (changed) SetDimRooms(vm, state);
            ReportLostRooms(vm, state);
            CollectRent(vm, state);
            LookAfterOutings(vm, state);
        }

        private static void LookAfterOutings(VM vm, LotState state)
        {
            if (vm.TS1State?.CurrentFamily == null) return; //the lot is being built: nobody is about
            var now = Now(vm);
            var present = AvatarsByGuid(vm);
            var others = Others(vm);

            foreach (var outing in state.Out.Values.ToList())
            {
                var family = others.FirstOrDefault(x => x.ChunkID == outing.Family);
                if (family == null || !present.TryGetValue(outing.Guid, out var sim))
                {
                    //the game took them off the lot itself (a visitor who decided to leave)
                    state.Out.Remove(outing.Guid);
                    AptLog.Write(outing.Name + " left the lot by themselves. Last seen: " + (outing.Doing ?? "(not seen)"));
                    continue;
                }
                TopUp(sim);
                if (sim.GetPersonData(VMPersonDataVariable.GreetStatus) != Greeted) sim.SetPersonData(VMPersonDataVariable.GreetStatus, Greeted);
                if (Watch)
                {
                    var doing = Describe(vm, sim, state, outing.Family);
                    if (doing != outing.Doing)
                    {
                        outing.Doing = doing;
                        AptLog.Write(sim.Name + ": " + doing);
                    }
                }

                if (now < outing.Ends) continue;
                var room = (sim.Position == LotTilePos.OUT_OF_WORLD) ? (ushort)0 : vm.Context.GetObjectRoom(sim);
                var atHome = state.RoomOwners.TryGetValue(room, out var owner) && owner == outing.Family;
                if (atHome)
                {
                    GoOffStage(vm, state, sim, family, "went home");
                }
                else if (outing.HomewardSince < 0)
                {
                    outing.HomewardSince = now;
                    var home = HomeSpot(vm, state, outing.Family);
                    if (home == null || !WalkTo(vm, sim, home.Value))
                    {
                        //no apartment to walk to (or no way to be told to): they are simply home
                        GoOffStage(vm, state, sim, family, (home == null) ? "went home (they have no apartment to walk to)" : "went home (they could not be told to walk there)");
                    }
                    else AptLog.Write(sim.Name + " is walking home.");
                }
                else if (now - outing.HomewardSince > WalkHomeLimit)
                {
                    GoOffStage(vm, state, sim, family, "went home (they had not got there after " + WalkHomeLimit + " minutes of walking)");
                }
            }

            //a neighbour who lives here but is on the lot without being out (the game let one in some other way) is left alone

            if (ComeOut == Outings.Never) return;
            if (state.NextOuting < 0) ScheduleOuting(vm, state);
            if (now < state.NextOuting) return;
            ScheduleOuting(vm, state);
            var hour = vm.Context.Clock.Hours;
            if (hour < FirstHour || hour >= LastHour || state.Out.Count >= MostOutAtOnce) return;
            Guarded("bringing a neighbour out", () => BringOut(vm));
        }

        private static void Guarded(string doing, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                AptLog.Write("Something went wrong while " + doing + ": " + e);
            }
        }

        /// <summary>A neighbour does not progress: while they are out their needs are held up, so nobody collapses in the hall.</summary>
        private static void TopUp(VMAvatar sim)
        {
            if (NeedsFloor == null) return;
            var floor = (short)NeedsFloor.Value;
            foreach (var need in Needs)
            {
                if (sim.GetMotiveData(need) < floor) sim.SetMotiveData(need, floor);
            }
        }

        // ---- dimmed apartments ----

        /// <summary>Work out which rooms are drawn dimmed, and light the lot again if that has changed.</summary>
        private static void SetDimRooms(VM vm, LotState state)
        {
            var dim = new HashSet<ushort>();
            var played = vm.TS1State?.CurrentFamily?.ChunkID ?? 0;
            var rooms = vm.Context.RoomInfo;
            if (Dim && rooms != null && played != 0) //(with nobody played the building is being laid out: all of it is shown)
            {
                foreach (var pair in state.RoomOwners)
                {
                    if (pair.Value == played || pair.Key >= rooms.Length) continue;
                    dim.Add(rooms[pair.Key].Room.LightBaseRoom);
                }
            }
            if (dim.SetEquals(state.DimRooms)) return;
            state.DimRooms = dim;
            Relight(vm);
        }

        /// <summary>The Dim setting changed: light the lot being played again.</summary>
        public static void DimChanged(VM vm)
        {
            if (vm != null && Lots.TryGetValue(vm, out var state)) SetDimRooms(vm, state);
        }

        private static void Relight(VM vm)
        {
            if (vm.Context?.RoomInfo == null) return;
            Guarded("lighting the lot again", () => vm.Context.RefreshAllLighting());
        }

        /// <summary>
        /// The engine has just worked out a room's light. If the room is part of an apartment that is not the played
        /// household's, turn it down: nobody is seen to be home.
        /// </summary>
        public static void AfterRoomLit(VMContext context, ushort room)
        {
            var vm = context?.VM;
            if (vm == null || !Lots.TryGetValue(vm, out var state) || state.DimRooms.Count == 0) return;
            var rooms = context.RoomInfo;
            if (rooms == null || room == 0 || room >= rooms.Length) return;
            var lit = rooms[room].Room.LightBaseRoom;
            if (!state.DimRooms.Contains(lit) || lit >= rooms.Length) return;
            var light = rooms[lit].Light;
            if (light == null) return;
            light.Lights.Clear();
            light.AmbientLight = Math.Min(light.AmbientLight, DimAmbient);
            light.OutsideLight = Math.Min(light.OutsideLight, DimOutside);
        }

        //how light a dimmed apartment is left (of 100): enough to see it is furnished, not enough to look lived in
        private const ushort DimAmbient = 10;
        private const ushort DimOutside = 30;

        /// <summary>Say so, once, when a wall coming down (or anything else) leaves part of an apartment marking no room.</summary>
        private static void ReportLostRooms(VM vm, LotState state)
        {
            if (state.File == null) return;
            foreach (var apartment in state.File.Apartments)
            {
                foreach (var tile in Units.LostTiles(vm, apartment))
                {
                    if (!state.ReportedLost.Add(tile)) continue;
                    AptLog.Write("Part of " + apartment.Name + " is no longer a room (floor " + tile.Level + ", tile " + tile.X + "," + tile.Y + "): it is open to the outdoors, or gone. It counts as shared until it is a room again.");
                }
            }
        }

        // ---- rent ----

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
    }
}
