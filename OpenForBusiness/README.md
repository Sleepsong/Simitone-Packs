# Open for Business

A home business for The Sims 1, after **The Sims 2: Open for Business**. Nothing from The Sims 2 is
used: this is the idea, built again on The Sims 1's engine.

**This is stage 1 of several**, and it has not been played yet: see [State](#state).

## Stage 1: selling from home

- **Put an object up for sale.** In your family's home, click an object from the buy catalog and
  choose **Sell...**. Type the price. The question shows the catalog price to compare against.
- **Change your mind.** A priced object's menu has **For Sale (§price)** with **Change Price...** and
  **Take Off Sale**. Pointing at it shows its price and the catalog's.
- **Visitors buy.** Neighbours who are visiting look at what is for sale now and then. One who decides
  to buy walks over to the object, it leaves the lot, and its price is added to the family's money
  with the buy-mode ca-ching.
- **Price decides.** Each time a visitor looks at an object:

  | Price against the catalog's | Chance they buy |
  |---|---|
  | Half or less | 95% |
  | Three quarters | about 73% |
  | The same | 50% |
  | One and a half times | 25% |
  | Twice or more | never |

  A visitor who looks and does not buy looks again about a minute later.
- **Keep the books.** The `business` console command (Ctrl+Shift+C) shows what is for sale, the
  latest sales, and how many objects the house has sold and for how much.

Objects for sale stay usable. Something in use, or with something on it, is not taken until it is free.

Only the family's own home can be a business, only objects that are in the buy catalog can be sold,
and only visiting neighbours buy: not the family, pets, or the maid, the burglar and other NPCs.

## What is still to come

| Stage | What | State |
|---|---|---|
| 1 | Home business: prices, visitors who buy, the takings | This pack |
| 2 | Restocking sold objects, a cash register and someone to run it | Not started |
| 3 | A business lot the family owns apart from its home | Not started |
| 4 | Employees, then business rank, customer loyalty and perks | Not started |

## State

It builds, and its patches apply to the `addon-packs` build of Simitone (all five target methods are
found and patched). The price table and the saved totals are checked by a small test. **It has not
been run with the game**, because that needs a copy of The Sims. The parts that only playing can show:

- That visiting neighbours are the Sims with person type 1, as the engine's code suggests.
- That a visitor walks to the object. If the walk cannot be queued for them, they buy from where they
  stand after about 45 seconds, so a sale never depends on it.
- How the menu entries and the price question look.

The terminal shows each sale in a line starting with `[business]`.

## How it works

`Shop.cs` is the business and changes nothing in the engine. What is for sale is a field the engine
already has, `VMMultitileGroup.SalePrice`, which FreeSO added for its player shops. It is already
written into a lot's save and The Sims 1 never sets it, so prices need no save data of their own.
FreeSO also disables a priced object, which stops anyone using it and stops its menu opening. This
pack leaves that alone, so the family can use what is on show and reprice it from its menu.

`OpenForBusinessPack.cs` is where the business meets the game, as Harmony patches:

1. A pie menu has been built: add the entries. They are not in any object's files, so they use an
   interaction number no object has, with a parameter that says which entry it is.
2. An interaction is asked for: if it is one of those, this pack carries it out.
3. The lot ticks: visitors look, walk over and buy. The walk uses the engine's own "go here" marker.
4. The lot screen updates: if the engine showed no tooltip over a priced object, show its price.

The totals (objects sold, money taken) are this pack's own data for each lot, kept with
`LotPackData`. With the pack off, prices stay in the save and do nothing.
