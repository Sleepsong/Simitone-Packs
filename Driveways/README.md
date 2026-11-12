# Driveways

A **driveway** and a **car your Sims can drive**, in the spirit of The Sims 2 Nightlife, for The Sims 1.

**This is a first stage.** The objects have been placed in the game; driving itself has had little
testing. Expect to find things to fix. The `driveways` console command (below) is there to help with that.

## Using it

The objects are in buy mode under **Decorative > Other**.

1. Lay **Driveways** end to end from where the car will stand to the edge of your lot by the road. A
   Driveway is two tiles wide and four long, the size of the car. **Driveway Tile** is a single square,
   for filling in where a whole Driveway does not fit.
2. Put the **Runabout** on the driveway with its front or its back towards the road.
3. Click the car and choose **Go for a Drive**. Any adult of the household can.

The Sim walks to the car, the car pulls out along the driveway, turns onto the road and drives off the
lot. It is away for about 20 seconds at normal speed, and the Sim's Fun goes up all the while. Then it
comes back from the other end of the road and parks where it was. Cancelling the interaction while the
car is away brings it straight home.

If the car cannot be driven, a message says why:

| Message | What to do |
|---|---|
| The car needs a driveway under it, all the way to the road | Every tile the car passes over, the ones it is parked on included, needs driveway on it, up to where your lot stops and the verge begins |
| Something is in the way between the car and the road | Move the object, wall or fence that is on the driveway |
| The car has to be parked with its front or its back towards the road | Turn it a quarter turn |
| There is no road on this lot to drive on | The pack did not recognise this lot's road. Run `driveways` and report what it says |

### The `driveways` command

Open the console (Ctrl+Shift+C) and type `driveways`. It shows where the pack thinks the road is on this
lot, which floor tiles the part of the lot you cannot build on is made of, and whether each car on the
lot can be driven right now.

## Changes

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
- **Nothing gets run over.** The car checks that the driveway is clear before it sets off. A Sim who
  walks onto the driveway afterwards is driven through.
- **The road is found by its tiles.** The pack looks for The Sims 1's two road floor tiles, or failing
  that the paved strip you cannot build on. This is the part most likely to need adjusting on real lots.
- The car is a few coloured boxes. It has no art files: like the Test Cube, its pictures are drawn by
  the generator.

## How it is made

| | |
|---|---|
| `pack/driveways/content/Downloads/Driveways.iff` | The three objects. Written by `generator`, and committed, since it is data |
| `generator/` | Writes that file, then reads it back and checks it. The car and the driveway are lists of boxes; the generator traces rays against them to draw each sprite and its depth, with the camera maths the engine uses to rebuild a 3D model from a sprite. The car covers eight tiles, and each tile gets its slice |
| `VMDrive.cs` | A new SimAntics primitive, number 200. The objects' scripts are one or two instructions that call it: to set an object up, to say whether the car is parked, and to drive |
| `Route.cs` | Where the car goes: finding the road, the way out and back, and how fast. Plain numbers, nothing from the engine |
| `tests/` | Checks `Route.cs` |
| `DrivewaysPack.cs` | Adds the primitive and the console command, and one Harmony patch on the game's tick so a car is never left hidden if its drive is cut short |
| `Shared.cs` | The ids and sizes the object file and the code must agree on. Compiled into both |

To write the object file again, and look at what it drew:

```bash
dotnet run --project Driveways/generator -- Driveways/pack/driveways/content/Downloads previews
dotnet run --project Driveways/tests
```

The second folder is optional. It gets `.ppm` pictures of the car from each side, next to its depth.

## What comes next

Roughly in order: watch it run and fix what that shows; real art; Sims getting in and out; the car
taking a Sim to work in place of the carpool; driving to community lots.
