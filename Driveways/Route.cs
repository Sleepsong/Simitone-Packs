/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;

namespace Driveways
{
    // Where a car goes, worked out with plain numbers and nothing from the engine, so it can be checked on its own
    // (see the tests folder).
    //
    // Everything here is in "tile space": tile (x, y) covers x..x+1 and y..y+1, so its middle is (x+0.5, y+0.5).
    // y grows to the south, as on the lot. A heading is a number of quarter turns clockwise from north:
    // 0 north (0,-1), 1 east (1,0), 2 south (0,1), 3 west (-1,0).

    public enum RouteProblem
    {
        None,
        /// <summary>The lot has no road the pack can recognise.</summary>
        NoRoad,
        /// <summary>The car is on the road already, or sits across it.</summary>
        OnRoad,
        /// <summary>The car is parked along the road instead of pointing at it or away from it.</summary>
        NotFacingRoad,
        /// <summary>Not on the ground floor.</summary>
        NotGroundLevel,
        /// <summary>A wall or an object is in the way between the car and the road.</summary>
        Blocked,
        /// <summary>Part of the way to the road, or the ground under the car, has no driveway on it.</summary>
        NoDriveway,
    }

    /// <summary>The strip of road tiles along one side of a lot.</summary>
    public struct Road
    {
        /// <summary>True when the road runs west-east (it fills rows of the lot), false when it runs north-south.</summary>
        public bool AlongX;
        /// <summary>The first and last row (or column) the road covers.</summary>
        public int First, Last;
        /// <summary>
        /// The first and last row (or column) of the whole strip along that side of the lot that cannot be built on:
        /// the road and the verge beside it. Not set when the road was found by its tiles alone.
        /// </summary>
        public int? VergeFirst, VergeLast;

        /// <summary>Where the strip that cannot be built on starts and ends (the road itself, when no verge is known).</summary>
        public int StripFirst => VergeFirst ?? First;
        public int StripLast => VergeLast ?? Last;

        /// <summary>
        /// Add the verge to a road found by its tiles. Between the house and the road a Sims 1 lot has a pavement,
        /// one tile wide, and a tile of grass. pavement says whether a row (or column, when the road runs north-south)
        /// is the pavement; it is looked for within three tiles of the road, on the side away from the nearer edge
        /// of the lot. The strip then runs from the pavement to that edge. With no pavement found, it is the road
        /// and everything between it and the edge.
        /// </summary>
        public Road WithVerge(Func<int, bool> pavement, int lotWidth, int lotHeight)
        {
            var road = this;
            int across = AlongX ? lotHeight : lotWidth;
            bool roadAtHighEdge = (across - 1 - Last) <= First;
            int houseSide = roadAtHighEdge ? First : Last;
            for (int i = 1; i <= 3; i++)
            {
                int line = roadAtHighEdge ? First - i : Last + i;
                if (line < 0 || line >= across) break;
                if (pavement(line)) houseSide = line;
            }
            if (roadAtHighEdge) { road.VergeFirst = houseSide; road.VergeLast = across - 1; }
            else { road.VergeFirst = 0; road.VergeLast = houseSide; }
            return road;
        }

        /// <summary>
        /// Find the road from where the player cannot build. A lot has a narrow margin round it that cannot be built
        /// on, and a much deeper one along the side with the road. The deepest margin is the strip; the road is taken
        /// to be its outer part, with two tiles of verge left on the house's side when the strip is deep enough.
        /// asphalt, if given, says which rows (or columns) of the strip are known to be road, and is used instead
        /// when it names any.
        /// </summary>
        public static Road? FindByMargin(Func<int, int, bool> cannotBuild, int width, int height, Func<bool, int, bool> asphalt = null)
        {
            //a row or column is margin when nearly all of it cannot be built on
            bool Row(int y) { int n = 0; for (int x = 0; x < width; x++) if (cannotBuild(x, y)) n++; return n * 10 >= width * 9; }
            bool Col(int x) { int n = 0; for (int y = 0; y < height; y++) if (cannotBuild(x, y)) n++; return n * 10 >= height * 9; }
            int north = 0, south = 0, west = 0, east = 0;
            while (north < height && Row(north)) north++;
            while (south < height && Row(height - 1 - south)) south++;
            while (west < width && Col(west)) west++;
            while (east < width && Col(width - 1 - east)) east++;
            //(a lot where nothing can be built has no road side to speak of)
            if (north >= height || west >= width) return null;

            var deepest = Math.Max(Math.Max(north, south), Math.Max(west, east));
            if (deepest < 3) return null;
            Road road;
            if (south == deepest) road = new Road { AlongX = true, VergeFirst = height - south, VergeLast = height - 1 };
            else if (north == deepest) road = new Road { AlongX = true, VergeFirst = 0, VergeLast = north - 1 };
            else if (east == deepest) road = new Road { AlongX = false, VergeFirst = width - east, VergeLast = width - 1 };
            else road = new Road { AlongX = false, VergeFirst = 0, VergeLast = west - 1 };

            int first = road.VergeFirst.Value, last = road.VergeLast.Value;
            bool atLowEdge = first == 0;
            //the rows of the strip known to be road, if any
            int knownFirst = -1, knownLast = -1;
            if (asphalt != null)
                for (int i = first; i <= last; i++)
                    if (asphalt(road.AlongX, i)) { if (knownFirst == -1) knownFirst = i; knownLast = i; }
            if (knownFirst != -1) { road.First = knownFirst; road.Last = knownLast; }
            else
            {
                int verge = (deepest > 4) ? 2 : 0;
                road.First = atLowEdge ? first : first + verge;
                road.Last = atLowEdge ? last - verge : last;
            }
            return road;
        }

