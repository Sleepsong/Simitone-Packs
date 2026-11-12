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

// Writes Driveways.iff: the pack's two objects, a driveway piece and a car.
// Usage: MakeDriveways <output folder> [preview folder]   (then it reads the file back and checks it)
//
// Like the Test Cube sample, the objects have no art files. Each is a handful of boxes, and its sprites are drawn
// here by tracing rays against them with the same camera maths the engine uses to turn a sprite and its depth
// buffer back into a 3D model (DGRP3DMesh). The car is not the same from every side, so it is drawn once for each
// of the four views; and it covers several tiles, so each tile gets the slice of the car that stands on it, which
// is how The Sims draws every object bigger than a tile.
//
// Neither object has scripts of its own to speak of. Their scripts are an instruction or two that call the
// primitive the pack's code adds (Shared.Opcode); everything they do is in VMDrive.cs.

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

// --- the models. object space: x to the east and z to the south of the middle of the lead tile, y up, in tiles ---

var concrete = new Color(178, 178, 172);
var driveway = new[] { new Box(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0.02f, 0.5f), concrete) };
// the driveway of versions 0.2 to 0.4: one plain slab
var oldDriveway = new[] { new Box(new Vector3(-0.5f, 0, -0.5f), new Vector3(Shared.OldDrivewayWidth - 0.5f, 0.02f, Shared.OldDrivewayLength - 0.5f), concrete) };

// the driveway, after The Sims 2's: a pad of pale concrete cast in panels, with joints between them, which crosses
// the pavement and ends in a ridged apron at the kerb. it faces north: the apron is at high z
var pale = new Color(206, 192, 156);
var joint = new Color(170, 154, 120);
var ridge = new Color(188, 173, 138);
float padLeft = -0.5f, padRight = Shared.DrivewayWidth - 0.5f, padTop = -0.5f, padEnd = Shared.DrivewayLength - 0.5f;
float crossing = padEnd - Shared.DrivewayCrossing;  //where the pad meets the pavement
float apron = padEnd - 1;                           //where the apron starts
Color Paving(float x, float z)
{
    const float half = 0.035f; //half a joint's width
    //a joint all round the edge, across where the pad meets the pavement and the apron, and dividing the pad into four panels
    if (x < padLeft + 2 * half || x > padRight - 2 * half || z < padTop + 2 * half || z > padEnd - 2 * half) return joint;
    if (Math.Abs(z - crossing) < half || Math.Abs(z - apron) < half) return joint;
    if (z < crossing && (Math.Abs(x - (padLeft + padRight) / 2) < half || Math.Abs(z - (padTop + crossing) / 2) < half)) return joint;
    //the apron: ridges across it, inside a plain border
    if (z > apron + 0.18f && z < padEnd - 0.18f && x > padLeft + 0.3f && x < padRight - 0.3f && ((z - apron) % 0.16f) < 0.07f) return ridge;
    return pale;
}
var drivewayPad = new[] { new Box(new Vector3(padLeft, 0, padTop), new Vector3(padRight, 0.02f, padEnd), pale, Paving, new[] { pale, joint, ridge }) };

