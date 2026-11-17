/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspirations
{
    /// <summary>
    /// The pack's dialogs share a way of being built: a body that is made again whenever what it shows changes, and
    /// rides in and out with the dialog's stripes, as the Family Tree pack's window does.
    /// </summary>
    public abstract class UIPackDialog : UIMobileDialog
    {
        protected const int Margin = 60, Top = 100;
        protected UIContainer Body;
        private string Shown;
        private int Wait;

        protected float Inner => Width - Margin * 2;

        /// <summary>What the body shows, as a string: when it changes, the body is made again.</summary>
        protected abstract string Signature();

        /// <summary>Make the body. Returns how tall it is.</summary>
        protected abstract float Build(UIContainer body);

        protected void Refresh(bool always = false)
        {
            var signature = "";
            AspirationsPack.Guard("looking at the window", () => signature = Signature());
            if (!always && signature == Shown) return;
            Shown = signature;
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);
            float height = 0;
            if (!AspirationsPack.Guard("drawing the window", () => height = Build(Body))) height = 200;
            SetHeight((int)height);
        }

        public override void GameResized()
        {
            base.GameResized();
            Refresh(true);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (Body != null) Body.X = ((Closing) ? 1 : -1) * (1 - InterpolatedAnimation) * Width;
            if (Closing || --Wait > 0) return;
            Wait = 20;
            Refresh();
        }

        protected UIBigButton BigButton(UIContainer body, string caption, float x, float y, float width, bool green, Action click)
        {
            var button = new UIBigButton(green);
            button.Caption = caption;
            button.Width = width;
            //a caption that does not fit is made smaller, as far as it stays readable
            while (button.CaptionStyle.Size > 20 && button.CaptionStyle.MeasureString(caption).X > width - 30) button.CaptionStyle.Size -= 2;
            button.Position = new Vector2(x, y);
            button.OnButtonClick += (b) => AspirationsPack.Guard("pressing " + caption, click);
            body.Add(button);
            return button;
        }

        /// <summary>A row of big buttons, centred: their captions and what each does.</summary>
        protected float ButtonRow(UIContainer body, float y, params (string caption, bool green, Action click)[] buttons)
        {
            const float gap = 25;
            var width = Math.Min(275, (Inner - gap * (buttons.Length - 1)) / buttons.Length);
            var x = (Width - (width * buttons.Length + gap * (buttons.Length - 1))) / 2;
            foreach (var button in buttons)
            {
                BigButton(body, button.caption, x, y, width, button.green, button.click);
                x += width + gap;
            }
            return y + 125;
        }

        protected UILabel Text(UIContainer body, float x, float y, int size, Color colour, string text, float width)
        {
            var label = Labels.Make(body, new Vector2(x, y), size, colour);
            Labels.Fit(label, text, size, width);
            return label;
        }

        /// <summary>A paragraph, broken into lines. Returns where the next thing goes.</summary>
        protected float Paragraph(UIContainer body, float x, float y, int size, Color colour, string text, float width, int maxLines = 4)
        {
            var style = TextStyle.DefaultLabel.Clone();
            style.Size = size;
            foreach (var line in Labels.Wrap(style, text, width, maxLines))
            {
                Labels.Make(body, new Vector2(x, y), size, colour, line);
                y += size + 8;
            }
            return y;
        }
    }

    /// <summary>A bar of the meter's levels, drawn in the window.</summary>
    public class UIMeterBar : UIElement
    {
        public double Meter;
        public Vector2 Box;
        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            MeterDrawing.Draw(this, batch, Vector2.Zero, Box, Meter, Watcher.Balance, false);
        }
    }

    /// <summary>
    /// A Sim's aspiration, in full: the meter and its levels, the wants (to lock or not) and the fears, the
    /// lifetime want, rewards in effect, and the way to the rewards. Opened with F7, the button in Options, the
    /// meter beside the needs, or `aspire window`.
    /// </summary>
    public class UIAspirationWindow : UIPackDialog
    {
        private static UIAspirationWindow Current;
        private VMAvatar Sim;

        public static VMAvatar PlayedSim(VMAvatar preferred = null)
        {
            var screen = AspirationsPack.Screen;
            if (screen?.vm == null) return null;
            var household = Watcher.Household(screen.vm);
            if (preferred != null && household.Contains(preferred)) return preferred;
            var selected = screen.SelectedAvatar;
            if (selected != null && household.Contains(selected)) return selected;
            return household.FirstOrDefault();
        }

        public static void Open(VMAvatar avatar = null)
        {
            var sim = PlayedSim(avatar);
            if (sim == null || Watcher.Ledger == null)
            {
                AspirationsPack.Say("Aspiration", "Open this while you are playing a household, with one of its people on the lot.");
                return;
            }
            if (Current != null && Current.Parent != null)
            {
                Current.Sim = sim;
                Current.Refresh(true);
                return;
            }
            Current = new UIAspirationWindow(sim);
            UIScreen.GlobalShowDialog(Current, true);
        }

        private UIAspirationWindow(VMAvatar sim)
        {
            Sim = sim;
            Refresh(true);
        }

        private void CloseMe()
        {
            if (Current == this) Current = null;
            Close();
        }

        private SimRecord Record => Watcher.Ledger?.Find(Sim.Object.OBJ.GUID);

        protected override string Signature()
        {
            var r = Record;
            var b = Watcher.Balance;
            if (r == null) return "none";
            return Sim.ObjectID + "|" + (int)Math.Round(r.Meter) + "|" + r.Points + "|" + r.Aspiration + "|" + r.Chosen + "|" + r.LifetimeDone + "|" + (r.Lifetime?.Text ?? "")
                + "|" + string.Join(",", r.Wants.Select(x => x.Text + x.Locked)) + "|" + string.Join(",", r.Fears.Select(x => x.Text))
                + "|" + string.Join(",", r.Effects.Select(x => x.Name + Effects.Remaining(x, r))) + "|" + b.GetHashCode() + "|" + Watcher.Ledger.Settings.On;
        }

        protected override float Build(UIContainer body)
        {
            var b = Watcher.Balance;
            var r = Record;
            var style = UIStyle.Current;
            Caption = "Aspiration: " + Sim.Name;
            float y = Top;
            if (r == null || r.Aspiration == null)
            {
                y = Paragraph(body, Margin, y, 16, style.DialogText, Sim.Name + " has no aspiration yet. One is given within moments of the lot being played.", Inner);
                return ButtonRow(body, y + 20, ("Close", true, CloseMe));
            }
            var aspiration = b.Aspiration(r.Aspiration);
            var level = Rules.LevelIndex(r.Meter, b);
            var levelDef = b.Levels[level];

            // the aspiration, and the points
            var name = Text(body, Margin, y, 24, style.DialogText, aspiration?.Name ?? r.Aspiration, 260);
            var nameWidth = Math.Min(260, name.CaptionStyle.MeasureString(name.Caption).X);
            if (!r.Child)
            {
                var change = new UITextButton(r.Chosen ? "Change" : "Choose", 110, 30, 14) { Position = new Vector2(Margin + nameWidth + 20, y + 2) };
                change.OnClick += (x) => UIAspirationChooser.Open(Sim, false, null);
                body.Add(change);
            }
            var points = Rules.Number(r.Points) + " reward points";
            var pointsLabel = Text(body, 0, y + 4, 19, Palette.Gold, points, 320);
            pointsLabel.X = Width - Margin - pointsLabel.CaptionStyle.MeasureString(pointsLabel.Caption).X;
            y += 38;
            var about = (aspiration?.About ?? "") + (r.Chosen || r.Child ? "" : " Chance chose this aspiration: Choose to change it.");
            y = Paragraph(body, Margin, y, 13, style.SecondaryText, about, Inner, 2) + 4;
            if (!Watcher.Ledger.Settings.On) y = Paragraph(body, Margin, y, 14, Palette.Fear, "Aspirations are switched off: nothing changes until they are on again (aspire on).", Inner, 2);

            // the meter
            var bar = new UIMeterBar() { Meter = r.Meter, Box = new Vector2(Inner, 20), Position = new Vector2(Margin, y + 4) };
            body.Add(bar);
            y += 30;
            for (int i = 0; i < b.Levels.Count; i++)
            {
                var from = MeterDrawing.At(b.Levels[i].From, Inner);
                var to = i + 1 < b.Levels.Count ? MeterDrawing.At(b.Levels[i + 1].From, Inner) : Inner;
                Text(body, Margin + from + 3, y, 11, i == level ? Palette.ForLevel(i, b.Levels.Count) : style.DialogText * 0.6f, b.Levels[i].Name, to - from - 6);
            }
            y += 22;
            var risk = levelDef.Backfire;
            var levelText = levelDef.Name + ". " + Pace(levelDef) + " " + (risk > 0 ? "A reward used now has a " + (risk * 100).ToString("0") + "% chance of going wrong." : "Rewards cannot go wrong.");
            if (r.LifetimeDone) levelText += " The lifetime want is fulfilled: the meter stays at " + Rules.Level(b.Meter.LifetimeFloor, b).Name + " or above.";
            y = Paragraph(body, Margin, y, 14, Palette.ForLevel(level, b.Levels.Count), levelText, Inner, 2) + 8;

            // wants and fears, side by side
            var column = (Inner - 30) / 2;
            var fearsX = Margin + column + 30;
            Text(body, Margin, y, 19, Palette.Want, "Wants", column);
            Text(body, fearsX, y, 19, Palette.Fear, "Fears", column);
            y += 32;
            const float row = 36;
            for (int i = 0; i < Math.Max(b.Slots.Wants, b.Slots.Fears); i++)
            {
                if (i < b.Slots.Wants)
                {
                    var want = i < r.Wants.Count ? r.Wants[i] : null;
                    if (want == null) Text(body, Margin, y + i * row + 4, 15, style.DialogText * 0.5f, "(nothing yet)", column);
                    else
                    {
                        var lockWidth = b.Slots.Locks > 0 ? 80 : 0;
                        Text(body, Margin, y + i * row + 4, 15, style.DialogText, want.Text, column - 90 - lockWidth);
                        var p = Text(body, 0, y + i * row + 4, 15, Palette.Want, "+" + Rules.Number(want.Points), 90);
                        p.X = Margin + column - lockWidth - 10 - p.CaptionStyle.MeasureString(p.Caption).X;
                        if (b.Slots.Locks > 0)
                        {
                            var index = i;
                            var lockButton = new UITextButton(want.Locked ? "Locked" : "Lock", 70, 28, 13) { Position = new Vector2(Margin + column - 70, y + i * row + 1), Selected = want.Locked };
                            lockButton.OnClick += (x) =>
                            {
                                var problem = Aspiration.Lock(r, index, b);
                                if (problem != null) AspirationsPack.Notice(problem, NoticeKind.Info);
                                Refresh(true);
                            };
                            body.Add(lockButton);
                        }
                    }
                }
                if (i < b.Slots.Fears)
                {
                    var fear = i < r.Fears.Count ? r.Fears[i] : null;
                    if (fear == null) Text(body, fearsX, y + i * row + 4, 15, style.DialogText * 0.5f, "(nothing)", column);
                    else
                    {
                        Text(body, fearsX, y + i * row + 4, 15, style.DialogText, fear.Text, column - 90);
                        var p = Text(body, 0, y + i * row + 4, 15, Palette.Fear, "-" + Rules.Number(fear.Points), 90);
                        p.X = fearsX + column - p.CaptionStyle.MeasureString(p.Caption).X;
                    }
                }
            }
            y += Math.Max(b.Slots.Wants, b.Slots.Fears) * row + 8;

            // for life
            string lifetime;
            if (r.Child) lifetime = "Children have no lifetime want: they are given one when they grow up.";
            else if (r.LifetimeDone) lifetime = "Lifetime want fulfilled" + (r.Lifetime != null ? ": " + r.Lifetime.Text : "") + ".";
            else if (r.Lifetime != null) lifetime = "Lifetime want: " + r.Lifetime.Text + "  (+" + Rules.Number(r.Lifetime.Points) + ")";
            else lifetime = "No lifetime want.";
            Text(body, Margin, y, 16, r.LifetimeDone ? Palette.Platinum : Palette.Gold, lifetime, Inner);
            y += 30;
            if (r.Effects.Count > 0)
            {
                y = Paragraph(body, Margin, y, 14, style.SkillActive, "In effect: " + string.Join("; ", r.Effects.Select(e => e.Name + " (" + Effects.Remaining(e, r) + ")")) + ".", Inner, 2);
            }
            Text(body, Margin, y, 12, style.DialogText * 0.6f, r.Fulfilled + " wants fulfilled, " + r.Realized + " fears come true, " + r.RewardsUsed + " rewards used. Wants not locked, and fears, change when " + Sim.Name + " wakes up.", Inner);
            y += 26;

            var household = Watcher.Household(AspirationsPack.Screen?.vm);
            var buttons = new List<(string, bool, Action)>();
            buttons.Add(("Rewards", true, () => UIRewardsWindow.Open(Sim)));
            if (household.Count > 1)
                buttons.Add(("Next Sim", false, () =>
                {
                    var at = household.IndexOf(Sim);
                    Sim = household[(at + 1) % household.Count];
                    Refresh(true);
                }));
            buttons.Add(("Close", false, CloseMe));
            return ButtonRow(body, y + 10, buttons.ToArray());
        }

        /// <summary>How the level's numbers play, in words.</summary>
        public static string Pace(LevelDef level)
        {
            var needs = Math.Abs(level.NeedDecay - 1) < 0.005 ? "Needs fall as usual" : "Needs fall " + Math.Round(Math.Abs(level.NeedDecay - 1) * 100) + "% " + (level.NeedDecay < 1 ? "slower" : "faster");
            var meter = level.DecayPerHour > 0 ? ", and the meter " + level.DecayPerHour.ToString("0.0#") + " an hour." : ", and the meter holds still but for mood.";
            return needs + meter;
        }
    }

    /// <summary>The rewards: what each does and costs, and whether the Sim can use it now.</summary>
    public class UIRewardsWindow : UIPackDialog
    {
        private static UIRewardsWindow Current;
        private readonly VMAvatar Sim;
        private string Status = "";

        public static void Open(VMAvatar sim)
        {
            if (Current != null && Current.Parent != null) Current.CloseMe();
            Current = new UIRewardsWindow(sim);
            UIScreen.GlobalShowDialog(Current, true);
        }

        private UIRewardsWindow(VMAvatar sim)
        {
            Sim = sim;
            Refresh(true);
        }

        private void CloseMe()
        {
            if (Current == this) Current = null;
            Close();
        }

        private SimRecord Record => Watcher.Ledger?.Find(Sim.Object.OBJ.GUID);
        private VM Vm => AspirationsPack.Screen?.vm;

        protected override string Signature()
        {
            var r = Record;
            if (r == null || Vm == null) return "none";
            return r.Points + "|" + Rules.LevelIndex(r.Meter, Watcher.Balance) + "|" + Status + "|" + string.Join(",", Watcher.Balance.Rewards.Select(x => AspirationsPack.CannotUse(Vm, Sim, x) ?? ""));
        }

        protected override float Build(UIContainer body)
        {
            var b = Watcher.Balance;
            var r = Record;
            var style = UIStyle.Current;
            Caption = "Rewards: " + Sim.Name;
            float y = Top;
            if (r == null || Vm == null) return ButtonRow(body, y, ("Close", true, CloseMe));
            var level = Rules.Level(r.Meter, b);
            Text(body, Margin, y, 19, Palette.Gold, Rules.Number(r.Points) + " reward points", Inner);
            y += 30;
            var risk = level.Backfire;
            y = Paragraph(body, Margin, y, 14, risk > 0 ? Palette.Fear : style.SecondaryText,
                risk > 0 ? "At " + level.Name + ", each reward has a " + (risk * 100).ToString("0") + "% chance of going wrong, unless it is one that cannot. Lift the meter first, if you can."
                         : "At " + level.Name + ", rewards cannot go wrong.", Inner, 2);
            if (Status != "") y = Paragraph(body, Margin, y, 15, style.SkillActive, Status, Inner, 2);
            y += 6;

            var columns = Width >= 1000 ? 2 : 1;
            var gap = 30f;
            var cell = (Inner - gap * (columns - 1)) / columns;
            const float rowHeight = 74;
            var rewards = b.Rewards;
            for (int i = 0; i < rewards.Count; i++)
            {
                var reward = rewards[i];
                var x = Margin + (i % columns) * (cell + gap);
                var top = y + (i / columns) * rowHeight;
                var problem = AspirationsPack.CannotUse(Vm, Sim, reward);
                Text(body, x, top, 17, problem == null ? style.DialogText : style.DialogText * 0.6f, reward.Name, cell - 230);
                var cost = Text(body, 0, top, 15, Palette.Gold, Rules.Number(reward.Cost), 110);
                cost.X = x + cell - 110 - cost.CaptionStyle.MeasureString(cost.Caption).X;
                Text(body, x, top + 24, 12, style.SecondaryText, reward.About + (reward.Safe ? " Cannot go wrong." : ""), cell - 110);
                if (problem != null) Text(body, x, top + 44, 12, Palette.Fear, problem, cell - 110);
                var use = new UITextButton("Use", 90, 30, 14) { Position = new Vector2(x + cell - 90, top + 22), Enabled = problem == null };
                var chosen = reward;
                use.OnClick += (btn) =>
                {
                    if (Effects.Is(chosen.Effect, "SkillPoint")) UISkillChooser.Open(Sim, chosen, said => { Status = said ?? Status; Refresh(true); });
                    else
                    {
                        Status = AspirationsPack.Use(Vm, Sim, chosen);
                        Refresh(true);
                    }
                };
                body.Add(use);
            }
            y += (rewards.Count + columns - 1) / columns * rowHeight + 6;
            return ButtonRow(body, y, ("Back", true, () => { CloseMe(); UIAspirationWindow.Open(Sim); }), ("Close", false, CloseMe));
        }
    }

    /// <summary>Choosing an aspiration for a grown Sim: each, with what it is about.</summary>
    public class UIAspirationChooser : UIPackDialog
    {
        private readonly VMAvatar Sim;
        private readonly bool First;
        private readonly Action Closed;

        public static bool Open(VMAvatar sim, bool first, Action closed)
        {
            var record = Watcher.RecordOf(sim);
            if (record == null || record.Child) { closed?.Invoke(); return false; }
            UIScreen.GlobalShowDialog(new UIAspirationChooser(sim, first, closed), true);
            return true;
        }

        private UIAspirationChooser(VMAvatar sim, bool first, Action closed)
        {
            Sim = sim;
            First = first;
            Closed = closed;
            Refresh(true);
        }

        private void Done(string chosen)
        {
            var record = Watcher.RecordOf(Sim);
            if (record != null)
            {
                if (chosen != null) AspirationsPack.Choose(Sim, chosen);
                else record.Chosen = true;
            }
            Close();
            Closed?.Invoke();
        }

        protected override string Signature() => Sim.ObjectID + "|" + Watcher.Balance.GetHashCode();

        protected override float Build(UIContainer body)
        {
            var b = Watcher.Balance;
            var record = Watcher.RecordOf(Sim);
            var style = UIStyle.Current;
            Caption = "Choose an aspiration";
            float y = Top;
            var now = b.Aspiration(record?.Aspiration)?.Name;
            y = Paragraph(body, Margin, y, 15, style.DialogText,
                (First ? "What does " + Sim.Name + " want from life? " : "") + "Their aspiration decides which wants and fears they have." + (now != null ? " " + (record.Chosen ? "Now: " : "Chance would choose: ") + now + "." : "")
                + (First ? "" : " A new aspiration starts the wants and fears over; points and the meter stay."), Inner, 3) + 10;
            foreach (var a in b.Aspirations.Where(x => !x.Child))
            {
                var button = new UITextButton(a.Name, 200, 34, 16) { Position = new Vector2(Margin, y), Selected = string.Equals(a.Id, record?.Aspiration, StringComparison.OrdinalIgnoreCase) };
                var id = a.Id;
                button.OnClick += (x) => Done(id);
                body.Add(button);
                Text(body, Margin + 220, y + 7, 14, style.SecondaryText, a.About, Inner - 220);
                y += 44;
            }
            y += 6;
            return First
                ? ButtonRow(body, y, ("Keep " + (now ?? "it"), true, () => Done(null)))
                : ButtonRow(body, y, ("Cancel", false, () => { Close(); Closed?.Invoke(); }));
        }
    }

    /// <summary>Which skill a skill point goes to.</summary>
    public class UISkillChooser : UIPackDialog
    {
        private readonly VMAvatar Sim;
        private readonly RewardDef Reward;
        private readonly Action<string> Closed;

        public static void Open(VMAvatar sim, RewardDef reward, Action<string> closed)
        {
            UIScreen.GlobalShowDialog(new UISkillChooser(sim, reward, closed), true);
        }

        private UISkillChooser(VMAvatar sim, RewardDef reward, Action<string> closed)
        {
            Sim = sim;
            Reward = reward;
            Closed = closed;
            Refresh(true);
        }

        protected override string Signature() => Sim.ObjectID + "|" + string.Join(",", Watcher.Raw(Sim));

        protected override float Build(UIContainer body)
        {
            Caption = Reward.Name + ": which skill?";
            float y = Top;
            var raw = Watcher.Raw(Sim);
            var columns = Width >= 900 ? 3 : 2;
            var width = (Inner - 20 * (columns - 1)) / columns;
            for (int i = 0; i < Rules.SkillNames.Length; i++)
            {
                var level = raw[i] / 100;
                var button = new UITextButton(Rules.SkillNames[i] + "  " + level + (level >= Rules.MaxSkill ? " (top)" : " to " + (level + 1)), width, 40, 16)
                {
                    Position = new Vector2(Margin + (i % columns) * (width + 20), y + (i / columns) * 52),
                    Enabled = level < Rules.MaxSkill,
                };
                var skill = i;
                button.OnClick += (x) =>
                {
                    var said = AspirationsPack.Use(AspirationsPack.Screen?.vm, Sim, Reward, skill);
                    Close();
                    Closed?.Invoke(said);
                };
                body.Add(button);
            }
            y += (Rules.SkillNames.Length + columns - 1) / columns * 52 + 10;
            return ButtonRow(body, y, ("Cancel", false, () => { Close(); Closed?.Invoke(null); }));
        }
    }
}
