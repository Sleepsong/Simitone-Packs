/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;

namespace Undo
{
    /// <summary>One moment of the lot kept by the history: the lot as it was just after a change.</summary>
    public class Moment<T>
    {
        /// <summary>The lot at this moment (for the pack, a LotMoment).</summary>
        public T State;
        /// <summary>How many bytes State takes up.</summary>
        public long Size;
        /// <summary>The change that brought the lot to this moment, in a few words. Empty for the first moment.</summary>
        public string What = "";
        /// <summary>
        /// What that change took from the family's budget: negative if it gave money (selling). Undoing the change gives
        /// this back, and redoing it takes it again.
        /// </summary>
        public int Cost;
    }

    /// <summary>
    /// The moments of a lot, from when build or buy mode was opened, and which one the lot is at now. Undo goes to the
    /// moment before, redo to the moment after; a new change after an undo replaces the moments after it, as in The
    /// Sims 2. Nothing here knows the game: the pack keeps a LotMoment in each, and the tests keep strings.
    /// </summary>
    public class History<T>
    {
        /// <summary>Changes kept at most. The oldest are let go first.</summary>
        public int MaxSteps = 50;
        /// <summary>Bytes kept at most, for all the moments together. At least the current moment and the one before are kept.</summary>
        public long MaxBytes = 256L * 1024 * 1024;

        private readonly List<Moment<T>> Moments = new List<Moment<T>>();
        private int Current = -1;

        public bool Started => Current >= 0;
        public bool CanUndo => Current > 0;
        public bool CanRedo => Started && Current < Moments.Count - 1;
        /// <summary>How many changes can be undone, and redone.</summary>
        public int UndoCount => Math.Max(0, Current);
        public int RedoCount => Started ? Moments.Count - 1 - Current : 0;
        public long Bytes { get; private set; }

        /// <summary>The moment the lot is at.</summary>
        public Moment<T> Now => Started ? Moments[Current] : null;
        /// <summary>The change undo would take back (the moment the lot is at), or null.</summary>
        public Moment<T> NextUndo => CanUndo ? Moments[Current] : null;
        /// <summary>The change redo would make again (the moment after), or null.</summary>
        public Moment<T> NextRedo => CanRedo ? Moments[Current + 1] : null;

        /// <summary>Begin again from this moment, with nothing to undo or redo.</summary>
        public void Start(T state, long size)
        {
            Moments.Clear();
            Moments.Add(new Moment<T>() { State = state, Size = size });
            Current = 0;
            Bytes = size;
        }

        /// <summary>Forget everything: the lot has been left, or build mode closed.</summary>
        public void Clear()
        {
            Moments.Clear();
            Current = -1;
            Bytes = 0;
        }

        /// <summary>A change has been made: this is the lot after it. What could have been redone is gone.</summary>
        public void Add(T state, long size, string what, int cost)
        {
            if (!Started) throw new InvalidOperationException("The history has not been started");
            for (int i = Moments.Count - 1; i > Current; i--) Drop(i);
            Moments.Add(new Moment<T>() { State = state, Size = size, What = what ?? "", Cost = cost });
            Bytes += size;
            Current = Moments.Count - 1;
            Trim();
        }

        /// <summary>
        /// The lot at the current moment is not quite as it was kept: keep it as it is now instead, and count what the
        /// difference cost as part of the change that led here. For a change the pack could not see happen.
        /// </summary>
        public void Refresh(T state, long size, int extraCost)
        {
            if (!Started) return;
            var now = Moments[Current];
            Bytes += size - now.Size;
            now.State = state;
            now.Size = size;
            if (Current > 0) now.Cost += extraCost;
        }

        /// <summary>The lot has been put back to the moment before. Returns that moment.</summary>
        public Moment<T> Undone()
        {
            if (!CanUndo) throw new InvalidOperationException("Nothing to undo");
            return Moments[--Current];
        }

        /// <summary>The lot has been put forward to the moment after. Returns that moment.</summary>
        public Moment<T> Redone()
        {
            if (!CanRedo) throw new InvalidOperationException("Nothing to redo");
            return Moments[++Current];
        }

        /// <summary>The changes, oldest first, with whether each is done (true) or undone (false). For showing.</summary>
        public List<(string what, int cost, bool done)> Steps()
        {
            var result = new List<(string, int, bool)>();
            for (int i = 1; i < Moments.Count; i++) result.Add((Moments[i].What, Moments[i].Cost, i <= Current));
            return result;
        }

        private void Drop(int index)
        {
            Bytes -= Moments[index].Size;
            Moments.RemoveAt(index);
        }

        /// <summary>Let go of the oldest moments while there are too many, keeping the current one and the one before it.</summary>
        private void Trim()
        {
            while (Current > 1 && (Moments.Count - 1 > MaxSteps || Bytes > MaxBytes))
            {
                Drop(0);
                Current--;
            }
        }
    }

    /// <summary>The money of undoing and redoing. Nothing here knows the game.</summary>
    public static class Ledger
    {
        /// <summary>
        /// What a change cost: the budget when the lot was last kept, less the budget now. Null budgets (no family plays
        /// this lot, so building is free) cost nothing.
        /// </summary>
        public static int CostOf(int? before, int? after)
        {
            if (before == null || after == null) return 0;
            return before.Value - after.Value;
        }

        /// <summary>
        /// Why the family cannot afford to undo a change that cost this (giving it back can only be refused for a sale,
        /// whose money would be taken back), or null if it can. A null budget is a lot with no family: always yes.
        /// </summary>
        public static string CannotUndo(int? budget, int cost)
        {
            if (budget == null || budget.Value + (long)cost >= 0) return null;
            return "Undoing this would take back the " + Money(-cost) + " it was sold for, and the family has " + Money(budget.Value) + ".";
        }

        /// <summary>Why the family cannot afford to make this change again, or null if it can.</summary>
        public static string CannotRedo(int? budget, int cost)
        {
            if (budget == null || budget.Value - (long)cost >= 0) return null;
            return "Doing this again costs " + Money(cost) + ", and the family has " + Money(budget.Value) + ".";
        }

        /// <summary>The budget after undoing (or, with undo false, redoing) a change that cost this.</summary>
        public static int After(int budget, int cost, bool undo)
        {
            var result = undo ? (long)budget + cost : (long)budget - cost;
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, result));
        }

        public static string Money(int amount) => "§" + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
