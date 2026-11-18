/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    /// <summary>What an employee does, as in Open for Business: work the till, restock, or sell on the shop floor.</summary>
    public enum Job : byte
    {
        Cashier = 0,
        Restocker = 1,
        Salesperson = 2
    }

    public class Employee
    {
        /// <summary>The Sim's own number.</summary>
        public uint Sim;
        public Job Job;
        /// <summary>Pay for each hour of work.</summary>
        public int Wage;
        /// <summary>Hours worked in a row for less than they ask.</summary>
        public byte Underpaid;
    }

    /// <summary>
    /// A business's books: what it has taken, its till, the shelves that have sold out, its customers, rank and
    /// perks, and its staff. Kept for each lot, in this pack's own file.
    /// </summary>
    public class ShopBook
    {
        public const int MaxShelves = 200;
        public const int MaxCustomers = 2000;
        public const byte Version = 5;

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

        /// <summary>Every customer the business has had, by the Sim's own number, with their stars for it: -5 to 5.</summary>
        public Dictionary<uint, sbyte> Loyalty = new Dictionary<uint, sbyte>();
        /// <summary>The highest rank the business has reached. Each new rank brings a perk point.</summary>
        public sbyte HighestRank;
        /// <summary>How far along each perk tree the business is (0 to 5), by PerkTree.</summary>
        public byte[] PerkLevels = new byte[5];
        public List<Employee> Employees = new List<Employee>();
        /// <summary>Wages paid so far.</summary>
        public long Wages;
        /// <summary>Closed for business: nobody shops, and employees are not paid.</summary>
        public bool Closed;

        public bool Empty => Sold == 0 && Takings == 0 && Lost == 0 && TillID == 0 && Shelves.Count == 0 && Owner == 0 && Prices.Count == 0
            && Loyalty.Count == 0 && HighestRank == 0 && PerksTaken == 0 && Employees.Count == 0 && Wages == 0 && !Closed;

        /// <summary>The business's standing: all its customers' stars together.</summary>
        public int Stars => Loyalty.Values.Sum(x => (int)x);
        /// <summary>Its rank now, -5 to 10. It falls when customers think less of it.</summary>
        public int Rank => Reputation.RankFor(Stars);
        public int PerksTaken => PerkLevels.Sum(x => (int)x);
        /// <summary>
        /// Perk points to spend: one for each rank ever reached, up to 10, less the perks taken. A point not spent is
        /// lost while the rank is below it, as in Open for Business; perks already taken are kept.
        /// </summary>
        public int PerkPoints
        {
            get
            {
                var earned = Math.Min(Reputation.MaxPerkPoints, Math.Max(0, (int)HighestRank));
                return Math.Max(0, Math.Min(earned, Math.Max(0, Rank)) - PerksTaken);
            }
        }
        public int PerkLevel(PerkTree tree) => PerkLevels[(int)tree];
        public bool Has(PerkTree tree, int level) => PerkLevels[(int)tree] >= level;
        public int StarsOf(uint customer) => Loyalty.TryGetValue(customer, out var stars) ? stars : 0;
        public bool Knows(uint customer) => Loyalty.ContainsKey(customer);
        public Employee EmployeeOf(uint sim) => Employees.FirstOrDefault(x => x.Sim == sim);

        /// <summary>A customer comes for the first time, with the stars the business's Connections give them. Returns whether they were new.</summary>
        public bool Meet(uint customer)
        {
            if (customer == 0 || Loyalty.ContainsKey(customer) || Loyalty.Count >= MaxCustomers) return false;
            Loyalty[customer] = (sbyte)Reputation.StartingStars(PerkLevel(PerkTree.Connections));
            NoteRank();
            return true;
        }

        /// <summary>A customer gains or loses stars. Returns how the rank changed: 0, or the new rank's difference from the old.</summary>
        public int AddStars(uint customer, int change)
        {
            if (customer == 0) return 0;
            Meet(customer);
            if (change == 0 || !Loyalty.ContainsKey(customer)) return 0;
            var before = Rank;
            Loyalty[customer] = (sbyte)Math.Max(-Reputation.MaxStars, Math.Min(Reputation.MaxStars, StarsOf(customer) + change));
            NoteRank();
            return Rank - before;
        }

        private void NoteRank()
        {
            var rank = Rank;
            if (rank > HighestRank) HighestRank = (sbyte)rank;
        }

        /// <summary>Takes the next perk of a tree. Returns it, or null with why not.</summary>
        public PerkInfo TakePerk(PerkTree tree, out string problem)
        {
            problem = null;
            var level = PerkLevel(tree);
            if (level >= 5)
            {
                problem = "The business has every " + tree + " perk.";
                return null;
            }
            if (PerkPoints <= 0)
            {
                if (PerksTaken >= Reputation.MaxPerkPoints) problem = "The business has taken all " + Reputation.MaxPerkPoints + " of its perks.";
                else if (Rank < HighestRank) problem = $"The business has fallen to rank {Rank}. Its unspent perk points come back when it rises again.";
                else problem = $"A perk point comes with each new rank. Rank {Rank + 1} needs {Reputation.StarsForRank(Rank + 1)} stars; the business has {Stars}.";
                return null;
            }
            PerkLevels[(int)tree]++;
            return Reputation.Perk(tree, level + 1);
        }

        public byte[] Save()
        {
            using (var mem = new MemoryStream())
            using (var writer = new BinaryWriter(mem))
            {
                writer.Write(Version);
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
                //version 5 from here (version 4 had a rank of its own, five perks of its own, and employees without jobs)
                writer.Write(HighestRank);
                writer.Write((byte)PerkLevels.Length);
                writer.Write(PerkLevels);
                writer.Write(Wages);
                writer.Write(Closed);
                writer.Write(Employees.Count);
                foreach (var employee in Employees)
                {
                    writer.Write(employee.Sim);
                    writer.Write((byte)employee.Job);
                    writer.Write(employee.Wage);
                    writer.Write(employee.Underpaid);
                }
                writer.Write(Loyalty.Count);
                foreach (var customer in Loyalty)
                {
                    writer.Write(customer.Key);
                    writer.Write(customer.Value);
                }
                return mem.ToArray();
            }
        }

        public static ShopBook Load(byte[] data)
        {
            var result = new ShopBook();
            if (data == null || data.Length < 13 || data[0] < 1 || data[0] > Version) return result;
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
                    if (version < 4) return result; //stage 3 had no rank, perks or employees
                    if (version == 4)
                    {
                        //stage 4: its rank (0 to 5) and perks were this pack's own, before Open for Business's were followed.
                        //the rank is worked out again from the stars, and the perk points come back to spend on the new trees.
                        reader.ReadByte(); //rank
                        reader.ReadByte(); //perks
                        result.Wages = Math.Max(0, reader.ReadInt64());
                        var old = Math.Min(64, reader.ReadInt32());
                        for (int i = 0; i < old; i++)
                        {
                            var sim = reader.ReadUInt32();
                            if (result.Employees.Count < Reputation.MaxEmployees) result.Employees.Add(new Employee() { Sim = sim, Job = Job.Cashier, Wage = 20 });
                        }
                    }
                    else
                    {
                        result.HighestRank = (sbyte)Math.Max(Reputation.MinRank, Math.Min(Reputation.MaxRank, (int)reader.ReadSByte()));
                        var trees = reader.ReadByte();
                        var levels = reader.ReadBytes(trees);
                        for (int i = 0; i < Math.Min(levels.Length, result.PerkLevels.Length); i++) result.PerkLevels[i] = (byte)Math.Min(5, (int)levels[i]);
                        result.Wages = Math.Max(0, reader.ReadInt64());
                        result.Closed = reader.ReadBoolean();
                        var staff = Math.Min(64, reader.ReadInt32());
                        for (int i = 0; i < staff; i++)
                        {
                            var employee = new Employee();
                            employee.Sim = reader.ReadUInt32();
                            employee.Job = (Job)Math.Min(2, (int)reader.ReadByte());
                            employee.Wage = Math.Max(0, Math.Min(Reputation.MaxWage, reader.ReadInt32()));
                            employee.Underpaid = reader.ReadByte();
                            if (result.Employees.Count < Reputation.MaxEmployees) result.Employees.Add(employee);
                        }
                    }
                    var customers = Math.Min(MaxCustomers, reader.ReadInt32());
                    for (int i = 0; i < customers; i++)
                    {
                        var customer = reader.ReadUInt32();
                        var stars = reader.ReadSByte();
                        result.Loyalty[customer] = (sbyte)Math.Max(-Reputation.MaxStars, Math.Min(Reputation.MaxStars, (int)stars));
                    }
                }
            }
            catch (EndOfStreamException)
            {
                //cut short: keep what was read
            }
            result.NoteRank();
            return result;
        }
    }
}
