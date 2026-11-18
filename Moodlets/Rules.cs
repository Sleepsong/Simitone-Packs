/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Moodlets
{
    /// <summary>A moodlet a Sim has: which kind, how long it has left, and what it was about, if anything.</summary>
    public class Moodlet
    {
        public string Id { get; set; } = "";
        /// <summary>Minutes of game time left.</summary>
        public int Minutes { get; set; }
        /// <summary>What set it off, in a sentence, for the panel: "Now: Desk Sergeant." Empty for nothing more to say.</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// The rules of moodlets, with nothing of the game in them: giving one, time passing, and how much they add to a
    /// Sim's mood.
    /// </summary>
    public static class Rules
    {
        public enum Given { New, Renewed, Replaced, Unknown }

        /// <summary>
        /// Give a Sim a moodlet. One they have already starts again from the beginning, as in The Sims 3, with the
        /// newer note. One of the same group as one they have takes its place.
        /// </summary>
        public static Given Give(List<Moodlet> list, MoodletKind kind, Tuning tuning, string note = "")
        {
            if (list == null || kind == null || tuning == null) return Given.Unknown;
            note ??= "";
            var had = list.FirstOrDefault(x => x.Id == kind.Id);
            if (had != null)
            {
                had.Minutes = kind.Minutes;
                had.Note = note;
                return Given.Renewed;
            }
            var replaced = kind.Group != "" && list.RemoveAll(x => x.Id != kind.Id && tuning.Find(x.Id)?.Group == kind.Group) > 0;
            list.Add(new Moodlet() { Id = kind.Id, Minutes = kind.Minutes, Note = note });
            return replaced ? Given.Replaced : Given.New;
        }

        /// <summary>Game time has passed: every moodlet has that much less left. Returns those that ran out, which are taken off.</summary>
        public static List<Moodlet> Pass(List<Moodlet> list, int minutes)
        {
            var ended = new List<Moodlet>();
            if (list == null || minutes <= 0) return ended;
            foreach (var moodlet in list)
            {
                moodlet.Minutes -= minutes;
                if (moodlet.Minutes <= 0) ended.Add(moodlet);
            }
            list.RemoveAll(x => x.Minutes <= 0);
            return ended;
        }

        /// <summary>What a Sim's moodlets add up to, before the cap. Moodlets of a kind no longer known add nothing.</summary>
        public static int Sum(IEnumerable<Moodlet> list, Tuning tuning)
        {
            if (list == null || tuning == null) return 0;
            return list.Sum(x => tuning.Find(x.Id)?.Mood ?? 0);
        }

        /// <summary>What a Sim's moodlets add to their mood: their sum, held within the cap.</summary>
        public static int Total(IEnumerable<Moodlet> list, Tuning tuning)
        {
            if (tuning == null) return 0;
            return Math.Clamp(Sum(list, tuning), -tuning.Cap, tuning.Cap);
        }

        /// <summary>The game's mood with the moodlets added, kept on its scale of -100 to 100.</summary>
        public static short Adjust(int mood, int offset) => (short)Math.Clamp(mood + offset, -100, 100);

        /// <summary>The moodlets in the order the panel shows them: strongest first, the good before the bad when as strong.</summary>
        public static List<Moodlet> Ordered(IEnumerable<Moodlet> list, Tuning tuning)
        {
            return (list ?? Enumerable.Empty<Moodlet>())
                .OrderByDescending(x => Math.Abs(tuning.Find(x.Id)?.Mood ?? 0))
                .ThenByDescending(x => tuning.Find(x.Id)?.Mood ?? 0)
                .ThenByDescending(x => x.Minutes)
                .ToList();
        }

        /// <summary>"3 hours left", "40 minutes left", "2 days left". Rounded up, so it never says nothing is left of what still is.</summary>
        public static string TimeLeft(int minutes)
        {
            if (minutes < 60) return Plural(Math.Max(1, minutes), "minute") + " left";
            if (minutes <= 48 * 60) return Plural((minutes + 59) / 60, "hour") + " left";
            return Plural((minutes + 24 * 60 - 1) / (24 * 60), "day") + " left";
        }

        /// <summary>"14h", "40m", "3d": the time left, short enough for the panel.</summary>
        public static string ShortTime(int minutes)
        {
            if (minutes < 60) return Math.Max(1, minutes) + "m";
            if (minutes <= 48 * 60) return (minutes + 59) / 60 + "h";
            return (minutes + 24 * 60 - 1) / (24 * 60) + "d";
        }

        /// <summary>"+12", "-8", "0".</summary>
        public static string Signed(int mood) => mood > 0 ? "+" + mood : mood.ToString();

        public static string Plural(int n, string word) => n + " " + word + (n == 1 ? "" : "s");
    }

    /// <summary>
    /// Watches a lot's clock and says how many minutes have passed since it was last looked at. It only trusts the
    /// clock stepping forward a little at a time: a clock that jumps, as it does when a lot loads or a cheat sets
    /// the hour, is not time lived, so moodlets do not run out all at once.
    /// </summary>
    public class ClockWatch
    {
        private int Last = -1;

        /// <summary>The longest step taken for time passing. A tick of the game moves the clock a minute at most.</summary>
        public const int LongestStep = 30;

        public int Feed(int hours, int minutes)
        {
            var now = hours * 60 + minutes;
            var last = Last;
            Last = now;
            if (last < 0) return 0;
            var passed = (now - last + 24 * 60) % (24 * 60);
            return passed <= LongestStep ? passed : 0;
        }
    }

    /// <summary>A Sim's job as it was last seen, and what a change in it means.</summary>
    public static class Jobs
    {
        public enum Change { None, Promoted, Demoted, NewJob, Fired }

        /// <summary>
        /// What the change from one job and level to another is. The game gives a job of 0 to a Sim with none.
        /// A new job of any kind is a new job, at whatever level it starts; only a level changing within the same
        /// job is a promotion or a demotion.
        /// </summary>
        public static Change Between(short oldType, short oldLevel, short newType, short newLevel)
        {
            if (oldType == newType)
            {
                if (newType == 0 || newLevel == oldLevel) return Change.None;
                return newLevel > oldLevel ? Change.Promoted : Change.Demoted;
            }
            return newType == 0 ? Change.Fired : Change.NewJob;
        }

        public static string MoodletFor(Change change)
        {
            switch (change)
            {
                case Change.Promoted: return Tuning.Promoted;
                case Change.Demoted: return Tuning.Demoted;
                case Change.NewJob: return Tuning.NewJob;
                case Change.Fired: return Tuning.Fired;
                default: return null;
            }
        }
    }

    /// <summary>Interactions the game names in English, for what has no other sign.</summary>
    public static class Words
    {
        /// <summary>
        /// Whether an interaction's name says it is a proposal of marriage, as the Family Tree pack reads it. Only
        /// the English game's names are known.
        /// </summary>
        public static bool LooksLikeProposal(string interaction)
        {
            if (string.IsNullOrEmpty(interaction)) return false;
            var name = interaction.ToLowerInvariant();
            return name.Contains("propos") || name.Contains("marr");
        }
    }
}
