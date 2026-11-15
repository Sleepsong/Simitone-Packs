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
    /// Which of the game's adult heads are elders'. The Sims has no old heads of its own as such, only adult heads,
    /// some of which look old. The pack's heads.json, in its folder, names them: "elder" heads are for elders only,
    /// and "both" heads for adults and elders. It is read once, when Simitone starts.
    /// </summary>
    public class HeadLists
    {
        public List<string> Elder { get; set; } = new List<string>();
        public List<string> Both { get; set; } = new List<string>();
    }

    public static class Heads
    {
        public const string FileName = "heads.json";

        public static HeadLists Lists { get; private set; } = new HeadLists();

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>Read the lists from a pack's folder. A missing or broken file leaves both lists empty, and says so.</summary>
        public static string Load(string folder)
        {
            Lists = new HeadLists();
            var path = folder == null ? null : Path.Combine(folder, FileName);
            if (path == null || !File.Exists(path)) return "No " + FileName + ": elders are offered every adult head.";
            try
            {
                Use(JsonSerializer.Deserialize<HeadLists>(File.ReadAllText(path), Json));
                return FileName + ": " + Lists.Elder.Count + " heads for elders only, " + Lists.Both.Count + " for adults and elders.";
            }
            catch (Exception e)
            {
                Lists = new HeadLists();
                return FileName + " could not be read (" + e.Message + "): elders are offered every adult head.";
            }
        }

        /// <summary>Use these lists (the tests give their own). Names are kept trimmed and lower case.</summary>
        public static void Use(HeadLists lists)
        {
            List<string> Clean(List<string> list) => (list ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
            Lists = new HeadLists() { Elder = Clean(lists?.Elder), Both = Clean(lists?.Both) };
        }

        private static bool In(List<string> list, string head) => head != null && list.Contains(head.Trim().ToLowerInvariant());

        /// <summary>A head only for elders.</summary>
        public static bool ElderOnly(string head) => In(Lists.Elder, head);

        /// <summary>A head that may be offered for this stage. Only adults' and elders' heads are sorted.</summary>
        public static bool Suits(string head, Stage stage)
        {
            if (stage == Stage.Elder) return In(Lists.Elder, head) || In(Lists.Both, head);
            if (stage == Stage.Adult) return !In(Lists.Elder, head);
            return true;
        }

        /// <summary>
        /// Which of a list of adult heads to offer for a stage, by their places in it. If none would be left (the
        /// lists not filled in yet, or none of a skin colour), every head is offered rather than none.
        /// </summary>
        public static List<int> Offer(IList<string> heads, Stage stage)
        {
            var all = Enumerable.Range(0, heads.Count).ToList();
            var suited = all.Where(i => Suits(heads[i], stage)).ToList();
            return suited.Count > 0 ? suited : all;
        }
    }
}
