/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Driveways
{
    /// <summary>
    /// The drives under way on a lot when it was saved, kept in a file of the pack's own beside the save (see
    /// VMDrive.Saving). The save itself is the game's and is not touched: what the game keeps of a drive is a Sim part way
    /// through the car's script, standing by it, or out of the world at work. This is the rest: where the car was on
    /// its way, and which of the game's cars were being kept out of sight.
    ///
    /// Objects are named by their object id, which a save keeps, and checked against the character or object they were
    /// (their GUID) when they are found again. Plain numbers only, so it is checked without the engine.
    /// </summary>
    public class SavedDrives
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        /// <summary>The lot's clock when it was saved: the file belongs with the save that has the same time.</summary>
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }

        public List<SavedTrip> Trips { get; set; } = new List<SavedTrip>();
        public List<SavedCommute> Commutes { get; set; } = new List<SavedCommute>();
        /// <summary>The game's cars kept out of sight (the carpools that take and bring a Sim who drives), by their lead tile.</summary>
        public List<SavedObject> HiddenCars { get; set; } = new List<SavedObject>();
        /// <summary>The Sims a car set to drive them to work has been queued for, and the day.</summary>
        public List<SavedQueued> AutoQueued { get; set; } = new List<SavedQueued>();

        public bool IsEmpty => Trips.Count == 0 && Commutes.Count == 0;

        /// <summary>Whether this was saved at this time of day: if not, it is not the save that is being loaded.</summary>
        public bool SavedAt(int day, int hour, int minute) => Day == day && Hour == hour && Minute == minute;

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Options));

        /// <summary>What was saved, or null if there is nothing that can be read (no file, a broken one, or one from a later version).</summary>
        public static SavedDrives FromBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                var saved = JsonSerializer.Deserialize<SavedDrives>(data, Options);
                if (saved == null || saved.Version < 1 || saved.Version > CurrentVersion) return null;
                saved.Trips ??= new List<SavedTrip>();
                saved.Commutes ??= new List<SavedCommute>();
                saved.HiddenCars ??= new List<SavedObject>();
                saved.AutoQueued ??= new List<SavedQueued>();
                saved.Trips.RemoveAll(t => t == null || t.Car == null);
                saved.Commutes.RemoveAll(c => c == null || c.Car == null);
                saved.HiddenCars.RemoveAll(c => c == null);
                saved.AutoQueued.RemoveAll(q => q == null);
                return saved;
            }
            catch (Exception) { return null; }
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };
    }

    /// <summary>An object on the lot: its id, and its GUID to be sure it is the same one.</summary>
    public class SavedObject
    {
        public short ID { get; set; }
        public uint GUID { get; set; }

        public SavedObject() { }
        public SavedObject(short id, uint guid) { ID = id; GUID = guid; }
    }

    /// <summary>A drive under way (VMDrive.Trip).</summary>
    public class SavedTrip
    {
        public SavedObject Car { get; set; }
        public SavedObject Sim { get; set; }
        /// <summary>Out, Away, Back or Waiting.</summary>
        public string Phase { get; set; }
        public float Distance { get; set; }
        public int Timer { get; set; }
        public bool ToWork { get; set; }
        public short ToLot { get; set; }
        /// <summary>The carpool the Sim is to be handed to, if it has come: the tile that offers "Go to Work", and that interaction.</summary>
        public SavedObject Carpool { get; set; }
        public int CarpoolInteraction { get; set; }
        /// <summary>Where the Sim stood by the car, if they are out of the world waiting for the carpool.</summary>
        public SavedTile WaitingFrom { get; set; }
        public int WaitingFacing { get; set; }
    }

    /// <summary>A Sim gone to work in their own car (VMDrive.Commute).</summary>
    public class SavedCommute
    {
        public SavedObject Car { get; set; }
        public uint SimGUID { get; set; }
        public string SimName { get; set; }
        /// <summary>GettingIn, AtWork, Riding, Arrived or Home.</summary>
        public string State { get; set; }
        public int Ticks { get; set; }
        public float Distance { get; set; }
        public SavedObject Carpool { get; set; }
        public int CarpoolInteraction { get; set; }
        /// <summary>The game's cars on the lot when the Sim had gone to work, by their lead tile.</summary>
        public List<SavedObject> CarsWhenGone { get; set; }
    }

    public class SavedTile
    {
        public short X { get; set; }
        public short Y { get; set; }
        public sbyte Level { get; set; }
    }

    public class SavedQueued
    {
        public uint SimGUID { get; set; }
        public short Day { get; set; }
    }
}
