/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Apartments
{
    /// <summary>
    /// The switches on the window's Settings page, kept between runs in apartments.cfg in the user directory. So a part
    /// of the pack that misbehaves can be switched off and stays off.
    /// </summary>
    public static class Settings
    {
        public static string FilePath => Path.Combine(FSOEnvironment.UserDir, "apartments.cfg");

        /// <summary>The switches as lines of text. Kept apart from the file so it can be checked without one.</summary>
        public static string Write()
        {
            return string.Join("\n", new[]
            {
                "private=" + (Doors.Private ? 1 : 0),
                "visitors=" + (Doors.ShowVisitors ? 1 : 0),
                "mailbox=" + (Neighbours.MovableMailbox ? 1 : 0),
                "dim=" + (Neighbours.Dim ? 1 : 0),
                "watch=" + (Neighbours.Watch ? 1 : 0),
                "outings=" + Neighbours.ComeOut.ToString().ToLowerInvariant(),
                "needs=" + (Neighbours.NeedsFloor?.ToString() ?? "off"),
            }) + "\n";
        }

        public static void Read(string text)
        {
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var parts = raw.Trim().Split('=');
                if (parts.Length != 2) continue;
                var value = parts[1].Trim().ToLowerInvariant();
                var on = value == "1" || value == "on" || value == "true";
                switch (parts[0].Trim().ToLowerInvariant())
                {
                    case "private": Doors.Private = on; break;
                    case "visitors": Doors.ShowVisitors = on; break;
                    case "mailbox": Neighbours.MovableMailbox = on; break;
                    case "dim": Neighbours.Dim = on; break;
                    case "watch": Neighbours.Watch = on; break;
                    case "outings":
                        if (Enum.TryParse<Outings>(value, true, out var often) && Enum.IsDefined(typeof(Outings), often)) Neighbours.ComeOut = often;
                        break;
                    case "needs":
                        if (value == "off") Neighbours.NeedsFloor = null;
                        else if (int.TryParse(value, out var floor) && floor >= -100 && floor <= 100) Neighbours.NeedsFloor = floor;
                        break;
                }
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Read(File.ReadAllText(FilePath));
                AptLog.Write("Switches: " + Write().Trim().Replace("\n", ", ") + ".");
            }
            catch (Exception e)
            {
                AptLog.Write("Could not read the switches (" + FilePath + "): " + e.Message);
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, Write());
            }
            catch (Exception e)
            {
                AptLog.Write("Could not keep the switches (" + FilePath + "): " + e.Message);
            }
        }
    }
}
