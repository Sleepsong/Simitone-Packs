# Wants, Fears and Aspirations

The Sims 2's aspirations, for The Sims 1: each Sim has an aspiration, four wants and three fears rolled
for it, and an aspiration meter that runs from Aspiration Failure to Platinum. Fulfilled wants earn
points, and the points buy rewards.

**This is a first stage, and it has not been run in the game.** It builds against Simitone, its
patches fit the engine, and its rules are tested (below), but nobody has played a household with it
yet. Back up your Simitone user directory before you switch it on. Expect a lot of balancing: every
number is in `balance.json`, so it can be tuned without building the pack again.

## What it does

### Aspirations

| Aspiration | Wants mostly |
|---|---|
| Romance | Kisses, flirting, falling in love, skill in Charisma and Body |
| Family | A baby, a partner moving in, time with the family, cooking, children's grades |
| Fortune | Promotions, money earned and held, skills for the next promotion, buying things |
| Popularity | New friends, more friends, best friends, parties, jokes and compliments |
| Knowledge | Skill points in any skill, skills at the top, chess and the telescope |
| Pleasure | A great mood, fun, dancing, games, parties, going out |
| Grow Up | Children only: good grades, friends, skills, play and study |

Children have Grow Up until they grow up; then they are asked to choose. A grown Sim the pack meets for
the first time is given the aspiration that suits them best by chance (from their personality, skills,
job and children; `fit` in `balance.json`), and the player is asked to choose, once. The Aspiration
window changes it at any time: a new aspiration starts the wants and fears over, and keeps the points
and the meter.

### Wants and fears

A Sim has four wants and three fears at a time, rolled from those of their aspiration by weight.
Each is measured from how things were when it was rolled: "Get promoted" means a level above the one
they had then, "Earn §2,300" means that much more than the household had then.

- **A want fulfilled** pays its points, both as reward points and on the meter, and another is rolled.
- **A fear that comes true** lowers the meter by its points. It takes no reward points.
- **When the Sim wakes up**, their wants and fears are rolled again, as in The Sims 2, except a want
  that is **locked**. One want can be locked (in the window, or `aspire lock 1`).
- A goal that stops making sense (a want to be promoted, by a Sim who changes careers) is rolled again.

### The meter

The meter runs from -100 to 100, in five levels:

| Level | From | Meter falls an hour | Needs fall | A reward goes wrong |
|---|---|---|---|---|
| Aspiration Failure | -100 | (holds, but for mood) | 15% faster | 60% of the time |
| Low | -70 | 0.3 | 5% faster | 30% |
| Steady | -20 | 0.6 | as usual | 5% |
| Gold | 50 | 1.2 | 10% slower | never |
| Platinum | 85 | 2.4 | 25% slower | never |

A Sim's mood moves it too: up to 0.8 an hour in the best of moods, as far down in the worst. A want
worth 1,500 points lifts it 15. So in an even mood a Sim holds Gold with about 2,900 points of wants a
day (two average wants) and Platinum with about 5,800 (four); in a good mood, less.

**Aspiration failure.** A Sim who falls to the bottom has a breakdown: Fun and Social drop by 40 and
Comfort by 20, and a message says so. Rewards they use are likely to go wrong. Fulfilling wants lifts
them out; left alone for a day, they pull themselves together, back to Low. The Sims 2 sends a
therapist; The Sims has none.

### Lifetime want

Each grown Sim has one lifetime want for their aspiration: the top of a career, §100,000, four skills
at the top, fifteen friends, kissing ten different Sims, raising three children, or dancing with twelve
different Sims. Fulfilling it pays 20,000 points, and from then on the meter never falls below Gold.

### Rewards

The Sims 2's rewards are objects with animations of their own, which The Sims does not have. Here
each reward is an effect, bought with reward points and used at once. Some use the game's own
objects (the voucher pays for one you choose in buy mode).

