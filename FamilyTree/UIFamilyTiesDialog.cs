/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTree
{
    /// <summary>The family whose ties are being chosen, and the tree they go in.</summary>
    public class TiesSubject
    {
        public Kin Kin;
        public List<uint> Sims = new List<uint>();
        public Func<uint, string> Name = x => "Sim " + x;
        public Func<uint, bool> IsAdult = x => true;
        public Func<uint, bool> IsFemale = x => false;
        public Func<uint, Texture2D> Picture = x => null;
        /// <summary>A tie was changed.</summary>
        public Action Changed;
        /// <summary>The window was closed with Done.</summary>
        public Action Done;
    }

    /// <summary>
    /// Choosing a new family's ties in Create a Family, the way The Sims 2 does: the family's portraits in a row.
    /// Choose one Sim, then another, and say what the first is to the second: spouse, sibling, parent, child, or
    /// nothing (roommates). Once a Sim is chosen, everyone else's portrait says what they are to that Sim, including
    /// what follows from the rest (the brother nobody chose, through the parents they share).
    /// </summary>
    public class UIFamilyTiesDialog : UIMobileDialog
    {
        private const int Margin = 60;
        private const int Gap = 15;

        /// <summary>The choices, in the order of their buttons, read "the first Sim is the second's ...".</summary>
        public static readonly (Choice, string)[] Choices =
        {
            (Choice.Spouse, "Spouse"),
            (Choice.Sibling, "Sibling"),
            (Choice.Parent, "Parent"),
            (Choice.Child, "Child"),
            (Choice.Housemate, "Roommate"),
        };

        private readonly TiesSubject Subject;
        private UIContainer Body;
        private uint? First;
        private uint? Second;
        private string Message;

        public UIFamilyTiesDialog(TiesSubject subject) : base()
        {
            Subject = subject;
            Caption = "Family Relationships";
            Refresh();
        }

        public static UIFamilyTiesDialog Open(TiesSubject subject)
        {
            var dialog = new UIFamilyTiesDialog(subject);
            UIScreen.GlobalShowDialog(dialog, true);
            return dialog;
        }

        public override void GameResized()
        {
            base.GameResized();
            Refresh();
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //ride in and out with the dialog's stripes
            if (Body != null) Body.X = ((Closing) ? 1 : -1) * (1 - InterpolatedAnimation) * Width;
        }

        // ---- what the window says, apart from how it is drawn ----

        /// <summary>The line above the choices.</summary>
        public static string Prompt(TiesSubject s, uint? first, uint? second)
        {
            if (first == null) return "Choose a Sim.";
            if (second == null) return "Now choose who " + s.Name(first.Value) + " is related to.";
            return s.Name(first.Value) + " is " + s.Name(second.Value) + "'s:";
        }

        /// <summary>Under a portrait, what that Sim is to the one chosen first: "Wife", "Son". Empty for no tie.</summary>
        public static string Under(TiesSubject s, uint? first, uint sim)
        {
            if (first == null || first.Value == sim) return "";
            return TieNames.For(s.Kin.Between(first.Value, sim), s.IsFemale(sim)) ?? "";
        }

        /// <summary>A portrait was chosen: the first Sim, then the second, then start again.</summary>
        public static void Pick(ref uint? first, ref uint? second, uint sim)
        {
            if (first == null) first = sim;
            else if (first.Value == sim)
            {
                first = null;
                second = null;
            }
            else if (second == null || second.Value != sim) second = sim;
            else second = null;
        }

        /// <summary>Make the first Sim the second's spouse, sibling, parent or child, if the rules allow. Returns why not, or null.</summary>
        public static string Choose(TiesSubject s, uint first, uint second, Choice choice)
        {
            var why = s.Kin.WhyNot(first, second, choice, s.IsAdult, s.Name);
            if (why == null)
            {
                s.Kin.Set(first, second, choice);
                s.Changed?.Invoke();
            }
            return why;
        }

        // ---- drawing it ----

        private UILabel AddLabel(string text, int size, Color color, Vector2 pos, int width)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.Alignment = TextAlignment.Top | TextAlignment.Center;
            label.Wrapped = true;
            label.MaxLines = 2;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            label.Caption = text ?? "";
            Body.Add(label);
            return label;
        }

        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Message = "That went wrong: " + e.Message;
                People.Log("The Family Relationships window: " + e);
            }
            Refresh();
        }

        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);

            var s = Subject;
            var style = UIStyle.Current;
            var inner = Width - Margin * 2;
            float y = 95;

            //the family, in a row
            var step = Math.Min(120f, inner / (float)Math.Max(1, s.Sims.Count));
            var left = (Width - step * s.Sims.Count) / 2 + step / 2;
            for (int i = 0; i < s.Sims.Count; i++)
            {
                var sim = s.Sims[i];
                Texture2D face = null;
                try { face = s.Picture(sim); } catch (Exception) { }
                var button = new UIAvatarSelectButton(face);
                button.Opacity = 1f;
                button.Name = s.Name(sim);
                button.Outlined = sim == First || sim == Second;
                button.Position = new Vector2(left + i * step, y + 45);
                button.OnButtonClick += (b) => Guarded(() =>
                {
                    Pick(ref First, ref Second, sim);
                    Message = null;
                });
                Body.Add(button);
                AddLabel(Under(s, First, sim), 12, style.SecondaryText, new Vector2(left + i * step - step / 2, y + 115), (int)step);
            }
            y += 150;

            AddLabel(Prompt(s, First, Second), 19, style.DialogText, new Vector2(Margin, y), inner);
            y += 45;

            if (First != null && Second != null)
            {
                var first = First.Value;
                var second = Second.Value;
                var now = s.Kin.ChoiceFor(first, second);
                var width = Math.Min(230, (inner - Gap * (Choices.Length - 1)) / Choices.Length);
                var x = (Width - (width * Choices.Length + Gap * (Choices.Length - 1))) / 2;
                foreach (var (choice, text) in Choices)
                {
                    var button = new UIBigButton(choice == now);
                    button.Caption = text;
                    button.Width = width;
                    button.Position = new Vector2(x, y);
                    button.OnButtonClick += (b) => Guarded(() => Message = Choose(s, first, second, choice));
                    Body.Add(button);
                    x += width + Gap;
                }
                y += 110;
            }

            if (Message != null)
            {
                AddLabel(Message, 15, style.NegMoney, new Vector2(Margin, y), inner);
                y += 55;
            }

            var done = new UIBigButton(true);
            done.Caption = "Done";
            done.Width = 275;
            done.Position = new Vector2((Width - 275) / 2, y + 10);
            done.OnButtonClick += (b) =>
            {
                try
                {
                    s.Done?.Invoke();
                }
                catch (Exception e)
                {
                    People.Log("Closing the Family Relationships window: " + e);
                }
                Close();
            };
            Body.Add(done);

            SetHeight((int)(y + 10 + 125));
        }
    }
}
