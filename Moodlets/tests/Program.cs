/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Moodlets pack that need no copy of The Sims: that its patches fit the engine it is built against,
// that the engine still works out mood where the pack expects it to, and the pack's own rules (giving moodlets, time
// passing, the cap, what is remembered, the tuning file, and what the panel shows).
// What they cannot check is the game itself: nothing here loads a lot or a Sim.
// Run with: dotnet run --project Moodlets/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using HarmonyLib;
using Moodlets;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

// ---- the patches fit this engine ----
{
    var h = new Harmony("test.moodlets");
    var apply = typeof(MoodletsPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    try { apply.Invoke(null, new object[] { h }); }
    catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
    var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    //the optional ones too: they are optional to the pack, but should fit the Simitone it is built against
    foreach (var needed in new[] { "VMTS1MotiveDecay.Tick", "VM.Tick", "TS1NeighborhoodProvider.InitSpecific", "TS1GameScreen.Save",
        "TS1GameScreen.ExitLot", "UIMainPanel.SetSubpanel", "VMGenericTS1Call.Execute", "VMCreateObjectInstance.Execute", "VMContext.CreateObjectInstance", "VMBurn.Execute" })
        Check("patched " + needed, patched.Contains(needed));
    h.UnpatchAll(h.Id);
}

// ---- the engine works out mood where the pack expects ----
// The pack adds moodlets after VMTS1MotiveDecay.Tick, and knows the game counted the needs that tick because
// LastMinute changed. If the engine stops setting mood there, or stops keeping LastMinute, the pack must change.
{
    var tick = AccessTools.Method(typeof(VMTS1MotiveDecay), "Tick", new[] { typeof(VMAvatar), typeof(VMContext) });
    var code = PatchProcessor.GetOriginalInstructions(tick);
    var setsLastMinute = code.Any(i => i.opcode == OpCodes.Stfld && (i.operand as FieldInfo)?.Name == "LastMinute");
    var setMotive = AccessTools.Method(typeof(VMAvatar), "SetMotiveData");
    var calls = code.Select((i, n) => (i, n)).Where(x => (x.i.opcode == OpCodes.Call || x.i.opcode == OpCodes.Callvirt) && Equals(x.i.operand, setMotive)).ToList();
    //mood is motive 3, set to the sum of the needs divided by 8
    var setsMood = calls.Any(c => { var before = code.Take(c.n).Reverse().Take(8).ToList(); return before.Any(i => i.opcode == OpCodes.Ldc_I4_3) && before.Any(i => i.opcode == OpCodes.Div); });
    Check("the game still counts the needs in two-minute steps, keeping LastMinute", setsLastMinute);
    Check("the game still sets mood in VMTS1MotiveDecay.Tick", setsMood);
    Check("the game's mood is still motive 3", (int)VMMotive.Mood == 3);
    var sims = typeof(VMAvatar).Assembly.GetTypes().Where(t => typeof(VMIMotiveDecay).IsAssignableFrom(t) && !t.IsInterface).Select(t => t.Name).OrderBy(x => x).ToList();
    Check("the game has the two ways of counting needs the pack knows (" + string.Join(", ", sims) + ")", sims.SequenceEqual(new[] { "VMAvatarMotiveDecay", "VMTS1MotiveDecay" }));
}

// the per-tick hook must be harmless on a lot it knows nothing about
Moods.AfterTick(null);
Check("nobody has moodlets on no lot", Moods.Household(null).Count == 0);

// ---- the pack must run on every Simitone that can load code packs, not only the newest ----
{
    var allowed = new HashSet<string>() { "ISimitonePack", "Pack", "PackManager" };
    using var file = File.OpenRead(typeof(MoodletsPack).Assembly.Location);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var used = md.TypeReferences.Select(t => md.GetTypeReference(t))
        .Where(t => md.GetString(t.Namespace) == "Simitone.Client.Packs").Select(t => md.GetString(t.Name)).Distinct().ToList();
    Check("uses only the first pack API (" + string.Join(", ", used) + ")", used.All(allowed.Contains));
    Check("implements none of the later pack calls", typeof(MoodletsPack).GetMethod("OnLotSaved") == null && !typeof(MoodletsPack).GetMethods().Any(m => m.Name == "OnLotLoaded"));
}

var tuning = Tuning.Defaults();
var packFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

// ---- giving moodlets ----
{
    var list = new List<Moodlet>();
    Check("a moodlet is given for its hours", Rules.Give(list, tuning.Find(Tuning.Promoted), tuning, "Now: Desk Sergeant.") == Rules.Given.New && list.Single().Minutes == 24 * 60 && list[0].Note == "Now: Desk Sergeant.");
    Rules.Pass(list, 600);
    Check("given again, it starts again with the newer note", Rules.Give(list, tuning.Find(Tuning.Promoted), tuning, "Now: Captain.") == Rules.Given.Renewed && list.Single().Minutes == 24 * 60 && list[0].Note == "Now: Captain.");
    Check("one of the same group takes its place: fired after a promotion", Rules.Give(list, tuning.Find(Tuning.Fired), tuning) == Rules.Given.Replaced && list.Single().Id == Tuning.Fired);
    Check("a new job takes the place of being fired", Rules.Give(list, tuning.Find(Tuning.NewJob), tuning) == Rules.Given.Replaced && list.Single().Id == Tuning.NewJob);
    Rules.Give(list, tuning.Find(Tuning.Fire), tuning);
    Rules.Give(list, tuning.Find(Tuning.BurntFood), tuning);
    Check("moodlets of no group stand together", list.Select(x => x.Id).OrderBy(x => x).SequenceEqual(new[] { Tuning.BurntFood, Tuning.Fire, Tuning.NewJob }));
    Check("nothing is given of a kind not known", Rules.Give(list, null, tuning) == Rules.Given.Unknown && list.Count == 3);

    var ended = Rules.Pass(list, 4 * 60);
    Check("time runs out: Burnt Food after its 4 hours", ended.Select(x => x.Id).SequenceEqual(new[] { Tuning.BurntFood }) && list.Count == 2);
    Check("and the others have that much less", list.First(x => x.Id == Tuning.Fire).Minutes == 2 * 60 && list.First(x => x.Id == Tuning.NewJob).Minutes == 8 * 60);
    Check("no time, or time backwards, is nothing", Rules.Pass(list, 0).Count == 0 && Rules.Pass(list, -50).Count == 0 && list.First(x => x.Id == Tuning.Fire).Minutes == 120);
    Rules.Pass(list, 100000);
    Check("everything runs out in the end", list.Count == 0);
}

// ---- what moodlets do to mood ----
{
    var list = new List<Moodlet>();
    Check("no moodlets, no change", Rules.Total(list, tuning) == 0 && Rules.Adjust(37, 0) == 37);
    Rules.Give(list, tuning.Find(Tuning.Promoted), tuning);
    Check("one moodlet adds its mood", Rules.Total(list, tuning) == 12 && Rules.Adjust(30, Rules.Total(list, tuning)) == 42);
    Rules.Give(list, tuning.Find(Tuning.Fire), tuning);
    Check("a boost and a penalty add up", Rules.Total(list, tuning) == 0);
    var happy = new List<Moodlet>();
    foreach (var id in new[] { Tuning.JustMarried, Tuning.NewBaby, Tuning.Promoted }) Rules.Give(happy, tuning.Find(id), tuning);
    Check("together they are held to the cap: +47 is +" + tuning.Cap, Rules.Sum(happy, tuning) == 47 && Rules.Total(happy, tuning) == tuning.Cap);
    var sad = new List<Moodlet>();
    foreach (var id in new[] { Tuning.LostFamily, Tuning.Fired, Tuning.Fire, Tuning.BurntFood }) Rules.Give(sad, tuning.Find(id), tuning);
    Check("and the other way: -55 is -" + tuning.Cap, Rules.Sum(sad, tuning) == -55 && Rules.Total(sad, tuning) == -tuning.Cap);
    Check("mood stays on the game's scale", Rules.Adjust(95, 25) == 100 && Rules.Adjust(-90, -25) == -100 && Rules.Adjust(-100, 25) == -75);
    var unknown = new List<Moodlet>() { new Moodlet() { Id = "gone_from_the_file", Minutes = 60 } };
    Check("a moodlet of a kind no longer known adds nothing", Rules.Total(unknown, tuning) == 0);
    var noCap = Tuning.Defaults(); noCap.Cap = 0;
    Check("a cap of 0 is moodlets that do nothing", Rules.Total(happy, noCap) == 0);
    Check("strongest first, the good before the bad", Rules.Ordered(sad.Concat(new[] { new Moodlet() { Id = Tuning.JustMarried, Minutes = 5 } }), tuning).Select(x => x.Id)
        .SequenceEqual(new[] { Tuning.JustMarried, Tuning.LostFamily, Tuning.Fired, Tuning.Fire, Tuning.BurntFood }));
}

// ---- time ----
{
    Check("time left, rounded up", Rules.TimeLeft(1) == "1 minute left" && Rules.TimeLeft(40) == "40 minutes left" && Rules.TimeLeft(60) == "1 hour left"
        && Rules.TimeLeft(61) == "2 hours left" && Rules.TimeLeft(48 * 60) == "48 hours left" && Rules.TimeLeft(48 * 60 + 1) == "3 days left" && Rules.TimeLeft(0) == "1 minute left");
    Check("and short", Rules.ShortTime(40) == "40m" && Rules.ShortTime(14 * 60) == "14h" && Rules.ShortTime(72 * 60) == "3d");
    int[] Feed(params (int h, int m)[] times) { var w = new ClockWatch(); return times.Select(t => w.Feed(t.h, t.m)).ToArray(); }
    Check("minutes passing are counted", Feed((10, 0), (10, 1), (10, 1), (10, 3)).SequenceEqual(new[] { 0, 1, 0, 2 }));
    Check("over midnight too", Feed((23, 59), (0, 0), (0, 1)).SequenceEqual(new[] { 0, 1, 1 }));
    Check("a lot that has just loaded has not lived any time", Feed((14, 30))[0] == 0);
    Check("a clock that jumps has not lived the time", Feed((14, 0), (20, 0))[1] == 0 && Feed((9, 0), (8, 59))[1] == 0);
}

// ---- jobs ----
{
    Check("a level up in the same job is a promotion", Jobs.Between(3, 2, 3, 3) == Jobs.Change.Promoted);
    Check("a level down is a demotion", Jobs.Between(3, 5, 3, 4) == Jobs.Change.Demoted);
    Check("a job where there was none is a new job", Jobs.Between(0, 0, 4, 1) == Jobs.Change.NewJob);
    Check("another job is a new job, not a demotion", Jobs.Between(3, 7, 5, 1) == Jobs.Change.NewJob);
    Check("no job where there was one is being fired", Jobs.Between(3, 7, 0, 0) == Jobs.Change.Fired);
    Check("nothing changing is nothing", Jobs.Between(3, 2, 3, 2) == Jobs.Change.None && Jobs.Between(0, 0, 0, 0) == Jobs.Change.None && Jobs.Between(0, 1, 0, 3) == Jobs.Change.None);
    Check("each change has its moodlet", new[] { Jobs.Change.Promoted, Jobs.Change.Demoted, Jobs.Change.NewJob, Jobs.Change.Fired }.All(c => tuning.Find(Jobs.MoodletFor(c)) != null) && Jobs.MoodletFor(Jobs.Change.None) == null);
    Check("the job moodlets are one group", new[] { Tuning.Promoted, Tuning.Demoted, Tuning.NewJob, Tuning.Fired }.All(id => tuning.Find(id).Group == "job"));
    Check("a proposal, as the English game names it", Words.LooksLikeProposal("Propose") && Words.LooksLikeProposal("Ask to Marry") && !Words.LooksLikeProposal("Kiss") && !Words.LooksLikeProposal(null));
}

// ---- the tuning file ----
{
    var dir = Path.Combine(Path.GetTempPath(), "moodlet-tuning-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var shippedPath = Path.Combine(packFolder, "moodlets.json");
    var shipped = Tuning.Read(shippedPath, out var problem);
    Check("the pack's moodlets.json is there and reads (" + shippedPath + ")", File.Exists(shippedPath) && problem == null);
    Check("and says what the pack's own values say", shipped.Cap == tuning.Cap && shipped.Moodlets.Count == tuning.Moodlets.Count && tuning.Moodlets.Values.All(k =>
    {
        var s = shipped.Find(k.Id);
        return s != null && s.Name == k.Name && s.Text == k.Text && s.Mood == k.Mood && s.Hours == k.Hours && s.Group == k.Group;
    }));
    Check("every kind is known by its id, and the triggers' kinds are all there", new[] { Tuning.JustMarried, Tuning.NewBaby, Tuning.Promoted, Tuning.NewJob, Tuning.Demoted, Tuning.Fired, Tuning.BurntFood, Tuning.Fire, Tuning.LostFamily }.All(id => tuning.Find(id)?.Id == id));
    Check("a missing file is the pack's own values", Tuning.Read(Path.Combine(dir, "none.json"), out problem).Find(Tuning.Promoted).Mood == 12 && problem == null);

    var edited = Path.Combine(dir, "edited.json");
    File.WriteAllText(edited, "{ \"cap\": 400, // too much\n \"moodlets\": { \"Promoted\": { \"mood\": 30, \"hours\": 0 }, \"rained_on\": { \"name\": \"Rained On\", \"mood\": -5, \"hours\": 2 }, \"fired\": { \"name\": \"Let Go\", \"mood\": -9000, \"hours\": 9000 }, }, }");
    var read = Tuning.Read(edited, out problem);
    Check("an edited file is read, comments and all", problem == null);
    Check("what it changes is changed, held within reason", read.Cap == Tuning.MostMood && read.Find(Tuning.Promoted).Mood == 30 && read.Find(Tuning.Promoted).Hours == 1
        && read.Find(Tuning.Fired).Name == "Let Go" && read.Find(Tuning.Fired).Mood == -Tuning.MostMood && read.Find(Tuning.Fired).Hours == Tuning.LongestHours);
    Check("a kind it leaves out is as the pack has it", read.Find(Tuning.JustMarried).Mood == 20 && read.Find(Tuning.JustMarried).Name == "Just Married");
    Check("a kind of its own is kept, for the console", read.Find("rained_on")?.Name == "Rained On" && read.Find("rained_on").Id == "rained_on");
    Check("a kind named but not described keeps its id as a name", read.Find(Tuning.Promoted).Name == "promoted");

    var broken = Path.Combine(dir, "broken.json");
    File.WriteAllText(broken, "{ \"cap\": ");
    var fallback = Tuning.Read(broken, out problem);
    Check("a broken file is the pack's own values, and says so", problem != null && fallback.Find(Tuning.Promoted).Mood == 12 && fallback.Cap == Tuning.DefaultCap);

    var written = Path.Combine(dir, "written.json");
    var tuned = Tuning.Defaults();
    tuned.Find(Tuning.Fire).Mood = -20; tuned.Cap = 30;
    Check("tuning is written", tuned.Write(written, out problem));
    var back = Tuning.Read(written, out problem);
    Check("and read back the same", problem == null && back.Find(Tuning.Fire).Mood == -20 && back.Cap == 30 && back.Moodlets.Count == tuned.Moodlets.Count && File.ReadAllText(written).Contains("\"cap\": 30"));

    Check("a kind can be typed by its id, its name, or the start of either", tuning.Lookup("burnt_food")?.Id == Tuning.BurntFood && tuning.Lookup("Burnt Food")?.Id == Tuning.BurntFood
        && tuning.Lookup("burnt")?.Id == Tuning.BurntFood && tuning.Lookup("FIRE!")?.Id == Tuning.Fire && tuning.Lookup("lost")?.Id == Tuning.LostFamily);
    Check("but not when the start fits more than one", tuning.Lookup("new") == null && tuning.Lookup("") == null && tuning.Lookup("zebra") == null);
}

// ---- what is remembered ----
{
    FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "moodlet-test-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);
    var hood = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
    var other = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData2/");

    var fresh = Ledger.Read("sample.moodlets", hood);
    Check("no file: nobody has moodlets, and they are on", fresh.Sims.Count == 0 && fresh.Settings.On);
    Rules.Give(fresh.Of(0x1234abcd), tuning.Find(Tuning.Promoted), tuning, "Now: Desk Sergeant.");
    fresh.Of(7); //a Sim looked at who has none
    Check("written", fresh.Write("sample.moodlets", hood));
    var again = Ledger.Read("sample.moodlets", hood);
    Check("read back, without the Sims who have none", again.Sims.Count == 1 && again.Find(0x1234abcd).Single().Id == Tuning.Promoted && again.Find(0x1234abcd)[0].Note == "Now: Desk Sergeant." && again.Find(7).Count == 0);
    Check("another neighbourhood has its own", Ledger.Read("sample.moodlets", other).Sims.Count == 0 && Ledger.PathFor("sample.moodlets", hood) != Ledger.PathFor("sample.moodlets", other));
    Check("the file is beside the saves, not in them", Ledger.PathFor("sample.moodlets", hood).Replace('\\', '/').Contains("/LocalHouse/PackData/sample.moodlets/"));

    File.WriteAllText(Ledger.PathFor("sample.moodlets", other), "{ \"Sims\": { \"00000001\": [ { \"Id\": \"fire\", \"Minutes\": -5 }, null, { \"Id\": \"\", \"Minutes\": 60 }, { \"Id\": \"fire\", \"Minutes\": 30, \"Note\": null } ], \"00000002\": [] } }");
    var mended = Ledger.Read("sample.moodlets", other);
    Check("a hand-edited file loses what makes no sense", mended.Sims.Count == 1 && mended.Find(1).Single().Minutes == 30 && mended.Find(1)[0].Note == "" && mended.Settings.On);
    File.WriteAllText(Ledger.PathFor("sample.moodlets", other), "not json");
    Check("a broken file starts afresh", Ledger.Read("sample.moodlets", other).Sims.Count == 0);

    // the game's side of it, with the neighbourhood the tests make
    Moods.NeighbourhoodPath = () => hood;
    Moods.Tuning = Tuning.Defaults();
    Moods.Forget();
    Check("the record of the neighbourhood is read", Moods.Ledger.Find(0x1234abcd).Count == 1);
    Check("a Sim is given a moodlet by their GUID", Moods.Give(0x55, Tuning.Fire, "", "Bella") && Moods.Ledger.Find(0x55).Single().Id == Tuning.Fire);
    Check("but not one there is no such kind of", !Moods.Give(0x55, "zebra", "", "Bella"));
    Moods.Forget();
    Check("leaving the lot without saving forgets what came since", Moods.Ledger.Find(0x55).Count == 0 && Moods.Ledger.Find(0x1234abcd).Count == 1);
    Moods.Give(0x55, Tuning.Fire, "", "Bella");
    Moods.Saved();
    Moods.Forget();
    Check("saving keeps it", Moods.Ledger.Find(0x55).Single().Id == Tuning.Fire);

    Moods.Ledger.Settings.On = false;
    Moods.SettingsChanged();
    Check("while off, none are given", !Moods.Give(0x56, Tuning.Fire, "", "Bob") && Moods.Ledger.Find(0x56).Count == 0);
    Moods.Give(0x77, Tuning.Fire, "", "Unsaved"); //refused, but if it were not it would not be kept
    Moods.Forget();
    Check("a setting is kept at once, without what was not saved", !Moods.Ledger.Settings.On && Moods.Ledger.Find(0x55).Count == 1);
    Moods.Ledger.Settings.On = true;
    Moods.SettingsChanged();

    // ---- the console, as far as it goes without a lot ----
    bool understood;
    Check("help", MoodletsPack.Answer(new[] { "help" }, out understood).Contains("moodlet give") && understood);
    Check("kinds lists them all, and the cap", MoodletsPack.Answer(new[] { "kinds" }, out understood).Split('\n').Length == tuning.Moodlets.Count + 1 && MoodletsPack.Kinds(tuning).Contains("promoted: Promoted, +12 for 24 hours (job)"));
    Check("what needs a lot says so", MoodletsPack.Answer(new[] { "give", "fire" }, out understood) == "This works while you are in a lot." && MoodletsPack.Answer(Array.Empty<string>(), out understood) == "This works while you are in a lot.");
    var tunedAnswer = MoodletsPack.Answer(new[] { "tune", "promoted", "15", "36" }, out understood);
    Check("a kind is tuned: " + tunedAnswer.Split('\n')[0], Moods.Tuning.Find(Tuning.Promoted).Mood == 15 && Moods.Tuning.Find(Tuning.Promoted).Hours == 36 && tunedAnswer.StartsWith("Promoted is now +15 for 36 hours."));
    Check("within reason", MoodletsPack.Answer(new[] { "tune", "promoted", "500" }, out understood).StartsWith("Mood is from") && Moods.Tuning.Find(Tuning.Promoted).Mood == 15);
    Check("the cap is changed", MoodletsPack.Answer(new[] { "cap", "30" }, out understood).StartsWith("All of a Sim's moodlets together now add at most +30") && Moods.Tuning.Cap == 30);
    Check("off and on", MoodletsPack.Answer(new[] { "off" }, out understood).StartsWith("Moodlets are off") && !Moods.On && MoodletsPack.Answer(new[] { "on" }, out understood) == "Moodlets are on." && Moods.On);
    Check("what is not a command is not understood", MoodletsPack.Answer(new[] { "zebra" }, out understood) == null && !understood);
    Moods.Tuning = Tuning.Defaults();

    var bella = new List<Moodlet>();
    Rules.Give(bella, tuning.Find(Tuning.Promoted), tuning);
    Rules.Give(bella, tuning.Find(Tuning.Fire), tuning);
    Rules.Pass(bella, 60);
    Check("a Sim's moodlets in a line: " + MoodletsPack.Describe("Bella", bella, tuning), MoodletsPack.Describe("Bella", bella, tuning) == "Bella: Promoted +12 (23h), Fire! -12 (5h). 0 to mood.");
    var many = new List<Moodlet>();
    foreach (var id in new[] { Tuning.JustMarried, Tuning.NewBaby }) Rules.Give(many, tuning.Find(id), tuning);
    Check("and held to the cap", MoodletsPack.Describe("Bob", many, tuning).EndsWith("+25 to mood (+35, held to the cap).") && MoodletsPack.Describe("Ann", new List<Moodlet>(), tuning) == "Ann: no moodlets.");
    Moods.NeighbourhoodPath = () => null;
    Moods.Forget();
    Check("no neighbourhood: the console says so", MoodletsPack.Answer(new[] { "kinds" }, out understood) == "There is no neighbourhood loaded.");
}

// ---- what the panel shows ----
List<Moodlet> lots9() => tuning.Moodlets.Keys.Take(9).Select((id, i) => new Moodlet() { Id = id, Minutes = 60 + i }).ToList();
{
    var bella = new List<Moodlet>();
    foreach (var id in new[] { Tuning.Promoted, Tuning.Fire, Tuning.BurntFood }) Rules.Give(bella, tuning.Find(id), tuning);
    Rules.Pass(bella, 60);

    Check("the room beside the needs: none on a 1024 desktop, some on 1280, plenty on 1920",
        MoodletView.RoomIn(1024 - 442) == 0 && MoodletView.RoomIn(1280 - 442) == 99 && MoodletView.RoomIn(1920 - 442) == 739);
    Check("one wide column on 1366, two on 1600", MoodletView.For(bella, tuning, true, MoodletView.RoomIn(1366 - 442)).Chips.All(c => c.X == 0 && c.Width == 185)
        && MoodletView.For(lots9(), tuning, true, MoodletView.RoomIn(1600 - 442)).Chips.Select(c => c.X).Distinct().Count() == 2);
    Check("the layout fits the room", MoodletView.FitFor(0) == MoodletView.Layout.None && MoodletView.FitFor(39) == MoodletView.Layout.None
        && MoodletView.FitFor(99) == MoodletView.Layout.Narrow && MoodletView.FitFor(150) == MoodletView.Layout.Wide);

    var wide = MoodletView.For(bella, tuning, true, 739);
    Check("wide: a chip for each, strongest first, down a column", wide.Fit == MoodletView.Layout.Wide && wide.Chips.Count == 3
        && wide.Chips.Select(c => c.Name).SequenceEqual(new[] { "Promoted", "Fire!", "Burnt Food" }) && wide.Chips.All(c => c.X == 0) && wide.Chips.Select(c => c.Y).SequenceEqual(new[] { 36, 66, 96 }));
    Check("each with its mood, colour and time", wide.Chips[0].Mood == "+12" && wide.Chips[0].Good && wide.Chips[1].Mood == "-12" && !wide.Chips[1].Good && wide.Chips[2].Time == "3h"
        && Math.Abs(wide.Chips[2].Left - 0.75f) < 0.001f);
    Check("four columns fit in 739", wide.Chips[0].Width == (739 - 3 * MoodletView.Gap) / 4 && wide.Chips.All(c => c.X + c.Width <= 739));
    Check("the title says what they add", wide.Title == "Moodlets" && wide.Total == "-8 to mood" && !wide.TotalGood && wide.Empty == "");
    Check("the details: " + wide.Chips[0].Details.Replace("\n", " | "), wide.Chips[0].Details == "Promoted (+12 mood)\nMoved up at work.\n23 hours left");

    var narrow = MoodletView.For(bella, tuning, true, 99);
    Check("narrow: just the mood, and two columns of them in 99", narrow.Fit == MoodletView.Layout.Narrow && narrow.Chips.All(c => c.Width == MoodletView.PillWidth) && narrow.Total == "-8" && narrow.Title == "Moodlets");

    var lots = new List<Moodlet>();
    for (int i = 0; i < 9; i++) lots.Add(new Moodlet() { Id = tuning.Moodlets.Keys.ElementAt(i), Minutes = 60 + i });
    var full = MoodletView.For(lots, tuning, true, 99);
    var last = full.Chips.Last();
    Check("more than fit: the last place says how many more", full.Chips.Count == 6 && last.More && last.Mood == "+4" && last.Details.Split('\n').Length == 4 && last.X == 44 && last.Y == 96);
    Check("in a single wide column", MoodletView.For(lots, tuning, true, 200).Chips.Count == 3 && MoodletView.For(lots, tuning, true, 200).Chips.All(c => c.X == 0 && c.Width == 200));
    Check("and the second column starts beside the first", MoodletView.For(lots, tuning, true, 320).Chips.Where(c => c.X > 0).All(c => c.X == 156 + MoodletView.Gap));

    Check("no room: nothing but the console", MoodletView.For(bella, tuning, true, 20).Chips.Count == 0 && MoodletView.For(bella, tuning, true, 20).Title == "");
    Check("no moodlets: says none", MoodletView.For(new List<Moodlet>(), tuning, true, 300).Empty == "None" && MoodletView.For(new List<Moodlet>(), tuning, true, 300).Total == "");
    var off = MoodletView.For(bella, tuning, false, 300);
    Check("off: shown, adding nothing", off.Title == "Moodlets are off" && off.Total == "" && off.Chips.Count == 3 && off.Chips[0].Details.EndsWith("once moodlets are on again"));
    Check("for someone else's Sim", MoodletView.NotOurs(300).Empty == "Only for your household" && MoodletView.NotOurs(99).Empty == "");
    var later = MoodletView.For(bella.Select(x => new Moodlet() { Id = x.Id, Minutes = x.Minutes - 120, Note = x.Note }), tuning, true, 739);
    Check("but keeps its chips while only the time moves, so a tooltip stays", later.Shape == wide.Shape && later.Key != wide.Key);
    Check("and makes them again when a moodlet comes or goes", MoodletView.For(bella.Take(2), tuning, true, 739).Shape != wide.Shape);
    Check("the panel is drawn again when the time left moves", MoodletView.For(bella, tuning, true, 739).Key == wide.Key && MoodletView.For(bella.Select(x => new Moodlet() { Id = x.Id, Minutes = x.Minutes - 120, Note = x.Note }), tuning, true, 739).Key != wide.Key);
}

Console.WriteLine(failed == 0 ? "All passed." : failed + " failed.");
return failed == 0 ? 0 : 1;
