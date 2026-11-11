/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls.Catalog;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.SimAntics;
using FSO.UI.Panels.LotControls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace CreateAStyle
{
    /// <summary>
    /// Build mode's Style Brush: click an object to open its style, where each of its colours can be changed.
    /// - Ctrl and click (or the touch erase button) takes an object's style off.
    /// - Shift and click gives an object the style last made, if it is an object of the same kind.
    /// </summary>
    public class UIStyleBrush : UICustomLotControl
    {
        private VM vm;
        private World World;
        private ILotControl Parent;
        private short Hover;

        /// <summary>The style last made with the brush and the kind of object it was for, to give to others like it.</summary>
        private static Style LastStyle;
        private static uint LastGUID;

        public UIStyleBrush(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            this.vm = vm;
            World = parent.World;
            Parent = parent;
        }

        public static void Remember(VMEntity entity, Style style)
        {
            LastStyle = style?.Clone();
            LastGUID = KindOf(entity);
        }

        private static uint KindOf(VMEntity entity)
        {
            return (entity.MasterDefinition ?? entity.Object?.OBJ)?.GUID ?? 0;
        }

        /// <summary>The object under the cursor that can be styled, or null.</summary>
        private VMEntity Target()
        {
            if (Hover <= 0) return null;
            var entity = vm.GetObjectById(Hover);
            if (entity == null || entity is VMAvatar || !(entity.WorldUI is ObjectComponent)) return null;
            return entity;
        }

        private bool OverPanel => UIStylePanel.Current != null && UIStylePanel.Current.Contains(MousePosition);

        public override void MouseDown(UpdateState state)
        {
            if (OverPanel) return;
            var entity = Target();
            if (entity == null)
            {
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                return;
            }

            if (Modifiers.IsSet(UILotControlModifiers.CTRL))
            {
                if (UIStylePanel.Current != null && UIStylePanel.Current.IsFor(entity)) UIStylePanel.Close();
                CreateAStylePack.SetStyle(vm, entity, null);
                HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
            }
            else if (Modifiers.IsSet(UILotControlModifiers.SHIFT))
            {
                if (LastStyle == null || KindOf(entity) != LastGUID)
                {
                    HITVM.Get().PlaySoundEvent(UISounds.Error);
                    return;
                }
                if (UIStylePanel.Current != null && UIStylePanel.Current.IsFor(entity)) UIStylePanel.Close();
                CreateAStylePack.SetStyle(vm, entity, LastStyle.Clone());
                HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
            }
            else
            {
                UIStylePanel.Open(vm, entity);
                HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolDown);
            }
        }

        public override void MouseUp(UpdateState state) { }

        public override void Update(UpdateState state, bool scrolled)
        {
            if (OverPanel)
            {
                Hover = 0;
                return;
            }
            Hover = World.GetObjectIDAtScreenPos(MousePosition.X, MousePosition.Y, GameFacade.GraphicsDevice);
            var entity = Target();

            string tip;
            if (entity == null) tip = "Click an object to style it";
            else if (Modifiers.IsSet(UILotControlModifiers.CTRL)) tip = (CreateAStylePack.StyleOf(entity) != null) ? "Take the style off" : "No style to take off";
            else if (Modifiers.IsSet(UILotControlModifiers.SHIFT)) tip = (LastStyle != null && KindOf(entity) == LastGUID) ? "Give it the last style" : "The last style was for another kind of object";
            else tip = "Style " + entity.ToString();

            state.UIState.TooltipProperties.Show = true;
            state.UIState.TooltipProperties.Color = Color.Black;
            state.UIState.TooltipProperties.Opacity = 1;
            state.UIState.TooltipProperties.Position = new Vector2(MousePosition.X, MousePosition.Y);
            state.UIState.Tooltip = tip;
            state.UIState.TooltipProperties.UpdateDead = false;
        }

        public override void Release()
        {
            UIStylePanel.Close();
        }
    }

    /// <summary>The Style Brush's name, picture and price in the build catalogue (Wall Coverings).</summary>
    public class UICatalogStyleBrushResProvider : UICatalogResProvider
    {
        private static Texture2D Icon;

        public override Texture2D GetIcon(ulong id)
        {
            if (Icon == null || Icon.IsDisposed) Icon = DrawWheel(GameFacade.GraphicsDevice);
            return Icon;
        }

        public override Texture2D GetThumb(ulong id) => GetIcon(id);

        public override string GetName(ulong id) => "Style Brush";

        public override string GetDescription(ulong id)
        {
            return "Create a Style, as in The Sims 3: click an object to change its colours, one part at a time. "
                + "Hold Ctrl (or use erase) and click to take a style off. Hold Shift and click to give another object of the same kind the style you made last.";
        }

        public override int GetPrice(ulong id) => 0;

        public override bool DoDispose() => false;

        /// <summary>A colour wheel.</summary>
        private static Texture2D DrawWheel(GraphicsDevice gd)
        {
            const int size = 64;
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var dx = x - 31.5f;
                    var dy = y - 31.5f;
                    var d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d >= 28) continue;
                    var hue = (float)(Math.Atan2(dy, dx) * 180 / Math.PI) + 180;
                    var c = ColorMath.FromHsv(hue, Math.Min(1, d / 22f), 1);
                    if (d > 26) c = Color.Lerp(c, Color.Black, 0.35f);
                    data[x + y * size] = c;
                }
            }
            var tex = new Texture2D(gd, size, size);
            tex.SetData(data);
            return tex;
        }
    }
}
