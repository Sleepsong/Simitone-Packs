/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using System;
using System.Linq;

namespace CutContent
{
    /// <summary>
    /// Small changes to an object's scripts, in memory. The engine turns every script in an object's file into a
    /// routine when the file loads, so a script that is changed or added here is turned into a routine again with the
    /// engine's own assembler.
    /// </summary>
    public static class Scripts
    {
        public const byte ReturnTrue = 254;
        public const byte ReturnFalse = 255;

        /// <summary>The number of a primitive, by the name the engine gives it ("expression", "remove_object_instance").</summary>
        public static ushort Opcode(string name)
        {
            for (int i = 0; i < VMContext.Primitives.Length; i++)
            {
                if (VMContext.Primitives[i]?.Name == name) return (ushort)i;
            }
            throw new Exception($"the engine has no \"{name}\" primitive");
        }

        public static BHAVInstruction Step(string primitive, byte[] operand, byte onTrue, byte onFalse)
        {
            var bytes = new byte[8];
            Array.Copy(operand, bytes, Math.Min(8, operand.Length));
            return new BHAVInstruction { Opcode = Opcode(primitive), Operand = bytes, TruePointer = onTrue, FalsePointer = onFalse };
        }

        /// <summary>Adds a new script to an object's file and makes it runnable. Returns it, with its number.</summary>
        public static BHAV Add(GameIffResource res, string label, BHAVInstruction[] steps, BHAV like)
        {
            var iff = res.MainIff;
            var used = iff.List<BHAV>()?.Select(x => (int)x.ChunkID).ToList() ?? new System.Collections.Generic.List<int>();
            var id = (ushort)Math.Max(4096, used.Where(x => x < 8192).DefaultIfEmpty(4095).Max() + 1);
            if (id >= 8192) throw new Exception("no free script number in " + iff.Filename);
            var bhav = new BHAV
            {
                ChunkID = id,
                ChunkLabel = label,
                ChunkType = "BHAV",
                ChunkProcessed = true,
                AddedByPatch = true,
                Instructions = steps,
                Type = like?.Type ?? 0,
                Args = 0,
                Locals = 0,
                Version = like?.Version ?? 0,
            };
            iff.AddChunk(bhav);
            Reassemble(res, bhav);
            return bhav;
        }

        /// <summary>Turns a script into a routine again, after a change.</summary>
        public static void Reassemble(GameIffResource res, BHAV bhav)
        {
            if (res.RoutineCache == null) return; //made when the file is first used, from the scripts as they are then
            res.RoutineCache[bhav.ChunkID] = GameIffResource.BHAVAssembler(bhav, res);
        }

        /// <summary>
        /// The settings of an "expression" step: [left] [operator] [right]. Owners and operators are the game's numbers:
        /// owners 1 stack object's attribute, 7 literal; operators 0 greater than, 2 equals.
        /// </summary>
        public static byte[] Expression(short left, byte leftOwner, byte op, short right, byte rightOwner)
        {
            return new byte[] { (byte)left, (byte)(left >> 8), (byte)right, (byte)(right >> 8), 0, op, leftOwner, rightOwner };
        }
    }
}
