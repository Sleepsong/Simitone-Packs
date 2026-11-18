/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Chemistry pack that need no copy of The Sims: that its patches fit the engine it is built against,
// the rules of chemistry, its file and tuning, and the game's own Relationship primitive run with the patch on it, for
// made-up Sims in a made-up neighbourhood. Run with: dotnet run --project Chemistry/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chemistry;
using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Scopes;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using HarmonyLib;

void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) Environment.ExitCode = 1; }

// ---- the patches fit this Simitone ----
var h = new Harmony("test.chemistry");
var apply = typeof(ChemistryPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
try { apply.Invoke(null, new object[] { h }); }
catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
foreach (var needed in new[] { "VMRelationship.Execute", "UIRelationshipDisplay..ctor", "UIRelationshipDisplay.Update" })
    Check("patch attached: " + needed, patched.Contains(needed));

// ---- traits ----
Facts F(Action<Facts> set = null) { var f = new Facts() { Neat = 500, Outgoing = 500, Active = 500, Playful = 500, Nice = 500, Employed = true, Build = Build.Skinny, Hygiene = 0 }; set?.Invoke(f); return f; }
Check("7 personality points is neat, 3 is sloppy, 5 is neither",
    Traits.Has(F(f => f.Neat = 700), Trait.Neat) && Traits.Has(F(f => f.Neat = 300), Trait.Sloppy) && !Traits.Has(F(), Trait.Neat) && !Traits.Has(F(), Trait.Sloppy));
Check("5 skill points is skilled, 4 is not", Traits.Has(F(f => f.Cooking = 500), Trait.GoodCook) && !Traits.Has(F(f => f.Cooking = 499), Trait.GoodCook));
Check("a job, or none", Traits.Has(F(), Trait.HardWorker) && Traits.Has(F(f => f.Employed = false), Trait.Unemployed) && !Traits.Has(F(), Trait.Unemployed));
Check("stink is hygiene at -50 or less, and unknown hygiene never stinks",
    Traits.Has(F(f => f.Hygiene = -50), Trait.Stink) && !Traits.Has(F(f => f.Hygiene = -49), Trait.Stink) && !Traits.Has(F(f => f.Hygiene = null), Trait.Stink));
Check("clothes and build", Traits.Has(F(f => f.Clothes = Clothes.Swimwear), Trait.Swimwear) && Traits.Has(F(f => f.Build = Build.Fat), Trait.Fat) && !Traits.Has(F(), Trait.Fit));
Check("opposites go both ways, and nobody has both of a pair",
    Traits.All.All(t => Traits.Opposite(t) == null || Traits.Opposite(Traits.Opposite(t).Value) == t)
    && Enumerable.Range(0, 1000).Select(i => F(f => { f.Neat = (short)i; f.Employed = i % 2 == 0; f.Build = (Build)(i % 4); }))
        .All(f => Traits.All.All(t => Traits.Opposite(t) == null || !(Traits.Has(f, t) && Traits.Has(f, Traits.Opposite(t).Value)))));
Check("every trait's word reads back as that trait", Traits.All.All(t => Traits.TryParse(Traits.Word(t), out var back) && back == t));
Check("words are read loosely", Traits.TryParse("Good Cook", out var gc) && gc == Trait.GoodCook && Traits.TryParse("goodcook", out gc) && gc == Trait.GoodCook && !Traits.TryParse("hair", out _));
Check("a body's build, as the game reads it", Sims.BuildOf("B003FAFat_01,BODY=B003FAFatlgt_01") == Build.Fat && Sims.BuildOf("b010maskn_01") == Build.Skinny
    && Sims.BuildOf("B001MAFit_01") == Build.Fit && Sims.BuildOf((string)null) == Build.Unknown);
Check("only grown Sims, not pets, feel chemistry", Sims.IsGrown(27, 0) && Sims.IsGrown(65, 1) && Sims.IsGrown(0, 0) && !Sims.IsGrown(9, 0) && !Sims.IsGrown(15, 1) && !Sims.IsGrown(27, 8) && !Sims.IsGrown(27, 17));

// ---- chemistry ----
var likesNeatCook = new Likes(Trait.Neat, Trait.GoodCook, Trait.Stink);
Check("turn-ons that cannot go together are refused",
    new Likes(Trait.Neat, Trait.Neat, Trait.Stink).Problem() != null && new Likes(Trait.Neat, Trait.Sloppy, Trait.Stink).Problem() != null
    && new Likes(Trait.Neat, Trait.Shy, Trait.Neat).Problem() != null && likesNeatCook.Problem() == null && new Likes(Trait.Neat, Trait.Shy, Trait.Sloppy).Problem() == null);
var plain = F();
var neat = F(f => f.Neat = 800);
var neatCook = F(f => { f.Neat = 800; f.Cooking = 600; });
var smelly = F(f => f.Hygiene = -80);
var smellyNeat = F(f => { f.Hygiene = -80; f.Neat = 800; });
var smellyBoth = F(f => { f.Hygiene = -80; f.Neat = 800; f.Cooking = 600; });
var noLikes = new Likes(Trait.Swimwear, Trait.Sleepwear, Trait.FormalWear); // the plain Sim has none of these
Check("nothing in common is no chemistry", Attraction.Between(likesNeatCook, plain, noLikes, plain) == Level.None);
Check("one turn-on is one bolt", Attraction.Between(likesNeatCook, plain, noLikes, neat) == Level.OneBolt);
Check("both turn-ons are two bolts", Attraction.Between(likesNeatCook, plain, noLikes, neatCook) == Level.TwoBolts);
Check("...and three if it is felt back", Attraction.Between(likesNeatCook, plain, new Likes(Trait.HardWorker, Trait.Sleepwear, Trait.FormalWear), neatCook) == Level.ThreeBolts);
Check("the turn-off alone turns a Sim off", Attraction.Between(likesNeatCook, plain, noLikes, smelly) == Level.TurnedOff);
Check("the turn-off outweighs one turn-on", Attraction.Between(likesNeatCook, plain, noLikes, smellyNeat) == Level.TurnedOff);
Check("...but not both", Attraction.Between(likesNeatCook, plain, noLikes, smellyBoth) == Level.None);
Check("chemistry goes one way", Attraction.Between(likesNeatCook, plain, noLikes, neatCook) == Level.TwoBolts && Attraction.Between(noLikes, neatCook, likesNeatCook, plain) == Level.None);

var drawn = Enumerable.Range(0, 5000).Select(i => Attraction.Default((uint)(0x1000 + i * 7))).ToList();
Check("every Sim's own turn-ons go together", drawn.All(l => l.Problem() == null));
Check("the same Sim always has the same turn-ons", Attraction.Default(0xABCD1234).Same(Attraction.Default(0xABCD1234)));
Check("every trait is somebody's turn-on and somebody's turn-off", Traits.All.All(t => drawn.Any(l => l.On1 == t || l.On2 == t) && drawn.Any(l => l.Off == t)));
Check("Sims next to each other are not alike", Enumerable.Range(0, 50).Count(i => Attraction.Default((uint)i).Same(Attraction.Default((uint)i + 1))) < 5);

// ---- scaling a gain ----
var rng = new Random(7);
int Below(int max) => rng.Next(max);
Check("a loss, or no change, is left alone", Attraction.Scale(-10, 0.5, Below) == -10 && Attraction.Scale(-10, 2.0, Below) == -10 && Attraction.Scale(0, 2.0, Below) == 0);
Check("x1 is the game's own", Attraction.Scale(7, 1.0, Below) == 7);
Check("whole multiples are exact", Attraction.Scale(10, 1.5, Below) == 15 && Attraction.Scale(10, 0.5, Below) == 5 && Attraction.Scale(4, 1.25, Below) == 5 && Attraction.Scale(3, 2.0, Below) == 6);
Check("x0 gives nothing", Attraction.Scale(10, 0.0, Below) == 0);
var halves = Enumerable.Range(0, 20000).Select(_ => Attraction.Scale(3, 0.5, Below)).ToList();
Check("a part of a point is given that often: +3 at half is +1 or +2, +1.5 on average", halves.All(x => x == 1 || x == 2) && Math.Abs(halves.Average() - 1.5) < 0.03);
var ones = Enumerable.Range(0, 20000).Select(_ => Attraction.Scale(1, 1.25, Below)).ToList();
Check("+1 at 1.25 is +1 or +2, +1.25 on average", ones.All(x => x == 1 || x == 2) && Math.Abs(ones.Average() - 1.25) < 0.03);
Check("with no chance to draw, the part is dropped", Attraction.Scale(3, 0.5, null) == 1);

// ---- tuning ----
var t0 = Tuning.Parse(null, out var tp);
Check("no tuning file is the pack's own numbers", tp == null && t0.TurnedOff == 0.5 && t0.None == 1.0 && t0.OneBolt == 1.25 && t0.TwoBolts == 1.5 && t0.ThreeBolts == 2.0);
var t1 = Tuning.Parse("{ \"turnedOff\": 0.8, // gentler\n \"ThreeBolts\": 9, \"none\": -1, }", out tp);
Check("a tuning file changes what it names, within 0 to 4", tp == null && t1.TurnedOff == 0.8 && t1.ThreeBolts == Tuning.Most && t1.None == 0 && t1.OneBolt == 1.25);
var t2 = Tuning.Parse("{ turnedOff: banana", out tp);
Check("a broken tuning file is the pack's own numbers, and says why", tp != null && t2.TurnedOff == 0.5);
var shipped = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "chemistry.json");
Check("the tuning the pack ships reads cleanly, as the pack's own numbers", File.Exists(shipped) && Tuning.Read(Path.GetDirectoryName(Path.GetFullPath(shipped)), out tp).ToString() == new Tuning().ToString() && tp == null);

