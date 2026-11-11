/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Interfaces;
using FSO.SimAntics.Entities;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OpenForBusiness
{
    /// <summary>What a home business has taken so far. Kept for each lot, in this pack's own file.</summary>
    public class ShopTotals
    {
        public int Sold;
        public long Takings;

        public byte[] Save()
        {
            using (var mem = new MemoryStream())
            using (var writer = new BinaryWriter(mem))
            {
                writer.Write((byte)1); //version
                writer.Write(Sold);
                writer.Write(Takings);
                return mem.ToArray();
            }
        }

        public static ShopTotals Load(byte[] data)
        {
            var result = new ShopTotals();
            if (data == null || data.Length < 13 || data[0] != 1) return result;
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadByte();
                result.Sold = Math.Max(0, reader.ReadInt32());
                result.Takings = Math.Max(0, reader.ReadInt64());
            }
            return result;
        }
    }

    /// <summary>A visitor who has decided to buy something and is on their way to it.</summary>
    public class Shopper
    {
        public short AvatarID;
        public VMMultitileGroup Item;
        /// <summary>How many checks (about a second each) this shopper has been on their way.</summary>
        public int Waited;
    }

    /// <summary>The business on one loaded lot: who is shopping, and what happened lately.</summary>
    public class LotShop
    {
        public Dictionary<short, Shopper> Shoppers = new Dictionary<short, Shopper>();
        /// <summary>Visitors who looked and did not buy, and the check at which they may look again.</summary>
        public Dictionary<short, int> LookAgainAt = new Dictionary<short, int>();
        public List<string> Recent = new List<string>();
        public int Ticks;
        public int Checks;
        /// <summary>The house this lot was last seen to be, once known (0 before).</summary>
        public short House;
    }

    /// <summary>
    /// The home business itself. Nothing here patches the engine: OpenForBusinessPack.cs calls in from its patches.
    ///
    /// What is for sale is the engine's own field, VMMultitileGroup.SalePrice, which FreeSO added for its player shops
    /// and which is already written into a lot's save. The Sims 1 never sets it, so anything with a price was priced
    /// by this pack. FreeSO also marks a priced object as disabled, which stops anyone using it; this pack does not,
    /// so that the family can go on using what is on display and the price can be changed from the object's menu.
    /// </summary>
    public static class Shop
    {
        // ---- tuning ----

        /// <summary>The VM runs 30 ticks a second at normal speed. Visitors are looked at this often.</summary>
        public const int TicksPerCheck = 30;
        /// <summary>Each check, a visitor with nothing in mind has a 1 in this chance of looking at something for sale.</summary>
        public const int BrowseOneIn = 20;
        /// <summary>A visitor who looked and did not buy waits this many checks before looking again.</summary>
        public const int LookAgainAfter = 60;
        /// <summary>A shopper who has not reached the object after this many checks buys it from where they stand.</summary>
        public const int BuyAnywayAfter = 45;
        /// <summary>A shopper gives up after this many checks (the object stayed in use, or could not be taken).</summary>
        public const int GiveUpAfter = 120;
        /// <summary>A shopper this close to the object (in tiles) has reached it.</summary>
        public const int ReachTiles = 2;
        public const int MaxPrice = 999999;

        private const uint GOTO_GUID = 0x000007C4; //the engine's "go here" marker (VMNetGotoCmd)

        private static readonly Random Dice = new Random();
        private static readonly ConditionalWeakTable<VM, LotShop> Shops = new ConditionalWeakTable<VM, LotShop>();

        /// <summary>Totals by house number, for the houses seen this run.</summary>
        private static readonly Dictionary<short, ShopTotals> Totals = new Dictionary<short, ShopTotals>();
        /// <summary>Lots loaded from a file, with the totals read for them, until their house number is known.</summary>
        private static readonly ConditionalWeakTable<VM, ShopTotals> LoadedTotals = new ConditionalWeakTable<VM, ShopTotals>();

        /// <summary>Called when something sells, on the game thread: the object's name, the buyer's and the price.</summary>
        public static Action<VM, string, string, int> OnSale;

        public static LotShop For(VM vm) => Shops.GetValue(vm, x => new LotShop());

        // ---- which lots, Sims and objects ----

        /// <summary>A business can run where the family being played lives, not on a lot they are visiting.</summary>
        public static bool IsHomeLot(VM vm)
        {
            if (vm == null || !vm.TS1) return false;
            var family = vm.TS1State?.CurrentFamily;
            return family != null && family.HouseNumber > 0 && family.HouseNumber == vm.GetGlobalValue(10);
        }

        public static bool InFamily(VM vm, VMEntity sim)
        {
            var family = vm.TS1State?.CurrentFamily;
            return family != null && sim is VMAvatar avatar && !avatar.IsPet
                && avatar.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) == family.ChunkID;
        }

        /// <summary>A customer is a visiting neighbour: not one of the family, not a pet, and not a maid, burglar or other NPC.</summary>
        public static bool IsCustomer(VM vm, VMAvatar avatar)
        {
            return avatar != null && !avatar.Dead && !avatar.IsPet
                && avatar.Position != LotTilePos.OUT_OF_WORLD
                && avatar.GetPersonData(VMPersonDataVariable.PersonType) == 1
                && !InFamily(vm, avatar);
        }

        public static uint GuidOf(VMEntity obj)
        {
            return obj.MasterDefinition?.GUID ?? obj.Object.OBJ.GUID;
        }

        /// <summary>The object's entry in the buy catalog, if it has one. Only catalog objects can be sold.</summary>
        public static ObjectCatalogItem? CatalogItem(VMEntity obj)
        {
            if (!(obj is VMGameObject) || obj.MultitileGroup?.BaseObject == null) return null;
            var item = Content.Get().WorldCatalog?.GetItemByGUID(GuidOf(obj.MultitileGroup.BaseObject));
            return (item != null && item.Value.Price > 0) ? item : null;
        }

        public static string NameOf(VMEntity obj)
        {
            var name = CatalogItem(obj)?.Name;
            return string.IsNullOrWhiteSpace(name) ? obj.ToString() : name;
        }

        public static bool IsForSale(VMEntity obj)
        {
            return obj is VMGameObject && obj.MultitileGroup != null && obj.MultitileGroup.SalePrice > 0;
        }

        public static List<VMMultitileGroup> ForSale(VM vm)
        {
            return vm.Entities.Where(x => IsForSale(x) && !x.Dead && x.MultitileGroup.BaseObject == x).Select(x => x.MultitileGroup).ToList();
        }

        // ---- pricing ----

        /// <summary>Puts an object up for sale. Returns null, or why not.</summary>
        public static string SetPrice(VM vm, VMEntity obj, int price)
        {
            if (!IsHomeLot(vm)) return "A business can only be run from the family's own home.";
            if (CatalogItem(obj) == null) return "Only objects from the buy catalog can be sold.";
            if (price < 1 || price > MaxPrice) return "The price must be between 1 and " + MaxPrice + ".";
            obj.MultitileGroup.SalePrice = price;
            return null;
        }

        public static void TakeOffSale(VM vm, VMEntity obj)
        {
            if (obj?.MultitileGroup == null) return;
            obj.MultitileGroup.SalePrice = -1;
            var shop = For(vm);
            foreach (var gone in shop.Shoppers.Values.Where(x => x.Item == obj.MultitileGroup).ToList()) shop.Shoppers.Remove(gone.AvatarID);
        }

        /// <summary>
        /// How likely a customer who looks at an object is to buy it, in percent, from its price against the catalog's.
        /// A bargain nearly always sells; the catalog price sells half the time; at twice the catalog price nobody buys.
        /// </summary>
        public static int ChanceToBuy(int salePrice, int catalogPrice)
        {
            if (catalogPrice <= 0) return 0;
            var percent = (long)salePrice * 100 / catalogPrice; //the price as a percentage of the catalog's
            if (percent <= 50) return 95;
            if (percent <= 100) return (int)(95 - (percent - 50) * 45 / 50); //95 down to 50
            if (percent < 200) return (int)(50 - (percent - 100) * 50 / 100); //50 down to 0
            return 0;
        }

        // ---- the lot's tick ----

        public static void Tick(VM vm)
        {
            var shop = For(vm);
            if (++shop.Ticks < TicksPerCheck) return;
            shop.Ticks = 0;
            if (!IsHomeLot(vm)) return;
            NoteHouse(vm, shop);
            shop.Checks++;

            // 1. Sims on their way to buy
            foreach (var shopper in shop.Shoppers.Values.ToList())
            {
                var avatar = vm.GetObjectById(shopper.AvatarID) as VMAvatar;
                var item = shopper.Item;
                if (!IsCustomer(vm, avatar) || item.BaseObject == null || item.BaseObject.Dead || item.SalePrice <= 0 || ++shopper.Waited > GiveUpAfter)
                {
                    shop.Shoppers.Remove(shopper.AvatarID);
                    continue;
                }
                if (!Reached(avatar, item) && shopper.Waited < BuyAnywayAfter) continue;
                //something in use, or holding something, stays until it is free
                if (item.BaseObject.IsUserMovable(vm.Context, true) != VMPlacementError.Success) continue;
                shop.Shoppers.Remove(shopper.AvatarID);
                Sell(vm, shop, item, avatar);
            }

            // 2. Sims who might look at something
            List<VMMultitileGroup> stock = null;
            foreach (var avatar in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().ToList())
            {
                if (!IsCustomer(vm, avatar) || shop.Shoppers.ContainsKey(avatar.ObjectID)) continue;
                if (shop.LookAgainAt.TryGetValue(avatar.ObjectID, out var at) && shop.Checks < at) continue;
                if (Dice.Next(BrowseOneIn) != 0) continue;

                if (stock == null) stock = ForSale(vm).Where(x => !shop.Shoppers.Values.Any(y => y.Item == x)).ToList();
                if (stock.Count == 0) break;
                var item = stock[Dice.Next(stock.Count)];
                var catalog = CatalogItem(item.BaseObject);
                if (catalog == null || Dice.Next(100) >= ChanceToBuy(item.SalePrice, (int)catalog.Value.Price))
                {
                    shop.LookAgainAt[avatar.ObjectID] = shop.Checks + LookAgainAfter;
                    continue;
                }
                stock.Remove(item);
                shop.LookAgainAt.Remove(avatar.ObjectID);
                shop.Shoppers[avatar.ObjectID] = new Shopper() { AvatarID = avatar.ObjectID, Item = item };
                WalkTo(vm, avatar, item);
            }
        }

        private static int Tile(short pos) => pos >> 4;

        private static bool Reached(VMAvatar avatar, VMMultitileGroup item)
        {
            foreach (var part in item.Objects)
            {
                if (part.Position == LotTilePos.OUT_OF_WORLD || part.Position.Level != avatar.Position.Level) continue;
                if (Math.Abs(Tile(part.Position.x) - Tile(avatar.Position.x)) <= ReachTiles
                    && Math.Abs(Tile(part.Position.y) - Tile(avatar.Position.y)) <= ReachTiles) return true;
            }
            return false;
        }

        /// <summary>
        /// Sends a Sim to stand by an object, the way the player's "go here" does: the engine's own marker object is
        /// put on a tile beside it and its first interaction is queued for the Sim. If none of that is there to use
        /// the Sim stays put, and buys from where they are once BuyAnywayAfter has passed.
        /// </summary>
        private static void WalkTo(VM vm, VMAvatar avatar, VMMultitileGroup item)
        {
            try
            {
                var taken = new HashSet<(int, int)>(item.Objects.Select(x => (Tile(x.Position.x), Tile(x.Position.y))));
                var around = new (int x, int y)[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
                foreach (var part in item.Objects)
                {
                    if (part.Position == LotTilePos.OUT_OF_WORLD) continue;
                    foreach (var step in around.OrderBy(x => Dice.Next()))
                    {
                        var tx = Tile(part.Position.x) + step.x;
                        var ty = Tile(part.Position.y) + step.y;
                        if (taken.Contains((tx, ty))) continue;
                        var pos = new LotTilePos((short)(tx * 16 + 8), (short)(ty * 16 + 8), part.Position.Level);
                        if (vm.Context.IsOutOfBounds(pos)) continue;
                        if (vm.Context.ObjectQueries.GetObjectsAt(pos)?.Any(x => x is VMGameObject && x.MultitileGroup != item) ?? false) continue;

                        var marker = vm.Context.CreateObjectInstance(GOTO_GUID, pos, Direction.NORTH)?.Objects.FirstOrDefault();
                        if (marker == null) return; //this game has no marker object
                        var menu = (marker.Position == LotTilePos.OUT_OF_WORLD) ? null : marker.GetPieMenu(vm, avatar, true, false);
                        if (menu == null || menu.Count == 0)
                        {
                            marker.Delete(true, vm.Context);
                            continue;
                        }
                        marker.PushUserInteraction(menu[0].ID, avatar, vm.Context, false, new short[] { menu[0].Param0, 0, 0, 0 });
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                //walking over is for show. the sale still goes through.
                Console.WriteLine("[business] could not send " + avatar + " to " + item.BaseObject + ": " + e.Message);
            }
        }

        private static void Sell(VM vm, LotShop shop, VMMultitileGroup item, VMAvatar buyer)
        {
            var family = vm.TS1State.CurrentFamily;
            var price = item.SalePrice;
            var name = NameOf(item.BaseObject);
            var buyerName = buyer.Name;

            item.SalePrice = -1;
            item.Delete(vm.Context);
            family.Budget += price;

            var totals = TotalsFor(shop.House);
            totals.Sold++;
            totals.Takings += price;
            shop.Recent.Insert(0, $"{name} to {buyerName} for §{price:##,#0}");
            if (shop.Recent.Count > 8) shop.Recent.RemoveAt(shop.Recent.Count - 1);
            Console.WriteLine($"[business] sold {name} to {buyerName} for {price}");
            OnSale?.Invoke(vm, name, buyerName, price);
        }

        // ---- totals, saved for each lot ----

        private static ShopTotals TotalsFor(short house)
        {
            if (!Totals.TryGetValue(house, out var totals)) Totals[house] = totals = new ShopTotals();
            return totals;
        }

        /// <summary>The first time a lot's house number is known, what was read from its file becomes that house's totals.</summary>
        private static void NoteHouse(VM vm, LotShop shop)
        {
            var house = vm.GetGlobalValue(10);
            if (shop.House == house) return;
            shop.House = house;
            if (LoadedTotals.TryGetValue(vm, out var loaded))
            {
                Totals[house] = loaded;
                LoadedTotals.Remove(vm);
            }
        }

        public static ShopTotals TotalsOf(VM vm)
        {
            var shop = For(vm);
            NoteHouse(vm, shop);
            return TotalsFor(shop.House);
        }

        /// <summary>A lot came from its file: these are its totals (none, if the file had none), whatever house number it turns out to have.</summary>
        public static void Loaded(VM vm, byte[] data)
        {
            LoadedTotals.Remove(vm);
            LoadedTotals.Add(vm, ShopTotals.Load(data));
        }

        /// <summary>The totals to write beside a lot's save, or null if it has no business to speak of.</summary>
        public static byte[] ToSave(VM vm)
        {
            if (!IsHomeLot(vm)) return null;
            var totals = TotalsOf(vm);
            return (totals.Sold == 0 && totals.Takings == 0) ? null : totals.Save();
        }
    }
}
