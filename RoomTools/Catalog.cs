/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls.Catalog;
using FSO.Content.Interfaces;
using FSO.Content.Model;
using FSO.LotView.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoomTools
{
    /// <summary>
    /// The Rooms tab of build mode: the tools, then the library's rooms and lots. It is shown by the game's own catalogue
    /// panel, filled here instead of from the game's catalogue.
    /// </summary>
    public static class Catalog
    {
        /// <summary>The tab's number among build mode's: far from the game's own (0 to 2) and Apartments' (90).</summary>
        public const int CategoryId = 91;
        public const string TabIcon = "cat_build_flor.png";

        public enum Kind
        {
            MoveRoom = 0, CopyRoom = 1, SaveRoom = 2, PlaceRoom = 3,
            MoveWall = 10,
            Undo = 20, Redo = 21, SaveLot = 22, PlaceLot = 23,
        }

        /// <summary>The library as the tab last listed it: what a library entry's number refers to.</summary>
        public static List<Library.Entry> Listed = new List<Library.Entry>();

        private static readonly UICatalogRoomToolsResProvider Res = new UICatalogRoomToolsResProvider();

        public static List<UICatalogElement> Elements()
        {
            Listed = Library.All(true);
            UICatalogRoomToolsResProvider.Forget();
            var result = new List<UICatalogElement>();
            void Add(Kind kind, Type control, int index, string name, string description, int price, Func<Texture2D> icon)
            {
                var id = (ulong)((int)kind * 100000 + index + 1);
                UICatalogRoomToolsResProvider.Entries[id] = new UICatalogRoomToolsResProvider.Info { Name = name, Description = description, Price = price, Icon = icon };
                result.Add(new UICatalogElement
                {
                    Item = new ObjectCatalogItem { Name = name, Category = CategoryId, Price = (uint)Math.Max(0, price) },
                    DisplayName = name,
                    Special = new UISpecialCatalogElement
                    {
                        Control = control,
                        ResID = id,
                        Res = Res,
                        Parameters = new List<int> { (int)kind, index }
                    }
                });
            }

            Add(Kind.MoveRoom, typeof(UIRoomTool), 0, "Move Room",
                "Pick up a room with everything in it, and put it down somewhere else. Click a room, then click where it goes; [ and ] turn it. Walls the room shares with another stay for that room. Moving is free.",
                0, () => Icons.MoveRoom());
            Add(Kind.CopyRoom, typeof(UIRoomTool), 0, "Copy Room",
                "Build a copy of a room, with copies of everything in it. Click a room, then click where each copy goes; [ and ] turn it. You pay for the walls, floors and objects, as if you bought them.",
                0, () => Icons.CopyRoom());
            Add(Kind.MoveWall, typeof(UIWallMover), 0, "Move Wall",
                "Drag a straight wall to make a room bigger or smaller. The walls at its ends stretch or shrink to follow, the floor goes with the room, and doors and windows in the wall move with it.",
                0, () => Icons.MoveWall());
            Add(Kind.Undo, typeof(UIRoomAction), 0, "Undo",
                "Undo the last change made in build or buy mode (Ctrl+Z). Changes are kept until you go back to live mode.",
                0, () => Icons.Undo(false));
            Add(Kind.Redo, typeof(UIRoomAction), 0, "Redo",
                "Redo the last change you undid (Ctrl+Y, or Ctrl+Shift+Z).",
                0, () => Icons.Undo(true));
            Add(Kind.SaveRoom, typeof(UIRoomTool), 0, "Save Room",
                "Click a room to save it to your library, with everything in it. Saved rooms are listed here, to be built on any lot, and are kept as files in Library/Rooms that you can give to other players.",
                0, () => Icons.SaveRoom());
            Add(Kind.SaveLot, typeof(UIRoomAction), 0, "Save Lot",
                "Save this whole lot to your library (the Lot Bin): everything built, everything players can move, the ground and the roof. Saved lots are kept as files in Library/Lots that you can give to other players.",
                0, () => Icons.SaveLot());

            for (int i = 0; i < Listed.Count; i++)
            {
                var entry = Listed[i];
                var h = entry.Header;
                var when = h.Saved.ToLocalTime().ToString("d MMM yyyy");
                var path = entry.Path;
                if (!entry.IsLot)
                    Add(Kind.PlaceRoom, typeof(UIRoomTool), i, h.Name,
                        "A saved room, with " + h.ObjectCount + " object" + (h.ObjectCount == 1 ? "" : "s") + ", saved " + when + ". Click where it goes; [ and ] turn it. The price is its objects': walls and floors are added when you place it. Press Delete while it is chosen to take it out of the library.",
                        h.ObjectPrice, () => Icons.Thumbnail(path, h.Thumbnail, false));
                else
                    Add(Kind.PlaceLot, typeof(UIRoomAction), i, h.Name,
                        "A saved lot, with " + h.ObjectCount + " object" + (h.ObjectCount == 1 ? "" : "s") + ", saved " + when + ". Choose it to build it here in place of everything on this lot (the Sims stay). The price is its objects': you get back what this lot's would fetch.",
                        h.ObjectPrice, () => Icons.Thumbnail(path, h.Thumbnail, true));
            }
            return result;
        }
    }

    /// <summary>Names, pictures, words and prices of the Rooms tab's entries.</summary>
    public class UICatalogRoomToolsResProvider : UICatalogResProvider
    {
        public class Info
        {
            public string Name, Description;
            public int Price;
            public Func<Texture2D> Icon;
            public Texture2D Made;
        }

        public static Dictionary<ulong, Info> Entries = new Dictionary<ulong, Info>();

        /// <summary>A new listing: the pictures of library entries are made afresh (a file may have changed).</summary>
        public static void Forget()
        {
            foreach (var e in Entries.Values) if (e.Made != null && !Icons.IsShared(e.Made)) e.Made.Dispose();
            Entries.Clear();
        }

        public override Texture2D GetIcon(ulong id)
        {
            if (!Entries.TryGetValue(id, out var info)) return null;
            if (info.Made == null || info.Made.IsDisposed)
            {
                try { info.Made = info.Icon(); }
                catch (Exception e) { RoomToolsPack.Log("Could not draw the picture for " + info.Name + ": " + e.Message); }
            }
            return info.Made;
        }

        public override Texture2D GetThumb(ulong id) => GetIcon(id);
        public override string GetName(ulong id) => Entries.TryGetValue(id, out var info) ? info.Name : "";
        public override string GetDescription(ulong id) => Entries.TryGetValue(id, out var info) ? info.Description : "";
        public override int GetPrice(ulong id) => Entries.TryGetValue(id, out var info) ? info.Price : 0;
        public override bool DoDispose() => false;
    }

    /// <summary>The Rooms tab's pictures, drawn here: the game has none for these tools.</summary>
    public static class Icons
    {
        private const int Size = 64;
        private static readonly Dictionary<string, Texture2D> Shared = new Dictionary<string, Texture2D>();

        public static bool IsShared(Texture2D tex) => Shared.ContainsValue(tex);

        private static readonly Color Floor = new Color(222, 205, 168), WallColour = new Color(92, 70, 52), Arrow = new Color(52, 120, 200), Ghost = new Color(222, 205, 168, 140), Green = new Color(70, 160, 80);

        private class Canvas
        {
            public Color[] Px = new Color[Size * Size];
            public void Set(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < Size && y < Size) Px[y * Size + x] = c; }
            public void Fill(int x0, int y0, int x1, int y1, Color c) { for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) Set(x, y, c); }
            public void Line(float x0, float y0, float x1, float y1, float width, Color c)
            {
                var len = Math.Max(1, (int)(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2));
                for (int i = 0; i <= len; i++)
                {
                    var t = i / (float)len;
                    var x = x0 + (x1 - x0) * t; var y = y0 + (y1 - y0) * t;
                    for (float dy = -width / 2; dy <= width / 2; dy += 0.5f)
                        for (float dx = -width / 2; dx <= width / 2; dx += 0.5f)
                            if (dx * dx + dy * dy <= width * width / 4) Set((int)Math.Round(x + dx), (int)Math.Round(y + dy), c);
                }
            }
            public void Room(int x0, int y0, int x1, int y1, Color floor, Color wall)
            {
                Fill(x0, y0, x1, y1, floor);
                Line(x0, y0, x1, y0, 3, wall); Line(x1, y0, x1, y1, 3, wall); Line(x1, y1, x0, y1, 3, wall); Line(x0, y1, x0, y0, 3, wall);
            }
            public void Head(float x, float y, float dx, float dy, Color c)
            {
                var len = (float)Math.Sqrt(dx * dx + dy * dy);
                dx /= len; dy /= len;
                Line(x, y, x - dx * 8 - dy * 6, y - dy * 8 + dx * 6, 3, c);
                Line(x, y, x - dx * 8 + dy * 6, y - dy * 8 - dx * 6, 3, c);
            }
            public Texture2D Make()
            {
                var tex = new Texture2D(GameFacade.GraphicsDevice, Size, Size);
                tex.SetData(Px);
                return tex;
            }
        }

        private static Texture2D Keep(string name, Func<Canvas, Canvas> draw)
        {
            if (Shared.TryGetValue(name, out var tex) && !tex.IsDisposed) return tex;
            return Shared[name] = draw(new Canvas()).Make();
        }

        public static Texture2D MoveRoom() => Keep("move", c =>
        {
            c.Room(6, 22, 30, 46, Ghost, WallColour * 0.45f);
            c.Room(34, 14, 58, 38, Floor, WallColour);
            c.Line(16, 52, 44, 52, 3, Arrow); c.Head(46, 52, 1, 0, Arrow);
            return c;
        });

        public static Texture2D CopyRoom() => Keep("copy", c =>
        {
            c.Room(6, 8, 34, 36, Floor, WallColour);
            c.Room(28, 28, 58, 58, Floor, WallColour);
            c.Line(40, 36, 46, 36, 3, Green); c.Line(43, 33, 43, 39, 3, Green);
            return c;
        });

        public static Texture2D MoveWall() => Keep("wall", c =>
        {
            c.Fill(8, 14, 46, 50, Floor);
            c.Line(8, 14, 8, 50, 3, WallColour); c.Line(8, 14, 46, 14, 3, WallColour); c.Line(8, 50, 46, 50, 3, WallColour);
            c.Line(46, 14, 46, 50, 4, Arrow);
            c.Line(48, 32, 60, 32, 3, Arrow); c.Head(61, 32, 1, 0, Arrow);
            c.Line(44, 32, 32, 32, 3, Arrow); c.Head(31, 32, -1, 0, Arrow);
            return c;
        });

        public static Texture2D Undo(bool redo) => Keep(redo ? "redo" : "undo", c =>
        {
            //a hooked arrow: along the top, round, and back
            float s = redo ? -1 : 1;
            float X(float x) => redo ? Size - x : x;
            for (int i = 0; i <= 24; i++)
            {
                var a = Math.PI / 2 - Math.PI * i / 24;
                c.Line(X(36 + 14 * (float)Math.Cos(a)), 32 - 14 * (float)Math.Sin(a), X(36 + 14 * (float)Math.Cos(a)), 32 - 14 * (float)Math.Sin(a), 4, Arrow);
            }
            c.Line(X(36), 18, X(16), 18, 4, Arrow);
            c.Line(X(36), 46, X(26), 46, 4, Arrow);
            c.Head(X(14), 18, -s, 0, Arrow);
            return c;
        });

        public static Texture2D SaveRoom() => Keep("saveroom", c =>
        {
            c.Room(10, 6, 54, 36, Floor, WallColour);
            c.Line(32, 30, 32, 52, 4, Green); c.Head(32, 54, 0, 1, Green);
            c.Line(14, 58, 50, 58, 3, WallColour);
            return c;
        });

        public static Texture2D SaveLot() => Keep("savelot", c =>
        {
            c.Fill(16, 22, 48, 40, Floor);
            c.Line(16, 22, 16, 40, 3, WallColour); c.Line(48, 22, 48, 40, 3, WallColour); c.Line(16, 40, 48, 40, 3, WallColour);
            c.Line(12, 24, 32, 6, 3, new Color(170, 60, 50)); c.Line(32, 6, 52, 24, 3, new Color(170, 60, 50));
            c.Line(32, 40, 32, 54, 4, Green); c.Head(32, 56, 0, 1, Green);
            c.Line(14, 60, 50, 60, 3, WallColour);
            return c;
        });

        /// <summary>A library entry's picture, from its file (or a plain one if it has none).</summary>
        public static Texture2D Thumbnail(string path, byte[] png, bool lot)
        {
            if (png != null && png.Length > 0)
            {
                try
                {
                    using (var mem = new MemoryStream(png)) return Texture2D.FromStream(GameFacade.GraphicsDevice, mem);
                }
                catch { }
            }
            return lot ? SaveLot() : SaveRoom();
        }

        // ---- pictures for the library, made when something is saved ----

        /// <summary>A plan of a room seen from above: its floors, its walls, and a dot for each object. As a PNG.</summary>
        public static byte[] DrawPlan(Blueprint b, int size = 128)
        {
            var plan = b.Plan;
            var scale = (size - 8) / (float)Math.Max(plan.Width, plan.Height);
            var px = new Color[size * size];
            float ox = (size - plan.Width * scale) / 2, oy = (size - plan.Height * scale) / 2;
            void Set(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = c; }
            void Box(float x0, float y0, float x1, float y1, Color c)
            {
                for (int y = (int)Math.Floor(y0); y < (int)Math.Ceiling(y1); y++)
                    for (int x = (int)Math.Floor(x0); x < (int)Math.Ceiling(x1); x++) Set(x, y, c);
            }
            Color FloorColour(ushort pattern)
            {
                if (pattern == 0) return new Color(120, 170, 90);
                if (pattern >= 65534) return new Color(90, 160, 220);
                var rand = new Random(pattern);
                return new Color(170 + rand.Next(60), 150 + rand.Next(60), 120 + rand.Next(60));
            }
            var top = plan.Levels; //a lot: draw the ground floor, where most is
            foreach (var t in plan.Tiles.Where(x => x.Level == 1))
            {
                var colour = t.Diagonal ? FloorColour(t.Floor0) : FloorColour(t.Floor);
                Box(ox + t.X * scale, oy + t.Y * scale, ox + (t.X + 1) * scale, oy + (t.Y + 1) * scale, colour);
            }
            var wall = new Color(70, 50, 40);
            var thick = Math.Max(1.5f, scale / 6);
            foreach (var e in plan.Edges.Where(x => x.Edge.Level == 1))
            {
                if (e.Edge.Vertical) Box(ox + e.Edge.X * scale - thick / 2, oy + e.Edge.Y * scale - thick / 2, ox + e.Edge.X * scale + thick / 2, oy + (e.Edge.Y + 1) * scale + thick / 2, wall);
                else Box(ox + e.Edge.X * scale - thick / 2, oy + e.Edge.Y * scale - thick / 2, ox + (e.Edge.X + 1) * scale + thick / 2, oy + e.Edge.Y * scale + thick / 2, wall);
            }
            foreach (var t in plan.Tiles.Where(x => x.Diagonal && x.Level == 1))
            {
                for (float i = 0; i <= 1; i += 0.5f / scale)
                {
                    var x = t.DiagonalKind == WallSegments.VerticalDiag ? i : 1 - i;
                    Box(ox + (t.X + x) * scale - thick / 2, oy + (t.Y + i) * scale - thick / 2, ox + (t.X + x) * scale + thick / 2, oy + (t.Y + i) * scale + thick / 2, wall);
                }
            }
            var dot = new Color(60, 90, 160);
            var r = Math.Max(1.5f, scale / 4);
            foreach (var o in b.Objects.Where(x => x.Container < 0 && x.Level == 1))
                Box(ox + o.X / 16f * scale - r, oy + o.Y / 16f * scale - r, ox + o.X / 16f * scale + r, oy + o.Y / 16f * scale + r, dot);

            using (var tex = new Texture2D(GameFacade.GraphicsDevice, size, size))
            using (var mem = new MemoryStream())
            {
                tex.SetData(px);
                tex.SaveAsPng(mem, size, size);
                return mem.ToArray();
            }
        }

        /// <summary>A picture of the whole lot, as the game draws one for the neighbourhood. As a PNG, or empty if it can't be drawn.</summary>
        public static byte[] DrawLot(FSO.LotView.World world, Blueprint b)
        {
            try
            {
                var thumb = world.GetLotThumb(GameFacade.GraphicsDevice, (tex) => { });
                if (thumb == null) return DrawPlan(b);
                //smaller, as the game makes the neighbourhood's: by whole steps, to about 160 across
                var factor = Math.Max(1, (int)Math.Ceiling(Math.Max(thumb.Width, thumb.Height) / 160f));
                var small = (factor > 1) ? FSO.Common.Utils.TextureUtils.Decimate(thumb, GameFacade.GraphicsDevice, factor, false) : thumb;
                using (var mem = new MemoryStream())
                {
                    small.SaveAsPng(mem, small.Width, small.Height);
                    if (small != thumb) small.Dispose();
                    return mem.ToArray();
                }
            }
            catch (Exception e)
            {
                RoomToolsPack.Log("Could not draw the lot for its picture: " + e.Message);
                return DrawPlan(b);
            }
        }
    }
}
