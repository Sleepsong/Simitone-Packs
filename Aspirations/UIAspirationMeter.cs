/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;

namespace Aspirations
{
    /// <summary>Drawing the meter: a bar in bands, one for each level in its colour, lit up to where the Sim is.</summary>
    public static class MeterDrawing
    {
        /// <summary>Where on a bar of this length the meter is, from its start (the bottom, or the left).</summary>
        public static float At(double meter, float length) => (float)((Rules.Clamp(meter) - Rules.MeterMin) / (Rules.MeterMax - Rules.MeterMin) * length);

        public static void Draw(UIElement on, UISpriteBatch batch, Vector2 position, Vector2 size, double meter, Balance b, bool vertical)
        {
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var length = vertical ? size.Y : size.X;
            var thickness = vertical ? size.X : size.Y;
            var lit = At(meter, length);
            var level = Rules.LevelIndex(meter, b);
            for (int i = 0; i < b.Levels.Count; i++)
            {
                var from = At(b.Levels[i].From, length);
                var to = i + 1 < b.Levels.Count ? At(b.Levels[i + 1].From, length) : length;
                var colour = Palette.ForLevel(i, b.Levels.Count);
                Band(on, batch, px, position, from, to, thickness, length, vertical, colour * 0.25f);
                var litTo = Math.Min(to, lit);
                if (litTo > from) Band(on, batch, px, position, from, litTo, thickness, length, vertical, colour * (i == level ? 1f : 0.7f));
                //a thin gap between the levels
                if (i > 0) Band(on, batch, px, position, from, from + 1, thickness, length, vertical, Color.Black * 0.6f);
            }
            //where the Sim is
            var mark = Math.Clamp(lit, 1, length - 1);
            if (vertical) on.DrawLocalTexture(batch, px, null, new Vector2(position.X - 3, position.Y + size.Y - mark - 1), new Vector2(thickness + 6, 2), UIStyle.Current.Text);
            else on.DrawLocalTexture(batch, px, null, new Vector2(position.X + mark - 1, position.Y - 3), new Vector2(2, thickness + 6), UIStyle.Current.Text);
        }

        private static void Band(UIElement on, UISpriteBatch batch, Microsoft.Xna.Framework.Graphics.Texture2D px, Vector2 position, float from, float to, float thickness, float length, bool vertical, Color colour)
        {
            if (to <= from) return;
            if (vertical) on.DrawLocalTexture(batch, px, null, new Vector2(position.X, position.Y + length - to), new Vector2(thickness, to - from), colour);
            else on.DrawLocalTexture(batch, px, null, new Vector2(position.X + from, position.Y), new Vector2(to - from, thickness), colour);
        }
    }

    /// <summary>
    /// The selected Sim's aspiration meter beside their needs, as The Sims 2 has it beside the needs panel: a bar
    /// of the levels, the level's name and the Sim's reward points. Clicking it opens the Aspiration window.
    ///
    /// It goes at the right of the needs panel, past the needs, where Simitone leaves room on most screens. Where
    /// there is none, it is left out and the window (F7) still shows everything.
    /// </summary>
    public class UIAspirationMeter : UIContainer
    {
        public const int Width = 112, Height = 118;
        /// <summary>Where the needs panel's own bars end: four columns 180 apart from 17, each bar 150 wide.</summary>
        private const int NeedsEnd = 715;
        private const int BarWidth = 14;

        private readonly TS1GameScreen Game;
        private readonly UILabel Level, Points, Kind, Hint;
        private string Key = "";
        private double Meter;
        private bool Shown;
        private int Wait;

        public static void AddTo(UISubpanel panel, TS1GameScreen game)
        {
            var x = panel.Size.X - Width - 10;
            if (x < NeedsEnd) return;
            panel.Add(new UIAspirationMeter(game) { Position = new Vector2(x, 5) });
        }

        public UIAspirationMeter(TS1GameScreen game)
        {
            Game = game;
            Level = Labels.Make(this, new Vector2(BarWidth + 10, 0), 15, UIStyle.Current.Text);
            Points = Labels.Make(this, new Vector2(BarWidth + 10, 24), 12, UIStyle.Current.Text);
            Kind = Labels.Make(this, new Vector2(BarWidth + 10, 44), 12, UIStyle.Current.Text);
            Hint = Labels.Make(this, new Vector2(BarWidth + 10, 96), 11, UIStyle.Current.SecondaryText, game?.Desktop == false ? "Tap for more" : "F7: more");
            ListenForMouse(new Rectangle(0, 0, Width, Height), (type, state) =>
            {
                if (type == UIMouseEventType.MouseUp) AspirationsPack.Guard("opening the Aspiration window", () => UIAspirationWindow.Open(Game?.SelectedAvatar));
            });
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (--Wait > 0) return;
            Wait = 15;
            AspirationsPack.Guard("showing the meter", Refresh);
        }

        private void Refresh()
        {
            var b = Watcher.Balance;
            var vm = Game?.vm;
            var selected = Game?.SelectedAvatar;
            var ledger = Watcher.Ledger;
            SimRecord record = null;
            if (vm != null && selected != null && ledger != null && Watcher.Household(vm).Contains(selected)) record = ledger.Find(selected.Object.OBJ.GUID);
            string key;
            if (record == null || record.Aspiration == null) key = "none";
            else key = (int)Math.Round(record.Meter) + "|" + record.Points + "|" + record.Wants.Count + "|" + record.Fears.Count + "|" + record.Aspiration + "|" + ledger.Settings.On;
            if (key == Key) return;
            Key = key;
            Shown = record != null && record.Aspiration != null;
            
            if (!Shown)
            {
                Labels.Fit(Level, selected == null ? "" : "No aspiration", 13, Width - BarWidth - 10);
                Labels.Fit(Points, selected != null && selected.IsPet ? "Pets have none." : "", 12, Width - BarWidth - 10);
                Kind.Caption = "";
            }
            else
            {
                Meter = record.Meter;
                var level = Rules.LevelIndex(record.Meter, b);
                Level.CaptionStyle.Color = Palette.ForLevel(level, b.Levels.Count);
                Labels.Fit(Level, b.Levels[level].Name, 15, Width - BarWidth - 10);
                Labels.Fit(Points, Rules.Number(record.Points) + " points", 12, Width - BarWidth - 10);
                var aspiration = b.Aspiration(record.Aspiration)?.Name ?? record.Aspiration;
                Labels.Fit(Kind, ledger.Settings.On ? aspiration : "Switched off", 12, Width - BarWidth - 10);
            }
            Invalidate(); //the panel keeps a picture of itself, and only draws again when told
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            if (Shown) MeterDrawing.Draw(this, batch, new Vector2(3, 4), new Vector2(BarWidth, Height - 8), Meter, Watcher.Balance, true);
            base.Draw(batch);
        }
    }
}
