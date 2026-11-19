/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Utils;
using FSO.LotView.Model;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FSO.LotView;
using Microsoft.Xna.Framework;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;

namespace Driveways
{
    /// <summary>
    /// Driveways and cars Sims can drive, as a pack.
    ///
    /// The pack is two things. Its content layer is one object file with a driveway and some cars, written by the
    /// generator folder. Its code layer is this assembly: one new SimAntics primitive (VMDrive) that the objects'
    /// short scripts call, a console command, and Harmony patches: on the game's tick, the lot being copied, saved and loaded, an object being moved, deleted
    /// or given its picture, the game looking for an object under the cursor, and the player letting go of one. Every patch catches whatever
    /// goes wrong in it: the pack must never stop the game doing what it was doing.
    /// </summary>
    public class DrivewaysPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            // What the pack cannot do without comes first, and stops the pack if it is missing. Everything optional comes
            // after, each looked for and attached on its own, so nothing about one (not even being looked for, in a
            // Simitone that has two methods of that name) can stop the rest. The ones that did not attach are named in
            // the "driveways" report.
            var harmony = new Harmony("pack." + pack.Id);
            var tick = AccessTools.Method(typeof(VM), "Tick") ?? throw new Exception("VM.Tick was not found: this pack was made for a different version of Simitone");
            harmony.Patch(tick, postfix: new HarmonyMethod(typeof(DrivewaysPack), nameof(AfterTick)));
            //a driveway being put down snaps to the road
            var move = AccessTools.Method(typeof(VMMultitileGroup), "ChangePosition") ?? throw new Exception("VMMultitileGroup.ChangePosition was not found: this pack was made for a different version of Simitone");
            harmony.Patch(move, prefix: new HarmonyMethod(typeof(DrivewaysPack), nameof(BeforeMove)), postfix: new HarmonyMethod(typeof(DrivewaysPack), nameof(AfterMove)));
            //a driveway lays real floor under itself, and takes it up again when it goes
            var delete = AccessTools.Method(typeof(VMEntity), "Delete") ?? throw new Exception("VMEntity.Delete was not found: this pack was made for a different version of Simitone");
            harmony.Patch(delete, prefix: new HarmonyMethod(typeof(DrivewaysPack), nameof(BeforeDelete)));
            //the driveway is drawn with the game's own pavement, and the car with a real Sims 1 car's pictures when the game has one that fits
            var graphic = AccessTools.Method(typeof(VMGameObject), "RefreshGraphic") ?? throw new Exception("VMGameObject.RefreshGraphic was not found: this pack was made for a different version of Simitone");
            harmony.Patch(graphic, postfix: new HarmonyMethod(typeof(DrivewaysPack), nameof(AfterGraphic)));
            //a driveway that has been put down is hidden, its floor being what is seen: so the game is helped to find it
            //under the cursor in buy mode, and told again to hide it when it shows it after a move. both optional:
            //without them a driveway can still be bought, and sold from the lot's list of objects
            VMDrive.OnMessage = ShowMessage;
            Settings.Load();
            PackManager.RegisterCommand("driveways", Command);

            TryPatch(harmony, "finding a driveway under the cursor", () => AccessTools.Method(typeof(World), "GetObjectIDAtScreenPos"), postfix: nameof(AfterPick));
            TryPatch(harmony, "keeping a moved driveway hidden", () => AccessTools.Method(typeof(UIObjectHolder), "ClearSelected"), prefix: nameof(BeforeLetGo), postfix: nameof(AfterLetGo));
            //in 3D a driving car turns smoothly: its tiles' models are turned to their exact angle. optional: without it, it snaps as in 2D.
            //(the 3D model's matrix is patched, not the small RadianDirection getter it reads: that one is likely inlined into it)
            TryPatch(harmony, "smooth turns in 3D", () => AccessTools.PropertyGetter(typeof(FSO.LotView.Components.ObjectComponent), "World3D"), postfix: nameof(AfterWorld3D));

