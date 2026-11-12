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
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTree
{
    /// <summary>What to do when an adult joins the household being played.</summary>
    public enum MoveInAnswer
    {
        /// <summary>Leave the tree as it is.</summary>
        Nothing,
        /// <summary>They moved in by a proposal: they are married.</summary>
        Marry,
        /// <summary>They moved in some other way, and could be married: ask the player.</summary>
        Ask
    }

    /// <summary>
    /// The tree follows what happens in play.
    ///
    /// The Sims 1's scripts know nothing of family, so nothing in them says "these two are this baby's parents" or
    /// "these two are now married". What they do is make a baby object, turn it into a character days later, and add
    /// a character to the household. The pack watches for those three, and works out the rest from who was doing
    /// what at the time:
    ///
    ///  - A baby object is made. The Sims whose interaction made it are its parents. They are written down beside
    ///    the tree until it grows up, since the baby is an object and not yet a Sim.
    ///  - The baby becomes a child. The new character is given those parents, and starts out getting on with the
    ///    family as a child of theirs would.
    ///  - An adult joins the household. If a Sim of the household brought them in, the two may be married: they are
    ///    if the interaction was a proposal, and otherwise the player is asked.
    ///
    /// Everything it sees and decides is written to the terminal ("[family]"), since none of this has been run
    /// against the game yet.
    /// </summary>
    public static class Following
    {
        /// <summary>The game's "make new character" primitive, and where its operand says what kind (0: a Sim, not a pet).</summary>
        private const ushort MakeNewCharacterOpcode = 19;
        private const int AvatarTypeByte = 3;
        /// <summary>The game's global that holds the number of the house being played.</summary>
        private const ushort HouseNumberGlobal = 10;

        // ---- the parts that know nothing about a running game ----

        /// <summary>
        /// A baby's parents, from the Sims who had a hand in making it, most involved first: the first two of them
        /// who are adults of the household. With only one, their husband or wife is the other, if they live there too.
        /// </summary>
        public static List<uint> PickParents(IEnumerable<uint> involved, ICollection<uint> household, Kin kin, Func<uint, bool> isAdult)
        {
            var parents = new List<uint>();
            foreach (var sim in involved)
            {
                if (parents.Count == 2) break;
                if (!parents.Contains(sim) && household.Contains(sim) && isAdult(sim)) parents.Add(sim);
            }
            if (parents.Count == 1)
            {
                var spouse = kin.SpouseOf(parents[0]);
                if (spouse != null && household.Contains(spouse.Value)) parents.Add(spouse.Value);
            }
            return parents;
        }

        /// <summary>Whether an interaction's name says it is a proposal of marriage. Only the English game's names are known.</summary>
        public static bool LooksLikeProposal(string interaction)
        {
            if (string.IsNullOrEmpty(interaction)) return false;
            var name = interaction.ToLowerInvariant();
            return name.Contains("propos") || name.Contains("marr");
        }

        /// <summary>An adult has joined the household, brought in by one of its Sims: what that means for the two of them.</summary>
        public static MoveInAnswer MoveInDecision(Kin kin, uint newcomer, uint host, string interaction, Func<uint, bool> isAdult, Func<uint, string> name)
        {
            if (newcomer == host || !isAdult(newcomer) || !isAdult(host)) return MoveInAnswer.Nothing;
            //family already, of whatever kind, or one of them is married to someone else
            if (kin.Between(newcomer, host) != Tie.None) return MoveInAnswer.Nothing;
            if (kin.WhyNot(newcomer, host, Choice.Spouse, isAdult, name) != null) return MoveInAnswer.Nothing;
            return LooksLikeProposal(interaction) ? MoveInAnswer.Marry : MoveInAnswer.Ask;
        }

        /// <summary>
        /// Write down a baby's parents until it grows up. Anything kept for the same object in the same house is
        /// replaced: that baby is gone, and its number has been given to this one.
        /// </summary>
        public static void Expect(Kin kin, int house, int objectId, uint objectGuid, IList<uint> parents)
        {
            kin.Babies.RemoveAll(b => b.House == house && b.ObjectId == objectId);
            kin.Babies.Add(new Expected()
            {
                House = house,
                ObjectId = objectId,
                ObjectGuid = objectGuid,
                ParentA = parents.Count > 0 ? parents[0] : 0,
                ParentB = parents.Count > 1 ? parents[1] : 0
            });
        }

        /// <summary>
        /// A character has been made from one of these objects in this house: find the baby it was, and stop
        /// expecting it. If none of the objects is a baby that is expected but the house has exactly one, it is that
        /// one (guessed is set). Null if nothing is expected there.
        /// </summary>
        public static Expected? TakeExpected(Kin kin, int house, IEnumerable<int> objectIds, out bool guessed)
        {
            guessed = false;
            var ids = objectIds.ToList();
            var here = kin.Babies.Where(b => b.House == house).ToList();
            if (here.Count == 0) return null;
            var found = here.Where(b => ids.Contains(b.ObjectId)).ToList();
            if (found.Count == 0)
            {
                if (here.Count != 1) return null;
                found = here;
                guessed = true;
            }
            kin.Babies.Remove(found[0]);
            return found[0];
        }

        /// <summary>
        /// A child whose parents were written down has become a Sim: give them those parents, and start them off with
        /// the family. Returns what to tell the player.
        /// </summary>
        public static string Welcome(Kin kin, uint child, Expected baby)
        {
            var refused = new List<string>();
            var parents = kin.GiveParents(child, baby.Parents().Where(People.Exists), People.IsAdult, People.FirstName, refused);
            foreach (var why in refused) People.Log("A parent was left out: " + why);
            var name = People.FirstName(child);
            if (parents.Count == 0)
            {
                return name + " has joined the family, but their parents are not known. You can say who they are in the Family Tree, with Change ties.";
            }
            var raised = People.RaiseScoresWithFamily(kin, child);
            People.Log(name + " is the child of " + string.Join(" and ", parents.Select(People.FirstName)) + ". Starts as family with " + raised + " Sim(s).");
            return name + " is the child of " + string.Join(" and ", parents.Select(People.FullName)) + ". The family tree has been updated.";
        }

        // ---- watching the game ----

        /// <summary>The script that is making an object right now, if one is. (The game runs its scripts on one thread.)</summary>
        [ThreadStatic]
        private static VMStackFrame Creating;

        /// <summary>(An object's own scripts run as it is made and may make more objects, so the one before is put back afterwards.)</summary>
        public static void BeforeCreateObject(VMStackFrame context, out VMStackFrame __state)
        {
            __state = Creating;
            Creating = context;
        }

        public static void AfterCreateObject(VMStackFrame __state)
        {
            Creating = __state;
        }

        private static readonly Dictionary<uint, bool> BabyKinds = new Dictionary<uint, bool>();

        /// <summary>
        /// Whether an object is a baby: one whose own scripts can make a new Sim. Found from the scripts, so it does
        /// not depend on the game's language or on knowing the object beforehand.
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
                People.Log("Could not read the scripts of object " + guid.ToString("X8") + ": " + e.Message);
            }
            BabyKinds[guid] = baby;
            if (baby) People.Log("Found the game's baby: object " + guid.ToString("X8") + " (" + (obj.Object.OBJ.ChunkLabel ?? "unnamed") + ").");
            return baby;
        }

        private static uint? SimGuid(VMEntity entity)
        {
            var avatar = entity as VMAvatar;
            if (avatar == null || avatar.IsPet) return null;
            return avatar.Object?.OBJ?.GUID;
        }

        /// <summary>
        /// The Sims who have a hand in what a script is doing, most involved first: whoever is running it, then the
        /// Sims it is being done to or with at each step back up the script, the Sim their interaction is with, and
        /// any Sim in the same bed or seat as one of those.
        /// </summary>
        private static List<uint> Involved(VMStackFrame frame)
        {
            var result = new List<uint>();
            var entities = new List<VMEntity>();
            void Add(VMEntity entity)
            {
                if (entity == null || entities.Contains(entity)) return;
                entities.Add(entity);
                var guid = SimGuid(entity);
                if (guid != null && !result.Contains(guid.Value)) result.Add(guid.Value);
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
            //a couple in a bed are both in it, though the script may only name one of them
            foreach (var entity in entities.ToList())
            {
                var holder = entity.Container;
                if (holder == null) continue;
                for (int slot = 0; slot < holder.TotalSlots(); slot++) Add(holder.GetSlot(slot));
            }
            return result;
        }

        private static string Names(IEnumerable<uint> sims)
        {
            var names = sims.Select(People.FirstName).ToList();
            return names.Count == 0 ? "nobody" : string.Join(", ", names);
        }

        /// <summary>The game has made an object. If a script made it and it is a baby, write down who its parents are.</summary>
        public static void AfterObjectMade(VMMultitileGroup __result)
        {
            var frame = Creating;
            if (frame == null || __result == null) return;
            FamilyTreePack.Guard("looking at a new object", () =>
            {
                var vm = frame.VM;
                if (vm == null || !vm.TS1) return;
                var obj = __result.BaseObject;
                if (!IsBaby(obj)) return;
                var family = vm.TS1State?.CurrentFamily;
                if (family?.FamilyGUIDs == null) return;

                var involved = Involved(frame);
                var kin = KinStore.Get();
                var parents = PickParents(involved, family.FamilyGUIDs, kin, People.IsAdult);
                var house = vm.GetGlobalValue(HouseNumberGlobal);

                //forget babies of this house that are no longer there (taken away, or the lot was not saved)
                kin.Babies.RemoveAll(b => b.House == house && b.ObjectId != obj.ObjectID
                    && (vm.GetObjectById((short)b.ObjectId)?.Object?.OBJ?.GUID != b.ObjectGuid));
                Expect(kin, house, obj.ObjectID, obj.Object.OBJ.GUID, parents);
                KinStore.Save();
                People.Log("A baby in house " + house + " (object " + obj.ObjectID + "). Sims involved: " + Names(involved) + ". Parents: " + Names(parents)
                    + ". Interaction: " + (frame.Thread?.ActiveAction?.Name ?? "(none)") + ".");
            });
        }

        /// <summary>The game is about to make a character from an object: remember which, since it then puts the character in its place.</summary>
        public static void BeforeMakeCharacter(VMStackFrame context, out List<int> __state)
        {
            var ids = new List<int>();
            try
            {
                void Add(VMEntity entity)
                {
                    if (entity == null) return;
                    if (!ids.Contains(entity.ObjectID)) ids.Add(entity.ObjectID);
                    //the baby and its cot may be two objects, one in the other
                    if (entity.Container != null && !ids.Contains(entity.Container.ObjectID)) ids.Add(entity.Container.ObjectID);
                    for (int slot = 0; slot < entity.TotalSlots(); slot++)
                    {
                        var held = entity.GetSlot(slot);
                        if (held != null && !ids.Contains(held.ObjectID)) ids.Add(held.ObjectID);
                    }
                }
                Add(context.StackObject);
                Add(context.Caller);
                Add(context.Callee);
            }
            catch (Exception)
            {
                //whatever was found before it went wrong is still worth having
            }
            __state = ids;
        }

        /// <summary>The game has made a character. If it is a child that was a baby whose parents are known, they become its parents.</summary>
        public static void AfterMakeCharacter(VMStackFrame context, VMPrimitiveOperand args, List<int> __state, VMPrimitiveExitCode __result)
        {
            FamilyTreePack.Guard("looking at a new character", () =>
            {
                var vm = context.VM;
                if (vm == null || !vm.TS1 || __result != VMPrimitiveExitCode.GOTO_TRUE) return;
                if ((args as VMTS1MakeNewCharacterOperand)?.AvatarType != 0) return; //a pet
                var neighbour = People.Neighbourhood?.GetNeighborByID(context.StackObjectID);
                if (neighbour == null) return;
                var child = neighbour.GUID;
                var house = vm.GetGlobalValue(HouseNumberGlobal);

                var kin = KinStore.Get();
                var baby = TakeExpected(kin, house, __state ?? new List<int>(), out var guessed);
                if (baby == null)
                {
                    People.Log("A new character, " + People.FirstName(child) + ", in house " + house + ". No baby was expected there, so the tree is unchanged.");
                    return;
                }
                if (guessed) People.Log("The new character was not made from the baby that was expected in house " + house + ", but it is the only one there, so it is taken to be that baby.");
                if (People.IsAdult(child))
                {
                    People.Log(People.FirstName(child) + " is an adult, so is not given the baby's parents.");
                    KinStore.Save();
                    return;
                }
                var message = Welcome(kin, child, baby.Value);
                KinStore.Save();
                //the scores were raised in the neighbourhood, which the game saves when the lot is saved
                Tell(vm, message);
            });
        }

        /// <summary>The game is about to add a character to a household: remember who was in it.</summary>
        public static void BeforeGameCall(VMStackFrame context, VMPrimitiveOperand args, out uint[] __state)
        {
            __state = null;
            var call = (args as VMGenericTS1CallOperand)?.Call;
            if (call != VMGenericTS1CallMode.AddToFamily && call != VMGenericTS1CallMode.AddToFamilyInTemp0) return;
            //(the game replaces this list when it adds to it, so the one held here stays as it was)
            __state = context.VM?.TS1State?.CurrentFamily?.FamilyGUIDs;
        }

        /// <summary>The game has added a character to the household. If it is an adult a Sim of the household brought in, the two may be married.</summary>
        public static void AfterGameCall(VMStackFrame context, uint[] __state)
        {
            if (__state == null) return;
            FamilyTreePack.Guard("looking at who joined the household", () =>
            {
                var vm = context.VM;
                var family = vm?.TS1State?.CurrentFamily;
                if (family?.FamilyGUIDs == null) return;
                var before = __state;
                foreach (var newcomer in family.FamilyGUIDs.Where(g => !before.Contains(g)).ToList())
                {
                    if (!People.IsPerson(newcomer) || !People.IsAdult(newcomer)) continue; //children arrive as babies, above
                    var involved = Involved(context);
                    var interaction = context.Thread?.ActiveAction?.Name;
                    var host = involved.Where(g => g != newcomer && before.Contains(g) && People.IsAdult(g)).Cast<uint?>().FirstOrDefault();
                    People.Log(People.FirstName(newcomer) + " joined the household. Sims involved: " + Names(involved) + ". Interaction: " + (interaction ?? "(none)") + ".");
                    if (host == null) continue;

                    var kin = KinStore.Get();
                    switch (MoveInDecision(kin, newcomer, host.Value, interaction, People.IsAdult, People.FirstName))
                    {
                        case MoveInAnswer.Marry:
                            Marry(newcomer, host.Value);
                            Tell(vm, People.FullName(newcomer) + " and " + People.FullName(host.Value) + " are married. The family tree has been updated.");
                            break;
                        case MoveInAnswer.Ask:
                            AskIfMarried(vm, newcomer, host.Value);
                            break;
                    }
                }
            });
        }

        private static void Marry(uint a, uint b)
        {
            var kin = KinStore.Get();
            if (kin.WhyNot(a, b, Choice.Spouse, People.IsAdult, People.FirstName) != null) return;
            kin.Set(a, b, Choice.Spouse);
            KinStore.Save();
            People.Log(People.FirstName(a) + " and " + People.FirstName(b) + " are married.");
        }

        // ---- telling and asking the player ----

        private static bool OnScreen(VM vm)
        {
            return (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm == vm;
        }

        private static void Tell(VM vm, string message)
        {
            People.Log(message);
            GameThread.NextUpdate(x =>
            {
                if (!OnScreen(vm)) return;
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Family Tree",
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }

        private static void AskIfMarried(VM vm, uint newcomer, uint host)
        {
            GameThread.NextUpdate(x =>
            {
                if (!OnScreen(vm)) return;
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Family Tree",
                    Message = People.FullName(newcomer) + " has moved in. Are " + People.FirstName(newcomer) + " and " + People.FirstName(host) + " married?",
                    Buttons = UIAlertButton.YesNo(
                        (yes) =>
                        {
                            UIScreen.RemoveDialog(alert);
                            FamilyTreePack.Guard("marrying two Sims", () => Marry(newcomer, host));
                        },
                        (no) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
