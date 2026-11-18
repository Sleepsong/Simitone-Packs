/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;

namespace OpenForBusiness
{
    /// <summary>
    /// The sales pitches of Open for Business. Sell needs no badge; Offer Discount needs Bronze in Sales, Hard Sell
    /// Silver and Dazzle Gold, as in Open for Business. Manipulate comes with the Manipulation perk.
    /// </summary>
    public enum Pitch : byte
    {
        Sell = 0,
        OfferDiscount = 1,
        HardSell = 2,
        Dazzle = 3,
        Manipulate = 4
    }

    /// <summary>
    /// How a pitch goes. In Open for Business each pitch that works fills the customer's buy meter, and a full meter
    /// means they buy; the bigger pitches fill more and cost more when they fail. Whether a pitch works depends on the
    /// customer's loyalty and mood and the seller's Sales badge. The numbers here are this pack's.
    /// </summary>
    public static class Sales
    {
        public const int FullMeter = 100;
        /// <summary>Offer Discount sells at this much off, in percent.</summary>
        public const int DiscountPercent = 10;
        /// <summary>Assess Desire: the first pitch that works fills the meter this much further.</summary>
        public const int DesireBonus = 15;
        /// <summary>Convincing Personality: every pitch is this much more likely to work.</summary>
        public const int ConvincingBonus = 10;
        /// <summary>Each level of the seller's Sales badge makes a pitch this much more likely to work.</summary>
        public const int PerBadgeLevel = 5;
        /// <summary>A pitch made after this many checks (about a second each) without the seller reaching the customer is made from where they are.</summary>
        public const int PitchAnywayAfter = 20;

        public static BadgeLevel BadgeNeeded(Pitch pitch)
        {
            switch (pitch)
            {
                case Pitch.OfferDiscount: return BadgeLevel.Bronze;
                case Pitch.HardSell: return BadgeLevel.Silver;
                case Pitch.Dazzle: return BadgeLevel.Gold;
                default: return BadgeLevel.None;
            }
        }

        public static string Name(Pitch pitch)
        {
            switch (pitch)
            {
                case Pitch.OfferDiscount: return "Offer Discount";
                case Pitch.HardSell: return "Hard Sell";
                default: return pitch.ToString();
            }
        }

        private static int BaseChance(Pitch pitch) => new[] { 60, 75, 55, 50, 100 }[(int)pitch];
        /// <summary>How much of the buy meter a pitch that works fills.</summary>
        public static int Gain(Pitch pitch) => new[] { 25, 35, 45, 70, FullMeter }[(int)pitch];
        /// <summary>How much a pitch that fails empties the meter.</summary>
        public static int Loss(Pitch pitch) => new[] { 5, 5, 20, 35, 0 }[(int)pitch];
        /// <summary>Stars the customer loses for the business when a pitch fails (or, for Manipulate, when it works).</summary>
        public static int StarsLost(Pitch pitch, bool worked)
        {
            if (pitch == Pitch.Manipulate) return 2;
            if (worked) return 0;
            return (pitch == Pitch.HardSell || pitch == Pitch.Dazzle) ? 1 : 0;
        }

        /// <summary>
        /// The chance, in percent, that a pitch works. A happy customer, a loyal one, a better seller and a fair price
        /// all help. Between 5% and 95%, except Manipulate, which always works.
        /// </summary>
        /// <param name="salesBadge">The seller's Sales badge, with any Motivational Speech.</param>
        /// <param name="stars">The customer's stars for the business.</param>
        /// <param name="mood">The customer's mood, -100 to 100.</param>
        /// <param name="pricePercent">The price as a percentage of the catalog's.</param>
        public static int Chance(Pitch pitch, BadgeLevel salesBadge, int stars, int mood, int pricePercent, bool convincing)
        {
            if (pitch == Pitch.Manipulate) return 100;
            var chance = BaseChance(pitch) + (int)salesBadge * PerBadgeLevel + stars * Reputation.ChancePerStar + Math.Max(-100, Math.Min(100, mood)) / 10;
            if (pricePercent > 100) chance -= (pricePercent - 100) / 4;
            if (convincing) chance += ConvincingBonus;
            return Math.Max(5, Math.Min(95, chance));
        }

        /// <summary>The buy meter after a pitch.</summary>
        public static int Meter(int meter, Pitch pitch, bool worked, bool desire)
        {
            if (worked) return Math.Min(FullMeter, meter + Gain(pitch) + (desire ? DesireBonus : 0));
            return Math.Max(0, meter - Loss(pitch));
        }

        /// <summary>What a customer who was offered a discount pays.</summary>
        public static int Discounted(int price) => Math.Max(1, price * (100 - DiscountPercent) / 100);

        /// <summary>The pitch an employee in sales makes: the biggest their badge allows that the customer's loyalty will bear.</summary>
        public static Pitch EmployeePitch(BadgeLevel salesBadge, int stars, int meter)
        {
            if (salesBadge >= BadgeLevel.Gold && stars >= 3) return Pitch.Dazzle;
            if (salesBadge >= BadgeLevel.Silver && stars >= 1) return Pitch.HardSell;
            if (salesBadge >= BadgeLevel.Bronze && meter >= 50) return Pitch.OfferDiscount;
            return Pitch.Sell;
        }

        public static string MoodWord(int mood) => (mood < -20) ? "bad" : (mood > 30) ? "good" : "average";
    }
}
