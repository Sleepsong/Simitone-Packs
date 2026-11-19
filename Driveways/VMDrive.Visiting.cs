/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Linq;

namespace Driveways
{
    // ---- arriving by car ----
    //
    // A Sim who drives to a community lot is taken there by the game, which makes them on the new lot out of the world and
    // then puts them in it, where that lot puts arrivals. When they appear, the pack puts their car (the same model) at
    // the far kerb of that lot's road (the near one is where the game's own vehicles pull up), on a straight stretch near
    // them, hides them, and has the car drive in along the road and pull up, as their car brings them home from work;
    // then they get out at its door, on the side towards the lot. The car stays at the kerb while they are there. It
    // offers Drive Home: they get in, it drives off along the road, and the game takes them home, where their own car drives
    // in from the road and onto its driveway, and they get out of it.
    //
    // A car at the kerb is marked (its lead tile's attribute 1) so it can be told from the household's own: it offers
    // only Drive Home, cannot be moved or sold, and one left on a lot from an earlier visit (a lot saved while visiting)
    // is taken away when the lot is loaded.

    public partial class VMDrive
    {
        /// <summary>The lot number the game takes as the household's home (ChangeToLotInTemp0 with -1).</summary>
        public const short HomeLot = -1;

        private const int VisitorAttribute = 1;

        /// <summary>A Sim on their way to another lot by car.</summary>
        private class Outing
        {
            public uint SimGUID;
            /// <summary>The car's model (its master's GUID), for the car at the kerb.</summary>
            public uint CarGUID;
            /// <summary>The lot they are going to, and their home lot with their car on it.</summary>
            public short To, HomeHouse, HomeCar;
            public DateTime Until;
            /// <summary>The lot they left: the one they arrive at is the next one the game runs.</summary>
            public WeakReference<VM> From;
        }

        //on the way: taken by the lot they arrive at (only one Sim changes lots at a time)
        private static Outing Arriving;
        //the outing under way, for driving home: where home is, and which car there is theirs
        private static Outing Away;

        public static bool IsVisiting(VMMultitileGroup car) => car?.BaseObject != null && car.BaseObject.GetAttribute(VisitorAttribute) == 1;

        /// <summary>Whether this Sim may drive home in a car at the kerb: one of the household being played.</summary>
        private static bool CanDriveHome(VM vm, VMAvatar sim)
        {
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return true;
            return family.FamilyGUIDs != null && family.FamilyGUIDs.Contains(sim.Object.OBJ.GUID);
        }

        /// <summary>A car at the kerb: the way it came, and the way it leaves, worked out again from where it stands.</summary>
        private static DrivePlan KerbPlanFor(VM vm, VMMultitileGroup car)
        {
            var lead = car.BaseObject;
            var pos = lead.Position;
            if (pos == LotTilePos.OUT_OF_WORLD || pos.Level != 1) return new DrivePlan { Problem = RouteProblem.NotGroundLevel };
            var heading = HeadingOf(lead.Direction);
            if (heading == -1) return new DrivePlan { Problem = RouteProblem.NotFacingRoad };
            var road = FindRoad(vm, out _);
            if (road == null) return new DrivePlan { Problem = RouteProblem.NoRoad };
            var centre = DrivePlan.BodyCentre(pos.x / 16f, pos.y / 16f, heading, Shared.CarWidth, Shared.CarLength);
            float across = road.Value.AlongX ? centre.Y : centre.X, along = road.Value.AlongX ? centre.X : centre.Y;
            int lotSide = across < (road.Value.First + road.Value.Last + 1) / 2f ? -1 : 1;
            var arch = vm.Context.Architecture;
            return DrivePlan.AtKerb(road.Value, lotSide, along, Shared.CarWidth, Shared.CarLength, arch.Width, arch.Height, Settings.ReverseCars);
        }

