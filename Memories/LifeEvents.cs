/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Memories
{
    /// <summary>
    /// What happened. Each kind says what <see cref="LifeEvent.Sim"/>, <see cref="LifeEvent.Other"/> and the rest
    /// hold for it. A Sim is the GUID of their character; 0 is nobody.
    /// </summary>
    public enum LifeEventKind
    {
        /// <summary>
        /// An interaction a Sim was doing has ended. Sim: who did it. Other: the Sim it was with, if it was with one.
        /// ObjectGuid: what it was with, if not a Sim. Detail: its name, as the game shows it. Names: what the game's
        /// scripts call it (the same in every language). Value: for one with another Sim, 1 if it seems to have gone
        /// well (either of them likes the other more than before), 0 if not (both like each other less), -1 if
        /// nothing changed. Aborted: it was cut short. Raised for every interaction a Sim ends, so a listener picks
        /// out what it wants (see <see cref="Recognise"/>).
        /// </summary>
        Interaction,

        /// <summary>A kiss that went well. Sim: who kissed. Other: who was kissed. Detail: the interaction. Raised once for a couple, however many of them the game's scripts run for it.</summary>
        Kiss,

        /// <summary>Two Sims married: one of the household brought the other in to live there by proposing. Sim: the one of the household. Other: the newcomer.</summary>
        Married,

        /// <summary>A Sim joined the household being played. Sim: the newcomer. Other: the Sim of the household who brought them in, if one did. Detail: the interaction.</summary>
        JoinedHousehold,

        /// <summary>A Sim left the household being played. Sim: who. Others: the household, before. Detail: the interaction, if any.</summary>
        LeftHousehold,

        /// <summary>A baby arrived (the game's baby is an object until it grows up). Sim and Other: its parents, as far as is known (0 if not). ObjectGuid: the baby object. Value: its object ID. Detail: the interaction that made it.</summary>
        BabyArrived,

        /// <summary>A baby grew into a child: a new Sim. Sim: the child. Others: their parents, if known. Began: when the baby arrived, if known.</summary>
        ChildFromBaby,

        /// <summary>A Sim moved to another life stage. Sim: who. Value: the new stage (<see cref="Stage"/>). Detail: its name.</summary>
        GrewUp,

        /// <summary>A Sim got a job, or moved to another career. Sim: who. Value: the job (career) number. Value2: the level. Detail: the job's title and career.</summary>
        GotJob,

        /// <summary>A Sim was promoted. As <see cref="GotJob"/>. Value3 is 1 when it is the top of the career.</summary>
        Promoted,

        /// <summary>A Sim was demoted. As <see cref="GotJob"/>.</summary>
        Demoted,

        /// <summary>A Sim no longer has a job (fired or quit: the game does not say which). Value and Value2: the job and level they had. Detail: the title they had.</summary>
        LostJob,

        /// <summary>A Sim's skill went up a point. Sim: who. Detail: the skill. Value: the new level, 1 to 10.</summary>
        SkillUp,

        /// <summary>A Sim died. Sim: who. Others: the household they were part of, without them. Detail: how it was seen.</summary>
        Died,

        /// <summary>A fire broke out on the lot. Sim: whose doing it was, if a Sim's. Others: the household's Sims on the lot. Detail: the interaction, if any. Value3: 1 if it is the household's own lot.</summary>
        FireStarted,

        /// <summary>The last fire on the lot has gone out. Others: the household's Sims on the lot. Began: when it started.</summary>
        FireOut,

        /// <summary>A burglar has come onto the lot. Sim: the burglar. Others: the household. Value3: 1 if it is the household's own lot.</summary>
        BurglarCame,

        /// <summary>A burglar has gone. Sim: the burglar. Others: the household. Detail: what was seen taken, by name, one to a line, if anything. Value: 1 if the police came. Began: when the burglar came.</summary>
        BurglarGone,
    }

    /// <summary>A life stage, as the Life Stages pack has them. A Sim's stage is read from the age the game keeps for them.</summary>
    public enum Stage { Child = 0, Teen = 1, Adult = 2, Elder = 3 }

    /// <summary>A moment in play: the neighbourhood's day (counted from when this pack first saw it played), and the time on the lot's clock.</summary>
    public struct LifeTime : IComparable<LifeTime>
    {
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }

        public LifeTime(int day, int hour, int minute) { Day = day; Hour = hour; Minute = minute; }

        /// <summary>Minutes since the start of day 0, to compare and subtract.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public long Minutes => Day * 1440L + Hour * 60 + Minute;

        public int CompareTo(LifeTime other) => Minutes.CompareTo(other.Minutes);

        /// <summary>For example "Day 12, 6:05 pm".</summary>
        public override string ToString()
        {
            var hour = Hour % 12 == 0 ? 12 : Hour % 12;
            return "Day " + Day + ", " + hour + ":" + Minute.ToString("00") + (Hour < 12 ? " am" : " pm");
        }
    }

    /// <summary>Something that happened in play. Made by the pack as it happens; listeners should not change it.</summary>
    public sealed class LifeEvent
    {
        public LifeEventKind Kind { get; set; }
        public uint Sim { get; set; }
        public uint Other { get; set; }
        public uint[] Others { get; set; } = Array.Empty<uint>();
        public string Detail { get; set; }
        /// <summary>For <see cref="LifeEventKind.Interaction"/>: what the game's scripts call it (their labels are not translated).</summary>
        public string[] Names { get; set; } = Array.Empty<string>();
        public int Value { get; set; }
        public int Value2 { get; set; }
        public int Value3 { get; set; }
        public uint ObjectGuid { get; set; }
        public bool Aborted { get; set; }
        /// <summary>The house number of the lot it happened on (0 for a community lot).</summary>
        public int House { get; set; }
        public LifeTime When { get; set; }
        /// <summary>When what this ends began, for the kinds that say so.</summary>
        public LifeTime? Began { get; set; }

        public override string ToString()
        {
            var parts = new List<string>() { Kind.ToString() };
            if (Sim != 0) parts.Add("sim " + Sim.ToString("X8"));
            if (Other != 0) parts.Add("other " + Other.ToString("X8"));
            if (Others.Length > 0) parts.Add("others " + string.Join(",", Others.Select(x => x.ToString("X8"))));
            if (!string.IsNullOrEmpty(Detail)) parts.Add("\"" + Detail.Replace("\n", " / ") + "\"");
            if (Value != 0 || Value2 != 0) parts.Add("value " + Value + "/" + Value2);
            if (Aborted) parts.Add("aborted");
            parts.Add("house " + House);
            parts.Add(When.ToString());
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Life events as they happen, for anything that wants to know: this pack's own memories, and other packs (Wants,
    /// one day). Raised on the game thread, while the game is running the script that caused it, so a listener should
    /// be quick and must not change the game from here: put that off with GameThread.NextUpdate.
    ///
    /// Another pack reaches this through its own copy of nothing: it refers to Memories.dll without shipping it, and
    /// finds this pack's when it loads (see the README, "For other packs").
    /// </summary>
    public static class LifeEvents
    {
        /// <summary>Every life event, as it happens.</summary>
        public static event Action<LifeEvent> Happened;

        /// <summary>How many listeners have failed. Each failure is written to the log; the others still hear.</summary>
        public static int ListenerFailures { get; private set; }

        /// <summary>The most recent events, oldest first, for a listener that came late (and for the console).</summary>
        public static IReadOnlyList<LifeEvent> Recent => RecentEvents;
        private static readonly List<LifeEvent> RecentEvents = new List<LifeEvent>();
        public const int RecentKept = 50;

        /// <summary>Tell every listener. One that throws is written to the log, and does not stop the rest.</summary>
        public static void Raise(LifeEvent e)
        {
            if (e == null) return;
            RecentEvents.Add(e);
            if (RecentEvents.Count > RecentKept) RecentEvents.RemoveAt(0);
            if (e.Kind != LifeEventKind.Interaction || e.Other != 0) Log.Write("event: " + e);
            var listeners = Happened;
            if (listeners == null) return;
            foreach (Action<LifeEvent> listener in listeners.GetInvocationList())
            {
                try
                {
                    listener(e);
                }
                catch (Exception ex)
                {
                    if (++ListenerFailures <= 20) Log.Write("A listener to life events failed (" + listener.Method.DeclaringType?.FullName + "." + listener.Method.Name + "): " + ex);
                }
            }
        }
    }
}
