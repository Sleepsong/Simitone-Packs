/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Utils;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.SimAntics;
using FSO.SimAntics.NetPlay.Model.Commands;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenForBusiness
{
    /// <summary>
    /// A home business, after The Sims 2: Open for Business. Stage 1: objects in the family's home can be put up
    /// for sale at a price the player chooses, and visiting Sims buy them.
    ///
    /// The business itself is in Shop.cs and touches nothing in the engine. This file is where it meets the game:
    ///
    ///  1. A pie menu has been built: add "Sell..." (or the price, with "Change Price..." and "Take Off Sale").
    ///  2. An interaction is asked for: if it is one of those, it is this pack's to carry out.
    ///  3. The lot ticks: visitors look at what is for sale, walk over and buy.
    ///  4. The lot screen updates: hovering over something for sale shows its price.
    ///  5. Saving: what the business has taken is this pack's own data for the lot.
    ///
    /// Also the "business" console command, which shows what is for sale and what has sold.
    /// </summary>
    public class OpenForBusinessPack : ISimitonePack
    {
        public const string Title = "Open for Business";

        /// <summary>
        /// The menu entries are not in any object's files, so they have no interaction number of their own. They use
        /// one no object has (a tree table would need 255 interactions), with a parameter that says which they are.
        /// </summary>
        private const byte MenuID = 254;
        private const short ParamPrice = 0x4F42, ParamStop = 0x4F43;

        private static string PackId;

        public void OnLoad(Pack pack)
        {
            PackId = pack.Id;
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
            Shop.OnSale = Sold;
            PackManager.RegisterCommand("business", ShowBusiness);
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(OpenForBusinessPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            return AccessTools.Method(type, name, (parameters.Length == 0) ? null : parameters) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static AccessTools.FieldRef<UILotControl, UIPieMenu> PieMenuOf;

        private static void Apply(Harmony harmony)
        {
            if (AccessTools.Field(typeof(UILotControl), "PieMenu") == null) throw new Exception("UILotControl.PieMenu was not found: this pack was made for a different version of Simitone");
            PieMenuOf = AccessTools.FieldRefAccess<UILotControl, UIPieMenu>("PieMenu");

            // 1
            harmony.Patch(Find(typeof(VMEntity), "GetPieMenu"), postfix: Mine(nameof(AfterPieMenu)));
            // 2
            harmony.Patch(Find(typeof(VMNetInteractionCmd), "Verify", typeof(VM), typeof(VMAvatar)), prefix: Mine(nameof(BeforeVerify)));
            harmony.Patch(Find(typeof(VMNetInteractionCmd), "Execute", typeof(VM), typeof(VMAvatar)), prefix: Mine(nameof(BeforeExecute)));
            // 3
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 4
            harmony.Patch(Find(typeof(UILotControl), "Update"), postfix: Mine(nameof(AfterLotControlUpdate)));
        }

        private static string Money(long amount) => "§" + amount.ToString("##,#0");

        // ---- 1. the menu ----

        public static void AfterPieMenu(VMEntity __instance, VM vm, VMEntity caller, List<VMPieMenuInteraction> __result)
        {
            if (__result == null || !(__instance is VMGameObject) || !Shop.IsHomeLot(vm) || !Shop.InFamily(vm, caller)) return;
            if (Shop.CatalogItem(__instance) == null) return;
            if (Shop.IsForSale(__instance))
            {
                var under = "For Sale (" + Money(__instance.MultitileGroup.SalePrice) + ")/";
                __result.Add(new VMPieMenuInteraction() { Name = under + "Change Price...", ID = MenuID, Param0 = ParamPrice, Callee = __instance });
                __result.Add(new VMPieMenuInteraction() { Name = under + "Take Off Sale", ID = MenuID, Param0 = ParamStop, Callee = __instance });
            }
            else
            {
                __result.Add(new VMPieMenuInteraction() { Name = "Sell...", ID = MenuID, Param0 = ParamPrice, Callee = __instance });
            }
        }

        // ---- 2. choosing from it. the engine would look the number up in the object's own interactions and find nothing ----

        private static bool IsMine(VMNetInteractionCmd cmd)
        {
            return cmd.Interaction == MenuID && !cmd.Global && (cmd.Param0 == ParamPrice || cmd.Param0 == ParamStop);
        }

        public static bool BeforeVerify(VMNetInteractionCmd __instance, VM vm, ref bool __result)
        {
            if (!IsMine(__instance)) return true;
            __result = !__instance.FromNet && Shop.IsHomeLot(vm); //the player's own choice only
            return false;
        }

        public static bool BeforeExecute(VMNetInteractionCmd __instance, VM vm, ref bool __result)
        {
            if (!IsMine(__instance)) return true;
            __result = false;
            var obj = vm.GetObjectById(__instance.CalleeID);
            if (obj == null || !Shop.IsHomeLot(vm) || Shop.CatalogItem(obj) == null) return false;
            if (__instance.Param0 == ParamStop) Shop.TakeOffSale(vm, obj);
            else GameThread.NextUpdate(x => AskPrice(vm, obj));
            __result = true;
            return false;
        }

        private static void AskPrice(VM vm, VMEntity obj)
        {
            if (!Playing(vm) || obj.Dead) return;
            var catalog = Shop.CatalogItem(obj);
            if (catalog == null) return;
            var catalogPrice = (int)catalog.Value.Price;
            var current = Shop.IsForSale(obj) ? obj.MultitileGroup.SalePrice : catalogPrice;

            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = "Sell " + Shop.NameOf(obj),
                Message = "In the catalog this costs " + Money(catalogPrice) + ". What should it sell for?\n\n"
                    + "Visitors buy a bargain almost every time they look at it, pay the catalog price about half the time, "
                    + "and will not pay twice the catalog price.",
                TextEntry = true,
                MaxChars = 6,
                Buttons = UIAlertButton.OkCancel(
                    (btn) =>
                    {
                        var typed = (alert.ResponseText ?? "").Trim().TrimStart('§', '$').Replace(",", "");
                        if (!int.TryParse(typed, out var price) || obj.Dead || Shop.SetPrice(vm, obj, price) != null)
                        {
                            HITVM.Get().PlaySoundEvent(UISounds.Error);
                            return; //leave the question up
                        }
                        UIScreen.RemoveDialog(alert);
                    },
                    (btn) => UIScreen.RemoveDialog(alert))
            });
            alert.ResponseText = current.ToString();
            UIScreen.GlobalShowDialog(alert, true);
        }

        // ---- 3 ----

        public static void AfterTick(VM __instance)
        {
            if (__instance.TS1) Shop.Tick(__instance);
        }

        private static bool Playing(VM vm)
        {
            return (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm == vm;
        }

        private static void Sold(VM vm, string item, string buyer, int price)
        {
            GameThread.NextUpdate(x =>
            {
                if (Playing(vm)) HITVM.Get().PlaySoundEvent(UISounds.BuyPlace); //ca-ching
            });
        }

        // ---- 4. the engine has decided what the tooltip is. if it left it empty over something for sale, show the price ----

        public static void AfterLotControlUpdate(UILotControl __instance, UpdateState state)
        {
            var vm = __instance.vm;
            if (state?.UIState == null || vm == null || !vm.Ready || __instance.ObjectHover <= 0) return;
            if (PieMenuOf(__instance) != null || state.UIState.TooltipProperties.Show) return;
            var obj = vm.GetObjectById(__instance.ObjectHover);
            if (obj == null || !Shop.IsForSale(obj) || !Shop.IsHomeLot(vm)) return;

            var catalog = Shop.CatalogItem(obj);
            var text = "For sale: " + Money(obj.MultitileGroup.SalePrice);
            if (catalog != null) text += " (catalog " + Money((int)catalog.Value.Price) + ")";
            state.UIState.TooltipProperties.Show = true;
            state.UIState.TooltipProperties.Color = Color.Black;
            state.UIState.TooltipProperties.Opacity = 1;
            state.UIState.TooltipProperties.Position = new Vector2(state.MouseState.X, state.MouseState.Y);
            state.UIState.TooltipProperties.UpdateDead = false;
            state.UIState.Tooltip = text;
        }

        // ---- 5. saving ----

        public void OnLotSaved(VM vm, string lotPath)
        {
            if (lotPath == null || !Shop.IsHomeLot(vm)) return; //a lot being visited has no business, and nothing of this pack's to lose
            LotPackData.Write(PackId, lotPath, Shop.ToSave(vm));
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            if (lotPath == null) return; //handed back after a visit: its totals are still here from before
            Shop.Loaded(vm, LotPackData.Read(PackId, lotPath));
        }

        // ---- the "business" command ----

        private static bool ShowBusiness(string[] args)
        {
            if (args.Length > 0) return false;
            var vm = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm;
            if (vm == null) return false;

            string message;
            if (!Shop.IsHomeLot(vm))
            {
                message = "A business can only be run from the family's own home.";
            }
            else
            {
                var stock = Shop.ForSale(vm);
                var totals = Shop.TotalsOf(vm);
                var lines = new List<string>();
                lines.Add($"Sold so far: {totals.Sold} for {Money(totals.Takings)}");
                lines.Add("");
                if (stock.Count == 0) lines.Add("Nothing is for sale. Click an object and choose Sell.");
                else
                {
                    lines.Add($"For sale ({stock.Count}):");
                    foreach (var item in stock.Take(10)) lines.Add(Shop.NameOf(item.BaseObject) + "  " + Money(item.SalePrice));
                    if (stock.Count > 10) lines.Add($"and {stock.Count - 10} more");
                }
                var recent = Shop.For(vm).Recent;
                if (recent.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Latest sales:");
                    lines.AddRange(recent);
                }
                message = string.Join("\n", lines);
            }

            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = Title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
            return true;
        }
    }
}
