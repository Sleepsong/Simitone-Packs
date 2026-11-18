/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Room Tools pack that need no copy of The Sims: that its patches fit the engine it is built against,
// its walls and floors (finding rooms, carrying and turning them, dragging walls), checked against the engine's own
// way of turning a lot, and its library files. Run with: dotnet run --project RoomTools/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.LotView.Model;
using HarmonyLib;
using RoomTools;
using Blueprint = RoomTools.Blueprint;

int failures = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failures++; }
Func<int, int, int, bool> anywhere = (x, y, l) => true;

// ---- the patches fit this Simitone ----
{
    var h = new Harmony("test.roomtools");
    var apply = typeof(RoomToolsPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    try { apply.Invoke(null, new object[] { h }); }
    catch (TargetInvocationException e) { Console.WriteLine("FAIL patches: " + e.InnerException); return 1; }
    var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    foreach (var needed in new[] { "VM.SendCommand", "VM.Update", "VMArchitecture.RunCommands", "UIMainPanel.SetMode", "UIBuyBrowsePanel.InitCategory", "UIBuyBrowsePanel.Reset", "UILotControl.Update", "DebugLinesComponent.Draw", "UICatalogItem..ctor" })
        Check("patch on " + needed + " attached", patched.Contains(needed));
    h.UnpatchAll(h.Id);
}

// ---- building blocks ----

// a box of walls round tiles x0..x1-1, y0..y1-1, with wallpaper "inside" facing in, "outside" facing out (a wall already
// there is shared: only its inside face is set), and floor
void Box(IGrid g, int x0, int y0, int x1, int y1, int level, ushort inside, ushort outside, ushort floor)
{
    void Wall(Edge e, bool roomOnPlus)
    {
        if (Walls.Has(g, e)) { Walls.SetFace(g, e, roomOnPlus, inside); return; }
        Walls.Write(g, e, new EdgeLook { Style = 1, PlusPattern = roomOnPlus ? inside : outside, MinusPattern = roomOnPlus ? outside : inside });
    }
    for (int x = x0; x < x1; x++) { Wall(Edge.AlongX(x, y0, level), true); Wall(Edge.AlongX(x, y1, level), false); }
    for (int y = y0; y < y1; y++) { Wall(Edge.AlongY(x0, y, level), true); Wall(Edge.AlongY(x1, y, level), false); }
    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) g.SetFloor(x, y, level, floor);
}

RoomShape RoomAt(IGrid g, int x, int y, int level = 1, int half = 0) => RoomShape.At(g, new Regions(g, level), new Cell(x, y, half));

// what a grid has built, as text, to compare two grids by (offset by dx, dy)
List<string> Built(IGrid g, int dx = 0, int dy = 0, Func<int, int, bool> keep = null)
{
    var lines = new List<string>();
    for (int level = 1; level <= g.Stories; level++)
    {
        foreach (var e in Walls.All(g, level))
        {
            if (keep != null && !(keep(e.Plus.x, e.Plus.y) && keep(e.Minus.x, e.Minus.y))) continue;
            var l = Walls.Read(g, e);
            lines.Add((e.Vertical ? "V" : "H") + (e.X + dx) + "," + (e.Y + dy) + "," + level + " s" + l.Style + " +" + l.PlusPattern + " -" + l.MinusPattern);
        }
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                if (keep != null && !keep(x, y)) continue;
                var w = g.GetWall(x, y, level);
                if (Walls.IsDiagonal(w))
                {
                    var t = Plan.ReadDiagonal(w, x, y, level, 3);
                    lines.Add("D" + (x + dx) + "," + (y + dy) + "," + level + " " + t.DiagonalKind + " s" + t.DiagonalStyle + " f" + t.Floor0 + "/" + t.Floor1 + " p" + t.Face0 + "/" + t.Face1);
                }
                else if (g.GetFloor(x, y, level) != 0) lines.Add("F" + (x + dx) + "," + (y + dy) + "," + level + " " + g.GetFloor(x, y, level));
            }
    }
    lines.Sort(StringComparer.Ordinal);
    return lines;
}

