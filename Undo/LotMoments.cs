/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using FSO.SimAntics.Marshals;
using HarmonyLib;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Undo
{
    /// <summary>The lot at one moment: the engine's own saved state for it, and what the packs had for it.</summary>
    public class LotMoment
    {
        /// <summary>The VM's state (VM.Save), written as Simitone writes a lot's save (VMMarshal.SerializeInto, compressed).</summary>
        public byte[] State;
        /// <summary>What each pack had for the lot (PackParts).</summary>
        public PackParts.Parts Packs;

        public long Size => (State?.Length ?? 0) + (Packs?.Size ?? 0);
    }

    /// <summary>
    /// Keeping the lot being played as it is, and putting it back.
    ///
    /// The lot is kept as Simitone keeps it in a save: VM.Save, which has everything on it, every object and Sim with
    /// what they were doing, the walls, floors, ground, pools and roof. It is put back as Simitone loads a lot that was
    /// handed back after a visit: VM.Load, with no reset, so every Sim carries on as they were. Build and buy mode keep
    /// the game paused, so nothing moves between one moment and the next but the change itself.
    ///
    /// VM.Load makes everything again, the VM's context and the blueprint the world draws included. Some of what they
    /// held is not the lot's but the session's, and is carried across here: the cheats switched on, the weather, which
    /// Sim is selected. Everything the packs keep is theirs to put back (PackParts).
    /// </summary>
    public static class LotMoments
    {
        public static LotMoment Take(VM vm)
        {
            var marshal = vm.Save();
            using (var mem = new MemoryStream())
            {
                marshal.SerializeInto(new BinaryWriter(mem));
                return new LotMoment() { State = mem.ToArray(), Packs = PackParts.Snapshot(vm) };
            }
        }

        /// <summary>The family's budget, or null on a lot that no family plays (where building is free).</summary>
        public static int? Budget(VM vm)
        {
            var family = vm?.TS1State?.CurrentFamily;
            return (family == null) ? (int?)null : family.Budget;
        }

        public static void SetBudget(VM vm, int budget)
        {
            var family = vm?.TS1State?.CurrentFamily;
            if (family != null) family.Budget = budget;
        }

        private static readonly PropertyInfo CheatsProperty = AccessTools.Property(typeof(VMContext), nameof(VMContext.Cheats));
        private static readonly FieldInfo SnowField = AccessTools.Field(typeof(TS1GameScreen), "TerrainSnowApplied");

        /// <summary>Check this Simitone has what putting a lot back needs. Throws if not.</summary>
        public static void CheckEngine()
        {
            if (CheatsProperty?.GetSetMethod(true) == null) throw new Exception("VMContext.Cheats cannot be set: this pack was made for a different version of Simitone");
            if (SnowField == null || SnowField.FieldType != typeof(bool)) throw new Exception("TS1GameScreen.TerrainSnowApplied was not found: this pack was made for a different version of Simitone");
        }

        /// <summary>
        /// Put the lot being played back to a moment. The lot's own state is read first: if it cannot be, nothing changes.
        /// Returns the names of the packs that could not put their part back.
        /// </summary>
        public static List<string> Restore(VM vm, LotMoment moment, TS1GameScreen screen)
        {
            var marshal = new VMMarshal();
            using (var reader = new BinaryReader(new MemoryStream(moment.State))) marshal.Deserialize(reader);

            var lot = screen?.LotControl;
            //an object in the hand is a ghost the VM does not keep: put it down where it came from first
            if (lot?.ObjectHolder?.Holding != null) lot.ObjectHolder.ClearSelected();

            //what the session has that VM.Load would lose
            var cheats = vm.Context.Cheats;
            var weather = vm.Context.Blueprint?.Weather?.WeatherData;
            var selected = lot?.ActiveEntity?.ObjectID;

            vm.Load(marshal);

            if (cheats != null) CheatsProperty.SetValue(vm.Context, cheats);
            if (weather != null) vm.Context.Blueprint?.Weather?.SetWeather(weather.Value);
            //the screen puts snow on the ground when it snows, once: this ground is new
            if (screen != null) SnowField.SetValue(screen, false);
            if (lot != null && selected != null)
            {
                var sim = vm.GetObjectById(selected.Value);
                if (sim is VMAvatar) lot.ActiveEntity = sim;
            }

            return PackParts.Restore(vm, moment.Packs);
        }
    }
}