        /// <summary>
        /// Find the road from the lot's tiles. A row or column counts as road when at least half of it is road tiles,
        /// and the road is the widest unbroken run of those.
        /// </summary>
        public static Road? Find(Func<int, int, bool> isRoad, int width, int height)
        {
            var rows = new int[height];
            var cols = new int[width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (isRoad(x, y)) { rows[y]++; cols[x]++; }

            var byRow = WidestRun(rows, width);
            var byCol = WidestRun(cols, height);
            if (byRow == null && byCol == null) return null;
            //(a lot with roads on two sides: take the wider one)
            var useRows = byCol == null || (byRow != null && (byRow.Value.Item2 - byRow.Value.Item1) >= (byCol.Value.Item2 - byCol.Value.Item1));
            var run = useRows ? byRow.Value : byCol.Value;
            return new Road { AlongX = useRows, First = run.Item1, Last = run.Item2 };
        }

        private static (int, int)? WidestRun(int[] counts, int lineLength)
        {
            (int, int)? best = null;
            int start = -1;
            for (int i = 0; i <= counts.Length; i++)
            {
                var road = i < counts.Length && counts[i] * 2 >= lineLength && counts[i] > 0;
                if (road && start == -1) start = i;
                else if (!road && start != -1)
                {
                    if (best == null || (i - 1 - start) > (best.Value.Item2 - best.Value.Item1)) best = (start, i - 1);
                    start = -1;
                }
            }
            return best;
        }
    }

    /// <summary>Where a driveway goes: it always lies against the strip by the road, pointing at it.</summary>
    public static class DrivewaySnap
    {
        /// <summary>
        /// The place for a driveway the player is holding over the tile (x, y). The driveway is width tiles wide and
        /// length long; facing north its lead tile is its north west corner, and facing east (heading 1) it lies on
        /// its side with the lead tile at its north east corner. It slides along the road with the cursor and nowhere else.
        /// </summary>
        public static (int X, int Y, int Heading) Place(Road road, int x, int y, int width, int length, int lotWidth, int lotHeight)
        {
            bool roadAtHighEdge = road.StripFirst > 0;
            if (road.AlongX)
            {
                //upright: columns x..x+width-1, rows y..y+length-1
                int leadY = roadAtHighEdge ? road.StripFirst - length : road.StripLast + 1;
                return (Math.Max(0, Math.Min(x, lotWidth - width)), leadY, 0);
            }
            else
            {
                //on its side: columns x-length+1..x, rows y..y+width-1
                int leadX = roadAtHighEdge ? road.StripFirst - 1 : road.StripLast + length;
                return (leadX, Math.Max(0, Math.Min(y, lotHeight - width)), 1);
            }
        }
    }

    /// <summary>Where the car is at one moment: the middle of its body, and which way its nose points.</summary>
    public struct Pose
    {
        public float X, Y;
        public int Heading;
    }

    /// <summary>One straight piece of a drive.</summary>
    public struct Leg
    {
        public float X, Y;          //where it starts
        public float DX, DY;        //the direction of travel (one tile long)
        public float Length;        //in tiles
        public int Heading;         //which way the car's nose points on this leg (it may be reversing)
        public float StartSpeed, TopSpeed, EndSpeed; //in tiles for each tick
    }

    /// <summary>A drive: straight legs, one after the other.</summary>
    public class Path
    {
        /// <summary>How quickly a car picks up and loses speed, in tiles for each tick, each tick.</summary>
        public const float Acceleration = 0.004f;
        /// <summary>A car that is moving at all moves at least this fast, so a leg always ends.</summary>
        public const float CrawlSpeed = 0.01f;

