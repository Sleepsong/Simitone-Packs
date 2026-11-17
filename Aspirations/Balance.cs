/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Aspirations
{
    /// <summary>
    /// Every number the pack plays by, read from balance.json in the pack's folder: the aspirations, the meter and
    /// its levels, every want, fear and lifetime want, and every reward. Nothing about balance is in the code, so it
    /// can be tuned without building the pack again, and `aspire reload` reads the file again in the game.
    ///
    /// A copy of the file is inside the .dll. It is used when the pack's folder has none, or its file cannot be read,
    /// so a mistake in an edit never leaves the pack without rules.
    /// </summary>
    public class Balance
    {
        public SlotCounts Slots { get; set; } = new SlotCounts();
        public MeterRules Meter { get; set; } = new MeterRules();
        public List<LevelDef> Levels { get; set; } = new List<LevelDef>();
        public RelationshipRules Relationships { get; set; } = new RelationshipRules();
        public List<AspirationDef> Aspirations { get; set; } = new List<AspirationDef>();
        public List<GoalDef> Wants { get; set; } = new List<GoalDef>();
        public List<GoalDef> Fears { get; set; } = new List<GoalDef>();
        public List<GoalDef> Lifetime { get; set; } = new List<GoalDef>();
        public List<RewardDef> Rewards { get; set; } = new List<RewardDef>();

        public AspirationDef Aspiration(string id) => Aspirations.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        public GoalDef Want(string id) => Wants.FirstOrDefault(x => x.Id == id);
        public GoalDef Fear(string id) => Fears.FirstOrDefault(x => x.Id == id);
        public GoalDef LifetimeWant(string id) => Lifetime.FirstOrDefault(x => x.Id == id);
        public RewardDef Reward(string id) => Rewards.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

        // ---- reading ----

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public const string FileName = "balance.json";

        /// <summary>The copy inside the .dll.</summary>
        public static string Embedded()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aspirations.balance.json");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// The rules from the pack's folder, or else from the copy in the .dll. What was wrong, if anything, is in
        /// problems: a goal or reward the pack does not understand is left out, and the rest is used.
        /// </summary>
        public static Balance Load(string folder, List<string> problems)
        {
            if (folder != null)
            {
                var path = Path.Combine(folder, FileName);
                if (File.Exists(path))
                {
                    try
                    {
                        var read = Parse(File.ReadAllText(path), problems);
                        if (read != null) return read;
                    }
                    catch (Exception e)
                    {
                        problems.Add(FileName + " could not be read (" + e.Message + "). Using the pack's own copy.");
                    }
                }
                else problems.Add("There is no " + FileName + " in the pack's folder. Using the pack's own copy.");
            }
            var embedded = Embedded();
            return (embedded == null ? null : Parse(embedded, problems)) ?? new Balance().Repair(problems);
        }

        public static Balance Parse(string text, List<string> problems)
        {
            var balance = JsonSerializer.Deserialize<Balance>(text, Json);
            return balance?.Repair(problems);
        }

        /// <summary>Leave out what cannot be used, and put sense into numbers that make none.</summary>
        public Balance Repair(List<string> problems)
        {
            Slots ??= new SlotCounts();
            Slots.Wants = Math.Clamp(Slots.Wants, 1, 8);
            Slots.Fears = Math.Clamp(Slots.Fears, 0, 8);
            Slots.Locks = Math.Clamp(Slots.Locks, 0, Slots.Wants);
            Meter ??= new MeterRules();
            Meter.Failure ??= new FailureRules();
            Meter.Failure.Breakdown ??= new Dictionary<string, int>();
            Relationships ??= new RelationshipRules();

            Levels = (Levels ?? new List<LevelDef>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).OrderBy(x => x.From).ToList();
            if (Levels.Count == 0)
            {
                problems.Add("No meter levels are given. Using one level.");
                Levels.Add(new LevelDef() { Name = "Steady", From = -100 });
            }
            Levels[0].From = Rules.MeterMin;

            Aspirations = (Aspirations ?? new List<AspirationDef>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).GroupBy(x => x.Id.ToLowerInvariant()).Select(x => x.First()).ToList();
            foreach (var a in Aspirations)
            {
                a.Name ??= a.Id;
                a.About ??= "";
                a.Fit ??= new Dictionary<string, double>();
            }
            if (!Aspirations.Any(x => !x.Child)) problems.Add("No aspiration is given for grown Sims.");

            Wants = Goals(Wants, "want", problems);
            Fears = Goals(Fears, "fear", problems);
            Lifetime = Goals(Lifetime, "lifetime want", problems);

            Rewards = (Rewards ?? new List<RewardDef>()).Where(x => x != null).ToList();
            foreach (var bad in Rewards.Where(x => string.IsNullOrWhiteSpace(x.Id) || x.Effect == null || !Effects.Known(x.Effect.Kind)).ToList())
            {
                problems.Add("The reward " + (bad.Id ?? bad.Name ?? "(no id)") + " is left out: " + (bad.Effect == null ? "it has no effect" : "its effect, " + bad.Effect.Kind + ", is not one the pack knows") + ".");
                Rewards.Remove(bad);
            }
            foreach (var r in Rewards)
            {
                r.Name ??= r.Id;
                r.About ??= "";
                r.Ages = Rules.ParseAges(r.Ages);
                r.Cost = Math.Max(0, r.Cost);
                if (r.Backfire != null && !Effects.Known(r.Backfire.Kind))
                {
                    problems.Add("The reward " + r.Id + " cannot go wrong: its backfire, " + r.Backfire.Kind + ", is not an effect the pack knows.");
                    r.Backfire = null;
                }
            }
            Rewards = Rewards.GroupBy(x => x.Id.ToLowerInvariant()).Select(x => x.First()).ToList();
            return this;
        }

        private List<GoalDef> Goals(List<GoalDef> list, string what, List<string> problems)
        {
            var result = new List<GoalDef>();
            var ids = new HashSet<string>();
            foreach (var goal in list ?? new List<GoalDef>())
            {
                if (goal == null) continue;
                if (string.IsNullOrWhiteSpace(goal.Id)) { problems.Add("A " + what + " with no id is left out."); continue; }
                if (!ids.Add(goal.Id)) { problems.Add("The " + what + " " + goal.Id + " is given twice: the second is left out."); continue; }
                if (!Goal.Known(goal.Kind)) { problems.Add("The " + what + " " + goal.Id + " is left out: its kind, " + goal.Kind + ", is not one the pack knows."); continue; }
                goal.Text ??= goal.Id;
                goal.Ages = Rules.ParseAges(goal.Ages);
                goal.Weights ??= new Dictionary<string, double>();
                goal.Match = (goal.Match ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToLowerInvariant()).ToList();
                goal.Milestones = (goal.Milestones ?? new List<int>()).OrderBy(x => x).ToList();
                foreach (var key in goal.Weights.Keys.Where(k => k != Rules.AnyAspiration && Aspiration(k) == null).ToList())
                    problems.Add("The " + what + " " + goal.Id + " names an aspiration there is none of: " + key + ".");
                if (goal.Skill != null && goal.Skill != "any" && Rules.SkillIndex(goal.Skill) < 0) { problems.Add("The " + what + " " + goal.Id + " is left out: there is no skill called " + goal.Skill + "."); continue; }
                if (goal.Need != null && Rules.NeedIndex(goal.Need) < 0) { problems.Add("The " + what + " " + goal.Id + " is left out: there is no need called " + goal.Need + "."); continue; }
                result.Add(goal);
            }
            return result;
        }
    }

    public class SlotCounts
    {
        /// <summary>How many wants and fears a Sim has at once, and how many wants they can lock.</summary>
        public int Wants { get; set; } = 4;
        public int Fears { get; set; } = 3;
        public int Locks { get; set; } = 1;
    }

    public class MeterRules
    {
        /// <summary>How far the meter moves for each point a want or fear is worth.</summary>
        public double PerPoint { get; set; } = 0.01;
        /// <summary>How far an hour in the best of moods lifts the meter (the worst lowers it as far).</summary>
        public double MoodPerHour { get; set; } = 0.8;
        /// <summary>Where a Sim met for the first time starts.</summary>
        public double Start { get; set; } = 0;
        /// <summary>The lowest the meter goes once a Sim's lifetime want is fulfilled.</summary>
        public double LifetimeFloor { get; set; } = 50;
        public FailureRules Failure { get; set; } = new FailureRules();
    }

    public class FailureRules
    {
        /// <summary>What a breakdown does to needs, when a Sim falls into aspiration failure.</summary>
        public Dictionary<string, int> Breakdown { get; set; } = new Dictionary<string, int>();
        /// <summary>Hours of failure after which a Sim pulls themselves together, and where the meter goes then. 0: never.</summary>
        public double RecoverHours { get; set; } = 24;
        public double RecoverTo { get; set; } = -40;
        /// <summary>How far above failure the meter must climb before another breakdown can happen.</summary>
        public double Margin { get; set; } = 10;
    }

    public class LevelDef
    {
        public string Name { get; set; }
        /// <summary>The meter's value from which this level starts (the meter runs from -100 to 100).</summary>
        public double From { get; set; }
        /// <summary>How far the meter falls each hour at this level, before mood.</summary>
        public double DecayPerHour { get; set; }
        /// <summary>How fast needs fall at this level: 1 as the game has them, 0.8 a fifth slower, 1.2 a fifth faster.</summary>
        public double NeedDecay { get; set; } = 1;
        /// <summary>The chance that a reward used at this level goes wrong, from 0 to 1.</summary>
        public double Backfire { get; set; }
    }

    public class RelationshipRules
    {
        /// <summary>A friend: both Sims' daily relationship with the other at least this. The game's own rule is 50.</summary>
        public int FriendDaily { get; set; } = 50;
        /// <summary>A best friend: a friend with a lifetime relationship at least this, or, where the game keeps none, a daily one at least BestDaily.</summary>
        public int BestLifetime { get; set; } = 70;
        public int BestDaily { get; set; } = 90;
        /// <summary>
        /// Where the game marks two Sims in love: which of a relationship's numbers, and which of its bits. Not yet
        /// checked against the game: the log says what it sees there, so this can be put right.
        /// </summary>
        public int LoveIndex { get; set; } = 1;
        public int LoveMask { get; set; } = 2;
    }

    public class AspirationDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string About { get; set; }
        /// <summary>For children only (Grow Up). Grown Sims choose from the others.</summary>
        public bool Child { get; set; }
        /// <summary>How well a Sim suits this aspiration, when chance chooses: personality, skills and circumstances, each weighed.</summary>
        public Dictionary<string, double> Fit { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>A want, a fear or a lifetime want, as balance.json gives it. What each kind means is in Goal.cs.</summary>
    public class GoalDef
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        /// <summary>What the player is shown. {skill}, {amount}, {count}, {total}, {threshold} and {career} are filled in.</summary>
        public string Text { get; set; }
        public int Points { get; set; }
        /// <summary>Points more for each level the Sim has, in the skill or career the goal is about: higher levels take longer.</summary>
        public int PerLevel { get; set; }
        /// <summary>How likely this is to be rolled for each aspiration. "Any" counts for every aspiration not named.</summary>
        public Dictionary<string, double> Weights { get; set; } = new Dictionary<string, double>();
        /// <summary>"adult", "child" or "any".</summary>
        public string Ages { get; set; } = "any";

        public string Skill { get; set; }
        public string Need { get; set; }
        public int Amount { get; set; }
        public double Fraction { get; set; }
        public int Count { get; set; }
        public int Total { get; set; }
        public int Threshold { get; set; }
        public List<int> Milestones { get; set; } = new List<int>();
        /// <summary>Words that an interaction's name contains (in the game's English).</summary>
        public List<string> Match { get; set; } = new List<string>();
        /// <summary>Who the interaction is with: "person", "household", "other" (a person not of the household), "object" or "any".</summary>
        public string With { get; set; } = "any";
        /// <summary>How it went: "accepted", "rejected" or "any".</summary>
        public string Outcome { get; set; } = "any";

        public double WeightFor(string aspiration)
        {
            if (Weights == null) return 0;
            foreach (var pair in Weights)
                if (string.Equals(pair.Key, aspiration, StringComparison.OrdinalIgnoreCase)) return Math.Max(0, pair.Value);
            return Weights.TryGetValue(Rules.AnyAspiration, out var any) ? Math.Max(0, any) : 0;
        }
    }

    public class RewardDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string About { get; set; }
        public int Cost { get; set; }
        /// <summary>Sim hours before the same Sim can use it again.</summary>
        public double Cooldown { get; set; }
        public string Ages { get; set; } = "any";
        /// <summary>Never goes wrong, whatever the Sim's level.</summary>
        public bool Safe { get; set; }
        public EffectDef Effect { get; set; }
        /// <summary>What happens instead when it goes wrong. None: nothing happens, and the points are spent.</summary>
        public EffectDef Backfire { get; set; }
    }

    /// <summary>What a reward does. What each kind means is in Effects.cs.</summary>
    public class EffectDef
    {
        public string Kind { get; set; }
        public List<string> Needs { get; set; } = new List<string>();
        public int? Set { get; set; }
        public int? Add { get; set; }
        public double Hours { get; set; }
        public double Times { get; set; }
        public int Points { get; set; }
        public int Amount { get; set; }
        public int Days { get; set; }
        public int Daily { get; set; }
        public int Lifetime { get; set; }
        /// <summary>What the player is told when it happens.</summary>
        public string Text { get; set; }
    }
}
