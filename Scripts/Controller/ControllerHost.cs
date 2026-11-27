using Godot;

namespace TNBCY
{
    // Child node of Main in Main.tscn. It only wires the Controller Centre into the running game, so Main.cs stays untouched.
    public partial class ControllerHost : Node
    {
        public override void _Ready()
        {
            // A child's _Ready runs before its parent's, so wait until Main._Ready has built the menu and the HUD.
            Callable.From(new System.Action(Install)).CallDeferred();
        }

        void Install()
        {
            var main = GetParent() as Main;
            if (main != null) main.InstallControllerCentre();
        }
    }
}
