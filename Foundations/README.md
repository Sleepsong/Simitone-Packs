# Foundations

As in The Sims 2, a house can stand on a foundation or a deck: its ground floor is raised a few
steps, about a quarter of a storey, above the ground. Everything on it goes up with it: floors,
walls, furniture, Sims, roofs and the floors above.

**This pack is a work in progress.** See "What works so far" below before using it on a lot you care about.

## Using it

The tools are in build mode under **Terrain**:

- **Foundation**, **Latticed Deck** and **Column Deck**: drag out an area of clear ground. Dragging one
  kind over another of the same height changes it. Hold **Ctrl** to remove one.
- **Foundation Steps**: one entry for each straight staircase the game has. Click a ground tile beside
  a foundation or deck.

Lay floors, then build walls on it as usual; walls on its edge stand on it. Sims can only get on and
off by the steps, so give each house a set at its door. A foundation goes on empty ground: clear
furniture and walls off the tiles first. Furniture cannot straddle a foundation's edge, and pools
cannot go in one.

## What works so far

Stage 1, "the foundation exists and everything stands on it":

- placing and removing foundations, decks and steps, with previews and prices;
- saving, in this pack's own file beside Simitone's saves (`LocalHouse/PackData/sample.foundations/`);
- the rules: steps count as a slope, no pools in a foundation, nothing straddles an edge;
- routing: Sims cannot cross a foundation's edge except by its steps;
- heights: the ground, and so floors, objects and Sims, are as high as the foundation under them;
- walls (2D and 3D) and roofs stand on the foundation.

Not done yet (stage 2, what a foundation looks like):

- its sides, bare tops, lattice, columns and the steps' staircase pieces are **not drawn**: a foundation
  shows as raised ground with open gaps where its sides should be, and steps as a raised slope;
- the lot's own ground under column decks;
- painting a foundation's sides with wall coverings;
- wall-hung objects (windows, paintings) on a wall at a foundation's edge, seen from the lower side,
  hang at the lower tile's height.

## How it is made

This is a port of the `foundations` branches of Simitone and FreeSO, where the feature is an engine
change of about 2,300 lines in 24 files. Here nothing in the engine is changed.

- `FoundationMap.cs`, `VMFoundationTools.cs`, `FoundationLook.cs`, `Modelled3DFoundation.cs`,
  `FoundationStepsObjects.cs` and `UIFoundationTools.cs` are the feature's own code, almost as it was.
- `Lots.cs` keeps a lot's foundations beside the engine's own objects, since a pack cannot add fields
  to them.
- `FoundationsPack.cs` is the edits to existing engine code, redone as Harmony patches. It lists the
  places done so far and how each is reached from outside.

Lots with foundations made in the engine version keep them inside the save, which this pack does not read.
