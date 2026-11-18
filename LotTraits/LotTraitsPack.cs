/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
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
using System.IO;
using System.Linq;
using System.Reflection;

namespace LotTraits
{
    /// <summary>
    /// Lot Traits, a first stage, after The Sims 4's: a lot can have up to three traits, and each changes how fast
    /// the needs of every Sim on the lot drop, and how they feel about its rooms.
    ///
    /// The engine is not changed. The pack hangs on it in one place it cannot do without, a Harmony patch on the
    /// method where needs drop (NeedRates.cs), and two it can: the F7 key, and a button in Options. The traits are
    /// in traits.json (Traits.cs), and which a lot has is kept with it, in the pack's own file for the lot
    /// (Lots.cs), through Simitone's OnLotLoaded and OnLotSaved.
    /// </summary>
    public class LotTraitsPack : ISimitonePack
    {
        public static string PackId = "sample.lottraits";
        private const string ApartmentsPack = "sample.apartments";
        private const string FamilyTreePack = "sample.familytree";
        public const string Command = "lottraits";

        public void OnLoad(Pack pack)
        {
            PackId = pack.Id;
            var problems = new List<string>();
            Lots.Book = TraitBook.Load(Path.Combine(pack.Folder, "traits.json"), problems);
            foreach (var problem in problems) Log.Write(problem);
            Problems = problems;

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
            NeedRates.EffectFor = (avatar, context) => Lots.EffectOn(context?.VM);
            PackManager.RegisterCommand(Command, Run);
            Log.Write("Lot Traits loaded, with " + Lots.Book.Traits.Count + " traits. In a lot: Options > Lot Traits, the F7 key, or \"" + Command + " help\" in the console.");
        }

        /// <summary>What was wrong with traits.json, for the window to say.</summary>
        public static List<string> Problems = new List<string>();

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(LotTraitsPack), name);

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            //what the pack is for: the rate needs drop
            harmony.Patch(Find(typeof(VMTS1MotiveDecay), "Tick"), prefix: Mine(nameof(BeforeDecay)), postfix: Mine(nameof(AfterDecay)));

