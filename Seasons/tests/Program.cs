/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Seasons pack that need no copy of The Sims: that its patches fit the engine it is built against,
// and its own rules (the calendar, the weather's odds, the ground, wet and cold Sims, the file).
// What they cannot check is the game itself: nothing here loads a lot or a Sim.
// Run with: dotnet run --project Seasons/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.Content.Model;
using FSO.LotView.Components;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Seasons;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "seasons-test-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);
var hood = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
var other = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData2/");
var hoodNow = hood;
Sky.NeighbourhoodPath = () => hoodNow;

// ---- the patches fit this engine ----
var h = new Harmony("test.seasons");
var apply = typeof(SeasonsPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
try { apply.Invoke(null, new object[] { h }); }
catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
var patched = Harmony.GetAllPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
foreach (var needed in new[] { "VM.Tick", "TS1NeighborhoodProvider.InitSpecific", "TS1GameScreen.Save",
    "TerrainComponent.UpdateTerrain", "TerrainComponent.UpdateLotType", "VMTS1MotiveDecay.Tick",
    "UICheatTextbox.HandleWeatherCommand", "TS1GameScreen.UpdateWeatherEffects", "TS1GameScreen.ExitLot" })
    Check("patched " + needed, patched.Contains(needed));
Check("every optional patch fit (" + string.Join(" ", SeasonsPack.Missing) + ")", SeasonsPack.Missing.Count == 0 && Sky.GroundPatched);
// the per-tick hook must be harmless on a lot it knows nothing about
Sky.AfterTick(null);
Sky.ShowNow(null);
Check("nobody is at home on no lot", !Sky.AtHome(null) && Sky.Chilled(null).Count == 0);

// ---- the pack must run on every Simitone that can load code packs, not only the newest ----
// From Simitone's pack code it may use only what was there from the start (see the Apartments pack's tests).
{
    var allowed = new HashSet<string>() { "ISimitonePack", "Pack", "PackManager" };
    using var file = File.OpenRead(typeof(SeasonsPack).Assembly.Location);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var used = md.TypeReferences.Select(t => md.GetTypeReference(t))
        .Where(t => md.GetString(t.Namespace) == "Simitone.Client.Packs").Select(t => md.GetString(t.Name)).Distinct().ToList();
    Check("uses only the first pack API (" + string.Join(", ", used) + ")", used.All(allowed.Contains));
    Check("implements none of the later pack calls", typeof(SeasonsPack).GetMethod("OnLotSaved") == null && !typeof(SeasonsPack).GetMethods().Any(m => m.Name == "OnLotLoaded"));
    // and needs Simitone's weather, which draws what it chooses
    var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "pack.json"));
    Check("pack.json requires the built in Weather pack", manifest.Contains("\"requires\": [\"simitone.weather\"]") && Simitone.Client.Packs.BuiltInPacks.Weather == "simitone.weather");
}

