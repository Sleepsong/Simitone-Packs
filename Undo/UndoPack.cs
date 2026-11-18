/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.HIT;
using FSO.SimAntics;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.Platform;
using FSO.SimAntics.NetPlay.Model.Commands;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Undo
{
    /// <summary>
    /// Undo and redo in build and buy mode, as in The Sims 2: Ctrl+Z undoes the last change, Ctrl+Y (or Ctrl+Shift+Z)
    /// makes it again, and the undo and redo console commands do the same.
    ///
    /// After each change the lot is kept as it is (LotMoments: the engine's own saved state, and the packs' data for
    /// it), and undo puts back the one before. A change is what the engine's build and buy commands do (walls, floors,
    /// wallpaper, the ground, pools, roofs, buying, selling and moving), and what packs say they did
    /// (OnLotChanged, below). The money a change took from the family, or gave it, is what the budget moved by
    /// between one moment and the next; undo gives it back or takes it back, and redo the other way. The history
    /// begins when build or buy mode is opened and ends when it is closed: the game is paused all that time, so putting
    /// the lot back puts back nothing the Sims did.
    ///
    /// A pack that keeps something for a lot and cannot be put back with it would no longer agree with the lot after
    /// an undo (PackParts.NotTakingPart). While one is running, undo and redo are refused, and say which pack.
    ///
    /// Nothing in Simitone is changed for any of this: what the packs do with each other is a convention between
    /// them, kept by this pack (PackParts, and OnLotChanged here).
    /// </summary>
    public class UndoPack : ISimitonePack
    {
        public const string PackId = "sample.undo";
        private const string HarmonyId = "simitone.packs.undo";

        /// <summary>Ticks with no build or buy command before a change is taken to be finished, and kept (a third of a second).</summary>
        public const int QuietTicks = 10;

        public void OnLoad(Pack pack)
        {
            LotMoments.CheckEngine();
            //every pack's code is loaded by now: see what each does with lots, before any of it can be stopped
            PackParts.Look();
            Apply(new Harmony(HarmonyId));
            PackManager.RegisterCommand("undo", args => Command(args, true));
            PackManager.RegisterCommand("redo", args => Command(args, false));
            try { File.Delete(LogPath); } catch { }
        }

        /// <summary>
        /// A pack changed the lot with a tool of its own, without the engine's commands, and said so. Not one of
        /// ISimitonePack's calls: a pack says so by calling, on the code of every pack in use (PackManager.Active, then
        /// Pack.Code), a public method of this name taking a VM and a string, if it has one. See Create a Style's
        /// SayChanged for the few lines it takes.
        /// </summary>
        public void OnLotChanged(VM vm, string what)
        {
            Changed(vm, string.IsNullOrWhiteSpace(what) ? "Build" : what.Trim());
        }

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(UndoPack), name);

        private static MethodInfo Find(Type type, string name, params Type[] args)
        {
            return AccessTools.Method(type, name, args)
                ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static readonly AccessTools.FieldRef<VMNetBuyObjectCmd, VMMultitileGroup> CreatedGroup =
            AccessTools.FieldRefAccess<VMNetBuyObjectCmd, VMMultitileGroup>("CreatedGroup");

        private static void Apply(Harmony harmony)
        {
            var vmAvatar = new[] { typeof(VM), typeof(VMAvatar) };
            //1. counting the lot's ticks, to tell when a change is finished
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            //2. the engine's build and buy commands: a change is on its way (the money moves here), and made
            harmony.Patch(Find(typeof(VMNetArchitectureCmd), "Verify", vmAvatar), prefix: Mine(nameof(Activity)));
            harmony.Patch(Find(typeof(VMNetArchitectureCmd), "Execute", typeof(VM)), postfix: Mine(nameof(AfterArchitecture)));
            harmony.Patch(Find(typeof(VMNetBuyObjectCmd), "Verify", vmAvatar), prefix: Mine(nameof(Activity)));
            harmony.Patch(Find(typeof(VMNetBuyObjectCmd), "Execute", vmAvatar), postfix: Mine(nameof(AfterBuy)));
            harmony.Patch(Find(typeof(VMNetDeleteObjectCmd), "Execute", vmAvatar), prefix: Mine(nameof(BeforeSell)), postfix: Mine(nameof(AfterSell)));
            harmony.Patch(Find(typeof(VMNetMoveObjectCmd), "Execute", vmAvatar), postfix: Mine(nameof(AfterMove)));
            harmony.Patch(Find(typeof(VMNetSetRoofCmd), "Execute", vmAvatar), postfix: Mine(nameof(AfterRoof)));
            //3. each frame of the lot: keeping changes, and the keys
            harmony.Patch(Find(typeof(UILotControl), "Update", typeof(UpdateState)), postfix: Mine(nameof(AfterLotControlUpdate)));
            //(the global link holds architecture commands back while their money moves: see Quiet)
            if (ArchBuffer == null || WaitingOnArch == null) Log("The engine's architecture queue was not found: a change is taken to be finished after " + QuietTicks + " quiet ticks alone");
        }

        public static void AfterTick(VM __instance)
        {
            var lot = Lot;
            if (lot != null && lot.Is(__instance)) lot.Ticks++;
        }

        public static void Activity(VM vm)
        {
            var lot = Lot;
            if (lot != null && lot.Is(vm)) lot.LastActivity = lot.Ticks;
        }

        public static void AfterArchitecture(VMNetArchitectureCmd __instance, VM vm, bool __result)
        {
            Activity(vm);
            if (__result && __instance.Commands != null && __instance.Commands.Count > 0) Changed(vm, Describe(__instance.Commands));
        }

        public static void AfterBuy(VMNetBuyObjectCmd __instance, VM vm, bool __result)
        {
            Activity(vm);
            if (__result) Changed(vm, "Buy " + NameOf(CreatedGroup(__instance)));
        }

        public static void BeforeSell(VMNetDeleteObjectCmd __instance, VM vm, out string __state)
        {
            __state = (__instance.Mode == DeleteMode.Delete) ? NameOf(vm.GetObjectById(__instance.ObjectID)?.MultitileGroup) : null;
        }

        public static void AfterSell(VM vm, bool __result, string __state)
        {
            Activity(vm);
            if (__result && __state != null) Changed(vm, "Sell " + __state);
        }

        public static void AfterMove(VMNetMoveObjectCmd __instance, VM vm, bool __result)
        {
            Activity(vm);
            if (__result) Changed(vm, "Move " + NameOf(vm.GetObjectById(__instance.ObjectID)?.MultitileGroup));
        }

        public static void AfterRoof(VM vm, bool __result)
        {
            Activity(vm);
            if (__result) Changed(vm, "Change the roof");
        }

        private static string NameOf(VMMultitileGroup group)
        {
            var name = group?.Name;
            return string.IsNullOrWhiteSpace(name) ? "an object" : name.Trim();
        }

        /// <summary>A change in a few words, from the first of a batch of architecture commands.</summary>
        public static string Describe(List<VMArchitectureCommand> commands)
        {
            var first = commands[0];
            switch (first.Type)
            {
                case VMArchitectureCommandType.WALL_LINE: return "Build walls";
                case VMArchitectureCommandType.WALL_RECT: return "Build a room";
                case VMArchitectureCommandType.WALL_DELETE: return "Take down walls";
                case VMArchitectureCommandType.PATTERN_DOT:
                case VMArchitectureCommandType.PATTERN_FILL: return "Wallpaper";
                case VMArchitectureCommandType.FLOOR_RECT:
                case VMArchitectureCommandType.FLOOR_FILL:
                    if (first.pattern >= 65534) return "Build a pool";
                    return (first.pattern == 0) ? "Take up the floor" : "Lay a floor";
                case VMArchitectureCommandType.TERRAIN_RAISE:
                case VMArchitectureCommandType.TERRAIN_FLATTEN: return "Shape the ground";
                case VMArchitectureCommandType.GRASS_DOT: return "Paint the grass";
                default: return "Build"; //a command type of a pack's own
            }
        }

        // ---- the lot being played ----

        /// <summary>Undo's state for the lot being played. A lot is one VM: another VM is another lot.</summary>
        public class LotUndo
        {
            private readonly WeakReference<VM> Vm;
            public readonly History<LotMoment> History = new History<LotMoment>();
            /// <summary>The lot was paused for building at the last look, and its history has begun.</summary>
            public bool Building;
            /// <summary>A change has been made since the lot was last kept, and in a few words what.</summary>
            public string What;
            public uint Ticks;
            public uint LastActivity;
            /// <summary>The family's budget when the lot was last kept or put back (null: no family plays it).</summary>
            public int? BudgetThen;
            /// <summary>An undo (true) or redo (false) asked for while a change was still being made, to do once it is.</summary>
            public bool? Waiting;
            /// <summary>The packs that kept undo off when build mode was opened (PackParts.NotTakingPart). Nothing is kept while there are any.</summary>
            public List<string> Blocked = new List<string>();

            public LotUndo(VM vm) { Vm = new WeakReference<VM>(vm); }
            public bool Is(VM vm) => vm != null && Vm.TryGetTarget(out var mine) && mine == vm;
        }

        public static LotUndo Lot;

        private static LotUndo LotFor(VM vm)
        {
            if (Lot == null || !Lot.Is(vm)) Lot = new LotUndo(vm);
            return Lot;
        }

        private static void Changed(VM vm, string what)
        {
            var lot = Lot;
            if (lot == null || !lot.Is(vm) || !lot.Building) return; //only build and buy mode's changes are kept
            lot.What = (lot.What == null || lot.What == what) ? what : "Several changes";
            lot.LastActivity = lot.Ticks;
        }

        private static readonly AccessTools.FieldRef<VMTS1GlobalLinkStub, Queue<VMNetArchitectureCmd>> ArchBuffer = Ref<Queue<VMNetArchitectureCmd>>("ArchBuffer");
        private static readonly AccessTools.FieldRef<VMTS1GlobalLinkStub, bool> WaitingOnArch = Ref<bool>("WaitingOnArch");

        private static AccessTools.FieldRef<VMTS1GlobalLinkStub, T> Ref<T>(string field)
        {
            try { return AccessTools.FieldRefAccess<VMTS1GlobalLinkStub, T>(field); }
            catch { return null; }
        }

        /// <summary>No build or buy command is on its way: what has been done is finished, and its money has moved.</summary>
        private static bool Quiet(LotUndo lot, VM vm)
        {
            if (lot.Ticks - lot.LastActivity < QuietTicks) return false;
            if (vm.GlobalLink is VMTS1GlobalLinkStub link && ArchBuffer != null && WaitingOnArch != null)
            {
                var queue = ArchBuffer(link);
                lock (queue)
                {
                    if (queue.Count > 0 || WaitingOnArch(link)) return false;
                }
            }
            return true;
        }

        public static void AfterLotControlUpdate(UILotControl __instance, UpdateState state)
        {
            var screen = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
            var vm = __instance.vm;
            if (screen == null || vm == null || screen.LotControl != __instance) return;
            try
            {
                Update(screen, vm, state);
            }
            catch (Exception e)
            {
                Log("Something went wrong in build mode: " + e);
                if (Lot != null) { Lot.History.Clear(); Lot.Building = false; }
            }
        }

        private static void Update(TS1GameScreen screen, VM vm, UpdateState state)
        {
            var lot = LotFor(vm);
            //build and buy mode (and options) pause the game: the lot's history lasts as long
            if (vm.SpeedMultiplier != -1 || !vm.Ready)
            {
                if (lot.Building) Log("Build mode closed: " + lot.History.UndoCount + " changes forgotten");
                lot.Building = false;
                lot.History.Clear();
                lot.What = null;
                lot.Waiting = null;
                return;
            }
            if (!lot.Building)
            {
                lot.Building = true;
                lot.What = null;
                lot.Blocked = PackParts.NotTakingPart();
                if (lot.Blocked.Count > 0)
                {
                    //nothing could be undone: keep nothing
                    Log("Build mode opened. Undo is off: " + string.Join("; ", lot.Blocked.Select(PackParts.Why)));
                }
                else
                {
                    var first = LotMoments.Take(vm);
                    lot.History.Start(first, first.Size);
                    lot.BudgetThen = LotMoments.Budget(vm);
                    Log("Build mode opened: the lot is kept (" + Kb(first.Size) + ")");
                }
            }

            //the keys. not while typing, nor while a build tool is being dragged
            var keys = state.NewKeys;
            if (state.CtrlDown && state.InputManager?.GetFocus() == null && (keys.Contains(Keys.Z) || keys.Contains(Keys.Y))
                && state.MouseState.LeftButton != ButtonState.Pressed)
            {
                lot.Waiting = keys.Contains(Keys.Y) ? false : !state.ShiftDown;
            }

            if (!Quiet(lot, vm)) return;
            if (lot.What != null && lot.History.Started) Keep(lot, vm);
            if (lot.Waiting != null)
            {
                var undo = lot.Waiting.Value;
                lot.Waiting = null;
                Step(screen, vm, lot, undo);
            }
        }

        /// <summary>A change is finished: keep the lot as it is now.</summary>
        private static void Keep(LotUndo lot, VM vm)
        {
            var moment = LotMoments.Take(vm);
            var budget = LotMoments.Budget(vm);
            var cost = Ledger.CostOf(lot.BudgetThen, budget);
            lot.History.Add(moment, moment.Size, lot.What, cost);
            Log("Kept: " + lot.What + (cost != 0 ? " (" + Ledger.Money(cost) + ")" : "") + ", " + Kb(moment.Size)
                + "; " + lot.History.UndoCount + " to undo, " + Kb(lot.History.Bytes) + " in all");
            lot.BudgetThen = budget;
            lot.What = null;
        }

        /// <summary>Undo (or redo) one change. Returns what happened, in a few words, or why nothing did.</summary>
        private static string Step(TS1GameScreen screen, VM vm, LotUndo lot, bool undo)
        {
            var history = lot.History;
            var notTakingPart = PackParts.NotTakingPart();
            if (notTakingPart.Count > 0)
            {
                var why = "These packs keep their own part of a lot and cannot put it back, so undoing would leave the lot and them out of step:\n\n"
                    + string.Join("\n", notTakingPart) + "\n\nUndo and redo are off while they are on.";
                Log("Refused: " + string.Join("; ", notTakingPart.Select(PackParts.Why)));
                Tell(undo ? "Cannot Undo" : "Cannot Redo", why);
                lot.History.Clear(); //(a pack's code stopped since build mode was opened: nothing kept can be put back)
                return why;
            }
            var step = undo ? history.NextUndo : history.NextRedo;
            if (step == null)
            {
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                return undo ? "Nothing to undo." : "Nothing to redo.";
            }

            //what is on the lot now comes back with redo (or undo): keep it as it is, in case something changed that was not seen
            var holder = screen.LotControl?.ObjectHolder;
            if (holder?.Holding != null) holder.ClearSelected();
            var now = LotMoments.Take(vm);
            var budget = LotMoments.Budget(vm);
            history.Refresh(now, now.Size, Ledger.CostOf(lot.BudgetThen, budget));
            lot.BudgetThen = budget; //(what the budget moved by since is counted now: not again)
            step = undo ? history.NextUndo : history.NextRedo;

            var problem = undo ? Ledger.CannotUndo(budget, step.Cost) : Ledger.CannotRedo(budget, step.Cost);
            if (problem != null)
            {
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                Tell(undo ? "Cannot Undo" : "Cannot Redo", problem);
                return problem;
            }

            var target = undo ? history.Undone() : history.Redone();
            List<string> packsFailed;
            try
            {
                packsFailed = LotMoments.Restore(vm, target.State, screen);
            }
            catch (Exception e)
            {
                Log("Could not " + (undo ? "undo " : "redo ") + step.What + ": " + e);
                //put it back as it was a moment ago, which was taken just now
                if (undo) history.Redone(); else history.Undone();
                try { LotMoments.Restore(vm, now, screen); }
                catch (Exception again) { Log("...nor put the lot back as it was: " + again); }
                Tell(undo ? "Cannot Undo" : "Cannot Redo", "Something went wrong, and the lot was put back as it was. What happened is in Undo.log in the user directory.");
                return "Something went wrong.";
            }

            if (packsFailed.Count > 0)
            {
                //the lot is put back, but these packs may not agree with it. nothing more is undone this session
                history.Clear();
                Tell(undo ? "Undo" : "Redo", "The lot was put back, but these packs could not put their part of it back, and may no longer agree with it:\n\n"
                    + string.Join("\n", packsFailed) + "\n\nUndo is off from now on. If anything of theirs looks wrong, leave the lot without saving. What happened is in Undo.log in the user directory.");
            }
            if (budget != null) LotMoments.SetBudget(vm, Ledger.After(budget.Value, step.Cost, undo));
            lot.BudgetThen = LotMoments.Budget(vm);
            lot.What = null;
            lot.LastActivity = lot.Ticks;
            HITVM.Get().PlaySoundEvent(((undo ? step.Cost : -step.Cost) > 0) ? UISounds.MoneyBack : UISounds.Click);
            var said = (undo ? "Undone: " : "Redone: ") + step.What + (step.Cost != 0 ? " (" + Ledger.Money(undo ? step.Cost : -step.Cost) + ")" : "");
            Log(said + "; " + history.UndoCount + " to undo, " + history.RedoCount + " to redo");
            return said;
        }

        // ---- the console ----

        private static bool Command(string[] args, bool undo)
        {
            var screen = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
            var vm = screen?.vm;
            if (vm == null || args.Length > 1) return false;
            var lot = LotFor(vm);
            if (args.Length == 1)
            {
                if (args[0].ToLowerInvariant() != "list") return false;
                var steps = lot.History.Steps();
                var text = steps.Count == 0 ? "No changes yet. Undo keeps the changes made in build and buy mode, until it is closed."
                    : string.Join("\n", steps.Select(x => (x.done ? "" : "(undone) ") + x.what + (x.cost != 0 ? " " + Ledger.Money(x.cost) : "")));
                GameThread.NextUpdate(x => Tell("Changes", text));
                return true;
            }
            if (!lot.Building)
            {
                GameThread.NextUpdate(x => Tell(undo ? "Undo" : "Redo", "Undo and redo work in build and buy mode."));
                return true;
            }
            //(the console's own key presses are still being handled: do it on the next frame, as the keys would)
            lot.Waiting = undo;
            return true;
        }

        // ---- telling ----

        private static string LogPath => Path.Combine(FSOEnvironment.UserDir, "Undo.log");

        public static void Log(string text)
        {
            Console.WriteLine("Undo: " + text);
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine); }
            catch { }
        }

        private static string Kb(long bytes) => (bytes / 1024) + " KB";

        public static void Tell(string title, string message)
        {
            try
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((b) => alert.Close())
                });
                UIScreen.GlobalShowDialog(alert, true);
            }
            catch (Exception e)
            {
                Console.WriteLine("Undo could not show a message: " + e.Message);
            }
        }
    }
}
