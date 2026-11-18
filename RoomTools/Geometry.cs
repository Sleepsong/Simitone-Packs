/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoomTools
{
    // The walls and floors of a lot, worked on as plain data. Nothing in this file touches the running game, so all of
    // it can be checked without a copy of The Sims (see tests/). Engine.cs puts it to work on a lot.
    //
    // How the engine keeps walls (FreeSO's VMArchitectureTools), which everything here follows:
    //  - Each tile has four sides. Its x-min side is "top left" (TL), y-min is "top right" (TR), x-max is "bottom right"
    //    (BR), y-max is "bottom left" (BL).
    //  - A wall along x, between tiles (x, y-1) and (x, y), is kept twice: as TR on (x, y), which also holds its style and
    //    the wallpaper facing (x, y), and as BL on (x, y-1), with the wallpaper facing that tile.
    //  - A wall along y, between (x-1, y) and (x, y), is TL on (x, y), with its style and wallpaper, and BR on (x-1, y).
    //  - A diagonal wall fills a tile of its own, and no straight wall may touch that tile. The tile's two halves have a
    //    floor each, kept in its TopLeftPattern and TopLeftStyle, and the wall's two faces in BottomLeftPattern and
    //    BottomRightPattern; its style is TopRightStyle.

    /// <summary>Somewhere to read and write walls and floors: a lot in the game, or plain arrays in the tests.</summary>
    public interface IGrid
    {
        int Width { get; }
        int Height { get; }
        int Stories { get; }
        /// <summary>The wall tile at a place. Outside the lot, or on a floor there is not: an empty one.</summary>
        WallTile GetWall(int x, int y, int level);
        void SetWall(int x, int y, int level, WallTile wall);
        ushort GetFloor(int x, int y, int level);
        void SetFloor(int x, int y, int level, ushort pattern);
    }

    /// <summary>Walls and floors in arrays, laid out as the engine lays them out: [level - 1][y * width + x].</summary>
    public class ArrayGrid : IGrid
    {
        public int Width { get; }
        public int Height { get; }
        public int Stories { get; }
        public WallTile[][] Walls;
        public FloorTile[][] Floors;

        public ArrayGrid(int width, int height, int stories = 5)
        {
            Width = width; Height = height; Stories = stories;
            Walls = new WallTile[stories][];
            Floors = new FloorTile[stories][];
            for (int i = 0; i < stories; i++)
            {
                Walls[i] = new WallTile[width * height];
                Floors[i] = new FloorTile[width * height];
            }
        }

        public ArrayGrid(int width, int height, WallTile[][] walls, FloorTile[][] floors)
        {
            Width = width; Height = height; Stories = walls.Length;
            Walls = walls; Floors = floors;
        }

        private bool In(int x, int y, int level) => x >= 0 && y >= 0 && x < Width && y < Height && level >= 1 && level <= Stories;
        public WallTile GetWall(int x, int y, int level) => In(x, y, level) ? Walls[level - 1][y * Width + x] : default;
        public void SetWall(int x, int y, int level, WallTile wall) { if (In(x, y, level)) Walls[level - 1][y * Width + x] = wall; }
        public ushort GetFloor(int x, int y, int level) => In(x, y, level) ? Floors[level - 1][y * Width + x].Pattern : (ushort)0;
        public void SetFloor(int x, int y, int level, ushort pattern) { if (In(x, y, level)) Floors[level - 1][y * Width + x] = new FloorTile { Pattern = pattern }; }
    }

    /// <summary>
    /// Changes made on top of another grid, kept aside: what a lot would look like after a change, without changing it.
    /// Used to check a change before making it, and to show it while it is being made.
    /// </summary>
    public class OverlayGrid : IGrid
    {
        private readonly IGrid Base;
        public readonly Dictionary<(int x, int y, int level), WallTile> Walls = new Dictionary<(int, int, int), WallTile>();
        public readonly Dictionary<(int x, int y, int level), ushort> Floors = new Dictionary<(int, int, int), ushort>();

        public OverlayGrid(IGrid under) { Base = under; }
        public int Width => Base.Width;
        public int Height => Base.Height;
        public int Stories => Base.Stories;

        private bool In(int x, int y, int level) => x >= 0 && y >= 0 && x < Width && y < Height && level >= 1 && level <= Stories;
        public WallTile GetWall(int x, int y, int level) => Walls.TryGetValue((x, y, level), out var w) ? w : Base.GetWall(x, y, level);
        public void SetWall(int x, int y, int level, WallTile wall) { if (In(x, y, level)) Walls[(x, y, level)] = wall; }
        public ushort GetFloor(int x, int y, int level) => Floors.TryGetValue((x, y, level), out var f) ? f : Base.GetFloor(x, y, level);
        public void SetFloor(int x, int y, int level, ushort pattern) { if (In(x, y, level)) Floors[(x, y, level)] = pattern; }

        /// <summary>Make the changes for real, on the grid underneath (or another one laid out the same).</summary>
        public void WriteTo(IGrid target)
        {
            foreach (var w in Walls) target.SetWall(w.Key.x, w.Key.y, w.Key.level, w.Value);
            foreach (var f in Floors) target.SetFloor(f.Key.x, f.Key.y, f.Key.level, f.Value);
        }
    }

    /// <summary>
    /// One straight wall's place: the side between two tiles, on a floor. Along x (Vertical false) it is between tiles
    /// (X, Y - 1) and (X, Y); along y (Vertical true) between (X - 1, Y) and (X, Y). (X, Y) is its "plus" side.
    /// </summary>
    public readonly struct Edge : IEquatable<Edge>
    {
        public readonly bool Vertical;
        public readonly int X, Y, Level;

        public Edge(bool vertical, int x, int y, int level) { Vertical = vertical; X = x; Y = y; Level = level; }
        public static Edge AlongX(int x, int y, int level) => new Edge(false, x, y, level);
        public static Edge AlongY(int x, int y, int level) => new Edge(true, x, y, level);

        public (int x, int y) Plus => (X, Y);
        public (int x, int y) Minus => Vertical ? (X - 1, Y) : (X, Y - 1);
        public Edge Moved(int dx, int dy, int dLevel = 0) => new Edge(Vertical, X + dx, Y + dy, Level + dLevel);
        public Edge OnLevel(int level) => new Edge(Vertical, X, Y, level);

        public bool Equals(Edge o) => Vertical == o.Vertical && X == o.X && Y == o.Y && Level == o.Level;
        public override bool Equals(object obj) => obj is Edge e && Equals(e);
        public override int GetHashCode() => ((X * 397 + Y) * 31 + Level) * 2 + (Vertical ? 1 : 0);
        public override string ToString() => (Vertical ? "y-wall " : "x-wall ") + X + "," + Y + " floor " + Level;
    }

    /// <summary>How a wall looks: its style (a wall, a fence, a half wall...) and the wallpaper on each face.</summary>
    public struct EdgeLook
    {
        public ushort Style;
        public ushort PlusPattern, MinusPattern;
    }

    public static class Walls
    {
        private static WallSegments MainSeg(Edge e) => e.Vertical ? WallSegments.TopLeft : WallSegments.TopRight;
        private static WallSegments SubSeg(Edge e) => e.Vertical ? WallSegments.BottomRight : WallSegments.BottomLeft;

        public static bool InLot(IGrid g, int x, int y) => x >= 0 && y >= 0 && x < g.Width && y < g.Height;

        /// <summary>Both tiles of a wall's place are on the lot (the engine draws no wall at its edge).</summary>
        public static bool InLot(IGrid g, Edge e) => InLot(g, e.Plus.x, e.Plus.y) && InLot(g, e.Minus.x, e.Minus.y);

        public static bool Has(IGrid g, Edge e)
        {
            if ((g.GetWall(e.Plus.x, e.Plus.y, e.Level).Segments & MainSeg(e)) != 0) return true;
            return (g.GetWall(e.Minus.x, e.Minus.y, e.Level).Segments & SubSeg(e)) != 0;
        }

        public static EdgeLook Read(IGrid g, Edge e)
        {
            var plus = g.GetWall(e.Plus.x, e.Plus.y, e.Level);
            var minus = g.GetWall(e.Minus.x, e.Minus.y, e.Level);
            return e.Vertical
                ? new EdgeLook { Style = plus.TopLeftStyle, PlusPattern = plus.TopLeftPattern, MinusPattern = minus.BottomRightPattern }
                : new EdgeLook { Style = plus.TopRightStyle, PlusPattern = plus.TopRightPattern, MinusPattern = minus.BottomLeftPattern };
        }

        /// <summary>Put up a wall (or change how one looks), on both of its tiles.</summary>
        public static void Write(IGrid g, Edge e, EdgeLook look)
        {
            var (px, py) = e.Plus;
            if (InLot(g, px, py))
            {
                var plus = g.GetWall(px, py, e.Level);
                plus.Segments |= MainSeg(e);
                if (e.Vertical) { plus.TopLeftStyle = look.Style; plus.TopLeftPattern = look.PlusPattern; }
                else { plus.TopRightStyle = look.Style; plus.TopRightPattern = look.PlusPattern; }
                g.SetWall(px, py, e.Level, plus);
            }
            var (mx, my) = e.Minus;
            if (InLot(g, mx, my))
            {
                var minus = g.GetWall(mx, my, e.Level);
                minus.Segments |= SubSeg(e);
                if (e.Vertical) minus.BottomRightPattern = look.MinusPattern;
                else minus.BottomLeftPattern = look.MinusPattern;
                g.SetWall(mx, my, e.Level, minus);
            }
        }

        /// <summary>Change the wallpaper on one face of a wall that is there.</summary>
        public static void SetFace(IGrid g, Edge e, bool plusFace, ushort pattern)
        {
            var look = Read(g, e);
            if (plusFace) look.PlusPattern = pattern; else look.MinusPattern = pattern;
            Write(g, e, look);
        }

        /// <summary>Take a wall down. As the engine's own wall tool does, only the wall goes: what was kept for it is left.</summary>
        public static void Remove(IGrid g, Edge e)
        {
            var (px, py) = e.Plus;
            if (InLot(g, px, py))
            {
                var plus = g.GetWall(px, py, e.Level);
                plus.Segments &= ~MainSeg(e);
                g.SetWall(px, py, e.Level, plus);
            }
            var (mx, my) = e.Minus;
            if (InLot(g, mx, my))
            {
                var minus = g.GetWall(mx, my, e.Level);
                minus.Segments &= ~SubSeg(e);
                g.SetWall(mx, my, e.Level, minus);
            }
        }

        public static bool IsDiagonal(WallTile w) => (w.Segments & WallSegments.AnyDiag) != 0;

        /// <summary>Every straight wall on a floor.</summary>
        public static IEnumerable<Edge> All(IGrid g, int level)
        {
            for (int y = 0; y <= g.Height; y++)
                for (int x = 0; x <= g.Width; x++)
                {
                    var h = Edge.AlongX(x, y, level);
                    if (x < g.Width && Has(g, h)) yield return h;
                    var v = Edge.AlongY(x, y, level);
                    if (y < g.Height && Has(g, v)) yield return v;
                }
        }
    }

    /// <summary>A tile, or one half of a tile with a diagonal wall across it.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X, Y, Half;
        public Cell(int x, int y, int half) { X = x; Y = y; Half = half; }
        public bool Equals(Cell o) => X == o.X && Y == o.Y && Half == o.Half;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => (X * 397 + Y) * 2 + Half;
    }

    /// <summary>
    /// The lot's floors divided into the spaces walls make, as the game's rooms are, but with only walls (and fences)
    /// dividing them: a room with a pool in it, or with no floor in places, is one space here. A space that reaches the
    /// edge of the lot is outdoors.
    /// </summary>
    public class Regions
    {
        // the sides of a tile, in order: x-min (TL), y-min (TR), x-max (BR), y-max (BL)
        public static readonly int[] SideDX = { -1, 0, 1, 0 };
        public static readonly int[] SideDY = { 0, -1, 0, 1 };
        public static int Opposite(int side) => (side + 2) & 3;

        /// <summary>The wall's place on a side of a tile.</summary>
        public static Edge SideEdge(int x, int y, int level, int side)
        {
            switch (side)
            {
                case 0: return Edge.AlongY(x, y, level);
                case 1: return Edge.AlongX(x, y, level);
                case 2: return Edge.AlongY(x + 1, y, level);
                default: return Edge.AlongX(x, y + 1, level);
            }
        }

        /// <summary>Whether the room is on a wall's plus side, when the wall is on this side of one of its tiles.</summary>
        public static bool SideIsPlus(int side) => side < 2;

        // a diagonal along x = -y (HorizontalDiag) cuts a tile into the half at its (0,0) corner (sides TL, TR) and the
        // half at (1,1) (BR, BL). one along x = y (VerticalDiag) into the half at (1,0) (TR, BR) and at (0,1) (TL, BL).
        public static bool HalfHasSide(WallTile w, int half, int side)
        {
            if ((w.Segments & WallSegments.HorizontalDiag) != 0) return (half == 0) == (side == 0 || side == 1);
            if ((w.Segments & WallSegments.VerticalDiag) != 0) return (half == 0) == (side == 1 || side == 2);
            return half == 0;
        }

        public static int HalfFacing(WallTile w, int side)
        {
            if ((w.Segments & WallSegments.HorizontalDiag) != 0) return (side == 0 || side == 1) ? 0 : 1;
            if ((w.Segments & WallSegments.VerticalDiag) != 0) return (side == 1 || side == 2) ? 0 : 1;
            return 0;
        }

        /// <summary>Which half of a tile a point in it (0 to 1 across the tile) is in.</summary>
        public static int HalfAt(WallTile w, float fx, float fy)
        {
            if ((w.Segments & WallSegments.HorizontalDiag) != 0) return (fx + fy > 1) ? 1 : 0;
            if ((w.Segments & WallSegments.VerticalDiag) != 0) return (fx - fy > 0) ? 0 : 1;
            return 0;
        }

        public readonly int Width, Height, Level;
        /// <summary>The space each cell is in: [(y * width + x) * 2 + half], or -1 for the second half of a whole tile.</summary>
        public readonly int[] Label;
        public readonly List<bool> Outside = new List<bool>();
        public readonly List<int> Size = new List<int>();

        public int LabelOf(int x, int y, int half) => (x < 0 || y < 0 || x >= Width || y >= Height) ? -1 : Label[(y * Width + x) * 2 + half];
        public int LabelOf(Cell c) => LabelOf(c.X, c.Y, c.Half);

        public Regions(IGrid g, int level)
        {
            Width = g.Width; Height = g.Height; Level = level;
            Label = new int[Width * Height * 2];
            for (int i = 0; i < Label.Length; i++) Label[i] = -2; //not yet reached
            var stack = new Stack<Cell>();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var wall = g.GetWall(x, y, level);
                    var halves = Walls.IsDiagonal(wall) ? 2 : 1;
                    if (halves == 1) Label[(y * Width + x) * 2 + 1] = -1;
                    for (int half = 0; half < halves; half++)
                    {
                        if (Label[(y * Width + x) * 2 + half] != -2) continue;
                        var id = Outside.Count;
                        var outside = false;
                        var size = 0;
                        Label[(y * Width + x) * 2 + half] = id;
                        stack.Push(new Cell(x, y, half));
                        while (stack.Count > 0)
                        {
                            var c = stack.Pop();
                            size++;
                            if (c.X == 0 || c.Y == 0 || c.X == Width - 1 || c.Y == Height - 1) outside = true;
                            var cw = g.GetWall(c.X, c.Y, level);
                            for (int side = 0; side < 4; side++)
                            {
                                if (!HalfHasSide(cw, c.Half, side)) continue;
                                if (Walls.Has(g, SideEdge(c.X, c.Y, level, side))) continue;
                                int nx = c.X + SideDX[side], ny = c.Y + SideDY[side];
                                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                                var nh = HalfFacing(g.GetWall(nx, ny, level), Opposite(side));
                                var ni = (ny * Width + nx) * 2 + nh;
                                if (Label[ni] != -2) continue;
                                Label[ni] = id;
                                stack.Push(new Cell(nx, ny, nh));
                            }
                        }
                        Outside.Add(outside);
                        Size.Add(size);
                    }
                }
            }
        }
    }

    /// <summary>A wall on a room's edge.</summary>
    public struct RoomEdge
    {
        public Edge Edge;
        /// <summary>The room is on the wall's plus side.</summary>
        public bool RoomOnPlus;
        /// <summary>On the room's edge, with another room on the far side (not outdoors): it stays for that room.</summary>
        public bool Shared;
        /// <summary>Inside the room, with the room on both sides (a wall standing in it).</summary>
        public bool Interior;
    }

    /// <summary>A room: one walled space on a floor, with the walls round it and in it.</summary>
    public class RoomShape
    {
        public int Level;
        public int Label;
        public List<Cell> Cells = new List<Cell>();
        public HashSet<(int x, int y)> Tiles = new HashSet<(int, int)>();
        public Dictionary<Edge, RoomEdge> Edges = new Dictionary<Edge, RoomEdge>();
        /// <summary>Tiles with a diagonal wall the room has only one half of, whose other half is another room's.</summary>
        public HashSet<(int x, int y)> SharedDiagonals = new HashSet<(int, int)>();
        public int MinX, MinY, MaxX, MaxY;
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;

        /// <summary>The walls that go with the room when it moves: all but those it shares with another room.</summary>
        public IEnumerable<Edge> OwnWalls => Edges.Values.Where(x => !x.Shared).Select(x => x.Edge);

        /// <summary>The room a cell is in, or null if it is outdoors (or off the lot).</summary>
        public static RoomShape At(IGrid g, Regions regions, Cell cell)
        {
            var id = regions.LabelOf(cell);
            if (id < 0 || regions.Outside[id]) return null;
            var room = new RoomShape { Level = regions.Level, Label = id, MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue };
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    for (int half = 0; half < 2; half++)
                    {
                        if (regions.LabelOf(x, y, half) != id) continue;
                        room.Cells.Add(new Cell(x, y, half));
                        room.Tiles.Add((x, y));
                        room.MinX = Math.Min(room.MinX, x); room.MaxX = Math.Max(room.MaxX, x);
                        room.MinY = Math.Min(room.MinY, y); room.MaxY = Math.Max(room.MaxY, y);
                    }
                }
            }
            var level = regions.Level;
            foreach (var c in room.Cells)
            {
                var w = g.GetWall(c.X, c.Y, level);
                if (Walls.IsDiagonal(w))
                {
                    var other = regions.LabelOf(c.X, c.Y, 1 - c.Half);
                    if (other != id && other >= 0 && !regions.Outside[other]) room.SharedDiagonals.Add((c.X, c.Y));
                }
                for (int side = 0; side < 4; side++)
                {
                    if (!Regions.HalfHasSide(w, c.Half, side)) continue;
                    var e = Regions.SideEdge(c.X, c.Y, level, side);
                    if (!Walls.Has(g, e)) continue;
                    int nx = c.X + Regions.SideDX[side], ny = c.Y + Regions.SideDY[side];
                    var far = regions.LabelOf(nx, ny, Walls.InLot(g, nx, ny) ? Regions.HalfFacing(g.GetWall(nx, ny, level), Regions.Opposite(side)) : 0);
                    var interior = far == id;
                    var shared = !interior && far >= 0 && !regions.Outside[far];
                    if (room.Edges.TryGetValue(e, out var seen) && seen.Interior) continue;
                    room.Edges[e] = new RoomEdge { Edge = e, RoomOnPlus = Regions.SideIsPlus(side), Shared = shared, Interior = interior };
                }
            }
            return room;
        }
    }

    /// <summary>One tile of a plan: its floor, or its diagonal wall and the floors either side of it.</summary>
    public struct PlanTile
    {
        public int X, Y, Level;
        public ushort Floor;
        public bool Diagonal;
        /// <summary>HorizontalDiag or VerticalDiag.</summary>
        public WallSegments DiagonalKind;
        public ushort DiagonalStyle;
        /// <summary>The floors of the two halves (half 0, half 1) and the wallpaper facing each.</summary>
        public ushort Floor0, Floor1, Face0, Face1;
        /// <summary>Which halves belong to the plan (bit 0: half 0, bit 1: half 1). A whole tile is 3.</summary>
        public int Halves;
    }

    public struct PlanEdge
    {
        public Edge Edge;
        public EdgeLook Look;
        /// <summary>On the plan's edge (the room is on one side only), rather than inside it.</summary>
        public bool Boundary;
        public bool RoomOnPlus;
    }

    /// <summary>
    /// Walls and floors to put on a lot: a room, picked up or saved, or a whole lot. Its tiles and walls are counted
    /// from its own corner, so it can be put down anywhere, and turned.
    /// </summary>
    public class Plan
    {
        /// <summary>Its size in tiles.</summary>
        public int Width, Height;
        /// <summary>How many floors it has (a room has one, at level 1 of the plan).</summary>
        public int Levels = 1;
        public List<PlanTile> Tiles = new List<PlanTile>();
        public List<PlanEdge> Edges = new List<PlanEdge>();

        public static PlanTile ReadDiagonal(WallTile w, int x, int y, int level, int halves)
        {
            //half 0 of a HorizontalDiag tile is the TopLeftStyle half, half 0 of a VerticalDiag one the TopLeftPattern half
            var h = (w.Segments & WallSegments.HorizontalDiag) != 0;
            return new PlanTile
            {
                X = x, Y = y, Level = level, Diagonal = true, Halves = halves,
                DiagonalKind = h ? WallSegments.HorizontalDiag : WallSegments.VerticalDiag,
                DiagonalStyle = w.TopRightStyle,
                Floor0 = h ? w.TopLeftStyle : w.TopLeftPattern,
                Floor1 = h ? w.TopLeftPattern : w.TopLeftStyle,
                Face0 = h ? w.BottomRightPattern : w.BottomLeftPattern,
                Face1 = h ? w.BottomLeftPattern : w.BottomRightPattern,
            };
        }

        public static WallTile WriteDiagonal(WallTile w, PlanTile t)
        {
            var h = t.DiagonalKind == WallSegments.HorizontalDiag;
            w.Segments = t.DiagonalKind;
            w.TopRightStyle = t.DiagonalStyle;
            w.TopLeftStyle = h ? t.Floor0 : t.Floor1;
            w.TopLeftPattern = h ? t.Floor1 : t.Floor0;
            w.BottomRightPattern = h ? t.Face0 : t.Face1;
            w.BottomLeftPattern = h ? t.Face1 : t.Face0;
            return w;
        }

        /// <summary>A room's walls and floors, from the corner of the box round it.</summary>
        public static Plan Of(IGrid g, RoomShape room)
        {
            var plan = new Plan { Width = room.Width, Height = room.Height, Levels = 1 };
            foreach (var t in room.Tiles)
            {
                var w = g.GetWall(t.x, t.y, room.Level);
                if (Walls.IsDiagonal(w))
                {
                    var halves = 0;
                    foreach (var c in room.Cells) if (c.X == t.x && c.Y == t.y) halves |= 1 << c.Half;
                    plan.Tiles.Add(ReadDiagonal(w, t.x - room.MinX, t.y - room.MinY, 1, halves));
                }
                else plan.Tiles.Add(new PlanTile { X = t.x - room.MinX, Y = t.y - room.MinY, Level = 1, Floor = g.GetFloor(t.x, t.y, room.Level), Halves = 3 });
            }
            foreach (var re in room.Edges.Values)
            {
                plan.Edges.Add(new PlanEdge
                {
                    Edge = new Edge(re.Edge.Vertical, re.Edge.X - room.MinX, re.Edge.Y - room.MinY, 1),
                    Look = Walls.Read(g, re.Edge),
                    Boundary = !re.Interior,
                    RoomOnPlus = re.RoomOnPlus,
                });
            }
            return plan;
        }

        /// <summary>Everything built on part of a lot, on every floor, counted from the lot's own corner.</summary>
        public static Plan OfArea(IGrid g, Func<int, int, bool> inArea)
        {
            var plan = new Plan { Width = g.Width, Height = g.Height, Levels = g.Stories };
            for (int level = 1; level <= g.Stories; level++)
            {
                for (int y = 0; y < g.Height; y++)
                {
                    for (int x = 0; x < g.Width; x++)
                    {
                        if (!inArea(x, y)) continue;
                        var w = g.GetWall(x, y, level);
                        if (Walls.IsDiagonal(w)) plan.Tiles.Add(ReadDiagonal(w, x, y, level, 3));
                        else
                        {
                            var floor = g.GetFloor(x, y, level);
                            if (floor != 0) plan.Tiles.Add(new PlanTile { X = x, Y = y, Level = level, Floor = floor, Halves = 3 });
                        }
                    }
                }
                foreach (var e in Walls.All(g, level))
                {
                    if (!inArea(e.Plus.x, e.Plus.y) && !inArea(e.Minus.x, e.Minus.y)) continue;
                    plan.Edges.Add(new PlanEdge { Edge = e, Look = Walls.Read(g, e) });
                }
            }
            return plan;
        }

        // ---- turning ----

        /// <summary>A quarter turn of a tile, within a plan of this height.</summary>
        public static (int x, int y) TurnTile(int x, int y, int height) => (height - 1 - y, x);

        /// <summary>A quarter turn of a point, in sixteenths of a tile (as objects are placed), within a plan of this height.</summary>
        public static (int x, int y) TurnPoint(int x, int y, int height) => (height * 16 - y, x);

        /// <summary>
        /// A quarter turn of a wall. One along x becomes one along y and the other way round; a wall along x has its
        /// faces swapped, as its plus side (+y) turns to face -x. The same turn as the engine's own (VMLotRotate).
        /// </summary>
        public static (Edge edge, EdgeLook look, bool plusSwapped) TurnEdge(Edge e, EdgeLook look, int height)
        {
            if (!e.Vertical)
                return (Edge.AlongY(height - e.Y, e.X, e.Level), new EdgeLook { Style = look.Style, PlusPattern = look.MinusPattern, MinusPattern = look.PlusPattern }, true);
            return (Edge.AlongX(height - e.Y - 1, e.X, e.Level), look, false);
        }

        /// <summary>
        /// A quarter turn of a diagonal tile. A HorizontalDiag becomes a VerticalDiag with its halves as they were; a
        /// VerticalDiag becomes a HorizontalDiag with its halves swapped (as VMLotRotate turns them).
        /// </summary>
        public static PlanTile TurnDiagonal(PlanTile t)
        {
            if (t.DiagonalKind == WallSegments.HorizontalDiag)
            {
                t.DiagonalKind = WallSegments.VerticalDiag;
                return t;
            }
            t.DiagonalKind = WallSegments.HorizontalDiag;
            (t.Floor0, t.Floor1) = (t.Floor1, t.Floor0);
            (t.Face0, t.Face1) = (t.Face1, t.Face0);
            t.Halves = ((t.Halves & 1) << 1) | ((t.Halves >> 1) & 1);
            return t;
        }

        /// <summary>The plan turned a number of quarter turns, the same way as an object turned from north to east.</summary>
        public Plan Turned(int quarters)
        {
            quarters = ((quarters % 4) + 4) % 4;
            var plan = this;
            for (int i = 0; i < quarters; i++) plan = plan.TurnedOnce();
            return plan;
        }

        private Plan TurnedOnce()
        {
            var p = new Plan { Width = Height, Height = Width, Levels = Levels };
            foreach (var t in Tiles)
            {
                var n = t.Diagonal ? TurnDiagonal(t) : t;
                (n.X, n.Y) = TurnTile(t.X, t.Y, Height);
                p.Tiles.Add(n);
            }
            foreach (var pe in Edges)
            {
                var (edge, look, swapped) = TurnEdge(pe.Edge, pe.Look, Height);
                p.Edges.Add(new PlanEdge { Edge = edge, Look = look, Boundary = pe.Boundary, RoomOnPlus = swapped ? !pe.RoomOnPlus : pe.RoomOnPlus });
            }
            return p;
        }
    }

    /// <summary>Picking a room up, and putting one down: the walls and floors. (Objects are Engine.cs's.)</summary>
    public static class Rooms
    {
        /// <summary>Take a room's walls and floors off the lot, leaving the walls it shares with other rooms.</summary>
        public static void Clear(IGrid g, RoomShape room)
        {
            foreach (var e in room.OwnWalls) Walls.Remove(g, e);
            foreach (var t in room.Tiles)
            {
                var w = g.GetWall(t.x, t.y, room.Level);
                if (Walls.IsDiagonal(w))
                {
                    var both = room.Cells.Count(c => c.X == t.x && c.Y == t.y) == 2;
                    var cut = ReadHalves(w);
                    if (room.SharedDiagonals.Contains(t))
                    {
                        //the diagonal stays for the room on its other side; this side's floor goes
                        var mine = room.Cells.First(c => c.X == t.x && c.Y == t.y).Half;
                        if (mine == 0) cut.Floor0 = 0; else cut.Floor1 = 0;
                        g.SetWall(t.x, t.y, room.Level, Plan.WriteDiagonal(w, cut));
                    }
                    else
                    {
                        //outdoors on the other side (or the room on both): the diagonal goes, and the tile has outdoors' floor
                        var keep = both ? (ushort)0 : (room.Cells.Any(c => c.X == t.x && c.Y == t.y && c.Half == 0) ? cut.Floor1 : cut.Floor0);
                        g.SetWall(t.x, t.y, room.Level, new WallTile { Segments = 0 });
                        g.SetFloor(t.x, t.y, room.Level, keep);
                    }
                }
                else g.SetFloor(t.x, t.y, room.Level, 0);
            }
        }

        private static PlanTile ReadHalves(WallTile w) => Plan.ReadDiagonal(w, 0, 0, 0, 3);

        /// <summary>
        /// Why a plan cannot be put down with its corner at (ox, oy) on a floor, or null if it can. Only walls and floors
        /// are looked at here: objects and Sims in the way are Engine.cs's to check.
        /// </summary>
        public static string CheckPlace(IGrid g, Plan plan, int ox, int oy, int level, Func<int, int, int, bool> buildable)
        {
            var mine = new HashSet<(int, int, int)>();
            foreach (var t in plan.Tiles) mine.Add((t.X + ox, t.Y + oy, t.Level + level - 1));
            foreach (var t in plan.Tiles)
            {
                int x = t.X + ox, y = t.Y + oy, l = t.Level + level - 1;
                if (!Walls.InLot(g, x, y) || l < 1 || l > g.Stories) return "It doesn't fit on the lot there.";
                if (!buildable(x, y, l)) return "That's outside the part of the lot you can build on.";
                if (Walls.IsDiagonal(g.GetWall(x, y, l))) return "A diagonal wall is in the way.";
            }
            var planEdges = new HashSet<Edge>(plan.Edges.Select(x => x.Edge.Moved(ox, oy, level - 1)));
            foreach (var pe in plan.Edges)
            {
                var e = pe.Edge.Moved(ox, oy, level - 1);
                if (!Walls.InLot(g, e)) return "It doesn't fit on the lot there.";
                if (!buildable(e.Plus.x, e.Plus.y, e.Level) || !buildable(e.Minus.x, e.Minus.y, e.Level)) return "That's outside the part of the lot you can build on.";
                //a wall may join one already there on the room's edge, but not run into a diagonal
                if (Walls.IsDiagonal(g.GetWall(e.Plus.x, e.Plus.y, e.Level)) || Walls.IsDiagonal(g.GetWall(e.Minus.x, e.Minus.y, e.Level))) return "A diagonal wall is in the way.";
            }
            //no other wall may stand inside the room where it would go
            foreach (var t in plan.Tiles)
            {
                int x = t.X + ox, y = t.Y + oy, l = t.Level + level - 1;
                for (int side = 0; side < 4; side++)
                {
                    int nx = x + Regions.SideDX[side], ny = y + Regions.SideDY[side];
                    if (!mine.Contains((nx, ny, l))) continue;
                    var e = Regions.SideEdge(x, y, l, side);
                    if (Walls.Has(g, e) && !planEdges.Contains(e)) return "A wall is in the way.";
                }
            }
            return null;
        }

        /// <summary>
        /// Put a plan down with its corner at (ox, oy). A wall on the plan's edge where one stands already is joined to
        /// it: the wall that is there stays as it is, with the plan's wallpaper on the face toward the plan.
        /// </summary>
        public static void Place(IGrid g, Plan plan, int ox, int oy, int level)
        {
            foreach (var t in plan.Tiles)
            {
                int x = t.X + ox, y = t.Y + oy, l = t.Level + level - 1;
                if (!t.Diagonal)
                {
                    g.SetFloor(x, y, l, t.Floor);
                    continue;
                }
                var cut = t;
                if (t.Halves != 3)
                {
                    //the half that isn't the plan's keeps the floor that was there
                    var there = g.GetFloor(x, y, l);
                    if ((t.Halves & 1) == 0) cut.Floor0 = there;
                    if ((t.Halves & 2) == 0) cut.Floor1 = there;
                }
                g.SetFloor(x, y, l, 0);
                g.SetWall(x, y, l, Plan.WriteDiagonal(g.GetWall(x, y, l), cut));
            }
            foreach (var pe in plan.Edges)
            {
                var e = pe.Edge.Moved(ox, oy, level - 1);
                if (pe.Boundary && Walls.Has(g, e))
                {
                    Walls.SetFace(g, e, pe.RoomOnPlus, pe.RoomOnPlus ? pe.Look.PlusPattern : pe.Look.MinusPattern);
                    continue;
                }
                Walls.Write(g, e, pe.Look);
            }
        }
    }

    /// <summary>A straight run of wall between corners: what Move Wall drags.</summary>
    public class WallRun
    {
        /// <summary>The run is along y (its walls are Edge.Vertical), at x = Across; or along x, at y = Across.</summary>
        public bool Vertical;
        public int Across;
        /// <summary>From one corner to the other, along the run: its walls are at From to To - 1.</summary>
        public int From, To;
        public int Level;

        public int Length => To - From;

        /// <summary>A wall in line with the run, a distance along it and at some place across.</summary>
        public Edge Along(int along, int across) => Vertical ? Edge.AlongY(across, along, Level) : Edge.AlongX(along, across, Level);
        /// <summary>A wall crossing the run's line at a corner, between across and across + 1.</summary>
        public Edge Crossing(int corner, int across) => Vertical ? Edge.AlongX(across, corner, Level) : Edge.AlongY(corner, across, Level);
        /// <summary>A tile, by its place along the run and across it.</summary>
        public (int x, int y) Tile(int along, int across) => Vertical ? (across, along) : (along, across);

        public IEnumerable<Edge> Walls(int across) => Enumerable.Range(From, Length).Select(a => Along(a, across));

        /// <summary>Whether a corner (a place where walls meet) has a wall crossing the run's line at it.</summary>
        private static bool Crossed(IGrid g, WallRun run, int corner, int across) =>
            RoomTools.Walls.Has(g, run.Crossing(corner, across - 1)) || RoomTools.Walls.Has(g, run.Crossing(corner, across));

        /// <summary>The run a wall is part of: as far along its line as it goes without another wall meeting it.</summary>
        public static WallRun Of(IGrid g, Edge e)
        {
            if (!RoomTools.Walls.Has(g, e)) return null;
            var run = new WallRun { Vertical = e.Vertical, Across = e.Vertical ? e.X : e.Y, Level = e.Level };
            var at = e.Vertical ? e.Y : e.X;
            run.From = at; run.To = at + 1;
            while (!Crossed(g, run, run.From, run.Across) && RoomTools.Walls.Has(g, run.Along(run.From - 1, run.Across))) run.From--;
            while (!Crossed(g, run, run.To, run.Across) && RoomTools.Walls.Has(g, run.Along(run.To, run.Across))) run.To++;
            return run;
        }
    }

    /// <summary>A wall run dragged across: the walls that change, and the floor that follows.</summary>
    public class WallMove
    {
        public WallRun Run;
        public int Delta;
        public string Problem;
        public List<Edge> Removed = new List<Edge>();
        public List<(Edge edge, EdgeLook look)> Added = new List<(Edge, EdgeLook)>();
        public List<(int x, int y, ushort floor)> Floors = new List<(int, int, ushort)>();
        /// <summary>The tiles the run passes over, which change from one side of it to the other.</summary>
        public List<(int x, int y)> Swept = new List<(int, int)>();

        public const int Furthest = 16;

        /// <summary>Work out dragging a run by delta tiles across (toward +x or +y if positive), without doing it.</summary>
        public static WallMove Plan(IGrid g, WallRun run, int delta, Func<int, int, int, bool> buildable)
        {
            var m = new WallMove { Run = run, Delta = delta };
            if (delta == 0) return m;
            if (Math.Abs(delta) > Furthest) { m.Problem = "That's too far to move a wall in one go."; return m; }
            var level = run.Level;
            var from = run.Across;
            var to = from + delta;
            int low = Math.Min(from, to), high = Math.Max(from, to);

            //where the run goes must be free, on the lot
            foreach (var e in run.Walls(to))
            {
                if (!Walls.InLot(g, e)) { m.Problem = "It would go off the lot."; return m; }
                if (!buildable(e.Plus.x, e.Plus.y, level) || !buildable(e.Minus.x, e.Minus.y, level)) { m.Problem = "That's outside the part of the lot you can build on."; return m; }
                if (Walls.Has(g, e)) { m.Problem = "Another wall is in the way."; return m; }
            }
            //the tiles passed over: no diagonal walls, no walls across them (but at the run's two ends), nothing built above
            for (int c = low; c < high; c++)
            {
                for (int a = run.From; a < run.To; a++)
                {
                    var (x, y) = run.Tile(a, c);
                    if (!Walls.InLot(g, x, y)) { m.Problem = "It would go off the lot."; return m; }
                    if (Walls.IsDiagonal(g.GetWall(x, y, level))) { m.Problem = "A diagonal wall is in the way."; return m; }
                    if (level < g.Stories && (g.GetFloor(x, y, level + 1) != 0 || g.GetWall(x, y, level + 1).Segments != 0)) { m.Problem = "Something is built above where the wall would go."; return m; }
                    m.Swept.Add((x, y));
                }
            }
            for (int c = low + 1; c < high; c++)
                foreach (var e in run.Walls(c))
                    if (Walls.Has(g, e)) { m.Problem = "Another wall is in the way."; return m; }
            for (int a = run.From + 1; a < run.To; a++)
                for (int c = low; c < high; c++)
                    if (Walls.Has(g, run.Crossing(a, c))) { m.Problem = "A wall is in the way."; return m; }
            if (level < g.Stories)
            {
                foreach (var e in run.Walls(from))
                {
                    var above = e.OnLevel(level + 1);
                    if (Walls.Has(g, above) || g.GetFloor(e.Plus.x, e.Plus.y, level + 1) != 0 || g.GetFloor(e.Minus.x, e.Minus.y, level + 1) != 0) { m.Problem = "Something is built on top of this wall."; return m; }
                }
            }

            //each end: the wall it meets is stretched, shortened, or left as it is
            foreach (var corner in new[] { run.From, run.To })
            {
                //tiles past the end with a diagonal wall would be left hanging
                foreach (var c in new[] { from - 1, from })
                {
                    var (tx, ty) = run.Tile(corner == run.From ? corner - 1 : corner, c);
                    if (Walls.IsDiagonal(g.GetWall(tx, ty, level))) { m.Problem = "A diagonal wall meets this wall."; return m; }
                }
                var behind = run.Crossing(corner, delta > 0 ? from - 1 : from);
                var hasBehind = Walls.Has(g, behind);
                var between = Enumerable.Range(low, high - low).Select(c => run.Crossing(corner, c)).ToList();
                var standing = between.Count(e => Walls.Has(g, e));
                if (standing == 0)
                {
                    if (!hasBehind) continue; //a free end: it just moves
                    var look = Walls.Read(g, behind);
                    foreach (var e in between)
                    {
                        if (!Walls.InLot(g, e)) { m.Problem = "It would go off the lot."; return m; }
                        m.Added.Add((e, look));
                    }
                }
                else if (standing == between.Count)
                {
                    if (hasBehind) continue; //the wall it meets goes on past: it stays
                    m.Removed.AddRange(between);
                }
                else { m.Problem = "The wall at the corner is in the way."; return m; }
            }

            //the tiles passed over get the floor of the side that grows
            var growing = (delta > 0) ? from - 1 : from;
            for (int c = low; c < high; c++)
            {
                for (int a = run.From; a < run.To; a++)
                {
                    var (x, y) = run.Tile(a, c);
                    var (rx, ry) = run.Tile(a, growing);
                    m.Floors.Add((x, y, g.GetFloor(rx, ry, level)));
                }
            }
            return m;
        }

        /// <summary>Make the move on a grid (one it was worked out for).</summary>
        public void Apply(IGrid g)
        {
            if (Problem != null || Delta == 0) return;
            var looks = Run.Walls(Run.Across).Select(e => Walls.Read(g, e)).ToList();
            foreach (var e in Run.Walls(Run.Across)) Walls.Remove(g, e);
            foreach (var e in Removed) Walls.Remove(g, e);
            var i = 0;
            foreach (var e in Run.Walls(Run.Across + Delta)) Walls.Write(g, e, looks[i++]);
            foreach (var (e, look) in Added) Walls.Write(g, e, look);
            foreach (var (x, y, floor) in Floors) g.SetFloor(x, y, Run.Level, floor);
        }
    }
}
