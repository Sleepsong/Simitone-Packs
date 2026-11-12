/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Primitives;
using FSO.SimAntics.Utils;
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
using System.Runtime.CompilerServices;

namespace FamilyTree
{
    /// <summary>
    /// The Sims 2's family tree, for The Sims 1, added entirely from a pack.
    ///
    /// The Sims 1 keeps no family ties at all: only how well two Sims get on. So the tree is the pack's own, kept in
    /// its own file for each neighbourhood (KinStore), and the game's files and scripts are left as they are. The
    /// rules of the tree know nothing about the game (Kin). What is here is where the two meet, as Harmony patches:
    ///
    ///  1. Create a Family draws its screen: put a Family ties button on it, for choosing who is married to whom and
    ///     whose child is whose before the family is made.
    ///  2. Create a Family takes a Sim out of the family being made: their ties go with them.
    ///  3. The game has made the family: keep the ties that were chosen, and start those Sims off getting on as
    ///     family does (a married couple starts married, not as strangers).
    ///  4. The relationship panel shows a Sim: say what they are to the Sim being played.
    ///  5. The ways into the Family Tree window: a button in Options, and the F8 key.
    ///  6. The game makes a baby, and later a child from it: the child is given its parents (Following).
    ///  7. The game adds an adult to the household: they may be married to the Sim who brought them in (Following).
    ///
    /// Only 3 is needed for a family to be made with ties, with 1 and 2 to reach it. A version of Simitone that the
    /// others do not fit costs only those.
    /// </summary>
    public class FamilyTreePack : ISimitonePack
    {
        public const string Command = "tree";
        private const string ApartmentsPack = "sample.apartments";

        public void OnLoad(Pack pack)
        {
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
            PackManager.RegisterCommand(Command, args =>
            {
                UIFamilyTreeDialog.Open();
                return true;
            });
            People.Log("Family Tree loaded. In a lot: Options > Family Tree, the F8 key, or \"" + Command + "\" in the console.");
        }

        /// <summary>Nothing this pack does may take the game down with it. Whatever goes wrong is written to the terminal, and the game carries on.</summary>
        public static bool Guard(string doing, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (++Failures <= MostFailuresLogged) People.Log("Something went wrong while " + doing + ": " + e);
                return false;
            }
        }

