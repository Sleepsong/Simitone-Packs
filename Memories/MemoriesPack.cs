/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Content.TS1;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Primitives;
using HarmonyLib;
using Microsoft.Xna.Framework;
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

namespace Memories
{
    /// <summary>
    /// Memories, after The Sims 2: a timeline for each Sim of what has happened to them.
    ///
    /// Most of the pack is the layer under the timeline, which watches play for life events (Watching.cs) and tells
    /// whoever wants to know (LifeEvents.cs): this pack's own memories (Remembering.cs), and other packs. The engine is
    /// not changed. The pack hangs on it with Harmony:
    ///
    ///  1. A lot has ticked: the neighbourhood's day, and a look at the Sims on the lot (jobs, ages, ghosts, skills).
    ///  2. The game has loaded a neighbourhood, or a lot is left: forget what was not saved.
    ///  3. The game has saved: keep the memories with it.
    ///
    /// and, each of which the pack can do without:
    ///
    ///  4. An interaction begins and ends, or is cut short: kisses, and every interaction for other packs.
    ///  5. A script makes an object, and a character from it: babies, and the children they grow into.
    ///  6. A Sim is added to the household or taken out of it: weddings, and deaths.
    ///  7. A fire starts or spreads.
    ///  8. A script removes an object: what a burglar takes.
    ///  9. The ways into the window: a button in Options, the F7 key, and the console.
    ///
    /// Of Simitone's own pack calls it uses only OnLoad, the first there was, so it runs on any Simitone that can
    /// load a pack with code.
    /// </summary>
    public class MemoriesPack : ISimitonePack
    {
        public const string Command = "memories";
        private const string ApartmentsPack = "sample.apartments";
        private const string FamilyTreePack = "sample.familytree";

        public void OnLoad(Pack pack)
        {
            MemoryBook.PackId = pack.Id;
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
            LifeEvents.Happened += MemoryBook.Hear;
            PackManager.RegisterCommand(Command, Run);
            Log.Write("Memories loaded. In a lot: Options > Memories, the F7 key, or \"" + Command + "\" in the console (" + Command + " help).");
        }

        /// <summary>Nothing this pack does may take the game down with it. Whatever goes wrong is written to the log, and the game carries on.</summary>
        public static bool Guard(string doing, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (++Failures <= MostFailuresLogged) Log.Write("Something went wrong while " + doing + ": " + e);
                if (Failures == MostFailuresLogged) Log.Write("(No more of these will be written down this session.)");
                return false;
            }
        }

