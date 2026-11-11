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
using FSO.HIT;
using FSO.SimAntics;
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels;
using System;
using System.IO;
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
    ///   cutcontent force on|off also show matched interactions whose own check says no (experimental, until the game closes)
    /// </summary>
    public class CutContentPack : ISimitonePack
    {
        public static void Log(string msg) => Console.WriteLine("[cut] " + msg);

        public void OnLoad(Pack pack)
        {
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                harmony.Patch(Find(typeof(VMEntity), "GetAction", new[] { typeof(int), typeof(VMEntity), typeof(VMContext), typeof(bool), typeof(short[]) }),
                    prefix: new HarmonyMethod(typeof(Interactions), nameof(Interactions.BeforeGetAction)));
                harmony.Patch(Find(typeof(HITVM), "PlaySoundEvent", new[] { typeof(string) }),
                    prefix: new HarmonyMethod(typeof(GhostSting), nameof(GhostSting.BeforeSoundEvent)));
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing
                throw;
            }
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
                    var path = Path.Combine(FSOEnvironment.UserDir, "cutcontent-survey.txt");
                    File.WriteAllText(path, Survey.Run());
                    Show("Cut Content", "The survey was written to:\n" + Path.GetFullPath(path));
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
            text.AppendLine("Ghost sting: " + GhostSting.Status);
            text.AppendLine("Information Kiosk: " + Kiosk.Status);
            text.AppendLine();
            text.AppendLine("Switched off interactions (found as their objects are clicked on):");
            foreach (var rule in Interactions.Rules) text.AppendLine(rule.Name + ": " + rule.Status);
            if (Interactions.Force) text.AppendLine("(force is on)");
            text.AppendLine();
            text.AppendLine("Not started: the cut skills, the Vacation pickpocket.");
            return text.ToString();
        }

        private static void Show(string title, string message)
        {
            Log(message);
            GameThread.NextUpdate(x =>
            {
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
