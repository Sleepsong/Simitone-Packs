/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Apartments pack that need no copy of The Sims: that its patches fit the engine it is built against,
// and its own rules (who lives where, the pack's file for a lot, rent). Run with: dotnet run --project Apartments/tests
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Apartments;

var h = new Harmony("test.apartments");
var apply = typeof(ApartmentsPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
try { apply.Invoke(null, new object[] { h }); }
catch (TargetInvocationException e) { Console.WriteLine("FAILED: " + e.InnerException); return 1; }
foreach (var m in h.GetPatchedMethods()) {
    var info = Harmony.GetPatchInfo(m);
    Console.WriteLine($"patched {m.DeclaringType.Name}.{m.Name}: prefixes={info.Prefixes.Count} postfixes={info.Postfixes.Count}");
}
// the per-tick hook must be harmless on a VM it knows nothing about
Console.WriteLine("IsShared(null) = " + Neighbours.IsShared(null) + ", HomeHouse(null) = " + Neighbours.HomeHouse(null));

// ---- residency, against a made-up neighbourhood ----
FSO.Common.FSOEnvironment.UserDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apt-test-" + Guid.NewGuid().ToString("n"));
System.IO.Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);
var neigh = (FSO.Content.TS1.TS1NeighborhoodProvider)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FSO.Content.TS1.TS1NeighborhoodProvider));
neigh.UserPath = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/");
neigh.FamilyForHouse = new System.Collections.Generic.Dictionary<short, FSO.Files.Formats.IFF.Chunks.FAMI>();
neigh.MainResource = new FSO.Files.Formats.IFF.IffFile();
FSO.Files.Formats.IFF.Chunks.FAMI Fam(ushort id, int house, int sims) {
    var f = new FSO.Files.Formats.IFF.Chunks.FAMI() { ChunkID = id, ChunkType = "FAMI", ChunkLabel = "", ChunkProcessed = true, HouseNumber = house, FamilyGUIDs = Enumerable.Range(0, sims).Select(i => (uint)(id * 100 + i)).ToArray() };
    neigh.MainResource.AddChunk(f);
    return f;
}
var a = Fam(3, 5, 2); var b = Fam(4, 5, 1); var c = Fam(6, 0, 3); var d = Fam(7, 9, 1); var townie = Fam(8, 0, 0);
foreach (var f in new[] { a, b, d }) neigh.FamilyForHouse[(short)f.HouseNumber] = f; // as the game fills it: last one wins
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) Environment.ExitCode = 1; }
Check("house 5 has two residents", Residency.Residents(5, neigh).Count == 2);
Check("house 9 has one resident", Residency.Residents(9, neigh).Count == 1);
Check("game's table starts on the last family (4)", neigh.FamilyForHouse[5] == b);
Residency.FixTable(neigh);
Check("with no pack file, the first resident (3) is played", neigh.FamilyForHouse[5] == a);
Residency.WritePlayed(5, 4, neigh);
Check("played household reads back as 4", Residency.ReadPlayed(5, neigh) == 4);
neigh.FamilyForHouse[5] = a;
Residency.FixTable(neigh);
Check("table follows the pack file (4)", neigh.FamilyForHouse[5] == b);
Check("unshared house 9 untouched", neigh.FamilyForHouse[9] == d);
Residency.WritePlayed(9, 7, neigh);
Check("an unshared house keeps no pack file", Residency.ReadPlayed(9, neigh) == 0);
// the played household (4) is moved out by the game
neigh.MoveOut(5);
Check("after the game's MoveOut, family 4 has no house", b.HouseNumber == 0);
Check("...and the patch made family 3 the house's family", neigh.FamilyForHouse.TryGetValue(5, out var now5) && now5 == a);
Check("...and house 5 is ordinary again (no pack file)", Residency.ReadPlayed(5, neigh) == 0);
neigh.MoveOut(9);
Check("moving the only family out leaves the house empty", !neigh.FamilyForHouse.ContainsKey(9));
Check("townie/empty families are not households", !Residency.IsHousehold(townie));
// ---- the lot file ----
var lf = new LotFile() { Played = 4, Rate = 3 };
lf.UnitOf(4).Add(new Tile(1, 10, 12)); lf.UnitOf(4).Add(new Tile(2, 14, 12)); lf.UnitOf(3).Add(new Tile(1, 30, 31));
lf.AccountOf(4).Day = 718921; lf.AccountOf(4).Owed = 55;
var back = LotFile.Parse(lf.ToBytes());
Check("lot file: played and rate", back.Played == 4 && back.Rate == 3);
Check("lot file: units", back.Units[4].Count == 2 && back.Units[4][1].Equals(new Tile(2, 14, 12)) && back.Units[3][0].Equals(new Tile(1, 30, 31)));
Check("lot file: rent account", back.Rent[4].Day == 718921 && back.Rent[4].Owed == 55);
var old1 = LotFile.Parse(System.Text.Encoding.UTF8.GetBytes("v=1\nplayed=7\n"));
Check("lot file: the first stage's file still reads", old1.Played == 7 && old1.Rate == LotFile.DefaultRate && old1.Units.Count == 0);
var bad = LotFile.Parse(System.Text.Encoding.UTF8.GetBytes("played=9\nunit=oops\nrent=4:x,y\nunit=5:1,2,3\nnonsense\n"));
Check("lot file: bad lines are skipped, good ones kept", bad.Played == 9 && bad.Units.Count == 1 && bad.Units[5][0].Equals(new Tile(1, 2, 3)) && bad.Rent.Count == 0);
Check("lot file: nothing reads as empty", LotFile.Parse(null).Played == 0);
back.Keep(new[] { 4 });
Check("lot file: a household that left is forgotten", back.Units.Count == 1 && back.Units.ContainsKey(4));
// through the pack's file for a shared house
var c2 = Fam(10, 20, 1); var c3 = Fam(11, 20, 1);
var shared = new LotFile() { Played = 11, Rate = 2 }; shared.UnitOf(10).Add(new Tile(1, 5, 5)); shared.UnitOf(99).Add(new Tile(1, 6, 6));
Residency.Save(20, shared, neigh);
var sharedBack = Residency.Load(20, neigh);
Check("house file round trip, strangers dropped", sharedBack.Played == 11 && sharedBack.Rate == 2 && sharedBack.Units.Count == 1 && sharedBack.Units[10].Count == 1);
Residency.WritePlayed(20, 10, neigh);
Check("changing the played household keeps the apartments", Residency.Load(20, neigh).Played == 10 && Residency.Load(20, neigh).Units[10].Count == 1);

