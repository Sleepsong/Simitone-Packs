# Create a Style

Recolour any object on the lot, one part at a time, as in The Sims 3: turn a red sofa blue and leave its
wooden frame alone, or tint a white fridge.

**Version 0.2, still experimental.** The first version did nothing on Windows: see "What changed in
0.2" below. This version has been tested against the engine's own drawing code on a real graphics
device (in 2D and 3D, with the texture caching Simitone uses on Windows and on Linux and Mac), but not
yet in a full game. Expect to find things wrong, above all in how the panel looks and takes clicks.

## Using it

In build mode, open **Wall Coverings** and pick the **Style Brush**. (Or type `stylebrush` in the cheat
console, from build or buy mode.)

- **Click an object** to open its style at the side of the screen. Each part of the object has a block
  with two swatches (as it was, as it is now) and three sliders:

  | Slider | Does |
  | --- | --- |
  | Hue | Moves the part round the colour wheel. Its shades move together, so the shading stays. |
  | Strength | Makes the part's colours stronger or weaker. All the way down is grey. |
  | Light | Makes the part lighter or darker. |

  The greys (white, grey and black) are a part of their own, with **Tint** in place of Strength: how
  much of the chosen hue they are given. At nothing they stay grey.
- **Reset** puts every part back as it was made. **Done** closes the panel. The object changes as the
  sliders move; there is nothing to confirm.
- A line above the buttons says how it is going: how many of the object's pictures it has recoloured,
  that it is waiting for pictures still loading, or why it could not recolour one. If moving a slider
  does not change the object, that line is the thing to report.
- **Ctrl and click** (or the touch erase button) takes an object's style off.
- **Shift and click** gives an object the style made last, if it is the same kind of object: for the
  other five dining chairs.

Styling is free.

## What a part is

The Sims 3 can recolour things because each of its objects comes with masks marking the parts that
can change. The Sims' objects are finished pictures: nothing says which pixels are the cushions and
which the frame. So this pack finds the parts itself, by colour.

It counts the hues of all the object's pictures (every tile, from every side) round the colour wheel.
Each hump in the count is a part, up to four, and the dips between the humps are where one part ends
and the next begins. Humps 30 degrees or more apart are always separate parts; nearer ones are when the
count falls well away between them, so red cushions on a wooden frame are two parts, but the shades of
one wood are one. Colours too weak or too dark to have a hue worth trusting are the greys.

This works well when an object's parts differ in colour, and it is what limits the pack:

- Two parts of the same colour are one part. A sofa with red cushions and a red throw changes both.
- One part in two colours is two parts.
- Where two parts meet, the art blends them with in-between pixels, which belong to one side or the
  other. A strong change can leave a thin fringe.
- Patterns, as in The Sims 3, are out of reach: a sprite has no surface to lay a pattern on.

## 3D

- **Models the engine builds from the sprites** are textured with those sprites, so they are
  recoloured with them and look the same as in 2D.
- **Remade models with their own texture** (`MeshReplace`, or a model inside the object's file) are
  recoloured the same way, from their own texture. Their parts are found from the sprites and the
  texture together, since the object is drawn from one in 2D and the other in 3D. These have not been
  tested on a real remade model yet.

## Saving

Styles are saved when the lot is saved, in this pack's own file beside Simitone's saves
(`LocalHouse/PackData/sample.createastyle/`). The lot's save itself is unchanged. Without the pack the
lot loads with its objects as they were made, and Simitone warns that the pack is missing; switch the
pack back on and the styles are there again.

A style is kept against the object's number on the lot, with its kind. If the lot is played without
the pack and the number passes to another kind of object, the style is dropped rather than put on
the wrong thing.

## Not done yet

- **Sims' clothes.** Skins are textures like any other, and the same recolouring would work on them,
  but clothes and skin share a texture and are close in colour, so it needs more than this.
- **The catalogue.** A style is made on an object already placed. There is no styling while buying.
- **Old-format sprites** (`SPR#`, which very few objects use) should now work like the others, since
  the pack reads the textures rather than the sprite files, but none have been tried.
- The pack keeps a copy of the pixels of each picture it has recoloured, and each different style on
  show has its own textures. A lot with hundreds of differently styled objects will use noticeably more
  memory.

## What changed in 0.2

- **It works on Windows.** The pack got an object's pixels by decoding its sprites again. On Windows,
  Simitone draws with DirectX and has the engine keep every texture for good, and in that mode the
  engine throws a sprite's file data away once it has made its texture. So the sprites on show could
  not be decoded again, every recolouring quietly fell back to the original, and moving the sliders did
  nothing. The pack now reads the pixels back from the texture itself, as it is drawn, and decodes the
  sprite only if that fails.
- **3D is surer.** The model's textures are now swapped for the styled ones around the drawing of a
  styled object. Before, the texture was changed on its way into the shader, by patching a one-line
  method that the runtime was free to fold into its caller, which would have made the patch do nothing.
- **Pictures still loading** are tried again, and the object drawn again once they are ready. In 2D an
  object that is not moving is drawn once and kept, so it would not otherwise be tried again.
- **Closer colours are told apart:** a red top on wooden sides is two parts now, where it was one.
- Old copies are disposed two seconds after they stop being used, not at once, so nothing can draw a
  texture that has gone.
- The panel says how the recolouring is going.

## How it is made

Nothing in the engine is changed.

- `Style.cs` is the colour work: finding an object's parts, and recolouring a colour. It knows nothing
  of the game and is the tested part.
- `StyleTextures.cs` makes and keeps the recoloured copies of the game's textures. The engine has one
  texture for each sprite, shared by every object that uses it, so a styled object needs its own.
  Objects with the same style share theirs.
- `CreateAStylePack.cs` is where it meets the engine, as four Harmony patches. In 2D, an object's
  sprites are swapped for the styled copies as the engine lists them for drawing. In 3D, a model is
  shared by every object of its kind, so its textures are swapped for the styled copies just before a
  styled object is drawn, and put back just after.
- `UIStyleBrush.cs` and `UIStylePanel.cs` are the build tool and the panel. The panel draws itself
  from plain rectangles, so the pack has no artwork.
