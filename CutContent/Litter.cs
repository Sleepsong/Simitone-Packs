/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using System.Runtime.CompilerServices;

namespace CutContent
{
    /// <summary>
    /// Selling a litter of kittens or puppies (Unleashed).
    ///
    /// The survey found two versions. The Pet Trainer's "Sell Litter" is in his menu, but its check wants the Pet
    /// Maternity Box (a77f36ae) in the Sim's inventory on a community lot, and nothing ever puts it there. The Maternity
    /// Box has its own finished "Sell Litter" (script 4138) with no menu entry: it picks one of three offers from the box's
    /// texts ("This litter has potential. I'll give you 50 for them." and the like), asks yes or no, and pays $25, $50
    /// or $100. In The Sims' money step the flag the engine calls "subtract" means add, so it pays the household.
    ///
    /// This puts the box's version in the box's menu, with two things Maxis's script lacks:
    ///   - a check that the box holds a litter: the box keeps the parent pet in its attribute 38 ("care for litter" reads
    ///     it), which a box bought empty doesn't have;
    ///   - after a sale, the litter goes, as when Animal Control takes it ("Take Litter" removes the box). Without
    ///     this a litter could be sold again and again.
    /// </summary>
    public static class Litter
    {
        private static ConditionalWeakTable<BHAV, BHAV> Checks = new ConditionalWeakTable<BHAV, BHAV>();

        public static BHAV MakeCheck(GameIffResource res, BHAV action)
        {
            if (Checks.TryGetValue(action, out var check)) return check;
            check = Scripts.Add(res, "Sell Litter - TEST (Cut Content)", new[]
            {
                //the box's attribute 38 (the parent) is more than 0
                Scripts.Step("expression", Scripts.Expression(38, 1, 0, 0, 7), Scripts.ReturnTrue, Scripts.ReturnFalse),
            }, action);
            Checks.AddOrUpdate(action, check);
            CutContentPack.Log($"sell litter: added check script {check.ChunkID} to {res.MainIff?.Filename}");
            return check;
        }

        /// <summary>After the sale's sound (the last step, which returns true), remove the box.</summary>
        public static bool FixAction(GameIffResource res, BHAV action)
        {
            var steps = action.Instructions;
            //as the survey found it: 11 steps, the last a sound that ends the script
            if (steps.Length != 11 || steps[10].TruePointer != Scripts.ReturnTrue || steps[10].Opcode != Scripts.Opcode("play_sound")) return false;

            var added = new BHAVInstruction[12];
            System.Array.Copy(steps, added, 11);
            added[10] = new BHAVInstruction { Opcode = steps[10].Opcode, Operand = steps[10].Operand, TruePointer = 11, FalsePointer = steps[10].FalsePointer };
            //remove the stack object, which is the box. as Animal Control's "Take Litter" does
            added[11] = Scripts.Step("remove_object_instance", new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, Scripts.ReturnTrue, Scripts.ReturnTrue);
            action.Instructions = added;
            Scripts.Reassemble(res, action);
            CutContentPack.Log($"sell litter: script {action.ChunkID} in {res.MainIff?.Filename} now removes the litter once it is sold");
            return true;
        }
    }
}
