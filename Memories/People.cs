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
using System.Linq;

namespace Memories
{
    /// <summary>What the game knows about a Sim, by the GUID of their character: their name, age, sex and household, and how well they like another Sim.</summary>
    public static class People
    {
        /// <summary>The neighbourhood that is loaded.</summary>
        public static TS1NeighborhoodProvider Neighbourhood => Content.Get()?.Neighborhood;

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

        public static Neighbour ByNeighbourId(short id) => Neighbourhood?.GetNeighborByID(id);

        /// <summary>The Sim's first name, as the game shows it.</summary>
        public static string FirstName(uint guid)
        {
            try
            {
                var obj = Content.Get()?.WorldObjects?.Get(guid);
                if (obj != null)
                {
                    var name = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                    if (!string.IsNullOrWhiteSpace(obj.OBJ.ChunkLabel)) return obj.OBJ.ChunkLabel;
                }
            }
            catch (Exception)
            {
                //a character whose file cannot be read is still remembered
            }
            return "a Sim who has gone";
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

        /// <summary>First name and household name, or just the first name for a Sim of no household.</summary>
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

        /// <summary>The Sim's stage, by the age the neighbourhood keeps for them. A Sim it has no age for counts as an adult.</summary>
        public static Stage StageOf(uint guid) => Recognise.StageOf(Data(guid, VMPersonDataVariable.PersonsAge, 27));

        public static bool IsAdult(uint guid) => StageOf(guid) >= Stage.Adult;

        public static bool IsFemale(uint guid) => Data(guid, VMPersonDataVariable.Gender, 0) == 1;

        /// <summary>The game's scores from one Sim to another are a list: the daily score first.</summary>
        public const int DailyScore = 0;

        /// <summary>How much one Sim likes another today, by their numbers in the neighbourhood. Null if the game keeps no score between them.</summary>
        public static short? Daily(short fromNeighbour, short toNeighbour)
        {
            var from = ByNeighbourId(fromNeighbour);
            if (from?.Relationships == null || !from.Relationships.TryGetValue(toNeighbour, out var scores) || scores == null || scores.Count <= DailyScore) return null;
            return scores[DailyScore];
        }

        // ---- jobs ----

        /// <summary>A job's title and career, for example "Desk Sergeant (Law Enforcement)". Null if the game does not have it.</summary>
        public static string JobTitle(short job, short level)
        {
            try
            {
                var career = Content.Get()?.Jobs?.GetJob((ushort)job);
                if (career?.JobLevels == null || level < 0 || level >= career.JobLevels.Length) return null;
                var title = career.JobLevels[level]?.JobName?.Trim();
                var name = career.Name?.Trim();
                if (string.IsNullOrEmpty(title)) return name;
                return string.IsNullOrEmpty(name) ? title : title + " (" + name + ")";
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Whether a level is the last of its career.</summary>
        public static bool TopOfCareer(short job, short level)
        {
            try
            {
                var career = Content.Get()?.Jobs?.GetJob((ushort)job);
                return career?.JobLevels != null && career.JobLevels.Length > 0 && level == career.JobLevels.Length - 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The skills the game keeps, with their names.</summary>
        public static readonly (VMPersonDataVariable Data, string Name)[] Skills =
        {
            (VMPersonDataVariable.CookingSkill, "Cooking"),
            (VMPersonDataVariable.MechanicalSkill, "Mechanical"),
            (VMPersonDataVariable.CharismaSkill, "Charisma"),
            (VMPersonDataVariable.BodySkill, "Body"),
            (VMPersonDataVariable.LogicSkill, "Logic"),
            (VMPersonDataVariable.CreativitySkill, "Creativity"),
        };
    }
}
