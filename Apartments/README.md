# Apartments (prototype)

Several households on one lot. You play one of them. The others live there too, as neighbours the
game runs by itself, and you can change which household you play.

**This is a prototype.** A lot can be made an apartment building and divided into apartments;
households move into them from the neighbourhood screen; and the played household pays rent on its
own. The other households are at home, out of sight: their
apartments are dimmed, and now and then one of them comes out to the shared rooms for a while.
An apartment is private: a Sim who does not live in it cannot walk in, though yours can knock and be
asked in. Bills are still the game's own.
Everything the pack sees is written to a log, and each version is built on what the last one's log
showed.

**Back up your `UserData` folder before trying it.** It has been compiled and its patches checked
against the engine, but it has not been run in the game, and its window has not been seen on a
screen. If the window does not come up or looks wrong, everything still works from the console.

## Setting up a building

1. **Build it.** From the neighbourhood screen, enter an empty lot (or a house someone lives in) and
   build as usual. An apartment is a set of rooms, so give each one walls all the way round.
2. **Make it an apartment building.** In build mode, open the **Apartments** tab (the door, after
   the game's own tabs) and press **Make building**.
3. **Lay out the apartments.** Still on the Apartments tab, press **New**, then click inside each
   room that belongs to it, one after another. Press Esc (or the apartment's button again) when you
   have finished. Each apartment has a button in its own colour: press it to add more rooms to it.
   While an apartment's button is pressed, **Delete** deletes that apartment (its rooms become
   shared). **Take out** takes the rooms you click out of their apartment. To choose a room on another floor, change floor before
   clicking. Every room you leave out is shared by everyone, and so is outdoors.

   While build mode is open, each apartment's floor is drawn on the lot in its colour, with a line
   round it (while you choose rooms for one, the others are faint), and the room under the cursor is
   shown in white. Rooms with no colour are shared. Floors above the one you are looking at are not
   drawn. In live mode nothing is drawn.
4. **Save**, and go back to the neighbourhood.
5. **Move households in.** Choose a family to move in, as usual, and pick the building. Its panel
   says how many apartments are free. After the game's own "move in?" question you are asked which
   apartment the family takes; when only one is left, you are asked to confirm it. Moving in costs
   two days' rent, and a family that cannot afford an apartment is not offered it. (An apartment is
   rented, not bought, so the lot's price is not charged.)
6. **Play.** A household that has just moved in is told what it paid and what the rent will be.
   Entering a building where several households live asks which one you want to play. The others
   are at home, as neighbours.

A house two households already share (from an earlier version of this pack) is a building already,
and their rooms are its apartments.

## The Apartments window

Open it while on a lot. There are three ways in:

- **Options > Apartments** (the button after Packs),
- the **F9** key,
- `apt` in the cheat console (Ctrl+Shift+C).

It has four pages, chosen with the buttons across its top. What you did, and how it went, is shown
along the bottom. If the window is taller than the screen, **<** and **>** turn its pages.

**Households** lists everyone who lives on the lot, and which apartment each is in. **Options** on a
neighbouring household asks what to do with them: **Knock** (the Sim you are playing goes to their
door, to be asked in), **Play** (save, then play that household; yours go
home and become neighbours), **Bring out** (one of them comes out of their apartment now) or **Move
out** (back to the family bin; their things stay, and their apartment is vacant). The line for your
own household says what the last rent came to.

Moving a household out, deleting an apartment and making the lot an ordinary house again ask first.

**Apartments** lists the building's apartments. Their rooms are chosen in build mode (above); the
**More...** button on the Apartments tab there opens this page.

| Button | What it does |
|---|---|
| **Make building** | Makes this lot an apartment building (the Apartments tab in build mode does the same) |
| **More > Rename** | Gives the apartment a name of its own |
| **More > Tenant** | Chooses which of the households living here has that apartment, or nobody |
| **More > Delete** | The apartment is gone, and its rooms are shared |
| **-** and **+** (on "Rent") | The rent on this lot, in simoleons for each tile each day |
| **Ordinary house** | Makes the lot an ordinary house again (when no more than one household lives there) |

**Move in** lists the households with no house, to move one in beside the household you are playing.
You are asked which free apartment it takes. (Into an empty building, move households in from the
neighbourhood screen.)

**Settings** has the pack's switches: how often neighbours come out (never, sometimes,
often), **Bring out** and **Send home** to do it now, whether apartments are private, whether
visitors are shown to your door, whether the mailbox can be moved, whether the other apartments are
dimmed,
whether neighbours' needs are held up while they are out, and whether to write down what they do. It
also says where the log is.

