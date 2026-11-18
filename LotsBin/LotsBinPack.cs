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
using HarmonyLib;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LotsBin
{
    /// <summary>
    /// The Lots Bin, after The Sims 2's: save a copy of a lot, and put it down as another lot.
    ///
    /// Both are on the neighbourhood screen, in a lot's panel under More, where Simitone has buttons it has not given
    /// a job yet: Export becomes "Save to Bin", and on a lot nobody lives on, Bulldoze becomes "Place from Bin". One
    /// Harmony patch puts them there (a postfix on UIHouseSelectPanel's constructor); everything else is LotCopy (the
    /// copying) and Bin (the folder the copies are kept in). If Simitone ever gives those buttons jobs of its own, the
    /// pack leaves them alone and says so in its log (LotsBin.log, in the user directory): it then needs a new version.
    ///
    /// What packs keep for a lot goes with the copy (PackData): painted ground, drawn roofs, foundations, styles,
    /// apartments. A pack decides what of its own goes with an OnLotExport method, which this pack looks for by name.
    ///
    /// All of it is in this pack. It needs nothing from Simitone that its add-on pack builds have not had since packs
    /// could keep data for a lot (LotPackData).
    /// </summary>
    public class LotsBinPack : ISimitonePack
    {
        public const string PackId = "sample.lotsbin";
        private const string HarmonyId = "simitone.packs.lotsbin";

        public void OnLoad(Pack pack)
        {
            Apply(new Harmony(HarmonyId));
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(LotsBinPack), name);

        private static void Apply(Harmony harmony)
        {
            var ctor = AccessTools.Constructor(typeof(UIHouseSelectPanel), new[] { typeof(int) })
                ?? throw new Exception("UIHouseSelectPanel(int) was not found: this pack was made for a different version of Simitone");
            harmony.Patch(ctor, postfix: Mine(nameof(AfterHousePanel)));
        }

        // ---- the log ----

        private static string LogPath => Path.Combine(FSOEnvironment.UserDir, "LotsBin.log");

        public static void Log(string text)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text;
            Console.WriteLine("Lots Bin: " + text);
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        /// <summary>Do something for the player. What goes wrong is said to them and written down; it never takes the game down.</summary>
        public static void Guard(string doing, Action action)
        {
            try
            {
                action();
            }
            catch (BinException e)
            {
                Log("Could not finish " + doing + ": " + e.Message);
                Tell("Lots Bin", e.Message);
            }
            catch (Exception e)
            {
                Log("Something went wrong while " + doing + ": " + e);
                Tell("Lots Bin", "Something went wrong while " + doing + ". Nothing was changed if the bin could help it; what happened is in LotsBin.log.");
            }
        }

        public static void Tell(string title, string message)
        {
            try
            {
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = title,
                    Message = message,
                    Buttons = UIAlertButton.Ok((b) => alert.Close())
                });
                UIScreen.GlobalShowDialog(alert, true);
            }
            catch (Exception e)
            {
                Console.WriteLine("Lots Bin could not show a message: " + e.Message);
            }
        }

        public static void Ask(string title, string message, Action yes)
        {
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = title,
                Message = message,
                Buttons = UIAlertButton.YesNo((b) => { alert.Close(); Guard(title.ToLowerInvariant(), yes); }, (b) => alert.Close())
            });
            UIScreen.GlobalShowDialog(alert, true);
        }

        // ---- the house panel ----

        public const string SaveCaption = "Save to Bin";
        public const string PlaceCaption = "Place from Bin";

        /// <summary>
        /// A lot's panel on the neighbourhood screen has been made. Its More buttons include ones Simitone has not given a
        /// job yet: give two of them the bin's.
        /// </summary>
        public static void AfterHousePanel(UIHouseSelectPanel __instance, int houseID)
        {
            try
            {
                var screen = UIScreen.Current as TS1GameScreen;
                if (screen == null || screen.MoveInFamily != null) return; //moving a family in: the panel has no More
                var house = (short)houseID;

                var export = __instance.OptionButtons.FirstOrDefault(x => x.Caption == "Export");
                if (export != null && export.Disabled)
                {
                    export.Caption = SaveCaption;
                    export.Disabled = false;
                    export.OnButtonClick += (b) => Guard("saving the lot to the bin", () => AskName(house));
                }
                else Log("The house panel's Export button is not there, or has a job of its own: there is no Save to Bin.");

                var neigh = Content.Get().Neighborhood;
                if (LotCopy.LivingIn(neigh, house).Count > 0) return; //only a lot nobody lives on can be replaced
                var bulldoze = __instance.OptionButtons.FirstOrDefault(x => x.Caption == "Bulldoze");
                if (bulldoze != null && bulldoze.Disabled)
                {
                    bulldoze.Caption = PlaceCaption;
                    bulldoze.Disabled = false;
                    bulldoze.OnButtonClick += (b) => Guard("opening the bin", () => UIBinDialog.Open(house, __instance));
                }
                else Log("The house panel's Bulldoze button is not there, or has a job of its own: there is no Place from Bin.");
            }
            catch (Exception e)
            {
                Log("Could not add the bin's buttons to the house panel: " + e);
            }
        }

        /// <summary>Ask what to call the copy (the lot's own name, to start with), then make it.</summary>
        private static void AskName(short house)
        {
            var neigh = Content.Get().Neighborhood;
            var name = neigh.GetHouseNameDesc(house).Item1;
            if (string.IsNullOrWhiteSpace(name)) name = "House " + house;
            var lived = LotCopy.LivingIn(neigh, house).Count > 0;

            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = SaveCaption,
                Message = "A copy of this lot, as it was last saved, goes in the bin" + (lived ? ", without the Sims who live here" : "")
                    + ". It can be put down on any lot of the same size that nobody lives on.\n\nWhat should the copy be called?",
                TextEntry = true,
                MaxChars = 40,
                Buttons = UIAlertButton.OkCancel((b) =>
                {
                    var typed = alert.ResponseText;
                    alert.Close();
                    Guard("saving the lot to the bin", () => SaveNow(house, string.IsNullOrWhiteSpace(typed) ? name : typed));
                }, (b) => alert.Close())
            });
            alert.ResponseText = name;
            UIScreen.GlobalShowDialog(alert, true);
        }

        private static void SaveNow(short house, string name)
        {
            var lot = LotCopy.Save(house, name);
            var missing = PackData.Missing(lot.Info.SavedWith);
            Tell(SaveCaption, "\"" + lot.Name + "\" is in the bin." + (missing.Count == 0 ? "" :
                "\n\nIt was saved with packs that are switched off now (" + string.Join(", ", missing) + "). What they added goes with the copy, "
                + "and works again once they are on."));
        }

        /// <summary>A lot was put down from the bin: show it on the neighbourhood screen, and close its panel, which shows what was there before.</summary>
        public static void Placed(short house, UIHouseSelectPanel panel)
        {
            var screen = UIScreen.Current as TS1GameScreen;
            try
            {
                RefreshHouseButton(screen?.TS1NeighPanel, house);
            }
            catch (Exception e)
            {
                Log("The neighbourhood screen will show the new lot the next time it is opened (" + e.Message + ").");
            }
            if (screen?.TS1NeighPanel?.LastHS == panel) screen.TS1NeighPanel.ResetZoom();
        }

        /// <summary>
        /// The neighbourhood screen made its house buttons, with each lot's pictures, when it opened. Give this lot's button
        /// the pictures of the lot now there, from a button made the same way.
        /// </summary>
        private static void RefreshHouseButton(UINeighborhoodSelectionPanel panel, short house)
        {
            if (panel == null || !panel.HousePositions.TryGetValue(house, out var position)) return;
            var old = panel.GetChildren().OfType<UINeighborhoodHouseButton>().FirstOrDefault(x => x.Position == position);
            if (old == null) return;
            var scale = (float)AccessTools.Field(typeof(UINeighborhoodHouseButton), "HouseScale").GetValue(old);
            var fresh = new UINeighborhoodHouseButton(house, (h) => { }, scale);
            foreach (var field in new[] { "HouseTex", "HouseOpenTex", "Offsets" })
            {
                var info = AccessTools.Field(typeof(UINeighborhoodHouseButton), field) ?? throw new Exception("the house button has no " + field);
                info.SetValue(old, info.GetValue(fresh));
            }
        }
    }
}
