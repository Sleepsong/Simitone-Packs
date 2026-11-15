/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
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
            public ushort Room;       //the room that spot is in: being near it through a wall does not count
            public long GivesUp;
        }

        // ---- who may go where ----

        private static int FamilyOf(VMAvatar sim) => sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber);

        /// <summary>Whether a Sim may walk into an apartment they are not already in.</summary>
        /// <param name="gameCharacter">One of the game's own characters, there to do a job: the firefighter, the police, the repairman, the maid.</param>
        /// <param name="letIn">For a visitor of the played household: whether they have been greeted. One who has not waits outside.</param>
        public static bool MayEnter(int simFamily, int played, ICollection<int> residents, Apartment apartment, bool invited, bool gameCharacter = false, bool letIn = true)
        {
            if (apartment.Vacant) return true;
            if (gameCharacter) return true; //a fire next door has to be put out, whoever is playing
            if (simFamily != 0 && simFamily == apartment.Tenant) return true;
            if (apartment.Tenant == played)
            {
                //the played household's: its guests come in once they have been greeted. the neighbours do not
                return (simFamily == 0 || !residents.Contains(simFamily)) && letIn;
            }
            return simFamily == played && invited;
        }

        private static readonly HashSet<ushort> Nothing = new HashSet<ushort>();

        /// <summary>The rooms a Sim may not walk into, right now (empty if they may go anywhere). Do not change what it returns.</summary>
        public static HashSet<ushort> ClosedRooms(VM vm, VMAvatar sim)
        {
            if (!Private) return Nothing;
            var state = Neighbours.StateOf(vm);
            var playedFamily = vm.TS1State?.CurrentFamily;
            if (state == null || playedFamily == null) return Nothing;
            Neighbours.FreshRooms(vm, state);
            if (state.RoomApts.Count == 0) return Nothing;

            //a Sim plans many walks in a tick (and one that cannot get somewhere may try again and again): work it out once
            var here = (sim.Position == LotTilePos.OUT_OF_WORLD) ? (ushort)0 : vm.Context.GetRoomAt(sim.Position);
            var tick = Neighbours.TickOf(state);
            if (state.ClosedTick != tick)
            {
                state.ClosedTick = tick;
                state.ClosedNow.Clear();
            }
            var key = ((long)sim.ObjectID << 16) | here;
            if (state.ClosedNow.TryGetValue(key, out var known)) return known;

            var closed = new HashSet<ushort>();
            var family = FamilyOf(sim);
            var gameCharacter = sim.GetPersonData(VMPersonDataVariable.PersonType) > 1;
            var letIn = sim.GetPersonData(VMPersonDataVariable.GreetStatus) > 0;
            var residents = state.ResidentIds; //(kept by the lot's regular look)
            var now = Neighbours.Clock(vm);

            //where they are is never closed to them, so nobody is trapped: the apartment they stand in; and, from a room
            //that is in no apartment but opens only into apartments (a cupboard, a room cut off by a new wall), those
            var open = new HashSet<Apartment>();
            if (state.RoomApts.TryGetValue(here, out var standingIn)) open.Add(standingIn);
            else
            {
                var rooms = vm.Context.RoomInfo;
                var ways = (rooms != null && here != 0 && here < rooms.Length) ? rooms[here].Portals : null;
                if (ways != null && ways.Count > 0 && ways.All(x => state.RoomApts.ContainsKey(x.TargetRoom)))
                {
                    foreach (var way in ways) open.Add(state.RoomApts[way.TargetRoom]);
                }
            }

            foreach (var pair in state.RoomApts)
            {
                var apartment = pair.Value;
                if (open.Contains(apartment)) continue;
                var invited = state.Invited.TryGetValue(apartment.Id, out var until) && until > now;
                if (!MayEnter(family, playedFamily.ChunkID, residents, apartment, invited, gameCharacter, letIn)) closed.Add(pair.Key);
            }
            state.ClosedNow[key] = closed;
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
            Once(vm, 1, sim.ObjectID, () => sim.Name + " plans a walk with " + closed.Count + " room(s) closed to them: " + string.Join(", ", closed.OrderBy(x => x)) + ".");
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
            Once(vm, 2, sim.ObjectID, () => sim.Name + " chooses what to do with " + closed.Count + " room(s) closed to them.");
            var result = new List<VMEntity>(all.Count);
            foreach (var thing in all)
            {
                if (thing.Position != LotTilePos.OUT_OF_WORLD && closed.Contains(vm.Context.GetRoomAt(thing.Position))) continue;
                result.Add(thing);
            }
            return result;
        }

        private static System.Runtime.CompilerServices.ConditionalWeakTable<VM, HashSet<long>> Said = new System.Runtime.CompilerServices.ConditionalWeakTable<VM, HashSet<long>>();

        /// <summary>
        /// Write something to the log the first time it happens on a lot (for this kind of thing and this object), so
        /// the log shows what the pack had begun doing. Costs a lookup after the first time: the line is only made then.
        /// </summary>
        public static void Once(VM vm, int kind, int id, Func<string> line)
        {
            if (!Said.GetValue(vm, x => new HashSet<long>()).Add(((long)kind << 32) | (uint)id)) return;
            try { AptLog.Write("(first) " + line()); } catch { }
        }

        // ---- an apartment's doors ----

        /// <summary>A way into an apartment: the tile just outside one of its doors, and the room that tile is in.</summary>
        public struct Doorstep
        {
            public LotTilePos Outside;
            public ushort OutsideRoom;
            /// <summary>The part of the door on the apartment's side.</summary>
            public VMEntity Door;
            /// <summary>The part of the door on the other side (the hall, or wherever the door leads).</summary>
            public VMEntity Hall;
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
                    result.Add(new Doorstep() { Outside = at, OutsideRoom = portal.TargetRoom, Door = inside, Hall = outside });
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

        private static int GuidHash(VMAvatar sim) => (int)(sim.Object.OBJ.GUID & 0x7FFFFFFF);

        /// <summary>The apartment a thing stands in, if it is one the Sim may not walk into. Null if they may, or it is in none.</summary>
        public static Apartment ClosedApartmentOf(VM vm, VMEntity sim, VMEntity thing)
        {
            if (!(sim is VMAvatar avatar) || thing == null || thing.Position == LotTilePos.OUT_OF_WORLD) return null;
            var state = Neighbours.StateOf(vm);
            if (state == null) return null;
            var room = vm.Context.GetRoomAt(thing.Position);
            if (!ClosedRooms(vm, avatar).Contains(room)) return null;
            return state.RoomApts.TryGetValue(room, out var apartment) ? apartment : null;
        }

        /// <summary>The nearest of an apartment's doors to a Sim (null if it has none).</summary>
        public static Doorstep? NearestDoor(VM vm, VMAvatar sim, Apartment apartment)
        {
            var state = Neighbours.StateOf(vm);
            if (state == null) return null;
            var doors = DoorsOf(vm, state, apartment);
            if (doors.Count == 0) return null;
            return doors.OrderBy(x => Math.Abs(x.Outside.x - sim.Position.x) + Math.Abs(x.Outside.y - sim.Position.y) + Math.Abs(x.Outside.Level - sim.Position.Level) * 1000).First();
        }

        // ---- visitors at the door ----

        /// <summary>Whether a thing is (part of) a door. Never throws: some objects have no table of entry points.</summary>
        public static bool IsDoor(VMEntity thing)
        {
            try { return thing?.EntryPoints != null && thing.EntryPoints.Length > 15 && thing.Portal; }
            catch { return false; }
        }

        /// <summary>
        /// The game is giving a Sim something to do. If it is a visitor, and it is something to do at a door (ringing the
        /// bell, waiting to be let in), and the door is not the played household's own, it is moved to their own door:
        /// in a building the game's "front door" is the street door. The action is changed where it lies, so whatever
        /// gave it keeps hold of the same one. Returns what was done, for the log, or null if nothing was.
        /// </summary>
        public static string SendToOwnDoor(VM vm, VMAvatar sim, VMQueuedAction action)
        {
            if (!ShowVisitors || action?.Callee == null || action.Callee == sim) return null;
            var door = action.Callee;
            if (!IsDoor(door)) return null;
            var state = Neighbours.StateOf(vm);
            var played = vm.TS1State?.CurrentFamily;
            if (state?.File == null || played == null) return null;
            var family = FamilyOf(sim);
            if (family != 0 && state.ResidentIds.Contains(family)) return null; //people who live here use any door they like
            var home = state.File.ApartmentOf(played.ChunkID);
            if (home == null) return null;
            Neighbours.FreshRooms(vm, state);

            //a door of the played household's apartment, or one inside it, is the right one already
            foreach (var part in door.MultitileGroup?.Objects ?? new List<VMEntity>() { door })
            {
                if (part.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (state.RoomApts.TryGetValue(vm.Context.GetRoomAt(part.Position), out var inApt) && inApt == home) return null;
            }
            var doors = DoorsOf(vm, state, home);
            if (doors.Count == 0) return null;
            var nearest = doors.OrderBy(x => Math.Abs(x.Outside.x - sim.Position.x) + Math.Abs(x.Outside.y - sim.Position.y) + Math.Abs(x.Outside.Level - sim.Position.Level) * 1000).First();

            var fresh = SameThingAt(vm, sim, action, door, nearest);
            if (fresh == null) return "Visitor " + sim.Name + " was sent to " + Describe(door) + " for \"" + action.Name + "\", but the door of " + home.Name + " has nothing of that name, so they were left to go there.";

            var oldCallee = action.Callee;
            var ownIcon = action.IconOwner == oldCallee;
            action.Callee = fresh.Callee;
            action.StackObject = fresh.StackObject;
            action.CodeOwner = fresh.CodeOwner;
            action.ActionRoutine = fresh.ActionRoutine;
            action.CheckRoutine = fresh.CheckRoutine;
            action.InteractionNumber = fresh.InteractionNumber;
            if (!string.IsNullOrEmpty(fresh.Name)) action.Name = fresh.Name;
            if (ownIcon) action.IconOwner = fresh.Callee;
            return "Visitor " + sim.Name + " was sent to " + Describe(oldCallee) + " for \"" + action.Name + "\". Sent to the door of " + home.Name + " instead.";
        }

        /// <summary>
        /// The engine is about to plan a Sim's walk to a thing: to one of its routing slots, or to a spot next to it.
        /// That is how the game walks a visitor to its front door, which in a building is the street door; it does not
        /// give them anything to do at the door. So if a visitor (anyone who does not live here) is walking to a door that
        /// is not the played household's, the walk is planned to the household's own door instead: to the spots just
        /// outside it. What the visitor then does there (ringing, waiting) is the game's own, as it would have been.
        ///
        /// Not when the script doing the walking is the door's own: that is how a Sim walks through a door.
        /// Returns true if the walk was planned here, with whether a way there was found.
        /// </summary>
        public static bool WalkToOwnDoor(VMRoutingFrame frame, VMEntity target, out bool planned)
        {
            planned = false;
            if (!ShowVisitors || target == null || !(frame.Caller is VMAvatar sim) || sim.IsPet) return false;
            if (target is VMAvatar host) return WaitForHost(frame, sim, host, out planned);
            if (!IsDoor(target)) return false;
            var group = target.MultitileGroup?.Objects;
            if (frame.Callee != null && (frame.Callee == target || (group != null && group.Contains(frame.Callee)))) return false; //going through it
            var vm = frame.VM;
            var state = Neighbours.StateOf(vm);
            var played = vm?.TS1State?.CurrentFamily;
            if (state?.File == null || played == null) return false;
            var family = FamilyOf(sim);
            if (family != 0 && state.ResidentIds.Contains(family)) return false;
            var home = state.File.ApartmentOf(played.ChunkID);
            if (home == null) return false;
            Neighbours.FreshRooms(vm, state);
            foreach (var part in group ?? new List<VMEntity>() { target })
            {
                if (part.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (state.RoomApts.TryGetValue(vm.Context.GetRoomAt(part.Position), out var inApt) && inApt == home) return false; //their door already
            }
            var doors = DoorsOf(vm, state, home);
            if (doors.Count == 0) return false;
            var nearest = doors.OrderBy(x => Math.Abs(x.Outside.x - sim.Position.x) + Math.Abs(x.Outside.y - sim.Position.y) + Math.Abs(x.Outside.Level - sim.Position.Level) * 1000).First();
            var spots = SpotsOutside(vm, nearest, sim);
            if (spots.Count == 0) return false;
            planned = frame.InitRoutes(spots);
            AptLog.Write("Visitor " + sim.Name + " was walking to " + Describe(target) + ". Sent to the door of " + home.Name + " instead" + (planned ? "." : ", but no way there was found."));
            return true;
        }

        /// <summary>
        /// A visitor who has not been let in is walking to one of the played household who is at home. In a house the
        /// game has a visitor come to the Sim who greets them (the pizza delivery comes to whoever pays), which here would
        /// take them into the apartment. They walk to the apartment's door instead and wait there; the household's Sim
        /// is walking out to them (WalkOutToMeet), or is sent out now.
        ///
        /// Only for a visitor someone of the household has gone to meet, or is dealing with: anyone else who walks to a
        /// Sim (the social worker, say) is left alone.
        /// </summary>
        private static bool WaitForHost(VMRoutingFrame frame, VMAvatar visitor, VMAvatar host, out bool planned)
        {
            planned = false;
            var vm = frame.VM;
            var state = Neighbours.StateOf(vm);
            var played = vm?.TS1State?.CurrentFamily;
            if (state?.File == null || played == null || FamilyOf(host) != played.ChunkID || visitor == host) return false;
            var theirs = FamilyOf(visitor);
            if (theirs != 0 && state.ResidentIds.Contains(theirs)) return false;
            if (IsLetIn(visitor)) return false;
            var home = state.File.ApartmentOf(played.ChunkID);
            if (home == null || host.Position == LotTilePos.OUT_OF_WORLD || visitor.Position == LotTilePos.OUT_OF_WORLD) return false;
            Neighbours.FreshRooms(vm, state);
            if (!state.RoomApts.TryGetValue(vm.Context.GetRoomAt(host.Position), out var hostIn) || hostIn != home) return false; //the host is out: meet them there
            if (state.RoomApts.TryGetValue(vm.Context.GetRoomAt(visitor.Position), out var visitorIn) && visitorIn == home) return false;
            var now = Neighbours.Clock(vm);
            var meeting = (state.WalkedOut.TryGetValue(visitor.ObjectID, out var until) && until > now) || IsDealingWith(host, visitor);
            if (!meeting) return false;

            var doors = DoorsOf(vm, state, home);
            if (doors.Count == 0) return false;
            var door = doors.OrderBy(x => Math.Abs(x.Outside.x - visitor.Position.x) + Math.Abs(x.Outside.y - visitor.Position.y) + Math.Abs(x.Outside.Level - visitor.Position.Level) * 1000).First();
            var spots = SpotsOutside(vm, door, visitor);
            if (spots.Count == 0) return false;
            planned = frame.InitRoutes(spots);
            Once(vm, 8, visitor.ObjectID, () => visitor.Name + " was coming to " + host.Name + ", who is still at home: they wait at the door of " + home.Name + " instead.");
            //and the host comes out, if nobody has sent them yet
            if (!state.WalkedOut.ContainsKey(visitor.ObjectID) || state.WalkedOut[visitor.ObjectID] <= now)
            {
                var sent = SendOut(vm, state, host, visitor, door);
                if (sent != null) AptLog.Write(sent);
            }
            return true;
        }

        /// <summary>Whether a visitor has been let in: greeted, for a guest; for one of the game's own characters, not waiting at the door.</summary>
        public static bool IsLetIn(VMAvatar visitor)
        {
            var greeted = visitor.GetPersonData(VMPersonDataVariable.GreetStatus);
            return (visitor.GetPersonData(VMPersonDataVariable.PersonType) > 1) ? greeted != 0 : greeted > 0;
        }

        /// <summary>Whether one Sim has anything to do with another in their queue (greeting them, paying them).</summary>
        private static bool IsDealingWith(VMAvatar sim, VMAvatar other)
        {
            var queue = sim.Thread?.Queue;
            if (queue == null) return false;
            foreach (var action in queue) if (action?.Callee == other || action?.StackObject == other) return true;
            return false;
        }

        /// <summary>Send one of the household out of the apartment to a spot beside a visitor at one of its doors.</summary>
        private static string SendOut(VM vm, Neighbours.LotState state, VMAvatar sim, VMAvatar visitor, Doorstep door)
        {
            state.WalkedOut[visitor.ObjectID] = Neighbours.Clock(vm) + 30;
            var mine = new Tile(visitor.Position.Level, visitor.Position.TileX, visitor.Position.TileY);
            var spot = SpotsOutside(vm, door)
                .Select(x => new Tile(x.Position.Level, x.Position.TileX, x.Position.TileY))
                .Where(x => !x.Equals(mine))
                .OrderBy(x => Math.Abs(x.X - mine.X) + Math.Abs(x.Y - mine.Y))
                .Cast<Tile?>().FirstOrDefault();
            if (spot == null) return null;
            var walking = Neighbours.Walk(vm, sim, spot.Value);
            return sim.Name + (walking ? " walks out to meet " : " could not be sent out to meet ") + visitor.Name + " at the door.";
        }

        /// <summary>Where to stand at a door, from the hall: the spot just outside it, the ones around that, and the doorway itself last.</summary>
        public static List<VMFindLocationResult> SpotsOutside(VM vm, Doorstep door, VMAvatar walker = null)
        {
            var result = new List<VMFindLocationResult>();
            var seen = new HashSet<Tile>();
            //standing there facing the door, as someone ringing does
            var face = (door.Hall ?? door.Door)?.Position ?? door.Outside;
            void Add(LotTilePos pos, double score)
            {
                var tile = new Tile(pos.Level, pos.TileX, pos.TileY);
                if (!seen.Add(tile)) return;
                var centre = tile.ToPos();
                if (vm.Context.GetRoomAt(centre) != door.OutsideRoom) return; //the hall side only
                var facing = (float)Math.Atan2(face.x - centre.x, -(face.y - centre.y));
                var faceAnywhere = face.x == centre.x && face.y == centre.y;
                result.Add(new VMFindLocationResult() { Position = centre, RadianDirection = facing, FaceAnywhere = faceAnywhere, Score = score });
            }
            Add(door.Outside, 3);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) Add(new LotTilePos((short)(door.Outside.x + dx * 16), (short)(door.Outside.y + dy * 16), door.Outside.Level), 2);
            if (door.Hall != null && door.Hall.Position != LotTilePos.OUT_OF_WORLD) Add(door.Hall.Position, 1);
            if (walker == null) return result;

            //spots someone else stands on, or is on the way to, go last: two visitors sent to one spot made one of them move
            var state = Neighbours.StateOf(vm);
            var now = Neighbours.Clock(vm);
            var taken = new HashSet<Tile>();
            foreach (var other in vm.Context.ObjectQueries.Avatars)
            {
                if (other == walker || other.Dead || other.Position == LotTilePos.OUT_OF_WORLD) continue;
                taken.Add(new Tile(other.Position.Level, other.Position.TileX, other.Position.TileY));
            }
            if (state != null)
            {
                foreach (var pair in state.SpotsTaken)
                    if (pair.Value.Key != walker.ObjectID && pair.Value.Value > now) taken.Add(pair.Key);
            }
            Func<VMFindLocationResult, Tile> tileOf = x => new Tile(x.Position.Level, x.Position.TileX, x.Position.TileY);
            result = result.OrderBy(x => taken.Contains(tileOf(x)) ? 1 : 0).ToList();
            if (state != null && result.Count > 0) state.SpotsTaken[tileOf(result[0])] = new KeyValuePair<short, long>(walker.ObjectID, now + 30);
            return result;
        }

        /// <summary>
        /// One of the played household is being given something to do with a visitor who is waiting at their door, not
        /// yet greeted (greeting them, as a rule). The visitor may not come in until they are; so the Sim walks out to
        /// them first, to a spot beside them outside the door, and then does it. In a house the game has the visitor
        /// come to the Sim instead, which here would be into the apartment.
        /// </summary>
        public static string WalkOutToMeet(VM vm, VMAvatar sim, VMQueuedAction action)
        {
            if (!(action?.Callee is VMAvatar visitor) || visitor == sim || visitor.IsPet) return null;
            var state = Neighbours.StateOf(vm);
            var played = vm.TS1State?.CurrentFamily;
            if (state?.File == null || played == null || FamilyOf(sim) != played.ChunkID) return null;
            var theirs = FamilyOf(visitor);
            if (theirs != 0 && state.ResidentIds.Contains(theirs)) return null;
            if (IsLetIn(visitor)) return null;
            var home = state.File.ApartmentOf(played.ChunkID);
            if (home == null || sim.Position == LotTilePos.OUT_OF_WORLD || visitor.Position == LotTilePos.OUT_OF_WORLD) return null;
            Neighbours.FreshRooms(vm, state);
            if (!state.RoomApts.TryGetValue(vm.Context.GetRoomAt(sim.Position), out var simIn) || simIn != home) return null; //the Sim is not at home
            if (state.RoomApts.TryGetValue(vm.Context.GetRoomAt(visitor.Position), out var visitorIn) && visitorIn == home) return null; //the visitor is in already

            //once for each visitor in a while: several things done with them in a row need one walk
            var now = Neighbours.Clock(vm);
            if (state.WalkedOut.TryGetValue(visitor.ObjectID, out var until) && until > now) return null;
            var doors = DoorsOf(vm, state, home);
            if (doors.Count == 0) return null;
            //the door they are at, or the one they will come to
            var door = doors.OrderBy(x => Math.Abs(x.Outside.x - visitor.Position.x) + Math.Abs(x.Outside.y - visitor.Position.y) + Math.Abs(x.Outside.Level - visitor.Position.Level) * 1000).First();
            var sent = SendOut(vm, state, sim, visitor, door);
            return (sent == null) ? null : sent + " (for \"" + action.Name + "\")";
        }

        /// <summary>Write down, once for each visitor and each thing, where the game walks an arriving visitor to.</summary>
        public static void WatchWalk(VMRoutingFrame frame, VMEntity target)
        {
            if (!(frame.Caller is VMAvatar sim) || sim.IsPet || target == null) return;
            var vm = frame.VM;
            var state = Neighbours.StateOf(vm);
            if (state == null) return;
            var family = FamilyOf(sim);
            if (family != 0 && state.ResidentIds.Contains(family)) return;
            if (sim.GetPersonData(VMPersonDataVariable.GreetStatus) > 0 && !IsDoor(target)) return;
            var id = (int)((sim.Object.OBJ.GUID * 31) ^ (target.Object?.OBJ?.GUID ?? 0));
            Once(vm, 7, id, () => "[visitor] " + sim.Name + " (greeted " + sim.GetPersonData(VMPersonDataVariable.GreetStatus) + ") walks to " + (IsDoor(target) ? Describe(target) : target.ToString())
                + ", from the script \"" + (frame.Routine?.Rti?.Name ?? "?") + "\" of " + (frame.Callee?.ToString() ?? "?") + ".");
        }

        /// <summary>
        /// Write down, once for each visitor and each thing, what the game gives a visitor to do on a building, and to
        /// what. It is how the game's way of sending visitors to the door can be seen.
        /// </summary>
        public static void Watch(VM vm, VMAvatar sim, VMQueuedAction action)
        {
            var state = Neighbours.StateOf(vm);
            if (state == null || sim.IsPet) return;
            var family = FamilyOf(sim);
            if (family != 0 && state.ResidentIds.Contains(family)) return;
            //arriving (not yet greeted), and anything at a door: how they come to the door is what is being looked for
            if (sim.GetPersonData(VMPersonDataVariable.GreetStatus) > 0 && !IsDoor(action.Callee)) return;
            var name = action.Name ?? "";
            var id = (int)((sim.Object.OBJ.GUID * 31) ^ (uint)name.GetHashCode());
            Once(vm, 6, id, () => "[visitor] " + sim.Name + " (person type " + sim.GetPersonData(VMPersonDataVariable.PersonType) + ", greeted " + sim.GetPersonData(VMPersonDataVariable.GreetStatus)
                + ") was given \"" + name + "\" on " + (action.Callee == null ? "nothing" : action.Callee == sim ? "themselves" : (IsDoor(action.Callee) ? Describe(action.Callee) : action.Callee.ToString())) + ".");
        }

        private static string Describe(VMEntity thing)
        {
            try { return "the door at floor " + thing.Position.Level + ", tile " + thing.Position.TileX + "," + thing.Position.TileY + " (" + thing.ToString() + ")"; }
            catch { return "a door"; }
        }

        /// <summary>The same interaction as an action has, but on another door: the same entry if it is the same kind of door, else the one with the same name.</summary>
        private static VMQueuedAction SameThingAt(VM vm, VMAvatar sim, VMQueuedAction action, VMEntity from, Doorstep to)
        {
            var number = action.InteractionNumber;
            if (number < 0 && (number & 0x7FFFFFFF) == 0x7FFFFFFF) return null; //not an interaction from a menu table at all
            var global = (number & unchecked((int)0x80000000)) != 0;
            var index = number & 0x7FFFFFFF;
            //the side of the door the visitor stands at first, then the rest of it
            var parts = new List<VMEntity>();
            if (to.Hall != null) parts.Add(to.Hall);
            foreach (var part in to.Door.MultitileGroup?.Objects ?? new List<VMEntity>() { to.Door }) if (!parts.Contains(part)) parts.Add(part);

            foreach (var part in parts)
            {
                if (global || part.Object?.OBJ?.GUID == from.Object?.OBJ?.GUID)
                {
                    var same = part.GetAction(index, sim, vm.Context, global, action.Args);
                    if (same != null) return same;
                    continue;
                }
                var table = part.TreeTable?.InteractionByIndex;
                var names = part.TreeTableStrings;
                if (table == null || names == null || string.IsNullOrWhiteSpace(action.Name)) continue;
                foreach (var entry in table)
                {
                    string name;
                    try { name = names.GetString((int)entry.Value.TTAIndex); } catch { continue; }
                    if (!string.Equals((name ?? "").Trim(), action.Name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    var named = part.GetAction((int)entry.Key, sim, vm.Context, false, action.Args);
                    if (named != null) return named;
                }
            }
            return null;
        }

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
            state.Knocks.Add(new Knock() { Sim = sim.ObjectID, Apartment = apartment.Id, At = doorstep.Outside, Room = doorstep.OutsideRoom, GivesUp = Neighbours.Clock(vm) + KnockGivesUp });
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
                if (vm.Context.GetRoomAt(sim.Position) != vm.Context.GetRoomAt(knock.At)) continue; //near, but the other side of a wall
                state.Knocks.Remove(knock);
                Answer(vm, state, sim, apartment);
            }

            //(visitors are no longer walked to the door afterwards: their walk there is planned to it in the first place,
            //and a second walk given to a visitor waiting to be greeted cut their visit short)
        }

    }
}
