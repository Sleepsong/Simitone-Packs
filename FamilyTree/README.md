# Family Tree

The Sims 2's family tree, for The Sims 1: say who is married to whom and whose child is whose when
you make a family, and see each Sim's tree as you play.

The Sims 1 keeps no family ties at all. It only knows how well two Sims get on, so a married couple
made in Create a Family begin as two strangers who share a house. With this pack you say what they
are to each other, and they begin that way.

**It has not been run in the game.** It has been compiled, its patches checked against the engine,
and its rules, its saving and the layout of the tree checked without the game (below). Its windows
have not been seen on a screen, and how it follows babies and marriages rests on a reading of the engine that only play can
confirm. **Back up your `UserData` folders before trying it.**

## Using it

### Setting the family up, in Create a Family

The tree is put together in **Create a Family**, as in The Sims 2, and nowhere else.

Once the family you are making has two Sims, a **Relationships** button appears at the top right. It
opens the family's portraits in a row:

1. Choose a Sim.
2. Choose the Sim they are related to.
3. Say what the first is to the second: **Spouse**, **Sibling**, **Parent**, **Child**, or
   **Roommate** (no tie, which is what every pair starts as).

Once a Sim is chosen, each of the other portraits says what that Sim is to them: Wife, Son, Brother.
That includes what follows from the rest. Give two children the same parent and they are brother
and sister without being told.

A choice the rules do not allow is refused, and the window says why. The rules:

- Only adults marry or are parents, and a Sim has one spouse and at most two parents.
- Family by birth cannot marry: parents, children, siblings, grandparents, aunts and uncles, cousins.
- Nobody is their own ancestor, and a parent is not also a sibling.

If you take a Sim out of the family, their ties go with them.

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

**A household that already exists.** The same button appears on Create a Family's list of
households, for the one you have chosen there. Those are the households waiting for a house, so a
family you made before you had the pack can be given its ties before it moves in. Its Sims are
started off as family when you close the window. A household that already has a house is not on
that list, and cannot be changed.

### Looking at the tree, while you play

Open the **Family Tree** window on the Sim you are playing. There are three ways in:

- **Options > Family Tree** (the button after Packs),
- the **F8** key,
- `tree` in the cheat console (Ctrl+Shift+C).

It is drawn as The Sims 2 draws it: each Sim's portrait, a generation to a row, with lines down from
each couple to their children. The Sim it is drawn around is outlined. Above them are their parents
and grandparents, beside them their brothers and sisters (left) and their husband or wife (right),
and below them their children and grandchildren.

Choose any portrait to draw the tree around that Sim. A Sim with **...** after their name has family
that is not in the picture: choose them to see it. That is how you reach an aunt, a cousin or a
brother's wife.

Nothing can be changed here. The tree is only looked at in play.

In the **relationship panel**, a Sim who is family has what they are to your Sim under their name:
Wife, Father, Cousin.

### What changes the tree after that

Only what happens to the family, as in The Sims 2.

**Babies.** When a baby arrives, the pack notes who its parents are: the Sims of the household whose
interaction brought it. With only one Sim involved, their husband or wife is the other parent, if
they live there too. When the baby grows into a child, the child is given those parents in the tree,
starts out getting on with the whole family as the table above says, and a message tells you. If
the parents are not known (an adopted baby, say), the child has none in the tree.

**Marriages.** When a Sim of the household brings an adult in to live there by proposing, the two
are married in the tree, and a message says so. A Sim who moves in any other way is a roommate.

What the pack saw and decided each time is written to the terminal, in lines starting `[family]`.

## Limits

- **Following play is a best guess, and cannot be corrected.** The Sims 1 never says who a baby's
  parents are or that two Sims have married, so the pack works it out from who was doing what at
  the time. If it is wrong, the only way to put it right is the tree's file (below), with the game
  closed.
- **A proposal is known by its English name.** With the game in another language, no Sim is married
  by moving in.
- **The game does not act on the tree.** The Sims 1's own scripts know nothing of it, so relatives
  are offered the same interactions as anyone else. Only the starting scores reach the game.
- **A married couple starts with high scores, and nothing else.** If the game keeps something more
  for a couple in love than its two scores, the pack does not set it.
- A household that already lives in a house cannot be given ties.
- A child cannot be given a spouse or a child. Children in The Sims 1 never grow up by themselves;
  with the [Life Stages](../LifeStages) pack they do.
- A baby's parents are kept from when it arrives to when it grows up. A baby that was already there
  when you first switched the pack on has none noted.
- A Sim who moves out, or dies, keeps their place in the tree, and so does a marriage.
- A Sim who has left the neighbourhood keeps their place in the file but is not drawn.
- The picture shows two generations up and down from the Sim in the middle. Further relatives are
  reached through the portraits in between.

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
- `TreeLayout.cs` is where everything goes in the picture of a tree. It is only numbers, so it is
  checked without the game.
- `UITreeView.cs` puts that picture on the screen, with Simitone's own portrait button and lines
  drawn from a stretched square, so the pack has no artwork.
- `UIFamilyTreeDialog.cs` is the window in play, and `UIFamilyTiesDialog.cs` the one in Create a
  Family.

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
are in it. It checks the picture of the tree (who is in which row, that parents are over
their children, that nothing overlaps or leaves the picture), the steps of the window in Create a
Family, and that nothing in the window you see in play can change the tree. For following play it
checks how a baby is recognised, how its parents are chosen, that it is kept until it grows up and
the child then given them, and when a Sim who moves in is married. What it cannot check is the game's own scripts doing those things.