// the car faces north: its nose is at low z. it stands on Shared.CarWidth x Shared.CarLength tiles
var paint = new Color(44, 96, 178);
var glass = new Color(58, 78, 98);
var tyre = new Color(38, 38, 38);
var lamp = new Color(250, 240, 180);
var brake = new Color(214, 44, 44);
var chrome = new Color(150, 152, 156);
float carW = Shared.CarWidth, carL = Shared.CarLength;
float left = -0.5f + 0.12f, right = carW - 0.5f - 0.12f;      //the body's sides
float nose = -0.5f + 0.10f, tail = carL - 0.5f - 0.10f;       //and its ends
var car = new List<Box>
{
    new Box(new Vector3(left, 0.22f, nose), new Vector3(right, 0.78f, tail), paint),                               //body
    new Box(new Vector3(left + 0.10f, 0.78f, nose + 1.15f), new Vector3(right - 0.10f, 1.26f, tail - 0.75f), glass), //windows
    new Box(new Vector3(left + 0.08f, 1.26f, nose + 1.12f), new Vector3(right - 0.08f, 1.34f, tail - 0.72f), paint), //roof
    new Box(new Vector3(left - 0.02f, 0.22f, nose - 0.04f), new Vector3(right + 0.02f, 0.38f, nose), chrome),        //bumpers
    new Box(new Vector3(left - 0.02f, 0.22f, tail), new Vector3(right + 0.02f, 0.38f, tail + 0.04f), chrome),
    new Box(new Vector3(left + 0.10f, 0.50f, nose - 0.02f), new Vector3(left + 0.50f, 0.68f, nose), lamp),           //headlights
    new Box(new Vector3(right - 0.50f, 0.50f, nose - 0.02f), new Vector3(right - 0.10f, 0.68f, nose), lamp),
    new Box(new Vector3(left + 0.10f, 0.52f, tail), new Vector3(left + 0.45f, 0.68f, tail + 0.02f), brake),          //tail lights
    new Box(new Vector3(right - 0.45f, 0.52f, tail), new Vector3(right - 0.10f, 0.68f, tail + 0.02f), brake),
};
foreach (var z in new[] { nose + 0.45f, tail - 1.15f })                                                              //wheels
{
    car.Add(new Box(new Vector3(left - 0.05f, 0, z), new Vector3(left + 0.20f, 0.46f, z + 0.70f), tyre));
    car.Add(new Box(new Vector3(right - 0.20f, 0, z), new Vector3(right + 0.05f, 0.46f, z + 0.70f), tyre));
}

// faces are shaded by which way they point, the same in every view (the light does not turn with the camera)
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

// --- the driveway tile: one tile of paving. versions 0.1 and 0.2 sold these; now there is only the whole driveway ---

iff.AddChunk(new OBJD()
{
    ChunkID = 16807, ChunkLabel = "Driveway Tile", ChunkProcessed = true,
    GUID = Shared.OldTileGUID,
    ObjectType = OBJDType.Normal,
    StackSize = 8,
    BHAV_Init = InitDriveway,
    BaseGraphicID = 100, NumGraphics = 1,
    CatalogStringsID = 2000,
    Price = 15,
    //(not in the catalog any more: FunctionFlags is left at 0. it is kept so lots that have some still load them)
});
var drivewayStrings = new CTSS() { ChunkID = 2000, ChunkLabel = "Catalog - Driveway Tile", ChunkProcessed = true };
drivewayStrings.InsertString(0, new STRItem("Driveway Tile"));
drivewayStrings.InsertString(1, new STRItem("One square of driveway, from an earlier version of the Driveways pack."));
iff.AddChunk(drivewayStrings);
AddGraphics("Driveway Tile", 100, 100, driveway, new Point(0, 0), true);


// --- the driveway of versions 0.2 to 0.4, two tiles by four: kept, out of the catalog, so lots that have one still load it ---

const ushort DrivewayGroup = 2;
iff.AddChunk(new OBJD()
{
    ChunkID = 16820, ChunkLabel = "Driveway", ChunkProcessed = true,
    GUID = Shared.OldDrivewayGUID,
    ObjectType = OBJDType.Normal,
    MasterID = DrivewayGroup, SubIndex = -1,
    StackSize = 8,
    CatalogStringsID = 2002,
    Price = 100,
    BuildModeType = 0,       //(with FunctionFlags left at 0, not in the catalog)
});
var drivewayLargeStrings = new CTSS() { ChunkID = 2002, ChunkLabel = "Catalog - Driveway", ChunkProcessed = true };
drivewayLargeStrings.InsertString(0, new STRItem("Small Driveway"));
drivewayLargeStrings.InsertString(1, new STRItem("A small driveway, from an earlier version of the Driveways pack."));
iff.AddChunk(drivewayLargeStrings);

