/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoomTools
{
    /// <summary>A lot's walls and floors as the game has them. Writing through it is building for real (or, while the
    /// engine is working out a tool's preview, writing to the preview).</summary>
    public class EngineGrid : IGrid
    {
        public readonly VMArchitecture Arch;
        public EngineGrid(VMArchitecture arch) { Arch = arch; }
        public int Width => Arch.Width;
        public int Height => Arch.Height;
        public int Stories => Arch.Stories;
        private bool In(int x, int y, int level) => x >= 0 && y >= 0 && x < Arch.Width && y < Arch.Height && level >= 1 && level <= Arch.Stories;
        public WallTile GetWall(int x, int y, int level) => In(x, y, level) ? Arch.GetWall((short)x, (short)y, (sbyte)level) : default;
        public void SetWall(int x, int y, int level, WallTile wall) { if (In(x, y, level)) Arch.SetWall((short)x, (short)y, (sbyte)level, wall); }
        public ushort GetFloor(int x, int y, int level) => In(x, y, level) ? Arch.GetFloor((short)x, (short)y, (sbyte)level).Pattern : (ushort)0;
        public void SetFloor(int x, int y, int level, ushort pattern) { if (In(x, y, level)) Arch.SetFloor((short)x, (short)y, (sbyte)level, new FloorTile { Pattern = pattern }, true); }
    }

    /// <summary>The objects on a lot, as Room Tools needs to see them.</summary>
    public static class Things
    {
        public static string NameOf(VMMultitileGroup g)
        {
            var name = g?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = g?.BaseObject?.ToString();
            return string.IsNullOrWhiteSpace(name) ? "An object" : name.Trim();
        }

        /// <summary>Objects standing on their own on the lot: one for each multi-tile object, not the ones in slots, not Sims.</summary>
        public static List<VMMultitileGroup> Standing(VM vm)
        {
            var result = new List<VMMultitileGroup>();
            foreach (var ent in vm.Entities)
            {
                if (ent is VMAvatar || ent.Dead || ent.GhostImage) continue;
                var g = ent.MultitileGroup;
                if (g == null || g.BaseObject != ent || g.BaseObject is VMAvatar) continue;
                if (g.Objects.Any(x => x.Container != null)) continue;
                if (g.Objects.All(x => x.Position == LotTilePos.OUT_OF_WORLD)) continue;
                result.Add(g);
            }
            return result;
        }

        public static bool PlayersCanMove(VMMultitileGroup g, VMContext context)
        {
            if (context.Cheats.MoveObjects) return true;
            return g.Objects.All(x => ((VMMovementFlags)x.GetValue(VMStackObjectVariable.MovementFlags) & VMMovementFlags.PlayersCanMove) != 0);
        }

        public static bool PlayersCanDelete(VMMultitileGroup g, VMContext context)
        {
            if (context.Cheats.MoveObjects) return true;
            return g.Objects.All(x => ((VMMovementFlags)x.GetValue(VMStackObjectVariable.MovementFlags) & (VMMovementFlags.PlayersCanMove | VMMovementFlags.PlayersCanDelete)) == (VMMovementFlags.PlayersCanMove | VMMovementFlags.PlayersCanDelete));
        }

        private static int WallOff(Direction dir)
        {
            switch (dir)
            {
                case Direction.EAST: return 1;
                case Direction.SOUTH: return 2;
                case Direction.WEST: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// The walls an object needs to be against, as sides of its own tile (the engine's SetWallUse, which the
        /// object's wall placement flags are turned by its direction for).
        /// </summary>
        public static WallSegments SidesNeeded(VMEntity obj)
        {
            var flags = obj.GetValue(VMStackObjectVariable.WallPlacementFlags) & 15;
            if (flags == 0) return 0;
            var rotate = (8 - (WallOff(obj.Direction) + 1)) % 4;
            return (WallSegments)(((flags << (4 - rotate)) & 15) | ((flags & 15) >> rotate));
        }

        /// <summary>The walls an object hangs on (a door, a window, a picture, a wall lamp...).</summary>
        public static IEnumerable<Edge> WallsOf(VMEntity obj)
        {
            var pos = obj.Position;
            if (pos == LotTilePos.OUT_OF_WORLD || obj.Container != null) yield break;
            var sides = SidesNeeded(obj);
            int x = pos.TileX, y = pos.TileY, level = pos.Level;
            if ((sides & WallSegments.TopLeft) != 0) yield return Edge.AlongY(x, y, level);
            if ((sides & WallSegments.TopRight) != 0) yield return Edge.AlongX(x, y, level);
            if ((sides & WallSegments.BottomRight) != 0) yield return Edge.AlongY(x + 1, y, level);
            if ((sides & WallSegments.BottomLeft) != 0) yield return Edge.AlongX(x, y + 1, level);
        }

        public static int CatalogPrice(VMMultitileGroup g)
        {
            var item = Content.Get().WorldCatalog.GetItemByGUID(g.GUID);
            if (item != null) return (int)item.Value.Price;
            return Math.Max(0, g.InitialPrice);
        }

        /// <summary>An object's state, as the game saves one on its own (FSOo).</summary>
        public static byte[] StateOf(VMMultitileGroup g)
        {
            var marshal = new VMStandaloneObjectMarshal(g);
            if (marshal.Entities == null) return new byte[0];
            using (var mem = new MemoryStream())
            {
                marshal.SerializeInto(new BinaryWriter(mem));
                return mem.ToArray();
            }
        }

        /// <summary>
        /// A new object made from a blueprint's, out of the world: with the state it was saved with, if its kind is
        /// installed. As the game does placing an object from an inventory, it forgets what it knew of other objects
        /// and Sims. Null, and why, if it cannot be made.
        /// </summary>
        public static VMMultitileGroup Make(VM vm, PlanObject o, bool ghost, out string problem)
        {
            problem = null;
            try
            {
                if (o.State != null && o.State.Length > 0)
                {
                    var state = new VMStandaloneObjectMarshal();
                    using (var reader = new BinaryReader(new MemoryStream(o.State))) state.Deserialize(reader);
                    if (state.Entities == null || state.Entities.Length == 0 || state.MultitileGroup == null) { problem = "its saved state can't be read"; return null; }
                    foreach (var e in state.Entities)
                    {
                        if (Content.Get().WorldObjects.Get(e.GUID) == null) { problem = "it isn't installed"; return null; }
                        if (e is VMGameObjectMarshal gm) gm.Disabled = 0;
                        e.MeToObject = new VMEntityRelationshipMarshal[0];
                        e.PersistID = 0;
                    }
                    var group = state.CreateInstance(vm, ghost);
                    if (group == null || group.Objects.Count == 0) { problem = "it couldn't be made"; return null; }
                    if (ghost)
                    {
                        //a stand-in has the id of the object it was saved from: it must not be taken for that object
                        foreach (var obj in group.Objects)
                        {
                            obj.ObjectID = 0;
                            if (obj.WorldUI != null) obj.WorldUI.ObjectID = 0;
                        }
                    }
                    return group;
                }
                if (Content.Get().WorldObjects.Get(o.GUID) == null) { problem = "it isn't installed"; return null; }
                var made = vm.Context.CreateObjectInstance(o.GUID, LotTilePos.OUT_OF_WORLD, o.Dir, ghost);
                if (made == null || made.Objects.Count == 0) { problem = "it couldn't be made"; return null; }
                return made;
            }
            catch (Exception e)
            {
                problem = "it couldn't be made (" + e.Message + ")";
                return null;
            }
        }

        /// <summary>Delete an object and everything in its slots, as the game deletes one held in buy mode.</summary>
        public static void Delete(VMContext context, VMEntity real)
        {
            var group = real.MultitileGroup;
            foreach (var obj in group.Objects.ToList())
            {
                var slots = obj.TotalSlots();
                var contents = new List<VMEntity>();
                for (int j = 0; j < slots; j++)
                {
                    var s = obj.GetSlot(j);
                    if (s != null && !(s is VMAvatar)) contents.Add(s);
                }
                foreach (var s in contents) Delete(context, s);
            }
            group.Delete(context);
        }

        public static void Place(VMMultitileGroup g, LotTilePos pos, Direction dir, VMContext context, out VMPlacementError error)
        {
            var result = g.ChangePosition(pos, dir, context, VMPlaceRequestFlags.UserBuildableLimit);
            error = result.Status;
        }

        public static string Describe(VMPlacementError error)
        {
            switch (error)
            {
                case VMPlacementError.CantIntersectOtherObjects: return "something else is in the way";
                case VMPlacementError.LocationOutOfBounds: return "it would be off the part of the lot you can build on";
                case VMPlacementError.MustBeAgainstWall: return "it needs a wall";
                case VMPlacementError.CantPlaceOnSlope: return "the ground isn't flat";
                case VMPlacementError.NotAllowedOnFloor: return "it can't go on a floor";
                case VMPlacementError.NotAllowedOnTerrain: return "it needs a floor";
                case VMPlacementError.CantPlaceInAir: return "there's no floor under it";
                default: return "it doesn't fit there (" + error + ")";
            }
        }
    }

    /// <summary>What things cost, at the prices the game's own build and buy tools charge.</summary>
    public static class Prices
    {
        public static int WallStyle(ushort style)
        {
            if (style == 0) return 0;
            try { return Math.Max(0, Content.Get().WorldWalls.GetWallStyle(style)?.Price ?? 0); }
            catch { return 0; }
        }

        public static int Wallpaper(ushort pattern)
        {
            if (pattern == 0) return 0;
            return Content.Get().WorldWalls.Entries.TryGetValue(pattern, out var r) ? Math.Max(0, r.Price) : 0;
        }

        public static int Floor(ushort pattern)
        {
            if (pattern == 0) return 0;
            return Content.Get().WorldFloors.Entries.TryGetValue(pattern, out var r) ? Math.Max(0, r.Price) : 0;
        }

        /// <summary>What a wall comes to: the wall and the wallpaper on its faces.</summary>
        public static int Wall(EdgeLook look) => WallStyle(look.Style) + Wallpaper(look.PlusPattern) + Wallpaper(look.MinusPattern);

        /// <summary>A wall taken down: what the game's wall tool gives back for one.</summary>
        public const int WallRefund = 7;
        /// <summary>A floor taken up: the game gives back four fifths of its price.</summary>
        public static int FloorRefund(ushort pattern) => Floor(pattern) * 4 / 5;

        /// <summary>What it costs to build a plan on a lot (the walls that are already there are kept, and cost nothing).</summary>
        public static int Build(IGrid g, Plan plan, int ox, int oy, int level)
        {
            int cost = 0;
            foreach (var t in plan.Tiles)
            {
                if (!t.Diagonal) cost += Floor(t.Floor);
                else
                {
                    cost += WallStyle(t.DiagonalStyle) + Wallpaper(t.Face0) + Wallpaper(t.Face1);
                    if ((t.Halves & 1) != 0) cost += Floor(t.Floor0) / 2;
                    if ((t.Halves & 2) != 0) cost += Floor(t.Floor1) / 2;
                }
            }
            foreach (var pe in plan.Edges)
            {
                var e = pe.Edge.Moved(ox, oy, level - 1);
                if (pe.Boundary && Walls.Has(g, e)) cost += Wallpaper(pe.RoomOnPlus ? pe.Look.PlusPattern : pe.Look.MinusPattern);
                else cost += Wall(pe.Look);
            }
            return cost;
        }
    }

    /// <summary>A room picked up from the lot: its shape, walls and floors, objects, and the Sims standing in it.</summary>
    public class RoomPick
    {
        public RoomShape Room;
        public Blueprint Blueprint;
        public List<VMAvatar> Sims = new List<VMAvatar>();
        /// <summary>Why the room can't be moved, or null if it can.</summary>
        public string Problem;
        /// <summary>For a copy: objects left out, that players can't move (and so can't buy a second of).</summary>
        public List<string> LeftOut = new List<string>();
        /// <summary>Every object going with the room, those in slots included.</summary>
        public HashSet<VMEntity> Entities = new HashSet<VMEntity>();
    }

    public static class Building
    {
        /// <summary>Whether a tile can be built on: on the part of the lot that can be, and above the ground floor, held up.</summary>
        public static Func<int, int, int, bool> Buildable(VMArchitecture arch) => (x, y, level) =>
        {
            if (x < 0 || y < 0 || x >= arch.Width || y >= arch.Height || level < 1 || level > arch.Stories) return false;
            if (arch.OutsideClip((short)x, (short)y, (sbyte)level)) return false;
            if (level == 1) return true;
            return arch.Supported[level - 2][y * arch.Width + x];
        };

        /// <summary>
        /// Pick up the room at a point on a floor (tile coordinates, with the part of a tile, so a half cut off by a
        /// diagonal wall can be told apart). For a copy, objects players can't move are left out; for a move, they
        /// stop it. With state, each object's state is kept (needed to copy it, or to save it).
        /// </summary>
        public static RoomPick Pick(VM vm, float tx, float ty, int level, bool forMove, bool withState)
        {
            var arch = vm.Context.Architecture;
            var grid = new EngineGrid(arch);
            int x = (int)Math.Floor(tx), y = (int)Math.Floor(ty);
            if (!Walls.InLot(grid, x, y)) return new RoomPick { Problem = "That's off the lot." };
            var regions = new Regions(grid, level);
            var cell = new Cell(x, y, Regions.HalfAt(grid.GetWall(x, y, level), tx - x, ty - y));
            var room = RoomShape.At(grid, regions, cell);
            if (room == null) return new RoomPick { Problem = "That's outdoors. Room Tools picks up a room with walls (or fences) all round it." };
            var pick = new RoomPick { Room = room };
            var plan = Plan.Of(grid, room);
            pick.Blueprint = new Blueprint { Name = "Room", Plan = plan };
            var ownWalls = new HashSet<Edge>(room.OwnWalls);

            if (forMove && level < arch.Stories)
            {
                foreach (var t in room.Tiles)
                {
                    if (grid.GetFloor(t.x, t.y, level + 1) != 0 || grid.GetWall(t.x, t.y, level + 1).Segments != 0)
                    {
                        pick.Problem = "Something is built above this room. Room Tools moves rooms with nothing on top of them: move what's above first.";
                        break;
                    }
                }
            }

            bool InRoom(VMEntity o) => o.Position != LotTilePos.OUT_OF_WORLD && o.Position.Level == level && room.Tiles.Contains((o.Position.TileX, o.Position.TileY));
            bool OnOwnWall(VMEntity o) => Things.WallsOf(o).Any(ownWalls.Contains);

            foreach (var g in Things.Standing(vm))
            {
                var inside = g.Objects.Any(InRoom);
                var outer = !inside && g.Objects.Any(OnOwnWall);
                if (!inside && !outer) continue;
                var name = Things.NameOf(g);
                if (!g.Objects.All(o => InRoom(o) || OnOwnWall(o)))
                {
                    if (forMove) pick.Problem = pick.Problem ?? (name + " is partly outside the room.");
                    else pick.LeftOut.Add(name);
                    continue;
                }
                if (forMove)
                {
                    var movable = g.BaseObject.IsUserMovable(vm.Context, false);
                    if (movable != VMPlacementError.Success)
                    {
                        pick.Problem = pick.Problem ?? (name + (movable == VMPlacementError.InUse ? " is being used." : " can't be moved."));
                        continue;
                    }
                }
                else if (!Things.PlayersCanMove(g, vm.Context))
                {
                    pick.LeftOut.Add(name);
                    continue;
                }
                Add(pick, g, -1, 0, 0, room.MinX, room.MinY, level, withState, outer, vm.Context);
            }

            foreach (var ava in vm.Context.ObjectQueries.Avatars)
                if (ava is VMAvatar sim && InRoom(sim)) pick.Sims.Add(sim);
            return pick;
        }

        private static void Add(RoomPick pick, VMMultitileGroup g, int container, int part, int slot, int minX, int minY, int level, bool withState, bool outer, VMContext context)
        {
            if (pick.Entities.Contains(g.BaseObject)) return;
            foreach (var o in g.Objects) pick.Entities.Add(o);
            var b = g.BaseObject;
            var parent = (container >= 0) ? pick.Blueprint.Objects[container] : null;
            var po = new PlanObject
            {
                GUID = g.GUID,
                Name = Things.NameOf(g),
                State = withState ? Things.StateOf(g) : new byte[0],
                X = parent?.X ?? (b.Position.x - minX * 16),
                Y = parent?.Y ?? (b.Position.y - minY * 16),
                Level = parent?.Level ?? (b.Position.Level - level + 1),
                Dir = b.Direction,
                Container = container,
                ContainerPart = part,
                Slot = slot,
                Price = Things.CatalogPrice(g),
                Live = g,
                OnOuterWall = outer,
            };
            pick.Blueprint.Objects.Add(po);
            var index = pick.Blueprint.Objects.Count - 1;
            for (int i = 0; i < g.Objects.Count; i++)
            {
                var obj = g.Objects[i];
                var slots = obj.TotalSlots();
                for (int s = 0; s < slots; s++)
                {
                    var inSlot = obj.GetSlot(s);
                    if (inSlot == null || inSlot is VMAvatar || inSlot.MultitileGroup == null) continue;
                    Add(pick, inSlot.MultitileGroup, index, i, s, minX, minY, level, withState, false, context);
                }
            }
        }

        /// <summary>The whole lot, for the Lot Bin: everything built, the objects players can move, the ground and the roof.</summary>
        public static Blueprint CaptureLot(VM vm, out List<string> leftOut)
        {
            var arch = vm.Context.Architecture;
            var grid = new EngineGrid(arch);
            var b = new Blueprint { IsLot = true, Name = "Lot", LotSize = arch.Width };
            b.Plan = Plan.OfArea(grid, (x, y) => true);
            var pick = new RoomPick { Blueprint = b };
            leftOut = new List<string>();
            foreach (var g in Things.Standing(vm))
            {
                if (!Things.PlayersCanMove(g, vm.Context)) continue; //the lot's own fixtures (the mailbox, the rubbish bin, the game's helpers) stay with each lot
                if (g.Objects.Any(o => o.Position == LotTilePos.OUT_OF_WORLD))
                {
                    leftOut.Add(Things.NameOf(g));
                    continue;
                }
                Add(pick, g, -1, 0, 0, 0, 0, 1, true, false, vm.Context);
            }
            foreach (var o in b.Objects) o.Live = null;
            var t = arch.Terrain;
            b.Heights = (short[])t.Heights.Clone();
            b.Grass = (byte[])t.GrassState.Clone();
            b.LightType = (byte)t.LightType;
            b.DarkType = (byte)t.DarkType;
            b.RoofStyle = arch.RoofStyle;
            b.RoofPitch = arch.RoofPitch;
            return b;
        }

        // ---- putting a room down ----

        /// <summary>The tiles a plan covers, with its corner at (ox, oy).</summary>
        private static HashSet<(int, int, int)> Footprint(Plan plan, int ox, int oy, int level)
        {
            var set = new HashSet<(int, int, int)>();
            foreach (var t in plan.Tiles) set.Add((t.X + ox, t.Y + oy, t.Level + level - 1));
            return set;
        }

        /// <summary>
        /// Why a blueprint can't be put down with its corner at (ox, oy), or null if it can, and what it would cost.
        /// When moving, the room it was picked up from is taken away first, so it may be put down partly where it was.
        /// The walls and floors to show are left in the overlay.
        /// </summary>
        public static string Check(VM vm, Blueprint b, int ox, int oy, int level, RoomPick moving, out int cost, out OverlayGrid overlay)
        {
            var arch = vm.Context.Architecture;
            var grid = new EngineGrid(arch);
            overlay = new OverlayGrid(grid);
            cost = 0;
            if (moving?.Problem != null) return moving.Problem;
            if (moving != null) Rooms.Clear(overlay, moving.Room);
            var problem = Rooms.CheckPlace(overlay, b.Plan, ox, oy, level, Buildable(arch));
            if (problem == null)
            {
                var foot = Footprint(b.Plan, ox, oy, level);
                if (level == 1)
                {
                    foreach (var (x, y, l) in foot)
                        if (l == 1 && arch.GetTerrainSloped((short)x, (short)y)) { problem = "The ground isn't flat there."; break; }
                }
                if (problem == null)
                {
                    foreach (var ent in vm.Entities)
                    {
                        if (ent.Dead || ent.GhostImage || ent.Position == LotTilePos.OUT_OF_WORLD) continue;
                        if (moving != null && (moving.Entities.Contains(ent) || (ent is VMAvatar a && moving.Sims.Contains(a)))) continue;
                        if (!foot.Contains((ent.Position.TileX, ent.Position.TileY, ent.Position.Level))) continue;
                        problem = (ent is VMAvatar) ? "A Sim is in the way." : (Things.NameOf(ent.MultitileGroup) + " is in the way.");
                        break;
                    }
                }
            }
            if (problem == null && moving == null)
            {
                cost = Prices.Build(overlay, b.Plan, ox, oy, level) + b.ObjectPrice;
                var budget = vm.TS1State?.CurrentFamily?.Budget;
                if (budget != null && cost > budget.Value) problem = "You can't afford it: it costs §" + cost.ToString("##,#0") + ".";
            }
            Rooms.Place(overlay, b.Plan, ox, oy, level);
            return problem;
        }

        private static LotTilePos At(PlanObject o, int ox, int oy, int level) =>
            new LotTilePos((short)(ox * 16 + o.X), (short)(oy * 16 + o.Y), (sbyte)(level + o.Level - 1));

        /// <summary>
        /// Put a blueprint down with its corner at (ox, oy), on a lot it was checked against. A move takes the room away
        /// from where it was first, and brings its own objects and Sims; a copy (or a room from the library) buys new
        /// ones. If an object can't be put down, nothing is done: the caller puts the lot back as it was. Returns why it
        /// failed, or null.
        /// </summary>
        public static string Place(VM vm, Blueprint b, int ox, int oy, int level, RoomPick moving, int turns, int cost)
        {
            var context = vm.Context;
            var arch = context.Architecture;
            var grid = new EngineGrid(arch);

            //Sims and objects off the floor first, so the walls under them can change
            var simsAt = new List<(VMAvatar sim, LotTilePos pos, Direction dir)>();
            if (moving != null)
            {
                foreach (var o in b.Objects)
                    if (o.Container < 0 && o.Live != null) o.Live.ChangePosition(LotTilePos.OUT_OF_WORLD, o.Live.BaseObject.Direction, context, VMPlaceRequestFlags.Default);
                Rooms.Clear(grid, moving.Room);
            }
            Rooms.Place(grid, b.Plan, ox, oy, level);
            Settle(arch);

            var placed = new VMMultitileGroup[b.Objects.Count];
            for (int i = 0; i < b.Objects.Count; i++)
            {
                var o = b.Objects[i];
                VMMultitileGroup g;
                if (moving != null)
                {
                    g = o.Live;
                    placed[i] = g;
                    if (o.Container >= 0) continue; //comes with what it's in
                }
                else
                {
                    g = Things.Make(vm, o, false, out var why);
                    if (g == null) return o.Name + " couldn't be bought: " + why + ".";
                    placed[i] = g;
                    if (o.Container >= 0)
                    {
                        var holder = placed[o.Container];
                        var part = (holder != null && o.ContainerPart < holder.Objects.Count) ? holder.Objects[o.ContainerPart] : holder?.BaseObject;
                        if (part == null || !part.PlaceInSlot(g.BaseObject, o.Slot, true, context)) return o.Name + " couldn't be put back where it was.";
                        continue;
                    }
                }
                Things.Place(g, At(o, ox, oy, level), o.Dir, context, out var error);
                if (error != VMPlacementError.Success) return o.Name + " couldn't be put down: " + Things.Describe(error) + ".";
            }
            for (int i = 0; i < b.Objects.Count; i++)
                if (b.Objects[i].Container < 0) placed[i]?.ExecuteEntryPoint(11, context); //User Placement, as the game's buy mode does

            if (moving != null)
            {
                var room = moving.Room;
                foreach (var sim in moving.Sims)
                {
                    var p = sim.Position;
                    var plan = moving.Blueprint.Plan;
                    var (rx, ry) = Blueprint.TurnPoint(p.x - room.MinX * 16, p.y - room.MinY * 16, plan.Width, plan.Height, turns);
                    var target = new LotTilePos((short)(ox * 16 + rx), (short)(oy * 16 + ry), (sbyte)level);
                    if (sim.SetPosition(target, Blueprint.TurnDirection(sim.Direction, turns), context, VMPlaceRequestFlags.AllowIntersection).Status != VMPlacementError.Success)
                        VMFindLocationFor.FindLocationFor(sim, sim, context, VMPlaceRequestFlags.Default);
                }
            }
            Charge(vm, cost);
            context.RefreshAllLighting();
            return null;
        }

        /// <summary>Have the engine catch up with changed walls and floors: rooms, doors between them, what holds what up.</summary>
        public static void Settle(VMArchitecture arch)
        {
            var context = arch.Context;
            arch.SignalAllDirty();
            context.DisableRouteInvalidation = true;
            try { arch.Tick(); }
            finally { context.DisableRouteInvalidation = false; }
        }

        public static void Charge(VM vm, int cost)
        {
            var family = vm.TS1State?.CurrentFamily;
            if (family == null || cost == 0) return;
            family.Budget = Math.Max(0, family.Budget - cost);
        }

        // ---- moving a wall ----

        /// <summary>Objects hung on a wall run, and those that would be in the way of moving it.</summary>
        public static string CheckWallMove(VM vm, WallMove move, out List<VMMultitileGroup> riding, out int cost)
        {
            riding = new List<VMMultitileGroup>();
            cost = 0;
            if (move.Problem != null) return move.Problem;
            if (move.Delta == 0) return null;
            var context = vm.Context;
            var run = new HashSet<Edge>(move.Run.Walls(move.Run.Across));
            var removed = new HashSet<Edge>(move.Removed);
            var swept = new HashSet<(int, int)>(move.Swept);
            var level = move.Run.Level;
            foreach (var g in Things.Standing(vm))
            {
                var hung = g.Objects.SelectMany(Things.WallsOf).ToList();
                if (hung.Any(removed.Contains)) return Things.NameOf(g) + " is on the wall that would come down.";
                if (hung.Count > 0 && hung.All(run.Contains))
                {
                    var movable = g.BaseObject.IsUserMovable(context, false);
                    if (movable != VMPlacementError.Success) return Things.NameOf(g) + (movable == VMPlacementError.InUse ? " is being used." : " can't be moved.");
                    riding.Add(g);
                    continue;
                }
                if (hung.Any(run.Contains)) return Things.NameOf(g) + " is partly on another wall.";
                if (g.Objects.Any(o => o.Position.Level == level && swept.Contains((o.Position.TileX, o.Position.TileY)))) return Things.NameOf(g) + " is in the way.";
            }
            foreach (var ava in context.ObjectQueries.Avatars)
                if (ava.Position.Level == level && swept.Contains((ava.Position.TileX, ava.Position.TileY))) return "A Sim is in the way.";

            var grid = new EngineGrid(context.Architecture);
            foreach (var (e, look) in move.Added) cost += Prices.Wall(look);
            cost -= move.Removed.Count * Prices.WallRefund;
            foreach (var (x, y, floor) in move.Floors)
            {
                var old = grid.GetFloor(x, y, level);
                if (old == floor) continue;
                cost += Prices.Floor(floor) - Prices.FloorRefund(old);
            }
            var budget = vm.TS1State?.CurrentFamily?.Budget;
            if (budget != null && cost > budget.Value) return "You can't afford it: it costs §" + cost.ToString("##,#0") + ".";
            return null;
        }

        /// <summary>Move a wall run, with what hangs on it. Returns why it failed (the caller puts the lot back), or null.</summary>
        public static string MoveWall(VM vm, WallMove move, List<VMMultitileGroup> riding, int cost)
        {
            var context = vm.Context;
            var arch = context.Architecture;
            var dx = move.Run.Vertical ? move.Delta * 16 : 0;
            var dy = move.Run.Vertical ? 0 : move.Delta * 16;
            var back = riding.Select(g => (g, pos: g.BaseObject.Position, dir: g.BaseObject.Direction)).ToList();
            foreach (var (g, pos, dir) in back) g.ChangePosition(LotTilePos.OUT_OF_WORLD, dir, context, VMPlaceRequestFlags.Default);
            move.Apply(new EngineGrid(arch));
            Settle(arch);
            foreach (var (g, pos, dir) in back)
            {
                Things.Place(g, new LotTilePos((short)(pos.x + dx), (short)(pos.y + dy), pos.Level), dir, context, out var error);
                if (error != VMPlacementError.Success) return Things.NameOf(g) + " couldn't move with the wall: " + Things.Describe(error) + ".";
                g.ExecuteEntryPoint(11, context);
            }
            Charge(vm, cost);
            context.RefreshAllLighting();
            return null;
        }

        // ---- the Lot Bin: a whole lot ----

        /// <summary>What replacing the lot with a saved one would cost (less what is there now would fetch), or why it can't.</summary>
        public static string CheckLot(VM vm, Blueprint b, out int cost, out int refund)
        {
            var arch = vm.Context.Architecture;
            cost = 0; refund = 0;
            var buildable = Buildable(arch);
            foreach (var t in b.Plan.Tiles)
                if (t.X >= arch.Width || t.Y >= arch.Height || arch.OutsideClip((short)t.X, (short)t.Y, (sbyte)t.Level))
                    return "This lot is smaller than the one it was saved from, and it doesn't fit.";
            foreach (var pe in b.Plan.Edges)
                if (!Walls.InLot(new EngineGrid(arch), pe.Edge)) return "This lot is smaller than the one it was saved from, and it doesn't fit.";

            var empty = new ArrayGrid(arch.Width, arch.Height, arch.Stories);
            cost = Prices.Build(empty, b.Plan, 0, 0, 1) + b.ObjectPrice;
            foreach (var g in Things.Standing(vm))
                if (Things.PlayersCanDelete(g, vm.Context)) refund += Math.Max(0, g.Price);
            var grid = new EngineGrid(arch);
            for (int level = 1; level <= arch.Stories; level++)
            {
                refund += Walls.All(grid, level).Count() * Prices.WallRefund;
                for (int y = 0; y < arch.Height; y++)
                    for (int x = 0; x < arch.Width; x++)
                        refund += Prices.FloorRefund(grid.GetFloor(x, y, level));
            }
            var inUse = Things.Standing(vm).FirstOrDefault(g => Things.PlayersCanDelete(g, vm.Context) && g.BaseObject.IsInUse(vm.Context, true));
            if (inUse != null) return Things.NameOf(inUse) + " is being used.";
            var budget = vm.TS1State?.CurrentFamily?.Budget;
            if (budget != null && cost - refund > budget.Value) return "You can't afford it: it costs §" + (cost - refund).ToString("##,#0") + " more than this lot fetches.";
            return null;
        }

        /// <summary>
        /// Replace everything built on the lot with a saved lot. The Sims stay, and so do the lot's own fixtures (the
        /// mailbox, the rubbish bin). Objects that don't fit, or aren't installed, are left out and named in notes.
        /// </summary>
        public static void PlaceLot(VM vm, Blueprint b, int net, List<string> notes)
        {
            var context = vm.Context;
            var arch = context.Architecture;
            foreach (var g in Things.Standing(vm))
                if (Things.PlayersCanDelete(g, context)) Things.Delete(context, g.BaseObject);

            var grid = new EngineGrid(arch);
            for (int level = 1; level <= arch.Stories; level++)
                for (int y = 0; y < arch.Height; y++)
                    for (int x = 0; x < arch.Width; x++)
                    {
                        if (grid.GetWall(x, y, level).Segments != 0) grid.SetWall(x, y, level, new WallTile());
                        if (grid.GetFloor(x, y, level) != 0) grid.SetFloor(x, y, level, 0);
                    }

            var t = arch.Terrain;
            if (b.LotSize == arch.Width && b.Heights?.Length == t.Heights.Length && b.Grass?.Length == t.GrassState.Length)
            {
                //into the lot's own arrays: other packs keep their own ground (paint, foundations) beside this one
                Array.Copy(b.Heights, t.Heights, t.Heights.Length);
                Array.Copy(b.Grass, t.GrassState, t.GrassState.Length);
                t.LightType = (FSO.Content.Model.TerrainType)b.LightType;
                t.DarkType = (FSO.Content.Model.TerrainType)b.DarkType;
                t.RegenerateCenters();
                arch.SignalTerrainRedraw();
            }
            Rooms.Place(grid, b.Plan, 0, 0, 1);
            arch.SetRoof(b.RoofPitch, b.RoofStyle);
            Settle(arch);

            var placed = new VMMultitileGroup[b.Objects.Count];
            for (int i = 0; i < b.Objects.Count; i++)
            {
                var o = b.Objects[i];
                if (o.Container >= 0 && placed[o.Container] == null) continue; //what it was in was left out
                var g = Things.Make(vm, o, false, out var why);
                if (g == null) { notes.Add(o.Name + ": " + why); continue; }
                if (o.Container >= 0)
                {
                    var holder = placed[o.Container];
                    var part = (o.ContainerPart < holder.Objects.Count) ? holder.Objects[o.ContainerPart] : holder.BaseObject;
                    if (!part.PlaceInSlot(g.BaseObject, o.Slot, true, context)) { Things.Delete(context, g.BaseObject); notes.Add(o.Name + ": its place was taken"); continue; }
                    placed[i] = g;
                    continue;
                }
                Things.Place(g, At(o, 0, 0, 1), o.Dir, context, out var error);
                if (error != VMPlacementError.Success)
                {
                    Things.Delete(context, g.BaseObject);
                    notes.Add(o.Name + ": " + Things.Describe(error));
                    continue;
                }
                placed[i] = g;
            }
            for (int i = 0; i < b.Objects.Count; i++)
                if (b.Objects[i].Container < 0) placed[i]?.ExecuteEntryPoint(11, context);

            //Sims left standing in a wall or an object step out of it
            foreach (var ava in context.ObjectQueries.Avatars.ToList())
            {
                if (ava.Position == LotTilePos.OUT_OF_WORLD || ava.Container != null) continue;
                if (ava.PositionValid(ava.Position, ava.Direction, context, VMPlaceRequestFlags.Default).Status != VMPlacementError.Success)
                    VMFindLocationFor.FindLocationFor(ava, ava, context, VMPlaceRequestFlags.Default);
            }
            Charge(vm, net);
            context.RefreshAllLighting();
        }
    }
}
