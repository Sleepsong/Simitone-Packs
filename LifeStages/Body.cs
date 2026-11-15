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

            //heads for elders only are not given to a young adult
            var headList = heads.Distinct().ToList();
            var headChoices = Wearable(Looks.Heads.Offer(headList, Stage.Adult).Select(i => headList[i]), skin, texnames);
            if (headChoices.Count == 0) headChoices = Wearable(headList, skin, texnames); //none of the rest in this skin colour
            //the game files other things with its bodies (costumes, uniforms). only the three builds are everyday
            //bodies, and only they have the naked and underwear bodies an adult also needs. bodies for elders only
            //are not given to a young adult
            var bodyList = bodies.Distinct().ToList();
            List<(string Mesh, string Tex)> Everyday(IEnumerable<string> list) => list
                .Where(x => BuildOf(x) != null)
                .Select(x => (Mesh: x, Tex: RemoveExt(texnames.FirstOrDefault(y => y.StartsWith(ExtractID(x, skin), StringComparison.OrdinalIgnoreCase)))))
                .Where(x => x.Tex != null).ToList();
            var bodyChoices = Everyday(Looks.Bodies.Offer(bodyList, Stage.Adult).Select(i => bodyList[i]));
            if (bodyChoices.Count == 0) bodyChoices = Everyday(bodyList); //none of the rest in this skin colour
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

        /// <summary>
        /// The heads that can be worn in a skin colour, with their pictures: as Create a Sim does, those that have a
        /// picture in it. In the order given.
        /// </summary>
        public static List<(string Mesh, string Tex)> Wearable(IEnumerable<string> heads, string skin, IEnumerable<string> textures)
        {
            var texnames = textures as IList<string> ?? textures.ToList();
            return heads.Distinct()
                .Select(x => (Mesh: x, Tex: RemoveExt(texnames.FirstOrDefault(y => y.StartsWith(ExtractID(x, skin), StringComparison.OrdinalIgnoreCase)))))
                .Where(x => x.Tex != null).ToList();
        }

        /// <summary>
        /// The heads to offer a Sim choosing a new look: the one they wear first, so that keeping it is the choice
        /// already made, then the wearable heads for their stage.
        /// </summary>
        public static List<(string Mesh, string Tex)> Choices(string wornMesh, string wornTex, IEnumerable<string> heads, Stage stage, string skin, IEnumerable<string> textures)
        {
            var list = heads.Distinct().ToList();
            var offered = Wearable(Looks.Heads.Offer(list, stage).Select(i => list[i]), skin, textures);
            if (offered.Count == 0) offered = Wearable(list, skin, textures); //none for the stage in this skin colour: all
            var result = new List<(string Mesh, string Tex)>();
            if (!string.IsNullOrEmpty(wornMesh) && !string.IsNullOrEmpty(wornTex)) result.Add((wornMesh, wornTex));
            result.AddRange(offered.Where(x => !string.Equals(x.Mesh, wornMesh, StringComparison.OrdinalIgnoreCase)));
            return result;
        }

        /// <summary>
        /// The everyday outfits to offer a grown Sim choosing a new one: the one they wear first, so that keeping it is
        /// the choice already made, then those for their stage that can be worn in their skin colour, each with its
        /// hands' picture. Costumes and uniforms are not everyday outfits, as for a Sim who grows up.
        /// </summary>
        /// <param name="bodyLine">The Sim's everyday outfit line ("mesh,BODY=picture").</param>
        /// <param name="handLine">The Sim's left hand line ("HfLO,HAND=huaolgt").</param>
        public static List<Look> Outfits(string bodyLine, string handLine, IEnumerable<string> bodies, Stage stage, string skin, IEnumerable<string> textures)
        {
            var texnames = textures as IList<string> ?? textures.ToList();
            var list = bodies.Distinct().ToList();
            List<Look> Everyday(IEnumerable<string> names) => names
                .Where(x => BuildOf(x) != null)
                .Select(x => (Mesh: x, Tex: RemoveExt(texnames.FirstOrDefault(y => y.StartsWith(ExtractID(x, skin), StringComparison.OrdinalIgnoreCase)))))
                .Where(x => x.Tex != null)
                .Select(x => new Look(x.Mesh, x.Tex, HandsFor(x.Tex, skin, texnames))).ToList();
            var offered = Everyday(Looks.Bodies.Offer(list, stage).Select(i => list[i]));
            if (offered.Count == 0) offered = Everyday(list);

            var result = new List<Look>();
            var worn = Worn(bodyLine);
            if (!string.IsNullOrEmpty(worn.Mesh) && !string.IsNullOrEmpty(worn.Tex))
            {
                var hands = Worn(handLine).Tex;
                if (hands != null && hands.StartsWith("huao", StringComparison.OrdinalIgnoreCase)) hands = hands.Substring(4);
                result.Add(new Look(worn.Mesh, worn.Tex, string.IsNullOrEmpty(hands) ? skin : hands));
            }
            result.AddRange(offered.Where(x => !string.Equals(x.Mesh, worn.Mesh, StringComparison.OrdinalIgnoreCase)));
            return result;
        }

        /// <summary>The hands that go with an outfit's picture, as Create a Sim finds them: "lgt", or a glove's.</summary>
        public static string HandsFor(string bodyTex, string skin, IEnumerable<string> textures)
        {
            return (RemoveExt(textures.FirstOrDefault(y => string.Equals(RemoveExt(y), "huao" + FindHG(bodyTex), StringComparison.OrdinalIgnoreCase))) ?? "huao" + skin).Substring(4);
        }

        /// <summary>The head a Sim wears, from their body strings' head line ("mesh,HEAD-HEAD=picture").</summary>
        public static (string Mesh, string Tex) Worn(string headLine)
        {
            if (string.IsNullOrEmpty(headLine)) return (null, null);
            var comma = headLine.IndexOf(',');
            var equals = headLine.IndexOf('=');
            var mesh = comma > 0 ? headLine.Substring(0, comma) : headLine;
            var tex = equals > 0 ? headLine.Substring(equals + 1) : null;
            return (mesh, tex);
        }

        public static readonly string[] Builds = { "fat", "fit", "skn" };

        /// <summary>
        /// The build in a body's name: "fat", "fit" or "skn", the three letters before its first underscore, as the
        /// name spells them. Null for a body that is none of the three.
        /// </summary>
        public static string BuildOf(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            //the part before the first underscore, or all of it ("b812mafit"), less a skin colour some put there
            //("b640fafitlgt_sakura")
            var ind = body.IndexOf('_');
            var type = ind < 0 ? body : body.Substring(0, ind);
            foreach (var skin in SkinColours)
                if (type.EndsWith(skin, StringComparison.OrdinalIgnoreCase)) type = type.Substring(0, type.Length - skin.Length);
            if (type.Length < 3) return null;
            var build = type.Substring(type.Length - 3);
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
