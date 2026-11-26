using Godot;

namespace TNBCY
{
    // Adds the CONTROLLER CENTRE button to the home screen menu (between RESUME SESSION and SETTINGS).
    public partial class FrontEnd
    {
        public void AddControllerButton(System.Action open)
        {
            if (home == null) return;
            foreach (var child in home.GetChildren())
            {
                var menu = child as VBoxContainer;
                if (menu == null) continue;
                var b = Ui.MakeButton("CONTROLLER CENTRE", 28, false, new Vector2(500, 74));
                menu.AddChild(b);
                menu.MoveChild(b, System.Math.Min(3, menu.GetChildCount() - 1));
                b.Pressed += open;
                return;
            }
        }
    }
}
