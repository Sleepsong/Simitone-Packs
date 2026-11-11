/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Files.Formats.IFF.Chunks;
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
        /// <summary>
        /// On a business lot: the number, in the lot's file, of the object that stood here. 0 at home.
        /// A lot being visited is never saved, so the object is still in the file and is taken out again on each visit.
        /// </summary>
        public short SourceID;
    }

    /// <summary>On a business lot: the price of an object in the lot's file, put on it again on each visit.</summary>
    public class PriceTag
    {
        public short ObjectID;
        public uint GUID;
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
        /// <summary>The family that owns this lot as a business (its number in the neighbourhood), or 0. Never set for a home.</summary>
        public ushort Owner;
        /// <summary>What the family paid for the lot, which is what selling it gives back.</summary>
        public int PricePaid;
        /// <summary>On a business lot: what is for sale. At home the prices are in the lot's own save.</summary>
        public List<PriceTag> Prices = new List<PriceTag>();

        public bool Empty => Sold == 0 && Takings == 0 && Lost == 0 && TillID == 0 && Shelves.Count == 0 && Owner == 0 && Prices.Count == 0;

        public byte[] Save()
        {
            using (var mem = new MemoryStream())
            using (var writer = new BinaryWriter(mem))
            {
                writer.Write((byte)3); //version
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
                    writer.Write(shelf.SourceID);
                }
                writer.Write(Owner);
                writer.Write(PricePaid);
                writer.Write(Prices.Count);
                foreach (var tag in Prices)
                {
                    writer.Write(tag.ObjectID);
                    writer.Write(tag.GUID);
                    writer.Write(tag.Price);
                }
                return mem.ToArray();
            }
        }

        public static ShopBook Load(byte[] data)
        {
            var result = new ShopBook();
            if (data == null || data.Length < 13 || data[0] < 1 || data[0] > 3) return result;
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
                        if (version >= 3) shelf.SourceID = reader.ReadInt16();
                        if (shelf.Price > 0) result.Shelves.Add(shelf);
                    }
                    if (version < 3) return result; //stage 2 had no business lots
                    result.Owner = reader.ReadUInt16();
                    result.PricePaid = Math.Max(0, reader.ReadInt32());
                    var tags = Math.Min(4096, reader.ReadInt32());
                    for (int i = 0; i < tags; i++)
                    {
                        var tag = new PriceTag();
                        tag.ObjectID = reader.ReadInt16();
                        tag.GUID = reader.ReadUInt32();
                        tag.Price = reader.ReadInt32();
                        if (tag.Price > 0) result.Prices.Add(tag);
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
        /// <summary>The lot has just come from its file: on a business lot, the book has yet to be put onto it.</summary>
        public bool Fresh;
        /// <summary>On a business lot: objects restocked during this visit, and the object in the lot's file each stands in for.</summary>
        public Dictionary<short, short> StandsInFor = new Dictionary<short, short>();
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

        /// <summary>
        /// The family whose Sim is visiting this lot, or null if the lot is not being visited (it is a home being played,
        /// or a lot being built with nobody on it). The game screen knows; the engine does not.
        /// </summary>
        public static Func<VM, FAMI> VisitingFamily;
        /// <summary>
        /// Writes a visited lot's book to this pack's file for it (null: no file). The game never saves a lot that is
        /// being visited, so a business lot's book is written whenever it changes.
        /// </summary>
        public static Action<VM, byte[]> SaveVisitedBook;

        public const string NotAShop = "A business can only be run from the family's home, or from a lot the family owns.";

        public static LotShop For(VM vm) => Shops.GetValue(vm, x => new LotShop());

        // ---- which lots, Sims and objects ----

        /// <summary>The lot is the home of the family being played.</summary>
        public static bool IsHomeLot(VM vm)
        {
            if (vm == null || !vm.TS1) return false;
            var family = vm.TS1State?.CurrentFamily;
            return family != null && family.HouseNumber > 0 && family.HouseNumber == vm.GetGlobalValue(10);
        }

        /// <summary>One of the family's Sims is visiting this lot, which is not their home.</summary>
        public static bool IsVisiting(VM vm)
        {
            return vm != null && vm.TS1 && !IsHomeLot(vm) && VisitingFamily?.Invoke(vm) != null;
        }

        /// <summary>The lot is being visited by the family that owns it as a business.</summary>
        public static bool IsBusinessLot(VM vm)
        {
            if (!IsVisiting(vm)) return false;
            var owner = BookOf(vm).Owner;
            return owner != 0 && owner == VisitingFamily(vm).ChunkID;
        }

        /// <summary>A business can run where the family being played lives, and on a lot it owns while one of them is there.</summary>
        public static bool IsShop(VM vm)
        {
            return IsHomeLot(vm) || IsBusinessLot(vm);
        }

        /// <summary>The family being played: the one that lives here, or the one whose Sim is visiting.</summary>
        public static FAMI Family(VM vm)
        {
            if (vm == null || !vm.TS1) return null;
            return vm.TS1State?.CurrentFamily ?? VisitingFamily?.Invoke(vm);
        }

        public static bool InFamily(VM vm, VMEntity sim)
        {
            var family = Family(vm);
            return family?.FamilyGUIDs != null && sim is VMAvatar avatar && !avatar.IsPet
                && family.FamilyGUIDs.Contains(avatar.Object.OBJ.GUID);
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
            if (!IsShop(vm)) return NotAShop;
            if (CatalogItem(obj) == null) return "Only objects from the buy catalog can be sold.";
            if (price < 1 || price > MaxPrice) return "The price must be between 1 and " + MaxPrice + ".";
            if (IsTill(vm, obj)) return "The till is not for sale.";
            obj.MultitileGroup.SalePrice = price;
            Tag(vm, obj.MultitileGroup, price);
            return null;
        }

        public static void TakeOffSale(VM vm, VMEntity obj)
        {
            if (obj?.MultitileGroup == null) return;
            obj.MultitileGroup.SalePrice = -1;
            Tag(vm, obj.MultitileGroup, -1);
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
            if (!IsShop(vm)) return null;
            var book = BookOf(vm);
            if (book.TillID == 0) return null;
            var till = vm.GetObjectById(book.TillID);
            if (till is VMGameObject && !till.Dead && till.Position != LotTilePos.OUT_OF_WORLD) return till;
            if (till == null || till.Dead || !(till is VMGameObject)) book.TillID = 0; //sold in buy mode, or otherwise gone
            return null; //(picked up: still the till once it is put down)
        }

        public static bool IsTill(VM vm, VMEntity obj)
        {
            if (obj?.MultitileGroup == null || !IsShop(vm)) return false;
            var id = BookOf(vm).TillID;
            return id != 0 && obj.MultitileGroup.Objects.Any(x => x.ObjectID == id);
        }

        /// <summary>Makes an object the lot's till, in place of any other. Returns null, or why not.</summary>
        public static string SetTill(VM vm, VMEntity obj)
        {
            if (!IsShop(vm)) return NotAShop;
            if (!(obj is VMGameObject) || obj.MultitileGroup?.BaseObject == null) return "That cannot be a till.";
            TakeOffSale(vm, obj);
            BookOf(vm).TillID = obj.MultitileGroup.BaseObject.ObjectID;
            Changed(vm);
            return null;
        }

        public static void RemoveTill(VM vm)
        {
            if (!IsShop(vm)) return;
            BookOf(vm).TillID = 0;
            Changed(vm);
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
            return IsShop(vm) ? BookOf(vm).Shelves.Sum(x => (long)RestockCost(x)) : 0;
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
            if (!IsShop(vm)) return result;
            var family = Family(vm);
            var shop = For(vm);
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
                if (!Place(vm, shop, book, shelf, cost))
                {
                    result.Blocked++;
                    continue;
                }
                family.Budget -= cost;
                book.Shelves.Remove(shelf);
                result.Restocked++;
                result.Spent += cost;
            }
            Changed(vm);
            return result;
        }

        /// <summary>Puts a new object on a shelf, the way buying one from the catalog does (VMNetBuyObjectCmd).</summary>
        private static bool Place(VM vm, LotShop shop, ShopBook book, Shelf shelf, int cost)
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
                if (shelf.SourceID != 0)
                {
                    //on a business lot the new object lasts only for this visit: next time, the one in the file is back in its place
                    shop.StandsInFor[placed.ObjectID] = shelf.SourceID;
                    book.Prices.RemoveAll(x => x.ObjectID == shelf.SourceID);
                    book.Prices.Add(new PriceTag() { ObjectID = shelf.SourceID, GUID = shelf.GUID, Price = shelf.Price });
                }
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

        /// <summary>
        /// Forgets the empty shelves. Not on a business lot: there the sold objects are still in the lot's file, and
        /// forgetting them would bring them back for nothing on the next visit.
        /// </summary>
        public static bool ForgetShelves(VM vm)
        {
            if (!IsHomeLot(vm)) return false;
            BookOf(vm).Shelves.Clear();
            return true;
        }

        // ---- a business lot ----

        /// <summary>The object in the lot's file that this one is, or stands in for.</summary>
        private static short SourceOf(LotShop shop, VMMultitileGroup item)
        {
            var id = item.BaseObject.ObjectID;
            return shop.StandsInFor.TryGetValue(id, out var source) ? source : id;
        }

        /// <summary>On a business lot, notes an object's price in the book (or takes it out, below 1). At home the lot's save holds it.</summary>
        private static void Tag(VM vm, VMMultitileGroup item, int price)
        {
            if (!IsBusinessLot(vm) || item?.BaseObject == null) return;
            var book = BookOf(vm);
            var source = SourceOf(For(vm), item);
            book.Prices.RemoveAll(x => x.ObjectID == source);
            if (price > 0) book.Prices.Add(new PriceTag() { ObjectID = source, GUID = GuidOf(item.BaseObject), Price = price });
            Changed(vm);
        }

        /// <summary>A business lot's book has changed: write it now, since the lot itself will not be saved.</summary>
        private static void Changed(VM vm)
        {
            if (!IsVisiting(vm)) return;
            var book = BookOf(vm);
            SaveVisitedBook?.Invoke(vm, book.Empty ? null : book.Save());
        }

        /// <summary>
        /// A business lot has just come from its file, as it was built. Put the book onto it: take out what has sold
        /// and not been restocked, and price what is for sale. An entry whose object is no longer in the file (the lot
        /// has been rebuilt since) is dropped.
        /// </summary>
        private static void PutBookOnLot(VM vm, LotShop shop)
        {
            var book = BookOf(vm);
            var dropped = 0;
            VMEntity Find(short id, uint guid)
            {
                var obj = vm.GetObjectById(id);
                if (!(obj is VMGameObject) || obj.Dead || obj.MultitileGroup?.BaseObject == null) return null;
                return (GuidOf(obj.MultitileGroup.BaseObject) == guid) ? obj : null;
            }
            foreach (var shelf in book.Shelves.ToList())
            {
                var obj = (shelf.SourceID == 0) ? null : Find(shelf.SourceID, shelf.GUID);
                if (obj == null)
                {
                    book.Shelves.Remove(shelf);
                    dropped++;
                    continue;
                }
                shelf.Position = obj.MultitileGroup.BaseObject.Position;
                shelf.Direction = obj.MultitileGroup.BaseObject.Direction;
                obj.MultitileGroup.Delete(vm.Context);
            }
            foreach (var tag in book.Prices.ToList())
            {
                var obj = Find(tag.ObjectID, tag.GUID);
                if (obj == null || CatalogItem(obj) == null)
                {
                    book.Prices.Remove(tag);
                    dropped++;
                    continue;
                }
                obj.MultitileGroup.SalePrice = tag.Price;
            }
            Console.WriteLine($"[business] business lot {shop.House}: {book.Prices.Count} for sale, {book.Shelves.Count} sold out" + ((dropped > 0) ? $", {dropped} no longer there" : ""));
            if (dropped > 0) Changed(vm);
        }

        /// <summary>What a lot costs to buy as a business: its value as the neighbourhood has it, and never less than this.</summary>
        public const int LeastLotPrice = 5000;

        public static int LotPrice(VM vm)
        {
            var info = vm?.TS1State?.SimulationInfo;
            if (info == null) return LeastLotPrice;
            long built = (long)info.LotValue + info.ObjectsValue + info.ArchitectureValue;
            return (int)Math.Min(MaxPrice * 10L, Math.Max(LeastLotPrice, Math.Max(info.PurchaseValue, built)));
        }

        /// <summary>The visiting family buys the lot as a business. Returns null, or why not.</summary>
        public static string BuyLot(VM vm)
        {
            if (IsHomeLot(vm)) return "This is the family's home. It can already run a business.";
            if (!IsVisiting(vm)) return "Visit a lot with one of the family to buy it as a business.";
            var family = Family(vm);
            var book = BookOf(vm);
            if (book.Owner == family.ChunkID) return "The family already owns this lot.";
            if (book.Owner != 0) return "This lot belongs to another family's business.";
            var price = LotPrice(vm);
            if (family.Budget < price) return $"This lot costs §{price:##,#0}. The family has §{family.Budget:##,#0}.";
            family.Budget -= price;
            book.Owner = family.ChunkID;
            book.PricePaid = price;
            Changed(vm);
            Console.WriteLine($"[business] family {family.ChunkID} bought lot {For(vm).House} for {price}");
            return null;
        }

        /// <summary>
        /// The family sells its business lot for what it paid. The lot goes back to how it was built: next visit,
        /// everything that sold is back and nothing has a price. The totals are kept.
        /// </summary>
        public static string SellLot(VM vm, out int refund)
        {
            refund = 0;
            if (!IsBusinessLot(vm)) return "The family does not own this lot.";
            var family = Family(vm);
            var book = BookOf(vm);
            var shop = For(vm);
            foreach (var item in ForSale(vm)) item.SalePrice = -1;
            shop.Shoppers.Clear();
            shop.StandsInFor.Clear();
            refund = book.PricePaid;
            family.Budget += refund;
            book.Owner = 0;
            book.PricePaid = 0;
            book.TillID = 0;
            book.Prices.Clear();
            book.Shelves.Clear();
            SaveVisitedBook?.Invoke(vm, book.Empty ? null : book.Save()); //(not Changed: the lot is still being visited, but is no longer a shop)
            Console.WriteLine($"[business] family {family.ChunkID} sold lot {shop.House} for {refund}");
            return null;
        }

        // ---- the lot's tick ----

        public static void Tick(VM vm)
        {
            var shop = For(vm);
            if (shop.Fresh)
            {
                shop.Fresh = false;
                if (IsBusinessLot(vm)) PutBookOnLot(vm, shop);
            }
            if (++shop.Ticks < TicksPerCheck) return;
            shop.Ticks = 0;
            if (!IsShop(vm)) return;
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
            var family = Family(vm);
            var business = IsBusinessLot(vm);
            var price = item.SalePrice;
            var name = NameOf(item.BaseObject);
            var buyerName = buyer.Name;

            var book = BookFor(shop.House);
            var shelf = new Shelf()
            {
                GUID = GuidOf(item.BaseObject),
                Position = item.BaseObject.Position,
                Direction = item.BaseObject.Direction,
                Price = price,
                SourceID = business ? SourceOf(shop, item) : (short)0
            };
            if (business)
            {
                book.Prices.RemoveAll(x => x.ObjectID == shelf.SourceID);
                shop.StandsInFor.Remove(item.BaseObject.ObjectID);
            }

            item.SalePrice = -1;
            item.Delete(vm.Context);
            family.Budget += price;

            if (shelf.Position != LotTilePos.OUT_OF_WORLD && book.Shelves.Count < ShopBook.MaxShelves) book.Shelves.Add(shelf);
            book.Sold++;
            book.Takings += price;
            Note(shop, $"{name} to {buyerName} for §{price:##,#0}");
            Changed(vm);
            Console.WriteLine($"[business] sold {name} to {buyerName} for {price}");
            OnSale?.Invoke(vm, name, buyerName, price);
        }

        private static void Lose(VM vm, LotShop shop, VMMultitileGroup item, VMAvatar customer)
        {
            var name = customer.Name;
            BookFor(shop.House).Lost++;
            Changed(vm);
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
            For(vm).Fresh = true;
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
