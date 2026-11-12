/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Driveways
{
    /// <summary>
    /// Paints the driveway with one of the game's own floor tiles.
    ///
    /// The pack's object file gives the driveway its shape: each tile's sprites, with their depth, drawn flat in one
    /// plain colour. The pack ships no texture for it. When the game runs, this takes a floor tile out of the
    /// player's own copy of The Sims (the pavement beside the road) and makes a second set of sprites, the same shape
    /// and depth, with every pixel coloured from the place on the floor tile that it lies over. The driveway is then
    /// drawn with those. Nothing of the game's is copied into the pack or written to disk.
    ///
    /// This file uses only the engine's file formats, so the generator can run it too, to check it.
    /// </summary>
    public static class FloorDress
    {
        // --- the engine's sprite space -> object space transform, for the near zoom and one of the four views (see DGRP3DMesh) ---
        private const float ZOff = -55f;
        private const float DepthRange = 110.851251f / 0.39f;
        private static readonly Matrix[] ToObject = Enumerable.Range(0, 4).Select(view =>
            Matrix.CreateTranslation(new Vector3(-72, -344, ZOff))
            * Matrix.CreateScale((1f / 128) * 1.43f)
            * Matrix.CreateScale(1, -1, 1)
            * Matrix.CreateRotationX((float)Math.PI / -6)
            * Matrix.CreateRotationY(((float)Math.PI / 4) * (1 + view * 2))).ToArray();
        private static readonly Matrix FromObject0 = Matrix.Invert(ToObject[0]);

        /// <summary>The dressed drawing groups are numbered this much above the ones they stand in for.</summary>
        public const ushort IDOffset = 1000;

        /// <summary>
        /// A floor tile to paint with: the pixels of its near sprite (a diamond, a tile seen from above at an angle),
        /// and the palette they are drawn from.
        /// </summary>
        public class Tile
        {
            public Color[] Pixels;
            public int Width, Height;
            public Color[] Palette;

            /// <summary>The colour of the floor at a place on the tile: x to the east and z to the south, each from 0 to 1.</summary>
            public Color At(float x, float z)
            {
                //where that place on the ground is drawn, by the same camera the sprites are drawn with: the tile's
                //four corners are the diamond's four points, so the box round them is the sprite
                var p = Vector3.Transform(new Vector3(x - 0.5f, 0, z - 0.5f), FromObject0);
                var u = (p.X - GroundLeft) / (GroundRight - GroundLeft);
                var v = (p.Y - GroundTop) / (GroundBottom - GroundTop);
                int px = Math.Clamp((int)(u * Width), 0, Width - 1), py = Math.Clamp((int)(v * Height), 0, Height - 1);
                var colour = Pixels[py * Width + px];
                if (colour.A != 0) return colour;
                //just off the edge of the diamond: step towards its middle until there is floor
                for (int i = 1; i <= 8; i++)
                {
                    int qx = px + (Width / 2 - px) * i / 8, qy = py + (Height / 2 - py) * i / 8;
                    colour = Pixels[qy * Width + qx];
                    if (colour.A != 0) return colour;
                }
                return colour;
            }
        }

        //the box round one tile of ground on the near canvas
        private static readonly float GroundLeft, GroundRight, GroundTop, GroundBottom;
        static FloorDress()
        {
            var corners = (from x in new[] { -0.5f, 0.5f } from z in new[] { -0.5f, 0.5f } select Vector3.Transform(new Vector3(x, 0, z), FromObject0)).ToList();
            GroundLeft = corners.Min(c => c.X); GroundRight = corners.Max(c => c.X);
            GroundTop = corners.Min(c => c.Y); GroundBottom = corners.Max(c => c.Y);
        }

        /// <summary>
        /// Make dressed copies of these drawing groups of an object file: the same sprites, each pixel coloured from
        /// the floor tile. They come back in a file of their own, held in memory, each numbered IDOffset above its
        /// original.
        /// </summary>
        public static IffFile Build(IffFile source, IEnumerable<ushort> drawGroups, Tile tile)
        {
            //the floor's own palette, with one entry kept for "nothing here"
            var palette = (Color[])tile.Palette.Clone();
            if (palette.Length != 256) Array.Resize(ref palette, 256);
            const byte Nothing = 255;
            var indexOf = new Dictionary<uint, byte>();
            for (int i = 0; i < 256; i++)
                if (i != Nothing && !indexOf.ContainsKey(palette[i].PackedValue | 0xFF000000)) indexOf[palette[i].PackedValue | 0xFF000000] = (byte)i;
            byte Nearest(Color c)
            {
                if (indexOf.TryGetValue(c.PackedValue | 0xFF000000, out var exact)) return exact;
                int best = 0, bestD = int.MaxValue;
                for (int i = 0; i < 256; i++)
                {
                    if (i == Nothing) continue;
                    int dr = palette[i].R - c.R, dg = palette[i].G - c.G, db = palette[i].B - c.B, d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = i; }
                }
                indexOf[c.PackedValue | 0xFF000000] = (byte)best;
                return (byte)best;
            }

            // A sprite made in memory is given its palette indices by whatever "quantizer" is set (the game sets none;
            // the generator sets its own). Ours hands over the indices worked out for the frame being made.
            byte[] current = null;
            var quantizer = SPR2FrameEncoder.QuantizeFrame;
            var ts1 = IffFile.TargetTS1;
            SPR2FrameEncoder.QuantizeFrame = (SPR2Frame frame, out byte[] bytes) => { bytes = current; return palette; };
            try
            {
            var result = new IffFile();
            var palt = new PALT() { ChunkID = 1, ChunkLabel = "", ChunkProcessed = true, Colors = palette };
            result.AddChunk(palt);

            //each sprite is done once, however many drawing groups use it
            var sprites = new Dictionary<ushort, SPR2>();

            foreach (var id in drawGroups.Distinct())
            {
                var original = source.Get<DGRP>(id);
                if (original == null) continue;
                var dressed = new DGRP() { ChunkID = (ushort)(id + IDOffset), ChunkLabel = original.ChunkLabel, ChunkProcessed = true };
                var images = new List<DGRPImage>();
                foreach (var image in original.Images)
                {
                    int view = image.Direction == 0x01 ? 0 : image.Direction == 0x04 ? 1 : image.Direction == 0x10 ? 2 : 3;
                    float scale = 1f / (1 << (int)(3 - image.Zoom)); //near 1, medium 1/2, far 1/4
                    var parts = new List<DGRPSprite>();
                    foreach (var sprite in image.Sprites)
                    {
                        var spriteID = (ushort)sprite.SpriteID;
                        var from = source.Get<SPR2>(spriteID);
                        if (!sprites.TryGetValue(spriteID, out var to))
                        {
                            to = new SPR2() { ChunkID = spriteID, ChunkLabel = from.ChunkLabel, ChunkProcessed = true, Frames = new SPR2Frame[from.Frames.Length] };
                            result.AddChunk(to);
                            sprites[spriteID] = to;
                        }
                        var index = (int)sprite.SpriteFrameIndex;
                        if (to.Frames[index] == null)
                        {
                            var frame = from.Frames[index];
                            frame.DecodeIfRequired(true);
                            frame.DecodeIfRequired(false);
                            int w = frame.Width, h = frame.Height;
                            var px = new Color[w * h]; var z = new byte[w * h]; var pal = new byte[w * h];
                            //where the sprite sits on the near canvas (as the generator placed it)
                            float left = sprite.SpriteOffset.X, top = sprite.SpriteOffset.Y + 348 * scale - h;
                            for (int y = 0; y < h; y++)
                                for (int x = 0; x < w; x++)
                                {
                                    int i = y * w + x;
                                    z[i] = frame.ZBufferData[i];
                                    if (frame.PixelData[i].A == 0) { px[i] = Color.Transparent; pal[i] = Nothing; continue; }
                                    //the point of the driveway this pixel shows, from its depth: then the floor there
                                    var point = Vector3.Transform(new Vector3((left + x + 0.5f) / scale - 0.5f + 72, (top + y + 0.5f) / scale - 0.5f, (1f - z[i] / 255f) * DepthRange), ToObject[view]);
                                    var colour = tile.At(Math.Clamp(point.X + 0.5f, 0, 0.999f), Math.Clamp(point.Z + 0.5f, 0, 0.999f));
                                    pal[i] = Nearest(colour);
                                    px[i] = new Color(palette[pal[i]].R, palette[pal[i]].G, palette[pal[i]].B, (byte)255);
                                }
                            var made = new SPR2Frame(to);
                            current = pal;
                            made.SetData(px, z, new Rectangle(0, 0, w, h));
                            made.SetPalt(palt);
                            to.Frames[index] = made;
                        }
                        parts.Add(new DGRPSprite(dressed) { SpriteID = sprite.SpriteID, SpriteFrameIndex = sprite.SpriteFrameIndex, SpriteOffset = sprite.SpriteOffset, ObjectOffset = sprite.ObjectOffset, Flags = sprite.Flags });
                    }
                    images.Add(new DGRPImage(dressed) { Direction = image.Direction, Zoom = image.Zoom, Sprites = parts.ToArray() });
                }
                dressed.Images = images.ToArray();
                result.AddChunk(dressed);
            }
            //(a sprite with frames no drawing group asked for: give them an empty frame, so the file can be written)
            foreach (var sprite in sprites.Values)
                for (int i = 0; i < sprite.Frames.Length; i++)
                    if (sprite.Frames[i] == null)
                    {
                        var empty = new SPR2Frame(sprite);
                        current = new byte[] { Nothing };
                        empty.SetData(new Color[1], new byte[] { 255 }, new Rectangle(0, 0, 1, 1));
                        empty.SetPalt(palt);
                        sprite.Frames[i] = empty;
                    }

            // Sprites made in memory are lost when the game frees their pixels to save room: it can only get pixels
            // back from a sprite's packed form. So pack the file and read it again, as if it had come from disk.
            IffFile.TargetTS1 = true;
            using (var stream = new MemoryStream())
            {
                result.Write(stream);
                stream.Position = 0;
                var packed = new IffFile();
                packed.Read(stream);
                packed.SetFilename("driveways-dressed.iff");
                return packed;
            }
            }
            finally
            {
                SPR2FrameEncoder.QuantizeFrame = quantizer;
                IffFile.TargetTS1 = ts1;
            }
        }
    }
}
