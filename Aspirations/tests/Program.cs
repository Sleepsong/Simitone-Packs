/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Wants, Fears and Aspirations pack that need no copy of The Sims: that its patches fit the engine it
// is built against, that balance.json is sound, and its own rules (every kind of goal, the meter, failure, the
// lifetime want, the rewards, the file). And a report on balance, to read when tuning: how fast the meter falls at
// each level, and what each aspiration rolls. What they cannot check is the game itself: nothing here loads a lot.
// Run with: dotnet run --project Aspirations/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Aspirations;
using HarmonyLib;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }
var report = args.Contains("--report");

FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "aspire-test-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);

// ---- the patches fit this engine ----
{
    var h = new Harmony("test.aspirations");
    var apply = typeof(AspirationsPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    try { apply.Invoke(null, new object[] { h }); }
    catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
    var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    foreach (var needed in new[] { "VM.Tick", "TS1NeighborhoodProvider.InitSpecific", "TS1GameScreen.Save", "TS1GameScreen.ExitLot", "VMTS1MotiveDecay.Tick",
        "UIMainPanel.SetSubpanel", "UILotControl.Update", "UIMainPanel.AddPacksButton" })
        Check("patched " + needed, patched.Contains(needed));
    h.UnpatchAll(h.Id);
    Watcher.AfterTick(null);
    Check("nobody is watched on no lot", Watcher.Household(null).Count == 0 && !Watcher.AtHome(null));
}

// ---- the pack must run on every Simitone that can load code packs, not only the newest ----
{
    var allowed = new HashSet<string>() { "ISimitonePack", "Pack", "PackManager" };
    using var file = File.OpenRead(typeof(AspirationsPack).Assembly.Location);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var used = md.TypeReferences.Select(t => md.GetTypeReference(t))
        .Where(t => md.GetString(t.Namespace) == "Simitone.Client.Packs").Select(t => md.GetString(t.Name)).Distinct().ToList();
    Check("uses only the first pack API (" + string.Join(", ", used) + ")", used.All(allowed.Contains));
    Check("implements none of the later pack calls", typeof(AspirationsPack).GetMethod("OnLotSaved") == null && !typeof(AspirationsPack).GetMethods().Any(m => m.Name == "OnLotLoaded"));
}

// ---- balance.json ----
var problems = new List<string>();
var packFolder = Path.GetDirectoryName(typeof(AspirationsPack).Assembly.Location);
var source = FindUp("balance.json");
var b = Balance.Parse(File.ReadAllText(source), problems);
{
    Check("balance.json reads with nothing left out" + (problems.Count > 0 ? ": " + string.Join(" | ", problems) : ""), problems.Count == 0);
    Check("the copy inside the .dll is the file", Balance.Embedded() == File.ReadAllText(source));
    Check("seven aspirations, one of them for children", b.Aspirations.Count == 7 && b.Aspirations.Count(x => x.Child) == 1);
    Check("five levels, from failure to platinum, in order", b.Levels.Count == 5 && b.Levels[0].From == -100 && b.Levels.Zip(b.Levels.Skip(1)).All(p => p.First.From < p.Second.From));
    Check("four wants, three fears, one lock", b.Slots.Wants == 4 && b.Slots.Fears == 3 && b.Slots.Locks == 1);
    Check("every goal has points and words", b.Wants.Concat(b.Fears).Concat(b.Lifetime).All(x => x.Points > 0 && !string.IsNullOrWhiteSpace(x.Text)));
    Check("every reward has a cost and an effect the pack knows", b.Rewards.Count >= 10 && b.Rewards.All(x => x.Cost > 0 && Effects.Known(x.Effect.Kind)));
    Check("rewards that can go wrong say what happens, or do nothing", b.Rewards.All(x => x.Backfire == null || Effects.Known(x.Backfire.Kind)));
    foreach (var a in b.Aspirations)
    {
        var wants = b.Wants.Count(x => x.WeightFor(a.Id) > 0);
        var fears = b.Fears.Count(x => x.WeightFor(a.Id) > 0);
        var lifetime = b.Lifetime.Count(x => x.WeightFor(a.Id) > 0);
        Check(a.Name + " has wants (" + wants + "), fears (" + fears + ")" + (a.Child ? "" : " and lifetime wants (" + lifetime + ")") + " enough to fill its slots",
            wants >= 10 && fears >= 5 && (a.Child || lifetime >= 1));
    }
    Check("every kind of goal is used by balance.json", Goal.AllKinds.All(k => b.Wants.Concat(b.Fears).Concat(b.Lifetime).Any(x => string.Equals(x.Kind, k, StringComparison.OrdinalIgnoreCase))));
    Check("every kind of effect is used by a reward, but Nothing", Effects.AllKinds.Where(k => k != "Nothing").All(k => b.Rewards.Any(r => Effects.Is(r.Effect, k) || Effects.Is(r.Backfire, k))));

    var bad = new List<string>();
    var broken = Balance.Parse("{ \"wants\": [ { \"id\": \"x\", \"kind\": \"Teleport\", \"points\": 5 }, { \"id\": \"y\", \"kind\": \"SkillUp\", \"skill\": \"Juggling\" } ], \"rewards\": [ { \"id\": \"z\", \"effect\": { \"kind\": \"Fly\" } } ] }", bad);
    Check("a goal or reward the pack does not know is left out, and said why", broken.Wants.Count == 0 && broken.Rewards.Count == 0 && bad.Any(x => x.Contains("Teleport")) && bad.Any(x => x.Contains("Juggling")) && bad.Any(x => x.Contains("Fly")));
    var folder = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "pack");
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, Balance.FileName), "{ this is not json");
    var fallback = new List<string>();
    var used = Balance.Load(folder, fallback);
    Check("a file that cannot be read gives the pack's own copy, and says so", used.Wants.Count == b.Wants.Count && fallback.Count == 1 && fallback[0].Contains("could not be read"));
    File.Delete(Path.Combine(folder, Balance.FileName));
    fallback.Clear();
    Check("so does no file at all", Balance.Load(folder, fallback).Rewards.Count == b.Rewards.Count && fallback.Count == 1);
    Check("the file's own comments are allowed", File.ReadAllText(source).TrimStart().StartsWith("//"));
}

