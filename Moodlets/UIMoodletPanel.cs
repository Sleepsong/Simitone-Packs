/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.Utils;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;

namespace Moodlets
{
    /// <summary>
    /// The selected Sim's moodlets, in the Needs panel to the right of the bars: a title with what they add to mood,
    /// and a chip for each, green for a boost and orange for a penalty, with a line along its foot for the time it
    /// has left. Point at one for what it is about; on a touch screen, tap it.
    ///
    /// What it shows is worked out in MoodletView.cs. This only draws it, in the colours of Simitone's UI style, so the
    /// pack ships no pictures.
    /// </summary>
    public class UIMoodletPanel : UIContainer
    {
        private readonly TS1GameScreen Game;
        private readonly UISubpanel Needs;
        private readonly UILabel Title, Total, Empty;
        private readonly List<UIMoodletChip> Chips = new List<UIMoodletChip>();
        private MoodletView View = new MoodletView();
        private int Wait;

        public UIMoodletPanel(TS1GameScreen game, UISubpanel needs)
        {
            Game = game;
            Needs = needs;
            Position = new Vector2(MoodletView.Left, 0);

            //the title as the Needs panel makes the names of its needs: size 15, standing on the line above the bars
            Title = Label(15, UIStyle.Current.Text);
            Title.Alignment = TextAlignment.Bottom;
            Title.Size = new Vector2(1);
            Title.Position = new Vector2(0, MoodletView.TitleBottom);
            Total = Label(15, UIStyle.Current.PosMoney);
            Total.Alignment = TextAlignment.Bottom;
            Total.Size = new Vector2(1);
            Empty = Label(13, UIStyle.Current.SecondaryText);
            Empty.Position = new Vector2(0, MoodletView.Top + 3);
            Refresh();
        }

        private UILabel Label(int size, Color colour)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Color = colour;
            label.CaptionStyle.Size = size;
            label.Caption = "";
            Add(label);
            return label;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //a few times a second is plenty: the time left changes once a game minute
            if (--Wait > 0) return;
            Wait = 15;
            MoodletsPack.Guard("showing a Sim's moodlets", Refresh);
        }

        private void Refresh()
        {
            var view = Look();
            if (view.Key == View.Key) return;
            var sameShape = view.Shape == View.Shape && Chips.Count == view.Chips.Count;
            View = view;
            if (sameShape)
            {
                //only the times have moved: keep the chips, so a tooltip being read stays
                for (int i = 0; i < Chips.Count; i++) Chips[i].Show(view.Chips[i]);
                Invalidate();
                return;
            }

            Title.Caption = view.Title;
            Total.Caption = view.Total;
            Total.CaptionStyle.Color = view.TotalGood ? UIStyle.Current.PosMoney : UIStyle.Current.NegMoney;
            Total.Position = new Vector2(view.Title == "" ? 0 : Title.CaptionStyle.MeasureString(view.Title).X + 10, MoodletView.TitleBottom);
            Empty.Caption = view.Empty;

            foreach (var chip in Chips) Remove(chip);
            Chips.Clear();
            foreach (var chip in view.Chips)
            {
                var ui = new UIMoodletChip(chip, View.Fit);
                Add(ui);
                Chips.Add(ui);
            }
            Invalidate(); //the Needs panel keeps a picture of itself, and only draws again when told
        }

        private MoodletView Look()
        {
            var width = MoodletView.RoomIn(Needs?.Size.X ?? 0);
            var vm = Game?.vm;
            var ledger = Moods.Ledger;
            var selected = Game?.SelectedAvatar;
            if (vm == null || ledger == null || selected == null) return MoodletView.Nothing(width);
            if (!Moods.Household(vm).Contains(selected)) return MoodletView.NotOurs(width);
            return MoodletView.For(Moods.Of(selected), Moods.Tuning, ledger.Settings.On, width);
        }
    }

    /// <summary>One moodlet in the panel.</summary>
    public class UIMoodletChip : UIContainer
    {
        private MoodletView.Chip Chip;
        private readonly MoodletView.Layout Fit;
        private readonly UILabel Mood, Name, Time;
        private const int TimeWidth = 30, BarHeight = 2;

        public UIMoodletChip(MoodletView.Chip chip, MoodletView.Layout fit)
        {
            Chip = chip;
            Fit = fit;
            Position = new Vector2(chip.X, chip.Y);
            Tooltip = chip.Details;
            UIUtils.GiveTooltip(this);
            //on a touch screen there is no pointing: a tap says the same
            ListenForMouse(new Rectangle(0, 0, chip.Width, chip.Height), (type, state) =>
            {
                if (type == UIMouseEventType.MouseUp) MoodletsPack.Say(Chip.More ? "More moodlets" : Chip.Name, Chip.Details);
            });

            var pill = Fit == MoodletView.Layout.Wide ? MoodletView.PillWidth : chip.Width;
            Mood = Label(13, Color.White, new Vector2(0, 0), new Vector2(pill, chip.Height - BarHeight), TextAlignment.Middle | TextAlignment.Center);
            Mood.Caption = chip.Mood;
            if (Fit != MoodletView.Layout.Wide) return;

            var nameX = pill + 6;
            var nameWidth = Math.Max(0, chip.Width - nameX - TimeWidth - 4);
            Name = Label(13, UIStyle.Current.Text, new Vector2(nameX, 0), new Vector2(nameWidth, chip.Height - BarHeight), TextAlignment.Middle | TextAlignment.Left);
            Name.Caption = Name.CaptionStyle.TruncateToWidth(chip.Name, nameWidth);
            Time = Label(12, UIStyle.Current.SecondaryText, new Vector2(chip.Width - TimeWidth - 2, 0), new Vector2(TimeWidth, chip.Height - BarHeight), TextAlignment.Middle | TextAlignment.Right);
            Time.Caption = chip.Time;
        }

        /// <summary>The same moodlet, with less time left.</summary>
        public void Show(MoodletView.Chip chip)
        {
            Chip = chip;
            Tooltip = chip.Details; //the tooltip reads it afresh while it is shown
            if (Time != null) Time.Caption = chip.Time;
        }

        private UILabel Label(int size, Color colour, Vector2 position, Vector2 box, TextAlignment alignment)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Color = colour;
            label.CaptionStyle.Size = size;
            label.Position = position;
            label.Size = box;
            label.Alignment = alignment;
            label.Caption = "";
            Add(label);
            return label;
        }

        public override Rectangle GetBounds() => new Rectangle(0, 0, Chip.Width, Chip.Height);

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var style = UIStyle.Current;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var pill = Fit == MoodletView.Layout.Wide ? MoodletView.PillWidth : Chip.Width;
            var body = Chip.Height - BarHeight;

            if (Fit == MoodletView.Layout.Wide) DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(Chip.Width, body), Color.White * 0.08f);
            var colour = Chip.More ? style.SkillInactive : Chip.Good ? style.PosMoney : style.NegMoney;
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(pill, body), colour * 0.6f);

            // the time left, along the foot
            if (!Chip.More)
            {
                DrawLocalTexture(batch, px, null, new Vector2(0, body), new Vector2(Chip.Width, BarHeight), style.SkillInactive * 0.5f);
                var left = (int)Math.Round(Chip.Width * Chip.Left);
                if (left > 0) DrawLocalTexture(batch, px, null, new Vector2(0, body), new Vector2(left, BarHeight), style.SkillActive);
            }
            base.Draw(batch);
        }
    }
}
