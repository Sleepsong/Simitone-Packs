/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Family Tree pack that need no copy of The Sims: that its patches fit the engine it is built against,
// the rules of the tree, its file, and a family being made with ties (against a made-up neighbourhood, written with
// the game's own file format). Run with: dotnet run --project FamilyTree/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FamilyTree;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using HarmonyLib;

void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) Environment.ExitCode = 1; }

// ---- the patches fit this Simitone ----
var h = new Harmony("test.familytree");
var apply = typeof(FamilyTreePack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
try { apply.Invoke(null, new object[] { h }); }
catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
var patched = h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
foreach (var needed in new[] { "TS1CASScreen.Update", "TS1CASScreen.ModifySim", "SimitoneNeighbourGenerator.CreateFamily", "UIRelationshipDisplay..ctor", "UILotControl.Update", "UIMainPanel.AddPacksButton" })
    Check("patch attached: " + needed, patched.Contains(needed));

// ---- the rules. Sims are numbers: 1 and 2 are adults, 3 and 4 children, 5 to 9 adults ----
bool Adult(uint x) => x != 3 && x != 4;
string Name(uint x) => "S" + x;
string Why(Kin k, uint a, uint b, Choice c) => k.WhyNot(a, b, c, Adult, Name);

// 5 is the parent of 1 and 2: making 1 the parent of 2 as well must be refused
bool ParentOfSibling() { var k = new Kin(); k.Set(5, 1, Choice.Parent); k.Set(5, 2, Choice.Parent); return k.WhyNot(1, 2, Choice.Parent, Adult, Name) != null; }

var kin = new Kin();
Check("strangers have no tie", kin.Between(1, 2) == Tie.None && kin.ChoiceFor(1, 2) == Choice.Housemate);
Check("two adults can marry", Why(kin, 1, 2, Choice.Spouse) == null);
kin.Set(1, 2, Choice.Spouse);
Check("married, both ways", kin.Between(1, 2) == Tie.Spouse && kin.Between(2, 1) == Tie.Spouse && kin.SpouseOf(2) == 1);
Check("a child cannot marry", Why(kin, 3, 5, Choice.Spouse) != null);
Check("nobody has two spouses", Why(kin, 1, 5, Choice.Spouse) != null);
Check("a child cannot be a parent", Why(kin, 3, 4, Choice.Parent) != null && Why(kin, 4, 3, Choice.Child) != null);
kin.Set(1, 3, Choice.Parent);
kin.Set(3, 2, Choice.Child); // "3 is 2's child"
Check("parent and child, read from either end", kin.Between(3, 1) == Tie.Parent && kin.Between(1, 3) == Tie.Child && kin.ChoiceFor(3, 1) == Choice.Child && kin.ChoiceFor(2, 3) == Choice.Parent);
Check("a third parent is refused", Why(kin, 5, 3, Choice.Parent) != null);
kin.Set(1, 4, Choice.Parent);
Check("children of one parent are siblings", kin.Between(3, 4) == Tie.Sibling && kin.SiblingsOf(4).SequenceEqual(new uint[] { 3 }));
Check("the other parent's spouse is a step-parent", kin.Between(4, 2) == Tie.StepParent && kin.Between(2, 4) == Tie.StepChild);
Check("a sibling cannot also be a parent", Why(kin, 3, 4, Choice.Sibling) == null && kin.Clone().FirstProblem(Adult, Name) == null && ParentOfSibling());
kin.Set(5, 1, Choice.Parent);
Check("grandparent and grandchild", kin.Between(3, 5) == Tie.Grandparent && kin.Between(5, 4) == Tie.Grandchild);
Check("nobody is their own ancestor", Why(kin, 3, 5, Choice.Parent) != null);
Check("a grandparent cannot become a sibling", Why(kin, 5, 3, Choice.Sibling) != null);
Check("parent-in-law and child-in-law", kin.Between(2, 5) == Tie.ParentInLaw && kin.Between(5, 2) == Tie.ChildInLaw);
kin.Set(6, 1, Choice.Sibling);
Check("siblings by a link of their own", kin.Between(1, 6) == Tie.Sibling && kin.Between(6, 1) == Tie.Sibling);
Check("aunt or uncle, niece or nephew", kin.Between(3, 6) == Tie.AuntUncle && kin.Between(6, 3) == Tie.NieceNephew);
kin.Set(6, 7, Choice.Parent);
Check("cousins", kin.Between(3, 7) == Tie.Cousin && kin.Between(7, 4) == Tie.Cousin);
// grown-up cousins: 8 is 6's child and 9 is 1's, and 6 and 1 are siblings
kin.Set(6, 8, Choice.Parent); kin.Set(1, 9, Choice.Parent);
Check("strangers can marry, cousins cannot", Why(kin, 7, 5, Choice.Spouse) == null && kin.Between(8, 9) == Tie.Cousin && Why(kin, 8, 9, Choice.Spouse) != null);
Check("an aunt or uncle cannot marry a niece or nephew", kin.Between(9, 6) == Tie.AuntUncle && Why(kin, 6, 9, Choice.Spouse) != null);
Check("siblings through a parent cannot marry", kin.Between(8, 7) == Tie.Sibling && Why(kin, 8, 7, Choice.Spouse) != null);
Check("a refusal changes nothing", kin.ChoiceFor(8, 9) == Choice.Housemate && kin.ChoiceFor(6, 9) == Choice.Housemate && kin.SpouseOf(8) == null);
Check("a sound tree has no problem", kin.FirstProblem(Adult, Name) == null);

// a choice that breaks something further away: two spouses given the same parent
var far = new Kin();
far.Set(1, 2, Choice.Spouse);
far.Set(5, 1, Choice.Parent);
Check("spouses cannot be given the same parent", Why(far, 5, 2, Choice.Parent) != null);
Check("...but another parent each is fine", Why(far, 6, 2, Choice.Parent) == null);
Check("a married couple who are found to be family are still told apart from strangers", far.ByBirth(1, 2) == Tie.None && far.Between(1, 2) == Tie.Spouse);
// one sibling's parent is not the other's by itself, and may not also be made their sibling
var sib = new Kin();
sib.Set(1, 2, Choice.Sibling); sib.Set(2, 5, Choice.Sibling);
Check("a sibling's sibling is a sibling", sib.Between(1, 5) == Tie.Sibling);
sib.Set(6, 1, Choice.Parent);
Check("a parent of one sibling is not made the parent of the others", sib.Between(2, 6) == Tie.None);

// changing a pair replaces what was there
var swap = new Kin();
swap.Set(1, 2, Choice.Spouse); swap.Set(1, 2, Choice.Sibling);
Check("a new choice for a pair replaces the old one", swap.Links.Count == 1 && swap.Between(1, 2) == Tie.Sibling);
swap.Set(1, 2, Choice.Housemate);
Check("housemates keeps nothing", swap.Links.Count == 0);

// mending: a Sim who was married is changed into a child
var mend = new Kin();
mend.Set(0, 1, Choice.Spouse); mend.Set(0, 2, Choice.Parent); mend.Set(1, 2, Choice.Parent);
var said = mend.Mend(x => x == 1, x => "S" + x); // only Sim 1 is still an adult
Check("mending takes out what a child cannot have, and keeps the rest", said.Count == 2 && mend.Links.Count == 1 && mend.Between(2, 1) == Tie.Parent);

// a Sim is taken out of a family being made
var places = new Kin();
places.Set(0, 1, Choice.Spouse); places.Set(1, 3, Choice.Parent); places.Set(2, 3, Choice.Sibling);
places.RemoveAt(1);
Check("taking a Sim out drops their ties and moves the rest down", places.Links.Count == 1 && places.Between(1, 2) == Tie.Sibling);

// ---- the file ----
var text = kin.Write();
var back = Kin.Read(text, out var skipped);
Check("the file reads back the same", skipped == 0 && back.Links.Count == kin.Links.Count && back.Links.Zip(kin.Links).All(p => p.First.Kind == p.Second.Kind && p.First.A == p.Second.A && p.First.B == p.Second.B));
var bad = Kin.Read("# hello\nspouse 0000000A 0000000B\nnonsense\nparent zz 1\nparent 0000000A 0000000A\nsibling 0000000B 0000000A\nPARENT  c   d\n", out skipped);
Check("bad lines are skipped, good ones kept", bad.Links.Count == 2 && skipped == 4 && bad.Between(10, 11) == Tie.Spouse && bad.Between(13, 12) == Tie.Parent);
Check("nothing reads as an empty tree", Kin.Read(null, out skipped).Links.Count == 0 && skipped == 0);

// ---- words ----
Check("the words for a tie", TieNames.For(Tie.Spouse, true) == "Wife" && TieNames.For(Tie.AuntUncle, false) == "Uncle" && TieNames.For(Tie.None, true) == null);
Check("every tie has words and a starting score, and strangers have neither",
    Enum.GetValues<Tie>().Where(t => t != Tie.None).All(t => TieNames.For(t, false) != null && TieNames.For(t, true) != null && StartingScores.For(t) != null) && StartingScores.For(Tie.None) == null);
Check("a married couple starts closest", Enum.GetValues<Tie>().Where(t => t != Tie.None && t != Tie.Spouse).All(t => StartingScores.For(t).Value.Item1 < StartingScores.For(Tie.Spouse).Value.Item1));

// ---- a family is made with ties, in a made-up neighbourhood ----
FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "familytree-test-" + Guid.NewGuid().ToString("n"));
var neigh = (FSO.Content.TS1.TS1NeighborhoodProvider)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FSO.Content.TS1.TS1NeighborhoodProvider));
neigh.UserPath = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
Directory.CreateDirectory(neigh.UserPath);
neigh.DirtyAvatars = new HashSet<uint>();
neigh.MainResource = new IffFile();
neigh.Neighbors = new NBRS() { ChunkID = 1, ChunkType = "NBRS", ChunkLabel = "", ChunkProcessed = true, ChunkParent = neigh.MainResource, AddedByPatch = true };
neigh.MainResource.AddChunk(neigh.Neighbors);
People.StandIn = neigh;

