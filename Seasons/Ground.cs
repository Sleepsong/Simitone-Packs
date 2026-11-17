/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content.Model;

namespace Seasons
{
    /// <summary>
    /// The ground a lot shows: its own ground, in the season. The terrain types are the game's own, the ones Terrain
    /// Paint paints with: autumn grass in autumn, snow in winter.
    ///
    /// A lot's ground has two terrain types, light and dark, blended by how worn the grass is. Each is changed on its
    /// own, so a beach lot (grass and sand) keeps its sand in autumn and is all snow in winter.
    /// </summary>
    public static class Ground
    {
        /// <summary>The ground for one of a lot's two terrain types.</summary>
        /// <param name="own">The type the game gave the lot.</param>
        /// <param name="season">The season, or null if the season is not to change the ground (the player's setting).</param>
        /// <param name="snowing">Whether snow is falling on the lot: snow lies while it does, whatever the season.</param>
        public static TerrainType For(TerrainType own, Season? season, bool snowing)
        {
            if (snowing || season == Season.Winter)
            {
                switch (own)
                {
                    case TerrainType.GRASS:
                    case TerrainType.TS1DarkGrass:
                    case TerrainType.TS1AutumnGrass:
                    case TerrainType.SAND:
                        return TerrainType.SNOW;
                }
                return own; //snow, rock, water and cloud stay as they are
            }
            if (season == Season.Autumn && (own == TerrainType.GRASS || own == TerrainType.TS1DarkGrass)) return TerrainType.TS1AutumnGrass;
            return own;
        }
    }
}