bool Same(List<string> a, List<string> b, string what)
{
    var missing = a.Except(b).Take(5).ToList();
    var extra = b.Except(a).Take(5).ToList();
    if (missing.Count == 0 && extra.Count == 0) return true;
    Console.WriteLine("     " + what + ": expected but not found: " + string.Join("; ", missing) + " | found but not expected: " + string.Join("; ", extra));
    return false;
}

// every wall of a room has its inside wallpaper facing in and its outside one facing out
bool FacesRight(IGrid g, RoomShape room, ushort inside, ushort outside)
{
    foreach (var re in room.Edges.Values)
    {
        if (re.Interior) continue;
        var look = Walls.Read(g, re.Edge);
        var inFace = re.RoomOnPlus ? look.PlusPattern : look.MinusPattern;
        var outFace = re.RoomOnPlus ? look.MinusPattern : look.PlusPattern;
        if (inFace != inside || (!re.Shared && outFace != outside)) { Console.WriteLine("     wrong faces on " + re.Edge + ": in " + inFace + ", out " + outFace); return false; }
    }
    return true;
}

// ---- finding a room ----
{
    var g = new ArrayGrid(24, 24);
    Box(g, 4, 4, 8, 7, 1, 11, 22, 5);
    var room = RoomAt(g, 5, 5);
    Check("a walled box is a room", room != null);
    Check("the room has its 12 tiles", room?.Tiles.Count == 12);
    Check("and its 14 walls, none shared", room?.Edges.Count == 14 && room.Edges.Values.All(x => !x.Shared && !x.Interior));
    Check("outdoors is not a room", RoomAt(g, 1, 1) == null);
    Check("the room's walls have their wallpaper facing in", room != null && FacesRight(g, room, 11, 22));

    Box(g, 8, 4, 11, 7, 1, 33, 22, 6);
    room = RoomAt(g, 5, 5);
    var next = RoomAt(g, 9, 5);
    Check("a room built against it shares 3 walls with it", room.Edges.Values.Count(x => x.Shared) == 3 && next.Edges.Values.Count(x => x.Shared) == 3);
    Check("each side of the shared wall has its own room's wallpaper", FacesRight(g, room, 11, 22) && FacesRight(g, next, 33, 22));

    // a wall standing inside a room
    var h = new ArrayGrid(24, 24);
    Box(h, 4, 4, 9, 9, 1, 11, 22, 5);
    Walls.Write(h, Edge.AlongY(6, 6, 1), new EdgeLook { Style = 1, PlusPattern = 44, MinusPattern = 44 });
    var withWall = RoomAt(h, 5, 5);
    Check("a wall standing in a room is inside it", withWall != null && withWall.Edges.Values.Count(x => x.Interior) == 1 && withWall.Tiles.Count == 25);
}