// as the game makes a family of four: mother, father, two children
var guids = new uint[] { 0x1000, 0x1001, 0x1002, 0x1003 };
var family = new FAMI() { ChunkID = 5, ChunkType = "FAMI", ChunkLabel = "", ChunkProcessed = true, ChunkParent = neigh.MainResource, AddedByPatch = true, FamilyGUIDs = guids };
neigh.MainResource.AddChunk(family);
var infos = new[] { new SimTemplateCreateInfo("fafit", "lgt") { Name = "Ma" }, new SimTemplateCreateInfo("mafit", "lgt") { Name = "Pa" }, new SimTemplateCreateInfo("fcchd", "lgt") { Name = "Kid" }, new SimTemplateCreateInfo("mcchd", "lgt") { Name = "Lad" } };
for (int i = 0; i < guids.Length; i++)
{
    var data = new short[88];
    data[(int)VMPersonDataVariable.PersonsAge] = (short)(infos[i].Child ? 9 : 27);
    data[(int)VMPersonDataVariable.Gender] = infos[i].Gender;
    neigh.Neighbors.AddNeighbor(new Neighbour() { Name = "iffname", NeighbourID = (short)(i + 1), GUID = guids[i], PersonMode = 9, PersonData = data, Relationships = new Dictionary<int, List<short>>() });
}
Check("the game's facts about a Sim", People.Exists(0x1002) && !People.Exists(0x2000) && People.IsAdult(0x1000) && !People.IsAdult(0x1002) && People.IsFemale(0x1000) && !People.IsFemale(0x1001) && People.FamilyOf(0x1003) == family);

