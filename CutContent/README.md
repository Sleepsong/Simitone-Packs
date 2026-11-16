# Cut Content

Restores features Maxis left unfinished or switched off in The Sims and its expansion packs. **Version
0.6**, written from four surveys of a real game. Hugging a tree and locking a door have been played
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
| Cut skills: cleaning, gardening, music, literacy | Base game | Not started. |
| Pickpocket | Vacation | Not started. His textures are in the game (`cvacationmalgt_pickpocket.bmp`, `vacationmasknlgt_pickpocket.bmp` and beach, forest and lodge outfits), but no character uses them. |

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
| `cutcontent doors` | The doors on this lot that can be locked, and whether they are. |
| `cutcontent locks everyone` / `household` | Who a locked door keeps out. Lasts until the game closes. |
| `cutcontent force on` / `off` | Also shows matched interactions whose own check says no. Experimental, and lasts until the game closes. |

The terminal shows the same findings in lines starting with `[cut]`.

## The survey

`cutcontent survey` writes down what the pack has found and done, and the names and numbers in the
game's files that the restorations are written from: each object's menu table and how its entries are
marked, the scripts no entry leads to, the left-over texts, the sound lists and the skins. Run it in a
lot, with a Sim selected, and with every expansion you have installed.

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
- `Investments.cs` finishes the phone's chance calls when a phone is created, records investments with a
  primitive of its own, and counts them down as the lot's clock runs.
- `Kiosk.cs` adds the catalog entry once the game's content is loaded.
- `Survey.cs` writes the report.

Every change to the game is in memory, and is made again each time the game starts. A door's lock is the
door's own attribute, which the game saves with the lot. Pending investments are saved in the pack's own
file for the lot, beside the lot's save.
