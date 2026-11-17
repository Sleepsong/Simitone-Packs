# Lots Bin

The Sims 2's lots bin, for Simitone: save a copy of a lot, and put it down as another lot.

**Not yet played.** It builds, its patch fits the engine, and the bin's folders, how a copy is made
another house's, and how the packs' data goes with it are checked by its tests. Nothing has been
copied in a game yet, because that needs a copy of The Sims. **Back up your `UserData` folders before
trying it.**

## Using it

Both are on the neighbourhood screen. Choose a lot, then **More**.

- **Save to Bin** (where the panel had a greyed-out Export) puts a copy of the lot in the bin. It
  asks what to call it, starting from the lot's own name. The copy is the lot **as it was last
  saved**: save in the lot first if you have just built something.
- **Place from Bin** (on a lot nobody lives on, where the panel had a greyed-out Bulldoze) opens the
  bin. It shows its lots one at a time, with their pictures; **<** and **>** turn the pages. **Place
  from Bin** puts the one shown down on this lot, and **Delete** takes it out of the bin.

A lot in the bin can go on any lot of the **same size** that nobody lives on, in any neighbourhood.
If the lot there faces another way, or its road and pavement are laid out differently, the bin says
so and asks first: the copy brings its own road and pavement, which will not match the street around
it.

Putting a lot down replaces what was there. That lot's file and its packs' data are kept first, in
`LotsBin/Replaced/` in the user directory, in case it is wanted back (copy the house file back into
the neighbourhood's `Houses` folder by hand).

## What goes with a copy, and what does not

It goes:

- Everything that was built and bought: walls, floors, the shape of the ground, pools, roofs, every
  object as it was (dirty dishes included), and what is up for sale.
- What packs added to the lot, through Simitone's pack data calls: painted ground (Terrain Paint),
  drawn roofs (Roof Tool), foundations (Foundations), styles (Create a Style), and an apartment
  building's apartments (Apartments). A pack decides what of its own goes. Apartments sends the
  apartments, all vacant. Open for Business sends none of the business's books.
- The packs the lot was saved with, so the lot it is put down as warns of a missing pack just as the
  original would. The bin shows them too.

It does not go:

- The Sims and pets who live there, and anything they are holding.
- The household's family, money, and day-by-day accounts.
- The lot's name and description. Those belong to the lot in the neighbourhood, so the lot it is put
  down as keeps its own. The bin keeps the copy's name, which you can choose when saving.
- The price of the land. A lot put down is priced at the land it is on, plus what was built.
- The ground's look. Snow, sand or grass is the one of where it is put down.

## Where the bin is

`LotsBin/` in the user directory (next to `config.ini`). Each lot is a folder:

| File | What it is |
|---|---|
| `lot.iff` | The lot, without its Sims, in the same form Simitone saves a lot in (an `FSOV` chunk), with its `SIMI` and its pictures |
| `lot.json` | Its name, description, size, value, where it came from, and the packs it was saved with |
| `packs/` | Each pack's data for it, one file for each pack, named by the pack's id |

A folder can be copied into another computer's `LotsBin/` as it is. What the bin does is written to
`LotsBin.log` in the user directory.

## Known gaps

- **Babies.** In The Sims a baby is an object, in its crib, and goes with a copy like any other
  object. Save a lot with no baby in it.
- **The household's other things on the lot**, such as bills waiting in the mailbox, go with the copy:
  only the Sims are taken off, and what they hold.
- **No turning.** A lot only goes on a lot of the same size, as it is. It is not turned to face
  another street.
- **The picture on the neighbourhood screen** is the one the original lot was saved with.
- Saving and placing take a moment on a large lot: the lot is read and written as it would be by
  entering and saving it.

## How it works

Most of what it needs was in the engine already. `VMBlueprintRestoreCmd`, which loads every lot,
already loads a lot kept as Simitone's saved state (an `FSOV` chunk), and that is what a copy is. The
XML blueprints the engine can also write (`VMWorldExporter.SaveHouse`, behind a debug button in
`TS1GameScreen`) are not used: they keep only walls, floors and where objects stand. The shape of the
ground, pools, roofs, objects' state, a lot's size and The Sims' own lot information would all be lost.

`LotCopy.cs` does the copying:

- **Into the bin.** The house's file is loaded into a VM of its own, with no world and nothing
  running. A lot Simitone has saved is read as its saved state; one in The Sims' own format is
  converted as entering it would. What Sims hold is deleted, then the engine's own `VM.Reset` takes
  every Sim off, as it does when a lot's saved state is loaded. The family and its accounts go, the
  lot's value is worked out again, and the state is written into `lot.iff` with the lot's pictures.
  If anything on the lot could not be loaded (missing custom content, or a content pack switched off),
  nothing is put in the bin: the copy would be without it.
- **Out of the bin.** The copy's state is made the house's: its number (global 10, which the game
  saves a lot under, and 11, which it loads one as), the ground of where it is, and the price of the
  land. It is written as the house's file, and the packs' data follows.

The packs' data goes through `PackManager.ExportLot` and `ImportLot`, which Simitone added for this.
Each pack's `ISimitonePack.OnLotExport` decides what of its data for the lot goes with a copy, and
`OnLotImport` what to keep when the copy is put down. Both pass the data through unchanged unless a
pack says otherwise, which suits data about what was built. A pack whose code is not running sends
its data unchanged. See Simitone's `PACKS.md`.

One Harmony patch (a postfix on `UIHouseSelectPanel`'s constructor) gives the two buttons their jobs.
If a later Simitone gives them jobs of its own, the pack leaves them alone and says so in its log.

The pack needs a Simitone with `PackManager.ExportLot`. On an older one its code does not start, and
the Packs screen says why.

## Tests

```bash
dotnet run --project LotsBin/tests
```
