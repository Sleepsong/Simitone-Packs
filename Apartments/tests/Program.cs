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
// the two ways into the Apartments window are optional to the pack, but should fit the Simitone this is built against
foreach (var needed in new[] { "TS1GameScreen.Save", "VMContext.RefreshLighting", "UILotControl.Update", "UIMainPanel.AddPacksButton", "UIHouseSelectPanel..ctor", "TS1GameScreen.MoveInAndPlay", "TS1GameScreen.PlayHouse",
    "VMRoutingFrame.DoRoomRoute", "VMFindBestAction.Execute", "VMThread.Tick", "VMEntity.GetPieMenu", "VMNetInteractionCmd.Verify", "VMNetInteractionCmd.Execute", "VMEntity.IndividualUserMovable" }) {
    if (!h.GetPatchedMethods().Any(m => m.DeclaringType.Name + "." + m.Name == needed)) { Console.WriteLine("FAILED: the patch on " + needed + " did not attach"); return 1; }
}
// the per-tick hook must be harmless on a VM it knows nothing about
Console.WriteLine("IsShared(null) = " + Neighbours.IsShared(null) + ", HomeHouse(null) = " + Neighbours.HomeHouse(null));

// ---- the pack must run on every Simitone that can load code packs, not only the newest ----
// It was once built on a helper (LotPackData) that only the newest Simitone has, and failed on an older one. From
// Simitone's pack code it may use only what was there from the start.
{
    var allowed = new System.Collections.Generic.HashSet<string>() { "ISimitonePack", "Pack", "PackManager", "PackManifest" };
    using var file = System.IO.File.OpenRead(typeof(ApartmentsPack).Assembly.Location);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var used = md.TypeReferences.Select(t => md.GetTypeReference(t))
        .Where(t => md.GetString(t.Namespace) == "Simitone.Client.Packs").Select(t => md.GetString(t.Name)).Distinct().ToList();
    var extra = used.Where(t => !allowed.Contains(t)).ToList();
    Console.WriteLine((extra.Count == 0 ? "ok   " : "FAIL ") + "uses only the first pack API (" + string.Join(", ", used) + ")" + (extra.Count == 0 ? "" : ": NOT " + string.Join(", ", extra)));
    if (extra.Count > 0) return 1;
    var members = md.MemberReferences.Select(m => md.GetMemberReference(m)).Select(m => md.GetString(m.Name)).ToHashSet();
    var late = new[] { "NotifyLotSaved" }.Where(members.Contains).ToList();
    if (late.Count > 0) { Console.WriteLine("FAIL uses a later pack call: " + string.Join(", ", late)); return 1; }
}

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
Check("a house two households share is an apartment building", Residency.Load(5, neigh).Building);
neigh.FamilyForHouse[5] = a;
Residency.FixTable(neigh);
Check("table follows the pack file (4)", neigh.FamilyForHouse[5] == b);
Check("unshared house 9 untouched", neigh.FamilyForHouse[9] == d);
Residency.WritePlayed(9, 7, neigh);
Check("an ordinary house keeps no pack file", Residency.ReadPlayed(9, neigh) == 0 && !Residency.Load(9, neigh).Building);
// the played household (4) is moved out by the game
neigh.MoveOut(5);
Check("after the game's MoveOut, family 4 has no house", b.HouseNumber == 0);
Check("...and the patch made family 3 the house's family", neigh.FamilyForHouse.TryGetValue(5, out var now5) && now5 == a);
Check("...and the pack's file says so, and it is still a building", Residency.ReadPlayed(5, neigh) == 3 && Residency.Load(5, neigh).Building);
neigh.MoveOut(9);
Check("moving the only family out leaves the house empty", !neigh.FamilyForHouse.ContainsKey(9));
Check("townie/empty families are not households", !Residency.IsHousehold(townie));