        public List<Leg> Legs = new List<Leg>();

        public float Length
        {
            get
            {
                float total = 0;
                foreach (var leg in Legs) total += leg.Length;
                return total;
            }
        }

        private Leg LegAt(float distance, out float along)
        {
            along = Math.Max(0, distance);
            for (int i = 0; i < Legs.Count; i++)
            {
                if (along <= Legs[i].Length || i == Legs.Count - 1)
                {
                    along = Math.Min(along, Legs[i].Length);
                    return Legs[i];
                }
                along -= Legs[i].Length;
            }
            return default(Leg);
        }

        /// <summary>The car's position after it has gone this far along the drive.</summary>
        public Pose PoseAt(float distance)
        {
            var leg = LegAt(distance, out var along);
            return new Pose { X = leg.X + leg.DX * along, Y = leg.Y + leg.DY * along, Heading = leg.Heading };
        }

        /// <summary>How far the car moves in one tick from here: it speeds up out of each leg's start and slows into its end.</summary>
        public float SpeedAt(float distance)
        {
            var leg = LegAt(distance, out var along);
            var up = (float)Math.Sqrt(leg.StartSpeed * leg.StartSpeed + 2 * Acceleration * along);
            var down = (float)Math.Sqrt(leg.EndSpeed * leg.EndSpeed + 2 * Acceleration * (leg.Length - along));
            return Math.Max(CrawlSpeed, Math.Min(leg.TopSpeed, Math.Min(up, down)));
        }
    }

    /// <summary>A whole trip for one parked car: the way out to the road and off the lot, and the way back.</summary>
    public class DrivePlan
    {
        public const float DrivewaySpeed = 0.05f;   //tiles for each tick: about a tile and a half a second
        public const float RoadSpeed = 0.2f;
        public const float CornerSpeed = 0.03f;

        public RouteProblem Problem;
        public Path Out, Back;
        /// <summary>The middle of the parked car.</summary>
        public float HomeX, HomeY;
        public int HomeHeading;
        /// <summary>
        /// The tiles the car's body passes over between where it is parked and the road, the ones it is parked on
        /// included. They must be clear, and (where the player can build) paved with driveway.
        /// </summary>
        public List<(int X, int Y)> Corridor = new List<(int, int)>();

        public static (float X, float Y) HeadingVector(int heading)
        {
            switch (((heading % 4) + 4) % 4)
            {
                case 0: return (0, -1);
                case 1: return (1, 0);
                case 2: return (0, 1);
                default: return (-1, 0);
            }
        }

        /// <summary>Turn an offset in the car's own space (as a multi-tile object is laid out when it faces north) by its heading.</summary>
        public static (float X, float Y) Rotate(float x, float y, int heading)
        {
            switch (((heading % 4) + 4) % 4)
            {
                case 0: return (x, y);
                case 1: return (-y, x);
                case 2: return (-x, -y);
                default: return (y, -x);
            }
        }

        /// <summary>
        /// The middle of a car's body from its lead tile. Facing north, the lead tile is the front left one: the body
        /// reaches carWidth tiles to the east of it and carLength tiles to the south.
        /// </summary>
        public static (float X, float Y) BodyCentre(float leadX, float leadY, int heading, int carWidth, int carLength)
        {
            var off = Rotate((carWidth - 1) / 2f, (carLength - 1) / 2f, heading);
            return (leadX + off.X, leadY + off.Y);
        }

        /// <summary>The middle of the lead tile for a body whose middle is here: the opposite of BodyCentre.</summary>
        public static (float X, float Y) LeadFromCentre(float centreX, float centreY, int heading, int carWidth, int carLength)
        {
            var off = Rotate((carWidth - 1) / 2f, (carLength - 1) / 2f, heading);
            return (centreX - off.X, centreY - off.Y);
        }

