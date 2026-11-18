/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using System;
using System.Collections.Generic;

namespace LotsBin
{
    /// <summary>
    /// The bin, opened from a lot's panel on the neighbourhood screen: its lots one at a time, with their pictures, to
    /// put down on that lot or to delete. Made like the game's own dialogs, from its stripes, labels and buttons.
    /// </summary>
    public class UIBinDialog : UIMobileDialog
    {
        private const int Margin = 80;
        private const int Top = 105;
        private const int PictureBox = 320;
        private const int ButtonWidth = 275;
        private const int SmallButton = 150;
        private const int Gap = 20;
        private const int TextSize = 15;

        private short House;
        private UIHouseSelectPanel Panel;
        private List<BinLot> Lots;
        private int Index;
        private UIContainer Body;
        private Texture2D Picture;
        private bool Busy;

        /// <summary>What CheckFit said of each lot here, so turning the pages back and forth does not read them again.</summary>
        private Dictionary<string, Tuple<string, bool>> Fits = new Dictionary<string, Tuple<string, bool>>();

        public static void Open(short house, UIHouseSelectPanel panel)
        {
            UIScreen.GlobalShowDialog(new UIBinDialog(house, panel), true);
        }

        private UIBinDialog(short house, UIHouseSelectPanel panel) : base()
        {
            Caption = "Lots Bin";
            House = house;
            Panel = panel;
            Lots = Bin.All();
            Refresh();
        }

        public override void GameResized()
        {
            base.GameResized();
            LotsBinPack.Guard("laying the bin out again", Refresh);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //ride in and out with the dialog's stripes
            if (Body != null) Body.X = ((Closing) ? 1 : -1) * (1 - InterpolatedAnimation) * Width;
        }

        public override void Removed()
        {
            base.Removed();
            DropPicture();
        }

        private void DropPicture()
        {
            Picture?.Dispose();
            Picture = null;
        }

        private Tuple<string, bool> FitOf(BinLot lot)
        {
            if (!Fits.TryGetValue(lot.Folder, out var fit))
            {
                string problem;
                bool otherShape = false;
                try
                {
                    problem = LotCopy.CheckFit(lot, House, out otherShape);
                }
                catch (Exception e)
                {
                    LotsBinPack.Log("Could not see whether " + lot.Folder + " fits house " + House + ": " + e);
                    problem = "The lot in the bin could not be read.";
                }
                Fits[lot.Folder] = fit = Tuple.Create(problem, otherShape);
            }
            return fit;
        }

        private UILabel AddLabel(string text, int size, Color color, Vector2 pos, int width, int lines)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = lines;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            label.Caption = text ?? "";
            Body.Add(label);
            return label;
        }

        private UIBigButton AddButton(string text, bool green, Vector2 pos, int width, Action onClick)
        {
            var btn = new UIBigButton(green);
            btn.Caption = text;
            btn.Width = width;
            btn.Position = pos;
            btn.OnButtonClick += (b) => { if (!Busy) LotsBinPack.Guard(text.ToLowerInvariant(), onClick); };
            Body.Add(btn);
            return btn;
        }

        private static int LinesOf(string text, int width)
        {
            var perLine = Math.Max(10, (int)(width / (TextSize * 0.55f)));
            var lines = 0;
            foreach (var paragraph in (text ?? "").Split('\n')) lines += Math.Max(1, (paragraph.Length + perLine - 1) / perLine);
            return lines;
        }

