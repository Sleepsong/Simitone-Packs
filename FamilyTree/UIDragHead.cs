/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Model;
using System;

namespace FamilyTree
{
    /// <summary>
    /// A Sim's portrait that can be picked up and dropped on another, as in The Sims 2's Create a Family. It is drawn
    /// as Simitone's own portrait button is (the same pictures), but it reports the press and the release to its
    /// window instead of being a button, so the window can follow the drag in between.
    /// </summary>
    public class UIDragHead : UIElement
    {
        /// <summary>The portrait's size. Its position is its middle.</summary>
        public const float Side = 80;

        public readonly uint Sim;
        public Texture2D Icon;
        public string Name;
        /// <summary>The Sim whose family is shown: outlined in the selection colour.</summary>
        public bool Outlined;
        /// <summary>A dragged Sim is over this one, and would be dropped on it: outlined in green.</summary>
        public bool Target;
        /// <summary>This Sim is being dragged: what stays behind is faded.</summary>
        public bool Lifted;
        /// <summary>Pressed and released over the portrait. (This head, how, the update it happened in.)</summary>
        public Action<UIDragHead, UIMouseEventType, UpdateState> OnMouse;

        private readonly bool IsGhost;
        private readonly Texture2D Back;
        private readonly Texture2D Ring;
        private readonly TextStyle Style;

        /// <param name="ghost">The copy that follows the pointer during a drag: drawn lighter, with no name, and not pressed.</param>
        public UIDragHead(uint sim, Texture2D icon, string name, bool ghost = false)
        {
            Sim = sim;
            Icon = icon;
            Name = name;
            IsGhost = ghost;
            var gd = GameFacade.GraphicsDevice;
            Back = Content.Get().CustomUI.Get("pswitch_icon_bg.png").Get(gd);
            Ring = Content.Get().CustomUI.Get("pswitch_icon_sel.png").Get(gd);
            Style = TextStyle.DefaultLabel.Clone();
            Style.Color = Color.White;
            if (!ghost) ListenForMouse(new Rectangle((int)(-Side / 2), (int)(-Side / 2), (int)Side, (int)Side), (type, state) => OnMouse?.Invoke(this, type, state));
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var fade = IsGhost ? 0.8f : (Lifted ? 0.35f : 1f);
            DrawLocalTexture(batch, Back, null, new Vector2(Back.Width, Back.Height) / -2, Vector2.One, new Color(104, 164, 184, 255) * fade);
            if (Icon != null) DrawLocalTexture(batch, Icon, null, new Vector2(Icon.Width, Icon.Height) / -2, Vector2.One, Color.White * fade);
            if (Target) DrawLocalTexture(batch, Ring, null, new Vector2(Ring.Width, Ring.Height) / -2, Vector2.One, UIStyle.Current.SecondaryText);
            else if (Outlined) DrawLocalTexture(batch, Ring, null, new Vector2(Ring.Width, Ring.Height) / -2, Vector2.One, UIStyle.Current.ActiveSelection);
            if (!IsGhost && !string.IsNullOrEmpty(Name))
            {
                //(as Simitone's portrait button sizes its names)
                Style.Size = 10;
                var size = Style.MeasureString(Name);
                Style.Size = (int)Math.Min(15, Math.Max(8, (100 / Math.Max(1f, size.X)) * 10));
                var shown = Style.TruncateToWidth(Name, 100);
                DrawLocalString(batch, shown, new Vector2(0, Back.Height / 2 + 16), Style, new Rectangle(0, 0, 1, 1), TextAlignment.Center | TextAlignment.Middle);
            }
        }
    }

    /// <summary>A plain coloured rectangle. With a mouse handler it also stops presses reaching what is under it.</summary>
    public class UIBox : UIElement
    {
        public Vector2 Area;
        public Color Colour;
        private Texture2D Pixel;

        public UIBox(Vector2 area, Color colour, Action<UIMouseEventType> onMouse = null)
        {
            Area = area;
            Colour = colour;
            if (onMouse != null) ListenForMouse(new Rectangle(0, 0, (int)area.X, (int)area.Y), (type, state) => onMouse(type));
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            if (Pixel == null) Pixel = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            DrawLocalTexture(batch, Pixel, null, Vector2.Zero, Area, Colour);
        }
    }
}
