# Driveways

A **driveway** and **cars your Sims can drive**, in the spirit of The Sims 2 Nightlife, for The Sims 1.
The cars are the game's own, and the driveway is paved with one of the game's own floors: the pack
brings no pictures of its own.

**This is a first stage.** Driving and snapping the driveway to the road work in the game. The cars'
look works too. The floor under the driveway and the opening for the pavement are new in 0.8 and have
not been seen in the game yet.

## Using it

The objects are in buy mode under **Decorative > Other**.

1. Pick up the **Driveway**. It is five tiles wide, after The Sims 2's, and joins your garden to the
   road: it runs to the tarmac, with the pavement left open across it. It can only go there, and slides
   along the road with the cursor. It lays floor under itself, and takes it up again when it goes.
2. Buy a car: **Sedan**, **Junker**, **Pickup Truck**, **SUV**, **Town Car** or **Bentley**. Each is drawn
   with one of the game's own cars. A car can only be put down on a driveway, on the garden side of the
   pavement, and is turned to point along it.
3. Click the car and choose **Go for a Drive**. Any adult of the household can.

The Sim walks to the car, the car pulls out along the driveway, turns onto the road and drives off the
lot. It is away for about 20 seconds at normal speed, and the Sim's Fun goes up all the while. Then it
comes back from the other end of the road and parks where it was. Cancelling the interaction while the
car is away brings it straight home.

If the car cannot be driven, a message says why:

| Message | What to do |
|---|---|
| The car needs a driveway under it, all the way to the road | Park the car on the Driveway, all of it |
| Something is in the way between the car and the road | Move the object, wall or fence that is on the driveway |
| There is no road on this lot to drive on | The pack did not recognise this lot's road. Run `driveways` and report what it says |

### What the driveway is paved with

The pack has no texture of its own. The driveway is painted with one of the floors from your game: the
first one the game sells with "concrete", "cement", "asphalt", "gravel" or the like in its name, or
else the road's own tarmac. Never the pavement's. To choose:

| Console command | |
|---|---|
| `driveways floors` | Lists every floor the game sells in `driveways-floors.txt` |
| `driveways floor <words from a name>` | Paints driveways with that floor, at once, and remembers it |
| `driveways floor tarmac` | The road's tarmac |
| `driveways floor` | Back to the pack's own choice |

### Other console commands

Open the console with Ctrl+Shift+C. The files are written to Simitone's user directory, next to
`config.ini`.

| | |
|---|---|
| `driveways` | Where the pack thinks the road is on this lot, what the driveway and the cars are drawn with, and whether each car can be driven now. Also written, with maps of the lot, to `driveways-report.txt` |
| `driveways cars` | Lists the game's vehicles in `driveways-cars.txt` |
| `driveways work` | Writes how the game's carpool works (its menu and scripts) to `driveways-work.txt` |
| `driveways reverse` | If the cars drive along the road backwards, this turns them round. It is remembered |

## Changes

- **0.9**: a driveway no longer jumps sideways as it is put down. (The game places an object where it
  is already being held; the pack took that for the cursor and centred the driveway on it a second
  time.) A car held sideways to the driveway is turned to face the road, and turning it a notch now
  goes between nose to the road and tail to the road.

- **0.8**: the driveway lays real floor under itself, the same floor it is painted with, so it meets the
  road with no strip of grass showing between them. Where it crosses the pavement it is open: the
  pavement runs on across it. Moving or selling the driveway puts back the floor that was there
  before. Driveways already on a lot are done when the lot next loads. `driveways work` writes out
  how the game's own carpool works, for the next stage: driving to work.

- **0.7**: real cars. The game's own cars stand on three tiles by six, so the pack's now do, and there
  are six to buy, each drawn with one of the game's: the carpool's sedan, the junker, the pickup truck,
  the SUV, the town car and the Bentley. The box car is gone. A car can only be put down on a driveway.
  The driveway is five tiles by nine, and is no longer paved like the pavement: it takes a floor the
  game sells (see above), or the road's tarmac, and you can choose which. Driveways and the car from
  earlier versions stay on lots that have them, as plain shapes, but are not sold and that car cannot
  be driven.

- **0.6**: the driveway has no texture of the pack's own any more. It is painted, when the game runs,
  with the pavement floor tile out of your own copy of The Sims, so it matches the pavement it crosses.
  The joints and the ridged apron that 0.5 drew are gone. The `driveways` report says whether the
  painting worked; if it did not, the driveway shows as plain grey.

