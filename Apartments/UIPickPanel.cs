/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// What is on screen while the apartments are drawn on the lot: what to do, how the last click went, and which
    /// colour is which apartment. It sits in a corner and takes no clicks.
    /// </summary>
    public class UIPickPanel : UIContainer
    {
        private const int Pad = 12;
        private const int PanelWidth = 320;
        private const int TextHeight = 96;
        private const int KeyRow = 22;
        private static UIPickPanel Current;

        private VM vm;
        private Apartment PickedOut;
        private Texture2D White;
        private UILabel Label;
        private string Instructions;
        private List<Tuple<Color, UILabel>> Key = new List<Tuple<Color, UILabel>>();
        private int PanelHeight;

        /// <summary>Say how the last click went, under the instructions, and bring the key up to date.</summary>
        public static void Say(string result)
        {
            var panel = Current;
            if (panel == null) return;
            ApartmentsPack.Guard("saying how a click went", () =>
            {
                panel.Label.Caption = panel.Instructions + "\n" + (result ?? "");
                panel.MakeKey();
            });
        }

        /// <summary>The panel while rooms are chosen for an apartment (or taken out of theirs).</summary>
        public static void Open(VM vm, Apartment apartment, bool remove)
        {
            Close();
            ApartmentsPack.Guard("showing what to do while choosing a room", () =>
            {
                Current = new UIPickPanel(vm, apartment, (remove ? "Click in rooms to take them out of their apartment." : ("Click in rooms to add them to " + apartment.Name + "."))
                    + " The room under the cursor is shown in white. Press Esc when you have finished.");
                UIScreen.GlobalShowDialog(Current, false);
            });
        }

        /// <summary>The panel while every apartment is drawn on the lot.</summary>
        public static void OpenShowing(VM vm)
        {
            Close();
            ApartmentsPack.Guard("showing which colour is which apartment", () =>
            {
                Current = new UIPickPanel(vm, null, "Each apartment's floor is drawn in its colour; rooms with none are shared. Open the Apartments window again (F9) to put this away.");
                UIScreen.GlobalShowDialog(Current, false);
            });
        }

        public static void Close()
        {
            var panel = Current;
            Current = null;
            if (panel != null) ApartmentsPack.Guard("putting the panel away", () => UIScreen.RemoveDialog(panel));
        }

        private UIPickPanel(VM vm, Apartment apartment, string instructions)
        {
            this.vm = vm;
            PickedOut = apartment;
            White = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            Label = MakeLabel(12, Color.White, new Vector2(Pad, Pad), PanelWidth - Pad * 2, 6);
            Instructions = instructions;
            Label.Caption = Instructions;
            MakeKey();
            var screen = GameFacade.Screens.CurrentUIScreen;
            Position = new Vector2(Math.Max(0, screen.ScreenWidth - PanelWidth - 20), 20);
        }

        private UILabel MakeLabel(int size, Color colour, Vector2 pos, int width, int lines)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = colour;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = lines;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            Add(label);
            return label;
        }

        /// <summary>A row for each apartment: its colour, its name, and who lives there.</summary>
        private void MakeKey()
        {
            foreach (var row in Key) Remove(row.Item2);
            Key.Clear();
            var state = Neighbours.StateOf(vm);
            var file = state?.File;
            var y = Pad + TextHeight;
            if (file != null)
            {
                foreach (var apartment in file.Apartments.OrderBy(x => x.Id))
                {
                    var colour = LotOverlay.ColourOf(file, apartment);
                    if (PickedOut != null && apartment != PickedOut) colour = Color.Lerp(colour, new Color(24, 28, 40), 0.55f);
                    var label = MakeLabel(11, Color.White, new Vector2(Pad + 24, y + 2), PanelWidth - Pad * 2 - 24, 1);
                    label.Caption = apartment.Name + ": " + Actions.TenantText(state.House, apartment) + (apartment == PickedOut ? " (choosing)" : "");
                    Key.Add(Tuple.Create(colour, label));
                    y += KeyRow;
                }
            }
            PanelHeight = y + Pad - (Key.Count == 0 ? KeyRow : 0);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawLocalTexture(batch, White, null, Vector2.Zero, new Vector2(PanelWidth, PanelHeight), new Color(24, 28, 40) * 0.92f);
            for (int i = 0; i < Key.Count; i++)
            {
                DrawLocalTexture(batch, White, null, new Vector2(Pad, Pad + TextHeight + i * KeyRow + 2), new Vector2(16, 16), Key[i].Item1);
            }
            base.Draw(batch);
        }
    }
}