        private void Refresh()
        {
            if (Body != null) Remove(Body);
            DropPicture();
            Body = new UIContainer();
            Add(Body);

            var style = UIStyle.Current;
            var inner = Width - Margin * 2;
            int y;

            if (Lots.Count == 0)
            {
                var empty = "The bin is empty. To put a lot in it, choose the lot on the neighbourhood screen, then More, then " + LotsBinPack.SaveCaption + ".";
                AddLabel(empty, TextSize, style.DialogText, new Vector2(Margin, Top), inner, 4);
                y = Top + 4 * 24 + 30;
            }
            else
            {
                Index = Math.Max(0, Math.Min(Index, Lots.Count - 1));
                var lot = Lots[Index];
                var info = lot.Info;

                //the picture, on the left, scaled to fit its box
                var textX = Margin;
                try
                {
                    var iff = Bin.ReadLot(lot);
                    var chunk = (BMP)iff.Get<PNG>(513) ?? iff.Get<BMP>(513);
                    Picture = chunk?.GetTexture(GameFacade.GraphicsDevice);
                }
                catch (Exception e)
                {
                    LotsBinPack.Log("Could not show the picture of " + lot.Folder + ": " + e.Message);
                }
                if (Picture != null && inner > PictureBox * 2)
                {
                    var scale = Math.Min(1f, Math.Min(PictureBox / (float)Picture.Width, PictureBox / (float)Picture.Height));
                    var image = new UIImage(Picture);
                    image.ScaleX = image.ScaleY = scale;
                    image.Position = new Vector2(Margin + (PictureBox - Picture.Width * scale) / 2, Top + (PictureBox - Picture.Height * scale) / 2);
                    Body.Add(image);
                    textX = Margin + PictureBox + Gap * 2;
                }
                var textWidth = Width - Margin - textX;

                y = Top;
                AddLabel(lot.Name, 25, style.DialogTitle, new Vector2(textX, y), textWidth, 1);
                y += 40;
                if (!string.IsNullOrWhiteSpace(info.Description))
                {
                    var lines = Math.Min(5, LinesOf(info.Description, textWidth));
                    AddLabel(info.Description, TextSize, style.DialogText, new Vector2(textX, y), textWidth, lines);
                    y += lines * 24 + 10;
                }
                var facts = info.Size + " by " + info.Size + " tiles, built and furnished for §" + info.Value.ToString("##,#0")
                    + ". Saved " + info.Saved.ToString("d MMMM yyyy") + ", from house " + info.FromHouse + " of " + info.FromNeighbourhood + ".";
                AddLabel(facts, TextSize, style.SecondaryText, new Vector2(textX, y), textWidth, 3);
                y += Math.Min(3, LinesOf(facts, textWidth)) * 24 + 10;

                var fit = FitOf(lot);
                var fitText = fit.Item1 ?? (fit.Item2
                    ? "This lot faces another way, or its road and pavement are laid out differently. It can go here, but will not match the street."
                    : "It fits this lot.");
                AddLabel(fitText, TextSize, (fit.Item1 == null && !fit.Item2) ? style.DialogText : style.BtnActive, new Vector2(textX, y), textWidth, 4);
                y += Math.Min(4, LinesOf(fitText, textWidth)) * 24 + 10;

                var missing = PackData.Missing(info.SavedWith);
                if (missing.Count > 0)
                {
                    var text = "Saved with packs that are switched off now: " + string.Join(", ", missing) + ". What they added does not work until they are on again.";
                    AddLabel(text, TextSize, style.BtnActive, new Vector2(textX, y), textWidth, 4);
                    y += Math.Min(4, LinesOf(text, textWidth)) * 24 + 10;
                }

                AddLabel((Index + 1) + " of " + Lots.Count, TextSize, style.SecondaryText, new Vector2(Margin, Top + PictureBox + 10), PictureBox, 1);
                y = Math.Max(y, Top + PictureBox + 40) + 20;

                //place and delete, with the page turners either side
                var rowWidth = ButtonWidth * 2 + Gap;
                var place = AddButton(LotsBinPack.PlaceCaption, true, new Vector2((Width - rowWidth) / 2, y), ButtonWidth, () => AskPlace(lot));
                place.Disabled = fit.Item1 != null;
                AddButton("Delete", false, new Vector2((Width - rowWidth) / 2 + ButtonWidth + Gap, y), ButtonWidth, () => AskDelete(lot));
                if (Lots.Count > 1)
                {
                    var prev = AddButton("<", false, new Vector2(Margin, y), SmallButton, () => { Index--; Refresh(); });
                    prev.Disabled = Index == 0;
                    var next = AddButton(">", false, new Vector2(Width - Margin - SmallButton, y), SmallButton, () => { Index++; Refresh(); });
                    next.Disabled = Index == Lots.Count - 1;
                }
                y += 115;
            }

            AddButton("Cancel", false, new Vector2((Width - ButtonWidth) / 2, y), ButtonWidth, () => Close());
            SetHeight(Math.Min(ScrHeight, y + 125));
        }

        private void AskPlace(BinLot lot)
        {
            var fit = FitOf(lot);
            if (fit.Item1 != null) return;
            var message = "\"" + lot.Name + "\" will replace what is on this lot now, and the packs' data for it."
                + (fit.Item2 ? "\n\nThis lot faces another way or is laid out differently, so the copy will not match the street around it." : "")
                + "\n\nWhat is there now is kept in the bin's Replaced folder, in case it is wanted back. Put it down?";
            LotsBinPack.Ask(LotsBinPack.PlaceCaption, message, () =>
            {
                Busy = true;
                try
                {
                    LotCopy.Place(lot, House);
                }
                finally
                {
                    Busy = false;
                }
                Close();
                LotsBinPack.Placed(House, Panel);
                LotsBinPack.Tell(LotsBinPack.PlaceCaption, "\"" + lot.Name + "\" is on this lot now. Nobody lives there yet: move a family in, or enter it to build.");
            });
        }

        private void AskDelete(BinLot lot)
        {
            LotsBinPack.Ask("Delete", "Delete \"" + lot.Name + "\" from the bin? This cannot be undone.", () =>
            {
                Bin.Delete(lot);
                LotsBinPack.Log("Deleted " + lot.Folder + " from the bin.");
                Lots = Bin.All();
                Refresh();
            });
        }
    }
}