// ---- rent ----
var clock = new FSO.SimAntics.VMClock() { Year = 1997, Month = 6, DayOfMonth = 30 };
var d1 = Units.Today(clock); clock.Month = 7; clock.DayOfMonth = 1;
Check("the day after the 30th is the 1st", Units.Today(clock) == d1 + 1);
clock.Year = 1998; clock.Month = 1; clock.DayOfMonth = 1; var ny = Units.Today(clock); clock.Year = 1997; clock.Month = 12; clock.DayOfMonth = 30;
Check("new year's day follows the last day of the year", ny == Units.Today(clock) + 1);
Check("rent: 60 tiles at 2", Units.DailyRent(60, 2) == 120);
Check("rent: no days when never worked out", Units.DaysDue(0, 500) == 0);
Check("rent: no days on the same day or if the clock went back", Units.DaysDue(500, 500) == 0 && Units.DaysDue(500, 499) == 0);
Check("rent: one day", Units.DaysDue(500, 501) == 1);
Check("rent: never more than a week at once", Units.DaysDue(500, 900) == Units.MostDaysAtOnce);
int budget = 1000, owed = 0;
Check("pay in full", Units.Pay(ref budget, ref owed, 120) == 120 && budget == 880 && owed == 0);
budget = 50; owed = 0;
Check("pay what there is, owe the rest", Units.Pay(ref budget, ref owed, 120) == 50 && budget == 0 && owed == 70);
budget = 500;
Check("what is owed is taken with the next rent", Units.Pay(ref budget, ref owed, 120) == 190 && budget == 310 && owed == 0);
budget = -20; owed = 10;
Check("money already below zero is left alone", Units.Pay(ref budget, ref owed, 5) == 0 && budget == -20 && owed == 15);
budget = 100; owed = 0;
Check("no rent, nothing taken", Units.Pay(ref budget, ref owed, 0) == 0 && budget == 100 && owed == 0);
System.IO.Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true);
return Environment.ExitCode;
