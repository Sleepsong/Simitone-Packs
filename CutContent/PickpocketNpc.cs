/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Content;
using FSO.Content.Model;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CutContent
{
    /// <summary>
    /// The Vacation pickpocket, built.
    ///
    /// All Maxis left of him is his look (see Pickpocket.cs): a moustached face and beach, forest and lodge outfits for the
    /// body Vacation's male sales clerks wear, a tourist's disguise for each part of the island; and a masked face with the
    /// burglar's skinny body in a striped shirt, the thief's own look. No script, text or menu entry of his survives. So he
    /// is made here, from what the game has:
    ///   - his object is a copy of the male sales clerk's (whose body his outfits fit), made in memory like the kiosk, with
    ///     his own clothes, an animation table naming the Psycho Mime's stealing animation, and scripts of his own;
    ///   - on a Vacation lot, now and then in the day, he arrives through the lot's pedestrian portal dressed as a tourist for
    ///     that part of the island (sand: beach; snow: lodge; otherwise forest), and wanders about for a while;
    ///   - then he walks up behind a household Sim and lifts their wallet: $20 to $100 from the household. The victim may
    ///     catch him at it, the more likely the better their body skill: then he drops the money, his disguise is gone (the
    ///     striped shirt and the mask) and he runs. Either way he then leaves, by the game's own "Leave".
    /// "cutcontent pickpocket come" brings him to any lot at once, to try him out.
    /// He is never saved with a lot: one left on a lot that was saved is sent away when it loads.
    /// </summary>
    public static class PickpocketNpc
    {
        public const uint Guid = 0x6C7C0B0B; //his own number, made up: not one the game uses
        private const uint ClerkGuid = 0x24a1a980; //NPC - Vacation - Sales Clerk - Male - LGT
        private const int MarkSlot = 38; //a person data slot no script reads or sets (the survey's scan)
        private const short Mark = 0x5050;
        public const string InitName = "cutcontent_pickpocket_init", ThinkName = "cutcontent_pickpocket", StealName = "cutcontent_pickpocket_steal";

        public static string Status = "not made yet";
        public static bool Automatic = true;
        public static int Came, Picked, CaughtCount;
        private static readonly Random Dice = new Random();
        private static OBJD Made;

        private const string DisguiseHead = "C_Burglar,HEAD-HEAD=cvacationmalgt_pick";
        private const string ThiefBody = "B003MASkn_01,BODY=vacationmasknlgt_pickpocket";
        private const string ThiefHead = "C_Burglar,HEAD-HEAD=cvacationmalgt_pickpocket";
        private static string DisguiseBody(string area, char variant) => $"VacationMAfit_Salesclerk,BODY=vacationmafitlgt_pick{area}{variant}";

        /// <summary>What his scripts are told to do next, in Temp 0.</summary>
        private enum Plan : short { Wait = 0, Steal = 1, Leave = 2, Gone = 3, Wander = 4 }

        private class Visit
        {
            public int Arrived = -1, Casing, Attempts, DoneAt = -1;
            public bool Done, LeaveSent, Quick;
            public Plan Last = (Plan)(-1);
        }
        private static readonly ConditionalWeakTable<VMAvatar, Visit> Visits = new ConditionalWeakTable<VMAvatar, Visit>();

        private class Lot { public int LastMinute = -1, Came; }
        private static readonly ConditionalWeakTable<VM, Lot> Lots = new ConditionalWeakTable<VM, Lot>();

        // ---- making him ----

        /// <summary>Makes his object, once the game's content is ready and the pack's primitives are registered.</summary>
        public static void Make()
        {
            var objects = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objects == null) { Status = "not a Sims 1 game"; return; }
            lock (objects.Entries) if (objects.Entries.ContainsKey(Guid)) { Status = $"not made: the number he would have, {Guid:x8}, is taken"; return; }
            var clerk = objects.Get(ClerkGuid);
            var res = clerk?.Resource;
            if (res?.MainIff == null) { Status = "not made: he needs Vacation (its male sales clerk was not found)"; return; }
            var iff = res.MainIff;

            //clothes: the clerk's, with the disguise for the body and the moustached face for the head
            var clerkClothes = res.Get<STR>(clerk.OBJ.BodyStringID);
            var lines = Enumerable.Range(0, clerkClothes?.Length ?? 0).Select(i => clerkClothes.GetString(i) ?? "").ToList();
            if (lines.Count < 3) { Status = "not made: the clerk has no clothes list"; return; }
            lines[1] = DisguiseBody("forest", 'a');
            lines[2] = DisguiseHead;
            var clothes = MadeObjects.Texts(MadeObjects.FreeTextID(res, 0x6C70), "Pickpocket - clothes (Cut Content)", lines);
            var animationsId = MadeObjects.FreeTextID(res, (ushort)(clothes.ChunkID + 1));
            var animations = MadeObjects.Texts(animationsId, "Pickpocket - animations (Cut Content)", new[] { "a2o-mime-steal", "a2o-putinpocket-rarm" });

            //scripts
            var like = res.List<BHAV>()?.FirstOrDefault();
            var init = Scripts.Add(res, "Pickpocket - init (Cut Content)", new[] { Scripts.Step(InitName, new byte[8], Scripts.ReturnTrue, Scripts.ReturnTrue) }, like);
            var leave = GlobalScript("Leave", 368);
            const byte T = 8, L = 7, P = 9; //temps, literal, parameters
            var steps = new[]
            {
                /*0*/ Scripts.Step(ThinkName, new byte[8], 1, 1),
                /*1*/ Scripts.Step("expression", Scripts.Expression(0, T, 2, (short)Plan.Steal, L), 2, 5),
                /*2*/ Scripts.Step("goto_relative", new byte[] { 0, 0, 4, 0xFE, 0, 0, 0, 0 }, 3, 9), //behind the victim, facing them
                /*3*/ Scripts.Step("animate", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, 4, 4), //a2o-mime-steal, from his own table
                /*4*/ Scripts.Step(StealName, new byte[8], 9, 9),
                /*5*/ Scripts.Step("expression", Scripts.Expression(0, T, 2, (short)Plan.Leave, L), 10, 6),
                /*6*/ Scripts.Step("expression", Scripts.Expression(0, T, 2, (short)Plan.Wander, L), 7, 8),
                /*7*/ Scripts.Step("goto_relative", new byte[] { 0, 0, 0xFF, 0xFF, 0, 0, 0, 0 }, 9, 9), //anywhere near the object picked
                /*8*/ Scripts.Step("expression", Scripts.Expression(0, T, 2, (short)Plan.Gone, L), 11, 9),
                /*9*/ Scripts.Step("expression", Scripts.Expression(0, P, 5, 30, L), 12, 12), //wait 30 ticks
                /*10*/ new BHAVInstruction { Opcode = leave, Operand = Enumerable.Repeat((byte)0xFF, 8).ToArray(), TruePointer = 11, FalsePointer = 11 },
                /*11*/ Scripts.Step("remove_object_instance", new byte[8], Scripts.ReturnTrue, Scripts.ReturnTrue),
                /*12*/ Scripts.Step("sleep", new byte[8], 0, 0),
            };
            var main = Scripts.Add(res, "Pickpocket - main (Cut Content)", steps, like);
            main.Args = 4; //"sleep" counts down argument 0
            Scripts.Reassemble(res, main);

            //his object: the clerk's definition, with his own scripts, clothes and animations, and no menu
            var objd = MadeObjects.Copy(iff, clerk.OBJ);
            objd.GUID = Guid;
            objd.ChunkID = (ushort)((iff.List<OBJD>() ?? new List<OBJD>()).Max(o => o.ChunkID) + 1);
            objd.ChunkLabel = "NPC - Vacation - Pickpocket (Cut Content)";
            objd.UsesFnTable = 0;
            foreach (var prop in typeof(OBJD).GetProperties().Where(p => p.Name.StartsWith("BHAV_") && p.CanWrite && p.PropertyType == typeof(ushort)))
                prop.SetValue(objd, (ushort)0);
            objd.BHAV_MainID = main.ChunkID;
            objd.BHAV_Init = init.ChunkID;
            objd.BodyStringID = clothes.ChunkID;
            objd.AnimationTableID = animations.ChunkID;
            objd.TreeTableID = 65535;
            objd = MadeObjects.Copy(iff, objd);
            MadeObjects.Add(res, ClerkGuid, objd, new List<IffChunk> { clothes, animations, init, main, objd });
            Made = objd;
            Status = "made: he comes to Vacation lots now and then in the day";
            CutContentPack.Log($"pickpocket: made {Guid:x8} in {iff.Filename}: main {main.ChunkID}, init {init.ChunkID}, clothes {clothes.ChunkID}, animations {animations.ChunkID}, leave {leave}");
        }

        /// <summary>A global script's number, by its name, or the number it has in 1.0.</summary>
        private static ushort GlobalScript(string name, ushort fallback)
        {
            try
            {
                var global = Content.Get().WorldObjectGlobals.Get("global")?.Resource;
                var found = global?.List<BHAV>()?.FirstOrDefault(x => string.Equals((x.ChunkLabel ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found.ChunkID;
            }
            catch { }
            return fallback;
        }

        public static bool IsPickpocket(VMEntity e) => e is VMAvatar && e.Object?.OBJ?.GUID == Guid;

        // ---- bringing him ----

        /// <summary>
        /// Brings him to the lot now: made off the lot, then put beside its pedestrian portal (or a household Sim). Says what
        /// stopped him, if anything did, and logs each stage. Brought by the command ("quick"), he looks about for only a few
        /// minutes before he goes for someone.
        /// </summary>
        public static string Come(VM vm, bool quick = false)
        {
            var stage = "starting";
            try
            {
                if (Made == null) return "He isn't here: " + Status + ".";
                var already = vm.Context.ObjectQueries.Avatars.Where(IsPickpocket).OfType<VMAvatar>().ToList();
                if (already.Count > 0) return $"He is already on this lot, at {Where(already[0])}. " + Doing(vm, already[0]);

                stage = "making him";
                CutContentPack.Log("pickpocket: coming");
                var group = vm.Context.CreateObjectInstance(Guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH);
                var me = group?.Objects.OfType<VMAvatar>().FirstOrDefault();
                if (me == null) return $"He could not be made on this lot (the game gave back {(group == null ? "nothing" : group.Objects.Count + " objects, none a person")}).";
                Visits.Add(me, new Visit { Quick = quick });

                stage = "finding where he arrives";
                bool OnLot(VMEntity e) => e.Position.x != LotTilePos.OUT_OF_WORLD.x;
                var reference = vm.Entities.FirstOrDefault(e => !(e is VMAvatar) && OnLot(e) && Regex.IsMatch(Label(e), @"ped ?portal", RegexOptions.IgnoreCase))
                    ?? vm.Entities.FirstOrDefault(e => !(e is VMAvatar) && OnLot(e) && Regex.IsMatch(Label(e), "portal", RegexOptions.IgnoreCase) && !Regex.IsMatch(Label(e), @"car|phone|magic", RegexOptions.IgnoreCase))
                    ?? Household(vm, me).FirstOrDefault();
                if (reference == null) { me.Delete(true, vm.Context); return "There was nowhere for him to arrive: no pedestrian portal and no household Sim on the lot."; }
                stage = $"placing him near \"{Label(reference)}\"";
                if (!VMFindLocationFor.FindLocationFor(me, reference, vm.Context, VMPlaceRequestFlags.Default))
                {
                    me.Delete(true, vm.Context);
                    return $"There was no room for him near \"{Label(reference)}\".";
                }
                stage = "dressing him";
                var area = Dress(vm, me);
                Came++;
                var text = $"A pickpocket has arrived at {Where(me)}, dressed as a tourist for the {area}, near \"{Label(reference)}\". " +
                    (quick ? "He'll look about for a few minutes, then go for one of your Sims." : "He'll look about for a while first.");
                CutContentPack.Log("pickpocket: " + text);
                return text;
            }
            catch (Exception e)
            {
                CutContentPack.Log($"pickpocket: failed while {stage}: {e}");
                //where it failed, so a picture of the message is enough: the first few places in the code, without the file paths
                var where = string.Join("\n", (e.StackTrace ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.StartsWith("at ")).Take(6)
                    .Select(x => System.Text.RegularExpressions.Regex.Replace(x, @" in .*$", "")));
                return $"He couldn't come: something failed while {stage}: {e.GetType().Name}: {e.Message}\n{where}\n(The whole of it is in cutcontent.log.)";
            }
        }

        private static string Label(VMEntity e) => e.Object?.OBJ?.ChunkLabel ?? "";
        private static string Where(VMEntity e) => $"tile {e.Position.TileX},{e.Position.TileY}, level {e.Position.Level}";

        /// <summary>What he is doing, for the command's list.</summary>
        private static string Doing(VM vm, VMAvatar me)
        {
            if (!Visits.TryGetValue(me, out var visit)) return "He was saved with the lot, and is being sent away.";
            if (visit.Arrived < 0) return "He has only just arrived.";
            var since = Since(visit.Arrived, Now(vm));
            if (visit.Done) return "He is leaving.";
            if (since < visit.Casing) return $"He is looking about: {visit.Casing - since} more minutes before he goes for someone.";
            return $"He is going for one of your Sims (try {visit.Attempts} of 4).";
        }

        /// <summary>A line for the command's list: whether he is on this lot, where, and what he is doing.</summary>
        public static string Here(VM vm)
        {
            if (vm == null) return "No lot is loaded.";
            var me = vm.Context.ObjectQueries.Avatars.Where(IsPickpocket).OfType<VMAvatar>().FirstOrDefault();
            return me == null ? "He is not on this lot." : $"He is on this lot, at {Where(me)}. " + Doing(vm, me);
        }

        /// <summary>The disguise for this part of the island: sand is the beach, snow the lodge, the rest the forest.</summary>
        private static string Dress(VM vm, VMAvatar me)
        {
            var ground = vm.Context.Architecture?.Terrain?.LightType ?? TerrainType.GRASS;
            var area = ground == TerrainType.SAND ? "beach" : ground == TerrainType.SNOW ? "lodge" : "forest";
            me.BodyOutfit = Pickpocket.Body(me, DisguiseBody(area, Dice.Next(2) == 0 ? 'a' : 'b'));
            me.HeadOutfit = new VMOutfitReference(DisguiseHead, true);
            return area;
        }

        private static IEnumerable<VMAvatar> Household(VM vm, VMAvatar me)
        {
            var family = vm.TS1State?.CurrentFamily;
            if (family == null) return Enumerable.Empty<VMAvatar>();
            return vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().Where(x => x != me && !x.IsPet && x.Position.x != LotTilePos.OUT_OF_WORLD.x
                && x.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) == family.ChunkID);
        }

        private static int Now(VM vm) => vm.Context.Clock.Hours * 60 + vm.Context.Clock.Minutes;
        private static int Since(int then, int now) => (now - then + 1440) % 1440;

        /// <summary>
        /// Once a game minute: on a Vacation lot, between 8 in the morning and 9 at night, about once in four hours (and no more
        /// than twice a visit), he comes. One left over from a saved lot, or still about long after he was done, is sent away.
        /// </summary>
        public static void AfterTick(VM __instance)
        {
            var vm = __instance;
            if (!vm.TS1 || Made == null) return;
            var lot = Lots.GetOrCreateValue(vm);
            var minute = Now(vm);
            if (minute == lot.LastMinute) return;
            lot.LastMinute = minute;
            try
            {
                var here = vm.Context.ObjectQueries.Avatars.Where(IsPickpocket).OfType<VMAvatar>().ToList();
                foreach (var me in here)
                {
                    if (!Visits.TryGetValue(me, out var visit)) { me.Delete(true, vm.Context); CutContentPack.Log("pickpocket: one saved with the lot was sent away"); continue; }
                    if (visit.Done && visit.DoneAt >= 0 && Since(visit.DoneAt, minute) > 45) { me.Delete(true, vm.Context); CutContentPack.Log("pickpocket: he was still about long after, and was sent away"); }
                }
                if (here.Count > 0 || !Automatic) return;
                var house = vm.GetGlobalValue(10);
                if (house < 40 || house > 49) return; //Vacation lots
                if (minute < 8 * 60 || minute > 21 * 60 || lot.Came >= 2) return;
                if (!Household(vm, null).Any() || Dice.Next(240) != 0) return;
                var text = Come(vm);
                if (text.StartsWith("A pickpocket")) lot.Came++;
            }
            catch (Exception e) { CutContentPack.Log("pickpocket: " + e.Message); }
        }

        /// <summary>Sends him off: he leaves the next time he thinks.</summary>
        public static string SendAway(VM vm)
        {
            var any = false;
            foreach (var me in vm.Context.ObjectQueries.Avatars.Where(IsPickpocket).OfType<VMAvatar>())
            {
                var visit = Visits.GetOrCreateValue(me);
                visit.Done = true;
                visit.DoneAt = Now(vm);
                any = true;
            }
            return any ? "The pickpocket is leaving." : "There is no pickpocket on this lot.";
        }

        // ---- his primitives ----

        /// <summary>When he is made: an adult man, not of the household, marked as the pickpocket.</summary>
        public class InitPrimitive : VMPrimitiveHandler
        {
            public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand args)
            {
                if (context.Caller is VMAvatar me)
                {
                    me.SetPersonData(VMPersonDataVariable.PersonsAge, 27);
                    me.SetPersonData(VMPersonDataVariable.Gender, 0);
                    me.SetPersonData(VMPersonDataVariable.PersonType, 1);
                    me.SetPersonData(VMPersonDataVariable.TS1FamilyNumber, -1);
                    me.SetPersonData((VMPersonDataVariable)MarkSlot, Mark);
                }
                return VMPrimitiveExitCode.GOTO_TRUE;
            }
        }

        /// <summary>
        /// What next, into Temp 0, with the stack object set to whom or what: wander about for 30 to 90 minutes, sometimes
        /// walking over to something; then go for a household Sim (four tries); then leave; then go.
        /// </summary>
        public class ThinkPrimitive : VMPrimitiveHandler
        {
            public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand args)
            {
                var plan = Plan.Wait;
                try
                {
                    var me = context.Caller as VMAvatar;
                    var vm = context.VM;
                    var visit = Visits.GetOrCreateValue(me);
                    var now = Now(vm);
                    if (visit.Arrived < 0) { visit.Arrived = now; visit.Casing = visit.Quick ? 2 + Dice.Next(4) : 30 + Dice.Next(61); }

                    if (visit.Done)
                    {
                        if (visit.DoneAt < 0) visit.DoneAt = now;
                        plan = visit.LeaveSent ? Plan.Gone : Plan.Leave;
                        visit.LeaveSent = true;
                    }
                    else if (Since(visit.Arrived, now) < visit.Casing)
                    {
                        if (Dice.Next(10) < 3)
                        {
                            var things = vm.Entities.Where(e => !(e is VMAvatar) && e.Position.x != LotTilePos.OUT_OF_WORLD.x && e.Position.Level == me.Position.Level
                                && e.Object?.OBJ?.ObjectType == OBJDType.Normal).ToList();
                            if (things.Count > 0) { context.StackObject = things[Dice.Next(things.Count)]; plan = Plan.Wander; }
                        }
                    }
                    else if (visit.Attempts < 4)
                    {
                        var victims = Household(vm, me).ToList();
                        if (victims.Count == 0) { visit.Done = true; plan = Plan.Wait; }
                        else
                        {
                            visit.Attempts++;
                            context.StackObject = victims[Dice.Next(victims.Count)];
                            plan = Plan.Steal;
                        }
                    }
                    else visit.Done = true; //he never got close: he gives up
                }
                catch (Exception e) { CutContentPack.Log("pickpocket: " + e.Message); }
                try
                {
                    var v = Visits.GetOrCreateValue((VMAvatar)context.Caller);
                    if (v.Last != plan) CutContentPack.Log($"pickpocket: {plan}{(plan == Plan.Steal || plan == Plan.Wander ? " " + Label(context.StackObject) + (context.StackObject is VMAvatar a ? " " + a.Name : "") : "")}");
                    v.Last = plan;
                }
                catch { }
                context.Thread.TempRegisters[0] = (short)plan;
                return VMPrimitiveExitCode.GOTO_TRUE;
            }
        }

        /// <summary>
        /// Behind his victim: $20 to $100 from the household, unless he is caught at it, which is likelier the better the
        /// victim's body skill (one in five, to seven in ten). Caught, he drops it, his disguise is gone and he runs.
        /// </summary>
        public class StealPrimitive : VMPrimitiveHandler
        {
            public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand args)
            {
                try
                {
                    var me = context.Caller as VMAvatar;
                    var victim = context.StackObject as VMAvatar;
                    var vm = context.VM;
                    var family = vm.TS1State?.CurrentFamily;
                    if (me == null || victim == null || family == null) return VMPrimitiveExitCode.GOTO_TRUE;
                    if (victim.Position.Level != me.Position.Level || LotTilePos.Distance(victim.Position, me.Position) > 16 * 3) return VMPrimitiveExitCode.GOTO_TRUE; //they moved away: try again

                    var visit = Visits.GetOrCreateValue(me);
                    visit.Done = true;
                    visit.DoneAt = Now(vm);
                    var amount = Math.Min(family.Budget, 20 + 5 * Dice.Next(17));
                    if (amount <= 0) { CutContentPack.Show("Pickpocket", $"Someone went through {victim.Name}'s pockets, and found them empty.", vm); return VMPrimitiveExitCode.GOTO_TRUE; }
                    var body = Math.Max(0, Math.Min(1000, (int)victim.GetPersonData(VMPersonDataVariable.BodySkill)));
                    if (Dice.NextDouble() < 0.2 + 0.5 * body / 1000.0)
                    {
                        me.BodyOutfit = Pickpocket.Body(me, ThiefBody);
                        me.HeadOutfit = new VMOutfitReference(ThiefHead, true);
                        me.SetValue(VMStackObjectVariable.WalkStyle, 1); //runs
                        CaughtCount++;
                        CutContentPack.Show("Pickpocket", $"{victim.Name} caught a pickpocket with his hand in their pocket! He drops the ${amount}, his tourist act is over, and he runs for it.", vm);
                    }
                    else
                    {
                        family.Budget -= amount;
                        Picked++;
                        CutContentPack.Show("Pickpocket", $"{victim.Name} has had their pocket picked! ${amount} is gone. That \"tourist\" who brushed past is already on his way out.", vm);
                    }
                }
                catch (Exception e) { CutContentPack.Log("pickpocket: " + e.Message); }
                return VMPrimitiveExitCode.GOTO_TRUE;
            }
        }
    }
}
