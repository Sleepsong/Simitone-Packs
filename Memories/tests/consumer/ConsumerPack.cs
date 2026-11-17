/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using Memories;
using Simitone.Client.Packs;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace MemoriesConsumer
{
    /// <summary>
    /// A stand-in for a pack that builds on Memories, the way Wants will: it lists sample.memories under "requires" in
    /// its pack.json, refers to Memories.dll without shipping it, and in OnLoad links itself to the copy Simitone
    /// loaded for that pack. The checks read what it heard.
    /// </summary>
    public class ConsumerPack : ISimitonePack
    {
        public static int Heard;
        public static string LastKind;
        public static int Remembered;
        /// <summary>The load context of the Memories this pack reached: it should be the Memories pack's own.</summary>
        public static string ReachedIn;

        public void OnLoad(Pack pack)
        {
            //first, before anything here touches a type of Memories'
            PackLink.Use("Memories");
            Listen();
        }

        //kept out of OnLoad, so that Memories' types are looked for only once the link is made
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Listen()
        {
            LifeEvents.Happened += e =>
            {
                Heard++;
                LastKind = e.Kind.ToString();
            };
            MemoryBook.Remembered += m => Remembered++;
            ReachedIn = AssemblyLoadContext.GetLoadContext(typeof(LifeEvents).Assembly)?.Name;
        }
    }

    /// <summary>
    /// How one pack uses another pack's code. Simitone loads each pack's code on its own (an AssemblyLoadContext of
    /// its own), so a pack that refers to another pack's assembly does not find it by itself: the game looks in the
    /// pack's own folder and among its own assemblies, and it is in neither. This answers the question when the
    /// game cannot, with the copy already loaded for the other pack, so both packs share one, with its events and
    /// its data. The other pack must be in "requires", so that it is loaded first and is there to be found.
    /// </summary>
    public static class PackLink
    {
        public static void Use(string assemblyName)
        {
            var mine = AssemblyLoadContext.GetLoadContext(typeof(PackLink).Assembly);
            if (mine == null || mine == AssemblyLoadContext.Default) return;
            mine.Resolving += (context, name) =>
            {
                if (name.Name != assemblyName) return null;
                foreach (var other in AssemblyLoadContext.All)
                {
                    if (other == context) continue;
                    foreach (Assembly assembly in other.Assemblies)
                        if (assembly.GetName().Name == assemblyName) return assembly;
                }
                return null;
            };
        }
    }
}
