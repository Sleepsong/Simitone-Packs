/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using Driveways;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Writes Driveways.iff: the pack's objects, a driveway and some cars.
// Usage: MakeDriveways <output folder> [preview folder]   (then it reads the file back and checks it)
//
// The file holds what the objects are (their definitions, their menu, their scripts) and almost nothing of what
// they look like. A driveway and a car each cover many tiles, and every tile is its own object; all the driveway's
// tiles share one flat grey square, and all the cars' share one grey block. Like the Test Cube sample's, those are
// drawn here by tracing rays, with the camera maths the engine uses to turn a sprite and its depth back into 3D.
// In the game the grey is replaced: driveway tiles are painted with one of the game's floor tiles, and car tiles
// are drawn with the tiles of one of the game's own cars.
//
// The objects' scripts are an instruction or two that call the primitive the pack's code adds (Shared.Opcode);
// everything they do is in VMDrive.cs.

// --- the engine's sprite space -> object space transform, for the near zoom and one of the four views (see DGRP3DMesh) ---
const float ZOff = -55f;
const float DepthRange = 110.851251f / 0.39f; //sprite space distance covered by depth 1 -> 0
Matrix[] ToObject = Enumerable.Range(0, 4).Select(view =>
    Matrix.CreateTranslation(new Vector3(-72, -344, ZOff))
    * Matrix.CreateScale((1f / 128) * 1.43f)
    * Matrix.CreateScale(1, -1, 1)
    * Matrix.CreateRotationX((float)Math.PI / -6)
    * Matrix.CreateRotationY(((float)Math.PI / 4) * (1 + view * 2))).ToArray();
Matrix[] FromObject = ToObject.Select(m => Matrix.Invert(m)).ToArray();
// a near zoom canvas position (x from the middle, y from the top, see DGRP3DMesh "pos") at depth d
Vector3 Reconstruct(int view, float x, float y, float d) => Vector3.Transform(new Vector3(x + 72, y, (1f - d) * DepthRange), ToObject[view]);

// --- the models. object space: x to the east and z to the south of the middle of the tile, y up, in tiles ---
//
// Both are placeholders, one tile each, in one plain colour. The pack ships no texture and no car of its own: in
// the game every driveway tile is painted with one of the game's floor tiles (FloorDress.cs), and every car tile
// is drawn with the matching tile of one of the game's own cars (CarLook, in VMDrive.cs). These show only if that
// could not be done.

var grey = new Color(150, 150, 150);
var paving = new[] { new Box(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0.02f, 0.5f), grey) };
var block = new[] { new Box(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0.6f, 0.5f), grey) };

Color Shade(Color c, int axis) { var f = axis == 1 ? 1f : (axis == 0 ? 0.82f : 0.66f); return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f)); }