// ---- the file ----
var some = new Dictionary<uint, Likes>() { { 0x10, likesNeatCook }, { 0xABCDEF01, new Likes(Trait.FormalWear, Trait.Shy, Trait.HardWorker) } };
var back = LikesStore.Read(LikesStore.Write(some), out var skipped);
Check("the file reads back the same", skipped == 0 && back.Count == 2 && back[0x10].Same(likesNeatCook) && back[0xABCDEF01].Same(some[0xABCDEF01]));
var bad = LikesStore.Read("# hello\n0000000A neat good-cook stink\nnonsense\n0000000B neat sloppy stink\nzz neat shy lazy\n0000000C neat shy\n0000000D GOOD-COOK Formal-Wear lazy\n", out skipped);
Check("bad lines are skipped, good ones kept", skipped == 4 && bad.Count == 2 && bad[0xA].Same(likesNeatCook) && bad[0xD].Same(new Likes(Trait.GoodCook, Trait.FormalWear, Trait.Lazy)));

// ---- the game's Relationship primitive, with the patch on, for made-up Sims ----

// a neighbourhood, as the game would have it loaded, in a folder of its own
FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "chemistry-test-" + Guid.NewGuid().ToString("n"));
var neigh = (TS1NeighborhoodProvider)RuntimeHelpers.GetUninitializedObject(typeof(TS1NeighborhoodProvider));
neigh.UserPath = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
Directory.CreateDirectory(neigh.UserPath);
neigh.DirtyAvatars = new HashSet<uint>();
neigh.MainResource = new IffFile();
neigh.Neighbors = new NBRS() { ChunkID = 1, ChunkType = "NBRS", ChunkLabel = "", ChunkProcessed = true, ChunkParent = neigh.MainResource, AddedByPatch = true };
neigh.MainResource.AddChunk(neigh.Neighbors);
var content = (Content)RuntimeHelpers.GetUninitializedObject(typeof(Content));
content.Neighborhood = neigh;
AccessTools.Field(typeof(Content), "INSTANCE").SetValue(null, content);

