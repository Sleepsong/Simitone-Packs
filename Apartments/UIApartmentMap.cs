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
using FSO.LotView.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// A plan of one floor of the lot, a square for each tile, coloured by what the tile is: an apartment (each its
    /// own colour), a shared room, or outdoors. Drawn from plain rectangles, so it needs no artwork.
    /// </summary>
    public class UIApartmentMap : UIContainer
    {
        public static readonly Color[] Palette =
        {
            new Color(86, 156, 214), new Color(229, 140, 60), new Color(120, 190, 90), new Color(200, 100, 190),
            new Color(235, 205, 80), new Color(90, 200, 200), new Color(225, 95, 95), new Color(160, 130, 230)
        };
        public static readonly Color Shared = new Color(150, 150, 150);
        public static readonly Color Outdoors = new Color(52, 74, 52);
        public static readonly Color Frame = new Color(16, 20, 30);

        /// <summary>The colour an apartment is drawn in: by its place among the lot's apartments, so it does not change as others come and go... until one before it is deleted.</summary>
        public static Color ColourOf(LotFile file, Apartment apartment)
        {
            var index = file.Apartments.OrderBy(x => x.Id).ToList().IndexOf(apartment);
            return Palette[Math.Max(0, index) % Palette.Length];
        }

        private VM vm;
        private Texture2D White;
        public sbyte Level = 1;
        public int TileSize;
        /// <summary>The apartment to pick out: the others are drawn fainter. Null: all alike.</summary>
        public Apartment PickedOut;

        private int LotWidth, LotHeight;
        private byte[] Cells;       //for each tile: 0 outdoors, 1 shared, 2+ an apartment (its place among them)
        private List<Apartment> Order = new List<Apartment>();
        private int Age = int.MaxValue;

        public int PixelWidth => LotWidth * TileSize;
        public int PixelHeight => LotHeight * TileSize;

        public UIApartmentMap(VM vm, int tileSize)
        {
            this.vm = vm;
            TileSize = Math.Max(1, tileSize);
            White = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var arch = vm.Context.Architecture;
            LotWidth = arch.Width;
            LotHeight = arch.Height;
            Rebuild();
        }

        /// <summary>What each tile of a floor is. Kept apart from the drawing so it can be checked without a screen.</summary>
        public static byte[] Classify(int width, int height, Func<int, int, int> apartmentIndexAt, Func<int, int, bool> outdoorsAt)
        {
            var cells = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = apartmentIndexAt(x, y);
                    cells[x + y * width] = (index >= 0) ? (byte)Math.Min(255, 2 + index) : (outdoorsAt(x, y) ? (byte)0 : (byte)1);
                }
            }
            return cells;
        }

        private void Rebuild()
        {
            Age = 0;
            var state = Neighbours.StateOf(vm);
            var rooms = vm.Context.RoomInfo;
            Order = (state?.File == null) ? new List<Apartment>() : state.File.Apartments.OrderBy(x => x.Id).ToList();
            var roomApts = (state?.File == null) ? new Dictionary<ushort, Apartment>() : Units.RoomApartments(vm, state.File);
            if (rooms == null)
            {
                Cells = new byte[LotWidth * LotHeight];
                return;
            }
            Func<int, int, ushort> roomAt = (x, y) => vm.Context.GetRoomAt(LotTilePos.FromBigTile((short)x, (short)y, Level));
            Cells = Classify(LotWidth, LotHeight,
                (x, y) => roomApts.TryGetValue(roomAt(x, y), out var apartment) ? Order.IndexOf(apartment) : -1,
                (x, y) =>
                {
                    var room = roomAt(x, y);
                    return room == 0 || room >= rooms.Length || rooms[room].Room.IsOutside || rooms[room].Room.IsPool;
                });
        }

        public void Refresh() => Rebuild();

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (++Age > 45) ApartmentsPack.Guard("drawing the plan of the lot", Rebuild); //walls move, rooms are added
        }

        private void Fill(UISpriteBatch batch, int x, int y, int w, int h, Color colour)
        {
            DrawLocalTexture(batch, White, null, new Vector2(x, y), new Vector2(w, h), colour);
        }

        private Color ColourOfCell(byte cell)
        {
            if (cell == 0) return Outdoors;
            if (cell == 1) return Shared;
            var apartment = (cell - 2 < Order.Count) ? Order[cell - 2] : null;
            var colour = Palette[(cell - 2) % Palette.Length];
            return (PickedOut == null || apartment == PickedOut) ? colour : Color.Lerp(colour, Outdoors, 0.6f);
        }

        private bool Broken;

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Cells == null || Broken) return;
            try
            {
                DrawPlan(batch);
            }
            catch (Exception e)
            {
                Broken = true; //once is enough: it would fail again every frame
                AptLog.Write("The plan of the lot could not be drawn: " + e);
            }
            base.Draw(batch);
        }

        private void DrawPlan(UISpriteBatch batch)
        {
            Fill(batch, -3, -3, PixelWidth + 6, PixelHeight + 6, Frame);
            //each row, in runs of the same kind of tile: far fewer rectangles than tiles
            for (int y = 0; y < LotHeight; y++)
            {
                var start = 0;
                for (int x = 1; x <= LotWidth; x++)
                {
                    if (x < LotWidth && Cells[x + y * LotWidth] == Cells[start + y * LotWidth]) continue;
                    Fill(batch, start * TileSize, y * TileSize, (x - start) * TileSize, TileSize, ColourOfCell(Cells[start + y * LotWidth]));
                    start = x;
                }
            }
        }
    }

    /// <summary>The plan of the lot in a window, with which colour is which apartment, floor by floor.</summary>
    public class UIMapDialog : UIMobileDialog
    {
        private const int Margin = 80;
        private const int Top = 105;
        private const int ButtonWidth = 275;
        private const int SmallButton = 150;

        private VM vm;
        private UIContainer Body;
        private sbyte Level = 1;
        private Action Closed;

        public static void Show(VM vm, sbyte level, Action closed)
        {
            UIScreen.GlobalShowDialog(new UIMapDialog(vm, level, closed), true);
        }

        private UIMapDialog(VM vm, sbyte level, Action closed) : base()
        {
            this.vm = vm;
            Level = (sbyte)Math.Max(1, Math.Min(vm.Context.Architecture.Stories, (int)level));
            Closed = closed;
            Caption = "Apartments on this lot";
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
            if (Body != null) Body.X = ((Closing) ? 1 : -1) * (1 - InterpolatedAnimation) * Width;
        }

        private UILabel AddLabel(string text, int size, Color color, Vector2 pos, int width)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = 2;
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
            btn.OnButtonClick += (b) => ApartmentsPack.Guard("using the plan of the lot", onClick);
            Body.Add(btn);
            return btn;
        }

        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);
            var style = UIStyle.Current;
            var arch = vm.Context.Architecture;
            var file = Neighbours.FileOf(vm);
            var house = Neighbours.HomeHouse(vm);

            //the plan on the left, as large as the screen allows, and what the colours mean on the right
            var room = Math.Max(120, ScrHeight - Top - 60 - 130);
            var tile = Math.Max(2, Math.Min(10, Math.Min(room / Math.Max(1, arch.Height), (Width / 2 - Margin) / Math.Max(1, arch.Width))));
            var map = new UIApartmentMap(vm, tile) { Level = Level };
            map.Refresh();
            map.Position = new Vector2(Margin, Top);
            Body.Add(map);

            var x = Margin + map.PixelWidth + 50;
            var w = Math.Max(150, Width - x - Margin);
            var y = Top;
            AddLabel("Floor " + Level + " of " + arch.Stories, 19, style.DialogText, new Vector2(x, y), w);
            y += 44;
            if (file != null)
            {
                foreach (var apartment in file.Apartments.OrderBy(a => a.Id))
                {
                    if (y > Top + map.PixelHeight - 30) break;
                    AddLabel(apartment.Name + "  -  " + Actions.TenantText(house, apartment), 15, UIApartmentMap.ColourOf(file, apartment), new Vector2(x, y), w);
                    y += 30;
                }
            }
            AddLabel("Shared rooms", 15, UIApartmentMap.Shared, new Vector2(x, y), w);
            y += 30;
            AddLabel("Outdoors (always shared)", 15, Color.Lerp(UIApartmentMap.Outdoors, Color.White, 0.45f), new Vector2(x, y), w);

            var buttonY = Top + Math.Max(map.PixelHeight, 150) + 30;
            AddButton("Close", true, new Vector2((Width - ButtonWidth) / 2, buttonY), ButtonWidth, () =>
            {
                Close();
                Closed?.Invoke();
            });
            if (arch.Stories > 1)
            {
                var down = AddButton("Floor -", false, new Vector2(Margin, buttonY), SmallButton + 60, () => { Level--; Refresh(); });
                down.Disabled = Level <= 1;
                var up = AddButton("Floor +", false, new Vector2(Width - Margin - SmallButton - 60, buttonY), SmallButton + 60, () => { Level++; Refresh(); });
                up.Disabled = Level >= arch.Stories;
            }
            SetHeight(buttonY + 125);
        }
    }

    /// <summary>
    /// What is on screen while a room is being chosen by clicking: a small plan of the floor being looked at, with the
    /// apartment being changed picked out, and what to do. It sits in a corner and takes no clicks.
    /// </summary>
    public class UIPickPanel : UIContainer
    {
        private const int Pad = 12;
        private const int TextWidth = 300;
        private const int TextHeight = 130;
        private static UIPickPanel Current;

        private VM vm;
        private UIApartmentMap Map;
        private Texture2D White;
        private int PanelWidth, PanelHeight;
        private UILabel Label;
        private string Instructions;

        /// <summary>Say how the last click went, under the instructions, and draw the plan again.</summary>
        public static void Say(string result)
        {
            var panel = Current;
            if (panel == null) return;
            ApartmentsPack.Guard("saying how a click went", () =>
            {
                panel.Label.Caption = panel.Instructions + "\n" + (result ?? "");
                panel.Map.Refresh();
            });
        }

        public static void Open(VM vm, Apartment apartment, bool remove)
        {
            Close();
            ApartmentsPack.Guard("showing the plan while choosing a room", () =>
            {
                Current = new UIPickPanel(vm, apartment, (remove ? "Click in rooms to take them out of their apartment." : ("Click in rooms to add them to " + apartment.Name + "."))
                    + " The lit rooms are " + apartment.Name + "'s. Press Esc when you have finished.");
                UIScreen.GlobalShowDialog(Current, false);
            });
        }

        /// <summary>The panel while every apartment is shown on the lot: the plan, and how to finish.</summary>
        public static void OpenShowing(VM vm)
        {
            Close();
            ApartmentsPack.Guard("showing the plan while apartments are shown on the lot", () =>
            {
                Current = new UIPickPanel(vm, null, "The apartments are lit, and the shared rooms darkened. Open the Apartments window again (F9) to finish.");
                UIScreen.GlobalShowDialog(Current, false);
            });
        }

        public static void Close()
        {
            var panel = Current;
            Current = null;
            if (panel != null) ApartmentsPack.Guard("putting the plan away", () => UIScreen.RemoveDialog(panel));
        }

        private UIPickPanel(VM vm, Apartment apartment, string instructions)
        {
            this.vm = vm;
            White = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var arch = vm.Context.Architecture;
            var tile = Math.Max(2, Math.Min(5, 300 / Math.Max(1, Math.Max(arch.Width, arch.Height))));
            Map = new UIApartmentMap(vm, tile) { PickedOut = apartment, Level = LevelNow() };
            Map.Refresh();
            Map.Position = new Vector2(Pad, Pad + TextHeight);
            Add(Map);

            PanelWidth = Math.Max(TextWidth, Map.PixelWidth) + Pad * 2;
            PanelHeight = Pad * 2 + TextHeight + Map.PixelHeight;

            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = 12;
            label.CaptionStyle.Color = Color.White;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = 7;
            label.Position = new Vector2(Pad, Pad);
            label.Size = new Vector2(PanelWidth - Pad * 2, 1);
            Instructions = instructions;
            label.Caption = Instructions;
            Label = label;
            Add(label);

            var screen = GameFacade.Screens.CurrentUIScreen;
            Position = new Vector2(Math.Max(0, screen.ScreenWidth - PanelWidth - 20), 20);
        }

        private sbyte LevelNow()
        {
            var level = Actions.Screen?.LotControl?.World?.State?.Level ?? 1;
            return (sbyte)Math.Max(1, Math.Min(vm.Context.Architecture.Stories, (int)level));
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //follow the floor the player is looking at
            ApartmentsPack.Guard("following the floor on the plan", () =>
            {
                var level = LevelNow();
                if (level != Map.Level)
                {
                    Map.Level = level;
                    Map.Refresh();
                }
            });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawLocalTexture(batch, White, null, Vector2.Zero, new Vector2(PanelWidth, PanelHeight), new Color(24, 28, 40) * 0.92f);
            base.Draw(batch);
        }
    }
}
