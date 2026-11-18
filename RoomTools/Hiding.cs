/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;

namespace RoomTools
{
    /// <summary>
    /// Objects hidden for a while: a room's, while it is carried (as buy mode hides an object it is moving), so the
    /// room looks lifted from where it was. Hiding is the object's own Hidden flag, which the game saves with the lot,
    /// so whatever keeps the lot (an undo step) sees them as they really are, shown.
    /// </summary>
    public static class Hiding
    {
        private static readonly Dictionary<VMEntity, short> Ours = new Dictionary<VMEntity, short>();

        public static void Hide(IEnumerable<VMEntity> entities)
        {
            foreach (var e in entities)
            {
                if (e == null || e.Dead || Ours.ContainsKey(e)) continue;
                Ours[e] = e.GetValue(VMStackObjectVariable.Hidden);
                e.SetValue(VMStackObjectVariable.Hidden, 1);
            }
        }

        /// <summary>Show everything Room Tools hid, as it was.</summary>
        public static void ShowAll()
        {
            foreach (var pair in Ours)
                if (!pair.Key.Dead) pair.Key.SetValue(VMStackObjectVariable.Hidden, pair.Value);
            Ours.Clear();
        }

        /// <summary>
        /// Do something (keep the lot) with every object shown as it really is: those Room Tools has hidden, and the one
        /// buy mode hides while it is being moved (whose move is sent before buy mode shows it again).
        /// </summary>
        public static T AsItIs<T>(Func<T> keep)
        {
            var put = new List<(VMEntity entity, short value)>();
            void Set(VMEntity e, short value)
            {
                put.Add((e, e.GetValue(VMStackObjectVariable.Hidden)));
                e.SetValue(VMStackObjectVariable.Hidden, value);
            }
            foreach (var pair in Ours) if (!pair.Key.Dead) Set(pair.Key, pair.Value);
            try
            {
                var holding = RoomToolsPack.Screen?.LotControl?.ObjectHolder?.Holding;
                if (holding?.RealEnt != null && holding.Group?.BaseObject != null) AsBuyModeShows(holding.Group.BaseObject, holding.RealEnt, Set);
            }
            catch (Exception e) { RoomToolsPack.Log("Could not show the object being moved, to keep the lot: " + e.Message); }
            try { return keep(); }
            finally
            {
                for (int i = put.Count - 1; i >= 0; i--) put[i].entity.SetValue(VMStackObjectVariable.Hidden, put[i].value);
            }
        }

        /// <summary>As buy mode shows an object again when it is put down: each part as its stand-in is.</summary>
        private static void AsBuyModeShows(VMEntity fake, VMEntity real, Action<VMEntity, short> set)
        {
            var group = real.MultitileGroup;
            var fakes = fake.MultitileGroup;
            for (int i = 0; i < group.Objects.Count && i < fakes.Objects.Count; i++)
            {
                set(group.Objects[i], fakes.Objects[i].GetValue(VMStackObjectVariable.Hidden));
                var slots = group.Objects[i].TotalSlots();
                for (int j = 0; j < slots; j++)
                {
                    var slot = group.Objects[i].GetSlot(j);
                    var slot2 = fakes.Objects[i].GetSlot(j);
                    if (slot != null && slot2 != null && slot.GetValue(VMStackObjectVariable.Hidden) != slot2.GetValue(VMStackObjectVariable.Hidden))
                        AsBuyModeShows(slot2, slot, set);
                }
            }
        }
    }
}
