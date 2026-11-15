/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LifeStages
{
    /// <summary>
    /// Which of the game's heads, or bodies, are for which ages. Of the adults' ones, "elder" ones are for elders
    /// only, "adult" ones for adults only, and "both" ones for both. What none of the lists names is for adults, and
    /// for elders too if OthersForElders: heads are not (an elder is offered only the heads listed for elders), and
    /// bodies are (an elder may wear anything not kept for adults). Of the children's ones, "child" ones are for
    /// children only, not teens. Names are kept trimmed and lower case, and matched so.
    /// </summary>
    public class AgeList
    {
        public List<string> Elder { get; set; } = new List<string>();
        public List<string> Both { get; set; } = new List<string>();
        public List<string> Adult { get; set; } = new List<string>();
        public List<string> Child { get; set; } = new List<string>();

        /// <summary>Whether what no list names is offered to elders as well as adults.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool OthersForElders { get; set; }

        public AgeList Clean(bool othersForElders)
        {
            List<string> Tidy(List<string> list) => (list ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Plain).Distinct().ToList();
            return new AgeList() { Elder = Tidy(Elder), Both = Tidy(Both), Adult = Tidy(Adult), Child = Tidy(Child), OthersForElders = othersForElders };
        }

        /// <summary>
        /// A name as the lists keep it: trimmed, lower case, and without a skin colour. The game files some heads and
        /// outfits once for each skin colour, with the colour in the name before its underscore ("b640fafitlgt_sakura",
        /// "b640fafitmed_sakura", "b640fafitdrk_sakura"), as its own catalogue knows; one name in a list is all of them.
        /// </summary>
        public static string Plain(string name)
        {
            if (name == null) return null;
            name = name.Trim().ToLowerInvariant();
            var ind = name.IndexOf('_');
            var type = ind < 0 ? name : name.Substring(0, ind);
            foreach (var skin in new[] { "lgt", "med", "drk" })
            {
                if (type.Length > 4 + skin.Length && type.EndsWith(skin))
                    return type.Substring(0, type.Length - skin.Length) + (ind < 0 ? "" : name.Substring(ind));
            }
            return name;
        }

        private static bool In(List<string> list, string name) => name != null && list.Contains(Plain(name));

        /// <summary>For elders only.</summary>
        public bool ElderOnly(string name) => In(Elder, name);

        /// <summary>For adults only.</summary>
        public bool AdultOnly(string name) => In(Adult, name);

        /// <summary>For children only, not teens.</summary>
        public bool ChildOnly(string name) => In(Child, name);

        /// <summary>May be offered for this stage.</summary>
        public bool Suits(string name, Stage stage)
        {
            if (stage == Stage.Elder)
            {
                if (In(Elder, name) || In(Both, name)) return true;
                if (In(Adult, name)) return false;
                return OthersForElders;
            }
            if (stage == Stage.Adult) return !In(Elder, name);
            if (stage == Stage.Teen) return !In(Child, name);
            return true;
        }

        /// <summary>
        /// Which of a list of the game's heads or bodies to offer for a stage, by their places in it. If none would be
        /// left (the lists not filled in for this sex), all are offered rather than none.
        /// </summary>
        public List<int> Offer(IList<string> names, Stage stage)
        {
            var all = Enumerable.Range(0, names.Count).ToList();
            var suited = all.Where(i => Suits(names[i], stage)).ToList();
            return suited.Count > 0 ? suited : all;
        }
    }

    /// <summary>What looks.json holds.</summary>
    public class LooksFile
    {
        public AgeList Heads { get; set; } = new AgeList();
        public AgeList Bodies { get; set; } = new AgeList();
    }

    /// <summary>
    /// The heads and bodies that are elders'. The Sims has only adult heads and clothes, some of which look old: the
    /// pack's looks.json, in its folder, names them. It is read once, when Simitone starts.
    /// </summary>
    public static class Looks
    {
        public const string FileName = "looks.json";

        public static AgeList Heads { get; private set; } = new AgeList();
        public static AgeList Bodies { get; private set; } = new AgeList();

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>Read the lists from a pack's folder. A missing or broken file leaves them empty, and says so.</summary>
        public static string Load(string folder)
        {
            Use(null);
            var path = folder == null ? null : Path.Combine(folder, FileName);
            if (path == null || !File.Exists(path)) return "No " + FileName + ": elders are offered every adult head and body.";
            try
            {
                Use(JsonSerializer.Deserialize<LooksFile>(File.ReadAllText(path), Json));
                return FileName + ": " + Describe(Heads, "head", "heads") + "; " + Describe(Bodies, "body", "bodies") + ".";
            }
            catch (Exception e)
            {
                Use(null);
                return FileName + " could not be read (" + e.Message + "): elders are offered every adult head and body.";
            }
        }

        /// <summary>"15 heads for elders only, 13 for adults and elders": the lists that mean something for the kind.</summary>
        private static string Describe(AgeList list, string one, string many)
        {
            var parts = new List<string>() { list.Elder.Count + " " + (list.Elder.Count == 1 ? one : many) + " for elders only" };
            if (!list.OthersForElders || list.Both.Count > 0) parts.Add(list.Both.Count + " for adults and elders");
            if (list.OthersForElders || list.Adult.Count > 0) parts.Add(list.Adult.Count + " for adults only");
            if (list.Child.Count > 0) parts.Add(list.Child.Count + " for children only");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Use these lists (the tests give their own). Null for none. Heads not listed are adults' only; bodies not
        /// listed are anyone's.
        /// </summary>
        public static void Use(LooksFile file)
        {
            Heads = (file?.Heads ?? new AgeList()).Clean(othersForElders: false);
            Bodies = (file?.Bodies ?? new AgeList()).Clean(othersForElders: true);
        }
    }
}
