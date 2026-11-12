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

namespace FamilyTree
{
    /// <summary>
    /// A window of rows: a title, a line or two under it, and buttons at the right. It is made the way Simitone's own
    /// Packs window is (the same dialog, labels and buttons), so it needs no artwork and follows the game's look. The
    /// two windows of this pack say what their rows are, and this draws them, a page at a time.
    /// </summary>
    public abstract class UIRowsDialog : UIMobileDialog
    {
        protected const int Margin = 80;
        protected const int RowTop = 105;
        protected const int ButtonWidth = 275;
        protected const int SmallButton = 150;
        protected const int Gap = 20;

        protected class Row
        {
            public string Title;
            public string Detail;
            public bool Warn;
            public List<RowButton> Buttons = new List<RowButton>();

            public Row(string title, string detail) { Title = title; Detail = detail; }

            public Row Button(string text, Action onClick, bool green = false, bool disabled = false, int width = ButtonWidth)
            {
                Buttons.Add(new RowButton() { Text = text, OnClick = onClick, Green = green, Disabled = disabled, Width = width });
                return this;
            }
        }

        protected class RowButton
        {
            public string Text;
            public Action OnClick;
            public bool Green;
            public bool Disabled;
            public int Width;
        }

        private UIContainer Body;
        protected int Page;
        /// <summary>How the last thing the player did went. Shown under the rows.</summary>
        protected string Message;

        /// <summary>How tall a row is. A row whose buttons do not fit on one line takes more than one.</summary>
        protected virtual int RowHeight => 115;

        /// <summary>What the window says, as it is now.</summary>
        protected abstract List<Row> Rows();

        /// <summary>Buttons beside Close, at the bottom. (text, what it does)</summary>
        protected virtual List<(string, Action)> BottomButtons() => new List<(string, Action)>();

        protected virtual void OnClosed() { }

        protected void CloseMe()
        {
            OnClosed();
            Close();
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

        private UILabel AddLabel(string text, int size, Color color, Vector2 pos, int width, int maxLines)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = maxLines;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            label.Caption = text ?? "";
            Body.Add(label);
            return label;
        }

        private UIBigButton AddButton(string text, bool green, Vector2 pos, int width, Action onClick)
        {
            var btn = new UIBigButton(green);
            btn.Caption = text;
            btn.Width = width;
            btn.Position = pos;
            btn.OnButtonClick += (b) =>
            {
                try
                {
                    onClick();
                }
                catch (Exception e)
                {
                    Message = "That went wrong: " + e.Message;
                    People.Log("A window of the pack: " + e);
                    Refresh();
                }
            };
            Body.Add(btn);
            return btn;
        }

        /// <summary>How many lines of buttons a row needs in the room it has.</summary>
        private static int ButtonLines(Row row, int room, out int widest)
        {
            int lines = 1, x = 0;
            widest = 0;
            foreach (var button in row.Buttons)
            {
                if (x > 0 && x + Gap + button.Width > room)
                {
                    lines++;
                    x = 0;
                }
                x += (x > 0 ? Gap : 0) + button.Width;
                widest = Math.Max(widest, x);
            }
            return lines;
        }

        /// <summary>Rebuild the window from how things are now.</summary>
        protected void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);

            var style = UIStyle.Current;
            List<Row> all;
            try
            {
                all = Rows();
            }
            catch (Exception e)
            {
                People.Log("A window of the pack: " + e);
                all = new List<Row>() { new Row("Something went wrong", e.Message) { Warn = true } };
            }

            //buttons get at most this much of the row; what is left is for its words
            var inner = Width - Margin * 2;
            var buttonRoom = Math.Max(ButtonWidth, inner * 3 / 5);
            var heights = all.Select(row => RowHeight * ButtonLines(row, buttonRoom, out _)).ToList();

            //as many rows as the screen has room for, over the status line and the bottom buttons
            var room = Math.Max(RowHeight, ScrHeight - RowTop - 60 - 150);
            var pageStarts = new List<int>() { 0 };
            int used = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (used > 0 && used + heights[i] > room)
                {
                    pageStarts.Add(i);
                    used = 0;
                }
                used += heights[i];
            }
            var pages = pageStarts.Count;
            Page = Math.Max(0, Math.Min(Page, pages - 1));
            var first = pageStarts[Page];
            var end = (Page + 1 < pages) ? pageStarts[Page + 1] : all.Count;

            var y = RowTop;
            for (int i = first; i < end; i++)
            {
                var row = all[i];
                ButtonLines(row, buttonRoom, out var buttonsWidth);
                var textWidth = inner - (buttonsWidth > 0 ? buttonsWidth + Gap * 2 : 0);

                AddLabel(row.Title, 19, style.DialogText, new Vector2(Margin, y), textWidth, 1);
                AddLabel(row.Detail, 12, row.Warn ? style.NegMoney : style.DialogText * 0.75f, new Vector2(Margin, y + 40), textWidth, 3);

                var left = Width - Margin - buttonsWidth;
                int x = 0, line = 0;
                foreach (var button in row.Buttons)
                {
                    if (x > 0 && x + Gap + button.Width > buttonRoom)
                    {
                        line++;
                        x = 0;
                    }
                    if (x > 0) x += Gap;
                    var made = AddButton(button.Text, button.Green, new Vector2(left + x, y + line * RowHeight), button.Width, button.OnClick);
                    made.Disabled = button.Disabled;
                    x += button.Width;
                }
                y += heights[i];
            }
            if (end == first) y += RowHeight;

            if (Message != null) AddLabel(Message, 15, style.SecondaryText, new Vector2(Margin, y), inner, 2);

            var buttonY = y + 70;
            var bottom = BottomButtons();
            bottom.Add(("Close", CloseMe));
            var bottomWidth = bottom.Count * ButtonWidth + (bottom.Count - 1) * Gap;
            var bx = (Width - bottomWidth) / 2;
            for (int i = 0; i < bottom.Count; i++)
            {
                AddButton(bottom[i].Item1, i == bottom.Count - 1, new Vector2(bx, buttonY), ButtonWidth, bottom[i].Item2);
                bx += ButtonWidth + Gap;
            }
            if (pages > 1)
            {
                //the page buttons go under the others where the window is too narrow to have them beside
                var beside = (Width - bottomWidth) / 2 >= Margin + SmallButton + Gap;
                var pageY = beside ? buttonY : buttonY + 115;
                var prev = AddButton("<", false, new Vector2(Margin, pageY), SmallButton, () => { Page--; Message = null; Refresh(); });
                prev.Disabled = Page == 0;
                var next = AddButton(">", false, new Vector2(Width - Margin - SmallButton, pageY), SmallButton, () => { Page++; Message = null; Refresh(); });
                next.Disabled = Page == pages - 1;
                AddLabel((Page + 1) + " / " + pages, 15, style.DialogText, new Vector2(Width / 2 - 30, pageY + (beside ? 110 : 35)), 150, 1);
                if (!beside) buttonY = pageY;
            }

            SetHeight(buttonY + 125 + (pages > 1 ? 30 : 0));
        }
    }
}
