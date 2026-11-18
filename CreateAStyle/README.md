# Create a Style

Recolour any object on the lot, one part at a time, as in The Sims 3: turn a red sofa blue and leave its
wooden frame alone, or tint a white fridge. And the same for Sims: their clothes, hair and hats, never
their skin.

**Version 0.3, still experimental.** Objects have been played in the game. Sims are new in 0.3 and have
only been tested against the engine's own drawing code with made-up outfits, not yet with the game's
own: see "Sims" below for what to look out for.

## Using it

In build mode, open **Wall Coverings** and pick the **Style Brush**. (Or type `stylebrush` in the cheat
console, from build or buy mode.) For a Sim, you can also select them and type `stylesim` in the cheat
console, in any mode, live mode included.

- **Click an object or a Sim** to open its style at the side of the screen. A Sim has a tab for each
  thing they wear that can be styled: **Clothes**, **Hair and hat**, and **Hands** if they wear gloves.
  Each part of the object, or of what is on the tab, has a block
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
- **Ctrl and click** (or the touch erase button) takes an object's or a Sim's style off.
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

## Sims

A Sim is drawn as 3D meshes, each with a texture: one for the body (the clothes, with the arms, neck
and legs), one for the head (the face, hair and any hat), and the hands. Skin and clothes share a
texture, and colours alone cannot tell them apart: brown hair or a tan shirt can be the colour of skin.

But the game has each outfit and head in light, medium and dark skin (the `lgt`, `med` or `drk` in a
texture's name), and those versions differ only where there is skin. So the pack compares them: a pixel
that is the same in all of them is clothes, hair or hat, and can be recoloured; a pixel that differs is
skin, and is never touched. The parts on each tab are found from the clothes, hair and hat alone.

- **One style per outfit.** A Sim's style is kept for the outfit they have on. When they change into
  swimwear or pyjamas, those show as they were made until you style them too, and their everyday clothes
  keep their style for when they change back.
- **Styles follow the Sim**, not the lot: they are kept for the whole neighbourhood, so a styled Sim
  looks the same visiting another lot. They are saved when you save a lot.
- **Pets** have no skin, so all of their coat can be recoloured.

What to look out for:

- **Outfits and heads in only one skin tone** (much custom content) cannot be checked for skin, so they
  are not offered: their tab says so. Hats and gloves are on the head and hand textures, so the same
  goes for them.
- **A thin line of the old colour** can show where clothes or hair meet skin (a collar, a hairline). The
  game blends the two along that edge, so those pixels differ between skin tones and are left alone.
- **If something that is not skin will not change**, the game's versions in other skin tones may differ
  there too. The tab's line above the buttons says what it found.
- **Sim pictures in the interface** (portraits, relationships) are mostly drawn from copies of the
  Sim, which the pack does not know about, so they may keep the original colours. Create-a-Sim does.

## Saving

Styles are saved when the lot is saved, in this pack's own file beside Simitone's saves
(`LocalHouse/PackData/sample.createastyle/`): one file for each lot's objects, and one for each
neighbourhood's Sims. The lot's save itself is unchanged. Without the pack the
lot loads with its objects as they were made, and Simitone warns that the pack is missing; switch the
pack back on and the styles are there again.

A style is kept against the object's number on the lot, with its kind. If the lot is played without
the pack and the number passes to another kind of object, the style is dropped rather than put on
the wrong thing.

## Not done yet

- **The catalogue.** A style is made on an object already placed. There is no styling while buying.
- **Old-format sprites** (`SPR#`, which very few objects use) should now work like the others, since
  the pack reads the textures rather than the sprite files, but none have been tried.
- The pack keeps a copy of the pixels of each picture it has recoloured, and each different style on
  show has its own textures. A lot with hundreds of differently styled objects will use noticeably more
  memory.

## What changed in 0.3

- **Sims:** their clothes, hair and hats can be styled, with their skin left as it is. See "Sims" above.
- The panel has a tab for each thing a Sim wears.
- `stylesim` opens the style of the selected Sim, in any mode.

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
- `SimStyles.cs` is the Sims' side: telling skin apart, keeping each Sim's styles, and swapping a
  styled Sim's textures for the styled copies just before they are drawn, and back just after. It is
  hooked in with two more patches, on Avatar.AddBinding (to keep each texture's name, which the engine
  does not) and Avatar.DrawGeometry.
- `CreateAStylePack.cs` is where it meets the engine, as four Harmony patches for objects. In 2D, an object's
  sprites are swapped for the styled copies as the engine lists them for drawing. In 3D, a model is
  shared by every object of its kind, so its textures are swapped for the styled copies just before a
  styled object is drawn, and put back just after.
- `UIStyleBrush.cs` and `UIStylePanel.cs` are the build tool and the panel, and `StyleTargets.cs` what
  the panel is styling: an object, or a Sim and the things they wear. The panel draws itself
  from plain rectangles, so the pack has no artwork.