        /// <summary>
        /// Plan a trip. (leadX, leadY) is the middle of the car's lead tile, in tile space; the car is carWidth tiles
        /// wide and carLength long. The lot is lotWidth by lotHeight tiles.
        /// </summary>
        public static DrivePlan Make(float leadX, float leadY, int heading, int carWidth, int carLength, Road road, int lotWidth, int lotHeight)
        {
            var plan = new DrivePlan();
            var centre = BodyCentre(leadX, leadY, heading, carWidth, carLength);
            plan.HomeX = centre.X; plan.HomeY = centre.Y; plan.HomeHeading = heading;

            //work along one axis: "across" is the way to the road, "along" is the way the road runs
            float cAcross = road.AlongX ? centre.Y : centre.X;
            float cAlong = road.AlongX ? centre.X : centre.Y;
            int lotAlong = road.AlongX ? lotWidth : lotHeight;
            bool nosePointsAcross = road.AlongX ? (heading % 2 == 0) : (heading % 2 == 1);
            float halfAcross = (nosePointsAcross ? carLength : carWidth) / 2f;
            float halfAlong = (nosePointsAcross ? carWidth : carLength) / 2f;

            //the edge, on the car's side, of the strip that cannot be built on; the road's own edge; and which way they are (+1 or -1)
            float stripNear, roadNear, toRoad;
            if (cAcross + halfAcross <= road.StripFirst + 0.001f) { toRoad = 1; stripNear = road.StripFirst; roadNear = road.First; }
            else if (cAcross - halfAcross >= road.StripLast + 1 - 0.001f) { toRoad = -1; stripNear = road.StripLast + 1; roadNear = road.Last + 1; }
            else { plan.Problem = RouteProblem.OnRoad; return plan; }

            if (!nosePointsAcross) { plan.Problem = RouteProblem.NotFacingRoad; return plan; }

            //the lane on the house's side of the road
            float roadWidth = road.Last - road.First + 1;
            float laneIn = Math.Min(Math.Max(roadWidth / 4f, carWidth / 2f + 0.25f), roadWidth / 2f);
            float lane = roadNear + toRoad * laneIn;

            //towards the road, and along it: traffic keeps the house on its right
            (float X, float Y) across = road.AlongX ? (0f, toRoad) : (toRoad, 0f);
            (float X, float Y) travel = (-across.Y, across.X);
            int travelHeading = HeadingOf(travel);
            float travelSign = road.AlongX ? travel.X : travel.Y;

            float reach = Math.Abs(lane - cAcross);
            //far enough past the edge of the lot that none of the car is left on it
            float margin = carLength / 2f + 1;
            float exitAt = travelSign > 0 ? lotAlong + margin : -margin;
            float enterAt = travelSign > 0 ? -margin : lotAlong + margin;

            float cornerX = centre.X + across.X * reach, cornerY = centre.Y + across.Y * reach;

            plan.Out = new Path();
            plan.Out.Legs.Add(new Leg
            {
                X = centre.X, Y = centre.Y, DX = across.X, DY = across.Y, Length = reach, Heading = heading,
                StartSpeed = 0, TopSpeed = DrivewaySpeed, EndSpeed = CornerSpeed
            });
            plan.Out.Legs.Add(new Leg
            {
                X = cornerX, Y = cornerY, DX = travel.X, DY = travel.Y, Length = Math.Abs(exitAt - cAlong), Heading = travelHeading,
                StartSpeed = CornerSpeed, TopSpeed = RoadSpeed, EndSpeed = RoadSpeed
            });

            float runIn = Math.Abs(cAlong - enterAt);
            plan.Back = new Path();
            plan.Back.Legs.Add(new Leg
            {
                X = cornerX - travel.X * runIn, Y = cornerY - travel.Y * runIn, DX = travel.X, DY = travel.Y, Length = runIn, Heading = travelHeading,
                StartSpeed = RoadSpeed, TopSpeed = RoadSpeed, EndSpeed = CornerSpeed
            });
            plan.Back.Legs.Add(new Leg
            {
                X = cornerX, Y = cornerY, DX = -across.X, DY = -across.Y, Length = reach, Heading = heading,
                StartSpeed = CornerSpeed, TopSpeed = DrivewaySpeed, EndSpeed = 0
            });

            //the tiles between the back of the parked car and the road
            float from = cAcross - toRoad * halfAcross, to = stripNear;
            int acrossFirst = (int)Math.Floor(Math.Min(from, to) + 0.001f), acrossLast = (int)Math.Ceiling(Math.Max(from, to) - 0.001f) - 1;
            int alongFirst = (int)Math.Floor(cAlong - halfAlong + 0.001f), alongLast = (int)Math.Ceiling(cAlong + halfAlong - 0.001f) - 1;
            for (int a = acrossFirst; a <= acrossLast; a++)
                for (int b = alongFirst; b <= alongLast; b++)
                    plan.Corridor.Add(road.AlongX ? (b, a) : (a, b));

            return plan;
        }

        private static int HeadingOf((float X, float Y) v)
        {
            if (v.Y < -0.5f) return 0;
            if (v.X > 0.5f) return 1;
            if (v.Y > 0.5f) return 2;
            return 3;
        }
    }
}
