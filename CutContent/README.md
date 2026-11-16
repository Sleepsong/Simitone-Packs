# Cut Content

Restores features Maxis left unfinished or switched off in The Sims and its expansion packs. **Version
0.4**, written from three surveys of a real game. Hugging a tree has been played and works; the rest
has not been played yet. Nothing in the game's files is
changed: the pack finds each feature in your own game while it runs, and tells you what it found.

No other mod was found that restores any of these. Simlogical's lockable doors are the nearest thing:
they are new objects with their own key system, not the code Maxis left in its own doors.

## What it does now

| Cut feature | Needs | State |
|---|---|---|
| The unused third "spooky" sting for when a ghost appears | Base game | Built. `sting_spooky3.wav` is in the game but in no sound list. One time in three that the game plays its spooky sting, the third one plays instead. |
| Hug a tree | Vacation | Built. The palm (`trees2.iff`) and the large fir, spruce and pine (`Trees4.iff`) have a finished "Tree Hug" script with hug, sigh and kiss loops and two need boosts, but no menu entry. The pack adds **Hug**. Adults only, as the script itself requires. |
| Lock and unlock doors | Vacation, Unleashed, Superstar, Makin' Magic | Built, not working yet in the first test. "Interaction Lock/unlock" sets the door's attribute 0, but nothing read it, so a locked door stopped no one. The pack adds the menu entry (the check names it **Lock** or **Unlock**) and finishes the lock: a locked door keeps out everyone but the household, its pets and the emergency services (or the household too, with `cutcontent locks everyone`). Anyone can still use it to go outdoors, so nobody is shut in. The lock is saved with the lot. Maxis's check first calls a Hot Date routine for the old prude, which may hide the entry; 0.4 leaves that call out. Only doors from these four expansions have the lock. |
| Sell a litter | Unleashed | Built. The Pet Maternity Box has a finished "Sell Litter" script with three offers ("This litter has potential. I'll give you 50 for them."), paying $25, $50 or $100, but no menu entry. The pack adds **Sell Litter** to the box, for adults of the household, when the box holds a litter. After a sale the litter goes, as when Animal Control takes it. (The Pet Trainer's own **Sell Litter** wants the box in the Sim's inventory, which the game never does, so it stays hidden.) |
| Children kiss toads | Makin' Magic | Built. The toad's **Kiss** is marked "no children"; the pack lifts the mark. The script already plays the child animations and gives Toad Sweat only to adults. |
| Hire a limousine in place of a cab | Hot Date | Found, not built. The Go Downtown phone plugin has the unused texts "Would you prefer a taxi or a limo?", the limousine service's reply and a chauffeur's three "where to?" lines, but no script uses them, and its "Generate Cab" only ever makes the taxi. Hot Date's own limousine (`CarLimo.iff`) has none of the taxi's scripts. Superstar's limousines have the taxi's "Get In Cab"; survey 4 lists one in full to see whether it can go downtown. |
| Investment offers by telephone | Base game | Found, not built. The phone's "RandomCallDialog" has a branch that no step reaches: two calls in five would have offered to spend a multiple of $50, with the 20 placeholder texts "Would you like to spend some money on something random? Variation 1" to 20. The phone's texts also have a "Salesman:" caller and "Sucker! You just bought some worthless swamp land". Survey 4 lists the scripts that show these calls, to finish the texts and the payoff. |
| Information Kiosk missing from buy mode | Superstar | Not found. No object file has "kiosk" or "info" in its name, and no object is a kiosk. The Sims needs an object's catalog text to show it; Simitone doesn't, so the kiosk may already be in Simitone's catalog with no name. Survey 4 lists those. |
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

The restorations that are not done need names and numbers from the game's files. `cutcontent survey`
writes them down. Version 4 lists in full Superstar's black limousine and Vacation's doors, the steps of
the phones' chance calls, the doors' shared scripts and the cars' cab scripts, the global routine the
lock's check calls, and the objects in Simitone's catalog that have no catalog text. Run it in a lot, with
a Sim selected, and with every expansion you have installed.

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
- `Kiosk.cs` adds the catalog entry once the game's content is loaded.
- `Survey.cs` writes the report.

Nothing of the pack's own is saved: every change is in memory, and is made again each time the game
starts. A door's lock is the door's own attribute, which the game saves with the lot.
