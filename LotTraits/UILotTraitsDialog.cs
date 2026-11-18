/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Linq;

namespace LotTraits
{
    /// <summary>
    /// The lot's traits: every trait there is, each with what it does and a button to give it to the lot or take it
    /// off. Made like Simitone's own Packs window, from the game's dialog, labels and buttons.
    /// </summary>
    public class UILotTraitsDialog : UIMobileDialog
    {
        private const int Margin = 80;
        private const int RowTop = 105;
        private const int RowHeight = 115;
        private const int ButtonWidth = 275;

        private readonly VM Lot;
        private UIContainer Body;
        private int Page;
        private string Message; //why the last choice was refused

        public static void Open()
        {
            var vm = LotTraitsPack.CurrentLot;
            if (vm == null) return;
            UIScreen.GlobalShowDialog(new UILotTraitsDialog(vm), true);
        }

        private UILotTraitsDialog(VM vm) : base()
        {
            Lot = vm;
            Caption = "Lot Traits";
            Refresh();
        }

        public override void GameResized()
        {
            base.GameResized();
            LotTraitsPack.Guard("laying the Lot Traits window out again", Refresh);
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
            label.Caption = text;
            Body.Add(label);
            return label;
        }

        private UIBigButton AddButton(string text, bool green, Vector2 pos, int width, Action onClick)
        {
            var btn = new UIBigButton(green);
            btn.Caption = text;
            btn.Width = width;
            btn.Position = pos;
            btn.OnButtonClick += (b) => LotTraitsPack.Guard("choosing a lot trait", onClick);
            Body.Add(btn);
            return btn;
        }

        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);

            var book = Lots.Book;
            var set = Lots.Of(Lot);
            var traits = book.Traits;
            var style = UIStyle.Current;
            var textWidth = Width - Margin * 3 - ButtonWidth;

            //as many rows as the screen has room for, under the title and over the status line and buttons
            var perPage = Math.Max(1, (ScrHeight - RowTop - 60 - 150) / RowHeight);
            var pages = Math.Max(1, (traits.Count + perPage - 1) / perPage);
            Page = Math.Max(0, Math.Min(Page, pages - 1));
            var rows = Math.Min(perPage, traits.Count - Page * perPage);

            for (int i = 0; i < rows; i++)
            {
                var trait = traits[Page * perPage + i];
                var y = RowTop + i * RowHeight;
                var on = set.Ids.Contains(trait.Id);
                AddLabel(trait.Name, 19, style.DialogText, new Vector2(Margin, y), textWidth, 1);
                var detail = (string.IsNullOrEmpty(trait.Description) ? "" : trait.Description + " ") + trait.EffectText();
                AddLabel(detail, 12, style.DialogText * 0.75f, new Vector2(Margin, y + 40), textWidth, 3);
                AddButton(on ? "On" : "Off", on, new Vector2(Width - Margin - ButtonWidth, y), ButtonWidth, () => Toggle(trait));
            }

            var statusY = RowTop + Math.Max(rows, 1) * RowHeight;
            string status;
            var statusColor = style.DialogText;
            var chosen = set.Known(book).Count;
            if (Message != null) { status = Message; statusColor = style.NegMoney; }
            else if (traits.Count == 0) { status = string.Join("\n", LotTraitsPack.Problems.DefaultIfEmpty("There are no traits to choose.")); statusColor = style.NegMoney; }
            else
            {
                status = "This lot has " + chosen + " of the " + book.Most + " traits it can have. They are kept when the lot is saved.";
                var unknown = set.Unknown(book);
                if (unknown.Count > 0) status += "\nKept but not in use, since traits.json does not have them: " + string.Join(", ", unknown) + ".";
                statusColor = style.SecondaryText;
            }
            if (traits.Count == 0) statusY = RowTop + 20;
            AddLabel(status, 15, statusColor, new Vector2(Margin, statusY), Width - Margin * 2, 3);

            var buttonY = statusY + 80;
            AddButton("Close", true, new Vector2((Width - ButtonWidth) / 2, buttonY), ButtonWidth, Close);
            if (pages > 1)
            {
                var prev = AddButton("<", false, new Vector2(Margin, buttonY), 150, () => { Page--; Message = null; Refresh(); });
                prev.Disabled = Page == 0;
                var next = AddButton(">", false, new Vector2(Width - Margin - 150, buttonY), 150, () => { Page++; Message = null; Refresh(); });
                next.Disabled = Page == pages - 1;
                AddLabel((Page + 1) + " / " + pages, 15, style.DialogText, new Vector2(Margin + 170, buttonY + 35), 150, 1);
            }

            SetHeight(buttonY + 125);
        }

        private void Toggle(Trait trait)
        {
            var set = Lots.Of(Lot);
            if (set.Ids.Contains(trait.Id))
            {
                set.Remove(trait.Id);
                Message = null;
            }
            else Message = set.Add(trait, Lots.Book);
            Lots.Refresh(Lot);
            Log.Write((set.Ids.Contains(trait.Id) ? "On: " : "Off: ") + trait.Name + (Message != null ? " (" + Message + ")" : ""));
            Refresh();
        }
    }
}
