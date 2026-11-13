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

        public enum Tab { Households, Apartments, MoveIn, Testing }

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

        /// <summary>Open the window, or bring the open one up to date. It can be opened on a page, with something to say.</summary>
        public static void Open(Tab? tab = null, string message = null)
        {
            if (Actions.Screen?.vm == null)
            {
                Actions.Say("Apartments", "Open this while you are on a lot.");
                return;
            }
            if (Current == null)
            {
                Current = new UIApartmentsDialog();
                UIScreen.GlobalShowDialog(Current, true);
            }
            if (tab != null)
            {
                Current.Showing = tab.Value;
                Current.Page = 0;
            }
            if (message != null) Current.Message = message;
            Current.Refresh();
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
            //nothing a button does may take the game down: what goes wrong is logged, and shown along the bottom
            btn.OnButtonClick += (b) =>
            {
                if (!ApartmentsPack.Guard("doing what a button in the Apartments window asked (" + text + ")", onClick))
                {
                    Message = "That went wrong. The log says how.";
                    ApartmentsPack.Guard("showing the window again", Refresh);
                }
            };
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
            var residents = Residency.Residents(house);
            if (residents.Count == 0)
            {
                rows.Add(new Row("Nobody lives here yet", (file == null)
                    ? "To let several households share this lot, make it an apartment building and lay out its apartments on the Apartments page."
                    : "Lay out the apartments on the Apartments page and save. Then, from the neighbourhood screen, choose a family to move in and pick this house: you are asked which apartment they take.")
                    .Button("Apartments", () => Show(Tab.Apartments), true));
                return rows;
            }
            foreach (var family in residents)
            {
                var mine = Actions.IsPlayed(vm, family);
                var sims = family.FamilyGUIDs.Length;
                var detail = sims + " Sim" + (sims == 1 ? "" : "s") + ", " + family.Budget + " simoleons. " + (file == null ? "" : ("Lives in: " + Actions.UnitText(vm, file, family) + "."));
                if (!mine && file != null)
                {
                    var outNow = Neighbours.OutNow(vm, family);
                    detail += (outNow.Count == 0) ? " At home." : (" Out now: " + string.Join(", ", outNow) + ".");
                }
                var row = new Row(Residency.NameOf(family) + (mine ? "  -  you are playing them" : ""), detail);
                var target = family;
                if (!mine) row.Button("Options", () => HouseholdOptions(target));
                rows.Add(row);
            }
            if (file == null)
            {
                rows.Add(new Row("This is an ordinary house", "Make it an apartment building to let other households live here too, each in its own apartment.")
                    .Button("Apartments", () => Show(Tab.Apartments), true));
            }
            return rows;
        }

        /// <summary>What can be done with a neighbouring household, as a question with a button for each answer.</summary>
        private void HouseholdOptions(FAMI family)
        {
            Actions.Choose(Residency.NameOf(family),
                "Play: save the lot, then play this household. Yours go home, and become neighbours.\n\n"
                + "Bring out: one of them comes out of their apartment now.\n\n"
                + "Move out: they go back to the family bin. Their things stay.",
                new[]
                {
                    Actions.Choice("Play", () =>
                    {
                        var problem = Actions.Switch(family);
                        if (problem == null) CloseMe(); //the lot is about to be replaced
                        else Do(() => problem);
                    }),
                    Actions.Choice("Bring out", () => Do(() => Actions.BringOut(family))),
                    Actions.Choice("Move out", () => Do(() => Actions.MoveOut(family))),
                });
        }

        private List<Row> ApartmentRows()
        {
            var rows = new List<Row>();
            var vm = Actions.HomeLot(out var house, out var problem);
            if (vm == null)
            {
                rows.Add(new Row("Not at home", problem) { Warn = true });
                return rows;
            }
            var file = Neighbours.FileOf(vm);
            if (file == null)
            {
                rows.Add(new Row("This is an ordinary house", "An apartment building is divided into apartments, each rented by a household. You can make one out of an empty lot or a house someone lives in. Nothing about the house itself changes.")
                    .Button("Make building", () => Do(Actions.MakeBuilding), true));
                return rows;
            }

            foreach (var apartment in file.Apartments.OrderBy(x => x.Id))
            {
                var target = apartment;
                var detail = Actions.SizeText(vm, file, apartment) + ".";
                if (Units.LostTiles(vm, apartment).Count > 0) detail += " Part of it is no longer a room: a wall has gone. It counts as shared until it is closed again.";
                rows.Add(new Row(apartment.Name + "  -  " + Actions.TenantText(house, apartment), detail) { Warn = Units.LostTiles(vm, apartment).Count > 0 }
                    .Button("Add room", () => PickRoom(target, false), true, false, 200)
                    .Button("More", () => ApartmentOptions(house, file, target), false, false, SmallButton));
            }

            rows.Add(new Row("New apartment", file.Apartments.Count == 0
                    ? "Add the first apartment, then click in each room that belongs to it. Every room you leave out is shared by everyone, and so is outdoors."
                    : "Add another apartment, then click in each room that belongs to it.")
                .Button("Add", () =>
                {
                    var made = Actions.AddApartment(out var result);
                    if (made == null) Do(() => result);
                    else PickRoom(made, false);
                }, true));

            var rate = file.Rate;
            rows.Add(new Row("Rent: " + (rate == 0 ? "none" : rate + " a tile a day"), Actions.RentLine(file))
                .Button("-", () => Do(() => Actions.SetRent(rate - 1)), false, rate <= 0, SmallButton)
                .Button("+", () => Do(() => Actions.SetRent(rate + 1)), false, rate >= Actions.MostRent, SmallButton));

            if (Residency.Residents(house).Count <= 1)
            {
                rows.Add(new Row("Apartment building", "Make this an ordinary house again. Its apartments are forgotten.")
                    .Button("Ordinary house", () => Do(Actions.MakeOrdinary)));
            }
            return rows;
        }

        private void ApartmentOptions(int house, LotFile file, Apartment apartment)
        {
            Actions.Choose(apartment.Name,
                "Remove room: click in a room to take it out of the apartment.\n\n"
                + "Tenant: choose which household lives here, or nobody.\n\n"
                + "Delete: the apartment is gone, and its rooms are shared.",
                new[]
                {
                    Actions.Choice("Remove room", () => PickRoom(apartment, true)),
                    Actions.Choice("Tenant", () => ChooseTenant(house, file, apartment)),
                    Actions.Choice("Delete", () => Do(() => Actions.DeleteApartment(apartment))),
                });
        }

        private void ChooseTenant(int house, LotFile file, Apartment apartment)
        {
            var choices = new List<KeyValuePair<string, Action>>();
            foreach (var family in Residency.Residents(house))
            {
                if (family.ChunkID == apartment.Tenant) continue;
                var target = family;
                choices.Add(Actions.Choice(Residency.NameOf(family), () => Do(() => Actions.SetTenant(apartment, target))));
            }
            if (!apartment.Vacant) choices.Add(Actions.Choice("Nobody", () => Do(() => Actions.SetTenant(apartment, null))));
            Actions.Choose("Who lives in " + apartment.Name + "?",
                choices.Count == 0 ? "No household lives on this lot yet. Households move in from the neighbourhood screen, or from the Move in page."
                    : "Only households that already live on this lot are listed. A household that moves here takes this one's place in its old apartment.",
                choices);
        }

        private static bool PickHintShown;

        /// <summary>Put the window away and let the player click in a room, then come back and say how it went.</summary>
        private void PickRoom(Apartment apartment, bool remove)
        {
            CloseMe();
            Action begin = () =>
            {
                var started = UIRoomPicker.Begin(
                    pos => Open(Tab.Apartments, remove ? Actions.RemoveRoom(pos) : Actions.AddRoom(apartment, pos)),
                    () => Open(Tab.Apartments, "No room was chosen."));
                if (!started) Open(Tab.Apartments, "There is no lot to choose a room on.");
            };
            if (PickHintShown)
            {
                begin();
                return;
            }
            PickHintShown = true;
            Actions.Choose(remove ? "Remove a room" : "Add a room",
                "Click inside a room" + (remove ? " to take it out of its apartment." : (" to add it to " + apartment.Name + "."))
                + " To choose a room on another floor, change floor first. Esc cancels.\n\nThis window comes back when you have clicked.",
                new[] { Actions.Choice("OK", begin) },
                () => Open(Tab.Apartments, "No room was chosen."));
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
            if (Actions.Played(vm) == null)
            {
                rows.Add(new Row("Nobody lives here yet", "Households move into an empty building from the neighbourhood screen: choose the family, then this house, and you are asked which apartment they take. Save the lot first."));
                return rows;
            }
            var homeless = Residency.Homeless();
            if (homeless.Count == 0)
            {
                rows.Add(new Row("Nobody to move in", "Every household has a house. Make a family first and leave it in the family bin."));
                return rows;
            }
            foreach (var family in homeless)
            {
                var target = family;
                var sims = family.FamilyGUIDs.Length;
                rows.Add(new Row(Residency.NameOf(family), sims + " Sim" + (sims == 1 ? "" : "s") + ", " + family.Budget + " simoleons. They move in beside you, free of charge, and you choose their apartment.")
                    .Button("Move in", () => MoveInHere(target), true));
            }
            return rows;
        }

        /// <summary>Move a household from the family bin in beside the played one, asking which free apartment it takes.</summary>
        private void MoveInHere(FAMI family)
        {
            Action<Apartment> go = apartment =>
            {
                Showing = Tab.Households;
                Page = 0;
                Do(() => Actions.MoveIn(family, apartment));
            };
            var vm = Actions.HomeLot(out _, out _);
            var file = (vm == null) ? null : Neighbours.FileOf(vm);
            var free = file?.ToLet ?? new List<Apartment>();
            if (free.Count == 0)
            {
                go(null);
                return;
            }
            var choices = free.Select(x => Actions.Choice(x.Name, () => go(x))).ToList();
            choices.Add(Actions.Choice("No apartment", () => go(null)));
            Actions.Choose("Which apartment for " + Residency.NameOf(family) + "?",
                string.Join("\n", free.Select(x => x.Name + ": " + Actions.SizeText(vm, file, x) + ".")), choices);
        }

        private List<Row> TestingRows()
        {
            var rows = new List<Row>();
            var often = Neighbours.ComeOut;
            var next = (often == Outings.Never) ? Outings.Sometimes : (often == Outings.Sometimes) ? Outings.Often : Outings.Never;
            rows.Add(new Row("Neighbours come out", often == Outings.Never ? "Neighbours stay in their apartments unless you bring one out."
                    : ("A neighbour comes out about every " + (often == Outings.Often ? "45 minutes" : "three hours") + " of the game's day, between 8 in the morning and 10 at night, and goes home after an hour or two."))
                .Button(often.ToString(), () => Do(() => { Actions.SetOutings(next); return null; }), often != Outings.Never));

            var vm = Actions.Screen?.vm;
            var out_ = (vm == null) ? new List<string>() : Neighbours.OutNow(vm);
            rows.Add(new Row("Out now: " + (out_.Count == 0 ? "nobody" : string.Join(", ", out_)), "Bring a neighbour out of their apartment now, or send the ones who are out back home.")
                .Button("Bring out", () => Do(() => Actions.BringOut()), true, false, 190)
                .Button("Send home", () => Do(Actions.SendHome), false, out_.Count == 0, 190));

            var dim = Neighbours.Dim;
            rows.Add(new Row("Dim the other apartments", "The apartments of households you are not playing are drawn dark: nobody is seen to be home.")
                .Button(dim ? "On" : "Off", () => Do(() => { Actions.SetDim(!dim); return null; }), dim));

            var needs = Neighbours.NeedsFloor;
            rows.Add(new Row("Hold neighbours' needs up", needs == null ? "Neighbours' needs are left to fall while they are out." : ("While a neighbour is out, their needs never fall below " + needs + " (of -100 to 100)."))
                .Button(needs != null ? "On" : "Off", () => Do(() => { Actions.SetNeeds(needs == null ? 50 : (int?)null); return null; }), needs != null));

            var watch = Neighbours.Watch;
            rows.Add(new Row("Write down what neighbours do", "A line in the log each time a neighbour who is out starts doing something else, or moves to another room.")
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
            var tabs = new[] { ("Households", Tab.Households), ("Apartments", Tab.Apartments), ("Move in", Tab.MoveIn), ("Testing", Tab.Testing) };
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
                all = (Showing == Tab.Households) ? HouseholdRows() : (Showing == Tab.Apartments) ? ApartmentRows() : (Showing == Tab.MoveIn) ? MoveInRows() : TestingRows();
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