// a lot
var vm = (VM)RuntimeHelpers.GetUninitializedObject(typeof(VM));
vm.TS1 = true;
vm.Entities = new List<VMEntity>();
var byId = new Dictionary<short, VMEntity>();
AccessTools.Field(typeof(VM), "ObjectsById").SetValue(vm, byId);
var vmContext = (VMContext)RuntimeHelpers.GetUninitializedObject(typeof(VMContext));
vmContext.VM = vm;
vmContext.RandomSeed = 0x123456789ABCDEFUL;
AccessTools.PropertySetter(typeof(VM), "Context").Invoke(vm, new object[] { vmContext });

VMAvatar Sim(string name, short objectId, short neighbourId, uint guid, short age, params (VMPersonDataVariable v, short value)[] data)
{
    var sim = (VMAvatar)RuntimeHelpers.GetUninitializedObject(typeof(VMAvatar));
    sim.MultitileGroup = (FSO.SimAntics.Entities.VMMultitileGroup)RuntimeHelpers.GetUninitializedObject(typeof(FSO.SimAntics.Entities.VMMultitileGroup));
    sim.MultitileGroup.Name = name;
    var pd = new short[101];
    pd[(int)VMPersonDataVariable.PersonsAge] = age;
    pd[(int)VMPersonDataVariable.NeighborId] = neighbourId;
    foreach (var (v, value) in data) pd[(int)v] = value;
    AccessTools.Field(typeof(VMAvatar), "PersonData").SetValue(sim, pd);
    AccessTools.Field(typeof(VMAvatar), "MotiveData").SetValue(sim, new short[16]);
    sim.MeToObject = new Dictionary<ushort, List<short>>();
    sim.MeToPersist = new Dictionary<uint, List<short>>();
    sim.MayHaveRelToMe = new HashSet<ushort>();
    sim.ObjectID = objectId;
    sim.Object = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));
    sim.Object.OBJ = new OBJD() { GUID = guid };
    var thread = (VMThread)RuntimeHelpers.GetUninitializedObject(typeof(VMThread));
    thread.Context = vmContext;
    thread.Queue = new List<VMQueuedAction>();
    thread.ActiveQueueBlock = -1;
    sim.Thread = thread;
    vm.Entities.Add(sim);
    byId[objectId] = sim;
    if (neighbourId > 0)
        neigh.Neighbors.AddNeighbor(new Neighbour() { Name = "iffname", NeighbourID = neighbourId, GUID = guid, PersonMode = 9, PersonData = pd, Relationships = new Dictionary<int, List<short>>() });
    return sim;
}