// ---- moving a room ----
{
    var g = new ArrayGrid(24, 24);
    Box(g, 4, 4, 8, 7, 1, 11, 22, 5);
    Box(g, 8, 4, 11, 7, 1, 33, 22, 6);
    var room = RoomAt(g, 5, 5);
    var plan = Plan.Of(g, room);
    var before = Built(g);
    Rooms.Clear(g, room);
    Check("picking it up leaves the wall it shares", Walls.Has(g, Edge.AlongY(8, 5, 1)) && !Walls.Has(g, Edge.AlongY(4, 5, 1)) && !Walls.Has(g, Edge.AlongX(5, 4, 1)));
    Check("and takes its floor", g.GetFloor(5, 5, 1) == 0 && g.GetFloor(9, 5, 1) == 6);
    Check("the room next door is still a room", RoomAt(g, 9, 5)?.Tiles.Count == 9);
    Check("it can go down in the open", Rooms.CheckPlace(g, plan, 12, 14, 1, anywhere) == null);
    Rooms.Place(g, plan, 12, 14, 1);
    var moved = RoomAt(g, 13, 15);
    Check("put down, it is the same room", moved != null && moved.Tiles.Count == 12 && moved.Edges.Count == 14 && moved.MinX == 12 && moved.MinY == 14);
    // the wall it shared comes as a copy, with the room next door's wallpaper still on its far face
    Check("with its wallpaper facing in", moved.Edges.Values.All(e => (e.RoomOnPlus ? Walls.Read(g, e.Edge).PlusPattern : Walls.Read(g, e.Edge).MinusPattern) == 11));
    Check("and the copy of the wall it shared has the next room's wallpaper outside", Walls.Read(g, Edge.AlongY(16, 14, 1)).PlusPattern == 33);
    Check("and its floor", g.GetFloor(13, 15, 1) == 5);

    // put back where it was: the same as before, joining the wall it shared
    var back = new ArrayGrid(24, 24);
    Box(back, 4, 4, 8, 7, 1, 11, 22, 5);
    Box(back, 8, 4, 11, 7, 1, 33, 22, 6);
    var room2 = RoomAt(back, 5, 5);
    var plan2 = Plan.Of(back, room2);
    var lot = Built(back);
    Rooms.Clear(back, room2);
    Check("it can go back where it was, against the wall it shared", Rooms.CheckPlace(back, plan2, 4, 4, 1, anywhere) == null);
    Rooms.Place(back, plan2, 4, 4, 1);
    Check("and then the lot is as it was", Same(lot, Built(back), "put back"));

    // where it doesn't fit
    var crowded = new ArrayGrid(24, 24);
    Box(crowded, 4, 4, 8, 7, 1, 11, 22, 5);
    var cr = RoomAt(crowded, 5, 5);
    var cplan = Plan.Of(crowded, cr);
    Walls.Write(crowded, Edge.AlongY(14, 15, 1), new EdgeLook { Style = 1 });
    Check("a wall where the room would go is in the way", Rooms.CheckPlace(crowded, cplan, 12, 14, 1, anywhere) != null);
    Check("off the lot it doesn't fit", Rooms.CheckPlace(crowded, cplan, 21, 14, 1, anywhere) != null);
    Check("where it can't be built it doesn't go", Rooms.CheckPlace(crowded, cplan, 4, 14, 1, (x, y, l) => x < 6) != null);
    Check("overlapping where it is (without picking it up) is in the way", Rooms.CheckPlace(crowded, cplan, 5, 4, 1, anywhere) != null);
}

// ---- turning ----
{
    for (int q = 1; q <= 3; q++)
    {
        var g = new ArrayGrid(30, 30);
        Box(g, 4, 4, 9, 7, 1, 11, 22, 0);
        for (int y = 4; y < 7; y++) for (int x = 4; x < 9; x++) g.SetFloor(x, y, 1, (ushort)(100 + (x - 4) + 10 * (y - 4)));
        var room = RoomAt(g, 5, 5);
        var plan = Plan.Of(g, room);
        var turned = plan.Turned(q);
        Rooms.Place(g, turned, 15, 15, 1);
        var placed = RoomAt(g, 16, 16);
        var size = (q % 2 == 1) ? (3, 5) : (5, 3);
        Check("turned " + q + " quarter(s), the room is " + size.Item1 + " by " + size.Item2, placed != null && placed.Width == size.Item1 && placed.Height == size.Item2);
        Check("turned " + q + ", its wallpaper still faces in", placed != null && FacesRight(g, placed, 11, 22));
        var floorsRight = true;
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
            {
                int tx = x, ty = y, w = 5, hh = 3;
                for (int i = 0; i < q; i++) { (tx, ty) = Plan.TurnTile(tx, ty, hh); (w, hh) = (hh, w); }
                if (g.GetFloor(15 + tx, 15 + ty, 1) != 100 + x + 10 * y) floorsRight = false;
            }
        Check("turned " + q + ", each floor tile goes where its tile goes", floorsRight);
    }
    var g4 = new ArrayGrid(30, 30);
    Box(g4, 4, 4, 9, 7, 1, 11, 22, 5);
    var p = Plan.Of(g4, RoomAt(g4, 5, 5));
    var a = new ArrayGrid(30, 30); Rooms.Place(a, p, 10, 10, 1);
    var b4 = new ArrayGrid(30, 30); Rooms.Place(b4, p.Turned(4), 10, 10, 1);
    Check("four quarter turns are no turn at all", Same(Built(a), Built(b4), "four turns"));
    var dirs = new[] { Direction.NORTH, Direction.EAST, Direction.SOUTH, Direction.WEST };
    Check("objects turn north, east, south, west", Enumerable.Range(0, 4).All(i => Blueprint.TurnDirection(Direction.NORTH, i) == dirs[i]) && Blueprint.TurnDirection(Direction.NORTHEAST, 1) == Direction.SOUTHEAST);
    Check("a point at a tile's middle turns to the middle of the turned tile", Plan.TurnPoint(1 * 16 + 8, 2 * 16 + 8, 3) == ((3 - 1 - 2) * 16 + 8, 1 * 16 + 8));
}

