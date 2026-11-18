/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Lots Bin pack that need no copy of The Sims: that its patch fits the engine it is built against, the
// bin's folders, how a lot's pack data goes with a copy (PackData, with packs made up here) and that it is kept where
// Simitone's own LotPackData and LotPackRecord look for it, how a copy is made another house's, and that a lot's
// pictures are written out as they were read. They build against Simitone as it is: the pack changes nothing in it.
// What they cannot check is the game itself: nothing here loads a lot, so taking the Sims off one is not run.
// Run with: dotnet run --project LotsBin/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.Content.Model;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Marshals.Threads;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.TS1Platform;
using HarmonyLib;
using LotsBin;
using Simitone.Client.Packs;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "lotsbin-test-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);
try
{
    // ---- the patch fits this engine ----
    var h = new Harmony("test.lotsbin");
    var apply = typeof(LotsBinPack).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    try { apply.Invoke(null, new object[] { h }); Check("the patch applies", true); }
    catch (TargetInvocationException e) { Check("the patch applies: " + e.InnerException, false); }
    Check("patched UIHouseSelectPanel..ctor", h.GetPatchedMethods().Any(m => m.DeclaringType.Name + "." + m.Name == "UIHouseSelectPanel..ctor"));
    h.UnpatchAll("test.lotsbin");
    h.UnpatchAll("simitone.packs.lotsbin");

    // ---- the bin's folders ----
    Check("an empty bin has no lots", Bin.All().Count == 0);
    var copy = new LotPackCopy();
    copy.Data["sample.terrainpaint"] = new byte[] { 1, 2, 3 };
    copy.Data["../escape"] = new byte[] { 9 };
    copy.SavedWith["sample.terrainpaint"] = "Terrain Paint";
    var older = Bin.Add(new BinLotInfo() { Name = "Old: house/1", Saved = new DateTime(2026, 1, 2, 3, 4, 5), Size = 64 }, new byte[] { 7 }, copy);
    var newer = Bin.Add(new BinLotInfo() { Name = "New", Saved = new DateTime(2026, 5, 1), Size = 40 }, new byte[] { 8 }, null);
    var same = Bin.Add(new BinLotInfo() { Name = "New", Saved = new DateTime(2026, 5, 1), Size = 40 }, new byte[] { 8 }, null);
    var all = Bin.All();
    Check("three lots in the bin, newest first", all.Count == 3 && all[2].Folder == older.Folder);
    Check("two lots saved the same second get folders of their own", newer.Folder != same.Folder);
    Check("a lot's folder name is safe", Path.GetFileName(older.Folder) == "20260102-030405-Old_ house_1");
    Check("no half-written folders are left", !Directory.GetDirectories(Bin.Dir).Any(x => Path.GetFileName(x).StartsWith(".")));
    var back = Bin.ReadPacks(all[2]);
    Check("a pack's data comes back", back.Data.TryGetValue("sample.terrainpaint", out var tp) && tp.SequenceEqual(new byte[] { 1, 2, 3 }));
    Check("a made-up pack id is not written outside the bin", !back.Data.ContainsKey("../escape") && !File.Exists(Path.Combine(older.Folder, "escape.dat")));
    Check("the packs it was saved with come back", back.SavedWith.TryGetValue("sample.terrainpaint", out var n) && n == "Terrain Paint");
    Bin.Rename(all[2], "Renamed");
    Check("a renamed lot keeps its new name", Bin.All().Any(x => x.Folder == older.Folder && x.Name == "Renamed"));
    File.WriteAllText(Path.Combine(newer.Folder, "lot.json"), "{ \"Version\": 99 }");
    Check("a lot from a newer Lots Bin is left out", !Bin.All().Any(x => x.Folder == newer.Folder));
    Bin.Delete(Bin.Read(same.Folder));
    Check("a deleted lot is gone", !Directory.Exists(same.Folder));
    Check("ids: letters, digits, . _ -", Bin.PackIdIsSafe("sample.lots_bin-2") && !Bin.PackIdIsSafe("..") && !Bin.PackIdIsSafe("a/b") && !Bin.PackIdIsSafe("A"));
    Check("an empty name is still a name", Bin.SafeName("  ") == "lot" && Bin.SafeName(new string('x', 90)).Length == 40);

    // ---- the pack keeps the packs' data where Simitone does ----
    var from = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData/Houses/House05.iff");
    var to = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "UserData2/Houses/House05.iff");
    LotPackData.Write("sample.terrainpaint", from, new byte[] { 1 });
    Check("a pack's data Simitone wrote for a lot is found", PackData.PacksWithData(from).SequenceEqual(new[] { "sample.terrainpaint" }));
    Check("...and not for another lot of the same name", PackData.PacksWithData(to).Count == 0);
    PackData.DeleteAll(from);
    Check("removing a lot's data removes what Simitone reads", LotPackData.Read("sample.terrainpaint", from) == null && PackData.PacksWithData(from).Count == 0);
    PackData.SetSavedWith(to, new Dictionary<string, string>() { { "off.pack", "Off" } });
    Check("a lot's record set here is the one Simitone warns from", LotPackRecord.Missing(to).SequenceEqual(new[] { "Off" }));

    Pack Running(string id, ISimitonePack code) => new Pack() { Id = id, Code = new List<ISimitonePack>() { code }, HasCode = true };
    void SetActive(List<Pack> packs)
    {
        typeof(PackManager).GetProperty("Active").SetValue(null, packs);
        typeof(PackManager).GetField("ActiveIds", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, new HashSet<string>(packs.Select(x => x.Id)));
    }
    SetActive(new List<Pack>() { Running("saved.pack", new Plain()) });
    LotPackRecord.Saved(from);
    var savedWith = PackData.SavedWith(from);
    Check("the record Simitone writes when a lot is saved is read here", savedWith.Count == 1 && savedWith["saved.pack"] == "saved.pack");
    PackData.SetSavedWith(from, null);
    Check("a record can be cleared", PackData.SavedWith(from).Count == 0 && LotPackRecord.Missing(from).Count == 0);

    // ---- what the packs keep goes with a copy ----
    LotPackData.Write("off.pack", from, new byte[] { 1 });          //a pack that is not running: goes as it is
    LotPackData.Write("sample.apartments", from, new byte[] { 2 }); //running, chooses what goes
    LotPackData.Write("plain.pack", from, new byte[] { 3 });        //running, takes the default: as it is
    LotPackData.Write("broken.pack", from, new byte[] { 4 });       //running, and fails
    LotPackData.Write("sample.openforbusiness", from, new byte[] { 5 }); //running, carries nothing
    PackData.SetSavedWith(from, new Dictionary<string, string>() { { "off.pack", "Off" } });
    LotPackData.Write("old.pack", to, new byte[] { 6 });            //what the lot it goes to had: removed
    LotPackData.Write("off.pack", to, new byte[] { 7 });

    var active = new List<Pack>()
    {
        Running("sample.apartments", new Doubling()),
        Running("plain.pack", new Plain()),
        Running("broken.pack", new Broken()),
        Running("sample.openforbusiness", new Nothing()),
        Running("new.pack", new Plain()),
        Running("wrong.pack", new WrongShape()),
    };
    SetActive(active);
    LotPackData.Write("wrong.pack", from, new byte[] { 8 });

    Check("the packs with data for a lot are found", PackData.PacksWithData(from).SequenceEqual(new[] { "broken.pack", "off.pack", "plain.pack", "sample.apartments", "sample.openforbusiness", "wrong.pack" }));
    var exported = PackData.Export(from);
    Check("a pack that is not running: its data goes as it is", exported.Data["off.pack"].SequenceEqual(new byte[] { 1 }));
    Check("a running pack chooses what goes", exported.Data["sample.apartments"].SequenceEqual(new byte[] { 2, 2 }));
    Check("a running pack that does not choose: as it is", exported.Data["plain.pack"].SequenceEqual(new byte[] { 3 }));
    Check("a pack that fails: its data goes as it is", exported.Data["broken.pack"].SequenceEqual(new byte[] { 4 }));
    Check("...and its code is left running: that is Simitone's to stop", active[2].Code.Count == 1 && active[2].CodeError == null);
    Check("a pack can carry nothing", !exported.Data.ContainsKey("sample.openforbusiness"));
    Check("a running pack with nothing saved carries nothing", !exported.Data.ContainsKey("new.pack"));
    Check("a method of another shape is not the hook: as it is", exported.Data["wrong.pack"].SequenceEqual(new byte[] { 8 }));
    Check("the packs it was saved with go too", exported.SavedWith.Count == 1 && exported.SavedWith["off.pack"] == "Off");
    Check("the lot copied from keeps its own data", LotPackData.Read("sample.apartments", from).SequenceEqual(new byte[] { 2 }));

    PackData.Import(to, exported);
    Check("what the lot had is gone", LotPackData.Read("old.pack", to) == null);
    Check("a pack's data is replaced by the copy's", LotPackData.Read("off.pack", to).SequenceEqual(new byte[] { 1 }));
    Check("a running pack is given its data back", LotPackData.Read("sample.apartments", to).SequenceEqual(new byte[] { 2, 2, 9 }));
    Check("the default keeps it as it is", LotPackData.Read("plain.pack", to).SequenceEqual(new byte[] { 3 }));
    Check("a failed pack's data is kept as it is", LotPackData.Read("broken.pack", to).SequenceEqual(new byte[] { 4 }));
    Check("nothing carried, nothing kept", LotPackData.Read("sample.openforbusiness", to) == null && LotPackData.Read("new.pack", to) == null);
    Check("the lot warns of the same missing packs", LotPackRecord.Missing(to).SequenceEqual(new[] { "Off" }));
    PackData.Import(to, new LotPackCopy());
    Check("putting down a copy with no data clears the lot's", PackData.PacksWithData(to).Count == 0 && LotPackRecord.Missing(to).Count == 0);
    SetActive(new List<Pack>());

    // ---- a copy made another house's ----
    var state = MakeState(4, house: 7);
    state = LotCopy.ReadState(LotCopy.MakeFSOV(LotCopy.WriteState(state)));
    Check("a lot's state survives being written and read", state.GlobalState[10] == 7 && state.Context.Architecture.Width == 4);
    var simi = MakeSIMI(7, lotValue: 1000);
    var here = MakeSIMI(41, lotValue: 2500);
    LotCopy.Retarget(state, simi, 41, here);
    Check("the copy is house 41, and loads as it", state.GlobalState[10] == 41 && state.GlobalState[11] == 41);
    Check("nobody is selected, and no family plays", state.GlobalState[3] == 0 && state.GlobalState[9] == 0);
    Check("the ground is the snow of house 41", state.Context.Architecture.Terrain.DarkType == TerrainType.SNOW && state.Context.Architecture.Terrain.LightType == TerrainType.SNOW);
    var lotState = (VMTS1LotState)state.PlatformState;
    Check("the state's lot information is house 41's too", lotState.SimulationInfo.GlobalData[10] == 41 && lotState.CurrentFamily == null);
    Check("the land is priced as the lot here", simi.LotValue == 2500 && lotState.SimulationInfo.LotValue == 2500);
    Check("the building's value goes with the copy", simi.ObjectsValue == 300 && simi.ArchitectureValue == 400);
    Check("the household's accounts are left out", simi.BudgetDays.All(x => x.Valid == 0));
    Check("the lot information's globals are the state's", simi.GlobalData.SequenceEqual(state.GlobalState));
    LotCopy.Retarget(state, simi, 5, null);
    Check("on grass, with no lot information here, the copy keeps its land price", state.Context.Architecture.Terrain.DarkType == TerrainType.GRASS && simi.LotValue == 2500);
    var round = LotCopy.ReadState(LotCopy.MakeFSOV(LotCopy.WriteState(state)));
    //(the engine writes a SIMI's globals as zeros, the state's own included: the state's globals are the ones a lot loads with)
    Check("a made-over state is written and read", round.GlobalState[10] == 5 && round.GlobalState[11] == 5 && ((VMTS1LotState)round.PlatformState).SimulationInfo.LotValue == 2500);

    // ---- which lots a copy fits ----
    var square = new bool[16];
    var turned = new bool[16]; turned[0] = true;
    Check("another size does not fit", LotCopy.FitProblem(new LotCopy.Layout() { Size = 4, Unbuildable = square }, new LotCopy.Layout() { Size = 5 }, out _) != null);
    Check("the same size and layout fits", LotCopy.FitProblem(new LotCopy.Layout() { Size = 4, Unbuildable = square }, new LotCopy.Layout() { Size = 4, Unbuildable = (bool[])square.Clone() }, out var other1) == null && !other1);
    Check("the same size laid out another way fits, but says so", LotCopy.FitProblem(new LotCopy.Layout() { Size = 4, Unbuildable = square }, new LotCopy.Layout() { Size = 4, Unbuildable = turned }, out var other2) == null && other2);
    Check("a layout that is not known is said to differ", LotCopy.FitProblem(new LotCopy.Layout() { Size = 4 }, new LotCopy.Layout() { Size = 4, Unbuildable = square }, out var other3) == null && other3);
    var fsovLot = new IffFile();
    var withLayout = MakeState(4, house: 7);
    withLayout.Context.Architecture.FineBuildableArea = turned;
    fsovLot.AddChunk(LotCopy.MakeFSOV(LotCopy.WriteState(withLayout)));
    var layout = LotCopy.ReadLayout(fsovLot, 7);
    Check("a Simitone lot's layout is read from its state", layout.Size == 4 && layout.Unbuildable.SequenceEqual(turned));
    var ts1Lot = new IffFile();
    var ts1Simi = MakeSIMI(7, 0); ts1Simi.GlobalData[23] = 64;
    ts1Lot.AddChunk(Ready(ts1Simi, 1, "SIMI"));
    var flags = new ARRY() { Width = 64, Height = 64, Type = ARRYType.RLEFloor, Data = Enumerable.Range(0, 64 * 64).Select(i => (byte)(i % 3 == 0 ? 32 : 0)).ToArray() };
    ts1Lot.AddChunk(Ready(flags, 8, "ARRY"));
    var ts1Layout = LotCopy.ReadLayout(ts1Lot, 7);
    Check("a lot in The Sims' format: its size from its globals", ts1Layout.Size == 64);
    Check("...and where it can be built from its flags", ts1Layout.Unbuildable != null && ts1Layout.Unbuildable.Length == 64 * 64 && ts1Layout.Unbuildable.Count(x => !x) == ts1Layout.Unbuildable.Length / 3 + 1);

    // ---- the pictures go out as they came in ----
    var picturesFile = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "pictures.iff");
    var made = new IffFile();
    made.AddChunk(Ready(new PNG() { data = new byte[] { 1, 2, 3, 4 } }, 513, "PNG_"));
    made.AddChunk(Ready(new PNG() { data = new byte[] { 5, 6 } }, 512, "PNG_"));
    made.AddChunk(Ready(new BMP() { data = new byte[] { 7, 8, 9 } }, 513, "BMP_"));
    var thumbBytes = new byte[20]; BitConverter.GetBytes(120).CopyTo(thumbBytes, 0); BitConverter.GetBytes(-7).CopyTo(thumbBytes, 8);
    var thumb = Ready(new THMB(), 512, "THMB"); thumb.OriginalData = thumbBytes; //(written out as it was read: THMB has no writer)
    made.AddChunk(thumb);
    made.AddChunk(Ready(MakeSIMI(3, 0), 1, "SIMI"));
    File.WriteAllBytes(picturesFile, LotCopy.ToBytes(made));
    var source = new IffFile(picturesFile, true);
    var target = new IffFile();
    LotCopy.CopyPictures(source, target);
    var reread = new IffFile(); reread.Read(new MemoryStream(LotCopy.ToBytes(target)));
    Check("Simitone's pictures are copied", reread.Get<PNG>(513)?.data.SequenceEqual(new byte[] { 1, 2, 3, 4 }) == true && reread.Get<PNG>(512)?.data.SequenceEqual(new byte[] { 5, 6 }) == true);
    Check("The Sims' pictures are copied", reread.Get<BMP>(513)?.data.SequenceEqual(new byte[] { 7, 8, 9 }) == true);
    var thumbBack = reread.Get<THMB>(512);
    Check("...with their offsets", thumbBack != null && thumbBack.Width == 120 && thumbBack.BaseYOff == -7);
    Check("nothing but the pictures is copied", reread.Get<SIMI>(1) == null);

    // ---- a household's accounts ----
    var accounts = MakeSIMI(1, 0);
    accounts.BudgetDays[2] = new SIMI.SIMIBudgetDay() { Valid = 1, JobIncome = 500 };
    LotCopy.ClearAccounts(accounts);
    Check("a household's accounts are cleared", accounts.BudgetDays.Length == 6 && accounts.BudgetDays.All(x => x.Valid == 0));
}
finally
{
    try { Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true); } catch { }
}

