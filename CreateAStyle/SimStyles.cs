/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Content.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CreateAStyle
{
    /// <summary>
    /// Styles for Sims: their clothes, hair and hats, never their skin.
    ///
    /// A Sim is drawn as 3D meshes, each with a texture: one for the body (clothes, with arms, neck and legs), one
    /// for the head (face, hair and any hat), and the hands. Skin and clothes share a texture, so the colours alone
    /// cannot say which is which: brown hair or a tan shirt can be the colour of skin. But the game has each body and
    /// head in light, medium and dark skin (the "lgt", "med" or "drk" in a texture's name), and those versions differ
    /// only where there is skin. So a pixel is clothes, hair or hat if it is the same in all the versions there are,
    /// and skin if it is not. Only the first is ever recoloured. A texture with no other versions to compare with is
    /// not offered at all, since its skin cannot be told apart. Pets have no skin, so all of theirs is offered.
    ///
    /// A Sim's styles are kept by texture: an outfit they change out of keeps its style for when they wear it again.
    /// They are the Sim's, not the lot's, so they are kept in one file for each neighbourhood, and follow the Sim to
    /// any lot.
    /// </summary>
    public static class SimStyles
    {
        // ---- the names of the textures Sims wear ----

        /// <summary>The engine gives a Sim's meshes their textures by name, but does not keep the name: it is kept here.</summary>
        private static ConditionalWeakTable<ITextureRef, string> NameOf = new ConditionalWeakTable<ITextureRef, string>();

        public static string TextureName(ITextureRef texture)
        {
            return (texture != null && NameOf.TryGetValue(texture, out var name)) ? name : null;
        }

        /// <summary>Patched in after Avatar.AddBinding: the engine has just given one of a Sim's meshes its texture.</summary>
        public static void Named(Binding binding, AvatarBindingInstance __result)
        {
            if (binding == null || __result?.Texture == null || binding.MeshName == null) return;
            var name = binding.TextureName ?? __result.Mesh?.TextureName;
            if (!string.IsNullOrEmpty(name)) NameOf.AddOrUpdate(__result.Texture, name.ToLowerInvariant());
        }

        // ---- telling skin apart ----

        public static readonly string[] Tones = { "lgt", "med", "drk" };

        /// <summary>Finds a texture by name. The game's own content, unless a test says otherwise.</summary>
        public static Func<string, ITextureRef> Lookup = name => FSO.Content.Content.Get()?.AvatarTextures?.Get(name);

        /// <summary>
        /// The names of a texture in the other skin tones, or null if its name has none. The tone comes just before
        /// the first underscore, or at the end: b003mafitlgt_01 is b003mafitmed_01 in medium skin, huaolgt is huaodrk
        /// in dark.
        /// </summary>
        public static List<string> OtherTones(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.ToLowerInvariant();
            var cut = name.IndexOf('_');
            var head = (cut < 0) ? name : name.Substring(0, cut);
            var rest = (cut < 0) ? "" : name.Substring(cut);
            if (head.Length < 4) return null;
            var tone = head.Substring(head.Length - 3);
            if (!Tones.Contains(tone)) return null;
            var stem = head.Substring(0, head.Length - 3);
            return Tones.Where(t => t != tone).Select(t => stem + t + rest).ToList();
        }

        /// <summary>Which pixels of a texture are skin.</summary>
        public class SkinMask
        {
            /// <summary>True for skin. Null if skin cannot be told apart in this texture: then nothing in it is recoloured.</summary>
            public bool[] Skin;
            /// <summary>Why skin cannot be told apart, when it cannot.</summary>
            public string Note;
            /// <summary>How many other skin tones it was compared with.</summary>
            public int Compared;
            /// <summary>How much of the picture (the parts that show) is not skin, 0 to 1.</summary>
            public float Free;
        }

        /// <summary>Pixels that differ by more than this, in red, green, blue or transparency, between skin tones are skin.</summary>
        public const int Tolerance = 12;

        private static ConditionalWeakTable<Texture2D, SkinMask> Masks = new ConditionalWeakTable<Texture2D, SkinMask>();

        /// <summary>
        /// The skin in one of a Sim's textures. Null if it cannot be worked out yet (a picture still loading), so it is
        /// worth asking again.
        /// </summary>
        public static SkinMask MaskOf(string name, Texture2D texture, bool pet)
        {
            if (texture == null) return null;
            if (Masks.TryGetValue(texture, out var known)) return known;
            var mask = MakeMask(name, texture, pet);
            if (mask != null) Masks.AddOrUpdate(texture, mask);
            return mask;
        }

        private static SkinMask MakeMask(string name, Texture2D texture, bool pet)
        {
            var pixels = Pixels.Of(texture, null, out _, out _);
            if (pixels == null) return null;
            if (pet) return new SkinMask { Skin = new bool[pixels.Length], Free = 1 };

            var others = OtherTones(name);
            if (others == null) return new SkinMask { Note = "It has no skin tone in its name, so there is nothing to tell its skin apart by." };

            var skin = new bool[pixels.Length];
            int compared = 0;
            foreach (var other in others)
            {
                ITextureRef reference = null;
                try { reference = Lookup(other); } catch { }
                if (reference == null) continue; //not made in this skin tone
                Texture2D otherTexture;
                try { otherTexture = reference.Get(texture.GraphicsDevice); }
                catch { continue; }
                if (otherTexture == null || otherTexture.Width != texture.Width || otherTexture.Height != texture.Height) continue;
                var otherPixels = Pixels.Of(otherTexture, null, out _, out _);
                if (otherPixels == null) return null; //still loading: try again later
                compared++;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (Differs(pixels[i], otherPixels[i])) skin[i] = true;
                }
            }
            if (compared == 0) return new SkinMask { Note = "It comes in only one skin tone, so there is nothing to tell its skin apart by." };

            int shown = 0, free = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A < 128) continue;
                shown++;
                if (!skin[i]) free++;
            }
            return new SkinMask { Skin = skin, Compared = compared, Free = (shown == 0) ? 0 : free / (float)shown };
        }

        private static bool Differs(Color a, Color b)
        {
            if (a.A < 8 && b.A < 8) return false; //neither shows
            return Math.Abs(a.R - b.R) > Tolerance || Math.Abs(a.G - b.G) > Tolerance
                || Math.Abs(a.B - b.B) > Tolerance || Math.Abs(a.A - b.A) > Tolerance;
        }

        // ---- the styles of the neighbourhood's Sims ----

        /// <summary>For each Sim (KeyOf), for each texture they have had styled, its style.</summary>
        private static Dictionary<string, Dictionary<string, Style>> Table = new Dictionary<string, Dictionary<string, Style>>();
        /// <summary>Goes up whenever Table changes, so the Sims on show know to take their styles again.</summary>
        private static int Version;
        /// <summary>The neighbourhood folder the table belongs to.</summary>
        private static string Neighbourhood;

        /// <summary>A Sim, as the neighbourhood knows them: their neighbour number, and their own kind of object.</summary>
        public static string KeyOf(VMAvatar sim)
        {
            return sim.GetPersonData(VMPersonDataVariable.NeighborId) + ":" + (sim.Object?.OBJ?.GUID ?? 0).ToString("X8");
        }

        /// <summary>The style a Sim has for one of their textures, or null.</summary>
        public static Style StyleOf(VMAvatar sim, string texture)
        {
            return (Table.TryGetValue(KeyOf(sim), out var styles) && styles.TryGetValue(texture, out var style)) ? style : null;
        }

        /// <summary>Gives a Sim a style for one of their textures. Null, or a style that changes nothing, takes it off.</summary>
        public static void SetStyle(VMAvatar sim, string texture, Style style)
        {
            var key = KeyOf(sim);
            if (style == null || style.IsDefault)
            {
                if (Table.TryGetValue(key, out var styles))
                {
                    styles.Remove(texture);
                    if (styles.Count == 0) Table.Remove(key);
                }
            }
            else
            {
                if (!Table.TryGetValue(key, out var styles)) Table[key] = styles = new Dictionary<string, Style>();
                styles[texture] = style;
            }
            Version++;
            Sync(sim);
        }

        /// <summary>Takes all of a Sim's styles off.</summary>
        public static void Clear(VMAvatar sim)
        {
            if (Table.Remove(KeyOf(sim)))
            {
                Version++;
                Sync(sim);
            }
        }

        public static bool HasStyle(VMAvatar sim) => Table.ContainsKey(KeyOf(sim));

        // ---- the Sims on show ----

        /// <summary>What this pack keeps for a Sim being drawn: the styled copies of the textures that have a style.</summary>
        private class Live
        {
            public string Key;
            public int Version = -1;
            public bool Pet;
            public Dictionary<string, StyleTextures> Textures = new Dictionary<string, StyleTextures>();
        }

        private static ConditionalWeakTable<Avatar, Live> LiveOf = new ConditionalWeakTable<Avatar, Live>();
        private static List<(WeakReference<Avatar> avatar, Live live)> AllLive = new List<(WeakReference<Avatar>, Live)>();

        /// <summary>Has a Sim drawn with the styles they have now.</summary>
        public static void Sync(VMAvatar sim)
        {
            var avatar = sim.Avatar;
            if (avatar == null) return;
            var key = KeyOf(sim);
            LiveOf.TryGetValue(avatar, out var live);
            if (live != null && live.Version == Version && live.Key == key) return;
            if (live == null && !Table.ContainsKey(key)) return; //no styles, none shown

            var fresh = new Dictionary<string, StyleTextures>();
            if (Table.TryGetValue(key, out var styles))
            {
                foreach (var pair in styles) fresh[pair.Key] = StyleTextures.Acquire(pair.Value);
            }
            if (live == null)
            {
                live = new Live();
                LiveOf.Add(avatar, live);
                AllLive.Add((new WeakReference<Avatar>(avatar), live));
            }
            //(the new ones are taken before the old ones are let go, so a part that did not change keeps its copies)
            var old = live.Textures;
            live.Textures = fresh;
            foreach (var textures in old.Values) textures.Release();
            live.Key = key;
            live.Version = Version;
            live.Pet = sim.IsPet;
        }

        private static long NextTick;

        /// <summary>Every so often: Sims who have come onto the lot take their styles.</summary>
        public static void Tick(VM vm)
        {
            var now = Environment.TickCount64;
            if (now < NextTick) return;
            NextTick = now + 250;
            if (Table.Count == 0 && AllLive.Count == 0) return;
            foreach (var entity in vm.Entities)
            {
                if (entity is VMAvatar sim && !sim.Dead) Sync(sim);
            }
        }

        private static void ReleaseAll()
        {
            foreach (var (avatarRef, live) in AllLive)
            {
                foreach (var textures in live.Textures.Values) textures.Release();
                live.Textures.Clear();
                if (avatarRef.TryGetTarget(out var avatar)) LiveOf.Remove(avatar);
            }
            AllLive.Clear();
        }

        /// <summary>How the styling of one of a Sim's textures is going, in a few words for the panel.</summary>
        public static string StatusOf(VMAvatar sim, string texture)
        {
            var avatar = sim.Avatar;
            if (avatar == null || !LiveOf.TryGetValue(avatar, out var live) || !live.Textures.TryGetValue(texture, out var textures)) return null;
            if (textures.Failed > 0) return "Could not recolour it: " + textures.LastError;
            if (textures.Waiting > 0) return "Getting the picture ready...";
            if (textures.Styled == 0) return "Not on show: it will be styled when the Sim wears it.";
            return sim.IsPet ? "Recoloured." : "Recoloured, with the skin left as it is.";
        }

        // ---- drawing ----

        /// <summary>A texture reference that gives a styled copy, for the length of one draw.</summary>
        private class StyledRef : ITextureRef
        {
            private readonly Texture2D Copy;
            private readonly ITextureRef Original;
            public StyledRef(Texture2D copy, ITextureRef original) { Copy = copy; Original = original; }
            public Texture2D Get(GraphicsDevice device) => Copy;
            public TexBitmap GetImage() => Original.GetImage();
            public string ReplacePath { get => Original.ReplacePath; set => Original.ReplacePath = value; }
        }

        [ThreadStatic] private static List<(AvatarBindingInstance binding, ITextureRef original)> Swapped;

        /// <summary>
        /// Patched in before Avatar.DrawGeometry: a Sim is about to be drawn. Textures are shared by every Sim who wears
        /// them, so a styled Sim's are swapped for the styled copies only while they are drawn, and put back after.
        /// </summary>
        public static void BeforeDraw(Avatar __instance, GraphicsDevice device)
        {
            PutBack();
            if (__instance == null || !LiveOf.TryGetValue(__instance, out var live) || live.Textures.Count == 0) return;
            lock (__instance.Bindings)
            {
                foreach (var binding in __instance.Bindings)
                {
                    var name = TextureName(binding.Texture);
                    if (name == null || !live.Textures.TryGetValue(name, out var textures)) continue;
                    var original = binding.Texture.Get(device);
                    if (original == null) continue;
                    var mask = MaskOf(name, original, live.Pet);
                    if (mask?.Skin == null) continue; //not ready, or skin cannot be told apart: drawn as it is
                    var copy = textures.Get(original, null, out _, mask.Skin);
                    if (copy == original) continue;
                    (Swapped ??= new List<(AvatarBindingInstance, ITextureRef)>()).Add((binding, binding.Texture));
                    binding.Texture = new StyledRef(copy, binding.Texture);
                }
            }
        }

        public static Exception AfterDraw(Exception __exception)
        {
            PutBack();
            return __exception;
        }

        private static void PutBack()
        {
            if (Swapped == null || Swapped.Count == 0) return;
            foreach (var (binding, original) in Swapped) binding.Texture = original;
            Swapped.Clear();
        }

        // ---- the parts of a Sim, for the panel ----

        /// <summary>
        /// The textures a Sim is wearing, each as a piece for the panel: its style so far, or its parts as they are.
        /// A texture that is all skin (bare hands) is left out.
        /// </summary>
        public static List<StylePiece> PiecesOf(VMAvatar sim)
        {
            var result = new List<(int order, StylePiece piece)>();
            var avatar = sim.Avatar;
            if (avatar == null) return new List<StylePiece>();
            var gd = GameFacade.GraphicsDevice;
            List<AvatarBindingInstance> bindings;
            lock (avatar.Bindings) bindings = avatar.Bindings.ToList();
            var seen = new HashSet<string>();
            foreach (var binding in bindings)
            {
                var name = TextureName(binding.Texture);
                if (name == null || !seen.Add(name)) continue;
                var (order, title) = KindOf(binding, sim.IsPet);
                var piece = new StylePiece { Title = title, Texture = name };

                Texture2D texture = null;
                try { texture = binding.Texture.Get(gd); } catch { }
                var mask = MaskOf(name, texture, sim.IsPet);
                if (texture == null || mask == null) piece.Note = "Its picture is not ready yet. Close this and try again in a moment.";
                else if (mask.Skin == null) piece.Note = mask.Note + " To keep the skin as it is, this one cannot be styled.";
                else
                {
                    if (mask.Free < 0.02f) continue; //all skin
                    var saved = StyleOf(sim, name);
                    piece.Working = saved?.Clone() ?? Analyse(texture, mask);
                }
                result.Add((order, piece));
            }
            //number the pieces that would otherwise share a name
            var pieces = result.OrderBy(x => x.order).Select(x => x.piece).ToList();
            foreach (var group in pieces.GroupBy(x => x.Title).Where(g => g.Count() > 1))
            {
                int n = 0;
                foreach (var piece in group) piece.Title += " " + (++n);
            }
            return pieces;
        }

        /// <summary>The parts of one of a Sim's textures, leaving out its skin.</summary>
        public static Style Analyse(Texture2D texture, SkinMask mask)
        {
            var colours = new Dictionary<uint, int>();
            Pixels.Count(Pixels.Of(texture, null, out _, out _), colours, mask?.Skin);
            return Style.Analyse(colours);
        }

        /// <summary>What a mesh of a Sim is, from the bones it moves with: the head, the hands, or the body.</summary>
        private static (int order, string title) KindOf(AvatarBindingInstance binding, bool pet)
        {
            var bones = binding.Mesh?.BoneBindings?.Select(x => x.BoneName?.ToUpperInvariant() ?? "").ToList() ?? new List<string>();
            if (bones.Contains("HEAD")) return (1, pet ? "Head" : "Hair and hat");
            if (bones.Count > 0 && bones.All(x => x.Contains("HAND") || x.Contains("FINGER"))) return (2, "Hands");
            return (0, pet ? "Coat" : "Clothes");
        }

        // ---- saving ----

        private const byte SaveVersion = 1;

        /// <summary>Where a neighbourhood's Sim styles are kept: this pack's own data, for its Neighborhood.iff.</summary>
        private static string FileFor(string neighbourhood) => Path.Combine(neighbourhood, "Neighborhood.iff");

        /// <summary>A lot has loaded: the Sims there take their styles from the neighbourhood's.</summary>
        public static void LotLoaded(string packId, string lotPath)
        {
            ReleaseAll();
            string neighbourhood = null;
            try { neighbourhood = FSO.Content.Content.Get()?.Neighborhood?.UserPath; } catch { }
            //a lot handed over (from a visit) is of the same neighbourhood, with the styles as they were
            if (lotPath == null && neighbourhood == Neighbourhood) return;
            Neighbourhood = neighbourhood;
            Table = (neighbourhood == null) ? new Dictionary<string, Dictionary<string, Style>>() : Read(LotPackData.Read(packId, FileFor(neighbourhood)));
            Version++;
        }

        /// <summary>A lot has been saved: the neighbourhood's Sim styles are saved with it.</summary>
        public static void Save(string packId)
        {
            if (Neighbourhood == null) return;
            LotPackData.Write(packId, FileFor(Neighbourhood), (Table.Count == 0) ? null : Write(Table));
        }

        public static byte[] Write(Dictionary<string, Dictionary<string, Style>> table)
        {
            using (var mem = new MemoryStream())
            using (var io = new BinaryWriter(mem))
            {
                io.Write(SaveVersion);
                io.Write(table.Count);
                foreach (var sim in table)
                {
                    io.Write(sim.Key);
                    io.Write(sim.Value.Count);
                    foreach (var texture in sim.Value)
                    {
                        io.Write(texture.Key);
                        texture.Value.Write(io);
                    }
                }
                io.Flush();
                return mem.ToArray();
            }
        }

        public static Dictionary<string, Dictionary<string, Style>> Read(byte[] data)
        {
            var result = new Dictionary<string, Dictionary<string, Style>>();
            if (data == null) return result;
            try
            {
                using (var io = new BinaryReader(new MemoryStream(data)))
                {
                    if (io.ReadByte() != SaveVersion) return result;
                    var sims = io.ReadInt32();
                    for (int i = 0; i < sims; i++)
                    {
                        var key = io.ReadString();
                        var count = io.ReadInt32();
                        var styles = new Dictionary<string, Style>();
                        for (int j = 0; j < count; j++)
                        {
                            var texture = io.ReadString();
                            styles[texture] = Style.Read(io);
                        }
                        if (styles.Count > 0) result[key] = styles;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Create a Style: could not read the neighbourhood's Sim styles: " + e.Message);
            }
            return result;
        }

        /// <summary>For tests: the table, as it would be saved.</summary>
        public static Dictionary<string, Dictionary<string, Style>> Snapshot() => Table;
    }
}
