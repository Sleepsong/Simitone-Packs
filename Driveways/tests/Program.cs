/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using Driveways;
using System;
using System.Collections.Generic;
using System.Linq;

// Checks the pack's route planning (Route.cs), which uses nothing from the engine.
// Usage: dotnet run --project Driveways/tests     (exits with 1 if anything fails)

int failed = 0, passed = 0;
void Check(bool ok, string what)
{
    if (ok) passed++;
    else { failed++; Console.WriteLine("FAILED: " + what); }
}
bool Near(float a, float b, float tolerance = 0.001f) => Math.Abs(a - b) <= tolerance;

const int W = 2, L = 4; //the car, in tiles
const int Lot = 64;

// ---- finding the road ----

{
    var south = Road.Find((x, y) => y >= 57 && y <= 61, Lot, Lot);
    Check(south != null && south.Value.AlongX && south.Value.First == 57 && south.Value.Last == 61, "a road along the south side is found, with its rows");

    var west = Road.Find((x, y) => x >= 2 && x <= 6, Lot, Lot);
    Check(west != null && !west.Value.AlongX && west.Value.First == 2 && west.Value.Last == 6, "a road along the west side is found, with its columns");

    Check(Road.Find((x, y) => false, Lot, Lot) == null, "a lot with no road tiles has no road");
    Check(Road.Find((x, y) => x < 6 && y < 6, Lot, Lot) == null, "a small patch of road tiles is not a road");

    //a few stray road-coloured tiles on the lawn do not move the road
    var stray = Road.Find((x, y) => (y >= 57 && y <= 61) || (x == 20 && y == 20), Lot, Lot);
    Check(stray != null && stray.Value.AlongX && stray.Value.First == 57 && stray.Value.Last == 61, "stray road tiles are ignored");

    //a break in the road's rows: the wider run wins
    var split = Road.Find((x, y) => (y >= 50 && y <= 51) || (y >= 57 && y <= 61), Lot, Lot);
    Check(split != null && split.Value.First == 57 && split.Value.Last == 61, "of two strips, the wider is the road");
}

// ---- the car's body and its lead tile ----

for (int heading = 0; heading < 4; heading++)
{
    //the middles of the car's tiles, as the engine lays a multi-tile object out: each tile's offset turned by the heading
    var tiles = (from x in Enumerable.Range(0, W) from y in Enumerable.Range(0, L) select DrivePlan.Rotate(x, y, heading)).ToList();
    var meanX = 10.5f + tiles.Average(t => t.X); var meanY = 20.5f + tiles.Average(t => t.Y);
    var centre = DrivePlan.BodyCentre(10.5f, 20.5f, heading, W, L);
    Check(Near(centre.X, meanX) && Near(centre.Y, meanY), $"heading {heading}: the body's middle is the middle of its tiles");
    var lead = DrivePlan.LeadFromCentre(centre.X, centre.Y, heading, W, L);
    Check(Near(lead.X, 10.5f) && Near(lead.Y, 20.5f), $"heading {heading}: the lead tile is found again from the body's middle");

    //the nose is ahead of the tail
    var ahead = DrivePlan.HeadingVector(heading);
    var noseTile = DrivePlan.Rotate(0, 0, heading); var tailTile = DrivePlan.Rotate(0, L - 1, heading);
    Check((noseTile.X - tailTile.X) * ahead.X + (noseTile.Y - tailTile.Y) * ahead.Y > 0, $"heading {heading}: the lead tile is at the front");
}

// ---- a trip: house north of a road along the south side, car nose-in (facing north, away from the road) ----

List<Pose> Run(Path path, out int ticks, out float fastest, out float slowest)
{
    var poses = new List<Pose>();
    float s = 0; ticks = 0; fastest = 0; slowest = float.MaxValue;
    while (s < path.Length && ticks < 100000)
    {
        var v = path.SpeedAt(s);
        fastest = Math.Max(fastest, v); slowest = Math.Min(slowest, v);
        s += v; ticks++;
        poses.Add(path.PoseAt(s));
    }
    return poses;
}

