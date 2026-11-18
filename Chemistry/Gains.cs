/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using System;
using System.Collections.Generic;

namespace Chemistry
{
    /// <summary>
    /// Where chemistry meets the game. Every change to how one Sim feels about another goes through one primitive,
    /// Relationship (VMRelationship.Execute), whether a script adds to a score or works a new one out and sets it. So
    /// the pack notes the score before the primitive runs and looks again after: if it went up, between two grown Sims,
    /// while one of them is busy with an interaction on the other, the rise is made larger or smaller by how the first
    /// feels about the second.
    ///
    /// Left as the game has them:
    ///  - losses, and anything that is not a rise;
    ///  - every score but daily (0) and lifetime (2): the game keeps other things in the same list, such as whether two
    ///    Sims are friends, and those are not amounts;
    ///  - changes made while neither Sim is busy with the other: the night's fall in daily scores, a pack's starting
    ///    scores, talking about a third Sim, and a group activity at an object such as a hot tub or a chess table.
    /// </summary>
    public static class Gains
    {
        public const int Daily = 0;
        public const int Lifetime = 2;

        public static Tuning Tuning = new Tuning();

        /// <summary>Every scaled gain is written to the terminal while this is on (the console: chemistry log).</summary>
        public static bool Verbose;

        /// <summary>How many gains chemistry has changed since Simitone started, for the console.</summary>
        public static int Changed;

        /// <summary>One score, found as VMRelationship.Execute finds it, and what it was before the primitive ran.</summary>
        public sealed class Spot
        {
            public VMAvatar From, To;
            public int Index;
            public short Before;
            public bool NeverClamp;

            //the neighbourhood's scores (Sims who are in the neighbourhood), or else the lot's own
            public Dictionary<int, List<short>> NeighbourScores;
            public int NeighbourTarget;
            public Dictionary<ushort, List<short>> LotScores;
            public ushort LotTarget;

            private List<short> List()
            {
                if (NeighbourScores != null) return NeighbourScores.TryGetValue(NeighbourTarget, out var n) ? n : null;
                if (LotScores != null) return LotScores.TryGetValue(LotTarget, out var l) ? l : null;
                return null;
            }

            /// <summary>The score now. A score the game has not made yet is 0, as the primitive makes it.</summary>
            public short Read()
            {
                var list = List();
                return (list != null && list.Count > Index) ? list[Index] : (short)0;
            }

            public void Write(short value)
            {
                var list = List();
                if (list == null) return;
                while (list.Count <= Index) list.Add(0);
                list[Index] = value;
            }
        }

        // ---- the patch ----

        public static void BeforeRelationship(VMStackFrame context, VMPrimitiveOperand args, out Spot __state)
        {
            __state = null;
            try
            {
                __state = Prepare(context, args as VMRelationshipOperand);
            }
            catch (Exception e)
            {
                ChemistryPack.Failed("looking at a relationship change", e);
            }
        }

        public static void AfterRelationship(VMStackFrame context, Spot __state)
        {
            if (__state == null) return;
            try
            {
                Apply(context, __state);
            }
            catch (Exception e)
            {
                ChemistryPack.Failed("scaling a relationship gain", e);
            }
        }

        /// <summary>The score this primitive is about to change, if chemistry may have a say in it.</summary>
        public static Spot Prepare(VMStackFrame context, VMRelationshipOperand op)
        {
            if (context == null || op == null || op.SetMode == 0) return null; //0 only reads the score
            if (op.RelVar != Daily && op.RelVar != Lifetime) return null;
            var vm = context.VM;
            if (vm == null || !vm.TS1) return null;
            var spot = Locate(context, op, vm);
            if (spot == null || spot.From == spot.To) return null;
            if (!BusyWith(spot.From, spot.To) && !BusyWith(spot.To, spot.From)) return null;
            spot.Index = op.RelVar;
            spot.NeverClamp = op.FSONeverClamp;
            spot.Before = spot.Read();
            return spot;
        }

