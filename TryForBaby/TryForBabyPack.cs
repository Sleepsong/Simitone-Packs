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
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Engine.Utils;
using FSO.SimAntics.Primitives;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace TryForBaby
{
    /// <summary>
    /// Try for Baby, as in The Sims 2, added to the engine entirely from a pack.
    ///
    /// The same feature exists as an engine change (FreeSO branch simitone/try-for-baby). There it is one new class,
    /// VMTryForBaby, and a line or two in each of six places in the engine that call it. The class is here as it was
    /// (VMTryForBaby.cs, with one small property added): everything it uses in the engine is public. This file is the six calls, each as a Harmony
    /// patch, and the message Simitone's lot screen showed.
    ///
    ///  1. A pie menu is about to be built: give a bed with WooHoo the interaction.
    ///  2. A pie menu has been built, or one interaction is asked for: leave Try for Baby out where it isn't allowed.
    ///  3. A queued interaction starts: if it is Try for Baby, roll for the couple.
    ///  4. The game rolls its dice: while a couple is trying, make the roll come out as theirs did.
    ///  5. The game asks its baby question: while a couple is trying, answer it for them.
    ///  6. The lot ticks: see whether a try has ended.
    /// </summary>
    public class TryForBabyPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
            VMTryForBaby.OnResult = ShowResult;
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(TryForBabyPack), name);

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1 and 2
            harmony.Patch(Find(typeof(VMEntity), "GetPieMenu"), prefix: Mine(nameof(BeforePieMenu)), postfix: Mine(nameof(AfterPieMenu)));
            harmony.Patch(Find(typeof(VMEntity), "GetPieMenuForInteraction"), prefix: Mine(nameof(BeforePieMenuForInteraction)));
            // 3
            harmony.Patch(Find(typeof(VMThread), "ExecuteAction"), prefix: Mine(nameof(BeforeAction)));
            // 4
            harmony.Patch(Find(typeof(VMRandomNumber), "Execute"), prefix: Mine(nameof(SteerDice)));
            // 5
            harmony.Patch(Find(typeof(VMDialogPrivateStrings), "ExecuteGeneric"), prefix: Mine(nameof(AnswerQuestion)));
            // 6
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
        }

        // ---- 1 ----

        public static void BeforePieMenu(VMEntity __instance, VM vm, VMEntity caller)
        {
            VMTryForBaby.Prepare(vm, __instance, caller);
        }

        // ---- 2. the engine change skipped the interaction as the menu was built. from outside, it is taken out afterwards ----

        public static void AfterPieMenu(VMEntity __instance, VM vm, VMEntity caller, List<VMPieMenuInteraction> __result)
        {
            if (__result == null) return;
            __result.RemoveAll(item => !item.Global && item.Entry != null && !VMTryForBaby.Allowed(vm, caller, __instance, item.Entry));
        }

        /// <summary>The engine change checked this in the middle of the method, once it had found the interaction. Here it is found the same way first.</summary>
        public static bool BeforePieMenuForInteraction(VMEntity __instance, VM vm, VMEntity caller, int index, bool global, ref List<VMPieMenuInteraction> __result)
        {
            if (index < 0) return true;
            TTABInteraction entry;
            if (!global)
            {
                if (__instance.TreeTable == null || !__instance.TreeTable.InteractionByIndex.TryGetValue((uint)index, out entry)) return true;
            }
            else
            {
                if (vm.Context.GlobalTreeTable == null || !vm.Context.GlobalTreeTable.InteractionByIndex.TryGetValue((uint)index, out entry)) return true;
            }
            if (VMTryForBaby.Allowed(vm, caller, __instance, entry)) return true;
            __result = null;
            return false;
        }

        // ---- 3 ----

        public static void BeforeAction(VMEntity ___Entity, VMQueuedAction action)
        {
            VMTryForBaby.OnActionStart(___Entity, action);
        }

        // ---- 4. the engine change overrode the roll's result inside the primitive. from outside, the whole (small) primitive is done here when a roll is steered ----

        public static bool SteerDice(VMStackFrame context, VMPrimitiveOperand args, ref VMPrimitiveExitCode __result)
        {
            if (!VMTryForBaby.Trying) return true; //nearly always: this runs for every roll in the game
            var operand = (VMRandomNumberOperand)args;
            var range = (ushort)VMMemory.GetVariable(context, operand.RangeScope, operand.RangeData);
            var steered = VMTryForBaby.SteerRandom(context, range); //(only while trying for a baby)
            if (steered == null) return true;
            context.VM.Context.NextRandom(range); //the roll is still made, so the game's dice stay in step
            VMMemory.SetVariable(context, operand.DestinationScope, operand.DestinationData, (short)steered.Value);
            __result = VMPrimitiveExitCode.GOTO_TRUE;
            return false;
        }

        // ---- 5 ----

        public static bool AnswerQuestion(VMStackFrame context, VMPrimitiveOperand args, STR table, ref VMPrimitiveExitCode __result)
        {
            //only when the dialog is about to be shown, not while one is waiting for its answer
            if (!context.VM.TS1 || context.Thread.BlockingState is VMDialogResult) return true;
            var answer = VMTryForBaby.AnswerDialog(context, (VMDialogOperand)args, table);
            if (answer == null) return true;
            __result = answer.Value ? VMPrimitiveExitCode.GOTO_TRUE : VMPrimitiveExitCode.GOTO_FALSE;
            return false;
        }

        // ---- 6. (the engine change did this in the middle of the tick, right after the Sims' scripts run. the end of the tick does as well) ----

        public static void AfterTick(VM __instance)
        {
            if (__instance.TS1) VMTryForBaby.Tick(__instance);
        }

        // ---- the message ----

        /// <summary>
        /// A baby needs no message (the game asks for its name), but twins and a try with no baby get one.
        /// </summary>
        private static void ShowResult(VM fromVM, VMAvatar initiator, VMAvatar partner, VMTryForBabyOutcome outcome)
        {
            if (outcome == VMTryForBabyOutcome.Baby) return;
            var couple = (partner == null) ? initiator.Name : $"{initiator.Name} and {partner.Name}";
            string message;
            switch (outcome)
            {
                case VMTryForBabyOutcome.Twins:
                    message = $"Twins! {couple} have two new babies."; break;
                case VMTryForBabyOutcome.NoLuck:
                    message = $"No baby this time for {couple}. They can try again."; break;
                default:
                    message = $"No baby this time for {couple}: the game's own baby check didn't go ahead. They may need a better mood, or the household may be full."; break;
            }
            GameThread.NextUpdate(x =>
            {
                //only for the lot being played
                if ((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm != fromVM) return;
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = VMTryForBaby.InteractionName,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