// ---- the lot file ----
var lf = new LotFile() { Building = true, Played = 4, Rate = 3 };
var apA = lf.Add(); apA.Tiles.Add(new Tile(1, 10, 12)); apA.Tiles.Add(new Tile(2, 14, 12)); apA.Tenant = 4; apA.Rooms = 2; apA.Area = 62;
var apB = lf.Add("The | Penthouse"); apB.Tiles.Add(new Tile(1, 30, 31));
var apC = lf.Add();
lf.AccountOf(4).Day = 718921; lf.AccountOf(4).Owed = 55;
lf.Needs[0x1a2b3c4d] = new short[] { 0, 0, 0, 10, 0, -40, 55, 100, -100, 7, 0, 0, 0, 0, 30, 60 };
var back = LotFile.Parse(lf.ToBytes());
Check("lot file: a Sim's needs", back.Needs.Count == 1 && back.Needs[0x1a2b3c4d].SequenceEqual(lf.Needs[0x1a2b3c4d]));
Check("lot file: building, played and rate", back.Building && back.Played == 4 && back.Rate == 3);
Check("lot file: apartments keep their numbers and names", back.Apartments.Count == 3 && back.Find(1).Name == "Apartment 1" && back.Find(2).Name == "The / Penthouse" && back.Find(3).Name == "Apartment 3");
Check("lot file: an apartment's rooms, tenant and saved size", back.Find(1).Tiles.Count == 2 && back.Find(1).Tiles[1].Equals(new Tile(2, 14, 12)) && back.Find(1).Tenant == 4 && back.Find(1).Rooms == 2 && back.Find(1).Area == 62);
Check("lot file: who lives where", back.ApartmentOf(4) == back.Find(1) && back.ApartmentOf(7) == null && back.ApartmentOf(0) == null);
Check("lot file: only a vacant apartment with rooms is to let", back.ToLet.Count == 1 && back.ToLet[0].Id == 2);
Check("lot file: rent account", back.Rent[4].Day == 718921 && back.Rent[4].Owed == 55);
Check("lot file: a new apartment takes the next number", back.Add().Id == 4);
back.SetTenant(back.Find(2), 4);
Check("lot file: a household moving apartment leaves its old one", back.Find(1).Vacant && back.Find(2).Tenant == 4);
back.SetTenant(back.Find(1), 9);
back.Keep(new[] { 4 });
Check("lot file: a household that left is forgotten, and its apartment is vacant", back.Find(1).Vacant && back.Find(2).Tenant == 4 && back.Rent.ContainsKey(4));
var old1 = LotFile.Parse(System.Text.Encoding.UTF8.GetBytes("v=1\nplayed=7\n"));
Check("lot file: version 1 still reads", old1.Played == 7 && old1.Rate == LotFile.DefaultRate && old1.Apartments.Count == 0 && !old1.Building);
var old2 = LotFile.Parse(System.Text.Encoding.UTF8.GetBytes("v=2\nplayed=2\nrate=1\nunit=2:1,40,34\nunit=3:1,40,28;1,44,28\nrent=2:718921,0\n"));
Check("lot file: version 2's households' rooms become apartments they live in", old2.Building && old2.Apartments.Count == 2 && old2.ApartmentOf(2).Tiles.Count == 1 && old2.ApartmentOf(3).Tiles.Count == 2 && old2.ApartmentOf(3).Name == "Apartment 2");
var bad = LotFile.Parse(System.Text.Encoding.UTF8.GetBytes("played=9\napartment=oops\nrent=4:x,y\napartment=5|0|1|9|Flat|1,2,3\napartment=5|0|1|9|Twice|1,2,3\napartment=6|4|0|0||\napartment=7|4|0|0||\nnonsense\n"));
Check("lot file: bad lines are skipped, good ones kept", bad.Played == 9 && bad.Apartments.Count == 3 && bad.Find(5).Name == "Flat" && bad.Find(5).Tiles[0].Equals(new Tile(1, 2, 3)) && bad.Rent.Count == 0);
Check("lot file: a household named for two apartments lives in the first", bad.Find(6).Tenant == 4 && bad.Find(7).Vacant);
Check("lot file: nothing reads as empty", LotFile.Parse(null).Played == 0 && !LotFile.Parse(null).Building);

