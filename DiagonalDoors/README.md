# Diagonal Doors, Stairs and Pools

Three things The Sims never let you build on the diagonal: doors and windows in diagonal walls,
stairs that run diagonally, and pools with diagonal edges.

## Using it

### Doors and windows

Switch to full 3D, pick a door or window, and hold it over a diagonal wall. It turns 45 degrees and
sits in the wall. Rotating it flips it to face the other way.

- **One tile wide.** Doors and windows that take one tile of wall fit. Double doors and wide windows
  do not: a diagonal wall tile is longer than a straight one, so their halves would not meet.
- **Walls, not fences.** The diagonal has to be a wall.
- **The wall stays.** As on a straight wall, a wall cannot be deleted while a door or window is in it.

### Stairs

Switch to full 3D and pick up a staircase. It now turns 45 degrees at a time instead of 90, with the
rotate buttons or by dragging, so every other turn is a diagonal.

- **The hole upstairs.** The game opens the floor above stairs one whole tile at a time. Diagonal
  stairs cross tiles at an angle, so the pack also opens the tiles they partly cover. The hole comes
  out wider than the stairs, with stepped edges. A tile with a wall or an object on it is left closed.
- **Removing them.** As with straight stairs, the hole stays when the stairs are sold. Lay floor
  over it.

### Pools

Pick the pool tool and hold **Shift**. Instead of dragging out a rectangle, each click makes half a
tile of pool: a triangle in the corner of the tile nearest the cursor, with a rim along the diagonal.

- **Cutting a corner off a pool.** Shift-click a corner tile of a pool and it becomes a triangle;
  the rest of the tile goes back to ground.
- **Adding to a pool.** Shift-click the ground next to a pool, near the corner that touches it.
- **Removing one.** Hold **Ctrl** as well. Dragging the pool tool over a triangle makes it a whole
  tile of pool again.
- **The other half** is ground, or any floor: paint it with the floor tools as usual.
- **Where.** On the ground floor, on level ground, on a tile with no walls and nothing standing on it.
- **Ladders and diving boards** need a straight edge, as before.
- Works in 2D and 3D.

### Full 3D only, for doors and stairs

In 2D the game only has pictures of objects facing four ways, the same limit as the Build Cheats
pack's `rotate45`. Nothing turns diagonally there. A diagonal door or staircase placed in 3D stays
where it is if you switch to 2D, but it is drawn facing its nearest straight direction.

## Saving

Nothing extra is saved: the lot's save already keeps where objects are, which way they face, and
each tile's walls and floors. Without the pack, a lot loads with everything in place, and Simitone
warns that the pack is missing, but:

- Sims cannot walk through diagonal doors, because the engine no longer knows they are doors.
- A diagonal pool edge is a kind of wall this pack defines, and the engine does not know how to
  draw it. The pool is still two halves of a tile to Sims, but it will not look right.

Switch the pack back on and both work again.

## How it is made

Nothing in the engine is changed. Each feature is a set of Harmony patches around something the
engine can nearly do already.

**Doors** (`DiagonalDoorsPack.cs`). A door is two objects, one on each side of its wall. A diagonal
wall runs through the middle of one tile, which the engine already splits between two rooms, so the
pack puts both halves inside that tile. It patches placing (`VMMultitileGroup.ChangePosition`), the
engine's refusal of objects on diagonal walls (`VMEntity.WallChangeValid`), the wall's record of its
door and what blocks Sims there (`SetWallStyle`, `SetWallUse`, `VMRoomMap.GenerateMap`,
`GenerateRoomObs`), and the drawing of the hole (`WallComponentRC.Generate`,
`WallComponent.GetWallSprite`).

**Stairs** (`DiagonalStairs.cs`). The engine can already turn any object 45 degrees; Simitone only
does it with the `rotate45` cheat on. The pack switches that on for as long as stairs are held
(`UIObjectHolder.RotateObject`, `Update`), and widens the hole the engine makes when stairs are
placed (`VMEntity.ExecuteEntryPoint`).

**Pools** (`DiagonalPools.cs`, `PoolMesh.cs`). A tile with a diagonal wall keeps a floor for each
side of the wall, and everything in the engine that asks what is underfoot asks per half. The pack
adds a wall style that is no wall, only the line between the halves, and lets the pool tool put
pool on one side of it (`UIFloorPainter.Update`, `VMArchitectureTools.FloorPatternRect`). Rooms,
routing and object placement then follow from what the engine already does. The pool's 3D model
is the one part the pack builds itself (`Modelled3DPool.GenerateGeometry`): the engine assembles
pools from ready-made whole-tile pieces, so `PoolMesh` cuts those pieces along the diagonal and
turns one of the rims to run along it. A pool with no diagonals is still built by the engine.

## Status

Experimental. It builds against the `addon-packs` branch. Its patches, placement rules, wall and
floor bookkeeping, room and routing changes, and the pool model have been checked against the built
engine without the game running, and the pool model has been drawn from above and looked at. It
has not yet been played in the game itself, which needs The Sims' own files. Things to look at
first when it is:

- Sims walking through a diagonal door, and how each door's cut-out lines up with its frame.
- Sims walking up and down diagonal stairs, and whether the stairs place at all on a diagonal: the
  pack makes them turn, and relies on the engine's own checks to accept them.
- A diagonal pool in the game's own lighting, in 2D, and with Sims swimming up to the edge.
