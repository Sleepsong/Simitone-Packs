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
against the engine, but it has not been run in the game.

## Using it

Everything is done from the cheat console (Ctrl+Shift+C), while playing a household in its own house.

| Command | What it does |
|---|---|
| `apt help` | Lists the commands |
| `apt who` | The households in this house, and which one is played |
| `apt families` | Households with no house, which can move in |
| `apt movein <number or name>` | Moves a household in beside yours. Its Sims appear by the mailbox |
| `apt moveout <number or name>` | Moves a neighbouring household out, back to the family bin. Their things stay |
| `apt switch <number or name>` | Saves the lot, then plays that household. Yours become neighbours |
| `apt claim [number or name]` | The room your Sim is standing in joins your apartment (or the named household's) |
| `apt unclaim` | The room your Sim is standing in is shared again |
| `apt units` | Each household's apartment, its rent, and anything owed |
| `apt rent <number>\|off` | The rent on this lot, in simoleons for each tile each day (1) |
| `apt keep on\|off` | Bring neighbours back when the game sends them off the lot (on) |
| `apt needs <-100 to 100>\|off` | Neighbours' needs never fall below this (50) |
| `apt treat visitor\|family` | What the game's scripts are told the neighbours are (visitor) |
| `apt watch on\|off` | Write down what neighbours do (on) |
| `apt log` | Where the log is |

Moving in and out, apartments and the rent are kept when you save the lot, like anything else you do
on it. The settings (`keep`, `needs`, `treat`, `watch`) last until the game closes.

## Apartments and rent

An apartment is a set of rooms. Stand a Sim in a room and use `apt claim` to make it part of your
household's apartment, or `apt claim <household>` to give it to a neighbour's. Every room that is
nobody's is shared, and so is everywhere outdoors.

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
2. Play the house, and move the second family in: `apt families`, then `apt movein <number>`.
3. Play for a few game days at normal speed. Then quit and look at `Apartments.log` (`apt log` says
   where it is).
4. Try `apt switch <number>` and play the other household for a while.
5. Try the same with `apt treat family`.
6. Give each household an apartment (`apt claim`), and play a few more days.

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
- A neighbour who dies would be brought back by `apt keep`. Switch it off if that matters to you.
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
- `ApartmentsPack.cs` is the patches and the console.