// the two already dislike each other one way: starting scores must not lower or raise that the wrong way
neigh.Neighbors.NeighbourByID[1].Relationships[2] = new List<short>() { 95 };

Draft.Ties = new Kin();
Draft.Ties.Set(0, 1, Choice.Spouse);
Draft.Ties.Set(0, 2, Choice.Parent);
Draft.Ties.Set(1, 2, Choice.Parent);
Draft.Ties.Set(0, 3, Choice.Parent);
Draft.Ties.Set(7, 8, Choice.Sibling); // left over from a bigger family: must be dropped
Draft.Keep(family, infos);

Check("the draft is emptied once kept", Draft.Ties.Links.Count == 0);
var tree = KinStore.Get();
Check("the ties are in the neighbourhood's tree, under the new characters", tree.Links.Count == 4 && tree.Between(0x1000, 0x1001) == Tie.Spouse && tree.Between(0x1002, 0x1001) == Tie.Parent && tree.Between(0x1002, 0x1003) == Tie.Sibling && tree.Between(0x1003, 0x1001) == Tie.StepParent);
Check("the tree's file is in the neighbourhood's folder", File.Exists(Path.Combine(neigh.UserPath, KinStore.FileName)) && Kin.Read(File.ReadAllText(KinStore.PathNow), out _).Links.Count == 4);