// ---- the calendar ----
{
    var c = new Calendar() { Seed = 1 };
    var seen = new List<string>();
    for (int day = 1; day <= 29; day++)
    {
        var began = c.AddDay(true);
        if (began != null) seen.Add(SeasonInfo.Name(began.Value) + "@" + day);
    }
    Check("a year of seven day seasons: " + string.Join(", ", seen), string.Join(",", seen) == "Summer@7,Autumn@14,Winter@21,Spring@28");
    Check("and then spring's first day", c.Season == Season.Spring && c.DayOfSeason == 1 && c.Day == 29 && c.Describe() == "Spring, day 2 of 7" && c.DaysLeft == 6);

    var away = new Calendar() { Seed = 1 };
    for (int i = 0; i < 10; i++) away.AddDay(false);
    Check("midnights away from home give new weather days but no season days", away.Day == 10 && away.DayOfSeason == 0 && away.Season == Season.Spring);

    var still = new Calendar() { Seed = 1 };
    still.Settings.Turning = false;
    for (int i = 0; i < 30; i++) still.AddDay(true);
    Check("with the seasons off it stays spring, and the days go on", still.Season == Season.Spring && still.DayOfSeason == 0 && still.Day == 30);

    var one = new Calendar() { Seed = 1 };
    one.Settings.SeasonDays = 1;
    one.AddDay(true); one.AddDay(true);
    Check("one day seasons", one.Season == Season.Autumn && one.Describe() == "Autumn, day 1 of 1");

    var broken = new Calendar() { Season = (Season)9, DayOfSeason = 50, Day = -3, Seed = 0 };
    broken.Settings.SeasonDays = 0;
    broken.Repair();
    Check("a damaged calendar is put back within reason", broken.Season == Season.Spring && broken.Settings.SeasonDays == 1 && broken.DayOfSeason == 0 && broken.Day == 0 && broken.Seed != 0);

    var winterNews = Sky.Arrival(Season.Winter, new Calendar());
    Check("winter's news: " + winterNews, winterNews == "Winter has come, for 7 days. The weather: clear 50%, light rain 5%, light snow 30%, heavy snow 15%. Snow lies on the ground. Sims who stay outside get cold, and lose needs faster.");
    var quiet = new Calendar();
    quiet.Settings.Ground = false; quiet.Settings.Chill = false; quiet.Settings.SeasonDays = 1;
    Check("and with the ground and chill off", Sky.Arrival(Season.Winter, quiet) == "Winter has come, for a day. The weather: clear 50%, light rain 5%, light snow 30%, heavy snow 15%.");
    Check("season names", SeasonInfo.TryParse("Fall", out var fall) && fall == Season.Autumn && SeasonInfo.TryParse(" WINTER ", out var w) && w == Season.Winter && !SeasonInfo.TryParse("monsoon", out _));

    var watch = new DayWatch();
    Check("the clock: an hour at a time, and midnight is a day",
        !watch.Feed(22) && !watch.Feed(23) && watch.Feed(0) && !watch.Feed(0) && !watch.Feed(1));
    var jumpy = new DayWatch();
    Check("a clock that jumps to midnight, as on loading a lot, is not a day", !jumpy.Feed(15) && !jumpy.Feed(0));
}

// ---- the file ----
{
    Sky.Forget();
    var fresh = Sky.Calendar;
    Check("a neighbourhood with no file starts in spring, and is kept at once", fresh != null && fresh.Season == Season.Spring && fresh.Day == 0 && File.Exists(Calendar.PathFor(Sky.PackId, hood)));
    var seed = fresh.Seed;
    fresh.AddDay(true); fresh.AddDay(true);
    fresh.Settings.SeasonDays = 10;
    Sky.SettingsChanged();
    Sky.Forget();
    var back = Sky.Calendar;
    Check("a setting is kept at once, without the days played and not saved", back.Settings.SeasonDays == 10 && back.Day == 0 && back.Seed == seed);
    back.AddDay(true); back.AddDay(true); back.AddDay(true);
    Sky.Saved();
    Sky.Forget();
    Check("the days are kept when the game saves", Sky.Calendar.Day == 3 && Sky.Calendar.DayOfSeason == 3);
    Sky.Calendar.AddDay(true);
    Sky.Calendar.Season = Season.Winter; Sky.Calendar.DayOfSeason = 2;
    Sky.SeasonChosen();
    Sky.Forget();
    Check("a season chosen from the console is kept at once, without unsaved days", Sky.Calendar.Season == Season.Winter && Sky.Calendar.DayOfSeason == 2 && Sky.Calendar.Day == 3);

    hoodNow = other;
    Check("each neighbourhood has its own calendar and sky", Sky.Calendar.Season == Season.Spring && Sky.Calendar.Seed != seed);
    hoodNow = hood;
    Check("and the first one is still there", Sky.Calendar.Season == Season.Winter);

    File.WriteAllText(Calendar.PathFor(Sky.PackId, hood), "{ this is not json");
    Sky.Forget();
    Check("an unreadable file gives a fresh calendar", Sky.Calendar != null && Sky.Calendar.Season == Season.Spring);
    hoodNow = null;
    Sky.Forget();
    Check("no neighbourhood, no calendar", Sky.Calendar == null);
    var none = SeasonsPack.Run(new[] { "set", "winter" }, null, out _);
    Check("the console says so: " + none, none == "There is no neighbourhood loaded.");
    hoodNow = hood;
    Sky.Forget();
}