Console.WriteLine(failed == 0 ? "All checks passed." : failed + " checks FAILED.");
return failed == 0 ? 0 : 1;

static T Ready<T>(T chunk, ushort id, string type) where T : IffChunk
{
    chunk.ChunkID = id;
    chunk.ChunkType = type;
    chunk.ChunkLabel = "";
    chunk.ChunkProcessed = true;
    chunk.AddedByPatch = true;
    return chunk;
}

static SIMI MakeSIMI(short house, int lotValue)
{
    var simi = new SIMI() { Version = 0x3E, GlobalData = Enumerable.Range(0, 38).Select(i => (short)i).ToArray(), LotValue = lotValue, ObjectsValue = 300, ArchitectureValue = 400 };
    simi.GlobalData[10] = house;
    simi.BudgetDays = Enumerable.Range(0, 6).Select(i => new SIMI.SIMIBudgetDay() { Valid = 1, MiscIncome = 10 }).ToArray();
    return Ready(simi, 1, "SIMI");
}

static VMMarshal MakeState(int size, short house)
{
    var arch = new VMArchitectureMarshal()
    {
        Width = size, Height = size, Stories = 2,
        Terrain = new VMArchitectureTerrain(size, size) { LightType = TerrainType.SAND, DarkType = TerrainType.SAND },
        Walls = new[] { new WallTile[size * size], new WallTile[size * size] },
        Floors = new[] { new FloorTile[size * size], new FloorTile[size * size] },
    };
    var globals = new short[38];
    globals[10] = house; globals[11] = house; globals[3] = 12; globals[9] = 4;
    return new VMMarshal()
    {
        TS1 = true,
        Context = new VMContextMarshal() { Clock = new VMClockMarshal() { TicksPerMinute = 30 }, Architecture = arch, Ambience = new VMAmbientSoundMarshal() },
        Entities = new VMEntityMarshal[0],
        Threads = new VMThreadMarshal[0],
        MultitileGroups = new VMMultitileGroupMarshal[0],
        GlobalState = globals,
        PlatformState = new VMTS1LotState() { SimulationInfo = MakeSIMI(house, 1000) },
    };
}

/// <summary>Chooses what goes: the copy carries its data twice over, and is given a 9 on the end when put down.</summary>
class Doubling : ISimitonePack
{
    public byte[] OnLotExport(string lotPath, byte[] saved) => saved?.Concat(saved).ToArray();
    public byte[] OnLotImport(string lotPath, byte[] data) => data?.Concat(new byte[] { 9 }).ToArray();
}

class Plain : ISimitonePack { }

class Broken : ISimitonePack
{
    public byte[] OnLotExport(string lotPath, byte[] saved) => throw new InvalidOperationException("broken on purpose");
}

class Nothing : ISimitonePack
{
    public byte[] OnLotExport(string lotPath, byte[] saved) => null;
}

/// <summary>Has a method of the name, but not of the hook's shape.</summary>
class WrongShape : ISimitonePack
{
    public byte[] OnLotExport(string lotPath) => new byte[0];
}
