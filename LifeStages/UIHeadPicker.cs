/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Camera;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;

namespace LifeStages
{
    /// <summary>
    /// Choosing a grown Sim's head, as the game's dresser and clothes shops choose an outfit: copies of the Sim stand
    /// in a ring, each with one of the heads on offer, and are turned by dragging. The head the Sim has now is the
    /// first, so OK on it, or Cancel, keeps it. The head's name is shown under the ring.
    ///
    /// It is Simitone's own outfit picker (UISelectSkinAlert) with heads in place of bodies.
    /// </summary>
    public class UIHeadPicker : UIMobileDialog
    {
        private readonly UIHeadPickerPanel Panel;

        public UIHeadPicker(VMAvatar target, VM vm, List<(string Mesh, string Tex)> heads, string title, Action closed)
        {
            Caption = title;
            SetHeight(600);
            Panel = new UIHeadPickerPanel(target, vm, heads);
            Panel.Position = new Vector2((Width - 1030) / 2, 110);
            Panel.OnResult += (chosen) =>
            {
                LifeStagesPack.Guard("giving a Sim the head chosen", () =>
                {
                    if (chosen > 0) Life.SetHead(target, heads[chosen].Mesh, heads[chosen].Tex);
                });
                Panel.Cleanup();
                Close();
                if (closed != null) LifeStagesPack.Guard("after choosing a head", closed);
            };
            Add(Panel);
        }

        public override void GameResized()
        {
            base.GameResized();
            Panel.Position = new Vector2((Width - 1030) / 2, 110);
        }
    }

    public class UIHeadPickerPanel : UIContainer
    {
        private const int Ring = 18;
        private readonly VM Vm;
        private readonly List<(string Mesh, string Tex)> Heads;
        private readonly BasicCamera Camera;
        private readonly _3DTargetScene Scene;
        private readonly VMAvatar[] Avatars = new VMAvatar[Ring];
        private readonly UILabel Name;
        private float Position = -8f, Speed, XLast = -1f;
        private int PositionLast;
        private int ShownName = -1;

        /// <summary>The place in the list of the head chosen, or -1 for Cancel.</summary>
        public event Action<int> OnResult;

        public int Chosen => (int)DirectionUtils.PosMod(Math.Round(Position + 8), Heads.Count);

        public UIHeadPickerPanel(VMAvatar target, VM vm, List<(string Mesh, string Tex)> heads)
        {
            Vm = vm;
            Heads = heads;
            Camera = new BasicCamera(GameFacade.GraphicsDevice, new Vector3(5, 1, 0), new Vector3(0, 0, 0), new Vector3(0, 1, 0));
            Camera.NearPlane = 0.001f;
            Scene = new _3DTargetScene(GameFacade.GraphicsDevice, Camera, new Point(1030, 500), (GlobalSettings.Default.AntiAlias > 0) ? 8 : 0);
            Scene.Initialize(GameFacade.Scenes);

            // copies of the Sim, out of the world and hidden from it, drawn only here
            for (int i = 0; i < Ring; i++)
            {
                var copy = (VMAvatar)vm.Context.CreateObjectInstance(target.Object.OBJ.GUID, LotTilePos.OUT_OF_WORLD, Direction.NORTH, true).BaseObject;
                copy.SetValue(VMStackObjectVariable.Hidden, 1);
                if (target.BodyOutfit != null) copy.BodyOutfit = target.BodyOutfit;
                Scene.Add(copy.Avatar);
                Avatars[i] = copy;
            }
            for (int i = 0; i < Ring; i++) SetHead(Avatars[i], 17 - i);

            Name = new UILabel();
            Name.CaptionStyle = Name.CaptionStyle.Clone();
            Name.CaptionStyle.Size = 15;
            Name.CaptionStyle.Color = UIStyle.Current.Text;
            Name.Size = new Vector2(1030, 1);
            Name.Alignment = TextAlignment.Center | TextAlignment.Middle;
            Name.Position = new Vector2(0, 335);
            Add(Name);

            var ok = new UIBigButton(true);
            ok.Caption = GameFacade.Strings.GetString("142", "0");
            ok.Position = new Vector2((515 + 300) - 137, 370);
            ok.Width = 275;
            ok.OnButtonClick += (btn) => OnResult?.Invoke(Chosen);
            Add(ok);

            var cancel = new UIBigButton(true);
            cancel.Caption = GameFacade.Strings.GetString("142", "1");
            cancel.Position = new Vector2((515 - 300) - 137, 370);
            cancel.Width = 275;
            cancel.OnButtonClick += (btn) => OnResult?.Invoke(-1);
            Add(cancel);
        }

