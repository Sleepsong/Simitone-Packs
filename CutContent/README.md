# Cut Content

Restores features Maxis left unfinished or switched off in The Sims and its expansion packs. **Version
0.8**, written from four surveys of a real game. Hugging a tree and locking a door have been played
and work; the rest has not been played yet. Nothing in the game's files is
changed: the pack finds each feature in your own game while it runs, and tells you what it found.

No other mod was found that restores any of these. Simlogical's lockable doors are the nearest thing:
they are new objects with their own key system, not the code Maxis left in its own doors.

## What it does now

| Cut feature | Needs | State |
|---|---|---|
| The unused third "spooky" sting for when a ghost appears | Base game | Built. `sting_spooky3.wav` is in the game but in no sound list. One time in three that the game plays its spooky sting, the third one plays instead. |
| Hug a tree | Vacation | Built. The palm (`trees2.iff`) and the large fir, spruce and pine (`Trees4.iff`) have a finished "Tree Hug" script with hug, sigh and kiss loops and two need boosts, but no menu entry. The pack adds **Hug**. Adults only, as the script itself requires. |
| Lock and unlock doors | Vacation, Unleashed, Superstar, Makin' Magic | Built, and locking works. "Interaction Lock/unlock" sets the door's attribute 0, but nothing read it, so a locked door stopped no one. The pack adds the menu entry (the check names it **Lock** or **Unlock**) and finishes the lock: a locked door keeps out everyone but the household, its pets and the emergency services, so your own Sims still use it (`cutcontent locks everyone` keeps the household out too). Anyone can still use it to go outdoors, so nobody is shut in. The lock is saved with the lot. The first lock in a session says who it keeps out. Only doors from these four expansions have the lock: not the base game's, Livin' Large's or Hot Date's. |
| Sell a litter | Unleashed | Built. The Pet Maternity Box has a finished "Sell Litter" script with three offers ("This litter has potential. I'll give you 50 for them."), paying $25, $50 or $100, but no menu entry. The pack adds **Sell Litter** to the box, for adults of the household, when the box holds a litter. After a sale the litter goes, as when Animal Control takes it. (The Pet Trainer's own **Sell Litter** wants the box in the Sim's inventory, which the game never does, so it stays hidden.) |
| Children kiss toads | Makin' Magic | Built. The toad's **Kiss** is marked "no children"; the pack lifts the mark. The script already plays the child animations and gives Toad Sweat only to adults. |
| Hire a limousine in place of a cab | Hot Date | Built. The Go Downtown phone plugin has texts no script uses: "Would you prefer a taxi or a limo?", "Excellent. A representative from our prestigious limosine service will be there with a car shortly." and a "lack of funds" apology. Its "Generate Cab" only ever makes the taxi, and Hot Date's own limousine (`CarLimo.iff`) has the taxi's shape and pictures but none of its scripts. The pack asks the question after "Do you need a ride?"; a limousine costs $150 on top of the fare, or brings the apology and a taxi if the household can't pay. The car that comes is the taxi, which works, drawn with Hot Date's limousine pictures, and it stays a limousine if the lot is saved. |
| Investment offers by telephone | Base game | Built. The phone's "RandomCallDialog" has a branch no step reached: two chance calls in five would have offered to take a multiple of $50, with 20 placeholder texts ("Would you like to spend some money on something random? Variation 1" to 20). The pack leads the calls to the branch again, hands the amount to the text (Maxis never did), writes 20 offers in place of the placeholders, and finishes what Maxis never wrote: three to five days of play later an investment comes good (half as much again, two and a half times or four times) or goes bad, with a message. Pending investments are kept with the lot. |
| Information Kiosk missing from buy mode | Superstar | Not in this game. None of the 1,195 object files has "kiosk" or "info" in its name, no object is a kiosk, and the only catalog object without catalog text is a dead mouse. The copy of the game surveyed doesn't have it. |
| Cleaning skill | Base game | Built. The 1.0 base game's Behavior.iff lists ten skills in the order of person data 9 to 18: Cleaning, Cooking, Social, Repair, Gardening, Music, Creative, Literacy, Physical, Logic. Cleaning was kept, hidden: the global "clean inc", which the generic "Clean" and the showers call each time round, takes dirt off by ten times the Sim's cleaning skill, or ten times their Neat personality if the skill is 0. NPCs are given it (the maid and the janitors 1000); player Sims never learned it. Now household Sims learn it as they clean, about a point for every 50 times round, up to ten points. A Sim's first cleaning starts it at their Neat score, so learning never makes a neat Sim slower. The skills panel shows it beside the other six. |
| Gardening, music and literacy skills | Base game | Not in the game. 1.0's labels already call them "gardening ( old )", "music ( old )" and "literacy ( old )": they were dropped before release. Neither 1.0 nor the latest game has a step that teaches or uses them, a menu entry whose pull is scaled by them, or an object flagged as building them; only their names, NPCs' starting values and the outdoor flowers' "Look at". Hot Date later reused their numbers for interests. Bringing them back would be designing new skills. |
| Pickpocket | Vacation | Being looked into. Only his textures are in the game: a head with a five-o'clock shadow (`cvacationmalgt_pickpocket`, `cvacationmalgt_pick`), a skinny body (`vacationmasknlgt_pickpocket`) and beach, forest and lodge outfits (`vacationmafitlgt_pick...`). The burglar wears the skinny body `B003MASkn_01` and the head `C_Burglar`, and the pickpocket was to look like him. `cutcontent pickpocket` dresses the selected Sim in these, to see which meshes the textures fit before he is built. |

