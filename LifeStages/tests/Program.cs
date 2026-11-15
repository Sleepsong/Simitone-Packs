/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Life Stages pack that need no copy of The Sims: that its patches fit the engine it is built against,
// and its own rules (the stages, the clock, what is remembered, the body a child grows into, grey hair).
// What they cannot check is the game itself: nothing here loads a lot or a Sim.
// Run with: dotnet run --project LifeStages/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LifeStages;
using Microsoft.Xna.Framework;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

// ---- the patches fit this engine ----
var h = new Harmony("test.lifestages");
var apply = typeof(LifeStagesPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
try { apply.Invoke(null, new object[] { h }); }
catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
foreach (var needed in new[] { "VM.Tick", "TS1NeighborhoodProvider.InitSpecific", "TS1GameScreen.Save", "TS1GameScreen.ExitLot", "UIMainPanel.SetSubpanel" })
    Check("patched " + needed, patched.Contains(needed));
// the Create a Sim patches are optional to the pack, but should fit the Simitone this is built against
{
    var cas = new Harmony("test.lifestages.cas");
    try { CreateASim.Apply(cas); Check("the Create a Sim patches fit", true); }
    catch (Exception e) { Check("the Create a Sim patches fit: " + e.Message, false); }
    var casPatched = cas.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    foreach (var needed in new[] { "UISimCASPanel..ctor", "UISimCASPanel.UpdateType", "UISimCASPanel.SetTab", "TS1CASScreen.BuildMember", "TS1CASScreen.PrepareEdit", "TS1CASScreen.PopulateSimType", "TS1CASScreen.Update", "TS1CASScreen.SetFamilyMember", "TS1CASScreen.CASToNeighGen", "SimitoneNeighbourGenerator.CreateFamily" })
        Check("patched " + needed, casPatched.Contains(needed));
}
// the per-tick hook must be harmless on a lot it knows nothing about
Life.AfterTick(null);
Check("nobody lives on no lot", Life.Household(null).Count == 0 && !Life.AtHome(null));

// ---- the pack must run on every Simitone that can load code packs, not only the newest ----
// From Simitone's pack code it may use only what was there from the start (see the Apartments pack's tests).
{
    var allowed = new HashSet<string>() { "ISimitonePack", "Pack", "PackManager" };
    using var file = File.OpenRead(typeof(LifeStagesPack).Assembly.Location);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var used = md.TypeReferences.Select(t => md.GetTypeReference(t))
        .Where(t => md.GetString(t.Namespace) == "Simitone.Client.Packs").Select(t => md.GetString(t.Name)).Distinct().ToList();
    Check("uses only the first pack API (" + string.Join(", ", used) + ")", used.All(allowed.Contains));
    Check("implements none of the later pack calls", typeof(LifeStagesPack).GetMethod("OnLotSaved") == null && !typeof(LifeStagesPack).GetMethods().Any(m => m.Name == "OnLotLoaded"));
}

// ---- the stages ----
{
    var s = new Settings();
    var sim = new SimAge() { Stage = Stage.Child };
    var seen = new List<string>();
    for (int day = 0; day < 200; day++)
    {
        Stages.AddDay(sim, s);
        if (Stages.Due(sim, s)) { Stages.Advance(sim, s); seen.Add(Stages.Name(sim.Stage) + "@" + (day + 1)); }
    }
    Check("a life: " + string.Join(", ", seen), string.Join(",", seen) == "teen@8,adult@23,elder@52");
    Check("an elder stays an elder, and the days go on", sim.Stage == Stage.Elder && sim.Days == 148 && !Stages.Advance(sim, s));

    s.Teens = false;
    sim = new SimAge() { Stage = Stage.Child, Days = 8 };
    Check("without teens a child is due after its 8 days", Stages.Due(sim, s) && Stages.Advance(sim, s) && sim.Stage == Stage.Adult && sim.Days == 0);
    var teen = new SimAge() { Stage = Stage.Teen, Days = 3 };
    Check("a teen from before stays a teen until grown", Stages.Next(teen.Stage, s) == Stage.Adult && !Stages.Due(teen, s));
    s.Teens = true;

    var waiting = new SimAge() { Stage = Stage.Child, Days = 8 };
    Stages.AddDay(waiting, s); Stages.AddDay(waiting, s);
    Check("a Sim waiting to grow up runs up no debt", waiting.Days == 8);

    Check("the game sees a child and a teen as children", Stages.IsChildToGame(Stage.Child) && Stages.IsChildToGame(Stage.Teen));
    Check("the game sees an adult and an elder as adults", !Stages.IsChildToGame(Stage.Adult) && !Stages.IsChildToGame(Stage.Elder));
    Check("only a teen is drawn at another size", Stages.Size(Stage.Teen) != 100 && new[] { Stage.Child, Stage.Adult, Stage.Elder }.All(x => Stages.Size(x) == 100));
    foreach (Stage stage in Enum.GetValues(typeof(Stage)))
        Check("the age given to a " + Stages.Name(stage) + " reads back as one", Stages.FromGameAge(Stages.GameAge(stage)) == stage && Stages.TryParse(Stages.Name(stage), out var back) && back == stage);
    Check("the game's own ages are left alone", Stages.AgeFits(Stage.Child, 7) && Stages.AgeFits(Stage.Adult, 35) && Stages.AgeFits(Stage.Adult, 0) && !Stages.AgeFits(Stage.Adult, 9) && !Stages.AgeFits(Stage.Elder, 27));
    Check("describing", Stages.Describe(new SimAge() { Stage = Stage.Teen, Days = 2 }, s) == "teen, day 3 of 15" && Stages.Describe(new SimAge() { Stage = Stage.Elder, Days = 40 }, s) == "elder, day 41");
    Check("lengths can be set, within reason", s.SetLength(Stage.Adult, 40) && s.AdultDays == 40 && !s.SetLength(Stage.Adult, 0) && !s.SetLength(Stage.Elder, 5) && !s.SetLength(Stage.Child, 100000));
}

// ---- the clock ----
{
    DayWatch.Moment[] Feed(params int[] hours) { var w = new DayWatch(); return hours.Select(w.Feed).ToArray(); }
    Check("midnight is a day", Feed(22, 23, 23, 0, 0, 1).SequenceEqual(new[] { DayWatch.Moment.None, DayWatch.Moment.Evening, DayWatch.Moment.None, DayWatch.Moment.Midnight, DayWatch.Moment.None, DayWatch.Moment.Hour }));
    Check("birthdays can happen at six and at each hour of the evening after, and at no other", Feed(16, 17, 18, 19, 20, 21, 22, 23).Skip(1).SequenceEqual(new[] { DayWatch.Moment.Hour, DayWatch.Moment.Evening, DayWatch.Moment.Evening, DayWatch.Moment.Evening, DayWatch.Moment.Evening, DayWatch.Moment.Evening, DayWatch.Moment.Evening }) && Feed(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17).Skip(1).All(x => x == DayWatch.Moment.Hour));
    Check("a lot that loads at midnight has not lived a day", Feed(0)[0] == DayWatch.Moment.None);
    Check("a clock that jumps has not lived a day", Feed(14, 0)[1] == DayWatch.Moment.None && Feed(9, 18)[1] == DayWatch.Moment.None);
}

// ---- what is remembered ----
{
    FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "age-test-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);
    var hood = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
    var other = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData2/");

    var fresh = Ledger.Read("sample.lifestages", hood);
    Check("no file: a fresh record with The Sims 2's lengths", fresh.Sims.Count == 0 && fresh.Settings.Ageing && fresh.Settings.ChildDays == 8 && fresh.Settings.TeenDays == 15 && fresh.Settings.AdultDays == 29);
    Check("a Sim is met at the start of the stage its age says", fresh.Meet(0x1234abcd, 9).Stage == Stage.Child && fresh.Meet(7, 27).Stage == Stage.Adult && fresh.Meet(8, 65).Stage == Stage.Elder && fresh.Meet(9, 15).Stage == Stage.Teen);
    fresh.Meet(0x1234abcd, 9).Days = 5;
    Check("meeting again does not start over", fresh.Meet(0x1234abcd, 27).Days == 5 && fresh.Meet(0x1234abcd, 27).Stage == Stage.Child);
    fresh.Settings.TeenDays = 20; fresh.Settings.Teens = false;
    Check("written", fresh.Write("sample.lifestages", hood));
    var again = Ledger.Read("sample.lifestages", hood);
    Check("read back", again.Sims.Count == 4 && again.Find(0x1234abcd).Days == 5 && again.Find(8).Stage == Stage.Elder && again.Settings.TeenDays == 20 && !again.Settings.Teens);
    Check("another neighbourhood has its own", Ledger.Read("sample.lifestages", other).Sims.Count == 0 && Ledger.PathFor("sample.lifestages", hood) != Ledger.PathFor("sample.lifestages", other));
    Check("the file is beside the saves, not in them", Ledger.PathFor("sample.lifestages", hood).Replace('\\', '/').Contains("/LocalHouse/PackData/sample.lifestages/"));

    // a setting is kept at once, and days played but not saved are not kept with it
    var playing = Ledger.Read("sample.lifestages", hood);
    playing.Find(0x1234abcd).Days = 7; playing.Meet(99, 9); playing.Settings.AdultDays = 40;
    Check("settings written", playing.WriteSettings("sample.lifestages", hood));
    var kept = Ledger.Read("sample.lifestages", hood);
    Check("a changed setting is kept at once", kept.Settings.AdultDays == 40 && kept.Settings.TeenDays == 20);
    Check("without keeping days that were not saved", kept.Find(0x1234abcd).Days == 5 && kept.Find(99) == null && kept.Sims.Count == 4);
    Check("the days still in play are untouched", playing.Find(0x1234abcd).Days == 7 && playing.Find(99) != null);

    File.WriteAllText(Ledger.PathFor("sample.lifestages", hood), "{ \"Settings\": { \"ChildDays\": -4, \"AdultDays\": 5000000 }, \"Sims\": { \"00000001\": { \"Stage\": 2, \"Days\": -9 }, \"00000002\": { \"Stage\": 77, \"Days\": 1 }, \"00000003\": null } }");
    var mended = Ledger.Read("sample.lifestages", hood);
    Check("a damaged file is mended", mended.Settings.ChildDays == 1 && mended.Settings.AdultDays == Settings.LongestStage && mended.Sims.Count == 1 && mended.Find(1).Days == 0 && mended.Find(1).Stage == Stage.Adult);
    File.WriteAllText(Ledger.PathFor("sample.lifestages", hood), "not a record at all");
    Check("an unreadable file gives a fresh record", Ledger.Read("sample.lifestages", hood).Sims.Count == 0);
    Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true);
}

