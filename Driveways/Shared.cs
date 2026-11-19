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
        ///
        /// Near the top of the range, out of the way: other packs take free numbers as they start, counting up from 200
        /// (Cut Content does). If it is taken all the same, the pack takes another free number and changes its scripts to
        /// call that one (DrivewaysPack.OnContentReady). It was 200 until 0.18, which Cut Content takes.
        /// </summary>
        public const ushort Opcode = 250;

        /// <summary>Operand byte 0: take the Sim running this (the caller) for a drive in the car (the stack object).</summary>
        public const byte ModeDrive = 0;
        /// <summary>Operand byte 0: set a newly made driveway piece up (it runs as the piece itself).</summary>
        public const byte ModeInitDriveway = 1;
        /// <summary>Operand byte 0: set a newly made car tile up.</summary>
        public const byte ModeInitCar = 2;
        /// <summary>Operand byte 0: true when the car (the stack object) is parked, so it can be offered for a drive.</summary>
        public const byte ModeIsParked = 3;
        /// <summary>Operand byte 0: true when the Sim running this can drive the car to work now: the game's carpool has come for them.</summary>
        public const byte ModeCanDriveToWork = 4;
        /// <summary>Operand byte 0: take the Sim running this to work in the car, in place of the carpool.</summary>
        public const byte ModeDriveToWork = 5;
        /// <summary>Operand byte 0: the Sim running this is being driven home from work in the car; true once they are out of it.</summary>
        public const byte ModeRideHome = 6;
        /// <summary>Operand byte 0: true when the car is bringing the Sim running this home. Never true for the player's menu.</summary>
        public const byte ModeCanRideHome = 7;
        /// <summary>Operand byte 0: true when the car can take the Sim running this to a community lot; adds one menu entry for each lot.</summary>
        public const byte ModeCanDriveToLot = 8;
        /// <summary>Operand byte 0: take the Sim running this to the lot in the action's first argument (the menu entry's Param0).</summary>
        public const byte ModeDriveToLot = 9;
        /// <summary>
        /// Operand byte 0: before a Sim walks to the car for a drive (the drive's mode in operand byte 1): false, with a
        /// message, if the car cannot go; otherwise true, with the stack object set to the car's tile by its door.
        /// </summary>
        public const byte ModePrepare = 10;
        /// <summary>Operand byte 0: true when Sims get in by the car's left side (the driver's door), false for its right.</summary>
        public const byte ModeIsLeftDoor = 11;
        /// <summary>Operand byte 0: the test for switching "Always Drive to Work" on or off; names the menu entry.</summary>
        public const byte ModeCanToggleAuto = 12;
        /// <summary>Operand byte 0: switch "Always Drive to Work" on or off for the car.</summary>
        public const byte ModeToggleAuto = 13;
        /// <summary>Operand byte 0: show the Sim running this (hidden while they were in the car), to get out where they can be seen.</summary>
        public const byte ModeShow = 14;
        /// <summary>Operand byte 0: drive the caller home from another lot in the car (the stack object) they came in.</summary>
        public const byte ModeDriveHome = 15;
        /// <summary>Operand byte 0: whether the car is one a Sim came to this lot in, parked at the kerb, that the caller can drive home in.</summary>
        public const byte ModeCanDriveHome = 16;
    }
}
