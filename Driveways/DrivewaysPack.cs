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
using System.IO;
using System.Linq;
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
    /// Driveways and a car Sims can drive, as a pack.
    ///
    /// The pack is two things. Its content layer is one object file with a driveway piece and a car, written by the
    /// generator folder. Its code layer is this assembly: one new SimAntics primitive (VMDrive) that the objects'
    /// short scripts call, and two Harmony patches: one so a car is never left hidden if its drive is cut short, and
    /// one that snaps a driveway to the road as it is put down.
    /// </summary>
    public class DrivewaysPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
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
            VMDrive.OnMessage = ShowMessage;
            Settings.Load();
            PackManager.RegisterCommand("driveways", Command);
        }

        /// <summary>
        /// The console command (Ctrl+Shift+C):
        ///   driveways                 what the pack makes of this lot (and writes driveways-report.txt)
        ///   driveways cars            lists the game's vehicles in driveways-cars.txt
        ///   driveways floors          lists the game's floors in driveways-floors.txt
        ///   driveways work            writes how the game's carpool works to driveways-work.txt
        ///   driveways floor <words>   paints driveways with the floor whose name has those words; "tarmac" for the road's; nothing for the pack's choice
        ///   driveways reverse         turns the cars round as they drive along the road, if they drive backwards
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
                case "reverse":
                    Settings.ReverseCars = !Settings.ReverseCars;
                    Settings.Save();
                    Show("Driveways", Settings.ReverseCars ? "Cars now drive along the road with their lead tile at the back." : "Cars now drive along the road with their lead tile at the front.");
                    return true;
                default: return false;
            }
        }

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
        /// "driveways": says where the pack thinks the road is: says where the pack thinks the road is on this lot, and
        /// whether each car on it can be driven. For finding out why a car will not go.
        /// </summary>
        private static bool Report()
        {
            var vm = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm;
            if (vm == null) return false;
            var message = VMDrive.Describe(vm);
            try
            {
                //the same with maps of the lot, in a file that can be sent on
                var file = System.IO.Path.Combine(FSOEnvironment.UserDir, "driveways-report.txt");
                File.WriteAllText(file, VMDrive.DescribeWithMaps(vm));
                message += "\n\nWritten in full to " + file;
            }
            catch (Exception) { }
            Show("Driveways", message);
            return true;
        }

        public void OnContentReady()
        {
            PackManager.RegisterPrimitive(new VMPrimitiveRegistration(new VMDrive())
            {
                Opcode = Shared.Opcode,
                Name = "driveways_drive",
                OperandModel = typeof(VMDriveOperand)
            });
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
                if (!VMDrive.CarMayGo(__instance, pos, ref direction, context, flags))
                {
                    __result = new VMPlacementResult(VMPlacementError.NotAllowedOnTerrain);
                    return false;
                }
            }
            catch (Exception) { } //(never let this stop an object being moved)
            return true;
        }

        public static void AfterTick(VM __instance)
        {
            VMDrive.RepairOldObjects(__instance);
            VMDrive.EndStaleTrips(__instance);
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
