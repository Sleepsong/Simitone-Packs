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
using System.Text.Json.Serialization;

namespace Moodlets
{
    /// <summary>One kind of moodlet: what it is called, what it says, how much mood it gives or takes, and for how long.</summary>
    public class MoodletKind
    {
        /// <summary>The name shown: "Promoted".</summary>
        public string Name { get; set; } = "";
        /// <summary>What it is about, in a sentence: "Moved up at work."</summary>
        public string Text { get; set; } = "";
        /// <summary>Added to the Sim's mood while it lasts. The game's mood runs from -100 to 100.</summary>
        public int Mood { get; set; }
        /// <summary>How long it lasts, in hours of game time.</summary>
        public int Hours { get; set; }
        /// <summary>
        /// Moodlets of the same group replace each other: a Sim who is fired after a promotion is no longer happy
        /// about the promotion. Empty for none.
        /// </summary>
        public string Group { get; set; } = "";

        [JsonIgnore] public string Id = "";
        [JsonIgnore] public int Minutes => Hours * 60;
        [JsonIgnore] public bool Good => Mood >= 0;

        public MoodletKind Copy() => (MoodletKind)MemberwiseClone();
    }

    /// <summary>
    /// The moodlets there are, and how strong they are. Read from moodlets.json in the pack's folder, so they can be
    /// tuned without building the pack again; anything the file leaves out is as it is here.
    ///
    /// The game works out a Sim's mood as the average of their eight needs (seven needs and the room), each -100 to
    /// 100. So one point of mood is as much as every need moving one point, or one need moving eight. The values
    /// here are smaller than The Sims 3's for that reason: there, moodlets are the whole of a Sim's mood; here, they
    /// are added to what the needs make it. And all of a Sim's moodlets together are held to Cap, so that needs stay
    /// what matters most.
    /// </summary>
    public class Tuning
    {
        /// <summary>The most all of a Sim's moodlets together can add to their mood, or take from it.</summary>
        public int Cap { get; set; } = DefaultCap;
        /// <summary>By id: "promoted".</summary>
        public Dictionary<string, MoodletKind> Moodlets { get; set; } = new Dictionary<string, MoodletKind>();

        public const int DefaultCap = 25;
        /// <summary>How far a value may be set: the whole of the game's mood scale, and two weeks.</summary>
        public const int MostMood = 100, LongestHours = 24 * 14;

        // the moodlets the pack gives, by what sets them off
        public const string JustMarried = "just_married", NewBaby = "new_baby",
            Promoted = "promoted", NewJob = "new_job", Demoted = "demoted", Fired = "fired",
            BurntFood = "burnt_food", Fire = "fire", LostFamily = "lost_family";

        public static Tuning Defaults()
        {
            var t = new Tuning();
            void Add(string id, string name, string text, int mood, int hours, string group = "") =>
                t.Moodlets[id] = new MoodletKind() { Id = id, Name = name, Text = text, Mood = mood, Hours = hours, Group = group };
            Add(JustMarried, "Just Married", "Married, and living together.", 20, 48);
            Add(NewBaby, "New Baby", "A baby has been born into the household.", 15, 48);
            Add(Promoted, "Promoted", "Moved up at work.", 12, 24, "job");
            Add(NewJob, "New Job", "Starting out in a new job.", 6, 12, "job");
            Add(Demoted, "Demoted", "Moved down at work.", -12, 24, "job");
            Add(Fired, "Fired", "Lost their job.", -15, 24, "job");
            Add(BurntFood, "Burnt Food", "Set the cooking on fire.", -8, 4);
            Add(Fire, "Fire!", "There was a fire.", -12, 6);
            Add(LostFamily, "Lost a Loved One", "Someone has left the household for good.", -20, 72);
            return t;
        }

        public MoodletKind Find(string id) => id != null && Moodlets.TryGetValue(id, out var kind) ? kind : null;

        /// <summary>A kind by its id or its name, as typed: "promoted", "Burnt Food", "burnt".</summary>
        public MoodletKind Lookup(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed)) return null;
            var word = typed.Trim().ToLowerInvariant().Replace(' ', '_');
            var exact = Find(word) ?? Moodlets.Values.FirstOrDefault(k => Plain(k.Name) == Plain(typed));
            if (exact != null) return exact;
            var starts = Moodlets.Values.Where(k => k.Id.StartsWith(word) || Plain(k.Name).StartsWith(Plain(typed))).ToList();
            return starts.Count == 1 ? starts[0] : null;
        }

        private static string Plain(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>Put anything a hand-edited file holds back within reason, and give every kind its id.</summary>
        public void Repair()
        {
            Cap = Math.Clamp(Cap, 0, MostMood);
            Moodlets ??= new Dictionary<string, MoodletKind>();
            foreach (var gone in Moodlets.Where(x => x.Value == null || string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Key).ToList()) Moodlets.Remove(gone);
            foreach (var pair in Moodlets)
            {
                var kind = pair.Value;
                kind.Id = pair.Key;
                kind.Name = string.IsNullOrWhiteSpace(kind.Name) ? pair.Key : kind.Name.Trim();
                kind.Text ??= "";
                kind.Group ??= "";
                kind.Mood = Math.Clamp(kind.Mood, -MostMood, MostMood);
                kind.Hours = Math.Clamp(kind.Hours, 1, LongestHours);
            }
        }

        // ---- the file ----

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// The defaults, with what the file says on top. A moodlet the file names that the pack does not know is kept:
        /// it can be given from the console, though nothing in the game sets it off. Problem says what was wrong
        /// with the file, if anything; the defaults are used for what could not be read.
        /// </summary>
        public static Tuning Read(string path, out string problem)
        {
            problem = null;
            var tuning = Defaults();
            if (path == null || !File.Exists(path)) return tuning;
            try
            {
                var file = JsonSerializer.Deserialize<Tuning>(File.ReadAllText(path), Json);
                if (file == null) return tuning;
                tuning.Cap = file.Cap;
                foreach (var pair in file.Moodlets ?? new Dictionary<string, MoodletKind>())
                {
                    if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Key)) continue;
                    var id = pair.Key.Trim().ToLowerInvariant();
                    tuning.Moodlets[id] = pair.Value;
                }
            }
            catch (Exception e)
            {
                problem = Path.GetFileName(path) + " could not be read (" + e.Message + "), so the pack's own values are used";
                tuning = Defaults();
            }
            tuning.Repair();
            return tuning;
        }

        public bool Write(string path, out string problem)
        {
            problem = null;
            try
            {
                var temp = path + ".new";
                File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }
        }
    }
}
