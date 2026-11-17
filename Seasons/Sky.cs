/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Model;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Seasons
{
    /// <summary>
    /// What the pack does in the game: counting the days, setting the weather, choosing the ground, and wet and cold
    /// Sims. The rules themselves are in Calendar.cs, Forecast.cs, Ground.cs and Chill.cs.
    /// </summary>
    public static class Sky
    {
        public static string PackId = "sample.seasons";
        public static Odds Odds = Odds.Default();
        /// <summary>Tells the player something, with an OK button. Set by the pack; the tests leave it quiet.</summary>
        public static Action<VM, string, string> Announce = (vm, title, message) => { };

        // ---- the neighbourhood's calendar ----

        private static Calendar Current;
        private static string CurrentPath;

        /// <summary>Where the loaded neighbourhood is kept. The tests put another in its place.</summary>
        public static Func<string> NeighbourhoodPath = () => Content.Get()?.Neighborhood?.UserPath;

        /// <summary>The loaded neighbourhood's calendar, read from its file the first time it is wanted. Null with no neighbourhood.</summary>
        public static Calendar Calendar
        {
            get
            {
                var path = NeighbourhoodPath();
                if (path == null) return null;
                if (Current == null || CurrentPath != path)
                {
                    Current = Calendar.Read(PackId, path);
                    CurrentPath = path;
                    if (Current == null)
                    {
                        //kept at once: nothing has been played on it yet, and it keeps the neighbourhood's sky the same from now on
                        Current = Calendar.Fresh();
                        Current.Write(PackId, path);
                        Log.Write("A new calendar for " + path + ": " + Current.Describe() + ".");
                    }
                }
                return Current;
            }
        }

        /// <summary>
        /// Drop what is in memory, so that it is read from the file again: when the game loads a neighbourhood, and when
        /// a lot is left. If the game was saved first the file is the same; if not, the days played go with the lot.
        /// </summary>
        public static void Forget()
        {
            Current = null;
            CurrentPath = null;
        }

        /// <summary>The game has saved: keep the calendar with it.</summary>
        public static void Saved()
        {
            if (Current != null && CurrentPath != null) Current.Write(PackId, CurrentPath);
        }

        /// <summary>The player changed a setting: keep it now, whether or not the game is saved.</summary>
        public static void SettingsChanged()
        {
            if (Current != null && CurrentPath != null) Current.WriteSettings(PackId, CurrentPath);
        }

        /// <summary>
        /// The player moved the calendar to another season or day. The days are otherwise kept only when the game saves,
        /// but this is the player's choice, like a setting, so it is kept at once (with the days already saved).
        /// </summary>
        public static void SeasonChosen()
        {
            if (Current == null || CurrentPath == null) return;
            var saved = Calendar.Read(PackId, CurrentPath);
            if (saved == null) { Current.Write(PackId, CurrentPath); return; }
            saved.Season = Current.Season;
            saved.DayOfSeason = Current.DayOfSeason;
            saved.Settings = Current.Settings;
            saved.Write(PackId, CurrentPath);
        }

        /// <summary>Whether the played household is on its own lot. Days only count there, as Life Stages' do.</summary>
        public static bool AtHome(VM vm)
        {
            var family = vm?.TS1State?.CurrentFamily;
            return family != null && family.HouseNumber != 0 && family.HouseNumber == vm.GetGlobalValue(10);
        }

        // ---- a lot ----

        public static Climate ClimateOf(VM vm)
        {
            var terrain = vm?.Context?.Architecture?.Terrain;
            return terrain == null ? Climate.Seasonal : Forecast.ClimateOf(terrain.LightType, terrain.DarkType);
        }

        /// <summary>The weather the calendar gives this lot now.</summary>
        public static WeatherKind WeatherFor(VM vm, Calendar calendar)
        {
            var kind = Forecast.At(calendar.Seed, calendar.Day, Forecast.SpellOf(vm.Context.Clock.Hours), calendar.Season, Odds);
            return Forecast.OnLot(kind, ClimateOf(vm));
        }

        /// <summary>The weather the lot has now (whoever set it).</summary>
        public static void WeatherOn(VMContext context, out bool raining, out bool snowing)
        {
            var weather = context?.Blueprint?.Weather;
            if (weather == null) { raining = snowing = false; return; }
            Forecast.Read(weather.WeatherData, out raining, out snowing);
        }

        /// <summary>Whether it is cold outside on this lot.</summary>
        public static bool ColdOutside(VMContext context, Calendar calendar)
        {
            WeatherOn(context, out _, out var snowing);
            if (snowing) return true;
            switch (ClimateOf(context?.VM))
            {
                case Climate.Snowy: return true;
                case Climate.Fair: return false;
                default: return calendar?.Season == Season.Winter;
            }
        }

        private class LotWatch
        {
            public DayWatch Clock = new DayWatch();
            /// <summary>The ground last asked of the lot's terrain: so it is redrawn when it changes, and not otherwise.</summary>
            public (Season? season, bool snowing)? Ground;
        }

        private static readonly ConditionalWeakTable<VM, LotWatch> Watches = new ConditionalWeakTable<VM, LotWatch>();

        /// <summary>Set by the pack if it could patch the ground's drawing.</summary>
        public static bool GroundPatched;

        /// <summary>Called after every tick of every lot.</summary>
        public static void AfterTick(VM vm)
        {
            if (vm == null || !vm.TS1 || vm.Context?.Architecture == null) return;
            var calendar = Calendar;
            if (calendar == null) return;
            var watch = Watches.GetValue(vm, x => new LotWatch());

            // a day
            if (watch.Clock.Feed(vm.Context.Clock.Hours))
            {
                var began = calendar.AddDay(AtHome(vm));
                if (began != null)
                {
                    Log.Write(SeasonInfo.Name(began.Value) + " has begun (day " + calendar.Day + ").");
                    Announce(vm, SeasonInfo.Name(began.Value), Arrival(began.Value, calendar));
                }
            }
            Show(vm, calendar, watch);
        }

        /// <summary>
        /// Show the lot what the calendar says now: its weather and its ground. After every tick, and at once when the
        /// player changes something from the console, which is often done with the game paused (and no ticks).
        /// </summary>
        public static void ShowNow(VM vm)
        {
            var calendar = Calendar;
            if (vm?.Context?.Architecture == null || calendar == null) return;
            var watch = Watches.GetValue(vm, x => new LotWatch());
            watch.Ground = null; //redraw the ground whatever it was
            Show(vm, calendar, watch);
        }

        private static void Show(VM vm, Calendar calendar, LotWatch watch)
        {
            // the weather
            var weather = vm.Context.Blueprint?.Weather;
            if (weather != null && !calendar.Settings.PlayerWeather)
            {
                var wanted = Forecast.Data(WeatherFor(vm, calendar));
                if (weather.WeatherData != wanted) weather.SetWeather(wanted);
            }

            // the ground
            var terrain = vm.Context.Blueprint?.Terrain;
            if (terrain != null && GroundPatched)
            {
                WeatherOn(vm.Context, out _, out var snowing);
                var ground = (GroundSeason(calendar, ClimateOf(vm)), snowing);
                if (watch.Ground != ground)
                {
                    watch.Ground = ground;
                    Redraw(terrain);
                }
            }
        }

        /// <summary>What the player is told as a season begins: how long it lasts, its odds, and what it does with the settings as they are.</summary>
        public static string Arrival(Season season, Calendar calendar)
        {
            var settings = calendar.Settings;
            var length = settings.SeasonDays == 1 ? "a day" : settings.SeasonDays + " days";
            var text = SeasonInfo.Name(season) + " has come, for " + length + ". The weather: " + Odds.Summary(season) + ".";
            if (settings.Ground && season == Season.Autumn) text += " The grass turns brown.";
            if (settings.Ground && season == Season.Winter) text += " Snow lies on the ground.";
            if (settings.Ground && season == Season.Spring) text += " The snow is gone.";
            if (settings.Chill && season == Season.Winter) text += " Sims who stay outside get cold, and lose needs faster.";
            return text;
        }

        // ---- the ground: see SeasonsPack for the patches ----

        /// <summary>The season that should change a lot's ground: null if it should not (the player's setting, or a lot above the clouds).</summary>
        public static Season? GroundSeason(Calendar calendar, Climate climate)
        {
            if (calendar == null || !calendar.Settings.Ground || climate == Climate.Fair) return null;
            return calendar.Season;
        }

        /// <summary>A terrain component's own ground: the types the lot gave it, before any season.</summary>
        public class OwnGround
        {
            public TerrainType Light = TerrainType.GRASS;
            public TerrainType Dark = TerrainType.GRASS;
        }

        public static readonly ConditionalWeakTable<TerrainComponent, OwnGround> Own = new ConditionalWeakTable<TerrainComponent, OwnGround>();

        /// <summary>The two terrain types a terrain component should show, or null to leave it as it is.</summary>
        public static (TerrainType light, TerrainType dark)? GroundFor(TerrainComponent terrain, Blueprint bp)
        {
            if (!Own.TryGetValue(terrain, out var own)) return null; //not yet given a lot's ground
            var climate = Forecast.ClimateOf(own.Light, own.Dark);
            var snowing = false;
            if (bp?.Weather != null) Forecast.Read(bp.Weather.WeatherData, out _, out snowing);
            var season = GroundSeason(Calendar, climate);
            return (Ground.For(own.Light, season, snowing), Ground.For(own.Dark, season, snowing));
        }

        /// <summary>Show the ground again, as Simitone does when snow starts.</summary>
        public static void Redraw(TerrainComponent terrain)
        {
            terrain.UpdateLotType();
            terrain.TerrainDirty = true;
        }

        // ---- wet and cold Sims: see SeasonsPack for the patch ----

        private static readonly ConditionalWeakTable<VMAvatar, Chill.State> Chills = new ConditionalWeakTable<VMAvatar, Chill.State>();

        public static Chill.State ChillOf(VMAvatar avatar) => Chills.TryGetValue(avatar, out var state) ? state : null;

        /// <summary>What the game's decay of a Sim's needs looked like before it ran: null if it is not about to run.</summary>
        public class Before
        {
            public int LastMinute;
            public int[] Exact = new int[7];
        }

        public static Before BeforeDecay(VMTS1MotiveDecay decay, VMAvatar avatar, VMContext context)
        {
            if (avatar == null || context == null) return null;
            var calendar = Current; //(not read from the file here: this runs for every Sim on every tick)
            if (calendar == null || !calendar.Settings.Chill) return null;
            //the game decays needs once every two game minutes (VMTS1MotiveDecay.Tick)
            if (context.Clock.Minutes / 2 == decay.LastMinute || avatar.GetValue(VMStackObjectVariable.Hidden) > 0) return null;
            if (avatar.GetPersonData(VMPersonDataVariable.IsGhost) != 0) return null;
            var before = new Before() { LastMinute = decay.LastMinute };
            for (int i = 0; i < Chill.Needs.Length; i++) before.Exact[i] = Chill.Exact(avatar.GetMotiveData(Chill.Needs[i]), decay.MotiveFractions[i]);
            return before;
        }

        public static void AfterDecay(VMTS1MotiveDecay decay, VMAvatar avatar, VMContext context, Before before)
        {
            if (before == null || decay.LastMinute == before.LastMinute) return; //the game's decay did not run after all
            var state = Chills.GetValue(avatar, x => new Chill.State());
            var outside = avatar.Position != LotTilePos.OUT_OF_WORLD && IsOutside(context, avatar);
            WeatherOn(context, out var raining, out _);
            state.Update(outside, raining, ColdOutside(context, Current), 2);
            if (!state.Wet && !state.Cold) return;
            for (int i = 0; i < Chill.Needs.Length; i++)
            {
                var need = Chill.Needs[i];
                var decayed = before.Exact[i] - Chill.Exact(avatar.GetMotiveData(need), decay.MotiveFractions[i]);
                var take = state.Take(i, decayed);
                if (take <= 0) continue;
                avatar.SetMotiveData(need, (short)Math.Max(-100, avatar.GetMotiveData(need) - take));
            }
        }

        public static bool IsOutside(VMContext context, VMEntity entity)
        {
            var room = context.GetRoomAt(entity.Position);
            var rooms = context.RoomInfo;
            return rooms == null || room >= rooms.Length || rooms[room].Room.IsOutside;
        }

        /// <summary>The Sims on this lot who are wet or cold now.</summary>
        public static List<string> Chilled(VM vm)
        {
            var result = new List<string>();
            if (vm?.Context?.ObjectQueries?.Avatars == null) return result;
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                if (!(entity is VMAvatar avatar) || avatar.Dead) continue;
                var state = ChillOf(avatar);
                if (state == null || (!state.Wet && !state.Cold)) continue;
                result.Add(avatar.Name + " (" + (state.Wet && state.Cold ? "wet and cold" : state.Wet ? "wet" : "cold") + ")");
            }
            return result;
        }
    }
}
