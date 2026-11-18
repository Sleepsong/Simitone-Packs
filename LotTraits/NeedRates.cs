/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LotTraits
{
    /// <summary>
    /// How a lot changes a Sim's needs: how fast each of the seven needs that drop on their own drops, compared with
    /// the game's own rate, and points added to Room.
    /// </summary>
    public sealed class NeedEffect
    {
        /// <summary>One for each of the engine's dropping needs, in its order (VMTS1MotiveDecay.DecrementMotives). 1 is the game's own rate.</summary>
        public readonly float[] Rates;
        /// <summary>Added to Room, from -100 to 100 like any need.</summary>
        public int Room;

        public NeedEffect()
        {
            Rates = Enumerable.Repeat(1f, Needs.Count).ToArray();
        }

        /// <summary>The game's own rates and no Room points. Asked for every Sim on every tick, so it allocates nothing.</summary>
        public bool IsNone
        {
            get
            {
                if (Room != 0) return false;
                for (int i = 0; i < Rates.Length; i++) if (Rates[i] != 1f) return false;
                return true;
            }
        }

        public static readonly NeedEffect None = new NeedEffect();

        /// <summary>Several effects at once: their rates multiply and their Room points add up.</summary>
        public static NeedEffect Combine(IEnumerable<NeedEffect> effects)
        {
            var all = new NeedEffect();
            foreach (var effect in effects)
            {
                if (effect == null) continue;
                for (int i = 0; i < all.Rates.Length; i++) all.Rates[i] *= effect.Rates[i];
                all.Room += effect.Room;
            }
            all.Room = Math.Clamp(all.Room, -100, 100);
            return all;
        }
    }

    /// <summary>The needs, by the names the pack uses for them.</summary>
    public static class Needs
    {
        /// <summary>The needs that drop on their own, in the engine's order. Read from the engine, so that the order is the one it uses.</summary>
        public static readonly VMMotive[] Dropping = VMTS1MotiveDecay.DecrementMotives;

        public static int Count => Dropping.Length;

        public static string Name(VMMotive motive) => motive.ToString();

        /// <summary>Where a need is in Dropping, by its name ("hygiene"), or -1.</summary>
        public static int IndexOf(string name)
        {
            for (int i = 0; i < Dropping.Length; i++)
                if (string.Equals(Dropping[i].ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    /// <summary>
    /// The one patch most lot traits need, and moodlets need too: the rate at which needs drop.
    ///
    /// In The Sims 1 a Sim's needs drop in one place in the engine, VMTS1MotiveDecay.Tick, every second game minute.
    /// It keeps each need as whole points and, beside them, a fraction of a point in thousandths. The patch notes both
    /// before the game's own code runs and looks again after it: whatever the game took away from a need, the patch
    /// makes into that times the rate. So the game's own rules stay as they are (hunger drains faster the fuller a Sim
    /// is, bladder follows hunger, a sleeping Sim's hygiene drops slower, needs under an interaction's motive change
    /// do not drop at all), and only the amount changes.
    ///
    /// A need that rose instead (Energy, while a Sim sleeps) is left alone: these are rates of dropping, not of
    /// rest.
    ///
    /// Room is set afresh by the same method every tick from the room the Sim is in; the patch adds its points after.
    /// Mood is the average of the other eight, worked out at the end of the method; when the patch changes any of
    /// them, it works Mood out again the way the method does.
    ///
    /// Other packs may make the same patch (a moodlets pack would). Since each looks at what the game's code and
    /// the packs before it have done, and scales that, their rates multiply and their Room points add up, in
    /// whichever order they run.
    /// </summary>
    public static class NeedRates
    {
        /// <summary>The effect on a Sim: set by the pack. Null, or NeedEffect.None, means the game's own rates.</summary>
        public static Func<VMAvatar, VMContext, NeedEffect> EffectFor;

        //what each need was before the game's code ran, for the tick under way. one tick at a time on the game thread
        [ThreadStatic] private static short[] MotivesBefore;
        [ThreadStatic] private static short[] FractionsBefore;
        [ThreadStatic] private static NeedEffect Effect;
        [ThreadStatic] private static int MinuteBefore;

        public static void BeforeDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, VMContext context, out bool __state)
        {
            __state = false;
            if (EffectFor == null || avatar == null || __instance?.MotiveFractions == null) return;
            var effect = EffectFor(avatar, context);
            if (effect == null || effect.IsNone) return;
            var count = Math.Min(Needs.Count, __instance.MotiveFractions.Length);
            if (MotivesBefore == null || MotivesBefore.Length < count)
            {
                MotivesBefore = new short[count];
                FractionsBefore = new short[count];
            }
            for (int i = 0; i < count; i++)
            {
                MotivesBefore[i] = avatar.GetMotiveData(Needs.Dropping[i]);
                FractionsBefore[i] = __instance.MotiveFractions[i];
            }
            Effect = effect;
            MinuteBefore = __instance.LastMinute;
            __state = true;
        }

        public static void AfterDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, bool __state)
        {
            if (!__state) return;
            var effect = Effect;
            Effect = null;
            var count = Math.Min(Needs.Count, __instance.MotiveFractions.Length);
            var changed = false;

            //the game only lets needs drop every second game minute; on the other ticks there is nothing to scale
            if (__instance.LastMinute != MinuteBefore)
            {
                for (int i = 0; i < count; i++)
                {
                    var rate = effect.Rates[i];
                    if (rate == 1f) continue;
                    var motive = avatar.GetMotiveData(Needs.Dropping[i]);
                    var fraction = __instance.MotiveFractions[i];
                    if (!Scale(MotivesBefore[i], FractionsBefore[i], ref motive, ref fraction, rate)) continue;
                    __instance.MotiveFractions[i] = fraction;
                    avatar.SetMotiveData(Needs.Dropping[i], motive);
                    changed = true;
                }
            }

            if (effect.Room != 0)
            {
                var room = avatar.GetMotiveData(VMMotive.Room);
                avatar.SetMotiveData(VMMotive.Room, (short)Math.Clamp(room + effect.Room, -100, 100));
                changed = true;
            }

            //Mood is only worked out on the ticks the needs drop; on the others it stays as it was
            if (changed && __instance.LastMinute != MinuteBefore) avatar.SetMotiveData(VMMotive.Mood, MoodOf(avatar));
        }

        /// <summary>
        /// Mood as VMTS1MotiveDecay.Tick works it out: the dropping needs and Room, over eight. A need under an
        /// interaction's motive change is left out of the sum (but not the eight), except Energy, as the game does.
        /// </summary>
        public static short MoodOf(VMAvatar avatar)
        {
            int sum = 0;
            foreach (var need in Needs.Dropping)
            {
                if (avatar.HasMotiveChange(need) && need != VMMotive.Energy) continue;
                sum += avatar.GetMotiveData(need);
            }
            sum += avatar.GetMotiveData(VMMotive.Room);
            return (short)(sum / 8);
        }

        /// <summary>
        /// One need, through one tick of the game's own decay, scaled by a rate. The engine keeps the need as whole
        /// points and a fraction in thousandths, which together are one number, the thousandths drained so far:
        /// fraction - 1000 * points. What the tick drained is how much that grew; the rate scales it, and the result
        /// is put back as points and a fraction from 0 to 999, as the engine keeps it. Like the engine, it never takes
        /// a need below -100.
        ///
        /// True if the need was changed. A need the tick did not drain (it rose, or was left alone, or was already at
        /// the bottom) is not changed.
        /// </summary>
        public static bool Scale(short motiveBefore, short fractionBefore, ref short motive, ref short fraction, float rate)
        {
            if (rate == 1f || rate < 0 || float.IsNaN(rate)) return false;
            long before = fractionBefore - 1000L * motiveBefore;
            long after = fraction - 1000L * motive;
            long drained = after - before;
            if (drained <= 0) return false;

            long scaled = before + (long)Math.Round(drained * (double)rate, MidpointRounding.AwayFromZero);
            long points = FloorDiv(scaled, 1000);
            //never above where the need was before the tick, nor below the engine's bottom
            long newMotive = Math.Clamp(-points, -100, Math.Max(-100L, Math.Max((long)motiveBefore, motive)));
            long newFraction = scaled - 1000 * points;
            if (newMotive == motive && newFraction == fraction) return false;
            motive = (short)newMotive;
            fraction = (short)newFraction;
            return true;
        }

        private static long FloorDiv(long a, long b)
        {
            var q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
