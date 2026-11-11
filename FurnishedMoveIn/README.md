# Furnished or Unfurnished

When a family moves into a house, choose how they take it, as in The Sims 3:

- **Furnished:** the house as it stands, with everything in it, at the game's usual price.
- **Unfurnished:** the house without its furniture, for less. The price drops by what the furniture
  that is taken out is worth.

## Using it

Move a family in as usual. While you choose a house, its panel shows the unfurnished price under the
usual one. Press **Move In**, and where Simitone used to ask "move in?", it now asks **Furnished**,
**Unfurnished** or **Cancel**, with both prices and what the family has.

A family that cannot afford a house furnished, but can afford it unfurnished, can now move in. They
are only offered **Unfurnished**.

There is nothing to choose for an empty plot, or for a house with nothing in it that would be taken
out. Those ask "move in?" as before.

## What stays in an unfurnished house

What is built into the house stays, as in The Sims 3:

| Stays | Goes |
| --- | --- |
| Everything from build mode: doors, windows, stairs, fireplaces, plants, the pool's things | Seating and beds |
| Kitchen counters | Tables, end tables and desks |
| Stoves and fridges | Small and other appliances |
| Toilets, sinks, showers and baths | Hot tubs |
| Wall and ceiling lights | Table and floor lamps |
| The mailbox, the bin, and anything else that is not in the catalog | Electronics, decorations, and everything under Miscellaneous |

Anything standing on a piece of furniture goes with it. Something on a kitchen counter is judged for
what it is: the microwave goes, a sink set into the counter stays.

## Limits

- The furniture is removed, not kept somewhere. To have it again, buy it again.
- An object is sorted by the catalog section it is filed under. A custom object filed in the wrong
  section is treated as what it claims to be. Plumbing that names no section at all is kept.
- Magic Town's houses also cost MagiCoins. The choice is offered there, but a family without the
  MagiCoins still cannot move in.
- The furniture is taken out as the lot opens. As with any move, nothing is final until you save.

## How it works

Nothing in the engine is changed, and nothing new is saved.

- `Furniture.cs` holds the rule for what furniture is, and reads a lot into a plain list of its
  things, with what each was bought for. It can read a lot that is running, a house saved by
  Simitone, and a house saved by The Sims, so the price quoted from the house's file is for exactly
  the things removed once the lot is loaded.
- `FurnishedMoveInPack.cs` has two Harmony patches. One runs after the house panel is built, to add
  the unfurnished price and unlock **Move In**. The other runs after the neighbourhood screen is set
  up, to put the question in front of Simitone's own. Simitone then moves the family in exactly as
  it always does; for an unfurnished house the pack first gives the family the difference, and takes
  the furniture out when the lot has loaded (`OnLotLoaded`).

The terminal shows what happened in lines starting with `[unfurnished]`.
