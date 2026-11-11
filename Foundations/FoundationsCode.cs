/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using HarmonyLib;
using System;

namespace FoundationsPack
{
    /// <summary>Private parts of the engine this pack reaches into.</summary>
    public static class FoundationsCode
    {
        public static AccessTools.FieldRef<VMArchitecture, bool> FloorsDirtyOf;
        public static AccessTools.FieldRef<VMArchitecture, bool> RedrawOf;
        public static AccessTools.FieldRef<VMArchitecture, Blueprint> WorldOf;

        public static AccessTools.FieldRef<TOwner, T> Field<TOwner, T>(string name)
        {
            if (AccessTools.Field(typeof(TOwner), name) == null) throw new Exception(typeof(TOwner).Name + "." + name + " was not found: this pack was made for a different version of Simitone");
            return AccessTools.FieldRefAccess<TOwner, T>(name);
        }

        public static void Find()
        {
            FloorsDirtyOf = Field<VMArchitecture, bool>("FloorsDirty");
            RedrawOf = Field<VMArchitecture, bool>("Redraw");
            WorldOf = Field<VMArchitecture, Blueprint>("WorldUI");
        }
    }
}
