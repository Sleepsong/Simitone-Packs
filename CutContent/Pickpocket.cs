/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CutContent
{
    /// <summary>
    /// The Vacation pickpocket, first stage: trying his textures on.
    ///
    /// Survey 6 found only his textures: a head with a five-o'clock shadow (cvacationmalgt_pickpocket, cvacationmalgt_pick),
    /// a skinny body (vacationmasknlgt_pickpocket) and beach, forest and lodge outfits for the regular body
    /// (vacationmafitlgt_pick{beach,forest,lodge}{a,b}). The burglar wears the skinny body B003MASkn_01 and the head
    /// C_Burglar, and the pickpocket was to resemble him, so those are the likely meshes. Whether a texture fits a mesh
    /// only shows in the game, so "cutcontent pickpocket" dresses the selected Sim in the combinations to look at.
    ///
    /// The change is only for looking: "cutcontent pickpocket off" changes the Sim back, and so does loading the lot again,
    /// as the game dresses each Sim from their own file when a lot loads.
    /// </summary>
    public static class Pickpocket
    {
        private const string Head = "C_Burglar,HEAD-HEAD=cvacationmalgt_pickpocket";
        private const string FitBody = "VacationMAfit_Salesclerk"; //the regular body Vacation's men wear

        private static readonly (string name, string body, string head)[] Looks =
        {
            ("the burglar's body and head, with the pickpocket's skin and face", "B003MASkn_01,BODY=vacationmasknlgt_pickpocket", Head),
            ("as 1, with the other face (cvacationmalgt_pick)", "B003MASkn_01,BODY=vacationmasknlgt_pickpocket", "C_Burglar,HEAD-HEAD=cvacationmalgt_pick"),
            ("beach outfit A", FitBody + ",BODY=vacationmafitlgt_pickbeacha", Head),
            ("beach outfit B", FitBody + ",BODY=vacationmafitlgt_pickbeachb", Head),
            ("forest outfit A", FitBody + ",BODY=vacationmafitlgt_pickforesta", Head),
            ("forest outfit B", FitBody + ",BODY=vacationmafitlgt_pickforestb", Head),
            ("lodge outfit A", FitBody + ",BODY=vacationmafitlgt_picklodgea", Head),
            ("lodge outfit B", FitBody + ",BODY=vacationmafitlgt_picklodgeb", Head),
        };

        private class Original { public VMOutfitReference Body, Head; }
        private static ConditionalWeakTable<VMAvatar, Original> Originals = new ConditionalWeakTable<VMAvatar, Original>();

        /// <summary>
        /// cutcontent pickpocket            the list of looks
        /// cutcontent pickpocket 1 to 8     dress the selected Sim in one
        /// cutcontent pickpocket body MESH TEXTURE   or   head MESH TEXTURE   try any other mesh
        /// cutcontent pickpocket off        change the Sim back
        /// </summary>
        public static bool Command(string[] args)
        {
            var sim = (GameFacade.Screens.CurrentUIScreen as TS1GameScreen)?.SelectedAvatar;
            if (args.Length == 0)
            {
                var list = new List<string> { "Dress the selected Sim in one of these to see how the pickpocket's textures fit. Change back with \"cutcontent pickpocket off\" (loading the lot again does too)." };
                for (int i = 0; i < Looks.Length; i++) list.Add($"{i + 1}: {Looks[i].name}");
                list.Add("Or try any mesh: \"cutcontent pickpocket body MESH TEXTURE\" or \"cutcontent pickpocket head MESH TEXTURE\".");
                CutContentPack.Show("Pickpocket", string.Join("\n", list));
                return true;
            }
            if (sim == null) { CutContentPack.Show("Pickpocket", "Select a Sim first."); return true; }

            var what = args[0].ToLowerInvariant();
            if (what == "off")
            {
                if (Originals.TryGetValue(sim, out var original))
                {
                    sim.BodyOutfit = original.Body;
                    sim.HeadOutfit = original.Head;
                    Originals.Remove(sim);
                }
                return true;
            }
            if ((what == "body" || what == "head") && args.Length == 3)
            {
                Remember(sim);
                var data = $"{args[1]},{(what == "body" ? "BODY" : "HEAD-HEAD")}={args[2]}";
                if (what == "body") sim.BodyOutfit = new VMOutfitReference(data, false);
                else sim.HeadOutfit = new VMOutfitReference(data, true);
                CutContentPack.Log("pickpocket: " + what + " " + data);
                return true;
            }
            if (int.TryParse(what, out var n) && n >= 1 && n <= Looks.Length)
            {
                Remember(sim);
                var look = Looks[n - 1];
                sim.BodyOutfit = new VMOutfitReference(look.body, false);
                sim.HeadOutfit = new VMOutfitReference(look.head, true);
                CutContentPack.Log($"pickpocket: look {n}, {look.name}: {look.body} | {look.head}");
                return true;
            }
            return false;
        }

        private static void Remember(VMAvatar sim)
        {
            if (!Originals.TryGetValue(sim, out _)) Originals.Add(sim, new Original { Body = sim.BodyOutfit, Head = sim.HeadOutfit });
        }
    }
}
