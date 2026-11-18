/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Chemistry
{
    /// <summary>
    /// What a Sim can be turned on or off by. The Sims 2 had hair colours, glasses, make-up and the like as well; The
    /// Sims 1 keeps none of those where a script can read them, so the list is made of what it does keep: personality,
    /// skills, work, build, how clean a Sim is, and what they are wearing.
    /// </summary>
    public enum Trait
    {
        Neat, Sloppy,
        Outgoing, Shy,
        Active, Lazy,
        Playful, Serious,
        Nice, Grouchy,
        GoodCook, Charismatic, Mechanical, Creative, Athletic, Logical,
        HardWorker, Unemployed,
        Fit, Fat,
        Stink,
        Swimwear, FormalWear, Sleepwear,
    }

    public enum Build { Unknown, Fit, Fat, Skinny }

    public enum Clothes { Everyday, Swimwear, Formal, Sleepwear }

    /// <summary>
    /// What the game says about a Sim, as far as turn-ons go. Personality and skills are the game's own numbers, 0 to
    /// 1000 (ten points of 100 each). Hygiene is the motive, -100 to 100, or null when it is not known (a Sim who is not
    /// on the lot).
    /// </summary>
    public sealed class Facts
    {
        public short Neat, Outgoing, Active, Playful, Nice;
        public short Cooking, Charisma, Mechanical, Creativity, Body, Logic;
        public bool Employed;
        public Build Build;
        public short? Hygiene;
        public Clothes Clothes;
    }

    public static class Traits
    {
        /// <summary>Seven personality points or more is a lot of that trait; three or fewer, the opposite.</summary>
        public const short High = 700;
        public const short Low = 300;
        /// <summary>Five skill points or more, half way up the scale.</summary>
        public const short Skilled = 500;
        /// <summary>A hygiene motive this low or lower stinks.</summary>
        public const short Smelly = -50;

        public static readonly IReadOnlyList<Trait> All = (Trait[])Enum.GetValues(typeof(Trait));

        public static bool Has(Facts f, Trait t)
        {
            if (f == null) return false;
            switch (t)
            {
                case Trait.Neat: return f.Neat >= High;
                case Trait.Sloppy: return f.Neat <= Low;
                case Trait.Outgoing: return f.Outgoing >= High;
                case Trait.Shy: return f.Outgoing <= Low;
                case Trait.Active: return f.Active >= High;
                case Trait.Lazy: return f.Active <= Low;
                case Trait.Playful: return f.Playful >= High;
                case Trait.Serious: return f.Playful <= Low;
                case Trait.Nice: return f.Nice >= High;
                case Trait.Grouchy: return f.Nice <= Low;
                case Trait.GoodCook: return f.Cooking >= Skilled;
                case Trait.Charismatic: return f.Charisma >= Skilled;
                case Trait.Mechanical: return f.Mechanical >= Skilled;
                case Trait.Creative: return f.Creativity >= Skilled;
                case Trait.Athletic: return f.Body >= Skilled;
                case Trait.Logical: return f.Logic >= Skilled;
                case Trait.HardWorker: return f.Employed;
                case Trait.Unemployed: return !f.Employed;
                case Trait.Fit: return f.Build == Build.Fit;
                case Trait.Fat: return f.Build == Build.Fat;
                case Trait.Stink: return f.Hygiene.HasValue && f.Hygiene.Value <= Smelly;
                case Trait.Swimwear: return f.Clothes == Clothes.Swimwear;
                case Trait.FormalWear: return f.Clothes == Clothes.Formal;
                case Trait.Sleepwear: return f.Clothes == Clothes.Sleepwear;
                default: return false;
            }
        }

        /// <summary>The trait no Sim can have at the same time as this one, if there is one.</summary>
        public static Trait? Opposite(Trait t)
        {
            switch (t)
            {
                case Trait.Neat: return Trait.Sloppy;
                case Trait.Sloppy: return Trait.Neat;
                case Trait.Outgoing: return Trait.Shy;
                case Trait.Shy: return Trait.Outgoing;
                case Trait.Active: return Trait.Lazy;
                case Trait.Lazy: return Trait.Active;
                case Trait.Playful: return Trait.Serious;
                case Trait.Serious: return Trait.Playful;
                case Trait.Nice: return Trait.Grouchy;
                case Trait.Grouchy: return Trait.Nice;
                case Trait.HardWorker: return Trait.Unemployed;
                case Trait.Unemployed: return Trait.HardWorker;
                case Trait.Fit: return Trait.Fat;
                case Trait.Fat: return Trait.Fit;
                default: return null;
            }
        }

        /// <summary>The word for a trait in the console and in the pack's file: "good-cook".</summary>
        public static string Word(Trait t)
        {
            switch (t)
            {
                case Trait.GoodCook: return "good-cook";
                case Trait.HardWorker: return "hard-worker";
                case Trait.FormalWear: return "formal-wear";
                default: return t.ToString().ToLowerInvariant();
            }
        }

        /// <summary>The trait as the player reads it: "Good cook".</summary>
        public static string Name(Trait t)
        {
            var word = Word(t).Replace('-', ' ');
            return char.ToUpperInvariant(word[0]) + word.Substring(1);
        }

        public static bool TryParse(string word, out Trait trait)
        {
            word = (word ?? "").Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            foreach (var t in All)
            {
                if (Word(t) == word || Word(t).Replace("-", "") == word) { trait = t; return true; }
            }
            trait = default;
            return false;
        }

        public static string AllWords => string.Join(", ", All.Select(Word));
    }
}
