/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Lot Traits pack that need no copy of The Sims: that its patches fit the engine it is built against,
// that needs drop as they should through the engine's own decay code with the patch on, that traits.json is sound,
// and that a lot's traits are kept and read back through Simitone's own LotPackData.
// What they cannot check is the game itself: nothing here loads a lot, and the Sims here are bare.
// Run with: dotnet run --project LotTraits/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FSO.Common;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using HarmonyLib;
using LotTraits;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

var here = AppContext.BaseDirectory;
string PackFolder()
{
    //the pack's source folder, where traits.json is: up from the test's build output
    var dir = new DirectoryInfo(here);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LotTraits.csproj"))) dir = dir.Parent;
    return dir?.FullName;
}

// ---- the patches fit this engine ----
{
    var h = new Harmony("test.lottraits");
    var apply = typeof(LotTraitsPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    try { apply.Invoke(null, new object[] { h }); }
    catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
    var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    foreach (var needed in new[] { "VMTS1MotiveDecay.Tick", "UILotControl.Update", "UIMainPanel.AddPacksButton" })
        Check("patched " + needed, patched.Contains(needed));
    h.UnpatchAll(h.Id); //the checks below put the decay patch on by themselves
}

// ---- the needs, as the engine has them ----
Check("seven needs drop: " + string.Join(", ", Needs.Dropping), Needs.Count == 7 && Needs.IndexOf("hygiene") >= 0 && Needs.IndexOf("ROOM") < 0 && Needs.IndexOf("mood") < 0);
Check("the engine keeps a fraction for each", new VMTS1MotiveDecay().MotiveFractions.Length == Needs.Count);

// ---- traits.json ----
var folder = PackFolder();
var problems = new List<string>();
var book = TraitBook.Load(Path.Combine(folder, "traits.json"), problems);
Check("traits.json reads without problems" + (problems.Count > 0 ? ": " + string.Join(" / ", problems) : ""), problems.Count == 0);
Check("it has traits (" + string.Join(", ", book.Traits.Select(x => x.Name)) + ")", book.Traits.Count >= 6);
Check("a lot can have three, as in The Sims 4", book.Most == 3);
Check("the ids are all different", book.Traits.Select(x => x.Id).Distinct().Count() == book.Traits.Count);
foreach (var name in new[] { "Fresh Air", "Grody" }) Check("there is " + name, book.Find(name) != null);
Check("Grody: " + book.Get("grody")?.EffectText(), book.Get("grody")?.EffectText() == "Hygiene drops 50% faster, Room -10.");
Check("Mean Vibe: " + book.Get("mean-vibe")?.EffectText(), book.Get("mean-vibe")?.EffectText() == "Fun drops 15% faster, Social drops 25% faster.");
Check("found by the start of a name", book.Find("noisy")?.Id == "noisy-neighbours" && book.Find("fresh-air")?.Name == "Fresh Air" && book.Find("") == null);
{
    var built = Path.Combine(folder, "pack", "lottraits", "traits.json");
    Check("the build puts traits.json in the pack's folder", File.Exists(built) && File.ReadAllText(built) == File.ReadAllText(Path.Combine(folder, "traits.json")));
}

// what is wrong with a trait is said, and only that trait is left out
{
    var p = new List<string>();
    var b = TraitBook.Parse(@"{ ""most"": 2, ""traits"": [
        { ""id"": ""ok"", ""name"": ""Fine"", ""needs"": { ""fun"": 2 } },
        { ""id"": ""ok"", ""name"": ""Again"", ""needs"": { ""fun"": 2 } },
        { ""id"": ""Bad Id"", ""name"": ""x"", ""needs"": { ""fun"": 2 } },
        { ""id"": ""noname"", ""needs"": { ""fun"": 2 } },
        { ""id"": ""room-need"", ""name"": ""x"", ""needs"": { ""room"": 2 } },
        { ""id"": ""too-fast"", ""name"": ""x"", ""needs"": { ""fun"": 9 } },
        { ""id"": ""backwards"", ""name"": ""x"", ""needs"": { ""fun"": -1 } },
        { ""id"": ""text"", ""name"": ""x"", ""needs"": { ""fun"": ""fast"" } },
        { ""id"": ""nothing"", ""name"": ""x"", ""needs"": { ""fun"": 1 } },
        { ""id"": ""roomy"", ""name"": ""Roomy"", ""room"": 60 },
        { ""id"": ""never"", ""name"": ""Never Hungry"", ""needs"": { ""hunger"": 0 }, ""room"": 3 },
    ] }", p);
    Check("one of each bad trait is left out, with a reason: " + p.Count, p.Count == 9 && b.Traits.Count == 2 && b.Most == 2);
    Check("a need that does not drop on its own is named as one", p.Any(x => x.Contains("does not drop: room")));
    Check("never: " + b.Get("never")?.EffectText(), b.Get("never")?.EffectText() == "Hunger never drops, Room +3.");
    Check("twice as fast is said so", b.Get("ok").EffectText() == "Fun drops 2 times as fast.");
    var none = new List<string>();
    Check("no file, no traits, and a reason", TraitBook.Load(Path.Combine(folder, "no-such-file.json"), none).Traits.Count == 0 && none.Count == 1);
    var broken = new List<string>();
    Check("a broken file, no traits, and a reason", TraitBook.Load(WriteTemp("{ \"traits\": [ "), broken).Traits.Count == 0 && broken.Count == 1);
}

