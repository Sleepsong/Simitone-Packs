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
| Elder | for good | Still an adult to the game. You choose an elder's head as they become one. |

The lengths are The Sims 2's. A baby is still the game's own bassinet, and becomes a child as it always
has; from there the pack takes over.

- **Who ages.** The people of the household you are playing, while they are on their own lot. Pets,
  neighbours, townies and households you are not playing stay as they are, as in The Sims 2.
- **When.** A day is lived at each midnight on the home lot. A Sim who has lived a stage out moves on at
  six that evening, and a message says so. If the household is out at six, it happens at the first hour
  of an evening they are home.
- **Sims you already have** start at the beginning of the stage they are in: a child is on the first
  day of childhood, an adult on the first day of adulthood.
- **Growing up** gives a child an adult's body, of the same sex and skin colour, picked by chance from
  the ones the game has, and then lets you choose their head (below). The Sims has no grown-up version
  of a child's face, so the adult does not look like the child did. Their name, skills, personality,
  relationships and needs carry over.
- **Choosing a head.** When a Sim becomes an adult or an elder, a head picker opens once the birthday
  message is closed, like the game's dresser: drag to turn the ring of heads, then OK. The head the Sim
  has is first (for a new adult, one picked by chance), so Cancel, or OK on it, keeps it. A new adult is
  offered the adults' heads and a new elder the elders' heads (below), in the Sim's sex and skin colour.
  `age look` in the console opens it again for any grown Sim.
- **Sims from a household you played before** keep their stage when they visit: a teen is still drawn
  as a teen. They do not get older while you play someone else.

## Elders' heads

The Sims has only adult heads, some of which look old. Which are for elders is up to you, in
`heads.json` in the pack's folder:

```json
{
  "elder": ["c005ma_example"],
  "both": ["c010ma_helmet"]
}
```

- **elder**: heads for elders only. They are not offered for adults in Create a Sim, and a child who
  grows up is not given one.
- **both**: heads for adults and elders, such as helmets and bald heads.

A head's name is shown under the panel in Create a Sim while you scroll the heads, and under the heads
in the head picker. One name covers every skin colour of a head, and capitals do not matter. Restart
Simitone after changing the file. While both lists are empty, elders are offered every adult head.

## Making Sims of each age

In Create a Sim the two age buttons are four, in the order of a life: **child** and **teen** above,
**adult** and **elder** below. The stage chosen is named under them.

- A teen is made from a child's heads and bodies, and an elder from an adult's: The Sims has no others.
  Teens are shown larger than children, as they will be in the game.
- Elders are offered the elders' heads, and adults every head but those for elders only (see Elders'
  heads). The names of the head and body shown are under the panel.
- In the family being made, a teen stands larger than a child.
- Each Sim starts on the first day of their stage. The first time the family is played, the teens take
  on a teen's age and size and the elders an elder's.

If this part of the pack does not fit your version of Simitone, Create a Sim stays as it was, with only
child and adult, and the rest of the pack still works. `LifeStages.log` says so.

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
| `age` | See everyone in your household: stage, day, and days left |
| `age up` | Move a Sim to the next stage now |
| `age look` | Choose another head for a grown Sim, in the head picker: from the adults' heads for an adult, the elders' for an elder |
| `age day 5` | Put a Sim on a day of the stage they are in, for example to spread out a family's birthdays |
| `age days` | See how long the stages last. `age days adult 40` changes one |
| `age off` / `age on` | Stop and start the days counting. Nothing is forgotten while it is off |
| `age teens off` | Have children grow straight into adults |
| `age help` | See this list in the game |

`age up`, `age look` and `age day` are for the selected Sim. To name one instead, put the name last:
`age up Bella`, `age day 5 Bella`.

The settings are kept for each neighbourhood, as soon as you change them.


## Limits

- **Nobody dies of old age.** An elder stays an elder. Death is the next stage of this pack.
- **There are no toddlers.** A toddler needs a body, animations and furniture The Sims does not have.
  A child drawn small would still be sent to school by the game.
- **A teen is a large child.** It has a child's proportions and a child's clothes, and the game lets it
  do exactly what a child may do. Nothing is teen-only.
- **An elder is an adult with an older head, if you give them one.** They keep their job, and walk as
  they did.
- **The head picker shows the whole Sim**, as the dresser does, so the heads are small.
- **A new adult's other outfits** (formal, swimwear and so on) are the game's defaults for a new Sim.
- Growing up while in the middle of something may look wrong until the Sim starts their next action.

## Your saves

The pack keeps its own file for each neighbourhood, beside Simitone's saves:
`LocalHouse/PackData/sample.lifestages/` in the user directory. Ages are written when the game saves,
so if you leave a lot without saving, the days played there are not counted.

It does change the game's own files in two ways, when the game saves:

- A Sim who leaves childhood has an adult's head and body written into their character file, in the
  way the game writes one when a Sim buys clothes. So does a head chosen in the head picker. This cannot
  be undone, except by choosing again.
- A teen's age (15) and size, and an elder's age (65), are written where the game keeps a Sim's age.

So if you switch the pack off: adults and elders stay grown, with the heads they were given, and
a teen stays a large child until the pack is on again. Sims who never changed stage are not written to.

## How it works

The engine is not changed. The pack patches it with Harmony in three places it needs:

1. **Each tick of a lot**, it watches the clock for midnight and for the evening, and every few seconds
   checks that each Sim it knows is at the age and size of their stage.
2. **When the game loads a neighbourhood**, it forgets the ages of the one before.
3. **When the game saves**, it writes its own file.

and some it can do without, so a version of Simitone they do not fit costs only what they do:

4. **When a lot is left**, it reads its file again, so days that were not saved are dropped.
5. **When the live panel changes what it shows**, it adds the age display to the Personality panel, in
   the space Simitone leaves empty there.
6. **In Create a Sim**, it puts four age buttons in place of two, offers each stage its heads, shows
   teens larger and the head's name, and when the family is made, writes each Sim's stage into its file.
   These patches are put on together and taken off together.

The head picker is Simitone's own outfit picker with heads in place of bodies, and needs no patch.

The game's scripts ask one thing of a Sim's age: under 18 is a child. So a teen is given 15 and an
elder 65, and every object in the game goes on working. A teen's size is the game's own size setting
for a Sim.

| File | Holds |
|---|---|
| `Stages.cs` | The rules: stages, lengths, what follows what, and the clock |
| `Ledger.cs` | What is remembered for a neighbourhood, and the log |
| `Life.cs` | What is done in the game: counting days, and putting a stage on a Sim |
| `Body.cs` | Choosing the head and body a child grows into |
| `Heads.cs` | Reading `heads.json`: which heads are for elders |
| `UIHeadPicker.cs` | The head picker |
| `AgeView.cs` | What the age display shows: its words, the bar's segments, and the figures |
| `CreateASim.cs` | Making teens and elders in Create a Sim |
| `UIAgeDisplay.cs` | Drawing the age display, in Simitone's UI colours and with figures drawn in code |
| `LifeStagesPack.cs` | The patches and the console |

What it does goes to the terminal and to `LifeStages.log` in the user directory, in lines starting
`[age]`.

## Tests

```bash
dotnet run --project LifeStages/tests
```

They need no copy of The Sims. They check that the pack's patches fit the engine it is built
against, and the pack's own rules: a whole life day by day, the clock, the file, the body chosen for a
grown child, the head lists and the heads the picker offers, and what the age display shows. They do not load a lot or a Sim, so they say nothing
about how it looks or plays.
