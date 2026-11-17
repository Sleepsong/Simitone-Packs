/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content.Model;
using FSO.Content.TS1;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Seasons
{
    /// <summary>
    /// Seasons, a first stage: a calendar of spring, summer, autumn and winter for each neighbourhood, which chooses the
    /// weather from each season's odds, turns the grass in autumn and covers it with snow in winter, and makes Sims who
    /// are wet or cold lose their needs faster. Simitone's weather (the built in Weather pack) does the drawing and the
    /// sound; this pack decides what the weather is.
    ///
    /// The engine is not changed. The pack hangs on it in three places it cannot do without, each a Harmony patch:
    ///
    ///  1. A lot has ticked: count a day at midnight on the home lot, and set the lot's weather.
    ///  2. The game has loaded a neighbourhood: forget the calendar of the one before.
    ///  3. The game has saved: keep the calendar with it.
    ///
    /// and some it can do without, so a version of Simitone they do not fit costs only what they do:
    ///
    ///  4. The ground: the terrain is given a lot's ground (remember it), and works out its colours (put the season's
    ///     ground in its place). Without these, the ground stays as Simitone draws it.
    ///  5. Wet and cold Sims: the game's own decay of a Sim's needs, done a little more when they are wet or cold.
    ///  6. The console's weather command: the player taking the weather over, and "weather auto" handing it back.
    ///  7. The weather a lot was saved with, which Simitone puts back as the lot loads: the calendar's is used instead.
    ///  8. A lot is left: forget days played and not saved.
    ///
    /// Of Simitone's own pack calls it uses only OnLoad, the first there was, as Life Stages does.
    /// </summary>
    public class SeasonsPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            Sky.PackId = pack.Id;
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
            Sky.Odds = Odds.Load(pack.Folder, out var report);
            Log.Write(report);
            Sky.Announce = (vm, title, message) =>
            {
                if (Screen?.vm == vm) Say(title, message);
            };
            PackManager.RegisterCommand("season", Command);
            Log.Write("Seasons loaded. Console: season help");
        }

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(SeasonsPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            return (parameters.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, parameters))
                ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static AccessTools.FieldRef<TObject, TField> Field<TObject, TField>(string name)
        {
            if (AccessTools.Field(typeof(TObject), name) == null) throw new Exception(typeof(TObject).Name + "." + name + " was not found: this pack was made for a different version of Simitone");
            return AccessTools.FieldRefAccess<TObject, TField>(name);
        }

        private static AccessTools.FieldRef<TerrainComponent, TerrainType> LightTypeOf, DarkTypeOf;
        private static AccessTools.FieldRef<TerrainComponent, Blueprint> BlueprintOf;
        private static AccessTools.FieldRef<TS1GameScreen, short?> PendingWeatherOf;

        /// <summary>What could not be patched, for the console's status: each a sentence of what is missing.</summary>
        public static readonly List<string> Missing = new List<string>();

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 2
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(AfterNeighbourhoodLoad)));
            // 3
            harmony.Patch(Find(typeof(TS1GameScreen), "Save"), postfix: Mine(nameof(AfterSave)));

            // what the pack can do without. each in a Harmony of its own, so one that half fits can be taken off whole
            Optional(harmony.Id + ".ground", h =>
            {
                LightTypeOf = Field<TerrainComponent, TerrainType>("LightType");
                DarkTypeOf = Field<TerrainComponent, TerrainType>("DarkType");
                BlueprintOf = Field<TerrainComponent, Blueprint>("Bp");
                h.Patch(Find(typeof(TerrainComponent), "UpdateTerrain"), prefix: Mine(nameof(BeforeUpdateTerrain)));
                h.Patch(Find(typeof(TerrainComponent), "UpdateLotType"), prefix: Mine(nameof(BeforeUpdateLotType)));
                Sky.GroundPatched = true;
            }, "The ground does not change with the seasons");
            Optional(harmony.Id + ".chill", h =>
            {
                h.Patch(Find(typeof(VMTS1MotiveDecay), "Tick"), prefix: Mine(nameof(BeforeDecay)), postfix: Mine(nameof(AfterDecay)));
            }, "Being wet or cold does not touch a Sim's needs");
            Optional(harmony.Id + ".console", h =>
            {
                h.Patch(Find(typeof(UICheatTextbox), "HandleWeatherCommand"), postfix: Mine(nameof(AfterWeatherCommand)));
            }, "The console's weather command is overruled by the calendar at once");
            Optional(harmony.Id + ".restore", h =>
            {
                PendingWeatherOf = Field<TS1GameScreen, short?>("PendingWeatherData");
                h.Patch(Find(typeof(TS1GameScreen), "UpdateWeatherEffects"), prefix: Mine(nameof(BeforeWeatherEffects)));
            }, "A lot can show the weather it was saved with for a moment as it loads");
            Optional(harmony.Id + ".exit", h =>
            {
                h.Patch(Find(typeof(TS1GameScreen), "ExitLot"), postfix: Mine(nameof(AfterExitLot)));
            }, "Days played on a lot that is left without saving are kept until Simitone is closed");
        }

        private static void Optional(string id, Action<Harmony> patch, string without)
        {
            var h = new Harmony(id);
            try
            {
                patch(h);
            }
            catch (Exception e)
            {
                h.UnpatchAll(h.Id);
                Missing.Add(without + ".");
                Log.Write(without + ": " + e.Message);
            }
        }

        // 1, 2, 3, 8

        private static void AfterTick(VM __instance)
        {
            Guard("keeping the calendar", () => Sky.AfterTick(__instance));
        }

        private static void AfterNeighbourhoodLoad()
        {
            Sky.Forget();
        }

        private static void AfterSave()
        {
            Guard("saving the calendar", Sky.Saved);
        }

        private static void AfterExitLot()
        {
            Sky.Forget();
        }

        // 4. the ground

        /// <summary>The terrain is being given a lot's ground: these are the lot's own types, before any season.</summary>
        private static void BeforeUpdateTerrain(TerrainComponent __instance, TerrainType light, TerrainType dark)
        {
            var own = Sky.Own.GetValue(__instance, x => new Sky.OwnGround());
            own.Light = light;
            own.Dark = dark;
        }

        /// <summary>
        /// The terrain is about to work out its colours from its two types. Put the season's ground in their place. This
        /// also stands in for Simitone's own snow (ForceSnow), which it does the same as, while snow falls.
        /// </summary>
        private static void BeforeUpdateLotType(TerrainComponent __instance)
        {
            Guard("choosing the ground", () =>
            {
                var ground = Sky.GroundFor(__instance, BlueprintOf(__instance));
                if (ground == null) return;
                LightTypeOf(__instance) = ground.Value.light;
                DarkTypeOf(__instance) = ground.Value.dark;
            });
        }

        // 5. wet and cold

        private static void BeforeDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, VMContext context, out Sky.Before __state)
        {
            Sky.Before before = null;
            Guard("watching a Sim's needs", () => before = Sky.BeforeDecay(__instance, avatar, context));
            __state = before;
        }

        private static void AfterDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, VMContext context, Sky.Before __state)
        {
            if (__state == null) return;
            Guard("chilling a Sim", () => Sky.AfterDecay(__instance, avatar, context, __state));
        }

        // 6. the console's weather command

        /// <summary>
        /// Simitone has run "weather something". A kind of weather means the player wants that weather, so the calendar
        /// leaves the sky alone; "weather auto" hands it back to the calendar.
        /// </summary>
        private static void AfterWeatherCommand(string command)
        {
            Guard("handing the weather over", () =>
            {
                var calendar = Sky.Calendar;
                if (calendar == null) return;
                var words = (command ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 2) return;
                bool mine;
                switch (words[1])
                {
                    case "auto": mine = false; break;
                    case "rain":
                    case "storm":
                    case "thunder":
                    case "snow":
                    case "hail":
                    case "clear": mine = true; break;
                    default: return; //Simitone refused it
                }
                if (calendar.Settings.PlayerWeather == mine) return;
                calendar.Settings.PlayerWeather = mine;
                Sky.SettingsChanged();
                Log.Write(mine ? "The player has taken the weather over." : "The calendar has the weather again.");
                if (!mine) Sky.ShowNow(Screen?.vm);
                Say("Seasons", mine
                    ? "The weather is yours now: the seasons leave it alone until you type weather auto."
                    : "The seasons choose the weather again.");
            });
        }

        // 7. the weather a lot was saved with

        /// <summary>
        /// Simitone puts back the weather of the last save as a lot loads, once the lot is there. While the calendar has
        /// the weather, it sets the lot's itself, so there is nothing to put back.
        /// </summary>
        private static void BeforeWeatherEffects(TS1GameScreen __instance)
        {
            if (PendingWeatherOf(__instance) == null) return;
            var calendar = Sky.Calendar;
            if (calendar != null && !calendar.Settings.PlayerWeather) PendingWeatherOf(__instance) = null;
        }

        /// <summary>
        /// Nothing this pack does may take the game down with it. Whatever goes wrong is written to the log and the game
        /// carries on.
        /// </summary>
        public static bool Guard(string doing, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (++Failures <= MostFailuresLogged) Log.Write("Something went wrong while " + doing + ": " + e);
                if (Failures == MostFailuresLogged) Log.Write("(No more of these will be written down this session.)");
                return false;
            }
        }

        private const int MostFailuresLogged = 20;
        private static int Failures;

        // ---- the console ----

        private static TS1GameScreen Screen => GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;

        private const string Help =
            "season - the season, today's weather, and who is wet or cold\n" +
            "season forecast - the weather for the rest of today\n" +
            "season odds [season] - how likely each kind of weather is\n" +
            "season set [season] [day] - move the calendar, eg. season set winter 3\n" +
            "season days [number] - how many days a season lasts\n" +
            "season on / season off - whether the seasons turn\n" +
            "season ground on|off - whether the ground changes with the season\n" +
            "season chill on|off - whether wet and cold Sims lose needs faster\n" +
            "season plants - list this lot's plants in Seasons.log (for gardening, a later stage)\n" +
            "weather auto - let the seasons choose the weather again, after setting it yourself";

        private static bool Command(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the season command", () => answer = Run(words, Screen?.vm, out understood))) answer = "That went wrong. Seasons.log says how.";
            if (answer != null) Say("Seasons", answer);
            return understood;
        }

        /// <summary>"on" or "off" at words[at], if it is there: set it and keep it. Null if nothing was given.</summary>
        private static string OnOff(string[] words, int at, Action<bool> set, out bool understood)
        {
            understood = true;
            if (words.Length <= at) return null;
            var word = words[at].ToLowerInvariant();
            if (word != "on" && word != "off") { understood = false; return null; }
            set(word == "on");
            Sky.SettingsChanged();
            return word;
        }

        /// <summary>Does what was typed after "season", and returns what to tell the player.</summary>
        public static string Run(string[] words, VM vm, out bool understood)
        {
            understood = true;
            var calendar = Sky.Calendar;
            if (calendar == null) return "There is no neighbourhood loaded.";
            var settings = calendar.Settings;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            const string NeedsLot = "This works while you are in a lot.";

            switch (word)
            {
                case "":
                case "status":
                    {
                        var lines = new List<string>();
                        var next = SeasonInfo.Name(SeasonInfo.Next(calendar.Season)).ToLowerInvariant();
                        lines.Add(calendar.Describe() + (settings.Turning
                            ? " (" + (calendar.DaysLeft == 1 ? "the last day before " + next : calendar.DaysLeft + " days to " + next) + ")."
                            : ". The seasons are not turning: to start them, season on"));
                        if (vm != null)
                        {
                            if (settings.PlayerWeather) lines.Add("The weather is yours (set from the console). To let the seasons choose it again: weather auto");
                            else lines.Add("The weather now: " + Odds.Describe(Sky.WeatherFor(vm, calendar)) + ".");
                            var climate = Sky.ClimateOf(vm);
                            if (climate == Climate.Snowy) lines.Add("This lot is snowy all year: rain falls as snow, and it is cold outside.");
                            if (climate == Climate.Fair) lines.Add("This lot is above the clouds: no weather, and never cold.");
                            if (settings.Chill)
                            {
                                var chilled = Sky.Chilled(vm);
                                lines.Add(chilled.Count == 0 ? "Nobody here is wet or cold." : "Wet or cold: " + string.Join(", ", chilled) + ".");
                            }
                            if (!Sky.AtHome(vm)) lines.Add("The season's days count on the household's own lot.");
                        }
                        if (!settings.Ground) lines.Add("The ground does not change with the season (season ground on).");
                        if (!settings.Chill) lines.Add("Being wet or cold does not touch needs (season chill on).");
                        lines.AddRange(Missing);
                        return string.Join("\n", lines);
                    }

                case "help":
                    return Help;

                case "forecast":
                    {
                        if (vm == null) return NeedsLot;
                        if (settings.PlayerWeather) return "The weather is yours, set from the console. To let the seasons choose it again: weather auto";
                        var hour = vm.Context.Clock.Hours;
                        var lines = new List<string>();
                        WeatherKind? last = null;
                        for (int spell = Forecast.SpellOf(hour); spell < Forecast.SpellsPerDay; spell++)
                        {
                            var kind = Forecast.OnLot(Forecast.At(calendar.Seed, calendar.Day, spell, calendar.Season, Sky.Odds), Sky.ClimateOf(vm));
                            if (kind == last) continue;
                            last = kind;
                            var from = spell == Forecast.SpellOf(hour) ? "Now" : "From " + Clock(spell * Forecast.SpellHours);
                            lines.Add(from + ": " + Odds.Describe(kind));
                        }
                        return "The rest of today:\n" + string.Join("\n", lines);
                    }

                case "odds":
                    {
                        if (words.Length > 1)
                        {
                            if (!SeasonInfo.TryParse(words[1], out var season)) { understood = false; return null; }
                            return SeasonInfo.Name(season) + ": " + Sky.Odds.Summary(season) + ".";
                        }
                        var lines = ((Season[])Enum.GetValues(typeof(Season))).Select(s => SeasonInfo.Name(s) + ": " + Sky.Odds.Summary(s) + ".").ToList();
                        lines.Add("They are the share of the time. To change them, edit weather.json in the pack's folder and restart.");
                        return string.Join("\n", lines);
                    }

                case "set":
                    {
                        if (words.Length < 2 || !SeasonInfo.TryParse(words[1], out var season)) return "Which season? For example: season set winter";
                        var day = 1;
                        if (words.Length > 2 && (!int.TryParse(words[2], out day) || day < 1 || day > settings.SeasonDays))
                            return "A day from 1 to " + settings.SeasonDays + ", for example: season set winter 1";
                        calendar.Season = season;
                        calendar.DayOfSeason = day - 1;
                        Sky.SeasonChosen();
                        Sky.ShowNow(vm);
                        Log.Write("The player set the calendar to " + calendar.Describe() + ".");
                        return "It is now " + calendar.Describe().ToLowerInvariant() + ".";
                    }

                case "days":
                    {
                        if (words.Length > 1)
                        {
                            if (!int.TryParse(words[1], out var days) || days < 1 || days > Settings.LongestSeason)
                                return "A season lasts from 1 to " + Settings.LongestSeason + " days.";
                            settings.SeasonDays = days;
                            calendar.DayOfSeason = Math.Min(calendar.DayOfSeason, days - 1);
                            Sky.SettingsChanged();
                        }
                        return "Each season lasts " + (settings.SeasonDays == 1 ? "a day" : settings.SeasonDays + " days") + ". To change it: season days 10";
                    }

                case "on":
                case "off":
                    settings.Turning = word == "on";
                    Sky.SettingsChanged();
                    return settings.Turning ? "The seasons are turning." : "The seasons stand still: it stays " + SeasonInfo.Name(calendar.Season).ToLowerInvariant() + ". The weather still changes from day to day.";

                case "ground":
                    {
                        var done = OnOff(words, 1, x => settings.Ground = x, out understood);
                        if (!understood) return null;
                        if (done != null) Sky.ShowNow(vm);
                        return settings.Ground ? "The ground changes with the season: autumn grass in autumn, snow in winter." : "The ground stays as the lot's own, except while snow falls.";
                    }

                case "chill":
                    {
                        OnOff(words, 1, x => settings.Chill = x, out understood);
                        if (!understood) return null;
                        return settings.Chill
                            ? "Wet Sims lose Comfort twice as fast and Hygiene half as fast again, for an hour after coming in. Cold Sims, outside in winter or snow, lose Comfort, Energy and Hunger half as fast again."
                            : "Being wet or cold does not touch a Sim's needs.";
                    }

                case "plants":
                    if (vm == null) return NeedsLot;
                    return Plants(vm);

                default:
                    understood = false;
                    return null;
            }
        }

        private static string Clock(int hour) => (hour % 12 == 0 ? 12 : hour % 12) + (hour < 12 ? "am" : "pm");

        /// <summary>
        /// Gardening is a later stage: a plant's water and health are kept in its own script, which differs from plant to
        /// plant. This writes down what the lot's plants show from outside, so the next stage can be worked out from them.
        /// </summary>
        private static string Plants(VM vm)
        {
            var attributes = AccessTools.Field(typeof(VMEntity), "Attributes");
            var count = 0;
            Log.Write("The plants on lot " + vm.GetGlobalValue(10) + " at " + vm.Context.Clock.Hours + ":" + vm.Context.Clock.Minutes.ToString("00") + " (" + Sky.Calendar?.Describe() + "):");
            foreach (var entity in vm.Entities)
            {
                if (!(entity is VMGameObject) || entity.EntryPoints == null || entity.EntryPoints.Length <= 16 || entity.EntryPoints[16].ActionFunction == 0) continue;
                if (entity.MultitileGroup?.BaseObject != null && entity.MultitileGroup.BaseObject != entity) continue;
                var values = attributes?.GetValue(entity) as List<short>;
                Log.Write("  " + entity.Name + " (" + entity.Object.OBJ.GUID.ToString("x8") + ") " + (Sky.IsOutside(vm.Context, entity) ? "outside" : "inside")
                    + ", gardening value " + entity.GetValue(VMStackObjectVariable.GardeningValue)
                    + ", attributes [" + (values == null ? "?" : string.Join(", ", values)) + "]");
                count++;
            }
            if (count == 0) return "This lot has no plants a gardener tends.";
            return count + (count == 1 ? " plant" : " plants") + " written to Seasons.log. Water one, list them again, and what changed is its water.";
        }

        // ---- telling the player ----

        /// <summary>A message with an OK button.</summary>
        public static void Say(string title, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Log.Write(title + ": " + message.Replace("\n", " | "));
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
