/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Utils;
using FSO.Content;
using FSO.HIT;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CutContent
{
    /// <summary>
    /// Restores features that Maxis left unfinished or switched off in The Sims and its expansions.
    ///
    /// Nothing here was written with the game's files to hand, and the game's files are never changed. Each
    /// restoration looks for its feature in the player's own game when the game runs, and says what it found:
    ///
    ///   Interactions.cs  interactions that are still in an object's menu table but switched off
    ///   GhostSting.cs    the third "spooky" sting, which no sound event plays
    ///   Kiosk.cs         the Superstar Information Kiosk, which is missing from the catalog
    ///   Survey.cs        a report of everything in the game's files that belongs to a cut feature
    ///
    /// Console commands (Ctrl+Shift+C):
    ///   cutcontent              what was found and restored so far
    ///   cutcontent survey       write the report, for working out the restorations that are not done yet
    ///   cutcontent survey kiosk|pickpocket   a quick survey about the one thing (see FocusedSurveys.cs)
    ///   cutcontent survey old [FOLDER]   read an older copy of the game from its folder, or from the pack's folder (see OldSurvey.cs)
    ///   cutcontent skills [SKILL POINTS]   the household's gardening, music and literacy, or set one to try things out (see RestoredSkills.cs)
    ///   cutcontent pickpocket   try the pickpocket's textures on the selected Sim (see Pickpocket.cs)
    ///   cutcontent doors        the doors on this lot that can be locked, and whether they are
    ///   cutcontent locks everyone|household   who a locked door keeps out (until the game closes)
    ///   cutcontent force on|off also show matched interactions whose own check says no (experimental, until the game closes)
    /// </summary>
    public class CutContentPack : ISimitonePack
    {
        public static void Log(string msg) => Console.WriteLine("[cut] " + msg);

        /// <summary>The version this code is, kept the same as pack.json's.</summary>
        public const string Build = "0.9.4";

        /// <summary>Which version is running and from where, to tell an old copy of the pack from a new one.</summary>
        public static string Running = "Cut Content " + Build;

        public void OnLoad(Pack pack)
        {
            var listed = pack.Manifest?.Version;
            Running = $"Cut Content {Build}" + (listed != null && listed != Build ? $" (but its pack.json says {listed})" : "") + $", loaded from {pack.Folder}";
            Log(Running);
            Investments.PackId = pack.Id;
            RestoredSkills.PackId = pack.Id;
            OldSurvey.PackFolder = pack.Folder;
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                harmony.Patch(Find(typeof(VMEntity), "GetAction", new[] { typeof(int), typeof(VMEntity), typeof(VMContext), typeof(bool), typeof(short[]) }),
                    prefix: new HarmonyMethod(typeof(Interactions), nameof(Interactions.BeforeGetAction)));
                harmony.Patch(Find(typeof(HITVM), "PlaySoundEvent", new[] { typeof(string) }),
                    prefix: new HarmonyMethod(typeof(GhostSting), nameof(GhostSting.BeforeSoundEvent)));
                //an object takes its interaction table when it is created, and a tile of a larger object takes its leader's
                var setup = new HarmonyMethod(typeof(AddedInteractions), nameof(AddedInteractions.AfterSetup));
                harmony.Patch(AccessTools.Constructor(typeof(VMEntity), new[] { typeof(GameObject) })
                    ?? throw new Exception("VMEntity's constructor was not found: this pack was made for a different version of Simitone"), postfix: setup);
                harmony.Patch(Find(typeof(VMEntity), "UseTreeTableOf", new[] { typeof(GameObject) }), postfix: setup);
                //a door is locked: say who a lock keeps out, once
                harmony.Patch(Find(typeof(VMEntity), "SetAttribute", new[] { typeof(int), typeof(short) }),
                    postfix: new HarmonyMethod(typeof(DoorLocks), nameof(DoorLocks.AfterSetAttribute)));
                //an object's picture is set: a taxi sent as a limousine looks like one
                harmony.Patch(Find(typeof(VMGameObject), "RefreshGraphic", Type.EmptyTypes), postfix: new HarmonyMethod(typeof(Limousine), nameof(Limousine.AfterRefreshGraphic)));
                //the lot ticks: count down investments
                harmony.Patch(Find(typeof(VM), "Tick", Type.EmptyTypes), postfix: new HarmonyMethod(typeof(Investments), nameof(Investments.AfterTick)));
                //...and Sims learn gardening, music and literacy
                harmony.Patch(Find(typeof(VM), "Tick", Type.EmptyTypes), postfix: new HarmonyMethod(typeof(RestoredSkills), nameof(RestoredSkills.AfterTick)));
                //an object is looked up: the Information Kiosk, made in memory, is put back if its file was loaded again
                harmony.Patch(Find(typeof(FSO.Content.Interfaces.AbstractObjectProvider), "Get", new[] { typeof(ulong) }),
                    postfix: new HarmonyMethod(typeof(Kiosk), nameof(Kiosk.AfterGet)));
                //a Sim plans which doors to go through: leave out the ones locked against them
                harmony.Patch(Find(typeof(VMRoutingFrame), "DoRoomRoute", new[] { typeof(VMFindLocationResult) }),
                    prefix: new HarmonyMethod(typeof(DoorLocks), nameof(DoorLocks.BeforeRoomRoute)));
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
            //the skills panel gets a Cleaning bar. on its own: if Simitone's panel has changed, only the bar is lost
            try
            {
                harmony.Patch(AccessTools.Constructor(typeof(UIJobSubpanel), new[] { typeof(TS1GameScreen) }) ?? throw new Exception("the skills panel was not found"),
                    postfix: new HarmonyMethod(typeof(Cleaning), nameof(Cleaning.AfterPanel)));
                harmony.Patch(AccessTools.DeclaredMethod(typeof(UIJobSubpanel), "Update") ?? throw new Exception("the skills panel's Update was not found"),
                    postfix: new HarmonyMethod(typeof(Cleaning), nameof(Cleaning.AfterPanelUpdate)));
            }
            catch (Exception e) { Log("cleaning: no bar in the skills panel: " + e.Message); }
            try
            {
                harmony.Patch(AccessTools.Constructor(typeof(UIJobSubpanel), new[] { typeof(TS1GameScreen) }) ?? throw new Exception("the skills panel was not found"),
                    postfix: new HarmonyMethod(typeof(RestoredSkills), nameof(RestoredSkills.AfterPanel)));
                harmony.Patch(AccessTools.DeclaredMethod(typeof(UIJobSubpanel), "Update") ?? throw new Exception("the skills panel's Update was not found"),
                    postfix: new HarmonyMethod(typeof(RestoredSkills), nameof(RestoredSkills.AfterPanelUpdate)));
            }
            catch (Exception e) { Log("skills: no bars in the skills panel: " + e.Message); }
            PackManager.RegisterCommand("cutcontent", Command);
        }

        private static MethodInfo Find(Type type, string name, Type[] args)
        {
            return AccessTools.Method(type, name, args) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        public void OnContentReady()
        {
            //each on its own: one failing should not stop the other
            try { GhostSting.Find(); } catch (Exception e) { GhostSting.Status = "failed: " + e.Message; Log("ghost sting: " + e); }
            try { Kiosk.Restore(); } catch (Exception e) { Kiosk.Status = "failed: " + e.Message; Log("kiosk: " + e); }
            try { Scripts.RegisterPrimitive(Investments.PrimitiveName, new Investments.RecordPrimitive()); }
            catch (Exception e) { Investments.Status = "failed: " + e.Message; Log("investments: " + e); }
            try { Scripts.RegisterPrimitive(Limousine.PrimitiveName, new Limousine.MarkPrimitive()); }
            catch (Exception e) { Limousine.Status = "failed: " + e.Message; Log("limousine: " + e); }
            try { Scripts.RegisterPrimitive(Cleaning.PrimitiveName, new Cleaning.LearnPrimitive()); }
            catch (Exception e) { Cleaning.Status = "failed: " + e.Message; Log("cleaning: " + e); }
        }

        public void OnLotLoaded(VM vm, string lotPath)
        {
            Investments.OnLotLoaded(vm, lotPath);
            try { RestoredSkills.OnLotLoaded(vm, lotPath); } catch (Exception e) { RestoredSkills.Status = "failed to read: " + e.Message; Log("skills: " + e); }
        }

        public void OnLotSaved(VM vm, string lotPath)
        {
            Investments.OnLotSaved(vm, lotPath);
            try { RestoredSkills.OnLotSaved(vm, lotPath); } catch (Exception e) { Log("skills: could not save: " + e); }
        }

        private static bool Command(string[] args)
        {
            var arg = (args.Length > 0) ? args[0].ToLowerInvariant() : "";
            switch (arg)
            {
                case "":
                case "status":
                    Show("Cut Content", StatusText());
                    return true;
                case "survey":
                    if (args.Length >= 2 && args[1].ToLowerInvariant() == "old")
                    {
                        //an older copy of the game, read from its folder: "cutcontent survey old /path/to/The Sims", or nothing
                        //to read whatever is inside the pack's own folder
                        var typed = string.Join(" ", args.Skip(2));
                        if (OldSurvey.Find(typed, out var explained) == null)
                        {
                            Show("Cut Content", explained.TrimEnd() + "\nThe folder was not found.");
                            return true;
                        }
                        var oldPath = Path.Combine(FSOEnvironment.UserDir, "cutcontent-survey-old.txt");
                        File.WriteAllText(oldPath, OldSurvey.Run(typed));
                        Show("Cut Content", explained.TrimEnd() + "\n\nThe survey of the older copy was written to:\n" + Path.GetFullPath(oldPath));
                        return true;
                    }
                    if (args.Length >= 2 && (args[1].ToLowerInvariant() == "kiosk" || args[1].ToLowerInvariant() == "pickpocket"))
                    {
                        var which = args[1].ToLowerInvariant();
                        var focusedPath = Path.Combine(FSOEnvironment.UserDir, $"cutcontent-survey-{which}.txt");
                        File.WriteAllText(focusedPath, which == "kiosk" ? FocusedSurveys.Kiosk() : FocusedSurveys.Pickpocket(text => File.WriteAllText(focusedPath, text)));
                        Show("Cut Content", "The survey was written to:\n" + Path.GetFullPath(focusedPath));
                        return true;
                    }
                    var path = Path.Combine(FSOEnvironment.UserDir, "cutcontent-survey.txt");
                    File.WriteAllText(path, Survey.Run());
                    Show("Cut Content", "The survey was written to:\n" + Path.GetFullPath(path));
                    return true;
                case "pickpocket":
                    return Pickpocket.Command(args.Skip(1).ToArray());
                case "skills":
                case "skill":
                    {
                        var screen = GameFacade.Screens.CurrentUIScreen as TS1GameScreen;
                        return RestoredSkills.Command(args.Skip(1).ToArray(), screen?.SelectedAvatar, screen?.vm);
                    }
                case "doors":
                    Show("Door locks", DoorLocks.Report((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm));
                    return true;
                case "locks":
                    if (args.Length < 2) return false;
                    if (args[1].ToLowerInvariant() == "everyone") DoorLocks.Everyone = true;
                    else if (args[1].ToLowerInvariant() == "household") DoorLocks.Everyone = false;
                    else return false;
                    Show("Door locks", DoorLocks.Everyone ? "Locked doors now keep out everyone but the emergency services, the household as well." : "Locked doors now keep out everyone but the household, its pets and the emergency services.");
                    return true;
                case "force":
                    if (args.Length < 2 || args[1].ToLowerInvariant() == "on") Interactions.Force = true;
                    else if (args[1].ToLowerInvariant() == "off") Interactions.Force = false;
                    else return false;
                    return true;
                default:
                    return false;
            }
        }

        private static string StatusText()
        {
            var text = new StringBuilder();
            text.AppendLine(Running);
            text.AppendLine();
            text.AppendLine("Ghost sting: " + GhostSting.Status);
            text.AppendLine("Information Kiosk: " + Kiosk.Status);
            text.AppendLine();
            text.AppendLine("Interactions put back in menus (found as objects are created):");
            foreach (var rule in AddedInteractions.Rules) text.AppendLine(rule.Name + ": " + rule.Status);
            text.AppendLine("Locked doors: " + DoorLocks.Status);
            text.AppendLine("Investment calls: " + Investments.StatusText((GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm));
            text.AppendLine($"Limousine: {Limousine.Status}. This session: {Limousine.Hired} hired.");
            text.AppendLine($"Cleaning skill: {Cleaning.Status}. This session: {Cleaning.Lessons} times round.");
            text.AppendLine($"Gardening, music and literacy: {RestoredSkills.Status}. This session: {RestoredSkills.Minutes} minutes learned (\"cutcontent skills\" for more).");
            text.AppendLine();
            text.AppendLine("Switched off interactions (found as their objects are clicked on):");
            foreach (var rule in Interactions.Rules) text.AppendLine(rule.Name + ": " + rule.Status);
            if (Interactions.Force) text.AppendLine("(force is on)");
            text.AppendLine();
            text.AppendLine("Not done yet: the pickpocket.");
            return text.ToString();
        }

        public static void Show(string title, string message, VM onlyFor = null)
        {
            Log(message);
            GameThread.NextUpdate(x =>
            {
                if (onlyFor != null && (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.vm != onlyFor) return;
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                });
                UIScreen.GlobalShowDialog(alert, true);
            });
        }
    }
}
