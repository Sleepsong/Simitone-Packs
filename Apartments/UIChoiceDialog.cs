/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// A question with a button for each answer, and Cancel.
    ///
    /// The game's own message box cannot do this: it keeps its buttons by kind (OK, Yes, No, Cancel) and takes one of
    /// each, so a second answer of the same kind is an error. This is a dialog of the pack's own, made like its main
    /// window from the game's dialog, labels and buttons, and it takes as many answers as it is given: they are laid
    /// out in rows, and in pages if there are more than the screen has room for.
    /// </summary>
    public class UIChoiceDialog : UIMobileDialog
    {
        private const int Margin = 80;
        private const int Top = 105;
        private const int ButtonWidth = 275;
        private const int SmallButton = 150;
        private const int ButtonHeight = 115; //a button and the space under it
        private const int Gap = 20;
        private const int LineHeight = 24;
        private const int TextSize = 15;

        private string Title;
        private string Message;
        private List<KeyValuePair<string, Action>> Choices;
        private Action Cancelled;
        private UIContainer Body;
        private int Page;
        private bool Answered;

        public static void Show(string title, string message, IEnumerable<KeyValuePair<string, Action>> choices, Action cancelled = null)
        {
            var dialog = new UIChoiceDialog(title, message, choices.ToList(), cancelled);
            UIScreen.GlobalShowDialog(dialog, true);
        }

        private UIChoiceDialog(string title, string message, List<KeyValuePair<string, Action>> choices, Action cancelled) : base()
        {
            Caption = title;
            Title = title;
            Message = message ?? "";
            Choices = choices;
            Cancelled = cancelled;
            Refresh();
        }

        public override void GameResized()
        {
            base.GameResized();
            ApartmentsPack.Guard("laying a question out again", Refresh);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //ride in and out with the dialog's stripes
            if (Body != null) Body.X = ((Closing) ? 1 : -1) * (1 - InterpolatedAnimation) * Width;
        }

        /// <summary>How many lines a text takes when wrapped to a width. A guess from its length, on the generous side: room is kept for it.</summary>
        public static int LinesOf(string text, int width, int textSize)
        {
            var perLine = Math.Max(10, (int)(width / (textSize * 0.55f)));
            var lines = 0;
            foreach (var paragraph in (text ?? "").Split('\n'))
            {
                lines += Math.Max(1, (paragraph.Length + perLine - 1) / perLine);
            }
            return lines;
        }

        /// <summary>How many buttons fit side by side in a width.</summary>
        public static int Columns(int width, int buttonWidth, int gap)
        {
            return Math.Max(1, (width + gap) / (buttonWidth + gap));
        }

        private void Answer(Action action)
        {
            if (Answered) return;
            Answered = true;
            Close();
            if (action != null) ApartmentsPack.Guard("doing what was chosen", action);
        }

        private UIBigButton AddButton(string text, bool green, Vector2 pos, int width, Action onClick)
        {
            var btn = new UIBigButton(green);
            btn.Caption = text;
            btn.Width = width;
            btn.Position = pos;
            btn.OnButtonClick += (b) =>
            {
                AptLog.Write("[question] " + Title + ": " + text);
                ApartmentsPack.Guard("answering a question", onClick);
            };
            Body.Add(btn);
            return btn;
        }

        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);

            var style = UIStyle.Current;
            var inner = Width - Margin * 2;

            var lines = Math.Min(12, LinesOf(Message, inner, TextSize));
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = TextSize;
            label.CaptionStyle.Color = style.DialogText;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = lines;
            label.Position = new Vector2(Margin, Top);
            label.Size = new Vector2(inner, 1);
            label.Caption = Message;
            Body.Add(label);

            var buttonsTop = Top + lines * LineHeight + 30;

            //the answers, in rows: as many rows as the screen has room for above the last row (Cancel, and the page turners)
            var columns = Columns(inner, ButtonWidth, Gap);
            var rowsThatFit = Math.Max(1, (ScrHeight - buttonsTop - ButtonHeight - 60) / ButtonHeight);
            var perPage = columns * rowsThatFit;
            var pages = Math.Max(1, (Choices.Count + perPage - 1) / perPage);
            Page = Math.Max(0, Math.Min(Page, pages - 1));
            var shown = Math.Min(perPage, Choices.Count - Page * perPage);
            var rows = Math.Max(1, (shown + columns - 1) / columns);

            for (int i = 0; i < shown; i++)
            {
                var choice = Choices[Page * perPage + i];
                var inRow = Math.Min(columns, shown - (i / columns) * columns);
                var rowWidth = inRow * ButtonWidth + (inRow - 1) * Gap;
                var x = (Width - rowWidth) / 2 + (i % columns) * (ButtonWidth + Gap);
                var y = buttonsTop + (i / columns) * ButtonHeight;
                AddButton(choice.Key, true, new Vector2(x, y), ButtonWidth, () => Answer(choice.Value));
            }

            var lastY = buttonsTop + rows * ButtonHeight;
            AddButton("Cancel", false, new Vector2((Width - ButtonWidth) / 2, lastY), ButtonWidth, () => Answer(Cancelled));
            if (pages > 1)
            {
                var prev = AddButton("<", false, new Vector2(Margin, lastY), SmallButton, () => { Page--; Refresh(); });
                prev.Disabled = Page == 0;
                var next = AddButton(">", false, new Vector2(Width - Margin - SmallButton, lastY), SmallButton, () => { Page++; Refresh(); });
                next.Disabled = Page == pages - 1;
            }

            SetHeight(lastY + 125);
        }
    }
}
