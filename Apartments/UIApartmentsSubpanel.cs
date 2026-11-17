/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Content;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// The Apartments tab of build mode: a tool for laying a building out. A button for each apartment, in its colour;
    /// click one, then click rooms on the lot to add them to it, until Esc or the button again. New apartment, Take
    /// rooms out, and More (the Apartments window, for names, tenants and rent). On an ordinary house, Make building.
    ///
    /// Each apartment's floor is drawn on the lot in its colour while build mode is open (see LotOverlay).
    /// </summary>
    public class UIApartmentsSubpanel : UISubpanel
    {
        /// <summary>The tab's number among build mode's: far from the game's own (0 to 2).</summary>
        public const int CategoryId = 90;
        /// <summary>The tab's icon, one of Simitone's own.</summary>
        public const string TabIcon = "cat_build_door.png";

        private const int Slot = 120;
        private const int First = 50;

        public static UIApartmentsSubpanel Current;

        private VM vm;
        private List<UIElement> Parts = new List<UIElement>();
        private Apartment Choosing;
        private bool TakingOut;
        private string Message;

        public UIApartmentsSubpanel(TS1GameScreen game) : base(game)
        {
            Current = this;
            vm = game.vm;
            Build();
        }

        /// <summary>The lot has been put back to an earlier moment (the Undo pack): its apartments are new ones. Choose none, and lay out again.</summary>
        public void Refresh()
        {
            Choosing = null;
            TakingOut = false;
            Build();
        }

        /// <summary>Lay the tab out again (after anything has changed).</summary>
        private void Build()
        {
            foreach (var part in Parts) Remove(part);
            Parts.Clear();

            var lot = Actions.HomeLot(out var house, out var problem);
            var file = (lot == null) ? null : Neighbours.FileOf(lot);
            var slots = new List<Tuple<string, string, Color?, bool, Action>>(); //name, icon, colour, chosen, click
            string text;
            if (lot == null) text = problem;
            else if (file == null)
            {
                text = "This is an ordinary house. Make it an apartment building to divide it into apartments, each rented by a household. Nothing about the house itself changes.";
                slots.Add(Tuple.Create("Make building", "cat_build_arch.png", (Color?)null, false, (Action)(() => ApartmentsPack.Changing(vm, "Make an apartment building", () => After(Actions.MakeBuilding())))));
            }
            else
            {
                foreach (var apartment in file.Apartments.OrderBy(x => x.Id))
                {
                    var target = apartment;
                    slots.Add(Tuple.Create(apartment.Name, "cat_build_arch.png", (Color?)LotOverlay.ColourOf(file, apartment), Choosing == apartment, (Action)(() => Choose(target, false))));
                }
                slots.Add(Tuple.Create("New", "cat_other.png", (Color?)null, false, (Action)NewApartment));
                if (file.Apartments.Count > 0) slots.Add(Tuple.Create("Take out", "cat_build_flor.png", (Color?)null, TakingOut, (Action)(() => Choose(null, true))));
                var chosen = Choosing;
                if (chosen != null) slots.Add(Tuple.Create("Delete", "cat_cancel.png", (Color?)null, false, (Action)(() => Delete(house, chosen))));
                slots.Add(Tuple.Create("More...", "cat_misc.png", (Color?)null, false, (Action)(() => UIApartmentsDialog.Open(UIApartmentsDialog.Tab.Apartments))));

                if (Choosing != null) text = "Click rooms on the lot to add them to " + Choosing.Name + ". The room under the cursor is shown in white. Press Esc, or " + Choosing.Name + " again, when you have finished. Delete: " + Choosing.Name + " is gone, and its rooms are shared.";
                else if (TakingOut) text = "Take out: click rooms on the lot to take them out of their apartment. Press Esc, or Take out again, when you have finished.";
                else if (file.Apartments.Count == 0) text = "New: add the first apartment, then click each room that belongs to it. Every room you leave out is shared, and so is outdoors.";
                else text = "Press an apartment, then click rooms on the lot to add them to it (or press Delete). New: another apartment. Rooms with no colour are shared. More: names, tenants and rent.";
            }

            //narrower slots if there are many, so they still fit beside the words
            var width = (int)Size.X;
            var slot = (slots.Count == 0) ? Slot : Math.Max(84, Math.Min(Slot, (width - First - 260) / slots.Count));
            for (int i = 0; i < slots.Count; i++)
            {
                var item = slots[i];
                var x = First + i * slot;
                var button = new UICatButton(Content.Get().CustomUI.Get(item.Item2).Get(GameFacade.GraphicsDevice));
                if (item.Item3 != null)
                {
                    var colour = item.Item3.Value;
                    button.Color = colour;
                    button.HoverColor = Color.Lerp(colour, Color.White, 0.35f);
                    button.ActiveColor = Color.Lerp(colour, Color.White, 0.7f);
                }
                button.Selected = item.Item4;
                button.Position = new Vector2(x, 16);
                var click = item.Item5;
                button.OnButtonClick += (b) => ApartmentsPack.Guard("using the Apartments tool", click);
                Add(button);
                Parts.Add(button);

                //centred under the button (65 wide); a lone button's label may be wider, having nothing beside it
                var labelWidth = (slots.Count == 1) ? 200 : slot;
                var label = MakeLabel(item.Item1, 12, new Vector2(x + 32 - labelWidth / 2, 106), labelWidth, 1);
                label.Alignment = TextAlignment.Middle | TextAlignment.Center;
            }

            var textX = First + slots.Count * slot + 10;
            MakeLabel(text + (string.IsNullOrEmpty(Message) ? "" : "\n" + Message), 12, new Vector2(textX, 14), Math.Max(160, width - textX - 20), 6);
            Invalidate();
        }

        private UILabel MakeLabel(string text, int size, Vector2 pos, int width, int lines)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = UIStyle.Current.Text;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = lines;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            label.Caption = text ?? "";
            Add(label);
            Parts.Add(label);
            return label;
        }

        /// <summary>Something was done: say how it went, and lay the tab out again.</summary>
        private void After(string result)
        {
            Message = result;
            Build();
        }

        /// <summary>Delete an apartment, once the player has said so.</summary>
        private void Delete(int house, Apartment apartment)
        {
            Actions.Confirm("Delete " + apartment.Name + "?",
                "Its rooms become shared. " + (apartment.Vacant ? "Nobody lives in it." : (Actions.TenantText(house, apartment) + " live in it: they stay on the lot, with no apartment.")),
                "Delete", () => ApartmentsPack.Guard("deleting an apartment", () =>
                {
                    UIRoomPicker.Stop();
                    Choosing = null;
                    TakingOut = false;
                    ApartmentsPack.Changing(vm, "Delete " + apartment.Name, () => Message = Actions.DeleteApartment(apartment));
                    if (LotOverlay.On) Neighbours.Show(vm, true);
                    if (Current == this) Build();
                }));
        }

        private void NewApartment()
        {
            Apartment made = null;
            string result = null;
            ApartmentsPack.Changing(vm, "Add an apartment", () => made = Actions.AddApartment(out result));
            Message = result;
            if (made == null) Build();
            else Choose(made, false);
        }

        /// <summary>Start choosing rooms for an apartment (or taking rooms out of theirs). The same button again stops.</summary>
        private void Choose(Apartment apartment, bool takeOut)
        {
            if (UIRoomPicker.Current != null && Choosing == apartment && TakingOut == takeOut)
            {
                UIRoomPicker.Stop();
                return;
            }
            var lot = vm;
            var started = UIRoomPicker.Begin(
                pos =>
                {
                    ApartmentsPack.Changing(lot, takeOut ? "Take a room out of " + apartment.Name : "Add a room to " + apartment.Name,
                        () => Message = takeOut ? Actions.RemoveRoom(pos) : Actions.AddRoom(apartment, pos));
                    if (LotOverlay.On) Neighbours.Show(lot, true, apartment); //drawn again, with the room in its colour
                    Build();
                },
                () =>
                {
                    //(also when the panel itself has gone: the tool is put away with it)
                    Choosing = null;
                    TakingOut = false;
                    if (LotOverlay.On) Neighbours.Show(lot, true);
                    if (Current == this) Build();
                },
                true);
            if (!started)
            {
                After("There is no lot to choose rooms on.");
                return;
            }
            Choosing = apartment;
            TakingOut = takeOut;
            if (LotOverlay.On) Neighbours.Show(lot, true, apartment);
            Build();
        }

        public override void GameResized()
        {
            base.GameResized();
            ApartmentsPack.Guard("laying the Apartments tool out again", Build);
        }

        public override void Kill()
        {
            //another tab, or out of build mode: the room chooser goes with the tab
            if (Current == this) Current = null;
            ApartmentsPack.Guard("putting the room chooser away", UIRoomPicker.Stop);
            base.Kill();
        }

        // ---- the tab itself, in build mode ----

        /// <summary>Build mode is about to be opened: the Apartments tab is among its tabs on a home lot, and not elsewhere.</summary>
        public static void FitTab(List<UICategory> tabs)
        {
            if (tabs == null) return;
            var wanted = Actions.HomeLot(out _, out _) != null;
            var there = tabs.FirstOrDefault(x => x.ID == CategoryId);
            if (wanted && there == null) tabs.Add(new UICategory() { ID = CategoryId, IconName = TabIcon });
            else if (!wanted && there != null) tabs.Remove(there);
        }

        /// <summary>
        /// Every frame on a lot: the apartments are drawn on the lot while build mode is open on an apartment building,
        /// and not otherwise, nor while the floor tool is out.
        /// </summary>
        public static void KeepOverlay()
        {
            var screen = Actions.Screen;
            var vm = screen?.vm;
            var wanted = vm != null && screen.Frontend?.MainPanel?.Mode == UIMainPanelMode.BUILD && Neighbours.FileOf(vm) != null;
            //while the floor tool is out the colours would hide the floor being laid: they are not drawn, but kept
            LotOverlay.Paused = wanted && screen.LotControl?.CustomControl is FSO.Client.UI.Panels.LotControls.UIFloorPainter;
            if (wanted == LotOverlay.On) return;
            if (wanted) Neighbours.Show(vm, true, Current?.Choosing);
            else
            {
                if (vm != null) Neighbours.Show(vm, false);
                if (LotOverlay.On) LotOverlay.Hide(); //(a lot that was left while it was drawn)
            }
        }
    }
}
