/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls.Catalog;
using FSO.Content.Interfaces;
using FSO.LotView.RC;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model.Routing;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace FoundationsPack
{
    /// <summary>
    /// Foundations and decks, as in The Sims 2, added to the engine from a pack. A work in progress.
    ///
    /// The same feature exists as an engine change (FreeSO branch simitone/foundations: about 2,300 lines in 24 files).
    /// Its own code is here almost as it was: FoundationMap, VMFoundationTools, FoundationLook, Modelled3DFoundation,
    /// FoundationStepsObjects and UIFoundationTools. This file is where the engine change edited existing engine code.
    ///
    /// Done so far (stage 1: the foundation exists, and everything on it stands on it):
    ///   1. Where foundations are kept, and previews of them (Lots.cs).
    ///   2. The build commands.
    ///   3. The lot's tick: showing real or previewed foundations, and raising what stands on them when they change.
    ///   4. Saving.
    ///   5. Rules: steps count as a slope, no pools in a foundation, nothing straddles an edge.
    ///   6. Routing: Sims can't cross a foundation's edge except by its steps.
    ///   7. Heights: the ground, and so floors, objects and Sims, are as high as the foundation under them.
    ///   8. Walls and roofs stand on the foundation.
    ///   9. The build catalog.
    ///
    /// What a foundation looks like (stage 2, parts 10 to 14) is in FoundationsDrawing.cs.
    /// </summary>
    public partial class FoundationsPack : ISimitonePack
    {
        private static string PackId;

        public void OnLoad(Pack pack)
        {
            PackId = pack.Id;
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(FoundationsPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] args)
        {
            var method = args.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
            return method ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static MemberInfo WallLevel;

        private static void Apply(Harmony harmony)
        {
            FoundationsCode.Find();
            WallLevel = (MemberInfo)AccessTools.Field(typeof(WallComponent), "Level") ?? AccessTools.Property(typeof(WallComponent), "Level");
            if (WallLevel == null) throw new Exception("WallComponent.Level was not found: this pack was made for a different version of Simitone");

            // 1
            harmony.Patch(Find(typeof(VMArchitectureTerrain), "EnterVis"), postfix: Mine(nameof(EnterVis)));
            harmony.Patch(Find(typeof(VMArchitectureTerrain), "ExitVis"), postfix: Mine(nameof(ExitVis)));
            // 2
            harmony.Patch(Find(typeof(VMArchitecture), "RunCommands"), postfix: Mine(nameof(RunFoundationCommands)));
            // 3 and 4
            harmony.Patch(Find(typeof(VMArchitecture), "Tick"), prefix: Mine(nameof(BeforeTick)), postfix: Mine(nameof(AfterTick)));
            // 5
            harmony.Patch(Find(typeof(VMArchitecture), "GetTerrainSloped"), postfix: Mine(nameof(StepsAreSloped)));
            harmony.Patch(Find(typeof(VMArchitecture), "SetFloor"), prefix: Mine(nameof(NoPoolsInFoundations)));
            harmony.Patch(Find(typeof(VMMultitileGroup), "ChangePosition"), prefix: Mine(nameof(NoStraddling)));
            // 6
            harmony.Patch(Find(typeof(VMRoomMap), "GenerateMap"), postfix: Mine(nameof(AddFoundationEdges)));
            // 7
            harmony.Patch(Find(typeof(Blueprint), "GetAltitude"), postfix: Mine(nameof(RaiseTile)));
            harmony.Patch(Find(typeof(Blueprint), "InterpAltitude"), postfix: Mine(nameof(RaisePoint)));
            harmony.Patch(Find(typeof(TerrainComponent), "RegenTerrain"), postfix: Mine(nameof(RaiseGround)));
            harmony.Patch(AccessTools.PropertySetter(typeof(EntityComponent), "Position") ?? throw new Exception("EntityComponent.Position was not found"), prefix: Mine(nameof(RememberPosition)));
            // 8
            harmony.Patch(Find(typeof(WallComponent), "Draw"), prefix: Mine(nameof(EnterWallDraw)), finalizer: Mine(nameof(LeaveWallDraw)), transpiler: Mine(nameof(WallHeights)));
            var locals = typeof(WallComponentRC).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(x => x.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)).ToList();
            var lineGeom = locals.Where(x => x.ReturnType == typeof(void) && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] {
                typeof(int), typeof(int), typeof(Vector2), typeof(Vector2), typeof(ushort), typeof(ushort), typeof(int), typeof(float), typeof(float), typeof(float) })).ToList();
            if (lineGeom.Count != 1) throw new Exception("The 3D wall code is not as expected (" + lineGeom.Count + "): this pack was made for a different version of Simitone");
            harmony.Patch(lineGeom[0], prefix: Mine(nameof(BeforeWallLine)), finalizer: Mine(nameof(AfterWallLine)));
            harmony.Patch(Find(typeof(WallComponentRC), "Generate"), prefix: Mine(nameof(EnterWallGenerate)));
            harmony.Patch(Find(typeof(RoofComponent), "ToWorldPos"), postfix: Mine(nameof(RaiseRoof)));
            // 9
            harmony.Patch(Find(typeof(UIBuyBrowsePanel), "AddTerrainTools"), postfix: Mine(nameof(AddFoundationTools)));
            // 10 to 14: what a foundation looks like (FoundationsDrawing.cs)
            ApplyDrawing(harmony);
        }

        // ---- 1. previews: keep the preview copy in step with the engine's ----

        public static void EnterVis(VMArchitectureTerrain __instance) { Lots.For(__instance).EnterVis(); }
        public static void ExitVis(VMArchitectureTerrain __instance) { Lots.For(__instance).ExitVis(); }

        // ---- 2. the build commands: the engine has skipped them, as types it doesn't know. run them and add their cost ----

        public static void RunFoundationCommands(VMArchitecture __instance, List<VMArchitectureCommand> commands, ref int __result)
        {
            if (__instance.Terrain == null) return;
            foreach (var com in commands)
            {
                switch (com.Type)
                {
                    case VMFoundationTools.RectCommand:
                        //style: height (0 removes) in the low 12 bits, the kind of foundation above; pattern: the sides' covering
                        VMFoundationTools.PlaceFoundation(__instance, new Rectangle(com.x, com.y, com.x2, com.y2), (short)(com.style & 0xFFF), com.pattern,
                            (FoundationKind)(com.style >> 12), out var fCost);
                        __result += fCost;
                        break;
                    case VMFoundationTools.StepsCommand:
                        //pattern: direction the steps rise toward (255 picks the foundation next to the tile), style: 1 places, 0 removes
                        //x2: the staircase (catalog GUID) whose bottom piece the steps are, 0 for plain steps
                        VMFoundationTools.PlaceFoundationSteps(__instance, new Point(com.x, com.y), com.pattern, com.style != 0, (uint)com.x2, out var sCost);
                        __result += sCost;
                        break;
                    case VMFoundationTools.PaintCommand:
                        //a wall covering on a foundation's sides, priced as on walls (its own brick is free)
                        FSO.Content.WallReference covering = null;
                        FSO.Content.Content.Get().WorldWalls.Entries.TryGetValue(com.pattern, out covering);
                        if (covering == null && com.pattern != 0) break;
                        var painted = VMFoundationTools.PaintSides(__instance, new Point(com.x, com.y), com.x2, com.pattern);
                        if (painted > 0 && covering != null) __result += covering.Price * painted;
                        break;
                }
            }
        }

        // ---- 3. the lot's tick, and 4. saving ----

        /// <summary>Foundations read from a lot's file, waiting for the lot to be there to have them.</summary>
        private static ConditionalWeakTable<VM, byte[]> Loaded = new ConditionalWeakTable<VM, byte[]>();
        /// <summary>Architectures this pack has seen tick. A new one is a lot just loaded, or handed over in memory.</summary>
        private static ConditionalWeakTable<VMArchitecture, object> Seen = new ConditionalWeakTable<VMArchitecture, object>();

        public void OnLotSaved(VM vm, string lotPath)
        {
            var arch = vm.Context?.Architecture;
            if (arch?.Terrain == null) return;
            LotPackData.Write(PackId, lotPath, Save(Lots.For(arch.Terrain).Foundations));
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            Loaded.Remove(vm);
            if (lotPath == null) return; //not from a file: the lot is the one that was handed over, with its foundations
            var saved = LotPackData.Read(PackId, lotPath);
            if (saved != null) Loaded.Add(vm, saved);
        }

        /// <summary>
        /// Before the engine's tick. The engine change did this inside the tick, between its own steps; before it is near
        /// enough, since the tick goes on to rebuild rooms and redraw from what is set here.
        /// </summary>
        public static void BeforeTick(VMArchitecture __instance, out bool __state)
        {
            __state = FoundationsCode.RedrawOf(__instance);
            var terrain = __instance.Terrain;
            if (terrain == null) return;
            var lot = Lots.For(terrain);
            var vm = __instance.Context?.VM;

            if (vm != null && Loaded.TryGetValue(vm, out var saved))
            {
                Loaded.Remove(vm);
                var map = Load(saved);
                if (map != null && map.Width == terrain.Width && map.Height == terrain.Height)
                {
                    lot.Foundations.CopyFrom(map);
                    lot.Dirty = true;
                }
            }
            if (!Seen.TryGetValue(__instance, out _))
            {
                Seen.Add(__instance, null);
                if (lot.Foundations.Any) lot.Dirty = true; //(as the engine change did on loading)
            }

            var bp = VM.UseWorld ? FoundationsCode.WorldOf(__instance) : null;
            if (lot.Dirty)
            {
                lot.Dirty = false;
                lot.Foundations.Refresh();
                FoundationsCode.FloorsDirtyOf(__instance) = true; //rooms and routing: the foundations' edges block Sims
                __instance.SignalRedraw();
                __state = true;
                if (bp != null)
                {
                    Lots.Shown(bp).Foundations = lot.Foundations;
                    SyncSteps(bp);
                    if (bp.Terrain != null) bp.Terrain.TerrainDirty = true;
                    //everything standing on the changed tiles goes up or down with them
                    if (vm != null) foreach (var ent in vm.Entities) RefreshAltitude(ent.WorldUI);
                }
            }
            else if (bp != null && !lot.ShowingVis && Lots.Shown(bp).Foundations != lot.Foundations)
            {
                Lots.Shown(bp).Foundations = lot.Foundations;
                SyncSteps(bp);
            }
            if (__state) lot.VisChanged = false; //(the tick is about to run the build tool's preview)
        }

        /// <summary>After the engine's tick: a build tool previewing foundations shows its copy; otherwise the real ones.</summary>
        public static void AfterTick(VMArchitecture __instance, bool __state)
        {
            if (!__state || !VM.UseWorld || __instance.Terrain == null) return;
            var lot = Lots.For(__instance.Terrain);
            if (!lot.VisChanged && !lot.ShowingVis) return;
            var bp = FoundationsCode.WorldOf(__instance);
            if (bp == null) return;
            Lots.Shown(bp).Foundations = lot.VisChanged ? lot.VisFoundations : lot.Foundations;
            SyncSteps(bp);
            if (bp.Terrain != null) bp.Terrain.TerrainDirty = true;
            lot.ShowingVis = lot.VisChanged;
        }

        // saving: this pack's own data for a lot (Simitone's LotPackData), in the format the engine change wrote into saves

        private const byte Revision = 4;

        private static byte[] Save(FoundationMap map)
        {
            if (map == null || !map.Any) return null;
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write(Revision);
                writer.Write(map.Width);
                writer.Write(map.Height);
                foreach (var v in map.Raise) writer.Write(v);
                writer.Write(map.Steps);
                foreach (var v in map.Sides) writer.Write(v);
                writer.Write(map.Kind);
                foreach (var v in map.StepStyle) writer.Write(v);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static FoundationMap Load(byte[] saved)
        {
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(saved)))
                {
                    if (reader.ReadByte() != Revision) return null;
                    int width = reader.ReadInt32(), height = reader.ReadInt32();
                    if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return null;
                    var map = new FoundationMap(width, height);
                    for (int i = 0; i < map.Raise.Length; i++) map.Raise[i] = reader.ReadInt16();
                    map.Steps = reader.ReadBytes(width * height);
                    for (int i = 0; i < map.Sides.Length; i++) map.Sides[i] = reader.ReadUInt16();
                    map.Kind = reader.ReadBytes(width * height);
                    for (int i = 0; i < map.StepStyle.Length; i++) map.StepStyle[i] = reader.ReadUInt32();
                    if (map.Steps.Length != width * height || map.Kind.Length != width * height) return null;
                    map.Refresh();
                    return map;
                }
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        // ---- 5. rules ----

        /// <summary>Foundation steps count as a slope: no walls, and only objects that allow slopes.</summary>
        public static void StepsAreSloped(VMArchitecture __instance, short tileX, short tileY, ref bool __result)
        {
            if (__result) return;
            var map = Lots.Of(__instance);
            if (map == null || !map.Any) return;
            var off = __instance.GetOffset(tileX, tileY);
            if (off < map.Steps.Length && map.Steps[off] != 0) __result = true;
        }

        /// <summary>No pools in a foundation, or on its steps.</summary>
        public static bool NoPoolsInFoundations(VMArchitecture __instance, short tileX, short tileY, sbyte level, FloorTile floor, bool force, ref bool __result)
        {
            if (force || level != 1 || floor.Pattern <= 65533) return true;
            var map = Lots.Of(__instance);
            if (map == null || !map.Any) return true;
            var off = __instance.GetOffset(tileX, tileY);
            if (off >= map.Raise.Length || (map.Raise[off] == 0 && map.Steps[off] == 0)) return true;
            __result = false;
            return false;
        }

        /// <summary>
        /// A multitile object can't straddle a foundation's edge (or its steps): all of it stands at one height.
        /// The engine change checked this part way through ChangePosition, with values that method had worked out.
        /// Here they are worked out the same way first.
        /// </summary>
        public static bool NoStraddling(VMMultitileGroup __instance, LotTilePos pos, Direction direction, VMContext context, ref VMPlacementResult __result)
        {
            if (pos == LotTilePos.OUT_OF_WORLD || __instance.Objects.Count < 2) return true;
            if (pos.Level > context.Architecture.Stories) return true;
            var map = Lots.Of(context.Architecture);
            if (map == null || !map.Any) return true;

            int dir = FSO.Common.Utils.DirectionUtils.Log2Int((uint)direction);
            var rotMat = Matrix.CreateRotationZ((float)(dir * Math.PI / 4.0));
            var bOff = __instance.Offsets[__instance.Objects.IndexOf(__instance.BaseObject)];
            var leadOff = new Vector3(bOff.x, bOff.y, 0);
            int height = int.MinValue;
            for (int i = 0; i < __instance.Objects.Count; i++)
            {
                var off = Vector3.Transform(new Vector3(__instance.Offsets[i].x, __instance.Offsets[i].y, 0) - leadOff, rotMat);
                var tx = (int)Math.Round(pos.x + off.X) >> 4;
                var ty = (int)Math.Round(pos.y + off.Y) >> 4;
                var here = (map.StepDirection(tx, ty) >= 0) ? -1 : map.RaiseOf(tx, ty);
                if (height == int.MinValue) height = here;
                else if (here != height)
                {
                    __result = new VMPlacementResult(VMPlacementError.CantPlaceOnSlope);
                    return false;
                }
            }
            return true;
        }

        // ---- 6. routing ----

        /// <summary>
        /// A foundation's edges, as obstacles like walls: Sims can't step off a foundation, or onto one, except by its
        /// steps. The engine change added them to each room as the room was found. Here they are added to the finished
        /// rooms, and each changed room's routing is rebuilt from its obstacles the way the engine built it.
        /// </summary>
        public static void AddFoundationEdges(VMRoomMap __instance, int width, int height, List<VMRoom> rooms, sbyte floor, VMContext context)
        {
            if (floor != 0 || rooms == null || __instance.Map == null) return;
            var map = Lots.Of(context?.Architecture);
            if (map == null || !map.Any) return;
            var changed = new HashSet<int>();
            for (int y = 0; y < Math.Min(height, map.Height); y++)
            {
                for (int x = 0; x < Math.Min(width, map.Width); x++)
                {
                    bool e0 = map.EdgeBlocked(x, y, 0), e1 = map.EdgeBlocked(x, y, 1), e2 = map.EdgeBlocked(x, y, 2), e3 = map.EdgeBlocked(x, y, 3);
                    if (!(e0 || e1 || e2 || e3)) continue;
                    var room = (int)(__instance.Map[x + y * width] & 0xFFFF);
                    if (room >= rooms.Count || rooms[room].WallObs == null) continue;
                    var obstacles = rooms[room].WallObs;
                    int ox = x << 4, oy = y << 4;
                    //in 1/16 tile units, 3 either side of the edge
                    if (e0) obstacles.Add(new VMObstacle(ox - 3, oy - 3, ox + 19, oy + 3)); //-y edge
                    if (e1) obstacles.Add(new VMObstacle(ox + 13, oy - 3, ox + 19, oy + 19)); //+x edge
                    if (e2) obstacles.Add(new VMObstacle(ox - 3, oy + 13, ox + 19, oy + 19)); //+y edge
                    if (e3) obstacles.Add(new VMObstacle(ox - 3, oy - 3, ox + 3, oy + 19)); //-x edge
                    changed.Add(room);
                }
            }
            foreach (var index in changed)
            {
                var room = rooms[index];
                room.RoutingObstacles = VMObstacleSet.RoughBalanced(room.WallObs.Concat(room.RoomObs ?? new List<VMObstacle>()).ToList());
                rooms[index] = room;
            }
        }

        // ---- 7. heights ----

        /// <summary>The height of a tile's centre: the ground, and the tile's foundation if it has one.</summary>
        public static void RaiseTile(Blueprint __instance, int x, int y, ref float __result)
        {
            if (Lots.Unraised || x <= 0 || y <= 0) return;
            var map = Lots.Of(__instance);
            if (map == null || !map.Any) return;
            __result += map.RaiseOf(x % __instance.Width, y % __instance.Height) * __instance.TerrainFactor;
        }

        [ThreadStatic] private static bool InWallLine;
        [ThreadStatic] private static float WallLineRaise;

        /// <summary>The height under a point: the ground, and the foundation there (partway up, on steps).</summary>
        public static void RaisePoint(Blueprint __instance, Vector3 Position, ref float __result)
        {
            if (Lots.Unraised) return;
            if (InWallLine) { __result += WallLineRaise; return; } //(8: a wall's own height, worked out for its whole line)
            var map = Lots.Of(__instance);
            if (map == null || !map.Any) return;
            __result += map.RaiseAt(Position.X, Position.Y) * __instance.TerrainFactor;
            if (Positioning != null) __result += WallHungRaise(__instance, map, Position); //(14)
        }

        private static AccessTools.FieldRef<TerrainComponent, Rectangle> TerrainSize;
        private static AccessTools.FieldRef<TerrainComponent, Blueprint> TerrainBlueprint;

        /// <summary>
        /// The engine has just made the ground's vertices. A foundation raises its whole tile (the floors drawn on it,
        /// on every level, use these vertices). The engine change raised them as they were made. From here: read the
        /// vertices back, raise the foundations' tiles, and put them back.
        /// </summary>
        public static void RaiseGround(TerrainComponent __instance)
        {
            if (TerrainSize == null)
            {
                TerrainSize = FoundationsCode.Field<TerrainComponent, Rectangle>("Size");
                TerrainBlueprint = FoundationsCode.Field<TerrainComponent, Blueprint>("Bp");
            }
            var bp = TerrainBlueprint(__instance);
            var map = Lots.Of(bp);
            var vb = __instance.VertexBuffer;
            var shown = Lots.Shown(bp);
            shown.Ground?.Dispose();
            shown.Ground = null;
            if (map == null || !map.Any || vb == null || vb.IsDisposed) return;
            var quads = TerrainSize(__instance).Width;
            if (vb.VertexCount != quads * quads * 4) return; //not the ground this was written for
            var geom = new TerrainParallaxVertex[vb.VertexCount];
            vb.GetData(geom);
            // 11: the same ground, unraised, to draw under column decks
            for (int i = 0; i < map.Raise.Length; i++)
            {
                if (!UnderOpenDeck(map, i)) continue;
                shown.Ground = new VertexBuffer(vb.GraphicsDevice, typeof(TerrainParallaxVertex), geom.Length, BufferUsage.None);
                shown.Ground.SetData(geom);
                break;
            }
            bool any = false;
            for (var y = 0; y < quads; y++)
            {
                for (var x = 0; x < quads; x++)
                {
                    var raise = map.RaiseOf(x, y) * bp.TerrainFactor * 3;
                    if (raise == 0) continue;
                    any = true;
                    for (int v = 0; v < 4; v++) geom[(y * quads + x) * 4 + v].Position.Y += raise;
                }
            }
            if (any) vb.SetData(geom);
        }

        /// <summary>The position each thing in the world was last given, before the ground's height was added.</summary>
        private static ConditionalWeakTable<EntityComponent, StrongBox<Vector3>> Unraised = new ConditionalWeakTable<EntityComponent, StrongBox<Vector3>>();

        public static void RememberPosition(EntityComponent __instance, Vector3 value)
        {
            Positioning = __instance; //(14: until the position has been set)
            Unraised.GetOrCreateValue(__instance).Value = value;
        }

        /// <summary>Works out a thing's height again, after a foundation under it changed.</summary>
        private static void RefreshAltitude(EntityComponent comp)
        {
            if (comp == null || comp.Container != null) return;
            if (comp is AvatarComponent avatar) avatar.Position = avatar.StoredPosition;
            else if (Unraised.TryGetValue(comp, out var position)) comp.Position = position.Value;
        }

        // ---- 8. walls and roofs ----

        [ThreadStatic] private static WallComponent DrawingWalls;

        public static void EnterWallDraw(WallComponent __instance) { DrawingWalls = __instance; }
        public static Exception LeaveWallDraw(Exception __exception) { DrawingWalls = null; return __exception; }

        /// <summary>
        /// The 2D walls ask the blueprint for a tile's height in three places: once for a tile's walls, and twice for the
        /// junction at a tile's corner. The engine change gave each its own answer. From outside, those three calls are
        /// redirected, in the compiled code of the method, to this pack's versions.
        /// </summary>
        public static IEnumerable<CodeInstruction> WallHeights(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getAltitude = AccessTools.Method(typeof(Blueprint), "GetAltitude");
            var wall = AccessTools.Method(typeof(FoundationsPack), nameof(WallAltitude));
            var corner = AccessTools.Method(typeof(FoundationsPack), nameof(CornerAltitude));
            int count = 0;
            foreach (var instruction in code)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo method && method == getAltitude)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = (count == 0) ? wall : corner;
                    count++;
                }
            }
            if (count != 3) throw new Exception("WallComponent.Draw asks for a tile's height in " + count + " places, not 3: this pack was made for a different version of Simitone");
            return code;
        }

        private static float Ground(Blueprint bp, int x, int y)
        {
            var was = Lots.Unraised;
            Lots.Unraised = true;
            try { return bp.GetAltitude(x, y); }
            finally { Lots.Unraised = was; }
        }

        /// <summary>
        /// The height a tile's walls stand at: the ground under the tile, raised to the higher of the foundations either
        /// side of each wall (a wall on a foundation's edge stands on the foundation, whichever tile it belongs to).
        /// </summary>
        public static float WallAltitude(Blueprint bp, int x, int y)
        {
            var ground = Ground(bp, x, y);
            var map = Lots.Of(bp);
            if (map == null || !map.Any || x <= 0 || y <= 0 || DrawingWalls == null) return ground;
            var levelValue = (WallLevel is FieldInfo field) ? field.GetValue(DrawingWalls) : ((PropertyInfo)WallLevel).GetValue(DrawingWalls);
            var segments = bp.GetWall((short)x, (short)y, Convert.ToSByte(levelValue)).Segments;
            return ground + map.WallRaise(x % bp.Width, y % bp.Height, segments) * bp.TerrainFactor;
        }

        /// <summary>The height at a tile corner: the ground, raised to the highest foundation touching the corner.</summary>
        public static float CornerAltitude(Blueprint bp, int x, int y)
        {
            var ground = Ground(bp, x, y);
            var map = Lots.Of(bp);
            if (map == null || !map.Any || x <= 0 || y <= 0) return ground;
            return ground + map.RaiseNear(x, y, 0.25f) * bp.TerrainFactor;
        }

        [ThreadStatic] private static WallComponentRC GeneratingWalls;

        public static void EnterWallGenerate(WallComponentRC __instance) { GeneratingWalls = __instance; }

        /// <summary>
        /// Before a strip of 3D wall is built. A wall on a foundation's edge stands on the foundation: both its faces
        /// (either side of the edge) and its end caps take the highest foundation beside the line. The engine change
        /// worked that out inside the strip's code; here it is worked out before it, and given to the two height
        /// lookups the strip makes (RaisePoint, above).
        /// </summary>
        public static void BeforeWallLine(int __0, int __1, Vector2 __2, Vector2 __3)
        {
            var bp = GeneratingWalls?.blueprint;
            var mid = (__2 + __3) / 2 + new Vector2(__0, __1);
            WallLineRaise = (bp == null) ? 0 : Lots.FoundationNear(bp, mid.X, mid.Y, 0.2f);
            InWallLine = true;
        }

        public static Exception AfterWallLine(Exception __exception) { InWallLine = false; return __exception; }

        /// <summary>On a foundation the roof rises with the walls under it, out to its overhang.</summary>
        public static void RaiseRoof(RoofComponent __instance, int x, int y, ref Vector3 __result)
        {
            var bp = __instance.blueprint;
            var map = Lots.Of(bp);
            if (map == null || !map.Any) return;
            __result.Y += (Lots.FoundationNear(bp, x / 16f, y / 16f, 0.75f) - Lots.FoundationAt(bp, x / 16, y / 16)) * 3;
        }

        // ---- 9. the build catalog ----

        public static void AddFoundationTools(UIBuyBrowsePanel __instance)
        {
            var foundationRes = new UICatalogFoundationResProvider();
            var stepsRes = new UICatalogFoundationStepsResProvider();
            // steps: one entry per staircase, its bottom piece; plain steps if the game has none to take
            var staircases = UICatalogFoundationStepsResProvider.Staircases();
            foreach (var stair in staircases)
            {
                __instance.FullCategory.Insert(0, new UICatalogElement
                {
                    Item = new ObjectCatalogItem() { Name = "Foundation Steps", Category = 18, Price = (uint)VMFoundationTools.StepsPrice(stair.GUID) },
                    Special = new UISpecialCatalogElement
                    {
                        Control = typeof(UIFoundationStepsPlacer),
                        ResID = stair.GUID,
                        Res = stepsRes,
                        Parameters = new List<int> { (int)stair.GUID }
                    }
                });
            }
            if (staircases.Count == 0)
            {
                __instance.FullCategory.Insert(0, new UICatalogElement
                {
                    Item = new ObjectCatalogItem() { Name = "Foundation Steps", Category = 18, Price = (uint)VMFoundationTools.FoundationStepsPrice },
                    Special = new UISpecialCatalogElement
                    {
                        Control = typeof(UIFoundationStepsPlacer),
                        ResID = UICatalogFoundationResProvider.StepsID,
                        Res = foundationRes,
                        Parameters = new List<int> { }
                    }
                });
            }
            foreach (var kind in new[] { FoundationKind.ColumnDeck, FoundationKind.LatticedDeck, FoundationKind.Foundation })
            {
                var id = (kind == FoundationKind.LatticedDeck) ? UICatalogFoundationResProvider.LatticedDeckID
                    : (kind == FoundationKind.ColumnDeck) ? UICatalogFoundationResProvider.ColumnDeckID
                    : UICatalogFoundationResProvider.FoundationID;
                __instance.FullCategory.Insert(0, new UICatalogElement
                {
                    Item = new ObjectCatalogItem() { Name = foundationRes.GetName(id), Category = 18, Price = (uint)VMFoundationTools.TilePrice(kind) },
                    Special = new UISpecialCatalogElement
                    {
                        Control = typeof(UIFoundationPlacer),
                        ResID = id,
                        Res = foundationRes,
                        Parameters = new List<int> { (int)kind }
                    }
                });
            }
        }
    }
}