SimState Adult()
{
    var s = new SimState();
    for (int i = 0; i < s.Skills.Length; i++) s.Skills[i] = 250;
    for (int i = 0; i < s.Traits.Length; i++) s.Traits[i] = 500;
    s.Funds = 20000;
    s.HouseholdSize = 2;
    s.HouseholdAdults = 2;
    s.AtHome = true;
    return s;
}

SimState Employed(int level = 3, int levels = 10)
{
    var s = Adult();
    s.JobType = 4;
    s.JobLevel = level;
    s.JobLevels = levels;
    s.Career = "Science";
    s.NextSkills = new[] { 0, 0, 0, 0, 500, 0 };
    return s;
}

var rng = new Random(7);
GoalDef Def(string kind, Action<GoalDef> set = null)
{
    var d = new GoalDef() { Id = "t_" + kind, Kind = kind, Text = kind + " {skill} {amount} {count} {total} {career}", Points = 1000, Ages = "any", Weights = new Dictionary<string, double>() { { "Any", 1 } }, Match = new List<string>(), Milestones = new List<int>() };
    set?.Invoke(d);
    d.Match = d.Match.Select(x => x.ToLowerInvariant()).ToList();
    return d;
}

// a goal, given a Sim as they are now: whether it can be given, and once given, whether it is met after the change
(bool eligible, bool metBefore, bool metAfter, Slot slot) Try(GoalDef d, SimState s, Action<SimState> change, List<Happening> happenings = null)
{
    var eligible = Goal.Eligible(d, s, b);
    var slot = eligible ? Goal.Make(d, s, rng, b) : null;
    if (slot == null) return (eligible, false, false, null);
    var before = Goal.Met(d, slot, s, new List<Happening>(), b);
    change(s);
    var after = Goal.Met(d, slot, s, happenings ?? new List<Happening>(), b);
    return (eligible, before, after, slot);
}

bool Works((bool eligible, bool metBefore, bool metAfter, Slot slot) t) => t.eligible && t.slot != null && !t.metBefore && t.metAfter;

