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
using System.Linq;

namespace Memories
{
    /// <summary>
    /// A Sim's memories, newest first, a page at a time: each with a mark, green for a happy memory and red for a sad
    /// one as The Sims 2 colours them, what it was, and when (the neighbourhood's day, the time, and the Sim's stage
    /// then). Only looked at: memories are made by what happens in play.
    /// </summary>
    public class UIMemoriesDialog : UIMobileDialog
    {
        private const int Margin = 60;
        private const int Top = 100;
        private const int RowHeight = 54;
        private const int ButtonWidth = 220;
        private const int ButtonGap = 20;

        private static UIMemoriesDialog Current;

        private uint Sim;
        private int Page;
        private UIContainer Body;

        /// <summary>The Sims (not pets) of the household being played, if a lot is on screen.</summary>
        public static List<uint> HouseholdNow()
        {
            var screen = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
            var family = screen?.vm?.TS1State?.CurrentFamily?.FamilyGUIDs ?? new uint[0];
            return family.Where(g => People.ByGuid(g) != null && IsPersonName(g)).ToList();
        }

        private static bool IsPersonName(uint guid)
        {
            //pets are characters too; the game's body string says which, and Watching knows how to read it
            var kind = FSO.Content.Content.Get()?.WorldObjects?.Get(guid)?.Resource?.Get<FSO.Files.Formats.IFF.Chunks.STR>(200)?.GetString(0)?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(kind) || kind == "adult" || kind == "child";
        }

        /// <summary>The Sim being played: the selected one, or else the first of the household.</summary>
        public static uint? PlayedSim()
        {
            try
            {
                var screen = GameFacade.Screens.CurrentUIScreen as TS1GameScreen;
                if (screen?.vm == null) return null;
                var guid = screen.SelectedAvatar?.Object?.OBJ?.GUID;
                if (guid != null && People.ByGuid(guid.Value) != null) return guid;
                var household = HouseholdNow();
                if (household.Count > 0) return household[0];
            }
            catch (Exception e)
            {
                Log.Write("Could not tell which Sim is being played: " + e.Message);
            }
            return null;
        }

        /// <summary>Open the window on a Sim (the one being played, if none is given), or move the open one to them.</summary>
        public static void Open(uint? sim = null)
        {
            sim ??= PlayedSim();
            if (sim == null)
            {
                Say("Memories", "Open this while you are playing a household.");
                return;
            }
            if (Current != null)
            {
                Current.Show(sim.Value);
                return;
            }
            Current = new UIMemoriesDialog(sim.Value);
            UIScreen.GlobalShowDialog(Current, true);
        }

        public static void Say(string title, string message)
        {
            Log.Write(title + ": " + message.Replace("\n", " | "));
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

        private UIMemoriesDialog(uint sim) : base()
        {
            Sim = sim;
            Refresh();
        }

        private void CloseMe()
        {
            if (Current == this) Current = null;
            Close();
        }

        private void Show(uint sim)
        {
            Sim = sim;
            Page = 0;
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

        /// <summary>How many memories fit on a page of a screen this tall.</summary>
        public static int PerPage(int screenHeight) => Math.Max(3, Math.Min(10, (screenHeight - Top - 300) / RowHeight));

        private UILabel Text(string caption, int size, Vector2 at, float width)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = UIStyle.Current.DialogText;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.Position = at;
            label.Size = new Vector2(width, 1);
            label.Caption = caption;
            return label;
        }

        private UIBigButton Button(string caption, Action click)
        {
            var button = new UIBigButton(true);
            button.Caption = caption;
            button.Width = ButtonWidth;
            button.OnButtonClick += (b) => MemoriesPack.Guard("using the Memories window", click);
            return button;
        }

        /// <summary>Draw the window again, for the Sim and page it is on.</summary>
        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);
            Caption = "Memories of " + People.FullName(Sim);

            float y = Top;
            var memories = MemoryBook.Of(Sim).Reverse().ToList();
            var perPage = PerPage(ScrHeight);
            var pages = Math.Max(1, (memories.Count + perPage - 1) / perPage);
            Page = Math.Max(0, Math.Min(Page, pages - 1));
            try
            {
                if (memories.Count == 0)
                {
                    Body.Add(Text(People.FirstName(Sim) + " has no memories yet. They are made as things happen in play: a first kiss, a wedding, a new job or a promotion, a baby, a birthday, the death of a relative, a fire or a burglary.",
                        15, new Vector2(Margin, y), Width - Margin * 2));
                    y += 80;
                }
                foreach (var memory in memories.Skip(Page * perPage).Take(perPage))
                {
                    Body.Add(new UIMemoryMark(memory.Good) { Position = new Vector2(Margin, y + 6) });
                    Body.Add(Text(memory.Describe(People.FullName), 17, new Vector2(Margin + 28, y), Width - Margin * 2 - 28));
                    Body.Add(Text(memory.When + ", " + (memory.Stage == Stage.Adult || memory.Stage == Stage.Elder ? "an " : "a ") + Recognise.StageName(memory.Stage), 12,
                        new Vector2(Margin + 28, y + 26), Width - Margin * 2 - 28));
                    y += RowHeight;
                }
                if (pages > 1)
                {
                    Body.Add(Text("Page " + (Page + 1) + " of " + pages + " (newest first)", 12, new Vector2(Margin, y), Width - Margin * 2));
                    y += 24;
                }
            }
            catch (Exception e)
            {
                Log.Write("The Memories window: " + e);
            }

            //newer, older, the next Sim of the household; then close
            var row = new List<UIBigButton>();
            if (Page > 0) row.Add(Button("Newer", () => { Page--; Refresh(); }));
            if (Page < pages - 1) row.Add(Button("Older", () => { Page++; Refresh(); }));
            var household = HouseholdNow();
            if (household.Count > 1 || (household.Count == 1 && household[0] != Sim))
            {
                var next = household[(household.IndexOf(Sim) + 1) % household.Count];
                row.Add(Button("Next Sim", () => Show(next)));
            }
            y += 20;
            if (row.Count > 0)
            {
                var across = row.Count * ButtonWidth + (row.Count - 1) * ButtonGap;
                for (int i = 0; i < row.Count; i++)
                {
                    row[i].Position = new Vector2((Width - across) / 2 + i * (ButtonWidth + ButtonGap), y);
                    Body.Add(row[i]);
                }
                y += 90;
            }
            var close = Button("Close", CloseMe);
            close.Position = new Vector2((Width - ButtonWidth) / 2, y);
            Body.Add(close);

            SetHeight((int)(y + 125));
        }
    }

    /// <summary>A memory's mark: a small square, green for a happy memory and red for a sad one. Drawn from a stretched white pixel, so the pack has no artwork.</summary>
    public class UIMemoryMark : UIElement
    {
        private const float Side = 14;
        private readonly bool Good;
        private Texture2D Pixel;

        public UIMemoryMark(bool good)
        {
            Good = good;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            if (Pixel == null) Pixel = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var colour = Good ? new Color(92, 176, 72, 255) : new Color(196, 64, 56, 255);
            DrawLocalTexture(batch, Pixel, null, Vector2.Zero, new Vector2(Side, Side), colour);
        }
    }
}
