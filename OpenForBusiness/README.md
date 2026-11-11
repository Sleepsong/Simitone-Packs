# Open for Business

A home business for The Sims 1, after **The Sims 2: Open for Business**. Nothing from The Sims 2 is
used: this is the idea, built again on The Sims 1's engine.

**These are stages 1 to 3 of several**, and they have not been played yet: see [State](#state).

## Stage 1: selling from home

- **Put an object up for sale.** In your family's home, click an object from the buy catalog and
  choose **Business > Sell...**. Type the price. The question shows the catalog price to compare
  against.
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

Objects for sale stay usable. Something in use, or with something on it, is not taken until it is free.

A business runs in the family's own home, or on a lot the family owns (stage 3). Only objects that are in the buy catalog can be sold,
and only visiting neighbours buy: not the family, pets, or the maid, the burglar and other NPCs.

## Stage 2: restocking, and a till

- **Restock.** The business remembers each object that sells: what it was, where it stood and its
  price. Restocking buys them again at the catalog's price and puts each back in its place, for sale
  at the price it sold for. So selling above the catalog's price is profit, and selling below it is a
  loss once you restock. Restock from the till's menu (**Till > Restock (§cost)**) or with the
  `business restock` console command. A shelf stays empty if the family cannot afford it or something
  is now in the way, and you are told. `business forget` forgets the empty shelves, for things you
  sold once and do not want back.
- **A till.** Choose **Business > Use as Till** on any catalog object: a counter, a desk, a cash
  register if you have one. There is one till on a lot, and it is not for sale. Its menu has **Tend**,
  **Restock** and **Stop Using as Till**.
- **With a till, customers pay a person.** A customer who decides to buy goes to the till and waits.
  They are rung up, one at a time and the longest wait first, while one of the family stands within
  two tiles of the till: **Tend** sends a Sim there. It takes about three seconds each. A customer
  nobody serves within about two minutes gives up, with the error sound, and the object stays.
- **A tended till sells more.** While someone is at the till, visitors look at things twice as often
  and are 15 points more likely to buy (the catalog price sells 65% of the time, not 50%). What
  nobody would buy, at twice the catalog's price, they still will not.
- **Without a till** nothing changes from stage 1: customers go to the object and leave the money.

Pointing at the till shows how many customers have something in mind.

## Stage 3: a business lot

- **Buy a lot.** Send one of the family to a community lot (downtown, or anywhere else Sims visit).
  The `business` console command says what the lot costs, and `business buy` buys it. The price is
  the lot's value as the neighbourhood has it, and at least §5,000. A lot can belong to one family.
- **Run it like home.** Whenever one of the family is on a lot they own, everything in stages 1 and 2
  works there: price what is on the lot, make something the till, tend it, restock. The other Sims
  on the lot are the customers.
- **Stock it from the neighbourhood.** There is no buy mode on a lot you are visiting. To change what
  the shop holds, edit the lot from the neighbourhood, then visit and price the new things.
- **It stays as you left it.** What sold is still gone on your next visit, and prices, the till and
  the takings are kept.
- **Sell it.** `business sell`, on the lot, gives back what the family paid. The lot goes back to how
  it was built: on the next visit everything that sold is there again and nothing has a price.

Another family visiting an owned lot sees it as it was built, and cannot buy it.

## The books

The `business` console command (Ctrl+Shift+C) shows what is for sale, what has sold out and what
restocking would cost, the till and whether anyone is at it, the latest sales and lost customers, and
the house's totals.

## What is still to come

| Stage | What | State |
|---|---|---|
| 1 | Home business: prices, visitors who buy, the takings | This pack |
| 2 | Restocking sold objects, a till and someone to tend it | This pack |
| 3 | A business lot the family owns apart from its home | This pack |
| 4 | Employees, then business rank, customer loyalty and perks | Not started |

## State

It builds, and its patches apply to the `addon-packs` build of Simitone (all five target methods are
found and patched). The price table and the saved book are checked by a small test. **It has not
been run with the game**, because that needs a copy of The Sims. The parts that only playing can show:

- That visiting neighbours are the Sims with person type 1, as the engine's code suggests.
- That a Sim walks where they are sent (a customer to the object or the till, one of the family to the
  till). If the walk cannot be queued, a customer counts as arrived after about 45 seconds, so a sale
  never depends on it; a family Sim can be walked to the till by hand.
- That a restocked object goes back exactly as it was, above all one that stood on a table or shelf.
  It is placed the way buying from the catalog places it.
- How the menu entries and the price question look.
- On a business lot: that the game keeps the family's money as it is when the Sim goes home, that the
  Sims the game brings to a community lot count as customers, and that taking out the sold objects
  as the lot loads leaves it sound.

There is nothing in the world to mark an empty shelf: that would need a new object, and this pack has
only code. The `business` command and the till's menu say how many there are.

The terminal shows each sale, lost customer and restock in a line starting with `[business]`.

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

The business's book is this pack's own data for each lot, kept with `LotPackData`: the totals, which
object is the till (objects keep their numbers in a lot's save) and the shelves that have sold out.
With the pack off, prices stay in the save and do nothing.

A business lot is different, because the game never saves a lot that is being visited: it is loaded
from its file each time, as it was built. So the lot's file is left alone, and the book also holds who
owns the lot, what is priced and what has sold, each by the object's number in the file. The book is
written whenever it changes, and put back onto the lot each time the owners visit: sold objects are
taken out and prices put on. An object restocked during a visit lasts for that visit; next time the
one in the file is simply left in place. If the lot has been rebuilt and an object is no longer where
the book says, that entry is dropped.