// ---- a building laid out with nobody in it, then filled from the neighbourhood screen ----
var plan = new LotFile() { Building = true, Rate = 2 };
var flat1 = plan.Add(); flat1.Tiles.Add(new Tile(1, 5, 5)); flat1.Rooms = 1; flat1.Area = 20;
var flat2 = plan.Add(); flat2.Tiles.Add(new Tile(1, 9, 5)); flat2.Rooms = 2; flat2.Area = 35;
Residency.Save(20, plan, neigh);
Check("an empty building keeps its file", Residency.Load(20, neigh).Building && Residency.Load(20, neigh).ToLet.Count == 2 && Residency.Residents(20, neigh).Count == 0);
var first = Fam(10, 0, 1); var second = Fam(11, 0, 2); var third = Fam(12, 0, 1);
first.Budget = 1000; second.Budget = 79; third.Budget = 5000;
Check("moving in costs two days' rent", Units.Deposit(35, 2) == 140 && Units.Deposit(20, 2) == 80 && Units.Deposit(20, 0) == 0);
Residency.MoveIntoApartment(20, first, 2, neigh);
var after1 = Residency.Load(20, neigh);
Check("the first family takes the apartment it chose", first.HouseNumber == 20 && after1.ApartmentOf(10).Id == 2 && after1.Played == 10 && neigh.FamilyForHouse[20] == first);
Check("...and pays the deposit", first.Budget == 1000 - 140);
var tooPoor = false;
try { Residency.MoveIntoApartment(20, second, 1, neigh); } catch (InvalidOperationException) { tooPoor = true; }
Check("a family that cannot afford the deposit does not move in, and pays nothing", tooPoor && second.HouseNumber == 0 && second.Budget == 79 && Residency.Load(20, neigh).ToLet.Count == 1);
second.Budget = 80;
Check("...and one apartment is left", after1.ToLet.Count == 1 && after1.ToLet[0].Id == 1);
Residency.MoveIntoApartment(20, second, 1, neigh);
var after2 = Residency.Load(20, neigh);
Check("a family with exactly the deposit can move in, and is left with nothing", second.Budget == 0);
Check("the second family takes the one left, and is the one played", second.HouseNumber == 20 && after2.ApartmentOf(11).Id == 1 && after2.ApartmentOf(10).Id == 2 && after2.Played == 11 && neigh.FamilyForHouse[20] == second);
Check("...and the building is full", after2.ToLet.Count == 0 && Residency.Residents(20, neigh).Count == 2);
var refused = false;
try { Residency.MoveIntoApartment(20, third, 1, neigh); } catch (InvalidOperationException) { refused = true; }
Check("a taken apartment cannot be moved into, and nothing is paid", refused && third.HouseNumber == 0 && third.Budget == 5000);
neigh.MoveOut(20); // the game evicts the played household (11)
var after3 = Residency.Load(20, neigh);
Check("an evicted household's apartment is vacant again, and the other household is played", second.HouseNumber == 0 && after3.Find(1).Vacant && after3.ApartmentOf(10).Id == 2 && after3.Played == 10 && neigh.FamilyForHouse[20] == first);
neigh.MoveOut(20);
Check("with everyone gone the building is still a building, all to let", Residency.Load(20, neigh).Building && Residency.Load(20, neigh).ToLet.Count == 2 && !neigh.FamilyForHouse.ContainsKey(20));
Residency.WritePlayed(20, 10, neigh);
Check("the pack keeps its files where Simitone's own helper would", PackStore.PathFor("sample.apartments", neigh.GetHousePath(20)).Replace('\\', '/').Contains("/LocalHouse/PackData/sample.apartments/House20-"));
Check("writing nothing removes the file", PackStore.Write("sample.apartments", neigh.GetHousePath(20), null) && PackStore.Read("sample.apartments", neigh.GetHousePath(20)) == null);

