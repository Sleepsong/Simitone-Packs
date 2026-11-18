/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenForBusiness
{
    /// <summary>
    /// The talent badges of Open for Business that a shop uses. The others (Toy Making, Robotics, Flower Arranging,
    /// Cosmetology) need the crafting stations and makeover chair that came with it, which this pack, being only code,
    /// cannot add.
    /// </summary>
    public enum Talent : byte
    {
        Cashiering = 0,
        Restocking = 1,
        Sales = 2
    }

    public enum BadgeLevel : byte
    {
        None = 0,
        Bronze = 1,
        Silver = 2,
        Gold = 3
    }

    /// <summary>
    /// Every Sim's talent badges in one neighbourhood. A badge belongs to the Sim, not to a business, so it goes with
    /// them from lot to lot. Badges are earned by doing the work: each customer rung up, object restocked or sales
    /// pitch made is a point towards the next level.
    /// </summary>
    public class BadgeBook
    {
        public const int TalentCount = 3;
        /// <summary>Points for Bronze, Silver and Gold (this pack's numbers: the sources found do not give Open for Business's).</summary>
        public static readonly int[] Thresholds = { 8, 25, 60 };
        public const int MaxPoints = 999;
        public const int MaxSims = 5000;

        private readonly Dictionary<uint, short[]> Points = new Dictionary<uint, short[]>();
        /// <summary>Badges a level higher for a while (Motivational Speech), by Sim: the game hour it ends. Not saved.</summary>
        private readonly Dictionary<uint, long> Boosted = new Dictionary<uint, long>();
        public bool Dirty;

        public int Count => Points.Count;

        public static BadgeLevel LevelFor(int points)
        {
            var level = 0;
            while (level < Thresholds.Length && points >= Thresholds[level]) level++;
            return (BadgeLevel)level;
        }

        /// <summary>Points to go for the next level, or 0 at Gold.</summary>
        public static int ToNext(int points)
        {
            var level = (int)LevelFor(points);
            return (level >= Thresholds.Length) ? 0 : Thresholds[level] - points;
        }

        public int PointsOf(uint sim, Talent talent)
        {
            return Points.TryGetValue(sim, out var points) ? points[(int)talent] : 0;
        }

        /// <summary>The badge the Sim has earned.</summary>
        public BadgeLevel Earned(uint sim, Talent talent) => LevelFor(PointsOf(sim, talent));

        /// <summary>The badge the Sim works at: what they have earned, a level higher while a Motivational Speech lasts (but not past Gold).</summary>
        public BadgeLevel Level(uint sim, Talent talent, long gameHour)
        {
            var level = (int)Earned(sim, talent);
            if (level > 0 && Boosted.TryGetValue(sim, out var until) && gameHour < until) level = Math.Min((int)BadgeLevel.Gold, level + 1);
            return (BadgeLevel)level;
        }

        /// <summary>Every earned badge level the Sim holds, added up (0 to 9). What employees ask to be paid follows it.</summary>
        public int TotalLevels(uint sim)
        {
            var total = 0;
            for (int i = 0; i < TalentCount; i++) total += (int)Earned(sim, (Talent)i);
            return total;
        }

        /// <summary>A point of practice. Returns the new level if that earned one, or null.</summary>
        public BadgeLevel? Practice(uint sim, Talent talent, int points = 1)
        {
            if (sim == 0 || points <= 0) return null;
            if (!Points.TryGetValue(sim, out var all))
            {
                if (Points.Count >= MaxSims) return null;
                Points[sim] = all = new short[TalentCount];
            }
            var before = LevelFor(all[(int)talent]);
            all[(int)talent] = (short)Math.Min(MaxPoints, all[(int)talent] + points);
            Dirty = true;
            var after = LevelFor(all[(int)talent]);
            return (after > before) ? after : (BadgeLevel?)null;
        }

        /// <summary>Sets a badge outright (for the console's badge cheat): its level's points.</summary>
        public void Set(uint sim, Talent talent, BadgeLevel level)
        {
            if (sim == 0) return;
            if (!Points.TryGetValue(sim, out var all)) Points[sim] = all = new short[TalentCount];
            all[(int)talent] = (short)((level == BadgeLevel.None) ? 0 : Thresholds[(int)level - 1]);
            Dirty = true;
        }

        public void Boost(uint sim, long untilHour) => Boosted[sim] = untilHour;

        public bool IsBoosted(uint sim, long gameHour) => Boosted.TryGetValue(sim, out var until) && gameHour < until;

        public byte[] Save()
        {
            using (var mem = new MemoryStream())
            using (var writer = new BinaryWriter(mem))
            {
                writer.Write((byte)1); //version
                var kept = Points.Where(x => x.Value.Any(y => y > 0)).ToList();
                writer.Write(kept.Count);
                foreach (var sim in kept)
                {
                    writer.Write(sim.Key);
                    writer.Write((byte)TalentCount);
                    foreach (var p in sim.Value) writer.Write(p);
                }
                return mem.ToArray();
            }
        }

        public static BadgeBook Load(byte[] data)
        {
            var result = new BadgeBook();
            if (data == null || data.Length < 5 || data[0] != 1) return result;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    reader.ReadByte();
                    var count = Math.Min(MaxSims, reader.ReadInt32());
                    for (int i = 0; i < count; i++)
                    {
                        var sim = reader.ReadUInt32();
                        var talents = reader.ReadByte();
                        var points = new short[TalentCount];
                        for (int t = 0; t < talents; t++)
                        {
                            var p = reader.ReadInt16();
                            if (t < TalentCount) points[t] = (short)Math.Max(0, Math.Min(MaxPoints, (int)p));
                        }
                        result.Points[sim] = points;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                //cut short: keep what was read
            }
            return result;
        }

        public static string Describe(BadgeLevel level) => (level == BadgeLevel.None) ? "no badge" : level.ToString();

        // ---- what a badge does ----

        /// <summary>Checks (about a second each) to ring up a customer: slow without a badge, almost at once with Gold, as in Open for Business.</summary>
        public static int RingUpChecks(BadgeLevel cashiering) => new[] { 4, 3, 2, 1 }[(int)cashiering];

        /// <summary>Checks to put one sold object back: very slow without a badge, almost at once with Gold, as in Open for Business.</summary>
        public static int RestockChecks(BadgeLevel restocking) => new[] { 6, 4, 2, 1 }[(int)restocking];
    }
}