// ---- every kind of goal ----
{
    Check("SkillUp: a point in the skill rolled", Works(Try(Def("SkillUp", d => d.Skill = "Logic"), Adult(), s => s.Skills[4] = 300)));
    Check("SkillUp: not for a skill at its top", !Goal.Eligible(Def("SkillUp", d => d.Skill = "Logic"), new Func<SimState>(() => { var s = Adult(); s.Skills[4] = 1000; return s; })(), b));
    var anySkill = Try(Def("SkillUp", d => d.Skill = "any"), Adult(), s => { for (int i = 0; i < 6; i++) s.Skills[i] = 320; });
    Check("SkillUp any: one skill chosen, named in the words (" + anySkill.slot?.Text + ")", Works(anySkill) && Rules.SkillNames.Any(n => anySkill.slot.Text.Contains(n)));
    Check("SkillUp: more points at a higher level", Goal.Make(Def("SkillUp", d => { d.Skill = "Logic"; d.PerLevel = 100; }), new Func<SimState>(() => { var s = Adult(); s.Skills[4] = 700; return s; })(), rng, b).Points == 1700);
    Check("CareerSkill: a skill the next promotion asks for", Works(Try(Def("CareerSkill"), Employed(), s => s.Skills[4] = 300)) && !Goal.Eligible(Def("CareerSkill"), Adult(), b));
    Check("SkillMax: a skill near the top, to the top", Works(Try(Def("SkillMax"), new Func<SimState>(() => { var s = Adult(); s.Skills[2] = 850; return s; })(), s => s.Skills[2] = 1000)) && !Goal.Eligible(Def("SkillMax"), Adult(), b));
    Check("SkillsMaxed: a number of skills at the top", Works(Try(Def("SkillsMaxed", d => d.Total = 2), Adult(), s => { s.Skills[0] = 1000; s.Skills[1] = 1000; })));
    Check("GetJob: for the jobless, met by a job", Works(Try(Def("GetJob"), Adult(), s => { s.JobType = 3; s.JobLevels = 10; })) && !Goal.Eligible(Def("GetJob"), Employed(), b));
    Check("Promotion: a level up in the same career", Works(Try(Def("Promotion"), Employed(), s => s.JobLevel = 4)));
    var stale = Def("Promotion");
    var promo = Goal.Make(stale, Employed(), rng, b);
    var changed = Employed(); changed.JobType = 9;
    Check("Promotion: stale in another career, and not met by its level", Goal.Stale(stale, promo, changed) && !Goal.Met(stale, promo, new Func<SimState>(() => { var s = Employed(5); s.JobType = 9; return s; })(), new List<Happening>(), b));
    Check("TopOfCareer: only near the top, met at the top", Works(Try(Def("TopOfCareer", d => d.Count = 2), Employed(7), s => s.JobLevel = 9)) && !Goal.Eligible(Def("TopOfCareer", d => d.Count = 2), Employed(3), b));
    Check("CareerTop: any career's top", Works(Try(Def("CareerTop"), Adult(), s => { s.JobType = 2; s.JobLevels = 10; s.JobLevel = 9; })));
    Check("Performance: up to the threshold", Works(Try(Def("Performance", d => d.Threshold = 60), Employed(), s => s.Performance = 70)));
    Check("LowPerformance: down to the threshold", Works(Try(Def("LowPerformance", d => d.Threshold = -50), Employed(), s => s.Performance = -60)));
    Check("Fired: the job lost", Works(Try(Def("Fired"), Employed(), s => { s.JobType = 0; s.JobLevels = 0; })));
    Check("Fired: a new job instead is not a firing, and is rolled again", new Func<bool>(() => { var d = Def("Fired"); var sl = Goal.Make(d, Employed(), rng, b); var s = Employed(); s.JobType = 8; return !Goal.Met(d, sl, s, new List<Happening>(), b) && Goal.Stale(d, sl, s); })());
    Check("Demoted: a level down", Works(Try(Def("Demoted"), Employed(), s => s.JobLevel = 2)));
    var child = new SimState() { Child = true, Performance = 10, HouseholdSize = 3, HouseholdChildren = 1, AtHome = true };
    Check("Grades: a child's grades up", Works(Try(Def("Grades", d => d.Threshold = 60), child, s => s.Performance = 65)));
    var parent = Adult(); parent.ChildGrades[0x77] = 0; parent.HouseholdChildren = 1;
    Check("ChildGrades: a child of the household does well", Works(Try(Def("ChildGrades", d => d.Threshold = 60), parent, s => s.ChildGrades[0x77] = 61)));
    var parent2 = Adult(); parent2.ChildGrades[0x77] = 30;
    Check("ChildBadGrades: a child of the household does badly", Works(Try(Def("ChildBadGrades", d => d.Threshold = -20), parent2, s => s.ChildGrades[0x77] = -30)));
    var earn = Try(Def("Earn", d => { d.Amount = 1000; d.Fraction = 0.1; }), Adult(), s => s.Funds = 23000);
    Check("Earn: so much more than when rolled (" + earn.slot?.Text + ")", Works(earn) && earn.slot.Get("a") == 3000);
    Check("HaveFunds: the next milestone up", Works(Try(Def("HaveFunds", d => d.Milestones = new List<int>() { 10000, 50000, 100000 }), Adult(), s => s.Funds = 50000)) && Goal.Make(Def("HaveFunds", d => d.Milestones = new List<int>() { 10000, 50000 }), Adult(), rng, b).Get("a") == 50000);
    Check("Buy: an object bought at the price or more", Works(Try(Def("Buy", d => d.Amount = 500), Adult(), s => { }, new List<Happening>() { new Happening() { Kind = HappeningKind.Purchase, Price = 650 } }))
        && !Try(Def("Buy", d => d.Amount = 500), Adult(), s => { }, new List<Happening>() { new Happening() { Kind = HappeningKind.Purchase, Price = 200 } }).metAfter);
    Check("Broke: below the sum", Works(Try(Def("Broke", d => d.Amount = 500), Adult(), s => s.Funds = 400)));
    SimState Social() { var x = Adult(); x.Friends.UnionWith(new[] { 1, 2 }); x.BestFriends.Add(1); return x; }
    Check("NewFriend: someone who was not a friend", Works(Try(Def("NewFriend"), Social(), s => s.Friends.Add(3))));
    Check("NewFriend: losing one and gaining one counts", Works(Try(Def("NewFriend"), new Func<SimState>(() => { var s = Adult(); s.Friends.Add(1); return s; })(), s => { s.Friends.Remove(1); s.Friends.Add(2); })));
    var friends = Try(Def("Friends", d => d.Count = 2), Social(), s => s.Friends.UnionWith(new[] { 3, 4 }));
    Check("Friends: two more than now (" + friends.slot?.Text + ")", Works(friends) && friends.slot.Get("t") == 4);
    Check("BestFriend: a friend becomes a best friend", Works(Try(Def("BestFriend"), Social(), s => s.BestFriends.Add(2))));
    Check("FallInLove: someone new", Works(Try(Def("FallInLove"), Adult(), s => s.Loves.Add(5))));
    Check("LoseFriend: a friend no longer", Works(Try(Def("LoseFriend"), Social(), s => s.Friends.Remove(2))) && !Goal.Eligible(Def("LoseFriend"), Adult(), b));
    Check("NewHousemate: another adult in the household", Works(Try(Def("NewHousemate"), Adult(), s => { s.HouseholdAdults = 3; s.HouseholdSize = 3; })));
    Check("Baby: a baby arrives", Works(Try(Def("Baby"), Adult(), s => { }, new List<Happening>() { new Happening() { Kind = HappeningKind.Baby } })));
    Check("Children: so many children in the household", Works(Try(Def("Children", d => d.Total = 2), Adult(), s => s.HouseholdChildren = 2)));
    Check("LoseMember: someone leaves the household", Works(Try(Def("LoseMember"), Adult(), s => s.HouseholdSize = 1)));
    Check("LoseMember: someone who joined since counts too", new Func<bool>(() => { var d = Def("LoseMember"); var s = Adult(); var sl = Goal.Make(d, s, rng, b); s.HouseholdSize = 3; var a = Goal.Met(d, sl, s, new List<Happening>(), b); s.HouseholdSize = 2; return !a && Goal.Met(d, sl, s, new List<Happening>(), b); })());

    var kiss = Def("Interaction", d => { d.Match = new List<string>() { "Kiss" }; d.With = "person"; d.Outcome = "accepted"; });
    Happening Did(string name, Partner with, int outcome, int person = 9) => new Happening() { Kind = HappeningKind.Done, Name = name, With = with, Outcome = outcome, Person = person };
    Check("Interaction: a kiss that went down well", Works(Try(kiss, Adult(), s => { }, new List<Happening>() { Did("kiss/romantic", Partner.Other, 1) })));
    Check("Interaction: not one turned down, nor with an object", !Try(kiss, Adult(), s => { }, new List<Happening>() { Did("kiss", Partner.Other, -1), Did("kiss", Partner.Object, 0) }).metAfter);
    var rejected = Def("Interaction", d => { d.Match = new List<string>() { "kiss", "flirt" }; d.Outcome = "rejected"; d.With = "person"; });
    Check("Interaction fear: being turned down", Works(Try(rejected, Adult(), s => { }, new List<Happening>() { Did("flirt", Partner.Household, -1) })));
    var family = Def("Interaction", d => { d.Match = new List<string>() { "tickle" }; d.With = "household"; });
    Check("Interaction: with the household only", Works(Try(family, Adult(), s => { }, new List<Happening>() { Did("tickle", Partner.Household, 0) })) && !Try(family, Adult(), s => { }, new List<Happening>() { Did("tickle", Partner.Other, 1) }).metAfter);
    var kisses = Def("Interaction", d => { d.Match = new List<string>() { "kiss" }; d.With = "person"; d.Outcome = "accepted"; d.Total = 3; });
    {
        var s = Adult();
        var sl = Goal.Make(kisses, s, rng, b);
        var a = Goal.Met(kisses, sl, s, new List<Happening>() { Did("kiss", Partner.Other, 1, 1), Did("kiss", Partner.Other, 1, 1) }, b);
        var c = Goal.Met(kisses, sl, s, new List<Happening>() { Did("kiss", Partner.Other, 1, 2) }, b);
        var d2 = Goal.Met(kisses, sl, s, new List<Happening>() { Did("kiss", Partner.Other, 1, 3) }, b);
        Check("Interaction with a total: different people, counted over time", !a && !c && d2 && sl.Who.Count == 3);
    }
    Check("Received: someone slaps the Sim", Works(Try(Def("Received", d => d.Match = new List<string>() { "slap" }), Adult(), s => { }, new List<Happening>() { new Happening() { Kind = HappeningKind.Received, Name = "slap" } })));
    Check("Party: so many guests at home", Works(Try(Def("Party", d => d.Count = 3), Adult(), s => s.Guests = 3)) && !Goal.Met(Def("Party", d => d.Count = 3), new Slot(), new SimState() { Guests = 5, AtHome = false }, new List<Happening>(), b));
    Check("Outing: away from home", Works(Try(Def("Outing"), Adult(), s => s.AtHome = false)));
    Check("NeedHigh: mood up", Works(Try(Def("NeedHigh", d => { d.Need = "Mood"; d.Threshold = 70; }), Adult(), s => s.Needs[Rules.MoodNeed] = 75)));
    Check("NeedLow: hunger down", Works(Try(Def("NeedLow", d => { d.Need = "Hunger"; d.Threshold = -80; }), Adult(), s => s.Needs[0] = -85)));
    Check("Fire: a fire", Works(Try(Def("Fire"), Adult(), s => s.Fire = true)));
    Check("Visitor: a burglar", Works(Try(Def("Visitor", d => d.Match = new List<string>() { "burglar" }), Adult(), s => s.Visitors.Add("burglar npc_burglar.iff"))));
    Check("goals for grown Sims are not given to children", !Goal.Eligible(Def("GetJob"), child, b) && !Goal.Eligible(Def("Earn", d => d.Ages = "adult"), child, b));
    Check("words are filled in", Goal.Make(Def("TopOfCareer", d => d.Text = "Reach the top of the {career} career"), Employed(8), rng, b).Text == "Reach the top of the Science career");
}