// ---- diagonal walls ----
ArrayGrid DiagonalRoom()
{
    // a room with its corner at (4,4) cut off by a diagonal wall, from (5,4) to (4,5)
    var g = new ArrayGrid(30, 30);
    var look = new EdgeLook { Style = 1, PlusPattern = 11, MinusPattern = 22 };
    for (int x = 5; x < 8; x++) Walls.Write(g, Edge.AlongX(x, 4, 1), look);
    for (int y = 5; y < 8; y++) Walls.Write(g, Edge.AlongY(4, y, 1), look);
    for (int x = 4; x < 8; x++) Walls.Write(g, Edge.AlongX(x, 8, 1), new EdgeLook { Style = 1, PlusPattern = 22, MinusPattern = 11 });
    for (int y = 4; y < 8; y++) Walls.Write(g, Edge.AlongY(8, y, 1), new EdgeLook { Style = 1, PlusPattern = 22, MinusPattern = 11 });
    for (int y = 4; y < 8; y++) for (int x = 4; x < 8; x++) g.SetFloor(x, y, 1, 5);
    g.SetFloor(4, 4, 1, 0);
    g.SetWall(4, 4, 1, Plan.WriteDiagonal(new WallTile(), new PlanTile { Diagonal = true, DiagonalKind = WallSegments.HorizontalDiag, DiagonalStyle = 1, Floor0 = 0, Floor1 = 5, Face0 = 22, Face1 = 11 }));
    return g;
}
{
    var g = DiagonalRoom();
    var reg = new Regions(g, 1);
    Check("the diagonal's inner half is in the room", reg.LabelOf(4, 4, 1) == reg.LabelOf(5, 5, 0) && !reg.Outside[reg.LabelOf(5, 5, 0)]);
    Check("and its outer half is outdoors", reg.Outside[reg.LabelOf(4, 4, 0)]);
    var room = RoomAt(g, 5, 5);
    Check("the room has 15 whole tiles and half of one", room != null && room.Cells.Count == 16 && room.Tiles.Count == 16);
    var plan = Plan.Of(g, room);
    Check("the plan keeps the half that is the room's", plan.Tiles.Any(t => t.Diagonal && t.Halves == 2));
    Check("a room with a diagonal turns four times back to itself", Same(PlaceBuilt(plan, 0), PlaceBuilt(plan.Turned(4), 0), "diagonal, four turns"));
    for (int q = 1; q <= 3; q++)
    {
        var t = new ArrayGrid(30, 30);
        Rooms.Place(t, plan.Turned(q), 12, 12, 1);
        var r = RoomShape.At(t, new Regions(t, 1), new Cell(13, 13, 0)) ?? RoomShape.At(t, new Regions(t, 1), new Cell(14, 14, 0));
        Check("turned " + q + ", the diagonal room is whole (16 cells) with its wallpaper facing in", r != null && r.Cells.Count == 16 && FacesRight(t, r, 11, 22) && DiagonalFacesIn(t, r));
    }
    Rooms.Clear(g, room);
    Check("picking it up takes the diagonal, and leaves outdoors' floor", g.GetWall(4, 4, 1).Segments == 0 && g.GetFloor(4, 4, 1) == 0 && Built(g).Count == 0);
}