// What a ray through this canvas position shows of the model's slice on one tile: the colour and depth of the
// surface nearest the camera. tile is the tile's position from the lead tile. With whole set, the model is not
// sliced: that is what is really seen along the ray.
bool Trace(IEnumerable<Box> model, Point tile, int view, float x, float y, out Color colour, out float depth, out bool cut, bool whole = false)
{
    colour = Color.Transparent; depth = 1;
    var a = Reconstruct(view, x, y, 1f);          //the ray at depth 1 (furthest)...
    var b = Reconstruct(view, x, y, 0f) - a;      //...and how far it travels by depth 0 (nearest)
    var origin = new[] { a.X + tile.X, a.Y, a.Z + tile.Y }; var dir = new[] { b.X, b.Y, b.Z };
    float best = -1; cut = false;
    foreach (var box in model)
    {
        //this tile's slice of the box
        var lo = whole ? new[] { box.Lo.X, box.Lo.Y, box.Lo.Z } : new[] { Math.Max(box.Lo.X, tile.X - 0.5f), box.Lo.Y, Math.Max(box.Lo.Z, tile.Y - 0.5f) };
        var hi = whole ? new[] { box.Hi.X, box.Hi.Y, box.Hi.Z } : new[] { Math.Min(box.Hi.X, tile.X + 0.5f), box.Hi.Y, Math.Min(box.Hi.Z, tile.Y + 0.5f) };
        if (lo[0] >= hi[0] || lo[2] >= hi[2]) continue;
        float enter = 0, exit = 1; int exitAxis = -1; bool miss = false;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(dir[axis]) < 1e-9f) { if (origin[axis] < lo[axis] || origin[axis] > hi[axis]) { miss = true; break; } continue; }
            var t1 = (lo[axis] - origin[axis]) / dir[axis]; var t2 = (hi[axis] - origin[axis]) / dir[axis];
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > enter) enter = t1;
            if (t2 < exit) { exit = t2; exitAxis = axis; }
        }
        if (miss || enter > exit || exitAxis == -1 || exit <= best) continue;
        //travelling toward the camera, the ray leaves the box through the face the camera sees
        best = exit;
        colour = (exitAxis == 1 && box.Top != null) ? box.Top(origin[0] + dir[0] * exit, origin[2] + dir[2] * exit) : Shade(box.Colour, exitAxis);
        //...unless it leaves through the side of the slice, where the box carries on over the next tile
        cut = false;
        if (exitAxis != 1)
        {
            var at = origin[exitAxis] + dir[exitAxis] * exit;
            var (boxLo, boxHi) = exitAxis == 0 ? (box.Lo.X, box.Hi.X) : (box.Lo.Z, box.Hi.Z);
            cut = (Math.Abs(at - lo[exitAxis]) < 1e-4f && boxLo < lo[exitAxis] - 1e-4f) || (Math.Abs(at - hi[exitAxis]) < 1e-4f && boxHi > hi[exitAxis] + 1e-4f);
        }
    }
    if (best < 0) { colour = Color.Transparent; return false; }
    depth = 1f - best;
    //the next tile's sprite draws what is seen through a cut side, nearer the camera. the colour reported for it
    //here is that one, at the depth of the cut (see where the sprites are drawn for what is done with it)
    if (cut && !Trace(model, tile, view, x, y, out colour, out _, out _, true)) return false;
    return true;
}

// the quantizer normally comes from the Volcanic editor. ours is trivial: every sprite's pixels are already palette colours
var paletteOf = new Dictionary<SPR2Frame, Color[]>();
SPR2FrameEncoder.QuantizeFrame = (SPR2Frame frame, out byte[] indices) =>
{
    var palette = paletteOf[frame];
    indices = frame.PixelData.Select(p => p.A == 0 ? (byte)255 : (byte)Array.IndexOf(palette, new Color(p.R, p.G, p.B))).ToArray();
    return palette;
};

IffFile.TargetTS1 = true;
var iff = new IffFile();

