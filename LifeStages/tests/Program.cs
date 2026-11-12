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
foreach (var needed in new[] { "VM.Tick", "TS1AvatarTextureProvider.Get", "TS1NeighborhoodProvider.InitSpecific" })
    Check("patched " + needed, patched.Contains(needed));
// the per-tick hook must be harmless on a lot it knows nothing about
Life.AfterTick(null);
Check("nobody lives on no lot", Life.Household(null).Count == 0 && !Life.AtHome(null));

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
    Check("midnight is a day", Feed(22, 23, 23, 0, 0, 1).SequenceEqual(new[] { DayWatch.Moment.None, DayWatch.Moment.Hour, DayWatch.Moment.None, DayWatch.Moment.Midnight, DayWatch.Moment.None, DayWatch.Moment.Hour }));
    Check("six in the evening is birthdays", Feed(17, 18, 19)[1] == DayWatch.Moment.Birthdays);
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
    fresh.Settings.TeenDays = 20; fresh.Settings.GreyHair = false;
    Check("written", fresh.Write("sample.lifestages", hood));
    var again = Ledger.Read("sample.lifestages", hood);
    Check("read back", again.Sims.Count == 4 && again.Find(0x1234abcd).Days == 5 && again.Find(8).Stage == Stage.Elder && again.Settings.TeenDays == 20 && !again.Settings.GreyHair);
    Check("another neighbourhood has its own", Ledger.Read("sample.lifestages", other).Sims.Count == 0 && Ledger.PathFor("sample.lifestages", hood) != Ledger.PathFor("sample.lifestages", other));
    Check("the file is beside the saves, not in them", Ledger.PathFor("sample.lifestages", hood).Replace('\\', '/').Contains("/LocalHouse/PackData/sample.lifestages/"));

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
    var bodies = new[] { "B001FAFit_01", "B002FAFat_02", "B003FASkn_03", "B004FAFit_OnlyDark", "odd" };
    var textures = new[] {
        "C001FAlgt_Annie.bmp", "C001FAdrk_Annie.bmp", "C002FAlgt_Bea.bmp",
        "B001FAFitlgt_01.bmp", "B002FAFatlgt_02.bmp", "B003FASknlgt_03.bmp", "B004FAFitdrk_OnlyDark.bmp", "B001FAFitdrk_01.bmp",
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
    Check("skin colour, from the strings or else the person data", Body.SkinColour("MED", 0) == "med" && Body.SkinColour("", 2) == "drk" && Body.SkinColour(null, 9) == "drk" && Body.SkinColour("purple", -3) == "lgt");
}

// ---- grey hair ----
{
    int Spread(Color c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
    int Far(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    foreach (var tone in Body.SkinColours)
    {
        var skin = GreyHair.FallbackSkin(tone);
        Color Shade(float f) => new Color((int)(skin.R * f), (int)(skin.G * f), (int)(skin.B * f), 255);
        var face = new[] { skin, Shade(0.85f), Shade(1.08f), Shade(0.75f) };
        var kept = GreyHair.Process(face, skin);
        Check(tone + ": a face in light and shade is left", Enumerable.Range(0, face.Length).All(i => Far(face[i], kept[i]) <= 6));
        var hair = new[] { new Color(20, 16, 14), new Color(70, 40, 22), new Color(214, 180, 90), new Color(150, 60, 30), new Color(110, 110, 112) };
        var grey = GreyHair.Process(hair, skin);
        Check(tone + ": black, brown, fair, red and grey hair come out silver", grey.All(c => Spread(c) <= 26 && c.R >= 100));
        Check(tone + ": the shading in hair is kept", grey[0].R < grey[2].R);
    }
    var clear = GreyHair.Process(new[] { new Color(70, 40, 22, 0), new Color(70, 40, 22, 99) }, GreyHair.FallbackSkin("lgt"));
    Check("what is see-through stays so", clear[0].A == 0 && clear[1].A == 99);
    Check("a hand's average is its skin", GreyHair.Average(new[] { new Color(0, 0, 0, 255), new Color(200, 150, 100, 255), new Color(220, 170, 120, 255), new Color(9, 9, 9, 0) }) == new Color(210, 160, 110) && GreyHair.Average(new Color[] { Color.Black }) == null);

    var name = GreyHair.NameFor("c003falgt_annie", "lgt");
    Check("a grey head's name reads back", GreyHair.TryParse(name, out var tex, out var sk) && tex == "c003falgt_annie" && sk == "lgt");
    Check("no other name is taken for one", !GreyHair.TryParse("c003falgt_annie", out _, out _) && !GreyHair.TryParse(null, out _, out _) && !GreyHair.TryParse(GreyHair.Marker + "lgt", out _, out _));
    var asked = new List<string>();
    var real = new FSO.Content.Model.LoadedTextureRef(null);
    FSO.Content.Model.ITextureRef Lookup(string n) { asked.Add(n); return n == "c003falgt_annie" ? real : null; }
    var made = GreyHair.Find(name, Lookup);
    Check("a grey copy is made of a head that exists, once", made != null && made != real && GreyHair.Find(name, Lookup) == made && asked.Count == 1);
    Check("and not of one that does not", GreyHair.Find(GreyHair.NameFor("nosuchhead", "lgt"), Lookup) == null && GreyHair.Find("c003falgt_annie", Lookup) == null);
}

// ---- the console, with no game running ----
{
    var answer = LifeStagesPack.Run(new[] { "nonsense" }, out var understood);
    Check("the console does not fall over without a game", answer != null || !understood);
}

Console.WriteLine(failed == 0 ? "All passed." : failed + " FAILED.");
return failed == 0 ? 0 : 1;