int drivewayPart = 0;
for (int x = 0; x < Shared.OldDrivewayWidth; x++)
    for (int y = 0; y < Shared.OldDrivewayLength; y++)
    {
        var graphic = (ushort)(130 + drivewayPart);
        iff.AddChunk(new OBJD()
        {
            ChunkID = (ushort)(16821 + drivewayPart), ChunkLabel = $"Driveway - {x},{y}", ChunkProcessed = true,
            GUID = Shared.OldDrivewayPartGUID + (uint)drivewayPart,
            ObjectType = OBJDType.Normal,
            MasterID = DrivewayGroup, SubIndex = (short)((x << 8) | y),
            StackSize = 8,
            BHAV_Init = InitDriveway,
            BaseGraphicID = graphic, NumGraphics = 1,
            CatalogStringsID = 2002,
        });
        AddGraphics($"Driveway {x},{y}", graphic, (ushort)(300 + drivewayPart * 3), oldDriveway, new Point(x, y), false);
        drivewayPart++;
    }

// --- the driveway. like the car, one definition that is bought (the master) and one for each tile it covers ---

const ushort PadGroup = 3;
iff.AddChunk(new OBJD()
{
    ChunkID = 16840, ChunkLabel = "Driveway", ChunkProcessed = true,
    GUID = Shared.DrivewayGUID,
    ObjectType = OBJDType.Normal,
    MasterID = PadGroup, SubIndex = -1,
    StackSize = 8,
    CatalogStringsID = 2003,
    Price = 250,
    FunctionFlags = 32,      //buy mode: decorative
    FunctionSubsort = 128,   //...under "other"
    RoomFlags = 0xFF,        //for every kind of room. without a room the catalog hides it
});
var padStrings = new CTSS() { ChunkID = 2003, ChunkLabel = "Catalog - Driveway", ChunkProcessed = true };
padStrings.InsertString(0, new STRItem("Driveway"));
padStrings.InsertString(1, new STRItem("A concrete driveway. It joins your garden to the road, across the pavement, and can only go there: move it along the road to where you want it. Park a car on it. From the Driveways pack."));
iff.AddChunk(padStrings);

int padPart = 0;
for (int x = 0; x < Shared.DrivewayWidth; x++)
    for (int y = 0; y < Shared.DrivewayLength; y++)
    {
        var graphic = (ushort)(160 + padPart);
        iff.AddChunk(new OBJD()
        {
            ChunkID = (ushort)(16841 + padPart), ChunkLabel = $"Driveway - {x},{y}", ChunkProcessed = true,
            GUID = Shared.DrivewayPartGUID + (uint)padPart,
            ObjectType = OBJDType.Normal,
            MasterID = PadGroup, SubIndex = (short)((x << 8) | y),
            StackSize = 8,
            BHAV_Init = InitDriveway,
            BaseGraphicID = graphic, NumGraphics = 1,
            CatalogStringsID = 2003,
        });
        AddGraphics($"Driveway {x},{y}", graphic, (ushort)(400 + padPart * 3), drivewayPad, new Point(x, y), false);
        padPart++;
    }

// --- the car: one definition that is bought (the master), and one for each tile it stands on ---

const ushort CarGroup = 1;
iff.AddChunk(new OBJD()
{
    ChunkID = 16808, ChunkLabel = "Car", ChunkProcessed = true,
    GUID = Shared.CarGUID,
    ObjectType = OBJDType.Normal,
    MasterID = CarGroup, SubIndex = -1,
    StackSize = 8,
    TreeTableID = CarMenu,
    CatalogStringsID = 2001,
    Price = 1800,
    FunctionFlags = 32, FunctionSubsort = 128, RoomFlags = 0xFF,
});
var carStrings = new CTSS() { ChunkID = 2001, ChunkLabel = "Catalog - Car", ChunkProcessed = true };
carStrings.InsertString(0, new STRItem("Runabout"));
carStrings.InsertString(1, new STRItem("A small car of your own. Park it on a driveway with its front or back towards the road, and any adult in the house can take it for a drive, which is Fun. From the Driveways pack."));
iff.AddChunk(carStrings);

