/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Framework;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// Apartment buildings on the neighbourhood screen, where no lot is loaded: what a building's panel says, moving a
    /// family into one of its apartments, and choosing which of its households to play.
    ///
    /// The game's own steps are kept. Choosing a family and then a house still asks "move in?" and then calls the
    /// game's move-in; entering a house still calls the game's play. For a building, those two are met on the way in:
    /// the move-in asks which apartment (and charges nothing: an apartment is rented), and the play asks which
    /// household.
    /// </summary>
    public static class Hood
    {
        /// <summary>Set while the pack itself calls the game's play, so that it is not asked again.</summary>
        private static bool Chosen;

        private static string Plural(int count, string one, string many) => count + " " + (count == 1 ? one : many);

        // ---- the house's panel ----

        /// <summary>The panel for a house has just been made. If the house is an apartment building, say so, and let a family move in while an apartment is free.</summary>
        public static void DescribeHouse(UIHouseSelectPanel panel, int house)
        {
            var file = Residency.Load(house);
            if (!file.Building) return;
            var free = file.ToLet;
            var residents = Residency.Residents(house);
            var movingIn = Actions.Screen?.MoveInFamily != null;

            string text;
            if (!movingIn)
            {
                text = "Apartment building: " + Plural(residents.Count, "household", "households") + ", " + Plural(free.Count, "apartment", "apartments") + " free.";
            }
            else if (free.Count > 0)
            {
                var cheapest = free.Min(x => Units.DailyRent(x.Area, file.Rate));
                var budget = Content.Get().Neighborhood.GetFamily((ushort)Actions.Screen.MoveInFamily.Value)?.Budget ?? 0;
                var afford = budget >= cheapest * Units.DepositDays;
                text = "Apartment building: " + Plural(free.Count, "apartment", "apartments") + " free, from " + cheapest + " a day in rent, with " + Units.DepositDays + " days' rent to pay on moving in."
                    + (afford ? "" : " This family cannot afford the cheapest.");
                if (panel.EnterLot != null) panel.EnterLot.Disabled = !afford;
            }
            else
            {
                text = (file.Apartments.Count == 0) ? "Apartment building with no apartments laid out yet. Enter it and add some first."
                    : (file.Apartments.All(x => !x.Vacant) ? "Apartment building: every apartment is taken." : "Apartment building: its free apartments have no rooms yet.");
                if (panel.EnterLot != null) panel.EnterLot.Disabled = true;
            }
            if (panel.SecondaryText != null) panel.SecondaryText.Caption = text;
        }

        // ---- moving in ----

        /// <summary>
        /// The game is about to move a family into a house and play it. If the house is an apartment building, ask
        /// which apartment instead, and return true: the game's own move-in (which buys the house) is not wanted.
        /// </summary>
        public static bool MoveIn(TS1GameScreen screen, short house, int familyID, UIElement switcher)
        {
            var file = Residency.Load(house);
            if (!file.Building) return false;
            var family = Content.Get().Neighborhood.GetFamily((ushort)familyID);
            if (family == null) return false;
            var name = Residency.NameOf(family);
            var free = file.ToLet;
            if (free.Count == 0)
            {
                Actions.Say("Apartments", "No apartment in this building is free.");
                return true;
            }

            Action<Apartment> take = apartment =>
            {
                screen.MoveInFamily = null;
                var before = family.Budget;
                Residency.MoveIntoApartment(house, family, apartment.Id);
                Play(screen, house, switcher);
                //once they are in: what it cost, and what it will cost
                var rent = Units.DailyRent(apartment.Area, file.Rate);
                Actions.Say("Welcome to " + apartment.Name, name + " have moved in, and paid " + (before - family.Budget) + " to do so."
                    + (rent > 0 ? "\n\nThe rent is " + rent + " a day, taken as each day begins. If there is not enough money, the rest is owed until there is." : "")
                    + "\n\nThe other apartments are their neighbours' own: knock at a door to be asked in. The Apartments window is under Options, or F9.");
            };

            Func<Apartment, int> deposit = x => Units.Deposit(x.Area, file.Rate);
            Func<Apartment, string> line = x => x.Name + ": " + Actions.SavedSizeText(file, x) + ". To move in: " + deposit(x) + (family.Budget < deposit(x) ? " (they cannot afford it)" : "") + ".";
            var within = free.Where(x => family.Budget >= deposit(x)).ToList();
            var money = name + " have " + family.Budget + " simoleons. Moving in costs " + Units.DepositDays + " days' rent.";
            if (within.Count == 0)
            {
                Actions.Say("Apartments", string.Join("\n", free.Select(line)) + "\n\n" + money + " They cannot afford any of these.");
            }
            else if (free.Count == 1)
            {
                var only = free[0];
                Actions.Choose("Move into " + only.Name + "?", only.Name + " is the apartment that is free.\n" + line(only) + "\n\n" + money,
                    new[] { Actions.Choice("Move in", () => take(only)) });
            }
            else
            {
                Actions.Choose("Which apartment for " + name + "?", string.Join("\n", free.Select(line)) + "\n\n" + money,
                    within.Select(x => Actions.Choice(x.Name, () => take(x))));
            }
            return true;
        }

        // ---- playing ----

        /// <summary>
        /// The game is about to enter a house and play the family that lives there. If several households do, ask
        /// which, and return true. (Not when the pack has just asked.)
        /// </summary>
        public static bool ChooseHousehold(TS1GameScreen screen, short house, UIElement switcher)
        {
            if (Chosen) return false;
            var residents = Residency.Residents(house);
            if (residents.Count < 2) return false;
            var file = Residency.Load(house);
            var lines = residents.Select(x =>
            {
                var apartment = file.ApartmentOf(x.ChunkID);
                return Residency.NameOf(x) + ": " + Plural(x.FamilyGUIDs.Length, "Sim", "Sims") + ", " + (apartment == null ? "no apartment" : apartment.Name) + (x.ChunkID == file.Played ? " (played last)" : "");
            });
            Actions.Choose("Who do you want to play?", string.Join("\n", lines) + "\n\nThe others are at home, as neighbours.",
                residents.Select(x => Actions.Choice(Residency.NameOf(x), () =>
                {
                    Residency.SetPlayed(x);
                    Play(screen, house, switcher);
                })));
            return true;
        }

        private static void Play(TS1GameScreen screen, short house, UIElement switcher)
        {
            Chosen = true;
            try
            {
                screen.PlayHouse(house, switcher);
            }
            finally
            {
                Chosen = false;
            }
        }
    }
}
