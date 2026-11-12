/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;

namespace LifeStages
{
    /// <summary>The stages of life this pack knows. A baby is the game's own bassinet, and is not one of them.</summary>
    public enum Stage
    {
        Child = 0,
        Teen = 1,
        Adult = 2,
        Elder = 3
    }

    /// <summary>What the pack remembers about one Sim.</summary>
    public class SimAge
    {
        public Stage Stage { get; set; }
        /// <summary>Whole days lived in this stage, on the Sim's own lot.</summary>
        public int Days { get; set; }
    }

    /// <summary>
    /// The rules of ageing, with nothing of the game in them: how long a stage lasts, what follows it, and what the
    /// game is told about a Sim in it. Everything here can be checked without a copy of The Sims.
    /// </summary>
    public static class Stages
    {
        /// <summary>
        /// The age the game is told. Its scripts only ask one thing of it: under 18 is a child, anything else an
        /// adult. So a teen stays under 18 (school, and no job, marriage or baby), and an elder stays an adult.
        /// </summary>
        public static short GameAge(Stage stage)
        {
            switch (stage)
            {
                case Stage.Child: return 9;   //what the game gives its own children
                case Stage.Teen: return 15;
                case Stage.Adult: return 27;  //what the game gives its own adults
                default: return 65;
            }
        }

        /// <summary>The game's adult age: the line its scripts draw between a child and an adult.</summary>
        public const short AdultAge = 18;

        /// <summary>Whether the game sees a Sim in this stage as a child (its body, its animations, what it may do).</summary>
        public static bool IsChildToGame(Stage stage) => GameAge(stage) < AdultAge;

        /// <summary>
        /// How large a Sim in this stage is drawn, in percent. A teen is the game's child drawn larger: The Sims has
        /// no teenage body, and a child's animations only fit a child's skeleton.
        /// </summary>
        public static short Size(Stage stage) => stage == Stage.Teen ? (short)118 : (short)100;

        public static string Name(Stage stage)
        {
            switch (stage)
            {
                case Stage.Child: return "child";
                case Stage.Teen: return "teen";
                case Stage.Adult: return "adult";
                default: return "elder";
            }
        }

        public static bool TryParse(string word, out Stage stage)
        {
            foreach (Stage s in Enum.GetValues(typeof(Stage)))
            {
                if (string.Equals(Name(s), word, StringComparison.OrdinalIgnoreCase)) { stage = s; return true; }
            }
            stage = Stage.Child;
            return false;
        }

        /// <summary>Whether an age the game holds already suits a stage, so that it need not be changed.</summary>
        public static bool AgeFits(Stage stage, short age) => FromGameAge(age) == stage;

        /// <summary>The stage a Sim the pack has not met is in, from the age the game holds for it.</summary>
        public static Stage FromGameAge(short age)
        {
            if (age >= GameAge(Stage.Elder)) return Stage.Elder;
            if (age >= AdultAge || age == 0) return Stage.Adult; //the game reads 0 as an adult in most places
            return age >= GameAge(Stage.Teen) ? Stage.Teen : Stage.Child;
        }

        /// <summary>What follows a stage, or null after the last. With teens switched off a child grows straight up.</summary>
        public static Stage? Next(Stage stage, Settings settings)
        {
            switch (stage)
            {
                case Stage.Child: return settings.Teens ? Stage.Teen : Stage.Adult;
                case Stage.Teen: return Stage.Adult;
                case Stage.Adult: return Stage.Elder;
                default: return null; //an elder stays an elder: nobody dies of old age yet
            }
        }

        /// <summary>Days a stage lasts. 0 for one that does not end.</summary>
        public static int Length(Stage stage, Settings settings)
        {
            if (Next(stage, settings) == null) return 0;
            switch (stage)
            {
                case Stage.Child: return settings.ChildDays;
                case Stage.Teen: return settings.TeenDays;
                default: return settings.AdultDays;
            }
        }

        /// <summary>A day has passed for this Sim.</summary>
        public static void AddDay(SimAge sim, Settings settings)
        {
            //a Sim waiting to grow up (see Due) does not run up a debt of days
            if (!Due(sim, settings)) sim.Days++;
        }

        /// <summary>Whether the Sim has lived this stage out and should move to the next.</summary>
        public static bool Due(SimAge sim, Settings settings)
        {
            var length = Length(sim.Stage, settings);
            return length > 0 && sim.Days >= length;
        }

        /// <summary>Move the Sim on. Returns false if there is nothing after this stage.</summary>
        public static bool Advance(SimAge sim, Settings settings)
        {
            var next = Next(sim.Stage, settings);
            if (next == null) return false;
            sim.Stage = next.Value;
            sim.Days = 0;
            return true;
        }

        /// <summary>"day 3 of 8", or "day 3" for a stage that does not end.</summary>
        public static string Describe(SimAge sim, Settings settings)
        {
            var length = Length(sim.Stage, settings);
            var day = sim.Days + 1;
            if (length == 0) return Name(sim.Stage) + ", day " + day;
            return Name(sim.Stage) + ", day " + Math.Min(day, length) + " of " + length;
        }
    }

    /// <summary>The player's choices, kept with the neighbourhood. The lengths are The Sims 2's.</summary>
    public class Settings
    {
        /// <summary>Whether days count. Off, Sims stay as they are and nothing is forgotten.</summary>
        public bool Ageing { get; set; } = true;
        /// <summary>Whether children become teens before adults.</summary>
        public bool Teens { get; set; } = true;
        /// <summary>Whether an elder's hair is drawn grey.</summary>
        public bool GreyHair { get; set; } = true;
        public int ChildDays { get; set; } = 8;
        public int TeenDays { get; set; } = 15;
        public int AdultDays { get; set; } = 29;

        public const int LongestStage = 999;

        /// <summary>Put anything a hand-edited or damaged file holds back within reason.</summary>
        public void Repair()
        {
            ChildDays = Math.Clamp(ChildDays, 1, LongestStage);
            TeenDays = Math.Clamp(TeenDays, 1, LongestStage);
            AdultDays = Math.Clamp(AdultDays, 1, LongestStage);
        }

        public bool SetLength(Stage stage, int days)
        {
            if (days < 1 || days > LongestStage) return false;
            switch (stage)
            {
                case Stage.Child: ChildDays = days; return true;
                case Stage.Teen: TeenDays = days; return true;
                case Stage.Adult: AdultDays = days; return true;
                default: return false; //an elder's days are not numbered
            }
        }
    }

    /// <summary>
    /// Watches a lot's clock for the two moments that matter: midnight, when a day has been lived, and six in the
    /// evening, when birthdays happen (as in The Sims 2, and when Sims are mostly up). It only trusts the clock
    /// stepping from one hour to the next: a clock that jumps, as it does when a lot loads, is not a day gone by.
    /// </summary>
    public class DayWatch
    {
        public const int BirthdayHour = 18;
        private int LastHour = -1;

        public enum Moment { None, Hour, Midnight, Birthdays }

        public Moment Feed(int hour)
        {
            var last = LastHour;
            LastHour = hour;
            if (last == -1 || last == hour) return Moment.None;
            if ((last + 1) % 24 != hour) return Moment.None;
            if (hour == 0) return Moment.Midnight;
            return hour == BirthdayHour ? Moment.Birthdays : Moment.Hour;
        }
    }
}
