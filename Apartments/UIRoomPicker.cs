/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.LotView.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Panels;
using System;

namespace Apartments
{
    /// <summary>
    /// Choosing a room by clicking in it. It is one of the lot's own tools, like the wall or floor tool, so the click
    /// goes to it and not to the Sims or the object under the cursor, and Esc puts it away as it does theirs.
    ///
    /// The lot only hands clicks to a tool outside live mode, so live mode is switched off while this is out, and put
    /// back as it was afterwards.
    /// </summary>
    public class UIRoomPicker : UICustomLotControl
    {
        private UILotControl Lot;
        private bool WasLive;
        private bool Down;
        private bool Done;
        private Action<LotTilePos> Picked;
        private Action Cancelled;

        public static UIRoomPicker Current;

        /// <summary>Start choosing. Returns false if there is no lot to choose on.</summary>
        public static bool Begin(Action<LotTilePos> picked, Action cancelled)
        {
            var lot = Actions.Screen?.LotControl;
            if (lot == null) return false;
            Current?.Finish(false);
            var picker = new UIRoomPicker() { Lot = lot, WasLive = lot.LiveMode, Picked = picked, Cancelled = cancelled };
            lot.CustomControl?.Release();
            lot.CustomControl = picker;
            lot.LiveMode = false;
            Current = picker;
            return true;
        }

        public override void MouseDown(UpdateState state)
        {
            Down = true;
        }

        public override void MouseUp(UpdateState state)
        {
            if (!Down || Done) return;
            Down = false;
            //the tile under the cursor, on the floor being looked at (as the floor tool finds it)
            var world = Lot.World;
            var at = world.EstTileAtPosWithScroll(new Vector2(MousePosition.X, MousePosition.Y));
            var pos = LotTilePos.FromBigTile((short)Math.Floor(at.X), (short)Math.Floor(at.Y), world.State.Level);
            var picked = Picked;
            Finish(false);
            ApartmentsPack.Guard("choosing a room", () => picked?.Invoke(pos));
        }

        public override void Update(UpdateState state, bool scrolled)
        {
        }

        /// <summary>The lot is putting the tool away (Esc, or another tool was taken out).</summary>
        public override void Release()
        {
            if (Done) return;
            Done = true;
            if (Current == this) Current = null;
            Lot.LiveMode = WasLive;
            var cancelled = Cancelled;
            ApartmentsPack.Guard("putting the room chooser away", () => cancelled?.Invoke());
        }

        private void Finish(bool cancel)
        {
            if (Done) return;
            Done = true;
            if (Current == this) Current = null;
            if (Lot.CustomControl == this) Lot.CustomControl = null;
            Lot.LiveMode = WasLive;
            if (cancel) Cancelled?.Invoke();
        }
    }
}
