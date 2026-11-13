/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Who may go into which apartment, and how they come to be let in.
    ///
    /// An apartment is its household's own. A Sim who does not live in it cannot walk into it: when the engine plans
    /// a walk from room to room, the doors into apartments that Sim may not enter are left out, so no route goes
    /// through them, and a walk to somewhere inside fails as it would for a room with no door. A Sim who is already
    /// inside is never trapped: the way out is always open.
    ///
    ///  - A vacant apartment is open to everyone, so it can be looked round and furnished.
    ///  - The played household's apartment is open to it, to its visitors and to the game's own characters (the maid,
    ///    the repairman, a burglar), but not to the neighbours.
    ///  - Another household's apartment is open only to that household, and to the played household for a while after
    ///    it has knocked and been asked in.
    /// </summary>
    public static class Doors
    {
        /// <summary>How long an invitation lasts, in game minutes.</summary>
        public const int Welcome = 180;
        /// <summary>The hours between which a knock is answered.</summary>
        public const int FirstHour = 8, LastHour = 22;
        private const int KnockGivesUp = 60;      //game minutes for the Sim to reach the door
        private const int NearEnough = 2 * 16;    //how near the door counts as at it (sixteenths of a tile)

        /// <summary>Apartments are kept private. Off, anyone walks anywhere, as before.</summary>
        public static bool Private = true;
        /// <summary>Visitors who are waiting to be greeted are shown to the played household's own door.</summary>
        public static bool ShowVisitors = true;

        /// <summary>A Sim on the way to knock at an apartment's door.</summary>
        public class Knock
        {
            public short Sim;         //the Sim's object number on the lot
            public int Apartment;
            public LotTilePos At;     //where they stand to knock
            public long GivesUp;
        }

        // ---- who may go where ----

        private static int FamilyOf(VMAvatar sim) => sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber);

        /// <summary>Whether a Sim may walk into an apartment they are not already in.</summary>
        public static bool MayEnter(int simFamily, int played, ICollection<int> residents, Apartment apartment, bool invited)
        {
            if (apartment.Vacant) return true;
            if (simFamily != 0 && simFamily == apartment.Tenant) return true;
            if (apartment.Tenant == played)
            {
                //the played household's: its guests and the game's own characters come and go. the neighbours do not
                return simFamily == 0 || !residents.Contains(simFamily);
            }
            return simFamily == played && invited;
        }

        /// <summary>The rooms a Sim may not walk into, right now (empty if they may go anywhere).</summary>
        public static HashSet<ushort> ClosedRooms(VM vm, VMAvatar sim)
        {
            var closed = new HashSet<ushort>();
            if (!Private) return closed;
            var state = Neighbours.StateOf(vm);
            var playedFamily = vm.TS1State?.CurrentFamily;
            if (state == null || playedFamily == null || state.RoomApts.Count == 0) return closed;

            var family = FamilyOf(sim);
            var residents = state.ResidentIds; //(kept by the lot's regular look: this runs for every walk a Sim plans)
            var now = Neighbours.Clock(vm);
            Apartment standingIn = null;
            if (sim.Position != LotTilePos.OUT_OF_WORLD) state.RoomApts.TryGetValue(vm.Context.GetRoomAt(sim.Position), out standingIn);

            foreach (var pair in state.RoomApts)
            {
                var apartment = pair.Value;
                if (apartment == standingIn) continue; //already in there: they can always move about it, and leave
                var invited = state.Invited.TryGetValue(apartment.Id, out var until) && until > now;
                if (!MayEnter(family, playedFamily.ChunkID, residents, apartment, invited)) closed.Add(pair.Key);
            }
            return closed;
        }

        /// <summary>
        /// The engine is about to plan a Sim's way from room to room. Add every door into a room that is closed to
        /// them to the doors the plan leaves out.
        /// </summary>
        public static void CloseDoors(VM vm, VMEntity walker, HashSet<VMRoomPortal> leftOut)
        {
            if (!(walker is VMAvatar sim) || leftOut == null) return;
            var closed = ClosedRooms(vm, sim);
            if (closed.Count == 0) return;
            var rooms = vm.Context.RoomInfo;
            for (int i = 0; i < rooms.Length; i++)
            {
                var portals = rooms[i].Portals;
                if (portals == null) continue;
                foreach (var portal in portals)
                {
                    if (closed.Contains(portal.TargetRoom)) leftOut.Add(portal);
                }
            }
        }

        /// <summary>
        /// The things a Sim might choose to do by themselves, without the ones in rooms that are closed to them: there
        /// is no use wanting a fridge they cannot reach. Null if nothing is closed to them.
        /// </summary>
        public static List<VMEntity> Reachable(VM vm, VMEntity chooser, List<VMEntity> all)
        {
            if (!(chooser is VMAvatar sim) || all == null) return null;
            var closed = ClosedRooms(vm, sim);
            if (closed.Count == 0) return null;
            var result = new List<VMEntity>(all.Count);
            foreach (var thing in all)
            {
                if (thing.Position != LotTilePos.OUT_OF_WORLD && closed.Contains(vm.Context.GetRoomAt(thing.Position))) continue;
                result.Add(thing);
            }
            return result;
        }

        // ---- an apartment's doors ----

        /// <summary>A way into an apartment: the tile just outside one of its doors, and the room that tile is in.</summary>
        public struct Doorstep
        {
            public LotTilePos Outside;
            public ushort OutsideRoom;
            public VMEntity Door;
        }

        /// <summary>The doors that lead from an apartment to somewhere that is not part of it.</summary>
        internal static List<Doorstep> DoorsOf(VM vm, Neighbours.LotState state, Apartment apartment)
        {
            var result = new List<Doorstep>();
            var rooms = vm.Context.RoomInfo;
            if (rooms == null) return result;
            foreach (var pair in state.RoomApts)
            {
                if (pair.Value != apartment || pair.Key >= rooms.Length || rooms[pair.Key].Portals == null) continue;
                foreach (var portal in rooms[pair.Key].Portals)
                {
                    if (state.RoomApts.TryGetValue(portal.TargetRoom, out var beyond) && beyond == apartment) continue; //a door inside the apartment
                    var inside = vm.GetObjectById(portal.ObjectID);
                    if (inside?.MultitileGroup == null) continue;
                    //a door stands on two tiles, one each side of the wall. the other one is the doorstep
                    var outside = inside.MultitileGroup.Objects.FirstOrDefault(x => x != inside && x.Position != LotTilePos.OUT_OF_WORLD && vm.Context.GetRoomAt(x.Position) == portal.TargetRoom);
                    if (outside == null) continue;
                    //one step further out, if that is still on the far side: clear of the door itself
                    var step = new LotTilePos((short)(outside.Position.x * 2 - inside.Position.x), (short)(outside.Position.y * 2 - inside.Position.y), outside.Position.Level);
                    var at = (vm.Context.GetRoomAt(step) == portal.TargetRoom) ? step : outside.Position;
                    result.Add(new Doorstep() { Outside = at, OutsideRoom = portal.TargetRoom, Door = inside });
                }
            }
            return result;
        }

        /// <summary>If an object is (part of) a door into an apartment, that apartment and where to stand outside it.</summary>
        public static Apartment ApartmentBehind(VM vm, VMEntity thing, out Doorstep doorstep)
        {
            doorstep = default;
            var state = Neighbours.StateOf(vm);
            if (state == null || thing?.MultitileGroup == null || !thing.Portal) return null;
            foreach (var apartment in state.File.Apartments)
            {
                foreach (var door in DoorsOf(vm, state, apartment))
                {
                    if (door.Door.MultitileGroup == thing.MultitileGroup)
                    {
                        doorstep = door;
                        return apartment;
                    }
                }
            }
            return null;
        }

        private static Tile TileOf(LotTilePos pos) => new Tile(pos.Level, pos.TileX, pos.TileY);

        // ---- knocking ----

        /// <summary>Whether a Sim of the played household could knock at this apartment (it is somebody else's).</summary>
        public static bool CanKnock(VM vm, VMEntity sim, Apartment apartment)
        {
            var played = vm.TS1State?.CurrentFamily;
            return played != null && sim is VMAvatar avatar && !apartment.Vacant && apartment.Tenant != played.ChunkID && FamilyOf(avatar) == played.ChunkID;
        }

        /// <summary>Send a Sim to knock at an apartment's door. What happens is told when they get there.</summary>
        public static void GoAndKnock(VM vm, VMAvatar sim, Apartment apartment, Doorstep doorstep)
        {
            var state = Neighbours.StateOf(vm);
            if (state == null) return;
            state.Knocks.RemoveAll(x => x.Sim == sim.ObjectID);
            state.Knocks.Add(new Knock() { Sim = sim.ObjectID, Apartment = apartment.Id, At = doorstep.Outside, GivesUp = Neighbours.Clock(vm) + KnockGivesUp });
            var walking = Neighbours.Walk(vm, sim, TileOf(doorstep.Outside));
            AptLog.Write(sim.Name + " is going to knock at " + apartment.Name + (walking ? "." : " (they could not be told to walk there; they knock if they get near the door)."));
        }

        /// <summary>Called with what to tell the player when a knock is answered, or not.</summary>
        public static Action<VM, string, string> OnAnswer;

        /// <summary>A Sim has reached a door and knocks.</summary>
        private static void Answer(VM vm, Neighbours.LotState state, VMAvatar sim, Apartment apartment)
        {
            var tenant = Residency.Residents(state.House).FirstOrDefault(x => x.ChunkID == apartment.Tenant);
            if (tenant == null)
            {
                OnAnswer?.Invoke(vm, apartment.Name, "Nobody lives here.");
                return;
            }
            var name = Residency.NameOf(tenant);
            var hour = vm.Context.Clock.Hours;
            if (hour < FirstHour || hour >= LastHour)
            {
                AptLog.Write(sim.Name + " knocked at " + apartment.Name + " at " + hour + " o'clock. Nobody answered.");
                OnAnswer?.Invoke(vm, apartment.Name, sim.Name + " knocks. Nobody answers: it is too late, or too early. Try between " + FirstHour + " in the morning and " + (LastHour - 12) + " at night.");
                return;
            }
            var until = Neighbours.Clock(vm) + Welcome;
            state.Invited[apartment.Id] = until;
            var host = Neighbours.Host(vm, state, tenant, until);
            Neighbours.RefreshDim(vm, state);
            AptLog.Write(sim.Name + " knocked at " + apartment.Name + ". " + (host == null ? "Nobody could come to the door, but they are" : host.Name + " answered, and they are") + " welcome for " + Welcome + " minutes.");
            OnAnswer?.Invoke(vm, apartment.Name, sim.Name + " knocks, and " + (host == null ? name : host.Name + " of " + name) + " asks your household in. You are welcome for the next " + (Welcome / 60) + " hours.");
        }

        // ---- every few seconds ----

        internal static void Look(VM vm, Neighbours.LotState state)
        {
            var played = vm.TS1State?.CurrentFamily;
            if (played == null) return;
            var now = Neighbours.Clock(vm);

            //invitations that have run out: the apartment is dark again (anyone still inside can walk out, not back in)
            var over = state.Invited.Where(x => x.Value <= now).Select(x => x.Key).ToList();
            foreach (var id in over) state.Invited.Remove(id);
            if (over.Count > 0)
            {
                AptLog.Write("The welcome at " + string.Join(", ", over.Select(x => state.File.Find(x)?.Name ?? ("apartment " + x))) + " is over.");
                Neighbours.RefreshDim(vm, state);
            }

            foreach (var knock in state.Knocks.ToList())
            {
                var sim = vm.GetObjectById(knock.Sim) as VMAvatar;
                var apartment = state.File.Find(knock.Apartment);
                if (sim == null || sim.Dead || apartment == null || now > knock.GivesUp)
                {
                    state.Knocks.Remove(knock);
                    if (sim != null && apartment != null) AptLog.Write(sim.Name + " never got to the door of " + apartment.Name + ".");
                    continue;
                }
                if (sim.Position.Level != knock.At.Level || Math.Abs(sim.Position.x - knock.At.x) > NearEnough || Math.Abs(sim.Position.y - knock.At.y) > NearEnough) continue;
                state.Knocks.Remove(knock);
                Answer(vm, state, sim, apartment);
            }

            if (ShowVisitors) ShowVisitorsToDoor(vm, state, played);
        }

        /// <summary>
        /// The game sends a visitor to the front of the lot to wait to be greeted. In a building that is the street
        /// door, not the household's own. A visitor who is waiting there with nothing to do is walked to the played
        /// household's door instead, once, and waits there.
        /// </summary>
        private static void ShowVisitorsToDoor(VM vm, Neighbours.LotState state, FAMI played)
        {
            var home = state.File.ApartmentOf(played.ChunkID);
            if (home == null) return;
            List<Doorstep> doors = null;
            var residents = state.ResidentIds;
            foreach (var entity in vm.Entities)
            {
                if (!(entity is VMAvatar sim) || sim.Dead || sim.IsPet || sim.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (state.ShownToDoor.Contains(sim.ObjectID)) continue;
                if (sim.GetPersonData(VMPersonDataVariable.PersonType) != 1 || residents.Contains(FamilyOf(sim))) continue; //a visitor, and not one of the neighbours
                if (sim.GetPersonData(VMPersonDataVariable.GreetStatus) != 0) continue;                                    //waiting to be greeted
                if (sim.Thread?.ActiveAction != null || (sim.Thread?.Queue?.Count ?? 0) > 0) continue;                      //with nothing to do
                if (state.RoomApts.ContainsKey(vm.Context.GetRoomAt(sim.Position))) continue;                              //and not already inside an apartment

                doors = doors ?? DoorsOf(vm, state, home);
                if (doors.Count == 0) return;
                var nearest = doors.OrderBy(x => Math.Abs(x.Outside.x - sim.Position.x) + Math.Abs(x.Outside.y - sim.Position.y) + Math.Abs(x.Outside.Level - sim.Position.Level) * 1000).First();
                state.ShownToDoor.Add(sim.ObjectID);
                var walking = Neighbours.Walk(vm, sim, TileOf(nearest.Outside));
                AptLog.Write("Visitor " + sim.Name + " was waiting at the front of the lot" + (walking ? ", and is walking to the door of " + home.Name + "." : ". They could not be told to walk to the door of " + home.Name + "."));
            }
        }
    }
}