// ---- the weather ----
{
    var odds = Odds.Default();
    Check("the same day and hour always has the same weather",
        Enumerable.Range(0, 200).All(d => Forecast.At(42, d, 5, Season.Autumn, odds) == Forecast.At(42, d, 5, Season.Autumn, odds)));
    Check("two neighbourhoods have different skies",
        Enumerable.Range(0, 200).Count(d => Forecast.At(1, d, 3, Season.Spring, odds) != Forecast.At(2, d, 3, Season.Spring, odds)) > 40);

    // over many days, each kind of weather has about the share of the time its odds give it
    foreach (Season season in Enum.GetValues(typeof(Season)))
    {
        var counts = new int[Odds.Kinds.Length];
        var spells = 0;
        var freshDraws = 0;
        WeatherKind? last = null;
        var changes = 0;
        for (int day = 0; day < 3000; day++)
            for (int spell = 0; spell < Forecast.SpellsPerDay; spell++)
            {
                var kind = Forecast.At(7, day, spell, season, odds);
                counts[(int)kind]++;
                spells++;
                if (last != null && kind != last) changes++;
                last = kind;
                if (Forecast.Roll(7, (long)day * Forecast.SpellsPerDay + spell, 1) >= Forecast.CarryOn) freshDraws++;
            }
        var weights = odds.For(season);
        var total = (double)weights.Sum();
        var worst = Enumerable.Range(0, weights.Length).Max(i => Math.Abs(counts[i] / (double)spells - weights[i] / total));
        Check(SeasonInfo.Name(season) + ": each kind of weather within 2% of its odds (worst " + (worst * 100).ToString("0.0") + "%)", worst < 0.02);
        Check(SeasonInfo.Name(season) + ": a kind with no odds never comes", Enumerable.Range(0, weights.Length).All(i => weights[i] > 0 || counts[i] == 0));
        Check(SeasonInfo.Name(season) + ": the weather comes in spells (it changes at " + (100.0 * changes / spells).ToString("0") + "% of spell ends)", changes < spells * 0.4);
        Check(SeasonInfo.Name(season) + ": about 4 spells in 10 draw afresh", Math.Abs(freshDraws / (double)spells - (1 - Forecast.CarryOn)) < 0.02);
    }
    Check("no snow in summer, no storms in winter", odds.For(Season.Summer)[(int)WeatherKind.LightSnow] == 0 && odds.For(Season.Summer)[(int)WeatherKind.HeavySnow] == 0 && odds.For(Season.Winter)[(int)WeatherKind.Storm] == 0);
    Check("spells of two hours", Forecast.SpellOf(0) == 0 && Forecast.SpellOf(1) == 0 && Forecast.SpellOf(2) == 1 && Forecast.SpellOf(23) == 11 && Forecast.SpellsPerDay == 12);
    Check("rolls are from 0 to 1", Enumerable.Range(0, 10000).Select(i => Forecast.Roll(3, i, 2)).All(r => r >= 0 && r < 1));

    Check("snowy lots get snow for rain", Forecast.OnLot(WeatherKind.LightRain, Climate.Snowy) == WeatherKind.LightSnow
        && Forecast.OnLot(WeatherKind.Storm, Climate.Snowy) == WeatherKind.HeavySnow && Forecast.OnLot(WeatherKind.Clear, Climate.Snowy) == WeatherKind.Clear);
    Check("the clouds get no weather", Odds.Kinds.All(k => Forecast.OnLot(k, Climate.Fair) == WeatherKind.Clear));
    Check("ordinary lots get the season's weather", Odds.Kinds.All(k => Forecast.OnLot(k, Climate.Seasonal) == k));
    Check("climates from the lot's own ground",
        Forecast.ClimateOf(TerrainType.GRASS, TerrainType.GRASS) == Climate.Seasonal && Forecast.ClimateOf(TerrainType.GRASS, TerrainType.SAND) == Climate.Seasonal
        && Forecast.ClimateOf(TerrainType.SNOW, TerrainType.SNOW) == Climate.Snowy && Forecast.ClimateOf(TerrainType.TS1Cloud, TerrainType.TS1Cloud) == Climate.Fair);

    // the numbers are the ones the console's weather command sets (Simitone's UICheatTextbox)
    Check("weather storm", Forecast.Data(WeatherKind.Storm) == (short)((1 << 8) | (1 << 11) | 75));
    Check("weather snow and rain", Forecast.Data(WeatherKind.LightSnow) == (short)((1 << 8) | (1 << 9) | 35) && Forecast.Data(WeatherKind.HeavyRain) == (short)((1 << 8) | 85));
    Check("weather clear", Forecast.Data(WeatherKind.Clear) == (short)(1 << 8));
    bool Raining(short d) { Forecast.Read(d, out var r, out _); return r; }
    bool Snowing(short d) { Forecast.Read(d, out _, out var s); return s; }
    Check("what each setting means outside",
        Raining(Forecast.Data(WeatherKind.LightRain)) && Raining(Forecast.Data(WeatherKind.Storm)) && !Snowing(Forecast.Data(WeatherKind.Storm))
        && Snowing(Forecast.Data(WeatherKind.HeavySnow)) && !Raining(Forecast.Data(WeatherKind.HeavySnow))
        && !Raining(Forecast.Data(WeatherKind.Clear)) && !Snowing(Forecast.Data(WeatherKind.Clear)));
    Check("hail is wet, weather auto and a drizzle of 10 are not", Raining((short)((1 << 8) | (2 << 9) | 50)) && !Raining(0) && !Raining((short)((1 << 8) | 10)));
}