            //the ways into the window. without them the console still works
            Optional(() => harmony.Patch(Find(typeof(UILotControl), "Update"), postfix: Mine(nameof(AfterLotControlUpdate))), "the F7 key");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "AddPacksButton"), postfix: Mine(nameof(AfterPacksButton))), "the Lot Traits button in Options");
        }

        private static void Optional(Action patch, string without)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                Log.Write("Without " + without + ": " + e.Message);
            }
        }

        public static void BeforeDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, VMContext context, out bool __state)
        {
            __state = false;
            try
            {
                NeedRates.BeforeDecay(__instance, avatar, context, out __state);
            }
            catch (Exception e)
            {
                __state = false;
                DecayFailed(e);
            }
        }

        public static void AfterDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, bool __state)
        {
            if (!__state) return;
            try
            {
                NeedRates.AfterDecay(__instance, avatar, __state);
            }
            catch (Exception e)
            {
                DecayFailed(e);
            }
        }

        private const int MostDecayFailures = 5;
        private static int DecayFailures;

        /// <summary>The patch runs for every Sim on every tick. If it keeps going wrong, it stops, and needs drop at the game's own rate.</summary>
        private static void DecayFailed(Exception e)
        {
            if (++DecayFailures > MostDecayFailures) return;
            Log.Write("Something went wrong while changing how fast needs drop: " + e);
            if (DecayFailures == MostDecayFailures)
            {
                NeedRates.EffectFor = null;
                Log.Write("That has gone wrong " + MostDecayFailures + " times, so lot traits are off until Simitone is restarted.");
            }
        }

        public static void AfterLotControlUpdate(UpdateState state)
        {
            if (state?.NewKeys != null && state.NewKeys.Contains(Keys.F7)) Guard("opening the Lot Traits window", () => UILotTraitsDialog.Open());
        }

        /// <summary>Simitone has just put its Packs button in the Options panel. Put a Lot Traits button after it, made the same way.</summary>
        public static void AfterPacksButton(UISubpanel parent)
        {
            Guard("adding the Lot Traits button to Options", () =>
            {
                //after Packs, and after the Apartments and Family Tree packs' buttons if those are in use (they place theirs the same way)
                int index = 5 + (PackManager.IsActive(ApartmentsPack) ? 1 : 0) + (PackManager.IsActive(FamilyTreePack) ? 1 : 0);
                float buttonX = 50 + index * 120f;
                float labelX = buttonX - 27;

                var label = new UILabel();
                label.Caption = "Lot Traits";
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
                button.OnButtonClick += (btn) => Guard("opening the Lot Traits window", () => UILotTraitsDialog.Open());

                GameFacade.Screens.Tween.To(label, 0.5f, new Dictionary<string, float>() { { "X", labelX } }, TweenQuad.EaseOut);
                GameFacade.Screens.Tween.To(button, 0.5f, new Dictionary<string, float>() { { "X", buttonX } }, TweenQuad.EaseOut);

                parent.Add(label);
                parent.Add(button);
            });
        }

        // ---- the lot's own file ----

        public void OnLotLoaded(VM vm, string lotPath)
        {
            //a lot not loaded from a file has no traits of its own
            var set = lotPath == null ? new LotTraitSet() : LotTraitSet.Load(LotPackData.Read(PackId, lotPath));
            Lots.Loaded(vm, set);
            if (set.Ids.Count > 0) Log.Write("A lot was loaded with " + Describe(set) + ".");
        }

        public void OnLotSaved(VM vm, string lotPath)
        {
            if (!Lots.Knows(vm)) return; //a lot the pack was not told of: what it had stays as it was
            LotPackData.Write(PackId, lotPath, Lots.Of(vm).Save());
        }

        private static string Describe(LotTraitSet set)
        {
            var names = set.Known(Lots.Book).Select(x => x.Name).ToList();
            names.AddRange(set.Unknown(Lots.Book).Select(x => x + " (not in traits.json)"));
            return names.Count == 0 ? "no traits" : string.Join(", ", names);
        }

        // ---- the console ----

        public static VM CurrentLot => (GameFacade.Screens?.CurrentUIScreen as TS1GameScreen)?.vm;

        private const string Help =
            Command + " - this lot's traits, and what they do\n" +
            Command + " all - every trait there is\n" +
            Command + " add [trait] - give this lot a trait\n" +
            Command + " remove [trait] - take a trait off this lot\n" +
            Command + " clear - take all of them off\n" +
            "The window: Options > Lot Traits, or F7. A lot's traits are kept when the lot is saved.";

        private static bool Run(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the " + Command + " command", () => answer = Answer(words, out understood))) answer = "That went wrong. LotTraits.log says how.";
            if (answer != null) Say("Lot Traits", answer);
            return understood;
        }

        /// <summary>Does what was typed after the command, and returns what to tell the player.</summary>
        public static string Answer(string[] words, out bool understood)
        {
            understood = true;
            var book = Lots.Book;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            var rest = string.Join(" ", words.Skip(1));
            var vm = CurrentLot;
            const string NeedsLot = "This works while you are in a lot.";

            switch (word)
            {
                case "help":
                    return Help;

                case "all":
                    if (book.Traits.Count == 0) return "There are no traits to choose." + ProblemText();
                    return string.Join("\n", book.Traits.Select(x => x.Name + " (" + x.Id + "): " + x.EffectText())) + ProblemText();

                case "":
                case "list":
                    if (vm == null) return NeedsLot;
                    return LotText(vm);

                case "add":
                    if (vm == null) return NeedsLot;
                    var adding = book.Find(rest);
                    if (adding == null) return rest == "" ? "Which trait? For example: " + Command + " add " + (book.Traits.FirstOrDefault()?.Id ?? "grody") : "There is no trait called " + rest + ". To see them all: " + Command + " all";
                    var refused = Lots.Of(vm).Add(adding, book);
                    if (refused != null) return refused;
                    Lots.Refresh(vm);
                    return LotText(vm);

                case "remove":
                    if (vm == null) return NeedsLot;
                    var set = Lots.Of(vm);
                    var removing = book.Find(rest)?.Id ?? rest.Trim().ToLowerInvariant();
                    if (!set.Remove(removing)) return "This lot does not have " + (rest == "" ? "that trait" : rest) + ".";
                    Lots.Refresh(vm);
                    return LotText(vm);

                case "clear":
                    if (vm == null) return NeedsLot;
                    Lots.Of(vm).Ids.Clear();
                    Lots.Refresh(vm);
                    return LotText(vm);

                default:
                    understood = false;
                    return null;
            }
        }

        private static string LotText(VM vm)
        {
            var set = Lots.Of(vm);
            var known = set.Known(Lots.Book);
            var lines = known.Select(x => x.Name + ": " + x.EffectText()).ToList();
            if (lines.Count == 0) lines.Add("This lot has no traits.");
            var unknown = set.Unknown(Lots.Book);
            if (unknown.Count > 0) lines.Add("Kept but not in use, since traits.json does not have them: " + string.Join(", ", unknown) + ".");
            lines.Add("Changes are kept when the lot is saved.");
            return string.Join("\n", lines);
        }

        private static string ProblemText() => Problems.Count == 0 ? "" : "\n" + string.Join("\n", Problems);

        // ---- telling the player ----

        public static void Say(string title, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Log.Write(title + ": " + message.Replace("\n", " | "));
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }

        /// <summary>
        /// Nothing this pack does may take the game down with it. Whatever goes wrong is written to the log and the
        /// game carries on.
        /// </summary>
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
    }

    public static class Log
    {
        private static readonly object Lock = new object();

        public static void Write(string line)
        {
            line = "[lottraits] " + line;
            Console.WriteLine(line);
            try
            {
                lock (Lock) File.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "LotTraits.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
