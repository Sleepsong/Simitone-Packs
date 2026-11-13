/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeStages
{
    /// <summary>
    /// What the age display shows for one Sim, worked out from the pack's record with nothing of the game in it:
    /// the words, and a bar of life stages after The Sims 2's, each stage a segment as wide as it is long, filled
    /// as far as the Sim has lived. UIAgeDisplay.cs draws it.
    /// </summary>
    public class AgeView
    {
        public class Segment
        {
            public Stage Stage;
            public int X, Width;
            /// <summary>How much of the stage is lived, 0 to 1.</summary>
            public float Fill;
            public bool Current;
        }

        /// <summary>The Sim's stage: "Teen".</summary>
        public string Title = "";
        /// <summary>"Day 3 of 15".</summary>
        public string Counter = "";
        /// <summary>"12 days until adult".</summary>
        public string Status = "";
        /// <summary>A smaller line under it, when there is something to add.</summary>
        public string Note = "";
        public List<Segment> Segments = new List<Segment>();

        /// <summary>Everything shown, as one string: the display is redrawn when this changes.</summary>
        public string Key => Title + "|" + Counter + "|" + Status + "|" + Note + "|" + string.Join(",", Segments.Select(x => x.Stage + ":" + x.X + ":" + x.Width + ":" + x.Fill.ToString("0.000") + (x.Current ? "*" : "")));

        public const int NarrowestSegment = 30, Gap = 3;

        /// <summary>For a Sim the pack does not age: a pet, a visitor, a townie.</summary>
        public static AgeView Unaged()
        {
            return new AgeView() { Title = "Does not age", Status = "Not one of your household" };
        }

        /// <summary>For a Sim of the played household.</summary>
        /// <param name="atHome">Whether the household is on its own lot, where days count.</param>
        /// <param name="width">The width of the bar, in pixels.</param>
        public static AgeView For(SimAge sim, Settings settings, bool atHome, int width)
        {
            var view = new AgeView();
            var name = Stages.Name(sim.Stage);
            view.Title = char.ToUpperInvariant(name[0]) + name.Substring(1);

            var length = Stages.Length(sim.Stage, settings);
            var next = Stages.Next(sim.Stage, settings);
            if (length == 0)
            {
                view.Counter = "Day " + (sim.Days + 1);
                view.Status = "The last stage of life";
            }
            else
            {
                view.Counter = "Day " + Math.Min(sim.Days + 1, length) + " of " + length;
                var left = length - sim.Days;
                if (left <= 0) view.Status = "Becomes " + Article(next.Value) + " at 6 this evening";
                else view.Status = left + (left == 1 ? " day until " : " days until ") + Stages.Name(next.Value);
            }
            if (!settings.Ageing) view.Note = "Ageing is off.";
            else if (!atHome) view.Note = "Days only count at home.";

            // the bar: the stages this Sim passes through, each as wide as it is long
            var shown = new List<Stage>() { Stage.Child };
            if (settings.Teens || sim.Stage == Stage.Teen) shown.Add(Stage.Teen);
            shown.Add(Stage.Adult);
            shown.Add(Stage.Elder);
            var lengths = shown.Select(x => (float)StageLength(x, settings)).ToList();
            //an elder's days are not numbered: the last segment is a fifth as long as the rest together
            lengths[lengths.Count - 1] = Math.Max(1f, lengths.Take(lengths.Count - 1).Sum() / 5f);
            var spare = Math.Max(0, width - shown.Count * NarrowestSegment - (shown.Count - 1) * Gap);
            var total = lengths.Sum();
            int at = 0;
            float owed = 0; //what rounding has kept back so far, so that the widths still add up
            for (int i = 0; i < shown.Count; i++)
            {
                var exact = NarrowestSegment + spare * lengths[i] / total + owed;
                var segment = new Segment() { Stage = shown[i], X = at, Width = Math.Max(1, (int)Math.Round(exact)), Current = shown[i] == sim.Stage };
                owed = exact - segment.Width;
                //the last one ends where the bar does
                if (i == shown.Count - 1 && width - at > 0) segment.Width = width - at;
                at += segment.Width + Gap;
                if (shown[i] < sim.Stage) segment.Fill = 1;
                else if (segment.Current) segment.Fill = length == 0 ? 1 : Math.Clamp(sim.Days / (float)length, 0f, 1f);
                view.Segments.Add(segment);
            }
            return view;
        }

        /// <summary>A stage's days, whether or not teens are switched on.</summary>
        private static int StageLength(Stage stage, Settings settings)
        {
            switch (stage)
            {
                case Stage.Child: return settings.ChildDays;
                case Stage.Teen: return settings.TeenDays;
                default: return settings.AdultDays;
            }
        }

        private static string Article(Stage stage) => (stage == Stage.Adult || stage == Stage.Elder ? "an " : "a ") + Stages.Name(stage);
    }

    /// <summary>
    /// The little figures that stand on the age bar, one for each stage, drawn in code: the pack has no pictures of
    /// its own. Each is a grid of how solid each pixel is (0 to 255), to be coloured when drawn.
    /// </summary>
    public static class Figures
    {
        public const int Width = 18;

        public static int Height(Stage stage)
        {
            switch (stage)
            {
                case Stage.Child: return 17;
                case Stage.Teen: return 22;
                case Stage.Adult: return 27;
                default: return 25; //an elder stoops
            }
        }

        private static float Distance(float x, float y, float cx, float cy) => (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        /// <summary>Distance from a point to a line segment.</summary>
        private static float Capsule(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            var t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0f, 1f);
            return Distance(x, y, ax + dx * t, ay + dy * t);
        }

        /// <summary>The figure for a stage, Width wide and Height(stage) tall, a row at a time from the top.</summary>
        public static byte[] Mask(Stage stage)
        {
            int h = Height(stage);
            var elder = stage == Stage.Elder;
            float cx = Width / 2f - (elder ? 1.5f : 0f);
            float head = stage == Stage.Adult || elder ? 3.7f : 3.5f;
            float torso = stage == Stage.Child ? 2.5f : stage == Stage.Teen ? 2.7f : 3.1f;
            float lean = elder ? 1.6f : 0f; //head and shoulders forward
            float shoulders = head * 2 + torso + 0.3f, hips = h * 0.6f, limb = 1.25f;

            Func<float, float, float> distance = (x, y) =>
            {
                var d = Distance(x, y, cx + lean, head + 0.3f) - head;
                d = Math.Min(d, Capsule(x, y, cx + lean * 0.6f, shoulders, cx, hips) - torso);
                d = Math.Min(d, Capsule(x, y, cx - torso * 0.55f, hips, cx - torso * 0.7f, h - limb - 0.2f) - limb);
                d = Math.Min(d, Capsule(x, y, cx + torso * 0.55f, hips, cx + torso * 0.7f, h - limb - 0.2f) - limb);
                d = Math.Min(d, Capsule(x, y, cx - torso - 0.3f + lean * 0.6f, shoulders, cx - torso - 1.3f, hips + 0.5f) - limb * 0.85f);
                d = Math.Min(d, Capsule(x, y, cx + torso + 0.3f + lean * 0.6f, shoulders, cx + torso + 1.3f + lean, hips + 0.5f) - limb * 0.85f);
                if (elder) d = Math.Min(d, Capsule(x, y, cx + torso + 1.6f + lean, hips, cx + torso + 3.2f + lean, h - 0.7f) - 0.6f); //a stick
                return d;
            };

            var mask = new byte[Width * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < Width; x++)
                    mask[y * Width + x] = (byte)Math.Round(Math.Clamp(0.5f - distance(x + 0.5f, y + 0.5f), 0f, 1f) * 255f);
            return mask;
        }
    }
}
