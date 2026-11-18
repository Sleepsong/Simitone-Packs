/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Driveways
{
    // ---- saving a drive ----
    //
    // The game saves a lot as it is: a Sim part way through the car's script, hidden or out of the world at work. What
    // it does not know about is the drive itself (where the car is on its way, that the Sim at work went in their own
    // car, which carpools are kept out of sight), which lives only in the pack. So whenever the game takes a copy of a
    // lot (VM.Save) the pack takes one of its drives, and whenever the game puts a lot back from a copy (VM.Load) the
    // pack puts its drives back too, on the lot's first tick, before any script runs:
    //
    //  - saved to a file: the pack writes its copy to a file of its own beside the save, named as Simitone names a
    //    pack's lot data (LocalHouse/PackData/[pack]/[lot]-[hash].dat), and reads it back when the lot is loaded, if
    //    it was saved at the same time of day as the lot (else it belongs to another save of it)
    //  - kept in memory: the game keeps the home lot that way while the family is on a community lot, and the pack
    //    keeps its copy with the game's
    //
    // Nothing is put back that does not fit what the game loaded: a car or a Sim that is not there, or not the same,
    // or a Sim not where the drive would have them, and that part of the drive is dropped (the car parks).

    public partial class VMDrive
    {
        /// <summary>The pack's id, which names the folder its files are kept in.</summary>
        public static string PackId = "sample.driveways";

        //the pack's copy of each copy the game took of a lot
        private static ConditionalWeakTable<VMMarshal, SavedDrives> Copies = new ConditionalWeakTable<VMMarshal, SavedDrives>();
        //drives to put back on a lot's first tick
        private static ConditionalWeakTable<VM, SavedDrives> Pending = new ConditionalWeakTable<VM, SavedDrives>();

        /// <summary>The game has taken a copy of the lot (to save it, or to keep it while the family is away): take one of the drives.</summary>
        public static void Copied(VM vm, VMMarshal copy)
        {
            if (vm == null || copy == null) return;
            Copies.AddOrUpdate(copy, Snapshot(vm));
        }

        /// <summary>The game has saved the lot to its file: keep the drives with it (or remove what was kept, if there are none).</summary>
        public static void Saved(VM vm)
        {
            if (vm == null) return;
            var path = HousePath(vm);
            if (path == null) return;
            //(taken now, it is the same as the copy the game has just written: nothing has run in between)
            var drives = Snapshot(vm);
            WriteFile(path, drives.IsEmpty ? null : drives.ToBytes());
        }

        /// <summary>The game has put a lot back from a copy: find the drives that go with it, to be put back on its first tick.</summary>
        public static void Loaded(VM vm, VMMarshal copy)
        {
            if (vm == null) return;
            Pending.Remove(vm);
            SavedDrives drives = null;
            if (copy != null && Copies.TryGetValue(copy, out var kept)) drives = kept; //(the home lot, back from a community lot)
            else
            {
                var path = HousePath(vm);
                if (path != null) drives = SavedDrives.FromBytes(ReadFile(path));
                if (drives != null && !drives.SavedAt(vm.GetGlobalValue(1), vm.GetGlobalValue(0), vm.GetGlobalValue(5))) drives = null;
            }
            if (drives != null && !drives.IsEmpty) Pending.AddOrUpdate(vm, drives);
        }

        /// <summary>
        /// If the lot has drives waiting to be put back, put them back (once). Called before each tick, and by a drive that
        /// finds itself already under way. True if there were any.
        /// </summary>
        public static bool RestoreSaved(VM vm)
        {
            if (vm == null || !Pending.TryGetValue(vm, out var drives)) return false;
            Pending.Remove(vm);
            try { Restore(vm, drives); } catch (Exception) { }
            return true;
        }

        // ---- taking a copy ----

        private static SavedDrives Snapshot(VM vm)
        {
            var drives = new SavedDrives { Day = vm.GetGlobalValue(1), Hour = vm.GetGlobalValue(0), Minute = vm.GetGlobalValue(5) };
            if (AllTrips.TryGetValue(vm, out var trips))
            {
                foreach (var trip in trips.Values)
                {
                    var sim = vm.GetObjectById(trip.SimID) as VMAvatar;
                    if (sim == null || sim.Dead || !Alive(trip.Car)) continue;
                    var waiting = trip.WaitingFrom;
                    drives.Trips.Add(new SavedTrip
                    {
                        Car = Ref(trip.Car.BaseObject), Sim = new SavedObject(sim.ObjectID, sim.Object.OBJ.GUID),
                        Phase = trip.Phase.ToString(), Distance = trip.Distance, Timer = trip.Timer, ToWork = trip.ToWork, ToLot = trip.ToLot,
                        Carpool = RefCarpool(trip.Carpool), CarpoolInteraction = trip.Carpool?.Interaction ?? 0,
                        WaitingFrom = waiting == null ? null : new SavedTile { X = waiting.Value.x, Y = waiting.Value.y, Level = waiting.Value.Level },
                        WaitingFacing = (int)trip.WaitingFacing
                    });
                }
            }
            if (AllCommutes.TryGetValue(vm, out var commutes))
            {
                foreach (var commute in commutes)
                {
                    //(one on its way to a community lot is over as far as this lot goes: the car is home when they are)
                    if (commute.State == CommuteState.Departing || !Alive(commute.Car)) continue;
                    drives.Commutes.Add(new SavedCommute
                    {
                        Car = Ref(commute.Car.BaseObject), SimGUID = commute.SimGUID, SimName = commute.SimName,
                        State = commute.State.ToString(), Ticks = commute.Ticks, Distance = commute.Distance,
                        Carpool = RefCarpool(commute.Carpool), CarpoolInteraction = commute.Carpool?.Interaction ?? 0,
                        CarsWhenGone = commute.CarsWhenGone?.Where(Alive).Select(c => Ref(c.BaseObject)).ToList()
                    });
                }
            }
            if (AllHiddenCars.TryGetValue(vm, out var hidden))
                drives.HiddenCars.AddRange(hidden.Where(Alive).Select(c => Ref(c.BaseObject)));
            if (AllAutoQueued.TryGetValue(vm, out var queued))
                drives.AutoQueued.AddRange(queued.Select(q => new SavedQueued { SimGUID = q.Item1, Day = q.Item2 }));
            return drives;
        }

        private static bool Alive(VMMultitileGroup group) => group != null && group.Objects.Count > 0 && !group.Objects.Any(o => o.Dead);
        private static SavedObject Ref(VMEntity obj) => new SavedObject(obj.ObjectID, GUIDOf(obj));
        private static SavedObject RefCarpool(Carpool carpool) => (carpool?.Tile == null || carpool.Tile.Dead) ? null : Ref(carpool.Tile);

        // ---- putting it back ----

        private static void Restore(VM vm, SavedDrives drives)
        {
            var now = vm.Scheduler.CurrentTickID;
            var restored = false;

            var trips = TripsOf(vm);
            foreach (var saved in drives.Trips)
            {
                try
                {
                    var car = CarOf(Find(vm, saved.Car));
                    var sim = (saved.Sim == null) ? null : vm.GetObjectById(saved.Sim.ID) as VMAvatar;
                    if (car == null || sim == null || sim.Dead || sim.Object.OBJ.GUID != saved.Sim.GUID || !IsParked(vm, car)) continue;
                    if (!Enum.TryParse<Phase>(saved.Phase, out var phase)) continue;
                    var plan = PlanFor(vm, car);
                    if (plan.Problem != RouteProblem.None) continue;
                    var trip = new Trip
                    {
                        SimID = sim.ObjectID, Car = car, Plan = plan, Phase = phase, Timer = saved.Timer, ToWork = saved.ToWork, ToLot = saved.ToLot,
                        Carpool = CarpoolFrom(vm, saved.Carpool, saved.CarpoolInteraction), LastTick = now
                    };
                    if (saved.WaitingFrom != null && sim.Position == LotTilePos.OUT_OF_WORLD)
                    {
                        trip.WaitingFrom = new LotTilePos(saved.WaitingFrom.X, saved.WaitingFrom.Y, saved.WaitingFrom.Level);
                        trip.WaitingFacing = (Direction)saved.WaitingFacing;
                    }
                    switch (phase)
                    {
                        case Phase.Out:
                            trip.Distance = Math.Clamp(saved.Distance, 0, plan.Out.Length);
                            Draw(car, plan.Out.PoseAt(trip.Distance));
                            Show(car, true);
                            break;
                        case Phase.Back:
                            trip.Distance = Math.Clamp(saved.Distance, 0, plan.Back.Length);
                            Draw(car, plan.Back.PoseAt(trip.Distance));
                            Show(car, true);
                            break;
                        default:
                            Show(car, false);
                            break;
                    }
                    trips[car.BaseObject.ObjectID] = trip;
                    Show(sim, false);
                    restored = true;
                }
                catch (Exception) { }
            }

            var commutes = CommutesOf(vm);
            foreach (var saved in drives.Commutes)
            {
                try
                {
                    var car = CarOf(Find(vm, saved.Car));
                    if (car == null || !IsParked(vm, car)) continue;
                    if (!Enum.TryParse<CommuteState>(saved.State, out var state) || state == CommuteState.Departing) continue;
                    var plan = PlanFor(vm, car);
                    if (plan.Problem != RouteProblem.None) continue;
                    var sim = SimOnLot(vm, saved.SimGUID);
                    var commute = new Commute
                    {
                        Car = car, Plan = plan, SimGUID = saved.SimGUID, SimName = saved.SimName ?? sim?.Name, State = state, Ticks = saved.Ticks,
                        Distance = Math.Clamp(saved.Distance, 0, plan.Back.Length),
                        Carpool = CarpoolFrom(vm, saved.Carpool, saved.CarpoolInteraction),
                        CarsWhenGone = saved.CarsWhenGone == null ? null
                            : new HashSet<VMMultitileGroup>(saved.CarsWhenGone.Select(c => Find(vm, c)?.MultitileGroup).Where(Alive))
                    };
                    //the Sim must be where the game has them for this part of the day
                    bool fits;
                    switch (state)
                    {
                        case CommuteState.GettingIn: fits = sim != null && (sim.Position == LotTilePos.OUT_OF_WORLD || WithTheCarpool(sim, commute)); break;
                        case CommuteState.AtWork: fits = sim != null && sim.Position == LotTilePos.OUT_OF_WORLD; break;
                        case CommuteState.Riding:
                        case CommuteState.Arrived: fits = sim != null && sim.Position != LotTilePos.OUT_OF_WORLD && sim.Thread?.Queue?.Any(a => a?.Callee?.MultitileGroup == car) == true; break;
                        default: fits = true; break; //(the car on its way home on its own)
                    }
                    if (!fits) continue;
                    switch (state)
                    {
                        case CommuteState.GettingIn:
                        case CommuteState.AtWork:
                            Show(car, false);
                            break;
                        case CommuteState.Riding:
                        case CommuteState.Home:
                            Draw(car, plan.Back.PoseAt(commute.Distance));
                            Show(car, true);
                            break;
                        case CommuteState.Arrived:
                            Park(car);
                            break;
                    }
                    if (sim != null && sim.Position != LotTilePos.OUT_OF_WORLD && state != CommuteState.Home) Show(sim, false);
                    commutes.Add(commute);
                    restored = true;
                }
                catch (Exception) { }
            }

            //the game's cars that were kept out of sight, if anything they were kept out of sight for is back
            if (restored)
            {
                foreach (var saved in drives.HiddenCars)
                {
                    var obj = Find(vm, saved);
                    if (obj != null && obj is VMGameObject && IsGameCar(obj)) HideCar(vm, obj.MultitileGroup);
                }
            }
            var queued = AllAutoQueued.GetOrCreateValue(vm);
            foreach (var q in drives.AutoQueued) queued.Add((q.SimGUID, q.Day));
        }

        /// <summary>The object saved as this, if it is still the same one.</summary>
        private static VMEntity Find(VM vm, SavedObject saved)
        {
            if (saved == null) return null;
            var obj = vm.GetObjectById(saved.ID);
            return (obj == null || obj.Dead || GUIDOf(obj) != saved.GUID) ? null : obj;
        }

        private static Carpool CarpoolFrom(VM vm, SavedObject tile, int interaction)
        {
            var obj = Find(vm, tile);
            if (obj == null || !(obj is VMGameObject) || !IsGameCar(obj)) return null;
            return new Carpool { Car = obj.MultitileGroup, Tile = obj, Interaction = interaction };
        }

        // ---- the file ----

        /// <summary>The lot's file, as the game names it when it saves: by the lot's number (global 10).</summary>
        private static string HousePath(VM vm)
        {
            try
            {
                var house = vm.GetGlobalValue(10);
                if (house <= 0) return null;
                return FSO.Content.Content.Get().Neighborhood.GetHousePath(house);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Where the drives for a lot are kept: where Simitone keeps a pack's data for a lot (LotPackData), under the same name.</summary>
        private static string FileFor(string lotPath)
        {
            //the lot's file name, to be readable, plus a hash of its whole path: different neighbourhoods reuse the names
            var full = System.IO.Path.GetFullPath(lotPath).Replace('\\', '/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 12).ToLowerInvariant();
            var name = new string(System.IO.Path.GetFileNameWithoutExtension(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var pack = new string(PackId.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
            return System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "LocalHouse", "PackData", pack, name + "-" + hash + ".dat");
        }

        private static byte[] ReadFile(string lotPath)
        {
            try
            {
                var file = FileFor(lotPath);
                return File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            catch (Exception) { return null; }
        }

        private static void WriteFile(string lotPath, byte[] data)
        {
            try
            {
                var file = FileFor(lotPath);
                if (data == null)
                {
                    if (File.Exists(file)) File.Delete(file);
                    return;
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                File.WriteAllBytes(file, data);
            }
            catch (Exception) { }
        }
    }
}
