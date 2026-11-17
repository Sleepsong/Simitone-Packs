/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Memories
{
    /// <summary>
    /// Which life events are remembered, and by whom. Knows nothing of a running game: what it needs to know about a
    /// Sim it is given.
    /// </summary>
    public static class Remember
    {
        /// <summary>A relative of a Sim, and what they are to that Sim ("father"), or null where only the household says they are family.</summary>
        public struct Relative
        {
            public uint Sim;
            public string Word;
            public Relative(uint sim, string word) { Sim = sim; Word = word; }
        }

        /// <summary>
        /// The memories a life event leaves, not yet kept. stageOf gives a Sim's stage now; relativesOf the Sims who
        /// count a Sim as family, each with what that Sim is to them. A first kiss or a death already remembered is
        /// not remembered again.
        /// </summary>
        public static List<Memory> From(LifeEvent e, Journal journal, Func<uint, Stage> stageOf, Func<uint, IEnumerable<Relative>> relativesOf)
        {
            var made = new List<Memory>();
            Memory For(uint sim, MemoryKind kind)
            {
                var m = new Memory() { Sim = sim, Kind = kind, When = e.When, Stage = stageOf(sim), Home = e.House != 0 };
                made.Add(m);
                return m;
            }
            bool Had(uint sim, MemoryKind kind) => journal.Has(sim, kind) || made.Any(m => m.Sim == sim && m.Kind == kind);

            switch (e.Kind)
            {
                case LifeEventKind.Kiss:
                    foreach (var (sim, other) in new[] { (e.Sim, e.Other), (e.Other, e.Sim) })
                    {
                        if (sim != 0 && !Had(sim, MemoryKind.FirstKiss)) For(sim, MemoryKind.FirstKiss).Other = other;
                    }
                    break;

                case LifeEventKind.Married:
                    if (e.Sim != 0) For(e.Sim, MemoryKind.Married).Other = e.Other;
                    if (e.Other != 0) For(e.Other, MemoryKind.Married).Other = e.Sim;
                    break;

                case LifeEventKind.GotJob:
                case LifeEventKind.Promoted:
                case LifeEventKind.Demoted:
                case LifeEventKind.LostJob:
                    if (e.Sim == 0) break;
                    var kind = e.Kind == LifeEventKind.GotJob ? MemoryKind.GotJob
                        : e.Kind == LifeEventKind.Promoted ? MemoryKind.Promoted
                        : e.Kind == LifeEventKind.Demoted ? MemoryKind.Demoted : MemoryKind.LostJob;
                    var job = For(e.Sim, kind);
                    job.Detail = e.Detail;
                    job.Top = e.Kind == LifeEventKind.Promoted && e.Value3 == 1;
                    break;

                case LifeEventKind.BabyArrived:
                    var parents = new[] { e.Sim, e.Other }.Where(x => x != 0).Distinct().ToList();
                    foreach (var parent in parents) For(parent, MemoryKind.HadBaby).Other = parents.FirstOrDefault(x => x != parent);
                    break;

                case LifeEventKind.ChildFromBaby:
                    if (e.Sim == 0 || Had(e.Sim, MemoryKind.Born)) break;
                    var born = For(e.Sim, MemoryKind.Born);
                    born.Others = e.Others ?? Array.Empty<uint>();
                    born.When = e.Began ?? e.When;
                    born.Stage = Stage.Child;
                    break;

                case LifeEventKind.GrewUp:
                    //a stage is reached once, however often the game's number for it is put back and set again
                    if (e.Sim == 0 || journal.Memories.Concat(made).Any(m => m.Sim == e.Sim && m.Kind == MemoryKind.GrewUp && m.Stage == (Stage)e.Value)) break;
                    var grew = For(e.Sim, MemoryKind.GrewUp);
                    grew.Stage = (Stage)e.Value;
                    grew.Detail = Recognise.StageName((Stage)e.Value);
                    break;

                case LifeEventKind.Died:
                    if (e.Sim == 0 || Had(e.Sim, MemoryKind.Died)) break;
                    For(e.Sim, MemoryKind.Died);
                    var told = new HashSet<uint>() { e.Sim };
                    foreach (var relative in relativesOf(e.Sim) ?? Enumerable.Empty<Relative>())
                    {
                        if (relative.Sim == 0 || !told.Add(relative.Sim)) continue;
                        var loss = For(relative.Sim, MemoryKind.RelativeDied);
                        loss.Other = e.Sim;
                        loss.Detail = relative.Word;
                    }
                    break;

                case LifeEventKind.FireStarted:
                    var there = (e.Others ?? Array.Empty<uint>()).ToList();
                    if (e.Sim != 0 && !there.Contains(e.Sim)) there.Add(e.Sim);
                    foreach (var sim in there.Where(x => x != 0).Distinct())
                    {
                        var fire = For(sim, MemoryKind.Fire);
                        fire.Top = sim == e.Sim;
                        fire.Home = e.Value3 == 1;
                        if (fire.Top) fire.Detail = string.IsNullOrWhiteSpace(e.Detail) ? null : e.Detail;
                    }
                    break;

                case LifeEventKind.BurglarGone:
                    if (e.Value3 != 1) break; //only a burglary at home is remembered
                    foreach (var sim in (e.Others ?? Array.Empty<uint>()).Where(x => x != 0).Distinct())
                    {
                        var burgled = For(sim, MemoryKind.Burgled);
                        burgled.Detail = string.IsNullOrWhiteSpace(e.Detail) ? null : e.Detail;
                        burgled.Top = e.Value == 1;
                        burgled.When = e.Began ?? e.When;
                        burgled.Home = true;
                    }
                    break;
            }
            return made;
        }

        /// <summary>
        /// Who counts the dead Sim as family: their relatives in the family tree, with what the dead Sim was to each,
        /// and then the household they were part of. ties gives a Sim's relatives, each with the word for what that
        /// relative is to them.
        /// </summary>
        public static List<Relative> Mourners(uint dead, IEnumerable<uint> household, Func<uint, IEnumerable<Relative>> tiesOf)
        {
            var result = new List<Relative>();
            foreach (var r in tiesOf?.Invoke(dead) ?? Enumerable.Empty<Relative>())
            {
                if (r.Sim == 0 || r.Sim == dead || result.Any(x => x.Sim == r.Sim)) continue;
                //what the dead Sim was to them: the other side of the tie
                var back = (tiesOf(r.Sim) ?? Enumerable.Empty<Relative>()).Where(x => x.Sim == dead).Select(x => x.Word).FirstOrDefault();
                result.Add(new Relative(r.Sim, back));
            }
            foreach (var sim in household ?? Enumerable.Empty<uint>())
            {
                if (sim == 0 || sim == dead || result.Any(x => x.Sim == sim)) continue;
                result.Add(new Relative(sim, null));
            }
            return result;
        }
    }

    /// <summary>
    /// The family tree the Family Tree pack keeps, read from its file if it has one for the neighbourhood (this pack
    /// does not need that one). Only read, never written. "spouse A B", "parent A B" (A is B's parent) and "sibling A
    /// B", with Sims in hexadecimal; anything else in it is passed over.
    /// </summary>
    public sealed class FamilyTies
    {
        public const string FileName = "FamilyTree.txt";

        private readonly List<(string Kind, uint A, uint B)> Links = new List<(string, uint, uint)>();

        public static FamilyTies Parse(string text)
        {
            var ties = new FamilyTies();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3) continue;
                var kind = parts[0].ToLowerInvariant();
                if (kind != "spouse" && kind != "parent" && kind != "sibling") continue;
                if (!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)) continue;
                if (!uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) continue;
                if (a == 0 || b == 0 || a == b) continue;
                ties.Links.Add((kind, a, b));
            }
            return ties;
        }

        /// <summary>The tree of the neighbourhood whose folder this is, or an empty one.</summary>
        public static FamilyTies ReadFrom(string neighbourhoodFolder)
        {
            try
            {
                var path = string.IsNullOrEmpty(neighbourhoodFolder) ? null : Path.Combine(neighbourhoodFolder, FileName);
                if (path != null && File.Exists(path)) return Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Log.Write("Could not read the family tree: " + e.Message);
            }
            return new FamilyTies();
        }

        public int Count => Links.Count;

        public uint? SpouseOf(uint sim)
        {
            foreach (var l in Links)
            {
                if (l.Kind != "spouse") continue;
                if (l.A == sim) return l.B;
                if (l.B == sim) return l.A;
            }
            return null;
        }

        public List<uint> ParentsOf(uint sim) => Links.Where(l => l.Kind == "parent" && l.B == sim).Select(l => l.A).Distinct().ToList();
        public List<uint> ChildrenOf(uint sim) => Links.Where(l => l.Kind == "parent" && l.A == sim).Select(l => l.B).Distinct().ToList();

        public List<uint> SiblingsOf(uint sim)
        {
            var result = Links.Where(l => l.Kind == "sibling" && (l.A == sim || l.B == sim)).Select(l => l.A == sim ? l.B : l.A).ToList();
            foreach (var parent in ParentsOf(sim)) result.AddRange(ChildrenOf(parent));
            return result.Where(x => x != sim).Distinct().ToList();
        }

        /// <summary>A Sim's close family, each with the word for what they are to the Sim: spouse, parents, children, brothers and sisters, grandparents, grandchildren.</summary>
        public List<Remember.Relative> RelativesOf(uint sim, Func<uint, bool> isFemale)
        {
            var result = new List<Remember.Relative>();
            void Add(uint other, string female, string male)
            {
                if (other == sim || result.Any(r => r.Sim == other)) return;
                result.Add(new Remember.Relative(other, isFemale(other) ? female : male));
            }
            var spouse = SpouseOf(sim);
            if (spouse != null) Add(spouse.Value, "wife", "husband");
            foreach (var p in ParentsOf(sim)) Add(p, "mother", "father");
            foreach (var c in ChildrenOf(sim)) Add(c, "daughter", "son");
            foreach (var s in SiblingsOf(sim)) Add(s, "sister", "brother");
            foreach (var p in ParentsOf(sim)) foreach (var g in ParentsOf(p)) Add(g, "grandmother", "grandfather");
            foreach (var c in ChildrenOf(sim)) foreach (var g in ChildrenOf(c)) Add(g, "granddaughter", "grandson");
            return result;
        }
    }

    /// <summary>
    /// The memories of the neighbourhood being played: the journal, kept up to date from life events. Other packs may
    /// read it, and hear each new memory (Remembered).
    /// </summary>
    public static class MemoryBook
    {
        public static string PackId = "sample.memories";

        /// <summary>Where the loaded neighbourhood is kept. The tests put another in its place.</summary>
        public static Func<string> NeighbourhoodPath = () => Content.Get()?.Neighborhood?.UserPath;

        private static Journal Current;
        private static string CurrentPath;

        /// <summary>The loaded neighbourhood's journal, read from its file the first time it is wanted. Null with no neighbourhood loaded.</summary>
        public static Journal Journal
        {
            get
            {
                var path = NeighbourhoodPath();
                if (path == null) return null;
                if (Current == null || CurrentPath != path)
                {
                    Current = Journal.Load(PackId, path);
                    CurrentPath = path;
                }
                return Current;
            }
        }

        /// <summary>Each memory as it is made.</summary>
        public static event Action<Memory> Remembered;

        /// <summary>A Sim's memories, in the order they happened. Empty with no neighbourhood loaded.</summary>
        public static IReadOnlyList<Memory> Of(uint sim) => Journal?.Of(sim) ?? new List<Memory>();

        public static bool Has(uint sim, MemoryKind kind) => Journal?.Has(sim, kind) ?? false;

        /// <summary>
        /// Drop what is in memory, so that it is read from the file again: when the game loads a neighbourhood, and
        /// when a lot is left (if the game was saved first, the file is the same; if not, what happened goes with the lot).
        /// </summary>
        public static void Forget()
        {
            Current = null;
            CurrentPath = null;
            FamilyTreeCache = null;
        }

        /// <summary>The game has saved: keep the journal with it.</summary>
        public static void Saved()
        {
            if (Current != null && CurrentPath != null && Current.Save(PackId, CurrentPath))
                Log.Write("Kept " + Current.Memories.Count + " memories (day " + Current.Day + ").");
        }

        // ---- what the rules need to know about Sims, from the game ----

        public static Func<uint, Stage> StageOf = sim => People.StageOf(sim);
        public static Func<uint, bool> IsFemale = sim => People.IsFemale(sim);

        private static FamilyTies FamilyTreeCache;
        private static DateTime FamilyTreeRead;

        /// <summary>The Family Tree pack's tree for this neighbourhood, if there is one, read again if it is more than a minute old.</summary>
        public static FamilyTies FamilyTree
        {
            get
            {
                if (FamilyTreeCache == null || (DateTime.UtcNow - FamilyTreeRead).TotalMinutes > 1)
                {
                    FamilyTreeCache = FamilyTies.ReadFrom(NeighbourhoodPath());
                    FamilyTreeRead = DateTime.UtcNow;
                }
                return FamilyTreeCache;
            }
        }

        /// <summary>A life event has happened: remember what it leaves.</summary>
        public static void Hear(LifeEvent e)
        {
            var journal = Journal;
            if (journal == null) return;
            var tree = FamilyTree;
            IEnumerable<Remember.Relative> TiesOf(uint sim) => tree.RelativesOf(sim, IsFemale);
            var made = Remember.From(e, journal, StageOf, dead => Remember.Mourners(dead, e.Others, TiesOf));
            foreach (var memory in made)
            {
                journal.Add(memory);
                Log.Write("remembered: " + People.FullName(memory.Sim) + ": " + memory.Describe(People.FullName) + " (" + memory.When + ")");
                var listeners = Remembered;
                if (listeners == null) continue;
                foreach (Action<Memory> listener in listeners.GetInvocationList())
                {
                    try { listener(memory); }
                    catch (Exception ex) { Log.Write("A listener to memories failed: " + ex.Message); }
                }
            }
        }
    }
}