        /// <summary>The car has left this lot with the Sim, for another: note where they are going, and in what.</summary>
        private static void Leaving(VM vm, Trip trip, VMAvatar sim)
        {
            var simGUID = sim.Object.OBJ.GUID;
            var carGUID = GUIDOf(trip.Car.BaseObject);
            var until = DateTime.UtcNow.AddMinutes(5);
            var from = new WeakReference<VM>(vm);
            if (trip.ToLot == HomeLot)
            {
                //home, from a visit: their own car brings them in
                var away = (Away != null && Away.SimGUID == simGUID) ? Away : null;
                short home = away?.HomeHouse ?? (short)(vm.TS1State?.CurrentFamily?.HouseNumber ?? 0);
                Arriving = new Outing { SimGUID = simGUID, CarGUID = away?.CarGUID ?? carGUID, To = home, HomeHouse = home, HomeCar = away?.HomeCar ?? 0, Until = until, From = from };
                Away = null;
            }
            else
            {
                Away = new Outing { SimGUID = simGUID, CarGUID = carGUID, To = trip.ToLot, HomeHouse = vm.GetGlobalValue(10), HomeCar = trip.Car.BaseObject.ObjectID, Until = until };
                Arriving = new Outing { SimGUID = simGUID, CarGUID = carGUID, To = trip.ToLot, HomeHouse = Away.HomeHouse, HomeCar = Away.HomeCar, Until = until, From = from };
            }
        }

        /// <summary>
        /// Every tick: on the lot a Sim is driving to, as soon as the game has put them in it, bring their car in. Called
        /// after the lot's scripts have run, so they are hidden before they are ever drawn.
        /// </summary>
        public static void TickArrivals(VM vm)
        {
            var arriving = Arriving;
            if (arriving == null) return;
            if (DateTime.UtcNow > arriving.Until) { Arriving = null; return; }
            if (arriving.From != null && arriving.From.TryGetTarget(out var left) && left == vm) return; //(still on the lot they are leaving)
            var sim = SimOnLot(vm, arriving.SimGUID);
            if (sim == null || sim.Position == LotTilePos.OUT_OF_WORLD) return;
            Arriving = null;
            try
            {
                if (arriving.To == arriving.HomeHouse) ArriveHome(vm, sim, arriving);
                else ArriveVisiting(vm, sim, arriving);
            }
            catch (Exception) { Show(sim, true); }
        }

        /// <summary>Home from a visit: their own car drives in from the road onto its driveway with them, and they get out.</summary>
        private static void ArriveHome(VM vm, VMAvatar sim, Outing outing)
        {
            VMMultitileGroup car = null;
            var known = vm.GetObjectById(outing.HomeCar);
            if (known != null && !known.Dead && GUIDOf(known) == outing.CarGUID) car = CarOf(known);
            //(not known: a car of the same model, not out)
            car ??= vm.Entities.Where(e => e is VMGameObject && !e.Dead && GUIDOf(e) == outing.CarGUID).Select(e => e.MultitileGroup).Distinct()
                .FirstOrDefault(c => !IsVisiting(c) && IsParked(vm, c));
            if (car == null || !IsParked(vm, car)) return;
            var plan = PlanFor(vm, car);
            if (plan.Problem != RouteProblem.None) return;
            BringIn(vm, sim, car, plan);
        }

