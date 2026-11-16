/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CutContent
{
    /// <summary>
    /// The cleaning skill.
    ///
    /// The 1.0 base game's Behavior.iff lists ten skills in the order of person data 9 to 18: Cleaning, Cooking, Social,
    /// Repair, Gardening, Music, Creative, Literacy, Physical, Logic. Gardening, music and literacy were marked "old" before
    /// release and left nothing but their names; six were shipped. Cleaning was kept, hidden: the global "clean inc" (304),
    /// which the generic "Clean" (302) and the showers call each time round, takes dirt off the object by ten times the
    /// Sim's cleaning skill, or ten times their Neat personality if the skill is 0. NPCs are given it (the maid and the
    /// janitors 1000); player Sims never learn it, and no panel shows it.
    ///
    /// This finishes it:
    ///   - each time round "clean inc", a step of the pack's teaches a household Sim a little cleaning, up to 1000 (ten
    ///     points). A Sim's first cleaning starts the skill at their Neat score, so learning never makes a neat Sim slower;
    ///   - the skills panel shows Cleaning beside the other six.
    /// NPCs keep the cleaning they are given. The skill is the Sim's own person data, so it is saved with them.
    /// </summary>
    public static class Cleaning
    {
        public const string PrimitiveName = "cutcontent_learn_clean";
        public const int Gain = 2;          //per time round "clean inc": about a point (100) every 50 times round
        public const int Max = 1000;
        private const VMPersonDataVariable Skill = (VMPersonDataVariable)9, Neat = VMPersonDataVariable.NeatPersonality;

        public static string Status = "the global \"clean inc\" not seen yet";
        public static int Lessons;
        private static bool Done;

        /// <summary>The first time anything is created, the global file is loaded: finish "clean inc".</summary>
        public static void Prepare()
        {
            if (Done) return;
            Done = true;
            try
            {
                var global = Content.Get().WorldObjectGlobals.Get("global")?.Resource;
                var clean = global?.Get<BHAV>(304);
                if (clean == null) { Status = "the global \"clean inc\" was not found"; return; }
                Status = Fix(global, clean);
            }
            catch (Exception e) { Status = "failed: " + e.Message; }
            CutContentPack.Log("cleaning: " + Status);
        }

        private static string Fix(GameIffResource res, BHAV clean)
        {
            var steps = clean.Instructions;
            //as the survey found it: step 0 asks whether the skill is above 0, and no step goes back to it
            var first = new byte[] { 9, 0, 0, 0, 0, 0, 0x12, 7 };
            if ((clean.ChunkLabel ?? "").TrimEnd('\0') != "clean inc" || steps.Length != 7 || steps[0].Opcode != 2 || !steps[0].Operand.Take(8).SequenceEqual(first)
                || steps.Any(x => x.TruePointer == 0 || x.FalsePointer == 0))
                return "the global \"clean inc\" is not as the survey found it, so it was left alone";
            var changed = new BHAVInstruction[8];
            for (int i = 1; i < 7; i++) changed[i] = steps[i];
            changed[7] = steps[0];                                                    //the old first step, moved to the end
            changed[0] = Scripts.Step(PrimitiveName, new byte[8], 7, 7);              //learn, then on as before
            clean.Instructions = changed;
            Scripts.Reassemble(res, clean);
            return "restored: household Sims learn cleaning as they clean";
        }

        public class LearnPrimitive : VMPrimitiveHandler
        {
            public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand operand)
            {
                try
                {
                    var sim = context.Caller as VMAvatar;
                    var family = context.VM.TS1State?.CurrentFamily;
                    if (sim == null || sim.IsPet || family == null || sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != family.ChunkID) return VMPrimitiveExitCode.GOTO_TRUE;
                    int skill = sim.GetPersonData(Skill);
                    if (skill <= 0) skill = Math.Max(0, (int)sim.GetPersonData(Neat)); //start where their neatness already has them
                    skill = Math.Min(Max, skill + Gain);
                    sim.SetPersonData(Skill, (short)skill);
                    Lessons++;
                }
                catch (Exception e) { CutContentPack.Log("cleaning: " + e.Message); }
                return VMPrimitiveExitCode.GOTO_TRUE;
            }
        }

        // ---- the skills panel ----

        private static ConditionalWeakTable<UIJobSubpanel, UISkillDisplay> Bars = new ConditionalWeakTable<UIJobSubpanel, UISkillDisplay>();

        /// <summary>After the skills panel is made: a Cleaning bar, as a fourth column beside the others.</summary>
        public static void AfterPanel(UIJobSubpanel __instance)
        {
            try
            {
                const float x = 334 + 3 * 140;
                var label = new UILabel { Caption = "Cleaning", Position = new Vector2(x - 2, 11) };
                label.CaptionStyle = label.CaptionStyle.Clone();
                label.CaptionStyle.Color = UIStyle.Current.Text;
                label.CaptionStyle.Size = 15;
                __instance.Add(label);
                var bar = new UISkillDisplay { Position = new Vector2(x, 35) };
                __instance.Add(bar);
                Bars.AddOrUpdate(__instance, bar);
            }
            catch (Exception e) { CutContentPack.Log("cleaning: couldn't add the bar: " + e.Message); }
        }

        /// <summary>After the skills panel updates: the selected Sim's cleaning, in points.</summary>
        public static void AfterPanelUpdate(UIJobSubpanel __instance)
        {
            if (!Bars.TryGetValue(__instance, out var bar)) return;
            var sim = __instance.Game?.SelectedAvatar;
            if (sim != null) bar.Value = sim.GetPersonData(Skill) / 100;
        }
    }
}
