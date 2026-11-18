/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Moodlets
{
    /// <summary>
    /// Moodlets in the game: who has them, time passing, and what they do to mood.
    ///
    /// Who: the people (not pets) of the household being played, as with the Life Stages pack. Visitors, townies
    /// and households you are not playing have none.
    ///
    /// Time: a moodlet's hours are hours of game time lived on a lot being played, wherever that is. A Sim who is not
    /// on the lot (the rest of the family, while one Sim is Downtown) is held still, as the game holds their needs.
    ///
    /// Mood: the game works out each Sim's mood in one place, VMTS1MotiveDecay.Tick, every two minutes of game time:
    /// the seven needs and the room, added up and divided by eight. Each time it has done so, the pack adds the Sim's
    /// moodlets (AfterDecay). Since the game starts again from the needs each time, nothing builds up: a moodlet that
    /// runs out is gone from the mood at the next count. What the game's scripts, its free will and the panels read
    /// as the Sim's mood is then the mood with the moodlets in it.
    /// </summary>
    public static class Moods
    {
        public static string PackId = "sample.moodlets";

        /// <summary>The moodlets there are, from moodlets.json.</summary>
        public static Tuning Tuning = Tuning.Defaults();

        // ---- the neighbourhood's record ----

        private static Ledger Current;
        private static string CurrentPath;

        public static Ledger Ledger
        {
            get
            {
                var path = NeighbourhoodPath();
                if (path == null) return null;
                if (Current == null || CurrentPath != path)
                {
                    Current = Ledger.Read(PackId, path);
                    CurrentPath = path;
                    Changed();
                }
                return Current;
            }
        }

        /// <summary>
        /// Drop what is in memory, so that it is read from the file again: when the game loads a neighbourhood, and
        /// when a lot is left. If the game was saved first the file is the same; if not, what happened since goes
        /// with the lot.
        /// </summary>
        public static void Forget()
        {
            Current = null;
            CurrentPath = null;
            Changed();
        }

        /// <summary>The game has saved: keep the moodlets with it.</summary>
        public static void Saved()
        {
            if (Current != null && CurrentPath != null) Current.Write(PackId, CurrentPath);
        }

        /// <summary>The player changed a setting: keep it now, whether or not the game is saved.</summary>
        public static void SettingsChanged()
        {
            if (Current != null && CurrentPath != null) Current.WriteSettings(PackId, CurrentPath);
            Changed();
        }

        /// <summary>Where the loaded neighbourhood is kept. The tests put another in its place.</summary>
        public static Func<string> NeighbourhoodPath = () => Content.Get()?.Neighborhood?.UserPath;

        public static bool On => Ledger?.Settings.On ?? false;

        // ---- who ----

        /// <summary>The people of the played household who are on this lot, whether in the world or away at work.</summary>
        public static List<VMAvatar> Household(VM vm)
        {
            var result = new List<VMAvatar>();
            if (vm == null || !vm.TS1) return result;
            var family = vm.TS1State?.CurrentFamily;
            if (family?.FamilyGUIDs == null) return result;
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                var avatar = entity as VMAvatar;
                if (avatar == null || avatar.Dead || avatar.IsPet) continue;
                if (avatar.GetPersonData(VMPersonDataVariable.IsGhost) != 0) continue;
                if (!family.FamilyGUIDs.Contains(avatar.Object.OBJ.GUID)) continue;
                result.Add(avatar);
            }
            return result;
        }

        public static uint Guid(VMAvatar avatar) => avatar.Object.OBJ.GUID;

        /// <summary>The moodlets of a Sim, for reading.</summary>
        public static IReadOnlyList<Moodlet> Of(VMAvatar avatar) => avatar == null ? Array.Empty<Moodlet>() : Ledger?.Find(Guid(avatar)) ?? Array.Empty<Moodlet>();

        /// <summary>What a Sim's moodlets add to their mood now. Nothing while moodlets are off.</summary>
        public static int TotalOf(VMAvatar avatar) => On ? Rules.Total(Of(avatar), Tuning) : 0;

        // ---- giving and taking ----

        /// <summary>
        /// Give a Sim a moodlet, by their GUID: they need not be on the lot. False if moodlets are off or the kind is
        /// not known.
        /// </summary>
        public static bool Give(uint guid, string id, string note, string who)
        {
            var ledger = Ledger;
            var kind = Tuning.Find(id);
            if (ledger == null || kind == null || !ledger.Settings.On) return false;
            var given = Rules.Give(ledger.Of(guid), kind, Tuning, note);
            //a fire gives Fire! again each time it spreads: only a moodlet that is new is worth a line
            if (given != Rules.Given.Renewed)
                Log.Write((who ?? guid.ToString("x8")) + " has " + kind.Name + " (" + Rules.Signed(kind.Mood) + " for " + Rules.Plural(kind.Hours, "hour") + ")"
                    + (given == Rules.Given.Replaced ? ", in place of another of its group" : "") + (string.IsNullOrEmpty(note) ? "" : ": " + note));
            Changed();
            return true;
        }

        public static bool Give(VMAvatar avatar, string id, string note = "") => avatar != null && Give(Guid(avatar), id, note, avatar.Name);

        /// <summary>Take a Sim's moodlets away: one kind, or all if none is named. Returns how many.</summary>
        public static int Clear(VMAvatar avatar, string id = null)
        {
            var ledger = Ledger;
            if (ledger == null || avatar == null) return 0;
            var list = ledger.Of(Guid(avatar));
            var gone = list.RemoveAll(x => id == null || x.Id == id);
            ledger.Tidy();
            if (gone > 0) Changed();
            return gone;
        }

        // ---- each lot ----

        private class LotState
        {
            public ClockWatch Clock = new ClockWatch();
            public int Ticks;
            /// <summary>What each Sim's moodlets add to their mood, by GUID, as AfterDecay reads it. Kept ready, since that runs for every Sim every tick.</summary>
            public Dictionary<uint, int> Offsets = new Dictionary<uint, int>();
            public int Version = -1;
            /// <summary>Each Sim's job as last seen, by GUID.</summary>
            public Dictionary<uint, Events.JobSeen> Jobs = new Dictionary<uint, Events.JobSeen>();
        }

        private static readonly ConditionalWeakTable<VM, LotState> Lots = new ConditionalWeakTable<VM, LotState>();

        /// <summary>Bumped whenever any moodlet changes, so each lot knows to work its offsets out again.</summary>
        private static int Version;
        private static void Changed() => Version++;

        /// <summary>How often a lot's Sims are looked at, in ticks: a few seconds.</summary>
        public const int LookEvery = 90;

        /// <summary>Called after every tick of every lot.</summary>
        public static void AfterTick(VM vm)
        {
            if (vm == null || !vm.TS1 || vm.TS1State?.CurrentFamily == null) return;
            var ledger = Ledger;
            if (ledger == null) return;
            var lot = Lots.GetValue(vm, x => new LotState());

            var passed = lot.Clock.Feed(vm.Context.Clock.Hours, vm.Context.Clock.Minutes);
            var household = (passed > 0 || lot.Ticks % LookEvery == 0) ? Household(vm) : null;
            if (passed > 0 && ledger.Settings.On)
            {
                foreach (var avatar in household)
                {
                    if (!ledger.Sims.TryGetValue(Ledger.Key(Guid(avatar)), out var list)) continue;
                    foreach (var ended in Rules.Pass(list, passed))
                        Log.Write(avatar.Name + "'s " + (Tuning.Find(ended.Id)?.Name ?? ended.Id) + " has worn off.");
                    if (list.Count == 0) ledger.Sims.Remove(Ledger.Key(Guid(avatar)));
                }
                Changed(); //the panel shows the time left
            }
            if (lot.Ticks++ % LookEvery == 0) Events.LookAtJobs(vm, household, lot.Jobs);
            if (lot.Version != Version || household != null) Refresh(lot, household ?? Household(vm));
        }

        private static void Refresh(LotState lot, List<VMAvatar> household)
        {
            lot.Offsets.Clear();
            lot.Version = Version;
            if (!On) return;
            foreach (var avatar in household)
            {
                var total = Rules.Total(Of(avatar), Tuning);
                if (total != 0) lot.Offsets[Guid(avatar)] = total;
            }
        }

        /// <summary>A moodlet's strength or the cap was changed from the console: every Sim's mood is worked out again.</summary>
        public static void Retuned() => Changed();

        // ---- the mood ----

        /// <summary>Before the game counts a Sim's needs: remember which two minutes it last counted them in.</summary>
        public static void BeforeDecay(VMTS1MotiveDecay __instance, out int __state)
        {
            __state = __instance.LastMinute;
        }

        /// <summary>
        /// The game has looked at a Sim's needs. If it worked their mood out again (it does every two minutes of game
        /// time, and not for a Sim who is hidden), add their moodlets to it.
        /// </summary>
        public static void AfterDecay(VMTS1MotiveDecay __instance, VMAvatar avatar, VMContext context, int __state)
        {
            if (__instance.LastMinute == __state) return; //it did not count this tick
            var vm = context?.VM;
            if (vm == null || avatar == null || !Lots.TryGetValue(vm, out var lot)) return;
            if (!lot.Offsets.TryGetValue(Guid(avatar), out var offset) || offset == 0) return;
            avatar.SetMotiveData(VMMotive.Mood, Rules.Adjust(avatar.GetMotiveData(VMMotive.Mood), offset));
        }
    }
}
