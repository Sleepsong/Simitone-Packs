# Memories

The Sims 2's memories, for The Sims 1: a timeline for each Sim of what has happened to them. A first
kiss, a wedding, a new job and each promotion, a baby, growing up, the death of a relative, a fire, a
burglary.

On its own that is a small thing. Most of this pack is the layer under it, which watches play for
those events and tells anything that wants to know. The timeline is the first thing built on it;
Wants and Fears is meant to be the next, as another pack (below, **For other packs**).

**This is a first stage, and it has not been run in the game.** It builds against Simitone, its
patches fit the engine, and its rules are tested (below), but nobody has yet watched a Sim remember
anything with it. Much of what it sees rests on a reading of how The Sims 1's scripts do things,
which play has not yet confirmed. Everything it sees and decides is written to `Memories.log` in
Simitone's user directory, so that a session of play says what worked. Back up your Simitone user
directory before you switch it on.

## Using it

Open a Sim's memories while you play. There are three ways in:

- **Options > Memories** (after Packs, and after the Apartments and Family Tree buttons if you use
  those packs),
- the **F7** key,
- `memories` in the cheat console (Ctrl+Shift+C). `memories Bella` opens Bella's, if she is in your
  household.

The window shows the selected Sim's memories, newest first, a page at a time. Each has a mark, green
for a happy memory and red for a sad one, as The Sims 2 colours them, and under it when it happened:
the neighbourhood's day, the time, and what the Sim was then (a child, a teen...). **Older** and
**Newer** turn the pages, and **Next Sim** goes through the household.

Nothing can be changed there. Memories are made by what happens in play.

Other console commands: `memories events` lists the last life events the pack has seen, newest
first, which is the quickest way to see whether it noticed something. `memories day` gives the
neighbourhood's day. `memories help` lists them.

## What is remembered

| Memory | Who has it | How the pack knows |
|---|---|---|
| First kiss, with ... | Both Sims, the first time each kisses | An interaction called a kiss ends, and it went well (below) |
| Married ... | Both | A Sim of the household brings an adult in to live there, by a proposal |
| Got a job, Promoted, Demoted, Out of work | The Sim | Their job or its level changes |
| Had a baby (with ...) | Each parent | A baby arrives (as the Family Tree pack sees it) |
| Born to ... | The child | The baby grows into a child; the memory is dated when the baby arrived |
| Became a teen, an adult, an elder | The Sim | Their age moves to the next stage |
| Died | The Sim | They become a ghost, or leave the household as they die |
| Their father, ..., died | The family | As above. The family is the household, and with the Family Tree pack, their relatives in the tree |
| A fire at home, Started a fire | The household on the lot | A fire starts |
| A burglar broke in (and took ...) | The household | A burglar comes onto the household's own lot |

How each is seen, and how sure that is:

- **Kisses.** An interaction is a kiss when its name, or the name of the game's script that runs it,
  has "kiss" in it: not a kiss on the hand or cheek, a blown kiss or an air kiss. The script names are
  Maxis's own labels, which are in English in every copy of the game, so this should work in any
  language; that the kisses' scripts are labelled so has not been seen yet. The Sims 1 does not say
  whether a kiss was accepted, so the pack goes by the two Sims' scores: a kiss went well if either
  likes the other more after it (or both were at the top already and stayed there). A turned-down
  kiss lowers both. A couple's kiss counts once an hour, however many interactions the game runs for it.
