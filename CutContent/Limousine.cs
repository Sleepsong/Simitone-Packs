/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Components;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CutContent
{
    /// <summary>
    /// Hiring a limousine instead of a cab (Hot Date).
    ///
    /// The surveys found what Maxis left. The Go Downtown phone plugin (GoDowntownPhonePlugin.iff) has texts no script
    /// uses: "Would you prefer a taxi or a limo?" (17), "Excellent. A representative from our prestigious limosine service
    /// will be there with a car shortly." (18) and "We are dreadfully sorry. We are unable to process your limosine service
    /// request due to a lack of funds error..." (19). Its "CT - Plugin Call" (4134) asks "Do you need a ride?" and its
    /// "Generate Cab" (4098) only ever makes the taxi (7ae4654a). Hot Date's own limousine, "Car - Limo" (CarLimo.iff,
    /// c8ee6e7d), has the taxi's shape, 18 parts in 6 rows of 3, and its pictures, but none of the taxi's scripts.
    ///
    /// This finishes it, in memory. After "Do you need a ride?", the cab company asks "Would you prefer a taxi or a
    /// limo?". A limousine costs $150 on top of the fare: without it, the "lack of funds" message, and a taxi comes
    /// instead; with it, the money is taken and the "Excellent" message shown. The car that comes is the taxi, which
    /// works, drawn with Hot Date's limousine pictures, part for part. The plugin remembers the choice in its attribute 20
    /// and the car in its attribute 30, so a limousine is still one after the lot is saved and loaded.
    /// </summary>
    public static class Limousine
    {
        public const string PrimitiveName = "cutcontent_limo";
        public const int Price = 150;
        public static string Status = "the Go Downtown phone plugin not seen yet";
        public static int Hired;

        private const uint LimoGUID = 0xc8ee6e7d, TaxiGUID = 0x7ae4654a;
        private const int Choice = 20, Mark = 30;
        private const short Marked = 1;
        private const string Question = "Would you prefer a taxi or a limo?";

        private static ConditionalWeakTable<BHAV, object> Fixed = new ConditionalWeakTable<BHAV, object>();

        /// <summary>When an object is created: if it is the Go Downtown plugin, finish its scripts.</summary>
        public static void Prepare(VMEntity entity)
        {
            var res = entity?.Object?.Resource;
            if (res == null || entity.Object.OBJ.GUID != 0xa6f31853) return;
            var call = res.Get<BHAV>(4134);
            var generate = res.Get<BHAV>(4098);
            if (call == null || generate == null || Fixed.TryGetValue(call, out _)) return;
            Fixed.AddOrUpdate(call, call);
            Status = Fix(res, call, generate);
            CutContentPack.Log("limousine: " + Status);
        }

        private static string Fix(GameIffResource res, BHAV call, BHAV generate)
        {
            if ((call.ChunkLabel ?? "").TrimEnd('\0') != "CT - Plugin Call" || (generate.ChunkLabel ?? "").TrimEnd('\0') != "Generate Cab")
                return "the plugin's scripts are not as the surveys found them, so they were left alone";
            var steps = call.Instructions;
            var made = generate.Instructions;
            ushort create = Scripts.Opcode("create_object_instance");
            if (steps.Length != 36 || steps[10].Opcode != 4102 || steps[10].TruePointer != 9 || steps[10].FalsePointer != 2
                || made.Length != 11 || made[2].Opcode != create || made[2].TruePointer != 5 || BitConverter.ToUInt32(made[2].Operand, 0) != TaxiGUID)
                return "the plugin's scripts are not as the surveys found them, so they were left alone";

            //the texts: Maxis's three, and two buttons
            var texts = res.Get<STR>(301);
            if (texts == null || texts.Length < 20 || !(texts.GetString(17) ?? "").StartsWith(Question))
                return "the plugin's texts are not as the surveys found them, so it was left alone";
            var taxiButton = AddText(texts, "Taxi");
            var limoButton = AddText(texts, "Limo");
            if (taxiButton > 255 || limoButton > 255) return "no room for the buttons' texts";
            const byte asked = 18, excellent = 19, noFunds = 20, company = 4; //the dialog numbers count texts from 1

            //CT - Plugin Call: after "Do you need a ride?" says yes (step 10), ask; then on to step 9 as before
            var changed = Copy(steps, 43);
            changed[10].TruePointer = 36;
            changed[36] = Scripts.Step("dialog_private", new byte[] { 0, 0, asked, (byte)taxiButton, (byte)limoButton, 1, company, 0 }, 37, 38);
            changed[37] = Scripts.Step("expression", Scripts.Expression(Choice, 0x00, 5, 0, 0x07), 9, 253);           //a taxi
            changed[38] = Scripts.Step("budget", new byte[] { 3, 7, (byte)Price, Price >> 8, 1, 0, 0, 0 }, 40, 39);      //can they pay?
            changed[39] = Scripts.Step("dialog_private", new byte[] { 0, 0, noFunds, 0, 0, 0, company, 0 }, 37, 37);     //no: sorry, and a taxi
            changed[40] = Scripts.Step("budget", new byte[] { 3, 7, (byte)Price, Price >> 8, 0, 0, 0, 0 }, 41, 39);      //take it
            changed[41] = Scripts.Step("dialog_private", new byte[] { 0, 0, excellent, 0, 0, 0, company, 0 }, 42, 42);
            changed[42] = Scripts.Step("expression", Scripts.Expression(Choice, 0x00, 5, 1, 0x07), 9, 253);           //a limousine
            call.Instructions = changed;
            Scripts.Reassemble(res, call);

            //Generate Cab: once the car is made (step 2), mark it if a limousine was chosen, then on to step 5 as before
            var made2 = Copy(made, 12);
            made2[2].TruePointer = 11;
            made2[11] = Scripts.Step(PrimitiveName, new byte[8], 5, 5);
            generate.Instructions = made2;
            Scripts.Reassemble(res, generate);
            return $"restored: the cab company offers a limousine for ${Price} more";
        }

        private static BHAVInstruction[] Copy(BHAVInstruction[] steps, int length)
        {
            var copy = new BHAVInstruction[length];
            for (int i = 0; i < steps.Length; i++)
                copy[i] = new BHAVInstruction { Opcode = steps[i].Opcode, Operand = steps[i].Operand, TruePointer = steps[i].TruePointer, FalsePointer = steps[i].FalsePointer };
            return copy;
        }

        /// <summary>Adds a text at the end of a table and returns its dialog number (counted from 1).</summary>
        private static int AddText(STR texts, string value)
        {
            var at = texts.Length;
            for (int i = 0; i < texts.LanguageSets.Length; i++)
            {
                var set = texts.LanguageSets[i];
                if (set == null || (i > 0 && set.Strings.Length == 0)) continue;
                var list = set.Strings.ToList();
                while (list.Count < at) list.Add(new STRItem("") { LanguageCode = (byte)(i + 1) });
                list.Insert(at, new STRItem(value) { LanguageCode = (byte)(i + 1) });
                set.Strings = list.ToArray();
            }
            return at + 1;
        }

        // ---- the car ----

        public class MarkPrimitive : VMPrimitiveHandler
        {
            public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand operand)
            {
                try
                {
                    //Me is the plugin; the stack object is the car just made
                    if (context.Caller?.GetAttribute(Choice) == 1 && context.StackObject?.MultitileGroup != null)
                    {
                        foreach (var part in context.StackObject.MultitileGroup.Objects)
                        {
                            part.SetAttribute(Mark, Marked);
                            (part as VMGameObject)?.RefreshGraphic();
                        }
                        context.Caller.SetAttribute(Choice, 0);
                        Hired++;
                        CutContentPack.Log("limousine: a limousine is on its way");
                    }
                }
                catch (Exception e)
                {
                    CutContentPack.Log("limousine: couldn't mark the car: " + e.Message);
                }
                return VMPrimitiveExitCode.GOTO_TRUE;
            }
        }

        private static Dictionary<short, GameObject> LimoParts;

        /// <summary>After the engine sets an object's picture: if it is a taxi sent as a limousine, show the limousine's instead.</summary>
        public static void AfterRefreshGraphic(VMGameObject __instance)
        {
            try
            {
                if (__instance.GetAttribute(Mark) != Marked || !(__instance.WorldUI is ObjectComponent world)) return;
                var obj = __instance.Object?.OBJ;
                if (obj == null) return;
                if (LimoParts == null) LimoParts = FindLimo();
                if (!LimoParts.TryGetValue(obj.SubIndex, out var limo)) return;
                var graphic = limo.OBJ.BaseGraphicID + __instance.ObjectData[(int)VMStackObjectVariable.Graphic];
                var dgrp = limo.Resource.Get<DGRP>((ushort)graphic) ?? limo.Resource.Get<DGRP>(limo.OBJ.BaseGraphicID);
                if (dgrp != null) world.DGRP = dgrp;
            }
            catch (Exception e)
            {
                CutContentPack.Log("limousine: couldn't draw the car: " + e.Message);
            }
        }

        /// <summary>Hot Date's limousine, part by part, by the part's place in the car.</summary>
        private static Dictionary<short, GameObject> FindLimo()
        {
            var parts = new Dictionary<short, GameObject>();
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            var lead = objects?.Get(LimoGUID);
            if (lead == null) { CutContentPack.Log("limousine: Hot Date's limousine was not found"); return parts; }
            foreach (var objd in lead.Resource.List<OBJD>() ?? new List<OBJD>())
            {
                if (objd.MasterID != lead.OBJ.MasterID) continue;
                var part = objects.Get(objd.GUID);
                if (part != null) parts[objd.SubIndex] = part;
            }
            return parts;
        }
    }
}
