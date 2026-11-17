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
    /// <summary>What came of using a reward.</summary>
    public class RewardResult
    {
        public bool Backfired;
        /// <summary>The effect to put on the Sim now, if any: the reward's own, or its backfire.</summary>
        public EffectDef Effect;
        /// <summary>The skill a skill point goes to.</summary>
        public int Skill = -1;
        public string Text;
    }

    /// <summary>
    /// The Sims 2's rewards are objects, with animations of their own, which The Sims has not got. Here a reward is
    /// an effect, bought with the points wants earn. This is what each kind of effect means, and the rules for using
    /// one; GameEffects.cs puts them on a Sim in the game.
    ///
    ///   Needs       fill needs (Set), or change them (Add)
    ///   Freeze      fill needs, and keep them from falling for some hours
    ///   SkillGain   skills are learned so many Times as fast, for some hours
    ///   SkillPoint  a skill point (or Points of them) in a skill the player chooses
    ///   Money       money for the household now (Amount; less than nothing takes it away)
    ///   Dividend    money each morning at nine (Amount), for some Days
    ///   Performance job performance, or a child's grades, Set or changed by Add
    ///   Charm       everyone else on the lot gets on better with the Sim: Daily and Lifetime relationship
    ///   Voucher     the next object bought, up to Amount, costs nothing
    ///   Meter       the aspiration meter lifted to Set, if lower
    ///   Nothing     nothing at all
    ///
    /// As with The Sims 2's reward objects, a Sim in a low mood of aspiration risks it going wrong: the chance is
    /// the level's "backfire", and then the reward's own backfire happens instead.
    /// </summary>
    public static class Effects
    {
        private static readonly HashSet<string> Kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Needs", "Freeze", "SkillGain", "SkillPoint", "Money", "Dividend", "Performance", "Charm", "Voucher", "Meter", "Nothing",
        };

        public static bool Known(string kind) => kind != null && Kinds.Contains(kind);

        public static IEnumerable<string> AllKinds => Kinds;

        /// <summary>Lasting effects, kept on the Sim's record until they wear off.</summary>
        public static bool Lasts(EffectDef e) => e != null && (Is(e, "Freeze") || Is(e, "SkillGain") || Is(e, "Dividend") || Is(e, "Voucher"));

        public static bool Is(EffectDef e, string kind) => string.Equals(e?.Kind, kind, StringComparison.OrdinalIgnoreCase);

        /// <summary>The chance, from 0 to 1, that the reward goes wrong for this Sim now.</summary>
        public static double Risk(RewardDef reward, SimRecord r, Balance b) => reward.Safe ? 0 : Math.Clamp(Rules.Level(r.Meter, b).Backfire, 0, 1);

        /// <summary>Hours until the Sim can use the reward again; 0 if they can now.</summary>
        public static double CooldownLeft(RewardDef reward, SimRecord r)
        {
            if (reward.Cooldown <= 0 || !r.LastUsed.TryGetValue(reward.Id, out var last)) return 0;
            var left = last + reward.Cooldown * 60 - r.Minutes;
            return left > 0 ? left / 60.0 : 0;
        }

        /// <summary>Why the Sim cannot use the reward now, or null if they can. The skill is for a skill point, -1 for any.</summary>
        public static string CannotUse(RewardDef reward, SimRecord r, SimState s, Balance b, int skill = -1)
        {
            if (!Rules.AgeFits(reward.Ages, s.Child)) return s.Child ? "Only for grown Sims." : "Only for children.";
            if (r.Points < reward.Cost) return "Needs " + Rules.Number(reward.Cost - r.Points) + " more points.";
            var wait = CooldownLeft(reward, r);
            if (wait > 0) return "Again in " + Hours(wait) + ".";
            var e = reward.Effect;
            if (Lasts(e) && r.Effects.Any(x => x.Reward == reward.Id)) return "Already in effect.";
            if (Is(e, "Voucher") && r.Effects.Any(x => x.Kind == "Voucher")) return "A voucher is waiting to be used.";
            if (Is(e, "Performance") && !s.Child && !s.HasJob) return "Needs a job.";
            if (Is(e, "Performance") && e.Set != null && s.Performance >= e.Set.Value) return s.Child ? "Grades are as good as that already." : "Already doing that well.";
            if (Is(e, "Charm") && s.Others <= 0) return "Needs someone else on the lot.";
            if (Is(e, "Meter") && e.Set != null && r.Meter >= e.Set.Value) return "The meter is higher than that already.";
            if (Is(e, "SkillPoint"))
            {
                if (skill >= 0 && skill < Rules.SkillNames.Length && s.SkillLevel(skill) >= Rules.MaxSkill) return Rules.SkillNames[skill] + " is at its best already.";
                if (s.Skills.All(x => x / 100 >= Rules.MaxSkill)) return "Every skill is at its best already.";
            }
            if (Is(e, "Needs") && e.Set != null && Needs(e).All(i => s.Need(i) >= e.Set.Value)) return "Those needs are full already.";
            return null;
        }

        /// <summary>
        /// Use a reward: the points are spent, chance decides whether it works, and a lasting effect is noted on
        /// the record. What has to be done to the game now is in the result, for GameEffects.cs.
        /// </summary>
        public static RewardResult Use(RewardDef reward, SimRecord r, SimState s, Balance b, Random rng, int skill = -1)
        {
            var result = new RewardResult();
            r.Points -= reward.Cost;
            r.LastUsed[reward.Id] = r.Minutes;
            r.RewardsUsed++;
            result.Backfired = rng.NextDouble() < Risk(reward, r, b);
            var e = result.Backfired ? reward.Backfire : reward.Effect;
            result.Effect = e;
            if (e == null || Is(e, "Nothing"))
            {
                result.Text = result.Backfired ? reward.Name + " went wrong, and nothing came of it." : reward.Name + ": nothing happened.";
                return result;
            }
            if (Is(e, "SkillPoint")) result.Skill = SkillFor(s, skill);

            if (Lasts(e))
            {
                var lasting = new ActiveEffect()
                {
                    Reward = reward.Id,
                    Name = reward.Name,
                    Kind = Kinds.First(k => string.Equals(k, e.Kind, StringComparison.OrdinalIgnoreCase)),
                    Needs = Needs(e).Select(i => Rules.NeedNames[i]).ToList(),
                    Until = e.Hours > 0 ? r.Minutes + (long)Math.Round(e.Hours * 60) : 0,
                    Times = e.Times,
                    Value = e.Set ?? 100,
                    Left = Is(e, "Dividend") ? Math.Max(1, e.Days) : 0,
                };
                if (Is(e, "Voucher") || Is(e, "Dividend")) lasting.Value = e.Amount;
                r.Effects.Add(lasting);
            }
            if (Is(e, "Meter") && e.Set != null) r.Meter = Rules.Clamp(Math.Max(r.Meter, e.Set.Value));

            result.Text = !string.IsNullOrEmpty(e.Text)
                ? e.Text.Replace("{skill}", result.Skill >= 0 ? Rules.SkillNames[result.Skill] : "a skill")
                : result.Backfired ? reward.Name + " went wrong." : reward.Name + " worked.";
            return result;
        }

        /// <summary>The skill a skill point goes to: the one asked for, or else the one nearest its next point.</summary>
        public static int SkillFor(SimState s, int asked)
        {
            if (asked >= 0 && asked < Rules.SkillNames.Length && s.SkillLevel(asked) < Rules.MaxSkill) return asked;
            var open = Enumerable.Range(0, Rules.SkillNames.Length).Where(i => s.SkillLevel(i) < Rules.MaxSkill).ToList();
            if (open.Count == 0) return -1;
            return open.OrderByDescending(i => s.Skills[i] % 100).ThenBy(i => i).First();
        }

        /// <summary>The needs an effect is about, by index. "All" is the seven needs of the game, not the room or mood.</summary>
        public static List<int> Needs(EffectDef e)
        {
            var result = new List<int>();
            foreach (var name in e?.Needs ?? new List<string>())
            {
                if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < Rules.RoomNeed; i++) if (!result.Contains(i)) result.Add(i);
                    continue;
                }
                var index = Rules.NeedIndex(name);
                if (index >= 0 && index < Rules.RoomNeed && !result.Contains(index)) result.Add(index);
            }
            return result;
        }

        /// <summary>How long a lasting effect has left, in words.</summary>
        public static string Remaining(ActiveEffect e, SimRecord r)
        {
            if (e.Kind == "Voucher") return "up to " + Rules.Money(e.Value);
            if (e.Kind == "Dividend") return e.Left + (e.Left == 1 ? " payment" : " payments") + " of " + Rules.Money(e.Value);
            if (e.Until <= 0) return "";
            return Hours(Math.Max(0, e.Until - r.Minutes) / 60.0) + " left";
        }

        public static string Hours(double hours)
        {
            if (hours < 1) return Math.Max(1, (int)Math.Ceiling(hours * 60)) + " min";
            var whole = (int)Math.Ceiling(hours);
            return whole + (whole == 1 ? " hour" : " hours");
        }
    }
}
