# Chemistry

Turn-ons, turn-offs and chemistry, after The Sims 2: Nightlife. Each grown Sim has two turn-ons and
one turn-off, and how much a social raises how one Sim feels about another depends on the chemistry
between them.

**It changes the balance of the game, so it is opt-in.** It is a pack of its own, not part of
Simitone, and nothing changes until you install it and switch it on. Switching it off and restarting
puts the game's own gains back. Scores your Sims already have stay as they are.

**Where it stands.** Version 0.1 is a first stage and has not yet been run against a game. The rules,
the file and the patched Relationship primitive are checked by the pack's tests (below), on made-up
Sims. Whether The Sims 1's own social scripts reach the patch as the engine suggests they do has not
yet been seen in play. `chemistry gains` in the console counts the gains the pack has changed, so a
few socials show at once whether it is working.

## What it does

### Turn-ons and turn-offs

Every grown Sim has two turn-ons and a turn-off, chosen from these 24 traits:

| Kind | Traits |
|---|---|
| Personality (7 points or more, or 3 and under) | neat / sloppy, outgoing / shy, active / lazy, playful / serious, nice / grouchy |
| Skills (5 points or more) | good cook, charismatic, mechanical, creative, athletic (Body), logical |
| Work | hard worker (has a job), unemployed |
| Build | fit, fat |
| Hygiene (a quarter of the bar or less) | stink |
| What they are wearing | swimwear, formal wear, sleepwear |

The Sims 2 also had hair colours, glasses, make-up, jewellery and the like. The Sims 1 keeps none of
those where the pack can read them, so they are left out.

The two turn-ons are never opposites (nobody is both neat and sloppy), and the turn-off is never one
of them. A Sim gets their turn-ons the first time they are needed, picked at random but always the
same ones for the same Sim. You can change them in the console (below). There is no Create a Sim
screen for them yet.

### Chemistry

Chemistry goes one way. How Ann feels about Bob comes from Ann's turn-ons and Bob's traits:

| Bob has, of Ann's | Ann feels |
|---|---|
| her turn-off, and at most one turn-on | Turned off |
| nothing, or the turn-off and both turn-ons | No chemistry |
| one turn-on, and not the turn-off | 1 bolt |
| both turn-ons, and not the turn-off | 2 bolts |
| both turn-ons, not the turn-off, and Bob has at least one bolt for Ann | 3 bolts |

Traits are read as they are at that moment. A Sim who stinks or has changed into swimwear has that
trait until they wash or change.

### What chemistry changes

When a social raises how one Sim feels about another, the rise is multiplied by how the first Sim
feels about the second:

