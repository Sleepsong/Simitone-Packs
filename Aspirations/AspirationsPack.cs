/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content.TS1;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Aspirations
{
    public enum NoticeKind { Want, Fear, Level, Reward, Info }

    /// <summary>
    /// Wants, Fears and Aspirations, after The Sims 2. Each Sim of the household has an aspiration, four wants and
    /// three fears rolled for it, and a meter from Aspiration Failure to Platinum. Wants fulfilled earn points to
    /// spend on rewards, which are effects rather than objects.
    ///
    /// The engine is not changed. The pack hangs on it in three places, each a Harmony patch:
    ///
    ///  1. A lot has ticked: every few ticks look at the household (Watcher.cs).
    ///  2. The game has loaded a neighbourhood: forget the Sims of the one before.
    ///  3. The game has saved: keep the Sims' aspirations with it.
    ///
    /// and more it can do without: a lot is left (so what was not saved is forgotten); the game lets needs fall
    /// (so they fall faster or slower with the Sim's level); the live panel shows the needs (so the meter can go
    /// beside them); F7 and a button in Options (to open the Aspiration window).
    ///
    /// Of Simitone's own pack calls it uses only OnLoad, the first there was, so it runs on any Simitone that can
    /// load a pack with code.
    /// </summary>
    public class AspirationsPack : ISimitonePack
    {
        public const string Command = "aspire";
        private const string ApartmentsPack = "sample.apartments";
        private const string FamilyTreePack = "sample.familytree";

        public static string Folder;

        public void OnLoad(Pack pack)
        {
            Watcher.PackId = pack.Id;
            Folder = pack.Folder;
            var problems = new List<string>();
            Watcher.Balance = Balance.Load(Folder, problems);
            foreach (var p in problems) Log.Write(p);
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
            PackManager.RegisterCommand(Command, Run);
            Log.Write("Wants, Fears and Aspirations loaded: " + Describe(Watcher.Balance) + ". In a lot: F7, Options > Aspiration, or \"" + Command + " help\" in the console.");
        }

        public static string Describe(Balance b) =>
            b.Aspirations.Count + " aspirations, " + b.Wants.Count + " wants, " + b.Fears.Count + " fears, " + b.Lifetime.Count + " lifetime wants, " + b.Rewards.Count + " rewards";

        // ---- the patches ----

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(AspirationsPack), name);
        private static HarmonyMethod Effect(string name) => new HarmonyMethod(typeof(GameEffects), name);

        private static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            return (parameters.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, parameters))
                ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1
            harmony.Patch(Find(typeof(VM), "Tick"), postfix: Mine(nameof(AfterTick)));
            // 2
            harmony.Patch(Find(typeof(TS1NeighborhoodProvider), "InitSpecific"), postfix: Mine(nameof(AfterNeighbourhoodLoad)));
            // 3
            harmony.Patch(Find(typeof(TS1GameScreen), "Save"), postfix: Mine(nameof(AfterSave)));

            // what the pack can do without. a version of Simitone one of these does not fit costs only that
            Optional(() => harmony.Patch(Find(typeof(TS1GameScreen), "ExitLot"), postfix: Mine(nameof(AfterExitLot))),
                "What happens on a lot that is left without saving is kept until Simitone is closed");
            Optional(() => harmony.Patch(Find(typeof(VMTS1MotiveDecay), "Tick"), prefix: Effect(nameof(GameEffects.BeforeDecay)), postfix: Effect(nameof(GameEffects.AfterDecay))),
                "Needs fall at the game's own pace at every level");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "SetSubpanel"), postfix: Mine(nameof(AfterSetSubpanel))),
                "The meter is not shown beside the needs (the window still is: F7)");
            Optional(() => harmony.Patch(Find(typeof(UILotControl), "Update"), postfix: Mine(nameof(AfterLotControlUpdate))),
                "The F7 key does nothing (the console still opens the window: " + Command + ")");
            Optional(() => harmony.Patch(Find(typeof(UIMainPanel), "AddPacksButton"), postfix: Mine(nameof(AfterPacksButton))),
                "There is no Aspiration button in Options (F7 still opens the window)");
        }

        private static void Optional(Action patch, string without)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                Log.Write(without + ": " + e.Message);
            }
        }

        private static void AfterTick(VM __instance) => Guard("watching the household", () => Watcher.AfterTick(__instance));
        private static void AfterNeighbourhoodLoad() => Watcher.Forget();
        private static void AfterExitLot() => Watcher.Forget();
        private static void AfterSave() => Guard("saving the aspirations", Watcher.Saved);

        /// <summary>The live panel has changed what it shows: the needs panel gets the meter, at its right.</summary>
        private static void AfterSetSubpanel(UIMainPanel __instance, UISubpanel sub)
        {
            if (!(sub is UIMotiveSubpanel)) return;
            Guard("adding the meter", () => UIAspirationMeter.AddTo(sub, __instance.Game));
        }

        private static void AfterLotControlUpdate(UpdateState state)
        {
            if (state?.NewKeys != null && state.NewKeys.Contains(Keys.F7)) Guard("opening the Aspiration window", () => UIAspirationWindow.Open());
        }

        /// <summary>Simitone has just put its Packs button in the Options panel. Put an Aspiration button after it, and after the other packs' buttons.</summary>
        private static void AfterPacksButton(UISubpanel parent)
        {
            Guard("adding the Aspiration button to Options", () =>
            {
                int index = 5 + (PackManager.IsActive(ApartmentsPack) ? 1 : 0) + (PackManager.IsActive(FamilyTreePack) ? 1 : 0);
                UIOptionsButton.Add(parent, index, "Aspiration", () => UIAspirationWindow.Open());
            });
        }

        /// <summary>Nothing this pack does may take the game down with it. Whatever goes wrong is written to the log, and the game carries on.</summary>
        public static bool Guard(string doing, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (++Failures <= MostFailuresLogged) Log.Write("Something went wrong while " + doing + ": " + e);
                if (Failures == MostFailuresLogged) Log.Write("(No more of these will be written down this session.)");
                return false;
            }
        }

        private const int MostFailuresLogged = 20;
        private static int Failures;

        // ---- telling the player ----

        public static TS1GameScreen Screen => GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;

        /// <summary>What has happened to a Sim, from Aspiration.Step: shown, logged, and where it does something to the game, done.</summary>
        public static void Tell(VM vm, VMAvatar avatar, SimRecord record, List<News> news)
        {
            if (news.Count == 0) return;
            var b = Watcher.Balance;
            var name = avatar.Name;
            var shown = Screen?.vm == vm;
            foreach (var n in news)
            {
                Log.Write(name + ": " + n + ". Meter " + record.Meter.ToString("0.0") + ", " + record.Points + " points.");
                switch (n.Kind)
                {
                    case NewsKind.Fulfilled:
                        Notice(name + ": " + n.Text + "  +" + Rules.Number(n.Points), NoticeKind.Want);
                        break;
                    case NewsKind.Realized:
                        Notice(name + "'s fear came true: " + n.Text + "  " + Rules.Number(n.Points), NoticeKind.Fear);
                        break;
                    case NewsKind.Lifetime:
                        if (shown) Say("Lifetime want fulfilled", name + " has fulfilled their lifetime want: " + n.Text + ".\n\n+" + Rules.Number(n.Points)
                            + " points, and their aspiration meter will never fall below " + Rules.Level(b.Meter.LifetimeFloor, b).Name + " again.");
                        break;
                    case NewsKind.Level:
                        Notice(name + " is now " + n.Text + ".", NoticeKind.Level);
                        break;
                    case NewsKind.Breakdown:
                        GameEffects.Breakdown(avatar, b);
                        if (shown) Say(b.Levels[0].Name, name + " has fallen into " + b.Levels[0].Name.ToLowerInvariant() + ", and is close to breaking down.\n\n"
                            + "Fulfil their wants to lift the meter. Rewards they use now are likely to go wrong."
                            + (b.Meter.Failure.RecoverHours > 0 ? " Left alone, they pull themselves together after " + Effects.Hours(b.Meter.Failure.RecoverHours) + "." : ""));
                        break;
                    case NewsKind.Recovered:
                        Notice(name + " has pulled themselves together (" + n.Text + ").", NoticeKind.Level);
                        break;
                    case NewsKind.WornOff:
                        Notice(name + ": " + n.Text + " has worn off.", NoticeKind.Info);
                        break;
                    case NewsKind.GrewUp:
                        Notice(name + " " + n.Text + ".", NoticeKind.Info);
                        break;
                }
            }
        }

        /// <summary>A line at the top of the screen that fades, for news that need not stop play.</summary>
        public static void Notice(string text, NoticeKind kind)
        {
            Log.Write(text);
            if (Watcher.Ledger?.Settings.Notices == false) return;
            GameThread.NextUpdate(x => Guard("showing a notice", () => UINotices.Show(Screen, text, kind)));
        }

        /// <summary>A message with an OK button; then, once it is closed, what follows, if anything.</summary>
        public static void Say(string title, string message, Action then = null)
        {
            if (string.IsNullOrEmpty(message)) { if (then != null) Guard("after a message", then); return; }
            Log.Write(title + ": " + message.Replace("\n", " | "));
            GameThread.NextUpdate(x =>
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) =>
                    {
                        UIScreen.RemoveDialog(alert);
                        if (then != null) Guard("after a message", then);
                    })
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }

        // ---- asking for an aspiration ----

        private static readonly List<(VM vm, VMAvatar avatar)> ToAsk = new List<(VM, VMAvatar)>();
        private static bool Asking;

        /// <summary>A grown Sim nobody has chosen an aspiration for: ask the player, one Sim at a time.</summary>
        public static void AskLater(VM vm, VMAvatar avatar)
        {
            var ledger = Watcher.Ledger;
            var record = Watcher.RecordOf(avatar);
            if (ledger == null || record == null) return;
            if (!ledger.Settings.Ask || !VM.UseWorld)
            {
                record.Asked = true;
                return;
            }
            if (Screen?.vm != vm || ToAsk.Any(x => x.avatar == avatar)) return;
            ToAsk.Add((vm, avatar));
            AskNext();
        }

        private static void AskNext()
        {
            if (Asking || ToAsk.Count == 0) return;
            var (vm, avatar) = ToAsk[0];
            ToAsk.RemoveAt(0);
            var record = Watcher.RecordOf(avatar);
            if (record == null || record.Asked || avatar.Dead || Screen?.vm != vm) { AskNext(); return; }
            Asking = true;
            record.Asked = true;
            GameThread.NextUpdate(x =>
            {
                var opened = Guard("asking for an aspiration", () => UIAspirationChooser.Open(avatar, true, () => { Asking = false; AskNext(); }));
                if (!opened) { Asking = false; AskNext(); }
            });
        }

        /// <summary>Give a Sim the aspiration chosen. Returns what to tell the player.</summary>
        public static string Choose(VMAvatar avatar, string aspiration)
        {
            var b = Watcher.Balance;
            var record = Watcher.RecordOf(avatar);
            var def = b.Aspiration(aspiration);
            if (record == null) return "There is no neighbourhood loaded.";
            if (def == null) return "There is no aspiration called " + aspiration + ". There are: " + string.Join(", ", b.Aspirations.Where(x => !x.Child).Select(x => x.Id)) + ".";
            if (def.Child != record.Child) return def.Child ? "Only children have that aspiration." : "Children have the " + (b.Aspirations.FirstOrDefault(x => x.Child)?.Name ?? "Grow Up") + " aspiration until they grow up.";
            Aspiration.Assign(record, def.Id, record.Child, true, b);
            Log.Write(avatar.Name + " chose the " + def.Name + " aspiration.");
            return avatar.Name + "'s aspiration is " + def.Name + ".";
        }

        // ---- using a reward ----

        /// <summary>Why the Sim cannot use the reward now, or null if they can.</summary>
        public static string CannotUse(VM vm, VMAvatar avatar, RewardDef reward, int skill = -1)
        {
            var record = Watcher.RecordOf(avatar);
            if (record == null) return "There is no neighbourhood loaded.";
            if (Watcher.Ledger?.Settings.On == false) return "Aspirations are switched off.";
            return Effects.CannotUse(reward, record, Watcher.StateOf(vm, avatar, Watcher.Household(vm)), Watcher.Balance, skill);
        }

        /// <summary>Use a reward now, if it can be. Returns what to tell the player.</summary>
        public static string Use(VM vm, VMAvatar avatar, RewardDef reward, int skill = -1)
        {
            var problem = CannotUse(vm, avatar, reward, skill);
            if (problem != null) return reward.Name + ": " + problem;
            var record = Watcher.RecordOf(avatar);
            var state = Watcher.StateOf(vm, avatar, Watcher.Household(vm));
            var result = Effects.Use(reward, record, state, Watcher.Balance, Watcher.Rng, skill);
            GameEffects.Apply(vm, avatar, result);
            Log.Write(avatar.Name + " used " + reward.Name + (result.Backfired ? ", which went wrong" : "") + ": " + result.Text + " " + record.Points + " points left.");
            return result.Text;
        }

        // ---- the console ----

        private const string Help =
            "aspire - everyone in your household: aspiration, level, meter and points\n" +
            "aspire wants [name] - a Sim's wants, fears, lifetime want and rewards in effect\n" +
            "aspire window - open the Aspiration window (also F7)\n" +
            "aspire choose [aspiration] [name] - give a Sim an aspiration\n" +
            "aspire lock [1-4] [name] - lock or unlock a want, so it is not rolled again\n" +
            "aspire rewards [name] - the rewards, and whether a Sim can use them\n" +
            "aspire use [reward] [skill] [name] - use a reward\n" +
            "aspire stats - points earned and lost per day, for balancing\n" +
            "aspire reload - read balance.json again\n" +
            "aspire on / off, aspire ask on|off, aspire notices on|off\n" +
            "Cheats: aspire points [number] [name], aspire meter [-100 to 100] [name], aspire reroll [name]\n" +
            "Without a name, a command is for the selected Sim.";

        private static bool Run(string[] words)
        {
            string answer = null;
            var understood = true;
            if (!Guard("running the aspire command", () => answer = Run(words, out understood))) answer = "That went wrong. Aspirations.log says how.";
            if (answer != null) Say("Aspirations", answer);
            return understood;
        }

        /// <summary>The Sim a command is for: the one named, or else the selected one.</summary>
        private static VMAvatar Who(VM vm, IEnumerable<string> nameWords, out string problem)
        {
            problem = null;
            var household = Watcher.Household(vm);
            var name = string.Join(" ", nameWords).Trim();
            if (name != "")
            {
                var exact = household.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                var starts = household.Where(x => x.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var found = exact.Count > 0 ? exact : starts;
                if (found.Count == 1) return found[0];
                problem = found.Count == 0
                    ? "Nobody of your household here is called " + name + "."
                    : "More than one Sim here is called that: " + string.Join(", ", found.Select(x => x.Name)) + ". Select the one you mean and leave the name out.";
                return null;
            }
            var selected = Screen?.SelectedAvatar;
            var who = household.FirstOrDefault(x => x == selected);
            if (who == null) problem = "Select a Sim of your household, or give a name.";
            return who;
        }

        public static string Summary(VMAvatar avatar, SimRecord r, Balance b)
        {
            var aspiration = b.Aspiration(r.Aspiration)?.Name ?? "no aspiration";
            return avatar.Name + ": " + aspiration + (r.Chosen ? "" : " (chance's choice)") + ", " + Rules.Level(r.Meter, b).Name + " (meter " + r.Meter.ToString("0") + "), " + Rules.Number(r.Points) + " points.";
        }

        public static string Details(SimRecord r, Balance b)
        {
            var lines = new List<string>();
            lines.Add("Wants: " + (r.Wants.Count == 0 ? "none yet" : string.Join("; ", r.Wants.Select((w, i) => (i + 1) + ". " + w.Text + " (+" + Rules.Number(w.Points) + (w.Locked ? ", locked" : "") + ")"))));
            lines.Add("Fears: " + (r.Fears.Count == 0 ? "none" : string.Join("; ", r.Fears.Select(f => f.Text + " (-" + Rules.Number(f.Points) + ")"))));
            if (r.LifetimeDone) lines.Add("Lifetime want: fulfilled" + (r.Lifetime != null ? " (" + r.Lifetime.Text + ")" : "") + ".");
            else if (r.Lifetime != null) lines.Add("Lifetime want: " + r.Lifetime.Text + " (+" + Rules.Number(r.Lifetime.Points) + ").");
            if (r.Effects.Count > 0) lines.Add("In effect: " + string.Join("; ", r.Effects.Select(e => e.Name + " (" + Effects.Remaining(e, r) + ")")) + ".");
            return string.Join("\n", lines);
        }

        /// <summary>Does what was typed after "aspire", and returns what to tell the player.</summary>
        public static string Run(string[] words, out bool understood)
        {
            understood = true;
            var ledger = Watcher.Ledger;
            if (ledger == null) return "There is no neighbourhood loaded.";
            var b = Watcher.Balance;
            var settings = ledger.Settings;
            var word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            var vm = Screen?.vm;
            const string NeedsLot = "This works while you are in a lot.";
            string problem;
            VMAvatar who;

            switch (word)
            {
                case "":
                case "list":
                {
                    if (vm == null) return NeedsLot;
                    var lines = Watcher.Household(vm).Select(x => Summary(x, Watcher.RecordOf(x), b)).ToList();
                    if (lines.Count == 0) lines.Add("Nobody of the household you play is on this lot.");
                    if (!settings.On) lines.Add("Aspirations are off. To switch them on: aspire on");
                    return string.Join("\n", lines);
                }
                case "help":
                    return Help;
                case "on":
                case "off":
                    settings.On = word == "on";
                    Watcher.SettingsChanged();
                    return settings.On ? "Aspirations are on." : "Aspirations are off: wants, fears and the meter stand still, and are remembered.";
                case "ask":
                case "notices":
                {
                    var on = words.Length > 1 ? words[1].ToLowerInvariant() : "";
                    if (on != "on" && on != "off") { understood = false; return null; }
                    if (word == "ask") settings.Ask = on == "on"; else settings.Notices = on == "on";
                    Watcher.SettingsChanged();
                    return word == "ask"
                        ? (settings.Ask ? "Grown Sims without an aspiration will be asked to choose one." : "Chance will choose aspirations; change them in the window.")
                        : (settings.Notices ? "Wants and fears will be shown at the top of the screen." : "Wants and fears will not be shown at the top of the screen.");
                }
                case "window":
                case "open":
                    UIAspirationWindow.Open();
                    return null;
                case "reload":
                {
                    var problems = new List<string>();
                    Watcher.Balance = Balance.Load(Folder, problems);
                    foreach (var p in problems) Log.Write(p);
                    foreach (var sim in ledger.Sims.Values) sim.Repair(Watcher.Balance);
                    return "Read " + Balance.FileName + ": " + Describe(Watcher.Balance) + "." + (problems.Count > 0 ? "\n" + string.Join("\n", problems.Take(6)) + (problems.Count > 6 ? "\n(and more: see Aspirations.log)" : "") : "");
                }
                case "stats":
                {
                    if (vm == null) return NeedsLot;
                    var lines = new List<string>();
                    foreach (var x in Watcher.Household(vm))
                    {
                        var r = Watcher.RecordOf(x);
                        var days = r.Minutes / 1440.0;
                        var per = days >= 0.25 ? ", " + Rules.Number((long)(r.Earned / days)) + " a day earned and " + Rules.Number((long)(r.Lost / days)) + " lost" : "";
                        lines.Add(x.Name + ": " + days.ToString("0.0") + " days played; " + r.Fulfilled + " wants fulfilled, " + r.Realized + " fears come true, "
                            + Rules.Number(r.Earned) + " points earned" + per + "; " + r.RewardsUsed + " rewards used; " + r.Breakdowns + " breakdowns.");
                    }
                    return lines.Count == 0 ? "Nobody of the household you play is on this lot." : string.Join("\n", lines);
                }
                case "wants":
                case "fears":
                    if (vm == null) return NeedsLot;
                    who = Who(vm, words.Skip(1), out problem);
                    if (who == null) return problem;
                    return Summary(who, Watcher.RecordOf(who), b) + "\n" + Details(Watcher.RecordOf(who), b);
                case "choose":
                {
                    if (vm == null) return NeedsLot;
                    if (words.Length < 2) return "Which aspiration? " + string.Join(", ", b.Aspirations.Where(x => !x.Child).Select(x => x.Id)) + ". For example: aspire choose Fortune";
                    who = Who(vm, words.Skip(2), out problem);
                    return who == null ? problem : Choose(who, words[1]);
                }
                case "lock":
                case "unlock":
                {
                    if (vm == null) return NeedsLot;
                    if (words.Length < 2 || !int.TryParse(words[1], out var n)) return "Which want? For the first: aspire lock 1";
                    who = Who(vm, words.Skip(2), out problem);
                    if (who == null) return problem;
                    var r = Watcher.RecordOf(who);
                    problem = Aspiration.Lock(r, n - 1, b);
                    return problem ?? (r.Wants[n - 1].Locked ? "Locked: " : "Unlocked: ") + r.Wants[n - 1].Text + ".";
                }
                case "rewards":
                {
                    if (vm == null) return NeedsLot;
                    who = Who(vm, words.Skip(1), out problem);
                    if (who == null) return problem;
                    var r = Watcher.RecordOf(who);
                    var risk = b.Rewards.Count > 0 ? Rules.Level(r.Meter, b).Backfire : 0;
                    var lines = b.Rewards.Select(x => x.Id + " - " + x.Name + ", " + Rules.Number(x.Cost) + ": " + x.About + " " + (CannotUse(vm, who, x) ?? "Can be used.")).ToList();
                    lines.Insert(0, who.Name + " has " + Rules.Number(r.Points) + " points." + (risk > 0 ? " At " + Rules.Level(r.Meter, b).Name + ", a reward has a " + (risk * 100).ToString("0") + "% chance of going wrong." : ""));
                    return string.Join("\n", lines);
                }
                case "use":
                {
                    if (vm == null) return NeedsLot;
                    if (words.Length < 2) return "Which reward? aspire rewards lists them.";
                    var reward = b.Reward(words[1]) ?? b.Rewards.FirstOrDefault(x => string.Equals(x.Name.Replace(" ", ""), words[1], StringComparison.OrdinalIgnoreCase));
                    if (reward == null) return "There is no reward called " + words[1] + ". aspire rewards lists them.";
                    var rest = words.Skip(2).ToList();
                    var skill = rest.Count > 0 ? Rules.SkillIndex(rest[0]) : -1;
                    if (skill >= 0) rest.RemoveAt(0);
                    who = Who(vm, rest, out problem);
                    return who == null ? problem : Use(vm, who, reward, skill);
                }
                case "points":
                case "meter":
                {
                    if (vm == null) return NeedsLot;
                    if (words.Length < 2 || !double.TryParse(words[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                        return word == "points" ? "How many? For example: aspire points 5000" : "Where? From -100 to 100, for example: aspire meter 90";
                    who = Who(vm, words.Skip(2), out problem);
                    if (who == null) return problem;
                    var r = Watcher.RecordOf(who);
                    if (word == "points") r.Points = Math.Max(0, r.Points + (int)value);
                    else r.Meter = Rules.Clamp(value);
                    return Summary(who, r, b);
                }
                case "reroll":
                {
                    if (vm == null) return NeedsLot;
                    who = Who(vm, words.Skip(1), out problem);
                    if (who == null) return problem;
                    var r = Watcher.RecordOf(who);
                    r.Wants.RemoveAll(x => !x.Locked);
                    r.Fears.Clear();
                    Aspiration.Fill(r, Watcher.StateOf(vm, who, Watcher.Household(vm)), b, Watcher.Rng);
                    return Details(r, b);
                }
                default:
                    understood = false;
                    return null;
            }
        }
    }
}
