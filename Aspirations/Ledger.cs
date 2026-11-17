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

namespace Aspirations
{
    /// <summary>A want, fear or lifetime want a Sim has: which one, what it reads, and what things were like when it was rolled.</summary>
    public class Slot
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public int Points { get; set; }
        public bool Locked { get; set; }
        /// <summary>What the goal is measured against: the skill, the money, the level when it was rolled, and so on (Goal.cs).</summary>
        public Dictionary<string, int> N { get; set; } = new Dictionary<string, int>();
        /// <summary>People the goal is about: friends when it was rolled, or partners counted so far.</summary>
        public List<int> Who { get; set; } = new List<int>();
        /// <summary>Children the goal is about, by GUID.</summary>
        public List<uint> Kids { get; set; } = new List<uint>();

        public int Get(string key, int otherwise = 0) => N != null && N.TryGetValue(key, out var v) ? v : otherwise;
    }

    /// <summary>A reward that lasts: a need kept full, quicker learning, money to come, a voucher.</summary>
    public class ActiveEffect
    {
        public string Reward { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        /// <summary>The Sim's played minutes (SimRecord.Minutes) at which it ends. 0: until used up.</summary>
        public long Until { get; set; }
        public List<string> Needs { get; set; } = new List<string>();
        public int Value { get; set; }
        public double Times { get; set; }
        public int Left { get; set; }
    }

    /// <summary>All the pack knows of one Sim.</summary>
    public class SimRecord
    {
        public string Aspiration { get; set; }
        /// <summary>Whether the player chose the aspiration, rather than chance.</summary>
        public bool Chosen { get; set; }
        /// <summary>Whether the player has been asked to choose. Asked once; the window can change it after.</summary>
        public bool Asked { get; set; }
        public bool Child { get; set; }
        public double Meter { get; set; }
        /// <summary>Points to spend on rewards.</summary>
        public int Points { get; set; }
        /// <summary>Every point ever earned, and lost to fears (which do not take away points to spend).</summary>
        public int Earned { get; set; }
        public int Lost { get; set; }
        public List<Slot> Wants { get; set; } = new List<Slot>();
        public List<Slot> Fears { get; set; } = new List<Slot>();
        public Slot Lifetime { get; set; }
        public bool LifetimeDone { get; set; }
        public List<ActiveEffect> Effects { get; set; } = new List<ActiveEffect>();
        /// <summary>When each reward was last used, in played minutes.</summary>
        public Dictionary<string, long> LastUsed { get; set; } = new Dictionary<string, long>();
        /// <summary>Sim minutes this Sim has been played: the pack's own clock, for how long effects last.</summary>
        public long Minutes { get; set; }
        public double FailureMinutes { get; set; }
        /// <summary>A breakdown has happened, and not yet been climbed out of.</summary>
        public bool BrokeDown { get; set; }
        /// <summary>The level the player was last told of, so the meter wavering at a boundary is not news each time.</summary>
        public int Announced { get; set; } = -1;
        public int Fulfilled { get; set; }
        public int Realized { get; set; }
        public int RewardsUsed { get; set; }
        public int Breakdowns { get; set; }

        public void Repair(Balance b)
        {
            Wants ??= new List<Slot>();
            Fears ??= new List<Slot>();
            Effects ??= new List<ActiveEffect>();
            LastUsed ??= new Dictionary<string, long>();
            Wants.RemoveAll(x => x == null || b.Want(x.Id) == null);
            Fears.RemoveAll(x => x == null || b.Fear(x.Id) == null);
            if (Lifetime != null && b.LifetimeWant(Lifetime.Id) == null) Lifetime = null;
            foreach (var s in Wants.Concat(Fears).Append(Lifetime).Where(x => x != null))
            {
                s.N ??= new Dictionary<string, int>();
                s.Who ??= new List<int>();
                s.Kids ??= new List<uint>();
            }
            while (Wants.Count > b.Slots.Wants) Wants.RemoveAt(Wants.Count - 1);
            while (Fears.Count > b.Slots.Fears) Fears.RemoveAt(Fears.Count - 1);
            //more locked than may be (the number was lowered): the first ones stay locked
            var locked = 0;
            foreach (var w in Wants) if (w.Locked && ++locked > b.Slots.Locks) w.Locked = false;
            if (Aspiration != null && b.Aspiration(Aspiration) == null) { Aspiration = null; Chosen = false; Asked = false; }
            Meter = Rules.Clamp(double.IsFinite(Meter) ? Meter : 0);
            Points = Math.Max(0, Points);
            Effects.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Kind));
        }
    }

    public class Settings
    {
        public bool On { get; set; } = true;
        /// <summary>Ask the player to choose each grown Sim's aspiration when the pack first meets them.</summary>
        public bool Ask { get; set; } = true;
        /// <summary>Show a line at the top of the screen when a want is fulfilled or a fear comes true.</summary>
        public bool Notices { get; set; } = true;
    }

    /// <summary>
    /// The pack's own file for a neighbourhood: every Sim it has met, and the player's settings. It is beside
    /// Simitone's saves, not inside them, so the game's files stay as they were.
    ///
    /// The Sims' records are written only when the game saves, and read again when a neighbourhood is loaded or a
    /// lot is left. So they match the saved game: leave a lot without saving and what happened there is forgotten.
    /// The settings are written as soon as they are changed.
    /// </summary>
    public class Ledger
    {
        public Settings Settings { get; set; } = new Settings();
        /// <summary>By the Sim's GUID, written in hex.</summary>
        public Dictionary<string, SimRecord> Sims { get; set; } = new Dictionary<string, SimRecord>();

        public static string Key(uint guid) => guid.ToString("x8");

        public SimRecord Find(uint guid) => Sims.TryGetValue(Key(guid), out var sim) ? sim : null;

        public SimRecord Meet(uint guid, Balance b)
        {
            var key = Key(guid);
            if (!Sims.TryGetValue(key, out var sim))
            {
                sim = new SimRecord() { Meter = Rules.Clamp(b.Meter.Start) };
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

        public static Ledger Read(string packId, string userPath, Balance b)
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
                        ledger.Sims ??= new Dictionary<string, SimRecord>();
                        foreach (var gone in ledger.Sims.Where(x => x.Value == null).Select(x => x.Key).ToList()) ledger.Sims.Remove(gone);
                        foreach (var sim in ledger.Sims.Values) sim.Repair(b);
                        return ledger;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read what was kept for " + userPath + " (" + e.Message + "). Starting afresh.");
            }
            return new Ledger();
        }

        /// <summary>Keep the settings now, without keeping play that has not been saved.</summary>
        public bool WriteSettings(string packId, string userPath, Balance b)
        {
            var saved = Read(packId, userPath, b);
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
                Log.Write("Could not save the aspirations for " + userPath + ": " + e.Message);
                return false;
            }
        }
    }

    /// <summary>What the pack does, in the terminal and in Aspirations.log in Simitone's user directory.</summary>
    public static class Log
    {
        private static readonly object Lock = new object();

        public static void Write(string line)
        {
            line = "[aspire] " + line;
            Console.WriteLine(line);
            try
            {
                lock (Lock) File.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "Aspirations.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
