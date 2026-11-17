/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Aspirations
{
    /// <summary>
    /// The pack's eyes on the game. The Sims has no events for "a skill point was gained" or "a friend was made", so
    /// every few ticks each Sim of the household being played is looked at and compared with the last look:
    ///
    ///   skills, job, level and performance, and the household's money     from the Sim and the family
    ///   friends, best friends and loves                                    from the neighbourhood's relationships
    ///   an interaction finished, and whether it went down well             from the Sim's action queue, and the
    ///                                                                      relationship before and after
    ///   someone else's interaction with the Sim (a slap)                   from the others' action queues
    ///   an object bought, a baby arrived                                   objects new on the lot
    ///   a fire, a burglar, guests                                          from the lot
    ///
    /// What it sees is handed to the rules (Aspiration.Step) as a SimState and a list of Happenings, and what they
    /// say has happened is shown to the player (AspirationsPack.Tell) and done to the game (GameEffects).
    /// </summary>
    public static class Watcher
    {
        public static string PackId = "sample.aspirations";
        public static Balance Balance = new Balance();
        public static Random Rng = new Random();

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
                    Current = Ledger.Read(PackId, path, Balance);
                    CurrentPath = path;
                }
                return Current;
            }
        }

        /// <summary>Drop what is in memory, so it is read from the file again: a neighbourhood loaded, or a lot left.</summary>
        public static void Forget()
        {
            Current = null;
            CurrentPath = null;
        }

        public static void Saved()
        {
            if (Current != null && CurrentPath != null) Current.Write(PackId, CurrentPath);
        }

        public static void SettingsChanged()
        {
            if (Current != null && CurrentPath != null) Current.WriteSettings(PackId, CurrentPath, Balance);
        }

        /// <summary>Where the loaded neighbourhood is kept. The tests put another in its place.</summary>
        public static Func<string> NeighbourhoodPath = () => Content.Get()?.Neighborhood?.UserPath;

        public static SimRecord RecordOf(VMAvatar avatar) => Ledger?.Meet(avatar.Object.OBJ.GUID, Balance);

        // ---- who ----

        /// <summary>The people (not pets, nor ghosts) of the played household who are on this lot.</summary>
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

        public static bool AtHome(VM vm)
        {
            var family = vm?.TS1State?.CurrentFamily;
            return family != null && family.HouseNumber != 0 && family.HouseNumber == vm.GetGlobalValue(10);
        }

        public static bool IsChild(short age) => age < 18;

        // ---- watching a lot ----

        private class Doing
        {
            public ushort UID;
            public string Name;
            /// <summary>For an object: its name, so a want to play chess knows the chess table's "Play".</summary>
            public string Thing;
            public Partner With;
            public int Person;
            public int DailyBefore;
            public int Started;
        }

        private class SimWatch
        {
            public int[] Skills;
            public double SkillCarry;
            public bool Sleeping;
            public Doing Doing;
            /// <summary>Others' interactions with this Sim already counted, by the other's object number.</summary>
            public Dictionary<short, ushort> Received = new Dictionary<short, ushort>();
        }

        private class LotWatch
        {
            public int Ticks;
            public int LastMinute = -1;
            public int LastHour = -1;
            public HashSet<VMMultitileGroup> Groups;
            public Dictionary<uint, SimWatch> Sims = new Dictionary<uint, SimWatch>();
        }

        private static readonly ConditionalWeakTable<VM, LotWatch> Watches = new ConditionalWeakTable<VM, LotWatch>();

        /// <summary>How often the household is looked at, in ticks: three times a second at normal speed.</summary>
        public const int LookEvery = 10;
        /// <summary>An interaction must last this long (in ticks) to count, unless it changed a relationship: one called off at once does not.</summary>
        public const int ShortestInteraction = 90;
        /// <summary>A clock that jumps further than this (in minutes) has been set, not lived: no time passes for the meter.</summary>
        public const int LongestStep = 180;

        /// <summary>Called after every tick of every lot.</summary>
        public static void AfterTick(VM vm)
        {
            if (vm == null || !vm.TS1 || vm.TS1State?.CurrentFamily == null) return;
            var watch = Watches.GetValue(vm, x => new LotWatch());
            watch.Ticks++;
            var ledger = Ledger;
            if (ledger == null) return;
            var household = Household(vm);

            // needs kept full are kept so at every tick, so nothing in between can show them falling
            if (ledger.Settings.On)
                foreach (var avatar in household) GameEffects.Hold(avatar, ledger.Find(avatar.Object.OBJ.GUID));

            if (watch.Ticks % LookEvery != 0) return;
            if (!ledger.Settings.On)
            {
                foreach (var avatar in household) GameEffects.SetNeedDecay(avatar, 1);
                return;
            }
            Look(vm, watch, ledger, household);
        }

        private static void Look(VM vm, LotWatch watch, Ledger ledger, List<VMAvatar> household)
        {
            var b = Balance;
            var clock = vm.Context.Clock;
            var minuteOfDay = clock.Hours * 60 + clock.Minutes;
            var minutes = watch.LastMinute < 0 ? 0 : (minuteOfDay - watch.LastMinute + 1440) % 1440;
            if (minutes > LongestStep) minutes = 0;
            watch.LastMinute = minuteOfDay;
            var nine = watch.LastHour >= 0 && watch.LastHour != clock.Hours && clock.Hours == 9;
            watch.LastHour = clock.Hours;

            var lot = LookAtLot(vm, watch);
            var family = vm.TS1State.CurrentFamily;

            // a voucher pays for the first thing bought, whoever of the household holds it
            foreach (var bought in lot.Bought)
            {
                foreach (var avatar in household)
                {
                    var record = ledger.Find(avatar.Object.OBJ.GUID);
                    var voucher = record?.Effects.FirstOrDefault(x => x.Kind == "Voucher");
                    if (voucher == null) continue;
                    var refund = Math.Min(voucher.Value, bought.InitialPrice);
                    if (refund <= 0) continue;
                    family.Budget += refund;
                    //it cannot be sold back for the part that was free
                    bought.InitialPrice = Math.Max(0, bought.InitialPrice - refund);
                    record.Effects.Remove(voucher);
                    Log.Write(avatar.Name + "'s voucher paid " + Rules.Money(refund) + " of an object.");
                    AspirationsPack.Notice(avatar.Name + "'s " + voucher.Name + " paid " + Rules.Money(refund) + ".", NoticeKind.Reward);
                    break;
                }
            }

            foreach (var avatar in household)
            {
                var guid = avatar.Object.OBJ.GUID;
                var record = RecordOf(avatar);
                if (!watch.Sims.TryGetValue(guid, out var sim)) watch.Sims[guid] = sim = new SimWatch();

                var state = StateOf(vm, avatar, household, lot);
                var happenings = new List<Happening>(lot.Happenings);
                Interactions(vm, avatar, sim, watch.Ticks, household, happenings);

                // learning faster: a share more of every gain
                var skills = state.Skills.ToArray();
                if (sim.Skills != null) GameEffects.Learn(avatar, record, sim.Skills, skills, ref sim.SkillCarry);
                sim.Skills = Raw(avatar);

                var woke = sim.Sleeping && !state.Sleeping;
                sim.Sleeping = state.Sleeping;

                if (nine) GameEffects.PayDividends(avatar, record, family);

                var news = Aspiration.Step(record, state, happenings, minutes, woke, b, Rng);
                foreach (var h in happenings.Where(x => x.Kind == HappeningKind.Done || x.Kind == HappeningKind.Received)) Log.Write(avatar.Name + ": " + h);
                AspirationsPack.Tell(vm, avatar, record, news);
                GameEffects.SetNeedDecay(avatar, Rules.Level(record.Meter, b).NeedDecay);
                if (!record.Asked && !record.Child) AspirationsPack.AskLater(vm, avatar);
            }
        }

        /// <summary>
        /// The pack itself has changed a Sim's skills (a skill point bought): take them as they are now, so the
        /// next look does not count the change as learning.
        /// </summary>
        public static void Resync(VM vm, VMAvatar avatar)
        {
            if (vm == null || !Watches.TryGetValue(vm, out var watch)) return;
            if (watch.Sims.TryGetValue(avatar.Object.OBJ.GUID, out var sim)) sim.Skills = Raw(avatar);
        }

        public static int[] Raw(VMAvatar avatar) => SkillData.Select(v => (int)avatar.GetPersonData(v)).ToArray();

        // ---- the lot ----

        private class LotLook
        {
            public List<Happening> Happenings = new List<Happening>();
            public List<VMMultitileGroup> Bought = new List<VMMultitileGroup>();
            public bool Fire;
        }

        private static LotLook LookAtLot(VM vm, LotWatch watch)
        {
            var look = new LotLook();
            var groups = new HashSet<VMMultitileGroup>();
            var first = watch.Groups == null;
            foreach (var entity in vm.Entities)
            {
                if (entity == null || entity.Dead) continue;
                if (!look.Fire && entity.GetFlag(VMEntityFlags.Burning)) look.Fire = true;
                if (entity is VMAvatar) continue;
                var group = entity.MultitileGroup;
                if (group == null || group.BaseObject != entity || !groups.Add(group)) continue;
                if (first || watch.Groups.Contains(group)) continue;
                //new on the lot since the last look
                if (IsBaby(entity))
                {
                    look.Happenings.Add(new Happening() { Kind = HappeningKind.Baby, Name = "baby" });
                    Log.Write("A baby has arrived (object " + entity.Object.OBJ.GUID.ToString("X8") + ").");
                }
                else if (group.InitialPrice > 0 && AtHome(vm))
                {
                    look.Happenings.Add(new Happening() { Kind = HappeningKind.Purchase, Name = Label(entity), Price = group.InitialPrice });
                    look.Bought.Add(group);
                    Log.Write("Bought: " + Label(entity) + " for " + Rules.Money(group.InitialPrice) + ".");
                }
            }
            watch.Groups = groups;
            return look;
        }

        private static string Label(VMEntity entity) => entity.Object?.OBJ?.ChunkLabel ?? entity.ToString();

        private static readonly Dictionary<uint, bool> BabyKinds = new Dictionary<uint, bool>();
        /// <summary>The game's "make new character" primitive, and where its operand says what kind (0: a Sim, not a pet).</summary>
        private const ushort MakeNewCharacterOpcode = 19;
        private const int AvatarTypeByte = 3;

        /// <summary>
        /// Whether an object is a baby: one whose own scripts can make a new Sim. Found from the scripts, as the Family
        /// Tree pack finds it, so it does not depend on the game's language.
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
            return baby;
        }

        // ---- a Sim ----

        private static readonly VMPersonDataVariable[] SkillData =
        {
            VMPersonDataVariable.CookingSkill, VMPersonDataVariable.MechanicalSkill, VMPersonDataVariable.CharismaSkill,
            VMPersonDataVariable.BodySkill, VMPersonDataVariable.LogicSkill, VMPersonDataVariable.CreativitySkill,
        };

        private static readonly VMPersonDataVariable[] TraitData =
        {
            VMPersonDataVariable.NeatPersonality, VMPersonDataVariable.OutgoingPersonality, VMPersonDataVariable.ActivePersonality,
            VMPersonDataVariable.PlayfulPersonality, VMPersonDataVariable.NicePersonality,
        };

        public static readonly VMMotive[] NeedData =
        {
            VMMotive.Hunger, VMMotive.Comfort, VMMotive.Hygiene, VMMotive.Bladder, VMMotive.Energy, VMMotive.Fun,
            VMMotive.Social, VMMotive.Room, VMMotive.Mood,
        };

        public static int SkillIndexOf(VMPersonDataVariable v) => Array.IndexOf(SkillData, v);
        public static VMPersonDataVariable SkillVariable(int skill) => SkillData[skill];

        private static TS1NeighborhoodProvider Hood => Content.Get()?.Neighborhood;

        private static short Nid(VMAvatar avatar) => avatar.GetPersonData(VMPersonDataVariable.NeighborId);

        /// <summary>The daily relationship of one neighbour with another, as the game keeps it.</summary>
        public static int Daily(int from, int to)
        {
            var me = Hood?.GetNeighborByID((short)from);
            if (me?.Relationships == null || !me.Relationships.TryGetValue(to, out var values) || values.Count == 0) return 0;
            return values[0];
        }

        private static readonly HashSet<short> LoveValuesSeen = new HashSet<short>();

        /// <summary>What the game shows of a Sim now, for the rules.</summary>
        public static SimState StateOf(VM vm, VMAvatar avatar, List<VMAvatar> household)
        {
            return StateOf(vm, avatar, household, null);
        }

        private static SimState StateOf(VM vm, VMAvatar avatar, List<VMAvatar> household, LotLook lot)
        {
            var b = Balance;
            var s = new SimState();
            s.Child = IsChild(avatar.GetPersonData(VMPersonDataVariable.PersonsAge));
            for (int i = 0; i < SkillData.Length; i++) s.Skills[i] = Math.Clamp((int)avatar.GetPersonData(SkillData[i]), 0, 1000);
            for (int i = 0; i < TraitData.Length; i++) s.Traits[i] = Math.Clamp((int)avatar.GetPersonData(TraitData[i]), 0, 1000);
            for (int i = 0; i < NeedData.Length; i++) s.Needs[i] = avatar.GetMotiveData(NeedData[i]);
            s.Sleeping = avatar.GetMotiveData(VMMotive.SleepState) != 0;

            // work, or school
            s.JobType = avatar.GetPersonData(VMPersonDataVariable.JobType);
            s.JobLevel = avatar.GetPersonData(VMPersonDataVariable.JobPromotionLevel);
            s.Performance = avatar.GetPersonData(VMPersonDataVariable.JobPerformance);
            var job = s.JobType == 0 ? null : Content.Get()?.Jobs?.GetJob((ushort)s.JobType);
            if (job?.JobLevels != null && job.JobLevels.Length > 0)
            {
                s.JobLevels = job.JobLevels.Length;
                s.JobLevel = Math.Clamp(s.JobLevel, 0, s.JobLevels - 1);
                s.Career = job.Name ?? "";
                if (s.JobLevel + 1 < s.JobLevels)
                {
                    var next = job.JobLevels[s.JobLevel + 1];
                    s.NextSkills = Enumerable.Range(0, Rules.SkillNames.Length).Select(i => next.MinRequired.Length > i + 1 ? next.MinRequired[i + 1] : 0).ToArray();
                }
            }
            else if (s.JobType != 0 && !s.Child)
            {
                //a job the game has no record of: treat as none, so no want is made about it
                s.JobType = 0;
            }

            var family = vm.TS1State.CurrentFamily;
            s.Funds = family.Budget;
            s.AtHome = AtHome(vm);

            Relationships(avatar, s, b);
            Household(vm, avatar, family, s);

            // the lot
            var mine = new HashSet<VMAvatar>(household);
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                var other = entity as VMAvatar;
                if (other == null || other == avatar || other.Dead || other.IsPet) continue;
                if (other.GetValue(VMStackObjectVariable.Hidden) > 0) continue;
                s.Others++;
                if (mine.Contains(other)) continue;
                s.Visitors.Add(((other.Name ?? "") + " " + (other.Object?.OBJ?.ChunkLabel ?? "") + " " + (other.Object?.Resource?.MainIff?.Filename ?? "")).ToLowerInvariant());
                if (other.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) != 0) s.Guests++;
            }
            if (lot != null) s.Fire = lot.Fire;
            else s.Fire = vm.Entities.Any(e => e != null && !e.Dead && e.GetFlag(VMEntityFlags.Burning));
            return s;
        }

        private static void Relationships(VMAvatar avatar, SimState s, Balance b)
        {
            var hood = Hood;
            var nid = Nid(avatar);
            var me = hood?.GetNeighborByID(nid);
            if (me?.Relationships == null) return;
            var rules = b.Relationships;
            foreach (var pair in me.Relationships)
            {
                var values = pair.Value;
                if (values == null || values.Count == 0 || pair.Key == nid) continue;
                var them = hood.GetNeighborByID((short)pair.Key);
                if (them == null || IsPet(them.PersonData)) continue;
                if (values.Count > rules.LoveIndex && rules.LoveIndex >= 0)
                {
                    var flags = values[rules.LoveIndex];
                    if (flags != 0 && LoveValuesSeen.Add(flags))
                        Log.Write("Relationship number " + rules.LoveIndex + " has the value " + flags + " (0x" + flags.ToString("X") + ") between " + me.Name + " and " + them.Name + ": " + ((flags & rules.LoveMask) != 0 ? "taken as love" : "not taken as love") + ".");
                    if ((flags & rules.LoveMask) != 0) s.Loves.Add(pair.Key);
                }
                var daily = values[0];
                if (daily < rules.FriendDaily) continue;
                var back = them.Relationships != null && them.Relationships.TryGetValue(nid, out var theirs) && theirs.Count > 0 ? theirs[0] : (short)0;
                if (back < rules.FriendDaily) continue;
                s.Friends.Add(pair.Key);
                var best = values.Count > 2 ? values[2] >= rules.BestLifetime : daily >= rules.BestDaily;
                if (best) s.BestFriends.Add(pair.Key);
            }
        }

        private static bool IsPet(short[] personData)
        {
            var gender = (int)VMPersonDataVariable.Gender;
            return personData != null && personData.Length > gender && (personData[gender] & (8 | 16)) != 0;
        }

        private static void Household(VM vm, VMAvatar avatar, FSO.Files.Formats.IFF.Chunks.FAMI family, SimState s)
        {
            var guids = family.FamilyGUIDs ?? new uint[0];
            s.HouseholdSize = guids.Length;
            var onLot = new Dictionary<uint, VMAvatar>();
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
                if (entity is VMAvatar a && !a.Dead) onLot[a.Object.OBJ.GUID] = a;
            var hood = Hood;
            foreach (var guid in guids)
            {
                short age, performance;
                bool pet;
                if (onLot.TryGetValue(guid, out var a))
                {
                    age = a.GetPersonData(VMPersonDataVariable.PersonsAge);
                    performance = a.GetPersonData(VMPersonDataVariable.JobPerformance);
                    pet = a.IsPet;
                }
                else
                {
                    var id = hood?.GetNeighborIDForGUID(guid);
                    var data = id == null ? null : hood.GetNeighborByID(id.Value)?.PersonData;
                    if (data == null) continue;
                    age = data.Length > (int)VMPersonDataVariable.PersonsAge ? data[(int)VMPersonDataVariable.PersonsAge] : (short)30;
                    performance = data.Length > (int)VMPersonDataVariable.JobPerformance ? data[(int)VMPersonDataVariable.JobPerformance] : (short)0;
                    pet = IsPet(data);
                }
                if (pet) continue;
                if (IsChild(age))
                {
                    s.HouseholdChildren++;
                    if (guid != avatar.Object.OBJ.GUID) s.ChildGrades[guid] = performance;
                }
                else s.HouseholdAdults++;
            }
        }

        // ---- interactions ----

        private static void Interactions(VM vm, VMAvatar avatar, SimWatch sim, int tick, List<VMAvatar> household, List<Happening> happenings)
        {
            var nid = Nid(avatar);
            var action = avatar.Thread?.ActiveAction;
            var uid = action?.UID ?? 0;
            var name = action?.Name;

            // the one before has ended: was it taken well?
            if (sim.Doing != null && (action == null || sim.Doing.UID != uid || sim.Doing.Name != name))
            {
                var done = sim.Doing;
                sim.Doing = null;
                var lasted = tick - done.Started;
                var outcome = 0;
                if (done.Person != 0) outcome = Math.Sign(Daily(nid, done.Person) - done.DailyBefore);
                //one called off at once does not count, unless it changed how the two get on
                if (done.With != Partner.None && (outcome != 0 || lasted >= ShortestInteraction))
                    happenings.Add(new Happening() { Kind = HappeningKind.Done, Name = (done.Name + (done.Thing != null ? " | " + done.Thing : "")).ToLowerInvariant(), With = done.With, Person = done.Person, Outcome = outcome });
            }
            if (sim.Doing == null && action != null && !string.IsNullOrEmpty(name))
            {
                var doing = new Doing() { UID = uid, Name = name, Started = tick };
                var callee = action.Callee;
                if (callee is VMAvatar other && other != avatar)
                {
                    if (!other.IsPet)
                    {
                        doing.Person = Nid(other);
                        doing.With = household.Contains(other) ? Partner.Household : Partner.Other;
                        doing.DailyBefore = Daily(nid, doing.Person);
                    }
                }
                else if (callee != null && !(callee is VMAvatar))
                {
                    doing.With = Partner.Object;
                    doing.Thing = callee.Object?.OBJ?.ChunkLabel;
                }
                sim.Doing = doing;
            }

            // what the others are doing to this Sim
            var seen = new HashSet<short>();
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                var other = entity as VMAvatar;
                if (other == null || other == avatar || other.Dead) continue;
                var theirs = other.Thread?.ActiveAction;
                if (theirs?.Callee != avatar || string.IsNullOrEmpty(theirs.Name)) continue;
                //the other side of something this Sim is doing to them, not something done to this Sim
                if (sim.Doing != null && sim.Doing.Person != 0 && sim.Doing.Person == Nid(other) && string.Equals(sim.Doing.Name, theirs.Name, StringComparison.OrdinalIgnoreCase)) continue;
                seen.Add(other.ObjectID);
                if (sim.Received.TryGetValue(other.ObjectID, out var counted) && counted == theirs.UID) continue;
                sim.Received[other.ObjectID] = theirs.UID;
                happenings.Add(new Happening()
                {
                    Kind = HappeningKind.Received,
                    Name = theirs.Name.ToLowerInvariant(),
                    With = household.Contains(other) ? Partner.Household : Partner.Other,
                    Person = other.IsPet ? 0 : Nid(other),
                });
            }
            foreach (var gone in sim.Received.Keys.Where(k => !seen.Contains(k)).ToList()) sim.Received.Remove(gone);
        }
    }
}
