/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memories
{
    /// <summary>What a memory is of. A memory belongs to one Sim; most life events give a memory to each Sim they touch.</summary>
    public enum MemoryKind
    {
        /// <summary>Other: who they kissed, or who kissed them.</summary>
        FirstKiss,
        /// <summary>Other: who they married.</summary>
        Married,
        /// <summary>Detail: the job and career.</summary>
        GotJob,
        /// <summary>Detail: the new job and career. Top: the top of the career.</summary>
        Promoted,
        Demoted,
        /// <summary>Detail: the job they had.</summary>
        LostJob,
        /// <summary>Other: the baby's other parent, if known.</summary>
        HadBaby,
        /// <summary>Others: their parents, if known.</summary>
        Born,
        /// <summary>Detail: the stage they reached.</summary>
        GrewUp,
        Died,
        /// <summary>Other: who died. Detail: what they were to this Sim (father, wife...), if known.</summary>
        RelativeDied,
        /// <summary>Top: this Sim started it. Detail: how, if known. Home: on the household's own lot.</summary>
        Fire,
        /// <summary>Detail: what was taken, one thing to a line. Top: the police came.</summary>
        Burgled,
    }

    /// <summary>One thing a Sim remembers.</summary>
    public sealed class Memory
    {
        /// <summary>Numbered in the order they were made, so a timeline is in order even within a minute.</summary>
        public long Id { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MemoryKind Kind { get; set; }
        [JsonConverter(typeof(HexGuid))]
        public uint Sim { get; set; }
        [JsonConverter(typeof(HexGuid))]
        public uint Other { get; set; }
        [JsonConverter(typeof(HexGuids))]
        public uint[] Others { get; set; } = Array.Empty<uint>();
        public string Detail { get; set; }
        public bool Top { get; set; }
        public bool Home { get; set; }
        public LifeTime When { get; set; }
        /// <summary>The Sim's stage at the time.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Stage Stage { get; set; }

        /// <summary>A happy memory, or a sad one (shown green or red, as The Sims 2 does).</summary>
        public bool Good
        {
            get
            {
                switch (Kind)
                {
                    case MemoryKind.Demoted:
                    case MemoryKind.LostJob:
                    case MemoryKind.Died:
                    case MemoryKind.RelativeDied:
                    case MemoryKind.Fire:
                    case MemoryKind.Burgled:
                        return false;
                    default:
                        return true;
                }
            }
        }

        /// <summary>The memory in a line, for the timeline. name gives a Sim's full name.</summary>
        public string Describe(Func<uint, string> name)
        {
            string Who(uint sim) => sim == 0 ? null : name(sim);
            switch (Kind)
            {
                case MemoryKind.FirstKiss:
                    return Other == 0 ? "First kiss" : "First kiss, with " + Who(Other);
                case MemoryKind.Married:
                    return Other == 0 ? "Married" : "Married " + Who(Other);
                case MemoryKind.GotJob:
                    return "Got a job: " + (Detail ?? "a new career");
                case MemoryKind.Promoted:
                    return "Promoted to " + (Detail ?? "a better job") + (Top ? ", the top of the career" : "");
                case MemoryKind.Demoted:
                    return "Demoted to " + (Detail ?? "a lesser job");
                case MemoryKind.LostJob:
                    return Detail == null ? "Out of work" : "Out of work, after being " + Detail;
                case MemoryKind.HadBaby:
                    return Other == 0 ? "Had a baby" : "Had a baby with " + Who(Other);
                case MemoryKind.Born:
                    var parents = Others.Where(x => x != 0).Select(Who).ToList();
                    return parents.Count == 0 ? "Born" : "Born to " + string.Join(" and ", parents);
                case MemoryKind.GrewUp:
                    var stage = Detail ?? Recognise.StageName(Stage);
                    return "Became " + (stage.StartsWith("a") || stage.StartsWith("e") ? "an " : "a ") + stage;
                case MemoryKind.Died:
                    return "Died";
                case MemoryKind.RelativeDied:
                    return (Detail == null ? Who(Other) + ", of the family," : "Their " + Detail + ", " + Who(Other) + ",") + " died";
                case MemoryKind.Fire:
                    var fire = Top ? "Started a fire" : (Home ? "A fire at home" : "Saw a fire");
                    return Top && Detail != null ? fire + " (" + Detail + ")" : fire;
                case MemoryKind.Burgled:
                    var taken = (Detail ?? "").Split('\n').Where(x => x.Trim() != "").ToList();
                    var line = taken.Count == 0 ? "A burglar broke in" : "A burglar broke in and took " + string.Join(", ", taken);
                    return Top ? line + ". The police came" : line;
                default:
                    return Kind.ToString();
            }
        }
    }

    /// <summary>
    /// Everything remembered in a neighbourhood, with the babies waiting to grow up and the day the neighbourhood is
    /// on. Kept in a file of the pack's own, beside Simitone's saves and not inside them, so the game's files are as
    /// they were.
    ///
    /// It is written only when the game saves, and read again when a neighbourhood is loaded or a lot is left, so it
    /// matches the saved game: leave a lot without saving, and what happened there is forgotten with it. (As the
    /// Life Stages pack keeps its ages.)
    /// </summary>
    public sealed class Journal
    {
        /// <summary>The neighbourhood's day: 1 when the pack first sees it played, one more at each midnight on a lot played.</summary>
        public int Day { get; set; } = 1;
        public long NextId { get; set; } = 1;
        public List<Memory> Memories { get; set; } = new List<Memory>();
        public List<WaitingBaby> Babies { get; set; } = new List<WaitingBaby>();

        /// <summary>A Sim's memories, in the order they happened.</summary>
        public List<Memory> Of(uint sim) => Memories.Where(m => m.Sim == sim).OrderBy(m => m.When).ThenBy(m => m.Id).ToList();

        public bool Has(uint sim, MemoryKind kind) => Memories.Any(m => m.Sim == sim && m.Kind == kind);

        /// <summary>Keep a memory, numbered after the last.</summary>
        public Memory Add(Memory memory)
        {
            memory.Id = NextId++;
            Memories.Add(memory);
            return memory;
        }

        // ---- the file ----

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions() { WriteIndented = true };

        public static string PathFor(string packId, string userPath)
        {
            //a hash of the neighbourhood's folder: Simitone keeps several (UserData, UserData2...)
            var full = Path.GetFullPath(userPath).Replace('\\', '/').TrimEnd('/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 12).ToLowerInvariant();
            var name = new string(Path.GetFileName(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var pack = new string(packId.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
            return Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "PackData", pack, name + "-" + hash + ".json");
        }

        public string Write() => JsonSerializer.Serialize(this, Json);

        /// <summary>A journal from its file's text. Whatever cannot be read is left out, and counted.</summary>
        public static Journal Read(string text, out int skipped)
        {
            skipped = 0;
            var journal = JsonSerializer.Deserialize<Journal>(text, Json) ?? new Journal();
            journal.Memories ??= new List<Memory>();
            journal.Babies ??= new List<WaitingBaby>();
            var before = journal.Memories.Count + journal.Babies.Count;
            journal.Memories.RemoveAll(m => m == null || m.Sim == 0 || !Enum.IsDefined(typeof(MemoryKind), m.Kind));
            journal.Babies.RemoveAll(b => b == null);
            skipped = before - journal.Memories.Count - journal.Babies.Count;
            foreach (var m in journal.Memories) m.Others ??= Array.Empty<uint>();
            journal.Day = Math.Max(1, journal.Day);
            var highest = journal.Memories.Count == 0 ? 0 : journal.Memories.Max(m => m.Id);
            journal.NextId = Math.Max(journal.NextId, highest + 1);
            return journal;
        }

        /// <summary>What was last saved for this neighbourhood. A missing or unreadable file gives an empty journal.</summary>
        public static Journal Load(string packId, string userPath)
        {
            var path = PathFor(packId, userPath);
            try
            {
                if (File.Exists(path))
                {
                    var journal = Read(File.ReadAllText(path), out var skipped);
                    if (skipped > 0) Log.Write(skipped + " entr(ies) of " + path + " could not be read and were left out.");
                    return journal;
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read the memories kept in " + path + " (" + e.Message + "). Starting afresh; the file is kept as " + Path.GetFileName(path) + ".unreadable.");
                try { File.Copy(path, path + ".unreadable", true); } catch { }
            }
            return new Journal();
        }

        public bool Save(string packId, string userPath)
        {
            var path = PathFor(packId, userPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                //written beside and moved into place, so a crash half way leaves the old file whole
                var temp = path + ".new";
                File.WriteAllText(temp, Write());
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("Could not save the memories in " + path + ": " + e.Message);
                return false;
            }
        }
    }

    /// <summary>A Sim's GUID in the file, in hexadecimal as the Family Tree pack writes them.</summary>
    public sealed class HexGuid : JsonConverter<uint>
    {
        public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number) return reader.GetUInt32();
            var text = reader.GetString();
            return string.IsNullOrEmpty(text) ? 0 : uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value == 0 ? "" : value.ToString("X8"));
        }
    }

    public sealed class HexGuids : JsonConverter<uint[]>
    {
        private static readonly HexGuid One = new HexGuid();

        public override uint[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return Array.Empty<uint>();
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("a list of Sims was expected");
            var list = new List<uint>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) list.Add(One.Read(ref reader, typeof(uint), options));
            return list.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, uint[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var v in value ?? Array.Empty<uint>()) One.Write(writer, v, options);
            writer.WriteEndArray();
        }
    }

    /// <summary>What the pack does, in the terminal and in Memories.log in Simitone's user directory.</summary>
    public static class Log
    {
        private static readonly object Lock = new object();
        /// <summary>The tests turn the file off.</summary>
        public static bool ToFile = true;

        public static void Write(string line)
        {
            line = "[memories] " + line;
            Console.WriteLine(line);
            if (!ToFile) return;
            try
            {
                lock (Lock) File.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "Memories.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
