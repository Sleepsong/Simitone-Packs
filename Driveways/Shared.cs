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
        /// <summary>The driveway piece: one flat tile of paving.</summary>
        public const uint DrivewayGUID = 0x7D21A001;
        /// <summary>The car as it is bought, and the first of its tiles. Tile n has CarPartGUID + n.</summary>
        public const uint CarGUID = 0x7D21A010;
        public const uint CarPartGUID = 0x7D21A011;

        /// <summary>The car's size in tiles. Facing north, its lead tile is the front left one.</summary>
        public const int CarWidth = 2;
        public const int CarLength = 4;

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
