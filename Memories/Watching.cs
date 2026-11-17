/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Memories
{
    /// <summary>
    /// The life-event layer's view of the game: Harmony patches that see things happen, and a look at every Sim on
    /// the lot every second, and raise LifeEvents for them.
    ///
    /// Two ways of seeing, for two kinds of thing:
    ///
    ///  - What a Sim is: their job and its level, their age, whether they are a ghost, their skills. The game keeps
    ///    these as numbers on the Sim, and its scripts change them in many places, so instead of catching each place
    ///    the Sims on the lot are looked at every second, and a number that has moved is an event. A Sim is watched
    ///    for a few looks before anything about them counts, so a Sim arriving, or a pack putting their numbers
    ///    right as they arrive (Life Stages does), is not taken for a promotion or a birthday.
    ///
    ///  - What a Sim does: an interaction ending, a baby object being made, a character being made from it, a Sim
    ///    joining or leaving the household, a fire starting, a burglar taking something. These are moments in a
    ///    script, caught as the game runs the primitive or the method that does them, with the script still there
    ///    to say who was involved.
    ///
    /// Only lots with a household being played are watched. Nothing here changes the game.
    /// </summary>
    public static class Watching
    {
        /// <summary>The game's global that holds the number of the house being played.</summary>
        private const ushort HouseNumberGlobal = 10;
        /// <summary>The game's "make new character" primitive, and where its operand says what kind (0: a Sim, not a pet).</summary>
        private const ushort MakeNewCharacterOpcode = 19;
        private const int AvatarTypeByte = 3;

        /// <summary>Ticks between looks at the lot's Sims: a second, at normal speed.</summary>
        public const int LookEvery = 30;
        /// <summary>Looks at a Sim before a change in them counts.</summary>
        public const int SettlingLooks = 3;

        // ---- what is kept about each lot ----

        private sealed class Seen
        {
            public uint Guid;
            public int Looks;
            public short Job, Level, Age, Ghost;
            public short[] Skills;
            public bool Burglar, Police;
        }

        private sealed class Lot
        {
            public int LastHour = -1;
            public long Ticks;
            public readonly Dictionary<VMAvatar, Seen> Sims = new Dictionary<VMAvatar, Seen>();
            public readonly Dictionary<(uint, uint), long> LastKiss = new Dictionary<(uint, uint), long>();
            public readonly HashSet<uint> Dead = new HashSet<uint>();
            public readonly HashSet<(uint, Stage)> Reached = new HashSet<(uint, Stage)>();

            public bool Fire;
            public LifeTime FireBegan;

            public VMAvatar Burglar;
            public LifeTime BurglarCame;
            public bool PoliceCame;
            public readonly List<string> Taken = new List<string>();
            public readonly Dictionary<short, string> Held = new Dictionary<short, string>();
        }

        private static readonly ConditionalWeakTable<VM, Lot> Lots = new ConditionalWeakTable<VM, Lot>();

        private static Lot LotOf(VM vm) => Lots.GetValue(vm, x => new Lot());

        /// <summary>Whether a lot is one the layer watches: The Sims 1, with a household being played.</summary>
        private static bool Watched(VM vm) => vm != null && vm.TS1 && vm.TS1State?.CurrentFamily != null;

        public static int House(VM vm) => vm.GetGlobalValue(HouseNumberGlobal);

        /// <summary>Whether the household being played is on its own lot.</summary>
        public static bool AtHome(VM vm)
        {
            var family = vm?.TS1State?.CurrentFamily;
            return family != null && family.HouseNumber != 0 && family.HouseNumber == House(vm);
        }

        public static LifeTime Now(VM vm) => new LifeTime(MemoryBook.Journal?.Day ?? 0, vm.Context.Clock.Hours, vm.Context.Clock.Minutes);

        private static uint? SimGuid(VMEntity entity)
        {
            var avatar = entity as VMAvatar;
            if (avatar == null || avatar.IsPet) return null;
            return avatar.Object?.OBJ?.GUID;
        }

        /// <summary>The Sims (not pets) of the household being played.</summary>
        private static uint[] Household(VM vm)
        {
            var guids = vm?.TS1State?.CurrentFamily?.FamilyGUIDs ?? new uint[0];
            return guids.Where(IsPerson).ToArray();
        }

        /// <summary>The household's Sims who are on the lot now.</summary>
        private static uint[] HouseholdHere(VM vm)
        {
            var family = Household(vm);
            return vm.Context.ObjectQueries.Avatars.Select(SimGuid).Where(g => g != null && family.Contains(g.Value)).Select(g => g.Value).Distinct().ToArray();
        }

        private static readonly Dictionary<uint, bool> PersonCache = new Dictionary<uint, bool>();

        /// <summary>A Sim, not a pet: the first of a character's body strings says which. A character whose file does not say counts as a Sim.</summary>
        private static bool IsPerson(uint guid)
        {
            if (PersonCache.TryGetValue(guid, out var known)) return known;
            var person = true;
            try
            {
                var kind = FSO.Content.Content.Get()?.WorldObjects?.Get(guid)?.Resource?.Get<STR>(200)?.GetString(0);
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    kind = kind.Trim().ToLowerInvariant();
                    person = kind == "adult" || kind == "child";
                }
            }
            catch (Exception)
            {
                return true;
            }
            PersonCache[guid] = person;
            return person;
        }

        private static void Raise(VM vm, LifeEvent e)
        {
            e.House = House(vm);
            e.When = Now(vm);
            LifeEvents.Raise(e);
        }

        // ---- every tick: the clock, and a look at the Sims now and then ----

        public static void AfterTick(VM vm)
        {
            if (!Watched(vm)) return;
            var lot = LotOf(vm);
            var hour = vm.Context.Clock.Hours;
            if (lot.LastHour == 23 && hour == 0)
            {
                var journal = MemoryBook.Journal;
                if (journal != null)
                {
                    journal.Day++;
                    Log.Write("Day " + journal.Day + " begins.");
                }
            }
            lot.LastHour = hour;
            if (lot.Ticks++ % LookEvery != 0) return;
            Look(vm, lot);
        }

        private static void Look(VM vm, Lot lot)
        {
            var here = vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().Where(a => !a.Dead && !a.IsPet && a.Object?.OBJ != null).ToList();

            //Sims who have gone from the lot
            foreach (var gone in lot.Sims.Keys.Where(a => !here.Contains(a)).ToList())
            {
                if (gone == lot.Burglar) BurglarLeft(vm, lot);
                lot.Sims.Remove(gone);
            }

            foreach (var avatar in here)
            {
                var guid = avatar.Object.OBJ.GUID;
                var now = Read(avatar);
                if (!lot.Sims.TryGetValue(avatar, out var seen))
                {
                    now.Guid = guid;
                    now.Looks = 1;
                    var names = new[] { avatar.Name, avatar.Object.OBJ.ChunkLabel };
                    now.Burglar = Recognise.Burglar.InAny(names);
                    now.Police = !now.Burglar && Recognise.Police.InAny(names);
                    lot.Sims[avatar] = now;
                    Arrived(vm, lot, avatar, now);
                    continue;
                }
                if (++seen.Looks <= SettlingLooks)
                {
                    //still settling: what the Sim is now is where they start from
                    seen.Job = now.Job; seen.Level = now.Level; seen.Age = now.Age; seen.Ghost = now.Ghost; seen.Skills = now.Skills;
                    continue;
                }
                Compare(vm, lot, avatar, seen, now);
            }

            if (lot.Burglar != null) WatchBurglar(vm, lot);
            if (lot.Fire) WatchFire(vm, lot);
        }

        private static Seen Read(VMAvatar avatar)
        {
            return new Seen()
            {
                Job = avatar.GetPersonData(VMPersonDataVariable.JobType),
                Level = avatar.GetPersonData(VMPersonDataVariable.JobPromotionLevel),
                Age = avatar.GetPersonData(VMPersonDataVariable.PersonsAge),
                Ghost = avatar.GetPersonData(VMPersonDataVariable.IsGhost),
                Skills = People.Skills.Select(s => avatar.GetPersonData(s.Data)).ToArray(),
            };
        }

        /// <summary>What has changed about a Sim since the last look.</summary>
        private static void Compare(VM vm, Lot lot, VMAvatar avatar, Seen seen, Seen now)
        {
            var guid = seen.Guid;

            var change = Recognise.JobChange(seen.Job, seen.Level, now.Job, now.Level);
            if (change != null)
            {
                var lost = change == LifeEventKind.LostJob;
                var job = lost ? seen.Job : now.Job;
                var level = lost ? seen.Level : now.Level;
                Raise(vm, new LifeEvent()
                {
                    Kind = change.Value,
                    Sim = guid,
                    Value = job,
                    Value2 = level,
                    Value3 = change == LifeEventKind.Promoted && People.TopOfCareer(job, level) ? 1 : 0,
                    Detail = People.JobTitle(job, level),
                });
            }

            var before = Recognise.StageOf(seen.Age);
            var after = Recognise.StageOf(now.Age);
            //(once for each stage: a pack putting an age right after the game changed it is not a second birthday)
            if (after > before && lot.Reached.Add((guid, after))) Raise(vm, new LifeEvent() { Kind = LifeEventKind.GrewUp, Sim = guid, Value = (int)after, Detail = Recognise.StageName(after) });

            if (seen.Ghost == 0 && now.Ghost != 0) SimDied(vm, lot, guid, "became a ghost");

            for (int i = 0; i < now.Skills.Length && i < seen.Skills.Length; i++)
            {
                var was = Recognise.SkillPoints(seen.Skills[i]);
                var points = Recognise.SkillPoints(now.Skills[i]);
                if (points > was) Raise(vm, new LifeEvent() { Kind = LifeEventKind.SkillUp, Sim = guid, Detail = People.Skills[i].Name, Value = points });
            }

            seen.Job = now.Job; seen.Level = now.Level; seen.Age = now.Age; seen.Ghost = now.Ghost; seen.Skills = now.Skills;
        }

        private static void SimDied(VM vm, Lot lot, uint guid, string how, uint[] household = null)
        {
            if (!lot.Dead.Add(guid)) return;
            household ??= Household(vm);
            Raise(vm, new LifeEvent() { Kind = LifeEventKind.Died, Sim = guid, Others = household.Where(x => x != guid).ToArray(), Detail = how });
        }

        // ---- burglars ----

        private static void Arrived(VM vm, Lot lot, VMAvatar avatar, Seen seen)
        {
            if (seen.Burglar && lot.Burglar == null)
            {
                lot.Burglar = avatar;
                lot.BurglarCame = Now(vm);
                lot.PoliceCame = false;
                lot.Taken.Clear();
                lot.Held.Clear();
                Raise(vm, new LifeEvent() { Kind = LifeEventKind.BurglarCame, Sim = seen.Guid, Others = Household(vm), Value3 = AtHome(vm) ? 1 : 0 });
            }
            else if (seen.Police && lot.Burglar != null && !lot.PoliceCame)
            {
                lot.PoliceCame = true;
                Log.Write("The police have come (" + avatar.Name + ").");
            }
        }

        /// <summary>What the burglar is carrying. Anything carried that is gone when the burglar goes was taken.</summary>
        private static void WatchBurglar(VM vm, Lot lot)
        {
            var burglar = lot.Burglar;
            for (int slot = 0; slot < burglar.TotalSlots(); slot++)
            {
                var held = burglar.GetSlot(slot);
                if (held == null || held is VMAvatar) continue;
                if (!lot.Held.ContainsKey(held.ObjectID)) lot.Held[held.ObjectID] = held.Name;
            }
        }

        private static void BurglarLeft(VM vm, Lot lot)
        {
            foreach (var held in lot.Held)
            {
                var still = vm.GetObjectById(held.Key);
                if ((still == null || still.Dead) && !lot.Taken.Contains(held.Value)) lot.Taken.Add(held.Value);
            }
            var burglar = lot.Sims.TryGetValue(lot.Burglar, out var seen) ? seen.Guid : 0;
            Raise(vm, new LifeEvent()
            {
                Kind = LifeEventKind.BurglarGone,
                Sim = burglar,
                Others = Household(vm),
                Detail = string.Join("\n", lot.Taken),
                Value = lot.PoliceCame ? 1 : 0,
                Value3 = AtHome(vm) ? 1 : 0,
                Began = lot.BurglarCame,
            });
            lot.Burglar = null;
            lot.Taken.Clear();
            lot.Held.Clear();
        }

        /// <summary>The game is about to remove an object. If the burglar's script is doing it, it is being taken.</summary>
        public static void BeforeRemoveObject(VMStackFrame context, VMPrimitiveOperand args)
        {
            MemoriesPack.Guard("looking at an object being removed", () =>
            {
                var vm = context?.VM;
                if (!Watched(vm) || !Lots.TryGetValue(vm, out var lot) || lot.Burglar == null) return;
                var burglar = lot.Burglar;
                if (context.Caller != burglar && context.Thread?.ActiveAction?.Callee != burglar && context.Callee != burglar) return;
                var target = (args as VMRemoveObjectInstanceOperand)?.Target == 0 ? context.Caller : context.StackObject;
                if (target == null || target is VMAvatar) return;
                if (!lot.Taken.Contains(target.Name)) lot.Taken.Add(target.Name);
            });
        }

        // ---- fire ----

        /// <summary>The game has tried to start or spread a fire. The first on a lot with none starts an episode.</summary>
        public static void AfterBurn(VMStackFrame context, VMPrimitiveExitCode __result)
        {
            if (__result != VMPrimitiveExitCode.GOTO_TRUE) return;
            MemoriesPack.Guard("looking at a fire", () =>
            {
                var vm = context?.VM;
                if (!Watched(vm)) return;
                var lot = LotOf(vm);
                if (lot.Fire) return;
                lot.Fire = true;
                lot.FireBegan = Now(vm);
                var starter = Involved(context).FirstOrDefault(IsPerson);
                Raise(vm, new LifeEvent()
                {
                    Kind = LifeEventKind.FireStarted,
                    Sim = starter,
                    Others = HouseholdHere(vm),
                    Detail = starter == 0 ? null : context.Thread?.ActiveAction?.Name,
                    Value3 = AtHome(vm) ? 1 : 0,
                });
            });
        }

        private static void WatchFire(VM vm, Lot lot)
        {
            if (vm.Entities.Any(x => !x.Dead && x.Object?.OBJ?.GUID == VMBurn.FIRE_GUID)) return;
            lot.Fire = false;
            Raise(vm, new LifeEvent() { Kind = LifeEventKind.FireOut, Others = HouseholdHere(vm), Began = lot.FireBegan });
        }

        // ---- interactions ----

        private sealed class Start
        {
            public short? AtoB, BtoA;
        }

        private static readonly ConditionalWeakTable<VMQueuedAction, Start> Starts = new ConditionalWeakTable<VMQueuedAction, Start>();

        private static short Nid(VMAvatar avatar) => avatar.GetPersonData(VMPersonDataVariable.NeighborId);

        /// <summary>A Sim has begun an interaction. If it is with another Sim, note how the two got on before it.</summary>
        public static void AfterExecuteAction(VMQueuedAction action, VMEntity ___Entity, bool __result)
        {
            if (!__result || action == null) return;
            MemoriesPack.Guard("looking at an interaction starting", () =>
            {
                var actor = ___Entity as VMAvatar;
                var other = action.Callee as VMAvatar;
                if (actor == null || other == null || other == actor || actor.IsPet || other.IsPet) return;
                if (!Watched(actor.Thread?.Context?.VM)) return;
                Starts.AddOrUpdate(action, new Start() { AtoB = People.Daily(Nid(actor), Nid(other)), BtoA = People.Daily(Nid(other), Nid(actor)) });
            });
        }

        /// <summary>Set while the game cuts an interaction short, so that its end is known for what it is.</summary>
        [ThreadStatic]
        private static bool Aborting;

        public static void BeforeAbort() { Aborting = true; }
        public static void AfterAbort() { Aborting = false; }

        /// <summary>What an interaction is called: its name on screen, and the labels of the game's scripts that run it.</summary>
        private static string[] NamesOf(VMQueuedAction action)
        {
            var names = new List<string>();
            void Add(string name) { if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name); }
            Add(action?.Name);
            Add(action?.ActionRoutine?.Chunk?.ChunkLabel);
            Add(action?.ActionRoutine?.Rti?.Name);
            return names.ToArray();
        }

        /// <summary>The names of what a script is part of: the interaction it serves, and every routine on its thread's stack.</summary>
        private static List<string> StackNames(VMStackFrame frame)
        {
            var names = new List<string>(NamesOf(frame?.Thread?.ActiveAction));
            void Add(string name) { if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name); }
            var stack = frame?.Thread?.Stack;
            if (stack != null)
            {
                foreach (var f in stack)
                {
                    Add(f.Routine?.Chunk?.ChunkLabel);
                    Add(f.Routine?.Rti?.Name);
                }
            }
            if (frame != null)
            {
                Add(frame.Routine?.Chunk?.ChunkLabel);
                Add(frame.Routine?.Rti?.Name);
            }
            return names;
        }

        /// <summary>A Sim's interaction is ending, by itself or cut short.</summary>
        public static void BeforeEndInteraction(VMThread __instance, VMEntity ___Entity)
        {
            var aborted = Aborting;
            MemoriesPack.Guard("looking at an interaction ending", () =>
            {
                var thread = __instance;
                if (thread.IsCheck || thread.ActiveQueueBlock < 0 || thread.ActiveQueueBlock >= thread.Queue.Count) return;
                var actor = ___Entity as VMAvatar;
                if (actor == null || actor.IsPet) return;
                var vm = thread.Context?.VM;
                if (!Watched(vm)) return;
                var action = thread.Queue[thread.ActiveQueueBlock];
                var other = action.Callee as VMAvatar;
                if (other == actor || other?.IsPet == true) other = null;

                var wentWell = -1;
                if (other != null && Starts.TryGetValue(action, out var start))
                {
                    wentWell = Recognise.WentWell(start.AtoB, start.BtoA, People.Daily(Nid(actor), Nid(other)), People.Daily(Nid(other), Nid(actor)));
                    Starts.Remove(action);
                }
                var names = NamesOf(action);
                var actorGuid = actor.Object.OBJ.GUID;
                var otherGuid = other?.Object?.OBJ?.GUID ?? 0;
                Raise(vm, new LifeEvent()
                {
                    Kind = LifeEventKind.Interaction,
                    Sim = actorGuid,
                    Other = otherGuid,
                    ObjectGuid = other == null ? (action.Callee?.Object?.OBJ?.GUID ?? 0) : 0,
                    Detail = action.Name,
                    Names = names,
                    Value = wentWell,
                    Aborted = aborted,
                });

                if (other == null || aborted || !Recognise.Kiss.InAny(names)) return;
                if (wentWell != 1)
                {
                    Log.Write(actor.Name + " and " + other.Name + ": \"" + action.Name + "\" looks like a kiss, but did not seem to go well, so it is not counted.");
                    return;
                }
                var lot = LotOf(vm);
                var couple = Recognise.Couple(actorGuid, otherGuid);
                var minutes = Now(vm).Minutes;
                if (lot.LastKiss.TryGetValue(couple, out var last) && minutes - last < Recognise.KissMinutes && minutes >= last) return;
                lot.LastKiss[couple] = minutes;
                Raise(vm, new LifeEvent() { Kind = LifeEventKind.Kiss, Sim = actorGuid, Other = otherGuid, Detail = action.Name });
            });
        }

        // ---- the household ----

        /// <summary>The game is about to add a Sim to the household or take one out: remember who was in it.</summary>
        public static void BeforeGameCall(VMStackFrame context, VMPrimitiveOperand args, out uint[] __state)
        {
            __state = null;
            var call = (args as VMGenericTS1CallOperand)?.Call;
            if (call != VMGenericTS1CallMode.AddToFamily && call != VMGenericTS1CallMode.AddToFamilyInTemp0 && call != VMGenericTS1CallMode.RemoveFromFamily) return;
            //(the game replaces this list when it changes it, so the one held here stays as it was)
            __state = context?.VM?.TS1State?.CurrentFamily?.FamilyGUIDs;
        }

        public static void AfterGameCall(VMStackFrame context, uint[] __state)
        {
            if (__state == null) return;
            MemoriesPack.Guard("looking at the household changing", () =>
            {
                var vm = context.VM;
                if (!Watched(vm)) return;
                var before = __state;
                var after = vm.TS1State.CurrentFamily.FamilyGUIDs ?? new uint[0];
                var names = StackNames(context);
                var interaction = context.Thread?.ActiveAction?.Name;

                foreach (var newcomer in after.Where(g => !before.Contains(g) && IsPerson(g)).ToList())
                {
                    var involved = Involved(context);
                    var host = involved.Where(g => g != newcomer && before.Contains(g)).Cast<uint?>().FirstOrDefault() ?? 0;
                    Raise(vm, new LifeEvent() { Kind = LifeEventKind.JoinedHousehold, Sim = newcomer, Other = host, Detail = interaction });
                    if (host != 0 && People.IsAdult(newcomer) && People.IsAdult(host) && Recognise.Proposal.InAny(names))
                        Raise(vm, new LifeEvent() { Kind = LifeEventKind.Married, Sim = host, Other = newcomer, Detail = interaction });
                    else if (host != 0) Log.Write(People.FirstName(newcomer) + " moved in with " + People.FirstName(host) + ", not by a proposal (" + string.Join(" | ", names) + ").");
                }

                foreach (var leaver in before.Where(g => !after.Contains(g) && IsPerson(g)).ToList())
                {
                    var household = before.Where(g => g != leaver && IsPerson(g)).ToArray();
                    Raise(vm, new LifeEvent() { Kind = LifeEventKind.LeftHousehold, Sim = leaver, Others = household, Detail = interaction });
                    if (Recognise.Death.InAny(names)) SimDied(vm, LotOf(vm), leaver, "left the household as they died (" + string.Join(" | ", names.Where(Recognise.Death.In)) + ")", household);
                    else Log.Write(People.FirstName(leaver) + " left the household, not by dying as far as can be told (" + string.Join(" | ", names) + ").");
                }
            });
        }

        // ---- babies ----

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
        /// not depend on the game's language or on knowing the object beforehand. (As the Family Tree pack finds it.)
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

        /// <summary>
        /// The Sims who have a hand in what a script is doing, most involved first: whoever is running it, then the
        /// Sims it is being done to or with at each step back up the script, the Sim their interaction is with, and
        /// any Sim in the same bed or seat as one of those. (As the Family Tree pack finds them.)
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
            foreach (var entity in entities.ToList())
            {
                var holder = entity.Container;
                if (holder == null) continue;
                for (int slot = 0; slot < holder.TotalSlots(); slot++) Add(holder.GetSlot(slot));
            }
            return result;
        }

        /// <summary>The game has made an object. If a script made it and it is a baby, it has arrived: note who its parents are.</summary>
        public static void AfterObjectMade(VMMultitileGroup __result)
        {
            var frame = Creating;
            if (frame == null || __result == null) return;
            MemoriesPack.Guard("looking at a new object", () =>
            {
                var vm = frame.VM;
                if (!Watched(vm)) return;
                var obj = __result.BaseObject;
                if (!IsBaby(obj)) return;
                var household = Household(vm);
                var tree = MemoryBook.FamilyTree;
                var parents = Recognise.PickParents(Involved(frame), household, People.IsAdult, tree.SpouseOf);
                var house = House(vm);
                var journal = MemoryBook.Journal;
                if (journal != null)
                {
                    //forget babies of this house that are no longer there (taken away, or the lot was not saved)
                    journal.Babies.RemoveAll(b => b.House == house && (b.ObjectId == obj.ObjectID || vm.GetObjectById((short)b.ObjectId)?.Object?.OBJ?.GUID != b.ObjectGuid));
                    journal.Babies.Add(new WaitingBaby()
                    {
                        House = house,
                        ObjectId = obj.ObjectID,
                        ObjectGuid = obj.Object.OBJ.GUID,
                        ParentA = parents.Count > 0 ? parents[0] : 0,
                        ParentB = parents.Count > 1 ? parents[1] : 0,
                        Arrived = Now(vm),
                    });
                }
                Raise(vm, new LifeEvent()
                {
                    Kind = LifeEventKind.BabyArrived,
                    Sim = parents.Count > 0 ? parents[0] : 0,
                    Other = parents.Count > 1 ? parents[1] : 0,
                    ObjectGuid = obj.Object.OBJ.GUID,
                    Value = obj.ObjectID,
                    Detail = frame.Thread?.ActiveAction?.Name,
                });
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

        /// <summary>The game has made a character. If it was made from a baby, the baby has grown into a child.</summary>
        public static void AfterMakeCharacter(VMStackFrame context, VMPrimitiveOperand args, List<int> __state, VMPrimitiveExitCode __result)
        {
            MemoriesPack.Guard("looking at a new character", () =>
            {
                var vm = context.VM;
                if (!Watched(vm) || __result != VMPrimitiveExitCode.GOTO_TRUE) return;
                if ((args as VMTS1MakeNewCharacterOperand)?.AvatarType != 0) return; //a pet
                var neighbour = People.ByNeighbourId(context.StackObjectID);
                if (neighbour == null) return;
                var child = neighbour.GUID;
                var journal = MemoryBook.Journal;
                var house = House(vm);
                var baby = journal == null ? null : Recognise.TakeBaby(journal.Babies, house, __state ?? new List<int>(), out var guessed);
                var fromBaby = baby != null || (__state ?? new List<int>()).Any(id => IsBaby(vm.GetObjectById((short)id)));
                if (!fromBaby)
                {
                    Log.Write("A new character, " + People.FirstName(child) + ", not made from a baby. Not a birth.");
                    return;
                }
                if (People.IsAdult(child))
                {
                    Log.Write(People.FirstName(child) + " was made from a baby but is an adult; not counted as a birth.");
                    return;
                }
                Raise(vm, new LifeEvent()
                {
                    Kind = LifeEventKind.ChildFromBaby,
                    Sim = child,
                    Others = baby?.Parents() ?? new uint[0],
                    Began = baby?.Arrived,
                });
            });
        }
    }
}
