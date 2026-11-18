# Undo and Redo

Undo and redo in build and buy mode, as in The Sims 2. Simitone has none outside its object editor.

**Not yet played.** It builds, its patches fit the engine, and its history, its money and how it
asks the other packs for their part of a lot are checked by its tests. Nothing has been undone in a game yet,
because that needs a copy of The Sims. **Back up your `UserData` folders before trying it.**

It is a pack and nothing else: Simitone itself is not changed. It works with the `addon-packs` build
of Simitone as it is.

## Using it

In build or buy mode:

| Key | Does |
|---|---|
| Ctrl+Z | Undo the last change |
| Ctrl+Y, or Ctrl+Shift+Z | Make the last undone change again |

The cheat console (Ctrl+Shift+C) has `undo` and `redo` too, and `undo list` shows the changes kept.
There are no buttons on the screen yet, so on a touch screen the console is the only way.

The changes are kept from when build or buy mode is opened until it is closed again, as in The Sims 2:
going back to live mode forgets them. Moving between buy and build mode does not. Up to 50 changes are
kept; after that the oldest go.

A change is:

- **Building**: walls, wallpaper, floors, pools, the shape of the ground, the roof.
- **Buying, selling and moving** objects.
- **What other packs' build tools do.** Terrain Paint's paint, Roof Tool's roofs and Foundations'
  foundations go through the engine's own build command, so they are changes like any other. Create
  a Style's styles and the Apartments tab say when they have changed the lot.

A change that is still being made (a wall still being dragged) cannot be undone until it is finished.
Undoing while holding an object puts it back where it came from first.

## Money

A change costs what it took from the family's budget, and undoing it gives that back: all of it, not
what the object would sell for now. Undoing a sale takes back what it sold for, and redoing a purchase
pays for it again. If the family does not have the money for that, the undo or redo is refused and
says how much is needed. On a lot that no family plays, building is free and so is undoing.

## Packs

A pack that keeps its own part of a lot (painted ground, roofs, styles, apartments) has to be put back
with the lot, or after an undo the lot and the pack would no longer agree: the paint of a moment ago
on the ground of a moment before, a style on the object that has since taken another's number. Worse,
the lot is put back by loading it again, so a pack that held on to the lot's terrain or objects would
lose what it had altogether.

So each time the lot is kept, Undo asks every other pack for its part of the lot, and hands it back
when the lot is put back. Simitone has no call for this, and Undo changes nothing in Simitone, so it
is a convention between packs. Simitone already lets pack code see the packs in use and their running
code (`PackManager.Active`, `Pack.Code`); Undo looks there for these public methods on a pack's
`ISimitonePack` classes, by name:

```csharp
public byte[] OnLotSnapshot(VM vm)            // what the pack has for the lot now (often what OnLotSaved writes), or null
public void OnLotRestore(VM vm, byte[] data)  // the lot has been put back to the moment of that snapshot
public void OnLotChanged(VM vm, string what)  // Undo's own: a pack's build tool changed the lot (below)
```

`OnLotRestore` is called after the lot's own state is loaded again, before it ticks: everything on it
is a new object with its old number, and the terrain and architecture are new. Replace what the pack
has for the lot with `data` (null if it had nothing), and let go of what it held from before. A pack
that keeps nothing but holds on to objects or Sims needs only `OnLotRestore`, to find them again by
number. Keep `OnLotSnapshot` quick: it is called after every change.

A pack whose build tool changes the lot without the engine's own commands says so by calling
`OnLotChanged(vm, "a few words")` on the code of every pack in use that has it. Create a Style's
`SayChanged` is the dozen lines it takes, and does nothing when Undo is not installed. Changes made
through `VMNetArchitectureCmd` (including command types of a pack's own) and the engine's buy, sell
and move commands need nothing: Undo sees those itself.

**A pack that keeps something for a lot (it implements `OnLotLoaded` or `OnLotSaved`) and has neither
method turns undo off**: while it is on, undo and redo are refused, and say which pack. So does a pack
that took part and whose code has since been stopped, and one whose method fails. This is deliberate:
an undo that leaves a pack out of step can be saved, and then cannot be taken back.

Every code pack in this repository works with it, which the tests check:

| Pack | What is kept and put back |
|---|---|
| Terrain Paint (0.2), Roof Tool (0.2), Foundations (0.4) | The paint, roofs and foundations: put back on the lot's new terrain, as when the lot loads |
| Create a Style (0.2) | The styles, by object number. Says when the brush or the style panel has changed one |
| Apartments (0.23) | The apartments. Says when the Apartments tab has changed them |
| Open for Business (0.5) | Nothing: build and buy mode do not change the books. Shoppers find what they were going to buy again |
| Driveways (0.15) | Nothing: drives, commutes and hidden cars find their cars again |
| Try for Baby (0.2) | Nothing: a try under way finds its couple and bed again |
| Furnished or Unfurnished (0.2) | Nothing, and says so |

Packs that keep nothing for a lot being played (Build Cheats, Half Walls, Diagonal Doors, Cut Content,
Family Tree, Life Stages) need no change.

## Limits

- **A moment's pause.** After each change the whole lot is kept as Simitone saves it, and an undo
  loads it again: on a large lot each can take a moment. The log gives the size of each.
- **No buttons on the screen** yet: the keys and the console only.
- **A change a pack makes without saying so** is not a change of its own: it becomes part of the last
  change, and is undone with it.
- **The selected object's information** may need clicking again after an undo: the objects are new ones.
- What the game screen keeps about a lot outside it is carried across an undo by hand: the cheats
  switched on, the weather, the snow on the ground, and which Sim is selected. Anything else of the
  kind that a reload loses would be lost here too, and is the first thing to look for when playing.

## How it works

`LotMoments.cs` keeps the lot and puts it back. The lot is kept with the engine's own `VM.Save`,
written as Simitone writes a lot's save, and put back with `VM.Load` without a reset, which is how
Simitone loads a lot it handed over earlier in memory. Build and buy mode pause the game (Simitone sets
the lot's speed to -1 while either is open), so nothing on the lot moves between one moment and the
next but the change. Every Sim is put back exactly as they were. `VM.Load` makes the VM's context and
the world's blueprint again; the cheats, the weather and the selected Sim are carried across, and the
game screen is told the new ground has no snow on it yet. Then each pack is handed its part.

`PackParts.cs` finds the other packs' methods and calls them, and works out which packs would be left
out of step.

`UndoPack.cs` watches for changes, with postfixes on the engine's build and buy commands
(`VMNetArchitectureCmd`, `VMNetBuyObjectCmd`, `VMNetDeleteObjectCmd`, `VMNetMoveObjectCmd`,
`VMNetSetRoofCmd`), and `OnLotChanged` for the packs' own. A change's money moves before the change is
made (a purchase is paid for when it is checked, a wall when the engine's queue lets it through), so
a change is kept only once a third of a second has passed with no build or buy command on its way and
nothing in the engine's queue. Its cost is what the budget moved by since the last moment.

Before each undo or redo the lot is kept once more, in place of the moment it is at: anything that
changed without being seen comes back with redo, and what it cost goes with the last change.

`History.cs` is the history and the money, with nothing of the game in it: the moments, which one the
lot is at, what each change cost, and when the family can afford to undo or redo one.

What it does is written to `Undo.log` in the user directory, started again each run.

## Tests

```bash
dotnet run --project Undo/tests
```
