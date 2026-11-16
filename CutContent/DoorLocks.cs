/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CutContent
{
    /// <summary>
    /// Makes a locked door keep people out.
    ///
    /// The survey showed what Maxis left: "Interaction Lock/unlock" sets the door's attribute 0 to 1 (locked) or 0, on
    /// the part clicked and the next, and its check names the interaction Lock or Unlock from that attribute. Nothing
    /// reads it: the doors' portal functions check only things like a restroom's sex. This finishes it.
    ///
    /// When a Sim plans a route from room to room, the doors locked against them are left out, as the engine leaves out
    /// a door it already failed to get through. A locked door keeps out everyone but the household, their pets and the
    /// emergency services, and only on the way in: a door into the outdoors can always be used, so nobody is shut in.
    /// Only doors whose file has the lock script are affected, as other doors may use attribute 0 for something else.
    /// The lock is the door's own attribute, so it is saved with the lot.
    /// </summary>
    public static class DoorLocks
    {
        private const string LockScript = "Interaction Lock/unlock";

        /// <summary>Words in the names of the people a locked door never stops.</summary>
        private static readonly string[] Emergency = { "fire", "police", "cop", "grim", "social worker", "socialworker" };

        public static int TurnedAway;
        public static string Status => (TurnedAway == 0) ? "no one turned away yet" : $"{TurnedAway} route{(TurnedAway == 1 ? "" : "s")} kept from a locked door";

        private static ConditionalWeakTable<GameIffResource, object> Lockable = new ConditionalWeakTable<GameIffResource, object>();
        private static readonly object Yes = new object(), No = new object();

        /// <summary>Before a Sim plans a route from room to room.</summary>
        public static void BeforeRoomRoute(VMRoutingFrame __instance, HashSet<VMRoomPortal> ___IgnoredRooms)
        {
            try
            {
                var vm = __instance.VM;
                var sim = __instance.Caller as VMAvatar;
                if (vm == null || !vm.TS1 || sim == null || ___IgnoredRooms == null || MayPass(vm, sim)) return;

                var rooms = vm.Context.RoomInfo;
                var kept = false;
                for (int room = 0; room < rooms.Length; room++)
                {
                    var portals = rooms[room].Portals;
                    if (portals == null) continue;
                    foreach (var portal in portals)
                    {
                        if (portal.TargetRoom < rooms.Length && rooms[portal.TargetRoom].Room.IsOutside) continue; //leaving is always allowed
                        var door = vm.GetObjectById(portal.ObjectID);
                        if (door == null || !IsLocked(door)) continue;
                        if (___IgnoredRooms.Add(portal)) kept = true;
                    }
                }
                if (kept) TurnedAway++;
            }
            catch (Exception e)
            {
                CutContentPack.Log("door locks: " + e.Message);
            }
        }

        private static bool MayPass(VM vm, VMAvatar sim)
        {
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return true; //no household to lock anyone out for
            if (sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) == family.ChunkID) return true; //the household, and its pets
            var name = (sim.Object?.OBJ?.ChunkLabel ?? "").ToLowerInvariant();
            return Emergency.Any(x => name.Contains(x));
        }

        public static bool IsLocked(VMEntity door)
        {
            var parts = door.MultitileGroup?.Objects ?? new List<VMEntity> { door };
            if (!parts.Any(x => x.GetAttribute(0) != 0)) return false;
            var res = door.Object?.Resource;
            if (res == null) return false;
            if (!Lockable.TryGetValue(res, out var lockable))
            {
                lockable = (res.List<BHAV>()?.Any(x => (x.ChunkLabel ?? "").TrimEnd('\0') == LockScript) == true) ? Yes : No;
                Lockable.AddOrUpdate(res, lockable);
            }
            return lockable == Yes;
        }
    }
}