        /// <summary>At the lot they drove to: their car pulls up at the kerb nearest them, and they get out.</summary>
        private static void ArriveVisiting(VM vm, VMAvatar sim, Outing outing)
        {
            var road = FindRoad(vm, out _);
            if (road == null) return; //(no road: they arrive as the game has them)
            var arch = vm.Context.Architecture;
            float across = road.Value.AlongX ? sim.Position.y / 16f : sim.Position.x / 16f;
            float along = road.Value.AlongX ? sim.Position.x / 16f : sim.Position.y / 16f;
            //the lot is on the side of the road the Sim is on (if they are on the road itself, the side with more of the lot)
            int lotSide;
            if (across < road.Value.First) lotSide = -1;
            else if (across > road.Value.Last + 1) lotSide = 1;
            else lotSide = (road.Value.First > (road.Value.AlongX ? arch.Height : arch.Width) - 1 - road.Value.Last) ? -1 : 1;

            VMMultitileGroup car = null;
            DrivePlan placed = null;
            try
            {
                //at the far kerb, out of the way of the game's own vehicles, which pull up on the lot's side; on a straight
                //stretch of road, clear of junctions; as near them as that allows, and further along if something is there
                Func<int, int, bool> isRoad = (x, y) => x >= 0 && y >= 0 && x < arch.Width && y < arch.Height && RoadFloors.Contains(arch.GetFloor((short)x, (short)y, 1).Pattern);
                var kerbs = DrivePlan.KerbsNear(road.Value, -lotSide, along, isRoad, Shared.CarWidth, Shared.CarLength, arch.Width, arch.Height, Settings.ReverseCars);
                foreach (var plan in kerbs.Take(12))
                {
                    var lead = DrivePlan.LeadFromCentre(plan.HomeX, plan.HomeY, plan.HomeHeading, Shared.CarWidth, Shared.CarLength);
                    var pos = LotTilePos.FromBigTile((short)Math.Floor(lead.X), (short)Math.Floor(lead.Y), 1);
                    var dir = Directions[plan.HomeHeading];
                    car ??= vm.Context.CreateObjectInstance(outing.CarGUID, LotTilePos.OUT_OF_WORLD, dir);
                    if (car == null || car.Objects.Count == 0) return;
                    //(as the game puts its carpool on the road: not by the player's rules)
                    if (car.ChangePosition(pos, dir, vm.Context, VMPlaceRequestFlags.Default).Status == VMPlacementError.Success) { placed = plan; break; }
                }
            }
            catch (Exception) { placed = null; }
            if (placed == null)
            {
                if (car != null) { try { car.Delete(vm.Context); } catch (Exception) { } }
                return;
            }
            //a visitor: not the household's to move, sell or drive anywhere but home
            car.BaseObject.SetAttribute(VisitorAttribute, 1);
            foreach (var part in car.Objects)
            {
                var flags = (VMMovementFlags)part.GetValue(VMStackObjectVariable.MovementFlags);
                part.SetValue(VMStackObjectVariable.MovementFlags, (short)(flags & ~(VMMovementFlags.PlayersCanMove | VMMovementFlags.PlayersCanDelete)));
            }
            BringIn(vm, sim, car, placed);
        }

        /// <summary>Hide the Sim, and have the car drive in along its way back with them, park, and let them out at its door.</summary>
        private static void BringIn(VM vm, VMAvatar sim, VMMultitileGroup car, DrivePlan plan)
        {
            var commute = new Commute { Car = car, Plan = plan, SimGUID = sim.Object.OBJ.GUID, SimName = sim.Name };
            CommutesOf(vm).Add(commute);
            StartHome(commute, CommuteState.Riding);
            Show(sim, false);
            //held while the car comes in, then out at its door (Ride Home), after whatever the lot has them doing
            car.BaseObject.PushUserInteraction(RideHomeInteraction, sim, vm.Context, false);
        }

        /// <summary>Take away cars left at the kerb by an earlier visit (in a lot saved then). Once, when a lot is first run.</summary>
        private static void RemoveLeftoverVisitors(VM vm)
        {
            var left = vm.Entities.Where(e => e is VMGameObject && !e.Dead && IsCarGUID(GUIDOf(e))).Select(e => e.MultitileGroup).Distinct()
                .Where(c => c != null && IsVisiting(c) && !CommutesOf(vm).Any(m => m.Car == c)).ToList();
            foreach (var car in left)
            {
                try { car.Delete(vm.Context); } catch (Exception) { }
            }
        }
    }
}
