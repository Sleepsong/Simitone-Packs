/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FurnishedMoveIn
{
    /// <summary>
    /// One thing on a lot: an object, or all the tiles of an object that covers several.
    /// </summary>
    public class LotItem
    {
        /// <summary>What it is. For an object of several tiles, the definition of the whole object.</summary>
        public OBJD Definition;
        /// <summary>The object ids of its tiles.</summary>
        public List<short> Ids = new List<short>();
        /// <summary>The object it is standing on or in (0 if none).</summary>
        public short Container;
        /// <summary>What it was bought for.</summary>
        public int Price;
        /// <summary>False for an object that exists but is not on the lot.</summary>
        public bool Placed;
    }

    /// <summary>
    /// What "unfurnished" means, and what a house's furniture is worth.
    ///
    /// The same rule is used twice: on the house's file, to quote the price before the family moves in, and on the
    /// lot once it is loaded, to take the furniture out. Both read the lot into a list of LotItems first, so the
    /// price quoted is for exactly the things that are then removed.
    /// </summary>
    public static class Furniture
    {
        // The Sims' buy mode categories (OBJD.FunctionFlags) and what is in each (OBJD.FunctionSubsort), as bits
        private const int Surfaces = 1 << 1, Appliances = 1 << 2, Plumbing = 1 << 4, Lighting = 1 << 7;
        private const int Counters = 1 << 0;
        private const int Stoves = 1 << 0, Fridges = 1 << 1;
        private const int Toilets = 1 << 0, Showers = 1 << 1, Sinks = 1 << 2;
        private const int WallLamps = 1 << 2, HangingLamps = 1 << 3;

        /// <summary>
        /// True for something an unfurnished house comes without. As in The Sims 3, the house keeps what is built
        /// into it: everything from build mode (doors, windows, stairs, fireplaces, plants, the pool's things),
        /// kitchen counters, the stove and the fridge, toilets, sinks, showers and baths, and wall and ceiling lights.
        /// Things that are not in the catalog at all (the mailbox, the bin at the kerb, the game's own helpers) stay too.
        /// </summary>
        public static bool IsFurniture(OBJD def)
        {
            if (def == null || def.ObjectType == OBJDType.Person) return false;
            if (def.BuildModeType > 0) return false;
            int function = def.FunctionFlags & 0xFF;
            if (function == 0) return false;
            int sub = def.FunctionSubsort;

            if ((function & Surfaces) != 0 && (sub & Counters) != 0) return false;
            if ((function & Appliances) != 0 && (sub & (Stoves | Fridges)) != 0) return false;
            //plumbing that says nothing more about itself is kept: better a spare hot tub than no toilet
            if ((function & Plumbing) != 0 && (sub == 0 || (sub & (Toilets | Showers | Sinks)) != 0)) return false;
            if ((function & Lighting) != 0 && (sub & (WallLamps | HangingLamps)) != 0) return false;
            return true;
        }

        /// <summary>
        /// The items an unfurnished house comes without: its furniture, and whatever is standing on that furniture.
        /// </summary>
        public static List<LotItem> ToRemove(List<LotItem> items)
        {
            var removed = new HashSet<LotItem>();
            var byId = new Dictionary<short, LotItem>();
            foreach (var item in items)
                foreach (var id in item.Ids) byId[id] = item;

            foreach (var item in items)
            {
                //something standing on something else is on the lot if that is
                var placed = item.Placed || (item.Container != 0 && byId.ContainsKey(item.Container));
                if (placed && IsFurniture(item.Definition)) removed.Add(item);
            }

            //the lamp goes with the table it stands on, but people are never furniture
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var item in items)
                {
                    if (removed.Contains(item) || item.Container == 0) continue;
                    if (item.Definition == null || item.Definition.ObjectType == OBJDType.Person) continue;
                    if (byId.TryGetValue(item.Container, out var container) && removed.Contains(container))
                    {
                        removed.Add(item);
                        changed = true;
                    }
                }
            }
            return items.Where(removed.Contains).ToList();
        }

        public static int Value(List<LotItem> items)
        {
            return ToRemove(items).Sum(x => Math.Max(0, x.Price));
        }

        // ---- reading a lot into items ----

        /// <summary>A lot that is loaded and running.</summary>
        public static List<LotItem> FromLot(VM vm)
        {
            var result = new List<LotItem>();
            foreach (var group in vm.Entities.GroupBy(x => x.MultitileGroup))
            {
                var first = group.Key?.BaseObject ?? group.First();
                if (first is VMAvatar) continue;
                var contained = group.FirstOrDefault(x => x.Container != null);
                result.Add(new LotItem()
                {
                    Definition = first.MasterDefinition ?? first.Object.OBJ,
                    Ids = group.Select(x => x.ObjectID).ToList(),
                    Container = contained?.Container.ObjectID ?? 0,
                    Price = group.Key?.InitialPrice ?? 0,
                    Placed = first.Position != LotTilePos.OUT_OF_WORLD
                });
            }
            return result;
        }

        /// <summary>
        /// A house's file, without loading the lot. A house is either as The Sims saved it, or as Simitone did.
        /// Returns null if it cannot be read.
        /// </summary>
        public static List<LotItem> FromHouse(IffFile house)
        {
            try
            {
                var saved = house.List<FSOV>()?.FirstOrDefault();
                if (saved != null)
                {
                    var marshal = new VMMarshal();
                    using (var reader = new BinaryReader(new MemoryStream(saved.Data)))
                        marshal.Deserialize(reader);
                    return FromSimitoneSave(marshal);
                }
                return FromSimsSave(house);
            }
            catch (Exception e)
            {
                Console.WriteLine("[unfurnished] this house's objects could not be read: " + e.Message);
                return null;
            }
        }

        private static OBJD Define(uint guid)
        {
            return (guid == 0) ? null : Content.Get().WorldObjects.Get(guid)?.OBJ;
        }

        private static List<LotItem> FromSimitoneSave(VMMarshal marshal)
        {
            var result = new List<LotItem>();
            var entities = new Dictionary<short, VMEntityMarshal>();
            foreach (var entity in marshal.Entities) entities[entity.ObjectID] = entity;

            foreach (var group in marshal.MultitileGroups)
            {
                var tiles = group.Objects.Where(entities.ContainsKey).Select(x => entities[x]).ToList();
                if (tiles.Count == 0) continue;
                var first = tiles[0];
                if (first is VMAvatarMarshal) continue;
                result.Add(new LotItem()
                {
                    Definition = Define(first.MasterGUID) ?? Define(first.GUID),
                    Ids = tiles.Select(x => x.ObjectID).ToList(),
                    Container = tiles.FirstOrDefault(x => x.Container != 0)?.Container ?? 0,
                    Price = group.Price,
                    Placed = first.Position != LotTilePos.OUT_OF_WORLD
                });
            }
            return result;
        }

        /// <summary>The Sims' own save: its objects are in OBJM, and what each one is in OBJT. Read as the engine reads them.</summary>
        private static List<LotItem> FromSimsSave(IffFile house)
        {
            var objt = house.Get<OBJT>(0);
            var objm = house.Get<OBJM>(1);
            if (objt == null || objm == null) return new List<LotItem>(); //an empty plot

            objm.Prepare((ushort typeID) =>
            {
                var entry = objt.Entries[typeID - 1];
                return new OBJMResource() { OBJD = Define(entry.GUID), OBJT = entry };
            });

            var result = new List<LotItem>();
            var groups = new Dictionary<short, LotItem>(); //objects of several tiles, by the id of their lead tile

            foreach (var mapped in objm.ObjectData.Values)
            {
                var inst = mapped.Instance;
                if (inst.OBJD == null || inst.PersonData.HasValue) continue;

                var id = inst.ObjectData[(int)VMStackObjectVariable.ObjectId];
                var container = inst.ObjectData[(int)VMStackObjectVariable.ContainerId];
                var placed = !(inst.X == -16 && inst.Y == -16);

                if (inst.MultitileData == null)
                {
                    result.Add(new LotItem()
                    {
                        Definition = inst.OBJD,
                        Ids = new List<short>() { id },
                        Container = container,
                        Price = inst.OBJD.Price,
                        Placed = placed
                    });
                    continue;
                }

                var multitile = inst.MultitileData.Value;
                var lead = (multitile.MultitileParentID == 0) ? id : multitile.MultitileParentID;
                if (!groups.TryGetValue(lead, out var item))
                {
                    var tile = inst.OBJD;
                    var master = tile.ChunkParent.List<OBJD>()?.FirstOrDefault(x => x.IsMaster && x.MasterID == tile.MasterID) ?? tile;
                    item = new LotItem() { Definition = master, Price = master.Price, Placed = placed };
                    groups[lead] = item;
                    result.Add(item);
                }
                item.Ids.Add(id);
                if (container != 0) item.Container = container;
            }
            return result;
        }
    }
}