### The console

Most of what the window does can also be typed, as `apt` followed by a command.

| Command | What it does |
|---|---|
| `apt` | Opens the window |
| `apt help` | Lists the commands |
| `apt who` | The households in this house, and which one is played |
| `apt families` | Households with no house, which can move in |
| `apt building` | Makes this lot an apartment building |
| `apt movein <number or name>` | Moves a household in beside yours, with no apartment yet |
| `apt moveout <number or name>` | Moves a neighbouring household out |
| `apt switch <number or name>` | Saves the lot, then plays that household |
| `apt claim [number or name]` | The room your Sim is standing in joins your apartment (or the named household's) |
| `apt unclaim` | The room your Sim is standing in is shared again |
| `apt units` | Each apartment, who lives in it, and its rent |
| `apt rent <number>\|off` | The rent on this lot (1) |
| `apt outings never\|sometimes\|often` | How often a neighbour comes out of their apartment (sometimes) |
| `apt out` | Bring a neighbour out now |
| `apt home` | Send the neighbours who are out back home |
| `apt dim on\|off` | Dim the other households' apartments (on) |
| `apt needs <-100 to 100>\|off` | Neighbours' needs never fall below this while they are out (50) |
| `apt watch on\|off` | Write down what neighbours do (on) |
| `apt log` | Where the log is |

Everything is kept when you save the lot, like anything else you do on it. The switches on the
Settings page are kept between runs, in `apartments.cfg` in the user directory.

## Neighbours

The households you are not playing are at home, and home is off stage. While a neighbour is in their
apartment they are not on the lot at all, and the apartment is dimmed, as if nobody were in.

Now and then one comes out: about every three hours of the game's day (**Sometimes**) or every 45
minutes (**Often**), between 8 in the morning and 10 at night, and never more than two at once. They
appear in their apartment and go to do something in the shared rooms or outdoors: one of the things
there they could choose for themselves (the game's own menu for them), such as watching a TV,
sitting, playing, swimming or looking at a painting. Whenever they have nothing to do, they are
found something else. They are never sent to do anything that costs or earns money, looks after the
place, or takes things, nor to anything inside an apartment. After an hour or two they walk home and
are gone again. You can talk to them while they are out.

A neighbour can never spend the played household's money: anything a neighbour does that would cost
(or earn) money is refused, as the game refuses anyone who cannot afford a thing.

When the game sends a neighbour who is out off the lot (it sends visitors home by walking them to
the edge of the lot), they walk home to their apartment instead. (Until 0.18 the edge of the lot was
looked for by an internal name that the game leaves blank, so this never happened and neighbours
walked off; it is now known by the name the game shows, `*Portal ...`.) If the game takes a neighbour
off the lot any other way, what they did while out is kept all the same, and the log names the
game's scripts that did it.

When you change household, the one you were playing goes home in the same way, and the one you now
play is found in its apartment.

The first version kept every neighbour on the lot all the time. The game took them for visitors who
had just arrived: each walked to the front of the lot, rang the bell, waited, gave up and left, and
was put back to do it again. A neighbour who comes out is now marked as a guest who has been let in,
which is what stops that.

A neighbour's skills, job, personality and needs are kept when they go home, and are theirs again
when they next come out or are played.

## Apartments and rent

- A room has walls all the way round. Knock a wall through into the garden and it stops being a room;
  it counts as shared until it is closed again, and the window and the log say so.
- A vacant apartment is not dimmed, so you can see what you are furnishing.
- A household with no apartment still lives here. Its neighbours come out by the mailbox, and are
  simply gone when they go home.

Rent is the apartment's floor area times the lot's rate, for each game day. Nothing is taken by
itself: it builds up, and comes with the bills the mail carrier brings, as the game's own bills do.
When a new bill appears on the lot, the rent built up since the last is added to what is owed, and a
message says the rent came with the mail. If no bill has come with the mail for a week (no mailbox,
say), the rent bill comes on its own. A household builds up no rent while it is a neighbour, or while
it has no apartment. Moving into an apartment costs two days' rent, paid at once.

**The bills.** The game works a bill out from the value of everything on the lot, which in a building
is every apartment and every shared room, so on its own it would charge you for the whole building.
When a bill comes, your apartment's part is written on it instead: the building's bill, times the
value of the things in your apartment over the value of the things on the lot (walls, floors, doors
and windows, and everything in the shared rooms and in other apartments, are the building's), and
the rent built up since the last bill on top. A message says what the building's bill was, your
part, and the rent. Pay the bills as you always do; what is written on them is what is paid. A bill
from before this version, or one the pack could not write on, is paid by working out your part when
you pay it, with any rent owed. Rent still owed from a bill that was thrown away can be paid with
**Pay rent** in the window's line for your household.

The game's bills are known by their name (a word in it is "bill" or "bills"), and the amount is the
first value a bill holds (a bill of 1011 was seen holding 1011 first).

## Private apartments

An apartment is its household's own. A Sim who does not live there cannot walk into it: no route goes
through its doors, and a Sim told to use something inside gives up, as for a room with no door. Left
to themselves, Sims do not even want the things in an apartment they cannot enter.

| Apartment | Who can walk in |
|---|---|
| Yours | Your household, and your visitors once they have been greeted. Not the neighbours. |
| A neighbour's | That household only, and yours for three hours after being asked in. |
| Any | The game's own characters on a job: the firefighter, the police, the repairman, the maid. A fire next door has to be put out. |
| A vacant one | Everyone, so it can be looked round and furnished. |

A Sim who is already inside is never trapped: the way out is always open.

A room that is in no apartment but opens only into one (a cupboard, or a room cut off by a new wall)
can only be reached through that apartment, so it is out of bounds too.

Click anything inside an apartment your Sim cannot enter, or the floor in it, and nothing happens
but a warning by the cursor, as the game gives for a thing it will not let you use: "That's
Apartment 2. Knock at their door to be let in." The door itself has **Knock** on its menu.

**Knocking.** Click a neighbour's door with one of your Sims and choose **Knock** (or use **Options >
Knock** on the household, in the window). The Sim walks to the door. Between 8 in the morning and 10
at night someone answers: one of the household comes to the door, the apartment is lit, and your
household is welcome for three hours. At night nobody answers. When the welcome is over the
apartment goes dark again; anyone still inside can walk out, but not back in.

**Visitors.** The game walks visitors (the welcome wagon, guests, the pizza delivery) to its idea of
the front door, which in a building is the street door, and they ring there. So when a visitor's walk
to a door that is not your apartment's is about to be planned, it is planned to your apartment's door
instead: to the spot just outside it, or one beside that. What they do on getting there (ringing,
waiting to be let in) is the game's own. A Sim walking *through* a door is never touched, and nor is
anyone who lives in the building. Anything a visitor is given to do at the wrong door is moved to
yours too. They stand at your door facing it, and when several come at once (the welcome wagon)
each is sent to a different spot beside it.

A visitor waits outside until they are greeted. When you greet one (or do anything else with them)
while they wait, your Sim walks out to them first: in a house the game has the visitor come to your
Sim instead (the pizza delivery comes to whoever pays), which here would be into the apartment. And
a visitor coming to meet your Sim while your Sim is still inside walks to your door and waits there.
Once greeted, guests come in.

**The mailbox.** The game fixes the mailbox at the front of the lot. In a building it can be picked
up in buy or build mode and put where the mail should come: a mail room, or the hall. It cannot be
deleted. The mail carrier and the paper go to wherever it stands. There is still one mailbox for the
building, and what comes is for the household being played.

You can still build and buy anywhere on the lot, including in other households' apartments.

## What to try, and what to look for

1. Follow "Setting up a building" from an empty lot, with two apartments and two families.
2. Play one household. On the **Settings** page press **Bring out**, and watch what the neighbour does.
3. Play a game day or two at normal speed with neighbours coming out **Often**.
4. Leave to the neighbourhood, enter the building again, and choose the other household.

Then send `Apartments.log` (the **Settings** page says where it is). It answers these:

| Question | In the log |
|---|---|
| Did the building's layout survive the save? | The `--- House ...` line each time the lot loads lists its apartments and who is in them |
| Did each family land in the apartment it chose? | `moved into Apartment ... of house ...`, then `is at home, in their apartment` |
| Does a neighbour who comes out walk to the shared rooms? | `came out for ... minutes, walking to ...`, then the lines for where they are |
| Do they do anything while out? | `came out ..., to "..." on ...` and `goes to "..." on ...`, then a line each time what they are doing changes |
| Do they walk home when their time is up? | `is walking home`, then `went home`; or `had not got there after 30 minutes` |
| Do neighbours stay out of your apartment now? | No `THE APARTMENT OF ...` in a neighbour's line |
| Does knocking work? | `is going to knock at ...`, then `knocked at ... answered` |
| Do visitors ring at your door? | `Visitor ... was sent to the door at ... Sent to the door of ... instead.` |
| Do visitors walk to your door? | `Visitor ... was walking to the door at ... Sent to the door of ... instead.` |
| How does the game send visitors to the door? | `[visitor]` lines: where each arriving visitor walks, from which script, and what they are given to do |
| Does your household pay for what they do? | `Money: ... THAT IS A NEIGHBOUR` |
| What do bills come to, and when? | `Money:` lines naming the mailbox or the bill |
| Does rent build up each day, and come with the mail's bills? | `Rent:`, `The mail brought a bill` and `Rent bill with the mail` lines |
| What do the bills come to? | `Wrote ... on the bill (Apartment ...'s part of the building's ... is ...)`, then `Bills paid as written` |
| Do bills come at all? | `[mailbox]` lines: who walks to the mailbox, and when |
| Do neighbours still leave the lot? | `was leaving the lot by *Portal ... walking home to their apartment instead`, then `went home (the game's visitor script finished at their door)`. If not: `The game took ... off the lot, at ...` and the scripts that did it |
| Does your Sim walk out to greet a visitor? | `... walks out to meet ... at the door` |
| Does a visitor wait at your door instead of walking in? | `... was coming to ..., who is still at home: they wait at the door` |

## If the game goes down

Each time the game starts, a line of `=====` in `Apartments.log` marks it, with the pack's version.
When the log passes half a megabyte it is put aside as `Apartments.old.log` and a new one begun, so
the file to send stays short.

`Apartments.log` is written as things happen, not afterwards, so its last lines say what was going
on: `[window]` and `[question]` lines for each button pressed, `[tool]` and `[menu]` for choosing
rooms and knocking, and `(first)` lines the first time each part of the pack acts on a lot. Whatever
error closes the game is written there too, as `=== THE GAME IS CLOSING ON AN ERROR`, whether or not
the pack caused it.

Two things are done about errors the game would otherwise close on. An error in a Sim's or an
object's script that gets past the engine's own handling is stopped on a building lot: it is written
down as `=== A SCRIPT ERROR GOT PAST THE ENGINE`, the Sim or object is reset, and the game carries
on. And the first forty errors thrown by the engine or the pack in a run are written as `[thrown]`
lines as they happen, caught or not, in case the one that closes the game never reaches the log any
other way.

Each part has a switch on the Settings page, and the switches are kept between runs. If something
misbehaves, switch it off: the rest of the pack carries on without it.

## What it does not do yet

- **Building and buying are not kept to your own apartment.** You can change anything on the lot.
- **One mailbox for the building**, and the game decides when bills come. If they do not come on a
  lot, the log's `[mailbox]` lines say whether anyone came to the mailbox at all.
- **One purse at a time.** A neighbouring household's money does not change while you play another.
  If the log shows neighbours spending yours, that is to be fixed, not intended.
- **Unplayed households do not progress.** They are held as they are: no jobs, no bills, and their
  needs are as they were when they went home. Relationships are the exception. The game keeps those
  for each Sim, so they change as usual.
- **A dimmed apartment is only dimmed.** You can still look into it, and nothing in it moves.
- **Not yet seen working in the game:** private apartments and knocking, moving the mailbox, and
  the Apartments tab in build mode with the apartments drawn in colour (new in 0.20). (A visitor was seen being walked to the
  door.) Each has a switch
  on the Settings page or is only used when you ask for it, and each fails on its own: the log says
  if one could not be added.
- Away from home (downtown, on holiday) nothing here applies. The other households stay on the lot.

## How it works

The engine keeps one family for a lot, and all of the game's scripts expect that: money, the car
pool, bills and the burglar all go to "the" family. This pack leaves that as it is. There is still
exactly one played family, and switching changes which one it is.

**Who lives where** needs nothing new in the save. Every family in the neighbourhood file names its
own house. The game only expects one family to name a given house; here several may, and they are
that house's residents. What the file cannot say is that the house is a building, what its
apartments are and who is in each, which household is played, and what rent is owed. Those are in the pack's own file for the lot, beside the saves
(`LocalHouse/PackData/sample.apartments/` in the user directory).

An apartment is kept as one tile inside each of its rooms, not as the engine's room numbers, which
change whenever a wall is built or knocked down. Whatever room the tile is in now is the apartment's.
The file also notes each apartment's size when the lot is saved: only a loaded lot knows its rooms,
and the neighbourhood screen needs the sizes to offer the apartments.

Without the pack, a shared house shows as lived in by one of its families. The others are untouched,
but cannot be reached until the pack is on again.

On the lot, the rest is seven Harmony patches:

1. A neighbourhood has loaded: point the game's one-family-per-house table at the played household
   of each shared house.
2. The game moves a house's family out: if others live there, the house is not empty.
3. The game has checked the played family is on the lot: put its members in their apartment, and
   send everyone else who lives here home, off stage.
4. The lot ticks: now and then bring a neighbour out and walk them home again, take the day's rent,
   and write down what the neighbours do. A lot that is not shared costs one lookup a tick.
5. The game takes or gives money: write down who caused it.
6. The lot is saved: keep the pack's file for the lot with it.
7. A room's light has been worked out: turn it down if it is another household's apartment.

Others are optional: each one that does not attach only turns its own feature off, and the log says
which. They close apartment doors to the routes Sims plan, leave closed rooms out of what Sims choose
to do, send visitors to the apartment's door, show the knock warning, write the household's part on
the mail's bills, notice the game taking a neighbour off the lot, draw the apartments on the lot, and put the
Apartments tab in build mode.

On the neighbourhood screen there are three more (`Hood.cs`): a building's panel says what it is and
lets a family move in while an apartment is free; the game's move-in asks which apartment, and
charges nothing; and entering a building where several households live asks which to play. The
game's own steps are kept around them.

A Sim's skills, job and the rest live on the Sim itself while it is on a lot, and Simitone saves them
with the lot. So before a neighbour goes off stage, they are written to the Sim's record in the
neighbourhood, which is where a Sim put on a lot gets them from.

Nothing the pack does should be able to take the game down: each of these is guarded, and whatever
goes wrong is written to the log in full while the game carries on. Changing household stops at the
first step that fails and says so.

It uses only what every Simitone with code packs has, so it does not need the newest build.

- `Residency.cs` is who lives where.
- `Units.cs` is the apartments, the rent, and what the pack's file for a lot says.
- `Hood.cs` is apartment buildings on the neighbourhood screen.
- `Doors.cs` is who may walk into which apartment, knocking, and showing visitors to the door. Doors
  are closed by leaving them out when the engine plans a walk from room to room.
- `LotOverlay.cs` draws the apartments on the lot, each floor in its colour. The engine draws a list of
  line sets over the lot every frame (its own use is showing the routes Sims plan); the overlay adds
  an empty one, and a patch on its drawing draws the coloured floor in its place.
- `UIApartmentsSubpanel.cs` is the Apartments tab in build mode: a button for each apartment, in its
  colour, and the tools for laying the building out. Two patches put the tab among build mode's own
  and open it in place of a catalogue.
- `Settings.cs` keeps the Settings page's switches between runs.
- `UIChoiceDialog.cs` is a question with a button for each answer. The game's own message box takes
  only one button of each kind, so the pack has its own.
- `UIRoomPicker.cs` is choosing a room by clicking in it: one of the lot's own tools, like the wall
  tool.
- `PackStore.cs` is where that file is kept.
- `Neighbours.cs` is the unplayed households: off stage, coming out, going home, and the dimming.
- `Actions.cs` is everything the player can do, shared by the window and the console.
- `UIApartmentsDialog.cs` is the window. It is made the way Simitone's own Packs window is, from the
  same dialog, labels and buttons, so it has no artwork of its own.
- `ApartmentsPack.cs` is the patches, the ways into the window, and the console.
