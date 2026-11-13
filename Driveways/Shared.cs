/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

namespace Driveways
{
    /// <summary>
    /// What the pack's objects (written by the generator) and the pack's code have to agree on.
    /// This file is compiled into both.
    /// </summary>
    public static class Shared
    {
        /// <summary>The driveway as it is bought. Its tiles have the GUIDs after it, one each.</summary>
        public const uint DrivewayGUID = 0x7D21A100;
        public const uint DrivewayPartGUID = DrivewayGUID + 1;
        /// <summary>
        /// The driveway's size in tiles, after The Sims 2's. Facing north its lead tile is the north west one and it
        /// runs south, where its last rows cross the pavement and the verge to the road.
        /// </summary>
        public const int DrivewayWidth = 5;
        public const int DrivewayLength = 9;

        /// <summary>A car's size in tiles: that of The Sims 1's own cars. Facing north, its lead tile is the front left one.</summary>
        public const int CarWidth = 3;
        public const int CarLength = 6;

        /// <summary>
        /// A car the pack sells: its own object (the master's GUID; its tiles have the GUIDs after it), and the car of
        /// the game's whose pictures it is drawn with (that car's master GUID, as the game's own files give it).
        /// </summary>
        public class CarModel
        {
            public string Name, Description;
            public uint GUID, GameGUID;
            public int Price;
        }

        public static readonly CarModel[] Cars =
        {
            new CarModel { Name = "Sedan", Description = "A sensible car, the one the carpool drives.", GUID = 0x7D21B000, GameGUID = 0xDB6DEDB2, Price = 3500 },
            new CarModel { Name = "Junker", Description = "It runs. Mostly.", GUID = 0x7D21B040, GameGUID = 0x4B71ECDA, Price = 900 },
            new CarModel { Name = "Pickup Truck", Description = "For hauling things, or looking as if you might.", GUID = 0x7D21B080, GameGUID = 0xCD0876E1, Price = 2800 },
            new CarModel { Name = "SUV", Description = "Room for the whole household.", GUID = 0x7D21B0C0, GameGUID = 0x1D3DEC63, Price = 5200 },
            new CarModel { Name = "Town Car", Description = "For arriving in.", GUID = 0x7D21B100, GameGUID = 0xBC3AEC18, Price = 7500 },
            new CarModel { Name = "Bentley", Description = "For when the career has gone very well indeed.", GUID = 0x7D21B140, GameGUID = 0x17ACEC22, Price = 12000 },
        };

        /// <summary>What earlier versions sold, no longer sold but still loaded: one driveway tile (0.1), driveways (0.2 to 0.4, 0.5 to 0.6), a car (0.1 to 0.6).</summary>
        public const uint OldTileGUID = 0x7D21A001;
        public static readonly (uint GUID, int Width, int Length)[] OldDriveways = { (0x7D21A020, 2, 4), (0x7D21A040, 3, 8) };
        public const uint OldCarGUID = 0x7D21A010;
        public const int OldCarWidth = 2, OldCarLength = 4;

        /// <summary>
        /// The SimAntics primitive this pack adds. The objects' scripts are one or two instructions long and call it
        /// for everything, with the first byte of the operand saying what to do.
        /// </summary>
        public const ushort Opcode = 200;

        /// <summary>Operand byte 0: take the Sim running this (the caller) for a drive in the car (the stack object).</summary>
        public const byte ModeDrive = 0;
        /// <summary>Operand byte 0: set a newly made driveway piece up (it runs as the piece itself).</summary>
        public const byte ModeInitDriveway = 1;
        /// <summary>Operand byte 0: set a newly made car tile up.</summary>
        public const byte ModeInitCar = 2;
        /// <summary>Operand byte 0: true when the car (the stack object) is parked, so it can be offered for a drive.</summary>
        public const byte ModeIsParked = 3;
    }
}
