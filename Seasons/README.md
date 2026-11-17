# Seasons

Spring, summer, autumn and winter for The Sims 1, after The Sims 2: Seasons. Each season has its own
odds of rain, storms and snow, the grass turns brown in autumn and is covered in snow in winter, and
Sims who are wet or cold lose their needs faster.

**This is a first stage, and it has not been run in the game.** It builds against Simitone, its patches
fit the engine, and its rules are tested (below), but nobody has yet played a year with it. Back up
your Simitone user directory before you switch it on.

It needs Simitone's built in **Weather** pack switched on: Simitone draws and plays the weather, and
this pack decides what the weather is.

## What it does

| | Spring | Summer | Autumn | Winter |
|---|---|---|---|---|
| Clear | 55% | 72% | 50% | 50% |
| Light rain | 25% | 12% | 26% | 5% |
| Heavy rain | 10% | 6% | 14% | |
| Thunderstorm | 8% | 10% | 4% | |
| Light snow | 2% | | 5% | 30% |
| Heavy snow | | | 1% | 15% |
| The ground | the lot's own | the lot's own | autumn grass | snow |
| Outside | | | | cold |

- **The calendar.** Each neighbourhood has one, so every household in it shares the seasons. A season
  lasts 7 days. A new calendar starts on the first day of spring. A day of the season passes at each
  midnight on the household's own lot, as Life Stages counts ages, and a message says when a new season begins.
- **The weather.** The percentages are the share of the time each kind of weather is about. A day is
  split into two-hour spells: each spell either carries on the weather of the one before (6 times in 10)
  or is drawn afresh from the season's odds, so the weather comes in spells rather than changing every
  two hours. The weather follows from the neighbourhood, the day and the hour alone: come back to a lot
  at the same hour, or load the save again, and the sky is the same. Each day has its own weather, on
  community lots and holidays too.
- **The ground.** The lot's grass, as the game's own autumn grass in autumn and snow in winter: the
  terrain types Terrain Paint paints with. Sand stays sand in autumn and is snow in winter. Snow also
  lies while snow falls, in any season, as it already did in Simitone. Rock, water, cloud and lots
  that are snow already stay as they are.
- **Wet Sims.** A Sim outside in the rain or hail is wet: **Comfort** drops twice as fast and
  **Hygiene** half as fast again. A Sim stays wet for an hour after coming in.
- **Cold Sims.** A Sim outside in winter, or while snow falls, is cold: **Comfort**, **Energy** and
  **Hunger** drop half as fast again. Coming in warms a Sim at once.
- Wet and cold add up: outside in winter rain, Comfort drops two and a half times as fast. Only needs
  that are dropping are touched: a Sim in a comfortable chair in the rain still gains comfort, and a
  sleeping Sim still gains energy. It applies to everyone on the lot, visitors and pets too, but not
  ghosts.
- **Lots with weather of their own.** A lot whose own ground is snow, such as Vacation Island's mountain
  lots, gets snow where others get rain, and it is cold outside there all year. A lot above the clouds
  (cloud ground, in Magic Town) has no weather and is never cold.

"Outside" is where the game's rooms say it is: anywhere not walled in. A porch with a roof and no walls
is outside.

## The console

Open the cheat console (Ctrl+Shift+C):

| Type | To |
|---|---|
| `season` | See the season and day, the weather now, and who is wet or cold |
| `season forecast` | See the weather for the rest of today |
| `season odds` | See each season's odds. `season odds winter` for one |
| `season set winter` | Move the calendar to a season. `season set winter 3` to its third day |
| `season days 10` | Change how many days a season lasts (1 to 99) |
| `season off` / `season on` | Stop and start the seasons turning. The weather still changes from day to day |
| `season ground off` | Keep each lot's own ground. Snow still lies while it falls |
| `season chill off` | Let Sims be wet and cold without it touching their needs |
| `season plants` | Write this lot's plants to `Seasons.log` (see Gardening) |
| `season help` | See this list in the game |

**Setting the weather yourself.** Simitone's `weather` command still works: `weather rain`,
`weather snow` and the rest take the weather over, and the seasons leave it alone until you type
`weather auto`, which hands it back to them.

The settings are kept for each neighbourhood, as soon as you change them. So is a season chosen with
`season set`.