        private const int MostFailuresLogged = 20;
        private static int Failures;

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(MemoriesPack), name);
        private static HarmonyMethod Watch(string name) => new HarmonyMethod(typeof(Watching), name);

        private static MethodBase Found(MethodBase method, string what)
        {
            return method ?? throw new Exception(what + " was not found: this pack was made for a different version of Simitone");
        }

        private static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            var method = parameters.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, parameters);
            return (MethodInfo)Found(method, type.Name + "." + name);
        }

        private static void Apply(Harmony harmony)
        {
            // 1-3
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(Forget)));
            harmony.Patch(Find(typeof(TS1GameScreen), "Save"), postfix: Mine(nameof(AfterSave)));
            Optional(() => harmony.Patch(Find(typeof(TS1GameScreen), "ExitLot"), postfix: Mine(nameof(Forget))),
                "What happens on a lot that is left without saving is kept until Simitone is closed");

            // 4
            Optional(() =>
            {
                harmony.Patch(Find(typeof(VMThread), "ExecuteAction"), postfix: Watch(nameof(Watching.AfterExecuteAction)));
                harmony.Patch(Find(typeof(VMThread), "EndCurrentInteraction"), prefix: Watch(nameof(Watching.BeforeEndInteraction)));
                harmony.Patch(Find(typeof(VMThread), "AbortCurrentInteraction"), prefix: Watch(nameof(Watching.BeforeAbort)), finalizer: Watch(nameof(Watching.AfterAbort)));
            }, "No kisses are seen, and other packs hear of no interactions");
            // 5
            Optional(() =>
            {
                harmony.Patch(Find(typeof(VMCreateObjectInstance), "Execute"), prefix: Watch(nameof(Watching.BeforeCreateObject)), finalizer: Watch(nameof(Watching.AfterCreateObject)));
                harmony.Patch(Found(AccessTools.Method(typeof(VMContext), "CreateObjectInstance", new[] { typeof(uint), typeof(LotTilePos), typeof(Direction), typeof(short), typeof(short), typeof(bool) }),
                    "VMContext.CreateObjectInstance"), postfix: Watch(nameof(Watching.AfterObjectMade)));
                harmony.Patch(Find(typeof(VMTS1MakeNewCharacter), "Execute"), prefix: Watch(nameof(Watching.BeforeMakeCharacter)), postfix: Watch(nameof(Watching.AfterMakeCharacter)));
            }, "No babies are seen");
            // 6
            Optional(() => harmony.Patch(Find(typeof(VMGenericTS1Call), "Execute"), prefix: Watch(nameof(Watching.BeforeGameCall)), postfix: Watch(nameof(Watching.AfterGameCall))),
                "No weddings are seen, and a death only if the Sim becomes a ghost");
            // 7
            Optional(() => harmony.Patch(Find(typeof(VMBurn), "Execute"), postfix: Watch(nameof(Watching.AfterBurn))), "No fires are seen");
            // 8
            Optional(() => harmony.Patch(Find(typeof(VMRemoveObjectInstance), "Execute"), prefix: Watch(nameof(Watching.BeforeRemoveObject))),
                "A burglary is seen, but only what the burglar is seen carrying away counts as taken");
            // 9
            Optional(() => harmony.Patch(Find(typeof(UILotControl), "Update"), postfix: Mine(nameof(AfterLotControlUpdate))), "The F7 key does nothing (the console still opens the window: " + Command + ")");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "AddPacksButton"), postfix: Mine(nameof(AfterPacksButton))), "There is no Memories button in Options (the console still opens the window: " + Command + ")");
        }

        private static void Optional(Action patch, string without)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                Log.Write(without + ": " + e.Message);
            }
        }

        private static void AfterTick(VM __instance)
        {
            Guard("watching the lot", () => Watching.AfterTick(__instance));
        }

        private static void AfterSave()
        {
            Guard("keeping the memories", MemoryBook.Saved);
        }

        private static void Forget()
        {
            MemoryBook.Forget();
        }

        // ---- 9 ----

        private static void AfterLotControlUpdate(UpdateState state)
        {
            if (state?.NewKeys != null && state.NewKeys.Contains(Keys.F7)) Guard("opening Memories", () => UIMemoriesDialog.Open());
        }

        /// <summary>Simitone has just put its Packs button in the Options panel. Put a Memories button after it, and after the other packs' own.</summary>
        private static void AfterPacksButton(UISubpanel parent)
        {
            Guard("adding the Memories button to Options", () =>
            {
                int index = 5 + (PackManager.IsActive(ApartmentsPack) ? 1 : 0) + (PackManager.IsActive(FamilyTreePack) ? 1 : 0);
                float buttonX = 50 + index * 120f;
                float labelX = buttonX - 27;

                var label = new UILabel();
                label.Caption = "Memories";
                label.Alignment = TextAlignment.Middle | TextAlignment.Center;
                label.Wrapped = true;
                label.Position = new Vector2(labelX - 77, 106);
                label.Size = new Vector2(120, 1);
                label.CaptionStyle = label.CaptionStyle.Clone();
                label.CaptionStyle.Size = 12;
                label.CaptionStyle.Color = UIStyle.Current.Text;

                //the pack has no artwork of its own: the Packs button's picture, under its own name
                var button = new UICatButton(Content.Get().CustomUI.Get("opt_packs.png").Get(GameFacade.GraphicsDevice));
                button.Position = new Vector2(buttonX - 50, 16);
                button.OnButtonClick += (btn) => Guard("opening Memories", () => UIMemoriesDialog.Open());

                GameFacade.Screens.Tween.To(label, 0.5f, new Dictionary<string, float>() { { "X", labelX } }, TweenQuad.EaseOut);
                GameFacade.Screens.Tween.To(button, 0.5f, new Dictionary<string, float>() { { "X", buttonX } }, TweenQuad.EaseOut);

                parent.Add(label);
                parent.Add(button);
            });
        }

        // ---- the console ----

        private const string Help =
            "memories - the selected Sim's memories\n" +
            "memories [name] - a Sim of your household's memories\n" +
            "memories events - the last life events seen, newest first\n" +
            "memories day - the neighbourhood's day";

        private static bool Run(string[] words)
        {
            var understood = true;
            Guard("running the memories command", () =>
            {
                var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
                switch (word)
                {
                    case "help":
                        UIMemoriesDialog.Say("Memories", Help);
                        break;
                    case "events":
                        var recent = LifeEvents.Recent.Where(e => e.Kind != LifeEventKind.Interaction || e.Other != 0).Reverse().Take(15).Select(Describe).ToList();
                        UIMemoriesDialog.Say("Life events", recent.Count == 0 ? "None seen since Simitone started." : string.Join("\n", recent));
                        break;
                    case "day":
                        var journal = MemoryBook.Journal;
                        UIMemoriesDialog.Say("Memories", journal == null ? "There is no neighbourhood loaded." : "The neighbourhood is on day " + journal.Day + ". " + journal.Memories.Count + " memories are kept.");
                        break;
                    case "":
                        UIMemoriesDialog.Open();
                        break;
                    default:
                        var name = string.Join(" ", words).Trim();
                        var found = UIMemoriesDialog.HouseholdNow().Where(g => People.FirstName(g).StartsWith(name, StringComparison.OrdinalIgnoreCase)
                            || People.FullName(g).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (found.Count == 1) UIMemoriesDialog.Open(found[0]);
                        else if (found.Count == 0) understood = false;
                        else UIMemoriesDialog.Say("Memories", "More than one Sim is called that: " + string.Join(", ", found.Select(People.FullName)) + ".");
                        break;
                }
            });
            return understood;
        }

        /// <summary>A life event in a line, for the console.</summary>
        public static string Describe(LifeEvent e)
        {
            var who = new List<string>();
            if (e.Sim != 0) who.Add(People.FirstName(e.Sim));
            if (e.Other != 0) who.Add(People.FirstName(e.Other));
            var line = e.Kind + (who.Count > 0 ? ": " + string.Join(" and ", who) : "");
            if (!string.IsNullOrWhiteSpace(e.Detail)) line += " (" + e.Detail.Replace("\n", ", ") + ")";
            return line + ", " + e.When;
        }
    }
}
