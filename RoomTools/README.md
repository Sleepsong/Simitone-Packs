# Room Tools

The Sims 4's room tools: pick up a room with everything in it and move it, turn it or copy it; drag a
wall to make a room bigger or smaller; undo and redo in build and buy mode. Rooms and whole lots can be
saved to a library on your computer (the Lot Bin) and built again on any lot. They are kept as files,
so you can give them to other players. There is no online Gallery, because that needs a server.

**Back up your `UserData` folder before trying it.** The pack has been compiled, its patches checked
against the engine, and its walls, floors and library files tested without the game (see `tests/`).
It has not been run in the game yet.

## Using it

Everything is in build mode, on the **Rooms** tab (the floor icon, after the game's own tabs).

| Entry | What it does |
| --- | --- |
| **Move Room** | Click a room to pick it up, then click where it goes. **[** and **]** turn it a quarter turn. Moving is free. |
| **Copy Room** | The same, but the room stays where it is, and each click builds a copy. You pay for the walls, floors and objects as if you bought them. |
| **Move Wall** | Drag a straight wall across. The rooms either side of it grow and shrink. The walls at its ends stretch or shrink to follow, and doors, windows and anything else hung on it move with it. |
| **Undo**, **Redo** | The same as **Ctrl+Z**, and **Ctrl+Y** (or **Ctrl+Shift+Z**). |
| **Save Room** | Click a room to save it to your library, with everything in it. |
| **Save Lot** | Save the whole lot to your library: everything built, everything players can move, the ground and the roof. |
| saved rooms | Each room in your library. Choose one, then click where it goes, as with Copy Room. Press **Delete** while it is chosen to take it out of the library. |
| saved lots | Each lot in your library. Choose one to build it on this lot, in place of what is there. |

A room is a space with walls (or fences) all the way round it. The tile under the cursor shows which
room you would pick up: white if it can be picked up, red if not, with the reason under the cursor.
While you carry a room, where it would go is green, or red with the reason it can't go there. To put a
room on another floor, change floor while you carry it.

### What goes with a room

- Everything standing in it, and everything on those things (plates, lamps on tables).
- Doors, windows, pictures and lamps on its walls, on either side, unless the wall is shared.
- The Sims standing in it, when it is moved.
- A wall the room shares with another room stays where it is, for that room, with whatever hangs on
  that room's side of it. The room you move takes a copy of the wall. A door in a shared wall goes
  with the room whose side of the wall it stands on.

A room can't be moved while someone is using something in it. It also can't be moved if something is
built on top of it (a floor or walls on the storey above), or if an object in it is partly outside it.
Move what is above first. A room can't be put down across another wall, on sloping ground, off the part
of the lot you can build on, over objects or Sims, or on an upper floor with nothing to hold it up.

Objects players can't buy or move are left behind by Copy Room and Save Room. You are told which ones.

### Moving walls

Move Wall drags a straight run of wall, from one corner (or where another wall meets it) to the next.
The floor follows the room that grows. A wall can't be dragged into another wall, over objects or Sims,
or across a corner where the wall it meets only partly stands. It also can't be dragged if something is
built on top of it. The tooltip shows what the move costs: walls added cost what they cost to build, and
walls and floors taken away give back what the game's own tools give back.

### Undo and redo

Each change in build or buy mode can be undone: walls, floors, wallpaper, buying, moving and selling
objects, terrain, the roof, and Room Tools' own changes. The money comes back too. Up to 40 changes are
kept. They are forgotten when you go back to live mode, or to Options.

Terrain Paint, Roof Tool, Foundations and Create a Style keep their own data beside the lot. Undo keeps
and restores that data too. Other packs' changes are not undone. Undo only steps back through changes
made in build and buy mode, while the game is paused.

### The library and the Lot Bin

Saved rooms are files in `Library/Rooms`, and saved lots in `Library/Lots`. Both folders are in
Simitone's user folder, next to `config.ini` (on Linux, `~/.local/share/Simitone/`). To give one to
someone, send them the file and have them put it in the same folder. It shows up in their Rooms tab the
next time they open it. Objects are found by their ids, so any downloads or packs a room uses need to be
installed there too. Objects that aren't installed are left out, and you are told which.

Building a saved lot replaces everything built on the lot, and every object players can move or sell.
The Sims stay, and so do the lot's own fixtures that players can't sell. You get back what the lot's
objects would sell for, and pay for the saved lot's. A lot saved from a bigger lot only fits if
everything built on it fits. Its ground is only copied onto a lot the same size. Building a saved lot
can be undone.

## How it is made

Nothing in the engine is changed. `RoomToolsPack.cs` lists the five places the pack reaches into it,
each a Harmony patch:

- `Geometry.cs` is the walls and floors as plain data: finding rooms (half-tiles included, for
  diagonal walls), picking them up, putting them down, turning them, and dragging walls. It touches
  nothing in the running game, so it is what `tests/` checks. Its quarter turn is checked against the
  engine's own lot rotation (`VMLotRotate`).
- `Engine.cs` puts that to work on a lot. It chooses a room's objects, makes copies of them from their
  saved state (as The Sims Online's inventory did), and handles prices, money, and the Lot Bin.
- `Undo.cs` keeps each step as the game saves a lot (`VM.Save`) and puts it back as the game loads one
  (`VM.Load`).
- `Preview.cs` shows a change before it is made, through the engine's own preview of its tools. It
  also draws see-through copies of a carried room's objects and the coloured highlight on the lot.
- `Blueprint.cs` and `Library.cs` hold the library's file format and folder.
- `Catalog.cs` and `Tools.cs` are the Rooms tab and its tools.

Run the checks with `dotnet run --project RoomTools/tests`.

## Not done (yet)

- Moving a room with something built above it, or a whole house of several floors at once.
- Levelling the ground under a room.
- Undo for other packs' own tools, apart from the four named above.
- Turning a room for touch screens: [ and ] need a keyboard.
- The online Gallery, which needs a server.
