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
2. **Make it an apartment building.** Open the **Apartments** window (below), go to the
   **Apartments** page and press **Make building**.
3. **Lay out the apartments.** Press **Add** on "New apartment", then click inside each room that
   belongs to it, one after another, and press Esc when you have finished. While you choose, the
   rooms of that apartment are lit and the rest of the building is darkened, and a small plan in the
   corner of the screen shows the same and says how each click went. **Add room** adds more rooms
   later. To choose a room on another floor, change floor before clicking. Every room you leave out
   is shared by everyone, and so is outdoors.
4. **Save**, and go back to the neighbourhood.
5. **Move households in.** Choose a family to move in, as usual, and pick the building. Its panel
   says how many apartments are free. After the game's own "move in?" question you are asked which
   apartment the family takes; when only one is left, you are asked to confirm it. Moving in costs
   two days' rent, and a family that cannot afford an apartment is not offered it. (An apartment is
   rented, not bought, so the lot's price is not charged.)
6. **Play.** Entering a building where several households live asks which one you want to play. The
   others are at home, as neighbours.

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
out** (back to the family bin; their things stay, and their apartment is vacant).

**Apartments** is where a building is laid out.

| Button | What it does |
|---|---|
| **Make building** | Makes this lot an apartment building |
| **Add** (on "New apartment") | Adds an apartment, then lets you click its first room |
| **Add room** | Lets you click rooms to add to that apartment, until you press Esc |
| **Plan** | A plan of the lot, floor by floor, with each apartment in its own colour |
| **On the lot** | Shows the apartments on the lot itself: they are lit, and the shared rooms darkened, until you open the window again |
| **More > Remove room** | Lets you click a room to take out of its apartment |
| **More > Tenant** | Chooses which of the households living here has that apartment, or nobody |
| **More > Delete** | The apartment is gone, and its rooms are shared |
| **-** and **+** (on "Rent") | The rent on this lot, in simoleons for each tile each day |
| **Ordinary house** | Makes the lot an ordinary house again (when no more than one household lives there) |

**Move in** lists the households with no house, to move one in beside the household you are playing.
You are asked which free apartment it takes. (Into an empty building, move households in from the
neighbourhood screen.)

**Testing** has the switches for trying things out: how often neighbours come out (never, sometimes,
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
Testing page last until the game closes.

## Neighbours

The households you are not playing are at home, and home is off stage. While a neighbour is in their
apartment they are not on the lot at all, and the apartment is dimmed, as if nobody were in.

Now and then one comes out: about every three hours of the game's day (**Sometimes**) or every 45
minutes (**Often**), between 8 in the morning and 10 at night, and never more than two at once. They
appear in their apartment, walk to a shared room (or outdoors, if there is none), and spend an hour
or two there as any visiting Sim would. Then they walk home and are gone again. You can talk to them
while they are out.

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

Rent is the apartment's floor area times the lot's rate, each game day, taken from the household you
are playing. A household pays nothing while it is a neighbour. If there is not enough money, what is
left is owed and is taken when there is money again; money never goes below zero for rent. A
household with no apartment pays no rent. Moving into an apartment costs two days' rent, paid at once.

## Private apartments

An apartment is its household's own. A Sim who does not live there cannot walk into it: no route goes
through its doors, and a Sim told to use something inside gives up, as for a room with no door. Left
to themselves, Sims do not even want the things in an apartment they cannot enter.

| Apartment | Who can walk in |
|---|---|
| Yours | Your household, your visitors, and the game's own characters (the maid, the repairman, a burglar). Not the neighbours. |
| A neighbour's | That household only, and yours for three hours after being asked in. |
| A vacant one | Everyone, so it can be looked round and furnished. |

A Sim who is already inside is never trapped: the way out is always open.

**Knocking.** Click a neighbour's door with one of your Sims and choose **Knock** (or use **Options >
Knock** on the household, in the window). The Sim walks to the door. Between 8 in the morning and 10
at night someone answers: one of the household comes to the door, the apartment is lit, and your
household is welcome for three hours. At night nobody answers. When the welcome is over the
apartment goes dark again; anyone still inside can walk out, but not back in.

**Visitors.** The game sends a visitor to the front of the lot to wait to be greeted, which in a
building is the street door. A visitor waiting there is walked to your own apartment's door and
waits there instead.

**The mailbox.** The game fixes the mailbox at the front of the lot. In a building it can be picked
up in buy or build mode and put where the mail should come: a mail room, or the hall. It cannot be
deleted. The mail carrier and the paper go to wherever it stands. There is still one mailbox for the
building, and what comes is for the household being played.

You can still build and buy anywhere on the lot, including in other households' apartments.

## What to try, and what to look for

1. Follow "Setting up a building" from an empty lot, with two apartments and two families.
2. Play one household. On the **Testing** page press **Bring out**, and watch what the neighbour does.
3. Play a game day or two at normal speed with neighbours coming out **Often**.
4. Leave to the neighbourhood, enter the building again, and choose the other household.

Then send `Apartments.log` (the **Testing** page says where it is). It answers these:

| Question | In the log |
|---|---|
| Did the building's layout survive the save? | The `--- House ...` line each time the lot loads lists its apartments and who is in them |
| Did each family land in the apartment it chose? | `moved into Apartment ... of house ...`, then `is at home, in their apartment` |
| Does a neighbour who comes out walk to the shared rooms? | `came out for ... minutes, walking to ...`, then the lines for where they are |
| Do they do anything while out? | A line each time what they are doing changes; all `idle` means they only stand about |
| Do they walk home when their time is up? | `is walking home`, then `went home`; or `had not got there after 30 minutes` |
| Do neighbours stay out of your apartment now? | No `THE APARTMENT OF ...` in a neighbour's line |
| Does knocking work? | `is going to knock at ...`, then `knocked at ... answered` |
| Do visitors come to your door? | `Visitor ... was waiting at the front of the lot, and is walking to the door of ...` |
| Does your household pay for what they do? | `Money: ... THAT IS A NEIGHBOUR` |
| What do bills come to, and when? | `Money:` lines naming the mailbox or the bill |
| Is rent taken once a day, and is the amount right? | `Rent:` lines |

## What it does not do yet

- **Building and buying are not kept to your own apartment.** You can change anything on the lot.
- **Bills are the game's own**: one bill for the whole lot, paid by whoever is played, on top of the
  rent. There is one mailbox for the building.
- **One purse at a time.** A neighbouring household's money does not change while you play another.
  If the log shows neighbours spending yours, that is to be fixed, not intended.
- **Unplayed households do not progress.** They are held as they are: no jobs, no bills, and their
  needs are as they were when they went home. Relationships are the exception. The game keeps those
  for each Sim, so they change as usual.
- **A dimmed apartment is only dimmed.** You can still look into it, and nothing in it moves.
- **Not yet seen in the game:** private apartments and knocking, visitors being walked to your door,
  moving the mailbox, the plan of the lot, and the lighting while rooms are chosen. Each has a switch
  on the Testing page or is only used when you ask for it, and each fails on its own: the log says
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
- `UIApartmentMap.cs` is the plan of the lot: in a window, and in the corner while rooms are chosen.
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