// ---- a Sim's aspiration, step by step ----
{
    var s = Adult();
    var r = new SimRecord();
    var news = Aspiration.Step(r, s, null, 0, false, b, rng);
    Check("a Sim met is given an aspiration by chance (" + r.Aspiration + "), four wants, three fears and a lifetime want",
        r.Aspiration != null && !r.Chosen && !r.Asked && r.Wants.Count == 4 && r.Fears.Count == 3 && r.Lifetime != null);
    Check("their wants are four different goals", r.Wants.Select(x => x.Id).Distinct().Count() == 4);

    Aspiration.Assign(r, "Fortune", false, true, b);
    Check("choosing an aspiration starts the wants over", r.Aspiration == "Fortune" && r.Chosen && r.Asked && r.Wants.Count == 0);
    Aspiration.Step(r, s, null, 0, false, b, rng);
    Check("and they are filled for it", r.Wants.Count == 4 && r.Wants.All(w => b.Want(w.Id).WeightFor("Fortune") > 0));

    // fulfil a want by hand: one for money
    r.Wants[0] = Goal.Make(b.Want("earn"), s, rng, b);
    var points = r.Points;
    var meter = r.Meter;
    s.Funds += 100000;
    news = Aspiration.Step(r, s, null, 0, false, b, rng);
    var fulfilled = news.Where(n => n.Kind == NewsKind.Fulfilled).ToList();
    Check("a want fulfilled pays its points and lifts the meter", fulfilled.Count >= 1 && r.Points > points && r.Meter > meter && r.Earned == r.Points);
    Check("and its slot is filled again", r.Wants.Count == 4);

    // a fear
    r.Fears[0] = Goal.Make(b.Fear("broke"), s, rng, b);
    points = r.Points;
    meter = r.Meter;
    s.Funds = 100;
    news = Aspiration.Step(r, s, null, 0, false, b, rng);
    Check("a fear come true lowers the meter, and takes no points", news.Any(n => n.Kind == NewsKind.Realized) && r.Meter < meter && r.Points == points && r.Lost > 0);
    s.Funds = 20000;

    // locking, and waking
    Check("a want can be locked", Aspiration.Lock(r, 1, b) == null && r.Wants[1].Locked);
    Check("only one", Aspiration.Lock(r, 2, b) != null && !r.Wants[2].Locked);
    var locked = r.Wants[1];
    Aspiration.Step(r, s, null, 0, true, b, rng);
    Check("on waking, the locked want stays and the rest are rolled again", r.Wants.Contains(locked) && r.Wants.Count == 4 && r.Fears.Count == 3);
    Check("and unlocking it lets it go too", Aspiration.Lock(r, r.Wants.IndexOf(locked), b) == null && !locked.Locked);

    // a stale goal is rolled again
    var e = Employed();
    var worker = new SimRecord();
    Aspiration.Assign(worker, "Fortune", false, true, b);
    Aspiration.Step(worker, e, null, 0, false, b, rng);
    worker.Wants[0] = Goal.Make(b.Want("promotion"), e, rng, b);
    e.JobType = 9;
    Aspiration.Step(worker, e, null, 0, false, b, rng);
    Check("a want to be promoted goes when the career does", !worker.Wants.Any(x => x.Id == "promotion" && x.Get("jt") == 4));
}

