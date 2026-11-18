/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks for the Undo pack that need no copy of The Sims: that its patches fit the engine it is built against, the
// history of a lot's moments and the money of undoing (History.cs), the packs' part of a lot (PackParts.cs, with packs
// made up here), that a pack's own build tool can say it changed the lot (Create a Style's SayChanged reaching a pack
// with OnLotChanged), and that every code pack in this repository takes part, so that undo is not refused with any of
// them on. Nothing here needs a change to Simitone.
// What they cannot check is the game itself: nothing here loads a lot, so keeping one and putting it back is not run.
// Run with: dotnet run --project Undo/tests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using HarmonyLib;
using Simitone.Client.Packs;
using Undo;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

FSO.Common.FSOEnvironment.UserDir = Path.Combine(Path.GetTempPath(), "undo-test-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(FSO.Common.FSOEnvironment.UserDir);

void SetActive(List<Pack> packs)
{
    typeof(PackManager).GetProperty("Active").SetValue(null, packs);
    typeof(PackManager).GetField("ActiveIds", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, new HashSet<string>(packs.Select(x => x.Id)));
}

try
{
    // ---- the patches fit this engine, and this Simitone has the calls the pack needs ----
    try { new UndoPack().OnLoad(null); Check("OnLoad: the patches apply", true); }
    catch (Exception e) { Check("OnLoad: " + e.Message, false); }
    var patched = Harmony.GetAllPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
    foreach (var name in new[] { "VM.Tick", "VMNetArchitectureCmd.Verify", "VMNetArchitectureCmd.Execute", "VMNetBuyObjectCmd.Verify",
        "VMNetBuyObjectCmd.Execute", "VMNetDeleteObjectCmd.Execute", "VMNetMoveObjectCmd.Execute", "VMNetSetRoofCmd.Execute", "UILotControl.Update" })
        Check("patched " + name, patched.Contains(name));
    Check("the console has undo and redo", PackManager.TryRunCommand("undo list", out _) && PackManager.TryRunCommand("redo", out _));
    new Harmony("simitone.packs.undo").UnpatchAll("simitone.packs.undo");
    try { LotMoments.CheckEngine(); Check("the session's cheats and the screen's snow can be carried across a reload", true); }
    catch (Exception e) { Check("carried across a reload: " + e.Message, false); }

    // ---- the history ----
    var h = new History<string>();
    Check("a history not started has nothing to undo or redo", !h.Started && !h.CanUndo && !h.CanRedo && h.NextUndo == null && h.NextRedo == null);
    h.Start("open", 10);
    Check("started: the lot as build mode opened, nothing to undo", h.Started && !h.CanUndo && !h.CanRedo && h.Now.State == "open" && h.Bytes == 10);
    h.Add("wall", 20, "Build walls", 140);
    h.Add("sofa", 30, "Buy Sofa", 600);
    Check("two changes to undo", h.UndoCount == 2 && h.RedoCount == 0 && h.NextUndo.What == "Buy Sofa" && h.NextUndo.Cost == 600 && h.Bytes == 60);
    var back = h.Undone();
    Check("undo goes to the moment before", back.State == "wall" && h.Now.State == "wall" && h.UndoCount == 1 && h.RedoCount == 1);
    Check("redo would buy the sofa again", h.NextRedo.What == "Buy Sofa" && h.NextRedo.Cost == 600 && h.NextUndo.What == "Build walls");
    var forward = h.Redone();
    Check("redo goes to the moment after", forward.State == "sofa" && h.RedoCount == 0);
    h.Undone(); h.Undone();
    Check("undone to the start", h.Now.State == "open" && !h.CanUndo && h.RedoCount == 2);
    h.Add("floor", 5, "Lay a floor", 50);
    Check("a new change after undoing forgets what could be redone", !h.CanRedo && h.UndoCount == 1 && h.Now.State == "floor" && h.Bytes == 15);
    h.Refresh("floor+", 7, 3);
    Check("refresh: the lot as it is now, and what the unseen change cost joins the last change", h.Now.State == "floor+" && h.Now.Cost == 53 && h.Bytes == 17);
    h.Undone();
    h.Refresh("open+", 12, 9);
    Check("refreshing the first moment changes no cost", h.Now.State == "open+" && h.Now.Cost == 0 && h.Bytes == 19);
    var steps = h.Steps();
    Check("the steps, with what is undone", steps.Count == 1 && steps[0].what == "Lay a floor" && !steps[0].done);
    var threw = false;
    try { h.Undone(); } catch (InvalidOperationException) { threw = true; }
    Check("undoing with nothing to undo is refused", threw);
    h.Clear();
    Check("cleared: nothing kept", !h.Started && h.Bytes == 0 && h.Steps().Count == 0);
    threw = false;
    try { h.Add("x", 1, "x", 0); } catch (InvalidOperationException) { threw = true; }
    Check("a change before the history starts is refused", threw);

    var small = new History<int>() { MaxSteps = 3 };
    small.Start(0, 1);
    for (int i = 1; i <= 6; i++) small.Add(i, 1, "change " + i, i);
    Check("at most MaxSteps changes are kept, the oldest let go", small.UndoCount == 3 && small.Steps().First().what == "change 4" && small.Bytes == 4);
    var big = new History<int>() { MaxBytes = 100 };
    big.Start(0, 40);
    big.Add(1, 40, "a", 0);
    big.Add(2, 40, "b", 0);
    Check("over MaxBytes the oldest go", big.UndoCount == 1 && big.Bytes == 80 && big.Now.State == 2);
    big.Add(3, 500, "huge", 0);
    Check("the current moment and the one before are kept, however large", big.UndoCount == 1 && big.Now.State == 3 && big.NextUndo.What == "huge");

    // ---- the money ----
    Check("a change's cost is what the budget went down by", Ledger.CostOf(20000, 19400) == 600 && Ledger.CostOf(19400, 19800) == -400);
    Check("no family, no cost", Ledger.CostOf(null, 100) == 0 && Ledger.CostOf(100, null) == 0);
    Check("undoing a purchase always can: the money comes back", Ledger.CannotUndo(0, 600) == null && Ledger.After(0, 600, true) == 600);
    Check("undoing a sale takes its money back", Ledger.CannotUndo(500, -400) == null && Ledger.After(500, -400, true) == 100);
    Check("...and is refused if the family has spent it", (Ledger.CannotUndo(100, -400) ?? "").Contains("§400"));
    Check("redoing a purchase costs it again", Ledger.CannotRedo(600, 600) == null && Ledger.After(600, 600, false) == 0);
    Check("...and is refused if the family cannot pay", (Ledger.CannotRedo(599, 600) ?? "").Contains("§599"));
    Check("redoing a sale gives its money again", Ledger.CannotRedo(0, -400) == null && Ledger.After(0, -400, false) == 400);
    Check("a lot with no family can always undo and redo", Ledger.CannotUndo(null, -1000000) == null && Ledger.CannotRedo(null, 1000000) == null);
    Check("money stays a budget", Ledger.After(int.MaxValue, 10, true) == int.MaxValue && Ledger.Money(1234567) == "§1,234,567");

    // ---- the changes, in words ----
    VMArchitectureCommand Cmd(VMArchitectureCommandType type, ushort pattern = 1) => new VMArchitectureCommand() { Type = type, pattern = pattern };
    Check("walls", UndoPack.Describe(new List<VMArchitectureCommand> { Cmd(VMArchitectureCommandType.WALL_LINE) }) == "Build walls");
    Check("a pool", UndoPack.Describe(new List<VMArchitectureCommand> { Cmd(VMArchitectureCommandType.FLOOR_RECT, 65535) }) == "Build a pool");
    Check("taking up a floor", UndoPack.Describe(new List<VMArchitectureCommand> { Cmd(VMArchitectureCommandType.FLOOR_FILL, 0) }) == "Take up the floor");
    Check("a command type of a pack's own", UndoPack.Describe(new List<VMArchitectureCommand> { Cmd((VMArchitectureCommandType)200) }) == "Build");

    // ---- the packs' part of a lot, with packs made up here ----
    Pack Running(string id, string name, ISimitonePack code) => new Pack() { Id = id, Code = new List<ISimitonePack>() { code }, Manifest = new PackManifest() { Id = id, Name = name } };
    var keeper = new Keeper() { Data = new byte[] { 1, 2, 3 } };
    var keeps = new List<Pack>
    {
        Running("made.keeper", "Keeper", keeper),
        Running("made.nolots", "No Lots", new NoLots()),
        Running("made.restoreonly", "Restore Only", new RestoreOnly()),
    };
    SetActive(keeps);
    PackParts.Forget();
    Check("packs that take part, or keep nothing for a lot, keep undo on", PackParts.NotTakingPart().Count == 0);
    var snapshot = PackParts.Snapshot(null);
    Check("a pack's data is kept", snapshot.Size == 3);
    keeper.Data[0] = 9; //the pack changes its own array afterwards
    keeper.Data = null;
    Check("putting the parts back fails for none", PackParts.Restore(null, snapshot).Count == 0);
    Check("a pack is given back what it had, as it was then", keeper.Restored != null && keeper.Restored.SequenceEqual(new byte[] { 1, 2, 3 }));
    keeper.Restored[1] = 7;
    PackParts.Restore(null, snapshot);
    Check("the parts can be put back again, each time a copy", keeper.Restored.SequenceEqual(new byte[] { 1, 2, 3 }));
    var restoreOnly = (RestoreOnly)keeps[2].Code[0];
    Check("a pack that had nothing is told, with null", restoreOnly.Times == 2 && restoreOnly.Last == null);
    CreateAStyle.CreateAStylePack.SayChanged(null, "Style the sofa");
    Check("a pack's build tool tells every pack with OnLotChanged", keeper.Changes.SequenceEqual(new[] { "Style the sofa" }));

    var saver = Running("made.saver", "Saver", new SavesOnly());
    var loader = Running("made.loader", "Loader", new LoadsOnly());
    var brokenSnapshot = Running("made.brokensnapshot", "Broken Snapshot", new BrokenSnapshot());
    var brokenRestore = Running("made.brokenrestore", "Broken Restore", new BrokenRestore());
    var stoppedLater = Running("made.stopped", "Stopped", new RestoreOnly());
    SetActive(new List<Pack> { keeps[0], saver, loader, brokenSnapshot, brokenRestore, stoppedLater });
    PackParts.Forget();
    Check("packs that keep something for a lot and cannot put it back are named", PackParts.NotTakingPart().SequenceEqual(new[] { "Saver", "Loader" }));
    PackParts.Snapshot(null);
    Check("a pack whose snapshot fails is named from then on", PackParts.NotTakingPart().Contains("Broken Snapshot"));
    Check("...and is not stopped: that is Simitone's to do", brokenSnapshot.Code.Count == 1);
    Check("a pack whose restore fails is said to have failed", PackParts.Restore(null, new PackParts.Parts()).SequenceEqual(new[] { "Broken Restore" }));
    Check("...and is named from then on", PackParts.NotTakingPart().Contains("Broken Restore"));
    stoppedLater.Code = new List<ISimitonePack>();
    stoppedLater.CodeError = "Its code failed and was stopped: elsewhere";
    Check("a pack that took part and has since been stopped is named: its patches may still hold what it had", PackParts.NotTakingPart().Contains("Stopped"));
    Check("the log says why", PackParts.Why("Saver").Contains("neither OnLotSnapshot nor OnLotRestore") && PackParts.Why("Stopped").Contains("stopped"));
    SetActive(new List<Pack>());
    PackParts.Forget();

    // ---- every code pack in this repository takes part ----
    var names = new[] { "Apartments", "BuildCheats", "CreateAStyle", "CutContent", "DiagonalDoors", "Driveways", "FamilyTree", "Foundations",
        "FurnishedMoveIn", "HalfWalls", "LifeStages", "OpenForBusiness", "RoofTool", "TerrainPaint", "TryForBaby", "Undo" };
    var real = new List<Pack>();
    foreach (var name in names)
    {
        try
        {
            var assembly = Assembly.Load(name);
            var code = assembly.GetExportedTypes().Where(x => typeof(ISimitonePack).IsAssignableFrom(x) && x.IsClass && !x.IsAbstract)
                .Select(x => (ISimitonePack)Activator.CreateInstance(x)).ToList();
            if (code.Count == 0) throw new Exception("no ISimitonePack in it");
            real.Add(new Pack() { Id = "sample." + name.ToLowerInvariant(), Code = code, Manifest = new PackManifest() { Name = name } });
        }
        catch (Exception e)
        {
            Check(name + " could be looked at: " + e.Message, false);
        }
    }
    SetActive(real);
    var refused = PackParts.NotTakingPart();
    Check("with every pack here on, undo is on" + (refused.Count > 0 ? " (refused by " + string.Join(", ", refused) + ")" : ""), refused.Count == 0 && real.Count == names.Length);
    SetActive(new List<Pack>());
}
finally
{
    try { Directory.Delete(FSO.Common.FSOEnvironment.UserDir, true); } catch { }
}

Console.WriteLine(failed == 0 ? "All checks passed." : failed + " check(s) failed.");
return failed == 0 ? 0 : 1;

class Keeper : ISimitonePack
{
    public byte[] Data;
    public byte[] Restored;
    public List<string> Changes = new List<string>();
    public byte[] OnLotSnapshot(VM vm) => Data;
    public void OnLotRestore(VM vm, byte[] data) { Restored = data; }
    public void OnLotSaved(VM vm, string lotPath) { }
    public void OnLotChanged(VM vm, string what) { Changes.Add(what); }
}

class NoLots : ISimitonePack
{
    public void OnContentReady() { }
}

class RestoreOnly : ISimitonePack
{
    public int Times;
    public byte[] Last = new byte[1];
    public void OnLotLoaded(VM vm) { }
    public void OnLotRestore(VM vm, byte[] data) { Times++; Last = data; }
}

class SavesOnly : ISimitonePack
{
    public void OnLotSaved(VM vm, string lotPath) { }
}

class LoadsOnly : ISimitonePack
{
    public void OnLotLoaded(VM vm, string lotPath) { }
}

class BrokenSnapshot : ISimitonePack
{
    public byte[] OnLotSnapshot(VM vm) => throw new Exception("broken on purpose");
}

class BrokenRestore : ISimitonePack
{
    public void OnLotRestore(VM vm, byte[] data) => throw new Exception("broken on purpose");
}
