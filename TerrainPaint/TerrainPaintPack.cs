/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls.Catalog;
using FSO.Common;
using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Content.Model;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Effects;
using FSO.LotView.Model;
using FSO.LotView.Utils;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TerrainPaint
{
    /// <summary>
    /// Terrain paint, as in The Sims 2, added to the engine entirely from a pack.
    ///
    /// The same feature exists as an engine change (FreeSO branch simitone/terrain-paint, about 350 lines in 7 files).
    /// Most of that was new code, which lives here unchanged in Paint.cs and UITerrainPainter.cs. This file is the
    /// rest: the places where the engine change edited existing engine code, each redone as a Harmony patch.
    ///
    ///  1. Where the paint is kept. The engine change added fields to the terrain. Here it is kept beside the terrain.
    ///  2. The paint command. The engine change added a command type. Here an unused number is handled from outside.
    ///  3. Saving. The engine change wrote the paint into the lot's save. Here it is this pack's own data for the lot.
    ///  4. The build catalog. The engine change added lines to the terrain tools list. Here they are added after it.
    ///  5. Drawing the ground. The engine change edited how the ground's vertices are made. Here they are fixed up
    ///     after they are made.
    ///  6. Drawing the grass blades. The engine change wrapped the blade drawing in a loop over terrain types. Here
    ///     the painted types' blades are drawn by this pack's own copy of that loop, just before the engine's.
    /// </summary>
    public class TerrainPaintPack : ISimitonePack
    {
        private static string PackId;

        public void OnLoad(Pack pack)
        {
            PackId = pack.Id;
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(TerrainPaintPack), name);

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static AccessTools.FieldRef<TerrainComponent, T> Field<T>(string name)
        {
            if (AccessTools.Field(typeof(TerrainComponent), name) == null) throw new Exception("TerrainComponent." + name + " was not found: this pack was made for a different version of Simitone");
            return AccessTools.FieldRefAccess<TerrainComponent, T>(name);
        }

        //the terrain component's private parts that drawing needs
        private static AccessTools.FieldRef<TerrainComponent, Rectangle> SizeOf;
        private static AccessTools.FieldRef<TerrainComponent, byte[]> GrassStateOf;
        private static AccessTools.FieldRef<TerrainComponent, GrassEffect> EffectOf;
        private static AccessTools.FieldRef<TerrainComponent, Blueprint> BlueprintOf;
        private static AccessTools.FieldRef<TerrainComponent, Color> LightGreenOf, DarkGreenOf, LightBrownOf, DarkBrownOf;
        //and the architecture's: the blueprint it draws to
        private static AccessTools.FieldRef<VMArchitecture, Blueprint> WorldOf;

        private static void Apply(Harmony harmony)
        {
            if (AccessTools.Field(typeof(VMArchitecture), "WorldUI") == null) throw new Exception("VMArchitecture.WorldUI was not found: this pack was made for a different version of Simitone");
            WorldOf = AccessTools.FieldRefAccess<VMArchitecture, Blueprint>("WorldUI");
            SizeOf = Field<Rectangle>("Size");
            GrassStateOf = Field<byte[]>("GrassState");
            EffectOf = Field<GrassEffect>("Effect");
            BlueprintOf = Field<Blueprint>("Bp");
            LightGreenOf = Field<Color>("LightGreen"); DarkGreenOf = Field<Color>("DarkGreen");
            LightBrownOf = Field<Color>("LightBrown"); DarkBrownOf = Field<Color>("DarkBrown");

            // 1
            harmony.Patch(Find(typeof(VMArchitectureTerrain), "EnterVis"), postfix: Mine(nameof(EnterVis)));
            harmony.Patch(Find(typeof(VMArchitectureTerrain), "ExitVis"), postfix: Mine(nameof(ExitVis)));
            // 2
            harmony.Patch(Find(typeof(VMArchitecture), "RunCommands"), postfix: Mine(nameof(RunPaintCommands)));
            // 3 (saving is OnLotSaved and OnLotLoaded, below: no patch. this applies what was loaded once the lot is there)
            harmony.Patch(Find(typeof(VMArchitecture), "Tick"), prefix: Mine(nameof(BeforeArchitectureTick)));
            // 4
            harmony.Patch(Find(typeof(UIBuyBrowsePanel), "AddTerrainTools"), postfix: Mine(nameof(AddPaintTools)));
            // 5
            harmony.Patch(Find(typeof(TerrainComponent), "UpdateTerrain"), prefix: Mine(nameof(TakePaint)));
            harmony.Patch(Find(typeof(TerrainComponent), "RegenTerrain"), postfix: Mine(nameof(PaintTheGround)));
            harmony.Patch(Find(typeof(TerrainComponent), "Dispose"), postfix: Mine(nameof(DisposeBlades)));
            // 6
            harmony.Patch(Find(typeof(TerrainComponent), "Draw"), prefix: Mine(nameof(EnterDraw)), finalizer: Mine(nameof(LeaveDraw)));
            harmony.Patch(Find(typeof(Blueprint).GetField("FloorGeom").FieldType, "SetGrassIndices"), postfix: Mine(nameof(DrawPaintedBlades)));
        }

        // ---- 1. where the paint is kept: see PaintLayer. these keep its preview copy in step with the engine's ----

        public static void EnterVis(VMArchitectureTerrain __instance) { PaintLayer.For(__instance).EnterVis(); }
        public static void ExitVis(VMArchitectureTerrain __instance) { PaintLayer.For(__instance).ExitVis(); }

        // ---- 2. the paint command: the engine has skipped it, as a type it doesn't know. run it and add its cost ----

        public static void RunPaintCommands(VMArchitecture __instance, List<VMArchitectureCommand> commands, ref int __result)
        {
            foreach (var command in commands)
            {
                if (command.Type == PaintTools.PaintCommand) __result += PaintTools.Paint(__instance, command);
            }
        }

        // ---- 3. saving ----

        /// <summary>Paint read from a lot's file, waiting for the lot's ground to be there to put it on.</summary>
        private static ConditionalWeakTable<VM, byte[]> Loaded = new ConditionalWeakTable<VM, byte[]>();

        public void OnLotSaved(VM vm, string lotPath)
        {
            var terrain = vm.Context?.Architecture?.Terrain;
            if (terrain == null) return;
            LotPackData.Write(PackId, lotPath, PaintLayer.For(terrain).Save(terrain.Width, terrain.Height));
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            Loaded.Remove(vm);
            if (lotPath == null) return; //not from a file: the terrain is the one that was handed over, with its paint
            var saved = LotPackData.Read(PackId, lotPath);
            if (saved != null) Loaded.Add(vm, saved);
        }

        [ThreadStatic] private static VMArchitecture Ticking;

        public static void BeforeArchitectureTick(VMArchitecture __instance)
        {
            Ticking = __instance; //(for 5: which lot's ground the terrain component is about to be given)
            var vm = __instance.Context?.VM;
            if (vm == null || __instance.Terrain == null || !Loaded.TryGetValue(vm, out var saved)) return;
            Loaded.Remove(vm);
            var paint = PaintLayer.Load(saved, __instance.Terrain.Width, __instance.Terrain.Height);
            if (paint == null) return;
            PaintLayer.For(__instance.Terrain).Paint = paint;
            __instance.SignalTerrainRedraw();
        }

        // ---- 4. the build catalog ----

        public static void AddPaintTools(UIBuyBrowsePanel __instance)
        {
            var paintRes = new UICatalogTerrainPaintResProvider();
            var paints = PaintTools.PaintTypes.Select(x => (int)x).ToList();
            paints.Add(PaintTools.Erase);
            paints.Reverse();
            foreach (var paint in paints)
            {
                __instance.FullCategory.Insert(0, new UICatalogElement
                {
                    Item = new ObjectCatalogItem()
                    {
                        Name = paintRes.GetName((ulong)paint),
                        Category = 18,
                        Price = 1,
                    },
                    Special = new UISpecialCatalogElement
                    {
                        Control = typeof(UITerrainPainter),
                        ResID = (uint)paint,
                        Res = paintRes,
                        Parameters = new List<int> { paint }
                    }
                });
            }
        }

        // ---- 5. drawing the ground ----

        /// <summary>What this pack keeps for a terrain component: the paint it is showing, and the painted types' grass blades.</summary>
        private class Shown
        {
            public byte[] Paint;
            /// <summary>For each painted type, a copy of the ground's vertices where only that type's points grow blades.</summary>
            public List<(TerrainType type, VertexBuffer vb)> Blades = new List<(TerrainType, VertexBuffer)>();

            public void DisposeBlades()
            {
                foreach (var pass in Blades) pass.vb.Dispose();
                Blades.Clear();
            }
        }
        private static ConditionalWeakTable<TerrainComponent, Shown> ShownBy = new ConditionalWeakTable<TerrainComponent, Shown>();

        /// <summary>
        /// The engine is giving the terrain component a lot's ground to show: its real ground, or a tool's preview of it.
        /// The engine change passed the paint along as one more argument. From here, which lot it is has to be taken from
        /// the architecture that is ticking, and which of the two from which grass it was given.
        /// </summary>
        public static void TakePaint(TerrainComponent __instance, byte[] grass)
        {
            var terrain = Ticking?.Terrain;
            if (terrain == null || WorldOf(Ticking)?.Terrain != __instance) return;
            var layer = PaintLayer.For(terrain);
            ShownBy.GetOrCreateValue(__instance).Paint = ReferenceEquals(grass, terrain.VisGrass) ? layer.VisPaint : layer.Paint;
        }

        /// <summary>The painted terrain type at a point of the ground, or null for the lot's own.</summary>
        private static TerrainType? PaintAt(byte[] paint, int width, int x, int y)
        {
            var offset = (y - 1) * width + x - 1;
            if (offset < 0 || offset >= paint.Length) return null;
            var p = paint[offset];
            if (p == PaintLayer.Unpainted || p >= LotTypeGrassInfo.LightGreen.Length) return null;
            return (TerrainType)p;
        }

        private static float GrassAt(byte[] grass, int width, int x, int y)
        {
            var offset = (y - 1) * width + x - 1;
            if (offset < 0 || offset >= grass.Length) return 1;
            return grass[offset] / 255f;
        }

        /// <summary>
        /// The engine has just made the ground's vertices. The engine change gave painted points their type's colour
        /// as they were made. From here: read the vertices back, recolour the painted ones, and put them back. Painted
        /// points also stop growing the lot's blades (blade value 2), and get a copy of the ground each to grow their own.
        /// </summary>
        public static void PaintTheGround(TerrainComponent __instance, GraphicsDevice device)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown)) return;
            shown.DisposeBlades();
            var paint = shown.Paint;
            var grass = GrassStateOf(__instance);
            var vb = __instance.VertexBuffer;
            if (paint == null || grass == null || vb == null || vb.IsDisposed) return;

            var quads = SizeOf(__instance).Width;
            if (vb.VertexCount != quads * quads * 4) return; //not the ground this was written for
            var geom = new TerrainParallaxVertex[vb.VertexCount];
            vb.GetData(geom);

            //each vertex's point, in the order the engine makes them: four corners for each tile
            var types = new TerrainType?[geom.Length];
            var states = new float[geom.Length];
            bool any = false;
            int v = 0;
            for (var y = 0; y < quads; y++)
            {
                for (var x = 0; x < quads; x++)
                {
                    foreach (var (px, py) in new[] { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) })
                    {
                        states[v] = GrassAt(grass, quads, px, py);
                        var type = types[v] = PaintAt(paint, quads, px, py);
                        if (type != null)
                        {
                            any = true;
                            var t = (int)type.Value;
                            geom[v].Color = Color.Lerp(LotTypeGrassInfo.LightGreen[t], LotTypeGrassInfo.LightBrown[t], states[v]).ToVector4();
                            geom[v].GrassInfo.X = 2f; //none of the lot's blades here
                        }
                        v++;
                    }
                }
            }
            if (!any) return;
            vb.SetData(geom);

            foreach (var type in types.Where(t => t != null).Select(t => t.Value).Distinct().OrderBy(t => (int)t))
            {
                var copy = (TerrainParallaxVertex[])geom.Clone();
                for (int i = 0; i < copy.Length; i++) copy[i].GrassInfo.X = (types[i] == type) ? states[i] : 2f;
                var blades = new VertexBuffer(device, typeof(TerrainParallaxVertex), copy.Length, BufferUsage.None);
                blades.SetData(copy);
                shown.Blades.Add((type, blades));
            }
        }

        public static void DisposeBlades(TerrainComponent __instance)
        {
            if (ShownBy.TryGetValue(__instance, out var shown)) shown.DisposeBlades();
        }

        // ---- 6. drawing the grass blades ----

        [ThreadStatic] private static TerrainComponent Drawing;

        public static void EnterDraw(TerrainComponent __instance) { Drawing = __instance; }
        public static Exception LeaveDraw(Exception __exception) { Drawing = null; return __exception; }

        /// <summary>
        /// The terrain component, in the middle of Draw, has just chosen the grass tiles to draw blades on, and is about
        /// to draw the lot's blades. The engine change turned that drawing into a loop over the lot's and each painted
        /// type's blades. There is no way to wrap part of a method from outside, so this draws the painted types' blades
        /// itself, here, with its own copy of the engine's blade drawing, and puts back what the engine expects to find.
        /// </summary>
        public static void DrawPaintedBlades(GraphicsDevice gd, WorldState state, int __result)
        {
            var terrain = Drawing;
            if (terrain == null || __result <= 0 || !ShownBy.TryGetValue(terrain, out var shown) || shown.Blades.Count == 0) return;
            var effect = EffectOf(terrain);
            var bp = BlueprintOf(terrain);
            var _3d = state.CameraMode == CameraRenderMode._3D;

            float grassScale, baseDensity;
            switch (state.Zoom)
            {
                case WorldZoom.Far: grassScale = 4; baseDensity = 0.56f; break;
                case WorldZoom.Medium: grassScale = 2; baseDensity = 0.50f; break;
                default: grassScale = 1; baseDensity = 0.43f; break;
            }
            var altOff = bp.BaseAlt * bp.TerrainFactor * 3;
            var rot = (state.Cameras.Camera2D.Camera.RotateOff / 180) * Math.PI;
            var smat = new Vector4((float)Math.Cos(rot), (float)Math.Sin(rot) * 0.5f, -(float)Math.Sin(rot) / 0.5f, (float)Math.Cos(rot));
            var off2 = new Vector2(state.WorldSpace.WorldPxWidth, state.WorldSpace.WorldPxHeight);
            off2 = (off2 / state.PreciseZoom - off2) / 2;

            effect.TexMatrix = new Vector4(1f, 1f, -1f, 1f);
            effect.TexOffset = new Vector2(0.5f, 0.5f);
            effect.Alpha = (terrain.Alpha - 0.75f) * 4;
            effect.Level = (float)0.0001f;
            effect.RoomMap = state.Rooms.RoomMaps[0];
            effect.SetTechnique(GrassTechniques.DrawBlades);

            RenderTargetBinding[] rts = null;
            if (FSOEnvironment.UseMRT)
            {
                rts = gd.GetRenderTargets();
                if (rts.Length > 1) gd.SetRenderTarget((RenderTarget2D)rts[0].RenderTarget);
            }
            var depth = gd.DepthStencilState;
            gd.DepthStencilState = DepthStencilState.DepthRead;

            foreach (var (type, vb) in shown.Blades)
            {
                if (vb.IsDisposed) continue;
                var t = (int)type;
                var height = LotTypeGrassInfo.Heights[t];
                if (!FSOEnvironment.UseMRT) height /= 2;
                if (height == 0) height = 1;
                var grassNum = (int)Math.Ceiling(height / (float)grassScale);
                var density = baseDensity * LotTypeGrassInfo.GrassDensity[t];

                effect.LightGreen = LotTypeGrassInfo.LightGreen[t].ToVector4();
                effect.DarkGreen = LotTypeGrassInfo.DarkGreen[t].ToVector4();
                effect.LightBrown = LotTypeGrassInfo.LightBrown[t].ToVector4();
                effect.DarkBrown = LotTypeGrassInfo.DarkBrown[t].ToVector4();
                gd.SetVertexBuffer(vb);

                var offset = -state.WorldSpace.GetScreenOffset();
                for (int i = 1; i <= grassNum; i++)
                {
                    effect.World = Matrix.Identity * Matrix.CreateTranslation(0, i * (20 / 522f) * grassScale - altOff, 0);
                    effect.GrassProb = density * ((grassNum - (i / (2f * grassNum))) / (float)grassNum);
                    effect.ParallaxHeight = grassScale * (20 / 522f) * (100 / 512f) / 4;
                    offset += new Vector2(smat.Z, smat.W);
                    effect.ScreenOffset = offset - off2;

                    var pass = effect.CurrentTechnique.Passes[_3d ? 2 : WorldConfig.Current.PassOffset];
                    pass.Apply();
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, __result);
                }
            }

            //back to the lot's ground and colours, for the engine's own blades
            gd.SetVertexBuffer(terrain.VertexBuffer);
            effect.LightGreen = LightGreenOf(terrain).ToVector4();
            effect.DarkGreen = DarkGreenOf(terrain).ToVector4();
            effect.LightBrown = LightBrownOf(terrain).ToVector4();
            effect.DarkBrown = DarkBrownOf(terrain).ToVector4();
            if (FSOEnvironment.UseMRT) gd.SetRenderTargets(rts);
            gd.DepthStencilState = depth;
        }
    }
}
