/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Components.Geometry;
using FSO.LotView.Utils;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Corner = DiagonalDoors.DiagonalPools.Corner;

namespace DiagonalDoors
{
    /// <summary>
    /// The pieces of a pool's 3D model, for whole tiles and for the half tiles of a diagonal edge.
    ///
    /// The engine has one model for each way a whole tile of pool can border ground: sixteen of them, numbered by which
    /// of the four neighbours are pool too. Number 15 is the bare floor of the pool. The four that lack one neighbour
    /// (14, 13, 11 and 7) are each one rim, a strip along one side of the tile. Half tiles are made from those:
    ///  - the floor, cut along the diagonal;
    ///  - the rims of the triangle's two straight sides, where there is ground beyond them, cut the same way;
    ///  - a rim along the diagonal: the bottom rim moved to the middle of the tile, stretched to the diagonal's length,
    ///    turned 45 degrees to face the pool, and trimmed to the tile.
    /// Pieces are in tile space: X and Z from 0 to 1, Y up.
    /// </summary>
    public class PoolMesh
    {
        private readonly Modelled3DFloorTile[] PoolTiles;   //the engine's sixteen
        private readonly Modelled3DFloorTile[] CornerTiles; //and its four fillers for inside corners
        private readonly Dictionary<int, Modelled3DFloorTile> Halves = new Dictionary<int, Modelled3DFloorTile>();

        //the eight neighbours in the engine's order, and the bit (PoolSegments) each sets
        private static readonly Point[] Around = { new Point(0, -1), new Point(1, -1), new Point(1, 0), new Point(1, 1), new Point(0, 1), new Point(-1, 1), new Point(-1, 0), new Point(-1, -1) };
        private const int North = 1, NorthEast = 2, East = 4, SouthEast = 8, South = 16, SouthWest = 32, West = 64, NorthWest = 128;

        public PoolMesh(Modelled3DFloorTile[] poolTiles, Modelled3DFloorTile[] cornerTiles)
        {
            PoolTiles = poolTiles; CornerTiles = cornerTiles;
        }

        /// <summary>The engine's model with only the rim on one side of the tile.</summary>
        private Modelled3DFloorTile Rim(int dx, int dy)
        {
            if (dy < 0) return PoolTiles[14];
            if (dx < 0) return PoolTiles[13];
            if (dy > 0) return PoolTiles[11];
            return PoolTiles[7];
        }

        /// <summary>
        /// The pieces to draw for a tile: a whole tile of pool (corner null), or the half in a corner.
        /// pool(dx, dy, fx, fy) says whether the neighbour at (dx, dy) has pool along its side facing (fx, fy), or for
        /// a diagonal facing, at that corner of it.
        /// </summary>
        public IEnumerable<Modelled3DFloorTile> PiecesFor(Corner? corner, Func<int, int, int, int, bool> pool)
        {
            if (corner == null)
            {
                //what Modelled3DPool.GenerateGeometry does...
                int adj = 0;
                for (int i = 0; i < Around.Length; i++) if (pool(Around[i].X, Around[i].Y, -Around[i].X, -Around[i].Y)) adj |= 1 << i;
                int number = 0;
                if ((adj & North) != 0) number |= 1;
                if ((adj & West) != 0) number |= 2;
                if ((adj & South) != 0) number |= 4;
                if ((adj & East) != 0) number |= 8;
                yield return PoolTiles[15];
                if (number != 15) yield return PoolTiles[number];
                //...but for the fillers, below
                if (InsideCorner(pool, -1, -1)) yield return CornerTiles[0];
                if (InsideCorner(pool, -1, 1)) yield return CornerTiles[1];
                if (InsideCorner(pool, 1, 1)) yield return CornerTiles[2];
                if (InsideCorner(pool, 1, -1)) yield return CornerTiles[3];
                yield break;
            }

            GetCornerWay(corner.Value, out int cx, out int cy);
            bool rimX = !pool(cx, 0, -cx, 0), rimY = !pool(0, cy, 0, -cy);
            int key = (int)corner.Value | (rimX ? 4 : 0) | (rimY ? 8 : 0);
            if (!Halves.TryGetValue(key, out var half))
            {
                half = BuildHalf(cx, cy, rimX, rimY);
                Halves[key] = half;
            }
            yield return half;
            //a half tile has one corner of its own, and that can be an inside corner of the pool too
            if (InsideCorner(pool, cx, cy)) yield return CornerTiles[cx < 0 ? (cy < 0 ? 0 : 1) : (cy < 0 ? 3 : 2)];
        }

        /// <summary>
        /// Whether the pool's edge turns an inside corner at the corner of this tile towards (cx, cy), which the engine
        /// covers with a square filler: the tiles either side are pool, and the one diagonally across is not.
        /// The engine stops there, since with whole tiles that always means two rims meeting at a right angle. With
        /// half tiles it need not: where one diagonal edge ends at the corner and the next begins, the edge runs
        /// straight through it, and a filler would be a stray block in the pool. So the rims are checked as well: each
        /// of the two tiles must have pool along its side that runs up to the corner from outside.
        /// </summary>
        private static bool InsideCorner(Func<int, int, int, int, bool> pool, int cx, int cy)
        {
            if (!pool(cx, 0, -cx, 0) || !pool(0, cy, 0, -cy)) return false; //the tiles either side
            if (pool(cx, cy, -cx, -cy)) return false;                        //diagonally across
            return pool(cx, 0, 0, cy) && pool(0, cy, cx, 0);                 //their rims that meet here
        }