// Ann is turned on by neat Sims and good cooks, off by stink. Bob is neat and cooks well.
// Bob is turned on by shy Sims and fat ones, off by unemployment, and Ann has no job: he is turned off by her.
var ann = Sim("Ann", 10, 1, 0x2001, 27, (VMPersonDataVariable.OutgoingPersonality, 800));
var bob = Sim("Bob", 11, 2, 0x2002, 27, (VMPersonDataVariable.NeatPersonality, 800), (VMPersonDataVariable.CookingSkill, 600), (VMPersonDataVariable.JobType, 3));
var kid = Sim("Kid", 12, 3, 0x2003, 9, (VMPersonDataVariable.NeatPersonality, 800), (VMPersonDataVariable.CookingSkill, 600));
var maid = Sim("Maid", 13, 0, 0x2004, 27, (VMPersonDataVariable.NeatPersonality, 800), (VMPersonDataVariable.CookingSkill, 600)); // no neighbour number: her scores are the lot's
LikesStore.Set(0x2001, likesNeatCook);
LikesStore.Set(0x2002, new Likes(Trait.Shy, Trait.Fat, Trait.Unemployed));
LikesStore.Set(0x2003, likesNeatCook); // (a child has none in play: this is only to show it is not used)
LikesStore.Set(0x2004, new Likes(Trait.Shy, Trait.Fat, Trait.Lazy));

Check("Ann has two bolts for Bob, and Bob is turned off by her", Sims.Between(ann, bob) == Level.TwoBolts && Sims.Between(bob, ann) == Level.TurnedOff);
Check("a child has no chemistry, either way", Sims.Between(ann, kid) == Level.None && Sims.Between(kid, ann) == Level.None);
Check("chemistry from the neighbourhood's own data, for the relationship panel", Sims.Between(neigh.GetNeighborByID(1), neigh.GetNeighborByID(2), null) == Level.TwoBolts);

void Busy(VMAvatar who, VMEntity with) { who.Thread.Queue.Clear(); who.Thread.Queue.Add(new VMQueuedAction() { Callee = with }); who.Thread.ActiveQueueBlock = 0; }
void Idle(VMAvatar who) { who.Thread.Queue.Clear(); who.Thread.ActiveQueueBlock = -1; }
void IdleAll() { foreach (var s in new[] { ann, bob, kid, maid }) Idle(s); }

