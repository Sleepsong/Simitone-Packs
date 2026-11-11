/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content.Model;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.LotView.RC;
using FSO.LotView.Utils;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.Routing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DiagonalDoors
{
    /// <summary>
    /// Doors and windows on diagonal walls, as in The Sims 2, added from a pack: nothing in Simitone or FreeSO is changed.
    ///
    /// The engine is closer to this than it looks. Objects can already face eight ways and sit anywhere in a tile (the
    /// Build Cheats pack's rotate45 and offgrid), a tile with a diagonal wall already belongs to two rooms, and the 3D
    /// wall already cuts the hole of a door or window out of a diagonal wall if the tile says it has one. What is
    /// missing is everything that would put one there. So the pack does these things, in the order below:
    ///
    ///  1. Placing. A door is two objects, one on each side of its wall. On a straight wall that is two tiles. A
    ///     diagonal wall runs through the middle of one tile, so both halves go inside that tile, either side of the
    ///     wall, and the door is turned 45 degrees to face it.
    ///  2. Allowing it. The engine refuses anything on a tile with a diagonal wall unless the object says it may.
    ///  3. The hole in the wall, and the wall no longer blocking Sims where a door is.
    ///  4. Drawing: the hole is the right width in 3D, and 2D is kept away from wall graphics it does not have.
    ///
    /// Full 3D only, as with rotate45: in 2D the game only has pictures of objects facing four ways.
    /// </summary>
    public class DiagonalDoorsPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing: half of these patches would be worse than none
                throw;
            }
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(DiagonalDoorsPack), name);

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1. Every placement by the player goes through here, both the preview under the cursor and the real thing.
            harmony.Patch(Find(typeof(VMMultitileGroup), "ChangePosition"), prefix: Mine(nameof(SnapToDiagonal)));

            // 2. The engine's question "may this object be on a tile with this wall?". Also asked when a wall changes
            //    under an object, which is what stops the wall being deleted from under the door.
            harmony.Patch(Find(typeof(VMEntity), "WallChangeValid"), prefix: Mine(nameof(AllowOnDiagonal)));

            // 3. Where a door or window tells its wall about itself, and where it marks the wall as taken.
            harmony.Patch(Find(typeof(VMEntity), "SetWallStyle"), prefix: Mine(nameof(SetDiagonalStyle)));
            harmony.Patch(Find(typeof(VMEntity), "SetWallUse"), prefix: Mine(nameof(SkipWallUse)));
            //    Walls are turned into the obstacles Sims route around when rooms are worked out. A straight wall with a
            //    door in it is left out there; a diagonal one never is, so take it out afterwards.
            harmony.Patch(Find(typeof(VMRoomMap), "GenerateMap"), prefix: Mine(nameof(BeforeRoomMap)), postfix: Mine(nameof(OpenDiagonalDoors)), finalizer: Mine(nameof(AfterRoomMap)));
            //    Each room also blocks the tiles around it that are not its own, and a little way past their edges.
            harmony.Patch(Find(typeof(VMRoomMap), "GenerateRoomObs"), postfix: Mine(nameof(ClearDiagonalDoorTiles)));

            // 4. The 3D wall is built by a local function of Generate, which the compiler turned into a hidden method
            //    that can only be found by its shape (as the Half Walls pack does).
            var lineGeom = typeof(WallComponentRC).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(x => x.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Where(x => x.ReturnType == typeof(void) && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] {
                    typeof(int), typeof(int), typeof(Vector2), typeof(Vector2), typeof(ushort), typeof(ushort), typeof(int), typeof(float), typeof(float), typeof(float) })).ToList();
            if (lineGeom.Count != 1)
                throw new Exception("The 3D wall code is not as expected (" + lineGeom.Count + "): this pack was made for a different version of Simitone");
            harmony.Patch(lineGeom[0], prefix: Mine(nameof(FitHoleToDiagonal)));
            harmony.Patch(Find(typeof(WallComponent), "GetWallSprite"), prefix: Mine(nameof(PlainDiagonalIn2D)));

            // The same pack's other two features, each in its own file.
            DiagonalStairs.Apply(harmony, Find, name => new HarmonyMethod(typeof(DiagonalStairs), name));
            DiagonalPools.Apply(harmony, Find, name => new HarmonyMethod(typeof(DiagonalPools), name));
        }

        // ---- what fits ----

        private const VMEntityFlags2 Architectural = VMEntityFlags2.ArchitectualDoor | VMEntityFlags2.ArchitectualWindow;
        private const int WallRequired = 15; //the four "wall required" bits of WallPlacementFlags: in front, right, behind, left

        private static bool IsDiagonal(Direction direction) => ((int)direction & 0xAA) != 0;
        private static int Eighths(Direction direction) => DiagonalGeometry.Eighths((int)direction);

        /// <summary>
        /// Where an object's wall is, seen from the object: the middle of the wall as an offset from the object's lead
        /// part (in sixteenths of a tile, facing north), and the way from the object to the wall.
        /// Only for a door or window with one wall, one tile wide: every part that needs a wall must need the same one.
        /// </summary>
        private static bool TryGetWall(VMMultitileGroup group, out Vector2 middle, out Vector2 toWall)
        {
            middle = toWall = Vector2.Zero;
            if (group == null || group.Objects.Count == 0 || group.Objects.Count != group.Offsets.Count) return false;
            var lead = group.Offsets[Math.Max(0, group.Objects.IndexOf(group.BaseObject))];
            bool found = false;
            for (int i = 0; i < group.Objects.Count; i++)
            {
                var part = group.Objects[i];
                if (!(part is VMGameObject)) return false;
                var required = part.GetValue(VMStackObjectVariable.WallPlacementFlags) & WallRequired;
                if (required == 0) continue;
                if (((VMEntityFlags2)part.GetValue(VMStackObjectVariable.FlagField2) & Architectural) == 0) return false; //a painting, a wall lamp
                if (!DiagonalGeometry.TryGetWallSide(required, out var side)) return false; //needs two walls: a corner piece
                var partMiddle = new Vector2(group.Offsets[i].x - lead.x, group.Offsets[i].y - lead.y) + side * 8;
                if (!found)
                {
                    middle = partMiddle; toWall = side; found = true;
                }
                else if ((partMiddle - middle).LengthSquared() > 1f) return false; //two walls side by side: a double door, a wide window
            }
            return found;
        }

        /// <summary>The one diagonal wall on a tile, if it is a wall (not a fence) and the only diagonal there.</summary>
        private static bool TryGetDiagonal(in WallTile wall, out bool horizontal)
        {
            var diagonal = wall.Segments & WallSegments.AnyDiag;
            horizontal = diagonal == WallSegments.HorizontalDiag;
            return (horizontal || diagonal == WallSegments.VerticalDiag) && wall.TopRightStyle == 1;
        }

        /// <summary>Whether this object is a door or window part standing on, and turned to face, a diagonal wall.</summary>
        private static bool OnDiagonal(VMEntity entity, in WallTile wall, Direction direction)
        {
            if (!IsDiagonal(direction) || !(entity is VMGameObject)) return false;
            if (!TryGetDiagonal(wall, out var horizontal)) return false;
            if (!TryGetWall(entity.MultitileGroup, out _, out var toWall)) return false;
            return DiagonalGeometry.Faces(Eighths(direction), toWall, horizontal);
        }

        // ---- 1 ----

        /// <summary>
        /// The player is putting an object on a tile. If it is a door or window and the tile has a diagonal wall, turn it
        /// to face the wall (the nearest of the two ways to the one asked for, so that rotating flips it) and move it
        /// so that the middle of its wall is the middle of the tile.
        /// This only depends on what was asked for and the wall, so the preview and the real placement agree.
        /// </summary>
        public static void SnapToDiagonal(VMMultitileGroup __instance, ref LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags)
        {
            if ((flags & VMPlaceRequestFlags.UserBuildableLimit) == 0 || pos == LotTilePos.OUT_OF_WORLD) return;
            if (pos.Level < 1 || pos.Level > context.Architecture.Stories) return;
            if (VM.UseWorld && context.World?.State?.CameraMode != CameraRenderMode._3D) return;
            if (!TryGetDiagonal(context.Architecture.GetWall(pos.TileX, pos.TileY, pos.Level), out var horizontal)) return;
            if (!TryGetWall(__instance, out var middle, out var toWall)) return;

            var eighths = DiagonalGeometry.ChooseDirection(Eighths(direction), toWall, horizontal);
            var offset = DiagonalGeometry.Rotate(middle, eighths);
            pos = new LotTilePos(
                (short)(pos.TileX * 16 + 8 - (int)Math.Round(offset.X)),
                (short)(pos.TileY * 16 + 8 - (int)Math.Round(offset.Y)),
                pos.Level);
            direction = (Direction)(1 << eighths);
        }

        // ---- 2 ----

        public static bool AllowOnDiagonal(VMEntity __instance, WallTile wall, Direction direction, bool checkUnused, ref VMPlacementError __result)
        {
            if (!OnDiagonal(__instance, wall, direction)) return true; //not ours: the engine's answer stands
            //one door or window to a wall. only asked of the part that holds the wall, like the engine's own check:
            //the other part is asked first, while this one's hole from where it stood before is still there
            var holdsWall = (__instance.GetValue(VMStackObjectVariable.WallPlacementFlags) & WallRequired) != 0;
            __result = (checkUnused && holdsWall && (wall.ObjSetTRStyle != 0 || wall.TopRightDoor))
                ? VMPlacementError.MustBeAgainstUnusedWall : VMPlacementError.Success;
            return false;
        }

        // ---- 3 ----

        /// <summary>Wall styles that were put on diagonal walls by doors and windows, for the drawing below to know them by.</summary>
        private static readonly HashSet<ushort> DiagonalStyles = new HashSet<ushort>();

        /// <summary>
        /// A door or window setting, or clearing, the style of its wall: the shape of the hole. The engine picks one of
        /// the tile's two straight walls by the way the object faces. A diagonal wall keeps its style in the same place
        /// as the top right one, and both drawing and this pack's routing look for it there.
        /// </summary>
        public static bool SetDiagonalStyle(VMEntity __instance, VMArchitecture arch, ushort value)
        {
            var pos = __instance.Position;
            if (pos == LotTilePos.OUT_OF_WORLD || pos.Level < 1) return true;
            var wall = arch.GetWall(pos.TileX, pos.TileY, pos.Level);
            if (!OnDiagonal(__instance, wall, __instance.Direction)) return true;

            wall.ObjSetTRStyle = value;
            if (((VMEntityFlags2)__instance.GetValue(VMStackObjectVariable.FlagField2) & VMEntityFlags2.ArchitectualDoor) != 0) wall.TopRightDoor = value != 0;
            if (value != 0) lock (DiagonalStyles) DiagonalStyles.Add(value);
            arch.SetWall(pos.TileX, pos.TileY, pos.Level, wall);
            return false;
        }

        /// <summary>
        /// The engine marks which of a tile's four straight walls an object has taken. None of them, here: the style
        /// above is the mark.
        /// </summary>
        public static bool SkipWallUse(VMEntity __instance, VMArchitecture arch)
        {
            var pos = __instance.Position;
            if (pos == LotTilePos.OUT_OF_WORLD || pos.Level < 1 || !IsDiagonal(__instance.Direction)) return true;
            return !OnDiagonal(__instance, arch.GetWall(pos.TileX, pos.TileY, pos.Level), __instance.Direction);
        }

        [ThreadStatic] private static WallTile[] MapWalls; //the walls rooms are being worked out from, while they are

        public static void BeforeRoomMap(WallTile[] Walls, List<VMRoom> rooms, out int __state)
        {
            MapWalls = Walls;
            __state = rooms.Count; //the rooms of this floor are added after these
        }

        public static Exception AfterRoomMap(Exception __exception) { MapWalls = null; return __exception; }

        private static bool IsDiagonalDoor(in WallTile wall) => wall.TopRightDoor && (wall.Segments & WallSegments.AnyDiag) != 0;

        /// <summary>
        /// A diagonal wall blocks Sims with four small squares along it, in each of the two rooms it divides. Where
        /// there is a door, remove them. A square at the end of the wall may have been joined to a neighbouring wall's;
        /// that one stays, as the door post it is.
        /// </summary>
        public static void OpenDiagonalDoors(WallTile[] Walls, int width, List<VMRoom> rooms, int __state)
        {
            HashSet<(int, int, int, int)> open = null;
            for (int i = 0; i < Walls.Length; i++)
            {
                if (!IsDiagonalDoor(Walls[i])) continue;
                if (open == null) open = new HashSet<(int, int, int, int)>();
                foreach (var square in DiagonalGeometry.WallSquares((i % width) << 4, (i / width) << 4, (Walls[i].Segments & WallSegments.HorizontalDiag) != 0))
                    open.Add(square);
            }
            if (open == null) return;

            for (int i = Math.Max(0, __state); i < rooms.Count; i++)
            {
                var room = rooms[i]; //(a struct: changed, then put back)
                if (room.WallObs == null || room.WallObs.RemoveAll(x => open.Contains((x.x1, x.y1, x.x2, x.y2))) == 0) continue;
                room.RoutingObstacles = VMObstacleSet.RoughBalanced(room.WallObs.Concat(room.RoomObs ?? new List<VMObstacle>()).ToList());
                rooms[i] = room;
            }
        }

        /// <summary>
        /// A room blocks every tile around it that belongs to another room, and three sixteenths past the tile's edges.
        /// For a door in a straight wall the engine leaves the tile on the far side open. A door in a diagonal wall has
        /// its far side in the same tile, which already belongs to both rooms, but the neighbours' three sixteenths
        /// reach into it, up to where the far half of the door stands. Push them back to the tile's edge.
        /// (The walls themselves still block: this is only the room's own margin.)
        /// </summary>
        public static void ClearDiagonalDoorTiles(VMRoomMap __instance, ushort room, sbyte level, VMContext context, List<VMObstacle> __result)
        {
            var walls = MapWalls;
            if (walls == null)
            {
                var all = context?.Architecture?.Walls;
                if (all == null || level < 1 || level > all.Length) return;
                walls = all[level - 1];
            }
            var map = __instance.Map;
            if (__result == null || walls == null || map == null || __instance.Width <= 0) return;
            var length = Math.Min(walls.Length, map.Length);
            for (int i = 0; i < length; i++)
            {
                if (!IsDiagonalDoor(walls[i])) continue;
                if ((map[i] & 0xFFFF) != room && ((map[i] >> 16) & 0x7FFF) != room) continue;
                int x = (i % __instance.Width) << 4, y = (i / __instance.Width) << 4;
                foreach (var obstacle in __result) DiagonalGeometry.PushOut(obstacle, x, y);
            }
        }

        // ---- 4 ----

        /// <summary>
        /// Before a strip of 3D wall is built. A style's hole is drawn across a whole strip, and a diagonal strip is
        /// longer than a straight one, so the hole would come out wider than the door. Draw it at its own width, in the
        /// middle. The wallpaper on that strip goes with it.
        /// </summary>
        public static bool FitHoleToDiagonal(Vector2 __2, Vector2 __3, ushort __5, int __6, ref float __7, ref float __8)
        {
            if (__5 == DiagonalPools.EdgeStyle) return false; //a pool's diagonal edge: not a wall at all, build nothing
            if (__6 == 4 || __5 < 2) return true; //a fence, or plain wall
            var along = __3 - __2;
            if (Math.Abs(along.X) < 0.5f || Math.Abs(along.Y) < 0.5f) return true; //straight, or the cap on a wall's end
            lock (DiagonalStyles) if (!DiagonalStyles.Contains(__5)) return true;
            __7 = DiagonalGeometry.Widen(__7);
            __8 = DiagonalGeometry.Widen(__8);
            return true;
        }

        /// <summary>
        /// In 2D, a diagonal wall with a door or window style would be drawn with the style's picture for a diagonal
        /// wall, and the styles doors and windows bring were never drawn one. Draw the wall plain there instead.
        /// </summary>
        public static bool PlainDiagonalIn2D(ref WallStyle style, int rotation, WorldState world, ref _2DSprite __result)
        {
            if (style == null) return true;
            if (style.ID == DiagonalPools.EdgeStyle)
            {
                //a pool's diagonal edge: a sprite with no picture, which the caller does not draw
                __result = world._2D.NewSprite(_2DBatchRenderMode.WALL);
                return false;
            }
            if (rotation < 2) return true;
            lock (DiagonalStyles) if (DiagonalStyles.Contains(style.ID)) style = null;
            return true;
        }
    }

    /// <summary>The pack's arithmetic, apart from the game so that it can be checked on its own.</summary>
    public static partial class DiagonalGeometry
    {
        /// <summary>Eighths of a turn from north, for a direction's bit.</summary>
        public static int Eighths(int direction)
        {
            for (int i = 0; i < 8; i++) if ((direction & (1 << i)) != 0) return i;
            return 0;
        }

        /// <summary>Turns a vector that is right for an object facing north to one for an object facing this way, as the engine does.</summary>
        public static Vector2 Rotate(Vector2 v, int eighths)
        {
            var angle = eighths * Math.PI / 4.0;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        /// <summary>The way to the one wall an object needs, for an object facing north. North is up the Y axis, towards 0.</summary>
        public static bool TryGetWallSide(int required, out Vector2 side)
        {
            switch (required)
            {
                case 1: side = new Vector2(0, -1); return true; //in front
                case 2: side = new Vector2(1, 0); return true;  //on the right
                case 4: side = new Vector2(0, 1); return true;  //behind
                case 8: side = new Vector2(-1, 0); return true; //on the left
            }
            side = Vector2.Zero;
            return false;
        }

        /// <summary>
        /// Whether an object facing this way has its wall side square on to a diagonal wall. A "horizontal" diagonal
        /// runs from a tile's bottom left to its top right corner (x + y stays the same along it), a "vertical" one
        /// from top left to bottom right (x - y stays the same).
        /// </summary>
        public static bool Faces(int eighths, Vector2 toWall, bool horizontal)
        {
            var way = Rotate(toWall, eighths);
            return Math.Abs(horizontal ? way.X - way.Y : way.X + way.Y) < 0.01f && Math.Abs(way.X) > 0.5f;
        }

        /// <summary>
        /// The way to face a diagonal wall that is nearest the way asked for. There are always two, opposite each other.
        /// From one of the four main directions one of them is 45 degrees away. From a diagonal along the wall they are
        /// equally far, and the clockwise one is taken.
        /// </summary>
        public static int ChooseDirection(int asked, Vector2 toWall, bool horizontal)
        {
            foreach (var turn in new[] { 0, 1, -1, 2, -2, 3, -3, 4 })
            {
                var eighths = ((asked + turn) % 8 + 8) % 8;
                if (Faces(eighths, toWall, horizontal)) return eighths;
            }
            return asked; //(not reached: one of the eight always faces it)
        }

        /// <summary>
        /// The four squares the engine blocks a diagonal wall with, for the tile whose corner is at (x, y), in sixteenths
        /// of a tile: left, top, right, bottom. The same numbers as VMRoomMap.GenerateMap.
        /// </summary>
        public static IEnumerable<(int, int, int, int)> WallSquares(int x, int y, bool horizontal)
        {
            for (int i = 0; i < 4; i++)
            {
                var along = i * 4 - 1;
                var across = horizontal ? 11 - i * 4 : along;
                yield return (x + across, y + along, x + across + 6, y + along + 6);
            }
        }

        /// <summary>
        /// Moves the edge of an obstacle that reaches a little way into the tile whose corner is at (x, y) back to the
        /// tile's edge. An obstacle that covers the tile, or lies clear of it, is left as it is.
        /// </summary>
        public static void PushOut(VMObstacle obstacle, int x, int y)
        {
            int x2 = x + 16, y2 = y + 16;
            if (obstacle.x1 >= x2 || obstacle.x2 <= x || obstacle.y1 >= y2 || obstacle.y2 <= y) return; //clear of it
            bool acrossX = obstacle.x1 <= x && obstacle.x2 >= x2, acrossY = obstacle.y1 <= y && obstacle.y2 >= y2;
            if (acrossX && acrossY) return; //covers it: not a margin
            if (!acrossX)
            {
                //in from the left or the right (a corner counts as this too)
                if (obstacle.x1 > x) obstacle.x1 = x2; else obstacle.x2 = x;
            }
            else
            {
                //in from above or below
                if (obstacle.y1 > y) obstacle.y1 = y2; else obstacle.y2 = y;
            }
        }

        /// <summary>A place across a wall strip, from 0 to 1, for a strip that is as long as a tile's diagonal.</summary>
        public static float Widen(float across) => 0.5f + (across - 0.5f) * 1.41421356f;
    }
}