// read the neighbourhood back from the file the game's own code wrote
var saved = new IffFile(Path.Combine(neigh.UserPath, "Neighborhood.iff"));
var nbrs = saved.List<NBRS>()[0];
List<short> Scores(int from, int to) => nbrs.NeighbourByID[(short)from].Relationships.TryGetValue(to, out var v) ? v : null;
Check("the couple start married, both ways, in the saved neighbourhood", Scores(2, 1) != null && Scores(2, 1)[People.DailyScore] == 90 && Scores(2, 1)[People.LifetimeScore] == 80 && Scores(1, 2)[People.LifetimeScore] == 80);
Check("a score that was already higher is left alone", Scores(1, 2)[People.DailyScore] == 95);
Check("parent and child start close", Scores(1, 3)[People.DailyScore] == 75 && Scores(3, 2)[People.DailyScore] == 75);
Check("siblings through a parent start as siblings", Scores(3, 4)[People.DailyScore] == 60 && Scores(4, 3)[People.LifetimeScore] == 50);
Check("a step-parent starts less close", Scores(2, 4)[People.DailyScore] == 40);
Check("who a Sim has met", People.Met(0x1000).OrderBy(x => x).SequenceEqual(new uint[] { 0x1001, 0x1002, 0x1003 }));

// the tree window's rows, and who can be given a tie
var rows = UIFamilyTreeDialog.Relatives(tree, 0x1002);
Check("the tree of a child: parents, then brothers and sisters", rows.Count == 2 && rows[0].Item1 == Tie.Parent && rows[0].Item2.Count == 2 && rows[1].Item1 == Tie.Sibling);
Check("someone with no family has an empty tree", UIFamilyTreeDialog.Relatives(tree, 0x2000).Count == 0);
Check("candidates for a tie: the household first, never the Sim themselves", UIFamilyTreeDialog.Candidates(tree, 0x1000).SequenceEqual(new uint[] { 0x1001, 0x1002, 0x1003 }));

// a family made with no ties chosen changes nothing
var before = File.GetLastWriteTimeUtc(KinStore.PathNow);
Draft.Ties = new Kin();
Draft.Keep(family, infos);
Check("a family with no ties leaves the tree alone", KinStore.Get().Links.Count == 4 && File.GetLastWriteTimeUtc(KinStore.PathNow) == before);

// a tie chosen for a Sim who was then made a child is left out, not kept wrongly
Draft.Ties = new Kin();
Draft.Ties.Set(2, 3, Choice.Spouse);
Draft.Keep(family, infos);
Check("a tie the rules refuse is not kept", KinStore.Get().Between(0x1002, 0x1003) == Tie.Sibling);

// the ties window's words
var subject = new TiesSubject() { Kin = tree, Name = People.FirstName, IsFemale = People.IsFemale };
Check("the button for a pair says the choice", UIFamilyTiesDialog.ButtonText(Choice.Parent, "Ma Smith", "Kid Smith") == "Ma is parent" && UIFamilyTiesDialog.ButtonText(Choice.Child, "Ma", "Bartholomew") == "Bartholo. is parent");
Check("a pair in a sentence", UIFamilyTiesDialog.Sentence(subject, 0x1001, 0x1000).EndsWith("'s wife.") && UIFamilyTiesDialog.Sentence(subject, 0x1002, 0x1003).EndsWith("brother, through the rest of the family."));

try { Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true); } catch { }
Console.WriteLine(Environment.ExitCode == 0 ? "All checks passed." : "SOME CHECKS FAILED.");
return Environment.ExitCode;
