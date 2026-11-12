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

namespace LifeStages
{
    /// <summary>
    /// An elder's grey hair.
    ///
    /// The Sims has no old heads, and a head's hair and face are one picture. So the pack makes a grey copy of the
    /// picture the Sim already has, while the game runs: whatever is not the colour of the Sim's skin (hair, brows,
    /// beard) is turned silver, and the face is left. It is a guess made one pixel at a time, and will be wrong on
    /// some heads: lips and eyes go pale too, and hair close to the skin's own colour keeps some of it.
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
            var bright = Math.Max(0f, (l - sl) / sl - 0.35f);
            var score = tint * 2.5f + dark * 1.6f + bright * 1.2f;
            var t = Math.Clamp((score - 0.2f) / 0.3f, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The grey copy of a head picture's pixels.</summary>
        public static Color[] Process(Color[] pixels, Color skin)
        {
            var result = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var w = HairWeight(p, skin) * 0.9f;
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
                    Grey.SetData(Process(pixels, SkinColour(device)));
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