        /// <summary>The way from the middle of a tile to one of its corners.</summary>
        public static void GetCornerWay(Corner corner, out int cx, out int cy)
        {
            cx = (corner == Corner.TopRight || corner == Corner.BottomRight) ? 1 : -1;
            cy = (corner == Corner.BottomLeft || corner == Corner.BottomRight) ? 1 : -1;
        }

        // ---- building a half tile ----

        private struct V
        {
            public Vector3 P; public Vector2 T; public Vector3 N; //place (X and Z from the middle of the tile), texture, normal
            public static V Lerp(in V a, in V b, float t) => new V { P = Vector3.Lerp(a.P, b.P, t), T = Vector2.Lerp(a.T, b.T, t), N = Vector3.Lerp(a.N, b.N, t) };
        }

        private static readonly Vector3 Middle = new Vector3(0.5f, 0, 0.5f);

        private static List<V> Triangles(Modelled3DFloorTile tile)
        {
            var result = new List<V>(tile.Indices.Length);
            foreach (var index in tile.Indices)
            {
                var v = tile.Vertices[index];
                result.Add(new V { P = v.Position - Middle, T = new Vector2(v.GrassInfo.Y, v.GrassInfo.Z), N = v.Normal });
            }
            return result;
        }

        /// <summary>Keeps the part of each triangle where a * x + b * z + c is not negative.</summary>
        private static List<V> Clip(List<V> triangles, float a, float b, float c)
        {
            var result = new List<V>(triangles.Count);
            var poly = new List<V>(4); var next = new List<V>(4);
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                poly.Clear(); next.Clear();
                poly.Add(triangles[i]); poly.Add(triangles[i + 1]); poly.Add(triangles[i + 2]);
                for (int j = 0; j < poly.Count; j++)
                {
                    var from = poly[j]; var to = poly[(j + 1) % poly.Count];
                    float df = a * from.P.X + b * from.P.Z + c, dt = a * to.P.X + b * to.P.Z + c;
                    if (df >= 0) next.Add(from);
                    if ((df >= 0) != (dt >= 0)) next.Add(V.Lerp(from, to, df / (df - dt)));
                }
                for (int j = 1; j + 1 < next.Count; j++) //(a cut triangle is a triangle or a quad: as a fan, same way round)
                {
                    result.Add(next[0]); result.Add(next[j]); result.Add(next[j + 1]);
                }
            }
            return result;
        }

        private Modelled3DFloorTile BuildHalf(int cx, int cy, bool rimX, bool rimY)
        {
            var all = new List<V>();
            //the pool's half is the side of the diagonal its corner is on
            all.AddRange(Clip(Triangles(PoolTiles[15]), cx, cy, 0));
            if (rimX) all.AddRange(Clip(Triangles(Rim(cx, 0)), cx, cy, 0));
            if (rimY) all.AddRange(Clip(Triangles(Rim(0, cy)), cx, cy, 0));

            //the diagonal's rim. the bottom rim runs along z = 0.5 with the pool towards -z: bring it to the middle,
            //make it as long as the diagonal, and turn it so that "towards the pool" is towards the corner
            const float root2 = 1.41421356f;
            float sin = cx / root2, cos = -cy / root2;
            var rim = Triangles(PoolTiles[11]);
            for (int i = 0; i < rim.Count; i++)
            {
                var v = rim[i];
                float x = v.P.X * root2, z = v.P.Z - 0.5f;
                v.P = new Vector3(x * cos - z * sin, v.P.Y, x * sin + z * cos);
                v.N = new Vector3(v.N.X * cos - v.N.Z * sin, v.N.Y, v.N.X * sin + v.N.Z * cos);
                rim[i] = v;
            }
            rim = Clip(Clip(Clip(Clip(rim, 1, 0, 0.5f), -1, 0, 0.5f), 0, 1, 0.5f), 0, -1, 0.5f);
            all.AddRange(rim);

            var tile = (Modelled3DFloorTile)RuntimeHelpers.GetUninitializedObject(typeof(Modelled3DFloorTile)); //(its constructor reads a model file)
            tile.TextureName = PoolTiles[15].TextureName;
            tile.Vertices = new TerrainParallaxVertex[all.Count];
            tile.Indices = new int[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                var normal = all[i].N;
                if (normal.LengthSquared() < 0.0001f) normal = Vector3.Up; else normal.Normalize();
                tile.Vertices[i] = new TerrainParallaxVertex(all[i].P + Middle, Vector4.One, all[i].T, 0, normal);
                tile.Indices[i] = i;
            }
            return tile;
        }
    }
}
