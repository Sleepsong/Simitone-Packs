# Moodlets

Moodlets, after The Sims 3, for The Sims 1: things that happen give a Sim a mood boost or a mood penalty
for a while, such as Just Married, Promoted or Burnt Food. They are shown beside the needs.

**This is a first stage, and it has not been run in the game.** It builds against Simitone, its patches
fit the engine, and its rules are tested (below), but nobody has yet watched a Sim get a moodlet with it.
Back up your Simitone user directory before you switch it on.

## The moodlets

| Moodlet | Mood | Lasts | Given when |
|---|---|---|---|
| Just Married | +20 | 2 days | An adult moves into the household after a proposal. To both of them. Needs the game in English (below). |
| New Baby | +15 | 2 days | A baby is born into the household. To everyone in it. |
| Promoted | +12 | 1 day | A grown Sim's job goes up a level. |
| New Job | +6 | 12 hours | A grown Sim takes a job, or another job. |
| Demoted | -12 | 1 day | A grown Sim's job goes down a level. |
| Fired | -15 | 1 day | A grown Sim's job is gone. |
| Burnt Food | -8 | 4 hours | A Sim's cooking catches fire: their own interaction sets an appliance alight. To the cook. |
| Fire! | -12 | 6 hours | There is a fire. To everyone of the household on the lot (not those away at work). |
| Lost a Loved One | -20 | 3 days | The game's scripts take someone out of the household for good, as when a Sim dies or a child is taken away. To everyone left. |

The four job moodlets replace each other: a Sim fired the day after a promotion is no longer happy about
the promotion. Any other moodlet given again starts again from the beginning, as in The Sims 3.

Only the people of the household you are playing have moodlets, as only they age with the Life Stages
pack. A moodlet's hours are hours of game time played: a Sim who is not on the lot being played (the rest
of the family while one is Downtown, or a household you are not playing) keeps theirs until they are.

## How strong they are

The game works out a Sim's mood in one place: every two minutes of game time it adds up the seven needs and
the room, each from -100 to 100, and divides by eight. The pack adds the Sim's moodlets to that. So one
point from a moodlet is as much as every need moving one point, or one need moving eight. One need going
from empty to full moves mood by 25: Promoted, +12, is about half of that.

All of a Sim's moodlets together add at most **+25** and take at most **-25** (the cap). Needs still
matter most: a starving, exhausted Sim who was just married is still unhappy.

The values are the pack's own, scaled for The Sims 1, and not The Sims 3's. In The Sims 3 moodlets are the
whole of a Sim's mood (Just Married is +50 for two days there), and a Sim has many at once from their needs.
Here the needs already make the mood, and the moodlets ride on top.

### What this changes in play

This is the point of moodlets, and the reason they need tuning: the rest of the game reads the mood the
pack changes.

- **Free will.** The engine's choice of what a Sim does by themselves (the game's "find best interaction",
  `VMFindBestAction` in Simitone) counts mood as one of nine things a Sim wants. It scores what an interaction would *change*, so a mood that is a little
  higher or lower throughout changes the choice less than it might seem.
- **The game's own scripts.** The Sims 1's objects check mood in their scripts. From what is known of the
  game, that is how willing a Sim is to accept a social, whether they are in the mood to study or work out,
  and how a day at work goes. These scripts are in the game's files, not in Simitone, and have not been
  read for this pack: which checks there are, and where their lines fall, is the first thing to find out
  in play.
- **What you see.** The mood shown on the live mode button includes the moodlets.

There are loops to watch for. A promotion makes a Sim happier, a happy Sim may do better at work, and that
can bring the next promotion sooner. Being fired, or losing someone, works the other way. The cap is what
keeps these short.

Switching the pack off leaves nothing behind in the mood: the game works it out again from the needs
within two minutes of game time.

## Tuning

The values are in `moodlets.json` in the pack's folder:

```json
{
  "cap": 25,
  "moodlets": {
    "promoted": { "name": "Promoted", "text": "Moved up at work.", "mood": 12, "hours": 24, "group": "job" }
  }
}
```

A moodlet the file leaves out keeps the pack's own values. `mood` is from -100 to 100 and `hours` from 1 to
336. Moodlets of the same `group` replace each other. A moodlet the file adds of its own can be given from
the console, though nothing in the game sets it off. Restart Simitone after changing the file by hand.

Or tune in the game, from the console, and see the effect at once: `moodlet tune promoted 15` (or with
hours, `moodlet tune promoted 15 36`) and `moodlet cap 30`. These are written to `moodlets.json` straight
away. An update of the pack brings its own `moodlets.json`, so keep a copy of yours.

To try one out: select a Sim, `moodlet give promoted`, and watch their mood and what they do.

## Seeing them

Open the **Needs** panel in live mode. To the right of the eight bars are the selected Sim's moodlets, under
the title **Moodlets** and what they add to mood together. Each is a chip with its mood, green for a boost
and orange for a penalty, its name and the time it has left, with a line along its foot that shortens as
it runs out. Point at one for what it is about; on a touch screen, tap it.

How many fit depends on the width of Simitone's window, since the panel has what the eight bars leave:

| Window width | Shown |
|---|---|
| 1920 | Four columns of three, with names |
| 1600 | Two columns, with names |
| 1366 | One column, with names |
| 1280 | Two columns of just the mood (point at one for its name) |
| 1180 or less | No room: the console still has them |

