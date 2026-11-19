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
   along the road with the cursor. Once down it is real floor, laid on the ground, and moving or selling
   it puts back what was there before. To pick it up again, click its floor in buy mode.
2. Buy a car: **Sedan**, **Junker**, **Pickup Truck**, **SUV**, **Town Car** or **Bentley**. Each is drawn
   with one of the game's own cars. A car can only be put down on a driveway, on the garden side of the
   pavement. Hold it over a driveway and it slides to fit, and is turned to point along it; turn it a
   notch to swap its front and back.
3. Click the car and choose **Go for a Drive**. Any adult of the household can.

### Getting in and out

For every drive the Sim gets in as the game's carpool has them get in: they go to the car's door, to
the spot the game's cars send a Sim to (its global routing slot 4), and play the game's own animation
for that side of the car. The doors are the ones the game's car the model is drawn as marks; a Sim uses
one on the car's left, the nearer if there are two. Coming back, they get out the same way, the
animation backwards; back from work, the car parks and they get out at the door too. The car sounds as
the game's own car of that model does driving off, as it pulls out and as it comes back.
`driveways door` has Sims use the doors on the car's right instead.

### Driving to work

A Sim with a job can drive to work in their car. **Drive to Work** is on the car's menu from when the
game says the carpool will come (two hours before work) until the carpool leaves.

- Set off early, before the carpool: the Sim drives off and is away, off the lot as a Sim at work is.
  When the carpool comes it is kept out of sight, and the game takes them to work from there.
- Or wait for the carpool: choose Drive to Work while it is at the kerb, and it is hidden as they
  drive off.

Pay, performance and the hours are all the game's own: it is the game that takes them, out of sight.
The car stays away while they are at work. At the end of the day the carpool that brings them back
is hidden too: their car drives in, and they get out of it beside the driveway.

**Always Drive to Work**, on the car's menu, has the car do this by itself: when the carpool comes for
anyone in the household with a job, who is awake and at home, Drive to Work is queued for them, as if
you had chosen it. Once a day each: if you cancel it, they take the carpool. Choose it again to stop.
It is kept with the car, and saved with the lot.

Cancelling Drive to Work while they are still on the lot or waiting brings them straight home. If the
game will not take them (no carpool comes, it has gone, or it will not have them), the car comes home
and a message says so.

### Driving to a community lot

The car's **Drive to...** menu lists the community lots in your game, by part of town: Downtown, Old
Town, Studio Town, Magic Town, and lots in the neighbourhood zoned for the community. Choose one, and
the Sim drives off; once the car has left the lot, the game takes them there as its taxi would. What
they find there, and getting home, are the game's own: they come home the way the game brings them,
and the car is on the driveway, as the house was left.

Only the Sim who drives goes. Only community lots are on the list: not houses (in Old Town, or lots
zoned as houses anywhere, or any a family lives in), and not the vacation island. Cancelling while the car is
still on the lot brings it straight back. If the game does not change lots, the car comes home and a
message says so.

### Going for a drive

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
| `driveways work` | Writes how the game's carpool, car portals and taxi work (their menus and scripts) to `driveways-work.txt` |
| `driveways reverse` | If the cars drive along the road backwards, this turns them round. It is remembered |
| `driveways door` | Sims get in by the car's other side, if getting in looks wrong. It is remembered |
| `driveways help` | What the commands are |

## If the cars and driveways are grey blocks

If the cars and the driveway look like grey blocks, and cannot be put down or used, the pack's objects
are there but its code is not running. Open **Options > Packs**: under Driveways, in red, the game says
why (for instance, that its code has not been approved: switch the pack off and on again, and restart).
Before 0.18.2, "Primitive opcode 200 is already used" meant Cut Content was on: update Driveways.

## Changes