- **Weddings.** The Sims 1 marries two Sims by moving one in: the pack watches the game add a Sim to
  the household, and takes it for a wedding if the script doing it is part of a proposal (by its name
  on screen or its scripts' labels). The Family Tree pack does the same, by the English name only.
- **Jobs and growing up** are read from the Sim: every second, each Sim on the lot is looked at, and a
  job, level or age that has moved is an event. A Sim is watched for a few seconds before anything
  counts, so arriving on a lot is not taken for a promotion. Growing up includes the Life Stages
  pack's birthdays, which change the same age; this pack does not need Life Stages, and each stage is
  remembered once.
- **Babies** are found as the Family Tree pack finds them: the baby is the object whose scripts can
  make a new Sim, and its parents are the adults of the household whose interaction made it, or one
  of them and their husband or wife. An adopted baby has no parents noted.
- **Deaths** are the least sure. The Sims 1's death is all in its scripts, and the pack has two ways of
  seeing it: the Sim's ghost flag is set, or the Sim is taken out of the household by a script whose
  labels speak of dying (death, die, the Grim Reaper, starving, drowning...). Which of these the game
  does has not been seen yet. A Sim who leaves the household any other way (a child taken away, say)
  is not counted as dead, and the log says so.
- **Fires** are seen as the game starts one; a fire that spreads is the same fire until the last flame
  is out. The Sim who started it is the one whose script did, if a Sim's did.
- **Burglars** are known by name: a Sim on the lot called a burglar, by their character's name or the
  game's label for it. What they take is what they are seen carrying when they go, and anything their
  scripts remove. The police coming is noted by name too. Only a burglary at home is remembered.

## For other packs

The layer is public: any pack can hear the same events, and read the memories.

- `LifeEvents.Happened` is raised for each life event as it happens: every kind in the table above,
  and some no memory is made of: a skill point gained, a Sim joining or leaving the household, a fire
  going out, a burglar arriving, and every interaction a Sim ends, with whom or what, its name and its
  scripts' names, and whether it went well. `LifeEventKind` in `LifeEvents.cs` says what each holds.
  `LifeEvents.Recent` keeps the last fifty.
- `MemoryBook.Of(sim)` and `MemoryBook.Has(sim, kind)` read a Sim's memories, so that a pack can ask
  whether a Sim has had their first kiss. `MemoryBook.Remembered` is raised for each new memory.
- `Recognise` holds the rules by name (`Recognise.Kiss.InAny(e.Names)`) for a listener that picks out
  interactions of its own.

Events are raised on the game thread, while the game runs the script that caused them: a listener
should be quick, and put off any change to the game with `GameThread.NextUpdate`. A listener that
throws is written to the log and the others still hear.

**Reaching it.** Simitone loads each pack's code on its own, so a pack that refers to Memories.dll
does not find it by itself. Three things are needed, all in the pack that builds on this one:

1. In its `pack.json`, `"requires": ["sample.memories"]`, so that Memories is loaded first.
2. In its `.csproj`, a reference that does not ship a copy:
   `<ProjectReference Include="..\Memories\Memories.csproj" Private="false" />`. A copy of its own
   would be a second Memories, with its own events, that nothing raises.
3. In its `OnLoad`, before anything touches a type of Memories', a few lines that hand it the copy
   Simitone loaded for this pack. They are `PackLink` in `tests/consumer/ConsumerPack.cs`, a stand-in
   for such a pack, which also shows the rest. Keep the code that uses Memories in a method of its own
   (`[MethodImpl(MethodImplOptions.NoInlining)]`), so that its types are looked for only after the
   link is made.

The checks (below) load Memories and that stand-in as Simitone loads packs, with Simitone's own
loader, and see the events and memories reach it, and that without the link it cannot find Memories.

## Limits

- **Not yet run in the game.** The rules are checked, but whether the game's scripts do what the pack
  expects is not known: what a kiss's script is called, how a death is done, what a burglar's
  character is named. `Memories.log` is where to look.
- **Only what happens on a lot you play is seen**, as in The Sims 2 with the Sims you play. A promotion
  happens when a Sim comes home from work, so it is seen; a neighbour's life between your visits is not.
- **A kiss going well is a guess** from the scores. A kiss between Sims who have no scores yet is not
  counted.
- **Deaths, burglars and the police rest on names** in the game's scripts and characters; see above.
- **Out of work** does not say fired or quit: the game does not either.
- **The neighbourhood's day** is the pack's own count, one at each midnight on any lot played, from
  when the pack was switched on. The Sims 1 has no calendar; the day tells the order of things, not a
  Sim's age.
- **Memories cannot be changed** in the game. They can in their file (below), with the game closed.
- A Sim who was a baby when the pack was switched on has no parents noted when they grow up, and no
  date of birth: their Born memory is dated when they grew up.
- The window has no portraits yet, and memories have no pictures of their own.

## Where it is kept

The memories are kept for each neighbourhood, in a file of the pack's own beside Simitone's saves:
`LocalHouse/PackData/sample.memories/` in Simitone's user directory, named after the neighbourhood's
folder (`UserData-...json`). It is written when the game saves and read again when a neighbourhood
is loaded or a lot is left, so it matches the saved game: leave a lot without saving, and what
happened there is forgotten with it. The game's own files are not changed, so a neighbourhood loads
the same with the pack switched off.

It is JSON. A memory is its kind, the Sim (the GUID of their character, in hexadecimal, as the Family
Tree pack writes them), the other Sims in it, a detail (a job's title, what a burglar took), and when.
Babies waiting to grow up are kept there too, with their parents.

The Family Tree pack's `FamilyTree.txt`, if there is one, is read to know a Sim's relatives. It is
never written.

## How it works

- `LifeEvents.cs` is the layer's public face: the kinds of event, what each holds, and the event raised.
- `Recognise.cs` is the rules by which the layer tells what happened: names, jobs, stages, whether a
  social went well, a baby's parents. It knows nothing about the game, so it is checked without it.
- `Watching.cs` is the layer's view of the game, as Harmony patches: an interaction beginning and
  ending, a script making an object and a character, the household changing, a fire starting, an
  object removed, and every tick, the clock and a look at the Sims on the lot.
- `Remembering.cs` is which events leave which memories, the Family Tree's file, and `MemoryBook`,
  which keeps the neighbourhood's journal up to date.
- `Journal.cs` is a memory, how it reads, and the file.
- `People.cs` is what the game knows about a Sim, by their GUID.
- `MemoriesPack.cs` is where it all starts: the patches, the console, the F7 key and the Options button.
- `UIMemoriesDialog.cs` is the window.

## Checking it

```bash
dotnet run --project Memories/tests -c Release
dotnet run --project Memories/tests/crosspack -c Release
```

These need no copy of The Sims. The first checks that every patch attaches to the Simitone the pack
is built against, that it uses only the pack calls every Simitone with code packs has, the rules by
name (kisses, proposals, death, burglars, the police), stages, job changes, how a social went, a
baby's parents, which memories each event leaves and how they read, the family words from the Family
Tree's file, and the journal's file, written, read back, damaged, and forgotten when a lot is left
without saving. The second loads Memories and a stand-in for a pack built on it, each as Simitone
loads a pack, and checks that an event and a memory reach the other pack. What neither can check is
the game's own scripts doing these things.