{
    var road = new Road { AlongX = true, First = 57, Last = 61 };
    var plan = DrivePlan.Make(30.5f, 40.5f, 0, W, L, road, Lot, Lot);
    Check(plan.Problem == RouteProblem.None, "south road: a car pointing away from the road can be driven");
    Check(Near(plan.HomeX, 31) && Near(plan.HomeY, 42), "south road: the body's middle");

    var a = plan.Out.Legs[0]; var b = plan.Out.Legs[1];
    Check(Near(a.DX, 0) && Near(a.DY, 1) && a.Heading == 0, "south road: it backs out towards the road, still facing the house");
    Check(Near(a.Length, 16.25f), "south road: it stops in the near lane (a quarter of the way across a five tile road)");
    Check(b.Heading == 3 && Near(b.DX, -1) && Near(b.DY, 0), "south road: it drives off to the west, the house on its right");
    var end = plan.Out.PoseAt(plan.Out.Length);
    Check(end.X + L / 2f < 0 && Near(end.Y, 58.25f), "south road: it ends wholly off the lot, in its lane");

    var start = plan.Back.PoseAt(0);
    Check(start.X - L / 2f > Lot && Near(start.Y, 58.25f) && start.Heading == 3, "south road: it comes back from the other end of the road, the same way round");
    var home = plan.Back.PoseAt(plan.Back.Length);
    Check(Near(home.X, plan.HomeX) && Near(home.Y, plan.HomeY) && home.Heading == plan.HomeHeading, "south road: it ends where it was parked, facing as it was");

    Check(plan.Corridor.Count == 2 * 17, "south road: the corridor is two tiles wide from the back of the car to the road");
    Check(plan.Corridor.Contains((30, 40)) && plan.Corridor.Contains((31, 56)) && !plan.Corridor.Any(t => t.Y >= 57 || t.Y < 40 || t.X < 30 || t.X > 31), "south road: the corridor's tiles");

    var outPoses = Run(plan.Out, out var outTicks, out var outFast, out var outSlow);
    Check(outTicks > 100 && outTicks < 1500, $"south road: the way out takes a sensible time ({outTicks} ticks)");
    Check(outFast <= DrivePlan.RoadSpeed + 0.0001f && outSlow >= Path.CrawlSpeed - 0.0001f, "south road: never faster than the road speed, never stopped");
    Check(plan.Out.SpeedAt(0) < 0.02f && plan.Out.SpeedAt(8) <= DrivePlan.DrivewaySpeed + 0.0001f, "south road: it pulls away slowly, and keeps to the driveway speed on the driveway");
    Check(plan.Out.SpeedAt(plan.Out.Length - 0.1f) > DrivePlan.RoadSpeed * 0.9f, "south road: it is at speed when it leaves the lot");

    var backPoses = Run(plan.Back, out var backTicks, out var backFast, out _);
    Check(backTicks > 100 && backTicks < 1500, $"south road: the way back takes a sensible time ({backTicks} ticks)");
    Check(plan.Back.SpeedAt(plan.Back.Length - 0.01f) < 0.03f, "south road: it is nearly stopped as it parks");
    //no jumps: each tick moves the car no further than its speed allows
    bool smooth = true; Pose last = plan.Back.PoseAt(0);
    foreach (var pose in backPoses) { if (Math.Abs(pose.X - last.X) + Math.Abs(pose.Y - last.Y) > DrivePlan.RoadSpeed + 0.001f) smooth = false; last = pose; }
    Check(smooth, "south road: the car never jumps");
    //on the driveway legs the body stays inside the corridor's two columns
    Check(outPoses.Where(p => p.Heading == 0).All(p => Near(p.X, 31)), "south road: it stays on the driveway until the road");
}

// ---- the same lot with the car nose-out (facing the road): it drives out forwards and comes back in reverse ----

