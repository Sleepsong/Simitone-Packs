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
    /// Which of the game's adult heads, or bodies, are elders'. "Elder" ones are for elders only; "both" ones are for
    /// adults and elders. Names are kept trimmed and lower case, and matched so.
    /// </summary>
    public class AgeList
    {
        public List<string> Elder { get; set; } = new List<string>();
        public List<string> Both { get; set; } = new List<string>();

        public AgeList Clean()
        {
            List<string> Tidy(List<string> list) => (list ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
            return new AgeList() { Elder = Tidy(Elder), Both = Tidy(Both) };
        }

        private static bool In(List<string> list, string name) => name != null && list.Contains(name.Trim().ToLowerInvariant());

        /// <summary>For elders only.</summary>
        public bool ElderOnly(string name) => In(Elder, name);

        /// <summary>May be offered for this stage. Only adults' and elders' are sorted.</summary>
        public bool Suits(string name, Stage stage)
        {
            if (stage == Stage.Elder) return In(Elder, name) || In(Both, name);
            if (stage == Stage.Adult) return !In(Elder, name);
            return true;
        }

        /// <summary>
        /// Which of a list of the game's adult heads or bodies to offer for a stage, by their places in it. If none
        /// would be left (the lists not filled in for this sex), all are offered rather than none.
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
                string Count(int n, string one, string many) => n + " " + (n == 1 ? one : many);
                return FileName + ": " + Count(Heads.Elder.Count, "head", "heads") + " for elders only, " + Heads.Both.Count + " for adults and elders; "
                    + Count(Bodies.Elder.Count, "body", "bodies") + " for elders only, " + Bodies.Both.Count + " for adults and elders.";
            }
            catch (Exception e)
            {
                Use(null);
                return FileName + " could not be read (" + e.Message + "): elders are offered every adult head and body.";
            }
        }

        /// <summary>Use these lists (the tests give their own). Null for none.</summary>
        public static void Use(LooksFile file)
        {
            Heads = (file?.Heads ?? new AgeList()).Clean();
            Bodies = (file?.Bodies ?? new AgeList()).Clean();
        }
    }
}