            //a drive under way is kept with the lot when the game takes a copy of it (to save it, or while the family is on a
            //community lot), and put back when the lot is, before anything runs. optional: without them a lot loads with its
            //cars parked. (without the first, a drive is still put back, by its own script)
            try { if (!string.IsNullOrEmpty(pack.Id)) VMDrive.PackId = pack.Id; } catch (Exception) { }
            TryPatch(harmony, "putting saved drives back before a loaded lot runs", () => tick, prefix: nameof(BeforeTick));
            TryPatch(harmony, "keeping drives with a copy of the lot", () => Single(typeof(VM), "Save", m => m.ReturnType.Name == "VMMarshal"), postfix: nameof(AfterCopy));
            TryPatch(harmony, "putting drives back with a lot", () => Single(typeof(VM), "Load", m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "VMMarshal"), postfix: nameof(AfterLoad));
            TryPatch(harmony, "keeping drives with a saved lot", () => Single(typeof(TS1GameScreen), "Save", m => m.GetParameters().Length == 0), postfix: nameof(AfterSave));
        }

        /// <summary>The optional patches that could not be attached, by what they are for.</summary>
        public static readonly List<string> NotAttached = new List<string>();

        /// <summary>The one method of this name the type declares that fits, or null if there is none, or more than one.</summary>
        private static System.Reflection.MethodInfo Single(Type type, string name, Func<System.Reflection.MethodInfo, bool> fits)
        {
            var found = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name && fits(m)).ToList();
            return found.Count == 1 ? found[0] : null;
        }

        /// <summary>
        /// The console command (Ctrl+Shift+C):
        ///   driveways                 what the pack makes of this lot (and writes driveways-report.txt)
        ///   driveways cars            lists the game's vehicles in driveways-cars.txt
        ///   driveways floors          lists the game's floors in driveways-floors.txt
        ///   driveways work            writes how the game's carpool works to driveways-work.txt
        ///   driveways floor <words>   paints driveways with the floor whose name has those words; "tarmac" for the road's; nothing for the pack's choice
        ///   driveways reverse         turns the cars round as they drive along the road, if they drive backwards
        ///   driveways door            Sims get in by the car's other side
        ///   driveways help            (or anything else) says what the commands are
        /// </summary>
        private static bool Command(string[] args)
        {
            if (args.Length == 0) return Report();
            switch (args[0].ToLowerInvariant())
            {
                case "cars": return ReportCars();
                case "floors": return ReportFloors();
                case "work": return ReportTo("driveways-work.txt", CarLook.ListWork, "How the game's carpool works is written out in ");
                case "floor":
                    Settings.Floor = string.Join(" ", args, 1, args.Length - 1);
                    Settings.Save();
                    DrivewayLook.Repaint();
                    try { VMDrive.Relay((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm); } catch (Exception) { }
                    Show("Driveways", "The driveway's look: " + DrivewayLook.Describe());
                    return true;
                case "door":
                    Settings.DoorOnRight = !Settings.DoorOnRight;
                    Settings.Save();
                    Show("Driveways", Settings.DoorOnRight ? "Sims now get in and out by the car's right side." : "Sims now get in and out by the car's left side, the driver's.");
                    return true;
                case "reverse":
                    Settings.ReverseCars = !Settings.ReverseCars;
                    Settings.Save();
                    Show("Driveways", Settings.ReverseCars ? "Cars now drive along the road with their lead tile at the back." : "Cars now drive along the road with their lead tile at the front.");
                    return true;
                case "help":
                default:
                    Show("Driveways", Help);
                    return true;
            }
        }

        private const string Help =
            "driveways: what the pack makes of this lot, written in full to driveways-report.txt\n\n" +
            "driveways floors: lists the floors a driveway can be paved with, in driveways-floors.txt\n\n" +
            "driveways floor <words from a floor's name>: paves driveways with that floor. \"driveways floor tarmac\" for the road's; \"driveways floor\" alone for the pack's choice\n\n" +
            "driveways reverse: turns the cars round, if they drive along the road backwards\n\n" +
            "driveways door: Sims get in by the car's other side, if getting in looks wrong\n\n" +
            "driveways cars, driveways work: lists of the game's vehicles and how its carpool works, for whoever is working on the pack";

        private static bool ReportTo(string name, Func<string> what, string says)
        {
            string message;
            try
            {
                var file = System.IO.Path.Combine(FSOEnvironment.UserDir, name);
                File.WriteAllText(file, what());
                message = says + file;
            }
            catch (Exception e) { message = "It could not be written: " + e.Message; }
            Show("Driveways", message);
            return true;
        }

        private static bool ReportFloors()
        {
            string message;
            try
            {
                var file = System.IO.Path.Combine(FSOEnvironment.UserDir, "driveways-floors.txt");
                var floors = DrivewayLook.Floors();
                File.WriteAllLines(file, new[] { $"{floors.Count} floors the game sells. To paint driveways with one: driveways floor <words from its name>", "" }
                    .Concat(floors.Select(f => $"{f.Name}  (number {f.ID}, {f.Price})")));
                message = $"The game's {floors.Count} floors are listed in " + file + "\n\nThe driveway's look now: " + DrivewayLook.Describe();
            }
            catch (Exception e) { message = "The list could not be written: " + e.Message; }
            Show("Driveways", message);
            return true;
        }

        /// <summary>
        /// "driveways": says where the pack thinks the road is on this lot, what the driveway and the cars are drawn
        /// with, and whether each car on it can be driven. For finding out why a car will not go.
        /// </summary>
        private static bool Report()
        {
            var vm = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm;
            if (vm == null) { Show("Driveways", "Open a lot first: this says what the pack makes of the lot being played."); return true; }
            var missing = (NotAttached.Count == 0 ? "" : "\n\nNot working with this Simitone: " + string.Join("; ", NotAttached) + ".")
                + (PrimitiveNote == null ? "" : "\n\n" + PrimitiveNote);
            var message = VMDrive.Describe(vm) + missing;
            try
            {
                //the same with maps of the lot, in a file that can be sent on
                var file = System.IO.Path.Combine(FSOEnvironment.UserDir, "driveways-report.txt");
                File.WriteAllText(file, VMDrive.DescribeWithMaps(vm) + missing);
                message += "\n\nWritten in full to " + file;
            }
            catch (Exception) { }
            Show("Driveways", message);
            return true;
        }

        /// <summary>
        /// Add the pack's primitive. Under the number the object file's scripts call if it is free; if another pack has it
        /// (one that takes numbers as it starts, and started first), under another free number, and the object file's
        /// scripts are changed in memory to call that one. Either way the objects work.
        /// </summary>
        public void OnContentReady()
        {
            var opcode = FreeOpcode(Shared.Opcode);
            PackManager.RegisterPrimitive(new VMPrimitiveRegistration(new VMDrive())
            {
                Opcode = opcode,
                Name = "driveways_drive",
                OperandModel = typeof(VMDriveOperand)
            });
            Primitive = opcode;
            PrimitiveNote = null;
            if (opcode != Shared.Opcode)
            {
                var taken = VMContext.Primitives[Shared.Opcode]?.Name ?? "another pack";
                var changed = VMDrive.Renumber(Shared.Opcode, opcode);
                PrimitiveNote = $"The pack's primitive is number {opcode}: {Shared.Opcode}, the one its objects call, is \"{taken}\". " +
                    $"{changed} of the objects' script steps were changed to call {opcode}.";
            }
        }

        /// <summary>The number the pack's primitive was added under, and why it is not the usual one if it is not.</summary>
        public static ushort Primitive;
        public static string PrimitiveNote;

        /// <summary>This number if it is free, else the highest free one at or above 200, else any free one above the game's.</summary>
        private static ushort FreeOpcode(ushort wanted)
        {
            var all = VMContext.Primitives;
            if (all[wanted] == null) return wanted;
            for (int op = all.Length - 1; op >= 200; op--) if (all[op] == null) return (ushort)op;
            for (int op = 199; op >= 128; op--) if (all[op] == null) return (ushort)op;
            throw new Exception("there is no free primitive number for the pack: other packs have taken them all");
        }

        /// <summary>Attach an optional patch. Whatever goes wrong, finding the method included, is noted and nothing more.</summary>
        private static void TryPatch(Harmony harmony, string what, Func<System.Reflection.MethodBase> find, string prefix = null, string postfix = null)
        {
            try
            {
                var method = find();
                if (method == null) { NotAttached.Add(what + " (not found in this Simitone)"); return; }
                harmony.Patch(method,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(DrivewaysPack), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(DrivewaysPack), postfix));
            }
            catch (Exception e) { NotAttached.Add(what + " (" + e.GetType().Name + ": " + e.Message + ")"); }
        }

        /// <summary>
        /// The engine asks for the matrix that places an object's 3D model. For a car tile driving in 3D, make it as the
        /// engine makes it (ObjectComponent.World3D), turned to the tile's exact angle in place of its eighths of a turn.
        /// </summary>
        public static void AfterWorld3D(FSO.LotView.Components.ObjectComponent __instance, ref Matrix __result)
        {
            if (VMDrive.FreeAngleCount == 0) return;
            var angle = VMDrive.FreeAngleOf(__instance);
            if (angle == null) return;
            var at = FSO.LotView.WorldSpace.GetWorldFromTile(__instance.Position);
            __result = Matrix.CreateScale(3f) * Matrix.CreateRotationY(-angle.Value)
                * Matrix.CreateTranslation(new Vector3(1.5f, 0.1f, 1.5f)) * Matrix.CreateTranslation(at);
        }

        /// <summary>
        /// The game has looked for an object under the cursor and found none. In buy mode, a driveway that has been put
        /// down may be there: it is hidden, its floor being what is seen, so it is not drawn to be found. Say it is.
        /// </summary>
        public static void AfterPick(World __instance, int x, int y, ref short __result)
        {
            if (__result != 0) return;
            try
            {
                var screen = GameFacade.Screens.CurrentUIScreen as TS1GameScreen;
                var control = screen?.LotControl;
                if (control == null || control.LiveMode || screen.vm == null || __instance.State.Level != 1) return;
                var tile = __instance.EstTileAtPosWithScroll(new Vector2(x, y) / FSOEnvironment.DPIScaleFactor);
                var id = VMDrive.DrivewayAt(screen.vm, (int)Math.Floor(tile.X), (int)Math.Floor(tile.Y));
                if (id != 0) __result = id;
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The player has put down, or let go of, something they were moving. The game then shows the real object as
        /// the copy they held was shown, which for a driveway is not as it should be once it is down: put it right.
        /// </summary>
        public static void BeforeLetGo(UIObjectHolder __instance, out VMMultitileGroup __state)
        {
            __state = null;
            try { __state = __instance.Holding?.RealEnt?.MultitileGroup; } catch (Exception) { }
        }

        public static void AfterLetGo(VMMultitileGroup __state)
        {
            if (__state == null || __state.Objects.Count == 0) return;
            try { VMDrive.AfterDrivewayMoves(__state, __state.Objects[0].Thread?.Context ?? (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm?.Context); } catch (Exception) { }
        }

        /// <summary>After the game has moved an object, or tried to: a driveway lays its floor where it now is.</summary>
        public static void AfterMove(VMMultitileGroup __instance, VMContext context)
        {
            try { VMDrive.AfterDrivewayMoves(__instance, context); } catch (Exception) { }
        }

        /// <summary>Before the game deletes an object: a driveway's tile puts back the floor that was under it.</summary>
        public static void BeforeDelete(VMEntity __instance, VMContext context)
        {
            try { if (__instance is VMGameObject && VMDrive.IsDrivewayGUID((__instance.MasterDefinition ?? __instance.Object.OBJ).GUID)) VMDrive.Unpave(__instance, context); } catch (Exception) { }
        }

        public static void AfterGraphic(VMGameObject __instance)
        {
            //(never let a picture stop the game)
            try { DrivewayLook.Dress(__instance); } catch (Exception) { }
            try { CarLook.Dress(__instance); } catch (Exception) { }
        }

        /// <summary>
        /// "driveways cars": writes the game's vehicles, and the tiles each stands on, to driveways-cars.txt. For
        /// fitting the pack's car to one of them.
        /// </summary>
        private static bool ReportCars()
        {
            string message;
            try
            {
                var file = System.IO.Path.Combine(FSOEnvironment.UserDir, "driveways-cars.txt");
                File.WriteAllText(file, CarLook.ListVehicles());
                message = "The game's vehicles are listed in " + file + "\n\nThe car's look now: " + CarLook.Describe();
            }
            catch (Exception e) { message = "The list could not be written: " + e.Message; }
            Show("Driveways", message);
            return true;
        }

        /// <summary>
        /// Before the game moves an object: a driveway being put down is snapped to the road, and a car being put down
        /// anywhere but on a driveway is refused (the move is not made, and the game is told it cannot go there).
        /// </summary>
        public static bool BeforeMove(VMMultitileGroup __instance, ref LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags, ref VMPlacementResult __result)
        {
            try
            {
                VMDrive.BeforeDrivewayMoves(__instance, context);
                VMDrive.SnapDriveway(__instance, ref pos, ref direction, context, flags);
                if (!VMDrive.CarMayGo(__instance, ref pos, ref direction, context, flags))
                {
                    __result = new VMPlacementResult(VMPlacementError.NotAllowedOnTerrain);
                    return false;
                }
            }
            catch (Exception) { } //(never let this stop an object being moved)
            return true;
        }

        public static void BeforeTick(VM __instance)
        {
            //(a lot just loaded mid-drive: its drives are put back before any script runs)
            try { VMDrive.RestoreSaved(__instance); } catch (Exception) { }
        }

        public static void AfterCopy(VM __instance, FSO.SimAntics.Marshals.VMMarshal __result)
        {
            try { VMDrive.Copied(__instance, __result); } catch (Exception) { }
        }

        public static void AfterLoad(VM __instance, object[] __args)
        {
            try { VMDrive.Loaded(__instance, __args != null && __args.Length > 0 ? __args[0] as FSO.SimAntics.Marshals.VMMarshal : null); } catch (Exception) { }
        }

        public static void AfterSave(TS1GameScreen __instance)
        {
            try { VMDrive.Saved(__instance?.vm); } catch (Exception) { }
        }

        public static void AfterTick(VM __instance)
        {
            //(each on its own, and neither allowed to stop the game's tick)
            try { VMDrive.RepairOldObjects(__instance); } catch (Exception) { }
            try { VMDrive.EndStaleTrips(__instance); } catch (Exception) { }
            try { VMDrive.TickCommutes(__instance); } catch (Exception) { }
        }

        private static void ShowMessage(VM fromVM, string message)
        {
            GameThread.NextUpdate(x =>
            {
                //only for the lot being played
                if ((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm != fromVM) return;
                Show("Go for a Drive", message);
            });
        }

        private static void Show(string title, string message)
        {
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = title,
                Message = message,
                Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
            });
            UIScreen.GlobalShowDialog(alert, true);
        }
    }
}