// ---- weather.json ----
{
    var shipped = Odds.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."), out var report);
    Check("the shipped weather.json reads cleanly: " + report, report == "Weather odds read from weather.json.");
    Check("and holds the pack's own odds", ((Season[])Enum.GetValues(typeof(Season))).All(s => shipped.For(s).SequenceEqual(Odds.Default().For(s))));
    Odds.Load(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("n")), out report);
    Check("no file: the pack's own odds", report.StartsWith("No weather.json"));

    var edited = Odds.Parse("{ // a note\n \"Summer\": { \"clear\": 1, \"storm\": 1 }, \"fall\": { \"heavysnow\": 3 }, }", out report);
    Check("a season of the player's own: " + report, edited.For(Season.Summer).SequenceEqual(new[] { 1, 0, 0, 1, 0, 0 }) && edited.For(Season.Autumn)[(int)WeatherKind.HeavySnow] == 3 && report == "Weather odds read from weather.json.");
    Check("and the seasons it leaves out keep the pack's", edited.For(Season.Winter).SequenceEqual(Odds.Default().For(Season.Winter)));

    var bad = Odds.Parse("{ \"monsoon\": {}, \"spring\": { \"clear\": -1 }, \"summer\": { \"fog\": 5 }, \"winter\": { \"clear\": 0 } }", out report);
    Check("mistakes are named: " + report, report.Contains("\"monsoon\" is not a season") && report.Contains("spring: clear should be") && report.Contains("\"fog\" is not a kind") && report.Contains("winter has no weather"));
    Check("and those seasons keep the pack's odds", ((Season[])Enum.GetValues(typeof(Season))).All(s => bad.For(s).SequenceEqual(Odds.Default().For(s))));
    Odds.Parse("[1, 2]", out report);
    Check("not an object: " + report, report.Contains("one entry for each season"));
    Odds.Parse("{ nope", out report);
    Check("not JSON: " + report, report.Contains("not valid JSON"));

    Check("odds read out: " + Odds.Default().Summary(Season.Winter), Odds.Default().Summary(Season.Winter) == "clear 50%, light rain 5%, light snow 30%, heavy snow 15%");
    var allClear = Odds.Parse("{ \"spring\": { \"clear\": 1 } }", out _);
    Check("a season of only sun", Enumerable.Range(0, 100).All(d => Forecast.At(5, d, 0, Season.Spring, allClear) == WeatherKind.Clear));
}