{
    var road = new Road { AlongX = true, First = 57, Last = 61 };
    var plan = DrivePlan.Make(31.5f, 43.5f, 2, W, L, road, Lot, Lot); //facing south, the lead tile is the front left: south east corner
    Check(plan.Problem == RouteProblem.None && Near(plan.HomeX, 31) && Near(plan.HomeY, 42), "south road, nose out: the same body from the other lead tile");
    Check(plan.Out.Legs[0].Heading == 2 && plan.Back.Legs[1].Heading == 2, "south road, nose out: it keeps facing the road on the driveway");
    Check(plan.Corridor.Count == 2 * 17, "south road, nose out: the same corridor");
}

// ---- road along the north side ----

{
    var road = new Road { AlongX = true, First = 2, Last = 6 };
    var plan = DrivePlan.Make(31.5f, 23.5f, 2, W, L, road, Lot, Lot); //body's middle (31, 22)
    Check(plan.Problem == RouteProblem.None, "north road: can be driven");
    Check(Near(plan.Out.Legs[0].DY, -1) && Near(plan.Out.Legs[0].Length, 22 - 5.75f), "north road: out to the near lane, on the road's south half");
    Check(plan.Out.Legs[1].Heading == 1, "north road: it drives off to the east, the house on its right");
    Check(plan.Out.PoseAt(plan.Out.Length).X - L / 2f > Lot, "north road: it ends off the east edge");
    Check(plan.Corridor.Count == 2 * 17 && plan.Corridor.All(t => t.Y >= 7 && t.Y <= 23), "north road: the corridor runs from the road to the back of the car");
}

// ---- road along the west side (a north-south road) ----

{
    var road = new Road { AlongX = false, First = 2, Last = 6 };
    var plan = DrivePlan.Make(20.5f, 30.5f, 3, W, L, road, Lot, Lot); //facing west, towards the road
    Check(plan.Problem == RouteProblem.None, "west road: a car facing the road can be driven");
    var centre = DrivePlan.BodyCentre(20.5f, 30.5f, 3, W, L);
    Check(Near(plan.Out.Legs[0].DX, -1) && Near(plan.Out.Legs[0].DY, 0), "west road: out to the west");
    Check(plan.Out.Legs[1].Heading == 0, "west road: it drives off to the north, the house on its right");
    Check(plan.Out.PoseAt(plan.Out.Length).Y + L / 2f < 0, "west road: it ends off the north edge");
    Check(plan.Corridor.All(t => t.X >= 7 && Math.Abs(t.Y + 0.5f - centre.Y) <= 1) && plan.Corridor.Select(t => t.Y).Distinct().Count() == 2, "west road: the corridor is two rows wide");

    var east = new Road { AlongX = false, First = 57, Last = 61 };
    var plan2 = DrivePlan.Make(20.5f, 30.5f, 3, W, L, east, Lot, Lot);
    Check(plan2.Problem == RouteProblem.None && plan2.Out.Legs[1].Heading == 2, "east road: it drives off to the south, the house on its right");
}

// ---- cars that cannot be driven ----

{
    var road = new Road { AlongX = true, First = 57, Last = 61 };
    Check(DrivePlan.Make(30.5f, 40.5f, 1, W, L, road, Lot, Lot).Problem == RouteProblem.NotFacingRoad, "a car parked along the road is refused");
    Check(DrivePlan.Make(30.5f, 40.5f, 3, W, L, road, Lot, Lot).Problem == RouteProblem.NotFacingRoad, "...whichever way round");
    Check(DrivePlan.Make(30.5f, 55.5f, 0, W, L, road, Lot, Lot).Problem == RouteProblem.OnRoad, "a car sticking out into the road is refused");
    Check(DrivePlan.Make(30.5f, 53.5f, 0, W, L, road, Lot, Lot).Problem == RouteProblem.None, "a car parked right up to the road is fine");
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
