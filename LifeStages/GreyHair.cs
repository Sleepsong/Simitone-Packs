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
    /// A head's hair and face are one picture. So the pack makes a grey copy of the picture the Sim already has,
    /// while the game runs, with only the hair turned silver. Finding the hair takes four steps:
    ///
    ///  1. The skin's colour. The Sim's hand gives a first guess, and the face itself the answer (FaceSkin). A head
    ///     with no large patch of anything like the guess (a mask, paint) is left as it is.
    ///  2. What is not skin, pixel by pixel (HairWeight), less skin that is only shaded or flushed, as inside an ear
    ///     (SkinLike), and of that, what could be hair at all (HairLike): black, browns, reds, blonds and greys, but
    ///     not the blue, green or purple of a hat, nor bright orange or red.
    ///  3. Patches. Eyes, lips, brows and the inside of an ear are small, so only patches larger than KeepShare of
    ///     the head are hair. Brows and a small moustache keep their colour.
    ///  4. A check. When what was found is more than MostHair of the head, the face was taken for hair, and the head
    ///     is left as it is rather than turned grey all over.
    ///
    /// Some of the game's adult heads are old already, with grey or white hair (LooksGrey). Those are the elders'
    /// heads in Create a Sim, and are not given to a child who grows up.
    ///
    /// Nothing is written anywhere. A head is given the grey picture by adding Marker and the skin colour to its
    /// picture's name, which only this pack's patch on the game's texture lookup understands, and which the engine
    /// otherwise treats as the picture's own name. The game rebuilds a
    /// head from the Sim's own file whenever a lot loads, so a save never holds such a name.
    /// </summary>
    public static class GreyHair
    {
        /// <summary>
        /// What marks a grey copy's name. The engine finds which part of a head (the head, a hat, glasses) takes the
        /// head's picture by comparing names with the skin colours (lgt, med, drk) taken out, so the marker is made of
        /// nothing else: with them taken out, a grey copy's name is the picture's own, and goes to the same part.
        /// </summary>
        public const string Marker = "drkmedlgtmeddrk";

        /// <summary>A patch of hair-coloured pixels smaller than this share of the head is not hair: an eye, lips, a brow.</summary>
        public const float KeepShare = 0.012f;

        /// <summary>More hair than this share of a head means the face was taken for hair: the head is left alone.</summary>
        public const float MostHair = 0.6f;

        /// <summary>How far, in pixels, the soft edge of a patch of hair reaches.</summary>
        public const int EdgeReach = 1;

        /// <summary>A skin colour found on the face must be at least this near the hand's, in tint and brightness.</summary>
        public const float FarthestSkin = 0.2f;

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

        /// <summary>Hue in degrees, saturation and value, 0 to 1.</summary>
        public static void Hsv(Color c, out float hue, out float saturation, out float value)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            value = max;
            saturation = max <= 0 ? 0 : d / max;
            if (d <= 0) hue = 0;
            else if (max == r) hue = 60f * (((g - b) / d + 6f) % 6f);
            else if (max == g) hue = 60f * ((b - r) / d + 2f);
            else hue = 60f * ((r - g) / d + 4f);
        }

        /// <summary>
        /// Whether a colour could be hair: anything greyish (black, grey, white, ash), or a red, brown or blond
        /// that is not as bright and strong as paint. Not blue, green, purple or pink, which on a head are a hat.
        /// </summary>
        public static bool HairLike(Color c)
        {
            Hsv(c, out var hue, out var s, out var v);
            if (s < 0.3f) return true;
            var warm = hue >= 340f || hue <= 65f;
            if (!warm) return false;
            return !(s > 0.82f && v > 0.45f);
        }

        /// <summary>
        /// Whether a colour is the skin's own, only shaded or flushed: the inside of an ear, a cheek, the skin under
        /// the chin. Its hue is the skin's, it is a little stronger at most, and not much darker. Hair of a colour
        /// near the skin's is darker or stronger than that.
        /// </summary>
        public static bool SkinLike(Color c, Color skin)
        {
            Hsv(c, out var h, out var s, out var v);
            Hsv(skin, out var sh, out var ss, out var sv);
            var hueGap = Math.Abs(h - sh);
            if (hueGap > 180) hueGap = 360 - hueGap;
            return hueGap <= 12f && Math.Abs(s - ss) <= 0.2f && v >= sv * 0.8f;
        }

        /// <summary>
        /// How far from skin a colour is, 0 (skin) to 1 (not skin). Skin in shade or light keeps its tint, so the
        /// colour is compared with the skin colour brought to the same brightness; and far darker or far lighter
        /// than skin is taken for hair whatever its tint. Skin only shaded or flushed is never hair (SkinLike).
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
            var bright = Math.Max(0f, (l - sl) / sl - 0.6f);
            var score = tint * 2.5f + dark * 1.6f + bright * 1.2f;
            return Smooth(0.2f, 0.5f, score);
        }

        private static float Smooth(float from, float to, float x)
        {
            var t = Math.Clamp((x - from) / (to - from), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The skin's colour as the face shows it, or the guess if the face has none like it (see TryFaceSkin).</summary>
        public static Color FaceSkin(Color[] pixels, Color guess) => TryFaceSkin(pixels, guess, out var skin) ? skin : guess;

        /// <summary>
        /// The skin's colour as the face shows it. The hand's colour is only a guess: a hand's picture and a face's
        /// need not match, and a guess a little off would take the whole face for hair. So the head's pixels are
        /// sorted by their tint, which shade and light do not change, into groups. Of the large groups, the skin is
        /// the one nearest the guess in tint and brightness, and its colour is the average of its pixels. That is
        /// then refined: the pixels it takes for skin are averaged, twice over. False if no large group is near the
        /// guess: a head painted or masked, whose skin cannot be told.
        /// </summary>
        public static bool TryFaceSkin(Color[] pixels, Color guess, out Color skin)
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
            skin = guess;
            var best = float.MaxValue;
            for (int r = 0; r < Bins; r++)
            {
                for (int g = 0; g < Bins; g++)
                {
                    // a group is a tint more common than those either side of it, with them: a peak. a tint between
                    // two peaks (an ear, between skin and hair) would be a blend of both
                    var centre = count[r * Bins + g];
                    var peak = centre > 0;
                    for (int dr = -1; dr <= 1 && peak; dr++)
                        for (int dg = -1; dg <= 1 && peak; dg++)
                        {
                            int rr = r + dr, gg2 = g + dg;
                            if ((dr != 0 || dg != 0) && rr >= 0 && gg2 >= 0 && rr < Bins && gg2 < Bins && count[rr * Bins + gg2] > centre) peak = false;
                        }
                    if (!peak) continue;
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
            if (best > FarthestSkin) { skin = guess; return false; }

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
            return true;
        }

        /// <summary>How much each pixel of a head picture is hair, 0 to 1, with eyes, lips, hats and other small or unlikely patches left out.</summary>
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
                if (!HairLike(pixels[i]) || SkinLike(pixels[i], skin)) continue;
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

            // a patch kept takes its soft edge with it, one pixel wide; everything else is left. what is only
            // half unlike skin counts for little, so skin in shade beside the hair (an ear) is not dimmed
            var map = new float[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    if (weight[i] <= 0) continue;
                    var near = keep[i];
                    for (int dy = -EdgeReach; dy <= EdgeReach && !near; dy++)
                        for (int dx = -EdgeReach; dx <= EdgeReach && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            near = nx >= 0 && ny >= 0 && nx < width && ny < height && keep[ny * width + nx];
                        }
                    if (near) map[i] = keep[i] ? weight[i] : Smooth(0.3f, 0.7f, weight[i]);
                }
            }
            return map;
        }

        /// <summary>What share of a head's picture is hair, by its map.</summary>
        public static float HairShare(Color[] pixels, float[] map)
        {
            int opaque = 0, hair = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A < 128) continue;
                opaque++;
                if (map[i] >= 0.5f) hair++;
            }
            return opaque == 0 ? 0 : hair / (float)opaque;
        }

        /// <summary>
        /// The grey copy of a head picture's pixels, a row at a time from the top. A head whose skin or hair cannot
        /// be told is given back as it was.
        /// </summary>
        /// <param name="handSkin">The skin's colour as the Sim's hand shows it: a first guess.</param>
        public static Color[] Process(Color[] pixels, int width, int height, Color handSkin)
        {
            if (pixels.Length != width * height) throw new ArgumentException("the picture is not " + width + " by " + height);
            if (!TryFaceSkin(pixels, handSkin, out var skin)) return (Color[])pixels.Clone();
            var map = HairMap(pixels, width, height, skin);
            if (HairShare(pixels, map) > MostHair) return (Color[])pixels.Clone();
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

        /// <summary>
        /// Whether a head is old already: most of its hair grey or white. Those heads are elders' in Create a Sim,
        /// and are not given to a child who grows up. A head with little hair, or hair that cannot be told, is not.
        /// </summary>
        public static bool LooksGrey(Color[] pixels, int width, int height, Color handSkin)
        {
            if (pixels.Length != width * height) return false;
            if (!TryFaceSkin(pixels, handSkin, out var skin)) return false;
            var map = HairMap(pixels, width, height, skin);
            int opaque = 0, hair = 0, grey = 0;
            float value = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A < 128) continue;
                opaque++;
                if (map[i] < 0.5f) continue;
                hair++;
                Hsv(pixels[i], out _, out var s, out var v);
                value += v;
                if (s < 0.2f) grey++;
            }
            if (opaque == 0 || hair < opaque * 0.04f || hair > opaque * MostHair) return false;
            //grey or white, not black: black hair is as colourless as grey
            return grey >= hair * 0.75f && value / hair >= 0.42f;
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

        private static Func<string, ITextureRef> GameLookup => name => Content.Get()?.AvatarTextures?.Get(name);

        private static readonly Dictionary<string, bool> OldAlready = new Dictionary<string, bool>();

        /// <summary>
        /// Whether one of the game's head pictures is old already (LooksGrey), worked out once. Needs the game's
        /// graphics, so false where there are none.
        /// </summary>
        public static bool IsOldHead(string texture, string skin)
        {
            if (string.IsNullOrEmpty(texture)) return false;
            lock (OldAlready)
            {
                if (OldAlready.TryGetValue(texture, out var known)) return known;
            }
            var result = false;
            try
            {
                var device = FSO.Client.GameFacade.GraphicsDevice;
                var tex = device == null ? null : GameLookup(texture)?.Get(device);
                if (tex != null && tex.Format == SurfaceFormat.Color)
                {
                    var pixels = new Color[tex.Width * tex.Height];
                    tex.GetData(pixels);
                    result = LooksGrey(pixels, tex.Width, tex.Height, HandColour(skin, GameLookup, device));
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not look at the head " + texture + ": " + e.Message);
            }
            lock (OldAlready) OldAlready[texture] = result;
            return result;
        }

        /// <summary>The skin's colour as the Sim's hand shows it, or the game's usual one for the skin colour.</summary>
        private static Color HandColour(string skin, Func<string, ITextureRef> lookup, GraphicsDevice device)
        {
            try
            {
                var hand = lookup("huao" + skin)?.Get(device);
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
            return FallbackSkin(skin);
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
                    Grey.SetData(Process(pixels, source.Width, source.Height, HandColour(Skin, Lookup, device)));
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
        }
    }
}