List<string> PlaceBuilt(Plan plan, int q) { var t = new ArrayGrid(30, 30); Rooms.Place(t, plan, 12, 12, 1); return Built(t); }

// the diagonal wall's face toward the room has the inside wallpaper, and the room's half has its floor
bool DiagonalFacesIn(IGrid g, RoomShape room)
{
    foreach (var c in room.Cells)
    {
        var w = g.GetWall(c.X, c.Y, room.Level);
        if (!Walls.IsDiagonal(w)) continue;
        var t = Plan.ReadDiagonal(w, c.X, c.Y, room.Level, 3);
        var face = c.Half == 0 ? t.Face0 : t.Face1;
        var floor = c.Half == 0 ? t.Floor0 : t.Floor1;
        if (face != 11 || floor != 5) { Console.WriteLine("     diagonal at " + c.X + "," + c.Y + " half " + c.Half + ": face " + face + ", floor " + floor); return false; }
    }
    return true;
}

// ---- turning the same way as the engine turns a lot (FreeSO's VMLotRotate) ----
{
    ArrayGrid Lot()
    {
        var g = new ArrayGrid(24, 24);
        Box(g, 5, 6, 10, 9, 1, 11, 22, 5);   // a room
        Box(g, 10, 6, 13, 12, 1, 33, 44, 6); // and one beside it, sharing a wall
        // a diagonal on its own, out in the garden
        g.SetWall(16, 15, 1, Plan.WriteDiagonal(new WallTile(), new PlanTile { Diagonal = true, DiagonalKind = WallSegments.VerticalDiag, DiagonalStyle = 1, Floor0 = 7, Floor1 = 8, Face0 = 55, Face1 = 66 }));
        g.SetWall(17, 15, 1, Plan.WriteDiagonal(new WallTile(), new PlanTile { Diagonal = true, DiagonalKind = WallSegments.HorizontalDiag, DiagonalStyle = 1, Floor0 = 9, Floor1 = 10, Face0 = 77, Face1 = 88 }));
        // and a second floor, partly
        Box(g, 5, 6, 8, 9, 2, 99, 98, 4);
        return g;
    }
    var shifts = new[] { (0, 0), (-1, 0), (-1, -1), (0, -1) }; // the engine turns a lot about a point a tile off ours
    for (int n = 1; n <= 3; n++)
    {
        var src = Lot();
        var marshal = new FSO.SimAntics.Marshals.VMMarshal
        {
            Context = new FSO.SimAntics.Marshals.VMContextMarshal
            {
                Architecture = new FSO.SimAntics.Marshals.VMArchitectureMarshal
                {
                    Width = src.Width, Height = src.Height, Stories = src.Stories,
                    Walls = src.Walls.Select(x => (WallTile[])x.Clone()).ToArray(),
                    Floors = src.Floors.Select(x => (FloorTile[])x.Clone()).ToArray(),
                }
            },
            Entities = new FSO.SimAntics.Marshals.VMEntityMarshal[0]
        };
        new FSO.SimAntics.Utils.VMLotRotate(marshal).Rotate(n);
        var engine = new ArrayGrid(src.Width, src.Height, marshal.Context.Architecture.Walls, marshal.Context.Architecture.Floors);
        var mine = new ArrayGrid(src.Width, src.Height);
        Rooms.Place(mine, Plan.OfArea(src, (x, y) => true).Turned(n), 0, 0, 1);
        var (dx, dy) = shifts[n];
        Check("turned " + n + " quarter(s), walls, wallpaper, floors and diagonals land where the engine puts them",
            Same(Built(engine, 0, 0, (x, y) => x > 1 && y > 1 && x < 22 && y < 22), Built(mine, dx, dy, (x, y) => x + dx > 1 && y + dy > 1 && x + dx < 22 && y + dy < 22), "engine turn " + n));
    }
}

