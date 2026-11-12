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

// ---- finding the road from where the player cannot build ----

{
    //a lot with a one tile margin all round, and nine tiles that cannot be built on along the south side
    Func<int, int, bool> southLot = (x, y) => x < 1 || y < 1 || x > 62 || y > 54;
    var road = Road.FindByMargin(southLot, Lot, Lot);
    Check(road != null && road.Value.AlongX && road.Value.StripFirst == 55 && road.Value.StripLast == 63, "margin: the deep strip along the south side is found");
    Check(road != null && road.Value.First == 57 && road.Value.Last == 63, "margin: the road is the strip less two tiles of verge on the house's side");

    Func<int, int, bool> northLot = (x, y) => x < 1 || y < 9 || x > 62 || y > 62;
    var north = Road.FindByMargin(northLot, Lot, Lot);
    Check(north != null && north.Value.AlongX && north.Value.StripFirst == 0 && north.Value.StripLast == 8 && north.Value.First == 0 && north.Value.Last == 6, "margin: a strip along the north side, the verge on its south");

    Func<int, int, bool> westLot = (x, y) => x < 9 || y < 1 || x > 62 || y > 62;
    var west = Road.FindByMargin(westLot, Lot, Lot);
    Check(west != null && !west.Value.AlongX && west.Value.StripFirst == 0 && west.Value.StripLast == 8, "margin: a strip along the west side");
    Func<int, int, bool> eastLot = (x, y) => x < 1 || y < 1 || x > 54 || y > 62;
    var east = Road.FindByMargin(eastLot, Lot, Lot);
    Check(east != null && !east.Value.AlongX && east.Value.StripFirst == 55 && east.Value.StripLast == 63, "margin: a strip along the east side");

    Check(Road.FindByMargin((x, y) => x < 1 || y < 1 || x > 62 || y > 62, Lot, Lot) == null, "margin: a lot with only a narrow margin has no road");
    Check(Road.FindByMargin((x, y) => true, Lot, Lot) == null, "margin: a lot where nothing can be built has no road");
    Check(Road.FindByMargin((x, y) => false, Lot, Lot) == null, "margin: a lot where everything can be built has no road");

    //rows known to be road are used in place of the guess
    var known = Road.FindByMargin(southLot, Lot, Lot, (alongX, i) => alongX && i >= 58 && i <= 62);
    Check(known != null && known.Value.First == 58 && known.Value.Last == 62 && known.Value.StripFirst == 55, "margin: road tiles inside the strip say where the road is");

    //a car parked right against the strip drives across the verge to the road
    var plan = DrivePlan.Make(30.5f, 51.5f, 0, W, L, road.Value, Lot, Lot); //body rows 51..54
    Check(plan.Problem == RouteProblem.None, "margin: a car against the strip can be driven");
    Check(plan.Corridor.Count == 2 * 4 && plan.Corridor.All(t => t.Y >= 51 && t.Y <= 54), "margin: only the tiles it stands on need a driveway");
    var lane = plan.Out.Legs[1].Y;
    Check(lane - W / 2f >= 57 && lane + W / 2f <= 64, $"margin: its lane is on the road ({lane})");
    Check(DrivePlan.Make(30.5f, 52.5f, 0, W, L, road.Value, Lot, Lot).Problem == RouteProblem.OnRoad, "margin: a car standing on the strip is refused");
}

// ---- snapping a driveway to the road ----

{
    //the tiles a driveway covers from its lead tile, as the engine lays a multi-tile object out
    List<(int X, int Y)> Tiles((int X, int Y, int Heading) place) =>
        (from x in Enumerable.Range(0, W) from y in Enumerable.Range(0, L)
         let off = DrivePlan.Rotate(x, y, place.Heading)
         select (place.X + (int)Math.Round(off.X), place.Y + (int)Math.Round(off.Y))).ToList();

    var south = new Road { AlongX = true, First = 57, Last = 63, VergeFirst = 55, VergeLast = 63 };
    var a = Tiles(DrivewaySnap.Place(south, 20, 10, W, L, Lot, Lot));
    Check(a.Min(t => t.Y) == 51 && a.Max(t => t.Y) == 54 && a.Min(t => t.X) == 20 && a.Max(t => t.X) == 21, "snap, south road: against the strip, under the cursor's column");
    var b = Tiles(DrivewaySnap.Place(south, 63, 40, W, L, Lot, Lot));
    Check(b.Max(t => t.X) == 63 && b.Max(t => t.Y) == 54, "snap, south road: kept on the lot");

    var north = new Road { AlongX = true, First = 0, Last = 6, VergeFirst = 0, VergeLast = 8 };
    var c = Tiles(DrivewaySnap.Place(north, 20, 40, W, L, Lot, Lot));
    Check(c.Min(t => t.Y) == 9 && c.Max(t => t.Y) == 12, "snap, north road: against the strip");

    var east = new Road { AlongX = false, First = 57, Last = 63, VergeFirst = 55, VergeLast = 63 };
    var d = Tiles(DrivewaySnap.Place(east, 5, 30, W, L, Lot, Lot));
    Check(d.Min(t => t.X) == 51 && d.Max(t => t.X) == 54 && d.Min(t => t.Y) == 30 && d.Max(t => t.Y) == 31, "snap, east road: on its side against the strip, under the cursor's row");

    var west = new Road { AlongX = false, First = 0, Last = 6, VergeFirst = 0, VergeLast = 8 };
    var e = Tiles(DrivewaySnap.Place(west, 40, 30, W, L, Lot, Lot));
    Check(e.Min(t => t.X) == 9 && e.Max(t => t.X) == 12 && e.Count == 8 && e.Distinct().Count() == 8, "snap, west road: on its side against the strip");

    //a car put on a snapped driveway, nose to the road, is ready to go
    var place = DrivewaySnap.Place(south, 20, 10, W, L, Lot, Lot);
    var plan = DrivePlan.Make(place.X + 0.5f, place.Y + 0.5f, 0, W, L, south, Lot, Lot);
    Check(plan.Problem == RouteProblem.None && plan.Corridor.OrderBy(t => t).SequenceEqual(a.OrderBy(t => t)), "snap: a car on the driveway needs exactly the driveway's tiles");
}

