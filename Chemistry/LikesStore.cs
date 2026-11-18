/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Chemistry
{
    /// <summary>
    /// Every Sim's turn-ons, kept in a file of the pack's own inside the neighbourhood's folder (Chemistry.txt, beside
    /// Neighborhood.iff), one line for each Sim by the GUID of their character. The game's own files are not changed, so
    /// the neighbourhood loads the same with the pack switched off.
    ///
    /// A Sim who is not in the file yet is given the turn-ons Attraction.Default works out for them, and is written down
    /// the next time the file is saved, so that a later version of the pack with other traits does not change them.
    /// </summary>
    public static class LikesStore
    {
        public const string FileName = "Chemistry.txt";

        /// <summary>The neighbourhood that is loaded. (The pack's checks stand a made-up one in for it.)</summary>
        public static TS1NeighborhoodProvider Neighbourhood => StandIn ?? Content.Get()?.Neighborhood;
        public static TS1NeighborhoodProvider StandIn;

        private static Dictionary<uint, Likes> Loaded;
        private static string LoadedFrom;
        private static bool Changed;

        public static string PathNow
        {
            get
            {
                var folder = Neighbourhood?.UserPath;
                return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, FileName);
            }
        }

        private static Dictionary<uint, Likes> All()
        {
            var path = PathNow;
            if (Loaded != null && LoadedFrom == path) return Loaded;
            var all = new Dictionary<uint, Likes>();
            if (path != null)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        all = Read(File.ReadAllText(path), out var skipped);
                        if (skipped > 0) Log.Write(skipped + " line(s) of " + path + " could not be read; those Sims get their turn-ons worked out again.");
                    }
                }
                catch (Exception e)
                {
                    Log.Write("Could not read " + path + ": " + e.Message);
                }
            }
            Loaded = all;
            LoadedFrom = path;
            Changed = false;
            return all;
        }

        /// <summary>A Sim's turn-ons: from the file, or else worked out (and kept from now on).</summary>
        public static Likes For(uint guid)
        {
            var all = All();
            if (!all.TryGetValue(guid, out var likes))
            {
                likes = Attraction.Default(guid);
                all[guid] = likes;
                Changed = true;
            }
            return likes;
        }

        public static void Set(uint guid, Likes likes)
        {
            if (likes == null || likes.Problem() != null) throw new ArgumentException("Turn-ons that cannot go together");
            All()[guid] = likes;
            Changed = true;
        }

        /// <summary>Write the file if anything in it changed. Returns false only if it should have been written and was not.</summary>
        public static bool SaveIfChanged()
        {
            var path = PathNow;
            if (path == null || Loaded == null || LoadedFrom != path || !Changed) return true;
            try
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, Write(Loaded));
                File.Move(temp, path, true);
                Changed = false;
                return true;
            }
            catch (Exception e)
            {
                Log.Write("Could not save " + path + ": " + e.Message);
                return false;
            }
        }

        // ---- the file: one Sim to a line, "GUID turn-on turn-on turn-off" ----

        public static string Write(Dictionary<uint, Likes> all)
        {
            var text = new StringBuilder();
            text.Append("# The Chemistry pack's turn-ons and turn-offs, one Sim to a line: their character's GUID, two turn-ons, then the turn-off.\n");
            text.Append("# Change them with the game closed, or with \"chemistry set\" in the console. A Sim who is not here is given some.\n");
            text.Append("# The words: ").Append(Traits.AllWords).Append('\n');
            foreach (var pair in all.OrderBy(x => x.Key))
            {
                text.Append(pair.Key.ToString("X8", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(Traits.Word(pair.Value.On1)).Append(' ')
                    .Append(Traits.Word(pair.Value.On2)).Append(' ')
                    .Append(Traits.Word(pair.Value.Off)).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>The Sims in the file. A line that cannot be read, or whose turn-ons cannot go together, is skipped and counted.</summary>
        public static Dictionary<uint, Likes> Read(string text, out int skipped)
        {
            skipped = 0;
            var all = new Dictionary<uint, Likes>();
            if (text == null) return all;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line == "" || line.StartsWith("#")) continue;
                var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length != 4
                    || !uint.TryParse(words[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var guid)
                    || !Traits.TryParse(words[1], out var on1) || !Traits.TryParse(words[2], out var on2) || !Traits.TryParse(words[3], out var off))
                {
                    skipped++;
                    continue;
                }
                var likes = new Likes(on1, on2, off);
                if (likes.Problem() != null) { skipped++; continue; }
                all[guid] = likes;
            }
            return all;
        }
    }

    public static class Log
    {
        /// <summary>Lines the pack writes to the terminal start with this.</summary>
        public static void Write(string message) => Console.WriteLine("[chemistry] " + message);
    }
}
