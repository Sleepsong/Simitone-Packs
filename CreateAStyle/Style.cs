/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CreateAStyle
{
    /// <summary>
    /// One recolourable part of an object. The Sims' art has no masks saying which pixels are the fabric and which
    /// the wood, so a part is a group of colours instead: a stretch of the colour wheel (all the reds of a sofa's
    /// cushions), or the colours with no hue to speak of (white, grey, black).
    /// </summary>
    public class StyleChannel
    {
        /// <summary>The greys rather than a stretch of the colour wheel.</summary>
        public bool Neutral;
        /// <summary>The stretch of the wheel, in degrees, clockwise from From to To. It can pass through 360.</summary>
        public float From, To;
        /// <summary>The hue the part is centred on.</summary>
        public float Centre;
        /// <summary>A typical colour of the part, for showing it.</summary>
        public Color Sample;
        /// <summary>How much of the object the part is, 0 to 1.</summary>
        public float Share;

        // ---- what the player chose ----

        /// <summary>The hue the part's centre becomes. The shades around it move with it.</summary>
        public float Hue;
        /// <summary>For a colour part: how strong the colours are, where 1 leaves them (0 to 2). For the greys: how
        /// much colour they are given (0 to 1), where 0 leaves them grey.</summary>
        public float Saturation;
        /// <summary>How light the part is, where 1 leaves it (0.4 to 1.6).</summary>
        public float Brightness;

        public const float MinBrightness = 0.4f, MaxBrightness = 1.6f;
        public float MaxSaturation => Neutral ? 1f : 2f;

        public void Reset()
        {
            Hue = Neutral ? 0 : Centre;
            Saturation = Neutral ? 0 : 1;
            Brightness = 1;
        }

        public bool Changed
        {
            get
            {
                if (Math.Abs(Brightness - 1) > 0.004f) return true;
                if (Neutral) return Saturation > 0.004f;
                return Math.Abs(Saturation - 1) > 0.004f || Math.Abs(ColorMath.HueDelta(Hue, Centre)) > 0.4f;
            }
        }

        /// <summary>Whether a hue is in this part's stretch of the wheel.</summary>
        public bool Contains(float hue)
        {
            if (Neutral) return false;
            var span = ColorMath.Wrap(To - From);
            if (span == 0 && To != From) return true; //the whole wheel
            return ColorMath.Wrap(hue - From) < span || (span == 0 && hue == From);
        }

        /// <summary>The part's sample colour as it comes out with the player's choices.</summary>
        public Color Result => Apply(Sample);

        public Color Apply(Color c)
        {
            ColorMath.ToHsv(c, out var h, out var s, out var v);
            if (Neutral)
            {
                h = Hue;
                s = Saturation;
            }
            else
            {
                h = ColorMath.Wrap(Hue + ColorMath.HueDelta(h, Centre));
                s = Math.Min(1, s * Saturation);
            }
            v = Math.Min(1, v * Brightness);
            return ColorMath.FromHsv(h, s, v, c.A);
        }

        public StyleChannel Clone() => (StyleChannel)MemberwiseClone();
    }

    /// <summary>An object's parts and what the player chose for each.</summary>
    public class Style
    {
        public List<StyleChannel> Channels = new List<StyleChannel>();

        /// <summary>Colours weaker than this have no hue worth trusting: they are the greys.</summary>
        public const float NeutralSaturation = 0.15f;
        /// <summary>And so are colours darker than this.</summary>
        public const float NeutralValue = 0.08f;

        public static bool IsNeutral(float s, float v) => s < NeutralSaturation || v < NeutralValue;

        /// <summary>Nothing is changed: the object looks as it was made.</summary>
        public bool IsDefault => !Channels.Any(x => x.Changed);

        private Dictionary<uint, Color> Done = new Dictionary<uint, Color>();
        private string _Key;

        /// <summary>Call after changing a choice: forgets what was worked out from the old ones.</summary>
        public void Touch()
        {
            Done.Clear();
            _Key = null;
        }

        /// <summary>The same for any two styles that recolour the same way. Styled textures are shared by it.</summary>
        public string Key
        {
            get
            {
                if (_Key == null)
                {
                    var sb = new StringBuilder();
                    foreach (var c in Channels)
                    {
                        if (!c.Changed) continue;
                        sb.Append(c.Neutral ? 'n' : 'c');
                        foreach (var f in new[] { c.From, c.To, c.Centre, c.Hue, c.Saturation, c.Brightness })
                        {
                            sb.Append(Math.Round(f, 2).ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                        }
                        sb.Append(';');
                    }
                    _Key = sb.ToString();
                }
                return _Key;
            }
        }

        /// <summary>A colour of the object, as the style makes it. Transparency is kept.</summary>
        public Color Apply(Color c)
        {
            if (c.A < 8) return c;
            if (Done.TryGetValue(c.PackedValue, out var done)) return done;
            var result = c;
            var channel = ChannelOf(c);
            if (channel != null && channel.Changed) result = channel.Apply(c);
            //the art is a few hundred colours at most, so remembering each one makes a whole sprite a row of lookups
            if (Done.Count < 65536) Done[c.PackedValue] = result;
            return result;
        }

        /// <summary>The part a colour belongs to, or null if it is in none of them.</summary>
        public StyleChannel ChannelOf(Color c)
        {
            ColorMath.ToHsv(c, out var h, out var s, out var v);
            if (IsNeutral(s, v)) return Channels.FirstOrDefault(x => x.Neutral);
            foreach (var channel in Channels)
            {
                if (channel.Contains(h)) return channel;
            }
            return null;
        }

        public Style Clone()
        {
            return new Style { Channels = Channels.Select(x => x.Clone()).ToList() };
        }

        // ---- saving ----

        public void Write(BinaryWriter io)
        {
            io.Write((byte)Channels.Count);
            foreach (var c in Channels)
            {
                io.Write(c.Neutral);
                io.Write(c.From);
                io.Write(c.To);
                io.Write(c.Centre);
                io.Write(c.Sample.PackedValue);
                io.Write(c.Share);
                io.Write(c.Hue);
                io.Write(c.Saturation);
                io.Write(c.Brightness);
            }
        }

        public static Style Read(BinaryReader io)
        {
            var result = new Style();
            var count = io.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var c = new StyleChannel();
                c.Neutral = io.ReadBoolean();
                c.From = io.ReadSingle();
                c.To = io.ReadSingle();
                c.Centre = io.ReadSingle();
                c.Sample = new Color(io.ReadUInt32());
                c.Share = io.ReadSingle();
                c.Hue = Clean(io.ReadSingle(), 0, 360, 0);
                c.Saturation = Clean(io.ReadSingle(), 0, c.MaxSaturation, c.Neutral ? 0 : 1);
                c.Brightness = Clean(io.ReadSingle(), StyleChannel.MinBrightness, StyleChannel.MaxBrightness, 1);
                result.Channels.Add(c);
            }
            return result;
        }

        private static float Clean(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        // ---- finding an object's parts ----

        private const int Bins = 72; //5 degrees each
        /// <summary>No more colour parts than this, however many colours the object has.</summary>
        public const int MaxColourParts = 4;

        /// <summary>
        /// Works out an object's parts from its colours and how many pixels there are of each. The hues are counted
        /// around the wheel; each hump in the count is a part, and the dips between humps are where one part ends and
        /// the next begins, so every hue belongs to some part. A hump too small to matter joins its neighbour.
        /// </summary>
        public static Style Analyse(Dictionary<uint, int> colours)
        {
            var style = new Style();
            var hist = new double[Bins];
            long total = 0, neutral = 0;
            double nr = 0, ng = 0, nb = 0;
            foreach (var pair in colours)
            {
                var c = new Color(pair.Key);
                if (c.A < 128) continue;
                total += pair.Value;
                ColorMath.ToHsv(c, out var h, out var s, out var v);
                if (IsNeutral(s, v))
                {
                    neutral += pair.Value;
                    nr += c.R * (double)pair.Value; ng += c.G * (double)pair.Value; nb += c.B * (double)pair.Value;
                }
                else hist[(int)(h / 360f * Bins) % Bins] += pair.Value;
            }
            if (total == 0) return style;

            //smooth, so that one hump is not counted as several
            var smooth = new double[Bins];
            var kernel = new[] { 1, 2, 3, 2, 1 };
            for (int i = 0; i < Bins; i++)
            {
                for (int k = 0; k < kernel.Length; k++) smooth[i] += hist[(i + k - 2 + Bins) % Bins] * kernel[k];
            }

            var peaks = new List<int>();
            for (int i = 0; i < Bins; i++)
            {
                var prev = smooth[(i + Bins - 1) % Bins];
                var next = smooth[(i + 1) % Bins];
                if (smooth[i] > 0 && smooth[i] >= prev && smooth[i] > next) peaks.Add(i);
            }
            peaks = peaks.OrderByDescending(x => smooth[x]).ToList();

            //the biggest humps first, and none within 30 degrees of one already taken
            var taken = new List<int>();
            foreach (var peak in peaks)
            {
                if (taken.Count >= MaxColourParts) break;
                if (taken.Any(x => BinDistance(x, peak) < 6)) continue;
                taken.Add(peak);
            }

            var coloured = total - neutral;
            while (taken.Count > 0)
            {
                taken.Sort();
                var bounds = Boundaries(taken, hist);
                var parts = new List<StyleChannel>();
                int smallest = -1;
                double smallestCount = double.MaxValue;
                for (int i = 0; i < taken.Count; i++)
                {
                    var from = bounds[(i + taken.Count - 1) % taken.Count];
                    var to = bounds[i];
                    var part = MakePart(colours, taken.Count == 1, from, to, total, out var count);
                    parts.Add(part);
                    if (count < smallestCount) { smallestCount = count; smallest = i; }
                }
                //a part under a twentieth of the object's colour is not worth a control of its own
                if (taken.Count > 1 && smallestCount < coloured * 0.05)
                {
                    taken.RemoveAt(smallest);
                    continue;
                }
                style.Channels.AddRange(parts.Where(x => x.Share > 0).OrderByDescending(x => x.Share));
                break;
            }

            if (neutral >= total * 0.03)
            {
                var grey = new StyleChannel
                {
                    Neutral = true,
                    Sample = new Color((int)(nr / neutral), (int)(ng / neutral), (int)(nb / neutral)),
                    Share = neutral / (float)total
                };
                grey.Reset();
                style.Channels.Add(grey);
            }
            return style;
        }

        private static int BinDistance(int a, int b)
        {
            var d = Math.Abs(a - b);
            return Math.Min(d, Bins - d);
        }

        /// <summary>
        /// For each hump, the bin where its part ends: the lowest point before the next hump. The dip is looked for in
        /// a count smoothed only a little, or a big hump would spill over a small neighbour and push the dip into it.
        /// Where the dip is a stretch (often of nothing at all), the part ends in the middle of it.
        /// </summary>
        private static int[] Boundaries(List<int> peaks, double[] hist)
        {
            var light = new double[Bins];
            for (int i = 0; i < Bins; i++) light[i] = hist[(i + Bins - 1) % Bins] + hist[i] * 2 + hist[(i + 1) % Bins];

            var result = new int[peaks.Count];
            for (int i = 0; i < peaks.Count; i++)
            {
                var a = peaks[i];
                var b = peaks[(i + 1) % peaks.Count];
                var length = (b - a + Bins) % Bins;
                if (length == 0) length = Bins;
                double low = double.MaxValue;
                for (int j = 1; j < Math.Max(2, length); j++) low = Math.Min(low, light[(a + j) % Bins]);
                int first = -1, last = -1;
                for (int j = 1; j < Math.Max(2, length); j++)
                {
                    if (light[(a + j) % Bins] > low) { if (first != -1) break; continue; }
                    if (first == -1) first = j;
                    last = j;
                }
                result[i] = (a + (first + last) / 2) % Bins;
            }
            return result;
        }

        /// <summary>The part between two bins (the first not included, the second included).</summary>
        private static StyleChannel MakePart(Dictionary<uint, int> colours, bool wholeWheel, int fromBin, int toBin, long total, out double count)
        {
            var part = new StyleChannel();
            part.From = wholeWheel ? 0 : (fromBin + 1) * (360f / Bins) % 360f;
            part.To = wholeWheel ? 360 : (toBin + 1) * (360f / Bins) % 360f;
            if (!wholeWheel && part.From == part.To) part.To = ColorMath.Wrap(part.To + 0.001f);

            double r = 0, g = 0, b = 0, x = 0, y = 0;
            count = 0;
            foreach (var pair in colours)
            {
                var c = new Color(pair.Key);
                if (c.A < 128) continue;
                ColorMath.ToHsv(c, out var h, out var s, out var v);
                if (IsNeutral(s, v) || !part.Contains(h)) continue;
                double n = pair.Value;
                count += n;
                r += c.R * n; g += c.G * n; b += c.B * n;
                //the centre is where the colour is, so the strong colours count for more than the washed out ones
                x += Math.Cos(h * Math.PI / 180) * n * s;
                y += Math.Sin(h * Math.PI / 180) * n * s;
            }
            if (count > 0)
            {
                part.Sample = new Color((int)(r / count), (int)(g / count), (int)(b / count));
                part.Centre = ColorMath.Wrap((float)(Math.Atan2(y, x) * 180 / Math.PI));
                if (!part.Contains(part.Centre)) part.Centre = ColorMath.Wrap(part.From + ColorMath.Wrap(part.To - part.From) / 2);
            }
            part.Share = (float)(count / total);
            part.Reset();
            return part;
        }
    }

    public static class ColorMath
    {
        /// <summary>A hue brought into 0 to 360 (360 not included).</summary>
        public static float Wrap(float hue)
        {
            hue %= 360f;
            if (hue < 0) hue += 360f;
            return (hue >= 360f) ? 0 : hue;
        }

        /// <summary>How far round the wheel a hue is from another, -180 to 180.</summary>
        public static float HueDelta(float hue, float from)
        {
            var d = Wrap(hue - from);
            return (d > 180) ? d - 360 : d;
        }

        public static void ToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var d = max - min;
            v = max;
            s = (max <= 0) ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            h = Wrap(h);
        }

        public static Color FromHsv(float h, float s, float v, byte alpha = 255)
        {
            h = Wrap(h);
            s = Math.Max(0, Math.Min(1, s));
            v = Math.Max(0, Math.Min(1, v));
            var c = v * s;
            var x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            var m = v - c;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return new Color((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255), alpha);
        }
    }
}
