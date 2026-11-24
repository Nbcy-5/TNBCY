using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Small line drawing of a route: the line, the stations and their distances.
    public partial class RouteGlyph : Control
    {
        public List<string> Names = new List<string>(); public List<float> Km = new List<float>(); public float Total = 1f;
        public override void _Draw()
        {
            float w = Size.X, y = Size.Y * .5f, m = 26f;
            DrawLine(new Vector2(m, y), new Vector2(w - m, y), new Color(1f, 1f, 1f, .25f), 6f, true);
            DrawLine(new Vector2(m, y), new Vector2(w - m, y), Ui.Gold, 2.5f, true);
            for (int i = 0; i < Names.Count && i < Km.Count; i++)
            {
                float x = m + (w - 2f * m) * Mathf.Clamp(Km[i] / Mathf.Max(Total, .1f), 0f, 1f); bool up = i % 2 == 0;
                DrawCircle(new Vector2(x, y), 8f, Ui.Navy); DrawCircle(new Vector2(x, y), 5.5f, Ui.Gold);
                string n = Names[i].Length > 14 ? Names[i].Substring(0, 13) + "." : Names[i];
                DrawString(UiFont.Med, new Vector2(x - 60f, y + (up ? -16f : 28f)), n, HorizontalAlignment.Center, 120f, 13, Colors.White);
                DrawString(UiFont.Med, new Vector2(x - 40f, y + (up ? -32f : 44f)), Km[i].ToString("0") + " km", HorizontalAlignment.Center, 80f, 11, Ui.Dim);
            }
        }
    }
}