## Changing the odds

The odds are in `weather.json` in the pack's folder:

```json
{
  "spring": { "clear": 55, "lightRain": 25, "heavyRain": 10, "storm": 8, "lightSnow": 2, "heavySnow": 0 },
  "winter": { "clear": 50, "lightRain": 5, "heavyRain": 0, "storm": 0, "lightSnow": 30, "heavySnow": 15 }
}
```

Each number is a share: they need not add up to 100. A season left out, or with a mistake in it, keeps
the pack's own odds, and `Seasons.log` says what was wrong. Restart Simitone after changing the file.

## Gardening

Not in this stage. The Sims has no gardening rules in the engine for the pack to change: each plant
keeps its own water and health in its own script, and they differ from plant to plant. Rain watering
the garden, and plants resting in winter, need those scripts read first, with a copy of the game.

To help with that, `season plants` writes each plant on the lot to `Seasons.log`, with its numbers.
Water a plant, type it again, and the number that changed is the plant's water.

## Limits

- **No new looks.** Sims do not change into winter clothes, carry umbrellas or build snowmen: those need
  clothes and animations The Sims does not have.
- **No temperature.** Wet and cold are all there is, and their only effect is on needs. Nobody catches
  a cold or freezes.
- **Painted ground stays as painted.** Ground painted with Terrain Paint keeps its paint in every
  season: a painted lawn stays green in winter.
- **Wetness is not saved.** A Sim who was wet when the game was saved is dry when it loads.
- **The weather is one for the whole lot.** It does not rain on one side and not the other.
- **Hail** is left out of the odds: Simitone draws it as rain, with no sound. `weather hail` still works.

## Your saves

The pack keeps its own file for each neighbourhood, beside Simitone's saves:
`LocalHouse/PackData/sample.seasons/` in the user directory. The days are written when the game saves,
so if you leave a lot without saving, the days played there are not counted. The game's own files are
not changed.

Simitone keeps the weather of the last save in `LocalHouse/cas.weather`, and puts it back as a lot
loads. While the seasons choose the weather, the calendar's weather is used instead. Switch the pack
off, and the lots show their own ground again, and the weather is whatever was last saved.

## How it works

The engine is not changed. The pack patches it with Harmony in three places it needs:

1. **Each tick of a lot**: watch the clock for midnight, and give the lot the calendar's weather.
2. **When the game loads a neighbourhood**: forget the calendar of the one before.
3. **When the game saves**: write the calendar.

and some it can do without, so a version of Simitone they do not fit costs only what they do (and
`season` says what is missing):

4. **The ground**: as the terrain is given a lot's ground, remember the lot's own types, and as it works
   out its colours, put the season's ground in their place. This stands in for Simitone's own
   "snow while it snows" too, so the snow stays down in winter when a snowfall ends.
5. **Wet and cold Sims**: around the game's own decay of a Sim's needs, take a little more off.
6. **The console's `weather` command**: notice the player taking the weather over, and handing it back.
7. **The weather a lot was saved with**: not put back while the seasons choose the weather.
8. **When a lot is left**: read the file again, so days that were not saved are dropped.

| File | Holds |
|---|---|
| `Calendar.cs` | The seasons, the calendar and its settings, the file, and the clock |
| `Forecast.cs` | The odds (and `weather.json`), the weather for each spell, lots' climates, and Simitone's weather settings |
| `Ground.cs` | Which terrain type a lot's ground shows in each season |
| `Chill.cs` | Wet and cold: how much faster needs drop, and drying off |
| `Sky.cs` | What is done in the game: counting days, setting the weather, the ground, and wet and cold Sims |
| `SeasonsPack.cs` | The patches and the console |

What it does goes to the terminal and to `Seasons.log` in the user directory, in lines starting
`[seasons]`.

## Tests

```bash
dotnet run --project Seasons/tests
```

They need no copy of The Sims. They check that the pack's patches fit the engine it is built against,
and the pack's own rules: the calendar day by day, the file, that the weather over 3,000 days comes
within 2% of its odds and comes in spells, `weather.json` and its mistakes, the ground for each season,
and how much faster needs drop when wet or cold. They also give the engine's real terrain a lot's ground
and check it shows the season's. They do not load a lot or a Sim, so they say nothing about how it looks
or plays.
