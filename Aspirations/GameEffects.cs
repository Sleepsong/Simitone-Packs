/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Aspirations
{
    /// <summary>
    /// Rewards and levels, done to the game: needs filled and kept full, skills learned faster, money paid, job
    /// performance set, relationships changed, and needs falling faster or slower with the Sim's level.
    /// Effects.cs decides what is to be done; this does it.
    /// </summary>
    public static class GameEffects
    {
        // ---- a reward, used now ----

        public static void Apply(VM vm, VMAvatar avatar, RewardResult result)
        {
            var e = result.Effect;
            if (e == null) return;
            switch (e.Kind.ToLowerInvariant())
            {
                case "needs":
                case "freeze":
                    foreach (var i in Effects.Needs(e))
                    {
                        var motive = Watcher.NeedData[i];
                        var now = avatar.GetMotiveData(motive);
                        int to = now;
                        if (e.Set != null) to = Math.Max(now, e.Set.Value);
                        if (e.Add != null) to = now + e.Add.Value;
                        avatar.SetMotiveData(motive, (short)Math.Clamp(to, -100, 100));
                    }
                    break;
                case "skillpoint":
                    if (result.Skill >= 0)
                    {
                        var v = Watcher.SkillVariable(result.Skill);
                        var raw = avatar.GetPersonData(v);
                        avatar.SetPersonData(v, (short)Math.Min(Rules.MaxSkill * 100, raw + 100 * Math.Max(1, e.Points)));
                        Watcher.Resync(vm, avatar);
                    }
                    break;
                case "money":
                {
                    var family = vm.TS1State?.CurrentFamily;
                    if (family != null) family.Budget = Math.Max(0, family.Budget + e.Amount);
                    break;
                }
                case "performance":
                {
                    var now = avatar.GetPersonData(VMPersonDataVariable.JobPerformance);
                    int to = now;
                    if (e.Set != null) to = e.Set.Value;
                    if (e.Add != null) to = now + e.Add.Value;
                    avatar.SetPersonData(VMPersonDataVariable.JobPerformance, (short)Math.Clamp(to, -100, 100));
                    break;
                }
                case "charm":
                    Charm(vm, avatar, e.Daily, e.Lifetime);
                    break;
            }
        }

        /// <summary>Everyone else on the lot, and the Sim, get on better (or worse) with each other.</summary>
        private static void Charm(VM vm, VMAvatar avatar, int daily, int lifetime)
        {
            var hood = Content.Get()?.Neighborhood;
            if (hood == null) return;
            var me = avatar.GetPersonData(VMPersonDataVariable.NeighborId);
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                var other = entity as VMAvatar;
                if (other == null || other == avatar || other.Dead || other.IsPet) continue;
                var them = other.GetPersonData(VMPersonDataVariable.NeighborId);
                if (them <= 0) continue;
                Change(hood, me, them, daily, lifetime);
                Change(hood, them, me, daily, lifetime);
            }
        }

        private static void Change(TS1NeighborhoodProvider hood, short from, short to, int daily, int lifetime)
        {
            var neighbour = hood.GetNeighborByID(from);
            if (neighbour == null) return;
            neighbour.Relationships ??= new Dictionary<int, List<short>>();
            if (!neighbour.Relationships.TryGetValue(to, out var values))
            {
                values = new List<short>();
                neighbour.Relationships[to] = values;
            }
            while (values.Count < 1) values.Add(0);
            values[0] = (short)Math.Clamp(values[0] + daily, -100, 100);
            //the lifetime number only where the game keeps one already
            if (values.Count > 2 && lifetime != 0) values[2] = (short)Math.Clamp(values[2] + lifetime, -100, 100);
        }

        // ---- what lasts ----

        /// <summary>Needs a reward keeps full do not fall below where it keeps them. Called every tick.</summary>
        public static void Hold(VMAvatar avatar, SimRecord record)
        {
            if (record == null || record.Effects.Count == 0) return;
            foreach (var effect in record.Effects)
            {
                if (effect.Kind != "Freeze") continue;
                foreach (var name in effect.Needs)
                {
                    var i = Rules.NeedIndex(name);
                    if (i < 0 || i >= Rules.RoomNeed) continue;
                    var motive = Watcher.NeedData[i];
                    if (avatar.GetMotiveData(motive) < effect.Value) avatar.SetMotiveData(motive, (short)Math.Clamp(effect.Value, -100, 100));
                }
            }
        }

        /// <summary>
        /// Skills learned faster: whatever the Sim gained since the last look, a share more. The share left over
        /// from rounding is kept, so small gains add up.
        /// </summary>
        public static void Learn(VMAvatar avatar, SimRecord record, int[] before, int[] now, ref double carry)
        {
            var effect = record?.Effects.FirstOrDefault(x => x.Kind == "SkillGain");
            if (effect == null || effect.Times <= 1) { carry = 0; return; }
            for (int i = 0; i < now.Length && i < before.Length; i++)
            {
                var gained = now[i] - before[i];
                if (gained <= 0) continue;
                carry += gained * (effect.Times - 1);
                var extra = (int)Math.Floor(carry);
                if (extra <= 0) continue;
                carry -= extra;
                var v = Watcher.SkillVariable(i);
                avatar.SetPersonData(v, (short)Math.Min(Rules.MaxSkill * 100, avatar.GetPersonData(v) + extra));
            }
        }

        /// <summary>Nine in the morning: each dividend pays.</summary>
        public static void PayDividends(VMAvatar avatar, SimRecord record, FAMI family)
        {
            foreach (var effect in record.Effects.Where(x => x.Kind == "Dividend" && x.Left > 0))
            {
                family.Budget += effect.Value;
                effect.Left--;
                Log.Write(avatar.Name + "'s " + effect.Name + " paid " + Rules.Money(effect.Value) + " (" + effect.Left + " to come).");
                AspirationsPack.Notice(avatar.Name + "'s " + effect.Name + ": " + Rules.Money(effect.Value) + ".", NoticeKind.Reward);
            }
        }

        /// <summary>A breakdown, on falling into aspiration failure: needs drop, as balance.json says.</summary>
        public static void Breakdown(VMAvatar avatar, Balance b)
        {
            foreach (var pair in b.Meter.Failure.Breakdown)
            {
                var i = Rules.NeedIndex(pair.Key);
                if (i < 0 || i >= Rules.RoomNeed) continue;
                var motive = Watcher.NeedData[i];
                avatar.SetMotiveData(motive, (short)Math.Clamp(avatar.GetMotiveData(motive) + pair.Value, -100, 100));
            }
        }

        // ---- needs falling faster or slower ----

        private class Rate
        {
            public double Times = 1;
            public double[] Carry = new double[7];
        }

        private static readonly ConditionalWeakTable<VMAvatar, Rate> Rates = new ConditionalWeakTable<VMAvatar, Rate>();

        public static void SetNeedDecay(VMAvatar avatar, double times)
        {
            var rate = Rates.GetValue(avatar, x => new Rate());
            if (rate.Times != times) rate.Times = Math.Clamp(times, 0, 3);
        }

        public static double NeedDecay(VMAvatar avatar) => Rates.TryGetValue(avatar, out var rate) ? rate.Times : 1;

        /// <summary>Before the game lets a Sim's needs fall: note where they are.</summary>
        public static void BeforeDecay(VMAvatar avatar, out short[] __state)
        {
            __state = null;
            if (avatar == null || !Rates.TryGetValue(avatar, out var rate) || rate.Times == 1) return;
            __state = new short[VMTS1MotiveDecay.DecrementMotives.Length];
            for (int i = 0; i < __state.Length; i++) __state[i] = avatar.GetMotiveData(VMTS1MotiveDecay.DecrementMotives[i]);
        }

        /// <summary>
        /// After: whatever each need fell by, a share more (or less) at this level. Only what the game's own decay
        /// took is changed, so food eaten or a nap taken in the same moment is not. The share left over from
        /// rounding is kept for next time.
        /// </summary>
        public static void AfterDecay(VMAvatar avatar, short[] __state)
        {
            if (__state == null || !Rates.TryGetValue(avatar, out var rate)) return;
            for (int i = 0; i < __state.Length; i++)
            {
                var motive = VMTS1MotiveDecay.DecrementMotives[i];
                var after = avatar.GetMotiveData(motive);
                var fell = __state[i] - after;
                if (fell <= 0) continue;
                rate.Carry[i] += fell * (rate.Times - 1);
                var whole = (int)Math.Truncate(rate.Carry[i]);
                if (whole == 0) continue;
                rate.Carry[i] -= whole;
                //never back above where it was: slower, not rising
                var to = Math.Clamp(after - whole, -100, __state[i]);
                avatar.SetMotiveData(motive, (short)to);
            }
        }
    }
}
