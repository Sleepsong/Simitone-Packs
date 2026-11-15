/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
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

    /// <summary>One answer to "Pa is Ma's ...?": its words, whether it is what the two are now, and why it cannot be chosen (null if it can).</summary>
    public class TieOption
    {
        public Choice Choice;
        public string Label;
        public bool Current;
        public string WhyNot;
    }

    /// <summary>
    /// Choosing a new family's ties in Create a Family, the way The Sims 2 does: drag one Sim's portrait onto
    /// another's, and say what the first is to the second (Husband, Sister, Father, Son, or Roommate for no tie).
    ///
    /// The family's portraits are in a row along the bottom. Above them is the family tree as it stands, drawn around
    /// one Sim (outlined), and under each portrait is what that Sim is to them. Tapping a portrait, in the row or the
    /// tree, draws the tree around that Sim instead.
    /// </summary>
    public class UIFamilyTiesDialog : UIMobileDialog
    {
        private const int Margin = 60;
        private const int Gap = 15;
        private const int TopOfBody = 95;
        /// <summary>The tallest the tree is drawn: three generations at full size.</summary>
        private const int MostTreeHeight = 460;
        private const int OptionWidth = 230;
        private const int OptionHeight = 100;
        /// <summary>A press that moves less than this before it is let go is a tap, not a drag.</summary>
        public const float DragDistance = 12;

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
        private UILabel HintLabel;
        private readonly List<UIDragHead> Heads = new List<UIDragHead>();
        private UIDragHead Ghost;

        /// <summary>The Sim the tree is drawn around, and the labels under the portraits are about.</summary>
        private uint Focus;
        private string Message;

        // a drag in progress
        private UIDragHead Dragging;
        private int DragMouse;
        private Vector2 DragStart;
        private bool Moved;

        // a Sim dropped on another: the question is showing
        private uint? AskFrom;
        private uint? AskTo;

        public UIFamilyTiesDialog(TiesSubject subject) : base()
        {
            Subject = subject;
            Focus = subject.Sims.Count > 0 ? subject.Sims[0] : 0;
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
            if (Dragging != null)
            {
                try
                {
                    FollowDrag(state);
                }
                catch (Exception e)
                {
                    People.Log("Dragging a portrait: " + e);
                    EndDrag();
                }
            }
        }

        // ---- what the window says and does, apart from how it is drawn ----

        /// <summary>The line at the top.</summary>
        public static string Hint(TiesSubject s, uint? dragging)
        {
            if (dragging != null) return "Drop " + s.Name(dragging.Value) + " on the Sim they are related to.";
            return "Drag a picture in the row at the bottom onto another to say how the two Sims are related. Tap a picture to see that Sim's family.";
        }

        /// <summary>Under a portrait, what that Sim is to the one the tree is drawn around: "Wife", "Son". Empty for no tie.</summary>
        public static string Under(TiesSubject s, uint? focus, uint sim)
        {
            if (focus == null || focus.Value == sim) return "";
            return TieNames.For(s.Kin.Between(focus.Value, sim), s.IsFemale(sim)) ?? "";
        }

        /// <summary>The question when one Sim is dropped on another.</summary>
        public static string Question(TiesSubject s, uint first, uint second)
        {
            return s.Name(first) + " is " + s.Name(second) + "'s:";
        }

        /// <summary>
        /// The answers to "first is second's ...?", in the order of their buttons. Each is named for the first Sim
        /// (a man is a Husband, Brother, Father or Son), says if it is what they are now, and why the rules refuse it.
        /// </summary>
        public static List<TieOption> Options(TiesSubject s, uint first, uint second)
        {
            var now = s.Kin.ChoiceFor(first, second);
            var female = s.IsFemale(first);
            var result = new List<TieOption>();
            foreach (var (choice, plain) in Choices)
            {
                string label;
                switch (choice)
                {
                    case Choice.Spouse: label = TieNames.For(Tie.Spouse, female); break;
                    case Choice.Sibling: label = TieNames.For(Tie.Sibling, female); break;
                    case Choice.Parent: label = TieNames.For(Tie.Parent, female); break;
                    case Choice.Child: label = TieNames.For(Tie.Child, female); break;
                    default: label = plain; break;
                }
                result.Add(new TieOption()
                {
                    Choice = choice,
                    Label = label,
                    Current = choice == now,
                    WhyNot = choice == now ? null : s.Kin.WhyNot(first, second, choice, s.IsAdult, s.Name),
                });
            }
            return result;
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

        /// <summary>What to say once a choice is made.</summary>
        public static string Said(TiesSubject s, uint first, uint second, Choice choice)
        {
            if (choice == Choice.Housemate) return s.Name(first) + " and " + s.Name(second) + " are roommates.";
            var option = Options(s, first, second).First(o => o.Choice == choice);
            return s.Name(first) + " is " + s.Name(second) + "'s " + option.Label.ToLowerInvariant() + ".";
        }

        /// <summary>Whether a press let go here, after starting there, was a tap and not a drag.</summary>
        public static bool IsTap(Vector2 start, Vector2 end)
        {
            return Vector2.Distance(start, end) < DragDistance;
        }

        /// <summary>The Sim whose portrait is under a point, apart from the one being dragged. Null if none is.</summary>
        public static uint? HeadAt(IEnumerable<(uint, Vector2)> heads, Vector2 point, uint? except)
        {
            uint? best = null;
            var nearest = float.MaxValue;
            foreach (var (sim, centre) in heads)
            {
                if (sim == except) continue;
                if (Math.Abs(point.X - centre.X) > UIDragHead.Side / 2 || Math.Abs(point.Y - centre.Y) > UIDragHead.Side / 2) continue;
                var distance = Vector2.Distance(point, centre);
                if (distance < nearest)
                {
                    nearest = distance;
                    best = sim;
                }
            }
            return best;
        }

        // ---- dragging ----

        private IEnumerable<(uint, Vector2)> Places() => Heads.Select(h => (h.Sim, h.Position));

        private void OnHead(UIDragHead head, UIMouseEventType type, UpdateState state)
        {
            try
            {
                if (AskFrom != null || Closing) return;
                if (type == UIMouseEventType.MouseDown)
                {
                    EndDrag();
                    Dragging = head;
                    DragMouse = state.CurrentMouseID;
                    DragStart = Body.GetMousePosition(state.MouseState);
                    Moved = false;
                }
                else if (type == UIMouseEventType.MouseUp && Dragging == head)
                {
                    var at = Body.GetMousePosition(state.MouseState);
                    var from = head.Sim;
                    var dragged = Moved || !IsTap(DragStart, at);
                    EndDrag();
                    if (!dragged)
                    {
                        Focus = from;
                        Message = null;
                        Refresh();
                        return;
                    }
                    var to = HeadAt(Places(), at, from);
                    if (to == null) return; //dropped on nobody
                    AskFrom = from;
                    AskTo = to;
                    Message = null;
                    Refresh();
                }
            }
            catch (Exception e)
            {
                People.Log("A portrait in the Family Relationships window: " + e);
                EndDrag();
            }
        }

        /// <summary>The pointer has moved while a portrait is held: the copy follows it, and the Sim under it is outlined.</summary>
        private void FollowDrag(UpdateState state)
        {
            var mouse = state.MouseStates.FirstOrDefault(m => m.ID == DragMouse);
            if (mouse == null)
            {
                EndDrag(); //the finger was lifted somewhere the window was not told of
                return;
            }
            var at = Body.GetMousePosition(mouse.MouseState);
            if (!Moved && !IsTap(DragStart, at))
            {
                Moved = true;
                Dragging.Lifted = true;
                Ghost.Icon = Dragging.Icon;
                Ghost.Visible = true;
                if (HintLabel != null) HintLabel.Caption = Hint(Subject, Dragging.Sim);
            }
            if (!Moved) return;
            Ghost.Position = at;
            var over = HeadAt(Places(), at, Dragging.Sim);
            foreach (var head in Heads) head.Target = head.Sim == over;
        }

        private void EndDrag()
        {
            Dragging = null;
            Moved = false;
            foreach (var head in Heads)
            {
                head.Lifted = false;
                head.Target = false;
            }
            if (Ghost != null) Ghost.Visible = false;
            if (HintLabel != null) HintLabel.Caption = Hint(Subject, null);
        }

        // ---- the question ----

        private void Answer(uint first, uint second, Choice choice)
        {
            try
            {
                var why = Choose(Subject, first, second, choice);
                Message = why ?? Said(Subject, first, second, choice);
                if (why == null) Focus = first;
            }
            catch (Exception e)
            {
                Message = "That went wrong: " + e.Message;
                People.Log("The Family Relationships window: " + e);
            }
            AskFrom = null;
            AskTo = null;
            Refresh();
        }

        private void CancelQuestion()
        {
            AskFrom = null;
            AskTo = null;
            Refresh();
        }

        /// <summary>The question, over the rest of the window: a button for each answer, with why any cannot be chosen.</summary>
        private void AddQuestion(uint first, uint second, float height)
        {
            var s = Subject;
            var style = UIStyle.Current;
            Body.Add(new UIBox(new Vector2(Width, height), Color.Black * 0.55f, type =>
            {
                if (type == UIMouseEventType.MouseUp) CancelQuestion(); //a tap outside the question
            }));

            var options = Options(s, first, second);
            var refused = options.Where(o => o.WhyNot != null).ToList();
            var panelWidth = Math.Min(Width - Margin, OptionWidth * 3 + Gap * 2 + Margin * 2);
            var perRow = Math.Max(1, Math.Min(options.Count, (panelWidth - Margin * 2 + Gap) / (OptionWidth + Gap)));
            var rows = (options.Count + perRow - 1) / perRow;
            var panelHeight = 30 + 55 + rows * OptionHeight + refused.Count * 26 + 20 + OptionHeight + 10;
            var left = (Width - panelWidth) / 2f;
            var top = Math.Max(TopOfBody, (height - panelHeight) / 2f);
            Body.Add(new UIBox(new Vector2(panelWidth, panelHeight), new Color(16, 44, 82, 250), type => { }) { Position = new Vector2(left, top) });

            //who, around the question: the two portraits, the one dragged at the left
            var who = new UIDragHead(first, Safe(s.Picture, first), null, true) { Position = new Vector2(left + 70, top + 70) };
            var whom = new UIDragHead(second, Safe(s.Picture, second), null, true) { Position = new Vector2(left + panelWidth - 70, top + 70) };
            Body.Add(who);
            Body.Add(whom);
            var y = top + 30;
            var question = AddLabel(Question(s, first, second), 25, style.DialogText, new Vector2(left + 130, y), panelWidth - 260, 2);
            question.Alignment = TextAlignment.Top | TextAlignment.Center;
            y += 85;

            for (int row = 0; row < rows; row++)
            {
                var these = options.Skip(row * perRow).Take(perRow).ToList();
                var x = left + (panelWidth - (these.Count * OptionWidth + (these.Count - 1) * Gap)) / 2f;
                foreach (var option in these)
                {
                    var button = new UIBigButton(option.Current);
                    button.Caption = option.Label;
                    button.Width = OptionWidth;
                    button.Position = new Vector2(x, y);
                    button.Disabled = option.WhyNot != null;
                    var choice = option.Choice;
                    button.OnButtonClick += (b) => Answer(first, second, choice);
                    Body.Add(button);
                    x += OptionWidth + Gap;
                }
                y += OptionHeight;
            }
            foreach (var option in refused)
            {
                AddLabel("Not " + option.Label.ToLowerInvariant() + ": " + option.WhyNot, 12, style.DialogText * 0.75f, new Vector2(left + Margin, y), panelWidth - Margin * 2, 1);
                y += 26;
            }
            y += 20;
            var cancel = new UIBigButton(false);
            cancel.Caption = "Cancel";
            cancel.Width = OptionWidth;
            cancel.Position = new Vector2(left + (panelWidth - OptionWidth) / 2f, y);
            cancel.OnButtonClick += (b) => CancelQuestion();
            Body.Add(cancel);

        }

        // ---- drawing it ----

        private static Texture2D Safe(Func<uint, Texture2D> picture, uint sim)
        {
            try
            {
                return picture?.Invoke(sim);
            }
            catch (Exception e)
            {
                People.Log("Could not draw a Sim's portrait: " + e.Message);
                return null;
            }
        }

        private UILabel AddLabel(string text, int size, Color color, Vector2 pos, int width, int lines = 2)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.Alignment = TextAlignment.Top | TextAlignment.Center;
            label.Wrapped = true;
            label.MaxLines = lines;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            label.Caption = text ?? "";
            Body.Add(label);
            return label;
        }

        private void Refresh()
        {
            EndDrag();
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);
            Heads.Clear();

            var s = Subject;
            var style = UIStyle.Current;
            var inner = Width - Margin * 2;
            if (!s.Sims.Contains(Focus) && s.Sims.Count > 0) Focus = s.Sims[0];
            float y = TopOfBody;

            HintLabel = AddLabel(Hint(s, null), 15, style.DialogText, new Vector2(Margin, y), inner);
            y += 50;

            //the tree as it stands, around the Sim in focus, in the room the screen has to spare
            const int below = 150 + 45 + 135; //the portraits, the message and Done
            var room = Math.Min(MostTreeHeight, ScrHeight - (int)y - below - 20);
            if (room >= 110 && s.Sims.Count > 0)
            {
                try
                {
                    var layout = TreeLayout.For(s.Kin, Focus, sim => s.Sims.Contains(sim));
                    if (layout.Nodes.Count > 1)
                    {
                        var tree = new UITreeView(layout, sim => Safe(s.Picture, sim), s.Name, sim =>
                        {
                            if (AskFrom != null) return;
                            Focus = sim;
                            Message = null;
                            Refresh();
                        });
                        var scale = tree.Fit(inner, room);
                        tree.Position = new Vector2((Width - layout.Width * scale) / 2, y + (room - layout.Height * scale) / 2);
                        Body.Add(tree);
                    }
                    else
                    {
                        AddLabel("No family yet for " + s.Name(Focus) + ".", 15, style.DialogText * 0.6f, new Vector2(Margin, y + room / 2 - 10), inner, 1);
                    }
                }
                catch (Exception e)
                {
                    People.Log("Drawing the family's tree: " + e);
                }
                y += room;
            }

            //the family's portraits, to drag from and drop on
            var step = Math.Min(120f, inner / (float)Math.Max(1, s.Sims.Count));
            var left = (Width - step * s.Sims.Count) / 2 + step / 2;
            var row = y + 50;
            for (int i = 0; i < s.Sims.Count; i++)
            {
                var sim = s.Sims[i];
                var head = new UIDragHead(sim, Safe(s.Picture, sim), s.Name(sim));
                head.Position = new Vector2(left + i * step, row);
                head.Outlined = sim == Focus;
                head.OnMouse = OnHead;
                Body.Add(head);
                Heads.Add(head);
                AddLabel(Under(s, Focus, sim), 12, style.SecondaryText, new Vector2(left + i * step - step / 2, row + 75), (int)step, 1);
            }
            y = row + 100;

            if (Message != null) AddLabel(Message, 15, style.SecondaryText, new Vector2(Margin, y), inner);
            y += 45;

            var done = new UIBigButton(true);
            done.Caption = "Done";
            done.Width = 275;
            done.Position = new Vector2((Width - 275) / 2, y + 10);
            done.OnButtonClick += (b) =>
            {
                if (AskFrom != null) return;
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
            var height = (int)(y + 10 + 125);

            //the copy of a portrait that follows the pointer, over everything but the question
            Ghost = new UIDragHead(0, null, null, true) { Visible = false };
            Body.Add(Ghost);

            if (AskFrom != null && AskTo != null && s.Sims.Contains(AskFrom.Value) && s.Sims.Contains(AskTo.Value))
            {
                //the question needs room of its own on a short window
                height = Math.Max(height, TopOfBody + 30 + 55 + 2 * OptionHeight + 5 * 26 + 20 + OptionHeight + 10 + 40);
                AddQuestion(AskFrom.Value, AskTo.Value, height);
            }
            else
            {
                AskFrom = null;
                AskTo = null;
            }
            SetHeight(height);
        }
    }
}
