﻿namespace BossMod.Endwalker.Savage.P5SProtoCarbuncle
{
    // TODO: improve component?
    class ToxicCrunch : Components.CastCounter
    {
        public ToxicCrunch() : base(ActionID.MakeSpell(AID.ToxicCrunchAOE)) { }
    }

    class DoubleRush : Components.ChargeAOEs
    {
        public DoubleRush() : base(ActionID.MakeSpell(AID.DoubleRush), 50) { }
    }

    // TODO: show knockback?
    class DoubleRushReturn : Components.CastCounter
    {
        public DoubleRushReturn() : base(ActionID.MakeSpell(AID.DoubleRushReturn)) { }
    }

    // TODO: show aoe/poison
    class RubyReflection2 : Components.CastCounter
    {
        public RubyReflection2() : base(ActionID.MakeSpell(AID.RubyReflection2)) { }
    }

    public class P5S : BossModule
    {
        public P5S(WorldState ws, Actor primary) : base(ws, primary, new ArenaBoundsSquare(new(100, 100), 15)) { }
    }
}
