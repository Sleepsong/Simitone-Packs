/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Panels;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace DiagonalDoors
{
    /// <summary>
    /// Stairs that run diagonally.
    ///
    /// A staircase is one object of many parts: the steps and a pad at the foot on one floor, and on the floor above a
    /// pad at the head and "stubs" over the steps, which are what keep the hole in the upper floor open. The engine
    /// already turns such an object 45 degrees as a whole (Build Cheats' rotate45). Two things are missing:
    ///
    ///  1. Turning. Without the cheat an object only turns 90 degrees at a time. While stairs are held, in full 3D,
    ///     they turn 45.
    ///  2. The hole. When stairs are placed the engine removes the floor tile under each stub. Straight stairs cover
    ///     whole tiles, so that is the whole hole. Diagonal stairs cross tiles at an angle, and the tiles they only
    ///     partly cover would be left as floor across the stairway. Remove those too.
    /// </summary>
    public static class DiagonalStairs
    {
        public static void Apply(Harmony harmony, Func<Type, string, System.Reflection.MethodInfo> find, Func<string, HarmonyMethod> mine)
        {
            // 1. The two places the player turns a held object: the rotate buttons and keys, and dragging with the mouse.
            harmony.Patch(find(typeof(UIObjectHolder), "RotateObject"), prefix: mine(nameof(BeforeTurn)), finalizer: mine(nameof(AfterTurn)));
            harmony.Patch(find(typeof(UIObjectHolder), "Update"), prefix: mine(nameof(BeforeTurn)), finalizer: mine(nameof(AfterTurn)));

            // 2. Entry point 11 is "the player placed this", which is where the engine makes the hole for stairs.
            harmony.Patch(AccessTools.Method(typeof(VMEntity), "ExecuteEntryPoint", new[] { typeof(int), typeof(VMContext), typeof(bool), typeof(VMEntity), typeof(short[]) })
                ?? throw new Exception("VMEntity.ExecuteEntryPoint was not found: this pack was made for a different version of Simitone"),
                postfix: mine(nameof(WidenHole)));
        }

        /// <summary>Stairs: an object with a part on the floor above.</summary>
        public static bool IsStairs(VMMultitileGroup group)
        {
            if (group == null || group.Objects.Count < 2) return false;
            foreach (var part in group.Objects)
            {
                if (part is VMGameObject && part.Object?.OBJ != null && part.Object.OBJ.LevelOffset > 0) return true;
            }
            return false;
        }

        // ---- 1 ----

        /// <summary>
        /// Simitone turns objects 45 degrees while BuildOptions.Rotate45 is set and the lot is in full 3D. Set it for
        /// as long as the holder is working with stairs, and put it back after.
        /// </summary>
        public static void BeforeTurn(UIObjectHolder __instance, out bool? __state)
        {
            __state = null;
            if (BuildOptions.Rotate45 || !IsStairs(__instance.Holding?.Group)) return;
            __state = BuildOptions.Rotate45;
            BuildOptions.Rotate45 = true;
        }

        public static Exception AfterTurn(Exception __exception, bool? __state)
        {
            if (__state != null) BuildOptions.Rotate45 = __state.Value;
            return __exception;
        }

        // ---- 2 ----

        /// <summary>
        /// After the engine has removed the floor tile under a stub of diagonal stairs: also remove the neighbouring
        /// tiles the stub reaches well into, unless something stands on them.
        /// </summary>
        public static void WidenHole(VMEntity __instance, int entry, VMContext context)
        {
            if (entry != 11 || !(__instance is VMGameObject) || __instance.Object?.OBJ == null || __instance.Object.OBJ.LevelOffset <= 0) return;
            var pos = __instance.Position;
            if (pos == LotTilePos.OUT_OF_WORLD || pos.Level < 2 || ((int)__instance.Direction & 0xAA) == 0) return;
            if (((VMPlacementFlags)__instance.GetValue(VMStackObjectVariable.PlacementFlags) & VMPlacementFlags.InAir) == 0) return; //the pad at the head: it stands on floor

            var arch = context.Architecture;
            foreach (var tile in DiagonalGeometry.TilesUnderDiamond(pos.x, pos.y))
            {
                if (tile.X == pos.TileX && tile.Y == pos.TileY) continue; //the engine's own
                if (tile.X < 0 || tile.Y < 0 || tile.X >= arch.Width || tile.Y >= arch.Height) continue;
                var x = (short)tile.X; var y = (short)tile.Y;
                if (arch.GetFloor(x, y, pos.Level).Pattern == 0) continue;
                if (arch.GetWall(x, y, pos.Level).Segments != 0) continue;
                if (Occupied(context, __instance.MultitileGroup, LotTilePos.FromBigTile(x, y, pos.Level))) continue;
                arch.SetFloor(x, y, pos.Level, new FloorTile(), true);
            }
        }

        /// <summary>Whether anything but these stairs stands on a tile. The stairs' own pad at the head counts: it needs its floor.</summary>
        private static bool Occupied(VMContext context, VMMultitileGroup stairs, LotTilePos tile)
        {
            var objects = context.ObjectQueries.GetObjectsAt(tile);
            if (objects == null) return false;
            foreach (var obj in objects)
            {
                if (obj.MultitileGroup != stairs) return true;
                if (((VMPlacementFlags)obj.GetValue(VMStackObjectVariable.PlacementFlags) & VMPlacementFlags.InAir) == 0) return true;
            }
            return false;
        }
    }

    public static partial class DiagonalGeometry
    {
        /// <summary>How much of a tile a part of diagonal stairs has to cover for the tile to be opened: about a tenth.</summary>
        public const int HoleThreshold = 24; //of 256 points

        /// <summary>
        /// The tiles that a tile-sized square turned 45 degrees reaches well into, given its middle in sixteenths of a
        /// tile. Counted by points: the square is checked against each of a tile's 256 sixteenth-squares.
        /// </summary>
        public static List<Point> TilesUnderDiamond(int x, int y)
        {
            var count = new Dictionary<Point, int>();
            //a square of side 16 turned 45 degrees is the points within 8 * sqrt(2) of its middle, measured |dx| + |dy|
            const float reach = 11.3137f;
            for (int py = y - 12; py < y + 12; py++)
            {
                for (int px = x - 12; px < x + 12; px++)
                {
                    if (Math.Abs(px + 0.5f - x) + Math.Abs(py + 0.5f - y) > reach) continue;
                    var tile = new Point((int)Math.Floor(px / 16f), (int)Math.Floor(py / 16f));
                    count.TryGetValue(tile, out var n);
                    count[tile] = n + 1;
                }
            }
            var result = new List<Point>();
            foreach (var item in count) if (item.Value >= HoleThreshold) result.Add(item.Key);
            return result;
        }
    }
}