// ---- several traits at once ----
{
    var a = new NeedEffect() { Room = 60 }; a.Rates[Needs.IndexOf("hygiene")] = 1.5f;
    var b = new NeedEffect() { Room = 70 }; b.Rates[Needs.IndexOf("hygiene")] = 2f; b.Rates[Needs.IndexOf("fun")] = 0.5f;
    var both = NeedEffect.Combine(new[] { a, null, b });
    Check("rates multiply", both.Rates[Needs.IndexOf("hygiene")] == 3f && both.Rates[Needs.IndexOf("fun")] == 0.5f && both.Rates[Needs.IndexOf("hunger")] == 1f);
    Check("Room points add up, as far as 100", both.Room == 100);
    Check("nothing combined is no effect", NeedEffect.Combine(new NeedEffect[0]).IsNone && NeedEffect.None.IsNone && !both.IsNone);
}

// ---- one tick of one need, scaled ----
{
    short m, f;
    bool S(short m0, short f0, short m1, short f1, float rate, out short mo, out short fo) { mo = m1; fo = f1; return Scale(m0, f0, ref mo, ref fo, rate); }
    bool Scale(short m0, short f0, ref short m1, ref short f1, float rate) => NeedRates.Scale(m0, f0, ref m1, ref f1, rate);

    Check("the game's own rate changes nothing", !S(50, 900, 49, 70, 1f, out m, out f) && m == 49 && f == 70);
    // drained 170 thousandths (900 -> 1070): twice is 340, so 1240 -> one point and 240
    Check("twice as fast drains twice as much", S(50, 900, 49, 70, 2f, out m, out f) && m == 49 && f == 240);
    Check("three times as fast crosses a point the game did not", S(50, 900, 49, 70, 3f, out m, out f) && m == 49 && f == 410);
    Check("half as fast keeps a point the game took", S(50, 900, 49, 70, 0.5f, out m, out f) && m == 50 && f == 985);
    Check("a rate of 0 drains nothing", S(50, 900, 49, 70, 0f, out m, out f) && m == 50 && f == 900);
    Check("a tick between drops changes nothing", !S(50, 300, 50, 300, 2f, out m, out f) && m == 50 && f == 300);
    // sleeping: energy rose by 1286 thousandths (two points, 714 left over)
    Check("a need that rose is left alone", !S(20, 0, 22, 714, 2f, out m, out f) && m == 22 && f == 714);
    Check("a need never goes below -100", S(-99, 900, -100, 70, 5f, out m, out f) && m == -100);
    Check("a need at the bottom stays there", !S(-100, 900, -100, 100, 0.5f, out m, out f) && m == -100);
    Check("a bad rate changes nothing", !S(50, 900, 49, 70, -1f, out m, out f) && !S(50, 900, 49, 70, float.NaN, out m, out f));

    // two packs with the same patch, one after the other, multiply
    short m2 = 49, f2 = 70;
    Scale(50, 900, ref m2, ref f2, 2f);
    Scale(50, 900, ref m2, ref f2, 1.5f);
    S(50, 900, 49, 70, 3f, out m, out f);
    Check("two packs' rates multiply (" + m2 + "." + f2 + " = " + m + "." + f + ")", m2 == m && f2 == f);
}