// ---- the meter ----
{
    double Day(double start, int mood, double hours = 24)
    {
        var m = start;
        for (int i = 0; i < hours * 6; i++) m = Rules.Clamp(m + Rules.Drift(m, mood, 10, b));
        return m;
    }
    Check("in an even mood the meter falls", Day(0, 0) < 0);
    Check("in a fine mood it falls slower, or rises", Day(0, 80) > Day(0, 0));
    Check("in failure it holds but for mood", Day(-90, 0) == -90 && Day(-90, 50) > -90);
    Check("platinum falls fastest", (Day(95, 0, 1) - 95) < (Day(70, 0, 1) - 70) && (Day(70, 0, 1) - 70) < (Day(10, 0, 1) - 10));

    var r = new SimRecord() { Aspiration = "Fortune", Chosen = true, Asked = true, Meter = 0 };
    var s = Adult();
    Aspiration.Step(r, s, null, 0, false, b, rng);
    r.Meter = 60;
    var news = Aspiration.Step(r, s, null, 1, false, b, rng);
    Check("reaching a level is news", news.Any(n => n.Kind == NewsKind.Level && n.Text == "Gold"));
    r.Meter = 49.5;
    news = Aspiration.Step(r, s, null, 1, false, b, rng);
    Check("wavering at its edge is not", !news.Any(n => n.Kind == NewsKind.Level));

    r.Meter = -95;
    var needs = Adult(); needs.Needs[Rules.MoodNeed] = -100;
    news = Aspiration.Step(r, needs, null, 10, false, b, rng);
    Check("falling into failure is a breakdown", news.Count(n => n.Kind == NewsKind.Breakdown) == 1 && r.BrokeDown && r.Breakdowns == 1);
    news = Aspiration.Step(r, needs, null, 10, false, b, rng);
    Check("once", !news.Any(n => n.Kind == NewsKind.Breakdown));
    for (int i = 0; i < 24 * 6; i++) news.AddRange(Aspiration.Step(r, needs, null, 10, false, b, rng));
    Check("after a day of failure the Sim pulls themselves together (" + r.Meter.ToString("0") + ")", news.Any(n => n.Kind == NewsKind.Recovered) && !Rules.Failing(r.Meter, b));

    var life = new SimRecord() { Aspiration = "Knowledge", Chosen = true, Asked = true };
    var k = Adult();
    Aspiration.Step(life, k, null, 0, false, b, rng);
    life.Lifetime = Goal.Make(b.LifetimeWant("life_skills"), k, rng, b);
    for (int i = 0; i < 4; i++) k.Skills[i] = 1000;
    news = Aspiration.Step(life, k, null, 0, false, b, rng);
    Check("a lifetime want fulfilled pays and is news", life.LifetimeDone && news.Any(n => n.Kind == NewsKind.Lifetime) && life.Points >= 20000);
    life.Meter = -50;
    Aspiration.Step(life, k, null, 1, false, b, rng);
    Check("and the meter never falls below gold again", life.Meter >= b.Meter.LifetimeFloor && Rules.Level(life.Meter, b).Name == "Gold");

    var kid = new SimRecord();
    var ks = new SimState() { Child = true, HouseholdSize = 3, HouseholdChildren = 1, AtHome = true };
    Aspiration.Step(kid, ks, null, 0, false, b, rng);
    Check("a child grows up, with no lifetime want", kid.Aspiration == "GrowUp" && kid.Asked && kid.Lifetime == null && kid.Wants.Count == 4);
    ks.Child = false;
    news = Aspiration.Step(kid, ks, null, 0, false, b, rng);
    Check("a child who grows up is given a grown Sim's aspiration, and asked", news.Any(n => n.Kind == NewsKind.GrewUp) && kid.Aspiration != "GrowUp" && !kid.Asked && kid.Lifetime != null);
}

