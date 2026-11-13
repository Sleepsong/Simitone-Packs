/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Panels;
using System;
using System.Collections.Generic;

namespace FamilyTree
{
    /// <summary>
    /// The picture of a family tree, as The Sims 2 draws it: each Sim's portrait, a generation to a row, joined by
    /// lines from parents down to their children. Choosing a portrait is reported, so the tree can be drawn again
    /// around that Sim. Where everything goes was worked out by TreeLayout: this only puts it on the screen, with
    /// Simitone's own portrait button.
    /// </summary>
    public class UITreeView : UIContainer
    {
        public readonly float TreeWidth;
        public readonly float TreeHeight;

        public UITreeView(TreeLayout layout, Func<uint, Texture2D> picture, Func<uint, string> name, Action<uint> chosen)
        {
            TreeWidth = layout.Width;
            TreeHeight = layout.Height;
            Add(new UITreeLines(layout.Lines));
            foreach (var node in layout.Nodes)
            {
                Texture2D face = null;
                try
                {
                    face = picture?.Invoke(node.Sim);
                }
                catch (Exception e)
                {
                    People.Log("Could not draw a Sim's portrait: " + e.Message);
                }
                var button = new UIAvatarSelectButton(face);
                button.Opacity = 1f;
                button.Name = name(node.Sim) + (node.More ? " ..." : "");
                button.Outlined = node.Centre;
                button.Position = new Vector2(node.X, node.Y);
                var sim = node.Sim;
                button.OnButtonClick += (b) =>
                {
                    try
                    {
                        chosen?.Invoke(sim);
                    }
                    catch (Exception e)
                    {
                        People.Log("The family tree: " + e);
                    }
                };
                Add(button);
            }
        }

        /// <summary>Make the picture small enough for this much room. It is never made bigger than it is.</summary>
        public float Fit(float width, float height)
        {
            var scale = Math.Min(1f, Math.Min(width / Math.Max(1f, TreeWidth), height / Math.Max(1f, TreeHeight)));
            scale = Math.Max(0.25f, scale);
            ScaleX = scale;
            ScaleY = scale;
            return scale;
        }
    }

    /// <summary>The lines of a family tree: each is straight across or straight down, drawn as a thin stretched square.</summary>
    public class UITreeLines : UIElement
    {
        private const float Thickness = 3;
        private readonly List<TreeLine> Lines;
        private Texture2D Pixel;

        public UITreeLines(List<TreeLine> lines)
        {
            Lines = lines;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            if (Pixel == null) Pixel = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var colour = new Color(104, 164, 184, 255); //the blue behind the portraits
            foreach (var line in Lines)
            {
                var left = Math.Min(line.X1, line.X2);
                var top = Math.Min(line.Y1, line.Y2);
                var across = Math.Abs(line.X2 - line.X1);
                var down = Math.Abs(line.Y2 - line.Y1);
                //the ends of two lines that meet at a corner cover it between them
                DrawLocalTexture(batch, Pixel, null, new Vector2(left - Thickness / 2, top - Thickness / 2), new Vector2(across + Thickness, down + Thickness), colour);
            }
        }
    }
}
