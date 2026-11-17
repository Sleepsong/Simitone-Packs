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
using System.Text.Json;

namespace Seasons
{
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }

    public static class SeasonInfo
    {
        public static Season Next(Season season) => (Season)(((int)season + 1) % 4);

        public static string Name(Season season) => season.ToString();

        public static bool TryParse(string word, out Season season)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "spring": season = Season.Spring; return true;
                case "summer": season = Season.Summer; return true;
                case "autumn":
                case "fall": season = Season.Autumn; return true;
                case "winter": season = Season.Winter; return true;
                default: season = Season.Spring; return false;
            }
        }
    }

    /// <summary>
    /// The neighbourhood's calendar: which season it is, how far into it, and the player's settings. One for each
    /// neighbourhood, so every household in it shares the seasons, as in The Sims 2.
    ///
    /// Like Life Stages' ledger, the days are written only when the game saves and read again when a neighbourhood
    /// loads or a lot is left, so the calendar matches the saved game. The settings are the player's choices, not part
    /// of the game played, so they are written as soon as they change (WriteSettings).
    /// </summary>
    public class Calendar
    {
        public Season Season { get; set; } = Season.Spring;
        /// <summary>Days lived of this season, from 0.</summary>
        public int DayOfSeason { get; set; }
        /// <summary>
        /// Midnights since the calendar began, on any lot, whether or not the seasons were turning. The weather of a day
        /// hangs on it, so every day has its own, on a holiday too.
        /// </summary>
        public int Day { get; set; }
        /// <summary>This neighbourhood's own weather: two neighbourhoods on the same day have different skies.</summary>
        public int Seed { get; set; }
        public Settings Settings { get; set; } = new Settings();

        public static Calendar Fresh()
        {
            return new Calendar() { Seed = RandomNumberGenerator.GetInt32(1, int.MaxValue) };
        }

        /// <summary>
        /// A midnight. Only one on the household's own lot is a day of the season (as for Life Stages' ages); any other
        /// gives the next day's weather. Returns the season that began today, if one did.
        /// </summary>
        public Season? AddDay(bool atHome)
        {
            Day++;
            if (!atHome || !Settings.Turning) return null;
            DayOfSeason++;
            if (DayOfSeason < Settings.SeasonDays) return null;
            DayOfSeason = 0;
            Season = SeasonInfo.Next(Season);
            return Season;
        }

        public int DaysLeft => Math.Max(1, Settings.SeasonDays - DayOfSeason);

        /// <summary>"Autumn, day 3 of 7".</summary>
        public string Describe()
        {
            return SeasonInfo.Name(Season) + ", day " + Math.Min(DayOfSeason + 1, Settings.SeasonDays) + " of " + Settings.SeasonDays;
        }

        /// <summary>Put anything a hand-edited or damaged file holds back within reason.</summary>
        public void Repair()
        {
            Settings ??= new Settings();
            Settings.Repair();
            if (!Enum.IsDefined(typeof(Season), Season)) Season = Season.Spring;
            DayOfSeason = Math.Clamp(DayOfSeason, 0, Settings.SeasonDays - 1);
            Day = Math.Max(0, Day);
            if (Seed == 0) Seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
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

        /// <summary>What was last saved for this neighbourhood. Null if there is no file yet.</summary>
        public static Calendar Read(string packId, string userPath)
        {
            try
            {
                var path = PathFor(packId, userPath);
                if (File.Exists(path))
                {
                    var calendar = JsonSerializer.Deserialize<Calendar>(File.ReadAllText(path), Json);
                    if (calendar != null)
                    {
                        calendar.Repair();
                        return calendar;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read the calendar kept for " + userPath + " (" + e.Message + "). Starting afresh.");
            }
            return null;
        }

        /// <summary>
        /// Keep the settings now, without keeping days that have been played and not saved: the file's days stay as the
        /// last save left them. A neighbourhood with no file yet gets one, starting today.
        /// </summary>
        public bool WriteSettings(string packId, string userPath)
        {
            var saved = Read(packId, userPath);
            if (saved == null) return Write(packId, userPath);
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
                Log.Write("Could not save the calendar for " + userPath + ": " + e.Message);
                return false;
            }
        }
    }

    /// <summary>The player's choices, kept with the neighbourhood.</summary>
    public class Settings
    {
        /// <summary>How many days each season lasts.</summary>
        public int SeasonDays { get; set; } = 7;
        /// <summary>Whether the seasons turn. Off, the season stays as it is; the days and the weather go on.</summary>
        public bool Turning { get; set; } = true;
        /// <summary>Whether the season changes the ground: autumn grass in autumn, snow in winter.</summary>
        public bool Ground { get; set; } = true;
        /// <summary>Whether being wet or cold makes a Sim's needs drop faster.</summary>
        public bool Chill { get; set; } = true;
        /// <summary>
        /// The player set the weather from the console, so the calendar leaves it alone until "weather auto".
        /// </summary>
        public bool PlayerWeather { get; set; }

        public const int LongestSeason = 99;

        public void Repair()
        {
            SeasonDays = Math.Clamp(SeasonDays, 1, LongestSeason);
        }
    }

    /// <summary>
    /// Watches a lot's clock for midnight. It only trusts the clock stepping from one hour to the next: a clock that
    /// jumps, as it does when a lot loads, is not a day gone by. (The same watch as Life Stages', so the two packs count
    /// the same days.)
    /// </summary>
    public class DayWatch
    {
        private int LastHour = -1;

        public bool Feed(int hour)
        {
            var last = LastHour;
            LastHour = hour;
            return last != -1 && last != hour && (last + 1) % 24 == hour && hour == 0;
        }
    }

    /// <summary>What the pack does, in the terminal and in Seasons.log in Simitone's user directory.</summary>
    public static class Log
    {
        private static readonly object Lock = new object();

        public static void Write(string line)
        {
            line = "[seasons] " + line;
            Console.WriteLine(line);
            try
            {
                lock (Lock) File.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "Seasons.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
