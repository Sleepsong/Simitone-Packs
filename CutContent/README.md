# Cut Content

Restores features Maxis left unfinished or switched off in The Sims and its expansion packs. **Version
0.2**, written from the first survey of a real game, but still not played. Nothing in the game's files is
changed: the pack finds each feature in your own game while it runs, and tells you what it found.

No other mod was found that restores any of these. Simlogical's lockable doors are the nearest thing:
they are new objects with their own key system, not the code Maxis left in its own doors.

## What it does now

| Cut feature | Needs | State |
|---|---|---|
| The unused third "spooky" sting for when a ghost appears | Base game | Built. The game's `sting_spooky` sound picks from a list holding `sting_spooky1` and `sting_spooky2`. The pack adds `sting_spooky3.wav` to that list, so all three take turns. |
| Hug a tree | Vacation | Built. The palm (`trees2.iff`) and the large fir, spruce and pine (`Trees4.iff`) have a finished "Tree Hug" script and treehugger animations, but no menu entry. The pack adds **Hug** to their menus. Adults only, as only adult animations exist. |
| Lock and unlock doors | Superstar, Makin' Magic | Built, to be tested. Several door files have "Interaction Lock/unlock" and its check, with a combination lock ("Enter the combination from 0 to 9999"), but no menu entry. The pack adds it; the check names it **Lock** or **Unlock**. Whether a locked door stops anyone depends on Maxis's unfinished code, which the next survey lists. |
| Children kiss toads | Makin' Magic | Built. The toad's **Kiss** is marked "no children"; the pack lifts the mark. |
| Sell a litter to the Pet Trainer | Unleashed | Found, not yet working. **Sell Litter** is in the Pet Trainer's menu with no hiding mark, so its check script must be refusing. The next survey lists that script's steps. |
| Information Kiosk missing from buy mode | Superstar | Not found yet. No object has "kiosk" in its name. The next survey lists every object missing from the catalog. |
| Hire a limousine in place of a cab | Hot Date | Not found yet. The phones keep their calls in a shared script file, which the next survey reads. |
| Investment offers by telephone | Base game | Not found yet, as for the limousine. |
| Cut skills: cleaning, gardening, music, literacy | Base game | Not started. |
| Pickpocket | Vacation | Not started. His textures are in the game (`cvacationmalgt_pickpocket.bmp`, `vacationmasknlgt_pickpocket.bmp` and beach, forest and lodge outfits), but no character uses them. |

Objects and interactions are found by the names of their scripts and menu entries, in English.

**How entries are added.** When an object is created, the pack looks for a script it knows by name that
no menu entry leads to. If it finds one, the object gets a copy of its menu table with the entry added,
pointing at Maxis's own script. Sims never choose these on their own: the entries have no effect on needs.

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
| `cutcontent force on` / `off` | Also shows matched interactions whose own check says no. Experimental, and lasts until the game closes. |

The terminal shows the same findings in lines starting with `[cut]`.

## The survey

The restorations that are not done need names and numbers from the game's files. `cutcontent survey`
writes them down: each object's menu table and how its entries are marked, the scripts no entry leads to,
the left-over texts, the sound lists and the skins. Version 2 of the survey also lists the steps of the
scripts that matter (lock and unlock, Sell Litter, Tree Hug, Kiss Toad, the doors' passing checks, the
phones' cab and stock calls), reads the shared script files that phones and doors keep most of their code
in, and lists every object that buy mode doesn't show. Run it in a lot, with a Sim selected, and with every
expansion you have installed.

## How it is made

- `CutContentPack.cs` loads the pack, applies two Harmony patches and adds the console command.
- `AddedInteractions.cs` runs after an object is created, and gives it a copy of its menu table with the
  missing entries added.
- `Interactions.cs` runs before the engine looks an interaction up in an object's table, and lifts the
  marks on the cut ones the first time each table is seen.
- `GhostSting.cs` adds the third sting to the sound list once the game's content is loaded, and runs
  before each spooky sting so the game picks again.
- `Kiosk.cs` adds the catalog entry once the game's content is loaded.
- `Survey.cs` writes the report.

Nothing is saved: every change is in memory, and is made again each time the game starts.
