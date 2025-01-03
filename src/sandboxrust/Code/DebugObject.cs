

/// <summary>
/// Simple debug object that can be created when something needs to be visually checked.
/// </summary>
public class DebugObject : Component
{
    public float timeout = -1;
    private TimeSince timeSinceCreated;
    public Vector3? direction;

    public static DebugObjectBuilder Create()
    {
        return new DebugObjectBuilder();
    }

    protected override void OnStart()
    {
        base.OnStart();
        timeSinceCreated = 0;
    }

    protected override void OnUpdate()
    {
        if (timeout > 0 && timeSinceCreated > timeout)
        {
            GameObject.Destroy();
        }

        if (direction.HasValue) 
        {
            var target = WorldPosition + direction.Value * 100;
            Gizmo.Draw.Arrow(WorldPosition, target, 10, 2);
        }
        else
        {
            Gizmo.Draw.SolidSphere(WorldPosition, 5);
        }
    }

    public class DebugObjectBuilder
    {
        private GameObject debugObject;
        private DebugObject component;

        public DebugObjectBuilder()
        {
            debugObject = new GameObject();
            component = debugObject.AddComponent<DebugObject>();
        }

        public DebugObjectBuilder WithName( string name )
        {
            debugObject.Name = name;
            return this;
        }

        public DebugObjectBuilder WithPosition( Vector3 position )
        {
            debugObject.WorldPosition = position;
            return this;
        }

        public DebugObjectBuilder WithTimeout( float seconds )
        {
            component.timeout = seconds;
            return this;
        }

        public DebugObjectBuilder WithDirection( Vector3 direction )
        {
            component.direction = direction;
            return this;
        }

        public DebugObject Get()
        {
            return component;
        }
    }


}

