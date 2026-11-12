/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.UI.Panels.LotControls;
using FSO.LotView.Effects;
using FSO.SimAntics.Entities;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FoundationsPack
{
    /// <summary>
    /// Stage 2: what a foundation looks like. How each part of the engine change was done from outside:
    ///
    /// 10. The models (sides, bare tops, decks, plain steps). The engine change gave each floor level a list of
    ///     foundation models and drew them after the floors. The engine already draws any modelled floor group it
    ///     finds among a level's groups (that is how pools are drawn), so the models are put there, under numbers no
    ///     floor uses. Ground tiles the models stand in for are taken back out of their groups just before the engine
    ///     builds them.
    /// 11. The ground under column decks. The engine change kept a second, unraised copy of the ground's vertices and
    ///     drew those tiles from it. Here the copy is taken when the ground is raised (7), and drawn just before the
    ///     engine draws the floors. Grass blades are not drawn there (that was a change in the middle of the ground's
    ///     own drawing).
    /// 12. Steps made of a staircase's bottom piece: the engine change's own class, kept beside the blueprint and
    ///     brought up to date whenever the foundations or the rooms change.
    /// 13. Painting a foundation's sides: the wall painter's update is taken over while the cursor is on a side.
    /// 14. Things hung on a wall on a foundation's edge go up with the wall: which walls each object uses is noted
    ///     when the engine works it out, and added to the object's height when it is positioned.
    /// </summary>
    public partial class FoundationsPack
    {
        private static AccessTools.FieldRef<FloorLevel, Blueprint> LevelBlueprint;

        /// <summary>The first of the group numbers the models go under (floor numbers this high are not floors).</summary>
        private const ushort FirstModelGroup = 64000;

        private static void ApplyDrawing(Harmony harmony)
        {
            LevelBlueprint = FoundationsCode.Field<FloorLevel, Blueprint>("Bp");
            Painter.Find();
            // 10, 11
            harmony.Patch(Find(typeof(FloorLevel), "RegenAll"), prefix: Mine(nameof(HideCoveredTiles)), postfix: Mine(nameof(AddModels)));
            harmony.Patch(Find(typeof(_3DFloorGeometry), "DrawFloor"), prefix: Mine(nameof(BeforeFloors)));
            // 12
            harmony.Patch(Find(typeof(VMArchitecture), "RegenRoomMap"), postfix: Mine(nameof(AfterRooms)));
            // 13
            harmony.Patch(Find(typeof(UIWallPainter), "Update"), prefix: Mine(nameof(PaintSides)));
            // 14
            harmony.Patch(Find(typeof(VMEntity), "SetWallUse"), postfix: Mine(nameof(NoteWallUse)));
            harmony.Patch(AccessTools.PropertySetter(typeof(EntityComponent), "Position"), finalizer: Mine(nameof(Positioned)));
        }

        // ---- 10. the models ----

        private static bool IsGroundLevel(FloorLevel lvl, out Blueprint bp, out FoundationMap map)
        {
            bp = LevelBlueprint(lvl);
            map = Lots.Of(bp);
            return bp?.FloorGeom?.Floors != null && bp.FloorGeom.Floors.Length > 0 && bp.FloorGeom.Floors[0] == lvl;
        }

        /// <summary>
        /// Whether a ground floor tile is drawn by the foundation models instead: steps, and a foundation's top where
        /// nothing is laid on it (no grass on a foundation).
        /// </summary>
        public static bool HiddenByFoundation(FoundationMap map, int index, ushort pattern)
        {
            if (map == null || !map.Any) return false;
            //plain steps are drawn over the whole tile; a staircase's piece stands on the ground like any object
            if (map.Steps[index] != 0 && map.StepTop(index % map.Width, index / map.Width) > 0)
                return FoundationLook.StairFor(map.StepStyle[index]) == null;
            return pattern == 0 && map.Raise[index] > 0;
        }

        /// <summary>Whether a ground floor tile is under a column deck: open, with the lot's ground to be seen under it.</summary>
        public static bool UnderOpenDeck(FoundationMap map, int index)
        {
            return map != null && map.Any && map.Raise[index] > 0 && map.Kind[index] == (byte)FoundationKind.ColumnDeck;
        }

        /// <summary>Before the engine builds a level's floor groups: the tiles the models stand in for come out of them.</summary>
        public static void HideCoveredTiles(FloorLevel __instance)
        {
            if (!IsGroundLevel(__instance, out var bp, out var map)) return;
            var groups = __instance.GroupForTileType;
            foreach (var old in groups.Where(x => x.Value is Modelled3DFoundation).ToList())
            {
                old.Value.Dispose();
                groups.Remove(old.Key);
            }
            if (map == null || !map.Any) return;
            var floors = bp.Floors[0];
            var count = Math.Min(floors.Length, map.Raise.Length);
            for (int j = 0; j < count; j++)
            {
                if (map.Raise[j] == 0 && map.Steps[j] == 0) continue;
                if (!HiddenByFoundation(map, j, floors[j].Pattern)) continue;
                foreach (var group in groups.ToList())
                {
                    if (group.Value is Modelled3DFloor) continue; //(pools: never on a foundation)
                    if (!group.Value.GeomForOffset.ContainsKey((ushort)j)) continue;
                    if (!group.Value.RemoveIndex((ushort)j))
                    {
                        //nothing left in it: the engine would make an empty buffer for it
                        group.Value.Dispose();
                        groups.Remove(group.Key);
                    }
                }
            }
        }

        /// <summary>After the engine has built a level's floor groups: the foundations' models join them.</summary>
        public static void AddModels(FloorLevel __instance, GraphicsDevice gd)
        {
            if (!IsGroundLevel(__instance, out var bp, out var map)) return;
            Report.FloorBuilds++;
            var shown = Lots.Shown(bp);
            shown.UnderDecks?.Dispose();
            shown.UnderDecks = null;
            shown.UnderDeckTiles = 0;
            if (map == null || !map.Any) return;

            var groups = __instance.GroupForTileType;
            ushort key = FirstModelGroup;
            foreach (var (material, pattern) in Modelled3DFoundation.Groups(map))
            {
                var model = new Modelled3DFoundation(bp, material, pattern);
                model.PrepareGPU(gd);
                if (model.PrimitiveCount == 0 || model.GPUData == null)
                {
                    model.Dispose(); //(a plywood top under a laid floor, say)
                    continue;
                }
                while (groups.ContainsKey(key)) key++;
                groups[key++] = model;
            }

            // 11: the lot's own ground under column decks, from the unraised copy of the ground
            var under = new List<int>();
            for (int j = 0; j < map.Raise.Length; j++)
            {
                if (!UnderOpenDeck(map, j)) continue;
                var o = j * 4;
                under.AddRange(new[] { o, o + 1, o + 2, o + 2, o + 3, o });
            }
            if (under.Count > 0)
            {
                shown.UnderDecks = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, under.Count, BufferUsage.None);
                shown.UnderDecks.SetData(under.ToArray());
                shown.UnderDeckTiles = under.Count / 6;
            }
        }

        /// <summary>
        /// Before the engine draws the floors: the models find their textures (they need the world's sprite cache, which
        /// the engine doesn't hand a model), and the ground under column decks is drawn.
        /// </summary>
        public static void BeforeFloors(_3DFloorGeometry __instance, GraphicsDevice gd, GrassEffect e, List<Texture2D> roommaps, HashSet<sbyte> floors,
            EffectPass pass, Matrix? lightWorld, WorldState state)
        {
            var bp = __instance.Bp;
            if (bp == null || __instance.Floors == null || __instance.Floors.Length == 0) return;
            Report.FloorDraws++;
            if (state != null) Report.LastState = state;
            var map = Lots.Of(bp);
            if (map == null || !map.Any) return;
            if (state != null)
            {
                foreach (var group in __instance.Floors[0].GroupForTileType.Values)
                    if (group is Modelled3DFoundation model) model.ResolveTexture(gd, state);
            }

            // 11
            if (lightWorld != null || floors == null || !floors.Contains(0)) return;
            var shown = Lots.Shown(bp);
            var ground = shown.Ground;
            if (shown.UnderDecks == null || shown.UnderDeckTiles == 0 || ground == null || ground.IsDisposed || bp.Terrain?.VertexBuffer == null) return;
            if (ground.VertexCount < map.Raise.Length * 4) return;
            //as the engine draws the ground's own tiles, a moment later (it sets all of these again for each level)
            e.World = Matrix.CreateTranslation(0, -bp.BaseAlt * bp.TerrainFactor * 3, 0);
            e.Level = 0.001f;
            if (roommaps != null && roommaps.Count > 0) e.RoomMap = roommaps[0];
            e.UseTexture = false;
            e.IgnoreColor = false;
            e.GrassShininess = 0.02f;
            gd.SetVertexBuffer(ground);
            gd.Indices = shown.UnderDecks;
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, shown.UnderDeckTiles * 2);
            gd.SetVertexBuffer(bp.Terrain.VertexBuffer);
            e.UseTexture = true;
            e.IgnoreColor = true;
        }

        // ---- the `foundations` console command: what this pack sees, written to a file ----

        public static class Report
        {
            public static int FloorBuilds, FloorDraws;
            public static WorldState LastState;
            public static WeakReference<VM> Lot;

            public static bool Write(string[] args)
            {
                var lines = new List<string>();
                try
                {
                    Fill(lines);
                }
                catch (Exception e)
                {
                    lines.Add("The report itself failed: " + e);
                }
                var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "foundations-report.txt");
                System.IO.File.WriteAllLines(path, lines);
                return true;
            }

            private static void Fill(List<string> lines)
            {
                lines.Add("Foundations pack report, " + DateTime.Now);
                lines.Add("floor builds seen: " + FloorBuilds + ", floor draws seen: " + FloorDraws);
                VM vm = null;
                Lot?.TryGetTarget(out vm);
                var arch = vm?.Context?.Architecture;
                var bp = vm?.Context?.Blueprint;
                if (arch == null || bp == null) { lines.Add("no lot is loaded (vm " + (vm != null) + ", architecture " + (arch != null) + ", blueprint " + (bp != null) + ")"); return; }
                var lot = Lots.For(arch.Terrain);
                var real = lot.Foundations;
                var shown = Lots.Of(bp);
                lines.Add("lot " + arch.Width + "x" + arch.Height + ", blueprint " + bp.Width + "x" + bp.Height + ", terrain factor " + bp.TerrainFactor + ", base altitude " + bp.BaseAlt);
                lines.Add("the lot's foundations: " + real.Raise.Count(x => x > 0) + " raised tiles, " + real.Steps.Count(x => x != 0) + " steps tiles, any " + real.Any);
                lines.Add("shown by the world: " + (shown == null ? "none" : shown.Raise.Count(x => x > 0) + " raised tiles" + (shown == real ? " (the lot's)" : shown == lot.VisFoundations ? " (the preview's)" : " (another map)")));
                lines.Add("camera: " + (LastState == null ? "never seen" : LastState.CameraMode + ", level " + LastState.Level + ", zoom " + LastState.Zoom + ", rotation " + LastState.Rotation));
                lines.Add("graphics: mode " + WorldConfig.Current.Mode + ", advanced lighting " + WorldConfig.Current.AdvancedLighting + ", complex " + WorldConfig.Current.Complex);
                var terrain = bp.Terrain;
                lines.Add("ground mesh: " + (terrain?.VertexBuffer == null ? "none" : terrain.VertexBuffer.VertexCount + " vertices (expected " + bp.Width * bp.Height * 4 + ")") + ", unraised copy " + (Lots.Shown(bp).Ground != null) + ", tiles under decks " + Lots.Shown(bp).UnderDeckTiles);
                if (terrain?.VertexBuffer != null && shown != null && terrain.VertexBuffer.VertexCount == bp.Width * bp.Height * 4)
                {
                    var geom = new TerrainParallaxVertex[terrain.VertexBuffer.VertexCount];
                    terrain.VertexBuffer.GetData(geom);
                    for (int i = 0; i < shown.Raise.Length; i++)
                    {
                        if (shown.Raise[i] == 0) continue;
                        int x = i % shown.Width, y = i / shown.Width;
                        lines.Add("first raised tile " + x + "," + y + " raise " + shown.Raise[i] + " kind " + shown.Kind[i] + ": its ground vertex is at height " + geom[i * 4].Position.Y + ", the tile before it at " + geom[Math.Max(0, i - 1) * 4].Position.Y
                            + "; floor pattern " + bp.Floors[0][i].Pattern);
                        break;
                    }
                }
                var floors = bp.FloorGeom?.Floors;
                if (floors == null || floors.Length == 0) { lines.Add("no floor geometry"); return; }
                var groups = floors[0].GroupForTileType;
                lines.Add("ground floor groups: " + groups.Count + " (" + string.Join(", ", groups.Select(g => g.Key + ":" + (g.Value is Modelled3DFoundation ? "foundation" : g.Value is Modelled3DFloor ? "model" : g.Value.GeomForOffset.Count + " tiles"))) + ")");
                foreach (var group in groups)
                {
                    if (!(group.Value is Modelled3DFoundation model)) continue;
                    var tex = model.TextureInUse;
                    var line = "  model " + group.Key + ": " + model.Material + " covering " + model.WallPattern + ", " + model.PrimitiveCount + " triangles, buffers " + (model.GPUData != null && !model.GPUData.IsDisposed) + "/" + (model.VertGPUData != null && !model.VertGPUData.IsDisposed);
                    if (tex == null) line += ", texture not looked for yet";
                    else
                    {
                        line += ", texture " + tex.Width + "x" + tex.Height + " " + tex.Format + (tex.IsDisposed ? " DISPOSED" : "") + (model.TextureNote != null ? " (plain white: " + model.TextureNote + ")" : "");
                        if (!tex.IsDisposed && tex.Format == SurfaceFormat.Color)
                        {
                            var px = new Color[tex.Width * tex.Height];
                            tex.GetData(px);
                            line += ", middle pixel " + px[px.Length / 2 + tex.Width / 2] + ", see-through pixels " + px.Count(c => c.A < 128) + " of " + px.Length;
                        }
                    }
                    lines.Add(line);
                }
                lines.Add("looks chosen: brick wall " + FoundationLook.BrickWall() + ", lattice " + FoundationLook.LatticeCovering() + ", plywood " + FoundationLook.PlywoodFloor() + ", trim " + FoundationLook.TrimFloor()
                    + ", deck " + FoundationLook.DeckFloor() + ", column " + FoundationLook.ColumnFloor());
            }
        }

        // ---- 12. steps that are a staircase's bottom piece ----

        private static ConditionalWeakTable<Blueprint, FoundationStepsObjects> StepsObjects = new ConditionalWeakTable<Blueprint, FoundationStepsObjects>();

        /// <summary>Call after the foundations shown or the rooms change: the steps' staircase pieces follow.</summary>
        public static void SyncSteps(Blueprint bp)
        {
            if (bp == null) return;
            if (!StepsObjects.TryGetValue(bp, out var steps))
            {
                var map = Lots.Of(bp);
                if (map == null || !map.Any) return; //(nothing to make, nothing made before)
                steps = StepsObjects.GetValue(bp, x => new FoundationStepsObjects(x));
            }
            steps.Sync();
        }

        public static void AfterRooms(VMArchitecture __instance)
        {
            if (VM.UseWorld) SyncSteps(FoundationsCode.WorldOf(__instance)); //(the steps' pieces take their tiles' rooms' light)
        }

        // ---- 13. painting a foundation's sides ----

        private static class Painter
        {
            public static AccessTools.FieldRef<UIWallPainter, List<VMArchitectureCommand>> Commands;
            public static AccessTools.FieldRef<UIWallPainter, bool> Drawing;
            public static AccessTools.FieldRef<UIWallPainter, ushort> Pattern;
            public static AccessTools.FieldRef<UIWallPainter, int> CursorDir;
            public static AccessTools.FieldRef<UIWallPainter, VM> Vm;
            public static AccessTools.FieldRef<UIWallPainter, World> World;
            public static AccessTools.FieldRef<UIWallPainter, ILotControl> Parent;
            public static AccessTools.FieldRef<UIWallPainter, VMMultitileGroup> WallCursor;

            public static void Find()
            {
                Commands = FoundationsCode.Field<UIWallPainter, List<VMArchitectureCommand>>("Commands");
                Drawing = FoundationsCode.Field<UIWallPainter, bool>("Drawing");
                Pattern = FoundationsCode.Field<UIWallPainter, ushort>("Pattern");
                CursorDir = FoundationsCode.Field<UIWallPainter, int>("CursorDir");
                Vm = FoundationsCode.Field<UIWallPainter, VM>("vm");
                World = FoundationsCode.Field<UIWallPainter, World>("World");
                Parent = FoundationsCode.Field<UIWallPainter, ILotControl>("Parent");
                WallCursor = FoundationsCode.Field<UIWallPainter, VMMultitileGroup>("WallCursor");
            }
        }

        private static bool IsSidePaint(VMArchitectureCommand cmd) => cmd.Type == VMFoundationTools.PaintCommand;
        private static bool IsSideFill(VMArchitectureCommand cmd) => IsSidePaint(cmd) && cmd.x2 == VMFoundationTools.PaintWholeFoundation;

        /// <summary>
        /// The foundation side a camera ray meets first (world units, as the world's camera ray for the ground floor): its
        /// tile, and which side. Only sides that show on foundations (not decks), facing the camera; a foundation's top in
        /// front of one hides it.
        /// </summary>
        public static bool FoundationSideAt(Blueprint bp, Ray ray, out Point tile, out int dir)
        {
            tile = Point.Zero;
            dir = -1;
            var map = Lots.Of(bp);
            if (map == null || !map.Any || bp.Altitude == null) return false;
            var p = ray.Position / 3; //tile units, and the same distance along the ray
            var v = ray.Direction / 3;
            Func<int, int, float> ground = (gx, gy) =>
            {
                gx = Math.Max(0, Math.Min(bp.Width - 1, gx));
                gy = Math.Max(0, Math.Min(bp.Height - 1, gy));
                return (bp.Altitude[gy * bp.Width + gx] - bp.BaseAlt) * bp.TerrainFactor;
            };
            float best = float.MaxValue;
            bool found = false;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var raise = map.RaiseOf(x, y);
                    if (raise <= 0) continue;
                    var r = raise * bp.TerrainFactor;

                    // its top (level, at the corners' average)
                    if (v.Y != 0)
                    {
                        var h = (ground(x, y) + ground(x + 1, y) + ground(x, y + 1) + ground(x + 1, y + 1)) / 4 + r;
                        var t = (h - p.Y) / v.Y;
                        var q = p + v * t;
                        if (t > 0 && t < best && q.X >= x && q.X <= x + 1 && q.Z >= y && q.Z <= y + 1)
                        {
                            best = t;
                            found = false;
                        }
                    }

                    for (int d = 0; d < 4; d++)
                    {
                        if (!map.SidePaintable(x, y, d)) continue; //(a foundation's: decks aren't painted)
                        var n = FoundationMap.Directions[d];
                        if (n.X * v.X + n.Y * v.Z >= 0) continue; //facing away
                        Modelled3DFoundation.EdgeEnds(x, y, d, out var a, out var b);
                        float t;
                        if (n.X != 0) t = (a.X - p.X) / v.X;
                        else t = (a.Y - p.Z) / v.Z;
                        if (t <= 0 || t >= best) continue;
                        var q = p + v * t;
                        // how far along the edge, from a to b
                        float along = (n.X != 0) ? (q.Z - a.Y) / (b.Y - a.Y) : (q.X - a.X) / (b.X - a.X);
                        if (along < 0 || along > 1) continue;
                        var g = ground(a.X, a.Y) * (1 - along) + ground(b.X, b.Y) * along;
                        var bottom = g + (raise - map.SideDrop(x, y, d)) * bp.TerrainFactor;
                        if (q.Y < bottom || q.Y > g + r) continue;
                        best = t;
                        found = true;
                        tile = new Point(x, y);
                        dir = d;
                    }
                }
            }
            if (!found) dir = -1;
            return found;
        }

        /// <summary>
        /// The wall painter's update. A foundation's side under the cursor (on the ground floor) is painted like a wall:
        /// one side, or with shift every side of that foundation. Anywhere else the engine's own update runs.
        /// </summary>
        public static bool PaintSides(UIWallPainter __instance, UpdateState state)
        {
            var vm = Painter.Vm(__instance);
            var world = Painter.World(__instance);
            var commands = Painter.Commands(__instance);
            var bp = vm?.Context?.Blueprint;
            if (vm == null || world == null || commands == null || bp == null) return true;
            var mouse = __instance.MousePosition;

            Point tile = Point.Zero;
            int dir = -1;
            bool onSide = false;
            var map = Lots.Of(bp);
            if (world.State.Level == 1 && map != null && map.Any)
            {
                try
                {
                    onSide = FoundationSideAt(bp, world.State.CameraRayAtScreenPos(new Vector2(mouse.X, mouse.Y), 1), out tile, out dir);
                }
                catch (Exception)
                {
                    onSide = false;
                }
            }
            var arch = vm.Context.Architecture;
            if (!onSide)
            {
                //a whole foundation's paint is only for while the cursor is on it
                if (commands.Count > 0 && IsSideFill(commands[0]))
                {
                    commands.Clear();
                    arch.SignalRedraw();
                }
                return true;
            }

            ushort pattern = __instance.Modifiers.IsSet(UILotControlModifiers.CTRL) ? (ushort)0 : Painter.Pattern(__instance);
            if (!Painter.Drawing(__instance) && commands.Count > 0)
            {
                arch.SignalRedraw();
                commands.Clear();
            }
            var whole = __instance.Modifiers.IsSet(UILotControlModifiers.SHIFT);
            var cmd = new VMArchitectureCommand
            {
                Type = VMFoundationTools.PaintCommand,
                level = 1,
                pattern = pattern,
                x = tile.X,
                y = tile.Y,
                x2 = whole ? VMFoundationTools.PaintWholeFoundation : dir
            };
            var fill = commands.Count > 0 && (IsSideFill(commands[0]) || commands[0].Type == VMArchitectureCommandType.PATTERN_FILL);
            if (whole || fill)
            {
                //a fill replaces anything else
                if (commands.Count != 1 || !commands[0].Equals(cmd))
                {
                    commands.Clear();
                    arch.SignalRedraw();
                    commands.Add(cmd);
                }
            }
            else if (!commands.Contains(cmd))
            {
                arch.SignalRedraw();
                commands.Add(cmd);
            }
            //the roller faces the side: its wall direction is one on from the foundation's
            var cursorDir = (dir + 2) % 4;
            Painter.CursorDir(__instance) = cursorDir;

            // the rest is the end of the engine's own update
            var cmds = arch.Commands;
            cmds.Clear();
            foreach (var c in commands) cmds.Add(c);

            if (cmds.Count > 0)
            {
                var parent = Painter.Parent(__instance);
                var cost = arch.LastTestCost;
                if (cost != 0)
                {
                    var disallowed = parent != null && parent.ActiveEntity != null && cost > parent.Budget;
                    state.UIState.TooltipProperties.Show = true;
                    state.UIState.TooltipProperties.Color = disallowed ? Color.DarkRed : Color.Black;
                    state.UIState.TooltipProperties.Opacity = 1;
                    state.UIState.TooltipProperties.Position = new Vector2(mouse.X, mouse.Y);
                    state.UIState.Tooltip = (cost < 0) ? ("-$" + (-cost)) : ("$" + cost);
                    state.UIState.TooltipProperties.UpdateDead = false;

                    if (disallowed) HITVM.Get().PlaySoundEvent(UISounds.Error);
                }
                else
                {
                    state.UIState.TooltipProperties.Show = false;
                    state.UIState.TooltipProperties.Opacity = 0;
                }
            }

            Painter.WallCursor(__instance)?.SetVisualPosition(new Vector3(tile.X + 0.5f, tile.Y + 0.5f, 0), (Direction)(1 << (cursorDir * 2)), vm.Context);
            return false;
        }

        // ---- 14. things hung on a wall on a foundation's edge ----

        /// <summary>The walls of its tile each object needs (paintings, windows, doors...).</summary>
        private static ConditionalWeakTable<ObjectComponent, StrongBox<WallSegments>> WallUse = new ConditionalWeakTable<ObjectComponent, StrongBox<WallSegments>>();

        [ThreadStatic] private static EntityComponent Positioning;

        private static int WallOff(Direction dir)
        {
            switch (dir)
            {
                case Direction.EAST: return 1;
                case Direction.SOUTH: return 2;
                case Direction.WEST: return 3;
            }
            return 0;
        }

        /// <summary>The engine has just worked out which walls an object uses (as it does whenever one is placed or lifted).</summary>
        public static void NoteWallUse(VMEntity __instance, bool set)
        {
            if (!VM.UseWorld || !(__instance.WorldUI is ObjectComponent comp)) return;
            WallSegments use = 0;
            if (set)
            {
                //(as the engine works it out)
                var placeFlags = (int)__instance.GetValue(VMStackObjectVariable.WallPlacementFlags);
                int rotate = (8 - (WallOff(__instance.Direction) + 1)) % 4;
                int sides = placeFlags % 15;
                use = (WallSegments)(((sides << (4 - rotate)) & 15) | ((sides & 15) >> rotate));
            }
            if (use == 0 && !WallUse.TryGetValue(comp, out _)) return;
            var box = WallUse.GetOrCreateValue(comp);
            if (box.Value == use) return;
            box.Value = use;
            var map = Lots.Of(comp.blueprint);
            if (map != null && map.Any) RefreshAltitude(comp);
        }

        /// <summary>
        /// How much higher than its own tile an object being positioned stands: a wall it hangs on may stand on a higher
        /// foundation (a window on the outside of a wall on a foundation's edge).
        /// </summary>
        private static float WallHungRaise(Blueprint bp, FoundationMap map, Vector3 position)
        {
            if (!(Positioning is ObjectComponent comp) || comp.blueprint != bp) return 0;
            if (!WallUse.TryGetValue(comp, out var use) || use.Value == 0) return 0;
            int x = (int)Math.Floor(position.X), y = (int)Math.Floor(position.Y);
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return 0;
            var own = map.RaiseOf(x, y);
            var wall = map.WallRaise(x, y, use.Value);
            return (wall > own) ? (wall - own) * bp.TerrainFactor : 0;
        }

        public static Exception Positioned(Exception __exception)
        {
            Positioning = null;
            return __exception;
        }
    }
}
