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
using FSO.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Collections.Generic;

namespace Aspirations
{
    /// <summary>The colours of the meter's levels, low to high: red, orange, Simitone's green, gold, and platinum.</summary>
    public static class Palette
    {
        public static readonly Color Failure = new Color(220, 50, 40);
        public static readonly Color Low = new Color(255, 128, 0);
        public static readonly Color Steady = new Color(0, 220, 110);
        public static readonly Color Gold = new Color(255, 200, 40);
        public static readonly Color Platinum = new Color(225, 235, 255);

        public static Color ForLevel(int level, int levels)
        {
            if (levels <= 1) return Steady;
            //the lowest and the top two are always failure, gold and platinum; the rest are between
            if (level <= 0) return Failure;
            if (level == levels - 1) return Platinum;
            if (level == levels - 2) return Gold;
            return level == 1 && levels > 4 ? Low : Steady;
        }

        public static Color Want => UIStyle.Current.PosMoney;
        public static Color Fear => UIStyle.Current.NegMoney;
    }

    /// <summary>A plain button of the pack's own: a box with words in it, small enough for a list. The pack ships no pictures.</summary>
    public class UITextButton : UIElement
    {
        public string Caption;
        public bool Enabled = true;
        public bool Selected;
        public Vector2 Box;
        public event Action<UITextButton> OnClick;
        private TextStyle Style;
        private bool Hover, Down;
        private readonly UIMouseEventRef MouseRef;

        public UITextButton(string caption, float width, float height = 30, int textSize = 14)
        {
            Caption = caption;
            Box = new Vector2(width, height);
            Style = TextStyle.DefaultLabel.Clone();
            Style.Size = textSize;
            MouseRef = ListenForMouse(new Rectangle(0, 0, (int)width, (int)height), Mouse);
        }

        public void Resize(float width, float height)
        {
            Box = new Vector2(width, height);
            MouseRef.Region = new Rectangle(0, 0, (int)width, (int)height);
        }

