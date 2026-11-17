/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Memories pack that need no copy of The Sims: that its patches fit the engine it is built against,
// the rules by which the life-event layer tells what happened, which memories each event leaves, and the file they
// are kept in. What they cannot check is the game's own scripts doing these things: nothing here loads a lot.
// That another pack can use the layer is checked by tests/crosspack.
// Run with: dotnet run --project Memories/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Memories;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }
Log.ToFile = false;

// ---- the patches fit this engine ----
{
    var h = new Harmony("test.memories");
    var apply = typeof(MemoriesPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    try { apply.Invoke(null, new object[] { h }); }
    catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
    var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    foreach (var needed in new[] {
        "VM.Tick", "TS1NeighborhoodProvider.InitSpecific", "TS1GameScreen.Save", "TS1GameScreen.ExitLot",
        "VMThread.ExecuteAction", "VMThread.EndCurrentInteraction", "VMThread.AbortCurrentInteraction",
        "VMCreateObjectInstance.Execute", "VMContext.CreateObjectInstance", "VMTS1MakeNewCharacter.Execute",
        "VMGenericTS1Call.Execute", "VMBurn.Execute", "VMRemoveObjectInstance.Execute",
        "UILotControl.Update", "UIMainPanel.AddPacksButton" })
        Check("patched " + needed, patched.Contains(needed));
    h.UnpatchAll(h.Id);
}
// the per-tick hook must be harmless on a lot it knows nothing about
Watching.AfterTick(null);
Check("no baby where there is no object", !Watching.IsBaby(null));

// ---- the pack must run on every Simitone that can load code packs, not only the newest ----
{
    var allowed = new HashSet<string>() { "ISimitonePack", "Pack", "PackManager" };
    using var file = File.OpenRead(typeof(MemoriesPack).Assembly.Location);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var used = md.TypeReferences.Select(t => md.GetTypeReference(t))
        .Where(t => md.GetString(t.Namespace) == "Simitone.Client.Packs").Select(t => md.GetString(t.Name)).Distinct().ToList();
    Check("uses only the first pack API (" + string.Join(", ", used) + ")", used.All(allowed.Contains));
    Check("implements none of the later pack calls", typeof(MemoriesPack).GetMethod("OnLotSaved") == null && !typeof(MemoriesPack).GetMethods().Any(m => m.Name == "OnLotLoaded"));
}

// ---- telling things by name ----
{
    var kisses = new[] { "Kiss", "Kiss - Romantic", "Kiss - Passionate", "French Kiss", "Interaction - Kiss", "Social - Kissing", "kisses" };
    var notKisses = new[] { "Kiss Hand", "Blow Kiss", "Air Kiss", "Kiss Cheek", "Hug", "", null };
    Check("kisses are kisses: " + string.Join(", ", kisses), kisses.All(Recognise.Kiss.In));
    Check("and these are not: " + string.Join(", ", notKisses.Where(x => !string.IsNullOrEmpty(x))), notKisses.All(n => !Recognise.Kiss.In(n)));
    Check("a kiss is found among an interaction's names", Recognise.Kiss.InAny(new[] { "Smooch", "Social - Kiss" }) && !Recognise.Kiss.InAny(new[] { "Smooch", "Hug" }));
    Check("a proposal: Propose, Proposal, Marry", new[] { "Propose", "Ask to Marry", "Interaction - Proposal", "Marriage" }.All(Recognise.Proposal.In));
    Check("moving in is not a proposal", !Recognise.Proposal.In("Ask to Move In") && !Recognise.Proposal.In("Invite to Live Here"));
    Check("death, as scripts name it", new[] { "Die - Hunger", "Sim Dies", "Death - Fire", "Grim Reaper - Main", "Starvation", "Drowning", "dying" }.All(Recognise.Death.In));
    Check("and what is not death", new[] { "Diet", "Diesel", "Studies", "Go Here", "Near Death Experience" }.All(n => !Recognise.Death.In(n)));
    Check("the burglar", Recognise.Burglar.In("Burglar") && Recognise.Burglar.In("NPC - Burglar") && !Recognise.Burglar.In("Bella"));
    Check("the police", Recognise.Police.In("Police Officer") && Recognise.Police.In("Cop") && !Recognise.Police.In("Copper Pot") && !Recognise.Police.In("Copy"));
}

// ---- stages, jobs, skills ----
{
    Check("stages from the game's ages", Recognise.StageOf(9) == Stage.Child && Recognise.StageOf(15) == Stage.Teen && Recognise.StageOf(27) == Stage.Adult
        && Recognise.StageOf(65) == Stage.Elder && Recognise.StageOf(0) == Stage.Adult && Recognise.StageOf(17) == Stage.Teen && Recognise.StageOf(18) == Stage.Adult);
    Check("a first job", Recognise.JobChange(0, 0, 3, 0) == LifeEventKind.GotJob);
    Check("a promotion", Recognise.JobChange(3, 2, 3, 3) == LifeEventKind.Promoted);
    Check("a demotion", Recognise.JobChange(3, 2, 3, 1) == LifeEventKind.Demoted);
    Check("out of work", Recognise.JobChange(3, 2, 0, 0) == LifeEventKind.LostJob);
    Check("another career is a new job, whatever the level", Recognise.JobChange(3, 5, 7, 0) == LifeEventKind.GotJob);
    Check("no change is nothing, nor a level without a job", Recognise.JobChange(3, 2, 3, 2) == null && Recognise.JobChange(0, 0, 0, 4) == null);
    Check("skill points are hundreds", Recognise.SkillPoints(0) == 0 && Recognise.SkillPoints(99) == 0 && Recognise.SkillPoints(100) == 1 && Recognise.SkillPoints(1000) == 10 && Recognise.SkillPoints(-5) == 0);
}

// ---- how an interaction went ----
{
    Check("liked more: it went well", Recognise.WentWell(10, 10, 14, 10) == 1 && Recognise.WentWell(10, 10, 10, 12) == 1);
    Check("one up and one down still went well (the one asked liked it)", Recognise.WentWell(10, 10, 8, 15) == 1);
    Check("both down: it did not", Recognise.WentWell(10, 10, 5, 2) == 0);
    Check("nothing changed, or nothing known: unknown", Recognise.WentWell(10, 10, 10, 10) == -1 && Recognise.WentWell(null, null, 5, 5) == -1);
    Check("a score not known before is left out", Recognise.WentWell(null, 10, 3, 9) == 0);
    Check("two Sims at the top who stay there: it went well", Recognise.WentWell(100, 100, 100, 100) == 1 && Recognise.WentWell(100, 60, 100, 60) == -1);
}

// ---- babies ----
{
    const uint Ma = 1, Pa = 2, Kid = 3, Visitor = 9;
    var household = new List<uint>() { Ma, Pa, Kid };
    bool Adult(uint s) => s != Kid;
    uint? Spouse(uint s) => s == Ma ? Pa : s == Pa ? Ma : (uint?)null;
    Check("both Sims in the bed are the parents", Recognise.PickParents(new uint[] { Pa, Ma }, household, Adult, Spouse).SequenceEqual(new uint[] { Pa, Ma }));
    Check("with one, their spouse is the other", Recognise.PickParents(new uint[] { Ma }, household, Adult, Spouse).SequenceEqual(new uint[] { Ma, Pa }));
    Check("a child and a visitor are not parents", Recognise.PickParents(new uint[] { Kid, Visitor }, household, Adult, Spouse).Count == 0);
    Check("nobody involved: no parents", Recognise.PickParents(new uint[0], household, Adult, null).Count == 0);

    var waiting = new List<WaitingBaby>() {
        new WaitingBaby() { House = 5, ObjectId = 40, ParentA = Ma, ParentB = Pa, Arrived = new LifeTime(3, 14, 0) },
        new WaitingBaby() { House = 7, ObjectId = 41 } };
    var found = Recognise.TakeBaby(waiting, 5, new[] { 12, 40 }, out var guessed);
    Check("the child is the baby it was made from", found?.ObjectId == 40 && !guessed && waiting.Count == 1);
    found = Recognise.TakeBaby(waiting, 7, new[] { 99 }, out guessed);
    Check("or the only baby in the house, guessed", found?.ObjectId == 41 && guessed && waiting.Count == 0);
    Check("no baby in the house: none", Recognise.TakeBaby(waiting, 7, new[] { 41 }, out guessed) == null);
    Check("a baby's parents leave out the unknown", new WaitingBaby() { ParentA = Ma }.Parents().SequenceEqual(new uint[] { Ma }));
}

// ---- the life-event bus ----
{
    var heard = new List<LifeEventKind>();
    Action<LifeEvent> bad = e => throw new InvalidOperationException("a listener that fails");
    Action<LifeEvent> good = e => heard.Add(e.Kind);
    LifeEvents.Happened += bad;
    LifeEvents.Happened += good;
    var failuresBefore = LifeEvents.ListenerFailures;
    LifeEvents.Raise(new LifeEvent() { Kind = LifeEventKind.SkillUp, Sim = 1 });
    Check("a listener that fails does not stop the next", heard.SequenceEqual(new[] { LifeEventKind.SkillUp }) && LifeEvents.ListenerFailures == failuresBefore + 1);
    LifeEvents.Happened -= bad;
    LifeEvents.Happened -= good;
    for (int i = 0; i < LifeEvents.RecentKept + 10; i++) LifeEvents.Raise(new LifeEvent() { Kind = LifeEventKind.Interaction, Value = i });
    Check("the recent events are kept, the newest " + LifeEvents.RecentKept, LifeEvents.Recent.Count == LifeEvents.RecentKept && LifeEvents.Recent.Last().Value == LifeEvents.RecentKept + 9);
    Check("a time reads as the game would say it", new LifeTime(12, 18, 5).ToString() == "Day 12, 6:05 pm" && new LifeTime(1, 0, 30).ToString() == "Day 1, 12:30 am" && new LifeTime(2, 12, 0).ToString() == "Day 2, 12:00 pm");
    Check("times are in order across days", new LifeTime(2, 1, 0).CompareTo(new LifeTime(1, 23, 59)) > 0);
}

// ---- which memories an event leaves ----
const uint Bob = 0xB0B, Bella = 0xBE11A, Cass = 0xCA55, Mort = 0x3047, Kid2 = 0xC1D;
string Name(uint s) => s == Bob ? "Bob Newbie" : s == Bella ? "Bella Goth" : s == Cass ? "Cassandra Goth" : s == Mort ? "Mortimer Goth" : s == Kid2 ? "Alex Goth" : "Sim " + s.ToString("X");
Stage StageOf(uint s) => s == Kid2 ? Stage.Child : s == Cass ? Stage.Teen : Stage.Adult;
var noRelatives = (Func<uint, IEnumerable<Remember.Relative>>)(s => Enumerable.Empty<Remember.Relative>());
var at = new LifeTime(4, 20, 15);
{
    var j = new Journal();
    List<Memory> Keep(LifeEvent e, Func<uint, IEnumerable<Remember.Relative>> rel = null)
    {
        e.When = e.When.Day == 0 ? at : e.When;
        var made = Remember.From(e, j, StageOf, rel ?? noRelatives);
        foreach (var m in made) j.Add(m);
        return made;
    }

    var first = Keep(new LifeEvent() { Kind = LifeEventKind.Kiss, Sim = Bob, Other = Bella, House = 3 });
    Check("a first kiss is remembered by both", first.Count == 2 && first.All(m => m.Kind == MemoryKind.FirstKiss)
        && first.Any(m => m.Sim == Bob && m.Other == Bella) && first.Any(m => m.Sim == Bella && m.Other == Bob));
    Check("as " + first[0].Describe(Name), first.First(m => m.Sim == Bob).Describe(Name) == "First kiss, with Bella Goth");
    Check("a second kiss is not a first", Keep(new LifeEvent() { Kind = LifeEventKind.Kiss, Sim = Bella, Other = Bob }).Count == 0);
    var third = Keep(new LifeEvent() { Kind = LifeEventKind.Kiss, Sim = Bella, Other = Mort });
    Check("a kiss with someone new is their first only for them", third.Count == 1 && third[0].Sim == Mort);

    var wed = Keep(new LifeEvent() { Kind = LifeEventKind.Married, Sim = Bella, Other = Mort });
    Check("a wedding, for both: " + wed[0].Describe(Name), wed.Count == 2 && wed.Any(m => m.Sim == Mort && m.Other == Bella) && wed[0].Describe(Name) == "Married Mortimer Goth");

    var promo = Keep(new LifeEvent() { Kind = LifeEventKind.Promoted, Sim = Mort, Detail = "General (Military)", Value3 = 1 });
    Check("a promotion: " + promo[0].Describe(Name), promo.Count == 1 && promo[0].Describe(Name) == "Promoted to General (Military), the top of the career" && promo[0].Good);
    var lost = Keep(new LifeEvent() { Kind = LifeEventKind.LostJob, Sim = Bob, Detail = "Team Mascot (Athletics)" });
    Check("out of work, a sad memory: " + lost[0].Describe(Name), lost[0].Kind == MemoryKind.LostJob && !lost[0].Good);
    Check("skills and interactions leave no memory", Keep(new LifeEvent() { Kind = LifeEventKind.SkillUp, Sim = Bob, Value = 3 }).Count == 0
        && Keep(new LifeEvent() { Kind = LifeEventKind.Interaction, Sim = Bob, Other = Bella, Value = 1 }).Count == 0);

    var baby = Keep(new LifeEvent() { Kind = LifeEventKind.BabyArrived, Sim = Bella, Other = Mort, When = new LifeTime(5, 9, 0) });
    Check("a baby, for each parent: " + baby[0].Describe(Name), baby.Count == 2 && baby.Any(m => m.Sim == Mort && m.Other == Bella) && baby.First(m => m.Sim == Bella).Describe(Name) == "Had a baby with Mortimer Goth");
    Check("a baby with one known parent", Keep(new LifeEvent() { Kind = LifeEventKind.BabyArrived, Sim = Bob }).Single().Describe(Name) == "Had a baby");
    Check("a baby of nobody known leaves no memory", Keep(new LifeEvent() { Kind = LifeEventKind.BabyArrived }).Count == 0);
    var born = Keep(new LifeEvent() { Kind = LifeEventKind.ChildFromBaby, Sim = Kid2, Others = new[] { Bella, Mort }, Began = new LifeTime(5, 9, 0) });
    Check("the child remembers being born, when the baby came: " + born[0].Describe(Name), born.Count == 1 && born[0].When.Day == 5 && born[0].Stage == Stage.Child
        && born[0].Describe(Name) == "Born to Bella Goth and Mortimer Goth");
    Check("and only once", Keep(new LifeEvent() { Kind = LifeEventKind.ChildFromBaby, Sim = Kid2 }).Count == 0);

    var grew = Keep(new LifeEvent() { Kind = LifeEventKind.GrewUp, Sim = Cass, Value = (int)Stage.Adult });
    Check("a birthday: " + grew[0].Describe(Name), grew[0].Describe(Name) == "Became an adult" && grew[0].Stage == Stage.Adult);
    Check("and a teen's", Keep(new LifeEvent() { Kind = LifeEventKind.GrewUp, Sim = Kid2, Value = (int)Stage.Teen }).Single().Describe(Name) == "Became a teen");
    Check("a stage is reached once", Keep(new LifeEvent() { Kind = LifeEventKind.GrewUp, Sim = Kid2, Value = (int)Stage.Teen }).Count == 0);

    // a death: the tree says who Mortimer was to each, and the household are family too
    var tree = FamilyTies.Parse("# a comment\nspouse 00003047 000BE11A\nparent 00003047 00000C1D\nparent 000BE11A 00000C1D\nparent 00003047 0000CA55\nnonsense line\nsibling zz 1\n");
    bool Female(uint s) => s == Bella || s == Cass;
    IEnumerable<Remember.Relative> TiesOf(uint s) => tree.RelativesOf(s, Female);
    var mourners = Remember.Mourners(Mort, new[] { Bella, Kid2, Bob }, TiesOf);
    var died = Keep(new LifeEvent() { Kind = LifeEventKind.Died, Sim = Mort, Others = new[] { Bella, Kid2, Bob } }, s => mourners);
    var bellaLoss = died.FirstOrDefault(m => m.Sim == Bella);
    var kidLoss = died.FirstOrDefault(m => m.Sim == Kid2);
    var cassLoss = died.FirstOrDefault(m => m.Sim == Cass);
    var bobLoss = died.FirstOrDefault(m => m.Sim == Bob);
    Check("the Sim who died remembers it", died.Any(m => m.Sim == Mort && m.Kind == MemoryKind.Died));
    Check("his wife: " + bellaLoss?.Describe(Name), bellaLoss?.Describe(Name) == "Their husband, Mortimer Goth, died");
    Check("his son by the tree: " + kidLoss?.Describe(Name), kidLoss?.Describe(Name) == "Their father, Mortimer Goth, died");
    Check("his daughter, who does not live there: " + cassLoss?.Describe(Name), cassLoss?.Describe(Name) == "Their father, Mortimer Goth, died");
    Check("the household, with no tie in the tree: " + bobLoss?.Describe(Name), bobLoss?.Describe(Name) == "Mortimer Goth, of the family, died");
    Check("a death is sad, and remembered once", !bellaLoss.Good && Keep(new LifeEvent() { Kind = LifeEventKind.Died, Sim = Mort }, s => mourners).Count == 0);

    var fire = Keep(new LifeEvent() { Kind = LifeEventKind.FireStarted, Sim = Bob, Others = new[] { Bella, Kid2 }, Detail = "Cook Dinner", Value3 = 1 });
    Check("a fire, for everyone there and who started it", fire.Count == 3 && fire.Single(m => m.Sim == Bob).Describe(Name) == "Started a fire (Cook Dinner)"
        && fire.Single(m => m.Sim == Bella).Describe(Name) == "A fire at home");
    Check("a fire away from home", Keep(new LifeEvent() { Kind = LifeEventKind.FireStarted, Others = new[] { Bella } }).Single().Describe(Name) == "Saw a fire");

    var burgled = Keep(new LifeEvent() { Kind = LifeEventKind.BurglarGone, Sim = 0x666, Others = new[] { Bella, Kid2 }, Detail = "Television\nStereo", Value3 = 1, Began = new LifeTime(6, 2, 10) });
    Check("a burglary, for the household: " + burgled[0].Describe(Name), burgled.Count == 2 && burgled[0].Describe(Name) == "A burglar broke in and took Television, Stereo" && burgled[0].When.Hour == 2);
    Check("with the police", Keep(new LifeEvent() { Kind = LifeEventKind.BurglarGone, Others = new[] { Bella }, Value = 1, Value3 = 1 }).Single().Describe(Name) == "A burglar broke in. The police came");
    Check("but not on another lot", Keep(new LifeEvent() { Kind = LifeEventKind.BurglarGone, Others = new[] { Bella }, Value3 = 0 }).Count == 0);

    var bellas = j.Of(Bella);
    Check("a Sim's timeline is in order", bellas.Zip(bellas.Skip(1), (a, b) => a.When.CompareTo(b.When) < 0 || (a.When.CompareTo(b.When) == 0 && a.Id < b.Id)).All(x => x));
    Check("and the journal knows what they have had", j.Has(Bella, MemoryKind.FirstKiss) && !j.Has(Cass, MemoryKind.FirstKiss));

    // ---- the file ----
    j.Day = 9;
    j.Babies.Add(new WaitingBaby() { House = 5, ObjectId = 40, ObjectGuid = 0xABCDEF01, ParentA = Bella, ParentB = Mort, Arrived = new LifeTime(9, 3, 0) });
    var text = j.Write();
    Check("Sims are written in hexadecimal, as the Family Tree writes them", text.Contains("\"000BE11A\"") && text.Contains("\"FirstKiss\"") && !text.Contains("\"Minutes\""));
    var back = Journal.Read(text, out var skipped);
    Check("the journal reads back as it was written", skipped == 0 && back.Day == 9 && back.Memories.Count == j.Memories.Count && back.Write() == text
        && back.Babies.Single().ParentB == Mort && back.Babies.Single().Arrived.Hour == 3);
    Check("and numbers new memories after the last", back.NextId == j.NextId);
    var damaged = Journal.Read("{\"Day\": 0, \"Memories\": [null, {\"Kind\": \"FirstKiss\", \"Sim\": \"\"}, {\"Id\": 7, \"Kind\": \"Married\", \"Sim\": \"0000ABCD\", \"Other\": 4660}]}", out skipped);
    Check("a damaged file keeps what it can", skipped == 2 && damaged.Memories.Single().Other == 0x1234 && damaged.Day == 1 && damaged.NextId == 8);
}

// ---- the family tree's file ----
{
    var tree = FamilyTies.Parse("spouse A B\nparent A C\nparent B C\nparent A D\nsibling C E\nparent F A\nparent C 1A\n");
    bool Female(uint s) => s == 0xB || s == 0xD;
    string Words(uint s) => string.Join(",", tree.RelativesOf(s, Female).OrderBy(r => r.Sim).Select(r => r.Sim.ToString("X") + "=" + r.Word));
    Check("A's family: " + Words(0xA), Words(0xA) == "B=wife,C=son,D=daughter,F=father,1A=grandson");
    Check("C's family: " + Words(0xC), Words(0xC) == "A=father,B=mother,D=sister,E=brother,F=grandfather,1A=son");
    Check("a missing file is an empty tree", FamilyTies.ReadFrom(Path.Combine(Path.GetTempPath(), "no-such-neighbourhood-" + Guid.NewGuid())).Count == 0);
}

// ---- the book: the journal of the loaded neighbourhood, kept when the game saves ----
{
    var temp = Path.Combine(Path.GetTempPath(), "memories-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(temp, "UserData"));
    FSO.Common.FSOEnvironment.UserDir = temp;
    MemoryBook.NeighbourhoodPath = () => Path.Combine(temp, "UserData");
    MemoryBook.StageOf = StageOf;
    MemoryBook.IsFemale = s => s == Bella;
    File.WriteAllText(Path.Combine(temp, "UserData", FamilyTies.FileName), "spouse 00003047 000BE11A\n");
    MemoryBook.Forget();

    var heard = new List<Memory>();
    MemoryBook.Remembered += heard.Add;
    MemoryBook.Hear(new LifeEvent() { Kind = LifeEventKind.Kiss, Sim = Bob, Other = Bella, When = at });
    MemoryBook.Hear(new LifeEvent() { Kind = LifeEventKind.Died, Sim = Mort, Others = new[] { Bob }, When = at });
    Check("the book remembers, and says so", MemoryBook.Of(Bob).Count == 2 && heard.Count == 5);
    Check("a death in the book reads the family tree: " + MemoryBook.Of(Bella).Last().Describe(Name), MemoryBook.Of(Bella).Last().Describe(Name) == "Their husband, Mortimer Goth, died");
    MemoryBook.Journal.Day = 3;
    MemoryBook.Saved();
    var file = Journal.PathFor(MemoryBook.PackId, Path.Combine(temp, "UserData"));
    Check("saving writes the pack's own file, beside the saves", File.Exists(file) && file.StartsWith(Path.Combine(temp, "LocalHouse", "PackData", "sample.memories")));
    MemoryBook.Hear(new LifeEvent() { Kind = LifeEventKind.GrewUp, Sim = Cass, Value = (int)Stage.Adult, When = at });
    MemoryBook.Forget(); //the lot was left without saving
    Check("what was not saved is forgotten with the lot", MemoryBook.Of(Cass).Count == 0 && MemoryBook.Of(Bob).Count == 2 && MemoryBook.Journal.Day == 3);
    MemoryBook.NeighbourhoodPath = () => null;
    Check("with no neighbourhood there is nothing, and no harm", MemoryBook.Of(Bob).Count == 0 && MemoryBook.Journal == null);
    MemoryBook.Hear(new LifeEvent() { Kind = LifeEventKind.Kiss, Sim = Bob, Other = Bella });
    MemoryBook.Remembered -= heard.Add;
    try { Directory.Delete(temp, true); } catch { }
}

Console.WriteLine(failed == 0 ? "All checks passed." : failed + " check(s) FAILED.");
return failed == 0 ? 0 : 1;
