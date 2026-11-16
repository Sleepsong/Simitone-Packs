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

        /// <summary>Set with "cutcontent locks everyone": locked doors keep out the household and its pets as well. Lasts until the game closes.</summary>
        public static bool Everyone;

        private static HashSet<(short sim, short door)> Logged = new HashSet<(short, short)>();

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
                        if (Logged.Add((sim.ObjectID, door.ObjectID)))
                            CutContentPack.Log($"locked door: {door} is left out of {sim.Name}'s routes");
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
            var name = (sim.Object?.OBJ?.ChunkLabel ?? "").ToLowerInvariant();
            if (Emergency.Any(x => name.Contains(x))) return true;
            if (Everyone) return false;
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return true; //no household to lock anyone out for
            return sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) == family.ChunkID; //the household, and its pets
        }

        /// <summary>
        /// The check for the menu entry. Maxis's "Interaction Lock/unlock TEST" first calls global 323, "Trigger Prude Finger -
        /// 1.4", a Hot Date routine for the old prude that has nothing to do with doors, and returns false when it does. Its
        /// other three steps name the entry Lock or Unlock from attribute 0. This is those three steps without the first.
        /// If the check is not as the survey found it, Maxis's is used as it is.
        /// </summary>
        public static BHAV MakeCheck(GameIffResource res, BHAV action)
        {
            if (Checks.TryGetValue(res, out var made)) return made;
            var test = res.List<BHAV>()?.FirstOrDefault(x => (x.ChunkLabel ?? "").TrimEnd('\0') == LockScript + " TEST");
            if (test == null) return null;
            var steps = test.Instructions;
            BHAV check = test;
            if (steps.Length == 4 && steps[0].Opcode == 323 && steps[0].TruePointer == 1)
            {
                var kept = new BHAVInstruction[3];
                for (int i = 1; i < 4; i++)
                {
                    var step = steps[i];
                    kept[i - 1] = new BHAVInstruction { Opcode = step.Opcode, Operand = step.Operand, TruePointer = Back(step.TruePointer), FalsePointer = Back(step.FalsePointer) };
                }
                check = Scripts.Add(res, LockScript + " TEST (Cut Content)", kept, test);
                CutContentPack.Log($"door locks: check script {check.ChunkID} in {res.MainIff?.Filename} is Maxis's without the call to global 323");
            }
            else CutContentPack.Log($"door locks: the check in {res.MainIff?.Filename} is not as the survey found it, so Maxis's is used as it is");
            Checks.AddOrUpdate(res, check);
            return check;
        }

        private static ConditionalWeakTable<GameIffResource, BHAV> Checks = new ConditionalWeakTable<GameIffResource, BHAV>();

        private static byte Back(byte pointer) => (pointer < 253) ? (byte)(pointer - 1) : pointer;

        /// <summary>For "cutcontent doors": every door on the lot that can be locked, and whether it is.</summary>
        public static string Report(VM vm)
        {
            if (vm == null) return "Not in a lot.";
            var lines = new List<string>();
            var doors = vm.Entities.Where(x => x is VMGameObject && x.Object?.OBJ?.ObjectType == OBJDType.Portal && x.MultitileGroup != null)
                .Select(x => x.MultitileGroup).Distinct().ToList();
            int lockable = 0, locked = 0;
            foreach (var group in doors)
            {
                var part = group.Objects.FirstOrDefault();
                if (part == null) continue;
                var res = part.Object?.Resource;
                var canLock = res?.List<BHAV>()?.Any(x => (x.ChunkLabel ?? "").TrimEnd('\0') == LockScript) == true;
                if (!canLock) continue;
                lockable++;
                var isLocked = IsLocked(part);
                if (isLocked) locked++;
                lines.Add($"{(string.IsNullOrEmpty(group.Name) ? part.ToString() : group.Name)}: {(isLocked ? "locked" : "unlocked")} (attribute 0 of its parts: {string.Join(", ", group.Objects.Select(x => x.GetAttribute(0)))})");
            }
            var who = Everyone ? "everyone but the emergency services" : "everyone but the household, its pets and the emergency services";
            lines.Insert(0, $"{lockable} door{(lockable == 1 ? "" : "s")} on this lot can be locked, {locked} {(locked == 1 ? "is" : "are")} locked. Locked doors keep out {who}, on the way in. {Status}.");
            if (lockable == 0) lines.Add("Only doors from Vacation, Unleashed, Superstar and Makin' Magic have Maxis's lock: the base game's and Livin' Large's and Hot Date's doors do not.");
            return string.Join("\n", lines);
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