// ---- who may walk into an apartment ----
{
    var residents = new System.Collections.Generic.HashSet<int>() { 2, 3, 5 };
    var mine = new Apartment() { Id = 1, Name = "Mine", Tenant = 2 };
    var theirs = new Apartment() { Id = 2, Name = "Theirs", Tenant = 3 };
    var empty = new Apartment() { Id = 3, Name = "Empty", Tenant = 0 };
    const int played = 2, neighbour = 3, otherNeighbour = 5, guest = 9, npc = 0;
    Check("a vacant apartment is open to everyone", Doors.MayEnter(played, played, residents, empty, false) && Doors.MayEnter(neighbour, played, residents, empty, false) && Doors.MayEnter(npc, played, residents, empty, false));
    Check("a household may enter its own apartment", Doors.MayEnter(played, played, residents, mine, false) && Doors.MayEnter(neighbour, played, residents, theirs, false));
    Check("the played household's guests and the game's own characters may enter its apartment", Doors.MayEnter(guest, played, residents, mine, false) && Doors.MayEnter(npc, played, residents, mine, false));
    Check("the neighbours may not enter the played household's apartment", !Doors.MayEnter(neighbour, played, residents, mine, false) && !Doors.MayEnter(otherNeighbour, played, residents, mine, false));
    Check("the played household may not enter a neighbour's apartment uninvited", !Doors.MayEnter(played, played, residents, theirs, false));
    Check("...but may when asked in", Doors.MayEnter(played, played, residents, theirs, true));
    Check("the game's own characters on a job go anywhere: a fire next door has to be put out", Doors.MayEnter(npc, played, residents, theirs, false, true) && Doors.MayEnter(npc, played, residents, mine, false, true));
    Check("...but a visitor, or a neighbour, is not one of them", !Doors.MayEnter(guest, played, residents, theirs, false, false) && !Doors.MayEnter(otherNeighbour, played, residents, theirs, false, false));
    Check("an invitation is for the played household only", !Doors.MayEnter(guest, played, residents, theirs, true) && !Doors.MayEnter(npc, played, residents, theirs, true) && !Doors.MayEnter(otherNeighbour, played, residents, theirs, true));
}

// ---- the plan of the lot ----
{
    // a 4 by 2 floor: apartment 0 | apartment 1 | shared | outdoors, twice
    var cells = UIApartmentMap.Classify(4, 2, (x, y) => x < 2 ? x : -1, (x, y) => x == 3);
    Check("the plan marks apartments, shared rooms and outdoors", cells.Length == 8 && cells[0] == 2 && cells[1] == 3 && cells[2] == 1 && cells[3] == 0 && cells[4] == 2 && cells[7] == 0);
    var pf = new LotFile(); var p1 = pf.Add(); var p2 = pf.Add(); var p3 = pf.Add();
    Check("each apartment has its own colour", UIApartmentMap.ColourOf(pf, p1) != UIApartmentMap.ColourOf(pf, p2) && UIApartmentMap.ColourOf(pf, p2) != UIApartmentMap.ColourOf(pf, p3));
}

// ---- the switches, kept between runs ----
{
    var before = Settings.Write();
    Settings.Read("private=0\nvisitors=off\nmailbox=0\ndim=0\nwatch=0\noutings=often\nneeds=off\nnonsense\nwhat=ever\n");
    Check("the switches read back off", !Doors.Private && !Doors.ShowVisitors && !Neighbours.MovableMailbox && !Neighbours.Dim && !Neighbours.Watch && Neighbours.ComeOut == Outings.Often && Neighbours.NeedsFloor == null);
    var off = Settings.Write();
    Settings.Read(before);
    Check("...and on again, as they started", Doors.Private && Doors.ShowVisitors && Neighbours.MovableMailbox && Neighbours.Dim && Neighbours.Watch && Neighbours.ComeOut == Outings.Sometimes && Neighbours.NeedsFloor == 50 && Settings.Write() == before);
    Settings.Read(off + "needs=999\noutings=daily\n");
    Check("a switch set to nonsense is left as it was", Neighbours.NeedsFloor == null && Neighbours.ComeOut == Outings.Often);
    Settings.Read(before);
}

// ---- the question dialog's layout ----
Check("a short message is one line", UIChoiceDialog.LinesOf("Which apartment?", 800, 15) == 1);
Check("each paragraph is at least a line, blank ones too", UIChoiceDialog.LinesOf("one\n\nthree", 800, 15) == 3);
Check("a long paragraph wraps", UIChoiceDialog.LinesOf(new string('x', 400), 800, 15) >= 4);
Check("no message still leaves a line", UIChoiceDialog.LinesOf(null, 800, 15) == 1);
Check("three buttons fit across 865", UIChoiceDialog.Columns(865, 275, 20) == 3);
Check("a narrow screen still has one column", UIChoiceDialog.Columns(200, 275, 20) == 1);

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
