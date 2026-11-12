/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTree
{
    /// <summary>
    /// A Sim's family tree, as in The Sims 2, a generation to a row: grandparents at the top, then parents, the Sim's
    /// own generation, children and grandchildren. Each relative is a button that moves the tree to them.
    /// </summary>
    public class UIFamilyTreeDialog : UIRowsDialog
    {
        /// <summary>The rows, from the oldest generation down, with what each is called.</summary>
        private static readonly (Tie, string)[] Generations =
        {
            (Tie.Grandparent, "Grandparents"),
            (Tie.Parent, "Parents"),
            (Tie.StepParent, "Step-parents"),
            (Tie.ParentInLaw, "Parents-in-law"),
            (Tie.AuntUncle, "Aunts and uncles"),
            (Tie.Spouse, "Married to"),
            (Tie.Sibling, "Brothers and sisters"),
            (Tie.Cousin, "Cousins"),
            (Tie.Child, "Children"),
            (Tie.StepChild, "Step-children"),
            (Tie.ChildInLaw, "Children-in-law"),
            (Tie.NieceNephew, "Nieces and nephews"),
            (Tie.Grandchild, "Grandchildren"),
        };

        private static UIFamilyTreeDialog Current;

        private uint Centre;

        protected override int RowHeight => 105;

        /// <summary>The Sim being played, if a lot is on screen.</summary>
        public static uint? PlayedSim()
        {
            try
            {
                var screen = GameFacade.Screens.CurrentUIScreen as TS1GameScreen;
                if (screen?.vm == null) return null;
                var guid = screen.SelectedAvatar?.Object?.OBJ?.GUID;
                if (guid != null && People.Exists(guid.Value)) return guid;
                //nobody is selected: anyone of the household being played
                var family = screen.vm.TS1State?.CurrentFamily;
                if (family?.FamilyGUIDs != null && family.FamilyGUIDs.Length > 0) return family.FamilyGUIDs[0];
            }
            catch (Exception e)
            {
                People.Log("Could not tell which Sim is being played: " + e.Message);
            }
            return null;
        }

        /// <summary>Open the tree on a Sim (the one being played, if none is given), or move the open one to them.</summary>
        public static void Open(uint? sim = null)
        {
            sim = sim ?? PlayedSim();
            if (sim == null)
            {
                Say("Family Tree", "Open this while you are playing a household.");
                return;
            }
            if (Current != null)
            {
                Current.Show(sim.Value);
                return;
            }
            Current = new UIFamilyTreeDialog(sim.Value);
            UIScreen.GlobalShowDialog(Current, true);
        }

        public static void Say(string title, string message)
        {
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

        private UIFamilyTreeDialog(uint sim) : base()
        {
            Centre = sim;
            Caption = "Family Tree";
            Refresh();
        }

        protected override void OnClosed()
        {
            if (Current == this) Current = null;
        }

        private void Show(uint sim)
        {
            Centre = sim;
            Page = 0;
            Message = null;
            Refresh();
        }

        /// <summary>Everyone in the tree, with what they are to the Sim in the middle. For the window, and for checking it.</summary>
        public static List<(Tie, List<uint>)> Relatives(Kin kin, uint centre)
        {
            var everyone = kin.Links.SelectMany(l => new[] { l.A, l.B }).Distinct().Where(x => x != centre).ToList();
            var ties = everyone.ToDictionary(x => x, x => kin.Between(centre, x));
            var result = new List<(Tie, List<uint>)>();
            foreach (var generation in Generations)
            {
                var members = everyone.Where(x => ties[x] == generation.Item1).ToList();
                if (members.Count > 0) result.Add((generation.Item1, members));
            }
            return result;
        }

        private static string ButtonName(uint sim)
        {
            var name = People.FirstName(sim);
            return name.Length > 11 ? name.Substring(0, 10) + "." : name;
        }

        protected override List<Row> Rows()
        {
            var rows = new List<Row>();
            var kin = KinStore.Get();
            //a Sim who has left the neighbourhood keeps their place in the file, but is not shown
            var relatives = Relatives(kin, Centre)
                .Select(group => (group.Item1, group.Item2.Where(People.Exists).ToList()))
                .Where(group => group.Item2.Count > 0).ToList();

            var household = People.FamilyName(People.FamilyOf(Centre));
            var about = (People.IsAdult(Centre) ? "An adult" : "A child") + (household == null ? "." : " of the " + household + " household.");
            var played = PlayedSim();
            var top = new Row(People.FullName(Centre), relatives.Count == 0
                ? about + " No family is known for them. Family ties are chosen when a family is made, and with Change ties here."
                : about);
            if (played != null && played.Value != Centre) top.Button("Back to " + ButtonName(played.Value), () => Show(played.Value), false, false, 330);
            rows.Add(top);

            foreach (var (tie, members) in relatives)
            {
                var title = Generations.First(g => g.Item1 == tie).Item2;
                var detail = string.Join(", ", members.Select(m => People.FullName(m) + " (" + TieNames.For(tie, People.IsFemale(m)).ToLowerInvariant() + ")"));
                var row = new Row(title, detail);
                foreach (var member in members)
                {
                    var target = member;
                    row.Button(ButtonName(member), () => Show(target), tie == Tie.Spouse);
                }
                rows.Add(row);
            }
            return rows;
        }

        protected override List<(string, Action)> BottomButtons()
        {
            return new List<(string, Action)>() { ("Change ties", ChangeTies) };
        }

        /// <summary>Who the Sim in the middle could have a tie with: their household, the family they already have, and everyone they have met.</summary>
        public static List<uint> Candidates(Kin kin, uint centre)
        {
            var result = new List<uint>();
            void Add(IEnumerable<uint> sims)
            {
                foreach (var sim in sims)
                {
                    if (sim != centre && !result.Contains(sim) && People.IsPerson(sim)) result.Add(sim);
                }
            }
            Add(People.FamilyOf(centre)?.FamilyGUIDs ?? new uint[0]);
            Add(kin.Links.Where(l => l.Touches(centre)).Select(l => l.A == centre ? l.B : l.A));
            Add(People.Met(centre));
            return result;
        }

        private void ChangeTies()
        {
            var kin = KinStore.Get();
            var centre = Centre;
            UIFamilyTiesDialog.Open(new TiesSubject()
            {
                Caption = "Family of " + People.FirstName(centre),
                Kin = kin,
                Focus = centre,
                Sims = new[] { centre }.Concat(Candidates(kin, centre)).ToList(),
                Name = People.FullName,
                IsAdult = People.IsAdult,
                IsFemale = People.IsFemale,
                Changed = () => { if (!KinStore.Save()) People.Log("The family tree could not be saved."); },
                Empty = People.FirstName(centre) + " lives alone and has met nobody yet. Sims they meet are listed here."
            }, () =>
            {
                //the tree may have changed under this window
                if (Current == this) Refresh();
            });
        }
    }
}
