/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Chemistry
{
    /// <summary>A Sim's two turn-ons and one turn-off, as in The Sims 2: Nightlife.</summary>
    public sealed class Likes
    {
        public readonly Trait On1, On2, Off;

        public Likes(Trait on1, Trait on2, Trait off)
        {
            On1 = on1; On2 = on2; Off = off;
        }

        /// <summary>Why these cannot go together, or null if they can.</summary>
        public string Problem()
        {
            if (On1 == On2) return "The two turn-ons must be different.";
            if (Traits.Opposite(On1) == On2) return Traits.Name(On1) + " and " + Traits.Name(On2).ToLowerInvariant() + " cannot both be turn-ons: nobody is both.";
            if (Off == On1 || Off == On2) return "A turn-on cannot also be the turn-off.";
            return null;
        }

        public bool Same(Likes other) => other != null && other.Off == Off && ((other.On1 == On1 && other.On2 == On2) || (other.On1 == On2 && other.On2 == On1));

        public override string ToString() => "turned on by " + Traits.Name(On1).ToLowerInvariant() + " and " + Traits.Name(On2).ToLowerInvariant() + ", off by " + Traits.Name(Off).ToLowerInvariant();
    }

    /// <summary>How one Sim feels about another, in The Sims 2's lightning bolts.</summary>
    public enum Level
    {
        TurnedOff = -1,
        None = 0,
        OneBolt = 1,
        TwoBolts = 2,
        ThreeBolts = 3,
    }

    /// <summary>
    /// The rules of chemistry, which know nothing of the game. Chemistry goes one way: how one Sim feels about another
    /// comes from the first Sim's turn-ons and the second Sim's traits.
    /// </summary>
    public static class Attraction
    {
        /// <summary>One point for each of the turn-ons the other Sim has, two off if they have the turn-off: -2 to 2.</summary>
        public static int Score(Likes likes, Facts other)
        {
            if (likes == null || other == null) return 0;
            var score = 0;
            if (Traits.Has(other, likes.On1)) score++;
            if (Traits.Has(other, likes.On2)) score++;
            if (Traits.Has(other, likes.Off)) score -= 2;
            return score;
        }

        /// <summary>
        /// How a Sim who likes <paramref name="fromLikes"/> feels about a Sim with <paramref name="to"/>.
        /// Below nothing is turned off; each point above is a bolt. Three bolts is two, felt back: both turn-ons and not
        /// the turn-off, and the other Sim has at least one bolt the other way.
        /// </summary>
        public static Level Between(Likes fromLikes, Facts from, Likes toLikes, Facts to)
        {
            var score = Score(fromLikes, to);
            if (score < 0) return Level.TurnedOff;
            if (score == 0) return Level.None;
            if (score == 1) return Level.OneBolt;
            return Score(toLikes, from) >= 1 ? Level.ThreeBolts : Level.TwoBolts;
        }

        public static string Words(Level level)
        {
            switch (level)
            {
                case Level.TurnedOff: return "Turned off";
                case Level.OneBolt: return "1 bolt";
                case Level.TwoBolts: return "2 bolts";
                case Level.ThreeBolts: return "3 bolts";
                default: return "No chemistry";
            }
        }

        /// <summary>
        /// The turn-ons a Sim starts with, worked out from their character's GUID, so that a Sim has the same ones every
        /// time even before they are written down. Two turn-ons that are not opposites, and a turn-off that is neither.
        /// </summary>
        public static Likes Default(uint guid)
        {
            //splitmix64: the same Sim always draws the same, and Sims with GUIDs one apart draw unalike
            ulong state = guid * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            int Draw(int count)
            {
                state += 0x9E3779B97F4A7C15UL;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (int)(z % (ulong)count);
            }
            var all = Traits.All;
            var on1 = all[Draw(all.Count)];
            var second = all.Where(t => t != on1 && Traits.Opposite(on1) != t).ToList();
            var on2 = second[Draw(second.Count)];
            var third = all.Where(t => t != on1 && t != on2).ToList();
            var off = third[Draw(third.Count)];
            return new Likes(on1, on2, off);
        }

        /// <summary>
        /// A gain made larger or smaller by chemistry. Whole points only: the part of a point left over is given with
        /// that chance, drawn from <paramref name="below"/> (a number from 0 to less than what it is given), so that on
        /// average the gain is exactly the multiple. A loss, or no change, is left as it is.
        /// </summary>
        public static int Scale(int gain, double multiplier, Func<int, int> below)
        {
            if (gain <= 0 || multiplier == 1.0) return gain;
            var exact = gain * Math.Max(0.0, multiplier);
            var whole = (int)Math.Floor(exact + 1e-9);
            var part = exact - whole;
            if (part > 1e-9 && below != null && below(1000) < (int)Math.Round(part * 1000)) whole++;
            return whole;
        }
    }

    /// <summary>
    /// How much of the game's own gain each level of chemistry gives. Read from chemistry.json in the pack's folder at
    /// startup, so the balance can be changed without building the pack again.
    /// </summary>
    public sealed class Tuning
    {
        public const string FileName = "chemistry.json";
        public const double Most = 4.0;

        public double TurnedOff { get; set; } = 0.5;
        public double None { get; set; } = 1.0;
        public double OneBolt { get; set; } = 1.25;
        public double TwoBolts { get; set; } = 1.5;
        public double ThreeBolts { get; set; } = 2.0;

        public double Multiplier(Level level)
        {
            switch (level)
            {
                case Level.TurnedOff: return TurnedOff;
                case Level.OneBolt: return OneBolt;
                case Level.TwoBolts: return TwoBolts;
                case Level.ThreeBolts: return ThreeBolts;
                default: return None;
            }
        }

        private void Clamp()
        {
            double C(double x) => double.IsNaN(x) ? 1.0 : Math.Clamp(x, 0.0, Most);
            TurnedOff = C(TurnedOff); None = C(None); OneBolt = C(OneBolt); TwoBolts = C(TwoBolts); ThreeBolts = C(ThreeBolts);
        }

        /// <summary>The tuning in this text, or the pack's own where it says nothing. Problem is null if it read cleanly.</summary>
        public static Tuning Parse(string text, out string problem)
        {
            problem = null;
            var tuning = new Tuning();
            if (string.IsNullOrWhiteSpace(text)) return tuning;
            try
            {
                tuning = JsonSerializer.Deserialize<Tuning>(text, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }) ?? new Tuning();
            }
            catch (Exception e)
            {
                problem = e.Message;
                tuning = new Tuning();
            }
            tuning.Clamp();
            return tuning;
        }

        public static Tuning Read(string folder, out string problem)
        {
            problem = null;
            try
            {
                var path = folder == null ? null : Path.Combine(folder, FileName);
                if (path == null || !File.Exists(path)) return new Tuning();
                return Parse(File.ReadAllText(path), out problem);
            }
            catch (Exception e)
            {
                problem = e.Message;
                return new Tuning();
            }
        }

        public override string ToString() =>
            "turned off x" + TurnedOff + ", none x" + None + ", 1 bolt x" + OneBolt + ", 2 bolts x" + TwoBolts + ", 3 bolts x" + ThreeBolts;
    }
}
