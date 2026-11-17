/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Seasons
{
    /// <summary>The kinds of weather the calendar chooses between. Each is one of Simitone's weather settings.</summary>
    public enum WeatherKind
    {
        Clear = 0,
        LightRain = 1,
        HeavyRain = 2,
        Storm = 3,
        LightSnow = 4,
        HeavySnow = 5
    }

    /// <summary>What kind of weather a lot has, from its own ground (the ground the game gave it, before any season).</summary>
    public enum Climate
    {
        /// <summary>Grass and sand: the seasons' weather, and the seasons change the ground.</summary>
        Seasonal,
        /// <summary>Snow all year, like Vacation Island's mountain lots: the seasons' weather, but it falls as snow, and it is cold.</summary>
        Snowy,
        /// <summary>Magic Town's clouds: above the weather. Always clear.</summary>
        Fair
    }

    /// <summary>
    /// How likely each kind of weather is in each season: a weight for each kind, read from weather.json in the pack's
    /// folder. The weights are the share of the time each kind of weather is about, over a season (see Forecast).
    /// </summary>
    public class Odds
    {
        public Dictionary<Season, int[]> Weights = new Dictionary<Season, int[]>();

        public static readonly WeatherKind[] Kinds = (WeatherKind[])Enum.GetValues(typeof(WeatherKind));

        /// <summary>The names used in weather.json and the console, in the order of WeatherKind.</summary>
        public static readonly string[] Names = { "clear", "lightRain", "heavyRain", "storm", "lightSnow", "heavySnow" };

        public static string Describe(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Clear: return "clear";
                case WeatherKind.LightRain: return "light rain";
                case WeatherKind.HeavyRain: return "heavy rain";
                case WeatherKind.Storm: return "a thunderstorm";
                case WeatherKind.LightSnow: return "light snow";
                default: return "heavy snow";
            }
        }

        /// <summary>The pack's own odds, used for any season weather.json leaves out or gets wrong.</summary>
        public static Odds Default()
        {
            var odds = new Odds();
            //                                            clear, light rain, heavy rain, storm, light snow, heavy snow
            odds.Weights[Season.Spring] = new int[] { 55, 25, 10, 8, 2, 0 };
            odds.Weights[Season.Summer] = new int[] { 72, 12, 6, 10, 0, 0 };
            odds.Weights[Season.Autumn] = new int[] { 50, 26, 14, 4, 5, 1 };
            odds.Weights[Season.Winter] = new int[] { 50, 5, 0, 0, 30, 15 };
            return odds;
        }

        public int[] For(Season season) => Weights.TryGetValue(season, out var w) ? w : Default().Weights[season];

        /// <summary>The kind of weather a number from 0 to 1 falls on, in this season's weights.</summary>
        public WeatherKind Pick(Season season, double roll)
        {
            var weights = For(season);
            var total = weights.Sum();
            if (total <= 0) return WeatherKind.Clear;
            var at = roll * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (at < weights[i]) return Kinds[i];
                at -= weights[i];
            }
            //(rounding: the last kind with any weight)
            for (int i = weights.Length - 1; i >= 0; i--) if (weights[i] > 0) return Kinds[i];
            return WeatherKind.Clear;
        }

        /// <summary>"clear 55%, light rain 25%..." for a season.</summary>
        public string Summary(Season season)
        {
            var weights = For(season);
            var total = Math.Max(1, weights.Sum());
            return string.Join(", ", Kinds.Where(k => weights[(int)k] > 0).Select(k => Describe(k) + " " + Math.Round(100.0 * weights[(int)k] / total) + "%"));
        }

        /// <summary>
        /// Read weather.json. A missing file gives the pack's odds. A season the file leaves out or gets wrong keeps the
        /// pack's odds, and the problem is returned so it can be logged.
        /// </summary>
        public static Odds Load(string folder, out string report)
        {
            var odds = Default();
            var path = folder == null ? null : Path.Combine(folder, "weather.json");
            if (path == null || !File.Exists(path))
            {
                report = "No weather.json: using the pack's own odds.";
                return odds;
            }
            try
            {
                return Parse(File.ReadAllText(path), out report);
            }
            catch (Exception e)
            {
                report = "weather.json could not be read (" + e.Message + "): using the pack's own odds.";
                return odds;
            }
        }

        public static Odds Parse(string json, out string report)
        {
            var odds = Default();
            var problems = new List<string>();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException e)
            {
                report = "weather.json is not valid JSON (" + e.Message + "): using the pack's own odds.";
                return odds;
            }
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    report = "weather.json should hold one entry for each season: using the pack's own odds.";
                    return odds;
                }
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (entry.Name.StartsWith("_")) continue; //notes
                    if (!SeasonInfo.TryParse(entry.Name, out var season)) { problems.Add("\"" + entry.Name + "\" is not a season"); continue; }
                    if (entry.Value.ValueKind != JsonValueKind.Object) { problems.Add(entry.Name + " should list a weight for each kind of weather"); continue; }
                    var weights = new int[Kinds.Length];
                    var ok = true;
                    foreach (var kind in entry.Value.EnumerateObject())
                    {
                        var index = Array.FindIndex(Names, n => string.Equals(n, kind.Name, StringComparison.OrdinalIgnoreCase));
                        if (index < 0) { problems.Add(entry.Name + ": \"" + kind.Name + "\" is not a kind of weather (" + string.Join(", ", Names) + ")"); ok = false; break; }
                        if (kind.Value.ValueKind != JsonValueKind.Number || !kind.Value.TryGetInt32(out var weight) || weight < 0 || weight > 10000)
                        { problems.Add(entry.Name + ": " + kind.Name + " should be a whole number from 0 to 10000"); ok = false; break; }
                        weights[index] = weight;
                    }
                    if (ok && weights.Sum() == 0) { problems.Add(entry.Name + " has no weather at all (every weight is 0)"); ok = false; }
                    if (ok) odds.Weights[season] = weights;
                }
            }
            report = problems.Count == 0 ? "Weather odds read from weather.json."
                : "weather.json: " + string.Join("; ", problems) + ". Those seasons keep the pack's own odds.";
            return odds;
        }
    }

    /// <summary>
    /// The weather. A day is split into spells of two hours. Each spell either carries on the weather of the spell
    /// before or draws afresh from the season's odds. Since "carry on" or "draw afresh" leaves the share of each
    /// kind of weather as the odds give it, the odds are the share of the time; carrying on only makes the weather
    /// come in spells, with about five hours between fresh draws, instead of changing every two hours.
    ///
    /// Everything is worked out from the neighbourhood's seed, the day and the hour, with nothing remembered: leave a
    /// lot and come back at the same hour, or load the save again, and the sky is as it was.
    /// </summary>
    public static class Forecast
    {
        public const int SpellHours = 2;
        public const int SpellsPerDay = 24 / SpellHours;
        /// <summary>How likely a spell is to carry on the weather of the one before.</summary>
        public const double CarryOn = 0.6;
        /// <summary>The longest run of spells before the weather is drawn afresh whatever the dice say (a day).</summary>
        public const int LongestRun = SpellsPerDay;

        public static int SpellOf(int hour) => Math.Clamp(hour, 0, 23) / SpellHours;

        /// <summary>The weather the season's odds give for this spell of this day.</summary>
        public static WeatherKind At(int seed, int day, int spell, Season season, Odds odds)
        {
            long index = (long)day * SpellsPerDay + spell;
            for (int back = 0; back < LongestRun; back++, index--)
            {
                if (index <= 0) break;
                if (Roll(seed, index, 1) >= CarryOn) break; //this spell drew afresh
            }
            return odds.Pick(season, Roll(seed, Math.Max(0, index), 2));
        }

        /// <summary>The weather on a lot of this climate: snowy lots get snow for rain, and the clouds get none.</summary>
        public static WeatherKind OnLot(WeatherKind kind, Climate climate)
        {
            switch (climate)
            {
                case Climate.Fair: return WeatherKind.Clear;
                case Climate.Snowy:
                    switch (kind)
                    {
                        case WeatherKind.LightRain: return WeatherKind.LightSnow;
                        case WeatherKind.HeavyRain:
                        case WeatherKind.Storm: return WeatherKind.HeavySnow;
                    }
                    return kind;
                default: return kind;
            }
        }

        /// <summary>A lot's climate, from its own ground (the architecture's terrain types, before the season).</summary>
        public static Climate ClimateOf(TerrainType light, TerrainType dark)
        {
            if (light == TerrainType.TS1Cloud || dark == TerrainType.TS1Cloud) return Climate.Fair;
            if (light == TerrainType.SNOW) return Climate.Snowy;
            return Climate.Seasonal;
        }

        // ---- Simitone's weather settings ----
        // The same numbers the console's weather command sets: bit 8 says the weather is set (and not the online game's
        // automatic weather), bits 9-10 are the kind (0 rain, 1 snow), bit 11 thunder, and the low byte the intensity.

        public const short Manual = 1 << 8;
        public const int LightIntensity = 35;
        public const int HeavyIntensity = 85;
        public const int StormIntensity = 75; //what the console's "weather storm" uses

        public static short Data(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.LightRain: return (short)(Manual | LightIntensity);
                case WeatherKind.HeavyRain: return (short)(Manual | HeavyIntensity);
                case WeatherKind.Storm: return (short)(Manual | (1 << 11) | StormIntensity);
                case WeatherKind.LightSnow: return (short)(Manual | (1 << 9) | LightIntensity);
                case WeatherKind.HeavySnow: return (short)(Manual | (1 << 9) | HeavyIntensity);
                default: return Manual;
            }
        }

        /// <summary>What a weather setting means for a Sim outside: falling rain (or hail), falling snow, or neither.</summary>
        public static void Read(short data, out bool raining, out bool snowing)
        {
            raining = snowing = false;
            if ((data & Manual) == 0) return; //the online game's automatic weather: not something Simitone shows
            var intensity = data & 0xFF;
            if (intensity <= 10) return; //as Simitone's weather sounds and snowy ground: too light to count
            var type = (data >> 9) & 3;
            if (type == 1) snowing = true;
            else raining = true; //rain, and hail, which Simitone draws as rain
        }

        // ---- the dice: the same on every machine and every run ----

        /// <summary>A number from 0 to 1 for this spell and purpose.</summary>
        public static double Roll(int seed, long index, int purpose)
        {
            unchecked
            {
                ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
                h ^= (ulong)index * 0xC2B2AE3D27D4EB4FUL;
                h ^= (ulong)purpose * 0x165667B19E3779F9UL;
                //splitmix64's finish
                h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 27; h *= 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return (h >> 11) * (1.0 / (1UL << 53));
            }
        }
    }
}
