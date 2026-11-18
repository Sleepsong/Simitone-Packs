# Lot Traits

Lot traits, after The Sims 4, for The Sims 1: give a lot up to three traits, and every Sim on it finds
some needs drop faster or slower, and likes its rooms more or less.

**This is a first stage, and it has not been run in the game.** It builds against Simitone, its patch
fits the engine, and the tests (below) run the engine's own need code with the patch on, but nobody
has yet played a lot with it. It changes nothing in a lot's save, so switching it off leaves your lots
as the game has them.

## The traits

| Trait | What it does |
|---|---|
| Fresh Air | Energy drops 15% slower. Room +10. |
| Homey | Comfort drops 25% slower. Room +5. |
| Convivial | Social drops 25% slower. |
| Grody | Hygiene drops 50% faster. Room -10. |
| Mean Vibe | Social drops 25% faster, Fun 15% faster. |
| Noisy Neighbours | Energy drops 20% faster, Comfort 10% faster. |

The names are after The Sims 4's, but The Sims 1 has no moods or moodlets for a trait to give, so each
is made from what The Sims 1 has: how fast needs drop, and the Room need. Grody's Room -10 stands in for
The Sims 4's "things get dirty faster", since dirt in The Sims 1 is felt through Room. The numbers are a
first guess, to be tuned once the traits have been played.

- **Who.** Every Sim on the lot: the household, visitors, NPCs and pets.
- **How many.** Up to three on a lot, as in The Sims 4. Rates multiply: two traits that each make
  hygiene drop 50% faster make it 2.25 times as fast. Room points add up.
- **What is not changed.** A need only drops faster or slower. A Sim who sleeps gets their energy back
  at the game's own pace, and a need that an interaction is looking after (eating, bathing) is left to
  it, as the game does.

## Choosing a lot's traits

In a lot: **Options > Lot Traits**, or **F7**. Each trait has an On/Off button. Or in the console
(Ctrl+Shift+C):

```
lottraits                  this lot's traits, and what they do
lottraits all              every trait there is
lottraits add grody        give this lot a trait (by its id or name)
lottraits remove grody     take a trait off
lottraits clear            take all of them off
```

A change takes effect at once and is kept when the lot is saved, like anything else you change on it.
Leave without saving and the lot keeps the traits it had.

## Your own traits

The traits are in `traits.json` in the pack's folder. Change a trait's numbers, or add traits of your
own, and restart Simitone:

```json
{
  "id": "well-stocked",
  "name": "Well Stocked",
  "description": "There is always something in the fridge.",
  "needs": { "hunger": 0.8 },
  "room": 0
}
```

| Field | Meaning |
|---|---|
| `id` | A short name, kept with each lot that has the trait. Do not change it once in use. |
| `needs` | How fast each need drops, against the game's own rate (1): `hunger`, `comfort`, `hygiene`, `bladder`, `energy`, `fun`, `social`. 1.5 is half again as fast, 0.75 a quarter slower, 0 never. Up to 5. |
| `room` | Points added to the Room need of every Sim on the lot, from -50 to 50. |
| `most` | (At the top of the file.) How many traits one lot can have. |

A trait the file cannot use is left out, and why is written to `LotTraits.log` in the Simitone user
directory, and shown in the window. A lot that has a trait the file no longer has keeps it, unused, so
that it is back if the trait is.

## Not yet

These Sims 4 traits need more than a change to needs, and are not in this stage:

| Trait | What it would need |
|---|---|
| Gremlins (things break more often) | A way to make objects break. In The Sims 1 each object breaks in its own script, with its own chance, so this is a patch on objects' behaviour or a primitive of the pack's own, not on the engine's need code. |
| Party Place, and others about who visits | A say in who walks by and who comes to the door. That is the game's own visitor code, which would have to be read before it can be changed. |
| Chef's Kitchen, Science Lair, Great Acoustics, and others about skills | How fast skills are learned on the lot: a patch on skill gain, much like this one on needs. |
| Penny Pixies, and others about bills | The bills' amount. |
| Haunted, Quake Zone, and others that are events | Events of their own. |

## How it works

The engine is not changed. In The Sims 1 a Sim's needs drop in one place in the engine,
`VMTS1MotiveDecay.Tick`, every second game minute. The pack has a Harmony patch on it (`NeedRates.cs`)
that notes each need before the game's code runs and looks again after: whatever the game took from a
need, the patch makes that times the rate. So the game's own rules for how fast each need drops stay as
they are (hunger drains faster the fuller a Sim is, a sleeping Sim's hygiene drops slower, and so on),
and only the amount changes. It then adds the lot's Room points and works out Mood again, as the
game's code does.

**For moodlets.** This is the patch a moodlets pack needs too, and it is written so that two packs can
both have it: each scales what the game's code and the packs before it took away, so their rates
multiply and their Room points add, in whichever order they run. `NeedRates.cs` and `NeedEffect` do not
depend on lot traits; a moodlets pack can copy them and set `NeedRates.EffectFor` to its own.

Which traits a lot has is kept in the pack's own file for the lot, beside the saves, through
Simitone's `LotPackData` (see `PACKS.md` in Simitone): read when the lot loads, written when it is
saved. A lot saved with no traits has no file. So the pack needs a Simitone with `OnLotLoaded(vm,
lotPath)` and `OnLotSaved`, which the `addon-packs` branch has.

| File | What is in it |
|---|---|
| `LotTraitsPack.cs` | The patches, the lot's own file, the console, the Options button and F7 |
| `NeedRates.cs` | The need patch, and the effect of traits on needs |
| `Traits.cs` | Reading `traits.json`, and what a trait does in words |
| `Lots.cs` | Which traits each loaded lot has |
| `UILotTraitsDialog.cs` | The window |

## Tests

```bash
dotnet run --project LotTraits/tests -c Release
```

They check that the patches fit the engine; that `traits.json` is sound and a broken trait is left
out with a reason; that a need is scaled correctly through a tick, including crossing a point, the
bottom of -100, a need that rose, and two packs' patches together; that a lot's traits are kept and
read back through Simitone's own `LotPackData`; and they run the engine's own `VMTS1MotiveDecay.Tick`
for eight game hours on bare Sims with the patch on. On a Grody lot hygiene dropped 61 where the game
dropped it 40, and every other need dropped the same. They cannot check a real lot: the Sims are bare,
in no room, with no objects.
