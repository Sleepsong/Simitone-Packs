/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LotTraits
{
    /// <summary>One trait a lot can have.</summary>
    public sealed class Trait
    {
        public string Id;
        public string Name;
        public string Description;
        public NeedEffect Effect = new NeedEffect();

        /// <summary>What the trait does, in words, made from its numbers so that the two cannot disagree.</summary>
        public string EffectText()
        {
            var parts = new List<string>();
            for (int i = 0; i < Effect.Rates.Length; i++)
            {
                var rate = Effect.Rates[i];
                if (rate == 1f) continue;
                var need = Needs.Name(Needs.Dropping[i]);
                if (rate == 0f) { parts.Add(need + " never drops"); continue; }
                var percent = (int)Math.Round(Math.Abs(rate - 1f) * 100);
                if (rate >= 2f && rate == MathF.Round(rate)) parts.Add(need + " drops " + (int)rate + " times as fast");
                else parts.Add(need + " drops " + percent + "% " + (rate > 1f ? "faster" : "slower"));
            }
            if (Effect.Room != 0) parts.Add("Room " + (Effect.Room > 0 ? "+" : "") + Effect.Room);
            return parts.Count == 0 ? "No effect" : string.Join(", ", parts) + ".";
        }
    }

    /// <summary>
    /// The traits there are, from traits.json in the pack's folder, and how many a lot can have. The file is the
    /// pack's to change: a trait's numbers can be tuned, and traits that only change needs can be added, without
    /// building the pack again.
    /// </summary>
    public sealed class TraitBook
    {
        public const int DefaultMost = 3; //as many as The Sims 4 lets a lot have
        public const float FastestRate = 5f;
        private static readonly Regex IdPattern = new Regex("^[a-z0-9_-]{1,32}$");

        public int Most = DefaultMost;
        public List<Trait> Traits = new List<Trait>();

        public static TraitBook Empty => new TraitBook();

        public Trait Get(string id) => Traits.FirstOrDefault(x => x.Id == id);

        /// <summary>A trait by its id or its name, or the one whose name begins so, if only one does.</summary>
        public Trait Find(string words)
        {
            words = (words ?? "").Trim();
            if (words == "") return null;
            var exact = Traits.FirstOrDefault(x => x.Id == words.ToLowerInvariant() || string.Equals(x.Name, words, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var starts = Traits.Where(x => x.Name.StartsWith(words, StringComparison.OrdinalIgnoreCase)).ToList();
            return starts.Count == 1 ? starts[0] : null;
        }

        public static bool IsId(string id) => id != null && IdPattern.IsMatch(id);

        /// <summary>
        /// Read the traits from a file. What cannot be used is left out and said in the problems; a file that cannot
        /// be read at all gives no traits.
        /// </summary>
        public static TraitBook Load(string path, List<string> problems)
        {
            if (!File.Exists(path))
            {
                problems.Add("There is no " + Path.GetFileName(path) + " in the pack's folder, so there are no traits to choose.");
                return Empty;
            }
            try
            {
                return Parse(File.ReadAllText(path), problems);
            }
            catch (Exception e)
            {
                problems.Add(Path.GetFileName(path) + " could not be read, so there are no traits to choose: " + e.Message);
                return Empty;
            }
        }

        public static TraitBook Parse(string json, List<string> problems)
        {
            var book = new TraitBook();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.TryGetProperty("most", out var most))
            {
                if (most.ValueKind == JsonValueKind.Number && most.TryGetInt32(out var n) && n >= 1 && n <= 10) book.Most = n;
                else problems.Add("\"most\" should be a whole number from 1 to 10; it is " + DefaultMost + ".");
            }
            if (!root.TryGetProperty("traits", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                problems.Add("The file has no list of \"traits\".");
                return book;
            }
            foreach (var item in list.EnumerateArray())
            {
                var trait = ParseTrait(item, out var problem);
                if (trait != null && book.Get(trait.Id) != null) { trait = null; problem = "a second trait with the id " + item.GetProperty("id").GetString(); }
                if (trait == null) { problems.Add("A trait was left out: " + problem + "."); continue; }
                book.Traits.Add(trait);
            }
            return book;
        }

        private static Trait ParseTrait(JsonElement item, out string problem)
        {
            problem = null;
            if (item.ValueKind != JsonValueKind.Object) { problem = "it is not an object"; return null; }
            string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString().Trim() : null;

            var trait = new Trait() { Id = Text("id"), Name = Text("name"), Description = Text("description") ?? "" };
            if (!IsId(trait.Id)) { problem = "its id (" + (trait.Id ?? "none") + ") should be up to 32 lowercase letters, digits, - and _"; return null; }
            if (string.IsNullOrEmpty(trait.Name)) { problem = trait.Id + " has no name"; return null; }

            if (item.TryGetProperty("needs", out var needs))
            {
                if (needs.ValueKind != JsonValueKind.Object) { problem = trait.Id + "'s needs should be an object, such as { \"hygiene\": 1.5 }"; return null; }
                foreach (var need in needs.EnumerateObject())
                {
                    var index = Needs.IndexOf(need.Name);
                    if (index < 0) { problem = trait.Id + " names a need the game does not drop: " + need.Name + " (it drops " + string.Join(", ", Needs.Dropping.Select(Needs.Name)) + ")"; return null; }
                    if (need.Value.ValueKind != JsonValueKind.Number || !need.Value.TryGetSingle(out var rate) || !(rate >= 0 && rate <= FastestRate))
                    {
                        problem = trait.Id + "'s rate for " + need.Name + " should be a number from 0 (never drops) to " + FastestRate.ToString(CultureInfo.InvariantCulture) + ", where 1 is the game's own";
                        return null;
                    }
                    trait.Effect.Rates[index] = rate;
                }
            }
            if (item.TryGetProperty("room", out var room))
            {
                if (room.ValueKind != JsonValueKind.Number || !room.TryGetInt32(out var points) || points < -50 || points > 50)
                {
                    problem = trait.Id + "'s room should be a whole number from -50 to 50";
                    return null;
                }
                trait.Effect.Room = points;
            }
            if (trait.Effect.IsNone) { problem = trait.Id + " changes nothing"; return null; }
            return trait;
        }
    }
}
