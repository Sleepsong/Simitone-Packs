# Family Tree

Family ties, from The Sims 2, for The Sims 1: who is married to whom, whose child is whose, and a
family tree for each Sim.

The Sims 1 keeps no family ties at all. It only knows how well two Sims get on, so a married couple
made in Create a Family begin as two strangers who share a house. With this pack you say what they
are to each other, and they begin that way.

**It has not been run in the game.** It has been compiled, its patches checked against the engine,
and its rules and saving checked without the game (below). Its windows have not been seen on a
screen, and how it follows babies and marriages rests on a reading of the engine that only play can
confirm. **Back up your `UserData` folders before trying it.**

## Using it

### When you make a family

In **Create a Family**, once the family has two Sims, a **Family ties** button appears at the top
right. It lists every pair of Sims in the family. The button on a pair moves on to the next thing
they can be:

| Choice | Meaning |
|---|---|
| **No tie** | Housemates. This is what every pair starts as |
| **Married** | Husband and wife |
| **Siblings** | Brother and sister. Only needed when their parents are not in the family: children of the same parent are siblings already |
| **(name) is parent** | One is the other's mother or father, either way round |

Choices the rules do not allow are passed over, and the window says why. The rules:

- Only adults marry or are parents, and a Sim has one spouse and at most two parents.
- Family by birth cannot marry: parents, children, siblings, grandparents, aunts and uncles, cousins.
- Nobody is their own ancestor, and a parent is not also a sibling.

Everything else is worked out from what you chose: grandparents, aunts and uncles, cousins,
step-parents and in-laws. If you take a Sim out of the family, their ties go with them.

When you accept the family, its Sims start out getting on as family does. Each pair starts with at
least these scores, both ways (the game's scores run from -100 to 100):

| They are | Daily | Lifetime |
|---|---|---|
| Married | 90 | 80 |
| Parent and child | 75 | 65 |
| Siblings, grandparent and grandchild | 60 | 50 |
| Step-parent and step-child | 40 | 30 |
| Aunt or uncle, cousins, in-laws | 35 | 25 |

A family made with no ties chosen is exactly what it was without the pack.

### While you play

Open the **Family Tree** window on the Sim you are playing. There are three ways in:

- **Options > Family Tree** (the button after Packs),
- the **F8** key,
- `tree` in the cheat console (Ctrl+Shift+C).

It shows the Sim's family a generation to a row: grandparents, parents, aunts and uncles, who they
are married to, brothers and sisters, cousins, children, nieces and nephews, grandchildren. Each
relative is a button that moves the tree to them, and **Back to (name)** returns to your Sim.

**Change ties** lists the Sim's household, the family they already have and every Sim they have
met (not pets), with the same button as in Create a Family. Use it for families that were made
before you had the pack, and to put right anything the pack got wrong (below). Changing a tie here
does not change how well two Sims get on.

In the **relationship panel**, a Sim who is family has what they are to your Sim under their name:
Wife, Father, Cousin.

### What the tree follows by itself

**Babies.** When a baby arrives, the pack notes who its parents are: the Sims of the household whose
interaction brought it. With only one Sim involved, their husband or wife is the other parent, if
they live there too. When the baby grows into a child, the child is given those parents in the tree,
starts out getting on with the whole family as the table above says, and a message tells you. If
the parents are not known (an adopted baby, say), the message says so and the tree is left for you.

**Marriages.** When an adult joins the household, brought in by one of its Sims, the two may be
married. If the interaction was a proposal they are, and a message says so. Otherwise you are asked:
"Are they married?" Nobody is asked about a Sim who is already married, or about family.

What the pack saw and decided each time is written to the terminal, in lines starting `[family]`.

## Limits

- **Following play is a best guess.** The Sims 1 never says who a baby's parents are or that two
  Sims have married, so the pack works it out from who was doing what at the time. Where it is
  wrong, put it right with **Change ties**.
- A proposal is told from other ways of moving in by the interaction's English name. With the game
  in another language you are asked each time instead.
- A baby's parents are kept from when it arrives to when it grows up. A baby that was already there
  when you first switched the pack on has none noted.
- A Sim who moves out, or dies, keeps their place in the tree, and so does a marriage.
- **The game does not act on the tree.** The Sims 1's own scripts know nothing of it, so relatives
  are offered the same interactions as anyone else. Only the starting scores reach the game.
- **A married couple starts with high scores, and nothing else.** If the game keeps something more
  for a couple in love than its two scores, the pack does not set it.
- A child cannot be given a spouse or a child. Children in The Sims 1 never grow up by themselves;
  with the [Life Stages](../LifeStages) pack they do, and can be given both once they are adults.
- A Sim who has left the neighbourhood keeps their place in the file but is not shown.

## Where it is kept

The tree is kept for each neighbourhood, in `FamilyTree.txt` inside the neighbourhood's folder in
Simitone's user directory (`UserData`, `UserData2` and so on, beside `Neighborhood.iff`). It is plain
text, a tie to a line:

```
spouse 6A3B01F2 1C44E0D7
parent 6A3B01F2 53D0A1B9
sibling 53D0A1B9 2E9917C0
```

A Sim is the GUID of their character. The game's own files are not changed by the tree, so a
neighbourhood loads the same with the pack switched off, and the tree is still there when you switch
it on again. The starting scores are ordinary relationship scores in `Neighborhood.iff`, as if the
Sims had earned them.

## How it works

- `Kin.cs` is the tree and its rules. It knows nothing about the game: a Sim is a number.
- `People.cs` is what the game knows about a Sim (name, age, household, scores), and the tree's file.
- `FamilyTreePack.cs` is where the two meet, as Harmony patches: the button on the Create a Family
  screen, the moment the game makes the family, the relationship panel, and the ways into the window.
- `Following.cs` is how the tree follows play. The game's scripts make a baby object, turn it into a
  character days later, and add a character to a household. It watches for those three.
- `UIFamilyTiesDialog.cs` and `UIFamilyTreeDialog.cs` are the two windows, made from Simitone's own
  dialog, labels and buttons (`UIRowsDialog.cs`), so the pack has no artwork.

The baby is recognised by its scripts, not by name: it is the object whose scripts can make a new
Sim. So nothing here depends on which expansion packs are installed. Until it grows up it is an
object on its lot and not a Sim, so its parents are kept beside the tree, in a `baby` line of the
file, against the house and the object.

While a family is being made its Sims have no characters yet, so a Sim is their place in the family.
When the game makes the family, the ties move to the neighbourhood's tree under the characters it
made, and the scores are raised.

## Checking it

```bash
dotnet run --project FamilyTree/tests -c Release
```

This needs no copy of The Sims. It checks that every patch attaches to the Simitone the pack is
built against, the rules above, the file, and a family of four made with ties in a made-up
neighbourhood: the neighbourhood is saved with the game's own code and read back, to see the scores
are in it. For following play it checks how a baby is recognised, how its parents are chosen, that
it is kept until it grows up and the child then given them, and when a Sim who moves in is married
or asked about. What it cannot check is the game's own scripts doing those things.