// Draws one tile's slice of a model for every zoom and view, and adds its sprites and drawing group to the file.
// A model that looks the same from all four sides is drawn once and shared between the views.
void AddGraphics(string name, ushort dgrpID, ushort spriteID, IEnumerable<Box> model, Point tile, bool sameFromAllSides)
{
    var images = new List<DGRPImage>();
    var dgrp = new DGRP() { ChunkID = dgrpID, ChunkLabel = name, ChunkProcessed = true };
    var colours = model.SelectMany(b => new[] { Shade(b.Colour, 0), Shade(b.Colour, 1), Shade(b.Colour, 2) }.Concat(b.Paints ?? new Color[0])).Distinct().ToList();
    var palette = Enumerable.Repeat(new Color(255, 0, 255), 256).ToArray();
    for (int i = 0; i < colours.Count; i++) palette[i] = colours[i];

    var slice = model.Select(b => new Box(
        new Vector3(Math.Max(b.Lo.X, tile.X - 0.5f) - tile.X, b.Lo.Y, Math.Max(b.Lo.Z, tile.Y - 0.5f) - tile.Y),
        new Vector3(Math.Min(b.Hi.X, tile.X + 0.5f) - tile.X, b.Hi.Y, Math.Min(b.Hi.Z, tile.Y + 0.5f) - tile.Y), b.Colour))
        .Where(b => b.Lo.X < b.Hi.X && b.Lo.Z < b.Hi.Z).ToList();

    for (uint zoom = 3; zoom >= 1; zoom--)
    {
        var scale = 1f / (1 << (int)(3 - zoom)); //near 1, medium 1/2, far 1/4
        var id = (ushort)(spriteID + (3 - zoom));
        var palt = new PALT() { ChunkID = id, ChunkLabel = "", ChunkProcessed = true, Colors = palette };
        iff.AddChunk(palt);
        var spr = new SPR2() { ChunkID = id, ChunkLabel = name + " " + new[] { "", "far", "medium", "near" }[zoom], ChunkProcessed = true };
        iff.AddChunk(spr);
        var frames = new List<SPR2Frame>();

        for (int view = 0; view < 4; view++)
        {
            if (view > 0 && sameFromAllSides)
            {
                images.Add(new DGRPImage(dgrp) { Direction = 1u << (view * 2), Zoom = zoom, Sprites = new[] { new DGRPSprite(dgrp) { SpriteID = id, SpriteFrameIndex = 0, SpriteOffset = images[images.Count - 1].Sprites[0].SpriteOffset } } });
                continue;
            }
            // where the slice's corners land on the near canvas gives the sprite's rectangle
            var corners = (from box in slice from cx in new[] { box.Lo.X, box.Hi.X } from cy in new[] { box.Lo.Y, box.Hi.Y } from cz in new[] { box.Lo.Z, box.Hi.Z }
                           select Vector3.Transform(new Vector3(cx, cy, cz), FromObject[view])).ToList();
            int minX = (int)Math.Floor((corners.Min(c => c.X) - 72) * scale) - 1, maxX = (int)Math.Ceiling((corners.Max(c => c.X) - 72) * scale) + 1;
            int minY = (int)Math.Floor(corners.Min(c => c.Y) * scale) - 1, maxY = (int)Math.Ceiling(corners.Max(c => c.Y) * scale) + 1;
            int w = maxX - minX + 1, h = maxY - minY + 1;

            var px = new Color[w * h]; var z = new byte[w * h]; var kind = new byte[w * h]; //0 nothing, 1 the model's surface, 2 a cut side
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    //the middle of this pixel, in near zoom canvas units
                    var hit = Trace(model, tile, view, (minX + x + 0.5f) / scale - 0.5f, (minY + y + 0.5f) / scale - 0.5f, out var colour, out var depth, out var cut);
                    px[y * w + x] = hit ? colour : Color.Transparent;
                    z[y * w + x] = hit ? (byte)Math.Clamp((int)Math.Round(depth * 255), 0, 254) : (byte)255;
                    kind[y * w + x] = (byte)(hit ? (cut ? 2 : 1) : 0);
                }
            //a cut side is always covered by the next tile's sprite, so it is left as a hole: all but a one pixel
            //rim of it next to this tile's own surface. two tiles' sprites round to whole pixels differently, and
            //without the rim the joins between them show as hairline cracks
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (kind[y * w + x] != 2) continue;
                    var rim = false;
                    for (int ny = Math.Max(y - 1, 0); ny <= Math.Min(y + 1, h - 1) && !rim; ny++)
                        for (int nx = Math.Max(x - 1, 0); nx <= Math.Min(x + 1, w - 1); nx++)
                            if (kind[ny * w + nx] == 1) { rim = true; break; }
                    if (!rim) { px[y * w + x] = Color.Transparent; z[y * w + x] = 255; }
                }

            var frame = new SPR2Frame(spr);
            paletteOf[frame] = palette;
            frame.SetData(px, z, new Rectangle(0, 0, w, h));
            frame.SetPalt(palt);
            frames.Add(frame);

            // the canvas for a zoom is 136*scale wide with its baseline at 348*scale: a sprite is placed by its
            // left edge from the middle, and its bottom edge from the baseline
            var offset = new Vector2(minX, (maxY + 1) - 348 * scale);
            images.Add(new DGRPImage(dgrp)
            {
                Direction = 1u << (view * 2), Zoom = zoom,
                Sprites = new[] { new DGRPSprite(dgrp) { SpriteID = id, SpriteFrameIndex = (uint)(frames.Count - 1), SpriteOffset = offset } }
            });
        }
        spr.Frames = frames.ToArray();
    }
    dgrp.Images = images.ToArray();
    iff.AddChunk(dgrp);
}

// A script that is a single call of the pack's primitive.
BHAVInstruction Call(byte mode, byte ifTrue = 254, byte ifFalse = 255) =>
    new BHAVInstruction { Opcode = Shared.Opcode, TruePointer = ifTrue, FalsePointer = ifFalse, Operand = new byte[] { mode, 0, 0, 0, 0, 0, 0, 0 } };
