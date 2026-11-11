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

        public readonly Style Style;
        private int Users;
        private Dictionary<Texture2D, Texture2D> Copies = new Dictionary<Texture2D, Texture2D>();
        private HashSet<Texture2D> Mine = new HashSet<Texture2D>();
        /// <summary>Textures whose pixels could not be had yet (still loading, say), and when to try again.</summary>
        private Dictionary<Texture2D, long> TryAgain = new Dictionary<Texture2D, long>();

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
            foreach (var copy in Mine) copy.Dispose();
            Mine.Clear();
            Copies.Clear();
            TryAgain.Clear();
        }

        public bool IsMine(Texture2D texture) => Mine.Contains(texture);

        /// <summary>
        /// The styled copy of one of the game's textures, made on first use. Sprite is where the texture came from, if
        /// it came from a sprite. The texture itself is returned when it cannot be recoloured (yet).
        /// </summary>
        public Texture2D Get(Texture2D original, DGRPSprite sprite)
        {
            if (original == null || original.IsDisposed) return original;
            if (Copies.TryGetValue(original, out var copy) && !copy.IsDisposed) return copy;
            if (TryAgain.TryGetValue(original, out var when) && Environment.TickCount64 < when) return original;

            Forget();
            try
            {
                copy = Make(original, sprite);
            }
            catch (Exception e)
            {
                Console.WriteLine("Create a Style: could not recolour a texture: " + e.Message);
                copy = null;
                TryAgain[original] = long.MaxValue; //not one to keep trying
                return original;
            }
            if (copy == null)
            {
                TryAgain[original] = Environment.TickCount64 + 500;
                return original;
            }
            TryAgain.Remove(original);
            Copies[original] = copy;
            if (copy != original) Mine.Add(copy); //(it is the texture itself when the style changes nothing in it)
            return copy;
        }

        /// <summary>The engine throws its textures away when they go unused, and makes them anew: drop copies of those.</summary>
        private void Forget()
        {
            if (Copies.Count == 0 && TryAgain.Count == 0) return;
            foreach (var gone in Copies.Keys.Where(x => x.IsDisposed).ToList())
            {
                var copy = Copies[gone];
                Copies.Remove(gone);
                if (Mine.Remove(copy)) copy.Dispose();
            }
            foreach (var gone in TryAgain.Keys.Where(x => x.IsDisposed).ToList()) TryAgain.Remove(gone);
        }

        private Texture2D Make(Texture2D original, DGRPSprite sprite)
        {
            if (original.Format != SurfaceFormat.Color && original.Format != SurfaceFormat.Dxt5) return original;
            var pixels = (sprite != null) ? Pixels.OfSprite(sprite, out var w, out var h) : Pixels.OfTexture(original, out w, out h);
            if (pixels == null) return null;

            var data = new Color[pixels.Length];
            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                data[i] = Style.Apply(pixels[i]);
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
        /// <summary>
        /// A sprite's pixels, from its file. The engine does not keep them once they are on the graphics card, so this
        /// decodes the sprite again. Null if it has none, or it is of the old kind (SPR), which this pack leaves alone.
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
        /// A texture's pixels, read back from the graphics card: for a remade model's own texture, which comes from an
        /// image file instead of a sprite. Null when it has nothing in it, which is how a texture still loading looks.
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
            else
            {
                pixels = new Color[width * height];
                texture.GetData(0, null, pixels, 0, pixels.Length);
            }
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

        /// <summary>Adds a sprite's colours to a count of colours.</summary>
        public static void Count(Color[] pixels, Dictionary<uint, int> into)
        {
            if (pixels == null) return;
            foreach (var pixel in pixels)
            {
                if (pixel.A < 128) continue;
                var key = pixel.PackedValue | 0xFF000000;
                into.TryGetValue(key, out var n);
                into[key] = n + 1;
            }
        }
    }
}
