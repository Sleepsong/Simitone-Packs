# Life Stages

Ageing, after The Sims 2, for The Sims 1: the Sims you play grow older as the days pass.

**This is a first stage, and it has not been run in the game.** It builds against Simitone, its patches
fit the engine, and its rules are tested (below), but nobody has yet watched a Sim grow up with it.
Back up your Simitone user directory before you switch it on.

## What it does

| Stage | Lasts | What the Sim is |
|---|---|---|
| Child | 8 days | The game's own child. |
| Teen | 15 days | Still a child to the game: goes to school, and cannot take a job, marry or have a baby. Drawn larger than a child. |
| Adult | 29 days | The game's own adult. |
| Elder | for good | Still an adult to the game, with grey hair. |

The lengths are The Sims 2's. A baby is still the game's own bassinet, and becomes a child as it always
has; from there the pack takes over.

- **Who ages.** The people of the household you are playing, while they are on their own lot. Pets,
  neighbours, townies and households you are not playing stay as they are, as in The Sims 2.
- **When.** A day is lived at each midnight on the home lot. A Sim who has lived a stage out moves on at
  six that evening, and a message says so.
- **Sims you already have** start at the beginning of the stage they are in: a child is on the first
  day of childhood, an adult on the first day of adulthood.
- **Growing up** gives a child an adult's head and body, of the same sex and skin colour, picked by
  chance from the ones the game has. The Sims has no grown-up version of a child's face, so the adult
  does not look like the child did. Their name, skills, personality, relationships and needs carry over.

## Seeing a Sim's age

Open the **Personality** panel in live mode. Beside the Sim's personality is their age, after The Sims
2's age bar:

- the stage they are in,
- a bar of the stages of life, each as wide as it is long, filled as far as the Sim has lived, with a
  figure standing on each and a mark where the Sim is now,
- how many days until the next stage, and which day of this one it is.

It follows the Sim you have selected. For a pet or a visitor it says they do not age. Away from home, or
with ageing switched off, it says why the days are not counting.

## The console

Open the cheat console (Ctrl+Shift+C):

| Type | To |
|---|---|
| `age` | See the stages and days of everyone in your household at once |
| `age off` / `age on` | Stop and start the days counting. Nothing is forgotten while it is off |
| `age up` | Move the selected Sim to the next stage now. `age up Bella` for a Sim by name |
| `age days` | See how long the stages last. `age days adult 40` changes one |
| `age teens off` | Have children grow straight into adults |
| `age grey off` | Leave elders the hair they had |

The settings are kept for each neighbourhood.

## Limits

- **Nobody dies of old age.** An elder stays an elder. Death is the next stage of this pack.
- **There are no toddlers.** A toddler needs a body, animations and furniture The Sims does not have.
  A child drawn small would still be sent to school by the game.
- **A teen is a large child.** It has a child's proportions and a child's clothes, and the game lets it
  do exactly what a child may do. Nothing is teen-only.
- **An elder is an adult with grey hair.** They keep their job, and walk as they did.
- **Grey hair is a guess.** A head's hair and face are one picture, so the pack turns grey whatever in
  it is not the colour of the Sim's skin. Lips and eyes go pale with it, and hair close to the skin's own
  colour keeps some of it. `age grey off` if a head comes out badly.
- **A new adult's other outfits** (formal, swimwear and so on) are the game's defaults for a new Sim.
- Growing up while in the middle of something may look wrong until the Sim starts their next action.

## Your saves

The pack keeps its own file for each neighbourhood, beside Simitone's saves:
`LocalHouse/PackData/sample.lifestages/` in the user directory. It is written when the game saves.

It does change the game's own files in two ways, when the game saves:

- A Sim who leaves childhood has an adult's head and body written into their character file, in the
  way the game writes one when a Sim buys clothes. This cannot be undone.
- A teen's age (15) and size, and an elder's age (65), are written where the game keeps a Sim's age.

So if you switch the pack off: adults and elders stay grown, elders get their own hair colour back, and
a teen stays a large child until the pack is on again. Sims who never changed stage are not written to.

## How it works

The engine is not changed. The pack patches it in four places with Harmony:

1. **Each tick of a lot**, it watches the clock for midnight and for six in the evening.
2. **When the game looks up a skin texture**, it answers for the grey copies of heads, which it makes
   from the Sim's own head while the game runs. No picture is written to disk.
3. **When the game loads a neighbourhood**, it forgets the ages of the one before.
4. **When the live panel changes what it shows**, it adds the age display to the Personality panel, in
   the space Simitone leaves empty there. If this patch does not fit a version of Simitone, the rest of
   the pack still runs.

The game's scripts ask one thing of a Sim's age: under 18 is a child. So a teen is given 15 and an
elder 65, and every object in the game goes on working. A teen's size is the game's own size setting
for a Sim.

| File | Holds |
|---|---|
| `Stages.cs` | The rules: stages, lengths, what follows what, and the clock |
| `Ledger.cs` | What is remembered for a neighbourhood, and the log |
| `Life.cs` | What is done in the game: counting days, and putting a stage on a Sim |
| `Body.cs` | Choosing the head and body a child grows into |
| `GreyHair.cs` | The grey copy of a head |
| `AgeView.cs` | What the age display shows: its words, the bar's segments, and the figures |
| `UIAgeDisplay.cs` | Drawing the age display, in Simitone's UI colours and with figures drawn in code |
| `LifeStagesPack.cs` | The patches and the console |

What it does goes to the terminal and to `LifeStages.log` in the user directory, in lines starting
`[age]`.

## Tests

```bash
dotnet run --project LifeStages/tests
```

They need no copy of The Sims. They check that the four patches fit the engine the pack is built
against, and the pack's own rules: a whole life day by day, the clock, the file, the body chosen for a
grown child, grey hair on made-up colours, and what the age display shows. They do not load a lot or a Sim, so they say nothing
about how it looks or plays.