These are for Simitone on a computer. With the touch screen interface the live panel is 100 wider, and so is
the room.

If there are more than fit, the last place says how many more, and pointing at it names them.

## The console

Open the cheat console (Ctrl+Shift+C):

| Type | To |
|---|---|
| `moodlet` | See everyone in your household: their moodlets, the time left, and what they add to mood |
| `moodlet kinds` | See every moodlet there is, how strong, and for how long |
| `moodlet give promoted` | Give the selected Sim a moodlet, to see what it does |
| `moodlet clear` | Take the selected Sim's moodlets away. `moodlet clear fire` takes one kind |
| `moodlet tune promoted 15 36` | Make a moodlet stronger or weaker, longer or shorter (above) |
| `moodlet cap 30` | Change the most all of a Sim's moodlets together can add or take |
| `moodlet off` / `moodlet on` | Stop and start moodlets. While off they add nothing and none are given; the ones Sims have wait |
| `moodlet help` | See this list in the game |

`give` and `clear` are for the selected Sim. To name one instead, put the name last:
`moodlet give promoted Bella`.

## Limits

- **Nothing tells you a moodlet has come**, as The Sims 3 does. The game's own message usually does (a
  promotion, a wedding), and the panel and `Moodlets.log` show it.
- **No moodlets from needs.** The Sims 3's Hungry, Tired and the rest are left out: in The Sims 1 the needs
  are already the mood, and they would count twice.
- **Just Married needs the game in English.** The Sims 1's scripts have no marriage: a proposal brings the
  other Sim into the household. The pack knows it was a proposal from the interaction's name, as the
  Family Tree pack does. In another language the newcomer joins with no moodlet.
- **Burnt Food is a guess at what the stove does.** The pack gives it when a Sim's own interaction sets an
  appliance alight, which is what happens when cooking goes wrong. The game's files have not been read to
  check that no other appliance catches fire under a Sim's hands.
- **Fire! follows the game trying to start a fire.** Simitone's fire uses The Sims Online's fire object,
  which its own code notes may be different in The Sims 1. If fires do not show in your Simitone, the
  moodlets come anyway.
- **Lost a Loved One is any Sim taken out of the household.** The pack cannot tell a death from a child
  being taken away, and both are sad. That the game's scripts do this when a Sim dies or a child leaves is
  what the pack expects; it has not been seen in play.
- **Fired is any job lost.** The game keeps no reason, so if an expansion lets a Sim quit, quitting counts.
- **Visitors and townies have no moodlets**, even at a fire.
- The panel shares the Needs panel's width with nothing else, and on a narrow screen there is none to share.

## Your saves

The pack keeps its own file for each neighbourhood, beside Simitone's saves:
`LocalHouse/PackData/sample.moodlets/` in the user directory. Moodlets are written when the game saves, so
if you leave a lot without saving, those that came and went there are forgotten with it. A trip Downtown or
on Vacation is not leaving the lot. The `on` and `off` setting is kept at once.

The game's own files are not changed.

## How it works

The engine is not changed. The pack patches it with Harmony in four places it needs:

1. **When the game has worked out a Sim's mood** (`VMTS1MotiveDecay.Tick`), it adds their moodlets. It knows
   the game counted the needs that tick because the two-minute step the game keeps (`LastMinute`) moved.
   Since the game starts again from the needs each time, nothing builds up.
2. **Each tick of a lot**, it counts the minutes the moodlets have left, and every few seconds looks at the
   Sims' jobs.
3. **When the game loads a neighbourhood**, it forgets the moodlets of the one before.
4. **When the game saves**, it writes its own file.

and some it can do without, so a version of Simitone they do not fit costs only what they do:

5. **When a lot is left**, it reads its file again, so moodlets that were not saved are dropped.
6. **When the live panel shows the Needs panel**, it adds the moodlets to it.
7. **When the game adds a Sim to the household or takes one out** (`VMGenericTS1Call`): Just Married and Lost a
   Loved One.
8. **When a script makes an object** that is a baby (an object whose own scripts can make a Sim, as the Family
   Tree pack finds it): New Baby.
9. **When the game's burn primitive sets something alight** (`VMBurn`): Fire! and Burnt Food.

| File | Holds |
|---|---|
| `Tuning.cs` | The moodlets there are, and reading and writing `moodlets.json` |
| `Rules.cs` | The rules: giving a moodlet, time passing, the cap, the clock, and what a change of job is |
| `Ledger.cs` | What is remembered for a neighbourhood, and the log |
| `Moods.cs` | What is done in the game: who has moodlets, time passing, and the mood |
| `Events.cs` | What sets moodlets off |
| `MoodletView.cs` | What the panel shows: its words, and where each moodlet goes |
| `UIMoodletPanel.cs` | Drawing the panel, in Simitone's UI colours |
| `MoodletsPack.cs` | The patches and the console |

What it does goes to the terminal and to `Moodlets.log` in the user directory, in lines starting
`[moodlet]`: each moodlet given and worn off, each job change, and each Sim who joins or leaves the
household, with the interaction that did it.

## Tests

```bash
dotnet run --project Moodlets/tests
```

They need no copy of The Sims. They check that the pack's patches fit the engine it is built against, that
the engine still works out mood where the pack adds to it, and the pack's own rules: giving moodlets, time
running out, the cap, changes of job, the files, the console's answers, and what the panel shows at each
screen width. They do not load a lot or a Sim, so they say nothing about how it looks or plays.
