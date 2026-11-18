/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RoomTools
{
    /// <summary>
    /// One object in a blueprint (all the tiles of a multi-tile object are one): which object, its state as the game
    /// saves an object on its own (an FSOo marshal, as The Sims Online's inventory keeps one), and where it goes.
    /// </summary>
    public class PlanObject
    {
        public uint GUID;
        public string Name = "";
        /// <summary>The object's state: FreeSO's VMStandaloneObjectMarshal, serialised. Empty for a new one of its kind.</summary>
        public byte[] State = new byte[0];
        /// <summary>Where its main tile goes, in sixteenths of a tile from the plan's corner, and on which of the plan's floors.</summary>
        public int X, Y, Level = 1;
        public Direction Dir = Direction.NORTH;
        /// <summary>The object it sits in a slot of (an index into the blueprint's objects), or -1 if it stands on its own.</summary>
        public int Container = -1;
        /// <summary>Which of the container's tiles holds it (a multi-tile container has a set of slots on each), and which slot.</summary>
        public int ContainerPart, Slot;
        /// <summary>What it costs in the catalogue, to charge for a copy of it.</summary>
        public int Price;

        /// <summary>When a room is being moved rather than copied: the object on the lot, which moves itself.</summary>
        [NonSerialized] public FSO.SimAntics.Entities.VMMultitileGroup Live;
        /// <summary>Hung on a wall of the room from outside it (a window, seen from the garden): kept with that wall.</summary>
        [NonSerialized] public bool OnOuterWall;

        public PlanObject Clone() => (PlanObject)MemberwiseClone();
    }

    /// <summary>
    /// A room, or a whole lot, as Room Tools carries it: the walls and floors (a Plan), the objects, and for a lot its
    /// ground and roof. Saved to the library as a file of its own that can be given to someone else.
    /// </summary>
    public class Blueprint
    {
        public string Name = "";
        public bool IsLot;
        public Plan Plan = new Plan();
        public List<PlanObject> Objects = new List<PlanObject>();
        public DateTime Saved = DateTime.UtcNow;
        /// <summary>A picture of it for the library (a PNG), or none.</summary>
        public byte[] Thumbnail = new byte[0];

        // a whole lot's ground and roof
        public int LotSize;
        public short[] Heights;
        public byte[] Grass;
        public byte LightType, DarkType;
        public uint RoofStyle = 16;
        public float RoofPitch = 0.66f;

        /// <summary>Objects that could not be read back (their pack or download is not installed): their names.</summary>
        [NonSerialized] public List<string> Missing = new List<string>();

        /// <summary>The blueprint turned a number of quarter turns: its walls and floors, and its objects with them.</summary>
        public Blueprint Turned(int quarters)
        {
            quarters = ((quarters % 4) + 4) % 4;
            var b = (Blueprint)MemberwiseClone();
            b.Plan = Plan.Turned(quarters);
            b.Objects = Objects.Select(o => o.Clone()).ToList();
            var height = Plan.Height;
            var width = Plan.Width;
            for (int i = 0; i < quarters; i++)
            {
                foreach (var o in b.Objects)
                {
                    (o.X, o.Y) = Plan.TurnPoint(o.X, o.Y, height);
                    o.Dir = TurnDirection(o.Dir, 1);
                }
                (width, height) = (height, width);
            }
            return b;
        }

        /// <summary>A point (in sixteenths of a tile) in a plan of this size, turned with it.</summary>
        public static (int x, int y) TurnPoint(int x, int y, int width, int height, int quarters)
        {
            quarters = ((quarters % 4) + 4) % 4;
            for (int i = 0; i < quarters; i++)
            {
                (x, y) = Plan.TurnPoint(x, y, height);
                (width, height) = (height, width);
            }
            return (x, y);
        }

        /// <summary>A direction turned clockwise by quarter turns (north to east), as VMLotRotate turns objects.</summary>
        public static Direction TurnDirection(Direction dir, int quarters)
        {
            quarters = ((quarters % 4) + 4) % 4;
            int d = (int)dir & 255;
            for (int i = 0; i < quarters; i++) d = ((d << 2) | (d >> 6)) & 255;
            return (Direction)d;
        }

        public int ObjectPrice => Objects.Sum(x => Math.Max(0, x.Price));

        // ---- the file ----
        //
        // "SRTB", a version, what kind it is, its name, when it was saved, its picture, and then the rest compressed:
        // the plan, the objects (with their state), and a lot's ground. Read by Load, which refuses a newer version.

        private const int Magic = 0x42545253; //"SRTB": Simitone Room Tools Blueprint
        public const int FormatVersion = 1;
        public const string RoomExtension = ".sroom";
        public const string LotExtension = ".slot";

        public void Write(Stream stream)
        {
            var w = new BinaryWriter(stream, Encoding.UTF8, true);
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(IsLot);
            w.Write(Name ?? "");
            w.Write(Saved.ToBinary());
            w.Write(Objects.Count);
            w.Write(ObjectPrice);
            w.Write(Thumbnail.Length);
            w.Write(Thumbnail);

            using (var body = new MemoryStream())
            {
                using (var zip = new GZipStream(body, CompressionLevel.Optimal, true))
                using (var bw = new BinaryWriter(zip))
                {
                    WritePlan(bw, Plan);
                    bw.Write(Objects.Count);
                    foreach (var o in Objects)
                    {
                        bw.Write(o.GUID);
                        bw.Write(o.Name ?? "");
                        bw.Write(o.State.Length);
                        bw.Write(o.State);
                        bw.Write(o.X); bw.Write(o.Y); bw.Write(o.Level);
                        bw.Write((byte)o.Dir);
                        bw.Write(o.Container);
                        bw.Write(o.ContainerPart);
                        bw.Write(o.Slot);
                        bw.Write(o.Price);
                    }
                    bw.Write(LotSize);
                    WriteArray(bw, Heights, (x, v) => x.Write(v));
                    WriteArray(bw, Grass, (x, v) => x.Write(v));
                    bw.Write(LightType); bw.Write(DarkType);
                    bw.Write(RoofStyle); bw.Write(RoofPitch);
                }
                w.Write((int)body.Length);
                w.Write(body.ToArray());
            }
            w.Flush();
        }

        /// <summary>The name, kind and picture at the front of a blueprint file, read without the rest of it.</summary>
        public class Header
        {
            public bool IsLot;
            public string Name;
            public DateTime Saved;
            public int ObjectCount, ObjectPrice;
            public byte[] Thumbnail;
        }

        public static Header ReadHeader(Stream stream)
        {
            var r = new BinaryReader(stream, Encoding.UTF8, true);
            if (r.ReadInt32() != Magic) throw new InvalidDataException("This is not a Room Tools file.");
            var version = r.ReadInt32();
            if (version > FormatVersion) throw new InvalidDataException("This file was saved by a newer Room Tools.");
            var h = new Header { IsLot = r.ReadBoolean(), Name = r.ReadString(), Saved = DateTime.FromBinary(r.ReadInt64()), ObjectCount = r.ReadInt32(), ObjectPrice = r.ReadInt32() };
            var thumb = r.ReadInt32();
            if (thumb < 0 || thumb > 16 << 20) throw new InvalidDataException("This file is damaged.");
            h.Thumbnail = r.ReadBytes(thumb);
            return h;
        }

        public static Blueprint Read(Stream stream)
        {
            var h = ReadHeader(stream);
            var r = new BinaryReader(stream, Encoding.UTF8, true);
            var length = r.ReadInt32();
            if (length < 0 || length > 256 << 20) throw new InvalidDataException("This file is damaged.");
            var b = new Blueprint { IsLot = h.IsLot, Name = h.Name, Saved = h.Saved, Thumbnail = h.Thumbnail };
            using (var zip = new GZipStream(new MemoryStream(r.ReadBytes(length)), CompressionMode.Decompress))
            using (var br = new BinaryReader(zip))
            {
                b.Plan = ReadPlan(br);
                var count = br.ReadInt32();
                if (count < 0 || count > 100000) throw new InvalidDataException("This file is damaged.");
                for (int i = 0; i < count; i++)
                {
                    var o = new PlanObject { GUID = br.ReadUInt32(), Name = br.ReadString() };
                    var state = br.ReadInt32();
                    if (state < 0 || state > 64 << 20) throw new InvalidDataException("This file is damaged.");
                    o.State = br.ReadBytes(state);
                    o.X = br.ReadInt32(); o.Y = br.ReadInt32(); o.Level = br.ReadInt32();
                    o.Dir = (Direction)br.ReadByte();
                    o.Container = br.ReadInt32();
                    o.ContainerPart = br.ReadInt32();
                    o.Slot = br.ReadInt32();
                    o.Price = br.ReadInt32();
                    if (o.Container >= i) o.Container = -1; //a container always comes first: anything else is not to be trusted
                    b.Objects.Add(o);
                }
                b.LotSize = br.ReadInt32();
                b.Heights = ReadArray(br, x => x.ReadInt16());
                b.Grass = ReadArray(br, x => x.ReadByte());
                b.LightType = br.ReadByte(); b.DarkType = br.ReadByte();
                b.RoofStyle = br.ReadUInt32(); b.RoofPitch = br.ReadSingle();
            }
            return b;
        }

        private static void WritePlan(BinaryWriter w, Plan p)
        {
            w.Write(p.Width); w.Write(p.Height); w.Write(p.Levels);
            w.Write(p.Tiles.Count);
            foreach (var t in p.Tiles)
            {
                w.Write(t.X); w.Write(t.Y); w.Write((sbyte)t.Level);
                w.Write(t.Diagonal);
                if (t.Diagonal)
                {
                    w.Write((byte)t.DiagonalKind); w.Write(t.DiagonalStyle);
                    w.Write(t.Floor0); w.Write(t.Floor1); w.Write(t.Face0); w.Write(t.Face1);
                    w.Write((byte)t.Halves);
                }
                else w.Write(t.Floor);
            }
            w.Write(p.Edges.Count);
            foreach (var e in p.Edges)
            {
                w.Write(e.Edge.Vertical); w.Write(e.Edge.X); w.Write(e.Edge.Y); w.Write((sbyte)e.Edge.Level);
                w.Write(e.Look.Style); w.Write(e.Look.PlusPattern); w.Write(e.Look.MinusPattern);
                w.Write(e.Boundary); w.Write(e.RoomOnPlus);
            }
        }

        private static Plan ReadPlan(BinaryReader r)
        {
            var p = new Plan { Width = r.ReadInt32(), Height = r.ReadInt32(), Levels = r.ReadInt32() };
            if (p.Width <= 0 || p.Height <= 0 || p.Width > 256 || p.Height > 256 || p.Levels < 1 || p.Levels > 5) throw new InvalidDataException("This file is damaged.");
            var tiles = r.ReadInt32();
            if (tiles < 0 || tiles > p.Width * p.Height * p.Levels) throw new InvalidDataException("This file is damaged.");
            for (int i = 0; i < tiles; i++)
            {
                var t = new PlanTile { X = r.ReadInt32(), Y = r.ReadInt32(), Level = r.ReadSByte(), Diagonal = r.ReadBoolean(), Halves = 3 };
                if (t.Diagonal)
                {
                    t.DiagonalKind = (WallSegments)r.ReadByte();
                    if (t.DiagonalKind != WallSegments.HorizontalDiag && t.DiagonalKind != WallSegments.VerticalDiag) throw new InvalidDataException("This file is damaged.");
                    t.DiagonalStyle = r.ReadUInt16();
                    t.Floor0 = r.ReadUInt16(); t.Floor1 = r.ReadUInt16(); t.Face0 = r.ReadUInt16(); t.Face1 = r.ReadUInt16();
                    t.Halves = r.ReadByte() & 3;
                }
                else t.Floor = r.ReadUInt16();
                if (t.X < 0 || t.Y < 0 || t.X >= p.Width || t.Y >= p.Height || t.Level < 1 || t.Level > p.Levels) throw new InvalidDataException("This file is damaged.");
                p.Tiles.Add(t);
            }
            var edges = r.ReadInt32();
            if (edges < 0 || edges > 2 * (p.Width + 1) * (p.Height + 1) * p.Levels) throw new InvalidDataException("This file is damaged.");
            for (int i = 0; i < edges; i++)
            {
                var e = new Edge(r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadSByte());
                if (e.X < 0 || e.Y < 0 || e.X > p.Width || e.Y > p.Height || e.Level < 1 || e.Level > p.Levels) throw new InvalidDataException("This file is damaged.");
                p.Edges.Add(new PlanEdge
                {
                    Edge = e,
                    Look = new EdgeLook { Style = r.ReadUInt16(), PlusPattern = r.ReadUInt16(), MinusPattern = r.ReadUInt16() },
                    Boundary = r.ReadBoolean(),
                    RoomOnPlus = r.ReadBoolean(),
                });
            }
            return p;
        }

        private static void WriteArray<T>(BinaryWriter w, T[] values, Action<BinaryWriter, T> write)
        {
            if (values == null) { w.Write(-1); return; }
            w.Write(values.Length);
            foreach (var v in values) write(w, v);
        }

        private static T[] ReadArray<T>(BinaryReader r, Func<BinaryReader, T> read)
        {
            var n = r.ReadInt32();
            if (n < 0) return null;
            if (n > 1 << 20) throw new InvalidDataException("This file is damaged.");
            var result = new T[n];
            for (int i = 0; i < n; i++) result[i] = read(r);
            return result;
        }
    }
}
