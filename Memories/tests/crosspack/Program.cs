/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

// Checks that another pack can build on Memories: with Memories and a stand-in for such a pack (tests/consumer)
// each loaded the way Simitone loads a pack's code (its own PackLoadContext), an event raised in Memories reaches
// the other pack, and it is the same Memories the other pack sees, not a copy of its own.
// Run with: dotnet run --project Memories/tests/crosspack
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Simitone.Client.Packs;

var failed = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

var packs = Path.Combine(AppContext.BaseDirectory, "packs");
var memoriesDll = Path.Combine(packs, "memories", "Memories.dll");
var consumerDll = Path.Combine(packs, "consumer", "MemoriesConsumer.dll");
Check("both packs are built beside this program", File.Exists(memoriesDll) && File.Exists(consumerDll));
Check("the other pack does not ship a Memories.dll of its own", !File.Exists(Path.Combine(packs, "consumer", "Memories.dll")));
Check("Memories is not among the game's own assemblies here", !AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == "Memories"));

// Simitone's own loader, as it is in the Simitone this is built against
var loaderType = typeof(PackManager).GetNestedType("PackLoadContext", BindingFlags.NonPublic);
Check("Simitone's pack loader is there to use", loaderType != null);
if (loaderType == null) return 1;
AssemblyLoadContext Context(string id, string dll) =>
    (AssemblyLoadContext)Activator.CreateInstance(loaderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { id, Path.GetDirectoryName(dll) }, null);

// in dependency order, as Simitone does: Memories first, then the pack that requires it
var memoriesContext = Context("sample.memories", memoriesDll);
var memories = memoriesContext.LoadFromAssemblyPath(memoriesDll);
var consumerContext = Context("sample.consumer", consumerDll);
var consumer = consumerContext.LoadFromAssemblyPath(consumerDll);

memories.GetType("Memories.Log").GetField("ToFile").SetValue(null, false);

// without the link, a pack cannot find Memories at all: the game looks only in the pack's folder and among its own
{
    var bare = Context("sample.bare", consumerDll);
    try
    {
        bare.LoadFromAssemblyName(new AssemblyName("Memories"));
        Check("without the link, another pack does not find Memories", false);
    }
    catch (FileNotFoundException)
    {
        Check("without the link, another pack does not find Memories (so the link is needed)", true);
    }
}
var packType = consumer.GetExportedTypes().FirstOrDefault(t => typeof(ISimitonePack).IsAssignableFrom(t) && t.IsClass);
Check("the other pack's code is found as Simitone finds it", packType != null);
if (packType == null) return 1;
var pack = (ISimitonePack)Activator.CreateInstance(packType);
try
{
    pack.OnLoad(null);
    Check("the other pack loads, and links itself to Memories", true);
}
catch (Exception e)
{
    Check("the other pack loads, and links itself to Memories: " + e.GetBaseException().Message, false);
    return 1;
}
var reachedIn = (string)packType.GetField("ReachedIn").GetValue(null);
Check("it reached the Memories pack's own copy (" + reachedIn + ")", reachedIn == memoriesContext.Name);

// an event raised by Memories reaches it
var eventType = memories.GetType("Memories.LifeEvent");
var kindType = memories.GetType("Memories.LifeEventKind");
var e1 = Activator.CreateInstance(eventType);
eventType.GetProperty("Kind").SetValue(e1, Enum.Parse(kindType, "Promoted"));
eventType.GetProperty("Sim").SetValue(e1, 0x1234u);
memories.GetType("Memories.LifeEvents").GetMethod("Raise").Invoke(null, new[] { e1 });
Check("an event from Memories reaches the other pack", (int)packType.GetField("Heard").GetValue(null) == 1 && (string)packType.GetField("LastKind").GetValue(null) == "Promoted");

// and so does a memory: Memories' own book, with a made-up neighbourhood in place of the game's
var temp = Path.Combine(Path.GetTempPath(), "memories-crosspack-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
FSO.Common.FSOEnvironment.UserDir = temp;
var book = memories.GetType("Memories.MemoryBook");
book.GetField("NeighbourhoodPath").SetValue(null, (Func<string>)(() => Path.Combine(temp, "UserData")));
book.GetField("StageOf").SetValue(null, Delegate.CreateDelegate(book.GetField("StageOf").FieldType, typeof(Stand).GetMethod(nameof(Stand.StageOf)).MakeGenericMethod(memories.GetType("Memories.Stage"))));
book.GetMethod("Hear").Invoke(null, new[] { e1 });
Check("a memory made by Memories reaches the other pack", (int)packType.GetField("Remembered").GetValue(null) == 1);
try { Directory.Delete(temp, true); } catch { }

Console.WriteLine(failed == 0 ? "All checks passed." : failed + " check(s) FAILED.");
return failed == 0 ? 0 : 1;

static class Stand
{
    /// <summary>Every Sim an adult (the stage the game gives a Sim it knows nothing of).</summary>
    public static T StageOf<T>(uint sim) where T : struct, Enum => (T)Enum.ToObject(typeof(T), 2);
}