// ---- needs through the engine's own decay code, with the patch on ----
{
    var h = new Harmony("test.lottraits.engine");
    //the bare Sims here are in no room: the room code is the only part of the tick that needs a lot
    h.Patch(AccessTools.Method(typeof(VMContext), "GetRoomAt"), prefix: new HarmonyMethod(typeof(Bare), nameof(Bare.NoRoom)));
    h.Patch(AccessTools.Method(typeof(VMContext), "GetRoomScore"), prefix: new HarmonyMethod(typeof(Bare), nameof(Bare.RoomScore)));
    h.Patch(AccessTools.Method(typeof(VMTS1MotiveDecay), "Tick"),
        prefix: new HarmonyMethod(typeof(LotTraitsPack), nameof(LotTraitsPack.BeforeDecay)),
        postfix: new HarmonyMethod(typeof(LotTraitsPack), nameof(LotTraitsPack.AfterDecay)));

    var grody = book.Get("grody").Effect;
    var fresh = book.Get("fresh-air").Effect;
    var plain = Bare.Sim(); var dirty = Bare.Sim(); var airy = Bare.Sim(); var sleeper = Bare.Sim(); var sleeperDirty = Bare.Sim(); var eater = Bare.Sim();
    var effects = new Dictionary<VMAvatar, NeedEffect>() { { dirty, grody }, { airy, fresh }, { sleeperDirty, grody }, { eater, grody } };
    NeedRates.EffectFor = (avatar, context) => effects.TryGetValue(avatar, out var e) ? e : null;

    //a sleep state of -1 is the one in which the engine's decay code itself gives energy back (in a bed it is the bed's motive change)
    foreach (var s in new[] { sleeper, sleeperDirty }) { s.SetMotiveData(VMMotive.SleepState, -1); s.SetMotiveData(VMMotive.Energy, 10); }
    Bare.SetMotiveChange(eater, VMMotive.Hygiene, 10); //an interaction is looking after its hygiene: the game lets that need be

    //eight hours from nine in the morning (the sleepers from one at night, before the hour they wake)
    var awake = Bare.Run(new[] { plain, dirty, airy, eater }, 9, 8 * 60);
    var asleep = Bare.Run(new[] { sleeper, sleeperDirty }, 1, 5 * 60);

    int Drop(VMAvatar s, string need) => 80 - s.GetMotiveData(Needs.Dropping[Needs.IndexOf(need)]);
    var hp = Drop(plain, "hygiene"); var hd = Drop(dirty, "hygiene");
    Check("a plain Sim's hygiene drops at the game's rate (" + hp + " in 8 hours)", hp >= 38 && hp <= 43);
    Check("on a Grody lot hygiene drops half again as fast (" + hd + ")", Math.Abs(hd - hp * 1.5) <= 1.5);
    Check("and the other needs as the game has them", new[] { "hunger", "comfort", "bladder", "energy", "fun", "social" }.All(n => Drop(dirty, n) == Drop(plain, n)));
    var ep = Drop(plain, "energy"); var ea = Drop(airy, "energy");
    Check("Fresh Air: energy drops 15% slower (" + ea + " against " + ep + ")", Math.Abs(ea - ep * 0.85) <= 1.5);
    Check("Room is the room's own, with the lot's points (" + plain.GetMotiveData(VMMotive.Room) + ", " + dirty.GetMotiveData(VMMotive.Room) + ", " + airy.GetMotiveData(VMMotive.Room) + ")",
        plain.GetMotiveData(VMMotive.Room) == Bare.Score && dirty.GetMotiveData(VMMotive.Room) == Bare.Score - 10 && airy.GetMotiveData(VMMotive.Room) == Bare.Score + 10);
    Check("Mood is worked out again from the changed needs", awake.All(s => s.GetMotiveData(VMMotive.Mood) == NeedRates.MoodOf(s)) && dirty.GetMotiveData(VMMotive.Mood) < plain.GetMotiveData(VMMotive.Mood));
    Check("a need under an interaction's motive change does not drop, on any lot", Drop(eater, "hygiene") == 0);
    Check("sleep gives energy back at the game's own pace on any lot (" + sleeper.GetMotiveData(VMMotive.Energy) + ")",
        sleeper.GetMotiveData(VMMotive.Energy) > 50 && sleeperDirty.GetMotiveData(VMMotive.Energy) == sleeper.GetMotiveData(VMMotive.Energy));
    Check("a sleeping Sim's hygiene drops slower, and Grody still scales it", Drop(sleeperDirty, "hygiene") > Drop(sleeper, "hygiene") && Drop(sleeper, "hygiene") < hp);

    //the patch is off when nothing is set, and when the pack has switched it off
    var before = Bare.Sim(); var after = Bare.Sim();
    NeedRates.EffectFor = null;
    Bare.Run(new[] { before }, 9, 120);
    NeedRates.EffectFor = (avatar, context) => effects.TryGetValue(avatar, out var e) ? e : null;
    effects[after] = NeedEffect.None;
    Bare.Run(new[] { after }, 9, 120);
    Check("with no traits the game's needs are untouched", Needs.Dropping.All(n => before.GetMotiveData(n) == after.GetMotiveData(n)) && after.GetMotiveData(VMMotive.Room) == Bare.Score);
    h.UnpatchAll(h.Id);
    NeedRates.EffectFor = null;
}

