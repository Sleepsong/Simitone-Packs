/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Aspirations
{
    /// <summary>
    /// What the game shows of one Sim at one moment, as plain numbers. The pack's rules (Goal.cs, Aspiration.cs)
    /// read only this, never the game itself, so they can be tested without one. Watcher.cs fills it in from the lot.
    /// </summary>
    public class SimState
    {
        public bool Child;
        /// <summary>The game's raw skills, 0 to 1000 (a skill point is 100), in the order of Rules.SkillNames.</summary>
        public int[] Skills = new int[Rules.SkillNames.Length];
        /// <summary>Personality, 0 to 1000, in the order of Rules.TraitNames.</summary>
        public int[] Traits = new int[Rules.TraitNames.Length];

        public int JobType;
        public int JobLevel;
        /// <summary>How many levels the career has. 0 without a job.</summary>
        public int JobLevels;
        public string Career = "";
        /// <summary>Job performance, or a child's grades, from -100 to 100.</summary>
        public int Performance;
        /// <summary>Raw skills the next promotion asks for, in the order of the skills. Null without one to come.</summary>
        public int[] NextSkills;

        /// <summary>Needs and mood, from -100 to 100, in the order of Rules.NeedNames.</summary>
        public int[] Needs = new int[Rules.NeedNames.Length];
        public bool Sleeping;

        public int Funds;
        /// <summary>People, by the game's neighbour number, who are this Sim's friends, best friends, and loves.</summary>
        public HashSet<int> Friends = new HashSet<int>();
        public HashSet<int> BestFriends = new HashSet<int>();
        public HashSet<int> Loves = new HashSet<int>();

        /// <summary>Everyone in the household, pets too.</summary>
        public int HouseholdSize;
        public int HouseholdAdults;
        public int HouseholdChildren;
        /// <summary>The grades of the household's children other than this Sim, by the child's GUID.</summary>
        public Dictionary<uint, int> ChildGrades = new Dictionary<uint, int>();

        public bool AtHome;
        /// <summary>People on the lot who are not of the household and not the game's workers.</summary>
        public int Guests;
        /// <summary>Everyone else on the lot, the household and the game's workers too, pets not.</summary>
        public int Others;
        public bool Fire;
        /// <summary>The names of everyone else on the lot, in lower case, for spotting a burglar or the repo man.</summary>
        public List<string> Visitors = new List<string>();

        public int SkillLevel(int skill) => Skills[skill] / 100;
        public int Need(int need) => Needs[need];
        public int Mood => Needs[Rules.MoodNeed];
        public bool HasJob => JobType != 0 && JobLevels > 0;
        public bool AtTop => HasJob && JobLevel >= JobLevels - 1;
        public int SkillsMaxed => Skills.Count(x => x >= Rules.MaxSkill * 100);
    }

    public enum HappeningKind
    {
        /// <summary>The Sim finished an interaction.</summary>
        Done,
        /// <summary>Someone else did an interaction to the Sim.</summary>
        Received,
        /// <summary>The household bought an object.</summary>
        Purchase,
        /// <summary>A baby arrived on the lot.</summary>
        Baby,
    }

    public enum Partner { None, Household, Other, Object }

    /// <summary>Something that happened since the last look, for goals about events rather than about how things are.</summary>
    public class Happening
    {
        public HappeningKind Kind;
        /// <summary>The interaction's name, in lower case. For one with an object, followed by " | " and the object's name.</summary>
        public string Name = "";
        public Partner With;
        /// <summary>The person it was with, by neighbour number, or 0.</summary>
        public int Person;
        /// <summary>1 if the relationship went up (accepted), -1 if down (rejected), 0 if neither.</summary>
        public int Outcome;
        public int Price;

        public override string ToString() => Kind + " " + Name + " (" + With + (Person != 0 ? " " + Person : "") + (Outcome != 0 ? Outcome > 0 ? ", accepted" : ", rejected" : "") + (Price != 0 ? " §" + Price : "") + ")";
    }

    [Flags]
    public enum Ages { Child = 1, Adult = 2, Any = 3 }

    public static class Rules
    {
        public const double MeterMin = -100, MeterMax = 100;
        public const int MaxSkill = 10;
        public const string AnyAspiration = "Any";
        public const string GrowUp = "GrowUp";

        /// <summary>The game's skills, in the order the job panel shows them and careers ask for them.</summary>
        public static readonly string[] SkillNames = { "Cooking", "Mechanical", "Charisma", "Body", "Logic", "Creativity" };
        public static readonly string[] TraitNames = { "Neat", "Outgoing", "Active", "Playful", "Nice" };
        /// <summary>The needs the game has, then the room, then mood (the game's average of them).</summary>
        public static readonly string[] NeedNames = { "Hunger", "Comfort", "Hygiene", "Bladder", "Energy", "Fun", "Social", "Room", "Mood" };
        public const int MoodNeed = 8, RoomNeed = 7, EnergyNeed = 4;

        public static int SkillIndex(string name) => Array.FindIndex(SkillNames, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        public static int NeedIndex(string name) => Array.FindIndex(NeedNames, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        public static int TraitIndex(string name) => Array.FindIndex(TraitNames, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

        public static string ParseAges(string ages)
        {
            switch ((ages ?? "").Trim().ToLowerInvariant())
            {
                case "adult": case "adults": return "adult";
                case "child": case "children": return "child";
                default: return "any";
            }
        }

        public static bool AgeFits(string ages, bool child) => ages == "any" || (ages == "child") == child;

        public static string Money(long amount) => "§" + amount.ToString("#,0", CultureInfo.InvariantCulture);
        public static string Number(long amount) => amount.ToString("#,0", CultureInfo.InvariantCulture);

        // ---- the meter ----

        public static int LevelIndex(double meter, Balance b)
        {
            var index = 0;
            for (int i = 0; i < b.Levels.Count; i++) if (meter >= b.Levels[i].From) index = i;
            return index;
        }

        public static LevelDef Level(double meter, Balance b) => b.Levels[LevelIndex(meter, b)];

        /// <summary>Whether the meter is at the lowest level, aspiration failure.</summary>
        public static bool Failing(double meter, Balance b) => b.Levels.Count > 1 && LevelIndex(meter, b) == 0;

        /// <summary>
        /// How far the meter moves in the minutes given: down by the level's decay, and up or down by mood (the
        /// game's mood, -100 to 100). The decay of the level the meter is at when it starts is used throughout: a
        /// step is a few minutes at most.
        /// </summary>
        public static double Drift(double meter, int mood, double minutes, Balance b)
        {
            var hours = minutes / 60.0;
            var level = Level(meter, b);
            return (-level.DecayPerHour + b.Meter.MoodPerHour * Math.Clamp(mood, -100, 100) / 100.0) * hours;
        }

        public static double Clamp(double meter) => Math.Clamp(meter, MeterMin, MeterMax);

        // ---- choosing an aspiration ----

        /// <summary>
        /// How well a Sim suits each aspiration for grown Sims, from balance.json's "fit": each personality trait and
        /// skill is counted from 0 to 1, and HasJob, JobLevel (0 to 1 along the career), HasChildren and Friends
        /// (0 to 1, up to 10) likewise.
        /// </summary>
        public static Dictionary<string, double> Fits(SimState s, Balance b)
        {
            var result = new Dictionary<string, double>();
            foreach (var a in b.Aspirations.Where(x => !x.Child))
            {
                double score = 0;
                foreach (var pair in a.Fit)
                    score += pair.Value * Feature(s, pair.Key);
                result[a.Id] = score;
            }
            return result;
        }

        private static double Feature(SimState s, string name)
        {
            var trait = TraitIndex(name);
            if (trait >= 0) return s.Traits[trait] / 1000.0;
            var skill = SkillIndex(name);
            if (skill >= 0) return s.Skills[skill] / 1000.0;
            switch (name.ToLowerInvariant())
            {
                case "hasjob": return s.HasJob ? 1 : 0;
                case "joblevel": return s.HasJob && s.JobLevels > 1 ? s.JobLevel / (double)(s.JobLevels - 1) : 0;
                case "haschildren": return s.HouseholdChildren > 0 ? 1 : 0;
                case "friends": return Math.Min(10, s.Friends.Count) / 10.0;
                default: return 0;
            }
        }

        /// <summary>An aspiration for a Sim nobody has chosen one for: Grow Up for a child, and the best fit, with a little chance, for anyone else.</summary>
        public static string Suggest(SimState s, Balance b, Random rng)
        {
            if (s.Child) return b.Aspirations.FirstOrDefault(x => x.Child)?.Id ?? GrowUp;
            var fits = Fits(s, b);
            if (fits.Count == 0) return null;
            //chance in a fifth of the range between best and worst, so a close second sometimes wins
            var spread = Math.Max(0.1, fits.Values.Max() - fits.Values.Min());
            return fits.OrderByDescending(x => x.Value + rng.NextDouble() * spread * 0.2).First().Key;
        }

        // ---- picking by weight ----

        public static T Pick<T>(IList<(T item, double weight)> choices, Random rng) where T : class
        {
            var total = choices.Sum(x => x.weight);
            if (total <= 0) return null;
            var at = rng.NextDouble() * total;
            foreach (var choice in choices)
            {
                at -= choice.weight;
                if (at < 0) return choice.item;
            }
            return choices.Last(x => x.weight > 0).item;
        }

        /// <summary>A round sum: to two leading figures, upwards (1,234 becomes 1,300; 56,789 becomes 57,000).</summary>
        public static int Round(double amount)
        {
            if (amount <= 100) return (int)Math.Ceiling(amount / 10) * 10;
            var step = Math.Pow(10, Math.Floor(Math.Log10(amount)) - 1);
            return (int)(Math.Ceiling(amount / step) * step);
        }
    }
}
