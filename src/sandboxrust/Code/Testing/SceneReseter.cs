public class SceneReseter : GameObjectSystem
{
    public SceneReseter(Scene scene) : base(scene)
    {
        Listen(Stage.StartUpdate, 0, OnStartUpdate, "SceneReseter");
    }

    private void OnStartUpdate()
    {
        if(Input.Pressed("scene_reset"))
        {
            // Last minute addition, not tested if everything is disposed correctly when we 
            // simply kill the scene and load it again.
            Scene.LoadFromFile("scenes/main.scene");
        }
    }   
}