// ---- a lot's traits, kept and read back ----
{
    var set = new LotTraitSet();
    Check("a lot starts with none", set.Known(book).Count == 0 && set.Save() == null);
    Check("one trait", set.Add(book.Get("grody"), book) == null);
    Check("not the same one twice", set.Add(book.Get("grody"), book) != null);
    set.Add(book.Get("homey"), book); set.Add(book.Get("convivial"), book);
    Check("not a fourth", set.Add(book.Get("fresh-air"), book) != null && set.Ids.Count == 3);
    var back = LotTraitSet.Load(set.Save());
    Check("kept and read back, in order", string.Join(",", back.Ids) == "grody,homey,convivial");
    Check("the effect is the traits' together", back.Effect(book).Room == -5 && back.Effect(book).Rates[Needs.IndexOf("hygiene")] == 1.5f);

    //a trait traits.json no longer has is kept, does nothing, and leaves room for another
    var old = LotTraitSet.Load(System.Text.Encoding.UTF8.GetBytes("lottraits 1\ngone\ngrody\nGrody\nnot an id\n\n"));
    Check("a trait no longer there is kept but not used", string.Join(",", old.Ids) == "gone,grody" && old.Unknown(book).SequenceEqual(new[] { "gone" }) && old.Known(book).Count == 1);
    old.Add(book.Get("homey"), book);
    Check("and does not count against the three", old.Add(book.Get("convivial"), book) == null && old.Known(book).Count == 3);
    Check("a file that is not the pack's gives no traits", LotTraitSet.Load(System.Text.Encoding.UTF8.GetBytes("grody\n")).Ids.Count == 0 && LotTraitSet.Load(null).Ids.Count == 0);
}

