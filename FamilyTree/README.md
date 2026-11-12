# Family Tree

Family ties, from The Sims 2, for The Sims 1: who is married to whom, whose child is whose, and a
family tree for each Sim.

The Sims 1 keeps no family ties at all. It only knows how well two Sims get on, so a married couple
made in Create a Family begin as two strangers who share a house. With this pack you say what they
are to each other, and they begin that way.

**This is a first stage, and it has not been run in the game.** It has been compiled, its patches
checked against the engine, and its rules and saving checked without the game (below). Its windows
have not been seen on a screen. **Back up your `UserData` folders before trying it.**

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
met, with the same button as in Create a Family. Use it for families that were made before you had
the pack, and to keep the tree up to date (below). Changing a tie here does not change how well two
Sims get on.

In the **relationship panel**, a Sim who is family has what they are to your Sim under their name:
Wife, Father, Cousin.

## Limits

- **The tree does not follow what happens in play.** A proposal, a Sim moving in or a baby does not
  change it: add those ties yourself with **Change ties**. Following them is the next stage.
- **The game does not act on the tree.** The Sims 1's own scripts know nothing of it, so relatives
  are offered the same interactions as anyone else. Only the starting scores reach the game.
- **A married couple starts with high scores, and nothing else.** If the game keeps something more
  for a couple in love than its two scores, the pack does not set it.
- Children in The Sims 1 never grow up, so a child can never be given a spouse or a child.
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
- `UIFamilyTiesDialog.cs` and `UIFamilyTreeDialog.cs` are the two windows, made from Simitone's own
  dialog, labels and buttons (`UIRowsDialog.cs`), so the pack has no artwork.

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
are in it.
