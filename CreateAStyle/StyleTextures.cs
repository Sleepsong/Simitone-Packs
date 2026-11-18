/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common.Utils;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CreateAStyle
{
    /// <summary>
    /// The recoloured copies of the game's textures. The engine keeps one texture for each sprite and every object
    /// that uses the sprite shares it, so a styled object needs copies of its own. Objects with the same style share
    /// theirs, and a style's copies are thrown away when the last object stops using it.
    /// </summary>
    public class StyleTextures
    {
        private static Dictionary<string, StyleTextures> ByKey = new Dictionary<string, StyleTextures>();

        /// <summary>How long to wait before trying a texture again whose pixels could not be had, and how many times.</summary>
        private const int RetryMs = 500, MaxTries = 20;
        /// <summary>
        /// How long a copy is kept after it stops being used, before it is disposed. When an object's style changes, the
        /// engine lists its sprites afresh before it next draws it, so the old copies should be out of use at once: this
        /// is so that if anything still holds one for a frame, it draws a texture and not a disposed one (which crashes).
        /// </summary>
        private const int RetireMs = 2000;
        private static List<(Texture2D texture, long when)> Retired = new List<(Texture2D, long)>();

        public readonly Style Style;
        private int Users;
        /// <summary>For each of the game's textures, its styled copy, or itself if the style changes nothing in it.</summary>
        private Dictionary<Texture2D, Texture2D> Copies = new Dictionary<Texture2D, Texture2D>();
        private HashSet<Texture2D> Mine = new HashSet<Texture2D>();
        /// <summary>Textures whose pixels could not be had yet (still loading, say): when to try again, and how often it has been tried.</summary>
        private Dictionary<Texture2D, (long when, int tries)> TryAgain = new Dictionary<Texture2D, (long, int)>();

        // ---- for the panel to say how it went ----

        /// <summary>How many of the game's pictures this style has recoloured.</summary>
        public int Styled => Mine.Count;
        /// <summary>How many it is still waiting to be able to read.</summary>
        public int Waiting => TryAgain.Count;
        /// <summary>How many it has given up on, and why the last one failed.</summary>
        public int Failed { get; private set; }
        public string LastError { get; private set; }

        private StyleTextures(Style style) { Style = style; }

        /// <summary>The copies for a style, counted as one more user of them. Give them back with Release.</summary>
        public static StyleTextures Acquire(Style style)
        {
            if (!ByKey.TryGetValue(style.Key, out var result))
            {
                result = new StyleTextures(style.Clone());
                ByKey[style.Key] = result;
            }
            result.Users++;
            return result;
        }

        public void Release()
        {
            if (--Users > 0) return;
            ByKey.Remove(Style.Key);
            foreach (var copy in Mine) Retire(copy);
            Mine.Clear();
            Copies.Clear();
            TryAgain.Clear();
        }

        public bool IsMine(Texture2D texture) => Mine.Contains(texture);

        private static void Retire(Texture2D copy)
        {
            Retired.Add((copy, Environment.TickCount64 + RetireMs));
        }

        /// <summary>Disposes the copies that stopped being used a little while ago (or, with now, all of them).</summary>
        public static void Sweep(bool now = false)
        {
            if (Retired.Count == 0) return;
            var time = Environment.TickCount64;
            Retired.RemoveAll(x =>
            {
                if (!now && time < x.when) return false;
                x.texture.Dispose();
                return true;
            });
        }

        /// <summary>
        /// The styled copy of one of the game's textures, made on first use. Sprite is where the texture came from, if
        /// it came from a sprite. Keep marks pixels to leave as they are (a Sim's skin), if any; it is to be the same
        /// for every call with the same texture. The texture itself is returned when the style changes nothing in it, or
        /// when it cannot be recoloured yet: then pending is true, and it is worth asking again a little later.
        /// </summary>
        public Texture2D Get(Texture2D original, DGRPSprite sprite, out bool pending, bool[] keep = null)
        {
            pending = false;
            if (original == null || original.IsDisposed) return original;
            if (Copies.TryGetValue(original, out var copy) && !copy.IsDisposed) return copy;
            TryAgain.TryGetValue(original, out var retry);
            if (retry.tries > 0 && Environment.TickCount64 < retry.when)
            {
                pending = true;
                return original;
            }

            Forget();
            try
            {
                copy = Make(original, sprite, keep);
            }
            catch (Exception e)
            {
                Console.WriteLine("Create a Style: could not recolour a texture: " + e);
                GiveUp(original, e.Message);
                return original;
            }
            if (copy == null)
            {
                if (retry.tries + 1 >= MaxTries)
                {
                    GiveUp(original, "its picture could not be read");
                    return original;
                }
                TryAgain[original] = (Environment.TickCount64 + RetryMs, retry.tries + 1);
                pending = true;
                return original;
            }
            TryAgain.Remove(original);
            Copies[original] = copy;
            if (copy != original) Mine.Add(copy);
            return copy;
        }

        /// <summary>Stop trying a texture: it is shown as it was made.</summary>
        private void GiveUp(Texture2D original, string why)
        {
            TryAgain.Remove(original);
            Copies[original] = original;
            Failed++;
            LastError = why;
        }

        /// <summary>The engine throws its textures away when they go unused, and makes them anew: drop copies of those.</summary>
        private void Forget()
        {
            if (Copies.Count == 0 && TryAgain.Count == 0) return;
            foreach (var gone in Copies.Keys.Where(x => x.IsDisposed).ToList())
            {
                var copy = Copies[gone];
                Copies.Remove(gone);
                if (Mine.Remove(copy)) Retire(copy);
            }
            foreach (var gone in TryAgain.Keys.Where(x => x.IsDisposed).ToList()) TryAgain.Remove(gone);
        }

        /// <summary>The styled copy of a texture, the texture itself if the style changes none of it, or null if its pixels cannot be had yet.</summary>
        private Texture2D Make(Texture2D original, DGRPSprite sprite, bool[] keep)
        {
            if (original.Format != SurfaceFormat.Color && original.Format != SurfaceFormat.Dxt5) return original;
            var pixels = Pixels.Of(original, sprite, out var w, out var h);
            if (pixels == null) return null;

            var data = new Color[pixels.Length];
            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                data[i] = (keep != null && i < keep.Length && keep[i]) ? pixels[i] : Style.Apply(pixels[i]);
                if (data[i] != pixels[i]) changed = true;
            }
            if (!changed) return original; //none of this texture is in a part the player changed

            var mip = original.LevelCount > 1;
            var result = new Texture2D(original.GraphicsDevice, original.Width, original.Height, mip, original.Format);
            try
            {
                //as the engine uploads its own (SPR2Frame.GetTexture)
                if (original.Format == SurfaceFormat.Dxt5)
                {
                    var levels = mip ? TextureUtils.GenerateDXT5WithMips(result, w, h, data)
                        : new[] { new TextureData<byte>(0, TextureUtils.DXT5Compress(data, w, h).Item1, 1) };
                    TextureUtils.UploadTexData(result, levels);
                }
                else
                {
                    data = Pixels.Fit(data, w, h, original.Width, original.Height);
                    var levels = mip ? TextureUtils.GenerateMips(result, data) : new[] { new TextureData<Color>(0, data) };
                    TextureUtils.UploadTexData(result, levels);
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }
            result.Tag = original.Tag;
            return result;
        }
    }

    /// <summary>Getting at the pixels of the game's art.</summary>
    public static class Pixels
    {
        private class Read
        {
            public Color[] Data;
            public int Width, Height;
        }

        /// <summary>The pixels of the game's textures that have been read, so that moving a slider does not read them again.</summary>
        private static ConditionalWeakTable<Texture2D, Read> Cache = new ConditionalWeakTable<Texture2D, Read>();

        /// <summary>
        /// The pixels of one of the game's textures, as it shows them: read back from the graphics card, which works
        /// however the engine made the texture. If that cannot be done, from the sprite's file instead, when the
        /// texture is of a sprite. Null if neither can be had yet.
        /// </summary>
        public static Color[] Of(Texture2D texture, DGRPSprite sprite, out int width, out int height)
        {
            if (Cache.TryGetValue(texture, out var read))
            {
                width = read.Width;
                height = read.Height;
                return read.Data;
            }

            Color[] result = null;
            width = height = 0;
            try
            {
                result = OfTexture(texture, out width, out height);
            }
            catch (Exception e)
            {
                Console.WriteLine("Create a Style: could not read a texture back: " + e.Message);
            }
            if (result == null && sprite != null) result = OfSprite(sprite, out width, out height);
            if (result != null) Cache.AddOrUpdate(texture, new Read { Data = result, Width = width, Height = height });
            return result;
        }

        /// <summary>
        /// A sprite's pixels, for finding an object's parts: from its file if it can still be decoded, or else from its
        /// texture, which is made if the engine has not made it yet. Null if it has none.
        /// </summary>
        public static Color[] ForAnalysis(DGRPSprite sprite, GraphicsDevice gd)
        {
            var result = OfSprite(sprite, out _, out _);
            if (result != null || gd == null) return result;
            try
            {
                var texture = sprite.GetTexture(gd);
                return (texture == null) ? null : Of(texture, sprite, out _, out _);
            }
            catch (Exception e)
            {
                Console.WriteLine("Create a Style: could not read a sprite: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// A sprite's pixels, decoded again from its file. Null if it has none, if it is of the old kind (SPR), or if
        /// the engine has let go of its file data: it does once a sprite is on the graphics card when it keeps every
        /// texture for good (the PERMANENT cache, which Simitone uses with DirectX, so on Windows).
        /// </summary>
        public static Color[] OfSprite(DGRPSprite sprite, out int width, out int height)
        {
            width = height = 0;
            var frame = FrameOf(sprite);
            if (frame == null) return null;
            Color[] pixels = null;
            //the engine drops the pixels from another thread when it has uploaded them: if it does so between the
            //decode and the read, decode again
            for (int attempt = 0; attempt < 3 && pixels == null; attempt++)
            {
                frame.DecodeIfRequired(false);
                pixels = frame.PixelData;
            }
            if (pixels == null || frame.Height <= 0 || pixels.Length % frame.Height != 0) return null;
            height = frame.Height;
            width = pixels.Length / height;
            return pixels;
        }

        public static SPR2Frame FrameOf(DGRPSprite sprite)
        {
            var iff = CreateAStylePack.ParentOf(sprite)?.ChunkParent;
            var spr2 = iff?.Get<SPR2>((ushort)sprite.SpriteID);
            if (spr2 == null || sprite.SpriteFrameIndex >= spr2.Frames.Length) return null;
            return spr2.Frames[sprite.SpriteFrameIndex];
        }

        /// <summary>
        /// A texture's pixels, read back from the graphics card. Null when it has nothing in it, which is how a
        /// texture still loading looks.
        /// </summary>
        public static Color[] OfTexture(Texture2D texture, out int width, out int height)
        {
            width = texture.Width;
            height = texture.Height;
            Color[] pixels;
            if (texture.Format == SurfaceFormat.Dxt5)
            {
                var packed = new byte[Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16];
                texture.GetData(0, null, packed, 0, packed.Length);
                pixels = TextureUtils.DXT5Decompress(packed, width, height);
            }
            else if (texture.Format == SurfaceFormat.Color)
            {
                pixels = new Color[width * height];
                texture.GetData(0, null, pixels, 0, pixels.Length);
            }
            else return null;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].PackedValue != 0) return pixels;
            }
            return null;
        }

        /// <summary>Pixels laid out again for a texture of another size, with nothing in the part they do not cover.</summary>
        public static Color[] Fit(Color[] data, int w, int h, int toW, int toH)
        {
            if (w == toW && h == toH) return data;
            var result = new Color[toW * toH];
            var rows = Math.Min(h, toH);
            var cols = Math.Min(w, toW);
            for (int y = 0; y < rows; y++) Array.Copy(data, y * w, result, y * toW, cols);
            return result;
        }

        /// <summary>Adds a picture's colours to a count of colours, leaving out the pixels marked in skip, if any.</summary>
        public static void Count(Color[] pixels, Dictionary<uint, int> into, bool[] skip = null)
        {
            if (pixels == null) return;
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.A < 128 || (skip != null && i < skip.Length && skip[i])) continue;
                var key = pixel.PackedValue | 0xFF000000;
                into.TryGetValue(key, out var n);
                into[key] = n + 1;
            }
        }
    }
}