Objects and interactions are found by the names of their scripts and menu entries, in English.

**How entries are added.** When an object is created, the pack looks for a script it knows by name that
no menu entry leads to. If it finds one, the object gets a copy of its menu table with the entry added,
pointing at Maxis's own script. Sims never choose these on their own: the entries have no effect on needs.
Where Maxis left no check script, or a script needs finishing (Sell Litter), the pack adds or changes the
steps in memory and has the engine assemble them again.

**Switched off interactions.** The game can also leave an interaction in an object's table and keep it
off the menu: with a "debug only" mark, with an age mark, or with a check script that always says no. The
pack lifts the two marks. It leaves check scripts alone unless you type `cutcontent force on`, because a
check that says no can also be the game's own rule for when the interaction is allowed.

## Using it

Open the console (Ctrl+Shift+C):

| Command | What it does |
|---|---|
| `cutcontent` | Shows what has been found and restored so far. Interactions are found as you click on their objects. |
| `cutcontent survey` | Writes `cutcontent-survey.txt` to Simitone's user directory (next to `config.ini`). Takes a while: it reads every object in the game. |
| `cutcontent survey old [FOLDER]` | Reads an older copy of The Sims straight from its folder (the game doesn't load it, so the base game alone works), and writes `cutcontent-survey-old.txt`: every script step that reads or sets the hidden skill numbers, checked against the game being played (still there, changed, or gone), the steps of the old scripts that changed, and every text that mentions a skill. Folder names are matched without minding capitals; `~` is the home folder; a short name is looked for in the pack's folder; and with no folder at all it reads what is in the pack's folder. If a folder isn't found, it says which part and what is there instead. |
| `cutcontent pickpocket` | Lists the pickpocket's looks; `cutcontent pickpocket 1` to `8` dresses the selected Sim in one, `cutcontent pickpocket body MESH TEXTURE` (or `head`) tries any other, and `cutcontent pickpocket off` changes the Sim back. Only for looking: loading the lot again changes the Sim back too. |
| `cutcontent doors` | The doors on this lot that can be locked, and whether they are. |
| `cutcontent locks everyone` / `household` | Who a locked door keeps out. Lasts until the game closes. |
| `cutcontent force on` / `off` | Also shows matched interactions whose own check says no. Experimental, and lasts until the game closes. |

The terminal shows the same findings in lines starting with `[cut]`.

## The survey

`cutcontent survey` writes down what the pack has found and done, and the names and numbers in the
game's files that the restorations are written from. Version 8 lists the global "clean inc" routine,
which reads the cleaning skill, and every script that calls it, each Sim's hidden skill numbers, the menu
entries whose pull is scaled by a cut skill, and the objects' skill flags.
Run it in a lot, with a Sim selected, and with every expansion you have installed.

`cutcontent survey old [FOLDER]` reads an older copy's folder: the original 1.0 base game, or a build from
before the game came out. It lists what the copy's files are made of and anything the engine could not read
(a build from before release may keep menu tables or object definitions in older forms, and then "nothing
found" may only mean "not readable"); Behavior.iff's tables of names whole; career levels that ask for the
three unnamed skills ("min req 7, 8, 9"); script names that mention a skill, learning, studying or
practising; menu entries whose pull is scaled by a cut skill; objects' skill flags; every text that
mentions a skill; and every script step that reads or sets the hidden skill numbers, checked against the
game being played. It reads `.far` archives and `.iff` files anywhere under the folder, skipping saved lots,
families and neighbourhoods, and changes nothing. Files too old for the engine to open (a build from 1998
begins its files with something other than "IFF FILE 2.5") are described instead: the text they begin
with, a dump of their first bytes, and where the game's data tags are found in them, to work out how to
read them.

## How it is made

- `CutContentPack.cs` loads the pack, applies two Harmony patches and adds the console command.
- `AddedInteractions.cs` runs after an object is created, and gives it a copy of its menu table with the
  missing entries added. `Scripts.cs` adds and changes script steps in memory, and `Litter.cs` uses it to
  finish Sell Litter.
- `DoorLocks.cs` runs before a Sim plans a route from room to room, and leaves out the doors locked
  against them.
- `Interactions.cs` runs before the engine looks an interaction up in an object's table, and lifts the
  marks on the cut ones the first time each table is seen.
- `GhostSting.cs` runs before each spooky sting, and one time in three plays the third one instead.
- `Limousine.cs` adds the question to the Go Downtown plugin when it is created, marks the car with a
  primitive of its own, and draws a marked taxi with the limousine's pictures.
- `OldSurvey.cs` reads an older copy of the game from its folder with the engine's own file readers.
- `Cleaning.cs` adds a step to the global "clean inc" that teaches household Sims cleaning, and a bar to the
  skills panel.
- `Investments.cs` finishes the phone's chance calls when a phone is created, records investments with a
  primitive of its own, and counts them down as the lot's clock runs.
- `Kiosk.cs` adds the catalog entry once the game's content is loaded.
- `Survey.cs` writes the report.

Every change to the game is in memory, and is made again each time the game starts. A door's lock is the
door's own attribute, which the game saves with the lot. Pending investments are saved in the pack's own
file for the lot, beside the lot's save.
