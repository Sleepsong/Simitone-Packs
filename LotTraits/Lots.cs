/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace LotTraits
{
    /// <summary>The traits one lot has.</summary>
    public sealed class LotTraitSet
    {
        /// <summary>
        /// The ids of the lot's traits, in the order they were chosen. An id traits.json no longer has is kept, so that
        /// it is back if the trait is: it does nothing meanwhile, and does not count against the most a lot can have.
        /// </summary>
        public List<string> Ids = new List<string>();

        public const string Header = "lottraits 1";
        public const int MostKept = 16;

        public List<Trait> Known(TraitBook book) => Ids.Select(book.Get).Where(x => x != null).ToList();
        public List<string> Unknown(TraitBook book) => Ids.Where(x => book.Get(x) == null).ToList();

        public NeedEffect Effect(TraitBook book) => NeedEffect.Combine(Known(book).Select(x => x.Effect));

        /// <summary>The lot's data file: a line to say what it is, then a trait's id on each line. Null if there are no traits, so there is no file.</summary>
        public byte[] Save()
        {
            if (Ids.Count == 0) return null;
            return Encoding.UTF8.GetBytes(Header + "\n" + string.Join("\n", Ids) + "\n");
        }

        /// <summary>The traits in a lot's data file. A file that is not one gives none; lines that are not ids are passed over.</summary>
        public static LotTraitSet Load(byte[] data)
        {
            var set = new LotTraitSet();
            if (data == null) return set;
            string text;
            try { text = Encoding.UTF8.GetString(data); }
            catch { return set; }
            var lines = text.Replace("\r", "").Split('\n').Select(x => x.Trim()).ToList();
            if (lines.Count == 0 || lines[0] != Header) return set;
            foreach (var line in lines.Skip(1))
            {
                if (!TraitBook.IsId(line) || set.Ids.Contains(line)) continue;
                if (set.Ids.Count >= MostKept) break;
                set.Ids.Add(line);
            }
            return set;
        }

        /// <summary>Give the lot a trait. Null if it did, or else why not.</summary>
        public string Add(Trait trait, TraitBook book)
        {
            if (trait == null) return "There is no such trait.";
            if (Ids.Contains(trait.Id)) return "This lot is already " + trait.Name + ".";
            if (Known(book).Count >= book.Most) return "A lot can have " + book.Most + " traits at most. Take one off first.";
            if (Ids.Count >= MostKept) return "This lot cannot keep any more traits.";
            Ids.Add(trait.Id);
            return null;
        }

        public bool Remove(string id) => Ids.Remove(id);
    }

    /// <summary>
    /// The traits of each lot that is loaded, and the effect they have on needs, kept ready for the patch: it asks
    /// once for every Sim on every tick.
    /// </summary>
    public static class Lots
    {
        public static TraitBook Book = TraitBook.Empty;

        private static readonly ConditionalWeakTable<VM, LotTraitSet> Sets = new ConditionalWeakTable<VM, LotTraitSet>();
        private static readonly ConditionalWeakTable<VM, NeedEffect> Effects = new ConditionalWeakTable<VM, NeedEffect>();

        /// <summary>The traits of a loaded lot. A lot the pack has not been told of has none.</summary>
        public static LotTraitSet Of(VM vm)
        {
            if (vm == null) return new LotTraitSet();
            return Sets.GetValue(vm, x => new LotTraitSet());
        }

        /// <summary>The lot has been loaded, with these traits.</summary>
        public static void Loaded(VM vm, LotTraitSet set)
        {
            if (vm == null) return;
            Sets.AddOrUpdate(vm, set ?? new LotTraitSet());
            Refresh(vm);
        }

        /// <summary>The lot's traits were changed: work out their effect again.</summary>
        public static void Refresh(VM vm)
        {
            if (vm == null) return;
            Effects.AddOrUpdate(vm, Of(vm).Effect(Book));
        }

        /// <summary>Whether the pack has been told of this lot: a lot it has not is left as it is when saved.</summary>
        public static bool Knows(VM vm) => vm != null && Sets.TryGetValue(vm, out _);

        /// <summary>The effect of a lot's traits on the needs of the Sims on it.</summary>
        public static NeedEffect EffectOn(VM vm)
        {
            if (vm == null) return NeedEffect.None;
            return Effects.TryGetValue(vm, out var effect) ? effect : NeedEffect.None;
        }
    }
}