// ---- rewards ----
{
    var r = new SimRecord() { Aspiration = "Fortune", Chosen = true, Asked = true, Meter = 60, Points = 50000 };
    var s = Adult();
    var tonic = b.Reward("tonic");
    s.Needs[Rules.EnergyNeed] = -20;
    Check("a reward can be used with points enough", Effects.CannotUse(tonic, r, s, b) == null);
    var result = Effects.Use(tonic, r, s, b, new Random(1));
    Check("using it spends its points, and cannot go wrong at gold", r.Points == 50000 - tonic.Cost && !result.Backfired && Effects.Is(result.Effect, "Needs"));
    Check("and then waits for its cooldown", Effects.CannotUse(tonic, r, s, b)?.StartsWith("Again in") == true);
    r.Minutes += (long)(tonic.Cooldown * 60);
    Check("then can be used again", Effects.CannotUse(tonic, r, s, b) == null);

    var poor = new SimRecord() { Points = 10, Meter = 0 };
    Check("not without the points", Effects.CannotUse(tonic, poor, s, b)?.StartsWith("Needs") == true);
    var low = new SimRecord() { Points = 50000, Meter = -90 };
    Check("in failure, it is likely to go wrong", Effects.Risk(tonic, low, b) == 0.6);
    var wrong = Effects.Use(tonic, low, s, b, new FixedRandom(0.1));
    Check("and when it does, its backfire happens instead", wrong.Backfired && wrong.Effect == tonic.Backfire && wrong.Text.Contains("flat"));
    var voucher = b.Reward("voucher");
    Check("a safe reward never goes wrong", Effects.Risk(voucher, low, b) == 0 && !Effects.Use(voucher, low, s, b, new FixedRandom(0.0)).Backfired);
    Check("a voucher waits until something is bought", low.Effects.Any(x => x.Kind == "Voucher" && x.Value == 3000 && x.Until == 0));
    Check("and only one at a time", Effects.CannotUse(voucher, low, s, b) != null);

    var rec = new SimRecord() { Points = 100000, Meter = 90 };
    Effects.Use(b.Reward("serenity"), rec, s, b, new Random(1));
    var serenity = rec.Effects.Single();
    Check("Serenity keeps every need full for eight hours", serenity.Kind == "Freeze" && serenity.Needs.Count == 7 && serenity.Value == 100 && serenity.Until == rec.Minutes + 480);
    Check("and cannot be used twice at once", Effects.CannotUse(b.Reward("serenity"), rec, s, b) != null);
    var news = new List<News>();
    for (int i = 0; i < 9; i++) news.AddRange(Aspiration.Step(rec, s, null, 60, false, b, rng));
    Check("it wears off, and that is news", rec.Effects.Count == 0 && news.Any(n => n.Kind == NewsKind.WornOff && n.Text == "Serenity"));

    var dividend = b.Reward("dividend");
    Effects.Use(dividend, rec, s, b, new Random(1));
    Check("a dividend has its payments to come", rec.Effects.Single().Kind == "Dividend" && rec.Effects.Single().Left == 7 && rec.Effects.Single().Value == 400);

    var review = b.Reward("review");
    Check("a review needs a job, or grades", Effects.CannotUse(review, rec, s, b) == "Needs a job." && Effects.CannotUse(review, rec, Employed(), b) == null && Effects.CannotUse(review, rec, new SimState() { Child = true }, b) == null);
    Check("charm needs someone to charm", Effects.CannotUse(b.Reward("charm"), rec, s, b) != null && Effects.CannotUse(b.Reward("charm"), rec, new Func<SimState>(() => { var x = Adult(); x.Others = 1; return x; })(), b) == null);
    var wind = b.Reward("second_wind");
    Check("Second Wind only below where it lifts to", Effects.CannotUse(wind, rec, s, b) != null && Effects.CannotUse(wind, new SimRecord() { Points = 9999, Meter = -60 }, s, b) == null);
    var down = new SimRecord() { Points = 9999, Meter = -60 };
    Effects.Use(wind, down, s, b, new FixedRandom(0));
    Check("and lifts the meter", down.Meter == 0);
    Check("a windfall is not for children", Effects.CannotUse(b.Reward("windfall"), rec, new SimState() { Child = true }, b) != null);
    var skills = Adult(); skills.Skills = new[] { 1000, 340, 290, 380, 1000, 100 };
    Check("a skill point goes where asked, or else nearest its next point", Effects.SkillFor(skills, 5) == 5 && Effects.SkillFor(skills, 0) == 2 && Effects.SkillFor(skills, -1) == 2);
    Check("not to a skill at its top", Effects.CannotUse(b.Reward("tutor"), rec, skills, b, 0) != null);
}