List<short> Scores(VMAvatar from, VMAvatar to)
{
    var f = from.GetPersonData(VMPersonDataVariable.NeighborId);
    var t = to.GetPersonData(VMPersonDataVariable.NeighborId);
    if (f > 0 && t > 0) return neigh.GetNeighborByID(f).Relationships.TryGetValue(t, out var n) ? n : null;
    return from.MeToObject.TryGetValue((ushort)to.ObjectID, out var l) ? l : null;
}
void SetScore(VMAvatar from, VMAvatar to, int index, short value)
{
    var f = from.GetPersonData(VMPersonDataVariable.NeighborId);
    var t = to.GetPersonData(VMPersonDataVariable.NeighborId);
    Dictionary<int, List<short>> nd = null;
    List<short> list;
    if (f > 0 && t > 0) { nd = neigh.GetNeighborByID(f).Relationships; if (!nd.TryGetValue(t, out list)) nd[t] = list = new List<short>(); }
    else if (!from.MeToObject.TryGetValue((ushort)to.ObjectID, out list)) from.MeToObject[(ushort)to.ObjectID] = list = new List<short>();
    while (list.Count < 3) list.Add(0);
    list[index] = value;
}
short Score(VMAvatar from, VMAvatar to, int index) { var s = Scores(from, to); return s != null && s.Count > index ? s[index] : (short)0; }

VMStackFrame Frame(VMAvatar caller, VMAvatar stack)
{
    var frame = new VMStackFrame() { Thread = caller.Thread, Caller = caller, Callee = stack, Locals = new short[4], Args = new short[4] };
    frame.StackObject = stack;
    return frame;
}
VMRelationshipOperand Op(int index, int setMode, short value, byte mode = 0)
{
    var op = new VMRelationshipOperand() { RelVar = (byte)index, Mode = mode, VarScope = VMVariableScope.Literal, VarData = value };
    op.SetMode = setMode;
    return op;
}
var primitive = new VMRelationship();
// as a social script does it: the Sim running it is the caller, the other Sim the stack object
short Run(VMAvatar caller, VMAvatar stack, VMRelationshipOperand op, short before, bool fromCaller = true)
{
    var (from, to) = fromCaller ? (caller, stack) : (stack, caller);
    SetScore(from, to, op.RelVar, before);
    primitive.Execute(Frame(caller, stack), op);
    return Score(from, to, op.RelVar);
}
const int Daily = Gains.Daily, Lifetime = Gains.Lifetime, Flags = 1, Add = 2, Set = 1, Get = 0;

IdleAll(); Busy(ann, bob);
Check("Ann talks to Bob: her daily +10 is +15 at two bolts", Run(ann, bob, Op(Daily, Add, 10), 20) == 35);
Check("...and her lifetime +4 is +6", Run(ann, bob, Op(Lifetime, Add, 4), 10) == 16);
Check("...and his for her, +10, is +5: he is turned off (mode 1, from the stack object to the caller)", Run(ann, bob, Op(Daily, Add, 10, mode: 1), 20, fromCaller: false) == 25);
Check("a loss is the game's own", Run(ann, bob, Op(Daily, Add, -10), 20) == 10 && Run(ann, bob, Op(Daily, Add, -10, mode: 1), 20, fromCaller: false) == 10);
Check("a script that works the score out and sets it is scaled the same: 20 set to 30 is 35", Run(ann, bob, Op(Daily, Set, 30), 20) == 35);
Check("reading a score changes nothing", Run(ann, bob, Op(Daily, Get, 0), 20) == 20);
Check("the score that is not an amount (friends, in love) is never scaled", Run(ann, bob, Op(Flags, Add, 4), 0) == 4 && Run(ann, bob, Op(Flags, Set, 3), 0) == 3);
Check("the top is still 100", Run(ann, bob, Op(Daily, Add, 10), 95) == 100);
Check("a gain cut off at 100 by the game is scaled from what it gave", Run(ann, bob, Op(Daily, Add, 10, mode: 1), 98, fromCaller: false) == 99);

IdleAll(); Busy(bob, ann);
Check("the same from the other end: Bob is busy with Ann, and Ann's gain is still scaled", Run(bob, ann, Op(Daily, Add, 10, mode: 1), 20, fromCaller: false) == 35);

IdleAll();
Check("neither busy with the other (the night's change, a pack setting scores): the game's own", Run(ann, bob, Op(Daily, Add, 10), 20) == 30);
Busy(ann, maid);
Check("Ann busy with someone else: the game's own", Run(ann, bob, Op(Daily, Add, 10), 20) == 30);

