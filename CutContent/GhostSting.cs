/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.HIT;
using FSO.HIT;
using FSO.HIT.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CutContent
{
    /// <summary>
    /// The game has three "spooky" stings for when a ghost appears, and plays two. This gives the third its turn:
    /// one time in three that a spooky sting is asked for, the unused one plays in its place.
    ///
    /// If the game's sound tables have an event for the third sting, that event is played. If they only list the
    /// sound file, the file is played directly, at the sound effects volume.
    /// </summary>
    public static class GhostSting
    {
        public static string Status = "not looked for yet";

        private const string Unused = "spooky3";
        private static HashSet<string> UsedEvents = new HashSet<string>();
        private static string UnusedEvent;
        private static Patch UnusedPatch;
        private static Random Dice = new Random();

        public static void Find()
        {
            var content = Content.Get();
            if (!content.TS1 || content.Audio?.Events == null) { Status = "not a Sims 1 game"; return; }

            var groups = new HashSet<FSO.Content.Model.HITResourceGroup>();
            foreach (var evt in content.Audio.Events)
            {
                if (evt.Value.ResGroup != null) groups.Add(evt.Value.ResGroup);
                if (!evt.Key.Contains("spooky")) continue;
                if (evt.Key.Contains(Unused)) UnusedEvent = evt.Key;
                else UsedEvents.Add(evt.Key);
            }
            if (UnusedEvent == null)
            {
                UnusedPatch = groups.Where(x => x.hot?.Patches != null).SelectMany(x => x.hot.Patches.Values)
                    .FirstOrDefault(x => (x.Filename ?? "").ToLowerInvariant().Contains(Unused));
            }

            if (UsedEvents.Count == 0) Status = "the game's spooky sting events were not found";
            else if (UnusedEvent != null) Status = $"restored (event {UnusedEvent}, in turn with {string.Join(", ", UsedEvents)})";
            else if (UnusedPatch != null) Status = $"restored (file {UnusedPatch.Filename}, in turn with {string.Join(", ", UsedEvents)})";
            else Status = "the unused sting was not found in the game's sound tables";
            CutContentPack.Log("ghost sting: " + Status);
        }

        /// <summary>Before the engine plays a sound event.</summary>
        public static bool BeforeSoundEvent(ref string evt, ref HITSound __result)
        {
            if (evt == null || UsedEvents.Count == 0 || (UnusedEvent == null && UnusedPatch == null)) return true;
            if (!UsedEvents.Contains(evt.ToLowerInvariant()) || Dice.Next(3) != 0) return true;

            if (UnusedEvent != null)
            {
                evt = UnusedEvent; //the engine carries on, with the other sting
                return true;
            }
            try
            {
                var sfx = (Content.Get().Audio as TS1Audio)?.GetSFX(UnusedPatch);
                if (sfx == null) return true;
                sfx.Play(HITVM.Get().GetMasterVolume(HITVolumeGroup.FX), 0f, 0f);
                __result = null;
                return false;
            }
            catch (Exception e)
            {
                CutContentPack.Log("couldn't play the unused ghost sting: " + e.Message);
                UnusedPatch = null; //don't try again
                return true;
            }
        }
    }
}