void AddScript(ushort id, string name, ushort locals, params BHAVInstruction[] instructions) =>
    iff.AddChunk(new BHAV() { ChunkID = id, ChunkLabel = name, ChunkProcessed = true, Locals = locals, Instructions = instructions });

const ushort InitDriveway = 4100, InitCar = 4101, GoForADrive = 4102, IsParked = 4103;
const ushort CarMenu = 4200;

AddScript(InitDriveway, "Init - Driveway", 0, Call(Shared.ModeInitDriveway));
AddScript(InitCar, "Init - Car", 0, Call(Shared.ModeInitCar));
AddScript(IsParked, "Go for a Drive - Test", 0, Call(Shared.ModeIsParked));
AddScript(GoForADrive, "Go for a Drive", 1,
    // walk up to the tile of the car that was clicked (Go To Relative Position: anywhere near it, facing it)...
    new BHAVInstruction { Opcode = 27, TruePointer = 1, FalsePointer = 255, Operand = new byte[] { 0, 0, 0xFF, 0xFE, 0, 0, 0, 0 } },
    // ...then drive. this instruction runs every tick until the car is home again
    Call(Shared.ModeDrive));


const ushort PavingGraphic = 100, BlockGraphic = 101;
//(a square looks the same from all four sides, but each view gets its own sprite: the floor painted on it has to turn with it)
AddGraphics("Driveway tile", PavingGraphic, 100, paving, new Point(0, 0), false);
AddGraphics("Car tile", BlockGraphic, 110, block, new Point(0, 0), true);

ushort nextChunk = 16807, nextStrings = 2000, nextGroup = 1;

// An object on several tiles: one definition that is bought (the master) and one for each tile it covers. Every
// tile shares one drawing group. Not sold when it has no price.
void AddObject(string name, string description, uint guid, uint tileGuid, int width, int length, ushort graphic, ushort init, ushort menu, int price)
{
    var strings = nextStrings++; var group = nextGroup++;
    var catalog = new CTSS() { ChunkID = strings, ChunkLabel = "Catalog - " + name, ChunkProcessed = true };
    catalog.InsertString(0, new STRItem(name));
    catalog.InsertString(1, new STRItem(description));
    iff.AddChunk(catalog);
    var master = new OBJD()
    {
        ChunkID = nextChunk++, ChunkLabel = name, ChunkProcessed = true,
        GUID = guid, ObjectType = OBJDType.Normal,
        MasterID = group, SubIndex = -1,
        StackSize = 8, TreeTableID = menu, CatalogStringsID = strings,
        Price = (ushort)price,
    };
    if (price > 0)
    {
        master.FunctionFlags = 32;      //buy mode: decorative
        master.FunctionSubsort = 128;   //...under "other"
        master.RoomFlags = 0xFF;        //for every kind of room. without a room the catalog hides it
    }
    iff.AddChunk(master);
    int n = 0;
    for (int x = 0; x < width; x++)
        for (int y = 0; y < length; y++)
            iff.AddChunk(new OBJD()
            {
                ChunkID = nextChunk++, ChunkLabel = $"{name} - {x},{y}", ChunkProcessed = true,
                GUID = tileGuid + (uint)(n++), ObjectType = OBJDType.Normal,
                MasterID = group, SubIndex = (short)((x << 8) | y), //(how the engine reads a tile's place in the group: VMMultitileGroup.AddObject)
                StackSize = 8, BHAV_Init = init, TreeTableID = menu,
                BaseGraphicID = graphic, NumGraphics = 1, CatalogStringsID = strings,
            });
}

// --- the driveway ---

AddObject("Driveway", "A driveway. It joins your garden to the road, across the pavement, and can only go there: move it along the road to where you want it. Cars can only be parked on one. From the Driveways pack.",
    Shared.DrivewayGUID, Shared.DrivewayPartGUID, Shared.DrivewayWidth, Shared.DrivewayLength, PavingGraphic, InitDriveway, 0, 300);

// --- the cars: the same object several times over, each drawn in the game as a different one of the game's cars ---

