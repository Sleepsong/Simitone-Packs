# Apartments (prototype)

Several households on one lot. You play one of them. The others live there too, as neighbours the
game runs by itself, and you can change which household you play.

**This is a prototype of the first two stages.** Households can share a lot, each can be given rooms
as its apartment, and the played household pays rent on its own. Nothing stops a Sim walking into
someone else's apartment yet, and bills are still the game's own. It also exists to answer the
question the rest depends on: what do The Sims' own scripts make of Sims who live on a lot without
being played? Everything it sees is written to a log, and the next stages are built on what the log
shows.

**Back up your `UserData` folder before trying it.** It has been compiled and its patches checked
against the engine, but it has not been run in the game, and its window has not been seen on a
screen. If the window does not come up or looks wrong, everything still works from the console.

## Using it

Open the **Apartments** window while playing a household in its own house. There are three ways in:

- **Options > Apartments** (the button after Packs),
- the **F9** key,
- `apt` in the cheat console (Ctrl+Shift+C).

The window has three pages, chosen with the buttons across its top.

**Households** lists everyone who lives on the lot.

| Button | What it does |
|---|---|
| **Add this room** (on your own household) | The room your Sim is standing in joins your apartment |
| **Options** (on a neighbouring household) | Asks what to do with them: **Play** (save, then play that household; yours become neighbours), **Give this room** (the room your Sim is in joins their apartment) or **Move out** (back to the family bin; their things stay) |
| **Make shared** (on "The room your Sim is in") | That room is nobody's apartment again |
| **-** and **+** (on "Rent") | The rent on this lot, in simoleons for each tile each day |

**Move in** lists the households with no house. **Move in** puts one on the lot beside yours; its
Sims appear by the mailbox.

**Testing** has the switches for trying things out: bring neighbours back when the game sends them
away, hold their needs up, what the game is told they are (visitors or family), and whether to write
down what they do. It also says where the log is.

What you did, and how it went, is shown along the bottom of the window. If the window is taller than
the screen, **<** and **>** turn its pages.

### The console

Everything the window does can also be typed, as `apt` followed by a command.

