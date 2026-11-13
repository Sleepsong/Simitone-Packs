/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.IO;

namespace Apartments
{
    /// <summary>
    /// What the prototype sees, in the terminal and in Apartments.log in Simitone's user directory. The point of the
    /// prototype is this record: how the game's own scripts treat households that live on a lot but are not played.
    /// </summary>
    public static class AptLog
    {
        private static readonly object Lock = new object();
        private static string LogPath;

        public static string FilePath => LogPath ??= Path.Combine(FSOEnvironment.UserDir, "Apartments.log");

        private const long MostBytes = 512 * 1024;

        /// <summary>
        /// Called once as the game starts. A log that has grown long is put aside (as Apartments.old.log, replacing the
        /// one before it), so the file to send is the recent one; and the start of this run is marked.
        /// </summary>
        public static void StartRun(string version)
        {
            try
            {
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MostBytes)
                {
                    var old = Path.Combine(file.DirectoryName, "Apartments.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    file.MoveTo(old);
                }
            }
            catch
            {
                //the log just carries on in the same file
            }
            Write("================ Apartments " + version + " loaded. Window: Options > Apartments, or F9. ================");
        }

        public static void Write(string line)
        {
            line = "[apt] " + line;
            Console.WriteLine(line);
            try
            {
                lock (Lock) File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
                //the terminal still has it
            }
        }
    }
}