        public static void Apply(VMStackFrame context, Spot spot)
        {
            var after = spot.Read();
            var gain = after - spot.Before;
            if (gain <= 0) return;
            var level = Sims.Between(spot.From, spot.To);
            var multiplier = Tuning.Multiplier(level);
            if (multiplier == 1.0) return;
            var vmContext = context.VM?.Context;
            var scaled = Attraction.Scale(gain, multiplier, max => vmContext != null ? (int)vmContext.NextRandom((ulong)max) : 0);
            var value = spot.Before + scaled;
            if (!spot.NeverClamp) value = Math.Clamp(value, -100, 100);
            value = Math.Clamp(value, short.MinValue, short.MaxValue);
            if (value == after) return;
            spot.Write((short)value);
            Changed++;
            if (Verbose)
                Log.Write(Sims.Name(spot.From) + " to " + Sims.Name(spot.To) + ", " + (spot.Index == Daily ? "daily" : "lifetime") + ": +" + gain + " made +" + (value - spot.Before)
                    + " (" + Attraction.Words(level).ToLowerInvariant() + ", x" + multiplier + ")");
        }

        /// <summary>Whether a Sim is in the middle of an interaction on the other: a social, from either end.</summary>
        public static bool BusyWith(VMAvatar sim, VMAvatar other)
        {
            var thread = sim?.Thread;
            if (thread?.Queue == null) return false;
            var active = thread.ActiveQueueBlock;
            if (active < 0 || active >= thread.Queue.Count) return false;
            return thread.Queue[active]?.Callee == other;
        }

        /// <summary>
        /// The two Sims and the score, found the way VMRelationship.Execute finds them in The Sims 1: the neighbourhood's
        /// scores when the primitive says to use neighbour numbers or both Sims have one, else the lot's own. Null if
        /// either end is not a Sim on the lot.
        /// </summary>
        public static Spot Locate(VMStackFrame context, VMRelationshipOperand op, VM vm)
        {
            var old = op is VMOldRelationshipOperand;
            short Other() => old ? context.Args[0] : context.Locals[op.Local];

            if (op.UseNeighbor)
            {
                int fromId, toId;
                switch (op.Mode)
                {
                    case 0: fromId = NeighbourId(context.Caller); toId = context.StackObjectID; break;
                    case 1: fromId = context.StackObjectID; toId = NeighbourId(context.Caller); break;
                    case 2: fromId = context.StackObjectID; toId = Other(); break;
                    case 3: fromId = Other(); toId = context.StackObjectID; break;
                    default: return null;
                }
                var from = Sims.OnLot(vm, fromId);
                var to = Sims.OnLot(vm, toId);
                if (from == null || to == null) return null;
                var scores = Neighbourhood()?.GetNeighborByID((short)fromId)?.Relationships;
                if (scores == null) return null;
                return new Spot() { From = from, To = to, NeighbourScores = scores, NeighbourTarget = toId };
            }

            VMEntity obj1, obj2;
            switch (op.Mode)
            {
                case 0: obj1 = context.Caller; obj2 = context.StackObject; break;
                case 1: obj1 = context.StackObject; obj2 = context.Caller; break;
                case 2: obj1 = context.StackObject; obj2 = vm.GetObjectById(Other()); break;
                case 3: obj1 = vm.GetObjectById(Other()); obj2 = context.StackObject; break;
                default: return null;
            }
            if (!(obj1 is VMAvatar a) || !(obj2 is VMAvatar b)) return null;
            var aId = a.GetPersonData(VMPersonDataVariable.NeighborId);
            var bId = b.GetPersonData(VMPersonDataVariable.NeighborId);
            if (aId > 0 && bId > 0)
            {
                var scores = Neighbourhood()?.GetNeighborByID(aId)?.Relationships;
                if (scores == null) return null;
                return new Spot() { From = a, To = b, NeighbourScores = scores, NeighbourTarget = bId };
            }
            return new Spot() { From = a, To = b, LotScores = a.MeToObject, LotTarget = (ushort)b.ObjectID };
        }

        /// <summary>The neighbourhood whose scores the primitive changes: the game's, found as the primitive finds it.</summary>
        private static FSO.Content.TS1.TS1NeighborhoodProvider Neighbourhood() => FSO.Content.Content.Get()?.Neighborhood;

        private static int NeighbourId(VMEntity e) => (e as VMAvatar)?.GetPersonData(VMPersonDataVariable.NeighborId) ?? 0;
    }
}
