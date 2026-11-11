/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FurnishedMoveIn
{
    /// <summary>
    /// Furnished or unfurnished, as in The Sims 3: a family moving into a house can take it as it stands, or pay
    /// less and get it without its furniture.
    ///
    /// Nothing in the engine is changed, and nothing new is saved. There are three hooks:
    ///
    ///  1. A house is looked at while a family is moving in: show what it costs unfurnished, and let a family that
    ///     can only afford that go on.
    ///  2. The neighbourhood screen is set up: ask "furnished or unfurnished?" where Simitone asks "move in?".
    ///  3. The lot the family bought unfurnished has loaded (OnLotLoaded, no patch): take the furniture out.
    ///
    /// What counts as furniture, and what it is worth, is in Furniture.cs.
    /// </summary>
    public class FurnishedMoveInPack : ISimitonePack
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
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(FurnishedMoveInPack), name);

        private static T Found<T>(T member, string name) where T : class
        {
            return member ?? throw new Exception(name + " was not found: this pack was made for a different version of Simitone");
        }

        private static FieldInfo HouseSelectHandlers;

        private static void Apply(Harmony harmony)
        {
            HouseSelectHandlers = Found(AccessTools.Field(typeof(UINeighborhoodSelectionPanel), "OnHouseSelect"), "UINeighborhoodSelectionPanel.OnHouseSelect");
            // 1
            harmony.Patch(Found(AccessTools.Constructor(typeof(UIHouseSelectPanel), new Type[] { typeof(int) }), "UIHouseSelectPanel"),
                postfix: Mine(nameof(AfterHousePanel)));
            // 2
            harmony.Patch(Found(AccessTools.Method(typeof(TS1GameScreen), "NeighSelection", new Type[] { typeof(ushort), typeof(bool) }), "TS1GameScreen.NeighSelection"),
                postfix: Mine(nameof(AfterNeighSelection)));
        }

        // ---- the two prices of a house ----

        public class Offer
        {
            public short House;
            /// <summary>The game's own price for the house.</summary>
            public int Furnished;
            public int Unfurnished;
        }

        private static Offer LastOffer; //(a house's file is read again each time it is looked at. keep the last answer)

        /// <summary>
        /// What this house costs each way. Null when there is no choice to make: someone lives there, it is not a
        /// house to live in, or there is no furniture in it.
        /// </summary>
        public static Offer OfferFor(short house)
        {
            if (LastOffer != null && LastOffer.House == house) return LastOffer;
            var neigh = Content.Get().Neighborhood;
            if (neigh.GetFamilyForHouse(house) != null) return null;

            //(as the house panel decides it)
            short zone;
            if (!neigh.ZoningDictionary.TryGetValue(house, out zone)) zone = (short)((house >= 81 && house <= 89) ? 2 : 1);
            if (zone > 0) return null; //a community lot

            Offer offer = null;
            try
            {
                var file = neigh.GetHouse(house);
                var simi = file?.Get<SIMI>(1);
                var items = (simi == null) ? null : Furniture.FromHouse(file);
                if (items != null)
                {
                    //the furniture can't be worth more than the game counted for all the house's objects
                    var saving = Math.Min(Furniture.Value(items), simi.ObjectsValue);
                    if (saving > 0) offer = new Offer() { House = house, Furnished = simi.PurchaseValue, Unfurnished = simi.PurchaseValue - saving };
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[unfurnished] house " + house + " could not be priced: " + e.Message);
            }
            if (offer != null) LastOffer = offer;
            return offer;
        }

        private static string Money(int amount) => "§" + amount.ToString("##,#0");

        private static FAMI MovingIn(TS1GameScreen screen)
        {
            if (screen?.MoveInFamily == null) return null;
            return Content.Get().Neighborhood.GetFamily((ushort)screen.MoveInFamily.Value);
        }

        // ---- 1. the house panel has worked out its text and whether Move In can be pressed, for the furnished price ----

        public static void AfterHousePanel(UIHouseSelectPanel __instance, int houseID)
        {
            var family = MovingIn(UIScreen.Current as TS1GameScreen);
            if (family == null) return;
            var offer = OfferFor((short)houseID);
            if (offer == null) return;

            __instance.SecondaryText.Caption += "\nUnfurnished: " + Money(offer.Unfurnished);

            //Magic Town's houses also cost MagiCoins, which the panel has checked: leave its answer alone there
            var magic = houseID >= 90 && houseID <= 92;
            if (!magic && __instance.EnterLot.Disabled && family.Budget >= offer.Unfurnished) __instance.EnterLot.Disabled = false;
        }

        // ---- 2. the screen has just added its own answer to "a house was chosen". put ours in front of it ----

        public static void AfterNeighSelection(TS1GameScreen __instance)
        {
            var panel = __instance.TS1NeighPanel;
            if (panel == null) return;
            var screen = __instance;
            var original = (Action<int>)HouseSelectHandlers.GetValue(panel);
            HouseSelectHandlers.SetValue(panel, (Action<int>)((house) =>
            {
                //anything but a move with a choice to make goes on as it always has
                if (!AskHow(screen, (short)house)) original?.Invoke(house);
            }));
        }

        /// <summary>
        /// Ask how the family wants the house, in place of Simitone's "move in?". False if there is nothing to ask.
        /// </summary>
        private static bool AskHow(TS1GameScreen screen, short house)
        {
            var family = MovingIn(screen);
            if (family == null) return false;
            var offer = OfferFor(house);
            if (offer == null || family.Budget < offer.Unfurnished) return false;
            var familyID = screen.MoveInFamily.Value;

            var message = "Furnished: " + Money(offer.Furnished) + "\nThe house as it stands, with everything in it.\n\n"
                + "Unfurnished: " + Money(offer.Unfurnished) + "\nThe house without its furniture. The kitchen counters, stove and fridge, "
                + "the toilets, sinks, showers and baths, and the wall and ceiling lights stay.\n\n"
                + "The family has " + Money(family.Budget) + ".";
            if (family.Budget < offer.Furnished) message += " That is not enough for the house furnished.";

            UIMobileAlert alert = null;
            var buttons = new List<UIAlertButton>();
            if (family.Budget >= offer.Furnished)
                buttons.Add(new UIAlertButton(UIAlertButtonType.Yes, (b) => { alert.Close(); MoveIn(screen, offer, familyID, false); }, "Furnished"));
            buttons.Add(new UIAlertButton(UIAlertButtonType.No, (b) => { alert.Close(); MoveIn(screen, offer, familyID, true); }, "Unfurnished"));
            buttons.Add(new UIAlertButton(UIAlertButtonType.Cancel, (b) => alert.Close()));

            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = GameFacade.Strings.GetString("132", "0"),
                Message = message,
                Buttons = buttons.ToArray()
            });
            UIScreen.GlobalShowDialog(alert, true);
            return true;
        }

        /// <summary>The house the family is buying unfurnished, until its lot has loaded.</summary>
        private static Offer Bought;

        private static void MoveIn(TS1GameScreen screen, Offer offer, int familyID, bool unfurnished)
        {
            if (screen.MoveInFamily != familyID) return; //(no longer moving in: the dialog was left open over something else)
            var family = Content.Get().Neighborhood.GetFamily((ushort)familyID);
            var saving = offer.Furnished - offer.Unfurnished;
            if (unfurnished)
            {
                //Simitone takes the furnished price as the family moves in. hand over the difference first
                family.Budget += saving;
                Bought = offer;
            }
            var switcher = screen.GetChildren().OfType<UINeighbourhoodSwitcher>().LastOrDefault();
            try
            {
                screen.MoveInAndPlay(offer.House, familyID, switcher);
            }
            catch
            {
                if (unfurnished && family.HouseNumber != offer.House) family.Budget -= saving; //they never got in
                Bought = null;
                throw;
            }
            finally
            {
                LastOffer = null;
            }
        }

        // ---- 3. the lot has loaded ----

        public void OnLotLoaded(VM vm, string lotPath)
        {
            var bought = Bought;
            Bought = null; //whichever lot this is, the move is over
            if (bought == null || lotPath == null) return;
            var boughtPath = Content.Get().Neighborhood.GetHousePath(bought.House);
            if (!string.Equals(Path.GetFullPath(lotPath), Path.GetFullPath(boughtPath), StringComparison.OrdinalIgnoreCase)) return;
            TakeFurniture(vm, 0);
        }

        public void OnLotSaved(VM vm, string lotPath)
        {
            LastOffer = null; //the house is not what it was
        }

        /// <summary>
        /// Take the furniture out of the lot, once the lot's objects are there to take.
        /// </summary>
        private static void TakeFurniture(VM vm, int tries)
        {
            if ((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm != vm) return; //the lot has been left
            if (vm.Context?.Architecture == null || vm.Entities.Count == 0)
            {
                if (tries < 600) GameThread.NextUpdate((state) => TakeFurniture(vm, tries + 1));
                return;
            }

            var items = Furniture.FromLot(vm);
            var remove = Furniture.ToRemove(items);

            //what stands on something goes before the thing it stands on
            var byId = new Dictionary<short, LotItem>();
            foreach (var item in items)
                foreach (var id in item.Ids) byId[id] = item;
            int Depth(LotItem item)
            {
                var depth = 0;
                while (item.Container != 0 && depth < 8 && byId.TryGetValue(item.Container, out item)) depth++;
                return depth;
            }

            var taken = 0;
            foreach (var item in remove.OrderByDescending(Depth))
            {
                var obj = vm.GetObjectById(item.Ids[0]);
                if (obj == null || obj.Dead) continue;
                try
                {
                    obj.Delete(true, vm.Context);
                    taken++;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[unfurnished] " + obj.ToString() + " could not be taken out: " + e.Message);
                }
            }
            Console.WriteLine("[unfurnished] took " + taken + " things out of the house");
        }
    }
}