| Command | What it does |
|---|---|
| `apt` | Opens the window |
| `apt help` | Lists the commands |
| `apt who` | The households in this house, and which one is played |
| `apt families` | Households with no house, which can move in |
| `apt movein <number or name>` | Moves a household in beside yours |
| `apt moveout <number or name>` | Moves a neighbouring household out |
| `apt switch <number or name>` | Saves the lot, then plays that household |
| `apt claim [number or name]` | The room your Sim is standing in joins your apartment (or the named household's) |
| `apt unclaim` | The room your Sim is standing in is shared again |
| `apt units` | Each household's apartment, its rent, and anything owed |
| `apt rent <number>\|off` | The rent on this lot (1) |
| `apt keep on\|off` | Bring neighbours back when the game sends them off the lot (on) |
| `apt needs <-100 to 100>\|off` | Neighbours' needs never fall below this (50) |
| `apt treat visitor\|family` | What the game's scripts are told the neighbours are (visitor) |
| `apt watch on\|off` | Write down what neighbours do (on) |
| `apt log` | Where the log is |

Moving in and out, apartments and the rent are kept when you save the lot, like anything else you do
on it. The switches on the Testing page last until the game closes.

## Apartments and rent

An apartment is a set of rooms. Stand a Sim in a room and press **Add this room** to make it part of
your household's apartment, or **Options > Give this room** on a neighbouring household to make it
part of theirs. Every room that is nobody's is shared, and so is everywhere outdoors.

- A room has walls all the way round. Knock a wall through into the garden and it stops being a room;
  it counts as shared until it is closed again, and the log says so.
- A neighbour who is put on the lot, or brought back to it, appears in their own apartment.
- The log says where each neighbour is: their own apartment, shared space, or someone else's.

Rent is the apartment's floor area times the lot's rate, each game day, taken from the household you
are playing. A household pays nothing while it is a neighbour. If there is not enough money, what is
left is owed and is taken when there is money again; money never goes below zero for rent. A
household with no apartment pays no rent.

## What to try, and what to look for

1. Make two families. Move one into a house the usual way, and leave the other in the family bin.
2. Play the house, open the Apartments window (F9), and move the second family in from the
   **Move in** page.
3. Play for a few game days at normal speed. Then quit and look at `Apartments.log` (the **Testing**
   page says where it is).
4. On the **Households** page, press **Options** on the other household, then **Play**. Play them
   for a while.
5. Try the same with neighbours treated as **Family** (on the **Testing** page).
6. Give each household an apartment (**Add this room**, **Give this room**), and play a few more days.

The log answers these, and the answers decide how the next stage is built:

| Question | In the log |
|---|---|
| Do neighbours get sent home, and when? | `... is no longer on the lot. Last seen: ...` and how often `Brought ... back` follows |
| What do they do all day? | A line for each neighbour whenever what they are doing changes, with the room they are in |
| Does your household need to greet them, every time? | `greeted 0` staying 0, and neighbours standing outside |
| Does your household pay for what they do? | `Money: ... THAT IS A NEIGHBOUR` |
| What do bills come to, and when? | `Money:` lines naming the mailbox or the bill |
| Do neighbours stay in their own apartment, or wander into yours? | `their own apartment`, `shared space` or `THE APARTMENT OF ...` in each neighbour's line |
| Is rent taken once a day, and is the amount right? | `Rent:` lines |
| Does a household survive being switched to and back? | The lines after `=== Switching`, and whether its Sims act like a family again |

## What it does not do yet

- **Apartments are not enforced.** Neighbours can walk into yours and use what is in it, and you
  can build and buy anywhere. Locking doors and keeping build and buy mode to your own apartment
  come next, once the log shows how neighbours move about.
- **Bills are the game's own**: one bill for the whole lot, paid by whoever is played, on top of the
  rent.
- **One purse at a time.** A neighbouring household's money does not change while you play another.
  If the log shows neighbours spending yours, that is to be fixed, not intended.
- **Unplayed households do not progress.** They are held as they are: no jobs, no bills, needs kept
  up. Relationships are the exception. The game keeps those for each Sim, so they change as usual.
- A neighbour who dies would be brought back by **Bring neighbours back**. Switch it off if that
  matters to you.
- Away from home (downtown, on holiday) nothing here applies. The other households stay on the lot.

## How it works

The engine keeps one family for a lot, and all of the game's scripts expect that: money, the car
pool, bills and the burglar all go to "the" family. This pack leaves that as it is. There is still
exactly one played family, and switching changes which one it is.

**Who lives where** needs nothing new in the save. Every family in the neighbourhood file names its
own house. The game only expects one family to name a given house; here several may, and they are
that house's residents. What the file cannot say is which of them is played, which rooms are whose
and what rent is owed. Those are in the pack's own file for the lot (`LotPackData`).

An apartment is kept as one tile inside each of its rooms, not as the engine's room numbers, which
change whenever a wall is built or knocked down. Whatever room the tile is in now is the household's.

Without the pack, a shared house shows as lived in by one of its families. The others are untouched,
but cannot be reached until the pack is on again.

The rest is five Harmony patches:

1. A neighbourhood has loaded: point the game's one-family-per-house table at the played household
   of each shared house.
2. The game moves a house's family out: if others live there, the house is not empty.
3. The game has checked the played family is on the lot: put the other households on it too, and
   mark who is played.
4. The lot ticks: hold the neighbours' needs up, bring back any the game sent away, take the day's
   rent, and write down what the neighbours do. A lot that is not shared costs one lookup a tick.
5. The game takes or gives money: write down who caused it.

- `Residency.cs` is who lives where.
- `Units.cs` is the apartments, the rent, and the pack's file for a lot.
- `Neighbours.cs` is the unplayed households on the lot.
- `Actions.cs` is everything the player can do, shared by the window and the console.
- `UIApartmentsDialog.cs` is the window. It is made the way Simitone's own Packs window is, from the
  same dialog, labels and buttons, so it has no artwork of its own.
- `ApartmentsPack.cs` is the patches, the ways into the window, and the console.
