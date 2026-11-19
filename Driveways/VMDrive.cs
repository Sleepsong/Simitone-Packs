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
    /// The pack's one SimAntics primitive, and the rules that go with it. The cars and the driveway have scripts of an
    /// instruction or two that call it; what it does is chosen by the first byte of its operand (see Shared). Here
    /// too: where a driveway and a car may be put, and the floor a driveway lays.
    ///
    /// A drive changes nothing the game saves. The car stays where it is parked as far as the game's rules go (it
    /// still takes up its tiles), and the Sim stays standing beside it. Only what is drawn moves: the car's picture
    /// is taken along the route and then hidden, and the Sim's is hidden for the whole trip. Where the car is on its
    /// way is kept by the pack beside the save, and put back when the lot is loaded (VMDrive.Saving.cs).
    /// </summary>
    public partial class VMDrive : VMPrimitiveHandler
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

        private enum Phase { Out, Away, Back, Waiting }

        private class Trip
        {
            /// <summary>A drive to work: when the car has left the lot, the carpool is given the Sim.</summary>
            public bool ToWork;
            /// <summary>A drive to a community lot: when the car has left the lot, the game takes the Sim to this one.</summary>
            public short ToLot;
            public Carpool Carpool;
            public short SimID;
            public VMMultitileGroup Car;
            public DrivePlan Plan;
            public Phase Phase;
            public float Distance;
            public int Timer;
            public uint LastTick;
            /// <summary>How long the car has been waiting for someone to get out of its way.</summary>
            public int Waited;
            /// <summary>
            /// While waiting off the lot for the carpool, the Sim is out of the world, as the game keeps a Sim at work:
            /// where they stood by the car, to put them back if the car comes home with them.
            /// </summary>
            public LotTilePos? WaitingFrom;
            public Direction WaitingFacing;
        }

        //the trips under way on each lot, by the car's object id. kept beside the save by VMDrive.Saving
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
                        return (car != null && IsParked(context.VM, car)) ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE;
                    }
                case Shared.ModeCanDriveToWork:
                    {
                        try
                        {
                            var car = CarOf(context.StackObject);
                            var sim = context.Caller as VMAvatar;
                            if (car == null || sim == null || !IsParked(context.VM, car)) return VMPrimitiveExitCode.GOTO_FALSE;
                            //(each on its own: whatever goes wrong working out the hours must not hide the carpool's own offer)
                            bool early = false;
                            try { early = WorkWindow(context.VM, sim) != null; } catch (Exception) { }
                            return (early || CarpoolFor(context.VM, sim) != null) ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE;
                        }
                        catch (Exception) { return VMPrimitiveExitCode.GOTO_FALSE; }
                    }
                case Shared.ModePrepare:
                    try { return Prepare(context, operand.Arg) ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE; }
                    catch (Exception) { return VMPrimitiveExitCode.GOTO_FALSE; }
                case Shared.ModeIsLeftDoor:
                    {
                        //the door being used is the stack object (Prepare and Ride Home make it so): on the car's left if
                        //in its first column. the game's carpool chooses its animation the same way
                        var door = context.StackObject;
                        if (door != null && CarOf(door) != null && door.Object.OBJ.SubIndex != -1)
                            return (((ushort)door.Object.OBJ.SubIndex >> 8) == 0) ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE;
                        return Settings.DoorOnRight ? VMPrimitiveExitCode.GOTO_FALSE : VMPrimitiveExitCode.GOTO_TRUE;
                    }
                case Shared.ModeShow:
                    if (context.Caller is VMAvatar shown) Show(shown, true);
                    return VMPrimitiveExitCode.GOTO_TRUE;
                case Shared.ModeCanToggleAuto:
                    {
                        var car = CarOf(context.StackObject);
                        if (car == null || !(context.Caller is VMAvatar)) return VMPrimitiveExitCode.GOTO_FALSE;
                        if (context.Thread.IsCheck && context.Thread.ActionStrings != null)
                            context.Thread.ActionStrings.Add(new VMPieMenuInteraction { Name = IsAuto(car) ? "Stop Always Driving to Work" : "Always Drive to Work" });
                        return VMPrimitiveExitCode.GOTO_TRUE;
                    }
                case Shared.ModeToggleAuto:
                    {
                        var car = CarOf(context.StackObject);
                        if (car == null) return VMPrimitiveExitCode.GOTO_FALSE;
                        SetAuto(car, !IsAuto(car));
                        OnMessage?.Invoke(context.VM, IsAuto(car)
                            ? "Sims in this house with a job will drive to work in this car by themselves when their carpool comes, if they are awake and the car is free. Choose this again to stop."
                            : "Sims will take the carpool to work again, unless you choose Drive to Work.");
                        return VMPrimitiveExitCode.GOTO_TRUE;
                    }
                case Shared.ModeCanRideHome:
                    {
                        var sim = context.Caller as VMAvatar;
                        var commute = sim == null ? null : CommutesOf(context.VM).FirstOrDefault(c => c.SimGUID == sim.Object.OBJ.GUID && (c.State == CommuteState.Riding || c.State == CommuteState.Arrived));
                        return commute != null ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE;
                    }
                case Shared.ModeRideHome:
                    try { return RideHome(context); }
                    catch (Exception)
                    {
                        if (context.Caller is VMAvatar lost) Show(lost, true);
                        return VMPrimitiveExitCode.GOTO_FALSE;
                    }
                case Shared.ModeCanDriveToLot:
                    try
                    {
                        var car = CarOf(context.StackObject);
                        var sim = context.Caller as VMAvatar;
                        if (car == null || sim == null || !IsParked(context.VM, car)) return VMPrimitiveExitCode.GOTO_FALSE;
                        var places = Destinations(context.VM);
                        if (places.Count == 0) return VMPrimitiveExitCode.GOTO_FALSE;
                        //building the menu: one entry for each lot, in a menu for each part of town, as the game's own
                        //tests do (VMChangeActionString). the entry's Param0 is given to the action as its first argument
                        if (context.Thread.IsCheck && context.Thread.ActionStrings != null)
                            foreach (var place in places)
                                context.Thread.ActionStrings.Add(new VMPieMenuInteraction { Name = $"Drive to.../{place.Area}/{place.Name}", Param0 = place.House });
                        return VMPrimitiveExitCode.GOTO_TRUE;
                    }
                    catch (Exception) { return VMPrimitiveExitCode.GOTO_FALSE; }
                case Shared.ModeDrive:
                case Shared.ModeDriveToWork:
                case Shared.ModeDriveToLot:
                    try
                    {
                        var lot = (operand.Mode == Shared.ModeDriveToLot && context.Args != null && context.Args.Length > 0) ? context.Args[0] : (short)0;
                        return Drive(context, operand.Mode == Shared.ModeDriveToWork, lot);
                    }
                    catch (Exception)
                    {
                        //whatever went wrong, do not leave a car or a Sim hidden
                        AbandonTrip(context);
                        return VMPrimitiveExitCode.GOTO_FALSE;
                    }
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

        //(these are asked every time the game moves any object, so they are kept quick)
        private static readonly HashSet<uint> DrivewayGUIDs = new HashSet<uint>(Shared.OldDriveways.Select(o => o.GUID).Concat(new[] { Shared.DrivewayGUID, Shared.OldTileGUID }));
        private static readonly HashSet<uint> CarGUIDs = new HashSet<uint>(Shared.Cars.Select(c => c.GUID));
        public static bool IsDrivewayGUID(uint guid) => DrivewayGUIDs.Contains(guid);
        public static bool IsCarGUID(uint guid) => CarGUIDs.Contains(guid);
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

        /// <summary>
        /// The pack's primitive was added under another number than the object file's scripts call (another pack had
        /// that one): change every step of the file's scripts that calls the old number to call the new one, in memory,
        /// and turn any script the engine has already made runnable into a routine again. Returns how many steps changed.
        /// </summary>
        public static int Renumber(ushort from, ushort to)
        {
            int changed = 0;
            try
            {
                var objects = FSO.Content.Content.Get()?.WorldObjects;
                if (objects == null) return 0;
                var guids = new List<uint> { Shared.DrivewayGUID, Shared.OldTileGUID, Shared.OldCarGUID };
                guids.AddRange(Shared.Cars.Select(c => c.GUID));
                guids.AddRange(Shared.OldDriveways.Select(d => d.GUID));
                var done = new HashSet<FSO.Files.Formats.IFF.IffFile>();
                foreach (var guid in guids)
                {
                    FSO.Content.GameObjectResource res = null;
                    try { res = objects.Get(guid)?.Resource; } catch (Exception) { }
                    var iff = res?.MainIff;
                    if (iff == null || !done.Add(iff)) continue; //(all the pack's objects are in one file)
                    changed += Renumber(iff, from, to, res);
                }
            }
            catch (Exception) { }
            return changed;
        }

        /// <summary>The same for one object file: and if it has been made runnable (res), its changed scripts again.</summary>
        public static int Renumber(FSO.Files.Formats.IFF.IffFile iff, ushort from, ushort to, FSO.Content.GameIffResource res = null)
        {
            int changed = 0;
            foreach (var bhav in iff.List<FSO.Files.Formats.IFF.Chunks.BHAV>() ?? new List<FSO.Files.Formats.IFF.Chunks.BHAV>())
            {
                bool any = false;
                foreach (var step in bhav.Instructions)
                {
                    if (step.Opcode != from) continue;
                    step.Opcode = to;
                    any = true;
                    changed++;
                }
                if (any && res?.RoutineCache != null && FSO.Content.GameIffResource.BHAVAssembler != null)
                    res.RoutineCache[bhav.ChunkID] = FSO.Content.GameIffResource.BHAVAssembler(bhav, res);
            }
            return changed;
        }

        private static uint GUIDOf(VMEntity obj) => (obj.MasterDefinition ?? obj.Object.OBJ).GUID;

        private static VMMultitileGroup CarOf(VMEntity obj)
        {
            if (obj == null || !(obj is VMGameObject) || !IsCarGUID(GUIDOf(obj))) return null;
            return obj.MultitileGroup;
        }

        // ---- a drive ----

        /// <summary>End the trip this script is running, if any, and show the car and the Sim again.</summary>
        private static void AbandonTrip(VMStackFrame context)
        {
            try
            {
                var car = CarOf(context.StackObject);
                if (car != null && car.Objects.Count > 0)
                {
                    var trips = TripsOf(context.VM);
                    trips.TryGetValue(car.BaseObject.ObjectID, out var trip);
                    if (trip?.Carpool != null) UnhideCar(context.VM, trip.Carpool.Car);
                    trips.Remove(car.BaseObject.ObjectID);
                    Park(car);
                    if (trip != null && context.Caller is VMAvatar away) BackInTheWorld(context.VM, away, trip);
                    else if (context.Caller is VMAvatar left && left.Position == LotTilePos.OUT_OF_WORLD && context.Locals?.Length > 0 && context.Locals[0] == 1) PlaceBeside(context.VM, left, car);
                }
                if (context.Caller is VMAvatar sim) Show(sim, true);
            }
            catch (Exception) { }
        }

        private static bool IsParked(VM vm, VMMultitileGroup car)
        {
            var id = car.BaseObject.ObjectID;
            return !TripsOf(vm).ContainsKey(id) && !CommutesOf(vm).Any(c => c.Car == car);
        }

        private static VMPrimitiveExitCode Drive(VMStackFrame context, bool toWork, short toLot)
        {
            var vm = context.VM;
            var sim = context.Caller as VMAvatar;
            var car = CarOf(context.StackObject);
            if (sim == null || car == null || car.Objects.Count == 0) return VMPrimitiveExitCode.GOTO_FALSE;

            var trips = TripsOf(vm);
            var id = car.BaseObject.ObjectID;
            var started = context.Locals != null && context.Locals.Length > 0 && context.Locals[0] == 1;

            //(the lot was loaded mid-drive: what was saved carries on, once it has been put back)
            if (!trips.ContainsKey(id) && started) RestoreSaved(vm);
            if (!trips.TryGetValue(id, out var trip))
            {
                if (started)
                {
                    //this script was already driving, but the trip is gone: it was ended for being stale, or the lot
                    //was loaded mid-drive and the trip could not be kept. either way it is over. (a Sim who was waiting
                    //out of the world for the carpool is put back by the car)
                    Park(car);
                    if (sim.Position == LotTilePos.OUT_OF_WORLD) PlaceBeside(vm, sim, car);
                    Show(sim, true);
                    return VMPrimitiveExitCode.GOTO_TRUE;
                }

                if (!IsParked(vm, car)) return VMPrimitiveExitCode.GOTO_FALSE;
                var plan = PlanFor(vm, car);
                if (plan.Problem != RouteProblem.None)
                {
                    OnMessage?.Invoke(vm, Explain(plan.Problem));
                    return VMPrimitiveExitCode.GOTO_FALSE;
                }
                if (toLot != 0 && !Destinations(vm).Any(d => d.House == toLot))
                {
                    OnMessage?.Invoke(vm, "The car cannot take them there.");
                    return VMPrimitiveExitCode.GOTO_FALSE;
                }
                Carpool carpool = null;
                if (toWork)
                {
                    carpool = CarpoolFor(vm, sim);
                    if (carpool == null && WorkWindow(vm, sim) == null)
                    {
                        OnMessage?.Invoke(vm, $"It is not time for {sim.Name} to go to work. They can drive there from when the game says the carpool will come, an hour before it does.");
                        return VMPrimitiveExitCode.GOTO_FALSE;
                    }
                    //the carpool is not wanted: the Sim's own car takes them. (it is still what takes them, out of sight)
                    if (carpool != null) HideCar(vm, carpool.Car);
                }
                trip = new Trip { SimID = sim.ObjectID, Car = car, Plan = plan, Phase = Phase.Out, ToWork = toWork, Carpool = carpool, ToLot = toLot };
                trips[id] = trip;
                if (context.Locals != null && context.Locals.Length > 0) context.Locals[0] = 1;
                Show(sim, false);
                PlayEngine(car);
            }
            else if (trip.SimID != sim.ObjectID)
            {
                return VMPrimitiveExitCode.GOTO_FALSE; //somebody else has the car
            }

            trip.LastTick = vm.Scheduler.CurrentTickID;

            switch (trip.Phase)
            {
                case Phase.Out:
                    if (Blocked(vm, car, trip.Plan.Out, trip.Distance, sim.ObjectID, ref trip.Waited)) break;
                    trip.Distance += trip.Plan.Out.SpeedAt(trip.Distance);
                    if (trip.Distance >= trip.Plan.Out.Length && trip.ToLot != 0)
                    {
                        Show(car, false);
                        if (!sim.GetFlag(VMEntityFlags.InteractionCanceled))
                        {
                            trips.Remove(id);
                            return Travel(context, trip, sim);
                        }
                        //cancelled: come straight home
                        trip.ToLot = 0;
                        trip.Phase = Phase.Back;
                        trip.Distance = 0;
                        Draw(car, trip.Plan.Back.PoseAt(0));
                        Show(car, true);
                        PlayEngine(car);
                    }
                    else if (trip.Distance >= trip.Plan.Out.Length && trip.ToWork)
                    {
                        Show(car, false);
                        //(off the lot. early, before the carpool: wait for it out of the world, as the game keeps a Sim at work)
                        trip.Phase = Phase.Waiting;
                        if (sim.Position != LotTilePos.OUT_OF_WORLD)
                        {
                            trip.WaitingFrom = sim.Position;
                            trip.WaitingFacing = sim.Direction;
                            sim.SetPosition(LotTilePos.OUT_OF_WORLD, sim.Direction, vm.Context);
                        }
                        goto case Phase.Waiting;
                    }
                    else if (trip.Distance >= trip.Plan.Out.Length)
                    {
                        Show(car, false);
                        trip.Phase = Phase.Away;
                        //(cancelled on the way out: it turns straight round)
                        trip.Timer = sim.GetFlag(VMEntityFlags.InteractionCanceled) ? 1 : AwayTicks;
                    }
                    else Draw(car, trip.Plan.Out.PoseAt(trip.Distance));
                    break;

                case Phase.Waiting:
                    {
                        //the car is away with the Sim, who is to be handed to the carpool as soon as it comes for them
                        string why = null;
                        if (sim.GetFlag(VMEntityFlags.InteractionCanceled)) why = "";
                        else
                        {
                            if (trip.Carpool == null || trip.Carpool.Car.Objects.Any(o => o.Dead))
                            {
                                trip.Carpool = CarpoolFor(vm, sim);
                                if (trip.Carpool != null) HideCar(vm, trip.Carpool.Car);
                            }
                            if (trip.Carpool != null)
                            {
                                //back into the world (still hidden) beside the carpool, for the game to put them in it
                                PutBeside(vm, sim, trip.Carpool.Car, trip);
                                if (HandOver(vm, trip, sim))
                                {
                                    trips.Remove(id);
                                    return VMPrimitiveExitCode.GOTO_FALSE; //(gone: the Sim stays hidden, and the carpool takes them from here)
                                }
                                why = $"The game would not take {sim.Name} to work just now.";
                            }
                            else
                            {
                                //(the carpool is made on the hour, an hour before work: half an hour before, it is not coming)
                                var untilStart = MinutesUntilWork(vm, sim);
                                if (untilStart < 0 || untilStart <= 30 || untilStart > 150) why = $"No carpool came for {sim.Name}, so there was no work to drive to today.";
                            }
                        }
                        if (why == null) break; //(still waiting)
                        //cancelled, or no work after all: come straight home, and let the carpool be seen again
                        if (trip.Carpool != null) UnhideCar(vm, trip.Carpool.Car);
                        BackInTheWorld(vm, sim, trip);
                        trip.Carpool = null;
                        trip.ToWork = false;
                        trip.Phase = Phase.Back;
                        trip.Distance = 0;
                        Draw(car, trip.Plan.Back.PoseAt(0));
                        Show(car, true);
                        PlayEngine(car);
                        if (why != "") OnMessage?.Invoke(vm, why);
                        break;
                    }

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
                        PlayEngine(car);
                    }
                    break;

                case Phase.Back:
                    if (Blocked(vm, car, trip.Plan.Back, trip.Distance, sim.ObjectID, ref trip.Waited)) break;
                    trip.Distance += trip.Plan.Back.SpeedAt(trip.Distance);
                    if (trip.Distance >= trip.Plan.Back.Length)
                    {
                        trips.Remove(id);
                        Park(car);
                        BackInTheWorld(vm, sim, trip);
                        if (sim.Position == LotTilePos.OUT_OF_WORLD) PlaceBeside(vm, sim, car); //(home: never left out of the world)
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
                if (trip.Carpool != null) UnhideCar(vm, trip.Carpool.Car);
                Park(trip.Car);
                var sim = vm.GetObjectById(trip.SimID) as VMAvatar;
                if (sim != null) { BackInTheWorld(vm, sim, trip); Show(sim, true); }
            }
        }

        // ---- driving to a community lot ----
        //
        // The game takes Sims to another lot with one call, ChangeToLotInTemp0 (generic call 17): the lot's number in
        // temp 0, and in global 34 whether the household goes too. The taxi makes it once its passengers are in. The
        // car makes the same call, from the Sim's own script, once it has driven off the lot with them: so the trip,
        // what they find there and how they get home are all the game's own. Simitone keeps the home lot as it was
        // when they left, and puts it back when they return, so the car is on the driveway when they get home.

        /// <summary>A lot the car can drive to: its number, the part of town it is in, and its name.</summary>
        private class Destination
        {
            public short House;
            public string Area, Name;
        }

        /// <summary>
        /// The parts of town with community lots, by their number in LotLocations.iff (as the neighbourhood screen numbers
        /// them). The vacation island (3) is left out: a family goes there for days, not for a drive. Houses are left out
        /// everywhere: the game cannot take a Sim visiting to one (it shows an empty lot). In the main neighbourhood (1)
        /// and Old Town (4), which are zoned lot by lot, only lots zoned for the community count.
        /// </summary>
        private static readonly (int Mode, string Area)[] Areas = { (1, "Around Town"), (2, "Downtown"), (4, "Old Town"), (5, "Studio Town"), (8, "Magic Town") };

        private static ConditionalWeakTable<VM, List<Destination>> AllDestinations = new ConditionalWeakTable<VM, List<Destination>>();

        /// <summary>The community lots in the player's game that the car can drive to from this lot, by part of town and name.</summary>
        private static List<Destination> Destinations(VM vm)
        {
            if (AllDestinations.TryGetValue(vm, out var known)) return known;
            var list = new List<Destination>();
            try
            {
                var hood = FSO.Content.Content.Get().Neighborhood;
                var here = vm.GetGlobalValue(10); //(the lot being played, as the game's taxi reads it)
                var seen = new HashSet<int>();
                foreach (var (mode, area) in Areas)
                {
                    var locations = hood.LotLocations?.Get<FSO.Files.Formats.IFF.Chunks.STR>((ushort)mode);
                    if (locations == null) continue;
                    var inArea = new List<Destination>();
                    for (int i = 0; i < locations.Length; i++)
                    {
                        var parts = (locations.GetString(i) ?? "").Split(',');
                        if (!int.TryParse(parts[0].Trim(), out var num) || num <= 0 || num == 99 || num == here || !seen.Add(num)) continue;
                        //a house is not a destination, wherever it is: a lot zoned as one, or one a family lives in. the
                        //neighbourhood and Old Town are zoned lot by lot, so there a lot not zoned for the community is a house
                        var zoned = hood.ZoningDictionary.TryGetValue((short)num, out var zone);
                        var lived = hood.FamilyForHouse != null && hood.FamilyForHouse.ContainsKey((short)num);
                        if (!Driveways.Destinations.IsCommunityLot(mode, zoned, zone, lived)) continue;
                        if (!File.Exists(hood.GetHousePath(num))) continue;
                        string name = null;
                        try { name = hood.GetHouseNameDesc(num)?.Item1?.Trim(); } catch (Exception) { }
                        //(a menu entry's name is split at slashes into menus)
                        name = string.IsNullOrEmpty(name) ? $"Lot {num}" : name.Replace('/', '-');
                        inArea.Add(new Destination { House = (short)num, Area = area, Name = name });
                    }
                    list.AddRange(inArea.OrderBy(d => d.Name));
                }
            }
            catch (Exception) { }
            AllDestinations.AddOrUpdate(vm, list);
            return list;
        }

        /// <summary>
        /// The car has left the lot with the Sim: have the game take them to the lot, as its taxi does. Only the Sim who
        /// drove goes (global 34 is the game's own say in that, 0 for one Sim: whatever a past trip left there would
        /// otherwise decide). In case the game does not go, the car and the Sim are watched for a moment.
        /// </summary>
        private static VMPrimitiveExitCode Travel(VMStackFrame context, Trip trip, VMAvatar sim)
        {
            var vm = context.VM;
            CommutesOf(vm).Add(new Commute
            {
                Car = trip.Car, Plan = trip.Plan, SimGUID = sim.Object.OBJ.GUID, SimName = sim.Name, State = CommuteState.Departing,
                Destination = Destinations(vm).FirstOrDefault(d => d.House == trip.ToLot)?.Name
            });
            //(the lot is about to go: let go of the car's tiles)
            foreach (var part in trip.Car.Objects) if (part.WorldUI is ObjectComponent picture) ClearFreeAngle(picture);
            vm.SetGlobalValue(34, 0);
            context.Thread.TempRegisters[0] = trip.ToLot;
            new FSO.SimAntics.Primitives.VMGenericTS1Call().Execute(context,
                new FSO.SimAntics.Primitives.VMGenericTS1CallOperand { Call = FSO.SimAntics.Model.VMGenericTS1CallMode.ChangeToLotInTemp0 });
            return VMPrimitiveExitCode.GOTO_FALSE; //(gone: no getting out)
        }

        // ---- driving to work ----
        //
        // The game sends a Sim to work with the lot's car portal (CarPortal.iff), an object at the road's edge. Two hours
        // before work it says the carpool will come in about an hour; an hour before work it makes the carpool, which
        // waits at the kerb. The carpool's "Go to Work" gives the Sim the portal's "At Work": the Sim gets into the
        // carpool, is sent out of the world for the day, is paid, and at the end of the day the portal makes another
        // carpool at the kerb, runs its drop-off, puts the Sim back in the world beside it, and shows them again.
        //
        // Driving to work borrows all of that, so pay, performance and the hours are the game's own, and only what is
        // seen changes. From when the game says the carpool will come, a Sim's car offers "Drive to Work". They drive
        // off; when the carpool comes (or at once, if it is already there) it is hidden, and the Sim, still hidden, is
        // given its "Go to Work". When the portal puts them back in the world at the end of the day, the carpool that
        // brought them is hidden as well, and so are they: their car drives in, and they get out of it.

        /// <summary>The shared script of the game's cars that sends a Sim to work (carglobals.iff, "go to work").</summary>
        private const ushort GoToWorkAction = 8195;
        /// <summary>The lot's car portals: what the carpool hands a Sim to, to take them off the lot.</summary>
        private static readonly HashSet<uint> CarPortals = new HashSet<uint> { 0x865A6812, 0xD564C66B };
        /// <summary>The car's own interactions, as the object file numbers them.</summary>
        private const int RideHomeInteraction = 2;

        /// <summary>
        /// Whether an object is one of the game's cars: one that shares the cars' scripts (carglobals.iff). An action
        /// numbered 8192 or more is one of an object's shared scripts, so 8195 is "go to work" only for these.
        /// </summary>
        private static bool IsGameCar(VMEntity obj)
        {
            var shared = (obj.Object.Resource as FSO.Content.GameObjectResource)?.SemiGlobal?.MainIff?.Filename;
            return shared != null && shared.Equals("carglobals.iff", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A carpool that has come for a Sim: the car, its tile that offers "Go to Work", and that interaction.</summary>
        private class Carpool
        {
            public VMMultitileGroup Car;
            public VMEntity Tile;
            public int Interaction;
        }

        /// <summary>The game's carpool that has come for this Sim, by the game's own test: null if none has.</summary>
        private static Carpool CarpoolFor(VM vm, VMAvatar sim)
        {
            foreach (var obj in vm.Entities)
            {
                if (!(obj is VMGameObject) || obj.Dead || obj.GhostImage || obj.TreeTable == null || obj.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (!obj.TreeTable.Interactions.Any(i => i.ActionFunction == GoToWorkAction) || !IsGameCar(obj)) continue;
                foreach (var action in obj.TreeTable.Interactions)
                {
                    if (action.ActionFunction != GoToWorkAction) continue;
                    var allowed = obj.GetPieMenuForInteraction(vm, sim, (int)action.TTAIndex, false, true);
                    if (allowed != null && allowed.Count > 0) return new Carpool { Car = obj.MultitileGroup, Tile = obj, Interaction = (int)action.TTAIndex };
                }
            }
            return null;
        }

        /// <summary>
        /// If it is the time when a Sim may set off for work before their carpool comes, the hour their work starts;
        /// otherwise null. That is from when the game says the carpool will come in about an hour (two hours before
        /// work) until it does, on a day the Sim is due at work: worked out as the car portal works it out, from the
        /// job's start hour (job data 12) and the day they last went (person data 69: -1 for none yet, a job taken today).
        /// </summary>
        private static int? WorkWindow(VM vm, VMAvatar sim)
        {
            var untilStart = MinutesUntilWork(vm, sim);
            return (untilStart > 60 && untilStart <= 120) ? untilStart : (int?)null;
        }

        /// <summary>Game minutes until the Sim's work starts today (0 to 1439), or -1 if they are not due at work today.</summary>
        private static int MinutesUntilWork(VM vm, VMAvatar sim) => WorkHours(vm, sim, out _);

        /// <summary>
        /// The same, with why not when it is -1. The hour, the minute and the day are read as the game's scripts read
        /// them (its globals 0, 5 and 1), which come from the lot's clock: VM.GlobalState holds them only as they were
        /// when the lot was loaded.
        /// </summary>
        private static int WorkHours(VM vm, VMAvatar sim, out string why)
        {
            why = null;
            var job = sim.GetPersonData(VMPersonDataVariable.JobType);
            if (job <= 0) { why = "has no job"; return -1; }
            var last = sim.GetPersonData(VMPersonDataVariable.TS1LastDayAtWork);
            var today = vm.GetGlobalValue(1);
            if (last == -1) { why = "is not due at work today (a job taken today)"; return -1; }
            if (last == today) { why = "has been to work today"; return -1; }
            int start;
            try { start = FSO.Content.Content.Get().Jobs.GetJobData((ushort)job, sim.GetPersonData(VMPersonDataVariable.JobPromotionLevel), 12); }
            catch (Exception) { why = "has a job the game's job list does not have"; return -1; }
            if (start < 0 || start > 23) { why = "has a job with no start hour"; return -1; }
            int now = vm.GetGlobalValue(0) * 60 + vm.GetGlobalValue(5);
            return ((start * 60 - now) % 1440 + 1440) % 1440;
        }

        /// <summary>For the "driveways" report: when a Sim can drive to work, and if not now, why not.</summary>
        private static string DescribeWork(VM vm, VMAvatar sim)
        {
            var until = WorkHours(vm, sim, out var why);
            if (until < 0) return $"{sim.Name} {why}.";
            var start = (vm.GetGlobalValue(0) * 60 + vm.GetGlobalValue(5) + until) % 1440;
            var when = $"{sim.Name} starts work at {start / 60}:{start % 60:00}, in {until / 60}h {until % 60:00}m";
            bool carpool = false;
            try { carpool = CarpoolFor(vm, sim) != null; } catch (Exception) { }
            if (carpool) return when + ". Their carpool is here: they can drive to work now.";
            if (until > 60 && until <= 120) return when + ". They can drive to work now, before the carpool comes.";
            if (until > 120) return when + $". They can drive to work in {(until - 120) / 60}h {(until - 120) % 60:00}m, when the game says the carpool will come.";
            return when + ". Their carpool is not here, so they cannot drive to work now.";
        }

        private enum CommuteState { GettingIn, AtWork, Riding, Arrived, Home, Departing }

        /// <summary>A Sim gone to work in their own car: the car is away until they are back.</summary>
        private class Commute
        {
            public VMMultitileGroup Car;
            public DrivePlan Plan;
            public Carpool Carpool;
            public uint SimGUID;
            public string SimName;
            public CommuteState State;
            public int Ticks;      //how long it has been in its state, or doing nothing to do with the carpool
            public float Distance; //on the way home
            public HashSet<VMMultitileGroup> CarsWhenGone; //the game's cars on the lot when the Sim had gone to work
            public int Waited;     //how long the car has been waiting for someone to get out of its way
            public string Destination; //for a drive to a community lot: its name
        }

        private static ConditionalWeakTable<VM, List<Commute>> AllCommutes = new ConditionalWeakTable<VM, List<Commute>>();
        private static List<Commute> CommutesOf(VM vm) => AllCommutes.GetOrCreateValue(vm);

        /// <summary>
        /// The game's cars the pack is keeping out of sight: the carpool that takes a Sim who drove to work, and the
        /// one that brings them back. Each is hidden every tick until it has gone. Only its picture: which ones are kept beside the save.
        /// </summary>
        private static ConditionalWeakTable<VM, HashSet<VMMultitileGroup>> AllHiddenCars = new ConditionalWeakTable<VM, HashSet<VMMultitileGroup>>();

        private static void HideCar(VM vm, VMMultitileGroup car)
        {
            if (car == null) return;
            AllHiddenCars.GetOrCreateValue(vm).Add(car);
            ShowPictures(car, false);
        }

        private static void UnhideCar(VM vm, VMMultitileGroup car)
        {
            if (car == null) return;
            AllHiddenCars.GetOrCreateValue(vm).Remove(car);
            if (!car.Objects.Any(o => o.Dead)) ShowPictures(car, true);
        }

        /// <summary>
        /// The car has left the lot with a Sim on their way to work, and the carpool is here: give the Sim its own
        /// "Go to Work", which takes them from here. False if the carpool will not have them.
        /// </summary>
        private static bool HandOver(VM vm, Trip trip, VMAvatar sim)
        {
            var carpool = trip.Carpool;
            if (carpool == null || carpool.Car.Objects.Any(o => o.Dead) || carpool.Tile.Position == LotTilePos.OUT_OF_WORLD) return false;
            var allowed = carpool.Tile.GetPieMenuForInteraction(vm, sim, carpool.Interaction, false, true);
            if (allowed == null || allowed.Count == 0) return false;
            carpool.Tile.PushUserInteraction(carpool.Interaction, sim, vm.Context, false);
            CommutesOf(vm).Add(new Commute
            {
                Car = trip.Car, Plan = trip.Plan, Carpool = carpool,
                SimGUID = sim.Object.OBJ.GUID, SimName = sim.Name, State = CommuteState.GettingIn
            });
            return true;
        }

        /// <summary>The Sim with this character on the lot, if they are here at all (in the world or out of it).</summary>
        private static VMAvatar SimOnLot(VM vm, uint guid) =>
            vm.Entities.FirstOrDefault(e => e is VMAvatar && !e.Dead && !e.GhostImage && e.Object.OBJ.GUID == guid) as VMAvatar;

        /// <summary>Whether a Sim is busy with the carpool or a car portal: going to work, at work, or being brought back.</summary>
        private static bool WithTheCarpool(VMAvatar sim, Commute commute)
        {
            var queue = sim.Thread?.Queue;
            if (queue == null) return false;
            foreach (var action in queue)
            {
                var callee = action?.Callee;
                if (callee == null) continue;
                if ((commute.Carpool != null && callee.MultitileGroup == commute.Carpool.Car) || CarPortals.Contains(GUIDOf(callee))) return true;
                if (callee is VMGameObject && IsGameCar(callee)) return true;
            }
            return false;
        }

        /// <summary>
        /// Every tick: keep the game's cars that should not be seen out of sight, and follow each Sim who drove to
        /// work, from getting into the carpool to getting out of their own car at home.
        /// </summary>
        public static void TickCommutes(VM vm)
        {
            if (AllHiddenCars.TryGetValue(vm, out var hidden) && hidden.Count > 0)
            {
                hidden.RemoveWhere(c => c.Objects.Count == 0 || c.Objects.Any(o => o.Dead));
                foreach (var car in hidden) ShowPictures(car, false);
            }
            if (vm.Scheduler.CurrentTickID % 30 == 0)
            {
                try { AutoDrive(vm); } catch (Exception) { }
            }
            if (!AllCommutes.TryGetValue(vm, out var commutes) || commutes.Count == 0) return;
            foreach (var commute in commutes.ToList())
            {
                if (commute.Car.Objects.Count == 0 || commute.Car.Objects.Any(o => o.Dead)) { commutes.Remove(commute); continue; }
                var sim = SimOnLot(vm, commute.SimGUID);
                switch (commute.State)
                {
                    case CommuteState.GettingIn:
                        //gone once they are out of the world (or off the lot altogether)
                        if (sim == null || sim.Position == LotTilePos.OUT_OF_WORLD)
                        {
                            commute.State = CommuteState.AtWork;
                            commute.CarsWhenGone = new HashSet<VMMultitileGroup>(GameCars(vm));
                            break;
                        }
                        if (WithTheCarpool(sim, commute)) { commute.Ticks = 0; break; }
                        if (++commute.Ticks > 90)
                        {
                            //three seconds in the world doing nothing to do with the carpool: it did not take them
                            Show(sim, true);
                            if (commute.Carpool != null) UnhideCar(vm, commute.Carpool.Car);
                            StartHome(commute, CommuteState.Home);
                            OnMessage?.Invoke(vm, $"{commute.SimName} could not be taken to work from the car. They are back on the lot: send them in the carpool instead.");
                        }
                        break;

                    case CommuteState.AtWork:
                        //back when the car portal puts them back in the world, beside the carpool it has made to bring them
                        if (sim == null || sim.Position == LotTilePos.OUT_OF_WORLD) break;
                        Show(sim, false);
                        //the carpool that brought them: one of the game's cars that was not here when they went, made by the
                        //car portal beside itself. (near them or near the portal; failing that, the newest)
                        var fresh = GameCars(vm).Where(c => commute.CarsWhenGone == null || !commute.CarsWhenGone.Contains(c)).ToList();
                        var near = new List<LotTilePos> { sim.Position };
                        near.AddRange(vm.Entities.Where(e => !e.Dead && CarPortals.Contains(GUIDOf(e)) && e.Position != LotTilePos.OUT_OF_WORLD).Select(e => e.Position));
                        var brought = fresh.Where(c => c.Objects.Any(o => o.Position != LotTilePos.OUT_OF_WORLD
                            && near.Any(p => Math.Abs(o.Position.TileX - p.TileX) + Math.Abs(o.Position.TileY - p.TileY) <= 8))).ToList();
                        if (brought.Count == 0 && fresh.Count > 0) brought.Add(fresh.OrderByDescending(c => c.BaseObject.ObjectID).First());
                        foreach (var car in brought) HideCar(vm, car);
                        StartHome(commute, CommuteState.Riding);
                        //hold them while the car brings them in: queued after what the portal is still doing with them
                        commute.Car.BaseObject.PushUserInteraction(RideHomeInteraction, sim, vm.Context, false);
                        break;

                    case CommuteState.Riding:
                    case CommuteState.Home:
                        if (sim != null && commute.State == CommuteState.Riding) Show(sim, false); //(the portal shows them as it puts them back)
                        if (Blocked(vm, commute.Car, commute.Plan.Back, commute.Distance, sim?.ObjectID ?? 0, ref commute.Waited)) break;
                        commute.Distance += commute.Plan.Back.SpeedAt(commute.Distance);
                        if (commute.Distance < commute.Plan.Back.Length) { Draw(commute.Car, commute.Plan.Back.PoseAt(commute.Distance)); break; }
                        Park(commute.Car);
                        if (commute.State == CommuteState.Home) { commutes.Remove(commute); break; }
                        commute.State = CommuteState.Arrived;
                        commute.Ticks = 0;
                        break;

                    case CommuteState.Departing:
                        //the game changes lots at once, and this lot stops. still here two seconds later: it did not go
                        if (++commute.Ticks <= 60) break;
                        if (sim != null) Show(sim, true);
                        StartHome(commute, CommuteState.Home);
                        OnMessage?.Invoke(vm, $"The game did not take {commute.SimName} to {commute.Destination ?? "the lot"}, so they have come home.");
                        break;

                    case CommuteState.Arrived:
                        //the Sim's "Ride Home" lets them out. if it has not within ten seconds, let them out here
                        if (sim == null) { commutes.Remove(commute); break; }
                        Show(sim, false);
                        if (++commute.Ticks > 300)
                        {
                            LetOut(vm, sim, commute);
                            commutes.Remove(commute);
                        }
                        break;
                }
            }
        }

        private static IEnumerable<VMMultitileGroup> GameCars(VM vm) =>
            vm.Entities.Where(e => e is VMGameObject && !e.Dead && !e.GhostImage && IsGameCar(e)).Select(e => e.MultitileGroup).Distinct().ToList();

        private static void StartHome(Commute commute, CommuteState state)
        {
            commute.State = state;
            commute.Distance = 0;
            Draw(commute.Car, commute.Plan.Back.PoseAt(0));
            Show(commute.Car, true);
            PlayEngine(commute.Car);
        }

        /// <summary>The Sim's "Ride Home": waits while their car brings them in, then lets them out beside it.</summary>
        private static VMPrimitiveExitCode RideHome(VMStackFrame context)
        {
            var vm = context.VM;
            var sim = context.Caller as VMAvatar;
            if (sim == null) return VMPrimitiveExitCode.GOTO_FALSE;
            var commutes = CommutesOf(vm);
            var commute = commutes.FirstOrDefault(c => c.SimGUID == sim.Object.OBJ.GUID && (c.State == CommuteState.Riding || c.State == CommuteState.Arrived));
            //(false: no getting out of the car)
            if (commute == null) { Show(sim, true); return VMPrimitiveExitCode.GOTO_FALSE; }
            if (commute.State == CommuteState.Riding)
            {
                if (!sim.GetFlag(VMEntityFlags.InteractionCanceled)) return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                //cancelled: they walk home from the kerb, and the car comes in on its own
                commute.State = CommuteState.Home;
                Show(sim, true);
                return VMPrimitiveExitCode.GOTO_FALSE;
            }
            //beside the door, still hidden: the script takes them to the door's place, shows them, and they get out
            LetOut(vm, sim, commute, false);
            commutes.Remove(commute);
            var door = DoorTile(commute.Car, sim);
            if (door != null) context.StackObject = door;
            return VMPrimitiveExitCode.GOTO_TRUE;
        }

        /// <summary>Put the Sim beside their car, which has just parked, and show them (unless the script will).</summary>
        private static void LetOut(VM vm, VMAvatar sim, Commute commute, bool show = true)
        {
            var context = vm.Context;
            var car = commute.Car;
            //beside the door, facing the way the car does, to get out as they got in
            var door = DoorTile(car, sim);
            var heading = HeadingOf(car.BaseObject.Direction);
            if (door != null && heading >= 0)
            {
                var left = ((ushort)door.Object.OBJ.SubIndex >> 8) == 0;
                var side = DrivePlan.Rotate(left ? -1 : 1, 0, heading);
                var spot = LotTilePos.FromBigTile((short)(door.Position.TileX + (int)Math.Round(side.X)), (short)(door.Position.TileY + (int)Math.Round(side.Y)), 1);
                if (sim.SetPosition(spot, car.BaseObject.Direction, context).Status == VMPlacementError.Success) { if (show) Show(sim, true); return; }
            }
            var carTiles = new HashSet<(int, int)>(car.Objects.Select(o => ((int)o.Position.TileX, (int)o.Position.TileY)));
            float cx = (float)carTiles.Average(t => t.Item1), cy = (float)carTiles.Average(t => t.Item2);
            //the tiles next to the car, nearest its middle first: on the driveway, or else anywhere beside it
            var around = new List<(int X, int Y)>();
            foreach (var (x, y) in carTiles)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (!carTiles.Contains((x + dx, y + dy)) && !around.Contains((x + dx, y + dy))) around.Add((x + dx, y + dy));
            foreach (var (x, y) in around.OrderBy(t => DrivewayAt(vm, t.X, t.Y) != 0 ? 0 : 1).ThenBy(t => Math.Abs(t.X - cx) + Math.Abs(t.Y - cy)))
            {
                if (x < 0 || y < 0 || x >= context.Architecture.Width || y >= context.Architecture.Height) continue;
                var result = sim.SetPosition(LotTilePos.FromBigTile((short)x, (short)y, 1), sim.Direction, context);
                if (result.Status == VMPlacementError.Success) break;
            }
            Show(sim, true);
        }

        /// <summary>Show or hide a car's pictures without touching anything the game saves.</summary>
        private static void ShowPictures(VMMultitileGroup car, bool visible)
        {
            if (!VM.UseWorld || car == null) return;
            foreach (var part in car.Objects)
                if (part.WorldUI is ObjectComponent picture && picture.Visible != visible) picture.Visible = visible;
        }

        // ---- getting in and out ----
        //
        // Before a drive the Sim walks to the car's door and gets in, and at the end gets out, with the game's own
        // animations for getting into its cars (A2O-car-getinl and -getinr, named in the object file's table 129 as the
        // carpool names them; getting out is getting in played backwards). The scripts that do it are in the object
        // file; here is what they ask: whether the drive can go, and where the door is.

        /// <summary>The door's row when the game's car the model is drawn as is not there to say.</summary>
        private const int DoorRow = 2;

        /// <summary>
        /// The car's tile by the door a Sim uses. The game's cars mark theirs: the tiles whose init script is "init route
        /// piece", which the carpool sends a Sim to (global routing slot 4) and has them get in at. Of the game's car this
        /// one is drawn as, the door on the side the player chose ("driveways door": the left unless they changed it),
        /// the nearest the Sim if there are two; on the other side if there is none on that one.
        /// </summary>
        private static VMEntity DoorTile(VMMultitileGroup car, VMEntity near = null)
        {
            if (car == null) return null;
            int side = Settings.DoorOnRight ? Shared.CarWidth - 1 : 0;
            var doors = CarLook.DoorsOf(GUIDOf(car.BaseObject));
            var places = doors.Where(d => d.X == side).ToList();
            if (places.Count == 0) places = doors.ToList();
            if (places.Count == 0) places.Add((side, DoorRow));
            var tiles = places.Select(p => car.Objects.FirstOrDefault(o => !o.Dead && o.Object.OBJ.SubIndex == (short)((p.X << 8) | p.Y))).Where(o => o != null).ToList();
            if (tiles.Count == 0) return null;
            if (near == null || near.Position == LotTilePos.OUT_OF_WORLD) return tiles[0];
            return tiles.OrderBy(t => Math.Abs(t.Position.x - near.Position.x) + Math.Abs(t.Position.y - near.Position.y)).First();
        }

        /// <summary>
        /// Before the Sim walks to the car: whether this drive can go, and if not, why (so they do not walk over and get
        /// in for nothing). If it can, the stack object becomes the tile by the door, which the walk is relative to.
        /// </summary>
        private static bool Prepare(VMStackFrame context, byte mode)
        {
            var vm = context.VM;
            var sim = context.Caller as VMAvatar;
            var car = CarOf(context.StackObject);
            if (sim == null || car == null) return false;
            if (!IsParked(vm, car)) { OnMessage?.Invoke(vm, "The car is out."); return false; }
            var plan = PlanFor(vm, car);
            if (plan.Problem != RouteProblem.None) { OnMessage?.Invoke(vm, Explain(plan.Problem)); return false; }
            if (mode == Shared.ModeDriveToWork)
            {
                bool early = false;
                try { early = WorkWindow(vm, sim) != null; } catch (Exception) { }
                if (!early && CarpoolFor(vm, sim) == null)
                {
                    OnMessage?.Invoke(vm, $"It is not time for {sim.Name} to go to work. They can drive there from when the game says the carpool will come, an hour before it does.");
                    return false;
                }
            }
            if (mode == Shared.ModeDriveToLot)
            {
                var lot = (context.Args != null && context.Args.Length > 0) ? context.Args[0] : (short)0;
                if (!Destinations(vm).Any(d => d.House == lot)) { OnMessage?.Invoke(vm, "The car cannot take them there."); return false; }
            }
            var door = DoorTile(car, sim);
            if (door != null) context.StackObject = door;
            return true;
        }

        // ---- driving to work by themselves ----
        //
        // A car can be set to "Always Drive to Work": then, when the game's carpool comes for one of the household with a
        // job, who is awake and on the lot, the car's Drive to Work is queued for them, as if the player had chosen it.
        // Once a day for each Sim: if they cancel it, they take the carpool. The setting is kept on the car (its lead
        // tile's first attribute), so it is saved with the lot.

        private const int DriveToWorkInteraction = 1;

        private static bool IsAuto(VMMultitileGroup car) => car.BaseObject.GetAttribute(0) == 1;
        private static void SetAuto(VMMultitileGroup car, bool on) => car.BaseObject.SetAttribute(0, (short)(on ? 1 : 0));

        //the Sims a car has been queued for already, by character and day
        private static ConditionalWeakTable<VM, HashSet<(uint, short)>> AllAutoQueued = new ConditionalWeakTable<VM, HashSet<(uint, short)>>();

        private static void AutoDrive(VM vm)
        {
            var cars = vm.Entities.Where(e => e is VMGameObject && !e.Dead && !e.GhostImage && IsCarGUID(GUIDOf(e))).Select(e => e.MultitileGroup).Distinct()
                .Where(c => c.BaseObject.Position != LotTilePos.OUT_OF_WORLD && IsAuto(c) && IsParked(vm, c)).ToList();
            if (cars.Count == 0) return;
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return;
            var day = vm.GetGlobalValue(1);
            var queued = AllAutoQueued.GetOrCreateValue(vm);
            foreach (var sim in vm.Entities.OfType<VMAvatar>().ToList())
            {
                if (cars.Count == 0) break;
                if (sim.Dead || sim.GhostImage || sim.Position == LotTilePos.OUT_OF_WORLD || sim.GetValue(VMStackObjectVariable.Hidden) != 0) continue;
                if (sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != family.ChunkID) continue;
                if (sim.GetPersonData(VMPersonDataVariable.PersonsAge) < 18 || sim.GetPersonData(VMPersonDataVariable.JobType) <= 0) continue;
                if (sim.GetMotiveData(VMMotive.SleepState) != 0) continue; //(asleep: the game's carpool does not wake them either)
                var key = (sim.Object.OBJ.GUID, day);
                if (queued.Contains(key)) continue;
                if (TripsOf(vm).Values.Any(t => t.SimID == sim.ObjectID) || CommutesOf(vm).Any(c => c.SimGUID == sim.Object.OBJ.GUID)) continue;
                if (sim.Thread?.Queue?.Any(a => a?.Callee != null && IsCarGUID(GUIDOf(a.Callee))) == true) continue;
                if (CarpoolFor(vm, sim) == null) continue;
                var car = cars.FirstOrDefault(c => PlanFor(vm, c).Problem == RouteProblem.None);
                if (car == null) continue;
                car.BaseObject.PushUserInteraction(DriveToWorkInteraction, sim, vm.Context, false);
                queued.Add(key);
                cars.Remove(car);
            }
        }

        // ---- keeping out of the way ----
        //
        // While the car is on the lot it watches the space it is about to drive into (its body, a little bigger, over the
        // next tile and a half), and stops for anyone in it: a Sim, a visitor, a pet. After a second and a half it sounds
        // its horn, as the game's own car of its model does. Someone who stays put for twenty seconds is driven past, so a
        // Sim standing still on the driveway cannot keep a car out for ever. Off the lot, on the road, nobody walks.

        private const int HornAfter = 45, HornAgain = 150, GiveUpAfter = 600;

        /// <summary>
        /// True when someone is in the car's way, so it waits this tick (counting how long in waited). Once it has
        /// waited as long as it will, it drives on until its way is clear, and then waits for the next one again.
        /// </summary>
        private static bool Blocked(VM vm, VMMultitileGroup car, Path path, float distance, short driver, ref int waited)
        {
            if (InTheWay(vm, path, distance, driver) == null) { waited = 0; return false; }
            if (waited >= GiveUpAfter) return false;
            if (++waited % HornAgain == HornAfter) PlaySound(car, HornScript);
            return true;
        }

        /// <summary>Whoever is in the space the car is about to drive into, if anyone.</summary>
        private static VMEntity InTheWay(VM vm, Path path, float distance, short driver)
        {
            var arch = vm.Context.Architecture;
            for (float ahead = 0; ahead <= 1.6f; ahead += 0.4f)
            {
                var pose = path.PoseAt(Math.Min(distance + ahead, path.Length));
                if (pose.X < -1 || pose.Y < -1 || pose.X > arch.Width + 1 || pose.Y > arch.Height + 1) continue; //(off the lot)
                foreach (var who in vm.Context.ObjectQueries.Avatars)
                {
                    if (who == null || who.Dead || who.GhostImage || who.ObjectID == driver || who.Position == LotTilePos.OUT_OF_WORLD) continue;
                    if (who.GetValue(VMStackObjectVariable.Hidden) != 0 || (VM.UseWorld && who.WorldUI != null && !who.WorldUI.Visible)) continue;
                    //where they are, in the car's own space at that pose
                    var local = DrivePlan.RotateBy(who.Position.x / 16f - pose.X, who.Position.y / 16f - pose.Y, -pose.Angle);
                    if (Math.Abs(local.X) <= Shared.CarWidth / 2f + 0.25f && Math.Abs(local.Y) <= Shared.CarLength / 2f + 0.25f) return who;
                }
            }
            return null;
        }

        // ---- out of the world while the car is away ----

        /// <summary>
        /// A Sim sent out of the world while the car was away goes back where they got in (from out of the world, or from
        /// beside the carpool that would not have them). If that spot is taken, beside their car: never left out.
        /// </summary>
        private static void BackInTheWorld(VM vm, VMAvatar sim, Trip trip)
        {
            if (trip?.WaitingFrom == null) return; //(never sent out)
            var from = trip.WaitingFrom.Value;
            trip.WaitingFrom = null;
            if (sim.Position == from) return;
            if (sim.SetPosition(from, trip.WaitingFacing, vm.Context).Status == VMPlacementError.Success) return;
            if (sim.Position == LotTilePos.OUT_OF_WORLD) PlaceBeside(vm, sim, trip.Car);
        }

        /// <summary>Put a Sim who is out of the world back in it beside a car (the carpool), or else where they got in.</summary>
        private static void PutBeside(VM vm, VMAvatar sim, VMMultitileGroup car, Trip trip)
        {
            if (sim.Position != LotTilePos.OUT_OF_WORLD) return;
            if (!PlaceBeside(vm, sim, car)) BackInTheWorld(vm, sim, trip);
        }

        /// <summary>Put a Sim on a free tile beside a car, the nearest its middle. False if there is none.</summary>
        private static bool PlaceBeside(VM vm, VMAvatar sim, VMMultitileGroup car)
        {
            if (car == null) return false;
            var arch = vm.Context.Architecture;
            var tiles = new HashSet<(int, int)>(car.Objects.Where(o => !o.Dead && o.Position != LotTilePos.OUT_OF_WORLD).Select(o => ((int)o.Position.TileX, (int)o.Position.TileY)));
            if (tiles.Count == 0) return false;
            float cx = (float)tiles.Average(t => t.Item1), cy = (float)tiles.Average(t => t.Item2);
            var around = new List<(int X, int Y)>();
            foreach (var (x, y) in tiles)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (!tiles.Contains((x + dx, y + dy)) && !around.Contains((x + dx, y + dy))) around.Add((x + dx, y + dy));
            foreach (var (x, y) in around.OrderBy(t => Math.Abs(t.X - cx) + Math.Abs(t.Y - cy)))
            {
                if (x < 0 || y < 0 || x >= arch.Width || y >= arch.Height) continue;
                if (sim.SetPosition(LotTilePos.FromBigTile((short)x, (short)y, 1), sim.Direction, vm.Context).Status == VMPlacementError.Success) return true;
            }
            return false;
        }

        // ---- the engine ----
        //
        // A car sounds as the game's own car of that model sounds driving away: the shared car script "leave sound"
        // (carglobals.iff 8201) picks a sound by the car's object type, and the pack follows it for the game's car the
        // model is drawn as, on a residential lot. The sound is played on the car as the game's play sound primitive
        // plays it, so it follows the car's picture down the road.

        private const ushort LeaveSoundScript = 8201, HornScript = 8200;
        private static Dictionary<(uint, ushort), string> EngineSounds = new Dictionary<(uint, ushort), string>();

        private static string EngineSound(VM vm, VMMultitileGroup car) => CarSound(vm, car, LeaveSoundScript);

        /// <summary>
        /// The sound the game's car of this model plays in one of the cars' shared sound scripts (leave sound, honk
        /// horn), found by following the script as the game would for that car.
        /// </summary>
        private static string CarSound(VM vm, VMMultitileGroup car, ushort scriptID)
        {
            var model = Shared.Cars.FirstOrDefault(c => c.GUID == GUIDOf(car.BaseObject));
            if (model == null) return null;
            lock (EngineSounds)
            {
                if (EngineSounds.TryGetValue((model.GUID, scriptID), out var known)) return known;
                string name = null;
                try
                {
                    var game = FSO.Content.Content.Get().WorldObjects.Get(model.GameGUID);
                    var shared = (game?.Resource as FSO.Content.GameObjectResource)?.SemiGlobal?.MainIff;
                    var script = shared?.Get<FSO.Files.Formats.IFF.Chunks.BHAV>(scriptID);
                    if (script != null)
                    {
                        //follow the script as the game would for this car: object type tests by this car's type, anything else true
                        int? sound = null;
                        int at = 0;
                        for (int steps = 0; steps < 200 && at < script.Instructions.Length; steps++)
                        {
                            var ins = script.Instructions[at];
                            if (ins.Opcode == 23) { sound = BitConverter.ToUInt16(ins.Operand, 0); break; }
                            var next = (ins.Opcode == 32) ? ((BitConverter.ToUInt32(ins.Operand, 0) == model.GameGUID) ? ins.TruePointer : ins.FalsePointer) : ins.TruePointer;
                            if (next >= 253) break;
                            at = next;
                        }
                        if (sound == null) sound = script.Instructions.Where(i => i.Opcode == 23).Select(i => (int?)BitConverter.ToUInt16(i.Operand, 0)).FirstOrDefault();
                        if (sound != null)
                        {
                            var fwav = shared.Get<FSO.Files.Formats.IFF.Chunks.FWAV>((ushort)sound.Value) ?? vm.Context.Globals?.Resource?.Get<FSO.Files.Formats.IFF.Chunks.FWAV>((ushort)sound.Value);
                            name = fwav?.Name;
                        }
                    }
                }
                catch (Exception) { }
                EngineSounds[(model.GUID, scriptID)] = name;
                return name;
            }
        }

        private static void PlayEngine(VMMultitileGroup car) => PlaySound(car, LeaveSoundScript);

        private static void PlaySound(VMMultitileGroup car, ushort scriptID)
        {
            if (!VM.UseWorld || car == null || car.Objects.Count == 0) return;
            try
            {
                var owner = car.BaseObject;
                var vm = owner.Thread?.Context?.VM;
                if (vm == null) return;
                var name = CarSound(vm, car, scriptID);
                if (name == null) return;
                if (owner.SoundThreads.Any(x => x?.Name == name)) return; //(already playing)
                var thread = FSO.HIT.HITVM.Get().PlaySoundEvent(name);
                if (thread == null) return;
                if (!thread.AlreadyOwns(owner.ObjectID)) thread.AddOwner(owner.ObjectID);
                owner.SoundThreads.Add(new FSO.SimAntics.Model.Sound.VMSoundEntry { Sound = thread, Pan = true, Zoom = true, Loop = false, Name = name });
                vm.SoundEntities.Add(owner);
                owner.TickSounds();
            }
            catch (Exception) { }
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
        // What the player sees of a driveway that has been put down is real floor, laid on each of its tiles: the
        // floor it is painted with. The driveway object itself is hidden, so nothing is drawn above the floor; it is
        // only there to be snapped to the road, picked up, sold and parked on. Where it crosses the pavement nothing
        // is laid, so the pavement runs on across it. While a driveway is being held, it is a copy the game makes,
        // which lays nothing, and its painted picture shows where it will go. When the driveway is moved or sold, the
        // floor that was there before is put back.
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

        /// <summary>
        /// Hide or show one tile of a driveway. The game's own "hidden" value is set, which it saves; and the picture
        /// is told as an object's picture has to be, or in 2D (where objects that stand still are drawn once and
        /// kept) the change would not show until something else redrew that part of the lot.
        /// </summary>
        private static void Hide(VMEntity tile, bool hide)
        {
            var value = (short)(hide ? 1 : 0);
            if (tile.GetValue(VMStackObjectVariable.Hidden) == value) return;
            tile.SetValue(VMStackObjectVariable.Hidden, value);
            if (VM.UseWorld && tile.WorldUI is ObjectComponent picture)
            {
                picture.Visible = hide; //(the value above has already set it, quietly: set it wrong and then right, so the change is seen)
                picture.Visible = !hide;
            }
        }

        /// <summary>Before a driveway is moved: put back the floor under where it is.</summary>
        public static void BeforeDrivewayMoves(VMMultitileGroup group, VMContext context)
        {
            if (!IsNewDriveway(group) || context?.VM == null || !context.VM.TS1) return;
            foreach (var tile in group.Objects) Unpave(tile, context);
        }

        /// <summary>
        /// After a driveway has been moved (or was not, and is where it was): lay floor under it and hide it, leaving
        /// the pavement open across it. A copy being held lays nothing and shows its picture, as does a driveway when
        /// no floor of the game's could be found to lay.
        /// </summary>
        public static void AfterDrivewayMoves(VMMultitileGroup group, VMContext context)
        {
            if (!IsNewDriveway(group) || context?.VM == null || !context.VM.TS1) return;
            var arch = context.Architecture;
            ushort laying = 0;
            bool noFloor = false;
            foreach (var tile in group.Objects)
            {
                var pos = tile.Position;
                if (pos == LotTilePos.OUT_OF_WORLD || pos.Level != 1 || pos.TileX < 0 || pos.TileY < 0 || pos.TileX >= arch.Width || pos.TileY >= arch.Height)
                {
                    Hide(tile, false);
                    continue;
                }
                var floor = arch.GetFloor(pos.TileX, pos.TileY, 1);
                //a copy being held: its picture is the preview, open where it will cross the pavement
                if (tile.GhostImage) { Hide(tile, floor.Pattern == PavementFloor); continue; }

                bool paved = tile.GetAttribute(AttrPaved) == 1 && tile.GetAttribute(AttrX) == pos.TileX && tile.GetAttribute(AttrY) == pos.TileY;
                //the floor it laid, or the pavement running on across it
                if (paved || floor.Pattern == PavementFloor) { Hide(tile, true); continue; }

                if (laying == 0 && !noFloor)
                {
                    laying = DrivewayLook.FloorFor(tile);
                    noFloor = laying == 0;
                }
                //(no floor of the game's could be found: the driveway is only its object, and shows)
                if (noFloor) { Hide(tile, false); continue; }
                if (floor.Pattern != laying)
                {
                    tile.SetAttribute(AttrBefore, (short)floor.Pattern);
                    tile.SetAttribute(AttrX, pos.TileX);
                    tile.SetAttribute(AttrY, pos.TileY);
                    tile.SetAttribute(AttrLaid, (short)laying);
                    tile.SetAttribute(AttrPaved, 1);
                    floor.Pattern = laying;
                    arch.SetFloor(pos.TileX, pos.TileY, 1, floor, true);
                }
                //(already that floor: nothing laid, and nothing to put back later; but it is still what is seen)
                Hide(tile, true);
            }
        }

        /// <summary>
        /// The tile of a driveway that has been put down at this tile, for picking it up: a hidden driveway is not
        /// drawn, so the game does not find it under the cursor by itself. 0 if there is none.
        /// </summary>
        public static short DrivewayAt(VM vm, int x, int y)
        {
            var arch = vm.Context.Architecture;
            if (x < 0 || y < 0 || x >= arch.Width || y >= arch.Height) return 0;
            var here = vm.Context.ObjectQueries.GetObjectsAt(LotTilePos.FromBigTile((short)x, (short)y, 1));
            var tile = here?.FirstOrDefault(o => o != null && o is VMGameObject && !o.Dead && !o.GhostImage && GUIDOf(o) == Shared.DrivewayGUID);
            return tile?.ObjectID ?? 0;
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

        //the way the car last being held was pointed, and the sideways direction it was asked for when it was turned (-1: none yet)
        private static int LastCarHeading = -1, LastAcrossHeading = -1;

        /// <summary>
        /// A car the player is holding or putting down can only go on a driveway: every tile of it on driveway, clear
        /// of the pavement, and lengthways along the driveway. One held across is turned to point at the road, and one
        /// held partly on a driveway is slid to lie wholly on it. Returns false where it cannot go; changes the place
        /// and the direction it is being given.
        /// </summary>
        public static bool CarMayGo(VMMultitileGroup group, ref LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags)
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
                //lengthways to the road. one held across it is turned: the first time to face the road, and after
                //that (the player has turned it a notch) to face the other way from last time, so turning it
                //goes between nose to the road and tail to the road
                bool nosePointsAcross = road.Value.AlongX ? (heading % 2 == 0) : (heading % 2 == 1);
                if (!nosePointsAcross)
                {
                    if (LastCarHeading != -1 && LastAcrossHeading != heading) heading = (LastCarHeading + 2) % 4;
                    else if (LastCarHeading != -1) heading = LastCarHeading;
                    else
                    {
                        var arch = context.Architecture;
                        int across = road.Value.AlongX ? arch.Height : arch.Width;
                        bool roadAtHighEdge = (across - 1 - road.Value.Last) <= road.Value.First;
                        heading = road.Value.AlongX ? (roadAtHighEdge ? 2 : 0) : (roadAtHighEdge ? 1 : 3);
                    }
                    LastAcrossHeading = HeadingOf(direction);
                    direction = Directions[heading];
                }
                LastCarHeading = heading;
            }

            //slide it onto the driveway it is held over: the one under its lead tile, or else under any of it
            var driveway = DrivewayUnder(context, pos.TileX, pos.TileY);
            for (int x = 0; x < Shared.CarWidth && driveway == null; x++)
                for (int y = 0; y < Shared.CarLength && driveway == null; y++)
                {
                    var off = DrivePlan.Rotate(x, y, heading);
                    driveway = DrivewayUnder(context, pos.TileX + (int)Math.Round(off.X), pos.TileY + (int)Math.Round(off.Y));
                }
            if (driveway == null) return false;
            //(the part of it a car may stand on: not the pavement, the verge or the road)
            var garden = driveway.Objects.Select(o => o.Position).Where(p => p != LotTilePos.OUT_OF_WORLD
                && (road == null || (road.Value.AlongX ? p.TileY : p.TileX) < road.Value.StripFirst || (road.Value.AlongX ? p.TileY : p.TileX) > road.Value.StripLast)).ToList();
            if (garden.Count == 0) return false;
            var fit = CarSnap.Fit(garden.Min(p => p.TileX), garden.Min(p => p.TileY), garden.Max(p => p.TileX), garden.Max(p => p.TileY), pos.TileX, pos.TileY, heading, Shared.CarWidth, Shared.CarLength);
            if (fit == null) return false;
            if (fit.Value.X != pos.TileX || fit.Value.Y != pos.TileY) pos = LotTilePos.FromBigTile((short)fit.Value.X, (short)fit.Value.Y, 1);

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

        private static VMMultitileGroup DrivewayUnder(VMContext context, int x, int y)
        {
            var arch = context.Architecture;
            if (x < 0 || y < 0 || x >= arch.Width || y >= arch.Height) return null;
            var here = context.ObjectQueries.GetObjectsAt(LotTilePos.FromBigTile((short)x, (short)y, 1));
            return here?.FirstOrDefault(o => o != null && o is VMGameObject && !o.Dead && IsDriveway(o))?.MultitileGroup;
        }

        /// <summary>
        /// A driveway the player is holding or putting down goes against the road, pointing at it, as in The Sims 2:
        /// it follows the cursor along the road and nowhere else. Called before the game moves any object; changes
        /// where a driveway is being moved to. Only the player's own placing is touched, never a lot loading.
        /// </summary>
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
        /// What the pack makes of a lot, for the "driveways" console command: where it thinks the road is, what the
        /// driveway and the cars are drawn with, and whether each car could be driven now.
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

            lines.Add($"Driveways snapped to the road since the game started: {Snaps} times. The last: {LastSnap}.");
            lines.Add("The driveway's look: " + DrivewayLook.Describe());
            lines.Add("The car's look: " + CarLook.Describe());
            lines.Add($"Sims get in by the car's {(Settings.DoorOnRight ? "right" : "left")} side (\"driveways door\" to change it).");

            lines.Add($"The time is {vm.GetGlobalValue(0)}:{vm.GetGlobalValue(5):00} on day {vm.GetGlobalValue(1)}.");
            foreach (var person in vm.Entities.OfType<VMAvatar>().Where(a => !a.Dead && !a.GhostImage && a.GetPersonData(VMPersonDataVariable.JobType) > 0).ToList())
            {
                try { lines.Add(DescribeWork(vm, person)); } catch (Exception e) { lines.Add($"{person.Name}: the hours could not be worked out ({e.Message})."); }
            }

            var places = Destinations(vm);
            lines.Add(places.Count == 0 ? "No community lots for the car to drive to (none in this game, or none could be read)."
                : $"Community lots the car can drive to: {string.Join(", ", places.Select(p => $"{p.Name} ({p.Area}, lot {p.House})"))}.");

            var cars = vm.Entities.Where(e => e is VMGameObject && IsCarGUID(GUIDOf(e))).Select(e => e.MultitileGroup).Distinct().ToList();
            var driveways = vm.Entities.Where(e => e is VMGameObject && IsDriveway(e)).Select(e => e.MultitileGroup).Distinct().Count();
            lines.Add(driveways == 0 ? "No driveways on this lot." : $"{driveways} driveway{(driveways == 1 ? "" : "s")} on this lot.");
            if (cars.Count == 0) lines.Add("No cars on this lot.");
            foreach (var car in cars)
            {
                var lead = car.BaseObject;
                var where = $"Car at tile {lead.Position.TileX}, {lead.Position.TileY} facing {lead.Direction.ToString().ToLowerInvariant()}: ";
                if (TripsOf(vm).ContainsKey(lead.ObjectID)) { lines.Add(where + "out for a drive."); continue; }
                var commute = CommutesOf(vm).FirstOrDefault(c => c.Car == car);
                if (commute != null)
                {
                    var doing = commute.State == CommuteState.GettingIn ? $"taking {commute.SimName} to work."
                        : commute.State == CommuteState.AtWork ? $"away while {commute.SimName} is at work."
                        : commute.State == CommuteState.Home ? "on its way home."
                        : commute.State == CommuteState.Departing ? $"taking {commute.SimName} to {commute.Destination}."
                        : $"bringing {commute.SimName} home.";
                    lines.Add(where + doing);
                    continue;
                }
                var plan = PlanFor(vm, car);
                var sound = EngineSound(vm, car);
                lines.Add(where + (plan.Problem == RouteProblem.None ? "ready to drive." : Explain(plan.Problem))
                    + (IsAuto(car) ? " It takes the household to work by itself." : "")
                    + (sound != null ? $" Engine: {sound}." : " Engine: none found."));
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
        ///
        /// In 3D each tile is a model, and can be turned to any angle: the car turns smoothly round a corner (the
        /// pose's Angle), its tiles laid out round it, each telling the engine its exact angle (FreeAngles). In 2D a
        /// tile is one of four pictures, so the car keeps to the nearest of the four ways (the pose's Heading), and
        /// snaps round half way through a turn. Which it is is looked at every tick, so the camera can change mid-drive.
        /// </summary>
        private static void Draw(VMMultitileGroup car, Pose pose)
        {
            if (!VM.UseWorld) return;
            bool free = In3D(car);
            float angle = free ? pose.Angle : pose.Heading * (float)(Math.PI / 2);
            var lead = DrivePlan.LeadFromCentreAt(pose.X, pose.Y, angle, Shared.CarWidth, Shared.CarLength);
            var baseOffset = car.Offsets[car.Objects.IndexOf(car.BaseObject)];
            for (int i = 0; i < car.Objects.Count; i++)
            {
                var part = car.Objects[i];
                if (part.WorldUI == null) continue;
                var off = DrivePlan.RotateBy((car.Offsets[i].x - baseOffset.x) / 16f, (car.Offsets[i].y - baseOffset.y) / 16f, angle);
                if (part.WorldUI.Room == 0) part.WorldUI.Room = 65535; //(as SetVisualPosition does: never treated as off the lot)
                if (part.WorldUI is ObjectComponent picture)
                {
                    //in 2D, objects that stand still are drawn once and kept. one that moves must say so, to be drawn afresh every frame
                    picture.ForceDynamic = true;
                    if (free) SetFreeAngle(picture, angle); else ClearFreeAngle(picture);
                }
                part.WorldUI.Direction = Directions[pose.Heading];
                part.VisualPosition = new Vector3(lead.X + off.X, lead.Y + off.Y, 0);
            }
        }

        /// <summary>Whether the lot is being shown in 3D, where a car can turn smoothly.</summary>
        private static bool In3D(VMMultitileGroup car)
        {
            var world = car.BaseObject?.Thread?.Context?.World;
            return world?.State != null && world.State.CameraMode == FSO.LotView.Model.CameraRenderMode._3D;
        }

        // the exact angle of each car tile turning in 3D, which the engine is told in place of its own (see DrivewaysPack.AfterWorld3D)
        private static readonly Dictionary<ObjectComponent, float> FreeAngles = new Dictionary<ObjectComponent, float>();
        /// <summary>How many tiles have an angle of their own: when none do, the engine is not delayed by looking.</summary>
        public static int FreeAngleCount;

        private static void SetFreeAngle(ObjectComponent picture, float angle)
        {
            lock (FreeAngles) { FreeAngles[picture] = angle; FreeAngleCount = FreeAngles.Count; }
        }

        private static void ClearFreeAngle(ObjectComponent picture)
        {
            if (FreeAngleCount == 0) return;
            lock (FreeAngles) { FreeAngles.Remove(picture); FreeAngleCount = FreeAngles.Count; }
        }

        /// <summary>The exact angle a car tile is turned to while it drives in 3D, or null for its own.</summary>
        public static float? FreeAngleOf(ObjectComponent picture)
        {
            lock (FreeAngles) return FreeAngles.TryGetValue(picture, out var angle) ? angle : (float?)null;
        }

        /// <summary>Put the car's pictures back where the car really is.</summary>
        private static void Park(VMMultitileGroup car)
        {
            if (!VM.UseWorld) return;
            foreach (var part in car.Objects)
            {
                if (part.WorldUI == null || part.Dead || part.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (part.WorldUI is ObjectComponent picture) { ClearFreeAngle(picture); picture.ForceDynamic = false; }
                part.WorldUI.Direction = part.Direction;
                part.VisualPosition = new Vector3(part.Position.x / 16f, part.Position.y / 16f, (part.Position.Level - 1) * 2.95f);
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
        /// <summary>Sims get in and out by the car's right side, not its left (the driver's door).</summary>
        public static bool DoorOnRight;

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
                    else if (key == "dooronright") DoorOnRight = value == "1";
                }
            }
            catch (Exception) { }
        }

        public static void Save()
        {
            try { System.IO.File.WriteAllLines(File, new[] { "floor=" + Floor, "reversecars=" + (ReverseCars ? "1" : "0"), "dooronright=" + (DoorOnRight ? "1" : "0") }); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// The driveway's look: one of the game's own floors. Once put down, a driveway is that floor, laid for real. While
    /// it is held it is an object, and the object file gives it only its shape: here its tiles are given pictures
    /// painted with the same floor tile from the player's game (see FloorDress), so the preview looks like the result.
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
        private static ConditionalWeakTable<VMGameObject, object> Known = new ConditionalWeakTable<VMGameObject, object>();
        //(pictures are set from the loading thread as well as the game's: the looking is done by one at a time)
        private static readonly object Looking = new object();

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
            lock (Looking) { if (!Looked) Look(tile.Object.OBJ.ChunkParent); }
            return Pattern;
        }

        /// <summary>
        /// Called whenever the game sets an object's picture. For a tile of a driveway, set the painted one in its place.
        /// </summary>
        public static void Dress(VMGameObject obj)
        {
            if (!VM.UseWorld || !IsDrivewayTile(obj)) return;
            lock (Looking) { if (!Looked) Look(obj.Object.OBJ.ChunkParent); }
            lock (Tiles)
            {
                if (!Known.TryGetValue(obj, out _))
                {
                    Known.Add(obj, null);
                    if (Tiles.Count > 256) Tiles.RemoveAll(t => !t.TryGetTarget(out _));
                    Tiles.Add(new WeakReference<VMGameObject>(obj));
                }
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
            if (tiles.Count == 0) { lock (Looking) Looked = false; return; }
            lock (Looking) Look(tiles[0].Object.OBJ.ChunkParent);
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
        //for each car the pack sells (by its GUID): the places of its doors, as the game's car marks them (see VMDrive.DoorTile)
        private static Dictionary<uint, List<(int X, int Y)>> Doors = new Dictionary<uint, List<(int X, int Y)>>();
        //(held so the game keeps them loaded)
        private static List<FSO.Content.GameObject> Held = new List<FSO.Content.GameObject>();

        /// <summary>The places (column, row) of a car's doors, as the game's car it is drawn as marks them; none if it is not there.</summary>
        public static IReadOnlyList<(int X, int Y)> DoorsOf(uint carGUID)
        {
            lock (Held)
            {
                if (!Looked) Look();
                return Doors.TryGetValue(carGUID, out var doors) ? doors : new List<(int X, int Y)>();
            }
        }

        /// <summary>
        /// Whether a tile of one of the game's cars is a door: its init script is the cars' "init route piece", which marks
        /// the tile (its attribute 1 set to 1) for the carpool to send a Sim to.
        /// </summary>
        private static bool IsDoor(FSO.Content.GameObject tile)
        {
            ushort init;
            if (tile.OBJ.UsesFnTable == 0) init = tile.OBJ.BHAV_Init;
            else
            {
                var table = tile.Resource.Get<FSO.Files.Formats.IFF.Chunks.OBJf>(tile.OBJ.ChunkID);
                if (table?.functions == null || table.functions.Length == 0) return false;
                init = table.functions[0].ActionFunction;
            }
            if (init < 4096) return false;
            var script = (init >= 8192) ? tile.Resource.SemiGlobal?.Get<FSO.Files.Formats.IFF.Chunks.BHAV>(init) : tile.Resource.Get<FSO.Files.Formats.IFF.Chunks.BHAV>(init);
            if (script == null) return false;
            if ((script.ChunkLabel ?? "").IndexOf("route piece", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            //(or by what it does: my attribute 1 := 1)
            return script.Instructions.Any(i => i.Opcode == 2 && i.Operand != null && i.Operand.Length >= 8
                && i.Operand[0] == 1 && i.Operand[1] == 0 && i.Operand[2] == 1 && i.Operand[3] == 0 && i.Operand[5] == 5 && i.Operand[6] == 0 && i.Operand[7] == 7);
        }

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
                    var doors = new List<(int X, int Y)>();
                    foreach (var tile in tiles)
                    {
                        var obj = provider.Get((uint)tile.ID);
                        var dgrp = obj?.Resource.Get<FSO.Files.Formats.IFF.Chunks.DGRP>(obj.OBJ.BaseGraphicID);
                        if (dgrp == null) break;
                        var place = PlaceOf(tile.SubIndex);
                        borrowed[place.X * 256 + place.Y] = dgrp;
                        Held.Add(obj);
                        try { if (IsDoor(obj)) doors.Add(place); } catch (Exception) { }
                    }
                    if (borrowed.Count != places.Count) { missing.Add(model.Name + " (a tile has no picture)"); continue; }
                    Borrowed[model.GUID] = borrowed;
                    Doors[model.GUID] = doors.OrderBy(d => d.X).ThenBy(d => d.Y).ToList();
                    found.Add($"{model.Name} from \"{master.Name}\" (doors " + (doors.Count == 0 ? "not marked: row " + 2 + " is used" : string.Join(" ", Doors[model.GUID].Select(d => $"{d.X},{d.Y}"))) + ")");
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
            lock (Held) { if (!Looked) Look(); }
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
                //the lot's car portals, which the carpool hands a Sim to, and the taxi, which takes Sims to community lots
                foreach (var (guid, what) in new[] { (0x865A6812u, "Car portal 1"), (0xD564C66Bu, "Car portal 2"), (0x7AE4654Au, "The taxi") })
                {
                    try
                    {
                        var obj = provider.Get(guid);
                        if (obj == null) { text.AppendLine($"{what} ({guid:X8}) is not in this game."); continue; }
                        Dump(what, obj.Resource.MainIff);
                        var shared = (obj.Resource as FSO.Content.GameObjectResource)?.SemiGlobal;
                        if (shared != null) Dump("The scripts " + what.ToLowerInvariant() + " shares", shared.MainIff);
                    }
                    catch (Exception e) { text.AppendLine($"{what} could not be read: {e.Message}"); }
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
                    text.AppendLine("==== The scripts every object shares (names only, but for those about leaving the lot or going to and from work) ====");
                    var full = new System.Text.RegularExpressions.Regex("leave|portal|work|job|carpool|taxi|arriv|return home|go home|away", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (var script in global.List<FSO.Files.Formats.IFF.Chunks.BHAV>().OrderBy(b => b.ChunkID))
                    {
                        text.AppendLine($"script {script.ChunkID} \"{script.ChunkLabel}\" args {script.Args} locals {script.Locals}");
                        if (!full.IsMatch(script.ChunkLabel ?? "")) continue;
                        for (int i = 0; i < script.Instructions.Length; i++)
                        {
                            var ins = script.Instructions[i];
                            text.AppendLine($"  {i}: {ins.Opcode} [{string.Join(" ", ins.Operand.Select(b => b.ToString("X2")))}] t{ins.TruePointer} f{ins.FalsePointer}");
                        }
                    }
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
        /// <summary>Byte 1: for ModePrepare, the mode of the drive being got ready.</summary>
        public byte Arg { get; set; }

        public void Read(byte[] bytes)
        {
            Mode = bytes.Length > 0 ? bytes[0] : (byte)0;
            Arg = bytes.Length > 1 ? bytes[1] : (byte)0;
        }

        public void Write(byte[] bytes)
        {
            using (var io = new BinaryWriter(new MemoryStream(bytes))) { io.Write(Mode); io.Write(Arg); }
        }
    }
}