        private void Mouse(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseOver: Hover = true; break;
                case UIMouseEventType.MouseOut: Hover = false; Down = false; break;
                case UIMouseEventType.MouseDown: Down = true; break;
                case UIMouseEventType.MouseUp:
                    if (Down && Enabled && Visible) AspirationsPack.Guard("pressing " + Caption, () => OnClick?.Invoke(this));
                    Down = false;
                    break;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var style = UIStyle.Current;
            var fill = !Enabled ? style.BtnDisable * 0.25f : Selected ? style.BtnActive * 0.45f : Down ? style.BtnActive * 0.35f : Hover ? Color.White * 0.22f : Color.White * 0.12f;
            var edge = !Enabled ? style.BtnDisable * 0.5f : Selected || Hover ? style.BtnActive : Color.White * 0.5f;
            DrawLocalTexture(batch, px, null, Vector2.Zero, Box, fill);
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(Box.X, 1), edge);
            DrawLocalTexture(batch, px, null, new Vector2(0, Box.Y - 1), new Vector2(Box.X, 1), edge);
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(1, Box.Y), edge);
            DrawLocalTexture(batch, px, null, new Vector2(Box.X - 1, 0), new Vector2(1, Box.Y), edge);
            Style.Color = Enabled ? style.Text : style.BtnDisable;
            var text = Style.MeasureString(Caption).X > Box.X - 8 ? Style.TruncateToWidth(Caption, (int)Box.X - 8) : Caption;
            var measure = Style.MeasureString(text);
            DrawLocalString(batch, text, new Vector2((Box.X - measure.X) / 2, (Box.Y - measure.Y) / 2), Style);
        }
    }

    /// <summary>A label made the way Simitone's own panels make them, cut to a width so it never runs into what is beside it.</summary>
    public static class Labels
    {
        public static UILabel Make(UIContainer parent, Vector2 position, int size, Color colour, string text = "")
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Color = colour;
            label.CaptionStyle.Size = size;
            label.Position = position;
            label.Caption = text ?? "";
            parent.Add(label);
            return label;
        }

        /// <summary>Show text within a width: smaller if it must be, down to a size still easy to read, and cut short only past that.</summary>
        public static void Fit(UILabel label, string text, int size, float width)
        {
            text ??= "";
            var style = label.CaptionStyle;
            var smallest = Math.Max(10, size - 4);
            style.Size = size;
            while (style.Size > smallest && style.MeasureString(text).X > width) style.Size--;
            label.Caption = style.MeasureString(text).X > width ? style.TruncateToWidth(text, (int)width) : text;
        }

        /// <summary>Text broken into lines that fit a width, for labels that are not wrapped by themselves.</summary>
        public static List<string> Wrap(TextStyle style, string text, float width, int maxLines)
        {
            var lines = new List<string>();
            var line = "";
            foreach (var word in (text ?? "").Split(' '))
            {
                var tried = line == "" ? word : line + " " + word;
                if (style.MeasureString(tried).X <= width || line == "") line = tried;
                else
                {
                    lines.Add(line);
                    line = word;
                }
            }
            if (line != "") lines.Add(line);
            if (lines.Count > maxLines)
            {
                lines = lines.GetRange(0, maxLines);
                lines[maxLines - 1] = style.TruncateToWidth(lines[maxLines - 1] + " ...", (int)width);
            }
            return lines;
        }
    }

    /// <summary>The pack's button in the Options panel, made as Simitone makes its Packs button, after it.</summary>
    public static class UIOptionsButton
    {
        public static void Add(UISubpanel parent, int index, string caption, Action click)
        {
            float buttonX = 50 + index * 120f;
            float labelX = buttonX - 27;

            var label = new UILabel();
            label.Caption = caption;
            label.Alignment = TextAlignment.Middle | TextAlignment.Center;
            label.Wrapped = true;
            label.Position = new Vector2(labelX - 77, 106);
            label.Size = new Vector2(120, 1);
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = 12;
            label.CaptionStyle.Color = UIStyle.Current.Text;

            //the pack has no artwork of its own: the Packs button's picture, under its own name
            var button = new UICatButton(Content.Get().CustomUI.Get("opt_packs.png").Get(GameFacade.GraphicsDevice));
            button.Position = new Vector2(buttonX - 50, 16);
            button.OnButtonClick += (btn) => AspirationsPack.Guard("opening " + caption, click);

            GameFacade.Screens.Tween.To(label, 0.5f, new Dictionary<string, float>() { { "X", labelX } }, TweenQuad.EaseOut);
            GameFacade.Screens.Tween.To(button, 0.5f, new Dictionary<string, float>() { { "X", buttonX } }, TweenQuad.EaseOut);

            parent.Add(label);
            parent.Add(button);
        }
    }

    /// <summary>
    /// News that need not stop play, as a line at the top of the screen that fades: a want fulfilled, a fear come
    /// true, a new level. The Sims 2 shows these as an icon rising from the Sim; the pack has no pictures, so words.
    /// One overlay for each game screen, made the first time it is needed.
    /// </summary>
    public class UINotices : UIElement
    {
        private const int MostShown = 4;
        private const float Seconds = 5f, FadeSeconds = 0.6f;
        private const int LineHeight = 30, Width = 640;

        private class Line
        {
            public string Text;
            public Color Colour;
            public float Age;
        }

        private readonly List<Line> Lines = new List<Line>();
        private readonly TextStyle Style;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIScreen, UINotices> Overlays = new System.Runtime.CompilerServices.ConditionalWeakTable<UIScreen, UINotices>();

        private UINotices()
        {
            Style = TextStyle.DefaultLabel.Clone();
            Style.Size = 15;
        }

        public static void Show(UIScreen screen, string text, NoticeKind kind)
        {
            if (screen == null) return;
            var overlay = Overlays.GetValue(screen, s =>
            {
                var made = new UINotices();
                s.Add(made);
                return made;
            });
            //drawn above whatever the screen has added since
            if (overlay.Parent == screen) { screen.Remove(overlay); screen.Add(overlay); }
            var colour = kind == NoticeKind.Want ? Palette.Want : kind == NoticeKind.Fear ? Palette.Fear : kind == NoticeKind.Level ? Palette.Gold : kind == NoticeKind.Reward ? UIStyle.Current.SkillActive : UIStyle.Current.Text;
            overlay.Lines.Add(new Line() { Text = text, Colour = colour });
            while (overlay.Lines.Count > MostShown) overlay.Lines.RemoveAt(0);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            var dt = (float)Math.Clamp(state?.Time?.ElapsedGameTime.TotalSeconds ?? 1 / 60.0, 0, 0.25);
            foreach (var line in Lines) line.Age += dt;
            Lines.RemoveAll(x => x.Age > Seconds);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Lines.Count == 0) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var screenWidth = GameFacade.Screens.CurrentUIScreen.ScreenWidth;
            var x = (screenWidth - Width) / 2f;
            var y = 18f;
            foreach (var line in Lines)
            {
                var alpha = Math.Clamp(Math.Min(line.Age / 0.2f, (Seconds - line.Age) / FadeSeconds), 0, 1);
                var text = Style.MeasureString(line.Text).X > Width - 24 ? Style.TruncateToWidth(line.Text, Width - 24) : line.Text;
                var measure = Style.MeasureString(text);
                var boxWidth = measure.X + 24;
                var boxX = x + (Width - boxWidth) / 2;
                DrawLocalTexture(batch, px, null, new Vector2(boxX, y), new Vector2(boxWidth, LineHeight - 4), Color.Black * (0.7f * alpha));
                DrawLocalTexture(batch, px, null, new Vector2(boxX, y), new Vector2(3, LineHeight - 4), line.Colour * alpha);
                Style.Color = line.Colour * alpha;
                DrawLocalString(batch, text, new Vector2(boxX + 12, y + (LineHeight - 4 - measure.Y) / 2), Style);
                y += LineHeight;
            }
        }
    }
}