int partNumber = 0;
for (int x = 0; x < Shared.CarWidth; x++)
    for (int y = 0; y < Shared.CarLength; y++)
    {
        var graphic = (ushort)(110 + partNumber);
        iff.AddChunk(new OBJD()
        {
            ChunkID = (ushort)(16809 + partNumber), ChunkLabel = $"Car - {x},{y}", ChunkProcessed = true,
            GUID = Shared.CarPartGUID + (uint)partNumber,
            ObjectType = OBJDType.Normal,
            MasterID = CarGroup, SubIndex = (short)((x << 8) | y), //(how the engine reads a tile's place in the group: VMMultitileGroup.AddObject)
            StackSize = 8,
            BHAV_Init = InitCar,
            TreeTableID = CarMenu,
            BaseGraphicID = graphic, NumGraphics = 1,
            CatalogStringsID = 2001,
        });
        AddGraphics($"Car {x},{y}", graphic, (ushort)(200 + partNumber * 3), car, new Point(x, y), false);
        partNumber++;
    }

// the car's menu: one interaction, for adults of the household
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
if (objects.Count != 4 + Shared.CarWidth * Shared.CarLength + Shared.DrivewayWidth * Shared.DrivewayLength + Shared.OldDrivewayWidth * Shared.OldDrivewayLength) throw new Exception("Expected a driveway tile, a driveway and a car, and their tiles");

var slab = objects.Single(o => o.GUID == Shared.OldTileGUID);
if (slab.BHAV_Init != InitDriveway || slab.FunctionFlags != 0 || slab.NumGraphics != 1 || slab.MasterID != 0) throw new Exception("The driveway's definition did not survive");

// a multi-tile object's master and tiles, each tile's place read as the engine reads it
List<OBJD> TilesOf(uint guid, int width, int length, bool sold, string name)
{
    var m = objects.Single(o => o.GUID == guid);
    if (!m.IsMultiTile || m.SubIndex != -1 || m.FunctionFlags != (sold ? 32 : 0)) throw new Exception(name + ": the definition did not survive");
    var tiles = objects.Where(o => o.MasterID == m.MasterID && o.SubIndex != -1).ToList();
    var at = tiles.Select(p => (X: ((ushort)p.SubIndex) >> 8, Y: ((ushort)p.SubIndex) & 0xFF)).OrderBy(p => p).ToList();
    var want = (from x in Enumerable.Range(0, width) from y in Enumerable.Range(0, length) select (X: x, Y: y)).OrderBy(p => p).ToList();
    if (!at.SequenceEqual(want) || tiles.Any(p => p.BHAV_Init != InitDriveway || p.FunctionFlags != 0)) throw new Exception(name + ": the tiles did not survive");
    Console.WriteLine($"Objects: \"{check.Get<CTSS>(m.CatalogStringsID).GetString(0)}\" ({m.GUID:X8}) on {tiles.Count} tiles{(sold ? "" : ", not sold")}");
    return tiles;
}
var slabParts = TilesOf(Shared.OldDrivewayGUID, Shared.OldDrivewayWidth, Shared.OldDrivewayLength, false, "The small driveway");
var padParts = TilesOf(Shared.DrivewayGUID, Shared.DrivewayWidth, Shared.DrivewayLength, true, "The driveway");

