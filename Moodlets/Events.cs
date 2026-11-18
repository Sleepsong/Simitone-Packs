/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Moodlets
{
    /// <summary>
    /// What sets moodlets off. The Sims 1's scripts have no moodlets and say nothing like "this Sim was promoted", so
    /// the pack watches for what the scripts do, and works out what happened from that. Where it can, it watches
    /// something that does not depend on the game's language:
    ///
    ///   Promoted, Demoted,   a grown Sim's job and level, as the game keeps them, change between one look and the next
    ///   New Job, Fired       (every few seconds). A lot that has just loaded is looked at first, so nothing is given
    ///                        for what changed before.
    ///   Just Married         an adult joins the household, brought in by one of its adults with a proposal. The one
    ///                        thing that needs the game in English: the interaction's name says it is a proposal, as
    ///                        the Family Tree pack reads it.
    ///   New Baby             a script makes a baby: an object whose own scripts can make a Sim, as the Family Tree
    ///                        pack finds it. For everyone in the household.
    ///   Lost a Loved One     a Sim leaves the household for good. The game does this when a Sim dies, and when a
    ///                        child is taken away. For everyone left.
    ///   Fire!                the game sets something alight. For everyone of the household on the lot, and not away.
    ///   Burnt Food           a Sim's own script sets an appliance alight, which is what the stove does when the
    ///                        cooking goes wrong. For that Sim.
    ///
    /// Everything it sees and decides is written to the terminal and the log ("[moodlet]"), since none of this has
    /// been run against the game yet.
    /// </summary>
    public static class Events
    {
        /// <summary>The game's "make new character" primitive, and where its operand says what kind (0: a Sim, not a pet).</summary>
        private const ushort MakeNewCharacterOpcode = 19;
        private const int AvatarTypeByte = 3;
        /// <summary>The object catalogue's function flag for appliances (Simitone's third buy mode category).</summary>
        public const ushort AppliancesFlag = 4;

        // ---- jobs ----

        public struct JobSeen
        {
            public bool Grown;
            public short Type, Level;
        }

        /// <summary>Look at each Sim's job. Called every few seconds.</summary>
        public static void LookAtJobs(VM vm, List<VMAvatar> household, Dictionary<uint, JobSeen> seen)
        {
            if (household == null) return;
            foreach (var avatar in household)
            {
                var now = new JobSeen()
                {
                    Grown = !avatar.IsChild,
                    Type = avatar.GetPersonData(VMPersonDataVariable.JobType),
                    Level = avatar.GetPersonData(VMPersonDataVariable.JobPromotionLevel)
                };
                var guid = Moods.Guid(avatar);
                var had = seen.TryGetValue(guid, out var before);
                seen[guid] = now;
                //first sight of a Sim on this lot, or a child growing up: nothing to compare with
                if (!had || before.Grown != now.Grown || !now.Grown) continue;
                var change = Jobs.Between(before.Type, before.Level, now.Type, now.Level);
                if (change == Jobs.Change.None) continue;
                Log.Write(avatar.Name + "'s job went from " + JobName(before.Type, before.Level) + " to " + JobName(now.Type, now.Level) + ": " + change + ".");
                var note = change == Jobs.Change.Fired ? "Was: " + JobName(before.Type, before.Level) + "." : "Now: " + JobName(now.Type, now.Level) + ".";
                Moods.Give(avatar, Jobs.MoodletFor(change), note);
            }
        }

        /// <summary>What the game calls a job at a level: "Desk Sergeant". "none" for no job.</summary>
        public static string JobName(short type, short level)
        {
            if (type == 0) return "none";
            try
            {
                var name = Content.Get()?.Jobs?.GetJobLevel(type, level)?.JobName;
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            catch (Exception)
            {
                //a level the game has no name for
            }
            return "job " + type + ", level " + level;
        }

        // ---- the household changing ----

        /// <summary>The game is about to add a Sim to the household or take one out of it: remember who was in it.</summary>
        public static void BeforeGameCall(VMStackFrame context, VMPrimitiveOperand args, out uint[] __state)
        {
            __state = null;
            var call = (args as VMGenericTS1CallOperand)?.Call;
            if (call != VMGenericTS1CallMode.AddToFamily && call != VMGenericTS1CallMode.AddToFamilyInTemp0 && call != VMGenericTS1CallMode.RemoveFromFamily) return;
            //(the game replaces this list when it changes it, so the one held here stays as it was)
            __state = context.VM?.TS1State?.CurrentFamily?.FamilyGUIDs;
        }

        /// <summary>The game has changed the household, perhaps.</summary>
        public static void AfterGameCall(VMStackFrame context, uint[] __state)
        {
            if (__state == null) return;
            MoodletsPack.Guard("looking at who joined or left the household", () =>
            {
                var vm = context.VM;
                var after = vm?.TS1State?.CurrentFamily?.FamilyGUIDs;
                if (after == null) return;
                var before = __state;
                foreach (var newcomer in after.Where(g => !before.Contains(g)).ToList()) Joined(vm, context, newcomer, before);
                foreach (var gone in before.Where(g => !after.Contains(g)).ToList()) Left(vm, gone, after);
            });
        }

        private static void Joined(VM vm, VMStackFrame context, uint newcomer, uint[] before)
        {
            if (!IsPerson(newcomer) || !IsAdult(vm, newcomer)) return; //children arrive as babies
            var interaction = context.Thread?.ActiveAction?.Name;
            var host = Involved(context).Where(g => g != newcomer && before.Contains(g) && IsPerson(g) && IsAdult(vm, g)).Cast<uint?>().FirstOrDefault();
            Log.Write(NameOf(vm, newcomer) + " joined the household. Brought in by " + (host == null ? "nobody of it" : NameOf(vm, host.Value)) + ", with " + (interaction ?? "(no interaction)") + ".");
            if (host == null || !Words.LooksLikeProposal(interaction)) return;
            Moods.Give(newcomer, Tuning.JustMarried, "Married to " + NameOf(vm, host.Value) + ".", NameOf(vm, newcomer));
            Moods.Give(host.Value, Tuning.JustMarried, "Married to " + NameOf(vm, newcomer) + ".", NameOf(vm, host.Value));
        }

        private static void Left(VM vm, uint gone, uint[] after)
        {
            if (!IsPerson(gone)) return;
            var name = NameOf(vm, gone);
            Log.Write(name + " has left the household.");
            foreach (var sim in after.Where(IsPerson)) Moods.Give(sim, Tuning.LostFamily, name + " is gone.", NameOf(vm, sim));
        }

        // ---- babies ----

        /// <summary>The script making an object right now, if one is: set while the game's "create object" primitive runs.</summary>
        private static VMStackFrame Creating;

        public static void BeforeCreateObject(VMStackFrame context, out VMStackFrame __state)
        {
            __state = Creating;
            Creating = context;
        }

        /// <summary>A finalizer, so that what was being made is forgotten even if the game throws.</summary>
        public static void AfterCreateObject(VMStackFrame __state)
        {
            Creating = __state;
        }

        private static readonly Dictionary<uint, bool> BabyKinds = new Dictionary<uint, bool>();

        /// <summary>
        /// Whether an object is a baby: one whose own scripts can make a new Sim (and not a pet). Found from the
        /// scripts, as the Family Tree pack does, so it does not depend on the game's language or on knowing the
        /// object beforehand.
        /// </summary>
        public static bool IsBaby(VMEntity obj)
        {
            if (obj == null || obj is VMAvatar || obj.Object?.OBJ == null) return false;
            var guid = obj.Object.OBJ.GUID;
            if (BabyKinds.TryGetValue(guid, out var known)) return known;
            var baby = false;
            try
            {
                var scripts = (obj.Object.Resource?.List<BHAV>() ?? new List<BHAV>()).Concat(obj.SemiGlobal?.List<BHAV>() ?? new List<BHAV>());
                baby = scripts.Any(bhav => bhav.Instructions != null && bhav.Instructions.Any(i =>
                    i.Opcode == MakeNewCharacterOpcode && i.Operand != null && i.Operand.Length > AvatarTypeByte && i.Operand[AvatarTypeByte] == 0));
            }
            catch (Exception e)
            {
                Log.Write("Could not read the scripts of object " + guid.ToString("X8") + ": " + e.Message);
            }
            BabyKinds[guid] = baby;
            if (baby) Log.Write("Found the game's baby: object " + guid.ToString("X8") + " (" + (obj.Object.OBJ.ChunkLabel ?? "unnamed") + ").");
            return baby;
        }

        /// <summary>The game has made an object. If a script made a baby, the household has a new one.</summary>
        public static void AfterObjectMade(VMMultitileGroup __result)
        {
            var frame = Creating;
            if (frame == null || __result == null) return;
            MoodletsPack.Guard("looking at a new object", () =>
            {
                var vm = frame.VM;
                if (vm == null || !vm.TS1 || !IsBaby(__result.BaseObject)) return;
                var family = vm.TS1State?.CurrentFamily;
                if (family?.FamilyGUIDs == null) return;
                Log.Write("A baby was made, by " + (frame.Thread?.ActiveAction?.Name ?? "(no interaction)") + ".");
                foreach (var sim in family.FamilyGUIDs.Where(IsPerson)) Moods.Give(sim, Tuning.NewBaby, "", NameOf(vm, sim));
            });
        }

        // ---- fire ----

        /// <summary>The game has run its "burn" primitive. If it set something alight, there is a fire.</summary>
        public static void AfterBurn(VMStackFrame context, VMPrimitiveExitCode __result)
        {
            if (__result != VMPrimitiveExitCode.GOTO_TRUE) return;
            MoodletsPack.Guard("looking at a fire", () =>
            {
                var vm = context?.VM;
                if (vm == null || !vm.TS1) return;
                var household = Moods.Household(vm);
                var cook = context.Caller as VMAvatar;
                var burnt = context.StackObject;
                var appliance = burnt is VMGameObject && burnt.Object?.OBJ != null && (burnt.Object.OBJ.FunctionFlags & AppliancesFlag) != 0;
                Log.Write("A fire, started by " + (cook?.Name ?? burnt?.ToString() ?? "something") + (appliance ? " at an appliance (" + burnt + ")" : "") + ".");
                foreach (var sim in household)
                {
                    if (sim.Position == LotTilePos.OUT_OF_WORLD) continue; //away at work, or out
                    Moods.Give(sim, Tuning.Fire);
                }
                if (appliance && cook != null && household.Contains(cook)) Moods.Give(cook, Tuning.BurntFood);
            });
        }

        // ---- who ----

        private static readonly Dictionary<uint, bool> PersonCache = new Dictionary<uint, bool>();

        /// <summary>
        /// A Sim, and not a pet, by GUID, as the Family Tree pack tells them apart: the first of a character's body
        /// strings is what it is. A character whose file does not say counts as a Sim.
        /// </summary>
        public static bool IsPerson(uint guid)
        {
            if (PersonCache.TryGetValue(guid, out var known)) return known;
            var person = true;
            try
            {
                var kind = Content.Get()?.WorldObjects?.Get(guid)?.Resource?.Get<STR>(200)?.GetString(0);
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    kind = kind.Trim().ToLowerInvariant();
                    person = kind == "adult" || kind == "child";
                }
            }
            catch (Exception)
            {
                //unreadable: a Sim
            }
            PersonCache[guid] = person;
            return person;
        }

        /// <summary>Grown, by the Sim on the lot if they are here, else by the neighbourhood's record of them.</summary>
        private static bool IsAdult(VM vm, uint guid)
        {
            var here = OnLot(vm, guid);
            if (here != null) return !here.IsChild;
            var neighbour = Content.Get()?.Neighborhood?.GetNeighborByID(Content.Get().Neighborhood.GetNeighborIDForGUID(guid) ?? 0);
            if (neighbour?.PersonData == null || neighbour.PersonData.Length <= (int)VMPersonDataVariable.PersonsAge) return true;
            return neighbour.PersonData[(int)VMPersonDataVariable.PersonsAge] >= 18;
        }

        private static VMAvatar OnLot(VM vm, uint guid)
        {
            return vm?.Context.ObjectQueries.Avatars.OfType<VMAvatar>().FirstOrDefault(x => !x.Dead && x.Object.OBJ.GUID == guid);
        }

        /// <summary>A Sim's name: as on the lot if they are here, else as the game files them.</summary>
        public static string NameOf(VM vm, uint guid)
        {
            var here = OnLot(vm, guid);
            if (here != null && !string.IsNullOrWhiteSpace(here.Name)) return here.Name;
            try
            {
                var obj = Content.Get()?.WorldObjects?.Get(guid);
                var name = obj?.Resource?.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            catch (Exception)
            {
                //no name to be had
            }
            return guid.ToString("x8");
        }

        /// <summary>
        /// The Sims who have a hand in what a script is doing, most involved first: whoever is running it, then the
        /// Sims it is being done to or with at each step back up the script, and the Sim their interaction is with.
        /// </summary>
        private static List<uint> Involved(VMStackFrame frame)
        {
            var result = new List<uint>();
            void Add(VMEntity entity)
            {
                if (entity is VMAvatar avatar && !avatar.IsPet && !result.Contains(avatar.Object.OBJ.GUID)) result.Add(avatar.Object.OBJ.GUID);
            }
            Add(frame.Caller);
            var stack = frame.Thread?.Stack;
            if (stack != null)
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    Add(stack[i].Caller);
                    Add(stack[i].Callee);
                    Add(stack[i].StackObject);
                }
            }
            Add(frame.Thread?.ActiveAction?.Callee);
            return result;
        }
    }
}
