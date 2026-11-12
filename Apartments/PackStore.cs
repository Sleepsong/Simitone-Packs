/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Apartments
{
    /// <summary>
    /// The pack's own file for a lot, kept beside the saves and not inside them.
    ///
    /// Simitone has a helper for exactly this (LotPackData), but only in its newest builds. A build without it runs
    /// the rest of this pack perfectly well, so the pack keeps its files itself, in the same place and under the same
    /// names the helper uses: LocalHouse/PackData/[pack]/[lot]-[hash of the lot's path].dat in the user directory.
    /// </summary>
    public static class PackStore
    {
        public static string PathFor(string packId, string lotPath)
        {
            //the lot's file name, to be readable, plus a hash of its whole path: different neighbourhoods reuse the names
            var full = Path.GetFullPath(lotPath).Replace('\\', '/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 12).ToLowerInvariant();
            var name = new string(Path.GetFileNameWithoutExtension(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var pack = new string(packId.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
            return Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "PackData", pack, name + "-" + hash + ".dat");
        }

        /// <summary>What was last written for this lot, or null if there is nothing (or it cannot be read).</summary>
        public static byte[] Read(string packId, string lotPath)
        {
            try
            {
                var path = PathFor(packId, lotPath);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e)
            {
                AptLog.Write("Could not read the apartment file for " + lotPath + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Keep this for the lot. Null removes what was there. Returns whether it worked.</summary>
        public static bool Write(string packId, string lotPath, byte[] data)
        {
            try
            {
                var path = PathFor(packId, lotPath);
                if (data == null)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
                return true;
            }
            catch (Exception e)
            {
                AptLog.Write("Could not save the apartment file for " + lotPath + ": " + e.Message);
                return false;
            }
        }
    }
}
