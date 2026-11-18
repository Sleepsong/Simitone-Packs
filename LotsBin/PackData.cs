/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LotsBin
{
    /// <summary>
    /// What the packs keep for a lot, taken from it to go with a copy of the lot and put down again with the copy as
    /// another lot.
    /// </summary>
    public class LotPackCopy
    {
        /// <summary>Each pack's data for the copy, by pack id. A pack with nothing to carry is not here.</summary>
        public Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();

        /// <summary>
        /// The packs the lot was last saved with (pack id -> name), as Simitone's LotPackRecord has them, so that the lot
        /// the copy is put down as warns of a missing pack just as the original would.
        /// </summary>
        public Dictionary<string, string> SavedWith = new Dictionary<string, string>();
    }

    /// <summary>
    /// The packs' data for a lot, moved with a copy of it. Everything here is the pack's own: Simitone is not changed.
    ///
    /// Simitone keeps a pack's data for a lot where LotPackData puts it (LocalHouse/PackData/[pack id]/[lot]-[hash].dat
    /// in the user directory), and which packs a lot was saved with in LotPackRecord's LocalHouse/lotpacks.json. Its
    /// LotPackData can read and write one pack's file, but cannot list or remove them all, and LotPackRecord cannot read
    /// or set a lot's record. So this finds them, and reads and writes the record, the same way Simitone does, as the
    /// Apartments pack also does. The tests check both against Simitone's own code.
    ///
    /// Each pack decides what of its own data goes with a copy, by having these public methods in its ISimitonePack
    /// class. They are found by name; neither is needed, and either may be left out:
    ///
    ///     public byte[] OnLotExport(string lotPath, byte[] saved)  // what the copy carries (null: nothing)
    ///     public byte[] OnLotImport(string lotPath, byte[] data)   // what the lot it is put down as keeps
    ///
    /// Without them the data goes as it is, which is right for data about what was built (painted ground, roofs,
    /// styles). The data of a pack whose code is not running goes as it is, so switching it on again finds it.
    /// </summary>
    public static class PackData
    {
        public const string ExportName = "OnLotExport";
        public const string ImportName = "OnLotImport";

        private static string Root => Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "PackData");
        private static string RecordPath => Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "lotpacks.json");

        /// <summary>A lot's file name in each pack's folder: as LotPackData names it.</summary>
        public static string FileFor(string lotPath)
        {
            //the lot's file name, to be readable, plus a hash of its whole path: different neighbourhoods reuse the names
            var full = Path.GetFullPath(lotPath).Replace('\\', '/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 12).ToLowerInvariant();
            var name = new string(Path.GetFileNameWithoutExtension(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return name + "-" + hash + ".dat";
        }

        /// <summary>The ids of the packs that have data kept for this lot, whether or not they are installed and on.</summary>
        public static List<string> PacksWithData(string lotPath)
        {
            var result = new List<string>();
            try
            {
                if (!Directory.Exists(Root)) return result;
                var file = FileFor(lotPath);
                foreach (var dir in Directory.GetDirectories(Root).OrderBy(x => x, StringComparer.Ordinal))
                {
                    //(a pack id is letters, digits, '.', '_' and '-', so its folder has the id's own name)
                    var id = Path.GetFileName(dir);
                    if (Bin.PackIdIsSafe(id) && File.Exists(Path.Combine(dir, file))) result.Add(id);
                }
            }
            catch (Exception e)
            {
                LotsBinPack.Log("Could not list the packs' data for " + lotPath + ": " + e.Message);
            }
            return result;
        }

        /// <summary>Remove every pack's data for this lot.</summary>
        public static void DeleteAll(string lotPath)
        {
            foreach (var pack in PacksWithData(lotPath)) LotPackData.Write(pack, lotPath, null);
        }

        // ---- which packs a lot was saved with (LotPackRecord's file) ----

        private static string Key(string lotPath) => Path.GetFullPath(lotPath).Replace('\\', '/');

        private static Dictionary<string, Dictionary<string, string>> ReadRecord()
        {
            try
            {
                if (File.Exists(RecordPath))
                    return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(RecordPath))
                        ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch (Exception e)
            {
                LotsBinPack.Log("Could not read lotpacks.json: " + e.Message);
            }
            return new Dictionary<string, Dictionary<string, string>>();
        }

        /// <summary>The packs this lot was last saved with (pack id -> name). Empty if none are recorded.</summary>
        public static Dictionary<string, string> SavedWith(string lotPath)
        {
            return ReadRecord().TryGetValue(Key(lotPath), out var packs) && packs != null
                ? new Dictionary<string, string>(packs) : new Dictionary<string, string>();
        }

        /// <summary>Record the packs this lot's contents were saved with. Empty or null records none.</summary>
        public static void SetSavedWith(string lotPath, Dictionary<string, string> packs)
        {
            try
            {
                var record = ReadRecord();
                if (packs != null && packs.Count > 0) record[Key(lotPath)] = new Dictionary<string, string>(packs);
                else if (!record.Remove(Key(lotPath))) return; //nothing to record and nothing recorded: leave no file behind
                Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
                File.WriteAllText(RecordPath, JsonSerializer.Serialize(record, new JsonSerializerOptions() { WriteIndented = true }));
            }
            catch (Exception e)
            {
                LotsBinPack.Log("Could not save lotpacks.json: " + e.Message);
            }
        }

        /// <summary>Names of the packs in a record that are not in use now. Empty when nothing is missing.</summary>
        public static List<string> Missing(Dictionary<string, string> savedWith)
        {
            if (savedWith == null) return new List<string>();
            return savedWith.Where(x => !PackManager.IsActive(x.Key)).Select(x => x.Value ?? x.Key).OrderBy(x => x).ToList();
        }

        // ---- the copy ----

        /// <summary>
        /// Run a pack's code over its data for a copy, each of its classes in turn, through the method of that name if it
        /// has one. If one fails, the data goes on as it came in: what was built is never dropped for that. (The pack's
        /// code is not stopped: that is Simitone's to do, not another pack's.)
        /// </summary>
        private static byte[] PassThrough(Pack pack, string method, string lotPath, byte[] data)
        {
            if (pack == null || pack.Code.Count == 0) return data;
            var result = data;
            try
            {
                foreach (var code in pack.Code.ToList())
                {
                    var call = code.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(byte[]) }, null);
                    if (call == null || call.ReturnType != typeof(byte[])) continue;
                    result = (byte[])call.Invoke(code, new object[] { lotPath, result });
                }
                return result;
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                LotsBinPack.Log("The pack " + pack.Id + " failed in " + method + " for " + lotPath + ", so its data goes as it was: " + inner);
                return data;
            }
        }

        /// <summary>
        /// Each pack's data for a lot as last saved, to go with a copy of it. A pack whose code is running chooses what
        /// goes (OnLotExport); the data of any other pack goes as it is.
        /// </summary>
        public static LotPackCopy Export(string lotPath)
        {
            var copy = new LotPackCopy() { SavedWith = SavedWith(lotPath) };
            var ids = new HashSet<string>(PacksWithData(lotPath));
            foreach (var pack in PackManager.Active.Where(x => x.Code.Count > 0 && Bin.PackIdIsSafe(x.Id))) ids.Add(pack.Id);
            foreach (var id in ids.OrderBy(x => x, StringComparer.Ordinal))
            {
                var pack = PackManager.Active.FirstOrDefault(x => x.Id == id);
                var data = PassThrough(pack, ExportName, lotPath, LotPackData.Read(id, lotPath));
                if (data != null) copy.Data[id] = data;
            }
            return copy;
        }

        /// <summary>
        /// A copy has been put down as this lot (its file is written): the copy's pack data becomes this lot's, in place of
        /// everything the packs had for it. Every pack whose code is running is told (OnLotImport), whether or not the copy
        /// has data for it.
        /// </summary>
        public static void Import(string lotPath, LotPackCopy copy)
        {
            DeleteAll(lotPath);
            var ids = new HashSet<string>(copy?.Data?.Keys.Where(Bin.PackIdIsSafe) ?? Enumerable.Empty<string>());
            foreach (var pack in PackManager.Active.Where(x => x.Code.Count > 0 && Bin.PackIdIsSafe(x.Id))) ids.Add(pack.Id);
            foreach (var id in ids.OrderBy(x => x, StringComparer.Ordinal))
            {
                byte[] carried = null;
                copy?.Data?.TryGetValue(id, out carried);
                var pack = PackManager.Active.FirstOrDefault(x => x.Id == id);
                var data = PassThrough(pack, ImportName, lotPath, carried);
                if (data != null) LotPackData.Write(id, lotPath, data);
            }
            SetSavedWith(lotPath, copy?.SavedWith);
        }
    }
}
