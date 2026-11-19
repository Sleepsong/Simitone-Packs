/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Linq;
using HarmonyLib;
using Simitone.Client.Packs;

// Starts the pack as Simitone does (OnLoad, then OnContentReady) against the engine it is built against, and checks that
// it starts, that every patch it needs is attached, and that its primitive is registered, even when another pack has
// taken its number. A pack that does not start
// leaves its objects as grey blocks that cannot be placed or used, so this must never fail in the game.
// Usage: dotnet run --project Driveways/startup     (exits with 1 if anything fails)

int failed = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

var code = new Driveways.DrivewaysPack();
try { code.OnLoad(new Pack { Id = "sample.driveways" }); Check(true, "the pack starts (OnLoad)"); }
catch (Exception e) { Check(false, "the pack starts (OnLoad): " + e); }

var patched = Harmony.GetAllPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToHashSet();
foreach (var needed in new[] { "VM.Tick", "VMMultitileGroup.ChangePosition", "VMEntity.Delete", "VMGameObject.RefreshGraphic",
    "World.GetObjectIDAtScreenPos", "UIObjectHolder.ClearSelected", "ObjectComponent.get_World3D", "VM.Save", "VM.Load", "TS1GameScreen.Save" })
    Check(patched.Contains(needed), "patched " + needed);
Check(Driveways.DrivewaysPack.NotAttached.Count == 0, "every optional patch attached" +
    (Driveways.DrivewaysPack.NotAttached.Count == 0 ? "" : ": not " + string.Join("; ", Driveways.DrivewaysPack.NotAttached)));

try { code.OnContentReady(); Check(true, "the primitive is registered (OnContentReady)"); }
catch (Exception e) { Check(false, "the primitive is registered (OnContentReady): " + e.GetType().Name + ": " + e.Message); }
Check(FSO.SimAntics.VMContext.Primitives[Driveways.Shared.Opcode]?.Name == "driveways_drive", $"primitive {Driveways.Shared.Opcode} is the pack's");

Check(Driveways.DrivewaysPack.Primitive == Driveways.Shared.Opcode && Driveways.DrivewaysPack.PrimitiveNote == null, $"it is added under {Driveways.Shared.Opcode}, the number its objects call");

// ---- another pack has the number already (one that takes free numbers as it starts, and started first) ----
// as Cut Content took 200, which Driveways used until 0.18: the pack must still start, under another number
{
    var all = FSO.SimAntics.VMContext.Primitives;
    all[Driveways.DrivewaysPack.Primitive] = null;
    all[Driveways.Shared.Opcode] = new FSO.SimAntics.Engine.VMPrimitiveRegistration(new Driveways.VMDrive()) { Opcode = Driveways.Shared.Opcode, Name = "someone_else" };
    try { code.OnContentReady(); Check(true, "with its number taken, the pack still starts"); }
    catch (Exception e) { Check(false, "with its number taken, the pack still starts: " + e.GetType().Name + ": " + e.Message); }
    var op = Driveways.DrivewaysPack.Primitive;
    Check(op != Driveways.Shared.Opcode && all[op]?.Name == "driveways_drive", $"it is added under another free number ({op})");
    Check(all[Driveways.Shared.Opcode]?.Name == "someone_else", "the other pack keeps its own");
    Check(Driveways.DrivewaysPack.PrimitiveNote?.Contains("someone_else") == true, "the driveways report says so");
}

// ---- the objects' scripts can be changed to call another number ----
{
    var file = System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../pack/driveways/content/Downloads/Driveways.iff");
    var iff = new FSO.Files.Formats.IFF.IffFile(System.IO.Path.GetFullPath(file));
    int Calls(ushort op) => iff.List<FSO.Files.Formats.IFF.Chunks.BHAV>().Sum(b => b.Instructions.Count(i => i.Opcode == op));
    var before = Calls(Driveways.Shared.Opcode);
    Check(before > 0, $"the object file's scripts call {Driveways.Shared.Opcode} ({before} steps)");
    Check(Calls(200) == 0, "and none still call 200, which Cut Content takes");
    var changed = Driveways.VMDrive.Renumber(iff, Driveways.Shared.Opcode, 201);
    Check(changed == before && Calls(Driveways.Shared.Opcode) == 0 && Calls(201) == before, "renumbered, every one of them calls the new number");
}

Console.WriteLine(failed == 0 ? "The pack starts." : $"{failed} failed");
return failed == 0 ? 0 : 1;