// ---- dragging a wall ----
{
    var g = new ArrayGrid(24, 24);
    Box(g, 4, 4, 8, 7, 1, 11, 22, 5);
    var run = WallRun.Of(g, Edge.AlongY(8, 5, 1));
    Check("a wall's run goes from corner to corner", run != null && run.Vertical && run.Across == 8 && run.From == 4 && run.To == 7);
    var grow = WallMove.Plan(g, run, 2, anywhere);
    Check("dragged out 2, nothing is in the way", grow.Problem == null);
    Check("the walls at its ends stretch by 2 each", grow.Added.Count == 4 && grow.Removed.Count == 0);
    grow.Apply(g);
    var big = RoomAt(g, 5, 5);
    Check("the room is now 6 by 3, with its floor", big?.Tiles.Count == 18 && big.Edges.Count == 18 && g.GetFloor(9, 5, 1) == 5);
    Check("its wallpaper still faces in", FacesRight(g, big, 11, 22));

    var h = new ArrayGrid(24, 24);
    Box(h, 4, 4, 8, 7, 1, 11, 22, 5);
    var shrink = WallMove.Plan(h, WallRun.Of(h, Edge.AlongY(8, 5, 1)), -1, anywhere);
    Check("dragged in 1, the walls at its ends shrink", shrink.Problem == null && shrink.Removed.Count == 2 && shrink.Added.Count == 0);
    shrink.Apply(h);
    var small = RoomAt(h, 5, 5);
    Check("the room is now 3 by 3, and the floor left outside is gone", small?.Tiles.Count == 9 && small.Edges.Count == 12 && h.GetFloor(7, 5, 1) == 0);

    // a wall two rooms share: one grows, the other shrinks, and the walls round them stay whole
    var s = new ArrayGrid(24, 24);
    Box(s, 4, 4, 8, 7, 1, 11, 22, 5);
    Box(s, 8, 4, 12, 7, 1, 33, 22, 6);
    var shared = WallRun.Of(s, Edge.AlongY(8, 5, 1));
    Check("a shared wall's run stops where the outer walls cross it", shared.From == 4 && shared.To == 7);
    var push = WallMove.Plan(s, shared, 1, anywhere);
    Check("pushed into the room next door, the outer walls stay as they are", push.Problem == null && push.Added.Count == 0 && push.Removed.Count == 0);
    push.Apply(s);
    Check("one room is 5 wide and the other 3", RoomAt(s, 5, 5)?.Tiles.Count == 15 && RoomAt(s, 10, 5)?.Tiles.Count == 9 && s.GetFloor(8, 5, 1) == 5);

    var blocked = new ArrayGrid(24, 24);
    Box(blocked, 4, 4, 8, 7, 1, 11, 22, 5);
    Walls.Write(blocked, Edge.AlongY(10, 5, 1), new EdgeLook { Style = 1 });
    Check("a wall in the way stops it", WallMove.Plan(blocked, WallRun.Of(blocked, Edge.AlongY(8, 5, 1)), 2, anywhere).Problem != null);
    Check("past the other side of the room stops it", WallMove.Plan(h, WallRun.Of(h, Edge.AlongY(7, 5, 1)), -4, anywhere).Problem != null);
    var upstairs = new ArrayGrid(24, 24);
    Box(upstairs, 4, 4, 8, 7, 1, 11, 22, 5);
    Box(upstairs, 4, 4, 8, 7, 2, 11, 22, 5);
    Check("something built above stops it", WallMove.Plan(upstairs, WallRun.Of(upstairs, Edge.AlongY(8, 5, 1)), 1, anywhere).Problem != null);
}