foreach (var model in Shared.Cars)
    AddObject(model.Name, model.Description + " Park it on a driveway, and any adult in the house can take it for a drive, which is Fun. From the Driveways pack.",
        model.GUID, model.GUID + 1, Shared.CarWidth, Shared.CarLength, BlockGraphic, InitCar, CarMenu, model.Price);

// --- what earlier versions sold: kept, unsold, so lots that have them still load ---

iff.AddChunk(new OBJD()
{
    ChunkID = nextChunk++, ChunkLabel = "Driveway Tile (0.1)", ChunkProcessed = true,
    GUID = Shared.OldTileGUID, ObjectType = OBJDType.Normal, StackSize = 8, BHAV_Init = InitDriveway,
    BaseGraphicID = PavingGraphic, NumGraphics = 1, CatalogStringsID = 2000,
});
foreach (var old in Shared.OldDriveways)
    AddObject("Old Driveway", "A driveway from an earlier version of the Driveways pack.", old.GUID, old.GUID + 1, old.Width, old.Length, PavingGraphic, InitDriveway, 0, 0);
AddObject("Old Car", "A car from an earlier version of the Driveways pack. It cannot be driven.", Shared.OldCarGUID, Shared.OldCarGUID + 1, Shared.OldCarWidth, Shared.OldCarLength, BlockGraphic, InitCar, 0, 0);

// the cars' menu: one interaction, for adults of the household
iff.AddChunk(new TTAB()
{
    ChunkID = CarMenu, ChunkLabel = "Car", ChunkProcessed = true,
    Interactions = new[]
    {
        new TTABInteraction { ActionFunction = GoForADrive, TestFunction = IsParked, MotiveEntries = new TTABMotiveEntry[0], Flags = TTABFlags.TS1NoChild, TTAIndex = 0 }
    }
});
var menuStrings = new TTAs() { ChunkID = CarMenu, ChunkLabel = "Car", ChunkProcessed = true };
menuStrings.InsertString(0, new STRItem("Go for a Drive"));
iff.AddChunk(menuStrings);

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
var path = System.IO.Path.Combine(outDir, "Driveways.iff");
using (var stream = File.Create(path)) iff.Write(stream);
Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");

// --- read it back the way the game does, and check everything survived ---

var check = new IffFile(path);
var objects = check.List<OBJD>();
if (objects.Select(o => o.GUID).Distinct().Count() != objects.Count) throw new Exception("Two objects share a GUID");
if (objects.Select(o => o.ChunkID).Distinct().Count() != objects.Count) throw new Exception("Two objects share a chunk id");

// a multi-tile object's master and tiles, each tile's place read as the engine reads it
List<OBJD> TilesOf(uint guid, int width, int length, bool sold, ushort init, ushort graphic, ushort menu)
{
    var m = objects.Single(o => o.GUID == guid);
    var name = check.Get<CTSS>(m.CatalogStringsID).GetString(0);
    if (!m.IsMultiTile || m.SubIndex != -1 || m.FunctionFlags != (sold ? 32 : 0) || (sold && m.RoomFlags == 0) || m.TreeTableID != menu) throw new Exception(name + ": the definition did not survive");
    var tiles = objects.Where(o => o.MasterID == m.MasterID && o.SubIndex != -1).ToList();
    var at = tiles.Select(p => (X: ((ushort)p.SubIndex) >> 8, Y: ((ushort)p.SubIndex) & 0xFF)).OrderBy(p => p).ToList();
    var want = (from x in Enumerable.Range(0, width) from y in Enumerable.Range(0, length) select (X: x, Y: y)).OrderBy(p => p).ToList();
    if (!at.SequenceEqual(want) || tiles.Any(p => p.BHAV_Init != init || p.FunctionFlags != 0 || p.BaseGraphicID != graphic || p.NumGraphics != 1)) throw new Exception(name + ": the tiles did not survive");
    //(the pack's code finds a tile's object from its GUID: the master's, then one for each tile in turn)
    if (tiles.Any(p => p.GUID <= guid || p.GUID > guid + (uint)(width * length))) throw new Exception(name + ": the tiles' GUIDs do not follow the master's");
    Console.WriteLine($"Object: \"{name}\" ({m.GUID:X8}) on {width} by {length} tiles{(sold ? ", " + m.Price : ", not sold")}");
    return tiles;
}
var padParts = TilesOf(Shared.DrivewayGUID, Shared.DrivewayWidth, Shared.DrivewayLength, true, InitDriveway, PavingGraphic, 0);
var carParts = new List<OBJD>();
foreach (var model in Shared.Cars) carParts = TilesOf(model.GUID, Shared.CarWidth, Shared.CarLength, true, InitCar, BlockGraphic, CarMenu);
foreach (var old in Shared.OldDriveways) TilesOf(old.GUID, old.Width, old.Length, false, InitDriveway, PavingGraphic, 0);
TilesOf(Shared.OldCarGUID, Shared.OldCarWidth, Shared.OldCarLength, false, InitCar, BlockGraphic, 0);
var oldTile = objects.Single(o => o.GUID == Shared.OldTileGUID);
if (oldTile.BHAV_Init != InitDriveway || oldTile.FunctionFlags != 0 || oldTile.MasterID != 0) throw new Exception("The old driveway tile did not survive");

