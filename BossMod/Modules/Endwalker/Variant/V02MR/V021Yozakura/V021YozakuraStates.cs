﻿namespace BossMod.Endwalker.Variant.V02MR.V021Yozakura;

class V021YozakuraStates : StateMachineBuilder
{
    public V021YozakuraStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ArenaChange>()
            .ActivateOnEnter<Components.StayInBounds>()
            //Right No Dogu
            .ActivateOnEnter<RootArrangement>()
            .ActivateOnEnter<AccursedSeedling>()
            //Right Dogu
            .ActivateOnEnter<Witherwind>()
            //Left Windy
            .ActivateOnEnter<WindblossomWhirl>()
            .ActivateOnEnter<LevinblossomStrike>()
            .ActivateOnEnter<DriftingPetals>()
            //Left Rainy
            .ActivateOnEnter<Mudrain>()
            .ActivateOnEnter<Icebloom>()
            .ActivateOnEnter<Shadowflight>()
            .ActivateOnEnter<MudPie>()
            //Middle Rope Pulled
            .ActivateOnEnter<FireblossomFlare>()
            .ActivateOnEnter<ArtOfTheFluff1>()
            .ActivateOnEnter<ArtOfTheFluff2>()
            //Middle Rope Unpulled
            .ActivateOnEnter<LevinblossomLance>()
            .ActivateOnEnter<TatamiGaeshi>()
            //Standard
            .ActivateOnEnter<Components.StayInBounds>()
            .ActivateOnEnter<GloryNeverlasting>()
            .ActivateOnEnter<KugeRantsui>()
            .ActivateOnEnter<OkaRanman>()
            .ActivateOnEnter<SealOfRiotousBloom>()
            .ActivateOnEnter<SeasonsOfTheFleeting>()
            .ActivateOnEnter<ArtOfTheFireblossom>()
            .ActivateOnEnter<ArtOfTheWindblossom>();
    }
}
