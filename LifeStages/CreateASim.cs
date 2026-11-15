/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Components;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using FSO.Vitaboy;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Panels.CAS;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LifeStages
{
    /// <summary>
    /// Making Sims of every stage in Create a Sim.
    ///
    /// Create a Sim knows two ages, adult and child, and makes a Sim from a code of two letters: m or f, then a or c.
    /// The pack leaves that as it is. In place of its two age buttons there are four, in the order of a life: child
    /// and teen above, adult and elder below. A teen is made as a child and an elder as an adult, and the pack
    /// remembers which stage each Sim was given. When the family is made it writes them into its record, and the
    /// first time the family is played the Sims take on their stage (Life.Reconcile): a teen's age and size, an
    /// elder's age. Create a Sim shows teens larger. The heads offered to adults and to elders are the pack's lists
    /// in heads.json (Heads.cs), and the name of the head chosen is shown under the panel, to fill them in by.
    ///
    /// Every patch here is Harmony's, on Create a Sim's own methods. They are put on together and taken off together:
    /// if one does not fit a version of Simitone, Create a Sim is left as it was, and the rest of the pack runs.
    /// </summary>
    public static class CreateASim
    {
        public const int ButtonSize = 46, ButtonGap = 6;

        /// <summary>What the pack keeps for the Create a Sim panel: the stage chosen, and the buttons it added.</summary>
        private class PanelAges
        {
            public Stage Stage = Stage.Adult;
            public UIStencilButton Teen, Elder;
            /// <summary>The stage's name, under the buttons: the column's title is too narrow for it.</summary>
            public UILabel Name;
            /// <summary>The names of the head and body chosen, under the panel: the names heads.json lists heads by.</summary>
            public UILabel HeadName, BodyName;
        }

        private static readonly ConditionalWeakTable<UISimCASPanel, PanelAges> Panels = new ConditionalWeakTable<UISimCASPanel, PanelAges>();

        /// <summary>The stage each Sim of a family being made was given, until the family is made.</summary>
        private static readonly ConditionalWeakTable<CASFamilyMember, StrongBox<Stage>> Members = new ConditionalWeakTable<CASFamilyMember, StrongBox<Stage>>();
        private static readonly ConditionalWeakTable<SimTemplateCreateInfo, StrongBox<Stage>> Made = new ConditionalWeakTable<SimTemplateCreateInfo, StrongBox<Stage>>();

        public static void SetStageOf(CASFamilyMember member, Stage stage) => Members.AddOrUpdate(member, new StrongBox<Stage>(stage));

        /// <summary>The stage a Sim of the family being made was given. One the pack did not see is what their body says.</summary>
        public static Stage StageOf(CASFamilyMember member)
        {
            if (member != null && Members.TryGetValue(member, out var box)) return box.Value;
            return member != null && member.Gender > 1 ? Stage.Child : Stage.Adult;
        }

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(CreateASim), name);

        private static MethodBase Need(MethodBase method, string name) => method ?? throw new Exception(name + " was not found");

        public static void Apply(Harmony harmony)
        {
            var panel = typeof(UISimCASPanel);
            var screen = typeof(TS1CASScreen);
            harmony.Patch(Need(AccessTools.Constructor(panel, Type.EmptyTypes), "UISimCASPanel()"), postfix: Mine(nameof(AfterPanelMade)));
            harmony.Patch(Need(AccessTools.Method(panel, "UpdateType"), "UISimCASPanel.UpdateType"), prefix: Mine(nameof(BeforeUpdateType)), postfix: Mine(nameof(AfterUpdateType)));
            harmony.Patch(Need(AccessTools.Method(panel, "SetTab"), "UISimCASPanel.SetTab"), postfix: Mine(nameof(AfterSetTab)));
            harmony.Patch(Need(AccessTools.Method(screen, "BuildMember"), "TS1CASScreen.BuildMember"), postfix: Mine(nameof(AfterBuildMember)));
            harmony.Patch(Need(AccessTools.Method(screen, "PrepareEdit"), "TS1CASScreen.PrepareEdit"), prefix: Mine(nameof(BeforePrepareEdit)), postfix: Mine(nameof(AfterPrepareEdit)));
            harmony.Patch(Need(AccessTools.Method(screen, "PopulateSimType"), "TS1CASScreen.PopulateSimType"), postfix: Mine(nameof(AfterPopulateSimType)));
            harmony.Patch(Need(AccessTools.Method(screen, "Update"), "TS1CASScreen.Update"), postfix: Mine(nameof(AfterScreenUpdate)));
            harmony.Patch(Need(AccessTools.Method(screen, "SetFamilyMember"), "TS1CASScreen.SetFamilyMember"), postfix: Mine(nameof(AfterSetFamilyMember)));
            harmony.Patch(Need(AccessTools.Method(screen, "CASToNeighGen"), "TS1CASScreen.CASToNeighGen"), postfix: Mine(nameof(AfterCASToNeighGen)));
            harmony.Patch(Need(AccessTools.Method(typeof(SimitoneNeighbourGenerator), "CreateFamily", new[] { typeof(string), typeof(int), typeof(SimTemplateCreateInfo[]) }),
                "SimitoneNeighbourGenerator.CreateFamily"), postfix: Mine(nameof(AfterCreateFamily)));
            ShowAgain = AccessTools.Method(screen, "PopulateReal") ?? throw new Exception("TS1CASScreen.PopulateReal was not found");
            OfferAgain = AccessTools.Method(screen, "PopulateSimType") as MethodInfo ?? throw new Exception("TS1CASScreen.PopulateSimType was not found");
        }

        /// <summary>
        /// Create a Sim's own ways to show its heads and bodies again: PopulateReal fills the carousels as they are
        /// when unturned, and PopulateSimType chooses what is on offer and then does that. Only PopulateSimType tells
        /// the carousels they are unturned, so the pack uses it, or PopulateReal only straight after it: used alone,
        /// the carousel shows one head while Create a Sim takes another.
        /// </summary>
        private static MethodInfo ShowAgain, OfferAgain;

        // ---- the panel: four buttons in place of two ----

        private static void AfterPanelMade(UISimCASPanel __instance)
        {
            LifeStagesPack.Guard("adding teens and elders to Create a Sim", () =>
            {
                var ages = new PanelAges();
                // the age column is where the two buttons were, 212 to 276 across, between the sex and the skin
                // colour. four smaller ones fill it as two rows, level with the two sex buttons
                const int left = 244 - ButtonSize - ButtonGap / 2, top = 146 - ButtonSize / 2, below = 223 - ButtonSize / 2;
                Place(__instance.SimChildBtn, Stage.Child, left, top);
                ages.Teen = Place(new UIStencilButton(Icon(Stage.Teen)), Stage.Teen, left + ButtonSize + ButtonGap, top);
                Place(__instance.SimAdultBtn, Stage.Adult, left, below);
                ages.Elder = Place(new UIStencilButton(Icon(Stage.Elder)), Stage.Elder, left + ButtonSize + ButtonGap, below);
                __instance.Add(ages.Teen);
                __instance.Add(ages.Elder);

                ages.Name = new UILabel();
                ages.Name.CaptionStyle = __instance.SimAgeTitle.CaptionStyle.Clone();
                ages.Name.CaptionStyle.Size = 12;
                ages.Name.Size = Vector2.One;
                ages.Name.Alignment = TextAlignment.Middle | TextAlignment.Center;
                ages.Name.Position = new Vector2(244, below + ButtonSize + 8);
                __instance.Add(ages.Name);

                UILabel Readout(int y)
                {
                    var label = new UILabel();
                    label.CaptionStyle = __instance.SimAgeTitle.CaptionStyle.Clone();
                    label.CaptionStyle.Size = 14;
                    label.CaptionStyle.Shadow = true;
                    label.Size = Vector2.One;
                    label.Alignment = TextAlignment.Middle | TextAlignment.Center;
                    label.Position = new Vector2(295, y); //the middle of the panel, just under it
                    label.Caption = "";
                    __instance.Add(label);
                    return label;
                }
                ages.HeadName = Readout(285);
                ages.BodyName = Readout(305);

                // the game's own two still do what they did (child or adult bodies); this says which stage
                __instance.SimChildBtn.OnButtonClick += (b) => Choose(__instance, Stage.Child);
                __instance.SimAdultBtn.OnButtonClick += (b) => Choose(__instance, Stage.Adult);
                ages.Teen.OnButtonClick += (b) => Choose(__instance, Stage.Teen);
                ages.Elder.OnButtonClick += (b) => Choose(__instance, Stage.Elder);
                ages.Teen.Visible = ages.Elder.Visible = ages.Name.Visible = __instance.ActiveTab == 0;
                Panels.Add(__instance, ages);
                Show(__instance, ages);
            });
        }

        private static UIStencilButton Place(UIStencilButton button, Stage stage, int x, int y)
        {
            button.Texture = Icon(stage); //the game's own are a size larger
            button.Position = new Vector2(x, y);
            button.Tooltip = char.ToUpperInvariant(Stages.Name(stage)[0]) + Stages.Name(stage).Substring(1);
            return button;
        }

        private static readonly Dictionary<Stage, Texture2D> Icons = new Dictionary<Stage, Texture2D>();

        private static Texture2D Icon(Stage stage)
        {
            if (Icons.TryGetValue(stage, out var made) && !made.IsDisposed) return made;
            var alpha = Figures.Icon(stage, ButtonSize);
            var data = alpha.Select(a => Color.White * (a / 255f)).ToArray(); //premultiplied, as Simitone's own art is
            made = new Texture2D(GameFacade.GraphicsDevice, ButtonSize, ButtonSize);
            made.SetData(data);
            Icons[stage] = made;
            return made;
        }

        /// <summary>The player chose a stage. A teen or an elder changes the body as a child or an adult would.</summary>
        private static void Choose(UISimCASPanel panel, Stage stage)
        {
            LifeStagesPack.Guard("choosing a stage in Create a Sim", () =>
            {
                if (!Panels.TryGetValue(panel, out var ages)) return;
                var before = ages.Stage;
                ages.Stage = stage;
                var body = Stages.IsChildToGame(stage) ? 'c' : 'a';
                if (panel.AType[1] != body)
                {
                    //as the game's own button does: the heads and bodies change, and are shown again
                    panel.AType = panel.AType.Substring(0, 1) + body + panel.AType.Substring(2);
                    panel.UpdateType();
                }
                else
                {
                    Show(panel, ages);
                    //same body, another stage: an adult and an elder are offered different heads
                    if ((before == Stage.Elder) != (stage == Stage.Elder) && UIScreen.Current is TS1CASScreen screen && screen.vm != null)
                        OfferAgain.Invoke(screen, new object[] { screen.CurrentCode });
                }
                if (UIScreen.Current is TS1CASScreen shown) Size(shown, ages.Stage);
            });
        }

        /// <summary>
        /// The panel is about to tell Create a Sim what to offer. A body changed some other way (the game's own child
        /// and adult buttons) moves the stage with it first, so that a child is never offered an elder's heads.
        /// </summary>
        private static void BeforeUpdateType(UISimCASPanel __instance)
        {
            if (Panels.TryGetValue(__instance, out var ages) && __instance.AType?.Length >= 2)
                ages.Stage = Stages.Suiting(ages.Stage, __instance.AType[1] == 'c');
        }

        private static void AfterUpdateType(UISimCASPanel __instance)
        {
            if (Panels.TryGetValue(__instance, out var ages)) Show(__instance, ages);
        }

        /// <summary>Which button is lit, and the stage's name over them.</summary>
        private static void Show(UISimCASPanel panel, PanelAges ages)
        {
            panel.SimChildBtn.Selected = ages.Stage == Stage.Child;
            panel.SimAdultBtn.Selected = ages.Stage == Stage.Adult;
            ages.Teen.Selected = ages.Stage == Stage.Teen;
            ages.Elder.Selected = ages.Stage == Stage.Elder;
            var name = Stages.Name(ages.Stage);
            if (ages.Name != null) ages.Name.Caption = char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static void AfterSetTab(UISimCASPanel __instance, int tab)
        {
            if (!Panels.TryGetValue(__instance, out var ages)) return;
            ages.Teen.Visible = ages.Elder.Visible = tab == 0;
            if (ages.Name != null) ages.Name.Visible = tab == 0;
        }

        private static PanelAges AgesOf(TS1CASScreen screen) => screen?.CASPanel != null && Panels.TryGetValue(screen.CASPanel, out var ages) ? ages : null;

        /// <summary>
        /// The Sims on offer at the stage's size: a teen larger than a child. Heads and bodies stand in two rings at
        /// the same places, a head drawn on each body, so both are made larger together and stay joined.
        /// </summary>
        private static void Size(TS1CASScreen screen, Stage stage)
        {
            var scale = Stages.Size(stage) / 100f;
            foreach (var ring in new[] { screen.HeadAvatars, screen.BodyAvatars })
            {
                if (ring == null) continue;
                foreach (var sim in ring)
                    if (sim?.WorldUI is AvatarComponent drawn) drawn.Scale = scale;
            }
        }

        /// <summary>
        /// What Create a Sim offers has been chosen. For adults and elders the pack keeps the heads for the stage, by
        /// heads.json. New lists, not the old ones changed: Create a Sim's lists are the game's own, which the rest of
        /// the game uses too.
        /// </summary>
        private static void AfterPopulateSimType(TS1CASScreen __instance, string simtype)
        {
            LifeStagesPack.Guard("sorting the heads in Create a Sim", () =>
            {
                var ages = AgesOf(__instance);
                if (ages == null || simtype == null || simtype.Length < 2 || __instance.ActiveHeads == null) return;
                if (simtype[1] == 'a')
                {
                    var heads = __instance.ActiveHeads;
                    var textures = __instance.ActiveHeadTex;
                    var offer = Heads.Offer(heads, ages.Stage);
                    if (offer.Count < heads.Count)
                    {
                        __instance.ActiveHeads = offer.Select(i => heads[i]).ToList();
                        __instance.ActiveHeadTex = offer.Select(i => textures[i]).ToList();
                        ShowAgain.Invoke(__instance, null); //straight after PopulateSimType: the carousels are still unturned
                    }
                }
                Size(__instance, ages.Stage);
            });
        }

        // ---- the Sims being made ----

        private static void AfterBuildMember(TS1CASScreen __instance, CASFamilyMember __result)
        {
            var ages = AgesOf(__instance);
            if (ages != null && __result != null) SetStageOf(__result, Stages.Suiting(ages.Stage, __result.Gender > 1));
        }

        /// <summary>
        /// A Sim is opened to be changed, or a new one begun: their stage is chosen before Create a Sim chooses what
        /// to offer, so that the heads offered are the stage's and the Sim's own head is among them.
        /// </summary>
        private static void BeforePrepareEdit(TS1CASScreen __instance, int i)
        {
            var ages = AgesOf(__instance);
            if (ages == null) return;
            ages.Stage = i > -1 && i < __instance.WIPFamily.Count ? StageOf(__instance.WIPFamily[i]) : Stage.Adult;
        }

        private static void AfterPrepareEdit(TS1CASScreen __instance)
        {
            LifeStagesPack.Guard("opening a Sim in Create a Sim", () =>
            {
                var ages = AgesOf(__instance);
                if (ages == null) return;
                ages.Stage = Stages.Suiting(ages.Stage, __instance.CASPanel.AType[1] == 'c');
                Show(__instance.CASPanel, ages);
                Size(__instance, ages.Stage);
            });
        }

        /// <summary>
        /// While a Sim is being made, the head and body chosen are named under the panel: the names heads.json uses,
        /// and Create a Sim takes, for the one in front.
        /// </summary>
        private static void AfterScreenUpdate(TS1CASScreen __instance, UICASMode ___Mode)
        {
            var ages = AgesOf(__instance);
            if (ages?.HeadName == null) return;
            var editing = ___Mode == UICASMode.SimEdit;
            Name(ages.HeadName, "Head: ", editing ? __instance.ActiveHeads : null, __instance.HeadPosition);
            Name(ages.BodyName, "Body: ", editing ? __instance.ActiveBodies : null, __instance.BodyPosition);
        }

        private static void Name(UILabel label, string what, List<string> list, float position)
        {
            var show = list != null && list.Count > 0;
            label.Visible = show;
            if (!show) return;
            //as Create a Sim's BuildMember takes them
            var caption = what + list[(int)DirectionUtils.PosMod(Math.Round(position + 8), list.Count)];
            if (label.Caption != caption) label.Caption = caption;
        }

        /// <summary>A Sim of the family being made, standing with the others: at their stage's size.</summary>
        private static void AfterSetFamilyMember(TS1CASScreen __instance, int index)
        {
            LifeStagesPack.Guard("showing the family being made", () =>
            {
                if (index < 0 || index >= __instance.WIPFamily.Count || index >= __instance.RepresentFamily.Count) return;
                var data = __instance.WIPFamily[index];
                var sim = __instance.RepresentFamily[index];
                var stage = StageOf(data);
                if (sim.WorldUI is AvatarComponent drawn) drawn.Scale = Stages.Size(stage) / 100f;
            });
        }

        /// <summary>A Sim of the family is about to be made: what the game writes for them says their age.</summary>
        public static void AfterCASToNeighGen(CASFamilyMember x, SimTemplateCreateInfo __result)
        {
            if (x == null || __result == null) return;
            var stage = Stages.Suiting(StageOf(x), x.Gender > 1);
            Made.AddOrUpdate(__result, new StrongBox<Stage>(stage));
            __result.BodyStringReplace[13] = Stages.GameAge(stage).ToString();
        }

        /// <summary>The family has been made: the pack's record learns each Sim's stage.</summary>
        public static void AfterCreateFamily(FAMI __result, SimTemplateCreateInfo[] infos)
        {
            LifeStagesPack.Guard("keeping the stages of a new family", () =>
            {
                if (__result?.FamilyGUIDs == null || infos == null) return;
                var born = new List<(uint, Stage)>();
                for (int i = 0; i < infos.Length && i < __result.FamilyGUIDs.Length; i++)
                {
                    if (infos[i] != null && Made.TryGetValue(infos[i], out var stage)) born.Add((__result.FamilyGUIDs[i], stage.Value));
                }
                Life.Born(born);
            });
        }
    }
}
