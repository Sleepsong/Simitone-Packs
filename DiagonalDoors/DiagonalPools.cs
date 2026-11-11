/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.Content.Model;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Components.Geometry;
using FSO.LotView.Model;
using FSO.LotView.Utils;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DiagonalDoors
{
    /// <summary>
    /// Pools with diagonal edges.
    ///
    /// The engine already knows half tiles. A tile with a diagonal wall keeps a floor for each side of the wall, in
    /// the wall's own data, and everything that asks what is underfoot (rooms, routing, placing objects, drawing the
    /// floor) asks per half. A half can even be pool: the pool tool sets both halves when dragged over such a tile. What
    /// the engine has no way to say is "pool on one side, ground on the other, and no wall". So:
    ///
    ///  1. A new wall style that is no wall at all, only the line between the halves: the pool's edge.
    ///  2. The pool tool, with Shift held, puts one on the tile under the cursor, with the pool in the corner of the
    ///     tile the cursor is nearest. It goes through the engine's own floor command, so the preview, the price and
    ///     undoing with Ctrl all come with it.
    ///  3. Tidying: an edge only makes sense with pool on exactly one side of it.
    ///  4. Drawing: the edge is not drawn as a wall, and the pool is drawn as a triangle with a rim along the
    ///     diagonal (PoolMesh).
    /// </summary>
    public static class DiagonalPools
    {
        /// <summary>
        /// The style of a pool's diagonal edge. Like the Half Walls pack's style, it is well clear of the game's own,
        /// and is kept in the lot's save as it is: a saved diagonal pool needs this pack to be drawn as one.
        /// </summary>
        public const ushort EdgeStyle = 0x7F10;
        public const ushort Pool = 65535;

        /// <summary>Set on the "direction" of a single tile floor command to say: a diagonal pool, in this corner.</summary>
        public const ushort CornerCommand = 0x100;

        public static void Apply(Harmony harmony, Func<Type, string, MethodInfo> find, Func<string, HarmonyMethod> mine)
        {
            // 1.
            harmony.Patch(find(typeof(WorldWallProvider), "InitGlobals"), postfix: mine(nameof(AddEdgeStyle)));

            // 2.
            harmony.Patch(find(typeof(UIFloorPainter), "Update"), prefix: mine(nameof(PoolToolWithShift)));
            // 2 and 3. Every change to floors by the player comes through here, for real and for the preview.
            harmony.Patch(find(typeof(VMArchitectureTools), "FloorPatternRect"), prefix: mine(nameof(CornerCommandOrNot)), postfix: mine(nameof(TidyEdges)));

            // 4. (the walls are hidden in DiagonalDoorsPack's own wall patches, which ask IsEdge)
            harmony.Patch(find(typeof(Modelled3DPool), "GenerateGeometry"), prefix: mine(nameof(PoolWithDiagonals)));
        }

        // ---- the data ----

        /// <summary>Which corner of its tile a half pool is in. The other half is ground, or any floor.</summary>
        public enum Corner { TopLeft = 0, TopRight = 1, BottomRight = 2, BottomLeft = 3 }

        public static bool IsEdge(in WallTile wall) => (wall.Segments & WallSegments.AnyDiag) != 0 && wall.TopRightStyle == EdgeStyle;

        /// <summary>
        /// A tile's two halves as the engine keeps them. For a "horizontal" diagonal (bottom left to top right) the
        /// halves are the top left and bottom right corners; for a "vertical" one, the bottom left and top right.
        /// TopLeftStyle is the floor of the first of each pair and TopLeftPattern of the second (see
        /// VMArchitecture.GetPreciseFloor).
        /// </summary>
        public static WallTile MakeEdge(Corner corner, ushort otherFloor)
        {
            var wall = new WallTile { TopRightStyle = EdgeStyle };
            wall.Segments = (corner == Corner.TopLeft || corner == Corner.BottomRight) ? WallSegments.HorizontalDiag : WallSegments.VerticalDiag;
            if (corner == Corner.TopLeft || corner == Corner.BottomLeft) { wall.TopLeftStyle = Pool; wall.TopLeftPattern = otherFloor; }
            else { wall.TopLeftPattern = Pool; wall.TopLeftStyle = otherFloor; }
            return wall;
        }

        /// <summary>The corner the pool is in, for a tile split by any diagonal with pool on exactly one side.</summary>
        public static bool TryGetCorner(in WallTile wall, out Corner corner, out ushort otherFloor)
        {
            corner = Corner.TopLeft; otherFloor = 0;
            var diagonal = wall.Segments & WallSegments.AnyDiag;
            if (diagonal != WallSegments.HorizontalDiag && diagonal != WallSegments.VerticalDiag) return false;
            bool first = wall.TopLeftStyle == Pool, second = wall.TopLeftPattern == Pool;
            if (first == second) return false;
            bool horizontal = diagonal == WallSegments.HorizontalDiag;
            corner = horizontal ? (first ? Corner.TopLeft : Corner.BottomRight) : (first ? Corner.BottomLeft : Corner.TopRight);
            otherFloor = first ? wall.TopLeftPattern : wall.TopLeftStyle;
            return true;
        }

        // ---- 1 ----

        public static void AddEdgeStyle(WorldWallProvider __instance)
        {
            if (__instance.GetWallStyle(EdgeStyle) != null) return;
            //no pictures: it is never drawn. it only has to exist, for the places that look a wall's style up (its price, for one)
            AccessTools.Method(typeof(WorldWallProvider), "AddWallStyle").Invoke(__instance, new object[] { new WallStyle
            {
                ID = EdgeStyle,
                Price = 0,
                Name = "Diagonal Pool Edge",
                Description = "The edge of a pool that crosses a tile diagonally."
            } });
        }

        // ---- 2 ----

        private static VMArchitectureCommand LastCommand;

        /// <summary>
        /// The pool tool with Shift held: one tile, a diagonal pool in the corner nearest the cursor. With Ctrl as
        /// well, remove one. (Shift is the floor tools' "fill the room", which the pool tool has no use for.)
        /// </summary>
        public static bool PoolToolWithShift(UIFloorPainter __instance, UpdateState state, ushort ___Pattern, List<VMArchitectureCommand> ___Commands, VM ___vm, World ___World)
        {
            if (___Pattern != Pool || !__instance.Modifiers.IsSet(UILotControlModifiers.SHIFT) || ___World?.State == null) return true;

            var at = ___World.EstTileAtPosWithScroll(new Vector2(__instance.MousePosition.X, __instance.MousePosition.Y));
            int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y);
            var command = new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.FLOOR_RECT,
                level = ___World.State.Level,
                pattern = __instance.Modifiers.IsSet(UILotControlModifiers.CTRL) ? (ushort)0 : Pool,
                style = (ushort)(CornerCommand | (int)DiagonalGeometry.NearestCorner(at.X - x, at.Y - y)),
                x = x, y = y, x2 = 0, y2 = 0
            };

            var arch = ___vm.Context.Architecture;
            if (___Commands.Count != 1 || !___Commands[0].Equals(command) || !LastCommand.Equals(command))
            {
                LastCommand = command;
                ___Commands.Clear();
                ___Commands.Add(command);
                arch.SignalRedraw();
            }
            arch.Commands.Clear();
            arch.Commands.Add(command);
            if (state?.UIState?.TooltipProperties != null) state.UIState.TooltipProperties.Show = false;
            return false;
        }

        public static bool CornerCommandOrNot(VMArchitecture target, Rectangle rect, ushort dir, ushort pattern, sbyte level, ref PatternReplaceCount __result)
        {
            if ((dir & CornerCommand) == 0 || rect.Width != 0 || rect.Height != 0) return true;
            __result = new PatternReplaceCount(true);
            var old = PlaceCorner(target, (short)rect.X, (short)rect.Y, (Corner)(dir & 3), pattern, level);
            if (old != null) __result.Add(old.Value);
            return false;
        }

        /// <summary>
        /// Puts a diagonal pool on a tile, or (pattern 0) takes one off. Returns the floor that was replaced, for the
        /// price, or null if nothing changed.
        /// A tile can take one if it has no walls and nothing stands on it, and the ground under it is level. A whole
        /// tile of pool can be cut down to a corner, which is how to cut the corner off a pool already there.
        /// </summary>
        public static ushort? PlaceCorner(VMArchitecture target, short x, short y, Corner corner, ushort pattern, sbyte level)
        {
            if (level != 1 || target.OutsideClip(x, y, level)) return null; //pools are dug into the ground
            var wall = target.GetWall(x, y, level);
            var floor = target.GetFloor(x, y, level);
            bool isCorner = IsEdge(wall) && TryGetCorner(wall, out _, out _);
            TryGetCorner(wall, out var oldCorner, out var otherFloor);

            if (pattern == 0)
            {
                if (!isCorner) return null;
                target.SetWall(x, y, level, new WallTile());
                target.SetFloor(x, y, level, new FloorTile { Pattern = otherFloor }, true);
                return Pool;
            }
            if (pattern != Pool) return null;
            if (isCorner)
            {
                if (oldCorner == corner) return null;
            }
            else
            {
                if (wall.Segments != 0) return null;
                otherFloor = (floor.Pattern >= 65534) ? (ushort)0 : floor.Pattern;
            }

            var edge = MakeEdge(corner, otherFloor);
            var pos = LotTilePos.FromBigTile(x, y, level);
            if (target.Context != null && !target.Context.CheckWallValid(pos, edge)) return null; //something stands there, or the ground slopes
            target.SetFloor(x, y, level, new FloorTile(), true); //(a split tile's floors are in its wall)
            target.SetWall(x, y, level, edge);
            return isCorner ? (ushort)0 : floor.Pattern;
        }

        // ---- 3 ----

        /// <summary>
        /// After the engine has changed floors: any pool edge left with pool on both sides, or on neither, goes, and
        /// the tile becomes one floor again. That is what dragging the pool tool or a floor over a diagonal pool does.
        /// </summary>
        public static void TidyEdges(VMArchitecture target, Rectangle rect, ushort dir, sbyte level, bool __runOriginal)
        {
            if (!__runOriginal || level < 1 || level > target.Stories) return;
            for (int y = Math.Max(0, rect.Y); y <= Math.Min(target.Height - 1, rect.Y + rect.Height); y++)
            {
                for (int x = Math.Max(0, rect.X); x <= Math.Min(target.Width - 1, rect.X + rect.Width); x++)
                {
                    var wall = target.GetWall((short)x, (short)y, level);
                    if (!IsEdge(wall) || TryGetCorner(wall, out _, out _)) continue;
                    //both sides are the same now, or two different floors: keep the second's unless it is bare ground
                    var floor = wall.TopLeftPattern != 0 ? wall.TopLeftPattern : wall.TopLeftStyle;
                    target.SetWall((short)x, (short)y, level, new WallTile());
                    target.SetFloor((short)x, (short)y, level, new FloorTile { Pattern = floor }, true);
                }
            }
        }

        // ---- 4 ----

        private static readonly AccessTools.FieldRef<Modelled3DFloor, TerrainParallaxVertex[]> VerticesOf = AccessTools.FieldRefAccess<Modelled3DFloor, TerrainParallaxVertex[]>("Vertices");
        private static readonly AccessTools.FieldRef<Modelled3DFloor, int[]> IndicesOf = AccessTools.FieldRefAccess<Modelled3DFloor, int[]>("Indices");
        private delegate void AppendTileCall(Modelled3DFloor floor, List<int> inds, List<TerrainParallaxVertex> verts, Modelled3DFloorTile tile, ushort index);
        private static readonly AppendTileCall AppendTile = AccessTools.MethodDelegate<AppendTileCall>(AccessTools.Method(typeof(Modelled3DFloor), "AppendTile"));
        private static PoolMesh Meshes;

        /// <summary>
        /// The pool's 3D model, built by this pack when the pool has a diagonal in it. The engine builds it from ready
        /// made pieces, one for each way a whole tile can border ground. That cannot be patched into doing triangles,
        /// so while a pool has any, this does the whole pool: whole tiles exactly as the engine does, and half tiles
        /// from the same pieces, cut along the diagonal and given a rim there (PoolMesh). A pool with no diagonals is
        /// left to the engine.
        /// </summary>
        public static bool PoolWithDiagonals(Modelled3DPool __instance)
        {
            var bp = __instance.Bp;
            if (bp?.Walls == null || bp.Floors == null) return true;
            //which floor this pool is on (the engine does not tell it)
            int level = 0;
            var levels = bp.FloorGeom?.Floors;
            if (levels != null)
                for (int i = 0; i < levels.Length; i++)
                    if (levels[i] != null && levels[i].GroupForTileType.ContainsValue(__instance)) { level = i; break; }
            if (level >= bp.Walls.Length || level >= bp.Floors.Length) return true;
            var walls = bp.Walls[level]; var floors = bp.Floors[level];

            //(the engine files the two halves of a tile under the tile's number and that plus 32768)
            var tiles = new SortedSet<int>(__instance.GeomForOffset.Keys.Select(x => x & 0x7FFF).Where(x => x < walls.Length && x < floors.Length));
            if (!tiles.Any(x => TryGetCorner(walls[x], out _, out _))) return true;

            Modelled3DPool.EnsureTilesLoaded();
            if (Meshes == null)
            {
                Meshes = new PoolMesh(
                    (Modelled3DFloorTile[])AccessTools.Field(typeof(Modelled3DPool), "PoolTiles").GetValue(null),
                    (Modelled3DFloorTile[])AccessTools.Field(typeof(Modelled3DPool), "CornerTiles").GetValue(null));
            }

            var verts = new List<TerrainParallaxVertex>();
            var inds = new List<int>();
            foreach (var tile in tiles)
            {
                int x = tile % bp.Width, y = tile / bp.Width;
                Func<int, int, int, bool> poolTowards = (dx, dy, _) => HasPoolTowards(walls, floors, bp.Width, bp.Height, x + dx, y + dy, -dx, -dy);
                foreach (var piece in Meshes.PiecesFor(TryGetCorner(walls[tile], out var corner, out _) ? corner : (Corner?)null, poolTowards))
                    AppendTile(__instance, inds, verts, piece, (ushort)tile);
            }
            VerticesOf(__instance) = verts.ToArray();
            IndicesOf(__instance) = inds.ToArray();
            return false;
        }

        /// <summary>
        /// Whether the tile at (x, y) has pool where it meets a neighbour: along the side facing (dx, dy), or for a
        /// diagonal neighbour at the corner facing it. Off the lot counts as pool, as it does for the engine.
        /// </summary>
        public static bool HasPoolTowards(WallTile[] walls, FloorTile[] floors, int width, int height, int x, int y, int dx, int dy)
        {
            if (x <= 0 || x >= width - 1 || y <= 0 || y >= height - 1) return true;
            var wall = walls[y * width + x];
            if ((wall.Segments & WallSegments.AnyDiag) == 0) return floors[y * width + x].Pattern == Pool;
            if (wall.TopLeftStyle == Pool && wall.TopLeftPattern == Pool) return true;
            if (!TryGetCorner(wall, out var corner, out _)) return false;
            return DiagonalGeometry.CornerReaches(corner, dx, dy);
        }
    }

    public static partial class DiagonalGeometry
    {
        /// <summary>The corner of a tile nearest a place in it (0 to 1 each way).</summary>
        public static DiagonalPools.Corner NearestCorner(float x, float y)
        {
            if (x < 0.5f) return y < 0.5f ? DiagonalPools.Corner.TopLeft : DiagonalPools.Corner.BottomLeft;
            return y < 0.5f ? DiagonalPools.Corner.TopRight : DiagonalPools.Corner.BottomRight;
        }

        /// <summary>
        /// Whether the triangle in a corner of a tile reaches the tile's side in a direction (one of dx, dy is 0), or
        /// its corner in a diagonal direction. It has the two sides that meet at its own corner, and every corner of
        /// the tile but the one opposite.
        /// </summary>
        public static bool CornerReaches(DiagonalPools.Corner corner, int dx, int dy)
        {
            //the way from the middle of the tile to the triangle's corner
            int cx = (corner == DiagonalPools.Corner.TopRight || corner == DiagonalPools.Corner.BottomRight) ? 1 : -1;
            int cy = (corner == DiagonalPools.Corner.BottomLeft || corner == DiagonalPools.Corner.BottomRight) ? 1 : -1;
            if (dx != 0 && dy != 0) return !(dx == -cx && dy == -cy);
            return (dx != 0) ? dx == cx : dy == cy;
        }
    }
}