// ---- a real lot, as the "driveways" command reported it from the game: 52 by 52, the road along the east side ----

{
    const int Real = 52;
    //tarmac (9) on columns 43 to 49, rows 1 to 50, with dashes of the middle line (11) down column 46, three rows
    //on and three off; the pavement (12) on column 41; a house's floors in the garden
    ushort Floor(int x, int y)
    {
        if (y >= 1 && y <= 50)
        {
            if (x == 41) return 12;
            if (x >= 43 && x <= 49) return (ushort)((x == 46 && ((y - 1) % 6) >= 3) ? 11 : 9);
        }
        if (x >= 16 && x <= 27 && y >= 20 && y <= 30) return 0x15;
        return 0;
    }
    var roadTiles = new HashSet<ushort> { 9, 10, 11 };
    var found = Road.Find((x, y) => roadTiles.Contains(Floor(x, y)), Real, Real);
    Check(found != null && !found.Value.AlongX && found.Value.First == 43 && found.Value.Last == 49, "real lot: the road is columns 43 to 49");
    Check(Road.Find((x, y) => Floor(x, y) == 10 || Floor(x, y) == 11, Real, Real) == null, "real lot: the dashes of the middle line alone are not enough to find it (what 0.1 to 0.3 looked for)");

    var road = found.Value.WithVerge(line => Enumerable.Range(0, Real).Count(y => Floor(line, y) == 12) * 2 >= Real, Real, Real);
    Check(road.StripFirst == 41 && road.StripLast == 51, "real lot: with the pavement and the verge, columns 41 to 51");

    var place = DrivewaySnap.Place(road, 10, 25, W, L, Real, Real);
    var tiles = (from x in Enumerable.Range(0, W) from y in Enumerable.Range(0, L)
                 let off = DrivePlan.Rotate(x, y, place.Heading)
                 select (X: place.X + (int)Math.Round(off.X), Y: place.Y + (int)Math.Round(off.Y))).ToList();
    Check(tiles.Min(t => t.X) == 37 && tiles.Max(t => t.X) == 40 && tiles.Min(t => t.Y) == 25 && tiles.Max(t => t.Y) == 26, "real lot: a driveway lies against the pavement, columns 37 to 40");

    //a car on it, nose to the road (facing east): its lead tile is the front left, the north east corner
    var plan = DrivePlan.Make(40.5f, 25.5f, 1, W, L, road, Real, Real);
    Check(plan.Problem == RouteProblem.None, "real lot: a car on the driveway can be driven");
    Check(plan.Corridor.OrderBy(t => t).SequenceEqual(tiles.OrderBy(t => t)), "real lot: it needs exactly the driveway under it");
    var lane = plan.Out.Legs[1].X;
    Check(lane - W / 2f >= 43 && lane + W / 2f <= 46, $"real lot: it turns into the near half of the road ({lane})");
    Check(plan.Out.Legs[1].Heading == 2 && plan.Out.PoseAt(plan.Out.Length).Y - L / 2f > Real, "real lot: and drives off the south edge");
    var home = plan.Back.PoseAt(plan.Back.Length);
    Check(Near(home.X, 39) && Near(home.Y, 26) && home.Heading == 1, "real lot: and comes home");

    //a road at the low edge, for the same code the other way round
    var westRoad = new Road { AlongX = false, First = 2, Last = 8 }.WithVerge(line => line == 10, Real, Real);
    Check(westRoad.StripFirst == 0 && westRoad.StripLast == 10, "a west road's verge runs from the lot's edge to the pavement");
    var noPavement = new Road { AlongX = true, First = 43, Last = 49 }.WithVerge(line => false, Real, Real);
    Check(noPavement.StripFirst == 43 && noPavement.StripLast == 51, "with no pavement, the strip is the road and what is beyond it");
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
