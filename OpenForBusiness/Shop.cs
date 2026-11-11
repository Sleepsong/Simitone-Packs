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
    /// <summary>Somewhere an object stood until it sold: what it was, where, and what it was priced at.</summary>
    public class Shelf
    {
        public uint GUID;
        public LotTilePos Position;
        public Direction Direction;
        public int Price;
    }

    /// <summary>
    /// A home business's books: what it has taken, its till, and the shelves that have sold out.
    /// Kept for each lot, in this pack's own file.
    /// </summary>
    public class ShopBook
    {
        public const int MaxShelves = 200;

        public int Sold;
        public long Takings;
        /// <summary>Customers who came to the till and left without being served.</summary>
        public int Lost;
        /// <summary>The object that is the till, or 0 for none. Objects keep their numbers in a lot's save.</summary>
        public short TillID;
        public List<Shelf> Shelves = new List<Shelf>();

        public bool Empty => Sold == 0 && Takings == 0 && Lost == 0 && TillID == 0 && Shelves.Count == 0;

        public byte[] Save()
        {
            using (var mem = new MemoryStream())
            using (var writer = new BinaryWriter(mem))
            {
                writer.Write((byte)2); //version
                writer.Write(Sold);
                writer.Write(Takings);
                writer.Write(Lost);
                writer.Write(TillID);
                writer.Write(Shelves.Count);
                foreach (var shelf in Shelves)
                {
                    writer.Write(shelf.GUID);
                    writer.Write(shelf.Position.x);
                    writer.Write(shelf.Position.y);
                    writer.Write(shelf.Position.Level);
                    writer.Write((byte)shelf.Direction);
                    writer.Write(shelf.Price);
                }
                return mem.ToArray();
            }
        }

        public static ShopBook Load(byte[] data)
        {
            var result = new ShopBook();
            if (data == null || data.Length < 13 || (data[0] != 1 && data[0] != 2)) return result;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    var version = reader.ReadByte();
                    result.Sold = Math.Max(0, reader.ReadInt32());
                    result.Takings = Math.Max(0, reader.ReadInt64());
                    if (version < 2) return result; //stage 1 kept only the totals
                    result.Lost = Math.Max(0, reader.ReadInt32());
                    result.TillID = reader.ReadInt16();
                    var count = Math.Min(MaxShelves, reader.ReadInt32());
                    for (int i = 0; i < count; i++)
                    {
                        var shelf = new Shelf();
                        shelf.GUID = reader.ReadUInt32();
                        var x = reader.ReadInt16();
                        var y = reader.ReadInt16();
                        shelf.Position = new LotTilePos(x, y, reader.ReadSByte());
                        shelf.Direction = (Direction)reader.ReadByte();
                        shelf.Price = reader.ReadInt32();
                        if (shelf.Price > 0) result.Shelves.Add(shelf);
                    }
                }
            }
            catch (EndOfStreamException)
            {
                //cut short: keep what was read
            }
            return result;
        }
    }

    /// <summary>A visitor who has decided to buy something and is on their way to it.</summary>
    public class Shopper
    {
        public short AvatarID;
        public VMMultitileGroup Item;
        /// <summary>How many checks (about a second each) this shopper has been on their way, or waiting.</summary>
        public int Waited;
        /// <summary>With a till: how many checks someone has been ringing this shopper up.</summary>
        public int Served;
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
        /// <summary>A shopper gives up after this many checks (nobody came to the till, or the object stayed in use).</summary>
        public const int GiveUpAfter = 120;
        /// <summary>Ringing a customer up at the till takes this many checks.</summary>
        public const int RingUpChecks = 3;
        /// <summary>While someone tends the till, visitors look at things this many times as often...</summary>
        public const int TendedBrowseTimes = 2;
        /// <summary>...and are this much more likely (in percent) to buy what they look at.</summary>
        public const int TendedChanceBonus = 15;
        /// <summary>A shopper this close to the object (in tiles) has reached it.</summary>
        public const int ReachTiles = 2;
        public const int MaxPrice = 999999;

        private const uint GOTO_GUID = 0x000007C4; //the engine's "go here" marker (VMNetGotoCmd)

        private static readonly Random Dice = new Random();
        private static readonly ConditionalWeakTable<VM, LotShop> Shops = new ConditionalWeakTable<VM, LotShop>();

        /// <summary>Books by house number, for the houses seen this run.</summary>
        private static readonly Dictionary<short, ShopBook> Books = new Dictionary<short, ShopBook>();
        /// <summary>Lots loaded from a file, with the book read for them, until their house number is known.</summary>
        private static readonly ConditionalWeakTable<VM, ShopBook> LoadedBooks = new ConditionalWeakTable<VM, ShopBook>();

        /// <summary>Called when something sells, on the game thread: the object's name, the buyer's and the price.</summary>
        public static Action<VM, string, string, int> OnSale;
        /// <summary>Called when a customer leaves the till without being served, on the game thread.</summary>
        public static Action<VM, string> OnLost;

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
            if (IsTill(vm, obj)) return "The till is not for sale.";
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

        /// <summary>The same, when someone is tending the till. What nobody would buy, they still will not.</summary>
        public static int ChanceToBuy(int salePrice, int catalogPrice, bool tended)
        {
            var chance = ChanceToBuy(salePrice, catalogPrice);
            return (tended && chance > 0) ? Math.Min(95, chance + TendedChanceBonus) : chance;
        }

        // ---- the till ----

        /// <summary>The lot's till, if it has one that is still there.</summary>
        public static VMEntity Till(VM vm)
        {
            if (!IsHomeLot(vm)) return null;
            var book = BookOf(vm);
            if (book.TillID == 0) return null;
            var till = vm.GetObjectById(book.TillID);
            if (till is VMGameObject && !till.Dead && till.Position != LotTilePos.OUT_OF_WORLD) return till;
            if (till == null || till.Dead || !(till is VMGameObject)) book.TillID = 0; //sold in buy mode, or otherwise gone
            return null; //(picked up: still the till once it is put down)
        }

        public static bool IsTill(VM vm, VMEntity obj)
        {
            if (obj?.MultitileGroup == null || !IsHomeLot(vm)) return false;
            var id = BookOf(vm).TillID;
            return id != 0 && obj.MultitileGroup.Objects.Any(x => x.ObjectID == id);
        }

        /// <summary>Makes an object the lot's till, in place of any other. Returns null, or why not.</summary>
        public static string SetTill(VM vm, VMEntity obj)
        {
            if (!IsHomeLot(vm)) return "A business can only be run from the family's own home.";
            if (!(obj is VMGameObject) || obj.MultitileGroup?.BaseObject == null) return "That cannot be a till.";
            TakeOffSale(vm, obj);
            BookOf(vm).TillID = obj.MultitileGroup.BaseObject.ObjectID;
            return null;
        }

        public static void RemoveTill(VM vm)
        {
            if (IsHomeLot(vm)) BookOf(vm).TillID = 0;
        }

        /// <summary>Someone tends the till while one of the family stands by it.</summary>
        public static bool Tended(VM vm, VMEntity till)
        {
            if (till == null) return false;
            return vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().Any(x => InFamily(vm, x) && !x.Dead && Reached(x, till.MultitileGroup));
        }

        /// <summary>Sends one of the family to stand by the till.</summary>
        public static void Tend(VM vm, VMAvatar sim)
        {
            var till = Till(vm);
            if (till != null && sim != null) WalkTo(vm, sim, till.MultitileGroup);
        }

        // ---- restocking ----

        /// <summary>What it costs to put an object back: its catalog price, or 0 if it is not in the catalog any more.</summary>
        public static int RestockCost(Shelf shelf)
        {
            var item = Content.Get().WorldCatalog?.GetItemByGUID(shelf.GUID);
            return (item == null) ? 0 : (int)item.Value.Price;
        }

        public static long RestockCost(VM vm)
        {
            return IsHomeLot(vm) ? BookOf(vm).Shelves.Sum(x => (long)RestockCost(x)) : 0;
        }

        public struct RestockResult
        {
            public int Restocked, CannotAfford, Blocked;
            public long Spent;
        }

        /// <summary>
        /// Buys the sold objects again at the catalog's price and puts each back where it stood, at the price it sold
        /// for. A shelf stays empty if the family cannot afford it or something is in the way; it is forgotten if the
        /// object is not in the catalog any more.
        /// </summary>
        public static RestockResult Restock(VM vm)
        {
            var result = new RestockResult();
            if (!IsHomeLot(vm)) return result;
            var family = vm.TS1State.CurrentFamily;
            var book = BookOf(vm);
            foreach (var shelf in book.Shelves.ToList())
            {
                var cost = RestockCost(shelf);
                if (cost <= 0)
                {
                    book.Shelves.Remove(shelf);
                    continue;
                }
                if (family.Budget < cost)
                {
                    result.CannotAfford++;
                    continue;
                }
                if (!Place(vm, shelf, cost))
                {
                    result.Blocked++;
                    continue;
                }
                family.Budget -= cost;
                book.Shelves.Remove(shelf);
                result.Restocked++;
                result.Spent += cost;
            }
            return result;
        }

        /// <summary>Puts a new object on a shelf, the way buying one from the catalog does (VMNetBuyObjectCmd).</summary>
        private static bool Place(VM vm, Shelf shelf, int cost)
        {
            VMMultitileGroup group = null;
            try
            {
                group = vm.Context.CreateObjectInstance(shelf.GUID, LotTilePos.OUT_OF_WORLD, shelf.Direction);
                if (group == null) return false;
                group.ChangePosition(shelf.Position, shelf.Direction, vm.Context, VMPlaceRequestFlags.UserPlacement);
                group.ExecuteEntryPoint(11, vm.Context); //User Placement
                if (group.Objects.Count == 0) return false;
                if (group.BaseObject.Position == LotTilePos.OUT_OF_WORLD)
                {
                    group.Delete(vm.Context);
                    return false;
                }
                group.InitialPrice = cost;
                group.SalePrice = shelf.Price;
                var placed = group.BaseObject;
                vm.GlobalLink?.RegisterNewObject(vm, placed, (short objID, uint pid) =>
                {
                    var obj = vm.GetObjectById(objID);
                    if (obj != null) obj.PersistID = pid;
                });
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("[business] could not restock " + shelf.GUID.ToString("x8") + ": " + e.Message);
                try { if (group != null && group.Objects.Count > 0) group.Delete(vm.Context); } catch { }
                return false;
            }
        }

        public static void ForgetShelves(VM vm)
        {
            if (IsHomeLot(vm)) BookOf(vm).Shelves.Clear();
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

            var till = Till(vm);
            var tended = Tended(vm, till);

            // 1. Sims on their way to buy. With a till they go there and wait for one of the family to ring them up.
            //    Without one they go to the object and leave the money.
            var serving = false;
            foreach (var shopper in shop.Shoppers.Values.OrderByDescending(x => x.Served).ThenByDescending(x => x.Waited).ToList())
            {
                var avatar = vm.GetObjectById(shopper.AvatarID) as VMAvatar;
                var item = shopper.Item;
                if (!IsCustomer(vm, avatar) || item.BaseObject == null || item.BaseObject.Dead || item.SalePrice <= 0)
                {
                    shop.Shoppers.Remove(shopper.AvatarID);
                    continue;
                }
                if (++shopper.Waited > GiveUpAfter)
                {
                    shop.Shoppers.Remove(shopper.AvatarID);
                    shop.LookAgainAt[avatar.ObjectID] = shop.Checks + LookAgainAfter;
                    if (till != null) Lose(vm, shop, item, avatar);
                    continue;
                }
                var there = Reached(avatar, (till != null) ? till.MultitileGroup : item) || shopper.Waited >= BuyAnywayAfter;
                if (!there) continue;
                if (till != null)
                {
                    //one customer at a time, the one who has waited longest
                    if (!tended || serving) continue;
                    serving = true;
                    if (++shopper.Served < RingUpChecks) continue;
                }
                //something in use, or holding something, stays until it is free
                if (item.BaseObject.IsUserMovable(vm.Context, true) != VMPlacementError.Success) continue;
                shop.Shoppers.Remove(shopper.AvatarID);
                Sell(vm, shop, item, avatar);
            }

            // 2. Sims who might look at something
            List<VMMultitileGroup> stock = null;
            var browseOneIn = tended ? Math.Max(1, BrowseOneIn / TendedBrowseTimes) : BrowseOneIn;
            foreach (var avatar in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().ToList())
            {
                if (!IsCustomer(vm, avatar) || shop.Shoppers.ContainsKey(avatar.ObjectID)) continue;
                if (shop.LookAgainAt.TryGetValue(avatar.ObjectID, out var at) && shop.Checks < at) continue;
                if (Dice.Next(browseOneIn) != 0) continue;

                if (stock == null) stock = ForSale(vm).Where(x => !shop.Shoppers.Values.Any(y => y.Item == x)).ToList();
                if (stock.Count == 0) break;
                var item = stock[Dice.Next(stock.Count)];
                var catalog = CatalogItem(item.BaseObject);
                if (catalog == null || Dice.Next(100) >= ChanceToBuy(item.SalePrice, (int)catalog.Value.Price, tended))
                {
                    shop.LookAgainAt[avatar.ObjectID] = shop.Checks + LookAgainAfter;
                    continue;
                }
                stock.Remove(item);
                shop.LookAgainAt.Remove(avatar.ObjectID);
                shop.Shoppers[avatar.ObjectID] = new Shopper() { AvatarID = avatar.ObjectID, Item = item };
                WalkTo(vm, avatar, (till != null) ? till.MultitileGroup : item);
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

            var book = BookFor(shop.House);
            var shelf = new Shelf()
            {
                GUID = GuidOf(item.BaseObject),
                Position = item.BaseObject.Position,
                Direction = item.BaseObject.Direction,
                Price = price
            };

            item.SalePrice = -1;
            item.Delete(vm.Context);
            family.Budget += price;

            if (shelf.Position != LotTilePos.OUT_OF_WORLD && book.Shelves.Count < ShopBook.MaxShelves) book.Shelves.Add(shelf);
            book.Sold++;
            book.Takings += price;
            Note(shop, $"{name} to {buyerName} for §{price:##,#0}");
            Console.WriteLine($"[business] sold {name} to {buyerName} for {price}");
            OnSale?.Invoke(vm, name, buyerName, price);
        }

        private static void Lose(VM vm, LotShop shop, VMMultitileGroup item, VMAvatar customer)
        {
            var name = customer.Name;
            BookFor(shop.House).Lost++;
            Note(shop, $"{name} gave up waiting at the till ({NameOf(item.BaseObject)})");
            Console.WriteLine($"[business] {name} left the till without being served");
            OnLost?.Invoke(vm, name);
        }

        private static void Note(LotShop shop, string line)
        {
            shop.Recent.Insert(0, line);
            if (shop.Recent.Count > 8) shop.Recent.RemoveAt(shop.Recent.Count - 1);
        }

        // ---- the book, saved for each lot ----

        private static ShopBook BookFor(short house)
        {
            if (!Books.TryGetValue(house, out var book)) Books[house] = book = new ShopBook();
            return book;
        }

        /// <summary>The first time a lot's house number is known, what was read from its file becomes that house's book.</summary>
        private static void NoteHouse(VM vm, LotShop shop)
        {
            var house = vm.GetGlobalValue(10);
            if (shop.House == house) return;
            shop.House = house;
            if (LoadedBooks.TryGetValue(vm, out var loaded))
            {
                Books[house] = loaded;
                LoadedBooks.Remove(vm);
            }
        }

        public static ShopBook BookOf(VM vm)
        {
            var shop = For(vm);
            NoteHouse(vm, shop);
            return BookFor(shop.House);
        }

        /// <summary>A lot came from its file: this is its book (empty, if the file had none), whatever house number it turns out to have.</summary>
        public static void Loaded(VM vm, byte[] data)
        {
            LoadedBooks.Remove(vm);
            LoadedBooks.Add(vm, ShopBook.Load(data));
        }

        /// <summary>The book to write beside a lot's save, or null if it has no business to speak of.</summary>
        public static byte[] ToSave(VM vm)
        {
            if (!IsHomeLot(vm)) return null;
            var book = BookOf(vm);
            return book.Empty ? null : book.Save();
        }
    }
}
