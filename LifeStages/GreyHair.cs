/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeStages
{
    /// <summary>
    /// An elder's grey hair.
    ///
    /// The Sims has no old heads, and a head's hair and face are one picture. So the pack makes a grey copy of the
    /// picture the Sim already has, while the game runs. Hair is found as what is not the colour of the Sim's skin,
    /// and is turned silver; the face is left. Eyes, lips and brows are not the skin's colour either, but they are
    /// small, so only large patches are taken for hair: each patch is measured, and one smaller than KeepShare of the
    /// head is left as it was. So brows, and a small moustache, keep their colour. The skin's colour is taken from
    /// the Sim's hand and then from the face itself, since the two need not match.
    ///
    /// Nothing is written anywhere. A head is given the grey picture by adding Marker and the skin colour to its
    /// picture's name, which only this pack's patch on the game's texture lookup understands. The game rebuilds a
    /// head from the Sim's own file whenever a lot loads, so a save never holds such a name.
    /// </summary>
    public static class GreyHair
    {
        public const string Marker = "~grey";

        /// <summary>The name of the grey copy of a head picture.</summary>
        public static string NameFor(string texture, string skin) => texture + Marker + skin;

        /// <summary>Takes a grey copy's name apart. False for any other name.</summary>
        public static bool TryParse(string name, out string texture, out string skin)
        {
            texture = skin = null;
            var at = name?.LastIndexOf(Marker, StringComparison.Ordinal) ?? -1;
            if (at <= 0) return false;
            texture = name.Substring(0, at);
            skin = name.Substring(at + Marker.Length);
            return true;
        }

        /// <summary>Roughly the game's three skin colours, for when a Sim's hands cannot be looked at.</summary>
        public static Color FallbackSkin(string skin)
        {
            switch (skin)
            {
                case "drk": return new Color(122, 82, 60);
                case "med": return new Color(190, 140, 104);
                default: return new Color(232, 188, 160);
            }
        }

        private static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;

        /// <summary>
        /// How far from skin a colour is, 0 (skin) to 1 (not skin). Skin in shade or light keeps its tint, so the
        /// colour is compared with the skin colour brought to the same brightness; and much darker or much lighter
        /// than skin is taken for hair whatever its tint.
        /// </summary>
        public static float HairWeight(Color pixel, Color skin)
        {
            float r = pixel.R / 255f, g = pixel.G / 255f, b = pixel.B / 255f;
            float sr = skin.R / 255f, sg = skin.G / 255f, sb = skin.B / 255f;
            var l = Luma(r, g, b);
            var sl = Math.Max(0.05f, Luma(sr, sg, sb));
            var scale = l / sl;
            var tint = Math.Max(Math.Abs(r - sr * scale), Math.Max(Math.Abs(g - sg * scale), Math.Abs(b - sb * scale))) / Math.Max(l, 0.08f);
            var dark = Math.Max(0f, (sl - l) / sl - 0.3f);
            //only far lighter than skin: light on skin, the shine on a cheek, must not count
            var bright = Math.Max(0f, (l - sl) / sl - 0.6f);
            var score = tint * 2.5f + dark * 1.6f + bright * 1.2f;
            var t = Math.Clamp((score - 0.2f) / 0.3f, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>A patch of hair-coloured pixels smaller than this share of the head is not hair: an eye, lips, a brow.</summary>
        public const float KeepShare = 0.012f;

        /// <summary>How far, in pixels, the soft edge of a patch of hair reaches.</summary>
        public const int EdgeReach = 2;

        /// <summary>
        /// The skin's colour as the face shows it. The hand's colour is only a guess: a hand's picture and a face's
        /// need not match, and a guess a little off would take the whole face for hair. So the head's pixels are
        /// sorted by their tint, which shade and light do not change, into groups. Of the large groups, the skin is
        /// the one nearest the guess in tint and brightness, and its colour is the average of its pixels. That is
        /// then refined: the pixels it takes for skin are averaged, twice over.
        /// </summary>
        public static Color FaceSkin(Color[] pixels, Color guess)
        {
            const float Step = 0.025f;
            const int Bins = 40; //a tint's share of red or green is under one
            var count = new int[Bins * Bins];
            var sums = new long[Bins * Bins, 3];
            var opaque = 0;
            foreach (var p in pixels)
            {
                if (p.A < 128) continue;
                opaque++;
                var total = p.R + p.G + p.B;
                if (total < 60) continue; //too dark to have a tint
                int bin = Math.Min(Bins - 1, (int)(p.R / (float)total / Step)) * Bins + Math.Min(Bins - 1, (int)(p.G / (float)total / Step));
                count[bin]++;
                sums[bin, 0] += p.R; sums[bin, 1] += p.G; sums[bin, 2] += p.B;
            }

            float Chroma(Color c, out float cg) { float t = Math.Max(1, c.R + c.G + c.B); cg = c.G / t; return c.R / t; }
            var gr = Chroma(guess, out var gg);
            var gl = Luma(guess.R / 255f, guess.G / 255f, guess.B / 255f);
            var skin = guess;
            var best = float.MaxValue;
            for (int r = 0; r < Bins; r++)
            {
                for (int g = 0; g < Bins; g++)
                {
                    // a group is a tint and those either side of it
                    long n = 0, sr = 0, sg = 0, sb = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dg = -1; dg <= 1; dg++)
                        {
                            int rr = r + dr, gg2 = g + dg;
                            if (rr < 0 || gg2 < 0 || rr >= Bins || gg2 >= Bins) continue;
                            var bin = rr * Bins + gg2;
                            n += count[bin]; sr += sums[bin, 0]; sg += sums[bin, 1]; sb += sums[bin, 2];
                        }
                    if (count[r * Bins + g] == 0 || n < opaque * 0.1f) continue;
                    var c = new Color((int)(sr / n), (int)(sg / n), (int)(sb / n));
                    var cr = Chroma(c, out var cg);
                    var distance = (float)Math.Sqrt((cr - gr) * (cr - gr) + (cg - gg) * (cg - gg)) + 0.5f * Math.Abs(Luma(c.R / 255f, c.G / 255f, c.B / 255f) - gl);
                    if (distance < best) { best = distance; skin = c; }
                }
            }

            for (int round = 0; round < 2; round++)
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (var p in pixels)
                {
                    if (p.A < 128 || HairWeight(p, skin) >= 0.25f) continue;
                    r += p.R; g += p.G; b += p.B; n++;
                }
                if (n < Math.Max(1, opaque * 0.08f)) break;
                skin = new Color((int)(r / n), (int)(g / n), (int)(b / n));
            }
            return skin;
        }

        /// <summary>How much each pixel of a head picture is hair, 0 to 1, with eyes, lips and other small patches left out.</summary>
        public static float[] HairMap(Color[] pixels, int width, int height, Color skin)
        {
            var count = width * height;
            var weight = new float[count];
            var strong = new bool[count];
            var opaque = 0;
            for (int i = 0; i < count; i++)
            {
                if (pixels[i].A < 128) continue;
                opaque++;
                weight[i] = HairWeight(pixels[i], skin);
                strong[i] = weight[i] >= 0.5f;
            }

            // the patches, each found whole and measured
            var keep = new bool[count];
            var seen = new bool[count];
            var smallest = Math.Max(12, (int)(opaque * KeepShare));
            var patch = new List<int>();
            var todo = new Stack<int>();
            for (int start = 0; start < count; start++)
            {
                if (!strong[start] || seen[start]) continue;
                patch.Clear();
                todo.Push(start);
                seen[start] = true;
                while (todo.Count > 0)
                {
                    var i = todo.Pop();
                    patch.Add(i);
                    int x = i % width, y = i / width;
                    void Next(int j) { if (strong[j] && !seen[j]) { seen[j] = true; todo.Push(j); } }
                    if (x > 0) Next(i - 1);
                    if (x < width - 1) Next(i + 1);
                    if (y > 0) Next(i - width);
                    if (y < height - 1) Next(i + width);
                }
                if (patch.Count >= smallest) foreach (var i in patch) keep[i] = true;
            }

            // a patch kept takes its soft edge with it; everything else is left
            var map = new float[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    if (weight[i] <= 0) continue;
                    var near = false;
                    for (int dy = -EdgeReach; dy <= EdgeReach && !near; dy++)
                        for (int dx = -EdgeReach; dx <= EdgeReach && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            near = nx >= 0 && ny >= 0 && nx < width && ny < height && keep[ny * width + nx];
                        }
                    if (near) map[i] = weight[i];
                }
            }
            return map;
        }

        /// <summary>The grey copy of a head picture's pixels, a row at a time from the top.</summary>
        /// <param name="handSkin">The skin's colour as the Sim's hand shows it: a first guess.</param>
        public static Color[] Process(Color[] pixels, int width, int height, Color handSkin)
        {
            if (pixels.Length != width * height) throw new ArgumentException("the picture is not " + width + " by " + height);
            var map = HairMap(pixels, width, height, FaceSkin(pixels, handSkin));
            var result = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var w = map[i] * 0.9f;
                //silver: dark hair is lifted a long way, fair hair a little, and the shading in it is kept
                var grey = Luma(p.R / 255f, p.G / 255f, p.B / 255f) * 0.5f + 0.42f;
                result[i] = new Color(
                    (byte)Math.Clamp((p.R / 255f * (1 - w) + grey * 0.97f * w) * 255f, 0f, 255f),
                    (byte)Math.Clamp((p.G / 255f * (1 - w) + grey * 0.98f * w) * 255f, 0f, 255f),
                    (byte)Math.Clamp((p.B / 255f * (1 - w) + grey * w) * 255f, 0f, 255f),
                    p.A);
            }
            return result;
        }

        /// <summary>The average of a picture's pixels, leaving out the empty and the black: a hand's is the Sim's skin.</summary>
        public static Color? Average(Color[] pixels)
        {
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var p in pixels)
            {
                if (p.A < 128 || p.R + p.G + p.B < 60) continue;
                r += p.R; g += p.G; b += p.B; n++;
            }
            if (n == 0) return null;
            return new Color((int)(r / n), (int)(g / n), (int)(b / n));
        }

        // ---- in the game ----

        private static readonly Dictionary<string, ITextureRef> Made = new Dictionary<string, ITextureRef>();

        /// <summary>
        /// For the patch on the game's texture lookup: the grey copy a name asks for, or null if the name is not one
        /// of ours or the picture it is a copy of does not exist.
        /// </summary>
        public static ITextureRef Find(string name, Func<string, ITextureRef> lookup)
        {
            if (!TryParse(name, out var texture, out var skin)) return null;
            lock (Made)
            {
                if (Made.TryGetValue(name, out var made)) return made;
                var original = lookup(texture);
                if (original == null) return null;
                made = new GreyTextureRef(original, skin, lookup);
                Made[name] = made;
                return made;
            }
        }

        private class GreyTextureRef : ITextureRef
        {
            private readonly ITextureRef Original;
            private readonly string Skin;
            private readonly Func<string, ITextureRef> Lookup;
            private Texture2D Grey;
            private bool Failed;

            public GreyTextureRef(ITextureRef original, string skin, Func<string, ITextureRef> lookup)
            {
                Original = original;
                Skin = skin;
                Lookup = lookup;
            }

            public string ReplacePath { get; set; }

            public TexBitmap GetImage() => Original.GetImage();

            public Texture2D Get(GraphicsDevice device)
            {
                if (Grey != null && !Grey.IsDisposed) return Grey;
                var source = Original.Get(device);
                if (Failed || source == null) return source;
                try
                {
                    if (source.Format != SurfaceFormat.Color) throw new Exception("its picture is stored as " + source.Format);
                    var pixels = new Color[source.Width * source.Height];
                    source.GetData(pixels);
                    Grey = new Texture2D(device, source.Width, source.Height, false, SurfaceFormat.Color);
                    Grey.SetData(Process(pixels, source.Width, source.Height, SkinColour(device)));
                    return Grey;
                }
                catch (Exception e)
                {
                    //the head keeps its own hair
                    Failed = true;
                    Log.Write("Could not make a grey head: " + e.Message);
                    return source;
                }
            }

            private Color SkinColour(GraphicsDevice device)
            {
                try
                {
                    var hand = Lookup("huao" + Skin)?.Get(device);
                    if (hand != null && hand.Format == SurfaceFormat.Color)
                    {
                        var pixels = new Color[hand.Width * hand.Height];
                        hand.GetData(pixels);
                        var average = Average(pixels);
                        if (average != null) return average.Value;
                    }
                }
                catch
                {
                    //the fallback will do
                }
                return FallbackSkin(Skin);
            }
        }
    }
}
