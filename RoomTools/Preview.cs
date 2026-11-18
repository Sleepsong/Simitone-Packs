/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoomTools
{
    /// <summary>
    /// Showing a change before it is made: the walls and floors of a room being carried, or of a wall being dragged.
    ///
    /// The engine shows its own tools' changes this way: each time the lot is redrawn it copies the walls and floors,
    /// runs the tool's commands on the copy, and draws that. A command's type is one byte, and the engine passes over
    /// types it does not know, so Room Tools puts one of its own in the list (as Terrain Paint does) and, after the
    /// engine has run the list on the copy, writes the change it is showing into the copy too.
    /// </summary>
    public static class Preview
    {
        public const VMArchitectureCommandType Command = (VMArchitectureCommandType)201;

        private static OverlayGrid Shown;

        /// <summary>Show a change: walls and floors changed on top of the lot's.</summary>
        public static void Show(VMArchitecture arch, OverlayGrid change)
        {
            Shown = change;
            if (!arch.Commands.Any(x => x.Type == Command))
            {
                arch.Commands.Clear();
                arch.Commands.Add(new VMArchitectureCommand { Type = Command });
            }
            arch.SignalRedraw();
        }

        public static void Hide(VMArchitecture arch)
        {
            Shown = null;
            if (arch == null) return;
            arch.Commands.RemoveAll(x => x.Type == Command);
            arch.SignalRedraw();
        }

        /// <summary>The engine has run a tool's commands on its copy of the walls and floors: add what is being shown.</summary>
        public static void AfterCommands(VMArchitecture arch, List<VMArchitectureCommand> commands)
        {
            var change = Shown;
            if (change == null || arch.RealMode) return;
            if (!commands.Any(x => x.Type == Command)) return;
            change.WriteTo(new EngineGrid(arch));
        }
    }

    /// <summary>
    /// See-through copies of a room's objects, drawn where it would go while it is carried, as buy mode draws an object
    /// being placed. They are not part of the lot, and nothing can use them.
    /// </summary>
    public class Ghosts
    {
        /// <summary>A room with more objects than this is carried without them: a lot of objects slows drawing.</summary>
        public const int Most = 120;

        private readonly List<(int index, VMMultitileGroup group)> Made = new List<(int, VMMultitileGroup)>();

        public static Ghosts Make(VM vm, Blueprint b)
        {
            var ghosts = new Ghosts();
            if (b.Objects.Count > Most) return ghosts;
            var made = new VMMultitileGroup[b.Objects.Count];
            for (int i = 0; i < b.Objects.Count; i++)
            {
                var o = b.Objects[i];
                if (o.State == null || o.State.Length == 0) continue;
                var g = Things.Make(vm, o, true, out _);
                if (g == null) continue;
                made[i] = g;
                if (o.Container >= 0 && made[o.Container] != null)
                {
                    var holder = made[o.Container];
                    var part = (o.ContainerPart < holder.Objects.Count) ? holder.Objects[o.ContainerPart] : holder.BaseObject;
                    part.PlaceInSlot(g.BaseObject, o.Slot, false, vm.Context);
                    ghosts.Made.Add((i, g));
                    continue;
                }
                ghosts.Made.Add((i, g));
            }
            return ghosts;
        }

        /// <summary>Draw them where a blueprint (the one they were made from, turned) would put its objects.</summary>
        public void MoveTo(VM vm, Blueprint turned, int ox, int oy, int level, bool visible)
        {
            foreach (var (index, g) in Made)
            {
                var o = turned.Objects[index];
                if (o.Container >= 0) continue; //drawn in its container's slot
                var pos = visible
                    ? new Vector3((ox * 16 + o.X) / 16f, (oy * 16 + o.Y) / 16f, (level + o.Level - 2) * 2.95f)
                    : new Vector3(-1000, -1000, 0);
                g.SetVisualPosition(pos, o.Dir, vm.Context);
            }
        }

        public void Remove(VM vm)
        {
            foreach (var (_, g) in Made.AsEnumerable().Reverse())
            {
                try { g.Delete(vm.Context); }
                catch { }
            }
            Made.Clear();
        }
    }

    /// <summary>
    /// Colour washed over tiles of the lot, and lines along walls: the room under the cursor, where a room would go, the
    /// wall being dragged. Drawn as Apartments draws its apartments: the engine draws a list of line sets over the lot
    /// every frame, in either camera, and one put there by Room Tools is drawn by Room Tools instead.
    /// </summary>
    public static class Highlight
    {
        private class Mark { }
        private static readonly Mark Mine = new Mark();
        private const float Lift = 0.1f, FloorHeight = 2.95f, Line = 0.12f;

        private static VM Vm;
        private static DebugLinesComponent Slot;
        private static VertexPositionColor[] Verts = new VertexPositionColor[0];
        private static int[] Levels = new int[0];
        private static BasicEffect Effect;
        private static bool Broken;

        public static readonly Color Good = new Color(110, 200, 110), Bad = new Color(220, 80, 70), Hover = Color.White, Wall = new Color(250, 210, 80);

        public static bool IsMine(DebugLinesComponent lines) => lines != null && lines.Owner == Mine;

        /// <summary>Wash tiles over in a colour, and draw lines along walls. Replaces what was drawn.</summary>
        public static void Set(VM vm, IEnumerable<(int x, int y, int level)> tiles, Color wash, IEnumerable<Edge> lines, Color line)
        {
            var bp = vm?.Context?.Blueprint;
            if (bp == null) { Clear(); return; }
            if (Slot == null || Slot.BP != bp || !bp.DebugLines.Contains(Slot) || Vm != vm)
            {
                Clear();
                Slot = new DebugLinesComponent(bp) { Owner = Mine, Level = 1 };
                bp.DebugLines.Add(Slot);
                Vm = vm;
            }
            var verts = new List<VertexPositionColor>();
            var levels = new List<int>();
            void Quad(float x0, float y0, float x1, float y1, int level, Color colour)
            {
                VertexPositionColor C(float x, float y) => new VertexPositionColor(new Vector3(x, bp.InterpAltitude(new Vector3(x, y, 0)) + (level - 1) * FloorHeight + Lift, y), colour);
                var a = C(x0, y0); var b = C(x1, y0); var c = C(x1, y1); var d = C(x0, y1);
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(a); verts.Add(c); verts.Add(d);
                levels.Add(level); levels.Add(level);
            }
            foreach (var (x, y, level) in tiles) Quad(x, y, x + 1, y + 1, level, wash * 0.4f); //(premultiplied, for the blend it is drawn with)
            foreach (var e in lines)
            {
                if (e.Vertical) Quad(e.X - Line, e.Y, e.X + Line, e.Y + 1, e.Level, line * 0.95f);
                else Quad(e.X, e.Y - Line, e.X + 1, e.Y + Line, e.Level, line * 0.95f);
            }
            Verts = verts.ToArray();
            Levels = levels.ToArray();
        }

        public static void Clear()
        {
            var slot = Slot;
            Slot = null;
            Vm = null;
            Verts = new VertexPositionColor[0];
            Levels = new int[0];
            try { slot?.BP?.DebugLines.Remove(slot); } catch { }
        }

        /// <summary>Draw, in place of the empty set of lines. Floors above the one being looked at are not drawn.</summary>
        public static void Draw(GraphicsDevice gd, WorldState state)
        {
            if (Vm == null || Broken || Verts.Length < 3) return;
            try
            {
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

                var shown = new List<VertexPositionColor>(Verts.Length);
                for (int i = 0; i < Levels.Length; i++)
                    if (Levels[i] <= state.Level) for (int j = 0; j < 3; j++) shown.Add(Verts[i * 3 + j]);
                if (shown.Count < 3) return;
                var array = shown.ToArray();

                gd.DepthStencilState = DepthStencilState.DepthRead;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.BlendState = BlendState.AlphaBlend;
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserPrimitives(PrimitiveType.TriangleList, array, 0, array.Length / 3);
                }
                gd.DepthStencilState = DepthStencilState.Default;
            }
            catch (Exception e)
            {
                Broken = true; //once is enough: it would fail again every frame
                RoomToolsPack.Log("The room tools' highlight cannot be drawn: " + e);
            }
        }
    }
}
