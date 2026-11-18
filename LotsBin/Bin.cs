/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using FSO.Files.Formats.IFF;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LotsBin
{
    /// <summary>What the bin knows about a lot in it, kept in its lot.json.</summary>
    public class BinLotInfo
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime Saved { get; set; }
        /// <summary>The neighbourhood's folder (eg. UserData2) and the house number the lot was copied from.</summary>
        public string FromNeighbourhood { get; set; }
        public int FromHouse { get; set; }
        /// <summary>Tiles along each side of the lot. It can only be put down on a lot of the same size.</summary>
        public int Size { get; set; }
        /// <summary>What the building and its things were worth when it was copied.</summary>
        public int Value { get; set; }
        /// <summary>The packs the lot was saved with (pack id -> name).</summary>
        public Dictionary<string, string> SavedWith { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// A lot in the bin: a folder in (user directory)/LotsBin/ that holds
    ///
    ///   lot.iff    the lot as it was last saved, without its Sims: the game's state (an FSOV chunk, as in Simitone's
    ///              own saves), its SIMI, and its pictures for the neighbourhood screen
    ///   lot.json   what the bin shows of it (BinLotInfo)
    ///   packs/     each pack's data for it, a file for each pack, named by the pack's id
    ///
    /// A folder can be copied to another computer's LotsBin folder as it is.
    /// </summary>
    public class BinLot
    {
        public string Folder;
        public BinLotInfo Info;

        public string LotFile => Path.Combine(Folder, "lot.iff");
        public string Name => string.IsNullOrWhiteSpace(Info?.Name) ? Path.GetFileName(Folder) : Info.Name;
    }

    public static class Bin
    {
        public static string Dir => Path.Combine(FSOEnvironment.UserDir, "LotsBin");
        /// <summary>Where a house is kept before a lot from the bin replaces it, in case it is wanted back.</summary>
        public static string ReplacedDir => Path.Combine(Dir, "Replaced");

        private const string InfoName = "lot.json";
        private const string PacksName = "packs";
        private static JsonSerializerOptions JsonOptions = new JsonSerializerOptions() { WriteIndented = true };

        /// <summary>The lots in the bin, newest first. A folder that cannot be read is left out (and written to the log).</summary>
        public static List<BinLot> All()
        {
            var result = new List<BinLot>();
            if (!Directory.Exists(Dir)) return result;
            foreach (var folder in Directory.GetDirectories(Dir))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith(".") || name == "Replaced") continue;
                var lot = Read(folder);
                if (lot != null) result.Add(lot);
            }
            return result.OrderByDescending(x => x.Info.Saved).ThenBy(x => x.Folder, StringComparer.Ordinal).ToList();
        }

        public static BinLot Read(string folder)
        {
            try
            {
                var info = JsonSerializer.Deserialize<BinLotInfo>(File.ReadAllText(Path.Combine(folder, InfoName)));
                if (info == null) throw new Exception("its lot.json is empty");
                if (info.Version > BinLotInfo.CurrentVersion) throw new Exception("it was saved by a newer Lots Bin (version " + info.Version + ")");
                info.SavedWith ??= new Dictionary<string, string>();
                var lot = new BinLot() { Folder = folder, Info = info };
                if (!File.Exists(lot.LotFile)) throw new Exception("it has no lot.iff");
                return lot;
            }
            catch (Exception e)
            {
                LotsBinPack.Log("Skipped " + folder + " in the bin: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Put a lot in the bin. The folder is written under another name first and renamed when it is complete, so a lot
        /// that failed half way is never in the bin.
        /// </summary>
        public static BinLot Add(BinLotInfo info, byte[] lotIff, LotPackCopy packs)
        {
            //the packs the lot was saved with are the copy's: lot.json keeps them, for the bin to show
            if (packs?.SavedWith != null) info.SavedWith = new Dictionary<string, string>(packs.SavedWith);
            info.SavedWith ??= new Dictionary<string, string>();
            Directory.CreateDirectory(Dir);
            var temp = Path.Combine(Dir, ".new-" + Guid.NewGuid().ToString("n"));
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllBytes(Path.Combine(temp, "lot.iff"), lotIff);
                WritePacks(temp, packs);
                File.WriteAllText(Path.Combine(temp, InfoName), JsonSerializer.Serialize(info, JsonOptions));

                var stem = info.Saved.ToString("yyyyMMdd-HHmmss") + "-" + SafeName(info.Name);
                var folder = Path.Combine(Dir, stem);
                for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(Dir, stem + "-" + i);
                Directory.Move(temp, folder);
                return new BinLot() { Folder = folder, Info = info };
            }
            catch
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
                throw;
            }
        }

        public static void Delete(BinLot lot)
        {
            Directory.Delete(lot.Folder, true);
        }

        /// <summary>Change what the bin calls a lot.</summary>
        public static void Rename(BinLot lot, string name)
        {
            lot.Info.Name = name;
            File.WriteAllText(Path.Combine(lot.Folder, InfoName), JsonSerializer.Serialize(lot.Info, JsonOptions));
        }

        /// <summary>The lot's file, with its chunks' data kept so that the pictures can be written out again as they are.</summary>
        public static IffFile ReadLot(BinLot lot)
        {
            return new IffFile(lot.LotFile, true);
        }

        public static LotPackCopy ReadPacks(BinLot lot)
        {
            var copy = new LotPackCopy() { SavedWith = new Dictionary<string, string>(lot.Info.SavedWith) };
            var dir = Path.Combine(lot.Folder, PacksName);
            if (!Directory.Exists(dir)) return copy;
            foreach (var file in Directory.GetFiles(dir, "*.dat"))
            {
                copy.Data[Path.GetFileNameWithoutExtension(file)] = File.ReadAllBytes(file);
            }
            return copy;
        }

        private static void WritePacks(string folder, LotPackCopy packs)
        {
            if (packs == null || packs.Data.Count == 0) return;
            var dir = Path.Combine(folder, PacksName);
            Directory.CreateDirectory(dir);
            foreach (var pack in packs.Data)
            {
                //(a pack id is letters, digits, '.', '_' and '-': safe as a file name. anything else is not a pack's)
                if (!PackIdIsSafe(pack.Key)) continue;
                File.WriteAllBytes(Path.Combine(dir, pack.Key + ".dat"), pack.Value);
            }
        }

        public static bool PackIdIsSafe(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= 64 && id != "." && id != ".."
                && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-');
        }

        /// <summary>A name that can be part of a folder's name on any computer.</summary>
        public static string SafeName(string name)
        {
            var safe = new string((name ?? "").Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_').ToArray()).Trim();
            if (safe.Length > 40) safe = safe.Substring(0, 40).Trim();
            return safe.Length == 0 ? "lot" : safe;
        }

        /// <summary>
        /// Keep a copy of a house's file, and of the packs' data for it, before a lot from the bin replaces it.
        /// Returns the folder, or null if there was nothing to keep.
        /// </summary>
        public static string KeepReplaced(string lotPath, string neighbourhood, int house)
        {
            if (!File.Exists(lotPath)) return null;
            var stem = Path.Combine(ReplacedDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + SafeName(neighbourhood) + "-House" + house.ToString("00"));
            var folder = stem;
            for (int i = 2; Directory.Exists(folder); i++) folder = stem + "-" + i;
            Directory.CreateDirectory(folder);
            File.Copy(lotPath, Path.Combine(folder, Path.GetFileName(lotPath)));
            foreach (var pack in PackData.PacksWithData(lotPath))
            {
                var data = LotPackData.Read(pack, lotPath);
                if (data == null || !PackIdIsSafe(pack)) continue;
                Directory.CreateDirectory(Path.Combine(folder, PacksName));
                File.WriteAllBytes(Path.Combine(folder, PacksName, pack + ".dat"), data);
            }
            return folder;
        }
    }
}