var menu = check.Get<TTAB>(CarMenu);
var label = check.Get<TTAs>(CarMenu).GetString(0);
if (menu.Interactions.Length != 1 || menu.Interactions[0].ActionFunction != GoForADrive || menu.Interactions[0].TestFunction != IsParked || label != "Go for a Drive") throw new Exception("The cars' menu did not survive");
foreach (var (id, mode) in new[] { (InitDriveway, Shared.ModeInitDriveway), (InitCar, Shared.ModeInitCar), (IsParked, Shared.ModeIsParked) })
{
    var script = check.Get<BHAV>(id);
    if (script.Instructions.Length != 1 || script.Instructions[0].Opcode != Shared.Opcode || script.Instructions[0].Operand[0] != mode || script.Instructions[0].TruePointer != 254) throw new Exception("Script " + id + " did not survive");
}
var drive = check.Get<BHAV>(GoForADrive);
if (drive.Locals != 1 || drive.Instructions.Length != 2 || drive.Instructions[0].Opcode != 27 || drive.Instructions[1].Opcode != Shared.Opcode || drive.Instructions[1].Operand[0] != Shared.ModeDrive) throw new Exception("The drive script did not survive");
Console.WriteLine($"Menu: \"{label}\" -> script {GoForADrive} ({drive.Instructions.Length} instructions), test {IsParked}");

// rebuild 3D points from every near sprite exactly as DGRP3DMesh does, and measure how far they are from the model
float Distance(IEnumerable<Box> model, Vector3 p) => model.Min(b =>
{
    var outX = Math.Max(Math.Max(b.Lo.X - p.X, p.X - b.Hi.X), 0); var outY = Math.Max(Math.Max(b.Lo.Y - p.Y, p.Y - b.Hi.Y), 0); var outZ = Math.Max(Math.Max(b.Lo.Z - p.Z, p.Z - b.Hi.Z), 0);
    var outside = (float)Math.Sqrt(outX * outX + outY * outY + outZ * outZ);
    if (outside > 0) return outside;
    return Math.Min(Math.Min(Math.Min(p.X - b.Lo.X, b.Hi.X - p.X), Math.Min(p.Z - b.Lo.Z, b.Hi.Z - p.Z)), Math.Min(p.Y - b.Lo.Y, b.Hi.Y - p.Y));
});
void CheckGraphics(ushort graphic, IEnumerable<Box> model, string name, int least)
{
    var g = check.Get<DGRP>(graphic);
    if (g.Images.Length != 12) throw new Exception("Expected 12 images for " + name);
    float worst = 0; int solid = 0;
    for (uint view = 0; view < 4; view++)
    {
        for (uint zoom = 1; zoom <= 3; zoom++)
            if (g.GetImage(1, zoom, view) == null) throw new Exception($"{name}: no image for view {view} zoom {zoom}");
        var near = g.GetImage(1, 3, view).Sprites[0];
        var f = check.Get<SPR2>((ushort)near.SpriteID).Frames[near.SpriteFrameIndex];
        f.DecodeIfRequired(true);
        var start = near.SpriteOffset + new Vector2(72, 348 - f.Height);
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var d = f.ZBufferData[y * f.Width + x] / 255f;
                var p = f.PixelData[y * f.Width + x];
                if (d >= 0.999f) { if (p.A != 0) throw new Exception("Solid pixel without depth"); continue; }
                if (p.A != 255) throw new Exception("Depth without a solid pixel");
                solid++;
                worst = Math.Max(worst, Distance(model, Vector3.Transform(new Vector3(start.X + x, start.Y + y, (1f - d) * DepthRange), ToObject[view])));
            }
    }
    Console.WriteLine($"{name}: {solid} solid pixels over 4 views, each within {worst:0.000} tiles of the model when rebuilt in 3D");
    if (solid < least || worst > 0.03f) throw new Exception(name + ": the sprites do not describe the model");
}
CheckGraphics(PavingGraphic, paving, "Driveway tile", 12000);
CheckGraphics(BlockGraphic, block, "Car tile", 20000);

