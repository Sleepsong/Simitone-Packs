/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeStages
{
    /// <summary>
    /// Choosing the body a child grows into.
    ///
    /// The Sims has no grown-up version of a child: a child's head and clothes are meshes of their own. So a Sim
    /// who grows up is given an adult head and body of the same sex and skin colour, picked from the ones the game
    /// has, the way Create a Sim offers them. Which one is chance. (The Sims 2 keeps a face through life; that
    /// needs art The Sims does not have.)
    ///
    /// This file only works with names, so it can be checked without a copy of the game.
    /// </summary>
    public static class Body
    {
        public static readonly string[] SkinColours = { "lgt", "med", "drk" };

        /// <summary>
        /// The body strings of an adult, to write over a child's. Null if the game has no adult head or body for
        /// this sex and skin colour (then the Sim cannot grow up).
        /// </summary>
        /// <param name="heads">The game's adult heads for this sex (its "c" collection, type "ma" or "fa").</param>
        /// <param name="bodies">The game's adult bodies for this sex (its "b" collection, types starting "ma" or "fa").</param>
        /// <param name="textures">The names of all the game's skin textures.</param>
        public static Dictionary<int, string> ChooseAdult(bool female, string skin, IEnumerable<string> heads, IEnumerable<string> bodies, IEnumerable<string> textures, Random random)
        {
            var sex = female ? "f" : "m";
            var texnames = textures.ToList();

            //as Create a Sim does: a head or body can be worn if it has a texture in this skin colour
            var headChoices = heads.Distinct()
                .Select(x => new { Mesh = x, Tex = RemoveExt(texnames.FirstOrDefault(y => y.StartsWith(ExtractID(x, skin), StringComparison.OrdinalIgnoreCase))) })
                .Where(x => x.Tex != null).ToList();
            //the game files other things with its bodies (costumes, uniforms). only the three builds are everyday
            //bodies, and only they have the naked and underwear bodies an adult also needs
            var bodyChoices = bodies.Distinct()
                .Where(x => BuildOf(x) != null)
                .Select(x => new { Mesh = x, Tex = RemoveExt(texnames.FirstOrDefault(y => y.StartsWith(ExtractID(x, skin), StringComparison.OrdinalIgnoreCase))) })
                .Where(x => x.Tex != null).ToList();
            if (headChoices.Count == 0 || bodyChoices.Count == 0) return null;

            var head = headChoices[random.Next(headChoices.Count)];
            var body = bodyChoices[random.Next(bodyChoices.Count)];
            var hands = (RemoveExt(texnames.FirstOrDefault(y => string.Equals(RemoveExt(y), "huao" + FindHG(body.Tex), StringComparison.OrdinalIgnoreCase))) ?? "huao" + skin).Substring(4);

            //everything else an adult has (naked, underwear, the outfits expansions add), as for a new Sim
            var strings = new SimTemplateCreateInfo(sex + "a" + BuildOf(body.Mesh), skin).BodyStringReplace;
            strings[1] = body.Mesh + ",BODY=" + body.Tex;
            strings[2] = head.Mesh + ",HEAD-HEAD=" + head.Tex;
            strings[17] = "H" + sex + "LO,HAND=huao" + hands;
            strings[18] = "H" + sex + "RO,HAND=huao" + hands;
            strings[19] = "H" + sex + "LP,HAND=huao" + hands;
            strings[20] = "H" + sex + "RP,HAND=huao" + hands;
            strings[21] = "H" + sex + "LO,HAND=huao" + hands;
            strings[22] = "H" + sex + "RC,HAND=huao" + hands;
            return strings;
        }

        public static readonly string[] Builds = { "fat", "fit", "skn" };

        /// <summary>
        /// The build in a body's name: "fat", "fit" or "skn", the three letters before its first underscore, as the
        /// name spells them. Null for a body that is none of the three.
        /// </summary>
        public static string BuildOf(string body)
        {
            var ind = body.IndexOf('_');
            if (ind < 3) return null;
            var build = body.Substring(ind - 3, 3);
            return Builds.Contains(build.ToLowerInvariant()) ? build : null;
        }

        /// <summary>The skin colour in a Sim's body strings, or the one its person data names.</summary>
        public static string SkinColour(string fromStrings, short fromPersonData)
        {
            var skin = (fromStrings ?? "").Trim().ToLowerInvariant();
            if (SkinColours.Contains(skin)) return skin;
            return SkinColours[Math.Clamp((int)fromPersonData, 0, SkinColours.Length - 1)];
        }

        // these three are Create a Sim's own (TS1CASScreen), so the pack offers what it offers

        private static string FindHG(string item)
        {
            var ind = item.IndexOf('_');
            if (ind != -1) item = item.Substring(ind);
            return item;
        }

        private static string RemoveExt(string item)
        {
            if (item == null) return null;
            var ind = item.LastIndexOf('.');
            if (ind != -1) return item.Substring(0, ind);
            return item;
        }

        private static string ExtractID(string item, string skncol)
        {
            var ind = item.IndexOf('_');
            if (ind != -1) item = item.Substring(0, ind);
            return item + skncol;
        }
    }
}
