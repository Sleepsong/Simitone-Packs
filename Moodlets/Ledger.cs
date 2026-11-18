/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Moodlets
{
    public class Settings
    {
        /// <summary>
        /// Whether moodlets count. Off, they add nothing to mood, no new ones are given, and those Sims have stay as
        /// they are until it is on again.
        /// </summary>
        public bool On { get; set; } = true;
    }

    /// <summary>
    /// The pack's own file for a neighbourhood: each Sim's moodlets, and the player's settings. It is kept beside
    /// Simitone's saves and not inside them, so the game's files stay as they were.
    ///
    /// As with the Life Stages pack, the moodlets are written only when the game saves, and read again when the game
    /// loads a neighbourhood or a lot is left: leave a lot without saving, and what happened there is forgotten with
    /// it. A trip off the lot (Downtown, on Vacation) is not leaving it: the game keeps the lot, and so does the pack.
    /// The settings are written as soon as they are changed.
    /// </summary>
    public class Ledger
    {
        public Settings Settings { get; set; } = new Settings();
        /// <summary>By the Sim's GUID, written in hex. Only Sims with moodlets are kept.</summary>
        public Dictionary<string, List<Moodlet>> Sims { get; set; } = new Dictionary<string, List<Moodlet>>();

        public static string Key(uint guid) => guid.ToString("x8");

        /// <summary>The Sim's moodlets, for reading: empty if they have none.</summary>
        public IReadOnlyList<Moodlet> Find(uint guid) => Sims.TryGetValue(Key(guid), out var list) ? list : (IReadOnlyList<Moodlet>)Array.Empty<Moodlet>();

        /// <summary>The Sim's moodlets, for changing: a list is made for a Sim who has none.</summary>
        public List<Moodlet> Of(uint guid)
        {
            var key = Key(guid);
            if (!Sims.TryGetValue(key, out var list))
            {
                list = new List<Moodlet>();
                Sims[key] = list;
            }
            return list;
        }

        /// <summary>Drop the Sims who have no moodlets left, so the file does not fill with them.</summary>
        public void Tidy()
        {
            foreach (var key in Sims.Where(x => x.Value == null || x.Value.Count == 0).Select(x => x.Key).ToList()) Sims.Remove(key);
        }

        // ---- the file ----

        public static string PathFor(string packId, string userPath)
        {
            //a hash of the neighbourhood's folder: Simitone keeps several (UserData, UserData2...)
            var full = Path.GetFullPath(userPath).Replace('\\', '/').TrimEnd('/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 12).ToLowerInvariant();
            var name = new string(Path.GetFileName(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var pack = new string(packId.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
            return Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "PackData", pack, name + "-" + hash + ".json");
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions() { WriteIndented = true };

        /// <summary>What was last saved for this neighbourhood. A missing or unreadable file gives a fresh ledger.</summary>
        public static Ledger Read(string packId, string userPath)
        {
            try
            {
                var path = PathFor(packId, userPath);
                if (File.Exists(path))
                {
                    var ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path), Json);
                    if (ledger != null)
                    {
                        ledger.Settings ??= new Settings();
                        ledger.Sims ??= new Dictionary<string, List<Moodlet>>();
                        foreach (var list in ledger.Sims.Values.Where(x => x != null))
                        {
                            list.RemoveAll(x => x == null || string.IsNullOrWhiteSpace(x.Id) || x.Minutes <= 0);
                            foreach (var moodlet in list) moodlet.Note ??= "";
                        }
                        ledger.Tidy();
                        return ledger;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read the moodlets kept for " + userPath + " (" + e.Message + "). Starting afresh.");
            }
            return new Ledger();
        }

        /// <summary>
        /// Keep the settings now, without keeping moodlets that have come and gone since the last save: the file's
        /// Sims stay as that save left them.
        /// </summary>
        public bool WriteSettings(string packId, string userPath)
        {
            var saved = Read(packId, userPath);
            saved.Settings = Settings;
            return saved.Write(packId, userPath);
        }

        public bool Write(string packId, string userPath)
        {
            try
            {
                Tidy();
                var path = PathFor(packId, userPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                //written beside and moved into place, so a crash half way leaves the old file whole
                var temp = path + ".new";
                File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("Could not save the moodlets for " + userPath + ": " + e.Message);
                return false;
            }
        }
    }

    /// <summary>What the pack does, in the terminal and in Moodlets.log in Simitone's user directory.</summary>
    public static class Log
    {
        private static readonly object Lock = new object();

        public static void Write(string line)
        {
            line = "[moodlet] " + line;
            Console.WriteLine(line);
            try
            {
                lock (Lock) File.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "Moodlets.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
