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
            harmony.Patch(move, prefix: new HarmonyMethod(typeof(DrivewaysPack), nameof(BeforeMove)));
            VMDrive.OnMessage = ShowMessage;
            PackManager.RegisterCommand("driveways", args => Report());
        }

        /// <summary>
        /// The "driveways" console command (Ctrl+Shift+C): says where the pack thinks the road is on this lot, and
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

        public static void BeforeMove(VMMultitileGroup __instance, ref LotTilePos pos, ref Direction direction, VMContext context, VMPlaceRequestFlags flags)
        {
            VMDrive.SnapDriveway(__instance, ref pos, ref direction, context, flags);
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