// ---- the body a child grows into (names shaped like the game's) ----
{
    var heads = new[] { "C001FA_Annie", "C002FA_Bea", "C003FA_NoTexture" };
    var bodies = new[] { "B001FAFit_01", "B002FAFat_02", "B003FASkn_03", "B004FAFit_OnlyDark", "odd", "B900FACop_Uniform", "B001FAFit_01" };
    var textures = new[] {
        "C001FAlgt_Annie.bmp", "C001FAdrk_Annie.bmp", "C002FAlgt_Bea.bmp",
        "B900FACoplgt_Uniform.bmp", "B001FAFitlgt_01.bmp", "B002FAFatlgt_02.bmp", "B003FASknlgt_03.bmp", "B004FAFitdrk_OnlyDark.bmp", "B001FAFitdrk_01.bmp",
        "huaolgt.bmp", "huaodrk.bmp" };
    var random = new Random(1);
    var all = Enumerable.Range(0, 200).Select(i => Body.ChooseAdult(true, "lgt", heads, bodies, textures, random)).ToList();
    Check("a light-skinned woman can grow up", all.All(x => x != null));
    Check("she becomes an adult of 27", all.All(x => x[0] == "adult" && x[13] == "27" && x[12] == "female" && x[14] == "lgt"));
    Check("only heads with her skin colour are worn", all.Select(x => x[2]).Distinct().OrderBy(x => x).SequenceEqual(new[] { "C001FA_Annie,HEAD-HEAD=C001FAlgt_Annie", "C002FA_Bea,HEAD-HEAD=C002FAlgt_Bea" }));
    Check("and only such bodies: every build turns up", all.Select(x => x[1]).Distinct().OrderBy(x => x).SequenceEqual(new[] { "B001FAFit_01,BODY=B001FAFitlgt_01", "B002FAFat_02,BODY=B002FAFatlgt_02", "B003FASkn_03,BODY=B003FASknlgt_03" }));
    Check("her other clothes follow her build", all.All(x => x[15] == "n" + "f" + Body.BuildOf(x[1].Split(',')[0]) + "_01,BODY=nf" + Body.BuildOf(x[1].Split(',')[0]) + "lgt_01"));
    Check("she has a woman's hands", all.All(x => x[17] == "HfLO,HAND=huaolgt" && x[22] == "HfRC,HAND=huaolgt"));
    var dark = Body.ChooseAdult(true, "drk", heads, bodies, textures, random);
    Check("a dark-skinned woman gets what there is for her", dark != null && dark[2].StartsWith("C001FA_Annie") && dark[14] == "drk");
    Check("nothing to wear: no growing up", Body.ChooseAdult(true, "med", heads, bodies, textures, random) == null && Body.ChooseAdult(false, "lgt", new string[0], new string[0], textures, random) == null);
    Looks.Use(new LooksFile() { Heads = new AgeList() { Elder = new List<string>() { " c001fa_annie " } } });
    var noOld = Enumerable.Range(0, 100).Select(i => Body.ChooseAdult(true, "lgt", heads, bodies, textures, random)).ToList();
    Check("a head for elders only is not given to a young adult", noOld.All(x => x[2].StartsWith("C002FA_Bea")));
    Looks.Use(new LooksFile() { Heads = new AgeList() { Elder = new List<string>() { "C001FA_Annie", "c002fa_bea" } } });
    Check("unless there is no other", Body.ChooseAdult(true, "lgt", heads, bodies, textures, random) != null);
    Looks.Use(new LooksFile() { Bodies = new AgeList() { Elder = new List<string>() { "b001fafit_01", "b002fafat_02" } } });
    var dressed = Enumerable.Range(0, 100).Select(i => Body.ChooseAdult(true, "lgt", heads, bodies, textures, random)).ToList();
    Check("a body for elders only is not given to a young adult", dressed.All(x => x[1].StartsWith("B003FASkn_03")));
    Looks.Use(null);
    Check("a costume or uniform is not an everyday body", Body.BuildOf("B900FACop_Uniform") == null && Body.BuildOf("odd") == null && Body.BuildOf("b001mafit_01") == "fit" && Body.BuildOf("B002FAFat_02") == "Fat");
    var lower = Body.ChooseAdult(false, "lgt", new[] { "c010ma_bob" }, new[] { "b010mafit_01" }, new[] { "C010MALGT_BOB.BMP", "B010MAFITLGT_01.BMP" }, random);
    Check("names are matched whatever their case", lower != null && lower[12] == "male" && lower[17] == "HmLO,HAND=huaolgt");
    Check("skin colour, from the strings or else the person data", Body.SkinColour("MED", 0) == "med" && Body.SkinColour("", 2) == "drk" && Body.SkinColour(null, 9) == "drk" && Body.SkinColour("purple", -3) == "lgt");
}

