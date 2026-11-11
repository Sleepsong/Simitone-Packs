# Cut Content

Restores features Maxis left unfinished or switched off in The Sims and its expansion packs. **This is a
first stage.** It was written without the game's files to hand, and it has been compiled but not yet
run against a game. Nothing in the game's files is changed: the pack looks for each feature in your
own game while it runs, and tells you what it found.

No other mod was found that restores any of these. Simlogical's lockable doors are the nearest thing:
they are new objects with their own key system, not the interactions Maxis left in its own doors.

## What it does now

| Cut feature | Needs | State |
|---|---|---|
| The unused third "spooky" sting for when a ghost appears | Base game | Built. One time in three that the game plays a spooky sting, the unused one plays in its place. |
| Information Kiosk missing from buy mode | Superstar | Built. Adds a catalog entry, filling in only what the object lacks: Miscellaneous, every room and lot type, 500 Simoleons. |
| Hug a pine or palm tree | Vacation | Built for the case where the interaction is still in the tree's menu table (below). |
| Children kiss toads | Makin' Magic | The same. A "no children" mark is lifted as well. |
| Lock and unlock doors | Superstar, Makin' Magic | The same. |
| Sell a litter to the Pet Trainer | Unleashed | The same. |
| Hire a limousine in place of a cab | Hot Date | The same. |
| Investment offers by telephone | Base game | The same, though this was a random call, not a menu choice, so it is unlikely to be enough. |
| Cut skills: cleaning, gardening, music, literacy | Base game | Not started. The survey shows a Sim's numbers for them. |
| Pickpocket | Vacation | Not started. Only his textures are left, so he would be a new character. The survey lists them. |

**Switched off interactions.** The game can leave an interaction in an object's table and still keep
it off the menu: with a "debug only" mark, with an age mark, or with a check script that always says
no. The pack lifts the two marks. It leaves check scripts alone unless you type `cutcontent force on`,
because a check that says no can also be the game's own rule for when the interaction is allowed. An
interaction that was taken out of the table altogether is not brought back yet.

Objects and interactions are found by their English names, so the game must be in English.

## Using it

Open the console (Ctrl+Shift+C):

| Command | What it does |
|---|---|
| `cutcontent` | Shows what has been found and restored so far. Interactions are found as you click on their objects. |
| `cutcontent survey` | Writes `cutcontent-survey.txt` to Simitone's user directory (next to `config.ini`). Takes a while: it reads every object in the game. |
| `cutcontent force on` / `off` | Also shows matched interactions whose own check says no. Experimental, and lasts until the game closes. |

The terminal shows the same findings in lines starting with `[cut]`.

## The survey

The restorations that are not done need names and numbers from the game's files: which entries are in
each object's table and how they are marked, which scripts no entry leads to, the left-over texts, the
sound events and the skins. `cutcontent survey` writes all of that down for the cut features. Run it in
a lot, with a Sim selected, and with every expansion you have installed. The next stage of this pack
is written from that file.

## How it is made

- `CutContentPack.cs` loads the pack, applies two Harmony patches and adds the console command.
- `Interactions.cs` runs before the engine looks an interaction up in an object's table, and lifts the
  marks on the cut ones the first time each table is seen.
- `GhostSting.cs` runs before the engine plays a sound event.
- `Kiosk.cs` adds the catalog entry once the game's content is loaded.
- `Survey.cs` writes the report.

Nothing is saved: every change is in memory, and is made again each time the game starts.
