/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OpenForBusiness
{
    /// <summary>A visitor who has decided to buy something and is on their way to it, or to the till.</summary>
    public class Shopper
    {
        public short AvatarID;
        public VMMultitileGroup Item;
        /// <summary>How many checks (about a second each) this shopper has been on their way, or waiting.</summary>
        public int Waited;
        /// <summary>With a till: how many checks someone has been ringing this shopper up.</summary>
        public int Served;
        /// <summary>What they agreed to pay, if a discount was offered; 0 for the price on it.</summary>
        public int Price;
    }

    /// <summary>What a customer has in mind, and how near a sales pitch has brought them to buying it.</summary>
    public class BuyMeter
    {
        public VMMultitileGroup Item;
        public int Meter;
        /// <summary>Assess Desire's push has been given.</summary>
        public bool DesireUsed;
        /// <summary>They accepted a discount: they pay 10% less.</summary>
        public bool Discount;
    }

    /// <summary>A Sim on their way to a customer to make a sales pitch.</summary>
    public class PendingPitch
    {
        public short Customer;
        public Pitch Pitch;
        public int Waited;
        public bool ByEmployee;
    }

    /// <summary>A Sim putting sold objects back, one shelf at a time.</summary>
    public class RestockTask
    {
        public Shelf Shelf;
        public int Progress;
        public int Waited;
        public bool ByEmployee;
        /// <summary>Shelves this Sim could not fill (something in the way), not tried again by them.</summary>
        public HashSet<Shelf> Skipped = new HashSet<Shelf>();
    }

    /// <summary>The business on one loaded lot: who is shopping, selling and restocking, and what happened lately.</summary>
    public class LotShop
    {
        public Dictionary<short, Shopper> Shoppers = new Dictionary<short, Shopper>();
        /// <summary>Visitors who looked and did not buy, and the check at which they may look again.</summary>
        public Dictionary<short, int> LookAgainAt = new Dictionary<short, int>();
        public Dictionary<short, BuyMeter> Meters = new Dictionary<short, BuyMeter>();
        /// <summary>Pitches on their way, by the seller.</summary>
        public Dictionary<short, PendingPitch> Pitches = new Dictionary<short, PendingPitch>();
        /// <summary>Customers a salesperson has pitched to, and the check they did so: they are given a moment before the next.</summary>
        public Dictionary<short, int> PitchedAt = new Dictionary<short, int>();
        public Dictionary<short, RestockTask> Restockers = new Dictionary<short, RestockTask>();
        /// <summary>When each Sim was last perked up (game hour).</summary>
        public Dictionary<uint, long> PerkUpAt = new Dictionary<uint, long>();
        public long RallyAt = long.MinValue;
        public List<string> Recent = new List<string>();
        public int Ticks;
        public int Checks;
        /// <summary>The house this lot was last seen to be, once known (0 before).</summary>
        public short House;
        /// <summary>The lot has just come from its file: on a business lot, the book has yet to be put onto it.</summary>
        public bool Fresh;
        /// <summary>On a business lot: objects restocked during this visit, and the object in the lot's file each stands in for.</summary>
        public Dictionary<short, short> StandsInFor = new Dictionary<short, short>();
        /// <summary>The game hour at the last check, for paying wages as each hour passes (-1 before).</summary>
        public long LastHour = -1;
    }

    /// <summary>
    /// The business itself. Nothing here patches the engine: OpenForBusinessPack.cs calls in from its patches.
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
        /// <summary>A Sim this close (in tiles) to what they were sent to has reached it.</summary>
        public const int ReachTiles = 2;
        /// <summary>A restocker who has not reached a shelf after this many checks restocks it from where they are.</summary>
        public const int RestockAnywayAfter = 20;
        public const int MaxPrice = 999999;
        /// <summary>A cashier who has wandered from the till is sent back this often, in checks.</summary>
        public const int SendBackEvery = 30;
        /// <summary>A salesperson leaves a customer this many checks between pitches.</summary>
        public const int PitchAgainAfter = 15;
        /// <summary>Motivational Speech lasts this many hours; Rally Forth! can be used this often.</summary>
        public const int SpeechHours = 3, RallyEveryHours = 3;

        /// <summary>Price presets, as a percentage of the catalog's price. "Ridiculously Cheap", "Average" and "Expensive" are Open for Business's names; the percentages are this pack's.</summary>
        public static readonly (string Name, int Percent)[] PricePresets =
        {
            ("Ridiculously Cheap", 50), ("Cheap", 75), ("Average", 100), ("Expensive", 125), ("Very Expensive", 150)
        };

        private const uint GOTO_GUID = 0x000007C4; //the engine's "go here" marker (VMNetGotoCmd)

        private static readonly Random Dice = new Random();
        private static readonly ConditionalWeakTable<VM, LotShop> Shops = new ConditionalWeakTable<VM, LotShop>();

        /// <summary>Books by house number, for the houses seen this run.</summary>
        private static readonly Dictionary<short, ShopBook> Books = new Dictionary<short, ShopBook>();
        /// <summary>Lots loaded from a file, with the book read for them, until their house number is known.</summary>
        private static readonly ConditionalWeakTable<VM, ShopBook> LoadedBooks = new ConditionalWeakTable<VM, ShopBook>();

        /// <summary>Every Sim's badges in the neighbourhood being played. The pack loads and saves it.</summary>
        public static BadgeBook Badges = new BadgeBook();
        /// <summary>Writes Badges to its file.</summary>
        public static Action SaveBadges;

        /// <summary>Called when something sells, on the game thread: the object's name, the buyer's and the price.</summary>
        public static Action<VM, string, string, int> OnSale;
        /// <summary>Called when a customer leaves the till without being served.</summary>
        public static Action<VM, string> OnLost;
        /// <summary>Called when the business's rank changes: the new rank, and whether it is the highest yet (which brings a perk point).</summary>
        public static Action<VM, int, bool> OnRankChange;
        /// <summary>Called when a Sim earns a badge: their name, the badge, and whether they are one of the family.</summary>
        public static Action<VM, string, Talent, BadgeLevel, bool> OnBadge;
        /// <summary>Called when one of the family has made a pitch: whether it worked, the meter after it, and whether the customer is now buying.</summary>
        public static Action<VM, Pitch, bool, int, bool> OnPitch;
        /// <summary>Called with news of the staff, such as an employee quitting.</summary>
        public static Action<VM, string> OnStaff;

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

        /// <summary>A visiting neighbour: not one of the family, not a pet, and not a maid, burglar or other NPC. Customers and employees both are.</summary>
        public static bool IsCustomer(VM vm, VMAvatar avatar)
        {
            return avatar != null && !avatar.Dead && !avatar.IsPet
                && avatar.Position != LotTilePos.OUT_OF_WORLD
                && avatar.GetPersonData(VMPersonDataVariable.PersonType) == 1
                && !InFamily(vm, avatar);
        }

        /// <summary>A visiting neighbour who is not working here.</summary>
        private static bool IsShopper(VM vm, ShopBook book, VMAvatar avatar)
        {
            return IsCustomer(vm, avatar) && book.EmployeeOf(SimNumber(avatar)) == null;
        }

        public static uint SimNumber(VMAvatar avatar) => avatar.Object.OBJ.GUID;

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

        /// <summary>The price as a percentage of the catalog's (100 if it has none).</summary>
        public static int PricePercent(VMMultitileGroup item, int price)
        {
            var catalog = (int?)CatalogItem(item.BaseObject)?.Price ?? 0;
            return (catalog <= 0) ? 100 : (int)Math.Min(10000, (long)price * 100 / catalog);
        }

        /// <summary>The game's clock in hours, for things that last or happen by the hour.</summary>
        public static long GameHour(VM vm)
        {
            var clock = vm.Context.Clock;
            return (((long)clock.Year * 12 + clock.Month) * 31 + clock.DayOfMonth) * 24 + clock.Hours;
        }

        public static int Mood(VMAvatar avatar) => avatar.GetMotiveData(VMMotive.Mood);

        private static void AddMotive(VMAvatar avatar, VMMotive motive, int change)
        {
            avatar.SetMotiveData(motive, (short)Math.Max(-100, Math.Min(100, avatar.GetMotiveData(motive) + change)));
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

        /// <summary>Puts an object up for sale at a percentage of its catalog price.</summary>
        public static string SetPricePercent(VM vm, VMEntity obj, int percent)
        {
            var catalog = CatalogItem(obj);
            if (catalog == null) return "Only objects from the buy catalog can be sold.";
            return SetPrice(vm, obj, (int)Math.Max(1, Math.Min(MaxPrice, (long)catalog.Value.Price * percent / 100)));
        }

        public static void TakeOffSale(VM vm, VMEntity obj)
        {
            if (obj?.MultitileGroup == null) return;
            obj.MultitileGroup.SalePrice = -1;
            Tag(vm, obj.MultitileGroup, -1);
            var shop = For(vm);
            foreach (var gone in shop.Shoppers.Values.Where(x => x.Item == obj.MultitileGroup).ToList()) shop.Shoppers.Remove(gone.AvatarID);
        }

        // ---- open and closed ----

        public static bool IsOpen(VM vm) => IsShop(vm) && !BookOf(vm).Closed;

        /// <summary>Opens or closes the business. Closed, nobody new shops and employees are not paid; customers already buying are still served.</summary>
        public static void SetOpen(VM vm, bool open)
        {
            if (!IsShop(vm)) return;
            var book = BookOf(vm);
            if (book.Closed != !open)
            {
                book.Closed = !open;
                Note(For(vm), open ? "Opened for business" : "Closed for business");
            }
            Changed(vm);
        }

        // ---- badges ----

        public static BadgeLevel BadgeOf(VM vm, VMAvatar sim, Talent talent) => Badges.Level(SimNumber(sim), talent, GameHour(vm));

        private static void Practice(VM vm, LotShop shop, VMAvatar sim, Talent talent)
        {
            if (sim == null) return;
            var earned = Badges.Practice(SimNumber(sim), talent);
            if (earned == null) return;
            Note(shop, $"{sim.Name} earned a {earned} {talent} badge");
            Console.WriteLine($"[business] {sim.Name}: {earned} {talent}");
            OnBadge?.Invoke(vm, sim.Name, talent, earned.Value, InFamily(vm, sim));
        }

        // ---- employees ----

        public static Employee EmployeeOf(VM vm, VMEntity sim)
        {
            return (sim is VMAvatar avatar && IsShop(vm)) ? BookOf(vm).EmployeeOf(SimNumber(avatar)) : null;
        }

        public static bool IsEmployee(VM vm, VMEntity sim) => EmployeeOf(vm, sim) != null;

        /// <summary>What a Sim asks to be paid an hour: more for every badge level they hold.</summary>
        public static int AskingWage(VM vm, VMAvatar sim)
        {
            return Reputation.AskingWage(Badges.TotalLevels(SimNumber(sim)), BookOf(vm).Has(PerkTree.Motivation, 1));
        }

        public static string JobName(Job job) => (job == Job.Salesperson) ? "Salesperson" : job.ToString();

        /// <summary>Takes a visiting Sim on, at the pay they ask. Returns null, or why not.</summary>
        public static string Hire(VM vm, VMAvatar sim, Job job)
        {
            if (!IsShop(vm)) return NotAShop;
            if (!IsCustomer(vm, sim)) return "Only a visiting neighbour can be hired.";
            var book = BookOf(vm);
            if (book.EmployeeOf(SimNumber(sim)) != null) return sim.Name + " already works here.";
            if (book.Employees.Count >= Reputation.MaxEmployees) return "A business can have " + Reputation.MaxEmployees + " employees.";
            book.Employees.Add(new Employee() { Sim = SimNumber(sim), Job = job, Wage = AskingWage(vm, sim) });
            var shop = For(vm);
            shop.Shoppers.Remove(sim.ObjectID);
            shop.Meters.Remove(sim.ObjectID);
            Note(shop, $"Hired {sim.Name} as {JobName(job).ToLowerInvariant()}");
            Changed(vm);
            StartJob(vm, sim, job);
            return null;
        }

        public static void SetJob(VM vm, VMAvatar sim, Job job)
        {
            var employee = EmployeeOf(vm, sim);
            if (employee == null || employee.Job == job) return;
            employee.Job = job;
            var shop = For(vm);
            shop.Pitches.Remove(sim.ObjectID);
            shop.Restockers.Remove(sim.ObjectID);
            Changed(vm);
            StartJob(vm, sim, job);
        }

        private static void StartJob(VM vm, VMAvatar sim, Job job)
        {
            if (job != Job.Cashier) return; //the others start on the next check
            var till = Till(vm);
            if (till != null) WalkTo(vm, sim, till.MultitileGroup);
        }

        /// <summary>A raise (or, below 0, a cut). Returns the new wage.</summary>
        public static int ChangeWage(VM vm, VMAvatar sim, int change)
        {
            var employee = EmployeeOf(vm, sim);
            if (employee == null) return 0;
            employee.Wage = Math.Max(0, Math.Min(Reputation.MaxWage, employee.Wage + change));
            if (employee.Wage >= AskingWage(vm, sim)) employee.Underpaid = 0;
            Changed(vm);
            return employee.Wage;
        }

        public static void Dismiss(VM vm, VMAvatar sim)
        {
            if (sim == null || !IsShop(vm)) return;
            if (BookOf(vm).Employees.RemoveAll(x => x.Sim == SimNumber(sim)) == 0) return;
            var shop = For(vm);
            shop.Pitches.Remove(sim.ObjectID);
            shop.Restockers.Remove(sim.ObjectID);
            Note(shop, $"Let {sim.Name} go");
            Changed(vm);
        }

        private static List<(VMAvatar Sim, Employee Record)> StaffOnLot(VM vm, ShopBook book)
        {
            var result = new List<(VMAvatar, Employee)>();
            if (book.Employees.Count == 0) return result;
            foreach (var avatar in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
            {
                if (!IsCustomer(vm, avatar)) continue;
                var record = book.EmployeeOf(SimNumber(avatar));
                if (record != null) result.Add((avatar, record));
            }
            return result;
        }

        /// <summary>
        /// Each hour of the game's clock while the business is open, the employees on the lot are paid. One the family
        /// cannot pay leaves; one paid less than they ask for too many hours in a row quits, as in Open for Business.
        /// </summary>
        private static void PayWages(VM vm, LotShop shop, ShopBook book, List<(VMAvatar Sim, Employee Record)> staff)
        {
            var family = Family(vm);
            var quitAfter = Reputation.HoursToQuit(book.Has(PerkTree.Motivation, 4));
            foreach (var (sim, record) in staff)
            {
                if (family.Budget < record.Wage)
                {
                    book.Employees.Remove(record);
                    Quit(vm, shop, sim, $"{sim.Name} left: the family could not pay the wage");
                    continue;
                }
                family.Budget -= record.Wage;
                book.Wages += record.Wage;
                if (record.Wage >= AskingWage(vm, sim)) record.Underpaid = 0;
                else if (++record.Underpaid >= quitAfter)
                {
                    book.Employees.Remove(record);
                    Quit(vm, shop, sim, $"{sim.Name} quit: paid {record.Wage} an hour and asking {AskingWage(vm, sim)}");
                }
            }
            Changed(vm);
        }

        private static void Quit(VM vm, LotShop shop, VMAvatar sim, string why)
        {
            shop.Pitches.Remove(sim.ObjectID);
            shop.Restockers.Remove(sim.ObjectID);
            Note(shop, why);
            Console.WriteLine("[business] " + why);
            OnStaff?.Invoke(vm, why);
        }

        // ---- rank and perks ----

        private static void AddStars(VM vm, LotShop shop, ShopBook book, VMAvatar customer, int change)
        {
            var highest = book.HighestRank;
            var moved = book.AddStars(SimNumber(customer), change);
            RankMoved(vm, shop, book, moved, highest);
        }

        private static void Meet(VM vm, LotShop shop, ShopBook book, VMAvatar customer)
        {
            var highest = book.HighestRank;
            var before = book.Rank;
            if (book.Meet(SimNumber(customer))) RankMoved(vm, shop, book, book.Rank - before, highest);
        }

        private static void RankMoved(VM vm, LotShop shop, ShopBook book, int moved, sbyte highestBefore)
        {
            if (moved == 0) return;
            var rank = book.Rank;
            var newHigh = book.HighestRank > highestBefore;
            Note(shop, $"The business is now rank {rank}, {Reputation.RankName(rank)}");
            Console.WriteLine($"[business] rank {rank}");
            OnRankChange?.Invoke(vm, rank, newHigh);
        }

        /// <summary>Takes the next perk of a tree. Returns null, or why not. A Cash perk pays out at once.</summary>
        public static string TakePerk(VM vm, PerkTree tree, out PerkInfo perk)
        {
            perk = null;
            if (!IsShop(vm)) return NotAShop;
            var book = BookOf(vm);
            perk = book.TakePerk(tree, out var problem);
            if (perk == null) return problem;
            if (tree == PerkTree.Cash) Family(vm).Budget += Reputation.CashPrizes[perk.Level - 1];
            Note(For(vm), "Took the perk " + perk.Name);
            Changed(vm);
            return null;
        }

        /// <summary>Perk Up: +30 Fun and Social, once an hour for each Sim. An employee forgets being underpaid.</summary>
        public static string PerkUp(VM vm, VMAvatar actor, VMAvatar target)
        {
            if (!IsShop(vm) || !BookOf(vm).Has(PerkTree.Motivation, 2)) return "That needs the Perk Up perk.";
            if (target == null || target.Dead) return null;
            var shop = For(vm);
            var hour = GameHour(vm);
            if (shop.PerkUpAt.TryGetValue(SimNumber(target), out var last) && hour - last < 1) return target.Name + " has been perked up this hour already.";
            shop.PerkUpAt[SimNumber(target)] = hour;
            AddMotive(target, VMMotive.Fun, 30);
            AddMotive(target, VMMotive.Social, 30);
            var employee = EmployeeOf(vm, target);
            if (employee != null) employee.Underpaid = 0;
            Note(shop, $"{actor?.Name ?? "The owner"} perked up {target.Name}");
            Changed(vm);
            return null;
        }

        /// <summary>Motivational Speech: an employee's or one of the family's badges are a level higher for 3 hours. It tires the speaker.</summary>
        public static string Speech(VM vm, VMAvatar actor, VMAvatar target)
        {
            if (!IsShop(vm) || !BookOf(vm).Has(PerkTree.Motivation, 3)) return "That needs the Motivational Speech perk.";
            if (target == null || target.Dead) return null;
            if (!InFamily(vm, target) && !IsEmployee(vm, target)) return "A speech is for employees and the family.";
            if (Badges.TotalLevels(SimNumber(target)) == 0) return target.Name + " has no badges for a speech to raise.";
            Badges.Boost(SimNumber(target), GameHour(vm) + SpeechHours);
            if (actor != null) AddMotive(actor, VMMotive.Energy, -20);
            Note(For(vm), $"{actor?.Name ?? "The owner"} gave {target.Name} a motivational speech");
            return null;
        }

        /// <summary>Rally Forth!: the family and employees on the lot are refreshed, at the rallying Sim's cost.</summary>
        public static string Rally(VM vm, VMAvatar actor)
        {
            if (!IsShop(vm) || !BookOf(vm).Has(PerkTree.Motivation, 5)) return "That needs the Rally Forth! perk.";
            if (actor == null) return null;
            var shop = For(vm);
            var hour = GameHour(vm);
            if (hour - shop.RallyAt < RallyEveryHours) return $"Rally Forth! can be used once every {RallyEveryHours} hours.";
            shop.RallyAt = hour;
            var rallied = 0;
            foreach (var sim in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().ToList())
            {
                if (sim == actor || sim.Dead || sim.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (!InFamily(vm, sim) && !IsEmployee(vm, sim)) continue;
                foreach (var motive in new[] { VMMotive.Energy, VMMotive.Comfort, VMMotive.Fun, VMMotive.Social }) AddMotive(sim, motive, 40);
                rallied++;
            }
            AddMotive(actor, VMMotive.Energy, -50);
            AddMotive(actor, VMMotive.Comfort, -50);
            Note(shop, $"{actor.Name} rallied {rallied}");
            return null;
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

        /// <summary>The Sims at the till who can ring customers up: the family, and employees whose job it is.</summary>
        public static List<VMAvatar> Tenders(VM vm, VMEntity till)
        {
            if (till == null) return new List<VMAvatar>();
            return vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().Where(x => !x.Dead && x.Position != LotTilePos.OUT_OF_WORLD
                && (InFamily(vm, x) || EmployeeOf(vm, x)?.Job == Job.Cashier) && Reached(x, till.MultitileGroup)).ToList();
        }

        public static bool Tended(VM vm, VMEntity till) => Tenders(vm, till).Count > 0;

        /// <summary>Sends a Sim to stand by the till.</summary>
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

        /// <summary>The same for this business, with its Wholesale perks.</summary>
        public static int RestockCost(ShopBook book, Shelf shelf) => Reputation.Wholesale(RestockCost(shelf), book.PerkLevel(PerkTree.Wholesale));

        public static long RestockCost(VM vm)
        {
            if (!IsShop(vm)) return 0;
            var book = BookOf(vm);
            return book.Shelves.Sum(x => (long)RestockCost(book, x));
        }

        public struct RestockResult
        {
            public int Restocked, CannotAfford, Blocked;
            public long Spent;
        }

        /// <summary>
        /// Puts every sold object back at once, as stage 2 did: no Sim does the work, and nobody's Restocking badge
        /// improves. Used by the console. In the game, a Sim does it one shelf at a time (StartRestock).
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
                switch (Fill(vm, shop, book, family, shelf, out var cost))
                {
                    case FillResult.Gone: break;
                    case FillResult.CannotAfford: result.CannotAfford++; break;
                    case FillResult.Blocked: result.Blocked++; break;
                    default: result.Restocked++; result.Spent += cost; break;
                }
            }
            Changed(vm);
            return result;
        }

        private enum FillResult { Filled, Gone, CannotAfford, Blocked }

        private static FillResult Fill(VM vm, LotShop shop, ShopBook book, FAMI family, Shelf shelf, out int cost)
        {
            cost = RestockCost(book, shelf);
            if (RestockCost(shelf) <= 0)
            {
                book.Shelves.Remove(shelf);
                return FillResult.Gone;
            }
            if (family.Budget < cost) return FillResult.CannotAfford;
            if (!Place(vm, shop, book, shelf, cost)) return FillResult.Blocked;
            family.Budget -= cost;
            book.Shelves.Remove(shelf);
            return FillResult.Filled;
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

        /// <summary>Sets a Sim to restocking: they go from empty shelf to empty shelf, as fast as their Restocking badge allows. Returns null, or why not.</summary>
        public static string StartRestock(VM vm, VMAvatar sim) => StartRestock(vm, sim, false);

        private static string StartRestock(VM vm, VMAvatar sim, bool byEmployee)
        {
            if (!IsShop(vm)) return NotAShop;
            if (sim == null) return null;
            if (BookOf(vm).Shelves.Count == 0) return "Nothing has sold out.";
            var shop = For(vm);
            if (!shop.Restockers.ContainsKey(sim.ObjectID)) shop.Restockers[sim.ObjectID] = new RestockTask() { ByEmployee = byEmployee };
            return null;
        }

        /// <summary>
        /// Forgets the empty shelves. Not on a business lot: there the sold objects are still in the lot's file, and
        /// forgetting them would bring them back for nothing on the next visit.
        /// </summary>
        public static bool ForgetShelves(VM vm)
        {
            if (!IsHomeLot(vm)) return false;
            BookOf(vm).Shelves.Clear();
            For(vm).Restockers.Clear();
            return true;
        }

        private static void RestockWork(VM vm, LotShop shop, ShopBook book)
        {
            if (shop.Restockers.Count == 0) return;
            var family = Family(vm);
            var claimed = new HashSet<Shelf>(shop.Restockers.Values.Where(x => x.Shelf != null).Select(x => x.Shelf));
            foreach (var pair in shop.Restockers.ToList())
            {
                var sim = vm.GetObjectById(pair.Key) as VMAvatar;
                var task = pair.Value;
                if (sim == null || sim.Dead || sim.Position == LotTilePos.OUT_OF_WORLD || (task.ByEmployee && EmployeeOf(vm, sim)?.Job != Job.Restocker))
                {
                    shop.Restockers.Remove(pair.Key);
                    continue;
                }
                if (task.Shelf != null && !book.Shelves.Contains(task.Shelf)) task.Shelf = null; //filled another way
                if (task.Shelf == null)
                {
                    task.Shelf = book.Shelves.FirstOrDefault(x => !claimed.Contains(x) && !task.Skipped.Contains(x));
                    if (task.Shelf == null)
                    {
                        shop.Restockers.Remove(pair.Key);
                        if (!task.ByEmployee) Note(shop, $"{sim.Name} has finished restocking");
                        continue;
                    }
                    if (family.Budget < RestockCost(book, task.Shelf))
                    {
                        shop.Restockers.Remove(pair.Key);
                        Note(shop, $"{sim.Name} stopped restocking: the family cannot afford it");
                        continue;
                    }
                    claimed.Add(task.Shelf);
                    task.Progress = 0;
                    task.Waited = 0;
                    WalkToSpot(vm, sim, task.Shelf.Position);
                }
                if (!Near(sim, task.Shelf.Position) && ++task.Waited < RestockAnywayAfter) continue;
                if (++task.Progress < BadgeBook.RestockChecks(BadgeOf(vm, sim, Talent.Restocking))) continue;

                var shelf = task.Shelf;
                task.Shelf = null;
                switch (Fill(vm, shop, book, family, shelf, out var cost))
                {
                    case FillResult.Filled:
                        Practice(vm, shop, sim, Talent.Restocking);
                        Changed(vm);
                        break;
                    case FillResult.CannotAfford:
                        shop.Restockers.Remove(pair.Key);
                        Note(shop, $"{sim.Name} stopped restocking: the family cannot afford it");
                        break;
                    case FillResult.Blocked:
                        task.Skipped.Add(shelf);
                        break;
                }
            }
        }

        // ---- sales pitches ----

        /// <summary>Whether a Sim may make a pitch: their Sales badge, or the Manipulation perk.</summary>
        public static bool CanPitch(VM vm, VMAvatar seller, Pitch pitch)
        {
            if (seller == null || !IsShop(vm)) return false;
            if (pitch == Pitch.Manipulate) return BookOf(vm).Has(PerkTree.Perception, 5);
            return BadgeOf(vm, seller, Talent.Sales) >= Sales.BadgeNeeded(pitch);
        }

        public static BuyMeter MeterOf(VM vm, VMAvatar customer)
        {
            return (customer != null && For(vm).Meters.TryGetValue(customer.ObjectID, out var meter)) ? meter : null;
        }

        public static bool IsBuying(VM vm, VMAvatar customer) => customer != null && For(vm).Shoppers.ContainsKey(customer.ObjectID);

        /// <summary>Sends a Sim to make a pitch to a customer. Returns null, or why not.</summary>
        public static string StartPitch(VM vm, VMAvatar seller, VMAvatar customer, Pitch pitch) => StartPitch(vm, seller, customer, pitch, false);

        private static string StartPitch(VM vm, VMAvatar seller, VMAvatar customer, Pitch pitch, bool byEmployee)
        {
            if (!IsShop(vm)) return NotAShop;
            var book = BookOf(vm);
            if (seller == null || !IsShopper(vm, book, customer)) return "Pitches are for customers.";
            if (!CanPitch(vm, seller, pitch)) return $"{Sales.Name(pitch)} needs a {Sales.BadgeNeeded(pitch)} Sales badge.";
            var shop = For(vm);
            if (shop.Shoppers.ContainsKey(customer.ObjectID)) return customer.Name + " is already buying something.";
            if (ForSale(vm).Count == 0) return "Nothing is for sale.";
            shop.Pitches[seller.ObjectID] = new PendingPitch() { Customer = customer.ObjectID, Pitch = pitch, ByEmployee = byEmployee };
            WalkTo(vm, seller, customer.MultitileGroup);
            return null;
        }

        /// <summary>Look for Mark: sends a Sim to pitch to the customer most likely to buy. Returns null, or why not.</summary>
        public static string LookForMark(VM vm, VMAvatar seller)
        {
            if (!IsShop(vm) || !BookOf(vm).Has(PerkTree.Perception, 3)) return "That needs the Look for Mark perk.";
            var book = BookOf(vm);
            var shop = For(vm);
            VMAvatar best = null;
            var bestScore = int.MinValue;
            foreach (var customer in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
            {
                if (!IsShopper(vm, book, customer) || shop.Shoppers.ContainsKey(customer.ObjectID)) continue;
                var score = book.StarsOf(SimNumber(customer)) * 10 + Mood(customer) / 5 + (MeterOf(vm, customer)?.Meter ?? 0);
                if (score > bestScore)
                {
                    best = customer;
                    bestScore = score;
                }
            }
            if (best == null) return "There is nobody here to sell to.";
            var pitch = Sales.EmployeePitch(BadgeOf(vm, seller, Talent.Sales), book.StarsOf(SimNumber(best)), MeterOf(vm, best)?.Meter ?? 0);
            return StartPitch(vm, seller, best, pitch);
        }

        /// <summary>What a customer has in mind: what they last looked at, if it is still for sale and nobody else is buying it, or else what suits them best.</summary>
        private static VMMultitileGroup WantedItem(VM vm, LotShop shop, ShopBook book, VMAvatar customer)
        {
            var taken = new HashSet<VMMultitileGroup>(shop.Shoppers.Values.Select(x => x.Item));
            if (shop.Meters.TryGetValue(customer.ObjectID, out var meter) && meter.Item?.BaseObject != null && !meter.Item.BaseObject.Dead
                && meter.Item.SalePrice > 0 && !taken.Contains(meter.Item)) return meter.Item;
            var stars = book.StarsOf(SimNumber(customer));
            return ForSale(vm).Where(x => !taken.Contains(x))
                .OrderByDescending(x => Reputation.ChanceToBuy(x.SalePrice, (int?)CatalogItem(x.BaseObject)?.Price ?? 0, false, stars, 0))
                .FirstOrDefault();
        }

        private static void PitchWork(VM vm, LotShop shop, ShopBook book, VMEntity till)
        {
            foreach (var pair in shop.Pitches.ToList())
            {
                var seller = vm.GetObjectById(pair.Key) as VMAvatar;
                var pending = pair.Value;
                var customer = vm.GetObjectById(pending.Customer) as VMAvatar;
                if (seller == null || seller.Dead || !IsShopper(vm, book, customer) || shop.Shoppers.ContainsKey(pending.Customer)
                    || (pending.ByEmployee && EmployeeOf(vm, seller)?.Job != Job.Salesperson))
                {
                    shop.Pitches.Remove(pair.Key);
                    continue;
                }
                if (!NearSim(seller, customer) && ++pending.Waited < Sales.PitchAnywayAfter) continue;
                shop.Pitches.Remove(pair.Key);
                MakePitch(vm, shop, book, till, seller, customer, pending);
            }
        }

        private static void MakePitch(VM vm, LotShop shop, ShopBook book, VMEntity till, VMAvatar seller, VMAvatar customer, PendingPitch pending)
        {
            var item = WantedItem(vm, shop, book, customer);
            if (item == null) return;
            if (!shop.Meters.TryGetValue(customer.ObjectID, out var meter) || meter.Item != item)
            {
                shop.Meters[customer.ObjectID] = meter = new BuyMeter() { Item = item };
            }
            Meet(vm, shop, book, customer);
            var stars = book.StarsOf(SimNumber(customer));
            var pitch = pending.Pitch;
            var chance = Sales.Chance(pitch, BadgeOf(vm, seller, Talent.Sales), stars, Mood(customer), PricePercent(item, item.SalePrice), book.Has(PerkTree.Perception, 4));
            var worked = Dice.Next(100) < chance;
            var desire = worked && !meter.DesireUsed && book.Has(PerkTree.Perception, 2);
            if (desire) meter.DesireUsed = true;
            meter.Meter = Sales.Meter(meter.Meter, pitch, worked, desire);
            if (worked && pitch == Pitch.OfferDiscount) meter.Discount = true;
            var lost = Sales.StarsLost(pitch, worked);
            if (lost > 0) AddStars(vm, shop, book, customer, -lost);
            shop.PitchedAt[customer.ObjectID] = shop.Checks;
            Practice(vm, shop, seller, Talent.Sales);

            var buying = meter.Meter >= Sales.FullMeter;
            if (buying)
            {
                shop.Meters.Remove(customer.ObjectID);
                shop.LookAgainAt.Remove(customer.ObjectID);
                shop.Shoppers[customer.ObjectID] = new Shopper()
                {
                    AvatarID = customer.ObjectID,
                    Item = item,
                    Price = meter.Discount ? Sales.Discounted(item.SalePrice) : 0
                };
                WalkTo(vm, customer, (till != null) ? till.MultitileGroup : item);
            }
            Note(shop, $"{seller.Name}: {Sales.Name(pitch)} to {customer.Name} " + (worked ? "worked" : "failed") + (buying ? $", buying {NameOf(item.BaseObject)}" : $" ({meter.Meter}%)"));
            Changed(vm);
            if (!pending.ByEmployee) OnPitch?.Invoke(vm, pitch, worked, meter.Meter, buying);
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

        /// <summary>
        /// The book has changed. A business lot's is written now, since the lot itself will not be saved; and so are
        /// the badges, for the same reason. At home both are written when the lot is saved.
        /// </summary>
        private static void Changed(VM vm)
        {
            if (!IsVisiting(vm)) return;
            var book = BookOf(vm);
            SaveVisitedBook?.Invoke(vm, book.Empty ? null : book.Save());
            if (Badges.Dirty) SaveBadges?.Invoke();
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
        /// everything that sold is back and nothing has a price. The totals, customers and rank are kept.
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
            shop.Pitches.Clear();
            shop.Restockers.Clear();
            shop.Meters.Clear();
            refund = book.PricePaid;
            family.Budget += refund;
            book.Owner = 0;
            book.PricePaid = 0;
            book.TillID = 0;
            book.Prices.Clear();
            book.Shelves.Clear();
            book.Employees.Clear();
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

            var book = BookFor(shop.House);
            var open = !book.Closed;
            var till = Till(vm);
            var staff = StaffOnLot(vm, book);

            // 0. the hour: wages
            var hour = GameHour(vm);
            if (hour != shop.LastHour)
            {
                var first = shop.LastHour < 0;
                shop.LastHour = hour;
                if (!first && open && staff.Count > 0) PayWages(vm, shop, book, staff);
            }

            // 1. the staff at work, while the business is open
            if (open)
            {
                foreach (var (sim, record) in staff)
                {
                    if (book.EmployeeOf(record.Sim) == null) continue; //quit just now
                    switch (record.Job)
                    {
                        case Job.Cashier:
                            if (till != null && shop.Checks % SendBackEvery == 0 && !Reached(sim, till.MultitileGroup)) WalkTo(vm, sim, till.MultitileGroup);
                            break;
                        case Job.Restocker:
                            if (book.Shelves.Count > 0 && !shop.Restockers.ContainsKey(sim.ObjectID)
                                && Family(vm).Budget >= book.Shelves.Min(x => RestockCost(book, x))) StartRestock(vm, sim, true);
                            break;
                        case Job.Salesperson:
                            if (!shop.Pitches.ContainsKey(sim.ObjectID) && Dice.Next(3) == 0) PitchSomeone(vm, shop, book, sim);
                            break;
                    }
                }
            }
            RestockWork(vm, shop, book);
            PitchWork(vm, shop, book, till);

            // 2. Sims on their way to buy. With a till they go there and wait to be rung up. Without one they go to the
            //    object and leave the money.
            var tenders = Tenders(vm, till);
            var cashier = tenders.OrderByDescending(x => (int)BadgeOf(vm, x, Talent.Cashiering)).FirstOrDefault();
            var ringUpChecks = BadgeBook.RingUpChecks((cashier == null) ? BadgeLevel.None : BadgeOf(vm, cashier, Talent.Cashiering));
            var serving = false;
            foreach (var shopper in shop.Shoppers.Values.OrderByDescending(x => x.Served).ThenByDescending(x => x.Waited).ToList())
            {
                var avatar = vm.GetObjectById(shopper.AvatarID) as VMAvatar;
                var item = shopper.Item;
                if (!IsShopper(vm, book, avatar) || item.BaseObject == null || item.BaseObject.Dead || item.SalePrice <= 0)
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
                    if (cashier == null || serving) continue;
                    serving = true;
                    if (++shopper.Served < ringUpChecks) continue;
                }
                //something in use, or holding something, stays until it is free
                if (item.BaseObject.IsUserMovable(vm.Context, true) != VMPlacementError.Success) continue;
                shop.Shoppers.Remove(shopper.AvatarID);
                Sell(vm, shop, item, avatar, (shopper.Price > 0) ? shopper.Price : item.SalePrice);
                if (till != null) Practice(vm, shop, cashier, Talent.Cashiering);
            }

            // 3. Sims who might look at something, while the business is open
            if (!open) return;
            List<VMMultitileGroup> stock = null;
            var halves = Reputation.BrowseHalves(book.PerkLevel(PerkTree.Connections)) * ((cashier != null) ? 2 : 1);
            var browseOneIn = Math.Max(1, BrowseOneIn * 2 / halves);
            foreach (var avatar in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().ToList())
            {
                if (!IsShopper(vm, book, avatar) || shop.Shoppers.ContainsKey(avatar.ObjectID)) continue;
                if (shop.LookAgainAt.TryGetValue(avatar.ObjectID, out var at) && shop.Checks < at) continue;
                if (Dice.Next(browseOneIn) != 0) continue;

                if (stock == null) stock = ForSale(vm).Where(x => !shop.Shoppers.Values.Any(y => y.Item == x)).ToList();
                if (stock.Count == 0) break;
                var isNew = !book.Knows(SimNumber(avatar));
                Meet(vm, shop, book, avatar);
                var item = stock[Dice.Next(stock.Count)];
                var catalog = CatalogItem(item.BaseObject);
                var stars = book.StarsOf(SimNumber(avatar));
                if (catalog == null || Dice.Next(100) >= Reputation.ChanceToBuy(item.SalePrice, (int)catalog.Value.Price, cashier != null, stars, book.PerkLevel(PerkTree.Connections)))
                {
                    shop.LookAgainAt[avatar.ObjectID] = shop.Checks + LookAgainAfter;
                    //they have it in mind now: a sales pitch is about this
                    if (!shop.Meters.TryGetValue(avatar.ObjectID, out var meter) || meter.Item?.BaseObject == null || meter.Item.BaseObject.Dead || meter.Item.SalePrice <= 0)
                        shop.Meters[avatar.ObjectID] = new BuyMeter() { Item = item };
                    var putOff = (catalog == null) ? 0 : Reputation.StarsForLooking(item.SalePrice, (int)catalog.Value.Price);
                    if (putOff != 0) AddStars(vm, shop, book, avatar, putOff);
                    if (isNew || putOff != 0) Changed(vm);
                    continue;
                }
                stock.Remove(item);
                shop.LookAgainAt.Remove(avatar.ObjectID);
                shop.Meters.Remove(avatar.ObjectID);
                shop.Shoppers[avatar.ObjectID] = new Shopper() { AvatarID = avatar.ObjectID, Item = item };
                WalkTo(vm, avatar, (till != null) ? till.MultitileGroup : item);
            }
        }

        /// <summary>A salesperson picks a customer: one with something in mind first, and not one just pitched to.</summary>
        private static void PitchSomeone(VM vm, LotShop shop, ShopBook book, VMAvatar seller)
        {
            var busy = new HashSet<short>(shop.Pitches.Values.Select(x => x.Customer));
            var customers = vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>()
                .Where(x => IsShopper(vm, book, x) && !shop.Shoppers.ContainsKey(x.ObjectID) && !busy.Contains(x.ObjectID)
                    && !(shop.PitchedAt.TryGetValue(x.ObjectID, out var at) && shop.Checks - at < PitchAgainAfter))
                .OrderByDescending(x => shop.Meters.ContainsKey(x.ObjectID))
                .ThenBy(x => Dice.Next())
                .ToList();
            var customer = customers.FirstOrDefault();
            if (customer == null) return;
            var pitch = Sales.EmployeePitch(BadgeOf(vm, seller, Talent.Sales), book.StarsOf(SimNumber(customer)), MeterOf(vm, customer)?.Meter ?? 0);
            StartPitch(vm, seller, customer, pitch, true);
        }

        // ---- getting about ----

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

        private static bool Near(VMAvatar avatar, LotTilePos pos)
        {
            return pos.Level == avatar.Position.Level && Math.Abs(Tile(pos.x) - Tile(avatar.Position.x)) <= ReachTiles
                && Math.Abs(Tile(pos.y) - Tile(avatar.Position.y)) <= ReachTiles;
        }

        private static bool NearSim(VMAvatar a, VMAvatar b) => b.Position != LotTilePos.OUT_OF_WORLD && Near(a, b.Position);

        private static readonly (int x, int y)[] Around = { (0, 1), (1, 0), (0, -1), (-1, 0) };

        /// <summary>
        /// Sends a Sim to stand by an object (or another Sim), the way the player's "go here" does: the engine's own
        /// marker object is put on a tile beside it and its first interaction is queued for the Sim. If none of that is
        /// there to use the Sim stays put, and what they were sent for happens from where they are after a while.
        /// </summary>
        private static void WalkTo(VM vm, VMAvatar avatar, VMMultitileGroup item)
        {
            var taken = new HashSet<(int, int)>(item.Objects.Select(x => (Tile(x.Position.x), Tile(x.Position.y))));
            var tiles = new List<LotTilePos>();
            foreach (var part in item.Objects)
            {
                if (part.Position == LotTilePos.OUT_OF_WORLD) continue;
                foreach (var step in Around.OrderBy(x => Dice.Next()))
                {
                    var tx = Tile(part.Position.x) + step.x;
                    var ty = Tile(part.Position.y) + step.y;
                    if (!taken.Contains((tx, ty))) tiles.Add(new LotTilePos((short)(tx * 16 + 8), (short)(ty * 16 + 8), part.Position.Level));
                }
            }
            WalkToTiles(vm, avatar, tiles, item);
        }

        /// <summary>Sends a Sim to a spot (an empty shelf): the spot itself, or a tile beside it.</summary>
        private static void WalkToSpot(VM vm, VMAvatar avatar, LotTilePos pos)
        {
            var tiles = new List<LotTilePos>() { new LotTilePos((short)(Tile(pos.x) * 16 + 8), (short)(Tile(pos.y) * 16 + 8), pos.Level) };
            foreach (var step in Around.OrderBy(x => Dice.Next()))
                tiles.Add(new LotTilePos((short)((Tile(pos.x) + step.x) * 16 + 8), (short)((Tile(pos.y) + step.y) * 16 + 8), pos.Level));
            WalkToTiles(vm, avatar, tiles, null);
        }

        private static void WalkToTiles(VM vm, VMAvatar avatar, List<LotTilePos> tiles, VMMultitileGroup exclude)
        {
            try
            {
                foreach (var pos in tiles)
                {
                    if (vm.Context.IsOutOfBounds(pos)) continue;
                    if (vm.Context.ObjectQueries.GetObjectsAt(pos)?.Any(x => x is VMGameObject && x.MultitileGroup != exclude) ?? false) continue;

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
            catch (Exception e)
            {
                //walking over is for show. what they were sent for still happens.
                Console.WriteLine("[business] could not send " + avatar + ": " + e.Message);
            }
        }

        // ---- sales ----

        private static void Sell(VM vm, LotShop shop, VMMultitileGroup item, VMAvatar buyer, int price)
        {
            var family = Family(vm);
            var business = IsBusinessLot(vm);
            var name = NameOf(item.BaseObject);
            var buyerName = buyer.Name;
            var catalogPrice = (int?)CatalogItem(item.BaseObject)?.Price ?? 0;

            var book = BookFor(shop.House);
            var shelf = new Shelf()
            {
                GUID = GuidOf(item.BaseObject),
                Position = item.BaseObject.Position,
                Direction = item.BaseObject.Direction,
                Price = item.SalePrice, //restocked at the price it had, whatever this customer was offered
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
            AddStars(vm, shop, book, buyer, Reputation.StarsForSale(price, catalogPrice));
            Changed(vm);
            Console.WriteLine($"[business] sold {name} to {buyerName} for {price}");
            OnSale?.Invoke(vm, name, buyerName, price);
        }

        private static void Lose(VM vm, LotShop shop, VMMultitileGroup item, VMAvatar customer)
        {
            var name = customer.Name;
            var book = BookFor(shop.House);
            book.Lost++;
            Note(shop, $"{name} gave up waiting at the till ({NameOf(item.BaseObject)})");
            AddStars(vm, shop, book, customer, Reputation.StarsForGivingUp);
            Changed(vm);
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
