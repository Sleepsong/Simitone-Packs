/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// The Apartments window: everything the console's apt commands do, as buttons. It is made the way Simitone's own
    /// Packs window is (the same dialog, labels and buttons, in rows), so it needs no artwork and follows the game's
    /// look. Three pages: the households that live here, households that could move in, and the switches for trying
    /// things out.
    /// </summary>
    public class UIApartmentsDialog : UIMobileDialog
    {
        private const int Margin = 80;
        private const int RowTop = 105;
        private const int RowHeight = 115;
        private const int ButtonWidth = 275;
        private const int SmallButton = 150;
        private const int Gap = 20;

        private enum Tab { Households, MoveIn, Testing }

        /// <summary>One line of the window: a title, a line or two under it, and buttons at the right.</summary>
        private class Row
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

        private class RowButton
        {
            public string Text;
            public Action OnClick;
            public bool Green;
            public bool Disabled;
            public int Width;
        }

        private static UIApartmentsDialog Current;

        private UIContainer Body;
        private Tab Showing = Tab.Households;
        private int Page;
        private string Message; //how the last thing the player did went

        /// <summary>Open the window, or bring the open one up to date.</summary>
        public static void Open()
        {
            if (Actions.Screen?.vm == null)
            {
                Actions.Say("Apartments", "Open this while you are playing a lot.");
                return;
            }
            if (Current != null)
            {
                Current.Refresh();
                return;
            }
            Current = new UIApartmentsDialog();
            UIScreen.GlobalShowDialog(Current, true);
        }

        /// <summary>The lot is going away (a switch of household): the window goes with it.</summary>
        public static void CloseIfOpen()
        {
            Current?.CloseMe();
        }

        private UIApartmentsDialog() : base()
        {
            Caption = "Apartments";
            Refresh();
        }

        private void CloseMe()
        {
            if (Current == this) Current = null;
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

        // ---- the pieces ----

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
            btn.OnButtonClick += (b) => onClick();
            Body.Add(btn);
            return btn;
        }

        /// <summary>Do something, say how it went on the window's status line, and show the result.</summary>
        private void Do(Func<string> action)
        {
            try
            {
                Message = action();
            }
            catch (Exception e)
            {
                Message = "That went wrong: " + e.Message;
                AptLog.Write("The Apartments window: " + e);
            }
            Refresh();
        }

        private void Show(Tab tab)
        {
            Showing = tab;
            Page = 0;
            Message = null;
            Refresh();
        }

        // ---- what each page says ----

        private List<Row> HouseholdRows()
        {
            var rows = new List<Row>();
            var vm = Actions.HomeLot(out var house, out var problem);
            if (vm == null)
            {
                rows.Add(new Row("Not at home", problem) { Warn = true });
                return rows;
            }
            var file = Neighbours.FileOf(vm);
            foreach (var family in Residency.Residents(house))
            {
                var mine = Actions.IsPlayed(vm, family);
                var sims = family.FamilyGUIDs.Length;
                var detail = sims + " Sim" + (sims == 1 ? "" : "s") + ", " + family.Budget + " simoleons. Apartment: " + Actions.UnitText(vm, file, family) + ".";
                var row = new Row(Residency.NameOf(family) + (mine ? "  -  you are playing them" : ""), detail);
                var target = family;
                if (mine) row.Button("Add this room", () => Do(() => Actions.Claim(target)), true, file == null);
                else row.Button("Options", () => HouseholdOptions(target));
                rows.Add(row);
            }
            if (file == null)
            {
                rows.Add(new Row("Only one household lives here", "Move another in to share the lot. Apartments and rent start once two households live here.")
                    .Button("Move in...", () => Show(Tab.MoveIn), true));
                return rows;
            }

            var here = Actions.RoomHere(out var owner, out _);
            rows.Add(new Row("The room your Sim is in", here)
                .Button("Make shared", () => Do(Actions.Unclaim), false, owner == null));

            var rate = file.Rate;
            rows.Add(new Row("Rent: " + (rate == 0 ? "none" : rate + " a tile a day"), Actions.RentLine(file))
                .Button("-", () => Do(() => Actions.SetRent(rate - 1)), false, rate <= 0, SmallButton)
                .Button("+", () => Do(() => Actions.SetRent(rate + 1)), false, rate >= Actions.MostRent, SmallButton));
            return rows;
        }

        /// <summary>What can be done with a neighbouring household, as a question with a button for each answer.</summary>
        private void HouseholdOptions(FAMI family)
        {
            var name = Residency.NameOf(family);
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = name,
                Message = "Play: save the lot, then play this household. Yours become neighbours.\n\n"
                    + "Give this room: the room your Sim is standing in becomes part of their apartment.\n\n"
                    + "Move out: they go back to the family bin. Their things stay.",
                Buttons = new UIAlertButton[]
                {
                    new UIAlertButton(UIAlertButtonType.Yes, (b) =>
                    {
                        alert.Close();
                        var problem = Actions.Switch(family);
                        if (problem == null) CloseMe(); //the lot is about to be replaced
                        else Do(() => problem);
                    }, "Play"),
                    new UIAlertButton(UIAlertButtonType.OK, (b) => { alert.Close(); Do(() => Actions.Claim(family)); }, "Give this room"),
                    new UIAlertButton(UIAlertButtonType.No, (b) => { alert.Close(); Do(() => Actions.MoveOut(family)); }, "Move out"),
                    new UIAlertButton(UIAlertButtonType.Cancel, (b) => { alert.Close(); }, "Cancel"),
                }
            });
            UIScreen.GlobalShowDialog(alert, true);
        }

        private List<Row> MoveInRows()
        {
            var rows = new List<Row>();
            var vm = Actions.HomeLot(out _, out var problem);
            if (vm == null)
            {
                rows.Add(new Row("Not at home", problem) { Warn = true });
                return rows;
            }
            var homeless = Residency.Homeless();
            if (homeless.Count == 0)
            {
                rows.Add(new Row("Nobody to move in", "Every household has a house. " + Actions.NoHomeless.Substring("There are none. ".Length)));
                return rows;
            }
            foreach (var family in homeless)
            {
                var target = family;
                var sims = family.FamilyGUIDs.Length;
                rows.Add(new Row(Residency.NameOf(family), sims + " Sim" + (sims == 1 ? "" : "s") + ", " + family.Budget + " simoleons. They move in beside you, free of charge.")
                    .Button("Move in", () =>
                    {
                        Showing = Tab.Households;
                        Page = 0;
                        Do(() => Actions.MoveIn(target));
                    }, true));
            }
            return rows;
        }

        private List<Row> TestingRows()
        {
            var rows = new List<Row>();
            var keep = Neighbours.KeepHome;
            rows.Add(new Row("Bring neighbours back", "When the game sends a neighbour off the lot, put them back in their apartment.")
                .Button(keep ? "On" : "Off", () => Do(() => { Actions.SetKeep(!keep); return null; }), keep));

            var needs = Neighbours.NeedsFloor;
            rows.Add(new Row("Hold neighbours' needs up", needs == null ? "Neighbours' needs are left to fall." : ("Neighbours' needs never fall below " + needs + " (of -100 to 100), so nobody starves while you play another household."))
                .Button(needs != null ? "On" : "Off", () => Do(() => { Actions.SetNeeds(needs == null ? 50 : (int?)null); return null; }), needs != null));

            var treat = Neighbours.Treat;
            rows.Add(new Row("The game is told neighbours are", treat == Treatment.Visitor ? "Visitors: what the game makes of any Sim who is not in the family being played."
                    : "Family: as if they were in the family being played. An experiment.")
                .Button(treat == Treatment.Visitor ? "Visitors" : "Family", () => Do(() => { Actions.SetTreat(treat == Treatment.Visitor ? Treatment.Family : Treatment.Visitor); return null; })));

            var watch = Neighbours.Watch;
            rows.Add(new Row("Write down what neighbours do", "A line in the log each time a neighbour starts doing something else, or moves to another room.")
                .Button(watch ? "On" : "Off", () => Do(() => { Actions.SetWatch(!watch); return null; }), watch));

            rows.Add(new Row("The log", "These switches last until the game closes. Everything is written to:\n" + AptLog.FilePath));
            return rows;
        }

        // ---- drawing it ----

        /// <summary>Rebuild the window from how things are now.</summary>
        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);

            var style = UIStyle.Current;

            //the three pages, across the top
            var tabs = new[] { ("Households", Tab.Households), ("Move in", Tab.MoveIn), ("Testing", Tab.Testing) };
            var tabWidth = Math.Min(ButtonWidth, (Width - Margin * 2 - Gap * (tabs.Length - 1)) / tabs.Length);
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i].Item2;
                AddButton(tabs[i].Item1, Showing == tab, new Vector2(Margin + i * (tabWidth + Gap), RowTop), tabWidth, () => Show(tab));
            }
            var top = RowTop + RowHeight;

            List<Row> all;
            try
            {
                all = (Showing == Tab.Households) ? HouseholdRows() : (Showing == Tab.MoveIn) ? MoveInRows() : TestingRows();
            }
            catch (Exception e)
            {
                AptLog.Write("The Apartments window: " + e);
                all = new List<Row>() { new Row("Something went wrong", e.Message) { Warn = true } };
            }

            //as many rows as the screen has room for, under the pages and over the status line and buttons
            var perPage = Math.Max(1, (ScrHeight - top - 60 - 150) / RowHeight);
            var pages = Math.Max(1, (all.Count + perPage - 1) / perPage);
            Page = Math.Max(0, Math.Min(Page, pages - 1));
            var rows = Math.Min(perPage, all.Count - Page * perPage);

            for (int i = 0; i < rows; i++)
            {
                var row = all[Page * perPage + i];
                var y = top + i * RowHeight;
                var buttonsWidth = row.Buttons.Sum(x => x.Width) + Gap * Math.Max(0, row.Buttons.Count - 1);
                var textWidth = Width - Margin * 2 - (buttonsWidth > 0 ? buttonsWidth + Margin : 0);

                AddLabel(row.Title, 19, style.DialogText, new Vector2(Margin, y), textWidth, 1);
                AddLabel(row.Detail, 12, row.Warn ? style.NegMoney : style.DialogText * 0.75f, new Vector2(Margin, y + 40), textWidth, 3);

                var x = Width - Margin - buttonsWidth;
                foreach (var button in row.Buttons)
                {
                    var made = AddButton(button.Text, button.Green, new Vector2(x, y), button.Width, button.OnClick);
                    made.Disabled = button.Disabled;
                    x += button.Width + Gap;
                }
            }

            var statusY = top + Math.Max(rows, 1) * RowHeight;
            if (Message != null) AddLabel(Message, 15, style.SecondaryText, new Vector2(Margin, statusY), Width - Margin * 2, 2);

            var buttonY = statusY + 70;
            AddButton("Close", true, new Vector2((Width - ButtonWidth) / 2, buttonY), ButtonWidth, CloseMe);
            if (pages > 1)
            {
                var prev = AddButton("<", false, new Vector2(Margin, buttonY), SmallButton, () => { Page--; Message = null; Refresh(); });
                prev.Disabled = Page == 0;
                var next = AddButton(">", false, new Vector2(Width - Margin - SmallButton, buttonY), SmallButton, () => { Page++; Message = null; Refresh(); });
                next.Disabled = Page == pages - 1;
                AddLabel((Page + 1) + " / " + pages, 15, style.DialogText, new Vector2(Margin + SmallButton + Gap, buttonY + 35), 150, 1);
            }

            SetHeight(buttonY + 125);
        }
    }
}
