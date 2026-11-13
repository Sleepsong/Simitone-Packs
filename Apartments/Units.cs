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
    /// The pack's own file for a lot: everything about a shared house that the game's saves have no place for.
    ///
    ///   v=2
    ///   played=4                    the household being played (its family number)
    ///   rate=1                      rent, in simoleons for each tile each day (0: no rent)
    ///   unit=4:1,10,12;1,14,12      a household's apartment: one tile inside each of its rooms (floor,x,y)
    ///   rent=4:7215,0               a household's rent: the day it was last worked out, and what is owed
    ///   needs=1a2b3c4d:0,0,...      the needs a Sim went home with (the Sim's number, then its sixteen motives)
    ///
    /// An apartment is kept as a tile in each room, not as the engine's room numbers: those change whenever a wall is
    /// built or knocked down. Whatever room the tile is in now is the household's.
    /// </summary>
    public class LotFile
    {
        public const int DefaultRate = 1;

        public int Played;
        public int Rate = DefaultRate;
        public Dictionary<int, List<Tile>> Units = new Dictionary<int, List<Tile>>();
        public Dictionary<int, RentAccount> Rent = new Dictionary<int, RentAccount>();
        /// <summary>The needs each Sim went home with (by the Sim's own number), for when they are next put on the lot.</summary>
        public Dictionary<uint, short[]> Needs = new Dictionary<uint, short[]>();

        public RentAccount AccountOf(int family)
        {
            if (!Rent.TryGetValue(family, out var account)) Rent[family] = account = new RentAccount();
            return account;
        }

        public List<Tile> UnitOf(int family)
        {
            if (!Units.TryGetValue(family, out var unit)) Units[family] = unit = new List<Tile>();
            return unit;
        }

        /// <summary>Forget households that no longer live here.</summary>
        public void Keep(IEnumerable<int> families)
        {
            var keep = new HashSet<int>(families);
            foreach (var gone in Units.Keys.Where(x => !keep.Contains(x)).ToList()) Units.Remove(gone);
            foreach (var gone in Rent.Keys.Where(x => !keep.Contains(x)).ToList()) Rent.Remove(gone);
        }

        public byte[] ToBytes()
        {
            var text = new StringBuilder();
            text.Append("v=2\n");
            text.Append("played=").Append(Played).Append('\n');
            text.Append("rate=").Append(Rate).Append('\n');
            foreach (var unit in Units.Where(x => x.Value.Count > 0).OrderBy(x => x.Key))
            {
                text.Append("unit=").Append(unit.Key).Append(':').Append(string.Join(";", unit.Value)).Append('\n');
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
                        case "played":
                            file.Played = int.Parse(value); break;
                        case "rate":
                            file.Rate = Math.Max(0, int.Parse(value)); break;
                        case "unit":
                            {
                                var parts = value.Split(':');
                                var tiles = new List<Tile>();
                                foreach (var tile in parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    var n = tile.Split(',');
                                    tiles.Add(new Tile(sbyte.Parse(n[0]), short.Parse(n[1]), short.Parse(n[2])));
                                }
                                file.Units[int.Parse(parts[0])] = tiles;
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
            return file;
        }
    }

    /// <summary>
    /// Apartments: which rooms are whose, and the rent on them. Any room that is nobody's is shared, and so is
    /// everywhere outdoors.
    /// </summary>
    public static class Units
    {
        /// <summary>Rent is never worked out for more days than this at once, however long the clock jumped.</summary>
        public const int MostDaysAtOnce = 7;

        // ---- rooms ----

        /// <summary>The room a tile is in, or 0 if it is not in a room that can be someone's (outdoors, off the lot, a pool).</summary>
        public static ushort IndoorRoomAt(VM vm, LotTilePos pos)
        {
            if (pos == LotTilePos.OUT_OF_WORLD) return 0;
            var room = vm.Context.GetRoomAt(pos);
            var rooms = vm.Context.RoomInfo;
            if (room == 0 || rooms == null || room >= rooms.Length) return 0;
            var info = rooms[room].Room;
            return (info.IsOutside || info.IsPool) ? (ushort)0 : room;
        }

        /// <summary>Whose each room is, right now. A room two households have a tile in goes to the first (by family number).</summary>
        public static Dictionary<ushort, int> RoomOwners(VM vm, LotFile file)
        {
            var owners = new Dictionary<ushort, int>();
            if (file == null) return owners;
            foreach (var unit in file.Units.OrderBy(x => x.Key))
            {
                foreach (var tile in unit.Value)
                {
                    var room = IndoorRoomAt(vm, tile.ToPos());
                    if (room != 0 && !owners.ContainsKey(room)) owners[room] = unit.Key;
                }
            }
            return owners;
        }

        /// <summary>The floor area of a household's apartment, in tiles.</summary>
        public static int Area(VM vm, LotFile file, int family)
        {
            var area = 0;
            foreach (var pair in RoomOwners(vm, file))
            {
                if (pair.Value == family) area += vm.Context.RoomInfo[pair.Key].Room.Area;
            }
            return area;
        }

        public static int RoomCount(VM vm, LotFile file, int family) => RoomOwners(vm, file).Count(x => x.Value == family);

        /// <summary>Make the room at this spot part of a household's apartment. Returns what happened, for the player.</summary>
        public static string Claim(VM vm, LotFile file, int family, LotTilePos pos, Func<int, string> nameOf)
        {
            var room = IndoorRoomAt(vm, pos);
            if (room == 0) return "That is not in a room. Stand a Sim in a room with walls all round, and try again.";
            if (RoomOwners(vm, file).TryGetValue(room, out var owner))
            {
                return (owner == family) ? "That room is already part of the apartment." : ("That room is part of the apartment of " + nameOf(owner) + ". Free it first: apt unclaim");
            }
            file.UnitOf(family).Add(new Tile(pos.Level, pos.TileX, pos.TileY));
            return null;
        }

        /// <summary>Make the room at this spot shared again. Returns the household it was taken from (0 if it was nobody's).</summary>
        public static int Unclaim(VM vm, LotFile file, LotTilePos pos)
        {
            var room = IndoorRoomAt(vm, pos);
            if (room == 0 || !RoomOwners(vm, file).TryGetValue(room, out var owner)) return 0;
            file.UnitOf(owner).RemoveAll(tile => IndoorRoomAt(vm, tile.ToPos()) == room);
            return owner;
        }

        /// <summary>Tiles that mark a room no longer: their room was opened to the outdoors, or built over.</summary>
        public static List<Tile> LostTiles(VM vm, LotFile file, int family)
        {
            return file.Units.TryGetValue(family, out var unit) ? unit.Where(tile => IndoorRoomAt(vm, tile.ToPos()) == 0).ToList() : new List<Tile>();
        }

        // ---- rent ----

        /// <summary>A number for the game's day that only goes up. The game's months all have 30 days.</summary>
        public static int Today(VMClock clock) => clock.Year * 360 + (clock.Month - 1) * 30 + clock.DayOfMonth;

        public static int DailyRent(int area, int rate) => Math.Max(0, area) * Math.Max(0, rate);

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
