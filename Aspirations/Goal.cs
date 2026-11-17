/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspirations
{
    /// <summary>
    /// What each kind of want, fear and lifetime want means. balance.json says which goals there are, with their
    /// words and numbers; this says, for each kind, when a Sim can be given one (Eligible), what is noted when it
    /// is rolled (Make), when it has come true (Met), and when it no longer makes sense and is rolled again (Stale).
    ///
    /// A goal is measured against how things were when it was rolled, so "get promoted" means a level above the
    /// one the Sim had then, and "earn §2,000" means two thousand more than the household had then.
    /// </summary>
    public static class Goal
    {
        private static readonly HashSet<string> Kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            //skills
            "SkillUp", "CareerSkill", "SkillMax", "SkillsMaxed",
            //work and school
            "GetJob", "Promotion", "TopOfCareer", "CareerTop", "Performance", "LowPerformance", "Fired", "Demoted",
            "Grades", "ChildGrades", "ChildBadGrades",
            //money
            "Earn", "HaveFunds", "Buy", "Broke",
            //people
            "NewFriend", "Friends", "BestFriend", "FallInLove", "LoseFriend", "NewHousemate", "Baby", "Children", "LoseMember",
            "Interaction", "Received", "Party", "Outing",
            //how the Sim is, and what happens to the lot
            "NeedHigh", "NeedLow", "Fire", "Visitor",
        };

        public static bool Known(string kind) => kind != null && Kinds.Contains(kind);

        public static IEnumerable<string> AllKinds => Kinds;

        private static string K(GoalDef d) => d.Kind.ToLowerInvariant();

        /// <summary>A threshold, or the kind's usual one.</summary>
        private static int T(GoalDef d, int usual) => d.Threshold != 0 ? d.Threshold : usual;

        /// <summary>Whether the Sim can be given this goal now.</summary>
        public static bool Eligible(GoalDef d, SimState s, Balance b)
        {
            if (!Rules.AgeFits(d.Ages, s.Child)) return false;
            switch (K(d))
            {
                case "skillup":
                    return d.Skill == null || d.Skill == "any" ? s.Skills.Any(x => x / 100 < Rules.MaxSkill) : s.SkillLevel(Rules.SkillIndex(d.Skill)) < Rules.MaxSkill;
                case "careerskill":
                    return CareerSkills(s).Count > 0;
                case "skillmax":
                    return MaxCandidates(d, s).Count > 0;
                case "skillsmaxed":
                    return s.SkillsMaxed < Math.Max(1, d.Total);
                case "getjob":
                    return !s.Child && !s.HasJob;
                case "promotion":
                    return s.HasJob && !s.AtTop;
                case "topofcareer":
                    return s.HasJob && !s.AtTop && s.JobLevel >= s.JobLevels - 1 - Math.Max(1, d.Count == 0 ? 2 : d.Count);
                case "careertop":
                    return !s.Child && !s.AtTop;
                case "performance":
                    return s.HasJob && s.Performance < T(d, 60);
                case "grades":
                    return s.Child && s.Performance < T(d, 60);
                case "lowperformance":
                    return (s.Child || s.HasJob) && s.Performance > T(d, -50);
                case "childgrades":
                    return s.ChildGrades.Any(x => x.Value < T(d, 60));
                case "childbadgrades":
                    return s.ChildGrades.Any(x => x.Value > T(d, -20));
                case "fired":
                    return s.HasJob;
                case "demoted":
                    return s.HasJob && s.JobLevel > 0;
                case "earn":
                case "buy":
                    return true;
                case "havefunds":
                    return FundsTarget(d, s) > s.Funds;
                case "broke":
                    return s.Funds > Math.Max(0, d.Amount);
                case "newfriend":
                    return true;
                case "friends":
                    return s.Friends.Count < FriendsTarget(d, s);
                case "bestfriend":
                    return s.Friends.Any(x => !s.BestFriends.Contains(x));
                case "fallinlove":
                    return d.Total <= 0 || s.Loves.Count < d.Total;
                case "losefriend":
                    return s.Friends.Count > 0;
                case "newhousemate":
                case "baby":
                    return s.HouseholdSize < 8;
                case "children":
                    return s.HouseholdChildren < Math.Max(1, d.Total);
                case "losemember":
                    return s.HouseholdSize >= 2;
                case "interaction":
                case "received":
                    return d.Match.Count > 0;
                case "party":
                    return s.AtHome && s.Guests < Math.Max(1, d.Count);
                case "outing":
                    return s.AtHome;
                case "needhigh":
                    return s.Need(Rules.NeedIndex(d.Need)) < T(d, 80);
                case "needlow":
                    return s.Need(Rules.NeedIndex(d.Need)) > T(d, -80);
                case "fire":
                    return !s.Fire;
                case "visitor":
                    return d.Match.Count > 0 && !Seen(d, s);
                default:
                    return false;
            }
        }

        /// <summary>The goal as the Sim is given it: what it is measured against, its words and its points. Null if it cannot be made now.</summary>
        public static Slot Make(GoalDef d, SimState s, Random rng, Balance b)
        {
            var slot = new Slot() { Id = d.Id, Points = d.Points };
            int level = 0;
            switch (K(d))
            {
                case "skillup":
                {
                    int skill;
                    if (d.Skill == null || d.Skill == "any")
                    {
                        var open = Enumerable.Range(0, Rules.SkillNames.Length).Where(i => s.SkillLevel(i) < Rules.MaxSkill).ToList();
                        if (open.Count == 0) return null;
                        skill = open[rng.Next(open.Count)];
                    }
                    else skill = Rules.SkillIndex(d.Skill);
                    level = s.SkillLevel(skill);
                    slot.N["s"] = skill;
                    slot.N["lv"] = level;
                    break;
                }
                case "careerskill":
                {
                    var open = CareerSkills(s);
                    if (open.Count == 0) return null;
                    var skill = open[rng.Next(open.Count)];
                    level = s.SkillLevel(skill);
                    slot.N["s"] = skill;
                    slot.N["lv"] = level;
                    break;
                }
                case "skillmax":
                {
                    var open = MaxCandidates(d, s);
                    if (open.Count == 0) return null;
                    var skill = open.OrderByDescending(i => s.Skills[i]).First();
                    slot.N["s"] = skill;
                    break;
                }
                case "promotion":
                case "demoted":
                    slot.N["jt"] = s.JobType;
                    slot.N["jl"] = s.JobLevel;
                    level = s.JobLevel;
                    break;
                case "topofcareer":
                case "performance":
                case "fired":
                case "lowperformance":
                    slot.N["jt"] = s.JobType;
                    break;
                case "childgrades":
                    slot.Kids = s.ChildGrades.Where(x => x.Value < T(d, 60)).Select(x => x.Key).ToList();
                    break;
                case "childbadgrades":
                    slot.Kids = s.ChildGrades.Where(x => x.Value > T(d, -20)).Select(x => x.Key).ToList();
                    break;
                case "earn":
                    slot.N["f"] = s.Funds;
                    slot.N["a"] = Rules.Round(Math.Max(10, d.Amount + d.Fraction * Math.Max(0, s.Funds)));
                    break;
                case "havefunds":
                    slot.N["a"] = FundsTarget(d, s);
                    break;
                case "buy":
                    slot.N["a"] = Math.Max(0, d.Amount);
                    break;
                case "friends":
                    slot.N["t"] = FriendsTarget(d, s);
                    break;
                case "newfriend":
                case "losefriend":
                    slot.Who = s.Friends.ToList();
                    break;
                case "bestfriend":
                    slot.Who = s.BestFriends.ToList();
                    break;
                case "fallinlove":
                    slot.Who = s.Loves.ToList();
                    break;
                case "newhousemate":
                    slot.N["n"] = s.HouseholdAdults;
                    break;
                case "losemember":
                    slot.N["n"] = s.HouseholdSize;
                    break;
            }
            slot.Points = Math.Max(0, d.Points + d.PerLevel * level);
            slot.Text = Words(d, slot, s);
            return slot;
        }

        /// <summary>
        /// Whether the goal has come true. A goal that counts different people over time (a lifetime want to kiss ten
        /// Sims) notes each one in its slot as they come.
        /// </summary>
        public static bool Met(GoalDef d, Slot slot, SimState s, IList<Happening> h, Balance b)
        {
            switch (K(d))
            {
                case "skillup":
                case "careerskill":
                    return s.SkillLevel(slot.Get("s")) > slot.Get("lv");
                case "skillmax":
                    return s.SkillLevel(slot.Get("s")) >= Rules.MaxSkill;
                case "skillsmaxed":
                    return s.SkillsMaxed >= Math.Max(1, d.Total);
                case "getjob":
                    return s.HasJob;
                case "promotion":
                    return s.JobType == slot.Get("jt") && s.JobLevel > slot.Get("jl");
                case "topofcareer":
                    return s.JobType == slot.Get("jt") && s.AtTop;
                case "careertop":
                    return s.AtTop;
                case "performance":
                    return s.JobType == slot.Get("jt") && s.Performance >= T(d, 60);
                case "grades":
                    return s.Child && s.Performance >= T(d, 60);
                case "lowperformance":
                    return (s.Child || s.JobType == slot.Get("jt")) && s.Performance <= T(d, -50);
                case "childgrades":
                    return slot.Kids.Any(k => s.ChildGrades.TryGetValue(k, out var g) && g >= T(d, 60));
                case "childbadgrades":
                    return slot.Kids.Any(k => s.ChildGrades.TryGetValue(k, out var g) && g <= T(d, -20));
                case "fired":
                    return slot.Get("jt") != 0 && s.JobType == 0;
                case "demoted":
                    return s.JobType == slot.Get("jt") && s.JobLevel < slot.Get("jl");
                case "earn":
                    return s.Funds >= (long)slot.Get("f") + slot.Get("a");
                case "havefunds":
                    return s.Funds >= slot.Get("a");
                case "buy":
                    return h.Any(x => x.Kind == HappeningKind.Purchase && x.Price >= slot.Get("a"));
                case "broke":
                    return s.Funds < Math.Max(0, d.Amount);
                case "newfriend":
                    return s.Friends.Any(x => !slot.Who.Contains(x));
                case "friends":
                    return s.Friends.Count >= slot.Get("t");
                case "bestfriend":
                    return s.BestFriends.Any(x => !slot.Who.Contains(x));
                case "fallinlove":
                    return d.Total > 0 ? s.Loves.Count >= d.Total : s.Loves.Any(x => !slot.Who.Contains(x));
                case "losefriend":
                    return slot.Who.Any(x => !s.Friends.Contains(x));
                case "newhousemate":
                    return s.HouseholdAdults > slot.Get("n");
                case "baby":
                    return h.Any(x => x.Kind == HappeningKind.Baby);
                case "children":
                    return s.HouseholdChildren >= Math.Max(1, d.Total);
                case "losemember":
                    //someone who joins since counts as well
                    if (s.HouseholdSize > slot.Get("n")) slot.N["n"] = s.HouseholdSize;
                    return s.HouseholdSize < slot.Get("n");
                case "interaction":
                {
                    var matching = h.Where(x => x.Kind == HappeningKind.Done && Matches(d, x)).ToList();
                    if (d.Total <= 0) return matching.Count > 0;
                    foreach (var x in matching) if (x.Person != 0 && !slot.Who.Contains(x.Person)) slot.Who.Add(x.Person);
                    return slot.Who.Count >= d.Total;
                }
                case "received":
                    return h.Any(x => x.Kind == HappeningKind.Received && Named(d, x.Name));
                case "party":
                    return s.AtHome && s.Guests >= Math.Max(1, d.Count);
                case "outing":
                    return !s.AtHome;
                case "needhigh":
                    return s.Need(Rules.NeedIndex(d.Need)) >= T(d, 80);
                case "needlow":
                    return s.Need(Rules.NeedIndex(d.Need)) <= T(d, -80);
                case "fire":
                    return s.Fire;
                case "visitor":
                    return Seen(d, s);
                default:
                    return false;
            }
        }

        /// <summary>Whether a goal no longer makes sense (a want to be promoted, by a Sim who has changed careers) and should be rolled again.</summary>
        public static bool Stale(GoalDef d, Slot slot, SimState s)
        {
            switch (K(d))
            {
                case "promotion":
                case "topofcareer":
                case "performance":
                case "demoted":
                    return s.JobType != slot.Get("jt");
                case "lowperformance":
                    return !s.Child && s.JobType != slot.Get("jt");
                case "fired":
                    //a new job in place of the old one, rather than none
                    return s.JobType != 0 && s.JobType != slot.Get("jt");
                case "careerskill":
                    return !s.HasJob;
                case "childgrades":
                case "childbadgrades":
                    return !slot.Kids.Any(k => s.ChildGrades.ContainsKey(k));
                default:
                    return false;
            }
        }

        /// <summary>Two goals a Sim may not hold at once: the same goal, about the same skill.</summary>
        public static string Key(Slot slot) => slot.Id + (slot.N != null && slot.N.TryGetValue("s", out var skill) ? "/" + skill : "");

        // ---- the words ----

        public static string Words(GoalDef d, Slot slot, SimState s)
        {
            var text = d.Text ?? d.Id;
            if (slot.N.TryGetValue("s", out var skill) && skill >= 0 && skill < Rules.SkillNames.Length)
                text = text.Replace("{skill}", Rules.SkillNames[skill]);
            else text = text.Replace("{skill}", d.Skill ?? "a skill");
            var amount = slot.N.TryGetValue("a", out var a) ? a : d.Amount;
            text = text.Replace("{amount}", Rules.Money(amount));
            text = text.Replace("{count}", Rules.Number(slot.N.TryGetValue("t", out var t) ? t : Math.Max(1, d.Count)));
            text = text.Replace("{total}", Rules.Number(d.Total));
            text = text.Replace("{threshold}", Rules.Number(d.Threshold));
            text = text.Replace("{career}", string.IsNullOrEmpty(s?.Career) ? "their" : s.Career);
            return text;
        }

        // ---- helpers ----

        /// <summary>Skills the Sim's next promotion asks for more of, that they can still learn.</summary>
        private static List<int> CareerSkills(SimState s)
        {
            var result = new List<int>();
            if (!s.HasJob || s.AtTop || s.NextSkills == null) return result;
            for (int i = 0; i < Rules.SkillNames.Length && i < s.NextSkills.Length; i++)
                if (s.NextSkills[i] > s.Skills[i] && s.SkillLevel(i) < Rules.MaxSkill) result.Add(i);
            return result;
        }

        private static List<int> MaxCandidates(GoalDef d, SimState s)
        {
            var from = d.Threshold != 0 ? d.Threshold : 8;
            return Enumerable.Range(0, Rules.SkillNames.Length).Where(i => s.SkillLevel(i) >= from && s.SkillLevel(i) < Rules.MaxSkill).ToList();
        }

        private static int FundsTarget(GoalDef d, SimState s)
        {
            if (d.Total > 0) return d.Total;
            //the first milestone comfortably above what the household has
            foreach (var m in d.Milestones) if (m > s.Funds * 1.1 + 500) return m;
            return 0;
        }

        private static int FriendsTarget(GoalDef d, SimState s) => d.Total > 0 ? d.Total : s.Friends.Count + Math.Max(1, d.Count);

        private static bool Named(GoalDef d, string name) => !string.IsNullOrEmpty(name) && d.Match.Any(m => name.Contains(m));

        private static bool Seen(GoalDef d, SimState s) => s.Visitors.Any(v => Named(d, v));

        public static bool Matches(GoalDef d, Happening x)
        {
            if (!Named(d, x.Name)) return false;
            switch ((d.With ?? "any").ToLowerInvariant())
            {
                case "person": if (x.With != Partner.Household && x.With != Partner.Other) return false; break;
                case "household": if (x.With != Partner.Household) return false; break;
                case "other": if (x.With != Partner.Other) return false; break;
                case "object": if (x.With != Partner.Object) return false; break;
            }
            switch ((d.Outcome ?? "any").ToLowerInvariant())
            {
                case "accepted": return x.Outcome > 0;
                case "rejected": return x.Outcome < 0;
                default: return true;
            }
        }
    }
}
