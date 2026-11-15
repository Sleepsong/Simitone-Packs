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
    /// each midnight there, and a Sim who has lived a stage out moves to the next that evening, from six. Neighbours,
    /// townies and households you are not playing stay as they are, as in The Sims 2.
    ///
    /// What a stage is to the game:
    ///   child  the game's own child.
    ///   teen   still a child to the game (age 15: school, a child's interactions), drawn larger.
    ///   adult  the game's own adult. Leaving childhood is the one real change: the Sim's own file is given an
    ///          adult's head, body and hands (Body.cs), as the game does itself when a Sim buys clothes. The
    ///          player then chooses the head (UIHeadPicker.cs); the one chance gave is offered first.
    ///   elder  still an adult to the game (age 65). The player chooses an elder's head as they become one
    ///          (UIHeadPicker.cs), from the heads looks.json names for elders (Looks.cs).
    ///
    /// The pack's own record (Ledger.cs) says which stage a Sim is in and how many days into it. What the game holds
    /// (the Sim's file, its person data on the lot and in the neighbourhood) can fall behind that, for example in a
    /// lot that was left without saving after a birthday. So every few seconds each Sim of the household is looked
    /// at and what the game holds is brought into line (Reconcile).
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
                var path = NeighbourhoodPath();
                if (path == null) return null;
                if (Current == null || CurrentPath != path)
                {
                    Current = Ledger.Read(PackId, path);
                    CurrentPath = path;
                }
                return Current;
            }
        }

        /// <summary>
        /// Drop what is in memory, so that it is read from the file again. For when the game loads a neighbourhood
        /// (what was in memory belongs to the one before), and when a lot is left: if the game was saved first the
        /// file is the same, and if it was not, the days played and not saved go with the lot.
        /// </summary>
        public static void Forget()
        {
            Current = null;
            CurrentPath = null;
        }

        /// <summary>The game has saved: keep the record with it.</summary>
        public static void Saved()
        {
            if (Current != null && CurrentPath != null) Current.Write(PackId, CurrentPath);
        }

        /// <summary>Where the loaded neighbourhood is kept. The tests put another in its place.</summary>
        public static Func<string> NeighbourhoodPath = () => Content.Get()?.Neighborhood?.UserPath;

        /// <summary>
        /// Sims have just been made, in Create a Sim, each at a stage: keep them in the record on its first day. The
        /// game saves a new family at once, so the record is written at once too, without unsaved days with it.
        /// </summary>
        public static void Born(IEnumerable<(uint Guid, Stage Stage)> sims)
        {
            var path = NeighbourhoodPath();
            if (path == null) return;
            var list = sims.ToList();
            if (list.Count == 0) return;
            var saved = Ledger.Read(PackId, path);
            foreach (var sim in list) saved.Sims[Ledger.Key(sim.Guid)] = new SimAge() { Stage = sim.Stage, Days = 0 };
            saved.Write(PackId, path);
            if (Current != null && CurrentPath == path)
                foreach (var sim in list) Current.Sims[Ledger.Key(sim.Guid)] = new SimAge() { Stage = sim.Stage, Days = 0 };
            Log.Write("Made in Create a Sim: " + string.Join(", ", list.Select(x => Stages.Name(x.Stage))) + ".");
        }

        /// <summary>The player changed a setting: keep it now, whether or not the game is saved.</summary>
        public static void SettingsChanged()
        {
            if (Current != null && CurrentPath != null) Current.WriteSettings(PackId, CurrentPath);
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

        private class LotWatch
        {
            public DayWatch Clock = new DayWatch();
            public int Ticks;
        }

        private static readonly ConditionalWeakTable<VM, LotWatch> Watches = new ConditionalWeakTable<VM, LotWatch>();

        /// <summary>How often a lot's Sims are looked at, in ticks: a few seconds. A Sim who arrives is not left long at the wrong size.</summary>
        public const int LookEvery = 90;

        /// <summary>Called after every tick of every lot.</summary>
        public static void AfterTick(VM vm)
        {
            if (vm == null || !vm.TS1 || vm.TS1State?.CurrentFamily == null) return;
            var watch = Watches.GetValue(vm, x => new LotWatch());
            var moment = watch.Clock.Feed(vm.Context.Clock.Hours);
            if (moment == DayWatch.Moment.Midnight) Midnight(vm);
            if (moment == DayWatch.Moment.Evening) Birthdays(vm);
            if (moment != DayWatch.Moment.None || watch.Ticks++ % LookEvery == 0) Reconcile(vm);
        }

        public static void Midnight(VM vm)
        {
            var ledger = Ledger;
            if (ledger == null || !ledger.Settings.Ageing || !AtHome(vm)) return;
            foreach (var avatar in Household(vm)) Stages.AddDay(RecordOf(avatar), ledger.Settings);
        }

        /// <summary>
        /// Sims the game had no adult body for. They are not tried again by themselves, so the player is told once
        /// and not every evening. Asking from the console tries again.
        /// </summary>
        private static readonly HashSet<uint> CouldNotGrow = new HashSet<uint>();

        /// <summary>An hour of the evening: everyone who is due grows up. Returns what to tell the player.</summary>
        public static List<string> Birthdays(VM vm)
        {
            var news = new List<string>();
            var ledger = Ledger;
            if (ledger == null || !ledger.Settings.Ageing || !AtHome(vm)) return news;
            var choosers = new List<VMAvatar>();
            foreach (var avatar in Household(vm))
            {
                if (CouldNotGrow.Contains(avatar.Object.OBJ.GUID)) continue;
                if (Stages.Due(RecordOf(avatar), ledger.Settings)) GrowUp(vm, avatar, news, choosers);
            }
            if (news.Count > 0) Announce?.Invoke(vm, string.Join("\n", news), choosers);
            return news;
        }

        /// <summary>
        /// Tells the player something happened on this lot, then lets them choose a head for each Sim who has just
        /// become an adult or an elder. Set by the pack; nothing is told without it.
        /// </summary>
        public static Action<VM, string, List<VMAvatar>> Announce;

        /// <summary>
        /// Opens the head or outfit picker for a grown Sim, offering those for their stage; when it is closed, calls
        /// the action given, if any. Set by the pack; false where there is nothing to show it on.
        /// </summary>
        public static Func<VM, VMAvatar, LookKind, Action, bool> ChooseLook;

        // ---- putting a stage on a Sim ----

        /// <summary>
        /// Move a Sim to the next stage now. False, with the reason in news, if it cannot be done. A Sim who has
        /// become an adult or an elder is added to choosers, to choose their head.
        /// </summary>
        public static bool GrowUp(VM vm, VMAvatar avatar, List<string> news, List<VMAvatar> choosers = null)
        {
            var ledger = Ledger;
            var sim = RecordOf(avatar);
            var next = Stages.Next(sim.Stage, ledger.Settings);
            if (next == null)
            {
                news.Add(avatar.Name + " is an elder. Nothing comes after that yet.");
                return false;
            }
            var newBody = Stages.IsChildToGame(sim.Stage) && !Stages.IsChildToGame(next.Value);
            if (newBody && !GiveAdultBody(vm, avatar))
            {
                CouldNotGrow.Add(avatar.Object.OBJ.GUID);
                news.Add(avatar.Name + " could not grow up: the game has no adult head or body to give. LifeStages.log says more. To try again: age up " + avatar.Name);
                return false;
            }
            CouldNotGrow.Remove(avatar.Object.OBJ.GUID);
            Stages.Advance(sim, ledger.Settings);
            FitData(vm, avatar, sim);
            FitLooks(avatar, sim, ledger.Settings);
            Log.Write(avatar.Name + " is now " + Article(sim.Stage) + ".");

            var line = avatar.Name + " is now " + Article(sim.Stage) + ".";
            var length = Stages.Length(sim.Stage, ledger.Settings);
            if (length > 0) line += " " + length + " days until " + Stages.Name(Stages.Next(sim.Stage, ledger.Settings).Value) + ".";
            news.Add(line);
            if (newBody || sim.Stage == Stage.Elder) choosers?.Add(avatar);
            return true;
        }

        /// <summary>
        /// Give a Sim another head, one of the game's: written into the Sim's file (kept when the game is saved)
        /// and worn at once.
        /// </summary>
        public static void SetHead(VMAvatar avatar, string mesh, string texture)
        {
            var body = BodyStrings(avatar);
            if (body == null || body.Length <= 2 || string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(texture)) return;
            var line = mesh + ",HEAD-HEAD=" + texture;
            if (body.GetString(2) == line) return;
            body.SetString(2, line);
            Content.Get().Neighborhood.AvatarChanged(avatar.Object.OBJ.GUID);
            avatar.HeadOutfit = new VMOutfitReference(body, true);
            Log.Write(avatar.Name + " has a new head: " + mesh + ".");
        }

        /// <summary>
        /// Give a Sim another everyday outfit, one of the game's: written into the Sim's file, with its hands, as the
        /// game writes clothes bought. A body of another build brings that build's other outfits (naked, swimwear
        /// and so on), as for a new Sim. Worn at once if the Sim is in their everyday clothes.
        /// </summary>
        public static void SetBody(VMAvatar avatar, string mesh, string texture, string hands)
        {
            var body = BodyStrings(avatar);
            if (body == null || body.Length <= 22 || string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(texture)) return;
            var line = mesh + ",BODY=" + texture;
            if (body.GetString(1) == line) return;
            var sex = avatar.GetPersonData(VMPersonDataVariable.Gender) == 1 ? "f" : "m";
            var skin = Body.SkinColour(body.GetString(14), avatar.GetPersonData(VMPersonDataVariable.SkinColor));
            var oldBuild = Body.BuildOf(Body.Worn(body.GetString(1)).Mesh)?.ToLowerInvariant();
            var newBuild = Body.BuildOf(mesh)?.ToLowerInvariant();

            body.SetString(1, line);
            var hg = string.IsNullOrEmpty(hands) ? skin : hands;
            body.SetString(17, "H" + sex + "LO,HAND=huao" + hg);
            body.SetString(18, "H" + sex + "RO,HAND=huao" + hg);
            body.SetString(19, "H" + sex + "LP,HAND=huao" + hg);
            body.SetString(20, "H" + sex + "RP,HAND=huao" + hg);
            body.SetString(21, "H" + sex + "LO,HAND=huao" + hg);
            body.SetString(22, "H" + sex + "RC,HAND=huao" + hg);
            if (newBuild != null && newBuild != oldBuild)
            {
                var others = new FSO.SimAntics.Utils.SimTemplateCreateInfo(sex + "a" + newBuild, skin).BodyStringReplace;
                foreach (var key in new[] { 15, 16, 27, 28, 29, 30, 31, 32, 33, 34 })
                    if (others.TryGetValue(key, out var value) && key < body.Length) body.SetString(key, value);
            }
            Content.Get().Neighborhood.AvatarChanged(avatar.Object.OBJ.GUID);

            var everyday = new VMOutfitReference(body, false);
            avatar.DefaultSuits.Daywear = everyday;
            if (avatar.GetPersonData(VMPersonDataVariable.CurrentOutfit) == 0) avatar.BodyOutfit = everyday;
            Log.Write(avatar.Name + " has a new outfit: " + mesh + ".");
        }

        /// <summary>
        /// The game's adult everyday outfits of a Sim's sex, in the build they have now (as the dresser offers), or
        /// in every build if theirs is not one.
        /// </summary>
        public static List<string> AdultBodies(VMAvatar avatar)
        {
            var sex = avatar.GetPersonData(VMPersonDataVariable.Gender) == 1 ? "fa" : "ma";
            var bodies = Content.Get().BCFGlobal.CollectionsByName["b"];
            var build = Body.BuildOf(Body.Worn(BodyStrings(avatar)?.GetString(1)).Mesh)?.ToLowerInvariant();
            if (build != null && bodies.ClothesByAvatarType.TryGetValue(sex + build, out var same)) return same.ToList();
            return bodies.GeneralAvatarType(sex);
        }

        /// <summary>The adult heads of a Sim's sex, as the game files them.</summary>
        public static List<string> AdultHeads(VMAvatar avatar)
        {
            var type = avatar.GetPersonData(VMPersonDataVariable.Gender) == 1 ? "fa" : "ma";
            return Content.Get().BCFGlobal.CollectionsByName["c"].ClothesByAvatarType.TryGetValue(type, out var heads) ? heads.ToList() : new List<string>();
        }

        /// <summary>
        /// Let the player choose another head or everyday outfit for a grown Sim, from those for their stage. Growing
        /// up picks by chance, and the player may not like what chance gave. Returns what to tell the player.
        /// </summary>
        public static string NewLook(VM vm, VMAvatar avatar, LookKind kind)
        {
            var sim = RecordOf(avatar);
            var what = kind == LookKind.Head ? "head" : "outfit";
            if (Stages.IsChildToGame(sim.Stage)) return avatar.Name + " is " + Article(sim.Stage) + ". Only a grown Sim can choose another " + what + ".";
            if (ChooseLook == null || !ChooseLook(vm, avatar, kind, null)) return "The " + what + " picker could not be opened. LifeStages.log says why.";
            return null;
        }

        /// <summary>Put a Sim on a day of the stage they are in (1 is the first). Returns what to tell the player.</summary>
        public static string SetDay(VMAvatar avatar, int day)
        {
            var settings = Ledger.Settings;
            var sim = RecordOf(avatar);
            var length = Stages.Length(sim.Stage, settings);
            sim.Days = Math.Clamp(day - 1, 0, length > 0 ? length : Settings.LongestStage);
            CouldNotGrow.Remove(avatar.Object.OBJ.GUID);
            return avatar.Name + ": " + Stages.Describe(sim, settings) + (Stages.Due(sim, settings) ? ". They move on in the evening, at home." : ".");
        }

        private static string Article(Stage stage) => (stage == Stage.Adult || stage == Stage.Elder ? "an " : "a ") + Stages.Name(stage);

        /// <summary>
        /// Bring what the game holds into line with the record. For the household being played, that is everything.
        /// For any other Sim the pack knows (a household played before, here as a visitor), it is only how they are
        /// drawn: their size and hair. Nothing is written for a Sim who is only visiting.
        /// </summary>
        public static void Reconcile(VM vm)
        {
            var ledger = Ledger;
            if (ledger == null || vm == null || !vm.TS1) return;
            var household = Household(vm);
            foreach (var avatar in household)
            {
                var sim = RecordOf(avatar);
                var body = BodyStrings(avatar);
                if (body == null) continue;
                var childBody = body.GetString(0) != "adult";
                if (childBody && !Stages.IsChildToGame(sim.Stage))
                {
                    //the record says grown, the Sim's file says child: the file was lost or put back. grow again
                    var guid = avatar.Object.OBJ.GUID;
                    if (CouldNotGrow.Contains(guid)) continue;
                    if (!GiveAdultBody(vm, avatar))
                    {
                        CouldNotGrow.Add(guid);
                        continue;
                    }
                }
                else if (!childBody && Stages.IsChildToGame(sim.Stage))
                {
                    //the other way round: the record is the one that was lost. a grown body is not taken back
                    sim.Stage = Stage.Adult;
                    sim.Days = 0;
                }
                FitData(vm, avatar, sim);
                FitLooks(avatar, sim, ledger.Settings);
            }

            if (!VM.UseWorld) return;
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                var avatar = entity as VMAvatar;
                if (avatar == null || avatar.Dead || avatar.IsPet || household.Contains(avatar)) continue;
                var sim = ledger.Find(avatar.Object.OBJ.GUID);
                if (sim == null) continue;
                //only if the Sim's own file agrees: a visitor's body is not this lot's to change
                var body = BodyStrings(avatar);
                if (body == null || (body.GetString(0) != "adult") != Stages.IsChildToGame(sim.Stage)) continue;
                FitLooks(avatar, sim, ledger.Settings);
            }
        }

        /// <summary>
        /// Make the age and size the game holds for a Sim those of its stage: on the lot, in the neighbourhood, and
        /// in the Sim's file. Changes nothing that is already right.
        /// </summary>
        public static void FitData(VM vm, VMAvatar avatar, SimAge sim)
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
        }

        /// <summary>
        /// Make a Sim look their stage on this lot: a teen's size. Only what is drawn is changed, and nothing that is
        /// already right.
        /// </summary>
        public static void FitLooks(VMAvatar avatar, SimAge sim, Settings settings)
        {
            if (!VM.UseWorld) return;

            // size. a Sim who has just arrived or loaded holds the number, but is not drawn with it yet. a Sim of
            // full size is only touched if still drawn as a teen: the game resizes Sims too (a spell, a potion)
            var size = Stages.Size(sim.Stage) / 100f;
            var teenSize = Stages.Size(Stage.Teen) / 100f;
            if (avatar.WorldUI is AvatarComponent drawn && drawn.Scale != size && (size != 1f || Math.Abs(drawn.Scale - teenSize) < 0.001f))
                drawn.Scale = size;
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