// ---- through Simitone's own LotPackData, as the game calls the pack ----
{
    var userDir = Path.Combine(Path.GetTempPath(), "lottraits-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(userDir);
    var oldUserDir = FSOEnvironment.UserDir;
    FSOEnvironment.UserDir = userDir;
    try
    {
        Lots.Book = book;
        var pack = new LotTraitsPack();
        var lotPath = Path.Combine(userDir, "UserData", "Houses", "House01.iff");
        string[] Files() => Directory.Exists(Path.Combine(userDir, "LocalHouse")) ? Directory.GetFiles(Path.Combine(userDir, "LocalHouse"), "*", SearchOption.AllDirectories) : new string[0];

        var vm = (VM)RuntimeHelpers.GetUninitializedObject(typeof(VM));
        pack.OnLotLoaded(vm, lotPath);
        Check("a lot with no file of the pack's has no traits", Lots.Of(vm).Ids.Count == 0 && Lots.EffectOn(vm).IsNone);
        Lots.Of(vm).Add(book.Get("grody"), book); Lots.Refresh(vm);
        Check("its traits take effect at once", Lots.EffectOn(vm).Rates[Needs.IndexOf("hygiene")] == 1.5f);
        pack.OnLotSaved(vm, lotPath);
        Check("saving the lot keeps them in the pack's own file (" + string.Join(", ", Files().Select(Path.GetFileName)) + ")", Files().Length == 1);

        var later = (VM)RuntimeHelpers.GetUninitializedObject(typeof(VM));
        pack.OnLotLoaded(later, lotPath);
        Check("loading the lot again gives them back", Lots.Of(later).Ids.SequenceEqual(new[] { "grody" }) && Lots.EffectOn(later).Room == -10);
        var other = (VM)RuntimeHelpers.GetUninitializedObject(typeof(VM));
        pack.OnLotLoaded(other, Path.Combine(userDir, "UserData", "Houses", "House02.iff"));
        Check("another lot does not have them", Lots.Of(other).Ids.Count == 0);
        var external = (VM)RuntimeHelpers.GetUninitializedObject(typeof(VM));
        pack.OnLotLoaded(external, null);
        Check("a lot not from a file has none", Lots.Of(external).Ids.Count == 0);

        var stranger = (VM)RuntimeHelpers.GetUninitializedObject(typeof(VM));
        pack.OnLotSaved(stranger, lotPath);
        Check("a lot the pack was not told of leaves the file as it was", Files().Length == 1);

        Lots.Of(later).Ids.Clear(); Lots.Refresh(later);
        pack.OnLotSaved(later, lotPath);
        Check("a lot saved with no traits has no file", Files().Length == 0);
    }
    finally
    {
        FSOEnvironment.UserDir = oldUserDir;
        try { Directory.Delete(userDir, true); } catch { }
    }
}

// ---- the console, away from a lot ----
{
    Check("help is understood", LotTraitsPack.Answer(new[] { "help" }, out var ok) != null && ok);
    Check("the whole list needs no lot", LotTraitsPack.Answer(new[] { "all" }, out ok).Contains("Grody (grody)") && ok);
    Check("a lot's own traits need one", LotTraitsPack.Answer(new string[0], out ok).Contains("in a lot") && ok);
    Check("nonsense is not understood", LotTraitsPack.Answer(new[] { "frobnicate" }, out ok) == null && !ok);
}

Console.WriteLine(failed == 0 ? "All checks passed." : failed + " checks failed.");
return failed == 0 ? 0 : 1;

static string WriteTemp(string text)
{
    var path = Path.Combine(Path.GetTempPath(), "lottraits-" + Guid.NewGuid().ToString("N") + ".json");
    File.WriteAllText(path, text);
    return path;
}

/// <summary>Sims with no lot: just enough of a Sim and a lot for the engine's decay code to run on.</summary>
static class Bare
{
    public const short Score = 20;

    public static bool NoRoom(ref ushort __result) { __result = 0; return false; }
    public static bool RoomScore(ref short __result) { __result = Score; return false; }

    public static VMAvatar Sim()
    {
        var sim = (VMAvatar)RuntimeHelpers.GetUninitializedObject(typeof(VMAvatar));
        Set(sim, "MotiveData", new short[16]);
        Set(sim, "PersonData", new short[101]);
        Set(sim, "MotiveChanges", Enumerable.Range(0, 16).Select(i => new VMMotiveChange() { Motive = (VMMotive)i }).ToArray());
        sim.ObjectData = new short[80];
        foreach (var need in Needs.Dropping) sim.SetMotiveData(need, 80);
        sim.SetPersonData(VMPersonDataVariable.ActivePersonality, 500);
        sim.SetPersonData(VMPersonDataVariable.OutgoingPersonality, 500);
        return sim;
    }

    public static void SetMotiveChange(VMAvatar sim, VMMotive motive, short perHour)
    {
        var changes = (VMMotiveChange[])AccessTools.Field(typeof(VMAvatar), "MotiveChanges").GetValue(sim);
        changes[(int)motive].PerHourChange = perHour;
    }

    private static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);

    /// <summary>Run the engine's decay for some Sims, a tick each game minute from an hour on, as a lot would.</summary>
    public static VMAvatar[] Run(VMAvatar[] sims, int fromHour, int minutes)
    {
        var context = (VMContext)RuntimeHelpers.GetUninitializedObject(typeof(VMContext));
        var clock = new VMClock();
        AccessTools.Property(typeof(VMContext), "Clock").SetValue(context, clock);
        var decays = sims.Select(x => new VMTS1MotiveDecay() { LastMinute = -1 }).ToArray();
        for (int t = 0; t < minutes; t++)
        {
            var at = fromHour * 60 + t;
            clock.Hours = (at / 60) % 24;
            clock.Minutes = at % 60;
            for (int i = 0; i < sims.Length; i++) decays[i].Tick(sims[i], context);
        }
        return sims;
    }
}