- **0.19**: getting in where the game's carpool has Sims get in, and no houses on the Drive to... menu.
  - A Sim now goes to the spot the carpool sends Sims to before getting in (the game's global
    routing slot 4, beside the door), instead of the tile next to the car, so the animation starts
    where it should. The doors are the tiles the game's own car marks as its doors, not a guess, and
    the animation is the one the carpool plays for that side (it was the other side's before). Back
    from work, a Sim gets out at the door the same way, rather than appearing beside the car.
  - Drive to... no longer lists houses. Driving to one (in Old Town, say) left the game on an empty
    lot. A lot is listed only if it is zoned for the community (in the neighbourhood and Old Town,
    which are zoned lot by lot), or is one of Downtown's, Studio Town's or Magic Town's, and no
    family lives there.
  - The `driveways` report says where each car's doors are.

- **0.18.2**: Driveways works alongside Cut Content. Both packs added a primitive under the number 200:
  Cut Content takes the first free numbers from 200 up as it starts, and when it started first the game
  refused Driveways' and stopped its code, leaving the cars and the driveway grey blocks that could
  not be put down or used. Driveways now uses 250. If that is ever taken too, it takes another free
  number and changes its own scripts to call it, and the `driveways` report says so. The build checks
  both: that the pack starts with its number free, and with it taken.

- **0.18.1**: the pack starts whatever Simitone it is given. (Not the cause of the grey blocks: that
  was the primitive's number, fixed in 0.18.2.) 0.18 looked for the game's save and load as
  the pack started, before the patches that draw the cars and put them down, and a Simitone with two
  methods of either name would have stopped the pack there, leaving grey blocks that could not be
  placed or used. Those patches now come last, each on its own, and one that cannot be attached only
  loses what it is for (it is named in the `driveways` report). The build now starts the pack against
  the engine and fails if it does not start, or if any patch does not attach.

- **0.18**: cars stop for Sims, and drives are saved.
  - A car stops for anyone in its way, on the driveway or the pavement, as it pulls out or comes
    home: a Sim, a visitor or a pet. After a second and a half it sounds its horn, the game's own
    for that car, and again every five seconds. Someone who stays put for twenty seconds is driven
    past, so nobody can keep a car out for ever. Before, a Sim who stepped onto the driveway after
    the car had set off was driven through.
  - Waiting to be taken to work after leaving early, the Sim is off the lot, out of the world as a
    Sim at work is, not standing invisible by the driveway. If the car comes home with them, they
    are put back where they got out of it.
  - A lot saved in the middle of a drive, or while a Sim is at work, carries on where it was when it
    is loaded: the car is where it was on its way, and a Sim at work comes home in their car. The
    pack keeps this in a file of its own beside the save, `LocalHouse/PackData/sample.driveways/`,
    and the save itself is not changed. The same goes for the home lot while a Sim is on a
    community lot: a Sim at work while another drives into town still comes home in their car.
    A drive that cannot be put back as it was (the car or the Sim is not there, or not where the
    drive would have them) ends with the car parked, as before.

- **0.17**: smooth turns in 3D. The car rounds the corner between the driveway and the road on a curve,
  and in 3D turns smoothly through it. In 2D it follows the same curve but its picture still snaps
  round half way, since the game has four pictures of each car. The camera can change mid-drive.

- **0.16**: getting in and out, engine sounds, and driving to work by themselves.
  - Sims walk to the car's door and get in with the game's own animation, and get out the same way
    when they are back, from a drive or from work. Before, they vanished beside the car.
  - Before walking over, the car says if it cannot go (no road, something in the way, not time for
    work), so nobody gets in for nothing.
  - The car sounds as the game's car of its model does driving away, read from the game's own script
    for it, as it pulls out and as it comes back.
  - **Always Drive to Work** on the car's menu.

- **0.15**: **Drive to...** a community lot, a first stage. The car's menu lists the community lots in
  the game by part of town, and the game's own lot change takes the Sim there once the car has left.
  The `driveways` report lists the lots the car can drive to.

- **0.14**: fixed Drive to Work not being offered. The pack read the time and the day from a copy the
  game only fills in when a lot is loaded, so it worked out the wrong time of day, and after one day at
  work could think the Sim had already been that day, every day. It now reads them from the lot's
  clock, as the game's own scripts do. A fault in working out the hours can no longer hide the offer
  while the carpool is there. The `driveways` report now gives the time, and for each Sim with a job
  when they start work and when they can drive there.

- **0.13**: Sims come home from work in their car, and can leave for work early.
  - Coming home: the carpool that brings them back is hidden, and their car drives in with them in it.
    Before, the carpool was seen dropping them off and the car followed, empty.
  - Leaving early: Drive to Work is offered from the game's "the carpool will arrive in about an hour"
    warning. They drive off, and are handed to the carpool, out of sight, when it comes.
  - Fixed: "could not be taken to work from the car" appeared when the Sim came home, and sent the car
    home after the carpool. The game keeps a Sim at work out of the world, still busy with the car
    portal, rather than off the lot, which the pack did not expect; it now tells the two apart.

- **0.12**: **Drive to Work**, a first stage. While the carpool waits for a Sim, their car offers to
  take them instead: the carpool is hidden, they drive off, and the game's own "Go to Work" takes them
  from there. The car comes home when they do. The game still brings them home in its carpool. The
  `driveways work` list now includes the lot's car portals and the taxi, which is what the next stages
  (coming home in the car, and driving to community lots) need.

- **0.11**: a driveway that has been put down is only its floor. Since 0.8 the driveway laid real floor
  under itself but was also still drawn on top, a thin slab just above the ground, so the two showed as
  a dark strip and a floating one. The slab is now hidden once the driveway is down: what you see is the
  floor, flat on the ground, with the pavement open across it. While you hold a driveway its picture
  still shows where it will go. To pick one up again, click its floor in buy mode. Driveways already on
  a lot are put right when it loads.

- **0.10**: a review of the pack.
  - A car held over a driveway slides to lie wholly on it, so it no longer has to be lined up tile by
    tile, and no longer seems to jump when it is turned to face the road.
  - Cancelling a drive while the car is still on its way out turns it straight round. Before, only
    cancelling once it had left the lot did.
  - The opening for the pavement now shows at once in 2D as well as 3D.
  - Nothing the pack does on the game's tick, or during a drive, can stop the game any more: a fault
    there ends the drive and puts the car and the Sim back.
  - `driveways help` (or any command it does not know) says what the commands are. The `driveways`
    report drops a line that no longer meant anything and says how many driveways are on the lot.
  - Quicker checks on every object the game moves, and the looking-up of floors and cars is done by
    one thread at a time.

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

- **The carpool is still there, out of sight.** It is what takes a Sim to work and brings them back,
  so its horn can still be heard, morning and evening.
- **Driving to a community lot: they arrive and come home the game's way.** They do not drive up to
  the community lot, and they come home without the car; it is parked at home all along. Only the
  Sim who drives goes, without the household or a date. Studio Town and Magic Town have their own
  ways of arriving, and have not been tried.
- **A drive is saved beside the save, not in it.** Copy a neighbourhood to another computer without
  `LocalHouse/PackData`, and a lot saved mid-drive loads with the car parked, and a Sim at work
  comes home by carpool. A save made by an older version of the pack does the same.
- **Which side is the driver's is a guess.** Sims use the doors on the car's left, which may be the
  passenger's; `driveways door` has them use the right. A car whose game car is not in your game has
  no doors marked, and uses the third row.
- **The engine sound is the game's carpool's.** It is the sound for driving away, played as the car
  pulls out and as it comes back; there is none while it waits or drives on the driveway.
- **Only what is drawn moves.** For the game's rules the car never leaves its parking place, and the Sim
  stays standing next to it (unless they are waiting for the carpool, off the lot). Other Sims walk
  round the car while it is "away".
- **In 2D the car turns in quarter turns.** It has four pictures, like every Sims 1 object, so it snaps
  round half way through the corner. In 3D it turns smoothly.
- **Which end of a car is its front is a guess.** If they drive along the road backwards, `driveways
  reverse` turns them round.
- **Nothing gets run over, but a Sim who will not move is driven past.** The car waits twenty seconds
  for someone standing in its way, then drives on through them. It only looks on the lot: on the road,
  nobody walks.
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
| `FloorDress.cs` | Paints the driveway's preview, while it is held. It makes a second set of sprites in memory, the shape and depth of the grey square, each pixel coloured from the place on a floor tile of the game's that it lies over. Nothing of the game's is copied into the pack |
| `VMDrive.Saving.cs`, `Saved.cs` | Keeping drives under way with the lot: the pack's copy of them whenever the game takes one of the lot, written to a file beside the save, and put back when the lot is loaded. `Saved.cs` is the file's format, plain numbers |
| `VMDrive.cs` | A new SimAntics primitive, number 200. The objects' scripts are one or two instructions that call it: to set an object up, to say whether the car is parked, and to drive. Also where a driveway and a car may be put, and what each is drawn with: the floor for the driveway, and the game's cars' tiles for the cars |
| `Route.cs` | Where things go: finding the road, the car's way out and back and how fast, where a driveway snaps to, and where a car fits on one. Plain numbers, nothing from the engine |
| `tests/` | Checks `Route.cs`, and that `Saved.cs` reads back what it writes |
| `startup/` | Starts the pack as Simitone does, against the engine it is built for: it must start, attach every patch and register its primitive, also when another pack has its primitive's number |
| `DrivewaysPack.cs` | Adds the primitive and the console commands, and Harmony patches: on the game's tick, so a car is never left hidden if its drive is cut short, and a lot loaded mid-drive gets its drives back before anything runs; when the game takes a copy of the lot, saves it, or loads it, to keep the drives with it; round an object being moved or deleted, to snap a driveway to the road, keep cars on driveways, and lay and take up the driveway's floor; when an object's picture is set, to give the driveway's preview and the cars theirs; when the game looks for an object under the cursor, so a driveway that is down can be clicked; and when the player lets go of something, so a driveway that has been moved stays hidden |
| `Shared.cs` | The ids and sizes the object file and the code must agree on, and the cars: which of the game's each is drawn with. Compiled into both |

To write the object file again, and see a driveway painted with a made-up test tile:

```bash
dotnet run --project Driveways/generator -- Driveways/pack/driveways/content/Downloads previews
dotnet run --project Driveways/tests
```

The second folder is optional. It gets `driveway.ppm`.

## What comes next

Roughly in order: watching all of this run in the game and fixing what that shows; driving up to the
community lot rather than arriving the game's way; the household or a date coming along.
