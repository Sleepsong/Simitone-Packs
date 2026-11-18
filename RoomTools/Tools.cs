/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.HIT;
using FSO.LotView;
using FSO.SimAntics;
using FSO.UI.Panels.LotControls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoomTools
{
    /// <summary>Things all the Rooms tab's tools need: the cursor on the lot, a tooltip, and messages.</summary>
    public static class ToolHelp
    {
        /// <summary>Where the cursor is on a floor of the lot, in tiles (with the part of a tile).</summary>
        public static Vector2 Cursor(World world, Point mouse, int level) => world.EstTileAtPosWithScroll(new Vector2(mouse.X, mouse.Y), (sbyte)level);

        public static void Tooltip(UpdateState state, Point mouse, string text, bool bad)
        {
            state.UIState.TooltipProperties.Show = true;
            state.UIState.TooltipProperties.Color = bad ? Color.DarkRed : Color.Black;
            state.UIState.TooltipProperties.Opacity = 1;
            state.UIState.TooltipProperties.Position = new Vector2(mouse.X, mouse.Y);
            state.UIState.Tooltip = text;
            state.UIState.TooltipProperties.UpdateDead = false;
        }

        public static string Money(int amount) => (amount < 0 ? "-§" : "§") + Math.Abs(amount).ToString("##,#0");

        public static void Say(string title, string message)
        {
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions
            {
                Title = title,
                Message = message,
                Buttons = UIAlertButton.Ok((b) => alert.Close())
            });
            UIScreen.GlobalShowDialog(alert, true);
        }

        public static void Ask(string title, string message, string yes, Action onYes, string other = null, Action onOther = null)
        {
            UIMobileAlert alert = null;
            var buttons = new List<UIAlertButton> { new UIAlertButton(UIAlertButtonType.Yes, (b) => { alert.Close(); onYes(); }, yes) };
            if (other != null) buttons.Add(new UIAlertButton(UIAlertButtonType.No, (b) => { alert.Close(); onOther(); }, other));
            buttons.Add(new UIAlertButton(UIAlertButtonType.Cancel, (b) => alert.Close()));
            alert = new UIMobileAlert(new UIAlertOptions { Title = title, Message = message, Buttons = buttons.ToArray() });
            UIScreen.GlobalShowDialog(alert, true);
        }

        public static void AskName(string title, string message, string suggested, Action<string> onOk)
        {
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions
            {
                Title = title,
                Message = message,
                TextEntry = true,
                MaxChars = 48,
                Buttons = UIAlertButton.OkCancel(
                    (b) => { var name = alert.ResponseText?.Trim(); alert.Close(); onOk(string.IsNullOrEmpty(name) ? suggested : name); },
                    (b) => alert.Close())
            });
            alert.ResponseText = suggested;
            UIScreen.GlobalShowDialog(alert, true);
        }

        /// <summary>The catalogue panel showing the Rooms tab, if it is.</summary>
        public static Simitone.Client.UI.Panels.LiveSubpanels.UIBuyBrowsePanel Panel =>
            RoomToolsPack.Screen?.Frontend?.MainPanel?.SubPanel as Simitone.Client.UI.Panels.LiveSubpanels.UIBuyBrowsePanel;

        /// <summary>List the library again on the Rooms tab (something was saved or deleted).</summary>
        public static void RefreshTab()
        {
            var panel = Panel;
            if (panel == null || panel.Category != Catalog.CategoryId) return;
            panel.Deselect();
            panel.Reset();
        }

        /// <summary>A blueprint read from the library, with the objects that aren't installed here taken out (and named).</summary>
        public static Blueprint LoadReady(Library.Entry entry, out List<string> missing)
        {
            var b = Library.Load(entry);
            missing = new List<string>();
            var map = new int[b.Objects.Count];
            var kept = new List<PlanObject>();
            for (int i = 0; i < b.Objects.Count; i++)
            {
                var o = b.Objects[i];
                map[i] = -1;
                if (o.Container >= 0 && map[o.Container] < 0) continue; //with what it was in
                if (Content.Get().WorldObjects.Get(o.GUID) == null) { missing.Add(o.Name); continue; }
                if (o.Container >= 0) o.Container = map[o.Container];
                map[i] = kept.Count;
                kept.Add(o);
            }
            b.Objects = kept;
            return b;
        }
    }

    /// <summary>
    /// Move Room, Copy Room, Save Room, and a saved room put down from the library: one tool, which picks a room up (or
    /// starts with one) and carries it until it is put down. [ and ] turn it a quarter turn.
    /// </summary>
    public class UIRoomTool : UICustomLotControl
    {
        private readonly VM vm;
        private readonly World World;
        private readonly ILotControl Parent;
        private readonly Catalog.Kind Kind;

        //carrying
        private Blueprint Source;
        private RoomPick Moving;
        private int GrabX, GrabY, Turns;
        private Blueprint TurnedCache;
        private int TurnedFor = -1;
        private Ghosts Ghosts;
        private (int ox, int oy, int level, int turns) Last = (int.MinValue, 0, 0, 0);
        private string Problem;
        private int Cost;
        private int OX, OY, Level;
        private string Note;

        //hovering
        private (int x, int y, int half, int level) HoverAt = (int.MinValue, 0, 0, 0);
        private RoomPick Hovered;
        private int HoverAge;

        private KeyboardState LastKeys;
        private string Flash;
        private int FlashFrames;

        public UIRoomTool(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            this.vm = vm;
            World = parent.World;
            Parent = parent;
            Kind = (Catalog.Kind)((parameters != null && parameters.Count > 0) ? parameters[0] : 0);
            if (Kind == Catalog.Kind.PlaceRoom)
            {
                var index = (parameters.Count > 1) ? parameters[1] : -1;
                if (index < 0 || index >= Catalog.Listed.Count) return;
                try
                {
                    var b = ToolHelp.LoadReady(Catalog.Listed[index], out var missing);
                    if (missing.Count > 0) Note = "Not installed, left out: " + string.Join(", ", missing.Distinct().Take(4)) + (missing.Distinct().Count() > 4 ? "..." : "");
                    Carry(b, null, b.Plan.Width / 2, b.Plan.Height / 2);
                }
                catch (Exception e)
                {
                    Flash = "This room can't be read: " + e.Message;
                    FlashFrames = 600;
                }
            }
        }

        private bool Carrying => Source != null;
        private string Verb => Kind == Catalog.Kind.MoveRoom ? "move" : Kind == Catalog.Kind.CopyRoom ? "copy" : "save";

        private void Carry(Blueprint b, RoomPick moving, int grabX, int grabY)
        {
            Source = b;
            Moving = moving;
            GrabX = grabX; GrabY = grabY;
            Turns = 0;
            TurnedFor = -1;
            Last = (int.MinValue, 0, 0, 0);
            Ghosts?.Remove(vm);
            Ghosts = Ghosts.Make(vm, b);
            if (moving != null) Hiding.Hide(moving.Entities); //lifted from where it was: shown where it would go instead
        }

        private void PutDown()
        {
            Hiding.ShowAll();
            Preview.Hide(vm.Context.Architecture);
            Highlight.Clear();
            Ghosts?.Remove(vm);
            Ghosts = null;
            Source = null;
            Moving = null;
        }

        private Blueprint Turned()
        {
            if (TurnedFor != Turns) { TurnedCache = Source.Turned(Turns); TurnedFor = Turns; }
            return TurnedCache;
        }

        public override void MouseDown(UpdateState state)
        {
            if (!Carrying)
            {
                if (Kind == Catalog.Kind.PlaceRoom) return;
                var level = World.State.Level;
                var t = ToolHelp.Cursor(World, MousePosition, level);
                var forMove = Kind == Catalog.Kind.MoveRoom;
                var pick = Building.Pick(vm, t.X, t.Y, level, forMove, true);
                if (pick.Room == null || (forMove && pick.Problem != null))
                {
                    HITVM.Get().PlaySoundEvent(UISounds.Error);
                    Flash = pick.Problem; FlashFrames = 150;
                    return;
                }
                if (Kind == Catalog.Kind.SaveRoom) { Save(pick); return; }
                HITVM.Get().PlaySoundEvent(UISounds.ObjectRotate);
                if (pick.LeftOut.Count > 0) Note = "Not copied (players can't buy them): " + string.Join(", ", pick.LeftOut.Distinct().Take(4)) + (pick.LeftOut.Distinct().Count() > 4 ? "..." : "");
                Carry(pick.Blueprint, forMove ? pick : null, (int)Math.Floor(t.X) - pick.Room.MinX, (int)Math.Floor(t.Y) - pick.Room.MinY);
                return;
            }

            if (Last.turns != Turns || Last.ox == int.MinValue) return; //turned since it was last checked: checked again next frame
            if (Problem != null)
            {
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                return;
            }
            var b = Turned();
            int ox = OX, oy = OY, lvl = Level, cost = Cost, turns = Turns;
            var moving = Moving;
            Hiding.ShowAll();
            Preview.Hide(vm.Context.Architecture);
            Highlight.Clear();
            Ghosts?.Remove(vm);
            Ghosts = null;
            var what = Kind == Catalog.Kind.MoveRoom ? "moving a room" : Kind == Catalog.Kind.CopyRoom ? "copying a room" : "building a saved room";
            var step = Undo.Begin(vm, what);
            string error;
            try { error = Building.Place(vm, b, ox, oy, lvl, moving, turns, cost); }
            catch (Exception e)
            {
                RoomToolsPack.Log("Putting a room down failed: " + e);
                error = "Something went wrong: " + e.Message;
            }
            if (error != null)
            {
                Undo.Failed(vm, step); //(this tool is put down and picked up again)
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                ToolHelp.Say("Room Tools", "The room couldn't be put down there, so nothing has changed.\n\n" + error);
                return;
            }
            Undo.Done(step);
            HITVM.Get().PlaySoundEvent(moving != null ? UISounds.ObjectMovePlace : UISounds.BuyPlace);
            if (moving != null) PutDown(); //the room is where it was put: pick another
            else
            {
                Last = (int.MinValue, 0, 0, 0); //another copy can go down
                Ghosts = Ghosts.Make(vm, Source);
            }
        }

        public override void MouseUp(UpdateState state) { }

        private void Save(RoomPick pick)
        {
            var b = pick.Blueprint;
            foreach (var o in b.Objects) o.Live = null;
            var suggested = Library.FreshName("Room", false);
            var leftOut = pick.LeftOut.Distinct().ToList();
            ToolHelp.AskName("Save Room", "What should this room be called in your library?" + (leftOut.Count > 0 ? "\n\nLeft out, as players can't buy them: " + string.Join(", ", leftOut.Take(6)) + "." : ""), suggested, (name) =>
            {
                try
                {
                    b.Name = name;
                    b.Saved = DateTime.UtcNow;
                    b.Thumbnail = Icons.DrawPlan(b);
                    var path = Library.Save(b);
                    HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
                    ToolHelp.Say("Saved", "\"" + name + "\" is in your library, under Rooms in build mode. Its file is " + path + ": copy it to another player's Library/Rooms folder to give it to them.");
                    ToolHelp.RefreshTab();
                }
                catch (Exception e)
                {
                    RoomToolsPack.Log("Saving a room failed: " + e);
                    ToolHelp.Say("Room Tools", "The room couldn't be saved: " + e.Message);
                }
            });
        }

        private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && !LastKeys.IsKeyDown(key);

        public override void Update(UpdateState state, bool scrolled)
        {
            var keys = state.KeyboardState;
            var level = World.State.Level;
            var t = ToolHelp.Cursor(World, MousePosition, level);
            if (FlashFrames > 0) FlashFrames--;

            if (Carrying)
            {
                if (Pressed(keys, Keys.OemCloseBrackets)) { Turns = (Turns + 1) % 4; HITVM.Get().PlaySoundEvent(UISounds.ObjectRotate); }
                if (Pressed(keys, Keys.OemOpenBrackets)) { Turns = (Turns + 3) % 4; HITVM.Get().PlaySoundEvent(UISounds.ObjectRotate); }
            }
            if (Kind == Catalog.Kind.PlaceRoom && Pressed(keys, Keys.Delete)) AskDelete();
            LastKeys = keys;

            if (!Carrying)
            {
                if (Kind == Catalog.Kind.PlaceRoom)
                {
                    ToolHelp.Tooltip(state, MousePosition, Flash ?? "This room can't be placed.", true);
                    return;
                }
                Hover(state, t, level);
                return;
            }

            var b = Turned();
            var (gx, gy) = TurnTile(GrabX, GrabY, Source.Plan.Width, Source.Plan.Height, Turns);
            int ox = (int)Math.Floor(t.X) - gx, oy = (int)Math.Floor(t.Y) - gy;
            if (Last != (ox, oy, level, Turns))
            {
                Last = (ox, oy, level, Turns);
                OX = ox; OY = oy; Level = level;
                Problem = Building.Check(vm, b, ox, oy, level, Moving, out Cost, out var overlay);
                Preview.Show(vm.Context.Architecture, overlay);
                var foot = b.Plan.Tiles.Select(x => (x.X + ox, x.Y + oy, x.Level + level - 1));
                Highlight.Set(vm, foot.Select(f => (f.Item1, f.Item2, (int)f.Item3)), Problem == null ? Highlight.Good : Highlight.Bad, Enumerable.Empty<Edge>(), Highlight.Wall);
                Ghosts?.MoveTo(vm, b, ox, oy, level, true);
            }
            string text;
            if (Problem != null) text = Problem;
            else if (Moving != null) text = "Click to put the room down here. [ and ] turn it.";
            else text = "Click to build it here: " + ToolHelp.Money(Cost) + ". [ and ] turn it." + (Kind == Catalog.Kind.PlaceRoom ? " Delete takes it out of the library." : "");
            if (Note != null && Problem == null) text += "\n" + Note;
            ToolHelp.Tooltip(state, MousePosition, text, Problem != null);
        }

        private static (int x, int y) TurnTile(int x, int y, int width, int height, int quarters)
        {
            for (int i = 0; i < quarters; i++)
            {
                (x, y) = Plan.TurnTile(x, y, height);
                (width, height) = (height, width);
            }
            return (x, y);
        }

        private void Hover(UpdateState state, Vector2 t, int level)
        {
            int x = (int)Math.Floor(t.X), y = (int)Math.Floor(t.Y);
            var half = Regions.HalfAt(new EngineGrid(vm.Context.Architecture).GetWall(x, y, level), t.X - x, t.Y - y);
            if (HoverAt != (x, y, half, level) || ++HoverAge > 45)
            {
                HoverAt = (x, y, half, level);
                HoverAge = 0;
                Hovered = Building.Pick(vm, t.X, t.Y, level, Kind == Catalog.Kind.MoveRoom, false);
                if (Hovered.Room == null) Highlight.Clear();
                else
                {
                    var bad = Kind == Catalog.Kind.MoveRoom && Hovered.Problem != null;
                    Highlight.Set(vm, Hovered.Room.Tiles.Select(p => (p.x, p.y, (int)level)), bad ? Highlight.Bad : Highlight.Hover, Hovered.Room.OwnWalls, Highlight.Wall);
                }
            }
            string text;
            var badText = false;
            if (FlashFrames > 0 && Flash != null) { text = Flash; badText = true; }
            else if (Hovered?.Room == null) { text = Hovered?.Problem ?? ""; badText = true; }
            else if (Kind == Catalog.Kind.MoveRoom && Hovered.Problem != null) { text = Hovered.Problem; badText = true; }
            else
            {
                var count = Hovered.Blueprint.Objects.Count(o => o.Container < 0);
                var size = Hovered.Room.Cells.Count;
                text = "Click to " + Verb + " this room (" + size + " tile" + (size == 1 ? "" : "s") + ", " + count + " object" + (count == 1 ? "" : "s") + ").";
            }
            if (text != "") ToolHelp.Tooltip(state, MousePosition, text, badText);
        }

        private void AskDelete()
        {
            var panel = ToolHelp.Panel;
            if (panel == null || panel.ItemID < 0) return;
            var elem = panel.FilterCategory?.ElementAtOrDefault(panel.ItemID);
            var index = (elem?.Special?.Parameters?.Count ?? 0) > 1 ? elem.Special.Parameters[1] : -1;
            if (index < 0 || index >= Catalog.Listed.Count) return;
            var entry = Catalog.Listed[index];
            ToolHelp.Ask("Delete from Library", "Take \"" + entry.Name + "\" out of your library? Its file is deleted.", "Delete", () =>
            {
                try { Library.Delete(entry); }
                catch (Exception e) { ToolHelp.Say("Room Tools", "It couldn't be deleted: " + e.Message); return; }
                ToolHelp.RefreshTab();
            });
        }

        public override void Release()
        {
            PutDown();
            Highlight.Clear();
        }
    }

    /// <summary>
    /// Move Wall: drag a straight run of wall (from corner to corner) across, to make the rooms either side of it bigger
    /// and smaller. See WallMove for what happens at its ends.
    /// </summary>
    public class UIWallMover : UICustomLotControl
    {
        private readonly VM vm;
        private readonly World World;
        private readonly ILotControl Parent;

        private WallRun Hovered, Dragging;
        private int Delta;
        private WallMove Move;
        private string Problem;
        private int Cost;
        private List<FSO.SimAntics.Entities.VMMultitileGroup> Riding = new List<FSO.SimAntics.Entities.VMMultitileGroup>();
        private (int x, int y, int level, bool v) HoverKey = (int.MinValue, 0, 0, false);

        public UIWallMover(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            this.vm = vm;
            World = parent.World;
            Parent = parent;
        }

        /// <summary>The wall nearest a point on a floor, if it is close.</summary>
        public static Edge? WallNear(IGrid g, float fx, float fy, int level)
        {
            Edge? best = null;
            var bestDistance = 0.35f;
            int rx = (int)Math.Round(fx), ry = (int)Math.Round(fy);
            int tx = (int)Math.Floor(fx), ty = (int)Math.Floor(fy);
            var along = Edge.AlongX(tx, ry, level);
            if (Walls.Has(g, along) && Math.Abs(fy - ry) < bestDistance) { best = along; bestDistance = Math.Abs(fy - ry); }
            var across = Edge.AlongY(rx, ty, level);
            if (Walls.Has(g, across) && Math.Abs(fx - rx) < bestDistance) best = across;
            return best;
        }

        public override void MouseDown(UpdateState state)
        {
            if (Hovered == null) return;
            Dragging = Hovered;
            Delta = 0;
            Move = null;
            Problem = null;
            HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolDown);
        }

        public override void MouseUp(UpdateState state)
        {
            if (Dragging == null) return;
            var move = Move;
            var problem = Problem;
            Dragging = null;
            Preview.Hide(vm.Context.Architecture);
            Highlight.Clear();
            HoverKey = (int.MinValue, 0, 0, false);
            if (move == null || move.Delta == 0) { HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolUp); return; }
            if (problem != null) { HITVM.Get().PlaySoundEvent(UISounds.Error); return; }
            var step = Undo.Begin(vm, "moving a wall");
            string error;
            try { error = Building.MoveWall(vm, move, Riding, Cost); }
            catch (Exception e)
            {
                RoomToolsPack.Log("Moving a wall failed: " + e);
                error = "Something went wrong: " + e.Message;
            }
            if (error != null)
            {
                Undo.Failed(vm, step);
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                ToolHelp.Say("Room Tools", "The wall couldn't be moved, so nothing has changed.\n\n" + error);
                return;
            }
            Undo.Done(step);
            HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
        }

        public override void Update(UpdateState state, bool scrolled)
        {
            var level = World.State.Level;
            var t = ToolHelp.Cursor(World, MousePosition, level);
            var arch = vm.Context.Architecture;
            var grid = new EngineGrid(arch);

            if (Dragging == null)
            {
                var near = WallNear(grid, t.X, t.Y, level);
                var key = near == null ? (int.MinValue, 0, level, false) : (near.Value.X, near.Value.Y, level, near.Value.Vertical);
                if (key != HoverKey)
                {
                    HoverKey = key;
                    Hovered = near == null ? null : WallRun.Of(grid, near.Value);
                    if (Hovered == null) Highlight.Clear();
                    else Highlight.Set(vm, Enumerable.Empty<(int, int, int)>(), Highlight.Hover, Hovered.Walls(Hovered.Across), Highlight.Wall);
                }
                if (Hovered != null) ToolHelp.Tooltip(state, MousePosition, "Drag this wall to move it.", false);
                return;
            }

            var delta = (int)Math.Round((Dragging.Vertical ? t.X : t.Y) - Dragging.Across);
            delta = Math.Max(-WallMove.Furthest, Math.Min(WallMove.Furthest, delta));
            if (Move == null || delta != Delta)
            {
                Delta = delta;
                Move = WallMove.Plan(grid, Dragging, delta, Building.Buildable(arch));
                Problem = Building.CheckWallMove(vm, Move, out Riding, out Cost);
                var overlay = new OverlayGrid(grid);
                if (Move.Problem == null) Move.Apply(overlay);
                Preview.Show(arch, overlay);
                Highlight.Set(vm, Move.Swept.Select(p => (p.x, p.y, (int)level)), Problem == null ? Highlight.Good : Highlight.Bad,
                    Dragging.Walls(Dragging.Across + delta), Problem == null ? Highlight.Wall : Highlight.Bad);
            }
            string text;
            if (Delta == 0) text = "Drag across to move the wall.";
            else if (Problem != null) text = Problem;
            else text = Math.Abs(Delta) + " tile" + (Math.Abs(Delta) == 1 ? "" : "s") + ": " + ToolHelp.Money(Cost) + (Riding.Count > 0 ? ", with " + Riding.Count + " thing" + (Riding.Count == 1 ? "" : "s") + " on the wall" : "");
            ToolHelp.Tooltip(state, MousePosition, text, Problem != null);
        }

        public override void Release()
        {
            Dragging = null;
            Preview.Hide(vm.Context.Architecture);
            Highlight.Clear();
        }
    }

    /// <summary>
    /// The Rooms tab's entries that do one thing when chosen, rather than being a tool to use on the lot: Undo, Redo,
    /// Save Lot, and a saved lot from the library. Chosen, they do it, and put themselves down.
    /// </summary>
    public class UIRoomAction : UICustomLotControl
    {
        private readonly VM vm;
        private readonly World World;
        private readonly Catalog.Kind Kind;
        private readonly int Index;
        private bool Ran;

        public UIRoomAction(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            this.vm = vm;
            World = parent.World;
            Kind = (Catalog.Kind)((parameters != null && parameters.Count > 0) ? parameters[0] : 0);
            Index = (parameters != null && parameters.Count > 1) ? parameters[1] : -1;
        }

        public override void MouseDown(UpdateState state) { }
        public override void MouseUp(UpdateState state) { }
        public override void Release() { }

        public override void Update(UpdateState state, bool scrolled)
        {
            if (Ran) return;
            Ran = true;
            //put down first (in the next frame, not inside the lot control's own update), then do it
            GameThread.NextUpdate((s) =>
            {
                ToolHelp.Panel?.Deselect();
                try { Run(); }
                catch (Exception e)
                {
                    RoomToolsPack.Log("The Rooms tab's \"" + Kind + "\" failed: " + e);
                    ToolHelp.Say("Room Tools", "That didn't work: " + e.Message);
                }
            });
        }

        private void Run()
        {
            switch (Kind)
            {
                case Catalog.Kind.Undo:
                case Catalog.Kind.Redo:
                    RoomToolsPack.UndoOrRedo(vm, Kind == Catalog.Kind.Redo);
                    break;
                case Catalog.Kind.SaveLot:
                    SaveLot();
                    break;
                case Catalog.Kind.PlaceLot:
                    PlaceLot();
                    break;
            }
        }

        private void SaveLot()
        {
            var b = Building.CaptureLot(vm, out var leftOut);
            var house = vm.GetGlobalValue(10);
            var suggested = Library.FreshName(house > 0 ? "Lot " + house : "Lot", true);
            ToolHelp.AskName("Save Lot", "What should this lot be called in your library?" + (leftOut.Count > 0 ? "\n\nLeft out, being carried: " + string.Join(", ", leftOut.Distinct().Take(6)) + "." : ""), suggested, (name) =>
            {
                try
                {
                    b.Name = name;
                    b.Saved = DateTime.UtcNow;
                    b.Thumbnail = Icons.DrawLot(World, b);
                    var path = Library.Save(b);
                    HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
                    ToolHelp.Say("Saved", "\"" + name + "\" is in your library, under Rooms in build mode, to build on any lot. Its file is " + path + ": copy it to another player's Library/Lots folder to give it to them.");
                    ToolHelp.RefreshTab();
                }
                catch (Exception e)
                {
                    RoomToolsPack.Log("Saving a lot failed: " + e);
                    ToolHelp.Say("Room Tools", "The lot couldn't be saved: " + e.Message);
                }
            });
        }

        private void PlaceLot()
        {
            if (Index < 0 || Index >= Catalog.Listed.Count) return;
            var entry = Catalog.Listed[Index];
            Blueprint b;
            List<string> missing;
            try { b = ToolHelp.LoadReady(entry, out missing); }
            catch (Exception e) { ToolHelp.Say("Room Tools", "\"" + entry.Name + "\" can't be read: " + e.Message); return; }
            var problem = Building.CheckLot(vm, b, out var cost, out var refund);
            var missingText = missing.Count > 0 ? "\n\nNot installed here, so left out: " + string.Join(", ", missing.Distinct().Take(6)) + (missing.Distinct().Count() > 6 ? "..." : "") + "." : "";
            if (problem != null)
            {
                ToolHelp.Ask("Lot Bin", "\"" + entry.Name + "\" can't be built here. " + problem, "OK", () => { }, "Delete it", () => Delete(entry));
                return;
            }
            var free = vm.TS1State?.CurrentFamily == null;
            var money = free ? "" : "\n\nEverything here that players can sell fetches " + ToolHelp.Money(refund) + ", and the saved lot costs " + ToolHelp.Money(cost) + ": " + (cost >= refund ? ToolHelp.Money(cost - refund) + " to pay." : ToolHelp.Money(refund - cost) + " back.");
            ToolHelp.Ask("Lot Bin", "Build \"" + entry.Name + "\" here? Everything built on this lot, and every object players can move, is replaced by it. The Sims stay, and so do the lot's own mailbox and bin. You can undo it." + money + missingText,
                "Build it", () => Build(b, free ? 0 : cost - refund), "Delete it", () => Delete(entry));
        }

        private void Build(Blueprint b, int net)
        {
            var step = Undo.Begin(vm, "building a saved lot");
            var notes = new List<string>();
            try { Building.PlaceLot(vm, b, net, notes); }
            catch (Exception e)
            {
                RoomToolsPack.Log("Building a saved lot failed: " + e);
                Undo.Failed(vm, step);
                ToolHelp.Say("Lot Bin", "The lot couldn't be built, so nothing has changed: " + e.Message);
                return;
            }
            Undo.Done(step);
            HITVM.Get().PlaySoundEvent(UISounds.BuyPlace);
            if (notes.Count > 0) ToolHelp.Say("Lot Bin", "Built, but these were left out:\n\n" + string.Join("\n", notes.Take(12)) + (notes.Count > 12 ? "\n..." : ""));
        }

        private void Delete(Library.Entry entry)
        {
            ToolHelp.Ask("Delete from Library", "Take \"" + entry.Name + "\" out of your library? Its file is deleted.", "Delete", () =>
            {
                try { Library.Delete(entry); }
                catch (Exception e) { ToolHelp.Say("Room Tools", "It couldn't be deleted: " + e.Message); return; }
                ToolHelp.RefreshTab();
            });
        }
    }
}
