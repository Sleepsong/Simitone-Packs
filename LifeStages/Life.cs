/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Components;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LifeStages
{
    /// <summary>
    /// Ageing in the game: the days counted, and a stage put on a Sim.
    ///
    /// Who ages: the people (not pets) of the household being played, while it is on its own lot. A day is lived at
    /// each midnight there, and a Sim who has lived a stage out moves to the next at six that evening. Neighbours,
    /// townies and households you are not playing stay as they are, as in The Sims 2.
    ///
    /// What a stage is to the game:
    ///   child  the game's own child.
    ///   teen   still a child to the game (age 15: school, a child's interactions), drawn larger.
    ///   adult  the game's own adult. Leaving childhood is the one real change: the Sim's own file is given an
    ///          adult's head, body and hands (Body.cs), as the game does itself when a Sim buys clothes.
    ///   elder  still an adult to the game (age 65), with grey hair (GreyHair.cs).
    ///
    /// The pack's own record (Ledger.cs) says which stage a Sim is in and how many days into it. What the game holds
    /// (the Sim's file, its person data on the lot and in the neighbourhood) can fall behind that, for example in a
    /// lot that was left without saving after a birthday. So whenever a lot starts, and every hour after, each Sim
    /// of the household is looked at and what the game holds is brought into line.
    /// </summary>
    public static class Life
    {
        public static string PackId = "sample.lifestages";

        // ---- the neighbourhood's record ----

        private static Ledger Current;
        private static string CurrentPath;

        public static Ledger Ledger
        {
            get
            {
                var path = Content.Get()?.Neighborhood?.UserPath;
                if (path == null) return null;
                if (Current == null || CurrentPath != path)
                {
                    Current = Ledger.Read(PackId, path);
                    CurrentPath = path;
                }
                return Current;
            }
        }

        /// <summary>The game has loaded a neighbourhood: what was in memory belongs to the one before.</summary>
        public static void NeighbourhoodLoaded()
        {
            Current = null;
            CurrentPath = null;
        }

        /// <summary>The game has saved: keep the record with it.</summary>
        public static void Saved()
        {
            if (Current != null && CurrentPath != null) Current.Write(PackId, CurrentPath);
        }

        // ---- who ----

        /// <summary>The people of the played household who are on this lot.</summary>
        public static List<VMAvatar> Household(VM vm)
        {
            var result = new List<VMAvatar>();
            if (vm == null || !vm.TS1) return result;
            var family = vm.TS1State?.CurrentFamily;
            if (family?.FamilyGUIDs == null) return result;
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                var avatar = entity as VMAvatar;
                if (avatar == null || avatar.Dead || avatar.IsPet) continue;
                if (avatar.GetPersonData(VMPersonDataVariable.IsGhost) != 0) continue;
                if (!family.FamilyGUIDs.Contains(avatar.Object.OBJ.GUID)) continue;
                result.Add(avatar);
            }
            return result;
        }

        /// <summary>Whether the played household is on its own lot. Days only count there.</summary>
        public static bool AtHome(VM vm)
        {
            var family = vm?.TS1State?.CurrentFamily;
            return family != null && family.HouseNumber != 0 && family.HouseNumber == vm.GetGlobalValue(10);
        }

        public static SimAge RecordOf(VMAvatar avatar)
        {
            return Ledger?.Meet(avatar.Object.OBJ.GUID, avatar.GetPersonData(VMPersonDataVariable.PersonsAge));
        }

        // ---- the clock ----

        private static readonly ConditionalWeakTable<VM, DayWatch> Watches = new ConditionalWeakTable<VM, DayWatch>();

        /// <summary>Called after every tick of every lot.</summary>
        public static void AfterTick(VM vm)
        {
            if (vm == null || !vm.TS1 || vm.TS1State?.CurrentFamily == null) return;
            var first = !Watches.TryGetValue(vm, out var watch);
            if (first)
            {
                watch = new DayWatch();
                Watches.Add(vm, watch);
            }
            var moment = watch.Feed(vm.Context.Clock.Hours);
            if (first)
            {
                Reconcile(vm);
                return;
            }
            if (moment == DayWatch.Moment.None) return;
            if (moment == DayWatch.Moment.Midnight) Midnight(vm);
            if (moment == DayWatch.Moment.Birthdays) Birthdays(vm);
            Reconcile(vm);
        }

        /// <summary>A lot has loaded: look at it afresh on its next tick, even if it was seen before.</summary>
        public static void LotLoaded(VM vm)
        {
            if (vm != null) Watches.Remove(vm);
        }

        public static void Midnight(VM vm)
        {
            var ledger = Ledger;
            if (ledger == null || !ledger.Settings.Ageing || !AtHome(vm)) return;
            foreach (var avatar in Household(vm)) Stages.AddDay(RecordOf(avatar), ledger.Settings);
        }

        /// <summary>Six in the evening: everyone who is due grows up. Returns what to tell the player.</summary>
        public static List<string> Birthdays(VM vm)
        {
            var news = new List<string>();
            var ledger = Ledger;
            if (ledger == null || !ledger.Settings.Ageing || !AtHome(vm)) return news;
            foreach (var avatar in Household(vm))
            {
                if (Stages.Due(RecordOf(avatar), ledger.Settings)) GrowUp(vm, avatar, news);
            }
            if (news.Count > 0) Announce?.Invoke(vm, string.Join("\n", news));
            return news;
        }

        /// <summary>Tells the player something happened on this lot. Set by the pack; nothing is told without it.</summary>
        public static Action<VM, string> Announce;

        // ---- putting a stage on a Sim ----

        /// <summary>Move a Sim to the next stage now. False, with the reason in news, if it cannot be done.</summary>
        public static bool GrowUp(VM vm, VMAvatar avatar, List<string> news)
        {
            var ledger = Ledger;
            var sim = RecordOf(avatar);
            var next = Stages.Next(sim.Stage, ledger.Settings);
            if (next == null)
            {
                news.Add(avatar.Name + " is an elder. Nothing comes after that yet.");
                return false;
            }
            if (Stages.IsChildToGame(sim.Stage) && !Stages.IsChildToGame(next.Value) && !GiveAdultBody(vm, avatar))
            {
                news.Add(avatar.Name + " could not grow up: the game has no adult head or body to give. The log says more.");
                return false;
            }
            Stages.Advance(sim, ledger.Settings);
            Fit(vm, avatar, sim, ledger.Settings);
            Log.Write(avatar.Name + " is now " + Article(sim.Stage) + ".");
            news.Add(avatar.Name + " is now " + Article(sim.Stage) + ".");
            return true;
        }

        private static string Article(Stage stage) => (stage == Stage.Adult || stage == Stage.Elder ? "an " : "a ") + Stages.Name(stage);

        /// <summary>Bring what the game holds for every Sim of the household into line with the record.</summary>
        public static void Reconcile(VM vm)
        {
            var ledger = Ledger;
            if (ledger == null) return;
            foreach (var avatar in Household(vm))
            {
                var sim = RecordOf(avatar);
                var body = BodyStrings(avatar);
                if (body == null) continue;
                var childBody = body.GetString(0) != "adult";
                if (childBody && !Stages.IsChildToGame(sim.Stage))
                {
                    //the record says grown, the Sim's file says child: the file was lost or put back. grow again
                    if (!GiveAdultBody(vm, avatar)) continue;
                }
                else if (!childBody && Stages.IsChildToGame(sim.Stage))
                {
                    //the other way round: the record is the one that was lost. a grown body is not taken back
                    sim.Stage = Stage.Adult;
                    sim.Days = 0;
                }
                Fit(vm, avatar, sim, ledger.Settings);
            }
        }

        /// <summary>Make the Sim's age, size and hair those of its stage. Changes nothing that is already right.</summary>
        public static void Fit(VM vm, VMAvatar avatar, SimAge sim, Settings settings)
        {
            var guid = avatar.Object.OBJ.GUID;
            var neighbourhood = Content.Get().Neighborhood;
            var neighbour = NeighbourOf(neighbourhood, guid);

            // age. an age the game already holds that suits the stage is left as it is: the game's own Sims are not
            // all 9 or 27, and a Sim the pack has not changed should not be written to
            var age = Stages.GameAge(sim.Stage);
            if (!Stages.AgeFits(sim.Stage, avatar.GetPersonData(VMPersonDataVariable.PersonsAge)))
            {
                avatar.SetPersonData(VMPersonDataVariable.PersonsAge, age);
                avatar.InitBodyData(vm.Context); //the walk is chosen by age
            }
            if (!Stages.AgeFits(sim.Stage, NeighbourData(neighbour, VMPersonDataVariable.PersonsAge) ?? age))
                SetNeighbourData(neighbour, VMPersonDataVariable.PersonsAge, age);
            var body = BodyStrings(avatar);
            if (body != null && body.Length > 13 && !(short.TryParse(body.GetString(13), out var written) && Stages.AgeFits(sim.Stage, written)))
            {
                body.SetString(13, age.ToString());
                neighbourhood.AvatarChanged(guid);
            }

            // size. the game leaves this at 0 for a Sim it has never resized, and 0 must not be written: it would
            // draw the Sim at no size at all. so a full-sized Sim is only written to if it was left at a teen's size
            var size = Stages.Size(sim.Stage);
            var teenSize = Stages.Size(Stage.Teen);
            var held = avatar.GetPersonData(VMPersonDataVariable.TS1ScalingSim);
            if (held != size && (size != 100 || held == teenSize)) avatar.SetPersonData(VMPersonDataVariable.TS1ScalingSim, size);
            var heldThere = NeighbourData(neighbour, VMPersonDataVariable.TS1ScalingSim) ?? size;
            if (heldThere != size && (size != 100 || heldThere == teenSize)) SetNeighbourData(neighbour, VMPersonDataVariable.TS1ScalingSim, size);
            //a lot that has just loaded holds the number but has not drawn with it yet
            if (VM.UseWorld && avatar.WorldUI is AvatarComponent drawn && (size != 100 || held == teenSize) && drawn.Scale != size / 100f) drawn.Scale = size / 100f;

            // hair
            if (VM.UseWorld && body != null && body.Length > 14)
            {
                var head = body.GetString(2) ?? "";
                var worn = avatar.HeadOutfit?.OftData?.TS1TextureID;
                var isGrey = worn != null && GreyHair.TryParse(worn, out _, out _);
                var wantGrey = sim.Stage == Stage.Elder && settings.GreyHair && head.Contains('=');
                if (wantGrey && !isGrey)
                    avatar.HeadOutfit = new VMOutfitReference(GreyHair.NameFor(head, Body.SkinColour(body.GetString(14), avatar.GetPersonData(VMPersonDataVariable.SkinColor))), true);
                else if (!wantGrey && isGrey)
                    avatar.HeadOutfit = new VMOutfitReference(body, true);
            }
        }

        /// <summary>
        /// Turn a child into an adult: write an adult's head, body and hands into the Sim's own file, and rebuild
        /// the Sim on the lot from it. The file itself is written when the game next saves, with everything else.
        /// </summary>
        public static bool GiveAdultBody(VM vm, VMAvatar avatar)
        {
            try
            {
                var content = Content.Get();
                var body = BodyStrings(avatar);
                if (body == null) throw new Exception("the Sim has no body strings");
                var female = avatar.GetPersonData(VMPersonDataVariable.Gender) == 1;
                var skin = Body.SkinColour(body.GetString(14), avatar.GetPersonData(VMPersonDataVariable.SkinColor));
                var type = female ? "fa" : "ma";

                var collections = content.BCFGlobal.CollectionsByName;
                if (!collections["c"].ClothesByAvatarType.TryGetValue(type, out var heads)) heads = new List<string>();
                var bodies = collections["b"].GeneralAvatarType(type);
                var textures = ((TS1AvatarTextureProvider)content.AvatarTextures).GetAllNames();

                var strings = Body.ChooseAdult(female, skin, heads, bodies, textures, new Random());
                if (strings == null) throw new Exception("no adult " + (female ? "female" : "male") + " head and body with a \"" + skin + "\" texture were found (" + heads.Count + " heads, " + bodies.Count + " bodies looked at)");

                foreach (var item in strings)
                {
                    if (item.Key < body.Length) body.SetString(item.Key, item.Value);
                }
                content.Neighborhood.AvatarChanged(avatar.Object.OBJ.GUID);

                // the Sim on the lot, rebuilt from its strings as the game builds a Sim it creates
                var name = avatar.Name;
                var scale = (avatar.WorldUI as AvatarComponent)?.Scale ?? 1f;
                avatar.SetAvatarType(body); //an adult's skeleton
                if (VM.UseWorld && avatar.WorldUI is AvatarComponent drawn)
                {
                    drawn.Avatar = avatar.Avatar;
                    drawn.Scale = scale;
                }
                avatar.SetAvatarBodyStrings(body, vm.Context); //head, clothes, hands, age
                avatar.Name = name;
                avatar.InitBodyData(vm.Context); //an adult's walk
                avatar.SetPersonData(VMPersonDataVariable.CurrentOutfit, 0); //everyday clothes: a child's others are gone
                return true;
            }
            catch (Exception e)
            {
                Log.Write(avatar.Name + " could not be given an adult body: " + e.Message);
                return false;
            }
        }

        // ---- the game's records ----

        public static STR BodyStrings(VMAvatar avatar)
        {
            return avatar.Object.Resource.Get<STR>(avatar.Object.OBJ.BodyStringID);
        }

        private static Neighbour NeighbourOf(TS1NeighborhoodProvider neighbourhood, uint guid)
        {
            var id = neighbourhood.GetNeighborIDForGUID(guid);
            return id == null ? null : neighbourhood.GetNeighborByID(id.Value);
        }

        private static short? NeighbourData(Neighbour neighbour, VMPersonDataVariable variable)
        {
            var data = neighbour?.PersonData;
            return data != null && (int)variable < data.Length ? data[(int)variable] : (short?)null;
        }

        /// <summary>The neighbourhood's copy of a Sim's person data: what a Sim arriving on another lot is given.</summary>
        private static void SetNeighbourData(Neighbour neighbour, VMPersonDataVariable variable, short value)
        {
            //older neighbourhoods keep fewer entries than there are
            var data = neighbour?.PersonData;
            if (data != null && (int)variable < data.Length) data[(int)variable] = value;
        }
    }
}
