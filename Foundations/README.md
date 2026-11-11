# Foundations

As in The Sims 2, a house can stand on a foundation or a deck: its ground floor is raised a few
steps, about a quarter of a storey, above the ground. Everything on it goes up with it: floors,
walls, furniture, Sims, roofs and the floors above.

**This pack is new.** Its drawing has been checked piece by piece but not yet seen in a running game:
see "What has been checked" below before using it on a lot you care about.

## Using it

The tools are in build mode under **Terrain**:

- **Foundation**, **Latticed Deck** and **Column Deck**: drag out an area of clear ground. Dragging one
  kind over another of the same height changes it. Hold **Ctrl** to remove one.
- **Foundation Steps**: one entry for each straight staircase the game has. Click a ground tile beside
  a foundation or deck.

Paint a foundation's sides with the wall covering tool, as you would a wall; hold **Shift** to paint
every side of that foundation. Decks are not painted.

Lay floors, then build walls on it as usual; walls on its edge stand on it. Sims can only get on and
off by the steps, so give each house a set at its door. A foundation goes on empty ground: clear
furniture and walls off the tiles first. Furniture cannot straddle a foundation's edge, and pools
cannot go in one.

## What it does

Stage 1, "the foundation exists and everything stands on it":

- placing and removing foundations, decks and steps, with previews and prices;
- saving, in this pack's own file beside Simitone's saves (`LocalHouse/PackData/sample.foundations/`);
- the rules: steps count as a slope, no pools in a foundation, nothing straddles an edge;
- routing: Sims cannot cross a foundation's edge except by its steps;
- heights: the ground, and so floors, objects and Sims, are as high as the foundation under them;
- walls (2D and 3D) and roofs stand on the foundation.

Stage 2, "what a foundation looks like":

- a foundation's brick sides and bare plywood top, a deck's planks, edge board, lattice and columns,
  and plain steps, drawn with the ground floor;
- steps that are a staircase's bottom piece;
- the lot's own ground under column decks;
- painting a foundation's sides with wall coverings;
- windows, doors and paintings on a wall at a foundation's edge go up with the wall, from either side.

Different from the engine version: no grass blades grow under a column deck (the ground there is
drawn, without blades).

## What has been checked

Without the game's own files, on a software graphics device: the commands, rules, routing, heights and
saving (28 checks); and that the models and the ground copy are built on a graphics device with the
right tiles, which side the cursor finds, and the height of wall-hung things (18 checks).

Not yet seen: the picture itself. The textures come from the game's files, and the drawing, the
staircase pieces, the wall painter and the wall-hung objects all need a running game to look at.

## How it is made

This is a port of the `foundations` branches of Simitone and FreeSO, where the feature is an engine
change of about 2,300 lines in 24 files. Here nothing in the engine is changed.

- `FoundationMap.cs`, `VMFoundationTools.cs`, `FoundationLook.cs`, `Modelled3DFoundation.cs`,
  `FoundationStepsObjects.cs` and `UIFoundationTools.cs` are the feature's own code, almost as it was.
- `Lots.cs` keeps a lot's foundations beside the engine's own objects, since a pack cannot add fields
  to them.
- `FoundationsPack.cs` (the foundation exists) and `FoundationsDrawing.cs` (what it looks like) are the
  edits to existing engine code, redone as Harmony patches. Each lists its places and how they are
  reached from outside.

Lots with foundations made in the engine version keep them inside the save, which this pack does not read.
