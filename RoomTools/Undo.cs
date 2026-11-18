/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using FSO.SimAntics;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoomTools
{
    /// <summary>
    /// Undo and redo in build and buy mode, as in The Sims 4: Ctrl+Z steps back through the changes made since build or
    /// buy mode was opened, Ctrl+Y (or Ctrl+Shift+Z) forward again. Leaving for live mode forgets them.
    ///
    /// Each step is the whole lot as the game saves it (VM.Save), taken just before a change, with the family's money,
    /// which the game keeps outside the lot. Undoing puts the lot back as the game loads one (VM.Load). The game stands
    /// still in build and buy mode, so nothing else has moved on in between.
    ///
    /// Some packs keep things of their own beside the lot's objects (Terrain Paint's paint, Roof Tool's roofs,
    /// Foundations, Create a Style's colours), which a lot loaded afresh would not have. They already save that with each
    /// lot, and read it back when one loads, so each step asks those packs to do the same with a file of the step's own.
    /// Only those four: another pack's code is not called on a lot that is not really being saved or loaded.
    /// </summary>
    public static class Undo
    {
        /// <summary>The most steps kept. Each is a few hundred kilobytes for a furnished lot.</summary>
        public const int Most = 40;
        private const long MostBytes = 160L << 20;

        private class Step
        {
            public byte[] Lot;
            public int? Budget;
            public short? Weather;
            public string PackPath;
            public string What;
        }

        private static readonly List<Step> Back = new List<Step>();
        private static readonly List<Step> Forward = new List<Step>();
        private static WeakReference<VM> Owner = new WeakReference<VM>(null);
        private static int Serial;

        /// <summary>Called before a step back or forward puts the lot back (to put tools down) and after (to pick them up again).</summary>
        public static Action BeforeRestore, AfterRestore;

        public static string UndoName => Back.Count > 0 ? Back[Back.Count - 1].What : null;
        /// <summary>A change has been sent and hasn't happened yet: stepping now would put the lot back under it.</summary>
        public static bool Busy => Pending != null;
        public static string RedoName => Forward.Count > 0 ? Forward[Forward.Count - 1].What : null;

        private static bool OwnedBy(VM vm)
        {
            if (vm == null) return false;
            if (Owner.TryGetTarget(out var owner) && owner == vm) return true;
            Clear(); //another lot (or the same one, loaded again): what was kept is not its history
            Owner = new WeakReference<VM>(vm);
            return true;
        }

        /// <summary>Forget every step (leaving build mode, or the lot).</summary>
        public static void Clear()
        {
            if (Pending != null) { Forget(Pending); Pending = null; }
            foreach (var s in Back) Forget(s);
            foreach (var s in Forward) Forget(s);
            Back.Clear();
            Forward.Clear();
        }

        // ---- taking and putting back a lot ----

        private static Step Take(VM vm, string what)
        {
            var marshal = Hiding.AsItIs(() => vm.Save());
            byte[] lot;
            using (var mem = new MemoryStream())
            {
                marshal.SerializeInto(new BinaryWriter(mem));
                lot = mem.ToArray();
            }
            var step = new Step
            {
                Lot = lot,
                Budget = vm.TS1State?.CurrentFamily?.Budget,
                Weather = vm.Context.Blueprint?.Weather?.WeatherData,
                PackPath = Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "RoomTools", "roomtools-undo-" + (++Serial) + ".iff"),
                What = what,
            };
            PackData(vm, step.PackPath, true);
            return step;
        }

        private static void Restore(VM vm, Step step)
        {
            var marshal = new VMMarshal();
            using (var reader = new BinaryReader(new MemoryStream(step.Lot))) marshal.Deserialize(reader);
            vm.Load(marshal);
            var family = vm.TS1State?.CurrentFamily;
            if (family != null && step.Budget != null) family.Budget = step.Budget.Value;
            //a TS1 lot's weather is the game screen's to set, not the lot's: what was falling goes on falling
            var weather = vm.Context.Blueprint?.Weather;
            if (weather != null && step.Weather != null)
            {
                weather.SetWeather(step.Weather.Value);
                var terrain = vm.Context.Blueprint.Terrain;
                if (terrain != null && weather.WeatherType == FSO.LotView.Model.WeatherType.Snow && weather.WeatherIntensity > 0.1f)
                {
                    terrain.ForceSnow(0);
                    terrain.UpdateLotType();
                    terrain.TerrainDirty = true;
                }
            }
            PackData(vm, step.PackPath, false);
        }

        private static void Forget(Step step)
        {
            foreach (var id in Keeping)
            {
                try { ForgetData(id, step.PackPath); }
                catch { }
            }
        }

        // ---- other packs' data ----

        /// <summary>The packs that keep things of their own beside a lot's objects, which a lot loaded afresh would lose.</summary>
        private static readonly string[] Keeping = { "sample.terrainpaint", "sample.rooftool", "sample.foundations", "sample.createastyle" };

        private static void PackData(VM vm, string path, bool saving)
        {
            List<Pack> active;
            try { active = PackManager.Active.ToList(); }
            catch { return; }
            foreach (var pack in active)
            {
                if (!Keeping.Contains(pack.Id)) continue;
                foreach (var code in pack.Code.ToList())
                {
                    try
                    {
                        if (saving) CallSaved(code, vm, path);
                        else CallLoaded(code, vm, path);
                    }
                    catch (Exception e)
                    {
                        RoomToolsPack.Log("Could not " + (saving ? "keep " : "put back ") + pack.Id + "'s part of the lot for undo: " + e.Message);
                    }
                }
            }
        }

        //each in a method of its own: on a Simitone without these calls, the failure is caught where it is made
        private static void CallSaved(ISimitonePack code, VM vm, string path) => code.OnLotSaved(vm, path);
        private static void CallLoaded(ISimitonePack code, VM vm, string path) => code.OnLotLoaded(vm, path);
        private static void ForgetData(string id, string path) => LotPackData.Write(id, path, null);

        /// <summary>Remove the files that steps from an earlier run left beside the saves (the game was closed in build mode).</summary>
        public static void TidyUp()
        {
            try
            {
                var root = Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "PackData");
                if (!Directory.Exists(root)) return;
                foreach (var dir in Directory.GetDirectories(root))
                    foreach (var file in Directory.GetFiles(dir, "roomtools_undo_*.dat"))
                        File.Delete(file);
            }
            catch { }
        }

        private static void Push(Step step)
        {
            Back.Add(step);
            foreach (var s in Forward) Forget(s);
            Forward.Clear();
            while (Back.Count > Most || (Back.Count > 1 && Back.Sum(x => (long)x.Lot.Length) > MostBytes))
            {
                Forget(Back[0]);
                Back.RemoveAt(0);
            }
        }

        // ---- the game's own build and buy tools ----

        private static Step Pending;
        private static int PendingFrames;
        private static long PendingPrint;
        private static bool BuiltSince;

        /// <summary>Kinds of command the game's build and buy tools send. A pack's own command (a type from outside the engine) counts too.</summary>
        private static readonly HashSet<Type> Changes = new HashSet<Type>
        {
            typeof(VMNetArchitectureCmd), typeof(VMNetBuyObjectCmd), typeof(VMNetMoveObjectCmd), typeof(VMNetDeleteObjectCmd),
            typeof(VMNetSetRoofCmd), typeof(VMNetPlaceInventoryCmd), typeof(VMNetSendToInventoryCmd), typeof(VMNetBatchGraphicCmd),
            typeof(VMNetChangeLotSizeCmd),
        };

        private static string Describe(VMNetCommandBodyAbstract cmd)
        {
            switch (cmd)
            {
                case VMNetBuyObjectCmd _: return "buying";
                case VMNetMoveObjectCmd _: return "moving an object";
                case VMNetDeleteObjectCmd _: return "selling";
                case VMNetArchitectureCmd _: return "building";
                case VMNetSetRoofCmd _: return "the roof";
                default: return "the change";
            }
        }

        /// <summary>A command is about to be sent. If it is a build or buy mode change, keep the lot as it was before it.</summary>
        public static void BeforeCommand(VM vm, VMNetCommandBodyAbstract cmd)
        {
            if (cmd == null || !RoomToolsPack.InBuildOrBuy(vm)) return;
            var type = cmd.GetType();
            if (!Changes.Contains(type) && type.Assembly == typeof(VMNetCommandBodyAbstract).Assembly) return;
            if (!OwnedBy(vm)) return;
            if (Pending != null) return; //sent together: one step
            Pending = Take(vm, Describe(cmd));
            PendingPrint = Fingerprint(vm);
            PendingFrames = 0;
            BuiltSince = false;
        }

        /// <summary>The engine has really built something (not a tool's preview of it).</summary>
        public static void Built() { if (Pending != null) BuiltSince = true; }

        /// <summary>
        /// Each frame: once the command has done what it did, the step before it is kept. A command that changed nothing
        /// (a purchase that could not be paid for) keeps no step.
        /// </summary>
        public static void Frame(VM vm)
        {
            if (Pending == null) return;
            if (!Owner.TryGetTarget(out var owner) || owner != vm) return;
            if (BuiltSince || Fingerprint(vm) != PendingPrint)
            {
                Push(Pending);
                Pending = null;
            }
            else if (++PendingFrames > 90)
            {
                Forget(Pending);
                Pending = null;
            }
        }

        /// <summary>Enough of the lot to tell whether it has changed: its walls, floors, ground, roof, objects and money.</summary>
        private static long Fingerprint(VM vm)
        {
            unchecked
            {
                long h = 17;
                void Mix(long v) { h = h * 31 + v; }
                var arch = vm.Context.Architecture;
                for (int l = 0; l < arch.Stories; l++)
                {
                    var walls = arch.Walls[l];
                    var floors = arch.Floors[l];
                    for (int i = 0; i < walls.Length; i++)
                    {
                        var w = walls[i];
                        if (w.Segments != 0) Mix(i * 7 + (int)w.Segments + (w.TopLeftPattern << 8) + ((long)w.TopRightPattern << 24) + ((long)w.BottomLeftPattern << 40)
                            + ((long)w.BottomRightPattern << 48) + w.TopLeftStyle * 13 + w.TopRightStyle * 17);
                        if (floors[i].Pattern != 0) Mix(i * 11 + floors[i].Pattern);
                    }
                }
                var t = arch.Terrain;
                for (int i = 0; i < t.Heights.Length; i++) Mix(t.Heights[i] * 3 + t.GrassState[i]);
                Mix(arch.RoofStyle); Mix((long)(arch.RoofPitch * 1000));
                foreach (var e in vm.Entities)
                {
                    Mix(e.ObjectID); Mix(e.Object.OBJ.GUID); Mix(e.Position.x); Mix(e.Position.y); Mix(e.Position.Level);
                    Mix((int)e.Direction); Mix(e.Container?.ObjectID ?? -1); Mix(e.ContainerSlot);
                    Mix(e.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.Graphic));
                }
                Mix(vm.TS1State?.CurrentFamily?.Budget ?? 0);
                return h;
            }
        }

        // ---- Room Tools' own changes ----

        /// <summary>A change of Room Tools' own is about to be made: keep the lot as it is.</summary>
        public static object Begin(VM vm, string what)
        {
            OwnedBy(vm);
            Frame(vm);
            if (Pending != null) { Push(Pending); Pending = null; }
            return Take(vm, what);
        }

        /// <summary>The change was made: it can be undone.</summary>
        public static void Done(object step) { if (step is Step s) Push(s); }

        /// <summary>The change went wrong part way: put the lot back as it was, and keep no step for it.</summary>
        public static void Failed(VM vm, object step)
        {
            if (!(step is Step s)) return;
            BeforeRestore?.Invoke();
            try { Restore(vm, s); }
            finally
            {
                Forget(s);
                AfterRestore?.Invoke();
            }
        }

        // ---- stepping back and forward ----

        /// <summary>Undo the last change. Returns what was undone, or null if there was nothing to.</summary>
        public static string StepBack(VM vm) => Move(vm, Back, Forward);

        /// <summary>Redo the last change undone. Returns what was redone, or null if there was nothing to.</summary>
        public static string StepForward(VM vm) => Move(vm, Forward, Back);

        private static string Move(VM vm, List<Step> from, List<Step> to)
        {
            if (!OwnedBy(vm)) return null;
            Frame(vm);
            if (Pending != null) return null; //(the caller says to wait: see Busy)
            if (from.Count == 0) return null;
            var step = from[from.Count - 1];
            BeforeRestore?.Invoke();
            try
            {
                var now = Take(vm, step.What);
                Restore(vm, step);
                from.RemoveAt(from.Count - 1);
                Forget(step);
                to.Add(now);
            }
            finally { AfterRestore?.Invoke(); }
            return step.What;
        }
    }
}