// ---- the ground ----
{
    Check("spring and summer: the lot's own grass", Ground.For(TerrainType.GRASS, Season.Spring, false) == TerrainType.GRASS && Ground.For(TerrainType.GRASS, Season.Summer, false) == TerrainType.GRASS);
    Check("autumn: autumn grass, and sand stays sand", Ground.For(TerrainType.GRASS, Season.Autumn, false) == TerrainType.TS1AutumnGrass
        && Ground.For(TerrainType.TS1DarkGrass, Season.Autumn, false) == TerrainType.TS1AutumnGrass && Ground.For(TerrainType.SAND, Season.Autumn, false) == TerrainType.SAND);
    Check("winter: snow over grass and sand", new[] { TerrainType.GRASS, TerrainType.TS1DarkGrass, TerrainType.TS1AutumnGrass, TerrainType.SAND }.All(t => Ground.For(t, Season.Winter, false) == TerrainType.SNOW));
    Check("snow lies while it falls, in any season", Ground.For(TerrainType.GRASS, Season.Summer, true) == TerrainType.SNOW && Ground.For(TerrainType.GRASS, null, true) == TerrainType.SNOW);
    Check("rock, water, cloud and snow stay as they are", new[] { TerrainType.ROCK, TerrainType.WATER, TerrainType.TS1Cloud, TerrainType.SNOW }.All(t =>
        Ground.For(t, Season.Winter, true) == t && Ground.For(t, Season.Autumn, false) == t));
    Check("with the ground off, only falling snow changes it", Ground.For(TerrainType.GRASS, null, false) == TerrainType.GRASS);
    Check("no seasons on the ground above the clouds", Sky.GroundSeason(new Calendar(), Climate.Fair) == null && Sky.GroundSeason(new Calendar() { Season = Season.Winter }, Climate.Snowy) == Season.Winter);

    // the patches on the real terrain: it is given a lot's grass, and shows the season's ground
    try
    {
        var lightOf = AccessTools.FieldRefAccess<TerrainComponent, TerrainType>("LightType");
        var darkOf = AccessTools.FieldRefAccess<TerrainComponent, TerrainType>("DarkType");
        var bp = new FSO.LotView.Model.Blueprint(8, 8);
        var terrain = new TerrainComponent(new Rectangle(0, 0, 8, 8), bp);
        var heights = new short[64];
        var grass = new byte[64];
        Sky.Calendar.Season = Season.Autumn;
        terrain.UpdateTerrain(TerrainType.GRASS, TerrainType.SAND, heights, grass);
        Check("the terrain in autumn: autumn grass and sand", lightOf(terrain) == TerrainType.TS1AutumnGrass && darkOf(terrain) == TerrainType.SAND);
        bp.Weather.SetWeather(Forecast.Data(WeatherKind.LightSnow));
        terrain.UpdateLotType();
        Check("and white while it snows", lightOf(terrain) == TerrainType.SNOW && darkOf(terrain) == TerrainType.SNOW);
        //Simitone's own snow, when it stops: the season's ground, not plain grass
        terrain.ForceSnow(1);
        bp.Weather.SetWeather(Forecast.Data(WeatherKind.Clear));
        terrain.UpdateLotType();
        Check("and autumn's again when it stops, whatever Simitone's own snow does", lightOf(terrain) == TerrainType.TS1AutumnGrass);
        Sky.Calendar.Season = Season.Winter;
        terrain.UpdateLotType();
        Check("winter: snow", lightOf(terrain) == TerrainType.SNOW && darkOf(terrain) == TerrainType.SNOW);
        Sky.Calendar.Settings.Ground = false;
        terrain.UpdateLotType();
        Check("ground off: the lot's own", lightOf(terrain) == TerrainType.GRASS && darkOf(terrain) == TerrainType.SAND);
        Sky.Calendar.Settings.Ground = true;
        var snowLot = new TerrainComponent(new Rectangle(0, 0, 8, 8), bp);
        snowLot.UpdateTerrain(TerrainType.SNOW, TerrainType.SNOW, heights, grass);
        Sky.Calendar.Season = Season.Summer;
        snowLot.UpdateLotType();
        Check("a snowy lot stays snowy in summer", lightOf(snowLot) == TerrainType.SNOW);
        var unseen = new TerrainComponent(new Rectangle(0, 0, 8, 8), bp);
        Check("a terrain not yet given a lot is left alone", Sky.GroundFor(unseen, bp) == null);
    }
    catch (Exception e)
    {
        Check("the terrain could be made without a game to try the ground on: " + e.GetType().Name + " " + e.Message, false);
    }
    Sky.Forget();
}