| Reward | Points | What it does | If it goes wrong |
|---|---|---|---|
| Wake-Up Tonic | 4,000 | Energy full, at once | Energy drops by 60 |
| Hearty Charm | 6,000 | Hunger and Bladder kept full for a day | Hunger drops by 50 |
| Serenity | 18,000 | Every need kept full for eight hours | Every need drops by 30 |
| Quick Study | 8,000 | Skills learned twice as fast for a day | Energy and Fun drop by 40 |
| Tutor's Notes | 6,000 | A skill point in the skill you choose | Fun drops by 40, nothing is learned |
| Windfall | 6,000 | §2,000 for the household | Nothing happens |
| Dividend | 9,000 | §400 each morning at nine for a week | Nothing happens |
| Glowing Review | 7,000 | Job performance (or a child's grades) at the top | Performance drops by 40 |
| Charm Bracelet | 6,000 | Everyone else on the lot gets on better with the Sim (+15 daily, +5 lifetime) | Everyone gets on worse (-15) |
| Shopping Voucher | 9,000 | The next object bought, up to §3,000, costs nothing | Cannot go wrong |
| Second Wind | 5,000 | Lifts the meter to Steady, if it is lower | Cannot go wrong |

Each reward has a cooldown before the same Sim can use it again (12 hours for the tonic, a week for the
dividend), and a lasting one cannot be used twice at once. The part of an object the voucher paid for
cannot be sold back.

## Seeing it

- **Beside the needs.** In live mode the needs panel has the selected Sim's meter at its right, as The
  Sims 2 has it: a bar of the levels, the level, the aspiration and the points. Click it for the window.
  On screens too narrow for it (narrower than about 1280 on a desktop), it is left out.
- **The Aspiration window**: F7, **Options > Aspiration**, or `aspire window`. The meter and what each
  level does, the wants (with the lock) and fears, the lifetime want, rewards in effect, and the
  Rewards button. **Next Sim** goes through the household.
- **News.** A want fulfilled, a fear come true and a new level are shown as a line at the top of the
  screen that fades. A breakdown and a lifetime want fulfilled are messages with OK.

## How it sees what happens

The Sims has no events for "a skill point was gained" or "a friend was made", so a few times a second
the pack looks at each Sim of the household being played and compares them with the last look.

| What | How |
|---|---|
| Skills, job, level, performance, grades | The Sim's own numbers |
| Money | The household's funds |
| Friends | Both Sims' daily relationship 50 or more, the game's own rule |
| Best friends | A friend with a lifetime relationship of 70 or more (or a daily one of 90 where the game keeps no lifetime one) |
| Love | A bit in the relationship's flags (see Limits) |
| An interaction, and how it went | The Sim's action queue: when one ends, whether the relationship with the other Sim went up (taken well) or down (turned down) |
| Something done to the Sim | The others' action queues: an interaction whose target is the Sim |
| An object bought | An object new on the home lot that has a price |
| A baby | An object new on the lot whose scripts can make a Sim, as the Family Tree pack finds them |
| Someone leaving the household | The household's list of members |
| Guests, a fire, a burglar | The lot: people not of the household, anything burning, and names |

## Balancing

Everything is in `balance.json` in the pack's folder: the slots, the meter, the levels, the
relationship rules, the aspirations, every want, fear and lifetime want, and every reward. Edit it, then
type `aspire reload` in the console to use it at once. If it cannot be read, `Aspirations.log` says why
and the pack uses the copy built into it. `aspire stats` shows, for each Sim, the days played, wants
fulfilled and fears come true, and points earned and lost a day, which is what balancing needs.

A want, fear or lifetime want is:

```json
{ "id": "earn", "kind": "Earn", "amount": 1000, "fraction": 0.1, "text": "Earn {amount}",
  "points": 1500, "ages": "adult", "weights": { "Fortune": 8, "Family": 1, "Pleasure": 1 } }
```

`weights` say how likely it is to be rolled for each aspiration (`"Any"` for those not named), `points`
what it is worth, and `perLevel` points more for each level the Sim has in the skill or career it is
about. `ages` is `adult`, `child` or `any`. In `text`, `{skill}`, `{amount}`, `{count}`, `{total}`,
`{threshold}` and `{career}` are filled in. The kinds:

| Kind | Means | Uses |
|---|---|---|
| `SkillUp` | A skill point | `skill` (a skill, or `any`: one chosen when rolled) |
| `CareerSkill` | A point in a skill the next promotion asks for | |
| `SkillMax` | A skill at the top | `threshold`: from which level (8) |
| `SkillsMaxed` | So many skills at the top | `total` |
| `GetJob`, `Promotion`, `Demoted`, `Fired` | Work | |
| `TopOfCareer` | The top of the Sim's career | `count`: within how many levels of it (2) |
| `CareerTop` | The top of any career | |
| `Performance`, `LowPerformance` | Job performance (a child's grades for `LowPerformance` with `ages: child`) up to, or down to, `threshold` | `threshold` |
| `Grades` | A child's grades up to `threshold` | `threshold` |
| `ChildGrades`, `ChildBadGrades` | A child of the household's grades up to, or down to, `threshold` | `threshold` |
| `Earn` | So much more money: `amount` and `fraction` of what the household has | `amount`, `fraction` |
| `HaveFunds` | The household's funds at a milestone above what they have, or at `total` | `milestones` or `total` |
| `Buy` | An object bought, worth `amount` or more | `amount` |
| `Broke` | Funds below `amount` | `amount` |
| `NewFriend`, `BestFriend`, `FallInLove`, `LoseFriend` | Someone new, or someone lost | `total` for `FallInLove`: so many loves |
| `Friends` | `count` friends more, or `total` | `count` or `total` |
| `NewHousemate` | Another adult in the household (a marriage, or moving in) | |
| `Baby` | A baby arrives | |
| `Children` | So many children in the household | `total` |
| `LoseMember` | Someone leaves the household (death, or the social worker) | |
| `Interaction` | An interaction whose name has one of `match`'s words | `match`, `with` (`person`, `household`, `other`, `object`, `any`), `outcome` (`accepted`, `rejected`, `any`), `total`: so many different people |
| `Received` | Someone else's interaction with the Sim | `match` |
| `Party` | `count` guests on the home lot at once | `count` |
| `Outing` | Away from home | |
| `NeedHigh`, `NeedLow` | A need (or `Mood`) up to, or down to, `threshold` | `need`, `threshold` |
| `Fire` | A fire on the lot | |
| `Visitor` | Someone on the lot whose name has one of `match`'s words | `match` |

A reward is an `effect`, and a `backfire` for when it goes wrong (none: nothing happens):

| Effect | Does | Uses |
|---|---|---|
| `Needs` | Fill needs to `set`, or change them by `add` | `needs` (names, or `All`) |
| `Freeze` | Fill needs to `set` and keep them there for `hours` | `needs`, `set`, `hours` |
| `SkillGain` | Skills learned `times` as fast for `hours` | `times`, `hours` |
| `SkillPoint` | `points` skill points in a skill the player chooses | `points` |
| `Money` | `amount` for the household now | `amount` |
| `Dividend` | `amount` each morning at nine for `days` | `amount`, `days` |
| `Performance` | Job performance (or grades) to `set`, or changed by `add` | `set` or `add` |
| `Charm` | Everyone else on the lot: relationships changed by `daily` and `lifetime` | `daily`, `lifetime` |
| `Voucher` | The next object bought, up to `amount`, free | `amount` |
| `Meter` | The meter lifted to `set` | `set` |
| `Nothing` | Nothing | |

A reward also has a `cost`, a `cooldown` in Sim hours, `ages`, and `safe` (it never goes wrong).

The tests print a report on balance (`--report`): what each aspiration rolls most, how many points of
wants a day hold each level, and how rewards compare with wants. With the numbers as they are:

| | |
|---|---|
| Average want | 1,446 points |
| Average fear | 1,640 points |
| To hold Gold a day, in an even mood | 2,880 points (two wants) |
| To hold Platinum a day, in an even mood | 5,760 points (four wants) |
| The cheapest reward | 2.8 wants |
| The dearest (Serenity) | 12.5 wants |

## The console

Open the cheat console (Ctrl+Shift+C):

| Type | To |
|---|---|
| `aspire` | See everyone in your household: aspiration, level, meter and points |
| `aspire wants` | A Sim's wants, fears, lifetime want and rewards in effect |
| `aspire window` | Open the Aspiration window |
| `aspire choose Fortune` | Give a Sim an aspiration |
| `aspire lock 1` | Lock or unlock a want |
| `aspire rewards` | The rewards, their ids, and whether a Sim can use them |
| `aspire use tonic` | Use a reward. For a skill point, name the skill: `aspire use tutor Logic` |
| `aspire stats` | Points earned and lost a day, for balancing |
| `aspire reload` | Read `balance.json` again |
| `aspire on` / `aspire off` | Start and stop it all. Nothing is forgotten while it is off |
| `aspire ask off` | Let chance choose aspirations, without asking |
| `aspire notices off` | No lines at the top of the screen |
| `aspire points 5000`, `aspire meter 90`, `aspire reroll` | Cheats, for trying things out |
| `aspire help` | See this list in the game |

A command is for the selected Sim; to name one, put the name last: `aspire wants Bella`.

## Limits

These are what is not yet known from the game, and what The Sims cannot do:

- **Interactions are matched by name, in the English game.** A want to kiss someone looks for "kiss"
  in the interaction's name, and an interaction with an object for the object's name too. In another
  language those wants and fears are never fulfilled; edit their `match` words.
- **Love is a guess.** Which bit of a relationship marks love is not yet checked against the game
  (`loveIndex` and `loveMask` in `balance.json`). `Aspirations.log` writes down each value it sees
  there, with the two Sims, so it can be put right. The kiss and flirt wants do not depend on it.
- **Grades.** A child's grades are taken to be their performance, -100 to 100, with good grades from
  60. Not yet checked against the report card.
- **Guests** are people on the lot of another household; the game's workers are taken to have none.
  **A burglar** or the repo man is recognised by name.
- **Rewards are effects, not objects.** There is no money tree to water or tub to bathe in. The
  Elixir of Life and the like are left out: they would need Life Stages, and a way for one pack to
  call another.
- **The game's scripts know nothing of aspirations.** Sims do not act on their wants by themselves,
  and a Sim in failure does not refuse to do things; their needs fall faster instead.
- **Only the household you play** has aspirations, as in The Sims 2.

## Your saves

The pack keeps its own file for each neighbourhood, beside Simitone's saves:
`LocalHouse/PackData/sample.aspirations/` in the user directory. It is written when the game saves, so
if you leave a lot without saving, what happened there is forgotten with it. The settings (`aspire on`,
`ask`, `notices`) are written at once.

It changes the game's own data only as rewards say: needs, skills, job performance, the household's
money and relationships, the same numbers the game's objects change. Switching the pack off leaves
those as they are, and the pack's file as it was.

## How it works

The engine is not changed. The pack patches it with Harmony in three places it needs:

1. **Each tick of a lot**: a few times a second, look at the household (`Watcher.cs`), and keep needs
   full where a reward does.
2. **When the game loads a neighbourhood**, it forgets the Sims of the one before.
3. **When the game saves**, it writes its own file.

and some it can do without, so a version of Simitone they do not fit costs only what they do:

4. **When a lot is left**, it reads its file again, so what was not saved is dropped.
5. **When the game lets needs fall** (`VMTS1MotiveDecay.Tick`), each one falls faster or slower by the
   Sim's level. Only what the game's own decay takes is changed, not food eaten or sleep.
6. **When the live panel changes what it shows**, it puts the meter in the needs panel.
7. **The F7 key**, and **a button in Options**, after the Apartments and Family Tree packs' buttons if
   those are switched on.

| File | Holds |
|---|---|
| `balance.json` | Every number: aspirations, levels, wants, fears, lifetime wants, rewards |
| `Balance.cs` | Reading `balance.json`, and leaving out what cannot be used |
| `Rules.cs` | What a Sim is to the rules (`SimState`), what happened (`Happening`), the meter, choosing an aspiration |
| `Goal.cs` | Each kind of want and fear: when it can be given, what is noted, when it is met |
| `Aspiration.cs` | A Sim's aspiration moved on: goals paid out and rolled, the meter, failure, the lifetime want |
| `Effects.cs` | The rewards' rules: using one, the chance it goes wrong, what lasts |
| `Ledger.cs` | What is remembered for a neighbourhood, and the log |
| `Watcher.cs` | Looking at the game: the household, interactions, objects bought, babies, the lot |
| `GameEffects.cs` | Doing rewards and levels to the game |
| `AspirationsPack.cs` | The patches, the news, the console |
| `UIAspirationWindow.cs` | The Aspiration window, the rewards, and choosing an aspiration or a skill |
| `UIAspirationMeter.cs` | The meter beside the needs |
| `UIControls.cs` | Buttons, the news at the top of the screen, colours |

What it does goes to the terminal and to `Aspirations.log` in the user directory, in lines starting
`[aspire]`: every want fulfilled and fear come true, every interaction it sees end, everything bought,
and every reward used.

## Tests

```bash
dotnet run --project Aspirations/tests
dotnet run --project Aspirations/tests -- --report
```

They need no copy of The Sims. They check that the pack's patches fit the engine it is built against,
that `balance.json` reads whole and gives every aspiration enough to roll, and the pack's own rules:
every kind of goal from rolled to met, a Sim's wants and fears step by step (paying out, locking,
waking, a career changed), the meter at each level, failure and recovery, the lifetime want, growing
up, every kind of reward and its limits, and the file. With `--report` they also print the balance
report above. They do not load a lot or a Sim, so they say nothing about how it looks or plays.