IdleAll(); Busy(ann, kid);
Check("a child: the game's own, both ways", Run(ann, kid, Op(Daily, Add, 10), 20) == 30 && Run(ann, kid, Op(Daily, Add, 10, mode: 1), 20, fromCaller: false) == 30);

IdleAll(); Busy(ann, maid);
Check("a Sim with no neighbour number: the lot's own scores, scaled the same", Run(ann, maid, Op(Daily, Add, 10), 20) == 35 && Scores(ann, maid) == ann.MeToObject[(ushort)maid.ObjectID]);

// the old form of the primitive, which takes its amount from the tree's parameters
IdleAll(); Busy(ann, bob);
var oldOp = new VMOldRelationshipOperand();
oldOp.Read(new byte[] { 2 /*add*/, Daily, 0 /*parameter 0*/, 0 /*me to stack object*/, 0 });
var oldFrame = Frame(ann, bob); oldFrame.Args[0] = 10;
SetScore(ann, bob, Daily, 20);
primitive.Execute(oldFrame, oldOp);
Check("the old form of the primitive is scaled too", Score(ann, bob, Daily) == 35);

// from the stack object to a Sim named in a local
var localOp = Op(Daily, Add, 10, mode: 2); localOp.Local = 1;
var localFrame = Frame(bob, ann); localFrame.Locals[1] = bob.ObjectID; // stack object Ann, to Bob
SetScore(ann, bob, Daily, 20);
primitive.Execute(localFrame, localOp);
Check("from the stack object to a Sim in a local", Score(ann, bob, Daily) == 35);

// by neighbour numbers, as scripts do for Sims named by their number
var nOp = Op(Daily, Add, 10, mode: 0); nOp.UseNeighbor = true;
var nFrame = Frame(ann, bob); nFrame._StackObjectID = bob.GetPersonData(VMPersonDataVariable.NeighborId);
SetScore(ann, bob, Daily, 20);
primitive.Execute(nFrame, nOp);
Check("by neighbour numbers, with both Sims here", Score(ann, bob, Daily) == 35);

// three bolts: Bob now likes Sims who are outgoing, as Ann is, and his turn-off is something she is not
// (not lazy: Ann has no points in Active, so she is lazy)
LikesStore.Set(0x2002, new Likes(Trait.Outgoing, Trait.Fat, Trait.Swimwear));
Check("three bolts, felt back", Sims.Between(ann, bob) == Level.ThreeBolts && Sims.Between(bob, ann) == Level.OneBolt);
Check("...doubles her gain, and his one bolt makes +8 into +10", Run(ann, bob, Op(Daily, Add, 10), 20) == 40 && Run(ann, bob, Op(Daily, Add, 8, mode: 1), 20, fromCaller: false) == 30);

// what she wears and how clean she is
ann.SetMotiveData(VMMotive.Hygiene, -90);
LikesStore.Set(0x2002, new Likes(Trait.Outgoing, Trait.Fat, Trait.Stink));
Check("Ann stinks: Bob's one bolt is gone, and he is turned off", Sims.Between(bob, ann) == Level.TurnedOff);
ann.SetMotiveData(VMMotive.Hygiene, 0);

// the tuning, changed
var keep = Gains.Tuning;
Gains.Tuning = Tuning.Parse("{ \"twoBolts\": 1.0, \"turnedOff\": 0 }", out _);
LikesStore.Set(0x2002, new Likes(Trait.Shy, Trait.Fat, Trait.Unemployed));
Check("with two bolts tuned to x1, the game's own", Run(ann, bob, Op(Daily, Add, 10), 20) == 30);
Check("with turned off tuned to x0, no gain at all", Run(ann, bob, Op(Daily, Add, 10, mode: 1), 20, fromCaller: false) == 20);
Gains.Tuning = keep;

// ---- the console ----
string Say(string line, VMAvatar selected, out bool understood) => ChemistryPack.Answer(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), vm, selected, out understood);
var said = Say("set playful nice lazy", ann, out var u);
Check("chemistry set chooses the selected Sim's", u && LikesStore.For(0x2001).Same(new Likes(Trait.Playful, Trait.Nice, Trait.Lazy)) && said.Contains("playful"));
Check("...and writes them down", File.Exists(Path.Combine(neigh.UserPath, LikesStore.FileName))
    && LikesStore.Read(File.ReadAllText(Path.Combine(neigh.UserPath, LikesStore.FileName)), out skipped)[0x2001].Same(new Likes(Trait.Playful, Trait.Nice, Trait.Lazy)) && skipped == 0);
