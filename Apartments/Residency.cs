/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Which households live in which house.
    ///
    /// The game's neighbourhood file already lets this be said: every family record (FAMI) names its own house. The
    /// game only ever expects one family to name a given house, and keeps a one-family-per-house table when it loads.
    /// Here several families may name the same house, and they are that house's residents. Nothing is added to the
    /// neighbourhood file.
    ///
    /// The one thing the file cannot say is which of them is being played. That is kept in the pack's own file for the
    /// lot (PackStore), and the game's table is pointed at that family.
    ///
    /// Without the pack, a shared house shows as lived in by one of its families (the last in the file). The others
    /// are untouched but cannot be reached until the pack is on again.
    /// </summary>
    public static class Residency
    {
        public static string PackId = "sample.apartments";

        private static TS1NeighborhoodProvider Neigh => Content.Get()?.Neighborhood;

        /// <summary>Real households only: the game also keeps families for townies and its own characters.</summary>
        public static bool IsHousehold(FAMI family)
        {
            return family != null && family.ChunkID > 0 && family.FamilyGUIDs != null && family.FamilyGUIDs.Length > 0;
        }

        public static List<FAMI> AllFamilies(TS1NeighborhoodProvider neigh = null)
        {
            neigh = neigh ?? Neigh;
            if (neigh?.MainResource == null) return new List<FAMI>();
            return (neigh.MainResource.List<FAMI>() ?? new List<FAMI>()).Where(IsHousehold).OrderBy(x => x.ChunkID).ToList();
        }

        /// <summary>Every household living in this house, the played one included.</summary>
        public static List<FAMI> Residents(int house, TS1NeighborhoodProvider neigh = null)
        {
            if (house <= 0) return new List<FAMI>();
            return AllFamilies(neigh).Where(x => x.HouseNumber == house).ToList();
        }

        /// <summary>Households with no house, which can be moved in.</summary>
        public static List<FAMI> Homeless()
        {
            //the same test the family bin uses (ids from 1000 up are the game's own)
            return AllFamilies().Where(x => x.HouseNumber == 0 && x.ChunkID < 1000).ToList();
        }

        public static string NameOf(FAMI family)
        {
            if (family == null) return "(nobody)";
            string name = null;
            try
            {
                name = Neigh?.GetFamilyString((ushort)family.ChunkID)?.GetString(0);
            }
            catch
            {
            }
            return string.IsNullOrWhiteSpace(name) ? ("family " + family.ChunkID) : (name + " (" + family.ChunkID + ")");
        }

        /// <summary>Find a household by its number, or by the start of its name.</summary>
        public static FAMI Find(IEnumerable<FAMI> among, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var list = among.ToList();
            if (int.TryParse(text, out var id)) return list.FirstOrDefault(x => x.ChunkID == id);
            var matches = list.Where(x => NameOf(x).StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
            return (matches.Count == 1) ? matches[0] : null;
        }

        // ---- the played household ----

        private static string LotPath(int house, TS1NeighborhoodProvider neigh) => (neigh ?? Neigh).GetHousePath(house);

        /// <summary>The pack's file for a house (empty if it has none).</summary>
        public static LotFile Load(int house, TS1NeighborhoodProvider neigh = null)
        {
            try
            {
                return LotFile.Parse(PackStore.Read(PackId, LotPath(house, neigh)));
            }
            catch (Exception e)
            {
                AptLog.Write("Could not read the apartment file of house " + house + ": " + e.Message);
                return new LotFile();
            }
        }

        /// <summary>
        /// Keep the pack's file for a house. An ordinary house gets none. A house is an apartment building once it is
        /// made one, or once two households live in it.
        /// </summary>
        public static void Save(int house, LotFile file, TS1NeighborhoodProvider neigh = null)
        {
            var residents = Residents(house, neigh);
            if (file != null && residents.Count >= 2) file.Building = true;
            if (file == null || !file.Building)
            {
                PackStore.Write(PackId, LotPath(house, neigh), null);
                return;
            }
            file.Keep(residents.Select(x => (int)x.ChunkID));
            PackStore.Write(PackId, LotPath(house, neigh), file.ToBytes());
        }

        /// <summary>The household last played in this house, as the pack's file for the lot has it (0 if it has none).</summary>
        public static int ReadPlayed(int house, TS1NeighborhoodProvider neigh = null) => Load(house, neigh).Played;

        public static void WritePlayed(int house, int familyID, TS1NeighborhoodProvider neigh = null)
        {
            var file = Load(house, neigh);
            file.Played = familyID;
            Save(house, file, neigh);
        }

        /// <summary>
        /// Point the game's one-family-per-house table at the household that is played in each shared house. Called
        /// when a neighbourhood has loaded: the game has just filled the table with whichever family came last.
        /// </summary>
        public static void FixTable(TS1NeighborhoodProvider neigh)
        {
            if (neigh?.MainResource == null) return;
            foreach (var group in AllFamilies(neigh).Where(x => x.HouseNumber > 0).GroupBy(x => x.HouseNumber))
            {
                var residents = group.ToList();
                if (residents.Count < 2) continue;
                var played = ReadPlayed(group.Key, neigh);
                var family = residents.FirstOrDefault(x => x.ChunkID == played) ?? residents[0];
                neigh.FamilyForHouse[(short)group.Key] = family;
                AptLog.Write("House " + group.Key + " is shared by " + string.Join(", ", residents.Select(NameOf)) + ". Played: " + NameOf(family) + ".");
            }
        }

        /// <summary>Make this the household that is played in its house, from the next time the house is entered.</summary>
        public static void SetPlayed(FAMI family)
        {
            if (family == null || family.HouseNumber <= 0) return;
            Neigh.FamilyForHouse[(short)family.HouseNumber] = family;
            WritePlayed(family.HouseNumber, family.ChunkID);
        }

        /// <summary>
        /// The game has just moved a house's (played) family out, and thinks the house is empty. Its apartment is
        /// vacant again, and if other households live there, one of them becomes the played one.
        /// </summary>
        public static void AfterMoveOut(TS1NeighborhoodProvider neigh, short house)
        {
            var file = Load(house, neigh);
            var left = Residents(house, neigh);
            if (left.Count == 0)
            {
                if (file.Building)
                {
                    file.Played = 0;
                    Save(house, file, neigh); //(its apartments are all vacant now)
                }
                return;
            }
            var next = left.FirstOrDefault(x => x.ChunkID == file.Played) ?? left[0];
            neigh.FamilyForHouse[house] = next;
            file.Played = next.ChunkID;
            Save(house, file, neigh);
            AptLog.Write("The played household moved out of house " + house + ". " + NameOf(next) + " is now played there.");
        }

        /// <summary>
        /// A household with no house moves into an apartment of a building, from the neighbourhood screen. It becomes
        /// the household played there. Nothing is paid: an apartment is rented, not bought.
        /// </summary>
        public static void MoveIntoApartment(int house, FAMI family, int apartmentId, TS1NeighborhoodProvider neigh = null)
        {
            var file = Load(house, neigh);
            var apartment = file.Find(apartmentId);
            if (apartment == null || !apartment.ToLet) throw new InvalidOperationException("That apartment is not free.");
            family.HouseNumber = house;
            file.SetTenant(apartment, family.ChunkID);
            file.Played = family.ChunkID;
            file.AccountOf(family.ChunkID).Day = 0; //rent starts the day they are first played here
            (neigh ?? Neigh).FamilyForHouse[(short)house] = family;
            Save(house, file, neigh);
            AptLog.Write(NameOf(family) + " moved into " + apartment.Name + " of house " + house + ".");
        }
    }
}