// ---- which heads are elders' ----
{
    var heads = new List<string>() { "c001ma_kaj", "c002ma_ben", "c003ma_old", "c004ma_helmet", "c005ma_bald" };
    Looks.Use(null);
    Check("with no lists, adults and elders are offered every head", Looks.Heads.Offer(heads, Stage.Adult).Count == 5 && Looks.Heads.Offer(heads, Stage.Elder).Count == 5);
    Looks.Use(new LooksFile() { Heads = new AgeList() { Elder = new List<string>() { "C003MA_Old", "", null }, Both = new List<string>() { "c004ma_helmet ", "c005MA_bald" } } });
    Check("an elder's head is not offered to adults", Looks.Heads.Offer(heads, Stage.Adult).Select(i => heads[i]).SequenceEqual(new[] { "c001ma_kaj", "c002ma_ben", "c004ma_helmet", "c005ma_bald" }));
    Check("elders are offered theirs and those for both, in the game's order", Looks.Heads.Offer(heads, Stage.Elder).Select(i => heads[i]).SequenceEqual(new[] { "c003ma_old", "c004ma_helmet", "c005ma_bald" }));
    Check("names are matched whatever their capitals and spaces", Looks.Heads.ElderOnly("C003ma_OLD") && !Looks.Heads.ElderOnly("c004ma_helmet") && Looks.Heads.Suits("C005MA_BALD", Stage.Elder));
    Check("children's and teens' heads are not sorted", Looks.Heads.Suits("c003ma_old", Stage.Child) && Looks.Heads.Suits("c003ma_old", Stage.Teen));
    Looks.Use(new LooksFile() { Bodies = new AgeList() { Elder = new List<string>() { "b800fafit_eldenmama" }, Both = new List<string>() { "b003fafit_coat" } } });
    var gowns = new List<string>() { "b001fafit_jeans", "b800fafit_eldenmama", "b003fafit_coat" };
    Check("an elder's body is not offered to adults, and elders are offered theirs and those for both",
        Looks.Bodies.Offer(gowns, Stage.Adult).Select(i => gowns[i]).SequenceEqual(new[] { "b001fafit_jeans", "b003fafit_coat" })
        && Looks.Bodies.Offer(gowns, Stage.Elder).Select(i => gowns[i]).SequenceEqual(new[] { "b800fafit_eldenmama", "b003fafit_coat" }));
    Check("heads and bodies are listed apart", !Looks.Heads.ElderOnly("b800fafit_eldenmama") && Looks.Bodies.Offer(new List<string>() { "b001mafit_suit" }, Stage.Elder).Count == 1);
    Looks.Use(new LooksFile() { Heads = new AgeList() { Elder = new List<string>() { "C003MA_Old", "", null }, Both = new List<string>() { "c004ma_helmet ", "c005MA_bald" } } });
    Check("elders with none of their own in a list are offered every head", Looks.Heads.Offer(new List<string>() { "c001ma_kaj", "c002ma_ben" }, Stage.Elder).Count == 2);

    // the file, as shipped and as a player might leave it
    var dir = Path.Combine(Path.GetTempPath(), "age-heads-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var shipped = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", Looks.FileName);
    if (!File.Exists(shipped)) shipped = Path.Combine(Directory.GetCurrentDirectory(), "LifeStages", Looks.FileName);
    File.Copy(shipped, Path.Combine(dir, Looks.FileName));
    var said = Looks.Load(dir);
    Check("the looks.json the pack ships reads: " + said, said == Looks.FileName + ": 15 heads for elders only, 13 for adults and elders; 3 bodies for elders only, 0 for adults and elders." && Looks.Heads.ElderOnly("c631ma_mobius")
        && Looks.Bodies.ElderOnly("b800fafit_eldenmama") && Looks.Bodies.ElderOnly("B602FASKN_Cornelia") && !Looks.Heads.ElderOnly("b800fafit_eldenmama") && Looks.Heads.ElderOnly("c805ma_brad") && Looks.Heads.ElderOnly("c004fa_gma1") && !Looks.Heads.ElderOnly("c001ma_kaj")
        && Looks.Heads.Suits("c002ma_baldfat", Stage.Adult) && Looks.Heads.Suits("c002ma_baldfat", Stage.Elder) && !Looks.Heads.Suits("c001ma_kaj", Stage.Elder));
    File.WriteAllText(Path.Combine(dir, Looks.FileName), "// mine\n{ \"heads\": { \"elder\": [\"c003ma_old\", \"c009fa_gran\",], \"Both\": [\"c004ma_helmet\"] }, \"Bodies\": { \"elder\": [\"b900fafit_shawl\"] } }");
    said = Looks.Load(dir);
    Check("a file with notes, a trailing comma and a capital is read: " + said, Looks.Heads.Elder.Count == 2 && Looks.Heads.Both.Count == 1 && Looks.Heads.ElderOnly("c009fa_gran") && Looks.Bodies.ElderOnly("b900fafit_shawl") && Looks.Bodies.Both.Count == 0);
    File.WriteAllText(Path.Combine(dir, Looks.FileName), "{ elder: oops");
    said = Looks.Load(dir);
    Check("a broken file is said to be, and leaves the lists empty: " + said, said.Contains("could not be read") && Looks.Heads.Elder.Count == 0);
    File.Delete(Path.Combine(dir, Looks.FileName));
    Check("no file, no lists", Looks.Load(dir).StartsWith("No ") && Looks.Heads.Both.Count == 0);
    Directory.Delete(dir, true);

    // the heads offered in the picker: the one worn first, then the stage's, each once
    Looks.Use(new LooksFile() { Heads = new AgeList() { Elder = new List<string>() { "c003ma_old" }, Both = new List<string>() { "c004ma_helmet" } } });
    var textures = new[] { "c001malgt_kaj.bmp", "c002malgt_ben.bmp", "c003malgt_old.bmp", "c003madrk_old.bmp", "c004malgt_helmet.bmp" };
    var worn = Body.Worn("c001ma_kaj,HEAD-HEAD=c001malgt_kaj");
    Check("the head worn is read from the Sim's head line", worn.Mesh == "c001ma_kaj" && worn.Tex == "c001malgt_kaj" && Body.Worn(null).Mesh == null);
    var elderChoices = Body.Choices(worn.Mesh, worn.Tex, heads, Stage.Elder, "lgt", textures);
    Check("an elder is offered their own head first, then the elders' heads in their skin colour: " + string.Join(", ", elderChoices.Select(x => x.Mesh)),
        elderChoices.Select(x => x.Mesh).SequenceEqual(new[] { "c001ma_kaj", "c003ma_old", "c004ma_helmet" }) && elderChoices[1].Tex == "c003malgt_old");
    var adultChoices = Body.Choices("c002ma_ben", "c002malgt_ben", heads, Stage.Adult, "lgt", textures);
    Check("an adult is offered their own once, and no elder's: " + string.Join(", ", adultChoices.Select(x => x.Mesh)),
        adultChoices.Select(x => x.Mesh).SequenceEqual(new[] { "c002ma_ben", "c001ma_kaj", "c004ma_helmet" }));
    Check("a head with no picture in the skin colour is not offered", Body.Choices(null, null, heads, Stage.Elder, "drk", textures).Select(x => x.Mesh).SequenceEqual(new[] { "c003ma_old" }));
    Looks.Use(null);
}