- **0.5**: a new Driveway after The Sims 2's: three tiles by eight, cast in panels, crossing the pavement
  and the verge to an apron at the kerb. It snaps with its apron against the tarmac. The two by four
  driveway of 0.2 to 0.4 is no longer sold; ones on a lot stay. The car is drawn with a real Sims 1
  car's pictures when the game has one standing on the same two by four tiles as the pack's; otherwise
  it stays as boxes, and `driveways cars` lists the game's vehicles so the pack can be fitted to one.
  The `driveways` report says how often a driveway has been snapped, and what the car is drawn with.

- **0.4**: the road is found on real lots. A report from the game showed the tarmac is floor tile 9,
  with tile 11 only for the dashes of the middle line (which is all 0.1 to 0.3 looked for), the
  pavement is tile 12, and nothing on the lot is marked as impossible to build on (which is what 0.3
  went by). The Driveway now snaps against the pavement, and the car crosses the pavement and the
  verge to the road.

- **0.3**: the Driveway snaps to the road. The road is now found from the strip of the lot you cannot
  build on, which fixes "There is no road on this lot" on lots where it was looked for by its floor
  tiles and not found. The single square, Driveway Tile, is no longer sold; ones already on a lot stay,
  and can be picked up and sold. The `driveways` command also writes `driveways-report.txt`, with maps
  of the lot, in Simitone's user directory.

- **0.2**: the Driveway is now two tiles by four; the single square is still there as Driveway Tile. The
  car and the driveways can be picked up, moved and sold: in 0.1 they could be put down but never picked
  up again. Ones placed with 0.1 are put right when their lot is next loaded.

## Limits

- **The car does not go anywhere.** A drive is a trip off the lot and back, for Fun. It does not take a
  Sim to work or to a community lot yet.
- **The Sim does not get in.** They walk to the car and vanish until it is back. There are no animations
  for getting in and out.
- **Only what is drawn moves.** For the game's rules the car never leaves its parking place, and the Sim
  stays standing next to it. Other Sims walk round both while they are "away". The good side of this is
  that nothing about a drive is saved: a lot saved in the middle of one loads with the car parked and the
  Sim beside it.
- **The car turns in quarter turns.** It has four pictures, like every Sims 1 object, so it snaps round
  the corner onto the road.
- **Which end of a car is its front is a guess.** If they drive along the road backwards, `driveways
  reverse` turns them round.
- **Nothing gets run over.** The car checks that the driveway is clear before it sets off. A Sim who
  walks onto the driveway afterwards is driven through.
- **The road is found by its floor tiles.** Tarmac is tile 9 (and 10 and 11, the line down the
  middle), in a strip along one side of the lot; the pavement, tile 12, marks where the garden starts.
  A lot that paves its road differently will not be found.
- **A car needs its own game car.** A car whose game car is not in your game (from an expansion you do
  not have) shows as grey blocks. The `driveways` report says which were found.
- **Floors are found by their names in English.**

## How it is made

| | |
|---|---|
| `pack/driveways/content/Downloads/Driveways.iff` | The objects: what they are, and almost nothing of what they look like. Written by `generator`, and committed, since it is data |
| `generator/` | Writes that file, then reads it back and checks it. Every tile of a driveway or a car is its own object; all the driveway's share one flat grey square and all the cars' one grey block, drawn by tracing rays with the camera maths the engine uses to rebuild a 3D model from a sprite |
| `FloorDress.cs` | Paints the driveway. It makes a second set of sprites in memory, the shape and depth of the grey square, each pixel coloured from the place on a floor tile of the game's that it lies over. Nothing of the game's is copied into the pack |
| `VMDrive.cs` | A new SimAntics primitive, number 200. The objects' scripts are one or two instructions that call it: to set an object up, to say whether the car is parked, and to drive. Also where a driveway and a car may be put, and what each is drawn with: the floor for the driveway, and the game's cars' tiles for the cars |
| `Route.cs` | Where the car goes: finding the road, the way out and back, and how fast. Plain numbers, nothing from the engine |
| `tests/` | Checks `Route.cs` |
| `DrivewaysPack.cs` | Adds the primitive and the console commands, and Harmony patches: on the game's tick, so a car is never left hidden if its drive is cut short; round an object being moved or deleted, to snap a driveway to the road, keep cars on driveways, and lay and take up the driveway's floor; and when an object's picture is set, to give driveways and cars theirs |
| `Shared.cs` | The ids and sizes the object file and the code must agree on, and the cars: which of the game's each is drawn with. Compiled into both |

To write the object file again, and see a driveway painted with a made-up test tile:

```bash
dotnet run --project Driveways/generator -- Driveways/pack/driveways/content/Downloads previews
dotnet run --project Driveways/tests
```

The second folder is optional. It gets `driveway.ppm`.

## What comes next

Roughly in order: Sims getting in and out; the car
taking a Sim to work in place of the carpool; driving to community lots.
