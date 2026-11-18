/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Linq;

namespace OpenForBusiness
{
    /// <summary>The five perk trees of Open for Business. A business takes the perks of a tree in order.</summary>
    public enum PerkTree : byte
    {
        Cash = 0,
        Wholesale = 1,
        Perception = 2,
        Motivation = 3,
        Connections = 4
    }

    public class PerkInfo
    {
        public PerkTree Tree;
        /// <summary>1 to 5: its place in the tree.</summary>
        public int Level;
        public string Name;
        public string Does;
        /// <summary>True where the perk does something different from Open for Business's, because The Sims 1 has no such thing.</summary>
        public bool Adapted;
    }

    /// <summary>
    /// The business's rules, as plain sums: customer stars and business rank, the perks, sales pitches, and what
    /// employees ask to be paid. Nothing here knows about the game, so all of it can be tested without it.
    ///
    /// The numbers that come from Open for Business are marked as such. The others are this pack's own.
    /// </summary>
    public static class Reputation
    {
        // ---- customers and rank ----

        /// <summary>Each customer has between -5 and 5 stars for a business (as in Open for Business).</summary>
        public const int MaxStars = 5;
        public const int MinRank = -5, MaxRank = 10;
        /// <summary>One perk point for each new rank, up to 10 for a business (as in Open for Business).</summary>
        public const int MaxPerkPoints = 10;

        /// <summary>
        /// The stars a business needs for each rank, from -5 to 10. Ranks 0 to 10 and rank -5 are Open for Business's;
        /// ranks -1 to -4 are this pack's, since the sources found do not give them.
        /// </summary>
        private static readonly int[] Thresholds = { -50, -35, -20, -10, -3, 0, 3, 7, 12, 20, 30, 45, 60, 75, 100, 125 };

        private static readonly string[] Names =
        {
            "Bankrupt Bargain Basement", "Rank -4", "Rank -3", "Rank -2", "Rank -1",
            "Middling Merchant", "Tenderfoot Trader", "Budding Brokerage", "Fledgling Firm", "Burgeoning Bazaar",
            "Proficient Purveyor", "Empowered Emporium", "Unique Boutique", "Esteemed Establishment", "Outstanding Outlet",
            "Visionary Vending Venture"
        };

        public static int StarsForRank(int rank)
        {
            rank = Math.Max(MinRank, Math.Min(MaxRank, rank));
            return Thresholds[rank - MinRank];
        }

        public static string RankName(int rank)
        {
            rank = Math.Max(MinRank, Math.Min(MaxRank, rank));
            return Names[rank - MinRank];
        }

        /// <summary>The rank for a business's standing. Above 0 a rank needs at least its stars; below 0, a rank is reached at its stars or fewer.</summary>
        public static int RankFor(int stars)
        {
            if (stars >= 0)
            {
                var rank = 0;
                while (rank < MaxRank && stars >= StarsForRank(rank + 1)) rank++;
                return rank;
            }
            var down = 0;
            while (down > MinRank && stars <= StarsForRank(down - 1)) down--;
            return down;
        }

        /// <summary>A sale pleases the customer: two stars for a bargain (three quarters of the catalog's price or less), one up to the catalog's price, none above it.</summary>
        public static int StarsForSale(int salePrice, int catalogPrice)
        {
            if (catalogPrice <= 0) return 0;
            var percent = (long)salePrice * 100 / catalogPrice;
            return (percent <= 75) ? 2 : (percent <= 100) ? 1 : 0;
        }

        /// <summary>Looking at something priced at one and a half times the catalog's price or more, and not buying it, costs a star.</summary>
        public static int StarsForLooking(int salePrice, int catalogPrice)
        {
            if (catalogPrice <= 0) return 0;
            return ((long)salePrice * 100 / catalogPrice >= 150) ? -1 : 0;
        }

        /// <summary>Giving up at the till costs two stars.</summary>
        public const int StarsForGivingUp = -2;

        // ---- perks ----

