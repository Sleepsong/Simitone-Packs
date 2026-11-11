/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls.Catalog;
using FSO.Common.Utils;
using FSO.Content.Interfaces;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Effects;
using FSO.LotView.Model;
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

namespace RoofTool
{
    /// <summary>
    /// Drawn roofs, as in The Sims 2, added to the engine entirely from a pack.
    ///
    /// The same feature exists as an engine change (FreeSO branch simitone/roof-tool: about 620 lines added and 190
    /// removed, in 8 files). The feature's own code is here almost unchanged: DrawnRoofs.cs, RoofTools.cs and
    /// UIRoofTools.cs. This file and RoofMesh.cs are the rest: where the engine change edited existing engine code.
    ///
    ///  1. Where the roofs are kept. The engine change added a field to the architecture. Here they are kept beside it.
    ///  2. The roof commands. The engine change added three command types. Here three unused numbers are handled from outside.
    ///  3. Previews. The engine change swapped the roofs for a copy inside the engine's preview code. Here the copy is
    ///     made before that code runs and shown after it.
    ///  4. Saving. The engine change wrote the roofs into the lot's save. Here they are this pack's own data for the lot.
    ///  5. The build catalog. The engine change added lines to the roofs list. Here they are added after it.
    ///  6. Making the roof's shape. The engine change rewrote the roof component's meshing so that the automatic roof
    ///     and drawn roofs share it. That can't be done from outside. Here the engine's meshing is left alone for the
    ///     automatic roof, and replaced, whole, by this pack's own (RoofMesh.cs) while a lot has drawn roofs.
    ///  7. Drawing gable ends. The engine change added them to the roof's draw code. Here they are drawn right after it.
    /// </summary>
    public class RoofToolPack : ISimitonePack
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

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(RoofToolPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] args)
        {
            var method = args.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
            return method ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static AccessTools.FieldRef<TOwner, T> Field<TOwner, T>(string name)
        {
            if (AccessTools.Field(typeof(TOwner), name) == null) throw new Exception(typeof(TOwner).Name + "." + name + " was not found: this pack was made for a different version of Simitone");
            return AccessTools.FieldRefAccess<TOwner, T>(name);
        }

        //private parts of the engine this pack needs
        private static AccessTools.FieldRef<VMArchitecture, Blueprint> WorldOf;
        private static AccessTools.FieldRef<RoofComponent, List<RoofComponent.RoofRect>[]> RoofRectsOf;
        private static AccessTools.FieldRef<RoofComponent, GrassEffect> EffectOf;
        private static MethodInfo PrepTextures;
        private static FieldInfo MarioOf; //may be missing: only used to match one special camera mode

        private static void Apply(Harmony harmony)
        {
            WorldOf = Field<VMArchitecture, Blueprint>("WorldUI");
            RoofRectsOf = Field<RoofComponent, List<RoofComponent.RoofRect>[]>("RoofRects");
            EffectOf = Field<RoofComponent, GrassEffect>("Effect");
            PrepTextures = Find(typeof(RoofComponent), "PrepTextures");
            MarioOf = AccessTools.Field(typeof(AvatarComponent), "MyMario");

            // 2
            harmony.Patch(Find(typeof(VMArchitecture), "RunCommands"), postfix: Mine(nameof(RunRoofCommands)));
            // 3
            harmony.Patch(Find(typeof(VMArchitecture), "SimulateCommands"), prefix: Mine(nameof(EnterPreview)), finalizer: Mine(nameof(LeavePreview)));
            // 4 (saving is OnLotSaved and OnLotLoaded, below: no patch. this applies what was loaded once the lot is there)
            harmony.Patch(Find(typeof(VMArchitecture), "Tick"), prefix: Mine(nameof(BeforeArchitectureTick)));
            // 5
            harmony.Patch(Find(typeof(UIBuyBrowsePanel), "AddRoofs"), postfix: Mine(nameof(AddRoofTools)));
            // 6
            harmony.Patch(Find(typeof(RoofComponent), "RegenRoof", typeof(GraphicsDevice)), prefix: Mine(nameof(RegenDrawnRoofs)));
            harmony.Patch(Find(typeof(RoofComponent), "EnsureTextures"), prefix: Mine(nameof(EnsureDrawnTextures)));
            harmony.Patch(Find(typeof(RoofComponent), "MeshRectData"), prefix: Mine(nameof(MeshDrawnRoofs)));
            harmony.Patch(Find(typeof(RoofComponent), "MeshRects"), postfix: Mine(nameof(MakeGableBuffers)));
            harmony.Patch(Find(typeof(RoofComponent), "Dispose"), postfix: Mine(nameof(DisposeGables)));
            // 7
            harmony.Patch(Find(typeof(RoofComponent), "Draw"), postfix: Mine(nameof(DrawGables)));
            harmony.Patch(Find(typeof(RoofComponent), "DrawOne"), postfix: Mine(nameof(DrawOneGables)));
        }

        // ---- 1. where the roofs are kept ----

        /// <summary>A lot's drawn roofs: the real ones and, while a tool previews a command, a copy with the command applied.</summary>
        private class LotRoofs
        {
            public DrawnRoofs Roofs = new DrawnRoofs();
            public DrawnRoofs Preview;
            public bool InPreview;
            public DrawnRoofs Current => InPreview ? Preview : Roofs;
        }

        /// <summary>
        /// Kept by the lot's terrain object, not its architecture: when a lot is handed over in memory (coming home from
        /// a visit) the engine makes a new architecture, but gives it the same terrain.
        /// </summary>
        private static ConditionalWeakTable<VMArchitectureTerrain, LotRoofs> Lots = new ConditionalWeakTable<VMArchitectureTerrain, LotRoofs>();
        private static LotRoofs For(VMArchitecture arch) => Lots.GetOrCreateValue(arch.Terrain);

        /// <summary>A lot's drawn roofs, as they really are (not a preview).</summary>
        public static DrawnRoofs RoofsOf(VMArchitecture arch) => For(arch).Roofs;

        // ---- 2. the roof commands: the engine has skipped them, as types it doesn't know. run them and add their cost ----

        public static void RunRoofCommands(VMArchitecture __instance, List<VMArchitectureCommand> commands, ref int __result)
        {
            if (__instance.Terrain == null) return;
            LotRoofs lot = null;
            foreach (var command in commands)
            {
                if (command.Type != RoofTools.RectCommand && command.Type != RoofTools.EraseCommand && command.Type != RoofTools.AutoCommand) continue;
                lot = lot ?? For(__instance);
                __result += RoofTools.Apply(lot.Current, __instance.Width, __instance.Height, __instance.Stories, command, out var fail, out var changed);
                if (fail != 0) __instance.LastFailReason = fail;
                if (changed && !lot.InPreview) __instance.SignalRedraw(); //changed for real: show it
            }
        }

        // ---- 3. previews ----

        public static void EnterPreview(VMArchitecture __instance)
        {
            if (__instance.Terrain == null) return;
            var lot = For(__instance);
            lot.Preview = lot.Roofs.Clone();
            lot.InPreview = true;
        }

        public static Exception LeavePreview(VMArchitecture __instance, bool visualChange, Exception __exception)
        {
            if (__instance.Terrain == null) return __exception;
            var lot = For(__instance);
            lot.InPreview = false;
            //the engine shows the walls and floors of what it just simulated (or the real ones, with no commands). so with the roofs
            if (visualChange) Show(WorldOf(__instance), lot.Preview ?? lot.Roofs);
            return __exception;
        }

        // ---- 4. saving ----

        /// <summary>Roofs read from a lot's file, waiting for the lot to be there to have them.</summary>
        private static ConditionalWeakTable<VM, byte[]> Loaded = new ConditionalWeakTable<VM, byte[]>();

        public void OnLotSaved(VM vm, string lotPath)
        {
            var arch = vm.Context?.Architecture;
            if (arch?.Terrain == null) return;
            LotPackData.Write(PackId, lotPath, For(arch).Roofs.Save());
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            Loaded.Remove(vm);
            if (lotPath == null) return; //not from a file: the lot is the one that was handed over, with its roofs
            var saved = LotPackData.Read(PackId, lotPath);
            if (saved != null) Loaded.Add(vm, saved);
        }

        public static void BeforeArchitectureTick(VMArchitecture __instance)
        {
            var vm = __instance.Context?.VM;
            if (vm == null || __instance.Terrain == null || !Loaded.TryGetValue(vm, out var saved)) return;
            Loaded.Remove(vm);
            var roofs = DrawnRoofs.Load(saved);
            if (roofs == null) return;
            For(__instance).Roofs = roofs;
            __instance.SignalRedraw();
        }

        // ---- 5. the build catalog ----

        public static void AddRoofTools(UIBuyBrowsePanel __instance)
        {
            var toolRes = new UICatalogRoofToolResProvider();
            var tools = new[]
            {
                UICatalogRoofToolResProvider.Auto,
                UICatalogRoofToolResProvider.Remove,
                UICatalogRoofToolResProvider.Shed,
                UICatalogRoofToolResProvider.Gable,
                UICatalogRoofToolResProvider.Hip,
            };
            foreach (var tool in tools)
            {
                __instance.FullCategory.Insert(0, new UICatalogElement
                {
                    Item = new ObjectCatalogItem()
                    {
                        Name = toolRes.GetName((ulong)tool),
                        Category = 17,
                        Price = (uint)toolRes.GetPrice((ulong)tool),
                    },
                    Special = new UISpecialCatalogElement
                    {
                        Control = (tool == UICatalogRoofToolResProvider.Auto) ? typeof(UIAutoRoofer) : typeof(UIRoofTool),
                        ResID = (uint)tool,
                        Res = toolRes,
                        Parameters = new List<int> { tool }
                    }
                });
            }
        }

        // ---- 6. making the roof's shape ----

        /// <summary>What this pack keeps for a roof component: the drawn roofs it is showing, and their gable ends.</summary>
        private class Shown
        {
            public DrawnRoofs Roofs;
            /// <summary>True once the component has meshed drawn roofs, until the automatic roof has cleared them out again.</summary>
            public bool MeshedDrawn;
            /// <summary>By level - 2, as the component's own draw groups: gable ends made by the last meshing, and their buffers.</summary>
            public Dictionary<int, RoofMesh.Gables> GableData = new Dictionary<int, RoofMesh.Gables>();
            public Dictionary<int, (VertexBuffer vb, IndexBuffer ib, int primitives)> GableBuffers = new Dictionary<int, (VertexBuffer, IndexBuffer, int)>();

            public bool DrawnMode => Roofs != null && !Roofs.AutoRoof;

            public void DisposeGables(int index)
            {
                if (!GableBuffers.TryGetValue(index, out var buffers)) return;
                buffers.vb.Dispose();
                buffers.ib.Dispose();
                GableBuffers.Remove(index);
            }
        }
        private static ConditionalWeakTable<RoofComponent, Shown> ShownBy = new ConditionalWeakTable<RoofComponent, Shown>();

        /// <summary>Shows these drawn roofs on a lot's roof component (rebuilding the roof only if they changed).</summary>
        private static void Show(Blueprint blueprint, DrawnRoofs roofs)
        {
            var comp = blueprint?.RoofComp;
            if (comp == null) return;
            var shown = ShownBy.GetOrCreateValue(comp);
            if (roofs.SameAs(shown.Roofs)) return;
            shown.Roofs = roofs.Clone();
            comp.ShapeDirty = true;
        }

        /// <summary>
        /// The roof is being rebuilt. With drawn roofs there is no automatic roof to work out: mesh the drawn ones
        /// instead, and skip the engine's rebuild. Otherwise let the engine rebuild its automatic roof, first clearing
        /// out any drawn roofs it would leave behind on floors it has nothing to roof.
        /// </summary>
        public static bool RegenDrawnRoofs(RoofComponent __instance, GraphicsDevice device)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown)) return true;
            var rects = RoofRectsOf(__instance);
            if (!shown.DrawnMode && !shown.MeshedDrawn) return true;

            for (int i = 0; i < rects.Length; i++) rects[i] = new List<RoofComponent.RoofRect>();
            if (shown.DrawnMode)
            {
                EnsureDrawnTextures(__instance, device);
                shown.MeshedDrawn = true;
                for (int i = 1; i <= __instance.blueprint.Stories; i++) __instance.MeshRects((sbyte)(i + 1), device);
                __instance.blueprint.SM64?.UpdateRoof();
                return false;
            }
            shown.MeshedDrawn = false;
            for (int i = 1; i <= __instance.blueprint.Stories; i++) __instance.MeshRects((sbyte)(i + 1), device);
            return true;
        }

        /// <summary>
        /// The engine loads the roof's textures only when its automatic roof has something to cover.
        /// Drawn roofs need them too.
        /// </summary>
        public static bool EnsureDrawnTextures(RoofComponent __instance, GraphicsDevice device)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown) || !shown.DrawnMode) return true;
            if (shown.Roofs.Pieces.Count > 0 && (__instance.StyleDirty || __instance.Texture == null))
            {
                PrepTextures.Invoke(__instance, new object[] { device });
                __instance.StyleDirty = false;
            }
            return false;
        }

        /// <summary>The engine is meshing one floor's roof. With drawn roofs, this pack's meshing takes its place.</summary>
        public static bool MeshDrawnRoofs(RoofComponent __instance, int level, ref RoofData __result)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown)) return true;
            shown.GableData.Remove(level - 2);
            if (!shown.DrawnMode) return true;
            __result = RoofMesh.Mesh(__instance, shown.Roofs.Pieces.Where(x => x.Level == level).Select(RoofMesh.PieceShape).ToList(), level, out var gables);
            shown.GableData[level - 2] = gables;
            return false;
        }

        /// <summary>The engine has made a floor's roof buffers. Make the gable ends' buffers to go with them.</summary>
        public static void MakeGableBuffers(RoofComponent __instance, int level, GraphicsDevice device)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown)) return;
            var index = level - 2;
            shown.DisposeGables(index);
            if (!shown.GableData.TryGetValue(index, out var gables) || gables.Indices.Length == 0) return;
            var vb = new VertexBuffer(device, typeof(FSO.LotView.Utils.TerrainParallaxVertex), gables.Vertices.Length, BufferUsage.None);
            vb.SetData(gables.Vertices);
            var ib = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, sizeof(int) * gables.Indices.Length, BufferUsage.None);
            ib.SetData(gables.Indices);
            shown.GableBuffers[index] = (vb, ib, gables.Indices.Length / 3);
        }

        /// <summary>The gable ends last meshed for a floor of a roof component, if it is showing drawn roofs. For checks.</summary>
        public static RoofMesh.Gables? GablesOf(RoofComponent roof, int level)
        {
            return ShownBy.TryGetValue(roof, out var shown) && shown.GableData.TryGetValue(level - 2, out var gables) ? gables : (RoofMesh.Gables?)null;
        }

        public static void DisposeGables(RoofComponent __instance)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown)) return;
            foreach (var index in shown.GableBuffers.Keys.ToList()) shown.DisposeGables(index);
        }

        // ---- 7. drawing gable ends ----

        /// <summary>
        /// The engine has drawn the roof. The engine change drew each floor's gable ends along with that floor's roof,
        /// inside the engine's drawing code. From outside, they are drawn in one go after it, set up the way the engine
        /// set up each floor.
        /// </summary>
        public static void DrawGables(RoofComponent __instance, GraphicsDevice device, WorldState world)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown) || shown.GableBuffers.Count == 0 || __instance.Drawgroups == null) return;
            var effect = EffectOf(__instance);
            device.RasterizerState = RasterizerState.CullClockwise;
            device.BlendState = BlendState.AlphaBlend;
            //(the engine hides the top floor's roof in one of its special camera modes; that mode's marker is not public)
            var special = world.ScrollAnchor != null && MarioOf != null && MarioOf.GetValue(world.ScrollAnchor) != null;
            int maxLevel = special ? world.Level - 2 : world.Level - 1;
            for (int i = 0; i < __instance.Drawgroups.Length && i <= maxLevel; i++)
            {
                if (!shown.GableBuffers.TryGetValue(i, out var gables) || __instance.Drawgroups[i] == null || __instance.Drawgroups[i].NumPrimitives == 0) continue;
                effect.Level = (float)i + 1.0001f;
                PPXDepthEngine.RenderPPXDepth(effect, true, (depthMode) =>
                {
                    SetUp(effect, world.View, world.Projection, world);
                    DrawOneLevel(device, effect, gables, (world.CameraMode == CameraRenderMode._3D) ? 2 : WorldConfig.Current.PassOffset);
                });
            }
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullNone;
        }

        /// <summary>The same, for the engine's drawing of a single floor's roof (lot thumbnails).</summary>
        public static void DrawOneGables(RoofComponent __instance, GraphicsDevice device, Matrix view, Matrix projection, WorldState world, int i)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown) || !shown.GableBuffers.TryGetValue(i, out var gables)) return;
            if (i > world.Level - 1 || __instance.Drawgroups[i] == null || __instance.Drawgroups[i].NumPrimitives == 0) return;
            var effect = EffectOf(__instance);
            effect.Level = (float)i + 1.0001f;
            SetUp(effect, view, projection, world);
            effect.FadeRectangle = new Vector4(-1000, -1000, 2000, 2000);
            effect.FadeWidth = 35f * 3;
            DrawOneLevel(device, effect, gables, 2);
        }

        private static void SetUp(GrassEffect effect, Matrix view, Matrix projection, WorldState world)
        {
            effect.View = view;
            effect.Projection = projection;
            effect.World = Matrix.Identity;
            effect.DiffuseColor = new Vector4(world.OutsideColor.R / 255f, world.OutsideColor.G / 255f, world.OutsideColor.B / 255f, 1.0f);
            effect.UseTexture = true;
            effect.Alpha = 1f;
            effect.IgnoreColor = false;
            effect.TexOffset = Vector2.Zero;
            effect.TexMatrix = new Vector4(1, 0, 0, 1);
            effect.SetTechnique(GrassTechniques.DrawBase);
        }

        private static void DrawOneLevel(GraphicsDevice device, GrassEffect effect, (VertexBuffer vb, IndexBuffer ib, int primitives) gables, int passIndex)
        {
            //gable ends: plaster coloured (the vertex colour on plain white)
            device.SetVertexBuffer(gables.vb);
            device.Indices = gables.ib;
            effect.BaseTex = TextureGenerator.GetPxWhite(device);
            effect.GrassShininess = 0.002f;
            effect.CurrentTechnique.Passes[passIndex].Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, gables.primitives);
        }
    }
}
