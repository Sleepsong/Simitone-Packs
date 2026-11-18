/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Undo
{
    /// <summary>
    /// The other packs' part of the lot being played: what each keeps for it, taken with each moment the lot is kept and
    /// handed back when the lot is put back to that moment.
    ///
    /// Simitone has no call for this, and this pack changes nothing in Simitone, so it is a convention between packs.
    /// Simitone already lets pack code see the packs in use and their running code (PackManager.Active, Pack.Code); a
    /// pack takes part by giving one of its ISimitonePack classes two public methods, found here by name:
    ///
    ///     public byte[] OnLotSnapshot(VM vm)            // what the pack has for the lot now, or null for nothing
    ///     public void OnLotRestore(VM vm, byte[] data)  // the lot has been put back to the moment of that snapshot
    ///
    /// Either alone is enough (a pack that keeps nothing, but holds on to objects, needs only OnLotRestore). A pack
    /// that keeps something for the lots it plays (it implements ISimitonePack's OnLotLoaded or OnLotSaved) and has
    /// neither is taken to have something it cannot put back, and no lot is put back while it is running.
    /// </summary>
    public static class PackParts
    {
        /// <summary>What the packs had for the lot at one moment, by pack id and class. A pack with nothing is not here.</summary>
        public class Parts
        {
            internal Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
            public long Size => Data.Values.Sum(x => (long)x.Length);
        }

        private class Roles
        {
            /// <summary>A class of the pack implements OnLotLoaded or OnLotSaved: it keeps something for the lots it plays.</summary>
            public bool ReactsToLots;
            /// <summary>A class of the pack has OnLotSnapshot or OnLotRestore.</summary>
            public bool TakesPart;
        }

        /// <summary>
        /// What the code of each pack seen running does with lots. Kept after its code is stopped: its patches of the
        /// engine go on running, and so may hold on to what it had.
        /// </summary>
        private static readonly Dictionary<string, Roles> Seen = new Dictionary<string, Roles>();
        /// <summary>Packs whose snapshot or restore failed here: they may no longer agree with the lot.</summary>
        private static readonly Dictionary<string, string> Failed = new Dictionary<string, string>();

        /// <summary>This pack's own classes are left out: it keeps nothing for a lot, and puts the rest back.</summary>
        private static bool IsMine(object code) => code.GetType().Assembly == typeof(PackParts).Assembly;

        public static MethodInfo SnapshotOf(Type type)
        {
            var method = type.GetMethod("OnLotSnapshot", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(VM) }, null);
            return (method != null && method.ReturnType == typeof(byte[])) ? method : null;
        }

        public static MethodInfo RestoreOf(Type type)
        {
            return type.GetMethod("OnLotRestore", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(VM), typeof(byte[]) }, null);
        }

        /// <summary>Whether a class has its own body for one of ISimitonePack's calls, rather than the interface's empty default.</summary>
        private static bool Implements(Type type, string method)
        {
            var map = type.GetInterfaceMap(typeof(ISimitonePack));
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name == method && map.TargetMethods[i].DeclaringType != typeof(ISimitonePack)) return true;
            }
            return false;
        }

        /// <summary>Look at the code of every pack that is running now, and remember what it does with lots.</summary>
        public static void Look()
        {
            foreach (var pack in PackManager.Active)
            {
                var code = pack.Code.Where(x => !IsMine(x)).ToList();
                if (code.Count == 0) continue;
                var roles = new Roles();
                foreach (var type in code.Select(x => x.GetType()))
                {
                    if (Implements(type, nameof(ISimitonePack.OnLotLoaded)) || Implements(type, nameof(ISimitonePack.OnLotSaved))) roles.ReactsToLots = true;
                    if (SnapshotOf(type) != null || RestoreOf(type) != null) roles.TakesPart = true;
                }
                Seen[pack.Id] = roles;
            }
        }

        /// <summary>
        /// The names of the packs in use that would no longer agree with the lot if it were put back to an earlier
        /// moment: their code keeps something for lots and has neither method, or it took part and has been stopped (so
        /// it can no longer be told), or one of its methods failed here. Empty when the lot can be put back.
        /// </summary>
        public static List<string> NotTakingPart()
        {
            Look();
            var result = new List<string>();
            foreach (var pack in PackManager.Active)
            {
                if (Failed.ContainsKey(pack.Id)) { result.Add(pack.DisplayName); continue; }
                if (!Seen.TryGetValue(pack.Id, out var roles)) continue; //its code has not been seen running: nothing of it is in the lot
                var stopped = pack.Code.Count(x => !IsMine(x)) == 0;
                if ((roles.ReactsToLots && !roles.TakesPart) || (stopped && (roles.ReactsToLots || roles.TakesPart))) result.Add(pack.DisplayName);
            }
            return result;
        }

        /// <summary>Why a pack is named by NotTakingPart, for the log.</summary>
        public static string Why(string packName)
        {
            var pack = PackManager.Active.FirstOrDefault(x => x.DisplayName == packName);
            if (pack == null) return packName;
            if (Failed.TryGetValue(pack.Id, out var how)) return packName + " (" + how + ")";
            if (pack.Code.Count(x => !IsMine(x)) == 0) return packName + " (its code has been stopped: " + pack.CodeError + ")";
            return packName + " (it keeps something for lots, and has neither OnLotSnapshot nor OnLotRestore)";
        }

        private static string KeyOf(Pack pack, object code) => pack.Id + "/" + code.GetType().FullName;

        private static void Fail(Pack pack, string doing, Exception e)
        {
            var inner = (e as TargetInvocationException)?.InnerException ?? e;
            Failed[pack.Id] = doing + " failed: " + inner.Message;
            UndoPack.Log("Pack " + pack.Id + ": " + doing + " failed, so undo is off from now on: " + inner);
        }

        /// <summary>What each pack has for the lot being played. A pack whose method fails is named by NotTakingPart from then on.</summary>
        public static Parts Snapshot(VM vm)
        {
            var parts = new Parts();
            foreach (var pack in PackManager.Active)
            {
                foreach (var code in pack.Code.ToList())
                {
                    if (IsMine(code)) continue;
                    var method = SnapshotOf(code.GetType());
                    if (method == null) continue;
                    try
                    {
                        //a copy: the pack may go on changing the array it gave
                        if (method.Invoke(code, new object[] { vm }) is byte[] data) parts.Data[KeyOf(pack, code)] = (byte[])data.Clone();
                    }
                    catch (Exception e)
                    {
                        Fail(pack, "keeping its part of the lot", e);
                    }
                }
            }
            return parts;
        }

        /// <summary>
        /// The lot has been put back to the moment of these parts: give each pack what it had then, in the order the
        /// packs are in use (each after the packs it needs). Every pack with OnLotRestore is told, with null if it had
        /// nothing, so that it lets go of what it has now. Each is given a copy, so the parts can be handed back again.
        /// Returns the names of the packs whose OnLotRestore failed: they may no longer agree with the lot.
        /// </summary>
        public static List<string> Restore(VM vm, Parts parts)
        {
            var failed = new List<string>();
            foreach (var pack in PackManager.Active)
            {
                foreach (var code in pack.Code.ToList())
                {
                    if (IsMine(code)) continue;
                    var method = RestoreOf(code.GetType());
                    if (method == null) continue;
                    byte[] data = null;
                    parts?.Data.TryGetValue(KeyOf(pack, code), out data);
                    try
                    {
                        method.Invoke(code, new object[] { vm, (byte[])data?.Clone() });
                    }
                    catch (Exception e)
                    {
                        Fail(pack, "putting its part of the lot back", e);
                        if (!failed.Contains(pack.DisplayName)) failed.Add(pack.DisplayName);
                    }
                }
            }
            return failed;
        }

        /// <summary>For the tests: forget what has been seen and what failed.</summary>
        public static void Forget()
        {
            Seen.Clear();
            Failed.Clear();
        }
    }
}
