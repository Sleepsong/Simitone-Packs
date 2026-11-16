/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Codecs;
using FSO.Content.Framework;
using FSO.Content.Model;
using FSO.Content.TS1;
using FSO.Files.HIT;
using FSO.HIT;
using FSO.HIT.Model;
using HarmonyLib;
using Patch = FSO.Files.HIT.Patch;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CutContent
{
    /// <summary>
    /// The game has three "spooky" stings for when a ghost appears, and plays two.
    ///
    /// The survey showed how: the sound event sting_spooky plays track 0x74b, whose list of sounds (its hitlist) holds
    /// sting_spooky1.wav and sting_spooky2.wav. sting_spooky3.wav is in SoundData/Stings but in no list. This adds it to
    /// that list, in memory, so the game picks from all three as it does from the two.
    ///
    /// The engine keeps the first sound a track picks from its list and plays that one from then on, so the game plays
    /// the same spooky sting all session. Before each spooky sting this clears that choice, so all three take turns.
    ///
    /// If the list can't be found, one time in three the unused sting is played directly instead.
    /// </summary>
    public static class GhostSting
    {
        public static string Status = "not looked for yet";

        private const string Event = "sting_spooky";
        private const string Unused = "sting_spooky3";

        private static Track SpookyTrack;
        private static bool ClearChoice;
        private static Patch UnusedPatch;
        private static bool Direct;
        private static Random Dice = new Random();

        public static void Find()
        {
            var content = Content.Get();
            var audio = content.Audio as TS1Audio;
            if (!content.TS1 || audio?.Events == null) { Status = "not a Sims 1 game"; return; }

            //the file, which no list mentions
            var wavs = AccessTools.Field(typeof(TS1Audio), "WAVSounds")?.GetValue(audio) as TS1SubProvider<DecodedSFX>;
            var file = wavs?.GetAllNames().FirstOrDefault(x => x.ToLowerInvariant().Replace('\\', '/').EndsWith(Unused + ".wav"));
            if (file == null) { Status = $"{Unused}.wav was not found in the game's sound files"; CutContentPack.Log("ghost sting: " + Status); return; }
            var fileName = file.Replace('\\', '/').Split('/').Last();

            if (!audio.Events.TryGetValue(Event, out var evt) || evt.ResGroup?.hot == null)
            {
                UseDirectly(fileName, $"the {Event} sound event was not found");
                return;
            }
            var group = evt.ResGroup;
            var track = audio.GetTrack(evt.TrackID, 0, group);
            var list = (track != null && track.HitlistID != 0) ? audio.GetHitlist(track.HitlistID, group) : null;
            if (list?.IDs == null || list.IDs.Count == 0)
            {
                UseDirectly(fileName, $"track {evt.TrackID:x} has no list of sounds");
                return;
            }

            //a number for the new sound that is neither a sound nor a track already
            uint id = Math.Max(group.hot.Patches.Keys.DefaultIfEmpty(0u).Max(), group.hot.Tracks.Keys.DefaultIfEmpty(0u).Max()) + 1;
            UnusedPatch = new Patch($"{id},{Unused},\"SoundData/Stings/{fileName}\",0,0");
            group.hot.Patches[id] = UnusedPatch;
            if (!list.IDs.Contains(id)) list.IDs.Add(id);

            SpookyTrack = track;
            ClearChoice = track.SoundID == 0; //only clear a choice the engine made, not one the track came with
            Status = $"restored: {fileName} added to track {evt.TrackID:x}'s sounds, now {string.Join(", ", list.IDs.Select(x => x.ToString("x")))}";
            CutContentPack.Log("ghost sting: " + Status);
        }

        private static void UseDirectly(string fileName, string why)
        {
            UnusedPatch = new Patch($"0,{Unused},\"SoundData/Stings/{fileName}\",0,0");
            Direct = true;
            Status = $"restored, played directly one time in three ({why})";
            CutContentPack.Log("ghost sting: " + Status);
        }

        /// <summary>Before the engine plays a sound event.</summary>
        public static bool BeforeSoundEvent(string evt, ref HITSound __result)
        {
            if (evt == null || !evt.Equals(Event, StringComparison.OrdinalIgnoreCase)) return true;
            if (!Direct)
            {
                if (ClearChoice && SpookyTrack != null) SpookyTrack.SoundID = 0; //pick again from all three
                return true;
            }
            if (Dice.Next(3) != 0) return true;
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
                Direct = false; //don't try again
                return true;
            }
        }
    }
}