// ---- what is remembered ----
{
    var hood = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
    var other = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData2/");
    var fresh = Ledger.Read("sample.aspirations", hood, b);
    Check("no file: a fresh record", fresh.Sims.Count == 0 && fresh.Settings.On && fresh.Settings.Ask);
    var sim = fresh.Meet(0x1234abcd, b);
    Aspiration.Step(sim, Adult(), null, 0, false, b, rng);
    Aspiration.Lock(sim, 0, b);
    sim.Points = 1234;
    sim.Effects.Add(new ActiveEffect() { Reward = "serenity", Name = "Serenity", Kind = "Freeze", Needs = new List<string>() { "Hunger" }, Value = 100, Until = 300 });
    Check("written", fresh.Write("sample.aspirations", hood));
    var again = Ledger.Read("sample.aspirations", hood, b);
    var back = again.Find(0x1234abcd);
    Check("read back: aspiration, points, wants, the lock, fears, lifetime want and effects", back != null && back.Aspiration == sim.Aspiration && back.Points == 1234 && back.Wants.Count == 4 && back.Wants[0].Locked
        && back.Fears.Count == 3 && back.Lifetime?.Id == sim.Lifetime.Id && back.Effects.Single().Until == 300 && back.Wants[0].N.Count == sim.Wants[0].N.Count);
    Check("another neighbourhood has its own", Ledger.Read("sample.aspirations", other, b).Sims.Count == 0);
    Check("the file is beside the saves, not in them", Ledger.PathFor("sample.aspirations", hood).Replace('\\', '/').Contains("/LocalHouse/PackData/sample.aspirations/"));

    var playing = Ledger.Read("sample.aspirations", hood, b);
    playing.Find(0x1234abcd).Points = 99999;
    playing.Settings.Notices = false;
    playing.WriteSettings("sample.aspirations", hood, b);
    var kept = Ledger.Read("sample.aspirations", hood, b);
    Check("a setting is kept at once, and play that was not saved is not kept with it", !kept.Settings.Notices && kept.Find(0x1234abcd).Points == 1234);

    // a balance.json with fewer goals: what the Sim holds of the ones taken out goes
    var smaller = Balance.Parse(File.ReadAllText(source), new List<string>());
    smaller.Wants.RemoveAll(x => x.Id == back.Wants[1].Id);
    smaller.Slots.Fears = 2;
    var held = back.Wants[1].Id;
    back.Repair(smaller);
    Check("goals no longer in balance.json are dropped, and slots fit the numbers", back.Wants.All(x => x.Id != held) && back.Fears.Count == 2);
}

