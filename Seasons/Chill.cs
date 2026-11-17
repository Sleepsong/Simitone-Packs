/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics.Model;
using System;

namespace Seasons
{
    /// <summary>
    /// Wet and cold Sims. The Sims has no temperature and no moodlets, so the weather reaches a Sim the one way the game
    /// has: their needs drop faster.
    ///
    ///  - Wet: outside in the rain. Comfort drops twice as fast (wet clothes), Hygiene half as fast again (mud).
    ///    A Sim stays wet for an hour after coming in.
    ///  - Cold: outside in winter, or while it snows, or on a lot that is snowy all year. Comfort, Energy and Hunger
    ///    drop half as fast again (keeping warm). Coming in warms a Sim at once.
    ///
    /// Both at once add up: a Sim outside in winter rain loses Comfort two and a half times as fast. Only needs that are
    /// dropping are touched: a Sim sitting in a comfortable chair in the rain still gains comfort, and a sleeping Sim
    /// still gains energy.
    /// </summary>
    public static class Chill
    {
        /// <summary>The needs the game's own decay works on, in its order (VMTS1MotiveDecay.DecrementMotives).</summary>
        public static readonly VMMotive[] Needs =
        {
            VMMotive.Hunger, VMMotive.Comfort, VMMotive.Hygiene, VMMotive.Bladder, VMMotive.Energy, VMMotive.Fun, VMMotive.Social
        };

        /// <summary>How much faster each need drops when wet, in the order of Needs: 1 is twice as fast.</summary>
        public static readonly float[] WetExtra = { 0, 1f, 0.5f, 0, 0, 0, 0 };
        /// <summary>The same, when cold.</summary>
        public static readonly float[] ColdExtra = { 0.5f, 0.5f, 0, 0, 0.5f, 0, 0 };

        /// <summary>How long a Sim stays wet after coming in, in game minutes.</summary>
        public const int DryingMinutes = 60;

        public static float Extra(int need, bool wet, bool cold)
        {
            return (wet ? WetExtra[need] : 0) + (cold ? ColdExtra[need] : 0);
        }

        /// <summary>
        /// What one Sim is going through, kept beside the Sim for as long as the Sim is on the lot. Not saved: a Sim who
        /// was wet when the game was saved is dry when it loads.
        /// </summary>
        public class State
        {
            /// <summary>The game minutes of drying still to go: above 0, the Sim is wet.</summary>
            public int Drying;
            public bool Cold;
            /// <summary>Need lost to the weather and not yet taken off, in thousandths of a point, as the game keeps its own.</summary>
            public int[] Owed = new int[7];

            public bool Wet => Drying > 0;

            /// <summary>Where the Sim is now: outside or not, and what is falling. minutes: the game minutes since this was last called.</summary>
            public void Update(bool outside, bool raining, bool coldThere, int minutes)
            {
                if (outside && raining) Drying = DryingMinutes;
                else if (Drying > 0) Drying = Math.Max(0, Drying - minutes);
                Cold = outside && coldThere;
            }

            /// <summary>
            /// The game has just taken its own decay off a need: how much more to take off for the weather, in whole
            /// points. decay: what the game took, in thousandths of a point.
            /// </summary>
            public int Take(int need, int decay)
            {
                if (decay <= 0) return 0;
                var extra = Extra(need, Wet, Cold);
                if (extra <= 0) return 0;
                Owed[need] += (int)Math.Round(decay * extra);
                var whole = Owed[need] / 1000;
                Owed[need] %= 1000;
                return whole;
            }
        }

        /// <summary>
        /// A need as one number, in thousandths of a point, from the game's whole points and the fraction it has used
        /// up of the next one (VMTS1MotiveDecay.MotiveFractions).
        /// </summary>
        public static int Exact(short value, short fraction) => value * 1000 - fraction;
    }
}
