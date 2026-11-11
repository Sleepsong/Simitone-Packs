/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenForBusiness
{
    /// <summary>What a business can choose as it rises in rank: one for each rank.</summary>
    [Flags]
    public enum Perk : byte
    {
        None = 0,
        Wholesale = 1,
        Salesmanship = 2,
        WordOfMouth = 4,
        PatientCustomers = 8,
        QuickHands = 16
    }

    public class PerkInfo
    {
        public Perk Perk;
        /// <summary>The word typed after "business perk".</summary>
        public string Word;
        public string Name;
        public string Does;
    }

    /// <summary>
    /// Stage 4's rules, as plain sums: how customers come to like or dislike a business, how that makes its rank,
    /// and what the perks are worth. Nothing here knows about the game, so all of it can be tested without it.
    ///
    /// Each customer has between -5 and 5 stars for a business. The stars of all its customers together are the
    /// business's standing, and the highest standing it has reached decides its rank. A rank is never lost.
    /// </summary>
    public static class Reputation
    {
        public const int MaxStars = 5;
        public const int MaxRank = 5;

        /// <summary>A customer is this much more (or less) likely to buy, in percent, for each star.</summary>
        public const int ChancePerStar = 4;
        /// <summary>Salesmanship: customers are this much more likely to buy, in percent.</summary>
        public const int SalesmanshipBonus = 10;
        /// <summary>Wholesale: restocking costs this much less, in percent.</summary>
        public const int WholesalePercentOff = 20;
        /// <summary>Word of Mouth: visitors look at things this many times as often, in halves (3 = one and a half times).</summary>
        public const int WordOfMouthHalves = 3;
        /// <summary>Patient Customers: customers wait this many times as long at the till.</summary>
        public const int PatientTimes = 2;
        /// <summary>Quick Hands: ringing a customer up takes this many checks (about a second each), in place of three.</summary>
        public const int QuickRingUpChecks = 1;

        public static readonly PerkInfo[] Perks = new PerkInfo[]
        {
            new PerkInfo() { Perk = Perk.Wholesale, Word = "wholesale", Name = "Wholesale", Does = "Restocking costs 20% less." },
            new PerkInfo() { Perk = Perk.Salesmanship, Word = "salesmanship", Name = "Salesmanship", Does = "Customers are 10 points more likely to buy." },
            new PerkInfo() { Perk = Perk.WordOfMouth, Word = "wordofmouth", Name = "Word of Mouth", Does = "Visitors look at what is for sale one and a half times as often." },
            new PerkInfo() { Perk = Perk.PatientCustomers, Word = "patience", Name = "Patient Customers", Does = "Customers wait twice as long at the till." },
            new PerkInfo() { Perk = Perk.QuickHands, Word = "quickhands", Name = "Quick Hands", Does = "Ringing a customer up takes a third of the time." },
        };

        public static PerkInfo Find(string word)
        {
            word = (word ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            return Perks.FirstOrDefault(x => x.Word == word);
        }

        /// <summary>The standing a business needs for a rank: 4, 10, 18, 28 and 40 stars for ranks 1 to 5.</summary>
        public static int StarsForRank(int rank)
        {
            return (rank <= 0) ? 0 : rank * (rank + 3);
        }

        public static int RankFor(int stars)
        {
            var rank = 0;
            while (rank < MaxRank && stars >= StarsForRank(rank + 1)) rank++;
            return rank;
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

        public static int Count(Perk perks)
        {
            var count = 0;
            for (var bits = (int)perks; bits != 0; bits >>= 1) count += bits & 1;
            return count;
        }
    }
}