// ---- the age display ----
{
    var s = new Settings();
    const int W = 300;
    var teen = AgeView.For(new SimAge() { Stage = Stage.Teen, Days = 3 }, s, true, 12, W);
    Check("a teen on day 4: " + teen.Title + " / " + teen.Counter + " / " + teen.Status, teen.Title == "Teen" && teen.Counter == "Day 4 of 15" && teen.Status == "12 days until adult" && teen.Note == "");
    Check("the bar has the four stages, in order, across its whole width", teen.Segments.Select(x => x.Stage).SequenceEqual(new[] { Stage.Child, Stage.Teen, Stage.Adult, Stage.Elder }) && teen.Segments[0].X == 0 && teen.Segments.Last().X + teen.Segments.Last().Width == W);
    Check("segments do not touch or overlap", Enumerable.Range(1, 3).All(i => teen.Segments[i].X == teen.Segments[i - 1].X + teen.Segments[i - 1].Width + AgeView.Gap));
    Check("a longer stage is a wider segment, and none is too narrow for its figure", teen.Segments[0].Width < teen.Segments[1].Width && teen.Segments[1].Width < teen.Segments[2].Width && teen.Segments.All(x => x.Width >= AgeView.NarrowestSegment && x.Width >= Figures.Width));
    Check("what is lived is filled: childhood whole, 3 of 15 teen days, nothing after", teen.Segments[0].Fill == 1 && Math.Abs(teen.Segments[1].Fill - 0.2f) < 0.001f && teen.Segments[2].Fill == 0 && teen.Segments[3].Fill == 0);
    Check("only the Sim's own stage is marked", teen.Segments.Count(x => x.Current) == 1 && teen.Segments[1].Current);

    Check("one day left is one day", AgeView.For(new SimAge() { Stage = Stage.Child, Days = 7 }, s, true, 12, W).Status == "1 day until teen");
    var due = AgeView.For(new SimAge() { Stage = Stage.Adult, Days = 29 }, s, true, 12, W);
    Check("a Sim who is due: " + due.Status, due.Status == "Ages up at 6 pm" && due.Counter == "Day 29 of 29" && due.Segments[2].Fill == 1);
    var elder = AgeView.For(new SimAge() { Stage = Stage.Elder, Days = 40 }, s, true, 12, W);
    Check("when a Sim who is due moves on is said truthfully",
        AgeView.For(new SimAge() { Stage = Stage.Child, Days = 8 }, s, true, 20, W).Status == "Ages up within the hour" &&
        AgeView.For(new SimAge() { Stage = Stage.Child, Days = 8 }, s, false, 12, W).Status == "Ages up once back home" &&
        AgeView.For(new SimAge() { Stage = Stage.Child, Days = 8 }, new Settings() { Ageing = false }, true, 12, W).Status == "Ages up when ageing is on");
    Check("an elder: " + elder.Counter + " / " + elder.Status, elder.Title == "Elder" && elder.Counter == "Day 41" && elder.Status == "The last stage of life" && elder.Segments.All(x => x.Fill == 1));
    Check("away from home, and with ageing off, it says so", AgeView.For(new SimAge(), s, false, 12, W).Note == "Days only count at home." && AgeView.For(new SimAge(), new Settings() { Ageing = false }, true, 12, W).Note == "Ageing is off.");

    var noTeens = new Settings() { Teens = false };
    var child = AgeView.For(new SimAge() { Stage = Stage.Child, Days = 2 }, noTeens, true, 12, W);
    Check("without teens the bar has three stages and a child grows into an adult", child.Segments.Count == 3 && child.Status == "6 days until adult");
    Check("a teen from before still has a place on it", AgeView.For(new SimAge() { Stage = Stage.Teen, Days = 2 }, noTeens, true, 12, W).Segments.Count == 4);
    Check("the display changes when a day passes, and not otherwise", teen.Key != AgeView.For(new SimAge() { Stage = Stage.Teen, Days = 4 }, s, true, 12, W).Key && teen.Key == AgeView.For(new SimAge() { Stage = Stage.Teen, Days = 3 }, s, true, 12, W).Key);
    var none = AgeView.Unaged();
    Check("a Sim who does not age has words and no bar", none.Segments.Count == 0 && none.Title == "Does not age");
    Check("a very narrow bar still holds together", AgeView.For(new SimAge(), s, true, 12, 40).Segments.All(x => x.Width > 0));

    int Ink(Stage st) => Figures.Mask(st).Count(b => b > 127);
    foreach (Stage st in Enum.GetValues(typeof(Stage)))
    {
        var mask = Figures.Mask(st);
        int tall = Figures.Height(st);
        bool Row(int y) => Enumerable.Range(0, Figures.Width).Any(x => mask[y * Figures.Width + x] > 127);
        bool Col(int x) => Enumerable.Range(0, tall).Any(y => mask[y * Figures.Width + x] > 127);
        Check("the " + Stages.Name(st) + " figure fills its height and stays inside its width", mask.Length == Figures.Width * tall && Row(0) && Row(tall - 1) && !Col(0) && !Col(Figures.Width - 1));
    }
    Check("the figures grow with age, and an elder stoops", Figures.Height(Stage.Child) < Figures.Height(Stage.Teen) && Figures.Height(Stage.Teen) < Figures.Height(Stage.Adult) && Figures.Height(Stage.Elder) < Figures.Height(Stage.Adult) && Ink(Stage.Child) < Ink(Stage.Teen) && Ink(Stage.Teen) < Ink(Stage.Adult));

    // for a look at it without the game: dotnet run --project LifeStages/tests -- --preview [folder]
    var at = Array.IndexOf(args, "--preview");
    if (at >= 0 && at + 1 < args.Length)
    {
        Directory.CreateDirectory(args[at + 1]);
        foreach (Stage st in Enum.GetValues(typeof(Stage)))
            File.WriteAllBytes(Path.Combine(args[at + 1], Stages.Name(st) + ".pgm"), System.Text.Encoding.ASCII.GetBytes("P5\n" + Figures.Width + " " + Figures.Height(st) + "\n255\n").Concat(Figures.Mask(st)).ToArray());
        var views = new[] { AgeView.For(new SimAge() { Stage = Stage.Child, Days = 5 }, s, true, 12, W), teen, due, elder, AgeView.For(new SimAge() { Stage = Stage.Adult, Days = 10 }, s, false, 12, W), none };
        File.WriteAllLines(Path.Combine(args[at + 1], "views.txt"), views.Select(v => v.Key));
    }
}