// ---- the library's files ----
{
    var g = DiagonalRoom();
    var plan = Plan.Of(g, RoomAt(g, 5, 5));
    var b = new Blueprint
    {
        Name = "Café corner",
        Plan = plan,
        Thumbnail = new byte[] { 1, 2, 3 },
        Objects = new List<PlanObject>
        {
            new PlanObject { GUID = 0x12345678, Name = "Table", State = new byte[] { 9, 8, 7 }, X = 24, Y = 40, Dir = Direction.EAST, Price = 150 },
            new PlanObject { GUID = 0x2345, Name = "Lamp", X = 24, Y = 40, Container = 0, ContainerPart = 1, Slot = 2, Price = 35 },
        },
        LotSize = 64, Heights = new short[] { 1, 2, 3 }, Grass = new byte[] { 4, 5 }, LightType = 2, DarkType = 3, RoofStyle = 5, RoofPitch = 0.5f,
    };
    byte[] Bytes(Blueprint x) { using var m = new MemoryStream(); x.Write(m); return m.ToArray(); }
    var first = Bytes(b);
    var read = Blueprint.Read(new MemoryStream(first));
    Check("a saved room reads back the same", read.Name == b.Name && first.SequenceEqual(Bytes(read)));
    Check("with its objects in their slots", read.Objects.Count == 2 && read.Objects[1].Container == 0 && read.Objects[1].ContainerPart == 1 && read.Objects[1].Slot == 2 && read.Objects[0].Dir == Direction.EAST);
    var header = Blueprint.ReadHeader(new MemoryStream(first));
    Check("its name, count and price can be read without the rest", header.Name == "Café corner" && header.ObjectCount == 2 && header.ObjectPrice == 185 && !header.IsLot);
    var bad = (byte[])first.Clone(); bad[4] = 99;
    var refused = false;
    try { Blueprint.Read(new MemoryStream(bad)); } catch (InvalidDataException) { refused = true; }
    Check("a file from a newer Room Tools is refused, not misread", refused);
    var cut = first.Take(first.Length - 40).ToArray();
    var failed = false;
    try { Blueprint.Read(new MemoryStream(cut)); } catch (Exception) { failed = true; }
    Check("a damaged file is refused", failed);

    var turned = b.Turned(1);
    Check("a turned blueprint turns its objects with it", turned.Objects[0].Dir == Direction.SOUTH && (turned.Objects[0].X, turned.Objects[0].Y) == Plan.TurnPoint(24, 40, plan.Height));

    // the library folder
    FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "roomtools-test-" + Guid.NewGuid().ToString("n"));
    var path = Library.Save(b);
    var again = Library.Save(b);
    var lot = new Blueprint { Name = "Whole lot", IsLot = true, Plan = plan };
    Library.Save(lot);
    var all = Library.All(true);
    Check("saved twice under one name, the second gets a file of its own", path != again && File.Exists(path) && File.Exists(again));
    Check("the library lists rooms first, then lots", all.Count == 3 && !all[0].IsLot && !all[1].IsLot && all[2].IsLot && all[2].Name == "Whole lot");
    Check("a fresh name is one not used yet", Library.FreshName("Café corner", false) == "Café corner 2" && Library.FreshName("Room", false) == "Room");
    File.WriteAllText(Path.Combine(Library.RoomsFolder, "junk" + Blueprint.RoomExtension), "not a room");
    Check("a file that isn't a room is skipped", Library.All(true).Count == 3);
    Library.Delete(all[0]);
    Check("deleting takes it out", Library.All(true).Count == 2);
    Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true);
}

Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
return failures == 0 ? 0 : 1;
