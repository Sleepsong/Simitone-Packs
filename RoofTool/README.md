# Roof Tool

Draw roofs yourself, as in The Sims 2, instead of only using the game's automatic roof. Three roof
shapes, a pitch for each roof, and the lot's roof style.

## Using it

In build mode, open **Roofs**. The roof tools come before the roof styles:

| Tool | What it does |
| --- | --- |
| **Hip Roof** | Drag out a roof that slopes on all four sides. |
| **Gable Roof** | Drag out a roof with a ridge along its long side and upright triangular ends. Hold **Shift** for a ridge along the short side. |
| **Shed Roof** | Drag out a single slope, low on the side you drag towards. Hold **Shift** to make that side the high one. |
| **Remove Roof** | Click a roof to remove it. **Ctrl** does the same with any roof tool. |
| **Automatic Roof** | Go back to the game's automatic roof. Drawn roofs are removed, and their price is refunded. |

- **Where roofs sit.** A roof sits on top of the walls of the floor you are looking at. Go up a floor
  to roof the floor above.
- **Eaves.** Roofs overhang the walls by half a tile on their sloped sides. Gable ends and a shed
  roof's high side stand on the walls.
- **Pitch.** **+** and **−** change the pitch of the next roof, from 20% to 150%. The game's usual roof is 66%.
- **Price.** $10 a tile. Removing a roof refunds it.
- **Style.** Roof styles from the catalogue apply to drawn roofs too.
- **Replacing the automatic roof.** The first roof you draw on a lot replaces the automatic roof.
  The house then has only the roofs you draw, until you pick Automatic Roof.
- **Overlaps.** Roofs can overlap. Where they meet, they pass through each other.

## Saving

Drawn roofs are saved when the lot is saved, in this pack's own file beside Simitone's saves
(`LocalHouse/PackData/sample.rooftool/`). The lot's save itself is unchanged. Without the pack the
lot loads with its automatic roof, and Simitone warns that the pack is missing; switch the pack
back on and the drawn roofs are there again.

## How it is made

This is a port of the `roof-tool` branches of Simitone and FreeSO, where the feature is an engine
change of about 620 lines added and 190 removed, in 8 files. Here nothing in the engine is changed.

- `DrawnRoofs.cs`, `RoofTools.cs` and `UIRoofTools.cs` are the feature's own code: the roofs, the
  commands and the build tools. They came across almost unchanged.
- `RoofToolPack.cs` is the edits to existing engine code, redone as Harmony patches. It lists the
  seven places and how each is reached from outside.
- `RoofMesh.cs` is the one that could not be an edit. The engine change rewrote the roof component's
  meshing so the automatic roof and drawn roofs share it. A pack cannot rewrite a method in place, so
  this is that meshing as the pack's own, used instead of the engine's while a lot has drawn roofs.
  It will not pick up later changes to the engine's roof meshing, and would clash with another pack
  that replaces the same method.

Differences from the engine version:

- Lots with roofs drawn in the engine version keep them inside the save, which this pack does not read.
- Gable ends are drawn after the whole roof, not floor by floor with it.
