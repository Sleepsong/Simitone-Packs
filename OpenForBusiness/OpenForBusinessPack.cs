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
using FSO.Content;
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
    /// A home business, after The Sims 2: Open for Business.
    ///   Stage 1: objects in the family's home can be put up for sale at a price the player chooses, and visiting
    ///            Sims buy them.
    ///   Stage 2: what sells can be restocked, and an object can be made the till, where customers pay one of the
    ///            family instead of leaving the money.
    ///   Stage 3: the family can buy a lot it is visiting and run the same business there.
    ///   Stage 4: visitors can be hired to tend the till; customers come to like or dislike the business, which
    ///            gives it a rank, and each rank a perk.
    ///
    /// The business itself is in Shop.cs and touches nothing in the engine. This file is where it meets the game:
    ///
    ///  1. A pie menu has been built: add the business's entries to it.
    ///  2. An interaction is asked for: if it is one of those, it is this pack's to carry out.
    ///  3. The lot ticks: visitors look at what is for sale, walk over and buy.
    ///  4. The lot screen updates: hovering over something for sale, or the till, says so.
    ///  5. Saving: the business's book is this pack's own data for the lot.
    ///
    /// Also the "business" console command, which shows the book, restocks, forgets the empty shelves, buys and
    /// sells a business lot, and chooses perks.
    /// </summary>
    public class OpenForBusinessPack : ISimitonePack
    {
        public const string Title = "Open for Business";

        /// <summary>
        /// The menu entries are not in any object's files, so they have no interaction number of their own. They use
        /// one no object has (a tree table would need 255 interactions), with a parameter that says which they are.
        /// </summary>
        private const byte MenuID = 254;
        private const short ParamPrice = 0x4F42, ParamStop = 0x4F43, ParamTill = 0x4F44, ParamTillOff = 0x4F45, ParamTend = 0x4F46, ParamRestock = 0x4F47, ParamHire = 0x4F48, ParamDismiss = 0x4F49;

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
            Shop.OnLost = Lost;
            Shop.OnRankUp = RankUp;
            Shop.VisitingFamily = VisitingFamily;
            Shop.SaveVisitedBook = SaveVisitedBook;
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

        /// <summary>The game screen knows a lot is being visited, and by whom. While it is, the engine has no family for the lot.</summary>
        private static FSO.Files.Formats.IFF.Chunks.FAMI VisitingFamily(VM vm)
        {
            var screen = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
            return (screen != null && screen.vm == vm && screen.Downtown) ? screen.ActiveFamily : null;
        }

        /// <summary>The same file OnLotLoaded reads the book from when the lot is next visited.</summary>
        private static void SaveVisitedBook(VM vm, byte[] data)
        {
            var path = Content.Get().Neighborhood?.GetHousePath(vm.GetGlobalValue(10));
            if (!string.IsNullOrEmpty(path)) LotPackData.Write(PackId, path, data);
        }

        // ---- 1. the menu ----

        public static void AfterPieMenu(VMEntity __instance, VM vm, VMEntity caller, List<VMPieMenuInteraction> __result)
        {
            if (__result == null || !Shop.IsShop(vm) || !Shop.InFamily(vm, caller)) return;
            void Add(string name, short param) => __result.Add(new VMPieMenuInteraction() { Name = name, ID = MenuID, Param0 = param, Callee = __instance });

            if (__instance is VMAvatar sim)
            {
                //a visiting Sim can be hired, and an employee let go
                if (Shop.IsEmployee(vm, sim)) Add("Business/Dismiss", ParamDismiss);
                else if (Shop.IsCustomer(vm, sim)) Add("Business/Hire (" + Money(Shop.Wage) + " an hour)", ParamHire);
                return;
            }
            if (!(__instance is VMGameObject)) return;

            if (Shop.IsTill(vm, __instance))
            {
                Add("Till/Tend", ParamTend);
                var cost = Shop.RestockCost(vm);
                if (cost > 0) Add("Till/Restock (" + Money(cost) + ")", ParamRestock);
                Add("Till/Stop Using as Till", ParamTillOff);
                return;
            }
            if (Shop.CatalogItem(__instance) == null) return;
            if (Shop.IsForSale(__instance))
            {
                var under = "For Sale (" + Money(__instance.MultitileGroup.SalePrice) + ")/";
                Add(under + "Change Price...", ParamPrice);
                Add(under + "Take Off Sale", ParamStop);
            }
            else
            {
                Add("Business/Sell...", ParamPrice);
                Add("Business/Use as Till", ParamTill);
            }
        }

        // ---- 2. choosing from it. the engine would look the number up in the object's own interactions and find nothing ----

        private static bool IsMine(VMNetInteractionCmd cmd)
        {
            return cmd.Interaction == MenuID && !cmd.Global && cmd.Param0 >= ParamPrice && cmd.Param0 <= ParamDismiss;
        }

        public static bool BeforeVerify(VMNetInteractionCmd __instance, VM vm, ref bool __result)
        {
            if (!IsMine(__instance)) return true;
            __result = !__instance.FromNet && Shop.IsShop(vm); //the player's own choice only
            return false;
        }

        public static bool BeforeExecute(VMNetInteractionCmd __instance, VM vm, VMAvatar caller, ref bool __result)
        {
            if (!IsMine(__instance)) return true;
            __result = false;
            var obj = vm.GetObjectById(__instance.CalleeID);
            if (obj == null || !Shop.IsShop(vm)) return false;
            var isTill = Shop.IsTill(vm, obj);
            switch (__instance.Param0)
            {
                case ParamPrice:
                    if (isTill || Shop.CatalogItem(obj) == null) return false;
                    GameThread.NextUpdate(x => AskPrice(vm, obj));
                    break;
                case ParamStop:
                    Shop.TakeOffSale(vm, obj);
                    break;
                case ParamTill:
                    if (Shop.CatalogItem(obj) == null || Shop.SetTill(vm, obj) != null) return false;
                    break;
                case ParamTillOff:
                    if (!isTill) return false;
                    Shop.RemoveTill(vm);
                    break;
                case ParamTend:
                    if (!isTill) return false;
                    Shop.Tend(vm, caller ?? vm.GetObjectById(__instance.CallerID) as VMAvatar);
                    break;
                case ParamRestock:
                    if (!isTill) return false;
                    Restock(vm);
                    break;
                case ParamHire:
                    {
                        var problem = Shop.Hire(vm, obj as VMAvatar);
                        if (problem != null) GameThread.NextUpdate(x => { if (Playing(vm)) Tell(problem); });
                        else if (Shop.Till(vm) == null) GameThread.NextUpdate(x => { if (Playing(vm)) Tell("Hired. An employee tends the till, and this business has none yet: choose Business, Use as Till on an object. Wages are only paid while there is a till."); });
                        break;
                    }
                case ParamDismiss:
                    Shop.Dismiss(vm, obj as VMAvatar);
                    break;
            }
            __result = true;
            return false;
        }

        private static void Restock(VM vm)
        {
            var result = Shop.Restock(vm);
            var lines = new List<string>();
            if (result.Restocked > 0) lines.Add($"Put back {result.Restocked} for {Money(result.Spent)}.");
            if (result.CannotAfford > 0) lines.Add($"{result.CannotAfford} left empty: the family cannot afford them.");
            if (result.Blocked > 0) lines.Add($"{result.Blocked} left empty: something is in the way.");
            if (lines.Count == 0) lines.Add("There is nothing to restock.");
            Console.WriteLine("[business] restock: " + string.Join(" ", lines));
            GameThread.NextUpdate(x =>
            {
                if (!Playing(vm)) return;
                HITVM.Get().PlaySoundEvent((result.Restocked > 0) ? UISounds.ObjectPlace : UISounds.Error);
                if (result.CannotAfford > 0 || result.Blocked > 0 || result.Restocked == 0) Tell(string.Join("\n", lines));
            });
        }

        private static void Tell(string message)
        {
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = Title,
                Message = message,
                Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
            });
            UIScreen.GlobalShowDialog(alert, true);
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

        private static void Lost(VM vm, string customer)
        {
            GameThread.NextUpdate(x =>
            {
                if (Playing(vm)) HITVM.Get().PlaySoundEvent(UISounds.Error);
            });
        }

        private static void RankUp(VM vm, int rank)
        {
            GameThread.NextUpdate(x =>
            {
                if (!Playing(vm)) return;
                HITVM.Get().PlaySoundEvent(UISounds.BuyPlace);
                Tell($"The business has reached rank {rank} of {Reputation.MaxRank}.\n\nEach rank brings a perk. In the console, \"business perks\" lists them.");
            });
        }

        // ---- 4. the engine has decided what the tooltip is. if it left it empty over something for sale, show the price ----

        public static void AfterLotControlUpdate(UILotControl __instance, UpdateState state)
        {
            var vm = __instance.vm;
            if (state?.UIState == null || vm == null || !vm.Ready || __instance.ObjectHover <= 0) return;
            if (PieMenuOf(__instance) != null || state.UIState.TooltipProperties.Show) return;
            var obj = vm.GetObjectById(__instance.ObjectHover);
            if (obj == null || !Shop.IsShop(vm)) return;

            string text;
            if (Shop.IsForSale(obj))
            {
                var catalog = Shop.CatalogItem(obj);
                text = "For sale: " + Money(obj.MultitileGroup.SalePrice);
                if (catalog != null) text += " (catalog " + Money((int)catalog.Value.Price) + ")";
            }
            else if (Shop.IsTill(vm, obj))
            {
                var waiting = Shop.For(vm).Shoppers.Count;
                text = "The till";
                if (waiting > 0) text += ": " + waiting + ((waiting == 1) ? " customer" : " customers");
            }
            else return;
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
            //only a home's book is written with its save. a business lot's is written as it changes, and is left alone here:
            //this is also called when a lot is saved from the neighbourhood's lot editor, with no family in it.
            if (lotPath == null || !Shop.IsHomeLot(vm)) return;
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
            var vm = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm;
            if (vm == null || args.Length > 2) return false;
            var word = (args.Length == 0) ? "" : args[0].ToLowerInvariant();
            if (args.Length == 2 && word != "perk") return false;

            if (word == "buy")
            {
                var price = Shop.LotPrice(vm);
                var problem = Shop.BuyLot(vm);
                GameThread.NextUpdate(x =>
                {
                    HITVM.Get().PlaySoundEvent((problem == null) ? UISounds.BuyPlace : UISounds.Error);
                    Tell(problem ?? ("The family bought this lot for " + Money(price) + ".\n\n"
                        + "Whenever one of them is here they can put what is on the lot up for sale, as at home. "
                        + "To change what is on it, edit the lot from the neighbourhood."));
                });
                return true;
            }
            if (word == "sell")
            {
                var problem = Shop.SellLot(vm, out var refund);
                GameThread.NextUpdate(x =>
                {
                    HITVM.Get().PlaySoundEvent((problem == null) ? UISounds.MoneyBack : UISounds.Error);
                    Tell(problem ?? ("The family sold this lot for " + Money(refund) + ". It will be as it was built the next time anyone visits."));
                });
                return true;
            }
            if (!Shop.IsShop(vm))
            {
                string why;
                if (!Shop.IsVisiting(vm)) why = Shop.NotAShop;
                else if (Shop.BookOf(vm).Owner != 0) why = "This lot belongs to another family's business.";
                else why = "This lot can be bought as a business for " + Money(Shop.LotPrice(vm)) + ".\nType: business buy";
                GameThread.NextUpdate(x => Tell(why));
                return true;
            }
            if (word == "perks" || (word == "perk" && args.Length == 1))
            {
                var mine = Shop.BookOf(vm);
                var perks = new List<string>();
                perks.Add($"Rank {mine.Rank} of {Reputation.MaxRank}. Perks to choose: {mine.PerkPoints}.");
                perks.Add("");
                foreach (var perk in Reputation.Perks)
                {
                    perks.Add((mine.Has(perk.Perk) ? "[chosen] " : "") + perk.Name + ": " + perk.Does);
                    if (!mine.Has(perk.Perk)) perks.Add("    business perk " + perk.Word);
                }
                var list = string.Join("\n", perks);
                GameThread.NextUpdate(x => Tell(list));
                return true;
            }
            if (word == "perk")
            {
                var perk = Reputation.Find(args[1]);
                if (perk == null) return false;
                var problem = Shop.TakePerk(vm, perk.Perk);
                GameThread.NextUpdate(x =>
                {
                    HITVM.Get().PlaySoundEvent((problem == null) ? UISounds.BuyPlace : UISounds.Error);
                    Tell(problem ?? (perk.Name + ": " + perk.Does));
                });
                return true;
            }
            if (word == "restock")
            {
                Restock(vm);
                return true;
            }
            if (word == "forget")
            {
                if (!Shop.ForgetShelves(vm)) GameThread.NextUpdate(x => Tell("On a business lot what has sold stays sold until it is restocked. Selling the lot puts everything back."));
                return true;
            }
            if (word != "") return false;

            var stock = Shop.ForSale(vm);
            var book = Shop.BookOf(vm);
            var till = Shop.Till(vm);
            var lines = new List<string>();
            if (Shop.IsBusinessLot(vm)) lines.Add($"The family's business lot, bought for {Money(book.PricePaid)}.");
            lines.Add($"Sold so far: {book.Sold} for {Money(book.Takings)}");
            var rankLine = $"Rank {book.Rank} of {Reputation.MaxRank}, with {book.Stars} stars from {book.Loyalty.Count} customers";
            if (book.Rank < Reputation.MaxRank) rankLine += $" (rank {book.Rank + 1} at {Reputation.StarsForRank(book.Rank + 1)})";
            lines.Add(rankLine);
            if (book.PerkPoints > 0) lines.Add($"Perks to choose: {book.PerkPoints}. Type: business perks");
            if (book.Employees.Count > 0) lines.Add($"Employees: {book.Employees.Count}, paid {Money(book.Wages)} so far");
            if (book.Lost > 0) lines.Add($"Customers who gave up waiting: {book.Lost}");
            if (till == null) lines.Add("No till: customers leave the money.");
            else lines.Add("Till: " + Shop.NameOf(till) + (Shop.Tended(vm, till) ? " (tended)" : " (nobody at it)"));
            lines.Add("");
            if (stock.Count == 0) lines.Add("Nothing is for sale. Click an object and choose Business, Sell.");
            else
            {
                lines.Add($"For sale ({stock.Count}):");
                foreach (var item in stock.Take(8)) lines.Add(Shop.NameOf(item.BaseObject) + "  " + Money(item.SalePrice));
                if (stock.Count > 8) lines.Add($"and {stock.Count - 8} more");
            }
            if (book.Shelves.Count > 0)
            {
                lines.Add("");
                lines.Add($"Sold out: {book.Shelves.Count}. Restocking costs {Money(Shop.RestockCost(vm))}.");
            }
            var recent = Shop.For(vm).Recent;
            if (recent.Count > 0)
            {
                lines.Add("");
                lines.Add("Lately:");
                lines.AddRange(recent);
            }
            var message = string.Join("\n", lines);
            GameThread.NextUpdate(x => Tell(message));
            return true;
        }
    }
}
