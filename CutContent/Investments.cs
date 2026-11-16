/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace CutContent
{
    /// <summary>
    /// Investment offers over the telephone.
    ///
    /// The surveys found what Maxis left. The phone's "RandomCallDialog" (phoneglobals.iff, script 8213) has a branch that
    /// no step leads to: two chance calls in five would have drawn an amount in fifties (tuning 395 to 396) and, if the
    /// household could afford it, called "dialog - negative chance" (8215). That shows one of 20 placeholder texts, "Would
    /// you like to spend some money on something random? ($$Local:0). Variation 1" to 20 (the phone's texts 140 to 159),
    /// and on Yes takes the money. Step 35 skips straight past the branch to the ordinary windfall call. Two things were
    /// never done: the amount was not handed to the dialog, and nothing ever paid an investment back.
    ///
    /// This finishes it, in memory:
    ///   - step 35 leads to the branch again;
    ///   - a new step puts the amount where the dialog reads it, as the windfall branch already does;
    ///   - the 20 placeholders become 20 offers;
    ///   - after the money is taken, a new step (a primitive of this pack's) records the investment. Some days of play
    ///     later it comes good or goes bad, with a message, and pays out. Pending investments are kept with the lot.
    /// </summary>
    public static class Investments
    {
        public const string PrimitiveName = "cutcontent_invest";
        public static string Status = "the phone's chance calls not seen yet";
        public static string PackId;
        public static int Offered, Made, Matured;

        private const string Placeholder = "Would you like to spend some money on something random?";
        private const int FirstPlaceholder = 140;

        /// <summary>The offers, in place of Maxis's placeholders. $$Local:0 is the amount, as in the placeholders.</summary>
        private static readonly string[] Offers =
        {
            "This is Biff from Simsville Securities. I've got a hot tip on a company that makes self-buttering toast. Want in for $$Local:0?",
            "Psst. A friend of a friend says llama futures are about to go through the roof. Invest $$Local:0?",
            "SimCity Savings here! Our Gold Star Bond matures in just a few days. Buy one for $$Local:0?",
            "Hi! I'm opening the first all-night car wash for pets. $$Local:0 makes you a partner. Interested?",
            "Good evening. Would you care to buy shares in a promising company that delivers pizza by catapult? Only $$Local:0.",
            "We're drilling for oil under the old miniature golf course and need one more backer. Put in $$Local:0?",
            "Opportunity knocks! Garden gnome imports are booming. $$Local:0 gets you a piece of the action.",
            "I'm calling for your cousin's accountant. He says you'll want in on a can't-miss real estate deal. Invest $$Local:0?",
            "Our company is about to patent a fork that doubles as a back scratcher. Become an early investor for $$Local:0?",
            "Hello! For just $$Local:0 you can own part of a racehorse named Lucky Simoleon. Shall I sign you up?",
            "Pleasant View Land Company calling. Lakefront lots are going fast. Reserve yours for $$Local:0?",
            "The SimCity Film Fund is backing a musical about plumbers. Put in $$Local:0 and share in the box office?",
            "Rumor has it the mime academy downtown is going public. Want $$Local:0 worth of shares?",
            "I've got a crate of antique doorknobs that collectors will pay a fortune for. Your share is $$Local:0. Deal?",
            "A limited offer: $$Local:0 buys you bonds in the new SimCity monorail. Interested?",
            "Hey, it's the guy from the gym. My smoothie stand is expanding. Back me for $$Local:0?",
            "Congratulations! You've been chosen to invest in a gold mine. The minimum investment is $$Local:0. Shall we proceed?",
            "Want to get in on the ground floor? A new robot butler company needs $$Local:0 from forward-thinking Sims like you.",
            "Our hedge fund invests only in hedges. Topiary is the future! Invest $$Local:0?",
            "Grandma's Secret Pie Recipe Incorporated is looking for partners. Buy in for $$Local:0?",
        };

        private static readonly string[] Gains =
        {
            "That stock tip paid off big time. The ${0} you invested has earned you ${1}.",
            "Your investment came good! The ${0} you put in has grown to ${1}.",
            "Good news from your broker: your ${0} investment just paid out ${1}.",
            "The company you backed with ${0} has made it big. You receive ${1}.",
        };

        private static readonly string[] Losses =
        {
            "Sucker! You just bought some worthless swamp land. Even the alligators won't live there! Your ${0} is gone.",
            "The company you invested ${0} in has gone belly up. Your money is gone.",
            "Bad news: that can't-miss deal missed. You've lost the ${0} you invested.",
        };

        private class Investment
        {
            public int Family;
            public int Amount;
            public int MinutesLeft;
        }

        private class LotState
        {
            public string LotPath;
            public List<Investment> Pending = new List<Investment>();
            public int LastMinute = -1;
        }

        private static ConditionalWeakTable<VM, LotState> Lots = new ConditionalWeakTable<VM, LotState>();
        private static ConditionalWeakTable<BHAV, object> Fixed = new ConditionalWeakTable<BHAV, object>();
        private static ConditionalWeakTable<STR, object> Worded = new ConditionalWeakTable<STR, object>();
        private static Random Dice = new Random();

        // ---- the scripts ----

        /// <summary>When an object is created: if it is a phone, finish its scripts and word its offers.</summary>
        public static void Prepare(VMEntity entity)
        {
            var res = entity?.Object?.Resource;
            if (res == null) return;
            Word(res);
            var shared = res.SemiGlobal;
            if (shared == null) return;
            var call = shared.Get<BHAV>(8213);
            if (call == null || (call.ChunkLabel ?? "").TrimEnd('\0') != "RandomCallDialog" || Fixed.TryGetValue(call, out _)) return;
            Fixed.AddOrUpdate(call, call);
            Status = Fix(shared, call);
            CutContentPack.Log("investments: " + Status);
        }

        private static string Fix(GameIffResource res, BHAV call)
        {
            var steps = call.Instructions;
            ushort budget = Scripts.Opcode("budget"), random = Scripts.Opcode("random_number");
            //as the surveys found it
            if (steps.Length != 46 || steps[35].Opcode != 8214 || steps[35].TruePointer != 7 || steps[5].Opcode != random || steps[5].TruePointer != 4
                || steps[19].Opcode != budget || steps[19].TruePointer != 34 || steps[22].Opcode != budget || steps[22].TruePointer != 25 || steps[34].Opcode != 8215)
                return "the phone's RandomCallDialog is not as the surveys found it, so it was left alone";

            var changed = new BHAVInstruction[48];
            for (int i = 0; i < steps.Length; i++)
                changed[i] = new BHAVInstruction { Opcode = steps[i].Opcode, Operand = steps[i].Operand, TruePointer = steps[i].TruePointer, FalsePointer = steps[i].FalsePointer };
            changed[35].TruePointer = 5; //the chance call may be an offer again
            //46: Temp0 = Local4, the amount, which the offer's text shows as $$Local:0
            changed[46] = Scripts.Step("expression", Scripts.Expression(0, 0x08, 5, 4, 0x19), 34, 253);
            changed[19].TruePointer = 46;
            //47: once the money is taken, record the investment
            changed[47] = Scripts.Step(PrimitiveName, new byte[8], 25, 25);
            changed[22].TruePointer = 47;
            call.Instructions = changed;
            Scripts.Reassemble(res, call);
            return "restored: two chance calls in five can be investment offers";
        }

        /// <summary>Replaces the 20 placeholders in a phone's texts with offers.</summary>
        private static void Word(GameIffResource res)
        {
            var texts = res.Get<STR>(301);
            if (texts == null || Worded.TryGetValue(texts, out _)) return;
            Worded.AddOrUpdate(texts, texts);
            if (texts.Length < FirstPlaceholder + Offers.Length) return;
            if (!(texts.GetString(FirstPlaceholder) ?? "").StartsWith(Placeholder)) return;
            var set = texts.LanguageSets[0];
            for (int i = 0; i < Offers.Length; i++)
            {
                var at = FirstPlaceholder + i;
                if (!(texts.GetString(at) ?? "").StartsWith(Placeholder)) continue;
                set.Strings[at] = new STRItem(Offers[i]) { LanguageCode = set.Strings[at].LanguageCode, Comment = set.Strings[at].Comment };
            }
            Offered++;
        }

        // ---- recording an investment ----

        public class RecordPrimitive : VMPrimitiveHandler
        {
            public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand operand)
            {
                try
                {
                    var amount = (context.Locals != null && context.Locals.Length > 4) ? context.Locals[4] : (short)0;
                    var family = context.VM.TS1State?.CurrentFamily;
                    if (amount > 0 && family != null && Lots.TryGetValue(context.VM, out var lot))
                    {
                        var days = 3 + Dice.Next(3); //three to five days of play
                        lot.Pending.Add(new Investment { Family = family.ChunkID, Amount = amount, MinutesLeft = days * 24 * 60 });
                        Made++;
                        CutContentPack.Log($"investments: ${amount} invested, comes due in {days} days");
                    }
                }
                catch (Exception e)
                {
                    CutContentPack.Log("investments: couldn't record an investment: " + e.Message);
                }
                return VMPrimitiveExitCode.GOTO_TRUE;
            }
        }

        // ---- coming due ----

        public static void OnLotLoaded(VM vm, string lotPath)
        {
            var lot = new LotState { LotPath = lotPath };
            if (lotPath != null && PackId != null)
            {
                var data = LotPackData.Read(PackId, lotPath);
                if (data != null)
                {
                    foreach (var line in Encoding.UTF8.GetString(data).Split('\n'))
                    {
                        var parts = line.Trim().Split(' ');
                        if (parts.Length != 3) continue;
                        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var family)
                            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left))
                            lot.Pending.Add(new Investment { Family = family, Amount = amount, MinutesLeft = left });
                    }
                }
            }
            Lots.AddOrUpdate(vm, lot);
        }

        public static void OnLotSaved(VM vm, string lotPath)
        {
            if (lotPath == null || PackId == null || !Lots.TryGetValue(vm, out var lot)) return;
            var text = string.Join("\n", lot.Pending.Select(x => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", x.Family, x.Amount, x.MinutesLeft)));
            LotPackData.Write(PackId, lotPath, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>After each tick of a lot: count down the investments by the minutes of game time that passed.</summary>
        public static void AfterTick(VM __instance)
        {
            var vm = __instance;
            if (!vm.TS1 || !Lots.TryGetValue(vm, out var lot)) return;
            var clock = vm.Context.Clock;
            var minute = clock.Hours * 60 + clock.Minutes;
            if (minute == lot.LastMinute) return;
            var passed = (lot.LastMinute < 0) ? 0 : (minute - lot.LastMinute + 1440) % 1440;
            lot.LastMinute = minute;
            if (passed == 0 || lot.Pending.Count == 0) return;

            var family = vm.TS1State?.CurrentFamily;
            foreach (var investment in lot.Pending.ToList())
            {
                investment.MinutesLeft -= passed;
                if (investment.MinutesLeft > 0 || family == null || family.ChunkID != investment.Family) continue;
                lot.Pending.Remove(investment);
                Mature(vm, family, investment);
            }
        }

        private static void Mature(VM vm, FAMI family, Investment investment)
        {
            //four in ten fail; the rest return half as much again, two and a half times, or four times
            var roll = Dice.Next(100);
            var times = (roll < 40) ? 0 : (roll < 75) ? 1.5 : (roll < 93) ? 2.5 : 4.0;
            var payout = (int)(investment.Amount * times);
            string message;
            if (payout == 0) message = string.Format(Losses[Dice.Next(Losses.Length)], investment.Amount);
            else
            {
                family.Budget += payout;
                message = string.Format(Gains[Dice.Next(Gains.Length)], investment.Amount, payout);
            }
            Matured++;
            CutContentPack.Log("investments: " + message);
            GameThread.NextUpdate(x =>
            {
                if ((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm != vm) return;
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Investment",
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }

        public static string StatusText(VM vm)
        {
            var pending = (vm != null && Lots.TryGetValue(vm, out var lot)) ? lot.Pending.Count : 0;
            return $"{Status}. This session: {Made} made, {Matured} come due. {pending} pending on this lot.";
        }
    }
}
