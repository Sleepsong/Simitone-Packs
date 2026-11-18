/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Model;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.TS1Platform;
using FSO.SimAntics.NetPlay.Drivers;
using FSO.SimAntics.Utils;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LotsBin
{
    /// <summary>A reason the bin cannot do what was asked, to show to the player as it is.</summary>
    public class BinException : Exception
    {
        public BinException(string message) : base(message) { }
    }

    /// <summary>
    /// Copying a lot into the bin, and putting a copy down as another lot.
    ///
    /// A copy is the lot as it was last saved, in the game's own state (what Simitone keeps in a lot's file), so that
    /// everything goes with it: the ground's shape, pools, roofs, every object with its state. The XML blueprints the
    /// engine can also write (VMWorldExporter) keep only walls, floors and where objects stand, and are left alone.
    ///
    /// The Sims are taken off the copy in a VM of its own, with no world and nothing running, by the engine's own
    /// means: what each Sim is holding goes with them, and then VM.Reset takes off every Sim, as it does when a lot's
    /// saved state is loaded. The household's money and family go too. What the packs keep for the lot goes with the
    /// copy (PackData), and each pack decides what of its own goes.
    /// </summary>
    public static class LotCopy
    {
        /// <summary>Global 10 is the house number (TS1GameScreen saves a lot under it); 11 is set to it as a lot loads.</summary>
        private const int HouseGlobal = 10;
        private const int LoadedAsGlobal = 11;
        /// <summary>The selected Sim and the family playing: the household's, and a copy has none.</summary>
        private const int SelectedSimGlobal = 3;
        private const int FamilyGlobal = 9;

        // ---- into the bin ----

        /// <summary>
        /// Copy a house, as last saved, into the bin under a name. Throws BinException with a reason the player can read.
        /// Call from the neighbourhood screen, with no lot being played.
        /// </summary>
        public static BinLot Save(short house, string name)
        {
            var neigh = Content.Get().Neighborhood;
            var lotPath = neigh.GetHousePath(house);
            if (!File.Exists(lotPath)) throw new BinException("This lot has no file to copy.");
            var source = new IffFile(lotPath, true);

            var state = WithoutHousehold(source, house, out var simi, out var size);
            var lot = new IffFile();
            lot.AddChunk(MakeFSOV(state));
            AddSIMI(lot, simi);
            CopyPictures(source, lot);

            var nameDesc = neigh.GetHouseNameDesc(house);
            var info = new BinLotInfo()
            {
                Name = string.IsNullOrWhiteSpace(name) ? nameDesc.Item1 : name.Trim(),
                Description = nameDesc.Item2,
                Saved = DateTime.Now,
                FromNeighbourhood = NeighbourhoodName(neigh),
                FromHouse = house,
                Size = size,
                Value = simi.ObjectsValue + simi.ArchitectureValue,
            };
            var packs = PackData.Export(lotPath);
            var result = Bin.Add(info, ToBytes(lot), packs);
            LotsBinPack.Log("House " + house + " of " + info.FromNeighbourhood + " went into the bin as " + result.Folder
                + ", with data of " + (packs.Data.Count == 0 ? "no packs" : string.Join(", ", packs.Data.Keys)) + ".");
            return result;
        }

        /// <summary>
        /// The lot's saved state with nobody on it: the Sims, what they were holding, and the family gone. Also gives the
        /// lot's SIMI (its value worked out again, and the household's accounts left out) and its size.
        /// </summary>
        private static byte[] WithoutHousehold(IffFile source, short house, out SIMI simi, out int size)
        {
            var useWorld = VM.UseWorld;
            VM.UseWorld = false; //no lot is being played, and this VM has no world to draw: nothing may make one for it
            try
            {
                var vm = new VM(new VMContext(null), new VMServerDriver(new VMTS1GlobalLinkStub()), new VMNullHeadlineProvider());
                vm.Init();
                var fsov = source.List<FSOV>()?.FirstOrDefault();
                if (fsov != null)
                {
                    var saved = new VMMarshal();
                    using (var reader = new BinaryReader(new MemoryStream(fsov.Data))) saved.Deserialize(reader);
                    //as VM.Load does, but without running the objects' "client join" scripts: nothing on the copy runs
                    vm.LoadAsync(saved);
                    vm.FSOVClientJoin = false;
                    vm.LoadComplete();
                    if (vm.LoadErrors.Count > 0)
                        throw new BinException(vm.LoadErrors.Count + " of the things on this lot could not be loaded (custom content that is missing, "
                            + "or a pack that is switched off), and a copy would be without them.");
                }
                else
                {
                    //a lot Simitone has not saved yet is still in The Sims' own format: read it as the game does on entering it
                    new VMTS1ActivatorNew(vm, house).LoadFromIff(source);
                }
                if (vm.Context?.Architecture == null) throw new BinException("This lot's file could not be read.");

                RemoveHousehold(vm);
                vm.SetGlobalValue(SelectedSimGlobal, 0);
                vm.SetGlobalValue(FamilyGlobal, 0);

                var lotState = vm.TS1State;
                lotState.CurrentFamily = null;
                if (lotState.SimulationInfo == null) lotState.SimulationInfo = source.Get<SIMI>(1) ?? throw new BinException("This lot's file has no lot information (SIMI).");
                lotState.UpdateSIMI(vm);
                ClearAccounts(lotState.SimulationInfo);

                size = vm.Context.Architecture.Width;
                simi = lotState.SimulationInfo;
                using (var mem = new MemoryStream())
                {
                    vm.Save().SerializeInto(new BinaryWriter(mem));
                    return mem.ToArray();
                }
            }
            finally
            {
                VM.UseWorld = useWorld;
            }
        }

        /// <summary>
        /// Take the household off a lot: first what Sims hold (the most deeply held first, so nothing is dropped on the
        /// floor), then the Sims themselves, with the engine's VM.Reset.
        /// </summary>
        public static void RemoveHousehold(VM vm)
        {
            var held = new List<KeyValuePair<VMEntity, int>>();
            foreach (var entity in vm.Entities)
            {
                if (entity is VMAvatar) continue;
                var depth = 0;
                for (var holder = entity.Container; holder != null && depth < 32; holder = holder.Container)
                {
                    depth++;
                    if (holder is VMAvatar)
                    {
                        held.Add(new KeyValuePair<VMEntity, int>(entity, depth));
                        break;
                    }
                }
            }
            foreach (var entity in held.OrderByDescending(x => x.Value).Select(x => x.Key))
            {
                if (!entity.Dead) entity.Delete(true, vm.Context);
            }
            vm.Reset(); //deletes every Sim and pet on the lot (and any tiles reserved for one)
        }

        /// <summary>The household's money as the lot kept it, day by day: not the building's, so not the copy's.</summary>
        public static void ClearAccounts(SIMI simi)
        {
            if (simi.BudgetDays == null || simi.BudgetDays.Length != 6) simi.BudgetDays = new SIMI.SIMIBudgetDay[6];
            for (int i = 0; i < simi.BudgetDays.Length; i++) simi.BudgetDays[i] = new SIMI.SIMIBudgetDay();
        }

        // ---- out of the bin ----

        /// <summary>The families that live in a house (more than one, in an Apartments building).</summary>
        public static List<FAMI> LivingIn(TS1NeighborhoodProvider neigh, short house)
        {
            var families = neigh.MainResource?.List<FAMI>() ?? new List<FAMI>();
            var result = families.Where(x => x.HouseNumber == house).ToList();
            var family = neigh.GetFamilyForHouse(house);
            if (family != null && !result.Contains(family)) result.Add(family);
            return result;
        }

        /// <summary>
        /// Whether a lot from the bin can be put down as this house. Returns the reason it cannot, or null if it can.
        /// otherShape says it can, but the lot here faces another way or is laid out differently (where its road and
        /// pavement are), so the copy will not match the street around it.
        /// </summary>
        public static string CheckFit(BinLot lot, short house, out bool otherShape)
        {
            otherShape = false;
            var neigh = Content.Get().Neighborhood;
            if (LivingIn(neigh, house).Count > 0) return "Somebody lives here. Only a lot nobody lives on can be replaced.";
            var lotPath = neigh.GetHousePath(house);
            if (!File.Exists(lotPath)) return "This lot has no file to replace.";

            var here = ReadLayout(new IffFile(lotPath, true), house);
            var copy = ReadLayout(Bin.ReadLot(lot), (short)lot.Info.FromHouse);
            if (here.Size <= 0) return "The size of this lot could not be read.";
            if (copy.Size <= 0) return "The lot in the bin could not be read.";
            return FitProblem(copy, here, out otherShape);
        }

        /// <summary>A lot's size, and which of its tiles can be built on (where its road and pavement are not).</summary>
        public class Layout
        {
            public int Size;
            /// <summary>For each tile, true where it can NOT be built on (VMArchitecture's FineBuildableArea). Null if not known.</summary>
            public bool[] Unbuildable;
        }

        public static string FitProblem(Layout copy, Layout here, out bool otherShape)
        {
            otherShape = false;
            if (copy.Size != here.Size) return "This lot is " + here.Size + " tiles across, and the one in the bin is " + copy.Size + ". A lot can only go on a lot of the same size.";
            otherShape = copy.Unbuildable == null || here.Unbuildable == null || !copy.Unbuildable.SequenceEqual(here.Unbuildable);
            return null;
        }

        public static Layout ReadLayout(IffFile iff, short house)
        {
            var fsov = iff.List<FSOV>()?.FirstOrDefault();
            if (fsov != null)
            {
                var marshal = ReadState(fsov);
                var arch = marshal.Context?.Architecture;
                return new Layout() { Size = arch?.Width ?? 0, Unbuildable = arch?.FineBuildableArea };
            }
            //The Sims' own format: the size is a global, and the tiles that can be built on are marked in ARRY 8
            var simi = iff.Get<SIMI>(1);
            var size = (simi?.GlobalData != null && simi.GlobalData.Length > 23) ? simi.GlobalData[23] : 0;
            var flags = iff.Get<ARRY>(8)?.TransposeData;
            return new Layout() { Size = size, Unbuildable = (flags == null || size <= 0) ? null : VMTS1Activator.ResizeFlags(flags, size) };
        }

        /// <summary>
        /// Put a lot from the bin down as this house, in place of what is there (which is kept in the bin's Replaced
        /// folder first). Throws BinException with a reason the player can read. Call from the neighbourhood screen.
        /// </summary>
        public static void Place(BinLot lot, short house)
        {
            var problem = CheckFit(lot, house, out _);
            if (problem != null) throw new BinException(problem);

            var neigh = Content.Get().Neighborhood;
            var lotPath = neigh.GetHousePath(house);
            var here = new IffFile(lotPath, true);
            var copy = Bin.ReadLot(lot);
            var fsov = copy.List<FSOV>()?.FirstOrDefault() ?? throw new BinException("The lot in the bin has no saved state.");
            var simi = copy.Get<SIMI>(1) ?? throw new BinException("The lot in the bin has no lot information (SIMI).");

            var state = ReadState(fsov);
            Retarget(state, simi, house, here.Get<SIMI>(1));

            var result = new IffFile();
            result.AddChunk(MakeFSOV(WriteState(state)));
            AddSIMI(result, simi);
            CopyPictures(copy, result);
            var bytes = ToBytes(result); //all of it made before anything is changed

            var kept = Bin.KeepReplaced(lotPath, NeighbourhoodName(neigh), house);
            File.WriteAllBytes(lotPath, bytes);
            PackData.Import(lotPath, Bin.ReadPacks(lot));
            LotsBinPack.Log(lot.Folder + " was put down as house " + house + " of " + NeighbourhoodName(neigh)
                + (kept == null ? "." : "; what was there is kept in " + kept + "."));
        }

        /// <summary>
        /// Make a copy's state and SIMI those of this house: its number, the ground of where it is (snow, sand or grass),
        /// and the price of its land. Nobody's family, and no Sim selected.
        /// </summary>
        public static void Retarget(VMMarshal state, SIMI simi, short house, SIMI here)
        {
            var globals = state.GlobalState;
            globals[HouseGlobal] = house;
            globals[LoadedAsGlobal] = house;
            globals[SelectedSimGlobal] = 0;
            globals[FamilyGlobal] = 0;

            var terrain = state.Context.Architecture.Terrain;
            if (!VMTS1Activator.HouseNumToType.TryGetValue(house, out var ground)) ground = TerrainType.GRASS;
            terrain.LightType = (ground == TerrainType.SAND) ? TerrainType.GRASS : ground; //as the engine sets a lot's ground
            terrain.DarkType = ground;

            if (state.PlatformState is VMTS1LotState lotState)
            {
                lotState.CurrentFamily = null;
                if (lotState.SimulationInfo != null) RetargetSIMI(lotState.SimulationInfo, globals, here);
            }
            RetargetSIMI(simi, globals, here);
        }

        /// <summary>
        /// (The engine's SIMI writer does not write the globals it holds: it writes zeros. What a lot loads with are the
        /// globals in its state, so these are set for whatever reads the SIMI before it is written, and for the day the
        /// writer keeps them.)
        /// </summary>
        private static void RetargetSIMI(SIMI simi, short[] globals, SIMI here)
        {
            var data = new short[Math.Max(globals.Length, simi.GlobalData?.Length ?? 0)];
            if (simi.GlobalData != null) Array.Copy(simi.GlobalData, data, simi.GlobalData.Length);
            Array.Copy(globals, data, globals.Length);
            simi.GlobalData = data;
            if (here != null) simi.LotValue = here.LotValue; //the land is this lot's: its price goes with where it is
            ClearAccounts(simi);
        }

        // ---- files ----

        public static VMMarshal ReadState(FSOV fsov)
        {
            var state = new VMMarshal();
            using (var reader = new BinaryReader(new MemoryStream(fsov.Data))) state.Deserialize(reader);
            if (state.Context?.Architecture == null) throw new BinException("A lot's saved state could not be read.");
            return state;
        }

        public static byte[] WriteState(VMMarshal state)
        {
            using (var mem = new MemoryStream())
            {
                state.SerializeInto(new BinaryWriter(mem));
                return mem.ToArray();
            }
        }

        /// <summary>The chunk a lot's state is kept in, made as TS1GameScreen.Save makes it.</summary>
        public static FSOV MakeFSOV(byte[] state)
        {
            return new FSOV()
            {
                ChunkLabel = "Simitone Lot Data",
                ChunkID = 1,
                ChunkProcessed = true,
                ChunkType = "FSOV",
                AddedByPatch = true,
                Data = state
            };
        }

        private static void AddSIMI(IffFile iff, SIMI simi)
        {
            simi.ChunkID = 1;
            simi.ChunkType = "SIMI";
            simi.ChunkLabel ??= "";
            simi.ChunkProcessed = true;
            simi.AddedByPatch = true;
            iff.AddChunk(simi);
        }

        /// <summary>
        /// The pictures the neighbourhood screen shows of a lot: Simitone's PNGs, or The Sims' BMPs and their offsets
        /// (THMB), with the roof (513) and without (512). They are written out as they were read.
        /// </summary>
        public static void CopyPictures(IffFile from, IffFile to)
        {
            foreach (ushort id in new ushort[] { 512, 513 })
            {
                var png = from.Get<PNG>(id);
                if (png != null) Move(png, to);
                var bmp = from.Get<BMP>(id);
                if (bmp != null) Move(bmp, to);
            }
            var thumb = from.Get<THMB>(512);
            if (thumb != null && thumb.OriginalData != null) Move(thumb, to);
        }

        private static void Move(IffChunk chunk, IffFile to)
        {
            chunk.ChunkParent?.RemoveChunk(chunk);
            chunk.AddedByPatch = true;
            to.AddChunk(chunk);
        }

        public static byte[] ToBytes(IffFile iff)
        {
            using (var mem = new MemoryStream())
            {
                iff.Write(mem);
                return mem.ToArray();
            }
        }

        public static string NeighbourhoodName(TS1NeighborhoodProvider neigh)
        {
            var path = (neigh?.UserPath ?? "").TrimEnd('/', '\\');
            return path.Length == 0 ? "?" : Path.GetFileName(path);
        }
    }
}
