/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RoomTools
{
    /// <summary>
    /// Room Tools, after The Sims 4: pick up a room with everything in it and move, turn or copy it; drag a wall to
    /// resize a room; undo and redo in build and buy mode; and a library of saved rooms and lots (the Lot Bin), kept
    /// as files that can be given to other players. Everything is in the Rooms tab of build mode.
    ///
    /// The engine is not changed. These are the places the pack reaches into it, each a Harmony patch:
    ///
    ///  1. Undo. A build or buy command being sent (VM.SendCommand) keeps the lot as it was; once the lot has changed
    ///     (checked each frame, after VM.Update), that is a step to go back to. The engine really building something
    ///     (VMArchitecture.RunCommands, not for a preview) counts as a change.
    ///  2. Previews. A tool's walls and floors are shown through the engine's own preview of its tools' commands
    ///     (VMArchitecture.RunCommands, for a preview): see Preview.
    ///  3. The Rooms tab. Build mode's tabs (UIMainPanel.SetMode) get one more, which the game's catalogue panel shows
    ///     (UIBuyBrowsePanel.InitCategory and Reset are filled from the pack for it). Going back to live mode forgets
    ///     the undo steps.
    ///  4. Ctrl+Z and Ctrl+Y, after the lot control's update (UILotControl.Update).
    ///  5. The coloured highlight, drawn in place of a set of lines the pack puts on the lot (DebugLinesComponent.Draw).
    /// </summary>
    public class RoomToolsPack : ISimitonePack
    {
        public static string PackId = "sample.roomtools";

        public void OnLoad(Pack pack)
        {
            PackId = pack.Id;
            Undo.TidyUp();
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
            Undo.BeforeRestore = BeforeRestore;
            Undo.AfterRestore = AfterRestore;
            Command("undo", () => UndoOrRedo(Screen?.vm, false));
            Command("redo", () => UndoOrRedo(Screen?.vm, true));
        }

        private static void Command(string name, Action run)
        {
            try
            {
                PackManager.RegisterCommand(name, (args) =>
                {
                    if (args.Length > 0 || Screen?.vm == null) return false;
                    run();
                    return true;
                });
            }
            catch (Exception e) { Log("The console command \"" + name + "\" was not added: " + e.Message); }
        }

        public void OnLotLoaded(VM vm)
        {
            Undo.Clear();
            Highlight.Clear();
        }

        public static void Log(string text) => Console.WriteLine("[Room Tools] " + text);

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(RoomToolsPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] args)
        {
            var method = (args.Length == 0) ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
            return method ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1 and 2
            harmony.Patch(Find(typeof(VM), "SendCommand", typeof(VMNetCommandBodyAbstract)), prefix: Mine(nameof(BeforeSend)));
            harmony.Patch(Find(typeof(VM), "Update"), postfix: Mine(nameof(AfterVMUpdate)));
            harmony.Patch(Find(typeof(VMArchitecture), "RunCommands", typeof(List<VMArchitectureCommand>), typeof(bool)), postfix: Mine(nameof(AfterRunCommands)));
            // 3
            harmony.Patch(Find(typeof(UIMainPanel), "SetMode"), prefix: Mine(nameof(BeforeSetMode)), postfix: Mine(nameof(AfterSetMode)));
            harmony.Patch(Find(typeof(UIBuyBrowsePanel), "InitCategory", typeof(sbyte), typeof(bool)), prefix: Mine(nameof(BeforeInitCategory)));
            harmony.Patch(Find(typeof(UIBuyBrowsePanel), "Reset"), prefix: Mine(nameof(BeforeReset)));
            // 4
            harmony.Patch(Find(typeof(UILotControl), "Update", typeof(UpdateState)), postfix: Mine(nameof(AfterLotControlUpdate)));
            // 5
            harmony.Patch(Find(typeof(DebugLinesComponent), "Draw", typeof(GraphicsDevice), typeof(WorldState)), prefix: Mine(nameof(BeforeLinesDrawn)));
            // the price under the Rooms tab's tools, which cost nothing to choose: left blank. Only looks, so it may fail
            try
            {
                var item = AccessTools.Constructor(typeof(Simitone.Client.UI.Panels.LiveSubpanels.Catalog.UICatalogItem), new[] { typeof(UICatalogElement), typeof(UIBuyBrowsePanel) });
                if (item != null) harmony.Patch(item, postfix: Mine(nameof(AfterCatalogItem)));
            }
            catch (Exception e) { Log("The Rooms tab's tools will show a price of nothing: " + e.Message); }
        }

        public static void AfterCatalogItem(Simitone.Client.UI.Panels.LiveSubpanels.Catalog.UICatalogItem __instance, UICatalogElement elem)
        {
            if (elem?.Item.Category == Catalog.CategoryId && elem.Item.Price == 0 && __instance.PriceLabel != null) __instance.PriceLabel.Caption = "";
        }

        public static TS1GameScreen Screen => GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;

        /// <summary>Whether a lot is open in build or buy mode (where the game stands still, and undo works).</summary>
        public static bool InBuildOrBuy(VM vm)
        {
            var screen = Screen;
            if (screen == null || vm == null || screen.vm != vm) return false;
            var mode = screen.Frontend?.MainPanel?.Mode;
            return mode == UIMainPanelMode.BUILD || mode == UIMainPanelMode.BUY;
        }

        // ---- 1 and 2 ----

        public static void BeforeSend(VM __instance, VMNetCommandBodyAbstract cmd)
        {
            try { Undo.BeforeCommand(__instance, cmd); }
            catch (Exception e) { Log("Could not keep the lot for undo: " + e); }
        }

        public static void AfterVMUpdate(VM __instance)
        {
            try { Undo.Frame(__instance); }
            catch (Exception e) { Log("Could not keep an undo step: " + e); }
        }

        public static void AfterRunCommands(VMArchitecture __instance, List<VMArchitectureCommand> commands, bool transient)
        {
            if (!transient && __instance.RealMode) Undo.Built();
            else if (transient)
            {
                try { Preview.AfterCommands(__instance, commands); }
                catch (Exception e) { Log("Could not show a preview: " + e.Message); }
            }
        }

        // ---- 3 ----

        public static void BeforeSetMode(UIMainPanelMode mode, List<UICategory> ___BuildCategories)
        {
            if (mode != UIMainPanelMode.BUILD || ___BuildCategories == null) return;
            if (!___BuildCategories.Any(x => x.ID == Catalog.CategoryId))
                ___BuildCategories.Add(new UICategory() { ID = Catalog.CategoryId, IconName = Catalog.TabIcon });
        }

        public static void AfterSetMode(UIMainPanelMode mode)
        {
            if (mode == UIMainPanelMode.BUY || mode == UIMainPanelMode.BUILD) return;
            Undo.Clear(); //back to live mode (or Options): the game moves on, and what was kept can't be put back
            Highlight.Clear();
        }

        /// <summary>The Rooms tab is opening (or being listed again): its entries are the pack's, with no subcategories.</summary>
        public static bool BeforeInitCategory(UIBuyBrowsePanel __instance, sbyte category)
        {
            if (category != Catalog.CategoryId || __instance.Mode != UICatalogMode.Build) return true;
            try
            {
                foreach (var btn in __instance.SelButtons) __instance.Remove(btn);
                foreach (var label in __instance.SelLabels) __instance.Remove(label);
                __instance.SelButtons.Clear();
                __instance.SelLabels.Clear();
                __instance.FullCategory = Catalog.Elements();
                __instance.FilterCategory = __instance.FullCategory;
                __instance.ChoosingSub = false;
                __instance.CatContainer.Opacity = 1f;
                __instance.CatContainer.Reset();
            }
            catch (Exception e)
            {
                Log("Could not list the Rooms tab: " + e);
                __instance.FullCategory = new List<UICatalogElement>();
                __instance.FilterCategory = __instance.FullCategory;
            }
            return false;
        }

        /// <summary>The game fades a tab out to choose a subcategory again. The Rooms tab has none: it is listed again instead.</summary>
        public static bool BeforeReset(UIBuyBrowsePanel __instance)
        {
            if (__instance.Category != Catalog.CategoryId || __instance.Mode != UICatalogMode.Build) return true;
            __instance.InitCategory(__instance.Category, false);
            return false;
        }

        // ---- 4 ----

        private static string Toast;
        private static int ToastFrames;

        public static void AfterLotControlUpdate(UILotControl __instance, UpdateState state)
        {
            try
            {
                var vm = __instance.vm;
                if (!InBuildOrBuy(vm)) { ToastFrames = 0; return; }
                var typing = state.InputManager?.GetFocus() is UITextBox || state.InputManager?.GetFocus() is UITextEdit;
                if (!typing && state.CtrlDown && state.NewKeys != null)
                {
                    if (state.NewKeys.Contains(Keys.Z)) UndoOrRedo(vm, state.ShiftDown);
                    else if (state.NewKeys.Contains(Keys.Y)) UndoOrRedo(vm, true);
                }
                if (ToastFrames > 0)
                {
                    ToastFrames--;
                    if (__instance.CustomControl == null && __instance.ObjectHolder?.Holding == null)
                    {
                        var mouse = state.MouseState.Position;
                        state.UIState.TooltipProperties.Show = true;
                        state.UIState.TooltipProperties.Color = Color.Black;
                        state.UIState.TooltipProperties.Opacity = 1;
                        state.UIState.TooltipProperties.Position = new Vector2(mouse.X, mouse.Y);
                        state.UIState.Tooltip = Toast;
                        state.UIState.TooltipProperties.UpdateDead = false;
                    }
                }
            }
            catch (Exception e) { Log("Undo from the keyboard failed: " + e); }
        }

        /// <summary>Undo (or redo) a step on the lot open in build or buy mode, and say what it was.</summary>
        public static void UndoOrRedo(VM vm, bool redo)
        {
            if (!InBuildOrBuy(vm))
            {
                ShowToast("Undo works in build and buy mode.");
                return;
            }
            if (Undo.Busy)
            {
                ShowToast("Just a moment: the last change is still being made.");
                return;
            }
            string done;
            try { done = redo ? Undo.StepForward(vm) : Undo.StepBack(vm); }
            catch (Exception e)
            {
                Log((redo ? "Redo" : "Undo") + " failed: " + e);
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                ShowToast((redo ? "Redo" : "Undo") + " failed: " + e.Message);
                return;
            }
            if (done == null)
            {
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                ShowToast(redo ? "Nothing to redo." : "Nothing to undo.");
                return;
            }
            HITVM.Get().PlaySoundEvent(UISounds.ObjectRotate);
            var next = redo ? Undo.RedoName : Undo.UndoName;
            ShowToast((redo ? "Redone: " : "Undone: ") + done + (next != null ? ". " + (redo ? "Ctrl+Y redoes " : "Ctrl+Z undoes ") + next + "." : "."));
        }

        private static void ShowToast(string text)
        {
            Toast = text;
            ToastFrames = 150;
        }

        /// <summary>Before the lot is put back: tools and objects in hand are put down, as their lot is about to go.</summary>
        private static int Reselect = -1;

        private static void BeforeRestore()
        {
            Reselect = -1;
            var screen = Screen;
            var control = screen?.LotControl;
            if (control == null) return;
            var panel = screen.Frontend?.MainPanel?.SubPanel as UIBuyBrowsePanel;
            var custom = control.CustomControl;
            if (custom != null)
            {
                if (!(custom is UIRoomAction) && panel != null) Reselect = panel.ItemID;
                control.CustomControl = null;
                try { custom.Release(); } catch (Exception e) { Log("A tool could not be put down: " + e.Message); }
            }
            var holder = control.ObjectHolder;
            if (holder?.Holding != null)
            {
                if (!holder.Holding.IsBought && panel != null) Reselect = panel.ItemID; //a new object from the catalogue: take it up again
                try { holder.Cancel(); } catch (Exception e) { Log("What was in hand could not be put down: " + e.Message); }
            }
            if (control.QueryPanel != null) control.QueryPanel.Active = false;
            Highlight.Clear();
            Preview.Hide(screen.vm?.Context?.Architecture);
        }

        private static void AfterRestore()
        {
            var screen = Screen;
            var panel = screen?.Frontend?.MainPanel?.SubPanel as UIBuyBrowsePanel;
            if (panel == null) return;
            var index = Reselect;
            Reselect = -1;
            panel.ItemID = -1;
            if (index < 0 || panel.FilterCategory == null || index >= panel.FilterCategory.Count()) return;
            try { panel.Selected(index); }
            catch (Exception e) { Log("The tool could not be taken up again: " + e.Message); }
        }

        // ---- 5 ----

        public static bool BeforeLinesDrawn(DebugLinesComponent __instance, GraphicsDevice gd, WorldState state)
        {
            if (!Highlight.IsMine(__instance)) return true;
            Highlight.Draw(gd, state);
            return false;
        }
    }
}