// --- painting the driveway with a floor tile, as the game will with one of its own: here with a made-up tile whose
// four quarters are four colours, so what lands where can be checked ---
IffFile dressedFile;
{
    var quarters = new[] { new Color(200, 60, 60), new Color(60, 180, 60), new Color(60, 60, 200), new Color(220, 200, 60) };
    var fake = new FloorDress.Tile { Width = 128, Height = 64, Pixels = new Color[128 * 64], Palette = Enumerable.Repeat(new Color(255, 0, 255), 256).ToArray() };
    for (int i = 0; i < 4; i++) fake.Palette[i] = quarters[i];
    //the diamond: its points are the middles of the sprite's sides. the quarter is by which side of each axis of the tile a pixel is on
    for (int y = 0; y < 64; y++)
        for (int x = 0; x < 128; x++)
        {
            float dx = (x + 0.5f - 64) / 64f, dy = (y + 0.5f - 32) / 32f;
            if (Math.Abs(dx) + Math.Abs(dy) > 1) continue;
            fake.Pixels[y * 128 + x] = quarters[(dx + dy > 0 ? 1 : 0) + (dy - dx > 0 ? 2 : 0)];
        }
    dressedFile = FloorDress.Build(check, new[] { PavingGraphic }, fake);
    int painted = 0, wrong = 0; var seen = new HashSet<Color>();
    var plain = check.Get<DGRP>(PavingGraphic);
    var dressed = dressedFile.Get<DGRP>((ushort)(PavingGraphic + FloorDress.IDOffset));
    if (dressed == null || dressed.Images.Length != plain.Images.Length) throw new Exception("The dressed drawing group is missing");
    for (uint view = 0; view < 4; view++)
        for (uint zoom = 1; zoom <= 3; zoom++)
        {
            var a = plain.GetImage(1, zoom, view).Sprites[0]; var b = dressed.GetImage(1, zoom, view).Sprites[0];
            var fa = check.Get<SPR2>((ushort)a.SpriteID).Frames[a.SpriteFrameIndex]; var fb = dressedFile.Get<SPR2>((ushort)b.SpriteID).Frames[b.SpriteFrameIndex];
            fa.DecodeIfRequired(true); fb.DecodeIfRequired(true);
            if (fa.Width != fb.Width || fa.Height != fb.Height || a.SpriteOffset != b.SpriteOffset) throw new Exception("A dressed sprite is not the shape of its original");
            for (int i = 0; i < fa.PixelData.Length; i++)
            {
                if (fa.ZBufferData[i] != fb.ZBufferData[i] || (fa.PixelData[i].A == 0) != (fb.PixelData[i].A == 0)) throw new Exception("A dressed sprite's depth or outline changed");
                if (fb.PixelData[i].A == 0) continue;
                painted++; seen.Add(new Color(fb.PixelData[i].R, fb.PixelData[i].G, fb.PixelData[i].B));
                if (zoom != 3) continue;
                //which quarter of its tile this pixel shows, worked out again from its depth
                int x = i % fa.Width, y = i / fa.Width;
                var start = a.SpriteOffset + new Vector2(72, 348 - fa.Height);
                var pt = Vector3.Transform(new Vector3(start.X + x, start.Y + y, (1f - fa.ZBufferData[i] / 255f) * DepthRange), ToObject[view]);
                if (Math.Abs(pt.X) < 0.06f || Math.Abs(pt.Z) < 0.06f || Math.Abs(pt.X) > 0.44f || Math.Abs(pt.Z) > 0.44f) continue; //(too near a border to call)
                //on the tile, x runs down-right and z down-left on the canvas at view 0
                var p0 = Vector3.Transform(new Vector3(pt.X, 0, pt.Z), Matrix.Invert(ToObject[0])) - Vector3.Transform(Vector3.Zero, Matrix.Invert(ToObject[0]));
                var want = quarters[(p0.X / 64f + p0.Y / 32f > 0 ? 1 : 0) + (p0.Y / 32f - p0.X / 64f > 0 ? 2 : 0)];
                if (new Color(fb.PixelData[i].R, fb.PixelData[i].G, fb.PixelData[i].B) != want) wrong++;
            }
        }
    Console.WriteLine($"Dressed with a floor tile: {painted} pixels painted in {seen.Count} colours, {wrong} of the near ones not from the right quarter of the tile");
    if (painted < 12000 || seen.Count != 4 || !seen.All(c => quarters.Contains(c)) || wrong > painted / 500) throw new Exception("The driveway was not painted with the floor tile");
}
Console.WriteLine("OK");