Say("set neat sloppy stink", ann, out u);
Check("turn-ons that cannot go together are refused, and change nothing", u && LikesStore.For(0x2001).Same(new Likes(Trait.Playful, Trait.Nice, Trait.Lazy)));
Say("set neat hair stink", ann, out u);
Check("a word that is not a trait is refused", u && LikesStore.For(0x2001).Same(new Likes(Trait.Playful, Trait.Nice, Trait.Lazy)));
said = Say("", kid, out u);
Check("a child is told they have no turn-ons", u && said.Contains("grown"));
said = Say("", ann, out u);
Check("the selected Sim's turn-ons and chemistry with the grown Sims here", u && said.Contains("playful") && said.Split('\n').Length == 3);
said = Say("bob", ann, out u);
Check("chemistry with a name is that Sim's", u && said.StartsWith("Bob is") && said.Contains("Ann: turned off"));
Say("set neat nice lazy Bob", ann, out u);
Check("chemistry set with a name chooses that Sim's", u && LikesStore.For(0x2002).Same(new Likes(Trait.Neat, Trait.Nice, Trait.Lazy)) && LikesStore.For(0x2001).Same(new Likes(Trait.Playful, Trait.Nice, Trait.Lazy)));
said = Say("set neat nice lazy Kid", ann, out u);
Check("...but not a child's", u && said.Contains("No grown Sim") && LikesStore.For(0x2003).Same(likesNeatCook));
Say("nonsense", ann, out u);
Check("a word the command does not know is not understood", !u);
Check("help and the lists answer outside a lot", ChemistryPack.Answer(new[] { "help" }, null, null, out u) == ChemistryPack.Help && ChemistryPack.Answer(new[] { "traits" }, null, null, out u).Contains("good-cook")
    && ChemistryPack.Answer(new[] { "set", "neat", "shy", "lazy" }, null, null, out u).Contains("lot"));

// ---- how often each level comes about ----
// Not a check: an estimate of the balance, for made-up Sims spread as below. Personality is 25 points spread at
// random, as Create a Sim asks; skills 0 to 7 points; two in three have a job; fit, fat or skinny alike; one in five
// stinks; one in ten is in swimwear, formal wear or pyjamas.
var r = new Random(1);
Facts RandomSim()
{
    var p = new int[5];
    for (int i = 0; i < 25; i++) { int k; do k = r.Next(5); while (p[k] == 10); p[k]++; }
    short S() => (short)(r.Next(8) * 100);
    return new Facts()
    {
        Neat = (short)(p[0] * 100), Outgoing = (short)(p[1] * 100), Active = (short)(p[2] * 100), Playful = (short)(p[3] * 100), Nice = (short)(p[4] * 100),
        Cooking = S(), Charisma = S(), Mechanical = S(), Creativity = S(), Body = S(), Logic = S(),
        Employed = r.Next(3) > 0, Build = (Build)(1 + r.Next(3)), Hygiene = (short)(r.Next(5) == 0 ? -70 : 30),
        Clothes = r.Next(10) == 0 ? (Clothes)(1 + r.Next(3)) : Clothes.Everyday,
    };
}
var counts = new Dictionary<Level, int>();
double total = 0;
const int Pairs = 50000;
for (int i = 0; i < Pairs; i++)
{
    var level = Attraction.Between(Attraction.Default((uint)r.Next()), RandomSim(), Attraction.Default((uint)r.Next()), RandomSim());
    counts[level] = counts.TryGetValue(level, out var c) ? c + 1 : 1;
    total += new Tuning().Multiplier(level);
}
Console.WriteLine("     (estimate) " + string.Join(", ", Enum.GetValues<Level>().Select(l => Attraction.Words(l).ToLowerInvariant() + " " + (100.0 * (counts.TryGetValue(l, out var c) ? c : 0) / Pairs).ToString("0") + "%"))
    + "; average gain x" + (total / Pairs).ToString("0.00"));
Check("every level of chemistry comes about", Enum.GetValues<Level>().All(l => counts.ContainsKey(l)));

try { Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true); } catch (Exception) { }
return Environment.ExitCode;
