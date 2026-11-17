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
    public enum NewsKind
    {
        Fulfilled,
        Realized,
        Lifetime,
        /// <summary>The meter has reached a new level (Level is its index).</summary>
        Level,
        /// <summary>The Sim has fallen into aspiration failure.</summary>
        Breakdown,
        /// <summary>The Sim has pulled themselves together after a long failure.</summary>
        Recovered,
        /// <summary>A lasting reward has worn off.</summary>
        WornOff,
        /// <summary>A child has grown up, and has no aspiration of a grown Sim yet.</summary>
        GrewUp,
    }

    public class News
    {
        public NewsKind Kind;
        public string Text;
        public int Points;
        public int Level;

        public override string ToString() => Kind + ": " + Text + (Points != 0 ? " (" + Points + ")" : "");
    }

    /// <summary>
    /// A Sim's aspiration, moved on by one look at the game: goals that have come true are paid out and replaced,
    /// the meter drifts with time and mood, and failure and recovery happen. This knows nothing of the game:
    /// Watcher.cs gives it what the game looks like now (SimState) and what has happened (Happening), and acts on
    /// the news it returns.
    /// </summary>
    public static class Aspiration
    {
        /// <param name="minutes">Sim minutes since the last step.</param>
        /// <param name="woke">The Sim has just woken up: wants not locked, and fears, are rolled again, as in The Sims 2.</param>
        public static List<News> Step(SimRecord r, SimState s, IList<Happening> h, double minutes, bool woke, Balance b, Random rng)
        {
            var news = new List<News>();
            h ??= new List<Happening>();

            // a child who has grown up chooses again; a Sim met for the first time is given one by chance
            if (r.Aspiration != null && r.Child && !s.Child)
            {
                news.Add(new News() { Kind = NewsKind.GrewUp, Text = "has grown up, and needs a new aspiration" });
                r.Aspiration = null;
                r.Asked = false;
            }
            if (r.Aspiration == null || r.Child != s.Child) Assign(r, Rules.Suggest(s, b, rng), s.Child, false, b);

            if (woke)
            {
                r.Wants.RemoveAll(x => !x.Locked);
                r.Fears.Clear();
            }

            // what has come true. goals rolled in this step are not looked at until the next, so nothing that has
            // already happened can fulfil a goal given after it
            foreach (var slot in r.Wants.ToList())
            {
                var def = b.Want(slot.Id);
                if (def == null) { r.Wants.Remove(slot); continue; }
                if (Goal.Met(def, slot, s, h, b))
                {
                    r.Wants.Remove(slot);
                    Pay(r, slot.Points, b);
                    r.Fulfilled++;
                    news.Add(new News() { Kind = NewsKind.Fulfilled, Text = slot.Text, Points = slot.Points });
                }
                else if (Goal.Stale(def, slot, s)) r.Wants.Remove(slot);
            }
            foreach (var slot in r.Fears.ToList())
            {
                var def = b.Fear(slot.Id);
                if (def == null) { r.Fears.Remove(slot); continue; }
                if (Goal.Met(def, slot, s, h, b))
                {
                    r.Fears.Remove(slot);
                    r.Meter = Rules.Clamp(r.Meter - slot.Points * b.Meter.PerPoint);
                    r.Lost += slot.Points;
                    r.Realized++;
                    news.Add(new News() { Kind = NewsKind.Realized, Text = slot.Text, Points = -slot.Points });
                }
                else if (Goal.Stale(def, slot, s)) r.Fears.Remove(slot);
            }
            if (r.Lifetime != null && !r.LifetimeDone)
            {
                var def = b.LifetimeWant(r.Lifetime.Id);
                if (def != null && Goal.Met(def, r.Lifetime, s, h, b))
                {
                    r.LifetimeDone = true;
                    Pay(r, r.Lifetime.Points, b);
                    r.Fulfilled++;
                    news.Add(new News() { Kind = NewsKind.Lifetime, Text = r.Lifetime.Text, Points = r.Lifetime.Points });
                }
            }

            Fill(r, s, b, rng);

            // the meter
            r.Meter = Rules.Clamp(r.Meter + Rules.Drift(r.Meter, s.Mood, minutes, b));
            if (r.LifetimeDone) r.Meter = Math.Max(r.Meter, b.Meter.LifetimeFloor);
            Failure(r, minutes, b, news);
            var level = Rules.LevelIndex(r.Meter, b);
            if (r.Announced < 0) r.Announced = level;
            else if (level != r.Announced && Inside(r.Meter, level, b))
            {
                if (level > r.Announced || level > 0) news.Add(new News() { Kind = NewsKind.Level, Level = level, Text = b.Levels[level].Name });
                r.Announced = level;
            }

            // time, and what wears off with it
            r.Minutes += (long)Math.Round(minutes);
            foreach (var effect in r.Effects.ToList())
            {
                var over = (effect.Until > 0 && r.Minutes >= effect.Until) || (effect.Kind == "Dividend" && effect.Left <= 0);
                if (!over) continue;
                r.Effects.Remove(effect);
                news.Add(new News() { Kind = NewsKind.WornOff, Text = effect.Name });
            }
            return news;
        }

        /// <summary>Give a Sim an aspiration. A new one starts the wants and fears over; the meter and points stay.</summary>
        public static void Assign(SimRecord r, string aspiration, bool child, bool chosen, Balance b)
        {
            var changed = !string.Equals(r.Aspiration, aspiration, StringComparison.OrdinalIgnoreCase);
            r.Aspiration = b.Aspiration(aspiration)?.Id ?? aspiration;
            r.Child = child;
            r.Chosen = chosen;
            if (chosen) r.Asked = true;
            if (child) r.Asked = true;
            if (!changed) return;
            r.Wants.Clear();
            r.Fears.Clear();
            if (!r.LifetimeDone) r.Lifetime = null;
        }

        private static void Pay(SimRecord r, int points, Balance b)
        {
            r.Points += points;
            r.Earned += points;
            r.Meter = Rules.Clamp(r.Meter + points * b.Meter.PerPoint);
        }

        /// <summary>Whether the meter is a little way into a level, not just across its edge.</summary>
        private static bool Inside(double meter, int level, Balance b)
        {
            const double margin = 2;
            var from = level == 0 ? Rules.MeterMin - margin : b.Levels[level].From;
            var to = level + 1 < b.Levels.Count ? b.Levels[level + 1].From : Rules.MeterMax + margin;
            return meter >= from + margin && meter <= to - margin;
        }

        private static void Failure(SimRecord r, double minutes, Balance b, List<News> news)
        {
            var rules = b.Meter.Failure;
            if (Rules.Failing(r.Meter, b))
            {
                r.FailureMinutes += minutes;
                if (!r.BrokeDown)
                {
                    r.BrokeDown = true;
                    r.Breakdowns++;
                    news.Add(new News() { Kind = NewsKind.Breakdown, Text = b.Levels[0].Name, Level = 0 });
                }
                if (rules.RecoverHours > 0 && r.FailureMinutes >= rules.RecoverHours * 60)
                {
                    r.Meter = Rules.Clamp(Math.Max(r.Meter, rules.RecoverTo));
                    r.FailureMinutes = 0;
                    news.Add(new News() { Kind = NewsKind.Recovered, Text = Rules.Level(r.Meter, b).Name, Level = Rules.LevelIndex(r.Meter, b) });
                }
                return;
            }
            r.FailureMinutes = 0;
            if (r.BrokeDown && b.Levels.Count > 1 && r.Meter >= b.Levels[1].From + rules.Margin) r.BrokeDown = false;
        }

        // ---- rolling ----

        /// <summary>Fill the Sim's empty wants and fears, and give a grown Sim a lifetime want if they have none.</summary>
        public static void Fill(SimRecord r, SimState s, Balance b, Random rng)
        {
            while (r.Wants.Count < b.Slots.Wants)
            {
                var slot = Roll(b.Wants, r.Aspiration, s, r.Wants, b, rng);
                if (slot == null) break;
                r.Wants.Add(slot);
            }
            while (r.Fears.Count < b.Slots.Fears)
            {
                var slot = Roll(b.Fears, r.Aspiration, s, r.Fears, b, rng);
                if (slot == null) break;
                r.Fears.Add(slot);
            }
            if (!s.Child && r.Lifetime == null && !r.LifetimeDone)
                r.Lifetime = Roll(b.Lifetime, r.Aspiration, s, new List<Slot>(), b, rng);
        }

        /// <summary>
        /// A goal for the aspiration, by the weights balance.json gives: one the Sim can be given now, and not one
        /// they hold already (nor the same goal for the same skill). Null if there is none.
        /// </summary>
        public static Slot Roll(IList<GoalDef> pool, string aspiration, SimState s, IList<Slot> held, Balance b, Random rng)
        {
            var heldIds = new HashSet<string>(held.Select(x => x.Id));
            var heldKeys = new HashSet<string>(held.Select(Goal.Key));
            var choices = pool.Where(d => !heldIds.Contains(d.Id) && Goal.Eligible(d, s, b))
                .Select(d => (d, d.WeightFor(aspiration))).Where(x => x.Item2 > 0).ToList();
            // a goal about "any skill" may come out about a skill already held: a few tries, then without it
            for (int tries = 0; tries < 6 && choices.Count > 0; tries++)
            {
                var def = Rules.Pick(choices, rng);
                if (def == null) return null;
                var slot = Goal.Make(def, s, rng, b);
                if (slot != null && !heldKeys.Contains(Goal.Key(slot))) return slot;
                if (slot == null || tries >= 2) choices.RemoveAll(x => x.Item1 == def);
            }
            return null;
        }

        /// <summary>Lock or unlock a want. False, with the reason, if it cannot be.</summary>
        public static string Lock(SimRecord r, int index, Balance b)
        {
            if (index < 0 || index >= r.Wants.Count) return "There is no want " + (index + 1) + ".";
            var want = r.Wants[index];
            if (want.Locked) { want.Locked = false; return null; }
            if (b.Slots.Locks <= 0) return "Wants cannot be locked.";
            if (r.Wants.Count(x => x.Locked) >= b.Slots.Locks)
                return b.Slots.Locks == 1 ? "Only one want can be locked. Unlock it first." : "Only " + b.Slots.Locks + " wants can be locked. Unlock one first.";
            want.Locked = true;
            return null;
        }
    }
}