// optional: a preview folder gets driveway.ppm, a whole driveway put together as the game would draw it at the
// near zoom (painted with the made-up tile), beside its depth
if (args.Length > 1)
{
    Directory.CreateDirectory(args[1]);
    const int W = 900, H = 560, OX = 450, OY = 120;
    uint view = 1;
    var colour = new Color[W * H]; var depth = new float[W * H];
    Array.Fill(depth, float.NegativeInfinity);
    foreach (var obj in padParts)
    {
        var tile = new Point(((ushort)obj.SubIndex) >> 8, ((ushort)obj.SubIndex) & 0xFF);
        //where this tile's canvas sits: the screen position of its middle, from the lead tile's
        var shift = Vector3.Transform(new Vector3(tile.X, 0, tile.Y), FromObject[view]) - Vector3.Transform(Vector3.Zero, FromObject[view]);
        var near = dressedFile.Get<DGRP>((ushort)(obj.BaseGraphicID + FloorDress.IDOffset)).GetImage(1, 3, view).Sprites[0];
        var f = dressedFile.Get<SPR2>((ushort)near.SpriteID).Frames[near.SpriteFrameIndex];
        f.DecodeIfRequired(true);
        var start = near.SpriteOffset + new Vector2(0, 348 - f.Height);
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var p = f.PixelData[y * f.Width + x];
                if (p.A == 0) continue;
                int sx = OX + (int)Math.Round(start.X + x + shift.X), sy = OY + (int)Math.Round(start.Y + y + shift.Y) - 180;
                if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                //further along the view's depth axis is nearer the camera
                var z = (1f - f.ZBufferData[y * f.Width + x] / 255f) * DepthRange + shift.Z;
                if (z > depth[sy * W + sx]) { depth[sy * W + sx] = z; colour[sy * W + sx] = p; }
            }
    }
    float lo = depth.Where(d => !float.IsNegativeInfinity(d)).DefaultIfEmpty(0).Min(), hi = depth.Where(d => !float.IsNegativeInfinity(d)).DefaultIfEmpty(1).Max();
    using var ppm = new StreamWriter(System.IO.Path.Combine(args[1], "driveway.ppm"));
    ppm.WriteLine($"P3 {W * 2} {H} 255");
    for (int y = 0; y < H; y++)
    {
        for (int x = 0; x < W; x++) { var p = colour[y * W + x]; ppm.Write(float.IsNegativeInfinity(depth[y * W + x]) ? "70 130 70 " : $"{p.R} {p.G} {p.B} "); }
        for (int x = 0; x < W; x++) { var d = depth[y * W + x]; var v = float.IsNegativeInfinity(d) ? 0 : (int)(40 + 215 * (d - lo) / Math.Max(hi - lo, 1e-3f)); ppm.Write($"{v} {v} {v} "); }
        ppm.WriteLine();
    }
    Console.WriteLine("Preview written to " + args[1]);
}

// a box of one colour
record struct Box(Vector3 Lo, Vector3 Hi, Color Colour, Func<float, float, Color> Top = null, Color[] Paints = null);