        public static readonly PerkInfo[] Perks = new PerkInfo[]
        {
            // Cash: one-time payments (amounts as in Open for Business)
            new PerkInfo() { Tree = PerkTree.Cash, Level = 1, Name = "LeTourneau Prize", Does = "§1,000, once." },
            new PerkInfo() { Tree = PerkTree.Cash, Level = 2, Name = "Valued Client Rebate", Does = "§2,500, once." },
            new PerkInfo() { Tree = PerkTree.Cash, Level = 3, Name = "Chamber of Commerce Prize", Does = "§10,000, once." },
            new PerkInfo() { Tree = PerkTree.Cash, Level = 4, Name = "Owners Association Award", Does = "§20,000, once." },
            new PerkInfo() { Tree = PerkTree.Cash, Level = 5, Name = "Will Wright Grant", Does = "§50,000, once." },
            // Wholesale: off restocking (percentages as in Open for Business, where they are off catalog purchases)
            new PerkInfo() { Tree = PerkTree.Wholesale, Level = 1, Name = "Wholesale Discount", Does = "Restocking costs 12% less." },
            new PerkInfo() { Tree = PerkTree.Wholesale, Level = 2, Name = "Supplier Partnership", Does = "Restocking costs 15% less." },
            new PerkInfo() { Tree = PerkTree.Wholesale, Level = 3, Name = "Bargain Hunter", Does = "Restocking costs 19% less." },
            new PerkInfo() { Tree = PerkTree.Wholesale, Level = 4, Name = "Serious Negotiator", Does = "Restocking costs 24% less." },
            new PerkInfo() { Tree = PerkTree.Wholesale, Level = 5, Name = "Shark of Sharks", Does = "Restocking costs 30% less." },
            // Perception: selling
            new PerkInfo() { Tree = PerkTree.Perception, Level = 1, Name = "Assess Mood", Does = "Pointing at a customer shows their mood and their stars for the business." },
            new PerkInfo() { Tree = PerkTree.Perception, Level = 2, Name = "Assess Desire", Does = "Pointing at a customer shows what they want and how near they are to buying it, and the first pitch that works on it fills the buy meter further." },
            new PerkInfo() { Tree = PerkTree.Perception, Level = 3, Name = "Look for Mark", Does = "Click your Sim and choose Business, Look for Mark: they go to the customer most likely to buy and make a pitch." },
            new PerkInfo() { Tree = PerkTree.Perception, Level = 4, Name = "Convincing Personality", Does = "Every sales pitch is 10 points more likely to work." },
            new PerkInfo() { Tree = PerkTree.Perception, Level = 5, Name = "Manipulation", Does = "Adds Sales, Manipulate: the customer buys, and thinks two stars less of the business." },
            // Motivation: Open for Business's raise the owner's Influence, which The Sims 1 does not have. These work on employees and Sims instead.
            new PerkInfo() { Tree = PerkTree.Motivation, Level = 1, Name = "Simply Influential", Does = "Employees ask 10% less pay.", Adapted = true },
            new PerkInfo() { Tree = PerkTree.Motivation, Level = 2, Name = "Perk Up", Does = "Adds Business, Perk Up on any Sim: +30 Fun and +30 Social, once an hour for each Sim. An employee forgets being underpaid." },
            new PerkInfo() { Tree = PerkTree.Motivation, Level = 3, Name = "Motivational Speech", Does = "Adds Business, Motivational Speech on an employee or one of the family: their badges are a level higher for 3 hours. It tires the speaker." },
            new PerkInfo() { Tree = PerkTree.Motivation, Level = 4, Name = "Boundless Influence", Does = "Employees put up with low pay twice as long before they quit.", Adapted = true },
            new PerkInfo() { Tree = PerkTree.Motivation, Level = 5, Name = "Rally Forth!", Does = "Click your Sim and choose Business, Rally Forth!: the family and employees on the lot get +40 Energy, Comfort, Fun and Social, and your Sim loses 50 Energy and Comfort." },
            // Connections: Open for Business's start relationships higher and introduce Sims. These bring customers instead.
            new PerkInfo() { Tree = PerkTree.Connections, Level = 1, Name = "Notable Reputation", Does = "New customers start with 1 star.", Adapted = true },
            new PerkInfo() { Tree = PerkTree.Connections, Level = 2, Name = "Sterling Reputation", Does = "New customers start with 2 stars.", Adapted = true },
            new PerkInfo() { Tree = PerkTree.Connections, Level = 3, Name = "Network", Does = "Visitors look at what is for sale one and a half times as often.", Adapted = true },
            new PerkInfo() { Tree = PerkTree.Connections, Level = 4, Name = "Head for Numbers", Does = "Regulars (customers with 3 stars or more) are 10 points more likely to buy.", Adapted = true },
            new PerkInfo() { Tree = PerkTree.Connections, Level = 5, Name = "Power Network", Does = "Visitors look at what is for sale twice as often.", Adapted = true },
        };

