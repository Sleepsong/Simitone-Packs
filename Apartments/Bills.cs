/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Rent comes with the game's own bills. The mail carrier brings the household's bills as the game always has; when
    /// a new bill appears on the lot, the rent built up since the last one is added to what is owed, and the player is
    /// told it came with the mail. When the household pays its bills (the game's own Pay Bills), the rent owed is paid
    /// with them.
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
                + "\n\nIt is paid with the bills.");
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
        /// Money has just changed hands for the played household. If it was the household paying its bills, pay the
        /// rent owed too.
        /// </summary>
        public static void AfterPayment(VM vm, VMEntity payer, VMEntity using_, int change)
        {
            if (change >= 0 || !IsBill(using_)) return;
            var played = vm.TS1State?.CurrentFamily;
            if (played == null || !(payer is VMAvatar sim) || sim.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != played.ChunkID) return;
            var account = Neighbours.RentOf(vm);
            AptLog.Write("The bills were paid: " + (-change) + ".");
            if (account == null || account.Owed <= 0) return;
            var budget = played.Budget;
            var owed = account.Owed;
            var paid = Units.PayBills(ref budget, account);
            if (paid == 0)
            {
                OnNews?.Invoke(vm, "The bills are paid, but there was not enough left for the rent: " + owed + " simoleons are owed. Pay it in the Apartments window when there is.");
                return;
            }
            played.Budget = budget;
            AptLog.Write("Rent paid with the bills: " + paid + ". Money now " + budget + ".");
            OnNews?.Invoke(vm, "The rent was paid with the bills: " + paid + " simoleons.");
        }
    }
}