// ---- rolling: what each aspiration gets ----
{
    var lines = new List<string>();
    foreach (var a in b.Aspirations)
    {
        var s = a.Child ? new SimState() { Child = true, HouseholdSize = 3, HouseholdChildren = 1, AtHome = true, Performance = 0 } : Employed();
        s.Friends.UnionWith(new[] { 1, 2, 3 });
        for (int i = 0; i < 6; i++) s.Skills[i] = 300;
        var counts = new Dictionary<string, int>();
        var empty = 0;
        for (int i = 0; i < 3000; i++)
        {
            var slot = Aspiration.Roll(b.Wants, a.Id, s, new List<Slot>(), b, rng);
            if (slot == null) { empty++; continue; }
            counts[slot.Id] = counts.TryGetValue(slot.Id, out var n) ? n + 1 : 1;
        }
        Check(a.Name + " always has a want to roll", empty == 0);
        var top = counts.OrderByDescending(x => x.Value).Take(5).Select(x => x.Key + " " + (x.Value * 100 / 3000) + "%");
        lines.Add(a.Name + ": " + string.Join(", ", top));
    }
    var fortune = new Dictionary<string, int>();
    for (int i = 0; i < 3000; i++)
    {
        var slot = Aspiration.Roll(b.Wants, "Fortune", Employed(), new List<Slot>(), b, rng);
        fortune[slot.Id] = fortune.TryGetValue(slot.Id, out var n) ? n + 1 : 1;
    }
    var money = new[] { "earn", "funds", "promotion", "top_career", "skill_career", "performance", "buy_big", "buy_small", "get_job" }.Sum(x => fortune.TryGetValue(x, out var n) ? n : 0);
    Check("Fortune rolls mostly money and work (" + (money * 100 / 3000) + "%)", money > 3000 * 0.6);
    var kidWants = new SimState() { Child = true, HouseholdSize = 3, HouseholdChildren = 1, AtHome = true };
    Check("a child is never rolled a grown Sim's want", Enumerable.Range(0, 500).Select(i => Aspiration.Roll(b.Wants, "GrowUp", kidWants, new List<Slot>(), b, rng)).All(x => x == null || Rules.AgeFits(b.Want(x.Id).Ages, true)));
    if (report) foreach (var l in lines) Console.WriteLine("     " + l);

    var fits = Rules.Fits(new Func<SimState>(() => { var s = Adult(); s.Traits[3] = 1000; s.Traits[1] = 900; return s; })(), b);
    Check("a playful, outgoing Sim suits Pleasure, Romance or Popularity best (" + fits.OrderByDescending(x => x.Value).First().Key + ")", new[] { "Pleasure", "Romance", "Popularity" }.Contains(fits.OrderByDescending(x => x.Value).First().Key));
    var thinker = Adult(); thinker.Skills[4] = 900; thinker.Skills[5] = 700; thinker.Skills[1] = 700;
    Check("a Sim of logic and creativity suits Knowledge", Rules.Suggest(thinker, b, new Random(3)) == "Knowledge");
    Check("a child is always Grow Up", Rules.Suggest(new SimState() { Child = true }, b, rng) == "GrowUp");
}

// ---- balance: a report for tuning, and its limits ----
{
    // how many points of wants a day hold each level, in an even mood and in a good one
    foreach (var mood in new[] { 0, 50 })
    {
        var parts = new List<string>();
        foreach (var level in b.Levels.Skip(1))
        {
            var mid = level.From + 1;
            var fall = -Rules.Drift(mid, mood, 24 * 60, b);
            parts.Add(level.Name + " " + Math.Max(0, fall / b.Meter.PerPoint).ToString("#,0"));
        }
        if (report) Console.WriteLine("     points of wants a day to hold each level, mood " + mood + ": " + string.Join(", ", parts));
    }
    var averageWant = b.Wants.Average(x => x.Points);
    var gold = b.Levels.First(x => x.Name == "Gold");
    var platinum = b.Levels.First(x => x.Name == "Platinum");
    var wantsForGold = -Rules.Drift(gold.From + 1, 0, 24 * 60, b) / b.Meter.PerPoint / averageWant;
    var wantsForPlatinum = -Rules.Drift(platinum.From + 1, 0, 24 * 60, b) / b.Meter.PerPoint / averageWant;
    Check("holding gold takes a few wants a day (" + wantsForGold.ToString("0.0") + "), platinum more (" + wantsForPlatinum.ToString("0.0") + ")", wantsForGold >= 1 && wantsForGold <= 4 && wantsForPlatinum > wantsForGold && wantsForPlatinum <= 6);
    var cheapest = b.Rewards.Min(x => x.Cost);
    var dearest = b.Rewards.Max(x => x.Cost);
    Check("the cheapest reward takes a few wants (" + (cheapest / averageWant).ToString("0.0") + "), the dearest many (" + (dearest / averageWant).ToString("0.0") + ")", cheapest / averageWant >= 2 && dearest / averageWant >= 10);
    var windfall = b.Reward("windfall");
    var earnWant = b.Want("earn");
    Check("money bought with points is dearer than money earned for points", windfall.Effect.Amount / (double)windfall.Cost < 1000.0 / earnWant.Points * 1.0);
    Check("the lifetime want is worth a day or more of wants", b.Lifetime.All(x => x.Points >= averageWant * 8));
    if (report) Console.WriteLine("     average want " + averageWant.ToString("#,0") + " points; average fear " + b.Fears.Average(x => x.Points).ToString("#,0") + "; rewards " + Rules.Number(cheapest) + " to " + Rules.Number(dearest));
}

// ---- the console, with no game running ----
{
    var old = Watcher.NeighbourhoodPath;
    Watcher.NeighbourhoodPath = () => null;
    var answer = AspirationsPack.Run(new[] { "help" }, out var understood);
    Check("the console does not fall over without a game", answer == "There is no neighbourhood loaded.");
    Watcher.NeighbourhoodPath = old;
}

try { Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true); } catch { }
Console.WriteLine(failed == 0 ? "All passed." : failed + " FAILED.");
return failed == 0 ? 0 : 1;

static string FindUp(string name)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, name);
        if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Aspirations.csproj"))) return candidate;
        dir = dir.Parent;
    }
    throw new FileNotFoundException(name);
}

/// <summary>A Random that always gives the same number, to make chance come out one way.</summary>
class FixedRandom : Random
{
    private readonly double Value;
    public FixedRandom(double value) { Value = value; }
    public override double NextDouble() => Value;
    protected override double Sample() => Value;
}
