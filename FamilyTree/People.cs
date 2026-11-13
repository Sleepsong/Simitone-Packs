/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTree
{
    /// <summary>What the game knows about a Sim, by the GUID of their character: name, age, household, and how they get on with others.</summary>
    public static class People
    {
        /// <summary>Lines the pack writes to the terminal start with this.</summary>
        public static void Log(string message)
        {
            Console.WriteLine("[family] " + message);
        }

        /// <summary>The neighbourhood that is loaded. (The pack's checks stand a made-up one in for it.)</summary>
        public static TS1NeighborhoodProvider Neighbourhood => StandIn ?? Content.Get()?.Neighborhood;
        public static TS1NeighborhoodProvider StandIn;

        public static Neighbour ByGuid(uint guid)
        {
            var entries = Neighbourhood?.Neighbors?.Entries;
            if (entries == null) return null;
            foreach (var n in entries)
            {
                if (n.GUID == guid) return n;
            }
            return null;
        }

        public static bool Exists(uint guid) => ByGuid(guid) != null;

        /// <summary>The Sim's first name, as the game shows it.</summary>
        public static string FirstName(uint guid)
        {
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                if (obj != null)
                {
                    var name = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                    if (!string.IsNullOrWhiteSpace(obj.OBJ.ChunkLabel)) return obj.OBJ.ChunkLabel;
                }
            }
            catch (Exception)
            {
                //a character whose file cannot be read still has a place in the tree
            }
            return "(a Sim who has gone)";
        }

        public static FAMI FamilyOf(uint guid)
        {
            var families = Neighbourhood?.MainResource?.List<FAMI>();
            return families?.FirstOrDefault(f => f.FamilyGUIDs != null && f.FamilyGUIDs.Contains(guid));
        }

        public static string FamilyName(FAMI family)
        {
            if (family == null) return null;
            var name = Neighbourhood?.MainResource?.Get<FAMs>(family.ChunkID)?.GetString(0);
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        /// <summary>First name and household name.</summary>
        public static string FullName(uint guid)
        {
            var first = FirstName(guid);
            var last = FamilyName(FamilyOf(guid));
            return last == null ? first : first + " " + last;
        }

        private static short Data(uint guid, VMPersonDataVariable variable, short otherwise)
        {
            var data = ByGuid(guid)?.PersonData;
            var index = (int)variable;
            return (data != null && index < data.Length) ? data[index] : otherwise;
        }

        /// <summary>An adult to the game, by the age it keeps for them (which the Life Stages pack moves on). A Sim the game has no age for counts as an adult.</summary>
        public static bool IsAdult(uint guid) => Data(guid, VMPersonDataVariable.PersonsAge, 27) >= 18;

        public static bool IsFemale(uint guid) => Data(guid, VMPersonDataVariable.Gender, 0) == 1;

        private static readonly Dictionary<uint, bool> PersonCache = new Dictionary<uint, bool>();

        /// <summary>
        /// A Sim, and not a pet. Cats and dogs are characters to the game too, but are not in anyone's family tree.
        /// A character whose file does not say counts as a Sim.
        /// </summary>
        public static bool IsPerson(uint guid)
        {
            if (PersonCache.TryGetValue(guid, out var known)) return known;
            var person = true;
            try
            {
                //the first of a character's body strings is what it is: adult, child, or a kind of pet
                var kind = Content.Get()?.WorldObjects?.Get(guid)?.Resource?.Get<STR>(200)?.GetString(0);
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    kind = kind.Trim().ToLowerInvariant();
                    person = kind == "adult" || kind == "child";
                }
            }
            catch (Exception)
            {
                return true; //(not remembered: it may read next time)
            }
            PersonCache[guid] = person;
            return person;
        }

        /// <summary>Start a Sim off getting on with all the family they have in the tree, as their tie to each calls for. Returns how many.</summary>
        public static int RaiseScoresWithFamily(Kin kin, uint sim)
        {
            int raised = 0;
            foreach (var (other, tie) in kin.FamilyOf(sim))
            {
                var scores = StartingScores.For(tie);
                if (scores != null && RaiseScores(sim, other, scores.Value.Item1, scores.Value.Item2)) raised++;
            }
            return raised;
        }

        // ---- how two Sims get on ----

        /// <summary>The game's scores from one Sim to another are a list. These are the two it shows as bars.</summary>
        public const int DailyScore = 0;
        public const int LifetimeScore = 2;

        /// <summary>
        /// Start two Sims off getting on at least this well, both ways. A score that is already higher is left alone.
        /// Returns false if either Sim is not in the neighbourhood.
        /// </summary>
        public static bool RaiseScores(uint a, uint b, short daily, short lifetime)
        {
            var na = ByGuid(a);
            var nb = ByGuid(b);
            if (na == null || nb == null) return false;
            Raise(na, nb.NeighbourID, daily, lifetime);
            Raise(nb, na.NeighbourID, daily, lifetime);
            return true;
        }

        private static void Raise(Neighbour from, short to, short daily, short lifetime)
        {
            if (from.Relationships == null) from.Relationships = new Dictionary<int, List<short>>();
            if (!from.Relationships.TryGetValue(to, out var scores))
            {
                scores = new List<short>();
                from.Relationships[to] = scores;
            }
            while (scores.Count <= LifetimeScore) scores.Add(0);
            scores[DailyScore] = Math.Max(scores[DailyScore], daily);
            scores[LifetimeScore] = Math.Max(scores[LifetimeScore], lifetime);
        }
    }

    /// <summary>
    /// The neighbourhood's family tree, kept in a file of the pack's own inside the neighbourhood's folder
    /// (FamilyTree.txt, beside Neighborhood.iff). The game's own files are not changed by it, so a neighbourhood
    /// still loads the same with the pack switched off.
    /// </summary>
    public static class KinStore
    {
        public const string FileName = "FamilyTree.txt";

        private static Kin Loaded;
        private static string LoadedFrom;

        public static string PathNow
        {
            get
            {
                var folder = People.Neighbourhood?.UserPath;
                return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, FileName);
            }
        }

        /// <summary>The tree of the neighbourhood that is loaded now. Never null: with no neighbourhood, an empty tree that is not kept.</summary>
        public static Kin Get()
        {
            var path = PathNow;
            if (path == null) return new Kin();
            if (Loaded != null && LoadedFrom == path) return Loaded;
            var kin = new Kin();
            try
            {
                if (File.Exists(path))
                {
                    kin = Kin.Read(File.ReadAllText(path), out var skipped);
                    if (skipped > 0) People.Log(skipped + " line(s) of " + path + " could not be read and were left out.");
                }
            }
            catch (Exception e)
            {
                People.Log("Could not read " + path + ": " + e.Message);
            }
            Loaded = kin;
            LoadedFrom = path;
            return kin;
        }

        /// <summary>Write the tree to its file. Returns whether it worked.</summary>
        public static bool Save()
        {
            var path = PathNow;
            if (path == null || Loaded == null || LoadedFrom != path) return false;
            try
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, Loaded.Write());
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception e)
            {
                People.Log("Could not save " + path + ": " + e.Message);
                return false;
            }
        }
    }
}
