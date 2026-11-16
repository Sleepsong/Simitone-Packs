/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Content.Interfaces;
using FSO.Files.Formats.IFF.Chunks;
using FSO.Files.RC;
using FSO.LotView.Components;
using FSO.LotView.Utils;
using FSO.SimAntics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CreateAStyle
{
    /// <summary>
    /// Create a Style, after The Sims 3: recolour the parts of any object on the lot, with no engine change.
    ///
    /// The Sims 3 can do this because its objects come with masks marking their recolourable parts. The Sims' objects
    /// are finished pictures with no such thing, so this pack finds the parts itself, as groups of colours (Style.cs),
    /// and recolours by making its own copies of the object's textures (StyleTextures.cs). This file is where that
    /// meets the engine:
    ///
    ///  1. Which objects are styled. The engine knows nothing of it: it is kept beside the objects.
    ///  2. Drawing in 2D. An object's sprites are swapped for the styled copies as the engine lists them for drawing.
    ///  3. Drawing in 3D. A model is shared by every object of its kind, so its textures are swapped for the styled
    ///     copies only while a styled object is being drawn, and put back after. A model the engine built from the
    ///     sprites uses those sprites' textures; a model somebody remade has a texture of its own. Both are recoloured
    ///     from the texture's own pixels.
    ///  4. Saving. Styles are this pack's own data for the lot, by object, and are put back when the lot is loaded.
    ///  5. The tool. A Style Brush in build mode, with the wall coverings.
    /// </summary>
    public class CreateAStylePack : ISimitonePack
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
            PackManager.RegisterCommand("stylebrush", StyleBrushCommand);
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(CreateAStylePack), name);

        private static Exception WrongVersion(string what)
        {
            return new Exception(what + " was not found: this pack was made for a different version of Simitone");
        }

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw WrongVersion(type.Name + "." + name);
        }

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name)
        {
            if (AccessTools.Field(typeof(T), name) == null) throw WrongVersion(typeof(T).Name + "." + name);
            return AccessTools.FieldRefAccess<T, F>(name);
        }

        //the engine's private parts that this needs
        private static AccessTools.FieldRef<ObjectComponent, DGRPRenderer> RendererOf;
        private static AccessTools.FieldRef<DGRPRenderer, List<DGRPRendererItem>> ItemsOf;
        private static AccessTools.FieldRef<DGRPRenderer, DGRP3DMesh> MeshOf;
        public static AccessTools.FieldRef<DGRPSprite, DGRP> ParentOf;

        private static void Apply(Harmony harmony)
        {
            RendererOf = Field<ObjectComponent, DGRPRenderer>("dgrp");
            ItemsOf = Field<DGRPRenderer, List<DGRPRendererItem>>("Items");
            MeshOf = Field<DGRPRenderer, DGRP3DMesh>("Mesh");
            ParentOf = Field<DGRPSprite, DGRP>("Parent");

            // 2
            harmony.Patch(Find(typeof(DGRPRenderer), "ValidateSprite"), postfix: Mine(nameof(StyleSprites)));
            // 3
            harmony.Patch(Find(typeof(DGRPRenderer), "Draw3D"), prefix: Mine(nameof(EnterDraw3D)), finalizer: Mine(nameof(LeaveDraw3D)));
            // 4 (saving is OnLotSaved and OnLotLoaded, below: no patch. this applies what was loaded once the lot is there,
            //    and has objects whose pictures were not ready drawn again)
            harmony.Patch(Find(typeof(VM), "Update"), prefix: Mine(nameof(BeforeUpdate)));
            // 5
            harmony.Patch(Find(typeof(UIBuyBrowsePanel), "AddWallpapers"), postfix: Mine(nameof(AddStyleBrush)));
        }

        // ---- 1. which objects are styled ----

        /// <summary>What this pack keeps for a styled object's drawing.</summary>
        private class Shown
        {
            public StyleTextures Textures;
            public WeakReference<VMEntity> Entity;
            public WeakReference<VM> Lot;
            public WeakReference<ObjectComponent> Component;
            /// <summary>Some of the object's pictures could not be recoloured yet: it is to be drawn again a little later.</summary>
            public bool Pending;
        }

        private static ConditionalWeakTable<VMEntity, Style> Styles = new ConditionalWeakTable<VMEntity, Style>();
        private static ConditionalWeakTable<DGRPRenderer, Shown> ShownBy = new ConditionalWeakTable<DGRPRenderer, Shown>();
        /// <summary>Every styled object being drawn, to give their textures back when their lot goes.</summary>
        private static List<(WeakReference<DGRPRenderer> renderer, Shown shown)> Live = new List<(WeakReference<DGRPRenderer>, Shown)>();

        /// <summary>The style an object has, or null.</summary>
        public static Style StyleOf(VMEntity entity)
        {
            return (entity != null && Styles.TryGetValue(entity, out var style)) ? style : null;
        }

        /// <summary>All the tiles of an object: a sofa is two objects to the game.</summary>
        public static List<VMEntity> Whole(VMEntity entity)
        {
            var group = entity.MultitileGroup?.Objects;
            return (group != null && group.Count > 0) ? group.ToList() : new List<VMEntity> { entity };
        }

        /// <summary>Gives an object, all of it, a style. Null or a style that changes nothing takes its style off.</summary>
        public static void SetStyle(VM vm, VMEntity entity, Style style)
        {
            foreach (var tile in Whole(entity)) SetTileStyle(vm, tile, style);
        }

        private static void SetTileStyle(VM vm, VMEntity entity, Style style)
        {
            if (style != null && style.IsDefault) style = null;
            Styles.Remove(entity);
            if (style != null) Styles.Add(entity, style);

            var component = entity.WorldUI as ObjectComponent;
            if (component == null) return;
            var renderer = RendererOf(component);
            if (renderer == null) return;
            Show(renderer, style, entity, vm, component);
        }

        /// <summary>Has the engine draw an object's pictures with a style, or (null) as they were made.</summary>
        private static void Show(DGRPRenderer renderer, Style style, VMEntity entity, VM vm, ObjectComponent component)
        {
            if (ShownBy.TryGetValue(renderer, out var old))
            {
                ShownBy.Remove(renderer);
                Live.RemoveAll(x => x.shown == old);
                old.Textures.Release();
            }
            if (style != null)
            {
                var shown = new Shown
                {
                    Textures = StyleTextures.Acquire(style),
                    Entity = new WeakReference<VMEntity>(entity),
                    Lot = new WeakReference<VM>(vm),
                    Component = new WeakReference<ObjectComponent>(component)
                };
                ShownBy.Add(renderer, shown);
                Live.Add((new WeakReference<DGRPRenderer>(renderer), shown));
            }
            Redraw(renderer, component);
        }

        /// <summary>Has the engine list an object's sprites again, and draw it again.</summary>
        private static void Redraw(DGRPRenderer renderer, ObjectComponent component)
        {
            renderer.InvalidateRotation();
            component?.blueprint?.Changes.RegisterObjectChange(component);
        }

        /// <summary>
        /// How the styling of an object is going, in a few words for the panel. Null if it has no style on show.
        /// </summary>
        public static string StatusOf(VMEntity entity)
        {
            var tiles = Whole(entity);
            var textures = Live.Where(x => x.shown.Entity.TryGetTarget(out var e) && tiles.Contains(e))
                .Select(x => x.shown.Textures).Distinct().ToList();
            if (textures.Count == 0) return null;
            var styled = textures.Sum(x => x.Styled);
            var waiting = textures.Sum(x => x.Waiting);
            var failed = textures.Sum(x => x.Failed);
            if (failed > 0) return $"Could not recolour {failed} picture{(failed == 1 ? "" : "s")}: {textures.Select(x => x.LastError).FirstOrDefault(x => x != null)}";
            if (waiting > 0) return "Getting the object's pictures ready...";
            if (styled == 0) return "None of the pictures on show have the colours changed.";
            return $"Recoloured {styled} picture{(styled == 1 ? "" : "s")}.";
        }

        /// <summary>Gives back the textures of styled objects that are gone: deleted, or of a lot that is not this one.</summary>
        private static void ReleaseGone(VM current)
        {
            foreach (var (rendererRef, shown) in Live.ToList())
            {
                var gone = !rendererRef.TryGetTarget(out var renderer)
                    || !shown.Entity.TryGetTarget(out var entity) || entity.Dead
                    || !shown.Lot.TryGetTarget(out var lot) || lot != current;
                if (!gone) continue;
                Live.RemoveAll(x => x.shown == shown);
                if (renderer != null) ShownBy.Remove(renderer);
                shown.Textures.Release();
            }
        }

        /// <summary>
        /// The parts of an object, worked out from all of its pictures: every tile, from every side, close up. And
        /// from its remade model's own textures, if it has one and it is loaded.
        /// </summary>
        public static Style Analyse(VMEntity entity)
        {
            var colours = new Dictionary<uint, int>();
            foreach (var tile in Whole(entity))
            {
                var component = tile.WorldUI as ObjectComponent;
                var dgrp = component?.DGRP;
                if (dgrp == null) continue;
                foreach (var direction in new uint[] { 0x01, 0x04, 0x10, 0x40 })
                {
                    var image = dgrp.GetImage(direction, 3, 0);
                    if (image == null) continue;
                    foreach (var sprite in image.Sprites)
                    {
                        if (sprite != null) Pixels.Count(Pixels.ForAnalysis(sprite, GameFacade.GraphicsDevice), colours);
                    }
                }

                var mesh = MeshOf(RendererOf(component));
                if (mesh?.Geoms == null) continue;
                foreach (var geom in mesh.Geoms.SelectMany(x => x.Values))
                {
                    if (geom.CustomTexture == 0 || geom.Pixel == null || geom.Pixel.IsDisposed) continue;
                    try
                    {
                        Pixels.Count(Pixels.OfTexture(geom.Pixel, out _, out _), colours);
                    }
                    catch
                    {
                        //not a texture that can be read back: its parts come from the sprites alone
                    }
                }
            }
            return Style.Analyse(colours);
        }

        // ---- 2. drawing in 2D ----

        /// <summary>
        /// The engine has just made sure of the list of sprites it draws for an object (it makes the list anew when the
        /// object turns, changes its look, or the view zooms). Point the ones of a styled object at the styled copies.
        /// </summary>
        public static void StyleSprites(DGRPRenderer __instance)
        {
            if (!ShownBy.TryGetValue(__instance, out var shown)) return;
            var textures = shown.Textures;
            foreach (var item in ItemsOf(__instance))
            {
                var pixel = item.Sprite.Pixel;
                if (pixel == null || textures.IsMine(pixel)) continue;
                var styled = textures.Get(pixel, item.DGRPSprite, out var pending);
                if (styled != pixel) item.Sprite.Pixel = styled;
                if (pending) shown.Pending = true;
            }
        }

        // ---- 3. drawing in 3D ----

        /// <summary>The textures of the model being drawn that were swapped for styled copies, to put back after.</summary>
        [ThreadStatic] private static List<(DGRP3DGeometry geom, Texture2D original)> Swapped;

        /// <summary>
        /// The engine is about to draw an object's model. The model is shared by every object of its kind, so there is
        /// nowhere on it to keep a styled texture: instead, while a styled object is drawn, its textures are swapped for
        /// the styled copies, and put back as soon as it is done (LeaveDraw3D).
        /// </summary>
        public static void EnterDraw3D(DGRPRenderer __instance)
        {
            PutBack();
            if (!ShownBy.TryGetValue(__instance, out var shown)) return;
            __instance.GetBounds(); //makes sure the model is the one the engine is about to draw: it does the same first
            var mesh = MeshOf(__instance);
            if (mesh?.Geoms == null) return;
            foreach (var geom in mesh.Geoms.SelectMany(x => x.Values))
            {
                var pixel = geom.Pixel;
                if (pixel == null || shown.Textures.IsMine(pixel)) continue;
                var styled = shown.Textures.Get(pixel, SpriteOf(__instance, geom), out var pending);
                if (pending) shown.Pending = true;
                if (styled == pixel) continue;
                (Swapped ??= new List<(DGRP3DGeometry, Texture2D)>()).Add((geom, pixel));
                geom.Pixel = styled;
            }
        }

        public static Exception LeaveDraw3D(Exception __exception)
        {
            PutBack();
            return __exception;
        }

        private static void PutBack()
        {
            if (Swapped == null || Swapped.Count == 0) return;
            foreach (var (geom, original) in Swapped) geom.Pixel = original;
            Swapped.Clear();
        }

        /// <summary>The sprite a texture of an object's model is from, or null if it is the model's own texture.</summary>
        private static DGRPSprite SpriteOf(DGRPRenderer renderer, DGRP3DGeometry geom)
        {
            if (geom.CustomTexture != 0) return null;
            //the model says which: the sprite at this place in the object's picture, close up, from this side
            var sprites = renderer.DGRP?.GetImage(1, 3, geom.PixelDir)?.Sprites;
            return (sprites != null && geom.PixelSPR < sprites.Length) ? sprites[geom.PixelSPR] : null;
        }

        // ---- 4. saving ----

        private const byte SaveVersion = 1;

        /// <summary>Styles read from a lot's file, waiting for the lot's objects to be there to put them on.</summary>
        private static ConditionalWeakTable<VM, byte[]> Loaded = new ConditionalWeakTable<VM, byte[]>();

        public void OnLotSaved(VM vm, string lotPath)
        {
            ReleaseGone(vm);
            var styled = vm.Entities.Where(x => !x.Dead && x.Object?.OBJ != null && StyleOf(x) != null).ToList();
            if (styled.Count == 0)
            {
                LotPackData.Write(PackId, lotPath, null);
                return;
            }
            using (var mem = new MemoryStream())
            using (var io = new BinaryWriter(mem))
            {
                io.Write(SaveVersion);
                io.Write(styled.Count);
                foreach (var entity in styled)
                {
                    //an object is known by its number on the lot. its kind is kept too, to tell if the number has since
                    //gone to something else (the lot was played without this pack)
                    io.Write(entity.ObjectID);
                    io.Write(entity.Object.OBJ.GUID);
                    StyleOf(entity).Write(io);
                }
                io.Flush();
                LotPackData.Write(PackId, lotPath, mem.ToArray());
            }
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            UIStylePanel.Drop();
            ReleaseGone(vm);
            Loaded.Remove(vm);
            if (lotPath == null) return; //not from a file: the objects are the ones that were handed over, with their styles
            var saved = LotPackData.Read(PackId, lotPath);
            if (saved != null) Loaded.Add(vm, saved);
        }

        private static long NextRetry;

        public static void BeforeUpdate(VM __instance)
        {
            RetryPending();
            if (!Loaded.TryGetValue(__instance, out var saved)) return;
            if (__instance.Entities.Count == 0) return; //not there yet
            Loaded.Remove(__instance);
            try
            {
                using (var io = new BinaryReader(new MemoryStream(saved)))
                {
                    if (io.ReadByte() != SaveVersion) return;
                    var count = io.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var id = io.ReadInt16();
                        var guid = io.ReadUInt32();
                        var style = Style.Read(io);
                        var entity = __instance.GetObjectById(id);
                        if (entity?.Object?.OBJ == null || entity.Object.OBJ.GUID != guid) continue;
                        SetTileStyle(__instance, entity, style);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Create a Style: could not read this lot's styles: " + e.Message);
            }
        }

        /// <summary>
        /// Draws again the objects with pictures that could not be recoloured last time (most often, still loading).
        /// In 2D an object that is not moving is drawn once into a layer that is kept, so it would not be tried again
        /// on its own.
        /// </summary>
        private static void RetryPending()
        {
            var now = Environment.TickCount64;
            if (now < NextRetry) return;
            NextRetry = now + 250;
            StyleTextures.Sweep();
            foreach (var (rendererRef, shown) in Live)
            {
                if (!shown.Pending || !rendererRef.TryGetTarget(out var renderer)) continue;
                shown.Pending = false;
                shown.Component.TryGetTarget(out var component);
                Redraw(renderer, component);
            }
        }

        // ---- 5. the tool ----

        public static void AddStyleBrush(UIBuyBrowsePanel __instance)
        {
            var res = new UICatalogStyleBrushResProvider();
            //(free, so it sorts ahead of the wall coverings, which the engine puts in order of price after this)
            __instance.FullCategory.Insert(0, new UICatalogElement
            {
                Item = new ObjectCatalogItem()
                {
                    Name = res.GetName(0),
                    Category = 15,
                    Price = 0,
                },
                Special = new UISpecialCatalogElement
                {
                    Control = typeof(UIStyleBrush),
                    ResID = 0,
                    Res = res,
                    Parameters = new List<int>()
                }
            });
        }

        /// <summary>The console's "stylebrush": picks up the Style Brush from anywhere in build or buy mode.</summary>
        private static bool StyleBrushCommand(string[] args)
        {
            var control = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.LotControl;
            if (control?.vm == null || control.World == null) return false;
            control.ObjectHolder?.ClearSelected();
            control.CustomControl?.Release();
            control.CustomControl = new UIStyleBrush(control.vm, control.World, control, new List<int>());
            return true;
        }
    }
}
