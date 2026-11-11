/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TerrainPaint
{
    /// <summary>
    /// The paint on a lot's ground: a terrain type for each point, laid out like the engine's grass state, or Unpainted
    /// for the lot's own ground. The engine's terrain has no room for this, so it is kept beside it, one PaintLayer for
    /// each terrain object, for as long as that terrain object lives.
    /// </summary>
    public class PaintLayer
    {
        public const byte Unpainted = 255;

        /// <summary>Null until something is painted.</summary>
        public byte[] Paint;
        /// <summary>The copy shown while a tool previews a change, as the engine keeps for heights and grass.</summary>
        public byte[] VisPaint;
        private byte[] OldPaint;

        private static ConditionalWeakTable<VMArchitectureTerrain, PaintLayer> Layers = new ConditionalWeakTable<VMArchitectureTerrain, PaintLayer>();
        public static PaintLayer For(VMArchitectureTerrain terrain) => Layers.GetOrCreateValue(terrain);

        public void EnterVis()
        {
            OldPaint = Paint;
            VisPaint = (Paint == null) ? null : (byte[])Paint.Clone();
            Paint = VisPaint;
        }

        public void ExitVis()
        {
            VisPaint = Paint; //(a preview may have started painting)
            Paint = OldPaint;
        }

        public bool AnyPaint
        {
            get
            {
                if (Paint == null) return false;
                foreach (var p in Paint) if (p != Unpainted) return true;
                return false;
            }
        }

        // ---- saving: this pack's own data for a lot (Simitone's LotPackData) ----

        private const int Magic = 0x544E5054; //"TPNT"
        private const byte FormatVersion = 1;

        /// <summary>The paint as bytes to save, or null if nothing is painted.</summary>
        public byte[] Save(int width, int height)
        {
            if (!AnyPaint || Paint.Length != width * height) return null;
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(width);
                writer.Write(height);
                writer.Write(Paint.Length);
                writer.Write(Paint);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>The paint in saved bytes, or null if they are not paint for a lot of this size.</summary>
        public static byte[] Load(byte[] saved, int width, int height)
        {
            if (saved == null || saved.Length < 17) return null;
            using (var reader = new BinaryReader(new MemoryStream(saved)))
            {
                if (reader.ReadInt32() != Magic) return null;
                var version = reader.ReadByte();
                var w = reader.ReadInt32();
                var h = reader.ReadInt32();
                var length = reader.ReadInt32();
                if (version > FormatVersion || w != width || h != height || length != width * height || saved.Length < 17 + length) return null;
                return reader.ReadBytes(length);
            }
        }
    }

    /// <summary>
    /// The paint brush: a round brush of one terrain type, or one that takes the paint off again.
    /// </summary>
    public static class PaintTools
    {
        /// <summary>
        /// The command that paints. The engine's list of architecture commands can't be added to, but a command's type
        /// is saved and sent as a byte, and the engine ignores types it doesn't know, so this pack uses a number of its
        /// own and handles it itself. x, y: centre point. x2: brush radius. pattern: the terrain type, or Erase.
        /// style: 1 for a soft edge.
        /// </summary>
        public const VMArchitectureCommandType PaintCommand = (VMArchitectureCommandType)200;

        /// <summary>The terrain types there are to paint with (the game's, except water).</summary>
        public static readonly TerrainType[] PaintTypes =
        {
            TerrainType.GRASS,
            TerrainType.TS1DarkGrass,
            TerrainType.TS1AutumnGrass,
            TerrainType.SAND,
            TerrainType.ROCK,
            TerrainType.SNOW,
            TerrainType.TS1Cloud,
        };

        public static string Name(TerrainType type)
        {
            switch (type)
            {
                case TerrainType.GRASS: return "Grass";
                case TerrainType.TS1DarkGrass: return "Dark Grass";
                case TerrainType.TS1AutumnGrass: return "Autumn Grass";
                case TerrainType.SAND: return "Sand";
                case TerrainType.ROCK: return "Rock";
                case TerrainType.SNOW: return "Snow";
                case TerrainType.TS1Cloud: return "Cloud";
                default: return type.ToString();
            }
        }

        public const byte Erase = PaintLayer.Unpainted;
        /// <summary>The biggest brush (radius in tiles).</summary>
        public const int MaxRadius = 6;

        /// <summary>Paints for real (or for a preview): the number of points changed, which is also the cost.</summary>
        public static int Paint(VMArchitecture arch, VMArchitectureCommand com)
        {
            var bounds = arch.DisableClip ? new Rectangle(0, 0, arch.Width, arch.Height) : arch.BuildableArea;
            bounds.X--; bounds.Y--; bounds.Width++; bounds.Height++;
            var changed = Apply(PaintLayer.For(arch.Terrain), arch.Terrain.Width, arch.Terrain.Height, bounds, com);
            if (changed > 0)
            {
                arch.SignalTerrainRedraw();
                arch.SignalRedraw();
            }
            return changed;
        }

        /// <summary>Paints the command's brush onto a layer, within bounds (in paint points): the number of points changed.</summary>
        public static int Apply(PaintLayer layer, int width, int height, Rectangle bounds, VMArchitectureCommand com)
        {
            var type = (byte)Math.Min(255, (int)com.pattern);
            if (type != Erase && Array.IndexOf(PaintTypes, (TerrainType)type) == -1) return 0;
            if (type == Erase && layer.Paint == null) return 0; //nothing painted to take off
            var radius = Math.Max(0, Math.Min(MaxRadius, com.x2));
            var soft = com.style != 0;

            if (type != Erase && (layer.Paint == null || layer.Paint.Length != width * height))
            {
                layer.Paint = new byte[width * height];
                for (int i = 0; i < layer.Paint.Length; i++) layer.Paint[i] = PaintLayer.Unpainted;
            }
            var paint = layer.Paint;
            var limit = (radius + 0.5f) * (radius + 0.5f);
            int count = 0;
            for (int y = com.y - radius; y <= com.y + radius; y++)
            {
                for (int x = com.x - radius; x <= com.x + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= width || y >= height || !bounds.Contains(x, y)) continue;
                    var dx = x - com.x;
                    var dy = y - com.y;
                    var d2 = dx * dx + dy * dy;
                    if (d2 > limit) continue;
                    //a soft brush paints only some of the points around its edge, so the paint feathers into what's there
                    if (soft && radius > 0 && d2 > (radius - 0.5f) * (radius - 0.5f) && (Hash(x, y) & 1) == 0) continue;
                    var off = y * width + x;
                    if (paint[off] == type) continue;
                    paint[off] = type;
                    count++;
                }
            }
            if (type == Erase && !layer.AnyPaint) layer.Paint = null; //all off again: no layer
            return count;
        }

        /// <summary>A fixed pattern (the same on every machine) for the soft brush's edge.</summary>
        private static int Hash(int x, int y)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }
    }
}
