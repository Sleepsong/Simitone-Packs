# Terrain Paint

Paint the ground with the game's own terrain types, as in The Sims 2: a sandy path, a patch of autumn
grass, rock, or snow, on any lot, whatever ground the lot started with.

## Using it

In build mode, open **Terrain**. There is one paint for each terrain type, and **Remove Terrain Paint**.

| Paint | Ground | Grass blades |
| --- | --- | --- |
| Grass | the usual lawn | tall |
| Dark Grass | darker grass | tall |
| Autumn Grass | brown, autumn grass | tall |
| Sand | sand, as on beach lots | flat |
| Rock | bare rocky ground | flat |
| Snow | snow, as on winter lots | flat |
| Cloud | Magic Town's cloud ground | flat |

- **Painting:** drag over the ground. The brush shows where it would paint before you start.
- **Brush size:** **+** and **−** change it, from a single point up to a 6-tile radius.
- **Soft brush:** hold **Shift**. It paints only some points around the brush's edge, so the paint
  feathers into what is there.
- **Removing paint:** hold **Ctrl**, or use **Remove Terrain Paint**. The lot's own ground shows again.
- **Cost:** $1 for each point painted, like the Grass Tool.

## Saving

Painted ground is saved when the lot is saved, in this pack's own file beside Simitone's saves
(`LocalHouse/PackData/sample.terrainpaint/`). The lot's save itself is unchanged. Without the pack
the lot loads with its unpainted ground, and Simitone warns that the pack is missing; switch the
pack back on and the paint is there again.

## How it is made

This is a port of the `terrain-paint` branches of Simitone and FreeSO, where the feature is an engine
change of about 350 lines in 7 files. Here nothing in the engine is changed.

- `Paint.cs` and `UITerrainPainter.cs` are the feature's own code: the paint layer, the brush and
  the build tool. They came across almost unchanged.
- `TerrainPaintPack.cs` is everything that used to be an edit to existing engine code, redone as
  Harmony patches. It lists the six places and how each is reached from outside.

Differences from the engine version:

- Lots painted with the engine version keep their paint inside the save, which this pack does not read.
- When every point of a lot is painted, the engine version skips drawing the lot's own grass blades.
  This pack still draws them, with nothing to show.
