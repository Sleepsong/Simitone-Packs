/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Components;
using FSO.LotView.Utils;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoofTool
{
    /// <summary>
    /// Meshing for drawn roofs. On the roof-tool branch this is the roof component's own meshing, rewritten so that the
    /// automatic roof and drawn roofs share it. A pack can't rewrite a method in place, so this is that rewritten
    /// meshing as the pack's own, used in place of the engine's while a lot has drawn roofs. The engine's is left as
    /// it is for the automatic roof.
    /// </summary>
    public static class RoofMesh
    {
        /// <summary>The upright sides of one floor's roofs: gable ends, and a shed roof's high side.</summary>
        public struct Gables
        {
            public TerrainParallaxVertex[] Vertices;
            public int[] Indices;
        }

        //two small private helpers of the roof component, as it has them
        private static Vector3 ToWorldPos(RoofComponent roof, int x, int y, int z, int level, float pitch)
        {
            return new Vector3((x / 16f) * 3f, (z * pitch / 16f) * 3f + ((level - 1) * 2.95f * 3f) + roof.blueprint.GetAltitude(x / 16, y / 16) * 3, (y / 16f) * 3f);
        }

        private static Vector2 PosToTexCRim(Vector3 pos, float ypos)
        {
            return new Vector2((pos.X + pos.Z) * 0.66f, ypos);
        }

        /// <summary>
        /// A roof to mesh: four corners in 1/16 tile units (top left, top right, bottom right, bottom left), the points
        /// each corner's sides slope up to (x, y, and height in the same units, scaled by the pitch), and which sides slope.
        /// A side that doesn't slope is upright: a gable end, or a shed roof's high side.
        /// </summary>
        public struct RoofShape
        {
            public Point[] Corners;
            public Point3[] Ridge;
            public bool[] Slopes;
            public float Pitch;
        }

        public struct Point3
        {
            public int X, Y, Z;
            public Point3(int x, int y, int z) { X = x; Y = y; Z = z; }
        }

        /// <summary>Half a tile of eaves on a drawn roof's sloped sides (the automatic roof's rectangles include theirs).</summary>
        public const int Eave = 8;

        /// <summary>The automatic roof's shape for one of its rectangles: a hip roof.</summary>
        public static RoofShape HipShape(int x1, int y1, int x2, int y2, float pitch)
        {
            //determine roof height of the smallest edge. This height will be used on all edges
            var h = Math.Min(x2 - x1, y2 - y1) / 2;
            return new RoofShape
            {
                Corners = new[] { new Point(x1, y1), new Point(x2, y1), new Point(x2, y2), new Point(x1, y2) },
                Ridge = new[] { new Point3(x1 + h, y1 + h, h), new Point3(x2 - h, y1 + h, h), new Point3(x2 - h, y2 - h, h), new Point3(x1 + h, y2 - h, h) },
                Slopes = new[] { true, true, true, true },
                Pitch = pitch
            };
        }

        /// <summary>The shape of a roof drawn with the roof tool.</summary>
        public static RoofShape PieceShape(RoofPiece piece)
        {
            var slopes = new bool[4];
            var dir = piece.Direction % 4;
            switch (piece.Type)
            {
                case RoofPieceType.Gable:
                    if (dir % 2 == 0) slopes[0] = slopes[2] = true;
                    else slopes[1] = slopes[3] = true;
                    break;
                case RoofPieceType.Shed:
                    slopes[dir] = true;
                    break;
                default:
                    slopes[0] = slopes[1] = slopes[2] = slopes[3] = true;
                    break;
            }
            int x1 = piece.X * 16, y1 = piece.Y * 16, x2 = (piece.X + piece.Width) * 16, y2 = (piece.Y + piece.Height) * 16;
            //eaves overhang the walls on sloped sides; gable ends and a shed's high side stand on the walls
            if (slopes[0]) y1 -= Eave;
            if (slopes[1]) x2 += Eave;
            if (slopes[2]) y2 += Eave;
            if (slopes[3]) x1 -= Eave;
            var pitch = Math.Max(1, (int)piece.Pitch) / 100f;

            var corners = new[] { new Point(x1, y1), new Point(x2, y1), new Point(x2, y2), new Point(x1, y2) };
            Point3[] ridge;
            switch (piece.Type)
            {
                case RoofPieceType.Gable:
                    if (dir % 2 == 0)
                    {
                        var h = (y2 - y1) / 2; //ridge along x
                        ridge = new[] { new Point3(x1, y1 + h, h), new Point3(x2, y1 + h, h), new Point3(x2, y2 - h, h), new Point3(x1, y2 - h, h) };
                    }
                    else
                    {
                        var h = (x2 - x1) / 2; //ridge along y
                        ridge = new[] { new Point3(x1 + h, y1, h), new Point3(x2 - h, y1, h), new Point3(x2 - h, y2, h), new Point3(x1 + h, y2, h) };
                    }
                    break;
                case RoofPieceType.Shed:
                    {
                        //one slope, from the low side (dir) up to the opposite side, which is the full depth high
                        var high = (dir % 2 == 0) ? (y2 - y1) : (x2 - x1);
                        ridge = new Point3[4];
                        Point3 Raise(Point p) => new Point3(p.X, p.Y, high);
                        ridge[(dir + 2) % 4] = Raise(corners[(dir + 2) % 4]);
                        ridge[(dir + 3) % 4] = Raise(corners[(dir + 3) % 4]);
                        ridge[(dir + 1) % 4] = Raise(corners[(dir + 2) % 4]);
                        ridge[dir] = Raise(corners[(dir + 3) % 4]);
                        break;
                    }
                default:
                    return HipShape(x1, y1, x2, y2, pitch);
            }
            return new RoofShape { Corners = corners, Ridge = ridge, Slopes = slopes, Pitch = pitch };
        }

        /// <summary>Gable ends and a shed roof's high side: plaster coloured.</summary>
        public static Vector4 GableColor = new Vector4(0.93f, 0.89f, 0.80f, 1f);

        /// <summary>
        /// Meshes one floor's roofs for a roof component: the sloped sides and their trim, as the engine's RoofData, and
        /// the upright sides, which the engine's RoofData has no place for, separately.
        /// </summary>
        public static RoofData Mesh(RoofComponent roof, List<RoofShape> shapes, int level, out Gables gables)
        {

            var slopeCount = shapes.Sum(s => s.Slopes.Count(x => x));
            var gableCount = shapes.Count * 4 - slopeCount;

            var numQuads = slopeCount; //each sloped side
            TerrainParallaxVertex[] Vertices = new TerrainParallaxVertex[numQuads * 4];
            int[] Indices = new int[numQuads * 6];

            //advanced roof: add down facing edges at each sloped side, edge guards, and the bottom
            var advQuads = slopeCount * (1 + 3) + shapes.Count;
            TerrainParallaxVertex[] AdvVertices = new TerrainParallaxVertex[advQuads * 4];
            int[] AdvIndices = new int[advQuads * 6];

            TerrainParallaxVertex[] GableVertices = new TerrainParallaxVertex[gableCount * 4];
            int[] GableIndices = new int[gableCount * 6];

            int geomOffset = 0, indexOffset = 0;
            int advGeomOffset = 0, advIndexOffset = 0;
            int gableGeomOffset = 0, gableIndexOffset = 0;

            var mat3 = Matrix.CreateRotationZ((float)(Math.PI / -2f)); //good
            var mat1 = Matrix.CreateRotationZ((float)(Math.PI / -2f));
            var mat4 = Matrix.CreateRotationX((float)(Math.PI / 2f)) * Matrix.CreateRotationZ((float)(Math.PI / -2f)); //good
            var mat2 = Matrix.CreateRotationX((float)(Math.PI / 2f)) * Matrix.CreateRotationZ((float)(Math.PI / 2f));
            var mats = new[] { mat1, mat2, mat3, mat4 };
            var flips = new[] { true, true, false, false };
            Func<Vector3, Vector2>[] uvs =
            {
                p => new Vector2(p.X, p.Z * -1),
                p => new Vector2(p.Z, p.X),
                p => new Vector2(p.X, p.Z),
                p => new Vector2(p.Z, p.X * -1),
            };

            var edgeT = 0.7f;
            var edgeO = new Vector3(0, 0.005f, 0);
            var advCol = roof.RoofAvgColor.ToVector4();
            Vector2 texScale = new Vector2(2 / 3f, 1f) * roof.TexRescale;
            var col = Color.White.ToVector4();
            var thickness = new Vector3(0, -0.5f, 0);

            void Quad(int[] indices, ref int io, int baseVert)
            {
                indices[io++] = baseVert + 2;
                indices[io++] = baseVert + 1;
                indices[io++] = baseVert;
                indices[io++] = baseVert;
                indices[io++] = baseVert + 3;
                indices[io++] = baseVert + 2;
            }

            void AddEdge(Vector3 l, Vector3 r, Vector3 m_l, Vector3 m_r, Vector3 normal, Matrix mat, bool flip)
            {
                var lToR = r - l;
                lToR.Normalize();
                var lInner = l + lToR * edgeT;
                var rInner = r - lToR * edgeT;

                var mlToL = l - m_l;
                var diagLength = mlToL.Length();
                mlToL.Normalize();
                var mlInner = m_l + (mlToL + lToR) * edgeT;
                var innerDiagLength = (mlInner - lInner).Length();

                var mrToR = r - m_r;
                mrToR.Normalize();
                var mrInner = m_r + (mrToR - lToR) * edgeT;

                for (int j = 0; j < 12; j += 4) Quad(AdvIndices, ref advIndexOffset, advGeomOffset + j);

                var idOff = (diagLength - innerDiagLength) * 0.1f;
                var hmul = 0.5f;
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(mlInner + edgeO, advCol, new Vector2(-idOff * hmul, 0.33f), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(m_l + edgeO, advCol, new Vector2(0, 0), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(l + edgeO, advCol, new Vector2(diagLength * -hmul, 0), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(lInner + edgeO, advCol, new Vector2((idOff + innerDiagLength) * -hmul, 0.33f), 0, normal, mat, flip);

                var topLength = (m_r - m_l).Length();
                var innerTopLength = (mrInner - mlInner).Length();
                var itOff = (topLength - innerTopLength) / 2;

                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(mrInner + edgeO, advCol, new Vector2((itOff + innerTopLength) * hmul, 0.33f), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(m_r + edgeO, advCol, new Vector2(topLength * hmul, 0), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(m_l + edgeO, advCol, new Vector2(0, 0), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(mlInner + edgeO, advCol, new Vector2(itOff * hmul, 0.33f), 0, normal, mat, flip);

                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(mrInner + edgeO, advCol, new Vector2((topLength + idOff) * hmul, 0.33f), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(rInner + edgeO, advCol, new Vector2((topLength + idOff + innerDiagLength) * hmul, 0.33f), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(r + edgeO, advCol, new Vector2((topLength + diagLength) * hmul, 0f), 0, normal, mat, flip);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(m_r + edgeO, advCol, new Vector2(topLength * hmul, 0f), 0, normal, mat, flip);
            }

            foreach (var shape in shapes)
            {
                var pitch = shape.Pitch;
                //    /    \
                //   /      \
                //  /________\  each sloped side goes from its two corners up to their ridge points.
                var c = shape.Corners.Select(p => ToWorldPos(roof, p.X, p.Y, 0, level, pitch)).ToArray();
                var m = shape.Ridge.Select(p => ToWorldPos(roof, p.X, p.Y, p.Z, level, pitch)).ToArray();
                var b = c.Select(p => p + thickness).ToArray();
                var normals = new Vector3[4];

                for (int k = 0; k < 4; k++)
                {
                    var k1 = (k + 1) % 4;
                    var n = -Vector3.Normalize(Vector3.Cross(c[k] - c[k1], c[k1] - m[k1]));
                    if (float.IsNaN(n.X)) n = Vector3.Up;
                    normals[k] = n;
                    if (shape.Slopes[k])
                    {
                        Quad(Indices, ref indexOffset, geomOffset);
                        Vertices[geomOffset++] = new TerrainParallaxVertex(c[k], col, uvs[k](c[k]) * texScale, 0, n, mats[k], flips[k]);
                        Vertices[geomOffset++] = new TerrainParallaxVertex(c[k1], col, uvs[k](c[k1]) * texScale, 0, n, mats[k], flips[k]);
                        Vertices[geomOffset++] = new TerrainParallaxVertex(m[k1], col * 1.25f, uvs[k](m[k1]) * texScale, 0, n, mats[k], flips[k]);
                        Vertices[geomOffset++] = new TerrainParallaxVertex(m[k], col * 1.25f, uvs[k](m[k]) * texScale, 0, n, mats[k], flips[k]);

                        //thickness
                        var n5 = Vector3.Normalize(Vector3.Cross(c[k] - c[k1], c[k1] - b[k1]));
                        Quad(AdvIndices, ref advIndexOffset, advGeomOffset);
                        AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(b[k1], advCol, PosToTexCRim(b[k1], 0.73f), 0, n5, mats[k], flips[k]);
                        AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(c[k1], advCol, PosToTexCRim(c[k1], 0.4f), 0, n5, mats[k], flips[k]);
                        AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(c[k], advCol, PosToTexCRim(c[k], 0.4f), 0, n5, mats[k], flips[k]);
                        AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(b[k], advCol, PosToTexCRim(b[k], 0.73f), 0, n5, mats[k], flips[k]);
                    }
                    else
                    {
                        //an upright side: a gable end (a triangle) or a shed roof's high side
                        Quad(GableIndices, ref gableIndexOffset, gableGeomOffset);
                        GableVertices[gableGeomOffset++] = new TerrainParallaxVertex(c[k], GableColor, Vector2.Zero, 0, n, mats[k], flips[k]);
                        GableVertices[gableGeomOffset++] = new TerrainParallaxVertex(c[k1], GableColor, Vector2.Zero, 0, n, mats[k], flips[k]);
                        GableVertices[gableGeomOffset++] = new TerrainParallaxVertex(m[k1], GableColor, Vector2.Zero, 0, n, mats[k], flips[k]);
                        GableVertices[gableGeomOffset++] = new TerrainParallaxVertex(m[k], GableColor, Vector2.Zero, 0, n, mats[k], flips[k]);
                    }
                }

                //bottom (solid black)
                var nb = Vector3.Normalize(Vector3.Cross(b[0] - b[2], b[2] - b[3]));
                Quad(AdvIndices, ref advIndexOffset, advGeomOffset);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(b[3], advCol, new Vector2(0, 0.4f + 0.33f), 0, nb, mat4, false);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(b[2], advCol, new Vector2(0, 0.4f + 0.33f), 0, nb, mat4, false);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(b[1], advCol, new Vector2(0, 0.4f + 0.33f), 0, nb, mat4, false);
                AdvVertices[advGeomOffset++] = new TerrainParallaxVertex(b[0], advCol, new Vector2(0, 0.4f + 0.33f), 0, nb, mat4, false);

                //outer edge: 3 quads for top, left and right edges of each sloped side
                for (int k = 0; k < 4; k++)
                {
                    if (!shape.Slopes[k]) continue;
                    var k1 = (k + 1) % 4;
                    AddEdge(c[k], c[k1], m[k], m[k1], normals[k], mats[k], flips[k]);
                }
            }

            gables = new Gables { Vertices = GableVertices, Indices = GableIndices };
            return new RoofData()
            {
                Vertices = Vertices,
                Indices = Indices,
                NumPrimitives = numQuads * 2,

                AdvVertices = AdvVertices,
                AdvIndices = AdvIndices,
                AdvNumPrimitives = advQuads * 2,
            };
        }
    }
}
