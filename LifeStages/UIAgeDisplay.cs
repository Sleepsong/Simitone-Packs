/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;

namespace LifeStages
{
    /// <summary>
    /// The selected Sim's age, in the live panel: their stage, a bar of life stages after The Sims 2's with a figure
    /// standing on each, how far along it they are, and how many days until the next. It goes in the Personality
    /// panel, whose left third Simitone leaves empty.
    ///
    /// What it shows is worked out in AgeView.cs. This only draws it, in the colours of Simitone's UI style and with
    /// figures drawn in code, so the pack ships no pictures.
    /// </summary>
    public class UIAgeDisplay : UIContainer
    {
        public const int BarWidth = 300;
        private const int FiguresBottom = 59, BarY = 63, BarHeight = 8;

        private readonly TS1GameScreen Game;
        private readonly UILabel Title, Status, Note;
        private AgeView View = new AgeView();
        private int Wait;

        public UIAgeDisplay(TS1GameScreen game)
        {
            Game = game;
            Title = Label(new Vector2(0, 0), 19, UIStyle.Current.Text);
            Status = Label(new Vector2(0, 76), 15, UIStyle.Current.Text);
            Note = Label(new Vector2(0, 98), 12, UIStyle.Current.SecondaryText);
            Refresh();
        }

        private UILabel Label(Vector2 position, int size, Color colour)
        {
            //as the Personality panel makes its own labels
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Color = colour;
            label.CaptionStyle.Size = size;
            label.Position = position;
            label.Caption = "";
            Add(label);
            return label;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //a few times a second is plenty: an age changes once a day
            if (--Wait > 0) return;
            Wait = 15;
            LifeStagesPack.Guard("showing a Sim's age", Refresh);
        }

        private void Refresh()
        {
            var view = Look();
            if (view.Key == View.Key) return;
            View = view;
            Title.Caption = view.Title;
            Status.Caption = view.Status;
            Note.Caption = view.Counter == "" ? view.Note : view.Note == "" ? view.Counter : view.Counter + ". " + view.Note;
            Invalidate(); //the panel keeps a picture of itself, and only draws again when told
        }

        private AgeView Look()
        {
            var vm = Game?.vm;
            var ledger = Life.Ledger;
            if (vm == null || ledger == null) return new AgeView();
            var selected = Game.SelectedAvatar;
            if (selected == null) return new AgeView();
            if (!Life.Household(vm).Contains(selected)) return AgeView.Unaged();
            return AgeView.For(Life.RecordOf(selected), ledger.Settings, Life.AtHome(vm), BarWidth);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var style = UIStyle.Current;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            foreach (var segment in View.Segments)
            {
                // the stage's stretch of the bar, and how much of it is lived
                DrawLocalTexture(batch, px, null, new Vector2(segment.X, BarY), new Vector2(segment.Width, BarHeight), style.SkillInactive * 0.55f);
                var lived = (int)Math.Round(segment.Width * segment.Fill);
                if (lived > 0) DrawLocalTexture(batch, px, null, new Vector2(segment.X, BarY), new Vector2(lived, BarHeight), style.SkillActive);
                if (segment.Current)
                {
                    //where the Sim is now
                    var at = Math.Clamp(segment.X + lived, segment.X + 1, segment.X + segment.Width - 1);
                    DrawLocalTexture(batch, px, null, new Vector2(at - 1, BarY - 3), new Vector2(2, BarHeight + 6), style.Text);
                }

                // the figure standing on it: the Sim's own stage bright, those lived through lit, those to come dim
                var figure = Figure(segment.Stage);
                if (figure == null) continue;
                var colour = segment.Current ? style.Text : segment.Fill >= 1 ? style.SkillActive : style.SkillInactive;
                DrawLocalTexture(batch, figure, null, new Vector2(segment.X + (segment.Width - figure.Width) / 2, FiguresBottom - figure.Height), Vector2.One, colour);
            }
            base.Draw(batch);
        }

        // ---- the figures, as textures ----

        private static readonly Dictionary<Stage, Texture2D> Made = new Dictionary<Stage, Texture2D>();

        private static Texture2D Figure(Stage stage)
        {
            if (Made.TryGetValue(stage, out var made) && !made.IsDisposed) return made;
            try
            {
                var mask = Figures.Mask(stage);
                var data = new Color[mask.Length];
                for (int i = 0; i < mask.Length; i++) data[i] = Color.White * (mask[i] / 255f); //premultiplied, as Simitone's own UI art is
                made = new Texture2D(GameFacade.GraphicsDevice, Figures.Width, Figures.Height(stage));
                made.SetData(data);
                Made[stage] = made;
                return made;
            }
            catch (Exception e)
            {
                LifeStagesPack.Guard("drawing a figure for the age bar", () => throw e);
                return null;
            }
        }
    }
}
