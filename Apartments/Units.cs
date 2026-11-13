/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using FSO.SimAntics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Apartments
{
    /// <summary>One tile of the lot (whole tiles, not the engine's sixteenths).</summary>
    public struct Tile : IEquatable<Tile>
    {
        public sbyte Level;
        public short X;
        public short Y;

        public Tile(sbyte level, short x, short y) { Level = level; X = x; Y = y; }
        public LotTilePos ToPos() => LotTilePos.FromBigTile(X, Y, Level);
        public bool Equals(Tile other) => Level == other.Level && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Tile other && Equals(other);
        public override int GetHashCode() => (Level << 24) ^ (X << 12) ^ Y;
        public override string ToString() => Level + "," + X + "," + Y;
    }

    /// <summary>What a household owes for its apartment.</summary>
    public class RentAccount
    {
        /// <summary>The last game day rent was worked out for (see Units.Today). 0: never.</summary>
        public int Day;
        /// <summary>Rent that could not be paid when it fell due.</summary>
        public int Owed;
    }

    /// <summary>
    /// One apartment of a building: some rooms, and the household that rents them, if any. It exists whether or not
    /// anyone lives in it, so a building can be laid out before anybody moves in.
    ///
    /// Its rooms are kept as one tile inside each, not as the engine's room numbers: those change whenever a wall is
    /// built or knocked down. Whatever room the tile is in now belongs to the apartment.
    /// </summary>
    public class Apartment
    {
        public int Id;
        public string Name;
        public List<Tile> Tiles = new List<Tile>();
        /// <summary>The family number of the household that lives here. 0: vacant.</summary>
        public int Tenant;
        /// <summary>
        /// How many rooms it has and their floor area in tiles, as of the last time the lot was saved. Only a loaded
        /// lot knows its rooms, and the neighbourhood screen needs these to offer the apartment.
        /// </summary>
        public int Rooms, Area;

        public bool Vacant => Tenant == 0;
        /// <summary>Somewhere a household could move into: nobody lives there, and it has at least one room.</summary>
        public bool ToLet => Vacant && Tiles.Count > 0;
    }

    /// <summary>
    /// The pack's own file for a lot: everything about an apartment building that the game's saves have no place for.
    ///
    ///   v=3
    ///   building=1                             the lot is an apartment building
    ///   played=4                               the household being played (its family number)
    ///   rate=1                                 rent, in simoleons for each tile each day (0: no rent)
    ///   apartment=1|4|3|62|Apartment 1|1,10,12;1,14,12
    ///                                          its number, tenant (0: vacant), rooms and area when last saved,
    ///                                          name, and a tile inside each of its rooms (floor,x,y)
    ///   rent=4:7215,0                          a household's rent: the day it was last worked out, and what is owed
    ///   needs=1a2b3c4d:0,0,...                 the needs a Sim went home with (the Sim's number, then its motives)
    ///
    /// Version 2 gave each household its rooms directly (unit=4:1,10,12;...). That still reads: each becomes an
    /// apartment with that household as its tenant.
    /// </summary>
    public class LotFile
    {
        public const int DefaultRate = 1;

        public bool Building;
        public int Played;
        public int Rate = DefaultRate;
        public List<Apartment> Apartments = new List<Apartment>();
        public Dictionary<int, RentAccount> Rent = new Dictionary<int, RentAccount>();
        /// <summary>The needs each Sim went home with (by the Sim's own number), for when they are next put on the lot.</summary>
        public Dictionary<uint, short[]> Needs = new Dictionary<uint, short[]>();

        public RentAccount AccountOf(int family)
        {
            if (!Rent.TryGetValue(family, out var account)) Rent[family] = account = new RentAccount();
            return account;
        }

        public Apartment Find(int id) => Apartments.FirstOrDefault(x => x.Id == id);

        /// <summary>The apartment a household lives in, or null if it has none.</summary>
        public Apartment ApartmentOf(int family) => (family == 0) ? null : Apartments.FirstOrDefault(x => x.Tenant == family);

        /// <summary>The apartments a household could move into.</summary>
        public List<Apartment> ToLet => Apartments.Where(x => x.ToLet).ToList();

        public Apartment Add(string name = null)
        {
            var id = (Apartments.Count == 0) ? 1 : Apartments.Max(x => x.Id) + 1;
            var apartment = new Apartment() { Id = id, Name = string.IsNullOrWhiteSpace(name) ? ("Apartment " + id) : name };
            Apartments.Add(apartment);
            return apartment;
        }

        /// <summary>Make a household the tenant of an apartment (null: of none). It leaves the one it had.</summary>
        public void SetTenant(Apartment apartment, int family)
        {
            if (family != 0)
            {
                foreach (var other in Apartments.Where(x => x.Tenant == family)) other.Tenant = 0;
            }
            if (apartment != null) apartment.Tenant = family;
        }

        /// <summary>Forget households that no longer live here. Their apartments become vacant.</summary>
        public void Keep(IEnumerable<int> families)
        {
            var keep = new HashSet<int>(families);
            foreach (var apartment in Apartments.Where(x => x.Tenant != 0 && !keep.Contains(x.Tenant))) apartment.Tenant = 0;
            foreach (var gone in Rent.Keys.Where(x => !keep.Contains(x)).ToList()) Rent.Remove(gone);
        }

        private static string Clean(string name) => (name ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();

        public byte[] ToBytes()
        {
            var text = new StringBuilder();
            text.Append("v=3\n");
            text.Append("building=").Append(Building ? 1 : 0).Append('\n');
            text.Append("played=").Append(Played).Append('\n');
            text.Append("rate=").Append(Rate).Append('\n');
            foreach (var apartment in Apartments.OrderBy(x => x.Id))
            {
                text.Append("apartment=").Append(apartment.Id).Append('|').Append(apartment.Tenant).Append('|').Append(apartment.Rooms).Append('|')
                    .Append(apartment.Area).Append('|').Append(Clean(apartment.Name)).Append('|').Append(string.Join(";", apartment.Tiles)).Append('\n');
            }
            foreach (var rent in Rent.OrderBy(x => x.Key))
            {
                text.Append("rent=").Append(rent.Key).Append(':').Append(rent.Value.Day).Append(',').Append(rent.Value.Owed).Append('\n');
            }
            foreach (var needs in Needs.OrderBy(x => x.Key))
            {
                text.Append("needs=").Append(needs.Key.ToString("x8")).Append(':').Append(string.Join(",", needs.Value)).Append('\n');
            }
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        private static List<Tile> ParseTiles(string text)
        {
            var tiles = new List<Tile>();
            foreach (var tile in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var n = tile.Split(',');
                tiles.Add(new Tile(sbyte.Parse(n[0]), short.Parse(n[1]), short.Parse(n[2])));
            }
            return tiles;
        }

        /// <summary>Read a file. Lines that make no sense are skipped, so one bad line does not lose the rest.</summary>
        public static LotFile Parse(byte[] data)
        {
            var file = new LotFile();
            if (data == null) return file;
            foreach (var raw in Encoding.UTF8.GetString(data).Split('\n'))
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=');
                if (eq < 1) continue;
                var key = line.Substring(0, eq);
                var value = line.Substring(eq + 1);
                try
                {
                    switch (key)
                    {
                        case "building":
                            file.Building = int.Parse(value) != 0; break;
                        case "played":
                            file.Played = int.Parse(value); break;
                        case "rate":
                            file.Rate = Math.Max(0, int.Parse(value)); break;
                        case "apartment":
                            {
                                var parts = value.Split('|');
                                var id = int.Parse(parts[0]);
                                if (file.Find(id) != null) break; //the first of two with a number keeps it
                                file.Apartments.Add(new Apartment()
                                {
                                    Id = id,
                                    Tenant = Math.Max(0, int.Parse(parts[1])),
                                    Rooms = int.Parse(parts[2]),
                                    Area = int.Parse(parts[3]),
                                    Name = string.IsNullOrWhiteSpace(parts[4]) ? ("Apartment " + id) : parts[4],
                                    Tiles = ParseTiles(parts[5])
                                });
                                break;
                            }
                        case "unit": //version 2: a household's rooms
                            {
                                var parts = value.Split(':');
                                var tiles = ParseTiles(parts[1]);
                                var apartment = file.Add();
                                apartment.Tenant = int.Parse(parts[0]);
                                apartment.Tiles = tiles;
                                file.Building = true;
                                break;
                            }
                        case "needs":
                            {
                                var parts = value.Split(':');
                                file.Needs[Convert.ToUInt32(parts[0], 16)] = parts[1].Split(',').Select(short.Parse).ToArray();
                                break;
                            }
                        case "rent":
                            {
                                var parts = value.Split(':');
                                var n = parts[1].Split(',');
                                file.Rent[int.Parse(parts[0])] = new RentAccount() { Day = int.Parse(n[0]), Owed = Math.Max(0, int.Parse(n[1])) };
                                break;
                            }
                    }
                }
                catch
                {
                    AptLog.Write("Skipped a line of a lot's apartment file that could not be read: " + line);
                }
            }
            //a household lives in one apartment
            var seen = new HashSet<int>();
            foreach (var apartment in file.Apartments.OrderBy(x => x.Id))
            {
                if (apartment.Tenant != 0 && !seen.Add(apartment.Tenant)) apartment.Tenant = 0;
            }
            return file;
        }
    }

    /// <summary>
    /// Apartments on a loaded lot: which rooms are whose, and the rent on them. Any room that is in no apartment is
    /// shared, and so is everywhere outdoors.
    /// </summary>
    public static class Units
    {
        /// <summary>Rent is never worked out for more days than this at once, however long the clock jumped.</summary>
        public const int MostDaysAtOnce = 7;

        // ---- rooms ----

        /// <summary>The room a tile is in, or 0 if it is not in a room that can be an apartment's (outdoors, off the lot, a pool).</summary>
        public static ushort IndoorRoomAt(VM vm, LotTilePos pos)
        {
            if (pos == LotTilePos.OUT_OF_WORLD) return 0;
            var room = vm.Context.GetRoomAt(pos);
            var rooms = vm.Context.RoomInfo;
            if (room == 0 || rooms == null || room >= rooms.Length) return 0;
            var info = rooms[room].Room;
            return (info.IsOutside || info.IsPool) ? (ushort)0 : room;
        }

        /// <summary>Which apartment each room belongs to, right now. A room two apartments have a tile in goes to the first.</summary>
        public static Dictionary<ushort, Apartment> RoomApartments(VM vm, LotFile file)
        {
            var result = new Dictionary<ushort, Apartment>();
            if (file == null) return result;
            foreach (var apartment in file.Apartments.OrderBy(x => x.Id))
            {
                foreach (var tile in apartment.Tiles)
                {
                    var room = IndoorRoomAt(vm, tile.ToPos());
                    if (room != 0 && !result.ContainsKey(room)) result[room] = apartment;
                }
            }
            return result;
        }

        /// <summary>Which household each room is the home of, right now. Rooms of vacant apartments are nobody's.</summary>
        public static Dictionary<ushort, int> RoomOwners(VM vm, LotFile file)
        {
            var owners = new Dictionary<ushort, int>();
            foreach (var pair in RoomApartments(vm, file))
            {
                if (pair.Value.Tenant != 0) owners[pair.Key] = pair.Value.Tenant;
            }
            return owners;
        }

        public static int RoomCount(VM vm, LotFile file, Apartment apartment) => RoomApartments(vm, file).Count(x => x.Value == apartment);

        /// <summary>The floor area of an apartment, in tiles.</summary>
        public static int Area(VM vm, LotFile file, Apartment apartment)
        {
            if (apartment == null) return 0;
            var area = 0;
            foreach (var pair in RoomApartments(vm, file))
            {
                if (pair.Value == apartment) area += vm.Context.RoomInfo[pair.Key].Room.Area;
            }
            return area;
        }

        /// <summary>The floor area of the apartment a household lives in (0 if it has none).</summary>
        public static int Area(VM vm, LotFile file, int family) => Area(vm, file, file?.ApartmentOf(family));

        /// <summary>Note each apartment's size in the file, for the neighbourhood screen, where no lot is loaded.</summary>
        public static void NoteSizes(VM vm, LotFile file)
        {
            var rooms = RoomApartments(vm, file);
            foreach (var apartment in file.Apartments)
            {
                apartment.Rooms = rooms.Count(x => x.Value == apartment);
                apartment.Area = rooms.Where(x => x.Value == apartment).Sum(x => (int)vm.Context.RoomInfo[x.Key].Room.Area);
            }
        }

        /// <summary>Make the room at this spot part of an apartment. Returns what is wrong, for the player, or null.</summary>
        public static string Claim(VM vm, LotFile file, Apartment apartment, LotTilePos pos)
        {
            var room = IndoorRoomAt(vm, pos);
            if (room == 0) return "That is not in a room. A room has walls all the way round; outdoors is always shared.";
            if (RoomApartments(vm, file).TryGetValue(room, out var owner))
            {
                return (owner == apartment) ? ("That room is already part of " + apartment.Name + ".") : ("That room is part of " + owner.Name + ". Take it out of that one first.");
            }
            apartment.Tiles.Add(new Tile(pos.Level, pos.TileX, pos.TileY));
            return null;
        }

        /// <summary>Make the room at this spot shared again. Returns the apartment it was taken from (null if it was in none).</summary>
        public static Apartment Unclaim(VM vm, LotFile file, LotTilePos pos)
        {
            var room = IndoorRoomAt(vm, pos);
            if (room == 0 || !RoomApartments(vm, file).TryGetValue(room, out var owner)) return null;
            owner.Tiles.RemoveAll(tile => IndoorRoomAt(vm, tile.ToPos()) == room);
            return owner;
        }

        /// <summary>Tiles that mark a room no longer: their room was opened to the outdoors, or built over.</summary>
        public static List<Tile> LostTiles(VM vm, Apartment apartment)
        {
            return apartment.Tiles.Where(tile => IndoorRoomAt(vm, tile.ToPos()) == 0).ToList();
        }

        // ---- rent ----

        /// <summary>A number for the game's day that only goes up. The game's months all have 30 days.</summary>
        public static int Today(VMClock clock) => clock.Year * 360 + (clock.Month - 1) * 30 + clock.DayOfMonth;

        public static int DailyRent(int area, int rate) => Math.Max(0, area) * Math.Max(0, rate);

        /// <summary>What a household pays to move into an apartment: two days' rent.</summary>
        public static int Deposit(int area, int rate) => DailyRent(area, rate) * DepositDays;

        public const int DepositDays = 2;

        /// <summary>How many days' rent fall due, given the day it was last worked out. Never negative, and never more than a week at once.</summary>
        public static int DaysDue(int lastDay, int today)
        {
            if (lastDay <= 0 || today <= lastDay) return 0;
            return Math.Min(today - lastDay, MostDaysAtOnce);
        }

        /// <summary>
        /// Take what is due from a household's money: as much as it has, and the rest is owed. Money is never taken
        /// below zero. Returns what was actually paid.
        /// </summary>
        public static int Pay(ref int budget, ref int owed, int due)
        {
            var total = Math.Max(0, owed) + Math.Max(0, due);
            var paid = Math.Min(total, Math.Max(0, budget));
            budget -= paid;
            owed = total - paid;
            return paid;
        }
    }
}