        private void SetHead(VMAvatar avatar, int i)
        {
            var head = Heads[(int)DirectionUtils.PosMod(i, Heads.Count)];
            avatar.HeadOutfit = new VMOutfitReference(new Outfit() { TS1AppearanceID = head.Mesh + ".apr", TS1TextureID = head.Tex });
        }

        /// <summary>The copies of the Sim are taken out of the lot when the picker is closed.</summary>
        public void Cleanup()
        {
            foreach (var copy in Avatars)
            {
                if (copy == null) continue;
                Scene.Remove(copy.Avatar);
                copy.Delete(true, Vm.Context);
            }
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            Camera.Position = new Vector3(10f, 4f, 0);
            Camera.Target = new Vector3(4f, 0.8f, 0);
            GameFacade.GraphicsDevice.RasterizerState = Microsoft.Xna.Framework.Graphics.RasterizerState.CullCounterClockwise;
            Scene.Draw(GameFacade.GraphicsDevice);
            GameFacade.GraphicsDevice.RasterizerState = Microsoft.Xna.Framework.Graphics.RasterizerState.CullNone;
        }

        public override void Draw(UISpriteBatch batch)
        {
            base.Draw(batch);
            DrawLocalTexture(batch, Scene.Target, new Vector2(0, 0));
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            Turn(state);
            for (int i = 0; i < Ring; i++)
            {
                var relPos = DirectionUtils.PosMod(Position + i - 9, Ring);
                if (relPos > 9) relPos += 6;
                var angle = (relPos / 24) * (Math.PI * 2);
                var ava = Avatars[i].Avatar;
                ava.RotationY = -(float)angle + (float)Math.PI / 2;
                var facing = (float)Math.Abs(DirectionUtils.PosMod(angle + Math.PI, Math.PI * 2) - Math.PI);
                ava.AmbientLight = Vector4.One * Math.Max(0, Math.Min(1, 2 - facing));
                if (i != DirectionUtils.PosMod(-Math.Round(Position + 9), Ring)) ava.AmbientLight *= 0.5f;
                else ava.AmbientLight *= 1.1f;
                ava.Position = new Vector3(4.5f * (float)Math.Cos(angle), 0, 4.5f * (float)Math.Sin(angle));
                ava.Scale = Vector3.One / 2f;
            }
            if (Chosen != ShownName)
            {
                ShownName = Chosen;
                Name.Caption = Heads[Chosen].Mesh + (Chosen == 0 ? " (the head they have now)" : "");
            }
        }

        /// <summary>Dragging turns the ring, which then settles on a head: as the outfit picker does.</summary>
        private void Turn(UpdateState state)
        {
            var frac = 60f / FSOEnvironment.RefreshRate;
            var minSpeed = (Math.PI / 240f) * frac;
            var mult = (float)Math.Pow(0.95, frac);
            var moving = false;
            if (state.MouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                var pos = GlobalPoint(state.MouseState.Position.ToVector2());
                if (XLast == -1)
                {
                    if (pos.Y > -100) XLast = state.MouseState.X;
                }
                else
                {
                    if (pos.Y > 0 && pos.Y < 330)
                    {
                        Speed = ((XLast - state.MouseState.X) / 200f) * frac;
                        moving = true;
                    }
                    XLast = state.MouseState.X;
                }
            }
            else XLast = -1;

            Speed *= mult;
            if (Math.Abs(Speed) < minSpeed && !moving)
            {
                var target = (int)Math.Round(Position);
                Speed = (float)Math.Max(-minSpeed, Math.Min(minSpeed, target - Position));
            }
            Position += Speed;
            foreach (var copy in Avatars) copy.SetRoom(65534);

            var current = (int)Position;
            if (current == PositionLast) return;
            FSO.HIT.HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.Click);
            var replacePos = (int)DirectionUtils.PosMod(-PositionLast, Ring);
            var increment = current < PositionLast ? -1 : 1;
            var total = Math.Abs(current - PositionLast);
            for (int i = 0; i < total; i++)
            {
                SetHead(Avatars[replacePos], increment == -1 ? PositionLast - 1 : PositionLast + 17);
                PositionLast += increment;
                replacePos = ((replacePos - increment) + Ring) % Ring;
            }
            PositionLast = current;
        }
    }
}