        public static PerkInfo Perk(PerkTree tree, int level)
        {
            return Perks.FirstOrDefault(x => x.Tree == tree && x.Level == level);
        }

        public static PerkTree? FindTree(string word)
        {
            word = (word ?? "").Trim().ToLowerInvariant();
            foreach (PerkTree tree in Enum.GetValues(typeof(PerkTree)))
            {
                if (tree.ToString().ToLowerInvariant() == word) return tree;
            }
            if (word == "connection") return PerkTree.Connections;
            return null;
        }

        public static readonly int[] CashPrizes = { 1000, 2500, 10000, 20000, 50000 };
        public static readonly int[] WholesalePercentOff = { 0, 12, 15, 19, 24, 30 };

        /// <summary>What restocking costs, with the business's Wholesale level (0 to 5).</summary>
        public static int Wholesale(int cost, int level)
        {
            level = Math.Max(0, Math.Min(5, level));
            return (int)((long)cost * (100 - WholesalePercentOff[level]) / 100);
        }

        /// <summary>Stars a customer starts with, from the business's Connections level.</summary>
        public static int StartingStars(int connections) => (connections >= 2) ? 2 : (connections >= 1) ? 1 : 0;

        /// <summary>How often visitors look at what is for sale, in halves: 2 is normal, 3 one and a half times, 4 twice.</summary>
        public static int BrowseHalves(int connections) => (connections >= 5) ? 4 : (connections >= 3) ? 3 : 2;

        public const int RegularStars = 3;
        public const int RegularBonus = 10;

        // ---- buying ----

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

        /// <summary>A customer who looks is this much more likely to buy, in percent, for each star.</summary>
        public const int ChancePerStar = 4;
        /// <summary>...and this much more likely while someone tends the till.</summary>
        public const int TendedBonus = 15;

        /// <summary>
        /// The same, for a customer with that many stars for the business, with or without someone at the till, and with
        /// the business's Connections level. Never more than 95% or, for something anyone might buy, less than 1%.
        /// What nobody would buy, they still will not.
        /// </summary>
        public static int ChanceToBuy(int salePrice, int catalogPrice, bool tended, int stars, int connections)
        {
            var chance = ChanceToBuy(salePrice, catalogPrice);
            if (chance <= 0) return 0;
            if (tended) chance += TendedBonus;
            chance += stars * ChancePerStar;
            if (connections >= 4 && stars >= RegularStars) chance += RegularBonus;
            return Math.Max(1, Math.Min(95, chance));
        }

        // ---- employees ----

        /// <summary>The most employees a business can have, as in Open for Business.</summary>
        public const int MaxEmployees = 8;
        /// <summary>An employee with no badges asks this much an hour, and this much more for each badge level they hold.</summary>
        public const int BaseWage = 15, WagePerBadgeLevel = 10;
        /// <summary>A raise or a cut is this much an hour.</summary>
        public const int WageStep = 5;
        public const int MaxWage = 500;
        /// <summary>An employee paid less than they ask quits after this many hours of work (twice that with Boundless Influence).</summary>
        public const int UnderpaidHoursToQuit = 4;

        /// <summary>What an employee asks an hour: more for every badge level, as in Open for Business. Simply Influential takes 10% off.</summary>
        public static int AskingWage(int badgeLevels, bool simplyInfluential)
        {
            var wage = BaseWage + Math.Max(0, badgeLevels) * WagePerBadgeLevel;
            return simplyInfluential ? wage * 9 / 10 : wage;
        }

        public static int HoursToQuit(bool boundlessInfluence) => boundlessInfluence ? UnderpaidHoursToQuit * 2 : UnderpaidHoursToQuit;
    }
}
