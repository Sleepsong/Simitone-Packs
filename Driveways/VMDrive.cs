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
                    return VMPrimitiveExitCode.GOTO_TRUE;
                case Shared.ModeInitCar:
                    InitObject(context.Caller, false);
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

        private static bool IsDriveway(VMEntity obj) => GUIDOf(obj) == Shared.DrivewayGUID || GUIDOf(obj) == Shared.DrivewayLargeGUID;

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
                if (!(obj is VMGameObject) || !(IsDriveway(obj) || GUIDOf(obj) == Shared.CarGUID)) continue;
                var flags = (VMMovementFlags)obj.GetValue(VMStackObjectVariable.MovementFlags);
                if ((flags & Movable) != Movable) obj.SetValue(VMStackObjectVariable.MovementFlags, (short)(flags | Movable));
            }
        }

        private static uint GUIDOf(VMEntity obj) => (obj.MasterDefinition ?? obj.Object.OBJ).GUID;

        private static VMMultitileGroup CarOf(VMEntity obj)
        {
            if (obj == null || !(obj is VMGameObject) || GUIDOf(obj) != Shared.CarGUID) return null;
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
            Roads.Add(vm, new RoadFound { Road = road, How = how });
            return road;
        }

        /// <summary>
        /// A driveway the player is holding or putting down goes against the strip by the road, pointing at it, as in
        /// The Sims 2: it follows the cursor along the road and nowhere else. Called before the game moves any object;
        /// changes where a driveway is being moved to. Only the player's own placing is touched, never a lot loading.
        /// </summary>
        public static void SnapDriveway(VMMultitileGroup group, ref LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags)
        {
            if ((flags & VMPlaceRequestFlags.UserBuildableLimit) == 0 || pos == LotTilePos.OUT_OF_WORLD || group.Objects.Count == 0) return;
            if (GUIDOf(group.Objects[0]) != Shared.DrivewayLargeGUID) return;
            var vm = context.VM;
            if (vm == null || !vm.TS1) return;
            var road = FindRoad(vm, out _);
            if (road == null) return;
            var arch = context.Architecture;
            var place = DrivewaySnap.Place(road.Value, pos.TileX, pos.TileY, Shared.DrivewayWidth, Shared.DrivewayLength, arch.Width, arch.Height);
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

            var cars = vm.Entities.Where(e => e is VMGameObject && GUIDOf(e) == Shared.CarGUID).Select(e => e.MultitileGroup).Distinct().ToList();
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

            var plan = DrivePlan.Make(pos.x / 16f, pos.y / 16f, heading, Shared.CarWidth, Shared.CarLength, road.Value, arch.Width, arch.Height);
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
