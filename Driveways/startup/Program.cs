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
// it starts, that every patch it needs is attached, and that its primitive is registered. A pack that does not start
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

Console.WriteLine(failed == 0 ? "The pack starts." : $"{failed} failed");
return failed == 0 ? 0 : 1;