// ---- wet and cold ----
{
    Check("a need as one number, across the game's carry",
        Chill.Exact(50, 999) == 49001 && Chill.Exact(49, 0) == 49000 && Chill.Exact(50, -500) == 50500 && Chill.Exact(51, 500) == 50500);

    var s = new Chill.State();
    s.Update(true, true, false, 2);
    Check("outside in the rain: wet, not cold", s.Wet && !s.Cold);
    // comfort (need 1) decays 500 thousandths a step for this Sim; wet doubles it
    var taken = 0;
    for (int i = 0; i < 10; i++) taken += s.Take(1, 500);
    Check("wet: Comfort twice as fast (" + taken + " more points in 10 steps of half a point)", taken == 5);
    taken = 0;
    for (int i = 0; i < 10; i++) taken += s.Take(2, 500);
    Check("wet: Hygiene half as fast again (" + taken + ")", taken == 2);
    Check("wet: Hunger, Bladder, Energy, Fun and Social as usual", new[] { 0, 3, 4, 5, 6 }.All(n => s.Take(n, 5000) == 0));
    Check("a need that is rising is left alone", s.Take(1, -2000) == 0 && s.Take(1, 0) == 0);

    var steps = 0;
    while (s.Wet && steps < 100) { s.Update(false, true, false, 2); steps++; }
    Check("indoors, dry after an hour (" + steps * 2 + " minutes)", steps * 2 == Chill.DryingMinutes);
    var outdoorsDry = new Chill.State();
    outdoorsDry.Update(true, true, false, 2);
    outdoorsDry.Update(true, false, false, 2);
    Check("outside after the rain stops, drying too", outdoorsDry.Wet && outdoorsDry.Drying == Chill.DryingMinutes - 2);

    var cold = new Chill.State();
    cold.Update(true, false, true, 2);
    taken = 0;
    for (int i = 0; i < 10; i++) taken += cold.Take(0, 1000);
    Check("cold: Hunger half as fast again (" + taken + ")", cold.Cold && !cold.Wet && taken == 5);
    Check("cold: Energy and Comfort too, not Hygiene", cold.Take(4, 2000) == 1 && cold.Take(1, 2000) == 1 && cold.Take(2, 2000) == 0);
    cold.Update(false, false, true, 2);
    Check("coming in warms a Sim at once", !cold.Cold && cold.Take(0, 2000) == 0);

    var both = new Chill.State();
    both.Update(true, true, true, 2);
    taken = 0;
    for (int i = 0; i < 4; i++) taken += both.Take(1, 1000);
    Check("wet and cold: Comfort two and a half times as fast (" + taken + " more in 4 points)", taken == 6);
    Check("the needs in the game's order", Chill.Needs.Length == 7 && Chill.Needs.SequenceEqual(FSO.SimAntics.Entities.VMTS1MotiveDecay.DecrementMotives));
}

// ---- the console, with no game running ----
{
    var answer = SeasonsPack.Run(new[] { "nonsense" }, null, out var understood);
    Check("the console does not fall over without a game", answer == null && !understood);
    Check("season help", SeasonsPack.Run(new[] { "help" }, null, out _).Contains("season set"));
    var status = SeasonsPack.Run(new string[0], null, out _);
    Check("season: " + status.Replace("\n", " | "), status.StartsWith("Spring, day 1 of"));
    Check("season set winter 3", SeasonsPack.Run(new[] { "set", "winter", "3" }, null, out _) == "It is now winter, day 3 of 7." && Sky.Calendar.Season == Season.Winter);
    Check("season set winter 30 is refused", SeasonsPack.Run(new[] { "set", "winter", "30" }, null, out _).StartsWith("A day from 1 to 7"));
    Check("season days 3 keeps the day within the season", SeasonsPack.Run(new[] { "days", "3" }, null, out _).StartsWith("Each season lasts 3 days") && Sky.Calendar.DayOfSeason == 2);
    Check("season off", SeasonsPack.Run(new[] { "off" }, null, out _).StartsWith("The seasons stand still: it stays winter") && !Sky.Calendar.Settings.Turning);
    SeasonsPack.Run(new[] { "chill", "maybe" }, null, out understood);
    Check("season chill maybe is not understood", !understood);
    SeasonsPack.Run(new[] { "chill", "off" }, null, out _);
    Sky.Forget();
    Check("settings from the console are kept", !Sky.Calendar.Settings.Chill && !Sky.Calendar.Settings.Turning && Sky.Calendar.Settings.SeasonDays == 3);
    Check("season odds winter", SeasonsPack.Run(new[] { "odds", "winter" }, null, out _) == "Winter: clear 50%, light rain 5%, light snow 30%, heavy snow 15%.");
    Check("season forecast needs a lot", SeasonsPack.Run(new[] { "forecast" }, null, out _) == "This works while you are in a lot.");
}

Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true);
Console.WriteLine(failed == 0 ? "All passed." : failed + " FAILED.");
return failed == 0 ? 0 : 1;
