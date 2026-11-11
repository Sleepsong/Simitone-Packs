/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Runtime.CompilerServices;

namespace FoundationsPack
{
    /// <summary>
    /// Where a lot's foundations are kept. The engine change added fields for them to the engine's terrain, its
    /// architecture and its blueprint. A pack can't add fields, so they are kept beside those objects instead.
    /// </summary>
    public static class Lots
    {
        /// <summary>What the engine change kept on the terrain and the architecture.</summary>
        public class LotFoundations
        {
            public FoundationMap Foundations;
            /// <summary>The copy previews (build tools dragging) change, while the engine is previewing.</summary>
            public FoundationMap VisFoundations;
            private FoundationMap Old;
            public bool InVis;

            public bool Dirty;            //a real change: things on the lot need their heights again
            public bool VisChanged;       //a build tool's preview changed foundations
            public bool ShowingVis;

            public LotFoundations(int width, int height)
            {
                Foundations = new FoundationMap(width, height);
                VisFoundations = new FoundationMap(width, height);
            }

            public void EnterVis()
            {
                Old = Foundations;
                VisFoundations.CopyFrom(Foundations);
                Foundations = VisFoundations;
                InVis = true;
            }

            public void ExitVis()
            {
                if (Old != null) Foundations = Old;
                Old = null;
                InVis = false;
            }
        }

        /// <summary>
        /// Kept by the lot's terrain object: it is what the engine previews with, and it is handed on unchanged when a
        /// lot is handed over in memory.
        /// </summary>
        private static ConditionalWeakTable<VMArchitectureTerrain, LotFoundations> ByTerrain = new ConditionalWeakTable<VMArchitectureTerrain, LotFoundations>();

        public static LotFoundations For(VMArchitectureTerrain terrain)
        {
            return ByTerrain.GetValue(terrain, t => new LotFoundations(t.Width, t.Height));
        }

        /// <summary>A lot's foundations: the real ones or, while the engine previews a build tool, the preview's copy.</summary>
        public static FoundationMap Of(VMArchitecture arch) => (arch?.Terrain == null) ? null : For(arch.Terrain).Foundations;

        /// <summary>
        /// Call after changing a lot's foundations: rooms and routing (the foundations' edges block Sims), the ground's
        /// mesh, floors, walls and everything standing on the lot follow.
        /// </summary>
        public static void SignalChange(VMArchitecture arch)
        {
            var lot = For(arch.Terrain);
            lot.Foundations.Refresh();
            if (!lot.InVis)
            {
                lot.Dirty = true;
                FoundationsCode.FloorsDirtyOf(arch) = true;
            }
            else lot.VisChanged = true;
            arch.SignalRedraw();
        }

        // ---- the world's side: what the engine change kept on the blueprint ----

        public class ShownFoundations
        {
            public FoundationMap Foundations;
            /// <summary>
            /// The ground without foundations raising it, where something open stands over it (a column deck): the lot's
            /// own ground is drawn there from these. Null when there's nothing like that.
            /// </summary>
            public Microsoft.Xna.Framework.Graphics.VertexBuffer Ground;
            /// <summary>The ground tiles under column decks, as indices into <see cref="Ground"/>.</summary>
            public Microsoft.Xna.Framework.Graphics.IndexBuffer UnderDecks;
            public int UnderDeckTiles;
        }
        private static ConditionalWeakTable<Blueprint, ShownFoundations> ByBlueprint = new ConditionalWeakTable<Blueprint, ShownFoundations>();
        public static ShownFoundations Shown(Blueprint bp) => ByBlueprint.GetOrCreateValue(bp);

        /// <summary>The foundations a blueprint is showing (the lot's, or a preview's). Null for none.</summary>
        public static FoundationMap Of(Blueprint bp) => (bp != null && ByBlueprint.TryGetValue(bp, out var shown)) ? shown.Foundations : null;

        private static bool Has(Blueprint bp, out FoundationMap map)
        {
            map = Of(bp);
            return map != null && map.Any;
        }

        /// <summary>A tile's own foundation raise, in height (0 for none).</summary>
        public static float FoundationAt(Blueprint bp, int x, int y) => Has(bp, out var map) ? map.RaiseOf(x, y) * bp.TerrainFactor : 0f;

        /// <summary>The highest foundation, in height, within radius tiles of a point.</summary>
        public static float FoundationNear(Blueprint bp, float x, float y, float radius) => Has(bp, out var map) ? map.RaiseNear(x, y, radius) * bp.TerrainFactor : 0f;

        [ThreadStatic] internal static bool Unraised;

        /// <summary>
        /// The height of the ground itself under a point, without foundations. (The engine's InterpAltitude, which this
        /// pack patches to include them, asked not to.)
        /// </summary>
        public static float TerrainAltitude(Blueprint bp, Vector3 position)
        {
            var was = Unraised;
            Unraised = true;
            try { return bp.InterpAltitude(position); }
            finally { Unraised = was; }
        }
    }
}
