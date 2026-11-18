/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
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
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenForBusiness
{
    /// <summary>
    /// A business, after The Sims 2: Open for Business.
    ///   Stage 1: objects in the family's home can be put up for sale, and visiting Sims buy them.
    ///   Stage 2: what sells can be restocked, and an object can be made the till.
    ///   Stage 3: the family can buy a lot it is visiting and run the same business there.
    ///   Stage 4: visitors can be hired; customers come to like or dislike the business, which gives it a rank.
    ///   0.5:     Open for Business's own rules: its rank table and five perk trees, talent badges, sales pitches,
    ///            employee jobs and asking wages, restocking as work, open and closed, and price presets.
    ///
    /// The business itself is in Shop.cs, with its rules in Reputation.cs, Badges.cs and Sales.cs and its saved book
    /// in Book.cs; none of it touches the engine. This file is where it meets the game:
    ///
    ///  1. A pie menu has been built: add the business's entries to it.
    ///  2. An interaction is asked for: if it is one of those, it is this pack's to carry out.
    ///  3. The lot ticks: the business runs.
    ///  4. The lot screen updates: pointing at something for sale, the till or a Sim says what the business knows.
    ///  5. Saving: the business's book is this pack's own data for the lot, and the badges its data for the neighbourhood.
    ///
    /// Also the "business" console command: the ledger, perks, staff, badges, and buying and selling a business lot.
    /// </summary>
    public class OpenForBusinessPack : ISimitonePack
    {
        public const string Title = "Open for Business";

        /// <summary>
        /// The menu entries are not in any object's files, so they have no interaction number of their own. They use
        /// one no object has (a tree table would need 255 interactions), with a parameter that says which they are.
        /// </summary>
        private const byte MenuID = 254;
        private const short ParamPrice = 0x4F42, ParamStop = 0x4F43, ParamTill = 0x4F44, ParamTillOff = 0x4F45, ParamTend = 0x4F46,
            ParamRestock = 0x4F47, ParamHire = 0x4F48, ParamDismiss = 0x4F49, ParamHireRestocker = 0x4F4A, ParamHireSales = 0x4F4B,
            ParamJob = 0x4F4C, //+ Job: 0x4F4C to 0x4F4E
            ParamRaise = 0x4F4F, ParamCut = 0x4F50, ParamPayAsked = 0x4F51,
            ParamPerkUp = 0x4F52, ParamSpeech = 0x4F53, ParamRally = 0x4F54, ParamMark = 0x4F55,
            ParamOpen = 0x4F56, ParamClose = 0x4F57,
            ParamPitch = 0x4F58, //+ Pitch: 0x4F58 to 0x4F5C
            ParamPreset = 0x4F60, //+ preset: 0x4F60 to 0x4F64
            ParamLast = 0x4F6F;

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
            Shop.OnRankChange = RankChanged;
            Shop.OnBadge = BadgeEarned;
            Shop.OnPitch = Pitched;
            Shop.OnStaff = StaffNews;
            Shop.VisitingFamily = VisitingFamily;
            Shop.SaveVisitedBook = SaveVisitedBook;
            Shop.SaveBadges = SaveBadges;
            PackManager.RegisterCommand("business", Command);
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

        // ---- badges: one file for each neighbourhood, kept as this pack's data for its Neighborhood.iff ----

        private static string BadgesPath;

        private static string NeighbourhoodPath()
        {
            var userPath = Content.Get().Neighborhood?.UserPath;
            return string.IsNullOrEmpty(userPath) ? null : Path.Combine(userPath, "Neighborhood.iff");
        }

        private static void LoadBadges()
        {
            var path = NeighbourhoodPath();
            if (path == null || path == BadgesPath) return;
            if (BadgesPath != null && Shop.Badges.Dirty) SaveBadges();
            BadgesPath = path;
            Shop.Badges = BadgeBook.Load(LotPackData.Read(PackId, path));
        }

        private static void SaveBadges()
        {
            if (BadgesPath == null) return;
            LotPackData.Write(PackId, BadgesPath, (Shop.Badges.Count == 0) ? null : Shop.Badges.Save());
            Shop.Badges.Dirty = false;
        }

        // ---- 1. the menu ----

        public static void AfterPieMenu(VMEntity __instance, VM vm, VMEntity caller, List<VMPieMenuInteraction> __result)
        {
            if (__result == null || !Shop.IsShop(vm) || !Shop.InFamily(vm, caller)) return;
            void Add(string name, int param) => __result.Add(new VMPieMenuInteraction() { Name = name, ID = MenuID, Param0 = (short)param, Callee = __instance });
            var book = Shop.BookOf(vm);
            var actor = caller as VMAvatar;

            if (__instance is VMAvatar sim)
            {
                if (sim == caller)
                {
                    //the Sim's own menu: the business as a whole
                    Add(book.Closed ? "Business/Open for Business" : "Business/Close for Business", book.Closed ? ParamOpen : ParamClose);
                    if (book.Shelves.Count > 0) Add($"Business/Restock ({book.Shelves.Count} sold out)", ParamRestock);
                    if (book.Has(PerkTree.Perception, 3)) Add("Business/Look for Mark", ParamMark);
                    if (book.Has(PerkTree.Motivation, 5)) Add("Business/Rally Forth!", ParamRally);
                    return;
                }
                var employee = Shop.EmployeeOf(vm, sim);
                if (employee != null)
                {
                    foreach (Job job in Enum.GetValues(typeof(Job)))
                        if (job != employee.Job) Add("Business/Job/Make " + Shop.JobName(job), ParamJob + (int)job);
                    var asks = Shop.AskingWage(vm, sim);
                    if (employee.Wage < asks) Add($"Business/Pay/Pay What They Ask ({Money(asks)} an hour)", ParamPayAsked);
                    Add($"Business/Pay/Raise ({Money(employee.Wage)} to {Money(employee.Wage + Reputation.WageStep)})", ParamRaise);
                    if (employee.Wage > 0) Add($"Business/Pay/Cut ({Money(employee.Wage)} to {Money(Math.Max(0, employee.Wage - Reputation.WageStep))})", ParamCut);
                    Add("Business/Dismiss", ParamDismiss);
                    if (book.Has(PerkTree.Motivation, 2)) Add("Business/Perk Up", ParamPerkUp);
                    if (book.Has(PerkTree.Motivation, 3)) Add("Business/Motivational Speech", ParamSpeech);
                }
                else if (Shop.InFamily(vm, sim))
                {
                    if (book.Has(PerkTree.Motivation, 2)) Add("Business/Perk Up", ParamPerkUp);
                    if (book.Has(PerkTree.Motivation, 3)) Add("Business/Motivational Speech", ParamSpeech);
                }
                else if (Shop.IsCustomer(vm, sim))
                {
                    if (!Shop.IsBuying(vm, sim))
                    {
                        foreach (Pitch pitch in Enum.GetValues(typeof(Pitch)))
                            if (Shop.CanPitch(vm, actor, pitch)) Add("Sales/" + Sales.Name(pitch), ParamPitch + (int)pitch);
                    }
                    var wage = Shop.AskingWage(vm, sim);
                    Add($"Business/Hire/As Cashier ({Money(wage)} an hour)", ParamHire);
                    Add($"Business/Hire/As Restocker ({Money(wage)} an hour)", ParamHireRestocker);
                    Add($"Business/Hire/As Salesperson ({Money(wage)} an hour)", ParamHireSales);
                    if (book.Has(PerkTree.Motivation, 2)) Add("Business/Perk Up", ParamPerkUp);
                }
                return;
            }
            if (!(__instance is VMGameObject)) return;

            if (Shop.IsTill(vm, __instance))
            {
                Add("Till/Tend", ParamTend);
                var cost = Shop.RestockCost(vm);
                if (cost > 0) Add("Till/Restock (" + Money(cost) + ")", ParamRestock);
                Add(book.Closed ? "Till/Open for Business" : "Till/Close for Business", book.Closed ? ParamOpen : ParamClose);
                Add("Till/Stop Using as Till", ParamTillOff);
                return;
            }
            var catalog = Shop.CatalogItem(__instance);
            if (catalog == null) return;
            var catalogPrice = (int)catalog.Value.Price;
            var current = Shop.IsForSale(__instance) ? __instance.MultitileGroup.SalePrice : -1;
            var under = (current > 0) ? "For Sale (" + Money(current) + ")/Change Price/" : "Business/Sell/";
            for (int i = 0; i < Shop.PricePresets.Length; i++)
            {
                var price = (int)Math.Max(1, (long)catalogPrice * Shop.PricePresets[i].Percent / 100);
                if (price != current) Add(under + Shop.PricePresets[i].Name + " (" + Money(price) + ")", ParamPreset + i);
            }
            Add(under + "Custom Price...", ParamPrice);
            if (current > 0) Add("For Sale (" + Money(current) + ")/Take Off Sale", ParamStop);
            else Add("Business/Use as Till", ParamTill);
        }

        // ---- 2. choosing from it. the engine would look the number up in the object's own interactions and find nothing ----

        private static bool IsMine(VMNetInteractionCmd cmd)
        {
            return cmd.Interaction == MenuID && !cmd.Global && cmd.Param0 >= ParamPrice && cmd.Param0 <= ParamLast;
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
            var actor = caller ?? vm.GetObjectById(__instance.CallerID) as VMAvatar;
            var sim = obj as VMAvatar;
            var isTill = Shop.IsTill(vm, obj);
            var param = __instance.Param0;
            string problem = null, hint = null;

            if (param >= ParamPreset && param < ParamPreset + Shop.PricePresets.Length)
            {
                if (isTill) return false;
                problem = Shop.SetPricePercent(vm, obj, Shop.PricePresets[param - ParamPreset].Percent);
            }
            else if (param >= ParamPitch && param <= ParamPitch + (int)Pitch.Manipulate)
            {
                problem = Shop.StartPitch(vm, actor, sim, (Pitch)(param - ParamPitch));
            }
            else if (param >= ParamJob && param <= ParamJob + (int)Job.Salesperson)
            {
                Shop.SetJob(vm, sim, (Job)(param - ParamJob));
            }
            else switch (param)
            {
                case ParamPrice:
                    if (isTill || Shop.CatalogItem(obj) == null) return false;
                    GameThread.NextUpdate(x => AskPrice(vm, obj));
                    break;
                case ParamStop:
                    Shop.TakeOffSale(vm, obj);
                    break;
                case ParamTill:
                    if (Shop.CatalogItem(obj) == null) return false;
                    problem = Shop.SetTill(vm, obj);
                    break;
                case ParamTillOff:
                    if (!isTill) return false;
                    Shop.RemoveTill(vm);
                    break;
                case ParamTend:
                    if (!isTill) return false;
                    Shop.Tend(vm, actor);
                    break;
                case ParamRestock:
                    problem = Shop.StartRestock(vm, actor);
                    break;
                case ParamOpen:
                case ParamClose:
                    Shop.SetOpen(vm, param == ParamOpen);
                    GameThread.NextUpdate(x => { if (Playing(vm)) HITVM.Get().PlaySoundEvent(UISounds.Click); });
                    break;
                case ParamHire:
                case ParamHireRestocker:
                case ParamHireSales:
                    {
                        var job = (param == ParamHire) ? Job.Cashier : (param == ParamHireRestocker) ? Job.Restocker : Job.Salesperson;
                        problem = Shop.Hire(vm, sim, job);
                        if (problem == null && job == Job.Cashier && Shop.Till(vm) == null)
                            hint = "Hired. A cashier works the till, and this business has none yet: choose Business, Use as Till on an object.";
                        break;
                    }
                case ParamDismiss:
                    Shop.Dismiss(vm, sim);
                    break;
                case ParamRaise:
                    Shop.ChangeWage(vm, sim, Reputation.WageStep);
                    break;
                case ParamCut:
                    Shop.ChangeWage(vm, sim, -Reputation.WageStep);
                    break;
                case ParamPayAsked:
                    if (sim != null && Shop.EmployeeOf(vm, sim) != null) Shop.ChangeWage(vm, sim, Shop.AskingWage(vm, sim) - Shop.EmployeeOf(vm, sim).Wage);
                    break;
                case ParamPerkUp:
                    problem = Shop.PerkUp(vm, actor, sim);
                    break;
                case ParamSpeech:
                    problem = Shop.Speech(vm, actor, sim);
                    break;
                case ParamRally:
                    problem = Shop.Rally(vm, actor);
                    break;
                case ParamMark:
                    problem = Shop.LookForMark(vm, actor);
                    break;
                default:
                    return false;
            }
            if (problem != null) Later(vm, problem, true);
            else if (hint != null) Later(vm, hint, false);
            __result = true;
            return false;
        }

        /// <summary>A message for the player, after this tick, if they are still playing this lot.</summary>
        private static void Later(VM vm, string message, bool error)
        {
            GameThread.NextUpdate(x =>
            {
                if (!Playing(vm)) return;
                if (error) HITVM.Get().PlaySoundEvent(UISounds.Error);
                Tell(message);
            });
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

        private static void Sound(VM vm, string sound)
        {
            GameThread.NextUpdate(x =>
            {
                if (Playing(vm)) HITVM.Get().PlaySoundEvent(sound);
            });
        }

        private static void Sold(VM vm, string item, string buyer, int price) => Sound(vm, UISounds.BuyPlace); //ca-ching

        private static void Lost(VM vm, string customer) => Sound(vm, UISounds.Error);

        private static void Pitched(VM vm, Pitch pitch, bool worked, int meter, bool buying)
        {
            Sound(vm, buying ? UISounds.BuyPlace : worked ? UISounds.PieMenuSelect : UISounds.Error);
        }

        private static void StaffNews(VM vm, string news) => Later(vm, news, true);

        private static void BadgeEarned(VM vm, string sim, Talent talent, BadgeLevel level, bool family)
        {
            var message = $"{sim} has earned a {level} badge in {talent}.";
            if (talent == Talent.Sales && level > BadgeLevel.None) message += $"\n\nThey can now use {Sales.Name((Pitch)(int)level)} on customers.";
            if (!family) message += "\n\nEmployees ask for more pay with each badge. Pay them what they ask, or they will quit.";
            GameThread.NextUpdate(x =>
            {
                if (!Playing(vm)) return;
                HITVM.Get().PlaySoundEvent(UISounds.BuyPlace);
                Tell(message);
            });
        }

        private static void RankChanged(VM vm, int rank, bool newHigh)
        {
            var book = Shop.BookOf(vm);
            string message;
            if (newHigh)
            {
                message = $"The business has risen to rank {rank}: {Reputation.RankName(rank)}.";
                if (book.PerkPoints > 0) message += $"\n\nIt has {book.PerkPoints} perk point{(book.PerkPoints == 1 ? "" : "s")} to spend. Spend them before the rank falls, or they wait until it rises again. In the console: business perks";
            }
            else if (rank < book.HighestRank) message = $"The business has fallen to rank {rank}: {Reputation.RankName(rank)}. Fair prices, good service and a tended till win customers back.";
            else message = $"The business is back to rank {rank}: {Reputation.RankName(rank)}.";
            GameThread.NextUpdate(x =>
            {
                if (!Playing(vm)) return;
                HITVM.Get().PlaySoundEvent(newHigh ? UISounds.BuyPlace : UISounds.Error);
                Tell(message);
            });
        }

        // ---- 4. the engine has decided what the tooltip is. add what the business knows ----

        /// <summary>What was last added to a Sim's tooltip, to take off again before adding the next: the engine sets it only when the mouse moves.</summary>
        private static string LastNote;

        public static void AfterLotControlUpdate(UILotControl __instance, UpdateState state)
        {
            var vm = __instance.vm;
            if (state?.UIState == null || vm == null || !vm.Ready || __instance.ObjectHover <= 0) return;
            if (PieMenuOf(__instance) != null || !Shop.IsShop(vm)) return;
            var obj = vm.GetObjectById(__instance.ObjectHover);
            if (obj == null) return;

            if (obj is VMAvatar sim)
            {
                var props = state.UIState.TooltipProperties;
                if (!props.Show || state.UIState.Tooltip == null) return;
                var tip = state.UIState.Tooltip;
                if (LastNote != null && tip.EndsWith(LastNote)) tip = tip.Substring(0, tip.Length - LastNote.Length);
                var note = SimNote(vm, sim);
                LastNote = string.IsNullOrEmpty(note) ? null : "\n" + note;
                state.UIState.Tooltip = tip + (LastNote ?? "");
                return;
            }
            if (state.UIState.TooltipProperties.Show) return;

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
                text = Shop.IsOpen(vm) ? "The till" : "The till (closed)";
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

        private static string Badges(VM vm, VMAvatar sim)
        {
            var held = new List<string>();
            foreach (Talent talent in Enum.GetValues(typeof(Talent)))
            {
                var level = Shop.BadgeOf(vm, sim, talent);
                if (level > BadgeLevel.None) held.Add(talent + " " + level);
            }
            var text = string.Join(", ", held);
            if (Shop.Badges.IsBoosted(Shop.SimNumber(sim), Shop.GameHour(vm))) text += " (inspired)";
            return text;
        }

        /// <summary>What the business knows about a Sim, for their tooltip.</summary>
        private static string SimNote(VM vm, VMAvatar sim)
        {
            var book = Shop.BookOf(vm);
            var employee = Shop.EmployeeOf(vm, sim);
            if (employee != null)
            {
                var asks = Shop.AskingWage(vm, sim);
                var text = $"{Shop.JobName(employee.Job)}, {Money(employee.Wage)} an hour";
                if (employee.Wage < asks) text += $" (asks {Money(asks)})";
                var badges = Badges(vm, sim);
                return (badges == "") ? text : text + "\n" + badges;
            }
            if (Shop.InFamily(vm, sim)) return Badges(vm, sim);
            if (!Shop.IsCustomer(vm, sim)) return null;

            var parts = new List<string>();
            var number = Shop.SimNumber(sim);
            if (book.Knows(number)) parts.Add(Stars(book.StarsOf(number)));
            if (book.Has(PerkTree.Perception, 1)) parts.Add("mood " + Sales.MoodWord(Shop.Mood(sim)));
            var shopper = Shop.IsBuying(vm, sim) ? Shop.For(vm).Shoppers[sim.ObjectID] : null;
            if (shopper != null) parts.Add("buying " + Shop.NameOf(shopper.Item.BaseObject));
            else
            {
                var meter = Shop.MeterOf(vm, sim);
                if (meter != null && meter.Item?.BaseObject != null && !meter.Item.BaseObject.Dead)
                {
                    if (book.Has(PerkTree.Perception, 2)) parts.Add("wants " + Shop.NameOf(meter.Item.BaseObject) + $" ({meter.Meter}%)");
                    else if (meter.Meter > 0) parts.Add($"buy meter {meter.Meter}%");
                }
            }
            return string.Join(", ", parts);
        }

        private static string Stars(int stars)
        {
            if (stars == 0) return "no stars";
            return (stars > 0) ? new string('*', stars) + " " + stars + (stars == 1 ? " star" : " stars")
                : stars + (stars == -1 ? " star" : " stars");
        }

        // ---- 5. saving ----

        public void OnLotSaved(VM vm, string lotPath)
        {
            //only a home's book is written with its save. a business lot's is written as it changes, and is left alone here:
            //this is also called when a lot is saved from the neighbourhood's lot editor, with no family in it.
            if (lotPath == null || !Shop.IsHomeLot(vm)) return;
            LotPackData.Write(PackId, lotPath, Shop.ToSave(vm));
            if (Shop.Badges.Dirty) SaveBadges();
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            LoadBadges();
            if (lotPath == null) return; //handed back after a visit: its book is still here from before
            Shop.Loaded(vm, LotPackData.Read(PackId, lotPath));
        }

        // ---- the "business" command ----

        private static string NameOfSim(VM vm, uint number)
        {
            var onLot = vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().FirstOrDefault(x => Shop.SimNumber(x) == number);
            if (onLot != null) return onLot.Name;
            var neighbourhood = Content.Get().Neighborhood;
            var id = neighbourhood?.GetNeighborIDForGUID(number);
            return (id == null) ? "Sim " + number.ToString("x8") : neighbourhood.GetNeighborByID(id.Value)?.Name ?? "Sim " + number.ToString("x8");
        }

        private static string BadgeLine(VM vm, uint number)
        {
            var held = new List<string>();
            foreach (Talent talent in Enum.GetValues(typeof(Talent)))
            {
                var points = Shop.Badges.PointsOf(number, talent);
                var level = Shop.Badges.Earned(number, talent);
                var toNext = BadgeBook.ToNext(points);
                held.Add($"{talent} {BadgeBook.Describe(level)}" + ((toNext > 0) ? $" ({toNext} to go)" : ""));
            }
            return string.Join(", ", held);
        }

        private static bool Command(string[] args)
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
            var book = Shop.BookOf(vm);
            var lines = new List<string>();
            switch (word)
            {
                case "perks":
                    lines.Add($"Rank {book.Rank}: {Reputation.RankName(book.Rank)}. Perk points to spend: {book.PerkPoints}.");
                    lines.Add("");
                    foreach (PerkTree tree in Enum.GetValues(typeof(PerkTree)))
                    {
                        var level = book.PerkLevel(tree);
                        var line = $"{tree}: {level} of 5";
                        if (level > 0) line += ", " + Reputation.Perk(tree, level).Name;
                        lines.Add(line);
                        if (level < 5)
                        {
                            var next = Reputation.Perk(tree, level + 1);
                            lines.Add($"    next: {next.Name}. {next.Does}  (business perk {tree.ToString().ToLowerInvariant()})");
                        }
                    }
                    break;
                case "perk":
                    {
                        if (args.Length < 2) return false;
                        var tree = Reputation.FindTree(args[1]);
                        if (tree == null) return false;
                        var problem = Shop.TakePerk(vm, tree.Value, out var perk);
                        GameThread.NextUpdate(x =>
                        {
                            HITVM.Get().PlaySoundEvent((problem == null) ? UISounds.BuyPlace : UISounds.Error);
                            Tell(problem ?? (perk.Name + ": " + perk.Does));
                        });
                        return true;
                    }
                case "staff":
                    if (book.Employees.Count == 0) lines.Add("Nobody works here. Click a visiting Sim and choose Business, Hire.");
                    foreach (var employee in book.Employees)
                    {
                        var onLot = vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().FirstOrDefault(x => Shop.SimNumber(x) == employee.Sim);
                        var asks = Reputation.AskingWage(Shop.Badges.TotalLevels(employee.Sim), book.Has(PerkTree.Motivation, 1));
                        lines.Add($"{NameOfSim(vm, employee.Sim)}: {Shop.JobName(employee.Job)}, {Money(employee.Wage)} an hour (asks {Money(asks)})" + ((onLot == null) ? ", not here" : ""));
                        lines.Add("    " + BadgeLine(vm, employee.Sim) + ((employee.Underpaid > 0) ? $", underpaid {employee.Underpaid} hours" : ""));
                    }
                    lines.Add("");
                    lines.Add($"Wages paid so far: {Money(book.Wages)}");
                    break;
                case "badges":
                    foreach (var sim in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
                    {
                        if (!Shop.InFamily(vm, sim) && !Shop.IsEmployee(vm, sim)) continue;
                        lines.Add(sim.Name + ": " + BadgeLine(vm, Shop.SimNumber(sim)));
                    }
                    if (lines.Count == 0) lines.Add("Nobody of the family or staff is here.");
                    lines.Add("");
                    lines.Add($"Badges come with practice: Bronze at {BadgeBook.Thresholds[0]}, Silver at {BadgeBook.Thresholds[1]}, Gold at {BadgeBook.Thresholds[2]}. A point for each customer rung up, object restocked or sales pitch made.");
                    break;
                case "open":
                case "close":
                    Shop.SetOpen(vm, word == "open");
                    return true;
                case "restock":
                    Restock(vm);
                    return true;
                case "forget":
                    if (!Shop.ForgetShelves(vm)) GameThread.NextUpdate(x => Tell("On a business lot what has sold stays sold until it is restocked. Selling the lot puts everything back."));
                    return true;
                case "":
                    Ledger(vm, book, lines);
                    break;
                default:
                    return false;
            }
            var message = string.Join("\n", lines);
            GameThread.NextUpdate(x => Tell(message));
            return true;
        }

        private static void Ledger(VM vm, ShopBook book, List<string> lines)
        {
            var stock = Shop.ForSale(vm);
            var till = Shop.Till(vm);
            if (Shop.IsBusinessLot(vm)) lines.Add($"The family's business lot, bought for {Money(book.PricePaid)}.");
            lines.Add(book.Closed ? "Closed for business." : "Open for business.");
            var rankLine = $"Rank {book.Rank}: {Reputation.RankName(book.Rank)}, with {book.Stars} stars from {book.Loyalty.Count} customers";
            if (book.Rank < Reputation.MaxRank) rankLine += $" (rank {book.Rank + 1} at {Reputation.StarsForRank(book.Rank + 1)})";
            lines.Add(rankLine);
            if (book.PerkPoints > 0) lines.Add($"Perk points to spend: {book.PerkPoints}. Type: business perks");
            lines.Add($"Sold so far: {book.Sold} for {Money(book.Takings)}");
            if (book.Lost > 0) lines.Add($"Customers who gave up waiting: {book.Lost}");
            if (book.Employees.Count > 0) lines.Add($"Employees: {book.Employees.Count}, paid {Money(book.Wages)} so far. Type: business staff");
            if (till == null) lines.Add("No till: customers leave the money.");
            else
            {
                var tenders = Shop.Tenders(vm, till);
                lines.Add("Till: " + Shop.NameOf(till) + ((tenders.Count > 0) ? " (tended by " + string.Join(", ", tenders.Select(x => x.Name)) + ")" : " (nobody at it)"));
            }
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
            lines.Add("");
            lines.Add("Also: business perks, business staff, business badges, business open, business close");
        }
    }
}
