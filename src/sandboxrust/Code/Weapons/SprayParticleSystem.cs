using System;

public class SprayParticleSystem : Component
{

    [Property]
    public ParticleConeEmitter Emitter;

    [Property]
    public SoundPointComponent Sound;

    private float lastRate = 0;
    public float Rate
    {
        set
        {
            if ( lastRate == value )
            {
                return;
            }


            Emitter.Rate = value;
            Sound.Pitch = Random.Shared.Next( 50, 150 ) / 100f;
            Sound.Volume = value > 0 ? Random.Shared.Next( 25, 30 ) / 100f : 0;

            lastRate = value;
        }
    }
}