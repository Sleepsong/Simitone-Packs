/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.FAR1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// Two quick surveys, each about one thing, so they don't take as long as the whole one.
    ///
    /// "cutcontent survey kiosk": the Superstar Information Kiosk. The Sims Wiki says it is in Superstar's files but lacks
    /// data buy mode needs. The first search only looked at what Simitone loads as objects, by name ("kiosk" or "info"), and
    /// found nothing. This reads every file of the game itself, as the files are, so it also sees files Simitone doesn't load
    /// as objects (one with no object definition is never loaded), and looks for the kiosk in object names, texts and script
    /// names, under any of the words it might go by. It also lists every object of Superstar's that buy mode doesn't show.
    ///
    /// "cutcontent survey pickpocket": what there is to build the Vacation pickpocket from: the burglar he was drawn from
    /// and the template every Sim is made from, what each of their scripts is called, Vacation's own people and whatever
    /// brings visitors onto its lots, the shared person scripts (where leaving a lot and the like are), any animation, text
    /// or menu entry that sounds like picking pockets, and Vacation's sales clerks, whose body his outfits fit.
    /// </summary>
    public static class FocusedSurveys
    {
        // ---- the kiosk ----

        private static readonly Regex KioskWords = new Regex(@"kiosk|informat|\binfo\b|directory|who'?s who|tourist|visitor.?cent|map ?stand|guide ?book|help ?desk|sign ?post|signpost|star ?map|walk of fame", RegexOptions.IgnoreCase);

        public static string Kiosk()
        {
            var report = new StringBuilder();
            report.AppendLine("Cut Content survey: the Information Kiosk");
            report.AppendLine(CutContentPack.Running);
            var root = Content.Get().TS1BasePath;
            report.AppendLine($"The game's folder: {root}");
            var catalog = Content.Get().WorldObjects as TS1ObjectProvider;
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog != null) foreach (var entry in catalog.Entries.Values) known.Add(Path.GetFileName((entry.FileName ?? "").Replace('\\', '/')));

            var hits = new List<string>();
            var unlisted = new List<string>();
            var noObject = new List<string>();
            var whole = new List<string>();
            int files = 0;
            var failed = ForEachGameFile(root, report, (where, name, iff) =>
            {
                files++;
                var superstar = where.IndexOf("ExpansionPack6", StringComparison.OrdinalIgnoreCase) >= 0 || where.IndexOf("Superstar", StringComparison.OrdinalIgnoreCase) >= 0;
                List<OBJD> objds = Safe(() => iff.List<OBJD>());
                if (objds.Count == 0)
                {
                    if (superstar || KioskWords.IsMatch(name)) noObject.Add($"{where}/{name}: {Kinds(iff)}{(known.Contains(name) ? "" : ", not loaded by Simitone")}");
                }
                foreach (var objd in objds)
                {
                    var label = Survey.Clean(objd.ChunkLabel);
                    var inCatalog = catalog?.GetItemByGUID(objd.GUID) != null;
                    var line = $"{where}/{name}: object \"{label}\" {objd.GUID:x8}, type {objd.ObjectType}, {(inCatalog ? "in buy mode" : "not in buy mode")}, price {objd.Price}, " +
                        $"category {objd.FunctionFlags:x}, rooms {objd.RoomFlags:x}, build {objd.BuildModeType}, catalog text {objd.CatalogStringsID}, master {objd.MasterID}/{objd.SubIndex}";
                    if (KioskWords.IsMatch(label) || KioskWords.IsMatch(name)) hits.Add(line);
                    else if (superstar && !inCatalog && objd.ObjectType != OBJDType.Person && (objd.MasterID == 0 || objd.SubIndex == -1)) unlisted.Add(line);
                }
                foreach (var text in Safe(() => iff.List<STR>()))
                    for (int i = 0; i < text.Length; i++)
                    {
                        var value = text.GetString(i) ?? "";
                        if (KioskWords.IsMatch(value)) hits.Add($"{where}/{name}: {text.ChunkType} {text.ChunkID} \"{Survey.Clean(text.ChunkLabel)}\" line {i}: {Survey.Clean(value)}");
                    }
                foreach (var text in Safe(() => iff.List<CTSS>()))
                    for (int i = 0; i < text.Length; i++)
                    {
                        var value = text.GetString(i) ?? "";
                        if (KioskWords.IsMatch(value)) hits.Add($"{where}/{name}: catalog text {text.ChunkID} line {i}: {Survey.Clean(value)}");
                    }
                if (Safe(() => iff.List<CTSS>()).Any(t => Regex.IsMatch(t.GetString(0) ?? "", "information kiosk", RegexOptions.IgnoreCase))) Whole(whole, where, name, iff, catalog);
                foreach (var script in Safe(() => iff.List<BHAV>()))
                    if (KioskWords.IsMatch(script.ChunkLabel ?? "")) hits.Add($"{where}/{name}: script {script.ChunkID} \"{Survey.Clean(script.ChunkLabel)}\"");
            });
            report.AppendLine($"{files} files read{(failed > 0 ? $", {failed} could not be" : "")}.");
            Section(report, "Everything that mentions a kiosk, information, a directory, a guide or the like", hits.Take(400));
            Section(report, "The file with the kiosk's catalog text, whole", whole);
            Section(report, "Superstar's objects that buy mode doesn't show (people and the extra tiles of large objects left out)", unlisted.Take(300));
            Section(report, "Superstar's files with no object definition (Simitone never loads these as objects)", noObject.Take(200));
            return report.ToString();
        }

        /// <summary>
        /// Everything in the file that holds the kiosk's catalog text: each object with every field buy mode looks at, which
        /// catalog text each uses, the catalog texts, pictures, menus and scripts. Buy mode shows an object only if it has a
        /// category or build type, is not switched off, has graphics (or is a multi-tile object), and is the object itself,
        /// not a tile of a larger one: whichever the kiosk lacks is the missing data.
        /// </summary>
        private static void Whole(List<string> lines, string where, string name, IffFile iff, TS1ObjectProvider catalog)
        {
            lines.Add($"{where}/{name}:");
            var texts = Safe(() => iff.List<CTSS>());
            foreach (var o in Safe(() => iff.List<OBJD>()).OrderBy(x => x.ChunkID))
            {
                var text = texts.FirstOrDefault(t => t.ChunkID == o.CatalogStringsID);
                lines.Add($"  object {o.ChunkID} \"{Survey.Clean(o.ChunkLabel)}\" {o.GUID:x8}: catalog text {o.CatalogStringsID} (\"{Survey.Clean(text?.GetString(0))}\"), " +
                    $"{(catalog?.GetItemByGUID(o.GUID) != null ? "in buy mode" : "not in buy mode")}, type {o.ObjectType}, version {o.Version}");
                lines.Add($"    category {o.FunctionFlags:x}, build type {o.BuildModeType}, off {o.Disabled}, graphics {o.NumGraphics} from {o.BaseGraphicID}, multi-tile {o.IsMultiTile}, part {o.MasterID}/{o.SubIndex}, " +
                    $"price {o.Price}, rooms {o.RoomFlags:x}, sorts: function {o.FunctionSubsort}, community {o.CommunitySubsort}, downtown {o.DTSubsort}, vacation {o.VacationSubsort}, studio town {o.STSubsort}, magic town {o.MTSubsort}");
                lines.Add($"    menu {o.TreeTableID}, main {o.BHAV_MainID}, init {o.BHAV_Init}, slots {o.SlotID}, level offset {o.LevelOffset}, tile width {o.TileWidth}");
            }
            foreach (var t in texts.OrderBy(x => x.ChunkID)) lines.Add($"  catalog text {t.ChunkID}: \"{Survey.Clean(t.GetString(0))}\"");
            var pictures = Safe(() => iff.List<DGRP>()).OrderBy(x => x.ChunkID).Select(x => $"{x.ChunkID} \"{Survey.Clean(x.ChunkLabel)}\"").ToList();
            lines.Add($"  pictures ({pictures.Count}): {string.Join("; ", pictures)}");
            lines.Add($"  sprites: {Safe(() => iff.List<SPR2>()).Count}");
            foreach (var table in Safe(() => iff.List<TTAB>()))
            {
                TTAs names = null;
                try { names = iff.Get<TTAs>(table.ChunkID); } catch { }
                lines.Add($"  menu {table.ChunkID}: " + string.Join("; ", table.Interactions.Select(x => $"\"{Survey.Clean(names?.GetString((int)x.TTAIndex))}\" runs {x.ActionFunction}")));
            }
            lines.Add("  scripts: " + string.Join("; ", Safe(() => iff.List<BHAV>()).OrderBy(x => x.ChunkID).Select(x => $"{x.ChunkID} {Survey.Clean(x.ChunkLabel)}")));
        }

        // ---- the pickpocket ----

        private static readonly Regex PocketWords = new Regex(@"pick.?pocket|wallet|purse|\bstole|steal|thie[fv]|robb|snatch|swipe|pilfer|burgl|mugg|crook|crim", RegexOptions.IgnoreCase);
        private static readonly Regex PocketAnimations = new Regex(@"pick|pocket|steal|thie[fv]|burgl|\brob|swipe|wallet|purse|sneak|tiptoe|snatch|bump|frisk|caught|arrest|cuff", RegexOptions.IgnoreCase);
        private static readonly Regex VisitorWords = new Regex(@"npc|generat|controll|director|spawn|visitor|tourist|walk.?by|vacationer|townie|crowd", RegexOptions.IgnoreCase);
        private const uint ThiefGuid = 0xc7857d8a, TemplateGuid = 0x7FD96B54;

        /// <summary>The pickpocket survey. "save" is given the report after each part, so a part that hangs or closes the game still leaves what came before it.</summary>
        public static string Pickpocket(Action<string> save)
        {
            Saving = save;
            var report = new StringBuilder();
            report.AppendLine("Cut Content survey: the pickpocket");
            report.AppendLine(CutContentPack.Running);
            var catalog = Content.Get().WorldObjects as TS1ObjectProvider;
            if (catalog == null) { report.AppendLine("The game's objects are not loaded."); return report.ToString(); }
            List<(string file, GameObject obj)> all = null;
            Run(report, "Reading every object", r =>
            {
                all = AllObjects(catalog);
                r.AppendLine($"{all.Count} objects.");
            });
            if (all == null) return report.ToString();

            Run(report, "The burglar (\"NPC Thief\") and the template every Sim is made from", r =>
            {
                foreach (var guid in new[] { ThiefGuid, TemplateGuid })
                {
                    var found = all.FirstOrDefault(x => x.obj.OBJ.GUID == guid);
                    if (found.obj == null) { r.AppendLine($"{guid:x8}: not found"); continue; }
                    Describe(r, found.file, found.obj, wholeMain: true);
                }
            });
            Run(report, "Vacation's own people, and its sales clerks (the pickpocket's outfits fit the male clerks' body)", r =>
            {
                foreach (var (file, obj) in all.Where(x => x.obj.OBJ.ObjectType == OBJDType.Person && Regex.IsMatch(x.file + " " + x.obj.OBJ.ChunkLabel, "vacation", RegexOptions.IgnoreCase)).OrderBy(x => x.file))
                    Describe(r, file, obj, wholeMain: Regex.IsMatch(obj.OBJ.ChunkLabel ?? "", @"clerk.*male.*lgt|sales clerk - male - lgt", RegexOptions.IgnoreCase));
            });
            Run(report, "Objects that bring people onto lots (controllers, generators, walk-bys)", r =>
            {
                foreach (var (file, obj) in all.Where(x => x.obj.OBJ.ObjectType != OBJDType.Person && VisitorWords.IsMatch(x.file + " " + x.obj.OBJ.ChunkLabel)).OrderBy(x => x.file).Take(80))
                    Describe(r, file, obj, wholeMain: false);
            });
            Run(report, "The shared person scripts and the global scripts, by name", r =>
            {
                var person = all.Select(x => x.obj.Resource?.SemiGlobal).FirstOrDefault(x => x != null && Regex.IsMatch(x.MainIff?.Filename ?? "", "personglobals", RegexOptions.IgnoreCase));
                Names(r, "personglobals.iff", person);
                Names(r, "global.iff", Content.Get().WorldObjectGlobals.Get("global")?.Resource);
            });
            Run(report, "Animations that sound like picking pockets, sneaking or being caught", r =>
            {
                var names = Content.Get().BCFGlobal?.ListAllAnimations() ?? new List<string>();
                var matching = names.Where(x => PocketAnimations.IsMatch(x)).OrderBy(x => x).ToList();
                r.AppendLine($"{matching.Count} of {names.Count} animations:");
                foreach (var name in matching) r.AppendLine("  " + name);
            });
            Run(report, "Texts and menu entries about pickpockets, thieves, wallets or stealing", r =>
            {
                var done = new HashSet<GameIffResource>();
                foreach (var (file, obj) in all)
                {
                    var res = obj.Resource;
                    if (res == null || !done.Add(res)) continue;
                    foreach (var text in Safe(() => res.List<STR>()))
                        for (int i = 0; i < text.Length; i++)
                        {
                            var value = text.GetString(i) ?? "";
                            if (PocketWords.IsMatch(value)) r.AppendLine($"{file}: {text.ChunkType} {text.ChunkID} line {i}: {Survey.Clean(value)}");
                        }
                    foreach (var script in Safe(() => res.List<BHAV>()))
                        if (PocketWords.IsMatch(script.ChunkLabel ?? "")) r.AppendLine($"{file}: script {script.ChunkID} \"{Survey.Clean(script.ChunkLabel)}\"");
                }
            });
            return report.ToString();
        }

        // ---- helpers ----

        private static List<(string file, GameObject obj)> AllObjects(TS1ObjectProvider catalog)
        {
            var list = new List<(string, GameObject)>();
            foreach (var entry in catalog.Entries.Values)
            {
                GameObject obj = null;
                try { obj = catalog.Get(entry.ID); } catch { }
                if (obj?.OBJ != null) list.Add((Path.GetFileName((entry.FileName ?? "").Replace('\\', '/')), obj));
            }
            return list;
        }

        /// <summary>An object: what it is, its main and init scripts, the names of all its scripts, and its main script whole if asked.</summary>
        private static void Describe(StringBuilder r, string file, GameObject obj, bool wholeMain)
        {
            var o = obj.OBJ;
            var res = obj.Resource;
            var semi = res?.SemiGlobal;
            var main = Survey.Find(res, semi, o.BHAV_MainID);
            var init = Survey.Find(res, semi, o.BHAV_Init);
            r.AppendLine($"{file} \"{Survey.Clean(o.ChunkLabel)}\" {o.GUID:x8}: type {o.ObjectType}, main {o.BHAV_MainID} \"{Survey.Clean(main?.ChunkLabel)}\", init {o.BHAV_Init} \"{Survey.Clean(init?.ChunkLabel)}\", table {o.TreeTableID}" +
                (semi != null ? $", shares {semi.MainIff?.Filename}" : ""));
            var scripts = Safe(() => res.List<BHAV>()).OrderBy(x => x.ChunkID).Select(x => $"{x.ChunkID} {Survey.Clean(x.ChunkLabel)}").ToList();
            if (scripts.Count > 0) Wrap(r, scripts);
            if (wholeMain)
            {
                var lines = new List<string>();
                foreach (var script in new[] { main, init }.Where(x => x != null).Distinct()) Survey.Steps(lines, res, semi, script);
                foreach (var line in lines) r.AppendLine(line);
            }
        }

        private static void Names(StringBuilder r, string name, GameIffResource res)
        {
            if (res == null) { r.AppendLine($"{name}: not found"); return; }
            var scripts = Safe(() => res.List<BHAV>()).OrderBy(x => x.ChunkID).Select(x => $"{x.ChunkID} {Survey.Clean(x.ChunkLabel)}").ToList();
            r.AppendLine($"{name} ({scripts.Count} scripts):");
            Wrap(r, scripts);
        }

        private static void Wrap(StringBuilder r, List<string> items)
        {
            var line = new StringBuilder("   ");
            foreach (var item in items)
            {
                if (line.Length + item.Length > 150) { r.AppendLine(line.ToString().TrimEnd(';', ' ')); line.Clear().Append("   "); }
                line.Append(' ').Append(item).Append(';');
            }
            if (line.Length > 3) r.AppendLine(line.ToString().TrimEnd(';'));
        }

        /// <summary>Each .iff of the game, loose or in a .far archive, read as it is, one at a time; saved games left out. Returns how many could not be read.</summary>
        private static int ForEachGameFile(string root, StringBuilder report, Action<string, string, IffFile> each)
        {
            var failed = 0;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { report.AppendLine("The game's folder was not found."); return 0; }
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var shown = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (shown.IndexOf("userdata", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".far")
                {
                    FAR1Archive far = null;
                    try
                    {
                        far = new FAR1Archive(path, false);
                        foreach (var entry in far.GetAllFarEntries())
                        {
                            if (!entry.Filename.EndsWith(".iff", StringComparison.OrdinalIgnoreCase)) continue;
                            IffFile iff;
                            try
                            {
                                iff = new IffFile();
                                using (var stream = new MemoryStream(far.GetEntry(entry))) iff.Read(stream);
                            }
                            catch { failed++; continue; }
                            each(shown, Path.GetFileName(entry.Filename.Replace('\\', '/')), iff);
                        }
                    }
                    catch { failed++; }
                    finally { far?.Close(); }
                }
                else if (ext == ".iff")
                {
                    IffFile iff;
                    try { iff = new IffFile(path); } catch { failed++; continue; }
                    each(Path.GetDirectoryName(shown)?.Replace('\\', '/') ?? "", Path.GetFileName(path), iff);
                }
            }
            return failed;
        }

        private static string Kinds(IffFile iff)
        {
            var kinds = new List<string>();
            foreach (var (type, label) in new (Type, string)[] { (typeof(BHAV), "scripts"), (typeof(STR), "texts"), (typeof(TTAB), "menus"), (typeof(DGRP), "pictures"), (typeof(SPR2), "sprites"), (typeof(SLOT), "slots") })
            {
                try
                {
                    var list = (System.Collections.IList)typeof(IffFile).GetMethod("List").MakeGenericMethod(type).Invoke(iff, null);
                    if (list != null && list.Count > 0) kinds.Add($"{list.Count} {label}");
                }
                catch { }
            }
            return kinds.Count == 0 ? "nothing the survey reads" : string.Join(", ", kinds);
        }

        private static List<T> Safe<T>(Func<List<T>> get)
        {
            try { return get() ?? new List<T>(); } catch { return new List<T>(); }
        }

        private static void Section(StringBuilder report, string title, IEnumerable<string> lines)
        {
            report.AppendLine();
            report.AppendLine("== " + title + " ==");
            var any = false;
            foreach (var line in lines) { report.AppendLine(line); any = true; }
            if (!any) report.AppendLine("none");
        }

        private static Action<string> Saving;

        private static void Run(StringBuilder report, string title, Action<StringBuilder> write)
        {
            report.AppendLine();
            report.AppendLine("== " + title + " ==");
            CutContentPack.Log("survey: " + title);
            try { write(report); }
            catch (Exception e) { report.AppendLine("This part failed: " + e); }
            try { Saving?.Invoke(report.ToString()); } catch { }
        }
    }
}
