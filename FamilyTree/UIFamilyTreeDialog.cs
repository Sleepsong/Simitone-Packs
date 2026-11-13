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
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;

namespace FamilyTree
{
    /// <summary>
    /// A Sim's family tree, as in The Sims 2: portraits joined by lines, the Sim in the middle marked. Choosing a
    /// relative draws the tree around them. The tree can only be looked at here. It is put together in Create a
    /// Family, and after that it changes only as the family does.
    /// </summary>
    public class UIFamilyTreeDialog : UIMobileDialog
    {
        private const int Margin = 60;
        private const int Top = 100;
        private const int ButtonWidth = 275;

        private static UIFamilyTreeDialog Current;
        private static readonly Dictionary<uint, Texture2D> Faces = new Dictionary<uint, Texture2D>();

        private uint Centre;
        private UIContainer Body;

        /// <summary>The Sim being played, if a lot is on screen.</summary>
        public static uint? PlayedSim()
        {
            try
            {
                var screen = GameFacade.Screens.CurrentUIScreen as TS1GameScreen;
                if (screen?.vm == null) return null;
                var guid = screen.SelectedAvatar?.Object?.OBJ?.GUID;
                if (guid != null && People.Exists(guid.Value)) return guid;
                //nobody is selected: anyone of the household being played
                var family = screen.vm.TS1State?.CurrentFamily;
                if (family?.FamilyGUIDs != null && family.FamilyGUIDs.Length > 0) return family.FamilyGUIDs[0];
            }
            catch (Exception e)
            {
                People.Log("Could not tell which Sim is being played: " + e.Message);
            }
            return null;
        }

        /// <summary>Open the tree on a Sim (the one being played, if none is given), or move the open one to them.</summary>
        public static void Open(uint? sim = null)
        {
            sim = sim ?? PlayedSim();
            if (sim == null)
            {
                Say("Family Tree", "Open this while you are playing a household.");
                return;
            }
            if (Current != null)
            {
                Current.Show(sim.Value);
                return;
            }
            Current = new UIFamilyTreeDialog(sim.Value);
            UIScreen.GlobalShowDialog(Current, true);
        }

        public static void Say(string title, string message)
        {
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }

        /// <summary>A Sim's portrait. They are usually not on the lot, so Simitone draws their head by itself, once.</summary>
        public static Texture2D Face(uint sim)
        {
            if (Faces.TryGetValue(sim, out var face)) return face;
            try
            {
                face = UIIconCache.GetNeighbor(sim);
            }
            catch (Exception e)
            {
                People.Log("Could not draw the portrait of " + People.FirstName(sim) + ": " + e.Message);
            }
            Faces[sim] = face;
            return face;
        }

        private UIFamilyTreeDialog(uint sim) : base()
        {
            Centre = sim;
            Refresh();
        }

        private void CloseMe()
        {
            if (Current == this) Current = null;
            Close();
        }

        private void Show(uint sim)
        {
            Centre = sim;
            Refresh();
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

        /// <summary>Draw the window again, around the Sim in the middle.</summary>
        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);
            Caption = "Family Tree of " + People.FullName(Centre);

            float used = 0;
            try
            {
                //a Sim who has left the neighbourhood keeps their place in the file, but is not drawn
                var layout = TreeLayout.For(KinStore.Get(), Centre, sim => sim == Centre || People.Exists(sim));
                var tree = new UITreeView(layout, Face, People.FirstName, Show);
                var scale = tree.Fit(Width - Margin * 2, ScrHeight - Top - 150 - 40);
                tree.Position = new Vector2((Width - layout.Width * scale) / 2, Top);
                Body.Add(tree);
                used = layout.Height * scale;

                if (layout.Nodes.Count == 1)
                {
                    var label = new UILabel();
                    label.CaptionStyle = label.CaptionStyle.Clone();
                    label.CaptionStyle.Size = 15;
                    label.CaptionStyle.Color = UIStyle.Current.DialogText;
                    label.Alignment = TextAlignment.Top | TextAlignment.Center;
                    label.Wrapped = true;
                    label.Position = new Vector2(Margin, Top + used);
                    label.Size = new Vector2(Width - Margin * 2, 1);
                    label.Caption = "No family is known for " + People.FirstName(Centre) + ". Family is chosen when a household is made, in Create a Family.";
                    Body.Add(label);
                    used += 60;
                }
            }
            catch (Exception e)
            {
                People.Log("The Family Tree window: " + e);
            }

            var close = new UIBigButton(true);
            close.Caption = "Close";
            close.Width = ButtonWidth;
            close.Position = new Vector2((Width - ButtonWidth) / 2, Top + used + 20);
            close.OnButtonClick += (b) => CloseMe();
            Body.Add(close);

            SetHeight((int)(Top + used + 20 + 125));
        }
    }
}