var master = objects.Single(o => o.GUID == Shared.CarGUID);
if (!master.IsMultiTile || master.SubIndex != -1 || master.FunctionFlags != 32 || master.TreeTableID != CarMenu) throw new Exception("The car's definition did not survive");
var parts = objects.Where(o => o.MasterID == master.MasterID && o.SubIndex != -1).ToList();
if (parts.Count != Shared.CarWidth * Shared.CarLength) throw new Exception("The car is missing tiles");
//each tile's place, read as the engine reads it
var places = parts.Select(p => (X: ((ushort)p.SubIndex) >> 8, Y: ((ushort)p.SubIndex) & 0xFF)).OrderBy(p => p).ToList();
var expected = (from x in Enumerable.Range(0, Shared.CarWidth) from y in Enumerable.Range(0, Shared.CarLength) select (X: x, Y: y)).OrderBy(p => p).ToList();
if (!places.SequenceEqual(expected) || parts.Any(p => p.BHAV_Init != InitCar || p.FunctionFlags != 0)) throw new Exception("The car's tiles did not survive");
Console.WriteLine($"Objects: \"{check.Get<CTSS>(slab.CatalogStringsID).GetString(0)}\" ({slab.GUID:X8}), \"{check.Get<CTSS>(master.CatalogStringsID).GetString(0)}\" ({master.GUID:X8}) on {parts.Count} tiles");

var menu = check.Get<TTAB>(CarMenu);
var label = check.Get<TTAs>(CarMenu).GetString(0);
if (menu.Interactions.Length != 1 || menu.Interactions[0].ActionFunction != GoForADrive || menu.Interactions[0].TestFunction != IsParked || label != "Go for a Drive") throw new Exception("The car's menu did not survive");
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

bool Inside(IEnumerable<Box> model, Vector3 p) => model.Any(b => p.X >= b.Lo.X - 0.01f && p.X <= b.Hi.X + 0.01f && p.Y >= b.Lo.Y - 0.01f && p.Y <= b.Hi.Y + 0.01f && p.Z >= b.Lo.Z - 0.01f && p.Z <= b.Hi.Z + 0.01f);

void CheckGraphics(OBJD obj, IEnumerable<Box> model, Point tile, ref float worst, ref int solid, ref int rim)
{
    var g = check.Get<DGRP>(obj.BaseGraphicID);
    if (g.Images.Length != 12) throw new Exception("Expected 12 images for " + obj.ChunkLabel);
    for (uint view = 0; view < 4; view++)
    {
        for (uint zoom = 1; zoom <= 3; zoom++)
            if (g.GetImage(1, zoom, view) == null) throw new Exception($"{obj.ChunkLabel}: no image for view {view} zoom {zoom}");
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
                var pt = Vector3.Transform(new Vector3(start.X + x, start.Y + y, (1f - d) * DepthRange), ToObject[view]);
                //(the rim of a cut side is inside the model, on the edge of its tile)
                var onCut = Math.Abs(Math.Abs(pt.X) - 0.5f) < 0.01f || Math.Abs(Math.Abs(pt.Z) - 0.5f) < 0.01f;
                var off = Distance(model, pt + new Vector3(tile.X, 0, tile.Y));
                if (onCut && Inside(model, pt + new Vector3(tile.X, 0, tile.Y))) { rim++; continue; }
                worst = Math.Max(worst, off);
            }
    }
}

