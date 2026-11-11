# Try for Baby

**Try for Baby**, from The Sims 2, for The Sims 1. It is WooHoo with a chance of a baby:

- **50%** of tries bring a baby, and **1 in 10** of those babies are twins.
- There is no "do you want a baby?" question. The baby arrives in its bassinet, and the game asks you
  to name it. Twins arrive one after the other, and you name each.
- Otherwise there is no baby this time, and a message says so.

## Using it

The Sims 1's WooHoo is **Play** in the **Vibromatic Heart Bed** (Livin' Large), so Try for Baby
appears on that bed, next to Play. Choose it for one partner, and for the other as you would for
Play. It appears when the Sim:

- is an adult in the household that lives on this lot,
- has a partner there of the other sex (as in The Sims 2), and
- has a daily relationship of at least 50 with that partner, both ways.

The household must also have fewer than 8 members. The game's own rules for Play still apply.

Play itself still works as it always has, and so does a kiss. Either can still bring the game's baby
question by chance, as in The Sims 1.

## Limits

- It needs the Vibromatic Heart Bed, the only bed in The Sims 1 with WooHoo.
- It finds the game's baby question by its English text. With the game in another language it
  cannot answer the question.
- With the desktop `-jit` option, compiled scripts do not go through the steered dice rolls.
- There is no pregnancy. A successful try goes straight to the baby, as The Sims 1's own babies do.

## How it works

The Sims 1's baby lives in the game's own scripts, which Simitone runs. This pack does not change the
game's files. When a menu is first opened on a lot, it finds the baby question in the scripts, the
interactions that lead to it (the bed's Play, and the kiss), and the scripts in between, where the
game rolls its dice. When a Sim starts Try for Baby, it rolls once for the couple, steers those dice
to match, and answers the question for them. For twins the game's baby code runs a second time. The
new interaction is a copy of the bed's Play, kept in memory only.

The terminal shows what was found and what happened, in lines starting with `[baby]`.

## How it is made

This is a port of the `try-for-baby` branches of Simitone and FreeSO. There the feature is one new
class in the engine and a line or two in each of six places that call it. Here nothing in the engine
is changed.

- `VMTryForBaby.cs` is that class as it was, with one small property added. Everything it uses in
  the engine is public, so it needed no other change.
- `TryForBabyPack.cs` is the six calls, each as a Harmony patch, and the message for twins or no baby.

Nothing is saved: a try lasts only while the interaction runs.
