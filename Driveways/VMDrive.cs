/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Driveways
{
    /// <summary>
    /// The pack's one SimAntics primitive. The car and the driveway piece have scripts of an instruction or two that
    /// call it; what it does is chosen by the first byte of its operand (see Shared).
    ///
    /// A drive changes nothing the game saves. The car stays where it is parked as far as the game's rules go (it
    /// still takes up its tiles), and the Sim stays standing beside it. Only what is drawn moves: the car's picture
    /// is taken along the route and then hidden, and the Sim's is hidden for the whole trip. So a lot saved in the
    /// middle of a drive loads with the car parked and the Sim next to it.
    /// </summary>
    public class VMDrive : VMPrimitiveHandler
    {
        /// <summary>How long the car is away, in ticks (30 to the second at normal speed).</summary>
        public const int AwayTicks = 600;
        /// <summary>The Sim's Fun goes up by one every this many ticks while away.</summary>
        public const int FunEvery = 12;
        /// <summary>A trip whose Sim has stopped running it for this many ticks is ended, and the car put back.</summary>
        public const int StaleAfter = 10;

        /// <summary>
        /// Road tiles, as The Sims 1 stores them in a house: 9 is the tarmac, and 11 the tarmac with a dash of the line
        /// down the middle of the road (10 on mirrored lots: the engine's house loader swaps the two, and calls it
        /// flipping the road). Read off a lot in the game, with the "driveways" console command.
        /// </summary>
        public static readonly HashSet<ushort> RoadFloors = new HashSet<ushort> { 9, 10, 11 };
        /// <summary>The pavement that runs along the road, a tile of grass away from it.</summary>
        public const ushort PavementFloor = 12;

        /// <summary>Called when a drive cannot start, with what to tell the player.</summary>
        public static Action<VM, string> OnMessage;

        private enum Phase { Out, Away, Back }

        private class Trip
        {
            public short SimID;
            public VMMultitileGroup Car;
            public DrivePlan Plan;
            public Phase Phase;
            public float Distance;
            public int Timer;
            public uint LastTick;
        }

        //the trips under way on each lot, by the car's object id. nothing here is saved
        private static ConditionalWeakTable<VM, Dictionary<short, Trip>> AllTrips = new ConditionalWeakTable<VM, Dictionary<short, Trip>>();

        private static Dictionary<short, Trip> TripsOf(VM vm) => AllTrips.GetOrCreateValue(vm);

        public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand args)
        {
            var operand = args as VMDriveOperand;
            if (operand == null) return VMPrimitiveExitCode.GOTO_FALSE;
            switch (operand.Mode)
            {
                case Shared.ModeInitDriveway:
                    InitObject(context.Caller, true);
                    //(a new object's picture is set as it is made, before this runs; a loaded one's is caught by the patch)
                    if (context.Caller is VMGameObject paving) { try { DrivewayLook.Dress(paving); } catch (Exception) { } }
                    return VMPrimitiveExitCode.GOTO_TRUE;
                case Shared.ModeInitCar:
                    InitObject(context.Caller, false);
                    //(a new object's picture is set as it is made, before this runs; a loaded one's is caught by the patch)
                    if (context.Caller is VMGameObject tile) { try { CarLook.Dress(tile); } catch (Exception) { } }
                    return VMPrimitiveExitCode.GOTO_TRUE;
                case Shared.ModeIsParked:
                    {
                        var car = CarOf(context.StackObject);
                        var parked = car != null && !TripsOf(context.VM).ContainsKey(car.BaseObject.ObjectID);
                        return parked ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE;
                    }
                case Shared.ModeDrive:
                    return Drive(context);
                default:
                    return VMPrimitiveExitCode.GOTO_FALSE;
            }
        }

        // ---- setting the objects up ----

        /// <summary>
        /// What a Sims 1 object's own init script would do with a handful of instructions: say where it may be put.
        /// A driveway piece is also something Sims and objects can share a tile with.
        /// </summary>
        private static void InitObject(VMEntity obj, bool driveway)
        {
            if (obj == null) return;
            obj.SetValue(VMStackObjectVariable.PlacementFlags, (short)(VMPlacementFlags.OnFloor | VMPlacementFlags.OnTerrain));
            obj.SetValue(VMStackObjectVariable.AllowedHeightFlags, 1); //on the floor
            obj.SetValue(VMStackObjectVariable.MovementFlags, (short)Movable); //without this the player can put it down but never pick it up again
            if (driveway)
            {
                obj.SetFlag(VMEntityFlags.AllowPersonIntersection, true);
                obj.SetFlag(VMEntityFlags.HasZeroExtent, true);
            }
        }

        private const VMMovementFlags Movable = VMMovementFlags.SimsCanMove | VMMovementFlags.PlayersCanMove | VMMovementFlags.PlayersCanDelete;

        public static bool IsDrivewayGUID(uint guid) => guid == Shared.DrivewayGUID || guid == Shared.OldTileGUID || Shared.OldDriveways.Any(o => o.GUID == guid);
        public static bool IsCarGUID(uint guid) => Shared.Cars.Any(c => c.GUID == guid);
        private static bool IsDriveway(VMEntity obj) => IsDrivewayGUID(GUIDOf(obj));

        //the lots whose objects have been looked over since they loaded
        private static ConditionalWeakTable<VM, object> Repaired = new ConditionalWeakTable<VM, object>();

        /// <summary>
        /// Version 0.1 of the pack left its objects impossible to pick up, and they were saved that way. The first time
        /// a lot ticks, give any of the pack's objects on it the movement flags a new one gets. Called once a tick.
        /// </summary>
        public static void RepairOldObjects(VM vm)
        {
            if (Repaired.TryGetValue(vm, out _)) return;
            Repaired.Add(vm, new object());
            foreach (var obj in vm.Entities)
            {
                if (!(obj is VMGameObject) || !(IsDriveway(obj) || IsCarGUID(GUIDOf(obj)) || GUIDOf(obj) == Shared.OldCarGUID)) continue;
                var flags = (VMMovementFlags)obj.GetValue(VMStackObjectVariable.MovementFlags);
                if ((flags & Movable) != Movable) obj.SetValue(VMStackObjectVariable.MovementFlags, (short)(flags | Movable));
            }
            //driveways put down before 0.8 have no floor under them yet, and are not open where they cross the pavement
            foreach (var group in vm.Entities.Where(e => e is VMGameObject && GUIDOf(e) == Shared.DrivewayGUID).Select(e => e.MultitileGroup).Distinct().ToList())
            {
                try { AfterDrivewayMoves(group, vm.Context); } catch (Exception) { }
            }
        }

        private static uint GUIDOf(VMEntity obj) => (obj.MasterDefinition ?? obj.Object.OBJ).GUID;

        private static VMMultitileGroup CarOf(VMEntity obj)
        {
            if (obj == null || !(obj is VMGameObject) || !IsCarGUID(GUIDOf(obj))) return null;
            return obj.MultitileGroup;
        }

        // ---- a drive ----

        private static VMPrimitiveExitCode Drive(VMStackFrame context)
        {
            var vm = context.VM;
            var sim = context.Caller as VMAvatar;
            var car = CarOf(context.StackObject);
            if (sim == null || car == null || car.Objects.Count == 0) return VMPrimitiveExitCode.GOTO_FALSE;

            var trips = TripsOf(vm);
            var id = car.BaseObject.ObjectID;
            var started = context.Locals != null && context.Locals.Length > 0 && context.Locals[0] == 1;

            if (!trips.TryGetValue(id, out var trip))
            {
                if (started)
                {
                    //this script was already driving, but the trip is gone: the lot was saved and loaded in the
                    //middle of it, or the trip was ended for being stale. either way it is over
                    Park(car);
                    Show(sim, true);
                    return VMPrimitiveExitCode.GOTO_TRUE;
                }

                var plan = PlanFor(vm, car);
                if (plan.Problem != RouteProblem.None)
                {
                    OnMessage?.Invoke(vm, Explain(plan.Problem));
                    return VMPrimitiveExitCode.GOTO_FALSE;
                }
                trip = new Trip { SimID = sim.ObjectID, Car = car, Plan = plan, Phase = Phase.Out };
                trips[id] = trip;
                if (context.Locals != null && context.Locals.Length > 0) context.Locals[0] = 1;
                Show(sim, false);
            }
            else if (trip.SimID != sim.ObjectID)
            {
                return VMPrimitiveExitCode.GOTO_FALSE; //somebody else has the car
            }

            trip.LastTick = vm.Scheduler.CurrentTickID;

            switch (trip.Phase)
            {
                case Phase.Out:
                    trip.Distance += trip.Plan.Out.SpeedAt(trip.Distance);
                    if (trip.Distance >= trip.Plan.Out.Length)
                    {
                        Show(car, false);
                        trip.Phase = Phase.Away;
                        trip.Timer = AwayTicks;
                    }
                    else Draw(car, trip.Plan.Out.PoseAt(trip.Distance));
                    break;

                case Phase.Away:
                    trip.Timer--;
                    if (trip.Timer % FunEvery == 0)
                    {
                        var fun = sim.GetMotiveData(VMMotive.Fun);
                        if (fun < 100) sim.SetMotiveData(VMMotive.Fun, (short)(fun + 1));
                    }
                    //cancelling the interaction brings the car straight home
                    if (trip.Timer <= 0 || sim.GetFlag(VMEntityFlags.InteractionCanceled))
                    {
                        trip.Phase = Phase.Back;
                        trip.Distance = 0;
                        Draw(car, trip.Plan.Back.PoseAt(0));
                        Show(car, true);
                    }
                    break;

                case Phase.Back:
                    trip.Distance += trip.Plan.Back.SpeedAt(trip.Distance);
                    if (trip.Distance >= trip.Plan.Back.Length)
                    {
                        trips.Remove(id);
                        Park(car);
                        Show(sim, true);
                        return VMPrimitiveExitCode.GOTO_TRUE;
                    }
                    Draw(car, trip.Plan.Back.PoseAt(trip.Distance));
                    break;
            }
            return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
        }

        /// <summary>
        /// End any trip whose Sim is no longer running it (the Sim was reset, deleted, or its script was thrown away),
        /// so the car is never left hidden. Called once a tick.
        /// </summary>
        public static void EndStaleTrips(VM vm)
        {
            if (!AllTrips.TryGetValue(vm, out var trips) || trips.Count == 0) return;
            var now = vm.Scheduler.CurrentTickID;
            List<short> stale = null;
            foreach (var pair in trips)
            {
                if (now - pair.Value.LastTick > StaleAfter) (stale ??= new List<short>()).Add(pair.Key);
            }
            if (stale == null) return;
            foreach (var id in stale)
            {
                var trip = trips[id];
                trips.Remove(id);
                Park(trip.Car);
                var sim = vm.GetObjectById(trip.SimID) as VMAvatar;
                if (sim != null) Show(sim, true);
            }
        }

        // ---- reading the lot ----

        private static int HeadingOf(Direction direction)
        {
            switch (direction)
            {
                case Direction.NORTH: return 0;
                case Direction.EAST: return 1;
                case Direction.SOUTH: return 2;
                case Direction.WEST: return 3;
                default: return -1; //a diagonal
            }
        }

        private static readonly Direction[] Directions = { Direction.NORTH, Direction.EAST, Direction.SOUTH, Direction.WEST };

        //the road of each lot, once it has been looked for. where the player can build does not change while a lot is played
        private static ConditionalWeakTable<VM, RoadFound> Roads = new ConditionalWeakTable<VM, RoadFound>();
        private class RoadFound { public Road? Road; public string How; }

        /// <summary>
        /// Find the lot's road: by The Sims 1's road tiles, which fill a strip along one side of the lot, with the
        /// pavement beside it marking where the verge ends and the garden begins. A lot with no road tiles is tried
        /// by where the player cannot build, in case it has a deep strip of that along one side.
        /// </summary>
        private static Road? FindRoad(VM vm, out string how)
        {
            if (Roads.TryGetValue(vm, out var known)) { how = known.How; return known.Road; }
            var context = vm.Context;
            var arch = context.Architecture;
            Func<int, int, ushort> floor = (x, y) => arch.GetFloor((short)x, (short)y, 1).Pattern;

            how = "its road tiles";
            var road = Road.Find((x, y) => RoadFloors.Contains(floor(x, y)), arch.Width, arch.Height);
            if (road != null)
            {
                var alongX = road.Value.AlongX;
                //a row (or column) is the pavement when at least half of it is pavement tiles
                road = road.Value.WithVerge(line =>
                {
                    int n = 0, length = alongX ? arch.Width : arch.Height;
                    for (int j = 0; j < length; j++) if ((alongX ? floor(j, line) : floor(line, j)) == PavementFloor) n++;
                    return n * 2 >= length;
                }, arch.Width, arch.Height);
            }
            else
            {
                how = "the strip that cannot be built on";
                road = Road.FindByMargin((x, y) => context.IsUserOutOfBounds(LotTilePos.FromBigTile((short)x, (short)y, 1)), arch.Width, arch.Height);
            }
            //(a lot with no road is looked at again next time: it may not have finished loading)
            if (road != null) Roads.Add(vm, new RoadFound { Road = road, How = how });
            return road;
        }

        // ---- the floor under a driveway ----
        //
        // A driveway is an object, drawn a hair above the ground. Under it the pack lays real floor, the same floor it
        // is painted with, so that it meets the road and the garden as any floor does, with no ground showing at
        // the joins. Where it crosses the pavement nothing is laid and that tile of the driveway is hidden, so the
        // pavement runs on across it. When the driveway is moved or sold, the floor that was there before is put back.
        //
        // Each tile of a driveway remembers what it did in its own attributes, which the game saves with it:
        private const int AttrPaved = 0;     //1 when this tile has laid floor
        private const int AttrBefore = 1;    //the floor that was there before
        private const int AttrX = 2, AttrY = 3; //where
        private const int AttrLaid = 4;      //the floor it laid

        private static bool IsNewDriveway(VMMultitileGroup group) => group.Objects.Count > 0 && GUIDOf(group.Objects[0]) == Shared.DrivewayGUID;

        /// <summary>Put back the floor that was under one tile of a driveway, if it laid any and nobody has changed it since.</summary>
        public static void Unpave(VMEntity tile, VMContext context)
        {
            if (tile.GhostImage || tile.GetAttribute(AttrPaved) != 1) return;
            tile.SetAttribute(AttrPaved, 0);
            var arch = context.Architecture;
            short x = tile.GetAttribute(AttrX), y = tile.GetAttribute(AttrY);
            if (x < 0 || y < 0 || x >= arch.Width || y >= arch.Height) return;
            var now = arch.GetFloor(x, y, 1);
            if (now.Pattern != (ushort)tile.GetAttribute(AttrLaid)) return; //(the player has put another floor there: leave it)
            now.Pattern = (ushort)tile.GetAttribute(AttrBefore);
            arch.SetFloor(x, y, 1, now, true);
        }

        /// <summary>Before a driveway is moved: put back the floor under where it is.</summary>
        public static void BeforeDrivewayMoves(VMMultitileGroup group, VMContext context)
        {
            if (!IsNewDriveway(group) || context?.VM == null || !context.VM.TS1) return;
            foreach (var tile in group.Objects) Unpave(tile, context);
        }

        /// <summary>
        /// After a driveway has been moved (or was not, and is where it was): lay floor under it, and open it where it
        /// crosses the pavement. One only being held shows the opening and lays nothing.
        /// </summary>
        public static void AfterDrivewayMoves(VMMultitileGroup group, VMContext context)
        {
            if (!IsNewDriveway(group) || context?.VM == null || !context.VM.TS1) return;
            var arch = context.Architecture;
            ushort laying = 0;
            foreach (var tile in group.Objects)
            {
                var pos = tile.Position;
                if (pos == LotTilePos.OUT_OF_WORLD || pos.Level != 1 || pos.TileX < 0 || pos.TileY < 0 || pos.TileX >= arch.Width || pos.TileY >= arch.Height)
                {
                    if (tile.GetValue(VMStackObjectVariable.Hidden) != 0) tile.SetValue(VMStackObjectVariable.Hidden, 0);
                    continue;
                }
                var floor = arch.GetFloor(pos.TileX, pos.TileY, 1);
                bool paved = tile.GetAttribute(AttrPaved) == 1 && tile.GetAttribute(AttrX) == pos.TileX && tile.GetAttribute(AttrY) == pos.TileY;
                //the pavement runs on across the driveway
                bool pavement = floor.Pattern == PavementFloor && !paved;
                var hide = (short)(pavement ? 1 : 0);
                if (tile.GetValue(VMStackObjectVariable.Hidden) != hide) tile.SetValue(VMStackObjectVariable.Hidden, hide);
                if (pavement || paved || tile.GhostImage) continue;

                if (laying == 0)
                {
                    laying = DrivewayLook.FloorFor(tile);
                    if (laying == 0) return; //(no floor of the game's could be found: the driveway is only its object)
                }
                if (floor.Pattern == laying) continue; //(already that floor: nothing to lay, and nothing to put back later)
                tile.SetAttribute(AttrBefore, (short)floor.Pattern);
                tile.SetAttribute(AttrX, pos.TileX);
                tile.SetAttribute(AttrY, pos.TileY);
                tile.SetAttribute(AttrLaid, (short)laying);
                tile.SetAttribute(AttrPaved, 1);
                floor.Pattern = laying;
                arch.SetFloor(pos.TileX, pos.TileY, 1, floor, true);
            }
        }

        /// <summary>The player has chosen another floor: lay it under every driveway on the lot in place of the one laid before.</summary>
        public static void Relay(VM vm)
        {
            if (vm == null) return;
            var arch = vm.Context.Architecture;
            foreach (var tile in vm.Entities.Where(e => e is VMGameObject && GUIDOf(e) == Shared.DrivewayGUID && e.GetAttribute(AttrPaved) == 1).ToList())
            {
                var laying = DrivewayLook.FloorFor(tile);
                if (laying == 0) return;
                short x = tile.GetAttribute(AttrX), y = tile.GetAttribute(AttrY);
                if (x < 0 || y < 0 || x >= arch.Width || y >= arch.Height) continue;
                var now = arch.GetFloor(x, y, 1);
                if (now.Pattern != (ushort)tile.GetAttribute(AttrLaid) || now.Pattern == laying) continue;
                now.Pattern = laying;
                arch.SetFloor(x, y, 1, now, true);
                tile.SetAttribute(AttrLaid, (short)laying);
            }
        }

        //for the "driveways" report: how often a driveway has been snapped since the game started, and the last time
        private static int Snaps;
        private static string LastSnap = "never";

        /// <summary>
        /// A driveway the player is holding or putting down goes against the strip by the road, pointing at it, as in
        /// The Sims 2: it follows the cursor along the road and nowhere else. Called before the game moves any object;
        /// changes where a driveway is being moved to. Only the player's own placing is touched, never a lot loading.
        /// </summary>
        /// <summary>
        /// A car the player is holding or putting down can only go on a driveway: every tile of it on driveway, clear
        /// of the pavement, and lengthways along the driveway (it is turned to point at the road if it is held
        /// across). Returns false, with why, where it cannot go; changes the direction it is being given.
        /// </summary>
        public static bool CarMayGo(VMMultitileGroup group, LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags)
        {
            if ((flags & VMPlaceRequestFlags.UserBuildableLimit) == 0 || pos == LotTilePos.OUT_OF_WORLD || group.Objects.Count == 0) return true;
            if (!IsCarGUID(GUIDOf(group.Objects[0]))) return true;
            var vm = context.VM;
            if (vm == null || !vm.TS1) return true;
            if (pos.Level != 1) return false;
            var heading = HeadingOf(direction);
            if (heading == -1) return false;

            var road = FindRoad(vm, out _);
            if (road != null)
            {
                //lengthways to the road: one held across it is turned to face it
                bool nosePointsAcross = road.Value.AlongX ? (heading % 2 == 0) : (heading % 2 == 1);
                if (!nosePointsAcross)
                {
                    var arch = context.Architecture;
                    int across = road.Value.AlongX ? arch.Height : arch.Width;
                    bool roadAtHighEdge = (across - 1 - road.Value.Last) <= road.Value.First;
                    heading = road.Value.AlongX ? (roadAtHighEdge ? 2 : 0) : (roadAtHighEdge ? 1 : 3);
                    direction = Directions[heading];
                }
            }
            for (int x = 0; x < Shared.CarWidth; x++)
                for (int y = 0; y < Shared.CarLength; y++)
                {
                    var off = DrivePlan.Rotate(x, y, heading);
                    int tx = pos.TileX + (int)Math.Round(off.X), ty = pos.TileY + (int)Math.Round(off.Y);
                    if (road != null)
                    {
                        //not on the pavement, the verge or the road
                        int line = road.Value.AlongX ? ty : tx;
                        if (line >= road.Value.StripFirst && line <= road.Value.StripLast) return false;
                    }
                    var here = context.ObjectQueries.GetObjectsAt(LotTilePos.FromBigTile((short)tx, (short)ty, 1));
                    if (here == null || !here.Any(o => o != null && o is VMGameObject && IsDriveway(o))) return false;
                }
            return true;
        }

        public static void SnapDriveway(VMMultitileGroup group, ref LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags)
        {
            if ((flags & VMPlaceRequestFlags.UserBuildableLimit) == 0 || pos == LotTilePos.OUT_OF_WORLD || group.Objects.Count == 0) return;
            if (GUIDOf(group.Objects[0]) != Shared.DrivewayGUID) return;
            var vm = context.VM;
            if (vm == null || !vm.TS1) return;
            Snaps++;
            var road = FindRoad(vm, out _);
            if (road == null) { LastSnap = "no road was found, so the driveway was left where the cursor was"; return; }
            var arch = context.Architecture;
            var place = DrivewaySnap.Place(road.Value, pos.TileX, pos.TileY, Shared.DrivewayWidth, Shared.DrivewayLength, arch.Width, arch.Height);
            LastSnap = $"cursor at tile {pos.TileX}, {pos.TileY}; driveway's lead tile put at {place.X}, {place.Y} facing {Directions[place.Heading].ToString().ToLowerInvariant()}";
            pos = LotTilePos.FromBigTile((short)place.X, (short)place.Y, 1);
            direction = Directions[place.Heading];
        }

        /// <summary>
        /// What the pack makes of a lot, for the "driveways" console command: where it thinks the road is, which floor
        /// tiles the part of the lot that cannot be built on is made of, and whether each car could be driven now.
        /// </summary>
        public static string Describe(VM vm)
        {
            var context = vm.Context;
            var arch = context.Architecture;
            var lines = new List<string>();

            var road = FindRoad(vm, out var how);
            if (road == null) lines.Add($"No road found on this {arch.Width} by {arch.Height} lot.");
            else
            {
                var lines2 = road.Value.AlongX ? "rows" : "columns";
                lines.Add($"Road: {lines2} {road.Value.First} to {road.Value.Last} of a {arch.Width} by {arch.Height} lot, found by {how}."
                    + (road.Value.VergeFirst != null ? $" With its verge and pavement it is {lines2} {road.Value.StripFirst} to {road.Value.StripLast}: a driveway goes against that." : ""));
            }

            var floors = new Dictionary<ushort, int>();
            for (short y = 0; y < arch.Height; y++)
                for (short x = 0; x < arch.Width; x++)
                {
                    if (!context.IsUserOutOfBounds(LotTilePos.FromBigTile(x, y, 1))) continue;
                    var pattern = arch.GetFloor(x, y, 1).Pattern;
                    floors[pattern] = floors.TryGetValue(pattern, out var count) ? count + 1 : 1;
                }
            lines.Add("Floor tiles where the player cannot build (tile id x count): " +
                (floors.Count == 0 ? "none" : string.Join(", ", floors.OrderByDescending(f => f.Value).Take(8).Select(f => f.Key + " x " + f.Value))) + ".");

            lines.Add($"Driveways snapped to the road since the game started: {Snaps} times. The last: {LastSnap}.");
            lines.Add("The driveway's look: " + DrivewayLook.Describe());
            lines.Add("The car's look: " + CarLook.Describe());

            var cars = vm.Entities.Where(e => e is VMGameObject && IsCarGUID(GUIDOf(e))).Select(e => e.MultitileGroup).Distinct().ToList();
            if (cars.Count == 0) lines.Add("No cars on this lot.");
            foreach (var car in cars)
            {
                var lead = car.BaseObject;
                var where = $"Car at tile {lead.Position.TileX}, {lead.Position.TileY} facing {lead.Direction.ToString().ToLowerInvariant()}: ";
                if (TripsOf(vm).ContainsKey(lead.ObjectID)) { lines.Add(where + "out for a drive."); continue; }
                var plan = PlanFor(vm, car);
                lines.Add(where + (plan.Problem == RouteProblem.None ? "ready to drive." : Explain(plan.Problem)));
            }
            return string.Join("\n\n", lines);
        }

        /// <summary>
        /// The same, with two maps of the lot's ground floor, as text for a file: where the player cannot build, and
        /// the floor tile on each of those tiles. For sending to whoever is working on the pack.
        /// </summary>
        public static string DescribeWithMaps(VM vm)
        {
            var context = vm.Context;
            var arch = context.Architecture;
            var text = new System.Text.StringBuilder();
            text.AppendLine(Describe(vm).Replace("\n\n", Environment.NewLine));
            text.AppendLine();
            text.AppendLine("Where the player cannot build (#), north at the top:");
            for (short y = 0; y < arch.Height; y++)
            {
                for (short x = 0; x < arch.Width; x++) text.Append(context.IsUserOutOfBounds(LotTilePos.FromBigTile(x, y, 1)) ? '#' : '.');
                text.AppendLine();
            }
            text.AppendLine();
            text.AppendLine("Floor tile ids on the ground floor (two hex digits, .. for none, ** for 256 and up):");
            for (short y = 0; y < arch.Height; y++)
            {
                for (short x = 0; x < arch.Width; x++)
                {
                    var pattern = arch.GetFloor(x, y, 1).Pattern;
                    text.Append(pattern == 0 ? ".." : (pattern > 255 ? "**" : pattern.ToString("X2")));
                }
                text.AppendLine();
            }
            return text.ToString();
        }

        private static DrivePlan PlanFor(VM vm, VMMultitileGroup car)
        {
            var context = vm.Context;
            var arch = context.Architecture;
            var lead = car.BaseObject;
            var pos = lead.Position;

            if (pos == LotTilePos.OUT_OF_WORLD || pos.Level != 1) return new DrivePlan { Problem = RouteProblem.NotGroundLevel };
            var heading = HeadingOf(lead.Direction);
            if (heading == -1) return new DrivePlan { Problem = RouteProblem.NotFacingRoad };

            var road = FindRoad(vm, out _);
            if (road == null) return new DrivePlan { Problem = RouteProblem.NoRoad };

            var plan = DrivePlan.Make(pos.x / 16f, pos.y / 16f, heading, Shared.CarWidth, Shared.CarLength, road.Value, arch.Width, arch.Height, Settings.ReverseCars);
            if (plan.Problem != RouteProblem.None) return plan;

            if (plan.Corridor.Count > 0)
            {
                int minX = plan.Corridor.Min(t => t.X), maxX = plan.Corridor.Max(t => t.X);
                int minY = plan.Corridor.Min(t => t.Y), maxY = plan.Corridor.Max(t => t.Y);
                foreach (var tile in plan.Corridor)
                {
                    if (tile.X < 0 || tile.Y < 0 || tile.X >= arch.Width || tile.Y >= arch.Height) { plan.Problem = RouteProblem.Blocked; return plan; }

                    //a wall or fence the car would have to go through. a tile holds the walls on its west (top left)
                    //and north (top right) edges, so the ones on the outside of the corridor are left alone
                    var wall = arch.GetWall((short)tile.X, (short)tile.Y, 1);
                    if ((wall.Segments & WallSegments.AnyDiag) != 0
                        || (tile.X > minX && (wall.Segments & WallSegments.TopLeft) != 0)
                        || (tile.Y > minY && (wall.Segments & WallSegments.TopRight) != 0))
                    {
                        plan.Problem = RouteProblem.Blocked; return plan;
                    }

                    var tilePos = LotTilePos.FromBigTile((short)tile.X, (short)tile.Y, 1);
                    var here = context.ObjectQueries.GetObjectsAt(tilePos);
                    var paved = false;
                    if (here != null)
                    {
                        foreach (var obj in here)
                        {
                            if (obj == null || obj is VMAvatar || obj.MultitileGroup == car) continue;
                            if (IsDriveway(obj)) { paved = true; continue; }
                            //the same test the game uses for whether a Sim can walk through something
                            var flags = (VMEntityFlags)obj.GetValue(VMStackObjectVariable.Flags);
                            if ((flags & VMEntityFlags.DisallowPersonIntersection) != 0 || (flags & (VMEntityFlags.AllowPersonIntersection | VMEntityFlags.HasZeroExtent)) == 0)
                            {
                                plan.Problem = RouteProblem.Blocked; return plan;
                            }
                        }
                    }
                    //(the corridor stops at the pavement by the road: the pavement and verge need no driveway)
                    if (!paved && !context.IsUserOutOfBounds(tilePos)) { plan.Problem = RouteProblem.NoDriveway; return plan; }
                }
            }
            return plan;
        }

        private static string Explain(RouteProblem problem)
        {
            switch (problem)
            {
                case RouteProblem.NoRoad: return "There is no road on this lot to drive on.";
                case RouteProblem.OnRoad: return "The car has to be parked on the lot, clear of the road.";
                case RouteProblem.NotFacingRoad: return "The car has to be parked with its front or its back towards the road.";
                case RouteProblem.NotGroundLevel: return "The car has to be on the ground.";
                case RouteProblem.Blocked: return "Something is in the way between the car and the road.";
                case RouteProblem.NoDriveway: return "The car needs a driveway under it, all the way to the road.";
                default: return "The car cannot be driven from here.";
            }
        }

        // ---- drawing ----

        /// <summary>
        /// Draw the car's tiles as if it stood at this pose. This is VMMultitileGroup.SetVisualPosition, except that it
        /// leaves the objects' own direction alone: only their pictures turn.
        /// </summary>
        private static void Draw(VMMultitileGroup car, Pose pose)
        {
            if (!VM.UseWorld) return;
            var lead = DrivePlan.LeadFromCentre(pose.X, pose.Y, pose.Heading, Shared.CarWidth, Shared.CarLength);
            var baseOffset = car.Offsets[car.Objects.IndexOf(car.BaseObject)];
            for (int i = 0; i < car.Objects.Count; i++)
            {
                var part = car.Objects[i];
                if (part.WorldUI == null) continue;
                var off = DrivePlan.Rotate((car.Offsets[i].x - baseOffset.x) / 16f, (car.Offsets[i].y - baseOffset.y) / 16f, pose.Heading);
                if (part.WorldUI.Room == 0) part.WorldUI.Room = 65535; //(as SetVisualPosition does: never treated as off the lot)
                //in 2D, objects that stand still are drawn once and kept. one that moves must say so, to be drawn afresh every frame
                if (part.WorldUI is ObjectComponent picture) picture.ForceDynamic = true;
                part.WorldUI.Direction = Directions[pose.Heading];
                part.VisualPosition = new Vector3(lead.X + off.X, lead.Y + off.Y, 0);
            }
        }

        /// <summary>Put the car's pictures back where the car really is.</summary>
        private static void Park(VMMultitileGroup car)
        {
            if (!VM.UseWorld) return;
            foreach (var part in car.Objects)
            {
                if (part.WorldUI == null || part.Dead || part.Position == LotTilePos.OUT_OF_WORLD) continue;
                part.WorldUI.Direction = part.Direction;
                part.VisualPosition = new Vector3(part.Position.x / 16f, part.Position.y / 16f, (part.Position.Level - 1) * 2.95f);
                if (part.WorldUI is ObjectComponent picture) picture.ForceDynamic = false;
            }
            Show(car, true);
        }

        private static void Show(VMMultitileGroup car, bool visible)
        {
            if (!VM.UseWorld) return;
            foreach (var part in car.Objects)
            {
                //(an object's Visible hides the base class's: it must be set as an ObjectComponent to take effect)
                if (part.WorldUI is ObjectComponent picture) picture.Visible = visible;
            }
        }

        private static void Show(VMAvatar sim, bool visible)
        {
            if (!VM.UseWorld || sim.WorldUI == null) return;
            sim.WorldUI.Visible = visible;
        }
    }

    /// <summary>What the player has chosen, kept in driveways.cfg in Simitone's user directory.</summary>
    public static class Settings
    {
        /// <summary>Words from the name of the floor the driveway is painted with. Empty for the pack's own choice.</summary>
        public static string Floor = "";
        /// <summary>The game's cars have their front at the other end from the pack's guess: drive them the other way round.</summary>
        public static bool ReverseCars;

        private static string File => System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "driveways.cfg");

        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return;
                foreach (var line in System.IO.File.ReadAllLines(File))
                {
                    var split = line.IndexOf('=');
                    if (split < 0) continue;
                    var key = line.Substring(0, split).Trim().ToLowerInvariant(); var value = line.Substring(split + 1).Trim();
                    if (key == "floor") Floor = value;
                    else if (key == "reversecars") ReverseCars = value == "1";
                }
            }
            catch (Exception) { }
        }

        public static void Save()
        {
            try { System.IO.File.WriteAllLines(File, new[] { "floor=" + Floor, "reversecars=" + (ReverseCars ? "1" : "0") }); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// The driveway's look: one of the game's own floors. The pack's object file gives the driveway only its shape;
    /// here its tiles are given pictures painted with a floor tile from the player's game (see FloorDress).
    ///
    /// Which floor: the one the player chose with "driveways floor", else the first the game sells whose name has
    /// one of the Preferred words in it, else the road's own tarmac. Never the pavement's: a driveway that looks
    /// like the pavement it crosses does not look like a driveway.
    /// </summary>
    public static class DrivewayLook
    {
        /// <summary>Words to look for in a floor's name, best first, when the player has not chosen.</summary>
        private static readonly string[] Preferred = { "concrete", "cement", "asphalt", "tarmac", "blacktop", "gravel", "paver", "paving", "flagstone", "cobble" };
        /// <summary>The road's tarmac, among the game's built-in floors: what a driveway is painted with when nothing better is found.</summary>
        private const ushort Tarmac = 9;

        private static bool Looked;
        private static string Outcome = "not looked for yet.";
        //the number of the floor the driveway is painted with, as the lot's own floors are numbered; 0 if none was found
        private static ushort Pattern;
        //the painted drawing groups, each numbered FloorDress.IDOffset above the plain one it stands in for; held so they stay loaded
        private static FSO.Files.Formats.IFF.IffFile Dressed;
        //every driveway tile dressed so far, so they can be painted again when the floor is changed
        private static List<WeakReference<VMGameObject>> Tiles = new List<WeakReference<VMGameObject>>();

        public static string Describe() => Looked ? Outcome : "not looked for yet (no driveway has been drawn).";

        private static bool IsDrivewayTile(VMGameObject obj) => VMDrive.IsDrivewayGUID((obj.MasterDefinition ?? obj.Object.OBJ).GUID);

        /// <summary>The floors the game sells: each one's number and name.</summary>
        public static List<(ushort ID, string Name, int Price)> Floors()
        {
            var provider = FSO.Content.Content.Get().WorldFloors;
            return provider.Entries.Values.Select(e => ((ushort)e.ID, e.Name ?? "", e.Price)).OrderBy(e => e.Item2).ToList();
        }

        /// <summary>A floor tile's near sprite and palette, from a floor file of the game's.</summary>
        private static FloorDress.Tile Read(FSO.Files.Formats.IFF.IffFile file, ushort sprite)
        {
            var spr = file?.Get<FSO.Files.Formats.IFF.Chunks.SPR2>(sprite);
            if (spr == null || spr.Frames.Length == 0) return null;
            var frame = spr.Frames[0];
            frame.DecodeIfRequired(false);
            if (frame.PixelData == null || frame.Width < 8 || frame.Height < 4 || frame.PixelData.Length < frame.Width * frame.Height) return null;
            var palette = (file.Get<FSO.Files.Formats.IFF.Chunks.PALT>(frame.PaletteID) ?? file.Get<FSO.Files.Formats.IFF.Chunks.PALT>((ushort)spr.DefaultPaletteID))?.Colors;
            //(no palette to be found: the colours the tile uses will do)
            if (palette == null) palette = frame.PixelData.Where(c => c.A != 0).Select(c => new Color(c.R, c.G, c.B)).Distinct().Take(255).ToArray();
            return new FloorDress.Tile { Pixels = (Color[])frame.PixelData.Clone(), Width = frame.Width, Height = frame.Height, Palette = palette };
        }

        private static void Look(FSO.Files.Formats.IFF.IffFile packFile)
        {
            Looked = true;
            Dressed = null;
            Pattern = 0;
            try
            {
                var content = FSO.Content.Content.Get();
                ushort number = 0;
                FloorDress.Tile tile = null;
                string what = null;

                //a floor the game sells: the player's choice, or the first with a preferred word in its name
                var floors = Floors();
                var wanted = Settings.Floor.Trim().ToLowerInvariant();
                (ushort ID, string Name, int Price)? pick = null;
                if (wanted != "" && wanted != "tarmac")
                {
                    var words = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var matches = floors.Where(f => words.All(w => f.Name.ToLowerInvariant().Contains(w))).ToList();
                    if (matches.Count > 0) pick = matches.OrderBy(f => f.Name.Length).First();
                }
                else if (wanted == "")
                {
                    foreach (var word in Preferred)
                    {
                        var matches = floors.Where(f => f.Name.ToLowerInvariant().Contains(word)).ToList();
                        if (matches.Count > 0) { pick = matches.OrderBy(f => f.Name.Length).First(); break; }
                    }
                }
                if (pick != null)
                {
                    //(a floor file holds its tile three times, for the three zooms: sprite 513 is the near one)
                    var provider = content.WorldFloors;
                    var file = provider.Floors.Get(provider.Entries[pick.Value.ID].FileName);
                    tile = Read(file, 513);
                    if (tile != null) { what = $"the game's floor \"{pick.Value.Name}\""; number = pick.Value.ID; }
                }
                if (tile == null)
                {
                    //the game's built-in floors: floor n's near sprite is sprite n + 512 (as the engine's floor provider reads them)
                    var floorsFile = new FSO.Files.Formats.IFF.IffFile(System.IO.Path.Combine(content.TS1BasePath, "GameData/floors.iff"));
                    tile = Read(floorsFile, (ushort)(Tarmac + 512));
                    if (tile != null) number = Tarmac;
                    if (tile != null) what = "the road's tarmac" + (wanted != "" && wanted != "tarmac" ? $" (no floor named \"{Settings.Floor}\" was found)" : "");
                }
                if (tile == null) { Outcome = "the pack's plain grey: no floor tile could be read from the game."; return; }

                var groups = packFile.List<FSO.Files.Formats.IFF.Chunks.OBJD>().Where(o => o.NumGraphics > 0 && VMDrive.IsDrivewayGUID(o.MasterID == 0 ? o.GUID : MasterOf(packFile, o)))
                    .Select(o => o.BaseGraphicID).Distinct().ToList();
                Dressed = FloorDress.Build(packFile, groups, tile);
                Pattern = number;
                Outcome = $"painted with {what} ({tile.Width} by {tile.Height}). \"driveways floors\" lists the floors it could be; \"driveways floor <words from a name>\" picks one, and \"driveways floor tarmac\" the road's.";
            }
            catch (Exception e)
            {
                Dressed = null;
                Outcome = "the pack's plain grey: painting it with a floor of the game's failed (" + e.Message + ").";
            }
        }

        private static uint MasterOf(FSO.Files.Formats.IFF.IffFile file, FSO.Files.Formats.IFF.Chunks.OBJD tile)
        {
            return file.List<FSO.Files.Formats.IFF.Chunks.OBJD>().FirstOrDefault(o => o.MasterID == tile.MasterID && o.SubIndex == -1)?.GUID ?? 0;
        }

        /// <summary>The floor to lay under a driveway: the one it is painted with. 0 if none could be found.</summary>
        public static ushort FloorFor(VMEntity tile)
        {
            if (!Looked) Look(tile.Object.OBJ.ChunkParent);
            return Pattern;
        }

        /// <summary>
        /// Called whenever the game sets an object's picture. For a tile of a driveway, set the painted one in its place.
        /// </summary>
        public static void Dress(VMGameObject obj)
        {
            if (!VM.UseWorld || !IsDrivewayTile(obj)) return;
            if (!Looked) Look(obj.Object.OBJ.ChunkParent);
            lock (Tiles)
            {
                Tiles.RemoveAll(t => !t.TryGetTarget(out _));
                if (!Tiles.Any(t => t.TryGetTarget(out var known) && known == obj)) Tiles.Add(new WeakReference<VMGameObject>(obj));
            }
            Paint(obj);
        }

        private static void Paint(VMGameObject obj)
        {
            if (Dressed == null || !(obj.WorldUI is ObjectComponent picture)) return;
            var painted = Dressed.Get<FSO.Files.Formats.IFF.Chunks.DGRP>((ushort)(obj.Object.OBJ.BaseGraphicID + FloorDress.IDOffset));
            if (painted != null) picture.DGRP = painted;
        }

        /// <summary>The player has chosen another floor: paint every driveway again.</summary>
        public static void Repaint()
        {
            List<VMGameObject> tiles;
            lock (Tiles) tiles = Tiles.Select(t => t.TryGetTarget(out var obj) ? obj : null).Where(o => o != null && !o.Dead).ToList();
            if (tiles.Count == 0) { Looked = false; return; }
            Look(tiles[0].Object.OBJ.ChunkParent);
            foreach (var tile in tiles) { try { Paint(tile); } catch (Exception) { } }
        }
    }

    /// <summary>
    /// The cars' look. The pack has no car of its own to show: The Sims 1 has real cars, the ones that take Sims to
    /// work, and each car the pack sells is drawn with one of those. Each tile of the pack's car is given the picture
    /// of the matching tile of the game's car. Only the pictures are borrowed. The game's car objects and their
    /// scripts are not used, and nothing in the game's files is changed.
    ///
    /// Which of the game's cars each is drawn with is in Shared.Cars. The game's cars stand on three tiles by six,
    /// so the pack's do too. "driveways cars" lists every vehicle the game has.
    /// </summary>
    public static class CarLook
    {
        /// <summary>Words in an object's name, or its file's, that make it a vehicle.</summary>
        private static readonly System.Text.RegularExpressions.Regex VehicleName = new System.Text.RegularExpressions.Regex(
            @"(?<![a-z])(car|cars|carpool|car pool|taxi|cab|limo|limousine|bus|sedan|coupe|junker|truck|jeep|van|suv|vehicle|auto)(?![a-z])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static bool Looked;
        private static string Outcome = "not looked for yet.";
        //for each car the pack sells (by its GUID): the drawing group for each of its tiles, by the tile's place (x * 256 + y)
        private static Dictionary<uint, Dictionary<int, FSO.Files.Formats.IFF.Chunks.DGRP>> Borrowed = new Dictionary<uint, Dictionary<int, FSO.Files.Formats.IFF.Chunks.DGRP>>();
        //(held so the game keeps them loaded)
        private static List<FSO.Content.GameObject> Held = new List<FSO.Content.GameObject>();

        public static string Describe() => Looked ? Outcome : "not looked for yet (no car has been drawn).";

        private class Vehicle
        {
            public string File, Name;
            public uint GUID;
            public short Group;
            public List<FSO.Content.GameObjectReference> Tiles = new List<FSO.Content.GameObjectReference>();
        }

        /// <summary>Every multi-tile object in the game whose name, or whose file's name, says it is a vehicle.</summary>
        private static List<Vehicle> FindVehicles()
        {
            var provider = FSO.Content.Content.Get().WorldObjects;
            var byGroup = provider.Entries.Values.Where(e => e.Group != 0).GroupBy(e => (e.FileName, e.Group));
            var result = new List<Vehicle>();
            foreach (var group in byGroup)
            {
                var master = group.FirstOrDefault(e => e.SubIndex == -1);
                var tiles = group.Where(e => e.SubIndex != -1).ToList();
                if (master == null || tiles.Count < 2) continue;
                if (VMDrive.IsCarGUID((uint)master.ID) || (uint)master.ID == Shared.OldCarGUID) continue; //(the pack's own)
                var file = System.IO.Path.GetFileNameWithoutExtension((master.FileName ?? "").Replace('\\', '/'));
                if (!VehicleName.IsMatch(master.Name ?? "") && !VehicleName.IsMatch(file)) continue;
                result.Add(new Vehicle { File = file, Name = master.Name, GUID = (uint)master.ID, Group = master.Group, Tiles = tiles });
            }
            return result.OrderBy(v => v.File).ThenBy(v => v.Name).ToList();
        }

        private static (int X, int Y) PlaceOf(short subIndex) => (((ushort)subIndex) >> 8, ((ushort)subIndex) & 0xFF);

        private static void Look()
        {
            Looked = true;
            var found = new List<string>(); var missing = new List<string>();
            try
            {
                var provider = FSO.Content.Content.Get().WorldObjects;
                foreach (var model in Shared.Cars)
                {
                    //the game's car, by its master's GUID: its tiles are the other objects of its group in the same file
                    if (!provider.Entries.TryGetValue(model.GameGUID, out var master)) { missing.Add(model.Name); continue; }
                    var tiles = provider.Entries.Values.Where(e => e.FileName == master.FileName && e.Group == master.Group && e.SubIndex != -1).ToList();
                    var places = tiles.Select(t => PlaceOf(t.SubIndex)).ToList();
                    if (places.Count != Shared.CarWidth * Shared.CarLength || places.Max(p => p.X) != Shared.CarWidth - 1 || places.Max(p => p.Y) != Shared.CarLength - 1 || places.Distinct().Count() != places.Count) { missing.Add(model.Name + " (not on " + Shared.CarWidth + " by " + Shared.CarLength + " tiles)"); continue; }
                    var borrowed = new Dictionary<int, FSO.Files.Formats.IFF.Chunks.DGRP>();
                    foreach (var tile in tiles)
                    {
                        var obj = provider.Get((uint)tile.ID);
                        var dgrp = obj?.Resource.Get<FSO.Files.Formats.IFF.Chunks.DGRP>(obj.OBJ.BaseGraphicID);
                        if (dgrp == null) break;
                        var place = PlaceOf(tile.SubIndex);
                        borrowed[place.X * 256 + place.Y] = dgrp;
                        Held.Add(obj);
                    }
                    if (borrowed.Count != places.Count) { missing.Add(model.Name + " (a tile has no picture)"); continue; }
                    Borrowed[model.GUID] = borrowed;
                    found.Add($"{model.Name} from \"{master.Name}\"");
                }
                Outcome = (found.Count > 0 ? "borrowed from the game's cars: " + string.Join(", ", found) + "." : "none could be borrowed from the game.")
                    + (missing.Count > 0 ? " Not in this game, and drawn as grey blocks: " + string.Join(", ", missing) + "." : "");
            }
            catch (Exception e)
            {
                Outcome = "looking for the game's cars failed (" + e.Message + "); those not found are drawn as grey blocks. Found: " + string.Join(", ", found) + ".";
            }
        }

        /// <summary>
        /// Called whenever the game sets an object's picture. For a tile of one of the pack's cars, set the game's
        /// car's tile in its place.
        /// </summary>
        public static void Dress(VMGameObject obj)
        {
            if (!VM.UseWorld || obj.MasterDefinition == null || !VMDrive.IsCarGUID(obj.MasterDefinition.GUID)) return;
            if (!Looked) Look();
            if (!Borrowed.TryGetValue(obj.MasterDefinition.GUID, out var tiles)) return;
            var place = PlaceOf(obj.Object.OBJ.SubIndex);
            if (tiles.TryGetValue(place.X * 256 + place.Y, out var dgrp) && obj.WorldUI is ObjectComponent picture) picture.DGRP = dgrp;
        }

        /// <summary>
        /// How the game's own carpool works, for the "driveways work" console command: the menu and the scripts of the
        /// carpool's car, of the scripts it shares with other objects, and of anything else named for work. Scripts
        /// are given instruction by instruction, as numbers. For working out how a Sim's own car could take them to
        /// work.
        /// </summary>
        public static string ListWork()
        {
            var text = new System.Text.StringBuilder();
            var content = FSO.Content.Content.Get();
            var provider = content.WorldObjects;
            var done = new HashSet<FSO.Files.Formats.IFF.IffFile>();

            void Dump(string title, FSO.Files.Formats.IFF.IffFile file)
            {
                if (file == null || !done.Add(file)) return;
                text.AppendLine();
                text.AppendLine("==== " + title + " (" + file.Filename + ") ====");
                foreach (var objd in file.List<FSO.Files.Formats.IFF.Chunks.OBJD>() ?? new List<FSO.Files.Formats.IFF.Chunks.OBJD>())
                {
                    if (objd.MasterID != 0 && objd.SubIndex != -1 && objd.SubIndex != 0) continue; //(one tile of each is enough)
                    text.AppendLine($"object \"{objd.ChunkLabel}\" {objd.GUID:X8} type {objd.ObjectType} sub {objd.SubIndex}: init {objd.BHAV_Init} main {objd.BHAV_MainID} menu {objd.TreeTableID} attributes {objd.NumAttributes} global {objd.Global} fn table {objd.UsesFnTable}");
                }
                foreach (var glob in file.List<FSO.Files.Formats.IFF.Chunks.GLOB>() ?? new List<FSO.Files.Formats.IFF.Chunks.GLOB>())
                    text.AppendLine("shares scripts with: " + glob.Name);
                foreach (var table in file.List<FSO.Files.Formats.IFF.Chunks.TTAB>() ?? new List<FSO.Files.Formats.IFF.Chunks.TTAB>())
                {
                    var names = file.Get<FSO.Files.Formats.IFF.Chunks.TTAs>(table.ChunkID);
                    text.AppendLine($"menu {table.ChunkID}:");
                    foreach (var action in table.Interactions)
                        text.AppendLine($"  \"{names?.GetString((int)action.TTAIndex)}\" action {action.ActionFunction} test {action.TestFunction} flags {(uint)action.Flags:X} autonomy {action.AutonomyThreshold} attenuation {action.AttenuationCode}");
                }
                foreach (var strings in file.List<FSO.Files.Formats.IFF.Chunks.STR>() ?? new List<FSO.Files.Formats.IFF.Chunks.STR>())
                {
                    if (strings is FSO.Files.Formats.IFF.Chunks.TTAs || strings is FSO.Files.Formats.IFF.Chunks.CTSS) continue;
                    text.AppendLine($"strings {strings.ChunkID} \"{strings.ChunkLabel}\": " + string.Join(" | ", Enumerable.Range(0, Math.Min(strings.Length, 40)).Select(i => strings.GetString(i))));
                }
                foreach (var script in (file.List<FSO.Files.Formats.IFF.Chunks.BHAV>() ?? new List<FSO.Files.Formats.IFF.Chunks.BHAV>()).OrderBy(b => b.ChunkID))
                {
                    text.AppendLine($"script {script.ChunkID} \"{script.ChunkLabel}\" args {script.Args} locals {script.Locals}");
                    for (int i = 0; i < script.Instructions.Length; i++)
                    {
                        var ins = script.Instructions[i];
                        text.AppendLine($"  {i}: {ins.Opcode} [{string.Join(" ", ins.Operand.Select(b => b.ToString("X2")))}] t{ins.TruePointer} f{ins.FalsePointer}");
                    }
                }
            }

            try
            {
                //the carpool's car, and the scripts it shares
                var car = provider.Get(0xDB6DEDB2);
                if (car == null) text.AppendLine("The carpool's car (DB6DEDB2) is not in this game.");
                else
                {
                    Dump("The carpool's car", car.Resource.MainIff);
                    var resource = car.Resource as FSO.Content.GameObjectResource;
                    if (resource?.SemiGlobal != null) Dump("The scripts it shares", resource.SemiGlobal.MainIff);
                }
                //anything else whose name says it is to do with work or cars arriving
                var names = new System.Text.RegularExpressions.Regex("carpool|car pool|car portal|job|work|school bus", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var entry in provider.Entries.Values.Where(e => (e.SubIndex == -1 || e.Group == 0) && names.IsMatch(e.Name ?? "")).OrderBy(e => e.Name).Take(25))
                {
                    try { Dump("\"" + entry.Name + "\"", provider.Get((uint)entry.ID)?.Resource.MainIff); }
                    catch (Exception e) { text.AppendLine($"\"{entry.Name}\" could not be read: {e.Message}"); }
                }
                //the scripts every object shares: only their names, and those named for cars or work in full
                var global = content.WorldObjectGlobals.Get("global")?.Resource?.MainIff;
                if (global != null)
                {
                    text.AppendLine();
                    text.AppendLine("==== The scripts every object shares (names only) ====");
                    foreach (var script in global.List<FSO.Files.Formats.IFF.Chunks.BHAV>().OrderBy(b => b.ChunkID))
                        text.AppendLine($"script {script.ChunkID} \"{script.ChunkLabel}\" args {script.Args}");
                }
            }
            catch (Exception e) { text.AppendLine("Stopped: " + e); }
            return text.ToString();
        }

        /// <summary>The game's vehicles, for the "driveways cars" console command: each one's name, file and the tiles it stands on.</summary>
        public static string ListVehicles()
        {
            var text = new System.Text.StringBuilder();
            var provider = FSO.Content.Content.Get().WorldObjects;
            var vehicles = FindVehicles();
            text.AppendLine($"{vehicles.Count} multi-tile objects whose name or file says they are vehicles. The car's look now: {Describe()}");
            foreach (var vehicle in vehicles)
            {
                text.AppendLine();
                text.AppendLine($"\"{vehicle.Name}\" in {vehicle.File}, GUID {vehicle.GUID:X8}, group {vehicle.Group}, {vehicle.Tiles.Count} tiles");
                foreach (var tile in vehicle.Tiles.OrderBy(t => (ushort)t.SubIndex))
                {
                    var place = PlaceOf(tile.SubIndex);
                    var line = $"  tile {place.X},{place.Y}: \"{tile.Name}\" {(uint)tile.ID:X8}";
                    try
                    {
                        var obj = provider.Get((uint)tile.ID);
                        if (obj != null) line += $", graphics {obj.OBJ.BaseGraphicID} x {obj.OBJ.NumGraphics}, type {obj.OBJ.ObjectType}, level offset {obj.OBJ.LevelOffset}, tree table {obj.OBJ.TreeTableID}";
                    }
                    catch (Exception e) { line += " (could not be loaded: " + e.Message + ")"; }
                    text.AppendLine(line);
                }
            }
            return text.ToString();
        }
    }

    public class VMDriveOperand : VMPrimitiveOperand
    {
        public byte Mode { get; set; }

        public void Read(byte[] bytes)
        {
            Mode = bytes.Length > 0 ? bytes[0] : (byte)0;
        }

        public void Write(byte[] bytes)
        {
            using (var io = new BinaryWriter(new MemoryStream(bytes))) io.Write(Mode);
        }
    }
}
