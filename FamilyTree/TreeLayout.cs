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
    /// <summary>A Sim's picture in the tree. X and Y are the middle of the picture.</summary>
    public class TreeNode
    {
        public uint Sim;
        public float X;
        public float Y;
        /// <summary>The Sim the tree is drawn around.</summary>
        public bool Centre;
        /// <summary>This Sim has family that is not in the picture. Shown as "..." under them, as The Sims 2 does: choose them to see it.</summary>
        public bool More;
    }

    public struct TreeLine
    {
        public float X1, Y1, X2, Y2;
        public TreeLine(float x1, float y1, float x2, float y2) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
    }

    /// <summary>
    /// Where everything goes in the picture of a family tree, laid out as The Sims 2 does: a generation to a row,
    /// oldest at the top, with lines down from each couple to their children.
    ///
    /// The tree is drawn around one Sim. Above them are their parents, and each parent's parents. Beside them are
    /// their brothers and sisters (to the left) and their husband or wife (to the right). Below are their children
    /// and their children's children. Anyone further away (an aunt, a cousin, a brother's wife) is reached by
    /// choosing a Sim who is in the picture, which draws the tree around them.
    ///
    /// This knows nothing about the game or the screen: it is only numbers, so it can be checked without either.
    /// </summary>
    public class TreeLayout
    {
        /// <summary>From one picture to the next in a row, and from one row to the next.</summary>
        public const float Cell = 110;
        public const float RowHeight = 150;
        /// <summary>A picture's size, and the room under it for the Sim's name.</summary>
        public const float Icon = 80;
        public const float NameRoom = 30;
        private const float Edge = 55;
        /// <summary>Room above the top row and below the bottom one, for the lines that join a couple or siblings there.</summary>
        private const float Pad = 10;

        public List<TreeNode> Nodes = new List<TreeNode>();
        public List<TreeLine> Lines = new List<TreeLine>();
        public float Width;
        public float Height;

        private readonly Dictionary<uint, TreeNode> Placed = new Dictionary<uint, TreeNode>();

        private TreeNode Place(uint sim, float x, int row)
        {
            if (Placed.TryGetValue(sim, out var there)) return there;
            var node = new TreeNode() { Sim = sim, X = x, Y = row };
            Placed[sim] = node;
            Nodes.Add(node);
            return node;
        }

        /// <summary>
        /// The lines of one family: down from each parent to a bar, along the bar, and down from the bar to each
        /// child. With no children it joins a couple. With no parents it joins brothers and sisters from above.
        /// </summary>
        private void Join(List<TreeNode> parents, List<TreeNode> children)
        {
            parents = parents.Distinct().ToList();
            children = children.Distinct().Except(parents).ToList();
            if (parents.Count + children.Count < 2) return;
            var gap = RowHeight - Icon - NameRoom;
            float bar = parents.Count > 0
                ? parents[0].Y + Icon / 2 + NameRoom + gap / 2
                : children[0].Y - Icon / 2 - gap / 2;
            foreach (var parent in parents) Lines.Add(new TreeLine(parent.X, parent.Y + Icon / 2 + NameRoom, parent.X, bar));
            foreach (var child in children) Lines.Add(new TreeLine(child.X, bar, child.X, child.Y - Icon / 2));
            var xs = parents.Concat(children).Select(n => n.X).ToList();
            if (xs.Max() > xs.Min()) Lines.Add(new TreeLine(xs.Min(), bar, xs.Max(), bar));
        }

        /// <summary>Lay out the tree around a Sim. Only Sims that <paramref name="shown"/> allows are drawn (all, if it is not given).</summary>
        public static TreeLayout For(Kin kin, uint centre, Func<uint, bool> shown = null)
        {
            shown = shown ?? (x => true);
            var t = new TreeLayout();
            List<uint> Only(IEnumerable<uint> sims) => sims.Where(s => s != centre && shown(s)).Distinct().ToList();

            //rows, counted from the Sim's own (0): -2 grandparents, -1 parents, 1 children, 2 grandchildren
            var siblings = Only(kin.SiblingsOf(centre));
            var selfX = siblings.Count * Cell;
            for (int i = 0; i < siblings.Count; i++) t.Place(siblings[i], i * Cell, 0);
            var self = t.Place(centre, selfX, 0);
            self.Centre = true;
            var spouseOf = kin.SpouseOf(centre);
            TreeNode spouse = (spouseOf != null && shown(spouseOf.Value)) ? t.Place(spouseOf.Value, selfX + Cell, 0) : null;

            //parents over the middle of their children, far enough apart for two grandparents over each
            var parents = Only(kin.ParentsOf(centre)).Take(Kin.MostParents).ToList();
            var grandparents = parents.Select(p => Only(kin.ParentsOf(p)).Where(g => !parents.Contains(g)).Take(Kin.MostParents).ToList()).ToList();
            var middle = selfX / 2;
            var apart = grandparents.Any(g => g.Count > 0) ? Cell : Cell / 2;
            var parentNodes = new List<TreeNode>();
            for (int i = 0; i < parents.Count; i++)
            {
                var x = parents.Count == 1 ? middle : middle + (i == 0 ? -apart : apart);
                parentNodes.Add(t.Place(parents[i], x, -1));
            }
            for (int i = 0; i < parents.Count; i++)
            {
                var over = parentNodes[i];
                var theirs = new List<TreeNode>();
                for (int j = 0; j < grandparents[i].Count; j++)
                {
                    var x = grandparents[i].Count == 1 ? over.X : over.X + (j == 0 ? -Cell / 2 : Cell / 2);
                    theirs.Add(t.Place(grandparents[i][j], x, -2));
                }
            }

            //children under the couple, and each child's children under them
            var children = Only(kin.ChildrenOf(centre)).Where(c => !t.Placed.ContainsKey(c)).ToList();
            var under = spouse != null ? selfX + Cell / 2 : selfX;
            var childNodes = new List<TreeNode>();
            for (int i = 0; i < children.Count; i++) childNodes.Add(t.Place(children[i], under + (i - (children.Count - 1) / 2f) * Cell, 1));

            float next = float.MinValue;
            foreach (var child in childNodes)
            {
                var theirs = Only(kin.ChildrenOf(child.Sim)).Where(g => !t.Placed.ContainsKey(g)).ToList();
                if (theirs.Count == 0) continue;
                var start = Math.Max(child.X - (theirs.Count - 1) / 2f * Cell, next);
                for (int i = 0; i < theirs.Count; i++) t.Place(theirs[i], start + i * Cell, 2);
                next = start + theirs.Count * Cell;
            }

            //"...": family that is not in the picture
            foreach (var node in t.Nodes)
            {
                if (node.Centre) continue;
                var close = kin.ParentsOf(node.Sim).Concat(kin.ChildrenOf(node.Sim)).Concat(kin.SiblingsOf(node.Sim));
                var married = kin.SpouseOf(node.Sim);
                if (married != null) close = close.Concat(new[] { married.Value });
                node.More = close.Any(s => shown(s) && !t.Placed.ContainsKey(s));
            }

            //rows that are empty take no room, and the picture starts at its top left corner
            var rows = t.Nodes.Select(n => (int)n.Y).Distinct().OrderBy(r => r).ToList();
            float RowY(float row) => rows.IndexOf((int)Math.Floor(row)) * RowHeight + Icon / 2 + (RowHeight - Icon - NameRoom) / 2 + Pad;
            foreach (var node in t.Nodes) node.Y = RowY(node.Y);
            t.Joins(kin, centre);
            var left = t.Nodes.Min(n => n.X) - Edge;
            foreach (var node in t.Nodes) node.X -= left;
            for (int i = 0; i < t.Lines.Count; i++)
            {
                var l = t.Lines[i];
                t.Lines[i] = new TreeLine(l.X1 - left, l.Y1, l.X2 - left, l.Y2);
            }
            t.Width = t.Nodes.Max(n => n.X) + Edge;
            t.Height = rows.Count * RowHeight + Pad * 2;
            return t;
        }

        /// <summary>Draw every family's lines, now that everyone is in their place.</summary>
        private void Joins(Kin kin, uint centre)
        {
            List<TreeNode> In(IEnumerable<uint> sims) => sims.Where(Placed.ContainsKey).Select(s => Placed[s]).ToList();
            var self = Placed[centre];
            var parents = In(kin.ParentsOf(centre)).Where(n => n.Y < self.Y).ToList();
            foreach (var parent in parents)
            {
                Join(In(kin.ParentsOf(parent.Sim)).Where(n => n.Y < parent.Y).ToList(), new List<TreeNode>() { parent });
            }
            var generation = In(kin.SiblingsOf(centre)).Where(n => n.Y == self.Y).Concat(new[] { self }).ToList();
            Join(parents, generation);

            var couple = new List<TreeNode>() { self };
            var spouse = kin.SpouseOf(centre);
            if (spouse != null && Placed.ContainsKey(spouse.Value)) couple.Add(Placed[spouse.Value]);
            var children = In(kin.ChildrenOf(centre)).Where(n => n.Y > self.Y).ToList();
            Join(couple, children);
            foreach (var child in children)
            {
                Join(new List<TreeNode>() { child }, In(kin.ChildrenOf(child.Sim)).Where(n => n.Y > child.Y).ToList());
            }
        }
    }
}
