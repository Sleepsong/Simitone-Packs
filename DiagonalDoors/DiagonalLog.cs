/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DiagonalDoors
{
    /// <summary>
    /// A record of what the pack decided each time the player held a door, window or staircase over a tile, for finding
    /// out why one will not go where it should. Off until the "diagonallog" console command turns it on; then written
    /// to diagonals.log in Simitone's user directory (next to config.ini).
    /// </summary>
    public static class DiagonalLog
    {
        public static bool On;
        public static string File => Path.Combine(FSOEnvironment.UserDir ?? ".", "diagonals.log");
        private static string Last;

        public static bool Command(string[] args)
        {
            if (args.Length == 0 || args[0].ToLowerInvariant() == "on") On = true;
            else if (args[0].ToLowerInvariant() == "off") On = false;
            else return false;
            if (On) Write("---- logging on ----");
            return true;
        }

        /// <summary>Writes a line, unless it is the same as the last one (placement is asked many times a second).</summary>
        public static void Write(string line)
        {
            if (!On || line == Last) return;
            Last = line;
            try { System.IO.File.AppendAllText(File, line + Environment.NewLine); }
            catch { On = false; } //nowhere to write: give up quietly
        }

        /// <summary>An object's parts as the pack sees them: where each is, the walls it needs, and what kind of thing it is.</summary>
        public static string Describe(VMMultitileGroup group)
        {
            var text = new StringBuilder();
            try
            {
                var lead = group.BaseObject;
                text.Append(lead?.ToString() ?? "?").Append(" guid ").Append(((lead?.MasterDefinition ?? lead?.Object?.OBJ)?.GUID ?? 0).ToString("X8")).Append(" parts[");
                for (int i = 0; i < group.Objects.Count; i++)
                {
                    var part = group.Objects[i];
                    var flags2 = (VMEntityFlags2)part.GetValue(VMStackObjectVariable.FlagField2);
                    text.Append(i == 0 ? "" : "; ")
                        .Append(part == lead ? "LEAD " : "")
                        .Append("off ").Append(group.Offsets[i].x).Append(',').Append(group.Offsets[i].y).Append(',').Append(group.Offsets[i].Level)
                        .Append(" wall 0x").Append(((ushort)part.GetValue(VMStackObjectVariable.WallPlacementFlags)).ToString("X"))
                        .Append(" place 0x").Append(((ushort)part.GetValue(VMStackObjectVariable.PlacementFlags)).ToString("X"))
                        .Append((flags2 & VMEntityFlags2.ArchitectualDoor) != 0 ? " DOOR" : "")
                        .Append((flags2 & VMEntityFlags2.ArchitectualWindow) != 0 ? " WINDOW" : "")
                        .Append(part.Portal ? " portal" : "")
                        .Append(part.GhostImage ? " ghost" : "")
                        .Append(" lvloff ").Append(part.Object?.OBJ?.LevelOffset ?? 0);
                }
                text.Append(']');
            }
            catch (Exception e) { text.Append(" (could not describe: ").Append(e.Message).Append(')'); }
            return text.ToString();
        }

        public static string Describe(in WallTile wall)
        {
            return "wall{" + wall.Segments + " trStyle " + wall.TopRightStyle + " objTR " + wall.ObjSetTRStyle + (wall.TopRightDoor ? " door" : "") + " floors " + wall.TopLeftStyle + "/" + wall.TopLeftPattern + "}";
        }

        /// <summary>Whether an object is one this pack has anything to do with.</summary>
        public static bool Interesting(VMMultitileGroup group)
        {
            if (group == null) return false;
            if (DiagonalStairs.IsStairs(group)) return true;
            return group.Objects.Any(x => ((VMEntityFlags2)x.GetValue(VMStackObjectVariable.FlagField2) & (VMEntityFlags2.ArchitectualDoor | VMEntityFlags2.ArchitectualWindow)) != 0);
        }
    }
}
