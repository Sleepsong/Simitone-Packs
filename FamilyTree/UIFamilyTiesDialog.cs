/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTree
{
    /// <summary>The Sims a ties window is about, and the tree it changes. The same window serves a family that is still being made and one that is being played.</summary>
    public class TiesSubject
    {
        public string Caption = "Family ties";
        public Kin Kin;
        /// <summary>Everyone the window lists.</summary>
        public List<uint> Sims = new List<uint>();
        /// <summary>If set, only this Sim's tie to each of the others is listed. Otherwise every pair is.</summary>
        public uint? Focus;
        public Func<uint, string> Name = x => "Sim " + x;
        public Func<uint, bool> IsAdult = x => true;
        public Func<uint, bool> IsFemale = x => false;
        /// <summary>The tree was changed.</summary>
        public Action Changed;
        /// <summary>Shown when there is nobody to list.</summary>
        public string Empty = "There is nobody here to have a family tie with.";
    }

    /// <summary>
    /// Choose what two Sims are to each other, a pair to a row: housemates (no tie), married, siblings, or one the
    /// other's parent. The button on a row moves on to the next choice the rules allow.
    /// </summary>
    public class UIFamilyTiesDialog : UIRowsDialog
    {
        private static readonly Choice[] Order = { Choice.Housemate, Choice.Spouse, Choice.Sibling, Choice.Parent, Choice.Child };

        private readonly TiesSubject Subject;
        private readonly Action Closed;

        public UIFamilyTiesDialog(TiesSubject subject, Action closed = null) : base()
        {
            Subject = subject;
            Closed = closed;
            Caption = subject.Caption;
            Refresh();
        }

        protected override void OnClosed()
        {
            Closed?.Invoke();
        }

        /// <summary>What the button says for a pair: the choice as it stands.</summary>
        public static string ButtonText(Choice choice, string first, string second)
        {
            switch (choice)
            {
                case Choice.Spouse: return "Married";
                case Choice.Sibling: return "Siblings";
                case Choice.Parent: return Short(first) + " is parent";
                case Choice.Child: return Short(second) + " is parent";
                default: return "No tie";
            }
        }

        private static string Short(string name)
        {
            name = (name ?? "").Split(' ')[0];
            return name.Length > 9 ? name.Substring(0, 8) + "." : name;
        }

        /// <summary>The pair in a sentence: what the second Sim is to the first.</summary>
        public static string Sentence(TiesSubject s, uint first, uint second)
        {
            var tie = s.Kin.Between(first, second);
            var word = TieNames.For(tie, s.IsFemale(second));
            if (word == null) return "No family tie.";
            var direct = s.Kin.ChoiceFor(first, second) != Choice.Housemate;
            return s.Name(second) + " is " + s.Name(first) + "'s " + word.ToLowerInvariant() + (direct ? "." : ", through the rest of the family.");
        }

        private void Next(uint first, uint second)
        {
            var kin = Subject.Kin;
            var now = kin.ChoiceFor(first, second);
            var at = Array.IndexOf(Order, now);
            string refused = null;
            for (int step = 1; step < Order.Length; step++)
            {
                var choice = Order[(at + step) % Order.Length];
                var why = kin.WhyNot(first, second, choice, Subject.IsAdult, Subject.Name);
                if (why == null)
                {
                    kin.Set(first, second, choice);
                    Subject.Changed?.Invoke();
                    Message = refused;
                    Refresh();
                    return;
                }
                if (refused == null) refused = "Not " + ButtonText(choice, Subject.Name(first), Subject.Name(second)).ToLowerInvariant() + ": " + why;
            }
            Message = refused ?? "Nothing else is possible for these two.";
            Refresh();
        }

        protected override List<Row> Rows()
        {
            var rows = new List<Row>();
            var s = Subject;
            var pairs = new List<(uint, uint)>();
            if (s.Focus != null)
            {
                foreach (var other in s.Sims.Where(x => x != s.Focus.Value)) pairs.Add((s.Focus.Value, other));
            }
            else
            {
                for (int i = 0; i < s.Sims.Count; i++)
                {
                    for (int j = i + 1; j < s.Sims.Count; j++) pairs.Add((s.Sims[i], s.Sims[j]));
                }
            }
            if (pairs.Count == 0)
            {
                rows.Add(new Row("Nobody to choose", s.Empty));
                return rows;
            }
            foreach (var pair in pairs)
            {
                var (first, second) = pair;
                var choice = s.Kin.ChoiceFor(first, second);
                rows.Add(new Row(s.Name(first) + " and " + s.Name(second), Sentence(s, first, second))
                    .Button(ButtonText(choice, s.Name(first), s.Name(second)), () => Next(first, second), choice != Choice.Housemate, false, 330));
            }
            return rows;
        }

        /// <summary>Show a ties window.</summary>
        public static UIFamilyTiesDialog Open(TiesSubject subject, Action closed = null)
        {
            var dialog = new UIFamilyTiesDialog(subject, closed);
            UIScreen.GlobalShowDialog(dialog, true);
            return dialog;
        }
    }
}
