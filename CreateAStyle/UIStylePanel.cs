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
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreateAStyle
{
    /// <summary>
    /// The style of an object or a Sim. An object is one piece; a Sim has a tab for each texture they wear that can be
    /// styled (clothes, hair and hat, hands). Each piece has a block for each of its parts, with what the part looked
    /// like, what it looks like now, and three sliders (hue, colour strength, lightness). The object or Sim changes as
    /// the sliders move. The panel sits at the side of the screen so they stay in view, and draws itself from plain
    /// rectangles, so it needs no artwork of its own.
    /// </summary>
    public class UIStylePanel : UIContainer
    {
        public static UIStylePanel Current;

        private const int PanelWidth = 300;
        private const int Margin = 12;
        private const int HeaderHeight = 52;
        private const int TabHeight = 26;
        private const int BlockHeight = 96;
        private const int NoteHeight = 70;
        private const int FooterHeight = 74;
        private const int SliderX = 92;
        private const int SliderWidth = PanelWidth - SliderX - Margin;
        private const int SliderHeight = 14;
        private const int SliderGap = 22;
        private const int Segments = 32;
        private static readonly Color Soft = new Color(190, 200, 215);

        private StyleTarget Target;
        private int Tab;
        private StylePiece Piece => Target.Pieces.Count == 0 ? null : Target.Pieces[Tab];
        private Style Working => Piece?.Working;
        private bool HasTabs => Target.Pieces.Count > 1;
        private int ContentTop => HeaderHeight + (HasTabs ? TabHeight + 8 : 0);

        private int PanelHeight;
        private Texture2D White;
        private List<UILabel> Labels = new List<UILabel>();
        private UIMouseEventRef MouseRef;
        private List<Rectangle> Tabs = new List<Rectangle>();
        private Rectangle ResetButton, DoneButton;
        private UILabel Status;

        private bool WasDown;
        /// <summary>The slider being dragged: the part's number times 3, plus 0 for hue, 1 for strength, 2 for lightness. Or -1.</summary>
        private int Dragging = -1;
        /// <summary>Pieces changed since they were last shown in the game.</summary>
        private HashSet<StylePiece> Unapplied = new HashSet<StylePiece>();
        private long LastApplied;

        public static void Open(VM vm, VMEntity entity)
        {
            Close();
            var panel = new UIStylePanel(StyleTarget.For(vm, entity));
            Current = panel;
            UIScreen.GlobalShowDialog(panel, false);
        }

        public static void Close()
        {
            var panel = Current;
            if (panel == null) return;
            Current = null;
            panel.ApplyNow();
            if (panel.Target is ObjectTarget obj) UIStyleBrush.Remember(obj.Entity, obj.Pieces[0].Working.IsDefault ? null : obj.Pieces[0].Working);
            UIScreen.RemoveDialog(panel);
        }

        /// <summary>
        /// Closes the panel without putting its changes in the game: the lot it was for is gone, and the panel with it,
        /// or (remove) the style is being taken off.
        /// </summary>
        public static void Drop(bool remove = false)
        {
            var panel = Current;
            Current = null;
            if (remove && panel != null) UIScreen.RemoveDialog(panel);
        }

        public bool IsFor(VMEntity entity) => Target.IsFor(entity);

        /// <summary>Whether a point of the screen is on the panel.</summary>
        public bool Contains(Point screen)
        {
            var local = GlobalPoint(screen.ToVector2());
            return local.X >= 0 && local.Y >= 0 && local.X < PanelWidth && local.Y < PanelHeight;
        }

        private UIStylePanel(StyleTarget target)
        {
            Target = target;
            White = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            Build();
        }

        /// <summary>Lays the panel out for the piece on show.</summary>
        private void Build()
        {
            foreach (var label in Labels) Remove(label);
            Labels.Clear();
            if (MouseRef != null) RemoveMouseListener(MouseRef);
            Tabs.Clear();
            Dragging = -1;

            var piece = Piece;
            var parts = Working?.Channels.Count ?? 0;
            var body = (parts > 0) ? parts * BlockHeight : NoteHeight;
            PanelHeight = ContentTop + body + FooterHeight;

            AddLabel("Create a Style", Margin, 6, PanelWidth - Margin * 2, 16, Color.White);
            AddLabel(Target.Name, Margin, 28, PanelWidth - Margin * 2, 11, Soft);

            if (HasTabs)
            {
                var width = (PanelWidth - Margin * 2) / Target.Pieces.Count;
                for (int i = 0; i < Target.Pieces.Count; i++)
                {
                    var rect = new Rectangle(Margin + i * width, HeaderHeight, width - 4, TabHeight);
                    Tabs.Add(rect);
                    AddLabel(Target.Pieces[i].Title, rect.X, rect.Y + 6, rect.Width, 10, Color.White, TextAlignment.Center | TextAlignment.Top);
                }
            }

            if (piece == null)
            {
                AddLabel("Nothing here can be styled.", Margin, ContentTop + 12, PanelWidth - Margin * 2, 12, Color.White, wrapped: true);
            }
            else if (parts == 0)
            {
                AddLabel(piece.Note ?? "No colours here that can be changed.", Margin, ContentTop + 8, PanelWidth - Margin * 2, 11, Color.White, wrapped: true);
            }
            int colour = 0;
            for (int i = 0; i < parts; i++)
            {
                var channel = Working.Channels[i];
                var top = ContentTop + i * BlockHeight;
                var name = channel.Neutral ? "Greys" : "Colour " + (++colour);
                AddLabel(name + "  " + Math.Max(1, (int)Math.Round(channel.Share * 100)) + "%", Margin, top + 2, 120, 11, Color.White);
                AddLabel("Hue", SliderX - 62, top + 22, 58, 10, Soft, TextAlignment.Right | TextAlignment.Top);
                AddLabel(channel.Neutral ? "Tint" : "Strength", SliderX - 62, top + 22 + SliderGap, 58, 10, Soft, TextAlignment.Right | TextAlignment.Top);
                AddLabel("Light", SliderX - 62, top + 22 + SliderGap * 2, 58, 10, Soft, TextAlignment.Right | TextAlignment.Top);
            }

            Status = AddLabel("", Margin, PanelHeight - FooterHeight + 4, PanelWidth - Margin * 2, 10, Soft, wrapped: true);
            var buttonY = PanelHeight - 38;
            ResetButton = new Rectangle(Margin, buttonY, 130, 28);
            DoneButton = new Rectangle(PanelWidth - Margin - 130, buttonY, 130, 28);
            AddLabel("Reset", ResetButton.X, ResetButton.Y + 5, ResetButton.Width, 12, Color.White, TextAlignment.Center | TextAlignment.Top);
            AddLabel("Done", DoneButton.X, DoneButton.Y + 5, DoneButton.Width, 12, Color.White, TextAlignment.Center | TextAlignment.Top);

            //clicks on the panel are the panel's, not the lot's behind it
            MouseRef = ListenForMouse(new Rectangle(0, 0, PanelWidth, PanelHeight), (type, state) => { });
            Place();
        }

        private UILabel AddLabel(string text, int x, int y, int width, int size, Color colour,
            TextAlignment align = TextAlignment.Left | TextAlignment.Top, bool wrapped = false)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = colour;
            label.CaptionStyle.Shadow = false;
            label.Alignment = align;
            label.Wrapped = wrapped;
            label.Size = new Vector2(width, 0);
            label.Position = new Vector2(x, y);
            label.Caption = text ?? "";
            Add(label);
            Labels.Add(label);
            return label;
        }

        private void Place()
        {
            var screen = UIScreen.Current;
            if (screen == null) return;
            Position = new Vector2(Math.Max(0, screen.ScreenWidth - PanelWidth - 16), Math.Max(0, Math.Min(90, screen.ScreenHeight - PanelHeight)));
        }

        public override void GameResized()
        {
            base.GameResized();
            Place();
        }

        // ---- the sliders ----

        private Rectangle SliderRect(int part, int slider)
        {
            return new Rectangle(SliderX, ContentTop + part * BlockHeight + 24 + slider * SliderGap, SliderWidth, SliderHeight);
        }

        private static float Min(StyleChannel channel, int slider) => (slider == 2) ? StyleChannel.MinBrightness : 0;

        private static float Max(StyleChannel channel, int slider)
        {
            return (slider == 0) ? 360 : (slider == 1) ? channel.MaxSaturation : StyleChannel.MaxBrightness;
        }

        private static float Get(StyleChannel channel, int slider)
        {
            return (slider == 0) ? channel.Hue : (slider == 1) ? channel.Saturation : channel.Brightness;
        }

        private static void Set(StyleChannel channel, int slider, float value)
        {
            if (slider == 0) channel.Hue = Math.Min(value, 359.9f);
            else if (slider == 1) channel.Saturation = value;
            else channel.Brightness = value;
        }

        /// <summary>The part's sample colour as it would come out with a slider at some point along its length.</summary>
        private static Color Preview(StyleChannel channel, int slider, float along)
        {
            var copy = channel.Clone();
            Set(copy, slider, Min(channel, slider) + (Max(channel, slider) - Min(channel, slider)) * along);
            //a grey with no tint shows no hue: show the hue slider as the hues it would be tinted with
            if (slider == 0 && channel.Neutral && copy.Saturation < 0.5f) copy.Saturation = 0.5f;
            return copy.Result;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (Current != this) return;
            if (Target.Gone)
            {
                Close();
                return;
            }

            var down = state.MouseState.LeftButton == ButtonState.Pressed;
            var mouse = GlobalPoint(state.MouseState.Position.ToVector2());
            var point = new Point((int)mouse.X, (int)mouse.Y);
            var parts = Working?.Channels.Count ?? 0;

            if (down && !WasDown)
            {
                Dragging = -1;
                for (int i = 0; i < parts && Dragging == -1; i++)
                {
                    for (int s = 0; s < 3; s++)
                    {
                        var rect = SliderRect(i, s);
                        rect.Inflate(4, 4);
                        if (rect.Contains(point)) { Dragging = i * 3 + s; break; }
                    }
                }
                if (Dragging == -1)
                {
                    var tab = Tabs.FindIndex(x => x.Contains(point));
                    if (tab != -1 && tab != Tab)
                    {
                        Tab = tab;
                        WasDown = down;
                        Build();
                        return;
                    }
                    if (ResetButton.Contains(point) && Working != null)
                    {
                        foreach (var channel in Working.Channels) channel.Reset();
                        Changed();
                    }
                    else if (DoneButton.Contains(point))
                    {
                        WasDown = down;
                        Close();
                        return;
                    }
                }
            }
            if (!down) Dragging = -1;

            if (Dragging != -1 && Dragging / 3 < parts)
            {
                var channel = Working.Channels[Dragging / 3];
                var slider = Dragging % 3;
                var along = Math.Max(0f, Math.Min(1f, (mouse.X - SliderX) / SliderWidth));
                var value = Min(channel, slider) + (Max(channel, slider) - Min(channel, slider)) * along;
                if (Math.Abs(value - Get(channel, slider)) > 0.0005f)
                {
                    Set(channel, slider, value);
                    Changed();
                }
            }
            WasDown = down;

            //the game follows the sliders, but not on every twitch: each change makes its textures again
            if (Unapplied.Count > 0 && (!down || Environment.TickCount64 - LastApplied > 80)) ApplyNow();

            //how the recolouring is going, so that if it is not, it says why
            var status = (Piece == null) ? "" : Target.StatusOf(Piece) ?? (parts > 0 && Working.IsDefault ? "Move a slider to change a part." : "");
            if (Status.Caption != status) Status.Caption = status;
        }

        private void Changed()
        {
            Working.Touch();
            Unapplied.Add(Piece);
        }

        private void ApplyNow()
        {
            if (Unapplied.Count == 0) return;
            LastApplied = Environment.TickCount64;
            var pieces = Unapplied.ToList();
            Unapplied.Clear();
            if (Target.Gone) return;
            foreach (var piece in pieces) Target.Apply(piece);
        }

        // ---- drawing ----

        private void Fill(UISpriteBatch batch, Rectangle rect, Color colour)
        {
            DrawLocalTexture(batch, White, null, new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height), colour);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            Fill(batch, new Rectangle(0, 0, PanelWidth, PanelHeight), new Color(24, 28, 40) * 0.94f);
            Fill(batch, new Rectangle(0, 0, PanelWidth, HeaderHeight - 6), new Color(0, 70, 140) * 0.9f);

            for (int i = 0; i < Tabs.Count; i++)
            {
                Fill(batch, Tabs[i], (i == Tab) ? new Color(0, 110, 200) : new Color(55, 62, 80));
            }

            var parts = Working?.Channels.Count ?? 0;
            for (int i = 0; i < parts; i++)
            {
                var channel = Working.Channels[i];
                var top = ContentTop + i * BlockHeight;
                Fill(batch, new Rectangle(Margin, top - 3, PanelWidth - Margin * 2, 1), Color.White * 0.15f);

                //what the part was, and what it is now
                Fill(batch, new Rectangle(PanelWidth - Margin - 64, top + 1, 64, 16), Color.Black);
                Fill(batch, new Rectangle(PanelWidth - Margin - 63, top + 2, 30, 14), Opaque(channel.Sample));
                Fill(batch, new Rectangle(PanelWidth - Margin - 31, top + 2, 30, 14), Opaque(channel.Result));

                for (int s = 0; s < 3; s++)
                {
                    var rect = SliderRect(i, s);
                    Fill(batch, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), Color.Black);
                    for (int seg = 0; seg < Segments; seg++)
                    {
                        var x0 = rect.X + rect.Width * seg / Segments;
                        var x1 = rect.X + rect.Width * (seg + 1) / Segments;
                        Fill(batch, new Rectangle(x0, rect.Y, x1 - x0, rect.Height), Opaque(Preview(channel, s, (seg + 0.5f) / Segments)));
                    }
                    var along = (Get(channel, s) - Min(channel, s)) / (Max(channel, s) - Min(channel, s));
                    var knob = rect.X + (int)Math.Round(Math.Max(0, Math.Min(1, along)) * rect.Width);
                    Fill(batch, new Rectangle(knob - 3, rect.Y - 3, 6, rect.Height + 6), Color.Black);
                    Fill(batch, new Rectangle(knob - 2, rect.Y - 2, 4, rect.Height + 4), Color.White);
                }
            }

            Fill(batch, ResetButton, new Color(70, 76, 92));
            Fill(batch, DoneButton, new Color(0, 130, 70));
            base.Draw(batch);
        }

        private static Color Opaque(Color c) => new Color(c.R, c.G, c.B, (byte)255);
    }
}