        private const int MostFailuresLogged = 20;
        private static int Failures;

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(FamilyTreePack), name);

        private static MethodBase Found(MethodBase method, string what)
        {
            return method ?? throw new Exception(what + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Found(AccessTools.Method(typeof(TS1CASScreen), "Update"), "TS1CASScreen.Update"), postfix: Mine(nameof(AfterCASUpdate)));
            // 2
            harmony.Patch(Found(AccessTools.Method(typeof(TS1CASScreen), "ModifySim"), "TS1CASScreen.ModifySim"), prefix: Mine(nameof(BeforeModifySim)));
            // 3
            harmony.Patch(Found(AccessTools.Method(typeof(SimitoneNeighbourGenerator), "CreateFamily", new[] { typeof(string), typeof(int), typeof(SimTemplateCreateInfo[]) }),
                "SimitoneNeighbourGenerator.CreateFamily"), postfix: Mine(nameof(AfterCreateFamily)));

            // 4
            Optional(() => harmony.Patch(Found(AccessTools.Constructor(typeof(UIRelationshipDisplay), new[] { typeof(int), typeof(int), typeof(VM) }),
                "UIRelationshipDisplay's constructor"), postfix: Mine(nameof(AfterRelationshipDisplay))), "family ties in the relationship panel");
            // 5
            Optional(() => harmony.Patch(Found(AccessTools.Method(typeof(UILotControl), "Update"), "UILotControl.Update"), postfix: Mine(nameof(AfterLotControlUpdate))), "the F8 key");
            Optional(() => harmony.Patch(Found(AccessTools.Method(typeof(UIMainPanel), "AddPacksButton"), "UIMainPanel.AddPacksButton"), postfix: Mine(nameof(AfterPacksButton))), "the Family Tree button in Options");

            // 6. babies: a script makes an object (which script, and then which object), and later a character from it
            Optional(() =>
            {
                harmony.Patch(Found(AccessTools.Method(typeof(VMCreateObjectInstance), "Execute"), "VMCreateObjectInstance.Execute"),
                    prefix: Theirs(nameof(Following.BeforeCreateObject)), finalizer: Theirs(nameof(Following.AfterCreateObject)));
                harmony.Patch(Found(AccessTools.Method(typeof(VMContext), "CreateObjectInstance", new[] { typeof(uint), typeof(LotTilePos), typeof(Direction), typeof(short), typeof(short), typeof(bool) }),
                    "VMContext.CreateObjectInstance"), postfix: Theirs(nameof(Following.AfterObjectMade)));
                harmony.Patch(Found(AccessTools.Method(typeof(VMTS1MakeNewCharacter), "Execute"), "VMTS1MakeNewCharacter.Execute"),
                    prefix: Theirs(nameof(Following.BeforeMakeCharacter)), postfix: Theirs(nameof(Following.AfterMakeCharacter)));
            }, "babies to the family tree", "Give a new child their parents with Change ties.");
            // 7. an adult joins the household
            Optional(() => harmony.Patch(Found(AccessTools.Method(typeof(VMGenericTS1Call), "Execute"), "VMGenericTS1Call.Execute"),
                prefix: Theirs(nameof(Following.BeforeGameCall)), postfix: Theirs(nameof(Following.AfterGameCall))), "marriages to the family tree", "Marry two Sims with Change ties.");
        }

        private static HarmonyMethod Theirs(string name) => new HarmonyMethod(typeof(Following), name);

        private static void Optional(Action patch, string what, string instead = null)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                People.Log("Could not add " + what + ": " + e.Message + " " + (instead ?? "The console still opens the window: " + Command));
            }
        }

        // ---- 1. the button is the pack's own, one for each Create a Family screen, shown while a family of two or more is being put together ----

        private static readonly ConditionalWeakTable<TS1CASScreen, UIBigButton> TiesButtons = new ConditionalWeakTable<TS1CASScreen, UIBigButton>();
        private const int TiesButtonWidth = 330;

        public static void AfterCASUpdate(TS1CASScreen __instance, UICASMode ___Mode)
        {
            Guard("looking after the Family ties button", () =>
            {
                var screen = __instance;
                var count = screen.WIPFamily.Count;
                Draft.Trim(count);
                if (!TiesButtons.TryGetValue(screen, out var button))
                {
                    button = new UIBigButton(false);
                    button.Width = TiesButtonWidth;
                    button.OnButtonClick += (b) => Guard("opening the family ties", () => Draft.OpenWindow(screen));
                    screen.Add(button);
                    TiesButtons.Add(screen, button);
                }
                //only on the family's own page, and not while the screen slides to or from another
                button.Visible = ___Mode == UICASMode.FamilyEdit && count >= 2 && Math.Abs(screen.FamilySimInterp) < 0.02f;
                button.Caption = Draft.Ties.Links.Count == 0 ? "Family ties" : "Family ties (" + Draft.Ties.Links.Count + ")";
                //under the family's name, at the right
                button.Position = new Vector2(screen.ScreenWidth - TiesButtonWidth - 25, 125);
            });
        }

        // ---- 2 ----

        public static void BeforeModifySim(bool delete, int index)
        {
            if (delete && index >= 0) Draft.Ties.RemoveAt((uint)index);
        }

        // ---- 3 ----

        public static void AfterCreateFamily(FAMI __result, SimTemplateCreateInfo[] infos)
        {
            Guard("keeping the new family's ties", () => Draft.Keep(__result, infos));
        }

        // ---- 4. under the Sim's name, what they are to the Sim whose relationships these are ----

        public static void AfterRelationshipDisplay(int nidFrom, int nid, UILabel ___NameLabel)
        {
            Guard("naming a family tie in the relationship panel", () =>
            {
                var neigh = People.Neighbourhood;
                var from = neigh?.GetNeighborByID((short)nidFrom);
                var to = neigh?.GetNeighborByID((short)nid);
                if (from == null || to == null || ___NameLabel == null) return;
                var word = TieNames.For(KinStore.Get().Between(from.GUID, to.GUID), People.IsFemale(to.GUID));
                if (word != null) ___NameLabel.Caption = (___NameLabel.Caption ?? "") + "\n" + word;
            });
        }

        // ---- 5 ----

        public static void AfterLotControlUpdate(UpdateState state)
        {
            if (state?.NewKeys != null && state.NewKeys.Contains(Keys.F8)) Guard("opening the Family Tree", () => UIFamilyTreeDialog.Open());
        }

        /// <summary>Simitone has just put its Packs button in the Options panel. Put a Family Tree button after it, made the same way.</summary>
        public static void AfterPacksButton(UISubpanel parent)
        {
            Guard("adding the Family Tree button to Options", () =>
            {
                //after Packs, and after the Apartments pack's button if that pack is in use
                int index = PackManager.IsActive(ApartmentsPack) ? 6 : 5;
                float buttonX = 50 + index * 120f;
                float labelX = buttonX - 27;

                var label = new UILabel();
                label.Caption = "Family Tree";
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
                button.OnButtonClick += (btn) => Guard("opening the Family Tree", () => UIFamilyTreeDialog.Open());

                GameFacade.Screens.Tween.To(label, 0.5f, new Dictionary<string, float>() { { "X", labelX } }, TweenQuad.EaseOut);
                GameFacade.Screens.Tween.To(button, 0.5f, new Dictionary<string, float>() { { "X", buttonX } }, TweenQuad.EaseOut);

                parent.Add(label);
                parent.Add(button);
            });
        }
    }

    /// <summary>How well two Sims get on when their family is made, for each tie. The game's scores run from -100 to 100.</summary>
    public static class StartingScores
    {
        /// <summary>(daily, lifetime), or null for a tie that starts as strangers do.</summary>
        public static (short, short)? For(Tie tie)
        {
            switch (tie)
            {
                case Tie.Spouse: return (90, 80);
                case Tie.Parent:
                case Tie.Child: return (75, 65);
                case Tie.Sibling: return (60, 50);
                case Tie.Grandparent:
                case Tie.Grandchild: return (60, 50);
                case Tie.StepParent:
                case Tie.StepChild: return (40, 30);
                case Tie.AuntUncle:
                case Tie.NieceNephew:
                case Tie.Cousin:
                case Tie.ParentInLaw:
                case Tie.ChildInLaw: return (35, 25);
                default: return null;
            }
        }
    }

    /// <summary>
    /// The ties of the family that is being put together in Create a Family. The Sims have no characters yet, so a
    /// Sim here is their place in the family (0 for the first). When the game makes the family, the ties move to the
    /// neighbourhood's tree under the characters it made.
    /// </summary>
    public static class Draft
    {
        public static Kin Ties = new Kin();

        /// <summary>Ties to places the family no longer has are dropped (the screen was left and a new family started).</summary>
        public static void Trim(int count)
        {
            if (Ties.Links.Count > 0) Ties.Links.RemoveAll(l => l.A >= count || l.B >= count);
        }

        public static void OpenWindow(TS1CASScreen screen)
        {
            var family = screen.WIPFamily;
            string Name(uint i) => (i < family.Count && !string.IsNullOrWhiteSpace(family[(int)i].Name)) ? family[(int)i].Name.Trim() : "Sim " + (i + 1);
            UIFamilyTiesDialog.Open(new TiesSubject()
            {
                Caption = "Family ties",
                Kin = Ties,
                Sims = Enumerable.Range(0, family.Count).Select(i => (uint)i).ToList(),
                Name = Name,
                //adult 0,1... child 2,3
                IsAdult = i => i >= family.Count || family[(int)i].Gender < 2,
                IsFemale = i => i < family.Count && (family[(int)i].Gender & 1) == 1,
            });
        }

        /// <summary>
        /// The game has made a family. If ties were chosen for it, keep them in the neighbourhood's tree, and start
        /// those Sims off getting on as family does.
        /// </summary>
        public static void Keep(FAMI family, SimTemplateCreateInfo[] infos)
        {
            var guids = family?.FamilyGUIDs;
            if (guids == null || infos == null) return;
            Trim(Math.Min(guids.Length, infos.Length));
            if (Ties.Links.Count == 0) return;
            var ties = Ties;
            Ties = new Kin();

            //a Sim may have been changed into a child after their ties were chosen
            foreach (var said in ties.Mend(i => !infos[i].Child, i => infos[i].Name ?? ("Sim " + (i + 1)))) People.Log("A tie was left out: " + said);

            var tree = KinStore.Get();
            foreach (var link in ties.Links)
            {
                tree.Links.RemoveAll(l => l.Joins(guids[link.A], guids[link.B]));
                tree.Links.Add(new KinLink(link.Kind, guids[link.A], guids[link.B]));
            }
            int started = 0;
            for (uint i = 0; i < guids.Length; i++)
            {
                for (uint j = i + 1; j < guids.Length; j++)
                {
                    var scores = StartingScores.For(ties.Between(i, j));
                    if (scores != null && People.RaiseScores(guids[i], guids[j], scores.Value.Item1, scores.Value.Item2)) started++;
                }
            }
            var kept = KinStore.Save();
            //the game saved the neighbourhood as it made the family, before the scores were raised
            if (started > 0) People.Neighbourhood.SaveNeighbourhood(false);
            People.Log("New family: " + ties.Links.Count + " tie(s) " + (kept ? "kept" : "COULD NOT BE SAVED") + ", " + started + " pair(s) start as family.");
        }
    }
}
