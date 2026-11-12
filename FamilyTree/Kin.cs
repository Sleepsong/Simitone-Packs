/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTree
{
    /// <summary>What is kept between two Sims. Everything else (grandparents, cousins, in-laws) is worked out from these.</summary>
    public enum LinkKind
    {
        /// <summary>A and B are married.</summary>
        Spouse,
        /// <summary>A is B's parent.</summary>
        Parent,
        /// <summary>A and B are siblings. Only needed when their parents are not in the tree: children of the same parent are siblings already.</summary>
        Sibling
    }

    public struct KinLink
    {
        public LinkKind Kind;
        public uint A;
        public uint B;

        public KinLink(LinkKind kind, uint a, uint b) { Kind = kind; A = a; B = b; }

        public bool Joins(uint x, uint y) => (A == x && B == y) || (A == y && B == x);
        public bool Touches(uint x) => A == x || B == x;
    }

    /// <summary>What the player picks for a pair of Sims, read as "the first is the second's ...".</summary>
    public enum Choice
    {
        Housemate,
        Spouse,
        Sibling,
        Parent,
        Child
    }

    /// <summary>What one Sim is to another.</summary>
    public enum Tie
    {
        None,
        Spouse,
        Parent,
        Child,
        Sibling,
        Grandparent,
        Grandchild,
        AuntUncle,
        NieceNephew,
        Cousin,
        StepParent,
        StepChild,
        ParentInLaw,
        ChildInLaw
    }

    /// <summary>
    /// A family tree: who is married to whom, who is whose parent, and who are siblings. It knows nothing about the
    /// game. A Sim is a number (in the game, the GUID of their character; while a family is being made, their place
    /// in it), so the rules here can be checked without a copy of The Sims.
    /// </summary>
    public class Kin
    {
        public const int MostParents = 2;

        public List<KinLink> Links = new List<KinLink>();

        public Kin Clone() => new Kin() { Links = new List<KinLink>(Links) };

        // ---- who is what ----

        public uint? SpouseOf(uint x)
        {
            foreach (var l in Links)
            {
                if (l.Kind == LinkKind.Spouse && l.Touches(x)) return l.A == x ? l.B : l.A;
            }
            return null;
        }

        public List<uint> ParentsOf(uint x) => Links.Where(l => l.Kind == LinkKind.Parent && l.B == x).Select(l => l.A).Distinct().ToList();

        public List<uint> ChildrenOf(uint x) => Links.Where(l => l.Kind == LinkKind.Parent && l.A == x).Select(l => l.B).Distinct().ToList();

        /// <summary>Siblings: anyone joined by sibling links (a sibling's sibling is a sibling), and anyone with a parent in common.</summary>
        public List<uint> SiblingsOf(uint x)
        {
            var result = new HashSet<uint>() { x };
            var todo = new Queue<uint>();
            todo.Enqueue(x);
            while (todo.Count > 0)
            {
                var at = todo.Dequeue();
                foreach (var l in Links)
                {
                    if (l.Kind != LinkKind.Sibling || !l.Touches(at)) continue;
                    var other = l.A == at ? l.B : l.A;
                    if (result.Add(other)) todo.Enqueue(other);
                }
            }
            foreach (var parent in ParentsOf(x))
            {
                foreach (var child in ChildrenOf(parent)) result.Add(child);
            }
            result.Remove(x);
            return result.ToList();
        }

        /// <summary>Parents, their parents, and so on. Safe on a tree that goes round in a circle.</summary>
        public HashSet<uint> AncestorsOf(uint x)
        {
            var result = new HashSet<uint>();
            var todo = new Queue<uint>();
            todo.Enqueue(x);
            while (todo.Count > 0)
            {
                foreach (var parent in ParentsOf(todo.Dequeue()))
                {
                    if (result.Add(parent)) todo.Enqueue(parent);
                }
            }
            return result;
        }

        /// <summary>What <paramref name="other"/> is to <paramref name="x"/>: Between(child, mother) is Parent.</summary>
        public Tie Between(uint x, uint other)
        {
            if (x == other) return Tie.None;
            if (SpouseOf(x) == other) return Tie.Spouse;
            var birth = ByBirth(x, other);
            if (birth != Tie.None) return birth;

            //by marriage
            if (ParentsOf(x).Any(p => SpouseOf(p) == other)) return Tie.StepParent;
            var spouse = SpouseOf(x);
            if (spouse != null)
            {
                if (ChildrenOf(spouse.Value).Contains(other)) return Tie.StepChild;
                if (ParentsOf(spouse.Value).Contains(other)) return Tie.ParentInLaw;
            }
            if (ChildrenOf(x).Any(c => SpouseOf(c) == other)) return Tie.ChildInLaw;
            return Tie.None;
        }

        /// <summary>What the other is to x by birth alone, whoever is married to whom: parent to cousin, or None.</summary>
        public Tie ByBirth(uint x, uint other)
        {
            if (x == other) return Tie.None;
            var parents = ParentsOf(x);
            if (parents.Contains(other)) return Tie.Parent;
            var children = ChildrenOf(x);
            if (children.Contains(other)) return Tie.Child;
            var siblings = SiblingsOf(x);
            if (siblings.Contains(other)) return Tie.Sibling;

            if (parents.Any(p => ParentsOf(p).Contains(other))) return Tie.Grandparent;
            if (children.Any(c => ChildrenOf(c).Contains(other))) return Tie.Grandchild;
            if (parents.Any(p => SiblingsOf(p).Contains(other))) return Tie.AuntUncle;
            if (siblings.Any(s => ChildrenOf(s).Contains(other))) return Tie.NieceNephew;
            if (parents.Any(p => SiblingsOf(p).Any(aunt => ChildrenOf(aunt).Contains(other)))) return Tie.Cousin;
            return Tie.None;
        }

        /// <summary>Family by birth, who cannot marry: parent to cousin, or an ancestor however far back.</summary>
        public bool Related(uint x, uint other)
        {
            return ByBirth(x, other) != Tie.None || AncestorsOf(x).Contains(other) || AncestorsOf(other).Contains(x);
        }

        // ---- what the player chose for a pair ----

        /// <summary>The link kept between these two, as a choice read "x is other's ...". Housemate if there is none.</summary>
        public Choice ChoiceFor(uint x, uint other)
        {
            foreach (var l in Links)
            {
                if (!l.Joins(x, other)) continue;
                switch (l.Kind)
                {
                    case LinkKind.Spouse: return Choice.Spouse;
                    case LinkKind.Sibling: return Choice.Sibling;
                    default: return l.A == x ? Choice.Parent : Choice.Child;
                }
            }
            return Choice.Housemate;
        }

        /// <summary>Make x the other's spouse, sibling, parent or child, in place of whatever was kept between them. Nothing is checked.</summary>
        public void Set(uint x, uint other, Choice choice)
        {
            Links.RemoveAll(l => l.Joins(x, other));
            switch (choice)
            {
                case Choice.Spouse: Links.Add(new KinLink(LinkKind.Spouse, x, other)); break;
                case Choice.Sibling: Links.Add(new KinLink(LinkKind.Sibling, x, other)); break;
                case Choice.Parent: Links.Add(new KinLink(LinkKind.Parent, x, other)); break;
                case Choice.Child: Links.Add(new KinLink(LinkKind.Parent, other, x)); break;
            }
        }

        /// <summary>
        /// Why x cannot be the other's spouse, sibling, parent or child, or null if they can. The whole tree is
        /// checked as it would be afterwards, so a choice that breaks something further away is refused too (two
        /// spouses given the same mother, say).
        /// </summary>
        public string WhyNot(uint x, uint other, Choice choice, Func<uint, bool> isAdult, Func<uint, string> name)
        {
            if (x == other) return "A Sim has no family tie to themselves.";
            var after = Clone();
            after.Set(x, other, choice);
            return after.FirstProblem(isAdult, name)?.Why;
        }

        // ---- the rules ----

        public class Problem
        {
            public KinLink Link;
            public string Why;
        }

        /// <summary>The first thing wrong with this tree, with the link to take out to mend it, or null if nothing is.</summary>
        public Problem FirstProblem(Func<uint, bool> isAdult, Func<uint, string> name)
        {
            isAdult = isAdult ?? (x => true);
            name = name ?? (x => "Sim " + x);
            var spouses = new Dictionary<uint, uint>();
            var parentCount = new Dictionary<uint, int>();
            var seen = new List<KinLink>();

            foreach (var l in Links)
            {
                if (l.A == l.B) return new Problem() { Link = l, Why = name(l.A) + " cannot have a family tie to themselves." };
                if (seen.Any(s => s.Joins(l.A, l.B))) return new Problem() { Link = l, Why = name(l.A) + " and " + name(l.B) + " have more than one tie." };
                seen.Add(l);

                switch (l.Kind)
                {
                    case LinkKind.Spouse:
                        foreach (var who in new[] { l.A, l.B })
                        {
                            if (!isAdult(who)) return new Problem() { Link = l, Why = name(who) + " is a child, and cannot be married." };
                            if (spouses.ContainsKey(who)) return new Problem() { Link = l, Why = name(who) + " is already married to " + name(spouses[who]) + "." };
                        }
                        spouses[l.A] = l.B;
                        spouses[l.B] = l.A;
                        break;
                    case LinkKind.Parent:
                        if (!isAdult(l.A)) return new Problem() { Link = l, Why = name(l.A) + " is a child, and cannot be a parent." };
                        parentCount.TryGetValue(l.B, out var count);
                        if (count >= MostParents) return new Problem() { Link = l, Why = name(l.B) + " already has " + MostParents + " parents." };
                        parentCount[l.B] = count + 1;
                        break;
                }
            }

            foreach (var l in Links)
            {
                switch (l.Kind)
                {
                    case LinkKind.Parent:
                        //nobody is their own ancestor
                        if (AncestorsOf(l.A).Contains(l.B)) return new Problem() { Link = l, Why = name(l.A) + " descends from " + name(l.B) + ", and cannot also be their parent." };
                        //a parent is not also a sibling (through another parent they share)
                        if (Without(l).SiblingsOf(l.A).Contains(l.B)) return new Problem() { Link = l, Why = name(l.A) + " and " + name(l.B) + " are siblings, so one cannot be the other's parent." };
                        break;
                    case LinkKind.Sibling:
                        if (AncestorsOf(l.A).Contains(l.B) || AncestorsOf(l.B).Contains(l.A)) return new Problem() { Link = l, Why = name(l.A) + " and " + name(l.B) + " are parent and child (or further), and cannot be siblings." };
                        break;
                    case LinkKind.Spouse:
                        if (Related(l.A, l.B)) return new Problem() { Link = l, Why = name(l.A) + " and " + name(l.B) + " are family by birth (" + TieNames.Plain(ByBirth(l.A, l.B)) + "), and cannot be married." };
                        break;
                }
            }
            return null;
        }

        private Kin Without(KinLink link)
        {
            var copy = Clone();
            copy.Links.Remove(link);
            return copy;
        }

        /// <summary>Take out whatever breaks the rules, one link at a time, until nothing does. Returns what was said about each.</summary>
        public List<string> Mend(Func<uint, bool> isAdult, Func<uint, string> name)
        {
            var said = new List<string>();
            Problem problem;
            while ((problem = FirstProblem(isAdult, name)) != null)
            {
                said.Add(problem.Why);
                Links.Remove(problem.Link);
            }
            return said;
        }

        // ---- Sims coming and going ----

        /// <summary>Forget every tie this Sim has.</summary>
        public void Forget(uint x)
        {
            Links.RemoveAll(l => l.Touches(x));
        }

        /// <summary>
        /// For a family that is still being made, where a Sim is their place in the list: the Sim at this place was
        /// taken out, so their ties go and everyone after them moves down one.
        /// </summary>
        public void RemoveAt(uint index)
        {
            Forget(index);
            for (int i = 0; i < Links.Count; i++)
            {
                var l = Links[i];
                if (l.A > index) l.A--;
                if (l.B > index) l.B--;
                Links[i] = l;
            }
        }

        // ---- as text, one link to a line ----

        public string Write()
        {
            var lines = new List<string>()
            {
                "# Family ties for this neighbourhood, kept by the Family Tree pack.",
                "# spouse A B: married. parent A B: A is B's parent. sibling A B: siblings.",
                "# A Sim is the GUID of their character, in hexadecimal."
            };
            foreach (var l in Links)
            {
                lines.Add(l.Kind.ToString().ToLowerInvariant() + " " + l.A.ToString("X8") + " " + l.B.ToString("X8"));
            }
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>Read what Write wrote. Lines that make no sense are left out, and counted.</summary>
        public static Kin Read(string text, out int skipped)
        {
            var kin = new Kin();
            skipped = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line == "" || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3
                    && Enum.TryParse<LinkKind>(parts[0], true, out var kind) && Enum.IsDefined(typeof(LinkKind), kind)
                    && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var a)
                    && uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var b)
                    && a != b && !kin.Links.Any(l => l.Joins(a, b)))
                {
                    kin.Links.Add(new KinLink(kind, a, b));
                }
                else skipped++;
            }
            return kin;
        }
    }

    /// <summary>The words for a tie.</summary>
    public static class TieNames
    {
        /// <summary>The tie with no Sim in mind: "aunt or uncle".</summary>
        public static string Plain(Tie tie)
        {
            switch (tie)
            {
                case Tie.Spouse: return "spouse";
                case Tie.Parent: return "parent";
                case Tie.Child: return "child";
                case Tie.Sibling: return "sibling";
                case Tie.Grandparent: return "grandparent";
                case Tie.Grandchild: return "grandchild";
                case Tie.AuntUncle: return "aunt or uncle";
                case Tie.NieceNephew: return "niece or nephew";
                case Tie.Cousin: return "cousin";
                case Tie.StepParent: return "step-parent";
                case Tie.StepChild: return "step-child";
                case Tie.ParentInLaw: return "parent-in-law";
                case Tie.ChildInLaw: return "child-in-law";
                default: return "no family tie";
            }
        }

        /// <summary>The tie for a Sim who is female or male: "Aunt", "Uncle". Null if there is no tie.</summary>
        public static string For(Tie tie, bool female)
        {
            switch (tie)
            {
                case Tie.Spouse: return female ? "Wife" : "Husband";
                case Tie.Parent: return female ? "Mother" : "Father";
                case Tie.Child: return female ? "Daughter" : "Son";
                case Tie.Sibling: return female ? "Sister" : "Brother";
                case Tie.Grandparent: return female ? "Grandmother" : "Grandfather";
                case Tie.Grandchild: return female ? "Granddaughter" : "Grandson";
                case Tie.AuntUncle: return female ? "Aunt" : "Uncle";
                case Tie.NieceNephew: return female ? "Niece" : "Nephew";
                case Tie.Cousin: return "Cousin";
                case Tie.StepParent: return female ? "Stepmother" : "Stepfather";
                case Tie.StepChild: return female ? "Stepdaughter" : "Stepson";
                case Tie.ParentInLaw: return female ? "Mother-in-law" : "Father-in-law";
                case Tie.ChildInLaw: return female ? "Daughter-in-law" : "Son-in-law";
                default: return null;
            }
        }
    }
}
