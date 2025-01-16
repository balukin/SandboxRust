public class SprayParticleSystem : Component
{
    
    [Property]
    public ParticleConeEmitter Emitter;

    
    public float Rate 
    {
        set => Emitter.Rate = value;
    }
}