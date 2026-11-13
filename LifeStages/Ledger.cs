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

namespace LifeStages
{
    /// <summary>
    /// The pack's own file for a neighbourhood: every Sim it has met, with their stage and days, and the player's
    /// settings. It is kept beside Simitone's saves and not inside them, so the game's files stay as they were.
    ///
    /// The Sims' ages are written only when the game saves, and read again when the game loads a neighbourhood or a
    /// lot is left. So they match the saved game: leave a lot without saving, and the days played there are
    /// forgotten with it. The settings are the player's choices and not part of the game played, so they are
    /// written as soon as they are changed (WriteSettings).
    /// </summary>
    public class Ledger
    {
        public Settings Settings { get; set; } = new Settings();
        /// <summary>By the Sim's GUID, written in hex.</summary>
        public Dictionary<string, SimAge> Sims { get; set; } = new Dictionary<string, SimAge>();

        public static string Key(uint guid) => guid.ToString("x8");

        public SimAge Find(uint guid) => Sims.TryGetValue(Key(guid), out var sim) ? sim : null;

        /// <summary>The Sim's record. A Sim met for the first time starts its stage today.</summary>
        public SimAge Meet(uint guid, short gameAge)
        {
            var key = Key(guid);
            if (!Sims.TryGetValue(key, out var sim))
            {
                sim = new SimAge() { Stage = Stages.FromGameAge(gameAge), Days = 0 };
                Sims[key] = sim;
            }
            return sim;
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
                        ledger.Settings.Repair();
                        ledger.Sims ??= new Dictionary<string, SimAge>();
                        foreach (var gone in ledger.Sims.Where(x => x.Value == null || !Enum.IsDefined(typeof(Stage), x.Value.Stage)).Select(x => x.Key).ToList())
                            ledger.Sims.Remove(gone);
                        foreach (var sim in ledger.Sims.Values) sim.Days = Math.Max(0, sim.Days);
                        return ledger;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read the ages kept for " + userPath + " (" + e.Message + "). Starting afresh.");
            }
            return new Ledger();
        }

        /// <summary>
        /// Keep the settings now, without keeping days that have been played and not saved: the file's Sims stay as
        /// the last save left them.
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
                Log.Write("Could not save the ages for " + userPath + ": " + e.Message);
                return false;
            }
        }
    }

    /// <summary>What the pack does, in the terminal and in LifeStages.log in Simitone's user directory.</summary>
    public static class Log
    {
        private static readonly object Lock = new object();

        public static void Write(string line)
        {
            line = "[age] " + line;
            Console.WriteLine(line);
            try
            {
                lock (Lock) File.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "LifeStages.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