| Chemistry | Gain |
|---|---|
| Turned off | x0.5 |
| No chemistry | x1 (the game's own) |
| 1 bolt | x1.25 |
| 2 bolts | x1.5 |
| 3 bolts | x2 |

This applies to both the daily and the lifetime score. Ann's gain is scaled by her chemistry with Bob,
and Bob's by his with her, so a social can raise them by different amounts.

Rises come in whole points. Where a multiple leaves part of a point (+3 at half is +1.5), the extra
point is given with that chance, so on average the gain is exactly the multiple. The chance is drawn
from the game's own random numbers.

The game's own numbers are kept for:

- **losses**, and anything else that is not a rise;
- **children**, and pets and ghosts. The [Life Stages](../LifeStages) pack's teens are children to the
  game, so they have no chemistry either, not even with each other;
- **anything the two Sims are not doing together**: the overnight drop in daily scores, starting scores
  set by a pack such as [Family Tree](../FamilyTree), and what one Sim says about a third;
- **group activities at an object**, such as a hot tub or a chess table, and **phone calls**. Only an
  interaction one Sim runs on the other counts;
- **everything else in the relationship**, such as whether two Sims are friends or in love. The game
  keeps those in the same list as the scores, but they are not amounts.

The numbers can be changed without building the pack again: they are in `chemistry.json` in the pack's
folder, read when Simitone starts. Each is between 0 and 4.

```json
{
  "turnedOff": 0.5,
  "none": 1.0,
  "oneBolt": 1.25,
  "twoBolts": 1.5,
  "threeBolts": 2.0
}
```

**How much it changes overall.** The tests estimate it for made-up Sims (personality spread at random
as Create a Sim asks, skills of 0 to 7 points, two in three with a job, one in five stinking, one in
ten in swimwear, formal wear or pyjamas). About a quarter of pairs come out turned off, two in five
with no chemistry, three in ten with one bolt, and one in twenty with two or three. The average gain
is about x0.98, so friendships form at about the same pace overall but much faster or slower for
particular pairs. That is an estimate from made-up Sims, not a measurement from play.

## Seeing it

- **The relationship panel.** Under each Sim's bars, how the Sim whose panel it is feels about them:
  "Turned off", "1 bolt" and so on. Nothing is shown for no chemistry. For a Sim who is not on the lot
  it counts only what does not change from hour to hour: personality, skills, job and build. The
  label's place in the panel has not yet been seen in the game.
- **The console** (Ctrl+Shift+C):

| Command | Does |
|---|---|
| `chemistry` | The selected Sim's turn-ons and turn-off, and how they feel about every other grown Sim on the lot, and how each feels back |
| `chemistry set good-cook outgoing stink` | Choose the selected Sim's two turn-ons, then their turn-off |
| `chemistry traits` | The words for the traits |
| `chemistry gains` | The multipliers in use, and how many gains have been changed since Simitone started |
| `chemistry log on` / `off` | Write every changed gain to the terminal, in lines starting `[chemistry]` |
| `chemistry help` | These |

A name after `chemistry`, or after `chemistry set` and its three traits, is for that Sim instead of the
selected one: `chemistry Bella`, `chemistry set neat nice lazy Bella`.

## Your saves

The pack does not change the game's files. Turn-ons are kept in a file of the pack's own,
`Chemistry.txt`, in the neighbourhood's folder beside `Neighborhood.iff`, one Sim to a line: their
character's GUID, two turn-ons, then the turn-off. It is written when a lot is saved and when you
change a Sim's turn-ons. You can edit it with the game closed. A line it cannot read is skipped, and
that Sim gets their usual turn-ons again.

Relationship scores are the game's own, saved as always. With the pack switched off they stay where
chemistry left them, and from then on change as the game has them change.

Simitone remembers which packs a lot was saved with, and warns if you open it without one. For this
pack the warning can be ignored: it leaves nothing in the lot that would be lost.

## How it works

The engine is not changed. The pack patches it with Harmony in two places:

1. **The Relationship primitive** (`VMRelationship.Execute`), which every change to a score goes
   through, whether a script adds to a score or works out a new one and sets it. Before it runs, the
   pack finds the score it will change, as the primitive itself finds it (the neighbourhood's scores
   for Sims in the neighbourhood, otherwise the lot's own), and notes it. If the score has risen
   afterwards, between two grown Sims, while one of them is running an interaction on the other, the
   rise is scaled. This one is needed. Scripts reach it whether the game runs them itself or from the
   scripts Simitone compiles ahead of time (its `jit` option), which call the same primitive.
2. **The relationship panel** (`UIRelationshipDisplay`): the label under each Sim. If this does not
   fit a version of Simitone, the console still tells you.

| File | Holds |
|---|---|
| `Traits.cs` | The traits, and which a Sim has |
| `Attraction.cs` | The rules: turn-ons, chemistry, scaling a gain, and the tuning |
| `LikesStore.cs` | The neighbourhood's file of turn-ons |
| `Sims.cs` | What the game knows about a Sim: who is grown, their traits, chemistry between two |
| `Gains.cs` | The patch on the Relationship primitive |
| `ChemistryPack.cs` | Loading, the relationship panel, and the console |

## Limits

- **Not yet seen in play** (above). The pack's reading is that a Sim's social interactions run while
  their interaction is on the other Sim. If some of The Sims 1's socials change scores another way,
  those are left at the game's own numbers.
- **Grown Sims only.** Teens from Life Stages have no turn-ons yet.
- **Turn-ons are only chosen in the console** for now, not in Create a Sim.
- **Chemistry does not change who accepts what.** In The Sims 2 it also made romantic interactions
  more or less likely to be accepted. This pack changes only how much a social raises the scores.
- **Only rises are scaled.** A turned-off Sim does not lose more from a bad social.
- A gain the game cuts off at 100 is scaled from what it gave, so a turned-off Sim near the top gains a
  little less than half of what the script meant.

## Tests

```bash
dotnet run --project Chemistry/tests -c Release
```

They need no copy of The Sims. They check that the patches fit the Simitone being built against, the
rules of traits and chemistry, scaling a gain, the tuning and the file. They also run the game's own
Relationship primitive with the patch on for made-up Sims in a made-up neighbourhood: both ways
between two Sims, from either Sim's interaction, the old and new forms of the primitive, the
neighbourhood's scores and the lot's own, Sims named by neighbour number, losses, the friend and love
flags, the cap at 100, children, Sims busy with someone else, and the console.
