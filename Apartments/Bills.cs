/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Utils;
using FSO.SimAntics.Primitives;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Rent comes with the game's own bills. The mail carrier brings the household's bills as the game always has; when
    /// a new bill appears on the lot, the rent built up since the last one is added to what is owed, and the player is
    /// told it came with the mail. When the household pays its bills (the game's own Pay Bills), it pays its apartment's
    /// part of the building's bill, and the rent owed with it.
    ///
    /// A bill is known by its object's name ("Bills", or any name with the word "bill" in it, not "billiards"): the
    /// game's objects have English names whatever language it is played in. Each new bill is written to the log with
    /// its name and what it holds, so that a later version could put the rent on the bill itself.
    /// </summary>
    public static class Bills
    {
        /// <summary>If no bill from the game has come for this many days, the rent bill comes on its own (no mailbox, say).</summary>
        public const int OnItsOwnAfterDays = 7;

        private static readonly Dictionary<uint, bool> KindIsBill = new Dictionary<uint, bool>();

        /// <summary>Whether a name is a bill's: a word in it is "bill" or "bills".</summary>
        public static bool IsBillName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var words = name.ToLowerInvariant().Split(new[] { ' ', '-', '_', '.', '(', ')', '*', ':', ',', '/', '\\', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Any(x => x == "bill" || x == "bills");
        }

        /// <summary>Whether a thing is one of the game's bills. Worked out once for each kind of object.</summary>
        public static bool IsBill(VMEntity thing)
        {
            if (thing == null || thing is VMAvatar) return false;
            var guid = thing.Object?.OBJ?.GUID ?? 0;
            if (guid == 0) return false;
            lock (KindIsBill)
            {
                if (KindIsBill.TryGetValue(guid, out var known)) return known;
            }
            bool isBill;
            try { isBill = IsBillName(thing.Object.OBJ.ChunkLabel) || IsBillName(thing.ToString()); }
            catch { isBill = false; }
            lock (KindIsBill) KindIsBill[guid] = isBill;
            return isBill;
        }

        /// <summary>Called with what to tell the player about rent and the mail.</summary>
        public static Action<VM, string> OnNews;

        /// <summary>Every few seconds: has the mail brought a bill?</summary>
        internal static void Look(VM vm, Neighbours.LotState state)
        {
            var played = vm.TS1State?.CurrentFamily;
            if (played == null || state.File == null) return;
            var now = new HashSet<short>();
            foreach (var thing in vm.Entities.ToList())
            {
                if (thing is VMAvatar || thing.Dead || !IsBill(thing)) continue;
                now.Add(thing.ObjectID);
                if (!state.BillsScanned || state.BillsSeen.Contains(thing.ObjectID)) continue;
                Arrived(vm, state, played, thing);
            }
            //the first look on a lot only notes the bills already there: they are not new mail
            state.BillsSeen = now;
            state.BillsScanned = true;
        }

        private static void Arrived(VM vm, Neighbours.LotState state, FSO.Files.Formats.IFF.Chunks.FAMI played, VMEntity bill)
        {
            string held;
            try { held = string.Join(",", Enumerable.Range(0, 8).Select(i => bill.GetAttribute(i))); } catch { held = "?"; }
            AptLog.Write("The mail brought a bill: " + bill.ToString() + " (object " + (bill.Object?.OBJ?.GUID ?? 0).ToString("x8") + ", holding " + held + ").");

            var account = state.File.AccountOf(played.ChunkID);
            var today = Units.Today(vm.Context.Clock);
            if (account.Accrued <= 0) return; //nothing built up since the last: no rent with this one
            var overdue = account.Bills > 0;
            var amount = Units.IssueBill(account, today);
            var apartment = state.File.ApartmentOf(played.ChunkID);
            AptLog.Write("Rent bill with the mail: " + amount + " for " + (apartment?.Name ?? "the apartment") + ". Owed in all: " + account.Owed + ".");
            OnNews?.Invoke(vm, "The rent for " + (apartment?.Name ?? "your apartment") + " came with the mail: " + amount + " simoleons."
                + (overdue ? " The last rent has not been paid: " + account.Owed + " is owed in all." : "")
                + "\n\nIt is paid with the bills. (The bills themselves are your apartment's part of the building's: by the value of what is in it.)");
        }

        /// <summary>The rent bill comes on its own if the game's mail has brought no bill for a week. Returns whether it did.</summary>
        internal static bool OnItsOwn(VM vm, Neighbours.LotState state, RentAccount account, int today, string apartmentName)
        {
            if (account.Accrued <= 0 || account.BillDay <= 0 || today - account.BillDay < OnItsOwnAfterDays) return false;
            var overdue = account.Bills > 0;
            var amount = Units.IssueBill(account, today);
            AptLog.Write("No bill has come with the mail for " + OnItsOwnAfterDays + " days, so the rent bill came on its own: " + amount + ". Owed in all: " + account.Owed + ".");
            OnNews?.Invoke(vm, "The rent for " + (apartmentName ?? "your apartment") + " has come: " + amount + " simoleons."
                + (overdue ? " The last rent has not been paid: " + account.Owed + " is owed in all." : "")
                + " (No bills have come with the mail for a week; is there a mailbox?)\n\nPay it in the Apartments window.");
            return true;
        }

        /// <summary>
        /// The part of the building's bill that is the household's: the game works a bill out from the value of
        /// everything on the lot, which in a building is every apartment and every shared room. The household pays for
        /// what is in its own apartment: the bill, times the value of the things in the apartment over the value of the
        /// things on the lot. (Walls, floors, doors and windows are the building's.)
        /// </summary>
        public static int PartOf(int bill, long apartmentValue, long lotValue)
        {
            if (bill <= 0 || lotValue <= 0 || apartmentValue <= 0) return 0;
            return (int)Math.Round(bill * Math.Min(1.0, (double)apartmentValue / lotValue));
        }

        /// <summary>The value of the things in an apartment, and of the things on the whole lot, as the engine values them.</summary>
        internal static void Values(VM vm, Neighbours.LotState state, Apartment home, out long mine, out long all)
        {
            mine = 0; all = 0;
            var seen = new HashSet<FSO.SimAntics.Entities.VMMultitileGroup>();
            foreach (var thing in vm.Entities.ToList())
            {
                var group = thing.MultitileGroup;
                if (group == null || !seen.Add(group)) continue;
                var first = group.BaseObject;
                if (first == null || first is VMAvatar || first.Position == FSO.LotView.Model.LotTilePos.OUT_OF_WORLD) continue;
                if (vm.Context.IsUserOutOfBounds(first.Position)) continue;
                if ((first.MasterDefinition ?? first.Object.OBJ).BuildModeType > 0) continue; //the building's own
                var price = group.Price;
                if (price <= 0) continue;
                all += price;
                if (state.RoomApts.TryGetValue(vm.Context.GetRoomAt(first.Position), out var apartment) && apartment == home) mine += price;
            }
        }

        private static long LastNewsTick = -1000;

        /// <summary>
        /// The game's money primitive is about to run. If it is the played household paying a bill, it pays its
        /// apartment's part of the bill instead of the whole building's, and the rent owed with it if there is the money.
        /// Returns true if the payment was made here (with what the primitive should answer).
        /// </summary>
        public static bool PayBills(VMStackFrame context, VMPrimitiveOperand args, out VMPrimitiveExitCode answer)
        {
            answer = VMPrimitiveExitCode.GOTO_TRUE;
            var vm = context.VM;
            if (!IsBill(context.Callee) || !(args is VMTransferFundsOperand operand)) return false;
            var played = vm.TS1State?.CurrentFamily;
            if (played == null || !(context.Caller is VMAvatar sim) || sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != played.ChunkID) return false;
            var state = Neighbours.StateOf(vm);
            var home = state?.File?.ApartmentOf(played.ChunkID);
            if (home == null) return false; //no apartment: the game's bill, as it is

            //as the game works it out (in The Sims, a positive amount is taken from the household)
            var amount = VMMemory.GetBigVariable(context, operand.GetAmountOwner(), (short)operand.AmountData);
            if ((operand.Flags & VMTransferFundsFlags.Subtract) > 0) amount = -amount;
            if (amount <= 0) return false; //money coming in is not a bill being paid
            var budget = played.Budget;
            if (budget < 0) { answer = VMPrimitiveExitCode.GOTO_FALSE; return true; } //as the game: nothing is paid from below nothing
            if ((operand.Flags & VMTransferFundsFlags.JustTest) > 0) return true;

            Neighbours.FreshRooms(vm, state);
            Values(vm, state, home, out var mine, out var all);
            var part = PartOf(amount, mine, all);
            budget -= part;
            var account = state.File.AccountOf(played.ChunkID);
            var owed = account.Owed;
            var rent = (owed > 0 && budget >= owed) ? Units.PayBills(ref budget, account) : 0;
            played.Budget = budget;

            var percent = (all <= 0) ? 0 : (int)Math.Round(100.0 * mine / all);
            AptLog.Write("Bills paid: " + part + " of the building's " + amount + " (" + home.Name + " holds " + mine + " of the lot's " + all + " in things, " + percent + "%)"
                + (rent > 0 ? ", and the rent, " + rent : (owed > 0 ? "; the rent (" + owed + ") could not be paid" : "")) + ". Money now " + budget + ".");
            var tick = vm.Context.Clock.Ticks;
            if (tick - LastNewsTick > 60) //one message for bills paid together
            {
                LastNewsTick = tick;
                OnNews?.Invoke(vm, "The bills are paid: " + part + " simoleons. That is " + home.Name + "'s part of the building's bill of " + amount
                    + ", by the value of what is in it (" + percent + "% of everything on the lot). The rest is the building's."
                    + (rent > 0 ? "\n\nThe rent was paid with them: " + rent + " simoleons." : (owed > 0 ? "\n\nThere was not enough left for the rent: " + owed + " simoleons are owed. Pay it in the Apartments window when there is." : "")));
            }
            return true;
        }
    }
}
