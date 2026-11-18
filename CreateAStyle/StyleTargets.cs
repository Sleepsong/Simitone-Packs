/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;
using System.Collections.Generic;
using System.Linq;

namespace CreateAStyle
{
    /// <summary>One thing the panel styles as a whole: an object, or one of a Sim's textures (their clothes, say).</summary>
    public class StylePiece
    {
        /// <summary>Shown on its tab, when there is more than one. Null for an object.</summary>
        public string Title;
        /// <summary>For a Sim, the name of the texture.</summary>
        public string Texture;
        /// <summary>The style being made. Null when it cannot be styled: then Note says why.</summary>
        public Style Working;
        public string Note;
    }

    /// <summary>What the panel is styling.</summary>
    public abstract class StyleTarget
    {
        public string Name;
        public List<StylePiece> Pieces = new List<StylePiece>();
        /// <summary>It has gone from the lot.</summary>
        public abstract bool Gone { get; }
        public abstract bool IsFor(VMEntity entity);
        /// <summary>Has the game show a piece as it is being made.</summary>
        public abstract void Apply(StylePiece piece);
        /// <summary>How the styling of a piece is going, in a few words. Null if it has nothing to say.</summary>
        public abstract string StatusOf(StylePiece piece);

        public static StyleTarget For(VM vm, VMEntity entity)
        {
            return (entity is VMAvatar sim) ? new SimTarget(sim) : (StyleTarget)new ObjectTarget(vm, entity);
        }
    }

    public class ObjectTarget : StyleTarget
    {
        private VM vm;
        public readonly VMEntity Entity;

        public ObjectTarget(VM vm, VMEntity entity)
        {
            this.vm = vm;
            Entity = entity;
            Name = entity.ToString();
            //carry on from the style the object has, or start from its parts as they are
            Pieces.Add(new StylePiece { Working = CreateAStylePack.StyleOf(entity)?.Clone() ?? CreateAStylePack.Analyse(entity) });
        }

        public override bool Gone => Entity.Dead;
        public override bool IsFor(VMEntity entity) => CreateAStylePack.Whole(Entity).Contains(entity);
        public override void Apply(StylePiece piece) => CreateAStylePack.SetStyle(vm, Entity, piece.Working.Clone());
        public override string StatusOf(StylePiece piece) => CreateAStylePack.StatusOf(Entity);
    }

    public class SimTarget : StyleTarget
    {
        public readonly VMAvatar Sim;

        public SimTarget(VMAvatar sim)
        {
            Sim = sim;
            Name = sim.Name;
            Pieces = SimStyles.PiecesOf(sim);
        }

        public override bool Gone => Sim.Dead;
        public override bool IsFor(VMEntity entity) => entity == Sim;
        public override void Apply(StylePiece piece) => SimStyles.SetStyle(Sim, piece.Texture, piece.Working.Clone());
        public override string StatusOf(StylePiece piece) => SimStyles.StatusOf(Sim, piece.Texture);
    }
}
