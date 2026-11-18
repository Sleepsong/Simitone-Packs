/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Scopes;
using FSO.SimAntics.Model;
using System;
using System.Linq;

namespace Chemistry
{
    /// <summary>What the game knows about a Sim that chemistry needs: who can feel it, and their traits.</summary>
    public static class Sims
    {
        /// <summary>
        /// Whether a Sim has turn-ons at all. Grown Sims only: not children (nor the Life Stages pack's teens, who are
        /// children to the game), pets, or ghosts. An elder is grown. The game reads an age of 0 as grown.
        /// </summary>
        public static bool CanFeel(VMAvatar sim)
        {
            if (sim == null) return false;
            if (!IsGrown(sim.GetPersonData(VMPersonDataVariable.PersonsAge), sim.GetPersonData(VMPersonDataVariable.Gender))) return false;
            if (sim.GetPersonData(VMPersonDataVariable.IsGhost) != 0) return false;
            var kind = BodyStrings(sim)?.GetString(0);
            return string.IsNullOrWhiteSpace(kind) || kind.Trim().Equals("adult", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanFeel(Neighbour sim)
        {
            if (sim?.PersonData == null) return false;
            if (!IsGrown(Data(sim, VMPersonDataVariable.PersonsAge), Data(sim, VMPersonDataVariable.Gender))) return false;
            if (Data(sim, VMPersonDataVariable.IsGhost) != 0) return false;
            var kind = BodyStrings(sim.GUID)?.GetString(0);
            return string.IsNullOrWhiteSpace(kind) || kind.Trim().Equals("adult", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The game's line between a child and an adult is 18. Gender carries the dog and cat flags.</summary>
        public static bool IsGrown(short age, short gender) => (age >= 18 || age == 0) && (gender & (8 | 16)) == 0;

        public static uint Guid(VMAvatar sim) => sim?.Object?.OBJ?.GUID ?? 0;

        public static string Name(VMAvatar sim)
        {
            try { return sim?.Name ?? "?"; }
            catch (Exception) { return "a Sim"; }
        }

        // ---- traits ----

        public static Facts Of(VMAvatar sim)
        {
            short D(VMPersonDataVariable v) => sim.GetPersonData(v);
            return new Facts()
            {
                Nice = D(VMPersonDataVariable.NicePersonality),
                Active = D(VMPersonDataVariable.ActivePersonality),
                Playful = D(VMPersonDataVariable.PlayfulPersonality),
                Outgoing = D(VMPersonDataVariable.OutgoingPersonality),
                Neat = D(VMPersonDataVariable.NeatPersonality),
                Cooking = D(VMPersonDataVariable.CookingSkill),
                Charisma = D(VMPersonDataVariable.CharismaSkill),
                Mechanical = D(VMPersonDataVariable.MechanicalSkill),
                Creativity = D(VMPersonDataVariable.CreativitySkill),
                Body = D(VMPersonDataVariable.BodySkill),
                Logic = D(VMPersonDataVariable.LogicSkill),
                Employed = HasJob(D(VMPersonDataVariable.JobType)),
                Build = BuildOf(BodyStrings(sim)),
                Hygiene = sim.GetMotiveData(VMMotive.Hygiene),
                Clothes = ClothesOf(sim),
            };
        }

        /// <summary>
        /// A Sim who is not on the lot, from what the neighbourhood keeps of them: what does not change from hour to
        /// hour. How clean they are and what they are wearing are not known, so they neither stink nor are in swimwear.
        /// </summary>
        public static Facts Of(Neighbour sim)
        {
            short D(VMPersonDataVariable v) => Data(sim, v);
            return new Facts()
            {
                Nice = D(VMPersonDataVariable.NicePersonality),
                Active = D(VMPersonDataVariable.ActivePersonality),
                Playful = D(VMPersonDataVariable.PlayfulPersonality),
                Outgoing = D(VMPersonDataVariable.OutgoingPersonality),
                Neat = D(VMPersonDataVariable.NeatPersonality),
                Cooking = D(VMPersonDataVariable.CookingSkill),
                Charisma = D(VMPersonDataVariable.CharismaSkill),
                Mechanical = D(VMPersonDataVariable.MechanicalSkill),
                Creativity = D(VMPersonDataVariable.CreativitySkill),
                Body = D(VMPersonDataVariable.BodySkill),
                Logic = D(VMPersonDataVariable.LogicSkill),
                Employed = HasJob(D(VMPersonDataVariable.JobType)),
                Build = BuildOf(BodyStrings(sim.GUID)),
                Hygiene = null,
                Clothes = Clothes.Everyday,
            };
        }

        private static short Data(Neighbour sim, VMPersonDataVariable variable)
        {
            var data = sim?.PersonData;
            var index = (int)variable;
            return (data != null && index < data.Length) ? data[index] : (short)0;
        }

        /// <summary>A job is a career the game has. (Its job panel says "Unemployed" for any other.)</summary>
        private static bool HasJob(short type)
        {
            if (type <= 0) return false;
            try
            {
                var jobs = Content.Get()?.Jobs;
                if (jobs != null) return jobs.GetJob((ushort)type) != null;
            }
            catch (Exception)
            {
                //no careers to look in (the pack's checks): any job number is a job
            }
            return true;
        }

        private static STR BodyStrings(VMAvatar sim)
        {
            try
            {
                var obj = sim?.Object;
                return obj?.Resource?.Get<STR>(obj.OBJ.BodyStringID);
            }
            catch (Exception) { return null; }
        }

        private static STR BodyStrings(uint guid)
        {
            try
            {
                var obj = Content.Get()?.WorldObjects?.Get(guid);
                return obj?.Resource?.Get<STR>(obj.OBJ.BodyStringID);
            }
            catch (Exception) { return null; }
        }

        /// <summary>A grown Sim's build, as the game itself tells it when it dresses them: from the name of their everyday body.</summary>
        public static Build BuildOf(STR bodyStrings)
        {
            var body = bodyStrings?.GetString(1);
            return BuildOf(body);
        }

        public static Build BuildOf(string everydayBody)
        {
            if (string.IsNullOrWhiteSpace(everydayBody)) return Build.Unknown;
            var body = everydayBody.ToLowerInvariant();
            if (body.Contains("fat")) return Build.Fat;
            if (body.Contains("skn")) return Build.Skinny;
            return Build.Fit;
        }

        private static readonly (VMPersonSuits suit, Clothes clothes)[] Suits =
        {
            (VMPersonSuits.DefaultSwimwear, Clothes.Swimwear),
            (VMPersonSuits.TS1Formal, Clothes.Formal),
            (VMPersonSuits.DefaultSleepwear, Clothes.Sleepwear),
        };

        /// <summary>
        /// What a Sim has on: the body they are wearing, put beside the swimwear, formal wear and pyjamas the game would
        /// dress them in. (The game's note of the outfit last put on cannot be trusted: some scripts leave the wrong
        /// number in it.) Anything else is everyday clothes.
        /// </summary>
        public static Clothes ClothesOf(VMAvatar sim)
        {
            try
            {
                var now = sim?.BodyOutfit?.OftData;
                if (now?.TS1AppearanceID == null) return Clothes.Everyday;
                foreach (var (suit, clothes) in Suits)
                {
                    var that = VMSuitProvider.GetPersonSuitTS1(sim, (ushort)suit)?.OftData;
                    if (that != null && Same(that.TS1AppearanceID, now.TS1AppearanceID) && Same(that.TS1TextureID, now.TS1TextureID)) return clothes;
                }
            }
            catch (Exception)
            {
                //a Sim whose clothes cannot be read is in everyday clothes
            }
            return Clothes.Everyday;
        }

        private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        // ---- chemistry between two Sims ----

        /// <summary>How the first Sim feels about the second, both on the lot.</summary>
        public static Level Between(VMAvatar from, VMAvatar to)
        {
            if (from == null || to == null || from == to || !CanFeel(from) || !CanFeel(to)) return Level.None;
            var fromGuid = Guid(from);
            var toGuid = Guid(to);
            if (fromGuid == 0 || toGuid == 0) return Level.None;
            return Attraction.Between(LikesStore.For(fromGuid), Of(from), LikesStore.For(toGuid), Of(to));
        }

        /// <summary>How one Sim of the neighbourhood feels about another: from the live Sims if they are on this lot, else from what the neighbourhood keeps.</summary>
        public static Level Between(Neighbour from, Neighbour to, VM vm)
        {
            if (from == null || to == null || from.GUID == to.GUID) return Level.None;
            var liveFrom = OnLot(vm, from.NeighbourID);
            var liveTo = OnLot(vm, to.NeighbourID);
            if (liveFrom != null && liveTo != null) return Between(liveFrom, liveTo);
            if (!(liveFrom != null ? CanFeel(liveFrom) : CanFeel(from)) || !(liveTo != null ? CanFeel(liveTo) : CanFeel(to))) return Level.None;
            var fromFacts = liveFrom != null ? Of(liveFrom) : Of(from);
            var toFacts = liveTo != null ? Of(liveTo) : Of(to);
            return Attraction.Between(LikesStore.For(from.GUID), fromFacts, LikesStore.For(to.GUID), toFacts);
        }

        /// <summary>The Sim on the lot with this neighbour number, if any.</summary>
        public static VMAvatar OnLot(VM vm, int neighbourId)
        {
            if (vm?.Entities == null || neighbourId <= 0) return null;
            foreach (var e in vm.Entities)
            {
                if (e is VMAvatar a && a.GetPersonData(VMPersonDataVariable.NeighborId) == neighbourId) return a;
            }
            return null;
        }

        /// <summary>The grown Sims on the lot, for the console.</summary>
        public static VMAvatar[] GrownOnLot(VM vm) => vm?.Entities?.OfType<VMAvatar>().Where(CanFeel).ToArray() ?? new VMAvatar[0];
    }
}
