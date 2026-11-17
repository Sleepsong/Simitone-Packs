/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Memories
{
    /// <summary>
    /// The rules by which the layer tells what happened, kept apart from the game so that they can be checked without
    /// it. Nothing here touches a running game: a Sim is a number and a name is a string.
    ///
    /// The Sims 1 says little about what its scripts are doing, so some of this goes by names. Two kinds of name are
    /// looked at: what an interaction is called on screen (in the game's language), and what the game's scripts call
    /// the routines that run it. The second are Maxis's own labels, in English in every copy of the game, so a rule
    /// that matches them works whatever language the game is in.
    /// </summary>
    public static class Recognise
    {
        // ---- by name ----

        /// <summary>A kind of thing recognised by name: any of the words, and none of the exceptions, in any of the names.</summary>
        public sealed class Words
        {
            public string[] Any { get; }
            public string[] Except { get; }

            public Words(string[] any, string[] except = null)
            {
                Any = any;
                Except = except ?? Array.Empty<string>();
            }

            /// <summary>Whether one name says it.</summary>
            public bool In(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                var lower = name.ToLowerInvariant();
                return Any.Any(w => Has(lower, w)) && !Except.Any(w => Has(lower, w));
            }

            /// <summary>Whether any of these names says it.</summary>
            public bool InAny(IEnumerable<string> names) => names != null && names.Any(In);

            /// <summary>
            /// A word is found at the start of a word in the name, so "die" is in "Die - Hunger" and "Sim dies" but
            /// not in "Diet" ... unless the word given ends in '*', which also matches the rest of a longer word.
            /// A word that starts with '*' may be found anywhere.
            /// </summary>
            private static bool Has(string name, string word)
            {
                var anywhere = word.StartsWith("*");
                var prefix = word.EndsWith("*");
                var w = word.Trim('*');
                if (w.Length == 0) return false;
                for (int at = name.IndexOf(w, StringComparison.Ordinal); at >= 0; at = name.IndexOf(w, at + 1, StringComparison.Ordinal))
                {
                    var startsWord = anywhere || at == 0 || !char.IsLetter(name[at - 1]);
                    var end = at + w.Length;
                    var endsWord = prefix || end >= name.Length || !char.IsLetter(name[end]) || (name[end] == 's' && (end + 1 >= name.Length || !char.IsLetter(name[end + 1])));
                    if (startsWord && endsWord) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// A kiss: the game's own kisses, and Hot Date's. Not a kiss on the hand, a blown kiss or an air kiss, which
        /// are not a first kiss in The Sims 2 either. The English game's names, and the scripts' own.
        /// </summary>
        public static readonly Words Kiss = new Words(new[] { "kiss*" }, new[] { "hand", "blow*", "air", "cheek" });

        /// <summary>A proposal of marriage. "propos" covers Propose and Proposal; "marr" Marry and Marriage.</summary>
        public static readonly Words Proposal = new Words(new[] { "propos*", "marr*", "*wedding" });

        /// <summary>
        /// Death, as the game's scripts name it: dying of hunger, of fire, by drowning, at the Grim Reaper's hands.
        /// </summary>
        public static readonly Words Death = new Words(new[] { "die", "dies", "dying", "death*", "grim reaper", "reaper", "starv*", "drown*", "electrocut*" },
            new[] { "near death" });

        /// <summary>The burglar, by the name of their character or the game's label for it.</summary>
        public static readonly Words Burglar = new Words(new[] { "burglar*", "robber", "thief" });

        /// <summary>The police, who come for a burglar when an alarm goes off.</summary>
        public static readonly Words Police = new Words(new[] { "police*", "cop", "officer" });

        // ---- life stages ----

        /// <summary>A Sim's stage from the age the game keeps for them. The same as the Life Stages pack: elders 65 and up, adults 18 and up (or 0, which the game reads as an adult), teens 15 to 17.</summary>
        public static Stage StageOf(short gameAge)
        {
            if (gameAge >= 65) return Stage.Elder;
            if (gameAge >= 18 || gameAge <= 0) return Stage.Adult;
            return gameAge >= 15 ? Stage.Teen : Stage.Child;
        }

        public static string StageName(Stage stage)
        {
            switch (stage)
            {
                case Stage.Child: return "child";
                case Stage.Teen: return "teen";
                case Stage.Adult: return "adult";
                default: return "elder";
            }
        }

        public static bool IsAdult(short gameAge) => StageOf(gameAge) >= Stage.Adult;

        // ---- jobs ----

        /// <summary>What a change of job is. A job of 0 is none. Another career is a new job; the same career at another level is a promotion or a demotion.</summary>
        public static LifeEventKind? JobChange(short oldJob, short oldLevel, short newJob, short newLevel)
        {
            if (oldJob == newJob)
            {
                if (newJob <= 0 || oldLevel == newLevel) return null;
                return newLevel > oldLevel ? LifeEventKind.Promoted : LifeEventKind.Demoted;
            }
            if (newJob <= 0) return LifeEventKind.LostJob;
            return LifeEventKind.GotJob;
        }

        // ---- skills ----

        /// <summary>The game keeps a skill from 0 to 1000; a point is each hundred.</summary>
        public static int SkillPoints(short value) => Math.Max(0, Math.Min(10, value / 100));

        // ---- how an interaction with another Sim went ----

        /// <summary>The highest a daily score goes.</summary>
        public const short TopScore = 100;

        /// <summary>
        /// Whether an interaction between two Sims went well, from how much each liked the other before and after it
        /// (the game's daily scores): 1 if either likes the other more, 0 if neither does and one likes the other less,
        /// -1 if nothing changed (or nothing is known). The Sims 1 does not say whether a social was accepted, but
        /// its scripts raise both scores when one is, and lower them when it is turned down. Two Sims who are both at
        /// the top cannot like each other more, so for them nothing changing means it went well.
        /// </summary>
        public static int WentWell(short? aToBBefore, short? bToABefore, short? aToBAfter, short? bToAAfter)
        {
            int? Change(short? before, short? after) => before == null || after == null ? (int?)null : after.Value - before.Value;
            var ab = Change(aToBBefore, aToBAfter);
            var ba = Change(bToABefore, bToAAfter);
            if (ab > 0 || ba > 0) return 1;
            if (ab < 0 || ba < 0) return 0;
            if (ab == 0 && ba == 0 && aToBBefore >= TopScore && bToABefore >= TopScore) return 1;
            return -1;
        }

        // ---- babies ----

        /// <summary>
        /// A baby's parents, from the Sims who had a hand in making it, most involved first: the first two of them who
        /// are adults of the household. With only one, their husband or wife is the other, if known and of the
        /// household too. (As the Family Tree pack works them out.)
        /// </summary>
        public static List<uint> PickParents(IEnumerable<uint> involved, ICollection<uint> household, Func<uint, bool> isAdult, Func<uint, uint?> spouseOf)
        {
            var parents = new List<uint>();
            foreach (var sim in involved)
            {
                if (parents.Count == 2) break;
                if (sim != 0 && !parents.Contains(sim) && household.Contains(sim) && isAdult(sim)) parents.Add(sim);
            }
            if (parents.Count == 1)
            {
                var spouse = spouseOf?.Invoke(parents[0]);
                if (spouse != null && spouse.Value != parents[0] && household.Contains(spouse.Value)) parents.Add(spouse.Value);
            }
            return parents;
        }

        /// <summary>
        /// A character has been made from one of these objects in this house: which of the babies waiting there it
        /// was. If none of the objects is one, but the house has exactly one baby waiting, it is that one (guessed).
        /// The baby found is taken off the list. Null if no baby is waiting there.
        /// </summary>
        public static WaitingBaby TakeBaby(List<WaitingBaby> waiting, int house, IEnumerable<int> objectIds, out bool guessed)
        {
            guessed = false;
            var ids = (objectIds ?? Enumerable.Empty<int>()).ToList();
            var here = waiting.Where(b => b.House == house).ToList();
            if (here.Count == 0) return null;
            var found = here.FirstOrDefault(b => ids.Contains(b.ObjectId));
            if (found == null)
            {
                if (here.Count != 1) return null;
                found = here[0];
                guessed = true;
            }
            waiting.Remove(found);
            return found;
        }

        // ---- once is enough ----

        /// <summary>
        /// The game's scripts may run a kiss as two interactions, one for each Sim, and one Sim may kiss another
        /// several times in a row. A couple's kiss counts once in this many minutes of game time.
        /// </summary>
        public const int KissMinutes = 60;

        /// <summary>The two Sims as one key, whichever way round they are given.</summary>
        public static (uint, uint) Couple(uint a, uint b) => a < b ? (a, b) : (b, a);
    }

    /// <summary>A baby that has arrived and not yet grown up: it is an object on its lot until then, not a Sim.</summary>
    public sealed class WaitingBaby
    {
        /// <summary>The house it is in, and its object there.</summary>
        public int House { get; set; }
        public int ObjectId { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(HexGuid))]
        public uint ObjectGuid { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(HexGuid))]
        public uint ParentA { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(HexGuid))]
        public uint ParentB { get; set; }
        public LifeTime Arrived { get; set; }

        public uint[] Parents() => new[] { ParentA, ParentB }.Where(p => p != 0).ToArray();
    }
}