float slabWorst = 0, carWorst = 0; int slabSolid = 0, carSolid = 0, slabRim = 0, carRim = 0;
CheckGraphics(slab, driveway, new Point(0, 0), ref slabWorst, ref slabSolid, ref slabRim);
float largeWorst = 0; int largeSolid = 0, largeRim = 0;
foreach (var part in slabParts) CheckGraphics(part, oldDriveway, new Point(((ushort)part.SubIndex) >> 8, ((ushort)part.SubIndex) & 0xFF), ref largeWorst, ref largeSolid, ref largeRim);
Console.WriteLine($"Small Driveway: {largeSolid} solid pixels over {slabParts.Count} tiles and 4 views, each within {largeWorst:0.000} tiles of the model when rebuilt in 3D");
if (largeSolid < 30000 || largeWorst > 0.03f) throw new Exception("The small driveway's sprites do not describe it");
float padWorst = 0; int padSolid = 0, padRim = 0;
foreach (var part in padParts) CheckGraphics(part, drivewayPad, new Point(((ushort)part.SubIndex) >> 8, ((ushort)part.SubIndex) & 0xFF), ref padWorst, ref padSolid, ref padRim);
Console.WriteLine($"Driveway: {padSolid} solid pixels over {padParts.Count} tiles and 4 views, each within {padWorst:0.000} tiles of the model when rebuilt in 3D");
if (padSolid < 100000 || padWorst > 0.03f) throw new Exception("The driveway's sprites do not describe it");
foreach (var part in parts) CheckGraphics(part, car, new Point(((ushort)part.SubIndex) >> 8, ((ushort)part.SubIndex) & 0xFF), ref carWorst, ref carSolid, ref carRim);
Console.WriteLine($"Driveway Tile: {slabSolid} solid pixels over 4 views, each within {slabWorst:0.000} tiles of the model when rebuilt in 3D");
Console.WriteLine($"Car: {carSolid} solid pixels over {parts.Count} tiles and 4 views, each within {carWorst:0.000} tiles of the model when rebuilt in 3D ({carRim} of them the rims along the joins between tiles)");
if (slabSolid < 4000 || carSolid < 40000 || slabWorst > 0.03f || carWorst > 0.03f || carRim > carSolid / 20) throw new Exception("The sprites do not describe the models");
Console.WriteLine("OK");

// optional: a preview folder gets car-view0.ppm .. car-view3.ppm, the car's tiles put together as the game would
// draw them at the near zoom, each beside its depth; and driveway.ppm
if (args.Length > 1)
{
    Directory.CreateDirectory(args[1]);
    void Preview(string file, IEnumerable<OBJD> objs, uint view)
    {
        const int W = 900, H = 560, OX = 450, OY = 120;
        var colour = new Color[W * H]; var depth = new float[W * H];
        Array.Fill(depth, float.NegativeInfinity);
        foreach (var obj in objs)
        {
            var tile = obj.SubIndex == -1 || obj.MasterID == 0 ? new Point(0, 0) : new Point(((ushort)obj.SubIndex) >> 8, ((ushort)obj.SubIndex) & 0xFF);
            //where this tile's canvas sits: the screen position of its middle, from the lead tile's
            var shift = Vector3.Transform(new Vector3(tile.X, 0, tile.Y), FromObject[view]) - Vector3.Transform(Vector3.Zero, FromObject[view]);
            var near = check.Get<DGRP>(obj.BaseGraphicID).GetImage(1, 3, view).Sprites[0];
            var f = check.Get<SPR2>((ushort)near.SpriteID).Frames[near.SpriteFrameIndex];
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
        using var ppm = new StreamWriter(System.IO.Path.Combine(args[1], file));
        ppm.WriteLine($"P3 {W * 2} {H} 255");
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++) { var p = colour[y * W + x]; ppm.Write(float.IsNegativeInfinity(depth[y * W + x]) ? "70 130 70 " : $"{p.R} {p.G} {p.B} "); }
            for (int x = 0; x < W; x++) { var d = depth[y * W + x]; var v = float.IsNegativeInfinity(d) ? 0 : (int)(40 + 215 * (d - lo) / Math.Max(hi - lo, 1e-3f)); ppm.Write($"{v} {v} {v} "); }
            ppm.WriteLine();
        }
    }
    for (uint view = 0; view < 4; view++) Preview($"car-view{view}.ppm", parts, view);
    Preview("driveway-tile.ppm", new[] { slab }, 0);
    for (uint view = 0; view < 4; view++) Preview($"driveway-view{view}.ppm", padParts, view);
    Console.WriteLine("Previews written to " + args[1]);
}

// a box of one colour; or, with Top, one whose top face is painted: Top gives the colour at (x, z), and Paints lists the colours it uses
record struct Box(Vector3 Lo, Vector3 Hi, Color Colour, Func<float, float, Color> Top = null, Color[] Paints = null);