// ---- Create a Sim ----
{
    Check("a body keeps the stage chosen when it suits it", Stages.Suiting(Stage.Teen, true) == Stage.Teen && Stages.Suiting(Stage.Elder, false) == Stage.Elder && Stages.Suiting(Stage.Child, true) == Stage.Child);
    Check("and a body that does not suit it gives the nearest that does", Stages.Suiting(Stage.Elder, true) == Stage.Child && Stages.Suiting(Stage.Teen, false) == Stage.Adult);

    // a family of four, one of each stage, made as Create a Sim makes it
    FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "age-cas-" + Guid.NewGuid().ToString("n"));
    var hood = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
    Directory.CreateDirectory(hood);
    var oldPath = Life.NeighbourhoodPath;
    Life.NeighbourhoodPath = () => hood;
    var stages = new[] { Stage.Child, Stage.Teen, Stage.Adult, Stage.Elder };
    var members = stages.Select((st, i) => new Simitone.Client.UI.Screens.CASFamilyMember() { Name = "Sim" + i, Gender = (short)((i % 2) | (Stages.IsChildToGame(st) ? 2 : 0)), SkinColor = "med" }).ToList();
    for (int i = 0; i < 4; i++) CreateASim.SetStageOf(members[i], stages[i]);
    var unseen = new Simitone.Client.UI.Screens.CASFamilyMember() { Gender = 3 };
    Check("a Sim the pack did not see is what their body says", CreateASim.StageOf(unseen) == Stage.Child && CreateASim.StageOf(new Simitone.Client.UI.Screens.CASFamilyMember() { Gender = 1 }) == Stage.Adult);
    var infos = members.Select(m => { var info = new FSO.SimAntics.Utils.SimTemplateCreateInfo((m.Gender % 2 == 0 ? "m" : "f") + (m.Gender > 1 ? "c" : "a") + "fit", "med"); CreateASim.AfterCASToNeighGen(m, info); return info; }).ToArray();
    Check("each Sim's file says their age: " + string.Join(", ", infos.Select(x => x.BodyStringReplace[13])), infos.Select(x => x.BodyStringReplace[13]).SequenceEqual(new[] { "9", "15", "27", "65" }));
    Check("and a teen is still made with a child's body, an elder with an adult's", infos[1].BodyStringReplace[0] == "child" && infos[3].BodyStringReplace[0] == "adult");
    var fam = new FSO.Files.Formats.IFF.Chunks.FAMI() { FamilyGUIDs = new uint[] { 0x1001, 0x1002, 0x1003, 0x1004 } };
    var before = Ledger.Read("sample.lifestages", hood);
    before.Meet(0x2000, 27).Days = 12;
    before.Write("sample.lifestages", hood);
    CreateASim.AfterCreateFamily(fam, infos);
    var after = Ledger.Read("sample.lifestages", hood);
    Check("the new family is in the record at once, each at their stage on their first day",
        Enumerable.Range(0, 4).All(i => after.Find(fam.FamilyGUIDs[i])?.Stage == stages[i] && after.Find(fam.FamilyGUIDs[i]).Days == 0));
    Check("and the Sims already there are as they were", after.Find(0x2000)?.Days == 12 && after.Sims.Count == 5);
    CreateASim.AfterCreateFamily(new FSO.Files.Formats.IFF.Chunks.FAMI() { FamilyGUIDs = new uint[] { 0x3001 } }, new[] { new FSO.SimAntics.Utils.SimTemplateCreateInfo("mafit", "lgt") });
    Check("a family made some other way is left to be met as usual", Ledger.Read("sample.lifestages", hood).Find(0x3001) == null);
    Life.NeighbourhoodPath = oldPath;
    Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true);

    // the buttons
    const int B = CreateASim.ButtonSize;
    foreach (Stage st in Enum.GetValues(typeof(Stage)))
    {
        var icon = Figures.Icon(st, B);
        byte At(int x, int y) => icon[y * B + x];
        Check("the " + Stages.Name(st) + " button: a ring, a gap, a disc, and the figure cut out of it",
            icon.Length == B * B && At(B / 2, 1) > 200 && At(B / 2, (int)(B / 2 * (1 - 25.5f / 32))) < 40 && At(B / 2 - 14, B / 2) > 200 && At(B / 2, 7) > 100
            && At(B / 2, B / 2) < 60 && At(0, 0) == 0);
    }
    var pat2 = Array.IndexOf(args, "--preview");
    if (pat2 >= 0 && pat2 + 1 < args.Length)
        foreach (Stage st in Enum.GetValues(typeof(Stage)))
            File.WriteAllBytes(Path.Combine(args[pat2 + 1], "button-" + Stages.Name(st) + ".pgm"), System.Text.Encoding.ASCII.GetBytes("P5\n" + B + " " + B + "\n255\n").Concat(Figures.Icon(st, B)).ToArray());
}

// ---- the console, with no game running ----
{
    var answer = LifeStagesPack.Run(new[] { "nonsense" }, out var understood);
    Check("the console does not fall over without a game", answer != null || !understood);
}

Console.WriteLine(failed == 0 ? "All passed." : failed + " FAILED.");
return failed == 0 ? 0 : 1;
