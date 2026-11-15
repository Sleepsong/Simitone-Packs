/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// The apartments drawn on the lot itself: each apartment's floor is washed over in its own colour, with a firm
    /// line round its edge, and shared rooms are left as they are. While rooms are being chosen, the room under the
    /// cursor is washed over in white.
    ///
    /// The engine draws a list of line sets over the lot every frame, after the objects, in either camera (its own
    /// use is drawing the routes Sims plan, when debugging). The overlay puts one there as a place to draw from, and
    /// a patch on its drawing draws the coloured floor instead. If the patch is missing, the set has no lines and
    /// draws nothing.
    /// </summary>
    public static class LotOverlay
    {
        public static readonly Color[] Palette =
        {
            new Color(86, 156, 214), new Color(229, 140, 60), new Color(120, 190, 90), new Color(200, 100, 190),
            new Color(235, 205, 80), new Color(90, 200, 200), new Color(225, 95, 95), new Color(160, 130, 230)
        };

        /// <summary>The colour an apartment is drawn in: by its place among the lot's apartments.</summary>
        public static Color ColourOf(LotFile file, Apartment apartment)
        {
            var index = file.Apartments.OrderBy(x => x.Id).ToList().IndexOf(apartment);
            return Palette[Math.Max(0, index) % Palette.Length];
        }

        private const float Lift = 0.1f;        //how far above the floor it is drawn, in tiles (as high as the engine draws its routes)
        private const float Edge = 0.09f;       //how wide the line round an apartment is, in tiles
        private const float FloorHeight = 2.95f; //a storey, as the engine draws its lines
        private const float Wash = 0.42f, Faint = 0.16f, Line = 0.95f, HoverWash = 0.35f;

        private class Mark { }
        private static readonly Mark Mine = new Mark();

        private static VM Vm;
        private static DebugLinesComponent Slot;
        private static Apartment PickedOut;
        private static VertexPositionColor[][] ByLevel = new VertexPositionColor[0][];
        private static VertexPositionColor[] HoverShape = new VertexPositionColor[0];
        private static sbyte HoverLevel;
        private static ushort HoverRoom;
        private static int Age;
        private static bool Broken;
        private static BasicEffect Effect;

        /// <summary>Whether the apartments are being drawn on a lot.</summary>
        public static bool On => Vm != null;

        /// <summary>Kept, but not drawn for now (while the floor tool is out, so the floor being laid can be seen).</summary>
        public static bool Paused;

        /// <summary>One of the shapes the overlay is made of, in tiles: a wash over a run of tiles, or a line along an edge.</summary>
        public struct Shape
        {
            public float X0, Y0, X1, Y1;
            public int Index;   //which apartment (its place among them)
            public bool IsEdge;
        }

        /// <summary>
        /// The shapes for one floor, given which apartment each tile is in (its place among them, or -1 for none).
        /// Each row is washed over in runs of the same apartment, and a line is drawn inside each tile edge that is
        /// the apartment's edge. Kept apart from the drawing so that it can be checked without a screen.
        /// </summary>
        public static List<Shape> Layout(int width, int height, Func<int, int, int> indexAt)
        {
            var shapes = new List<Shape>();
            var grid = new int[width, height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) grid[x, y] = indexAt(x, y);
            Func<int, int, int> at = (x, y) => (x < 0 || y < 0 || x >= width || y >= height) ? -1 : grid[x, y];

            for (int y = 0; y < height; y++)
            {
                var start = 0;
                for (int x = 1; x <= width; x++)
                {
                    if (x < width && grid[x, y] == grid[start, y]) continue;
                    if (grid[start, y] >= 0) shapes.Add(new Shape { X0 = start, Y0 = y, X1 = x, Y1 = y + 1, Index = grid[start, y] });
                    start = x;
                }
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = grid[x, y];
                    if (index < 0) continue;
                    if (at(x - 1, y) != index) shapes.Add(new Shape { X0 = x, Y0 = y, X1 = x + Edge, Y1 = y + 1, Index = index, IsEdge = true });
                    if (at(x + 1, y) != index) shapes.Add(new Shape { X0 = x + 1 - Edge, Y0 = y, X1 = x + 1, Y1 = y + 1, Index = index, IsEdge = true });
                    if (at(x, y - 1) != index) shapes.Add(new Shape { X0 = x, Y0 = y, X1 = x + 1, Y1 = y + Edge, Index = index, IsEdge = true });
                    if (at(x, y + 1) != index) shapes.Add(new Shape { X0 = x, Y0 = y + 1 - Edge, X1 = x + 1, Y1 = y + 1, Index = index, IsEdge = true });
                }
            }
            return shapes;
        }

        /// <summary>Draw the apartments on a lot; with one picked out, it is drawn strongly and the others faintly.</summary>
        public static void Show(VM vm, Apartment pickedOut = null)
        {
            if (vm?.Context?.Blueprint == null) return;
            if (Vm != vm) Hide();
            var bp = vm.Context.Blueprint;
            Vm = vm;
            PickedOut = pickedOut;
            if (Slot == null || Slot.BP != bp || !bp.DebugLines.Contains(Slot))
            {
                Slot = new DebugLinesComponent(bp) { Owner = Mine, Level = 1 };
                bp.DebugLines.Add(Slot);
            }
            Broken = false;
            Rebuild();
            var count = ByLevel.Sum(x => x.Length) / 6;
            if (count != LastCount)
            {
                LastCount = count;
                AptLog.Write("[overlay] Drawing the apartments on the lot: " + count + " shapes" + (ByLevel.Length > 1 ? " (" + string.Join(", ", ByLevel.Select((x, i) => "floor " + (i + 1) + ": " + x.Length / 6)) + ")" : "") + ".");
            }
            FirstDraw = true;
        }

        private static int LastCount = -1;
        private static bool FirstDraw;

        /// <summary>Stop drawing the apartments.</summary>
        public static void Hide()
        {
            var slot = Slot;
            Slot = null;
            Vm = null;
            PickedOut = null;
            ByLevel = new VertexPositionColor[0][];
            HoverShape = new VertexPositionColor[0];
            HoverRoom = 0;
            try { slot?.BP?.DebugLines.Remove(slot); } catch { }
        }

        /// <summary>Whether a set of lines on the lot is the overlay's place to draw from.</summary>
        public static bool IsMine(DebugLinesComponent lines) => lines != null && lines.Owner == Mine;

        /// <summary>The cursor is over a tile, while rooms are being chosen: wash over the room it is in. Null: over none.</summary>
        public static void HoverAt(LotTilePos? pos)
        {
            if (Vm == null) return;
            var room = (pos == null) ? (ushort)0 : Vm.Context.GetRoomAt(pos.Value);
            var level = (pos == null) ? (sbyte)0 : pos.Value.Level;
            var rooms = Vm.Context.RoomInfo;
            if (rooms == null || room >= rooms.Length || rooms[room].Room.IsOutside) room = 0; //outdoors is nobody's
            if (room == HoverRoom && level == HoverLevel) return;
            HoverRoom = room;
            HoverLevel = level;
            if (room == 0)
            {
                HoverShape = new VertexPositionColor[0];
                return;
            }
            var arch = Vm.Context.Architecture;
            var shapes = Layout(arch.Width, arch.Height, (x, y) => Vm.Context.GetRoomAt(LotTilePos.FromBigTile((short)x, (short)y, level)) == room ? 0 : -1);
            var verts = new List<VertexPositionColor>();
            foreach (var shape in shapes) AddQuad(verts, level, shape, Color.White * (shape.IsEdge ? Line : HoverWash));
            HoverShape = verts.ToArray();
        }

        /// <summary>Work out the overlay again from the lot as it is now (rooms change as walls go up and come down).</summary>
        public static void Rebuild()
        {
            Age = 0;
            if (Vm == null) return;
            var state = Neighbours.StateOf(Vm);
            var arch = Vm.Context.Architecture;
            if (state?.File == null || arch == null || Vm.Context.RoomInfo == null)
            {
                ByLevel = new VertexPositionColor[0][];
                return;
            }
            var order = state.File.Apartments.OrderBy(x => x.Id).ToList();
            var roomApts = Units.RoomApartments(Vm, state.File);
            var levels = new VertexPositionColor[Math.Max(1, (int)arch.Stories)][];
            for (int i = 0; i < levels.Length; i++)
            {
                var level = (sbyte)(i + 1);
                var shapes = Layout(arch.Width, arch.Height, (x, y) =>
                    roomApts.TryGetValue(Vm.Context.GetRoomAt(LotTilePos.FromBigTile((short)x, (short)y, level)), out var apartment) ? order.IndexOf(apartment) : -1);
                var verts = new List<VertexPositionColor>(shapes.Count * 6);
                foreach (var shape in shapes)
                {
                    var apartment = (shape.Index < order.Count) ? order[shape.Index] : null;
                    var strong = PickedOut == null || apartment == PickedOut;
                    var colour = Palette[shape.Index % Palette.Length];
                    var alpha = shape.IsEdge ? (strong ? Line : Faint * 2) : (strong ? Wash : Faint);
                    AddQuad(verts, level, shape, colour * alpha); //(premultiplied, for the blend it is drawn with)
                }
                levels[i] = verts.ToArray();
            }
            ByLevel = levels;
        }

        private static void AddQuad(List<VertexPositionColor> verts, sbyte level, Shape shape, Color colour)
        {
            var bp = Vm.Context.Blueprint;
            Func<float, float, VertexPositionColor> corner = (x, y) =>
                new VertexPositionColor(new Vector3(x, bp.InterpAltitude(new Vector3(x, y, 0)) + (level - 1) * FloorHeight + Lift, y), colour);
            var a = corner(shape.X0, shape.Y0);
            var b = corner(shape.X1, shape.Y0);
            var c = corner(shape.X1, shape.Y1);
            var d = corner(shape.X0, shape.Y1);
            verts.Add(a); verts.Add(b); verts.Add(c);
            verts.Add(a); verts.Add(c); verts.Add(d);
        }

        /// <summary>Draw, in place of the empty set of lines. Floors above the one being looked at are not drawn.</summary>
        public static void Draw(GraphicsDevice gd, WorldState state)
        {
            if (Vm == null || Broken || Paused) return;
            try
            {
                if (++Age > 60) Rebuild(); //about once a second
                if (Effect == null || Effect.GraphicsDevice != gd) Effect = new BasicEffect(gd);
                var effect = Effect;
                effect.LightingEnabled = false;
                effect.TextureEnabled = false;
                effect.VertexColorEnabled = true;
                effect.Alpha = 1;
                effect.DiffuseColor = Vector3.One;
                effect.View = state.View;
                effect.Projection = state.Projection;
                effect.World = Matrix.CreateScale(3f);

                gd.DepthStencilState = DepthStencilState.DepthRead; //behind walls and objects, but not hiding anything
                gd.RasterizerState = RasterizerState.CullNone;
                gd.BlendState = BlendState.AlphaBlend;
                var top = Math.Min(ByLevel.Length, (int)state.Level);
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    for (int i = 0; i < top; i++)
                    {
                        var verts = ByLevel[i];
                        if (verts.Length >= 3) gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
                    }
                    if (HoverShape.Length >= 3 && HoverLevel <= state.Level) gd.DrawUserPrimitives(PrimitiveType.TriangleList, HoverShape, 0, HoverShape.Length / 3);
                }
                gd.DepthStencilState = DepthStencilState.Default;
                if (FirstDraw)
                {
                    FirstDraw = false;
                    AptLog.Write("[overlay] Drawn on the lot (" + state.CameraMode + " camera, floor " + state.Level + ").");
                }
            }
            catch (Exception e)
            {
                Broken = true; //once is enough: it would fail again every frame
                AptLog.Write("The apartments could not be drawn on the lot: " + e);
            }
        }
    }
}
