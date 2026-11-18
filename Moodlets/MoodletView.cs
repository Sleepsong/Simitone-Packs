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
    /// <summary>
    /// What the moodlet panel shows for one Sim, worked out with nothing of the game in it: the words, and where each
    /// moodlet goes. UIMoodletPanel.cs draws it.
    ///
    /// The panel is in the Needs panel, to the right of the eight bars, where Simitone leaves the rest of the screen's
    /// width empty. How much that is depends on the screen, so the moodlets are laid out to fit it:
    ///
    ///   wide     each moodlet a chip with its mood, its name and the time it has left, in columns of three;
    ///   narrow   each just its mood, smaller, in columns of three, with the rest in its tooltip;
    ///   none     too little room even for that: only the console shows them.
    ///
    /// They fill a column before the next, strongest first. If there are more than fit, the last place says how
    /// many more there are, and its tooltip names them.
    /// </summary>
    public class MoodletView
    {
        public class Chip
        {
            public int X, Y, Width, Height;
            /// <summary>"+12", or "+3" for the place that stands for three more.</summary>
            public string Mood = "";
            public string Name = "";
            /// <summary>"14h".</summary>
            public string Time = "";
            /// <summary>How much of its time it has left, 0 to 1.</summary>
            public float Left;
            public bool Good;
            /// <summary>The place that stands for those that did not fit.</summary>
            public bool More;
            /// <summary>Everything about it, for its tooltip and for when it is clicked.</summary>
            public string Details = "";
        }

        public enum Layout { Wide, Narrow, None }

        public Layout Fit = Layout.Wide;
        /// <summary>Above the moodlets, as the needs have their names above them: "Moodlets".</summary>
        public string Title = "";
        /// <summary>Beside the title: what they add to mood, "+12". Empty when nothing.</summary>
        public string Total = "";
        public bool TotalGood = true;
        /// <summary>In place of the moodlets, when there are none to show: "None".</summary>
        public string Empty = "";
        public List<Chip> Chips = new List<Chip>();

        /// <summary>Everything shown, as one string: the panel is drawn again when this changes.</summary>
        public string Key => Shape + "|" + string.Join(",", Chips.Select(c => c.Time + ":" + (int)(c.Left * c.Width) + ":" + c.Details));

        /// <summary>
        /// What is shown, but for the time left: while this stays the same, the chips are kept and only their times
        /// change, so that a tooltip being read is not taken away each game minute.
        /// </summary>
        public string Shape => Fit + "|" + Title + "|" + Total + "|" + Empty + "|" + string.Join(",", Chips.Select(c => c.X + ":" + c.Y + ":" + c.Width + ":" + c.Mood + ":" + c.Name + ":" + c.More));

        /// <summary>Where the moodlets start in the Needs panel: past the fourth bar (17 + 3 × 180 + 150) and its arrow.</summary>
        public const int Left = 727;
        /// <summary>The first row lines up with the first row of bars, and the title with their names.</summary>
        public const int Top = 36, TitleBottom = 30;
        public const int RowHeight = 24, RowGap = 6, Rows = 3, Gap = 8;
        public const int NarrowestWide = 150, WidestWide = 220, PillWidth = 40;
        /// <summary>Room the panel leaves at its right edge.</summary>
        public const int Margin = 12;

        /// <summary>The width the moodlets have in a Needs panel this wide.</summary>
        public static int RoomIn(float panelWidth) => Math.Max(0, (int)panelWidth - Left - Margin);

        /// <summary>The panel when there is nobody to show: no Sim selected, or no neighbourhood.</summary>
        public static MoodletView Nothing(int width) => new MoodletView() { Fit = FitFor(width), Title = TitleFor(width, "Moodlets") };

        /// <summary>For a Sim the pack gives no moodlets to.</summary>
        public static MoodletView NotOurs(int width) => new MoodletView() { Fit = FitFor(width), Title = TitleFor(width, "Moodlets"), Empty = width >= NarrowestWide ? "Only for your household" : "" };

        public static Layout FitFor(int width) => width >= NarrowestWide ? Layout.Wide : width >= PillWidth ? Layout.Narrow : Layout.None;

        private static string TitleFor(int width, string title) => width >= 70 ? title : "";

        /// <summary>For a Sim of the played household.</summary>
        /// <param name="on">Whether moodlets are on.</param>
        /// <param name="width">The room the panel has, from RoomIn.</param>
        public static MoodletView For(IEnumerable<Moodlet> moodlets, Tuning tuning, bool on, int width)
        {
            var view = new MoodletView() { Fit = FitFor(width) };
            var list = Rules.Ordered(moodlets, tuning);
            view.Title = TitleFor(width, on ? "Moodlets" : "Moodlets are off");
            if (view.Fit == Layout.None) return view;
            if (list.Count == 0)
            {
                view.Empty = "None";
                return view;
            }
            if (on)
            {
                var total = Rules.Total(list, tuning);
                view.Total = total == 0 ? "" : Rules.Signed(total) + (view.Fit == Layout.Wide ? " to mood" : "");
                view.TotalGood = total >= 0;
            }

            // the columns
            int chipWidth, columns;
            if (view.Fit == Layout.Wide)
            {
                columns = Math.Max(1, (width + Gap) / (NarrowestWide + Gap));
                chipWidth = Math.Min(WidestWide, (width - (columns - 1) * Gap) / columns);
            }
            else
            {
                const int narrowGap = 4;
                columns = Math.Max(1, (width + narrowGap) / (PillWidth + narrowGap));
                chipWidth = PillWidth;
            }
            var step = view.Fit == Layout.Wide ? chipWidth + Gap : chipWidth + 4;
            var places = columns * Rows;
            var shown = list.Count > places ? places - 1 : list.Count;

            for (int i = 0; i < shown; i++)
            {
                var moodlet = list[i];
                var kind = tuning.Find(moodlet.Id);
                var chip = Place(i, step);
                chip.Width = chipWidth;
                chip.Mood = Rules.Signed(kind?.Mood ?? 0);
                chip.Good = (kind?.Mood ?? 0) >= 0;
                chip.Name = kind?.Name ?? moodlet.Id;
                chip.Time = Rules.ShortTime(moodlet.Minutes);
                chip.Left = kind == null || kind.Minutes <= 0 ? 0 : Math.Clamp(moodlet.Minutes / (float)kind.Minutes, 0, 1);
                chip.Details = Details(moodlet, kind, on);
                view.Chips.Add(chip);
            }
            if (shown < list.Count)
            {
                var rest = list.Skip(shown).ToList();
                var chip = Place(shown, step);
                chip.Width = chipWidth;
                chip.More = true;
                chip.Mood = "+" + rest.Count;
                chip.Name = "more";
                chip.Good = true;
                chip.Details = string.Join("\n", rest.Select(x =>
                {
                    var kind = tuning.Find(x.Id);
                    return (kind?.Name ?? x.Id) + " " + Rules.Signed(kind?.Mood ?? 0) + ", " + Rules.TimeLeft(x.Minutes);
                }));
                view.Chips.Add(chip);
            }
            return view;
        }

        /// <summary>The place of the i-th moodlet: down a column, then the next.</summary>
        private static Chip Place(int i, int step)
        {
            return new Chip() { X = (i / Rows) * step, Y = Top + (i % Rows) * (RowHeight + RowGap), Height = RowHeight };
        }

        /// <summary>"Promoted (+12 mood)\nMoved up at work.\nNow: Desk Sergeant.\n14 hours left"</summary>
        public static string Details(Moodlet moodlet, MoodletKind kind, bool on)
        {
            var lines = new List<string>();
            lines.Add((kind?.Name ?? moodlet.Id) + " (" + Rules.Signed(kind?.Mood ?? 0) + " mood)");
            if (!string.IsNullOrWhiteSpace(kind?.Text)) lines.Add(kind.Text);
            if (!string.IsNullOrWhiteSpace(moodlet.Note)) lines.Add(moodlet.Note);
            lines.Add(on ? Rules.TimeLeft(moodlet.Minutes) : Rules.TimeLeft(moodlet.Minutes) + ", once moodlets are on again");
            return string.Join("\n", lines);
        }
    }
}
