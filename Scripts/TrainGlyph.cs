using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // A little side view of the train: loco + coaches.
    public partial class TrainGlyph : Control
    {
        public int Coaches = 8; public Color Body = new Color(.72f, .22f, .18f);
        public override void _Draw()
        {
            int n = Mathf.Clamp(Coaches, 2, 26); float w = Size.X - 20f, h = Size.Y, gap = 3f;
            float cw = (w - gap * n) / (n + .6f), ch = Mathf.Min(h * .5f, cw * .75f + 6f), y = h * .5f - ch * .5f; float x = 10f;
            DrawLine(new Vector2(0f, y + ch + 5f), new Vector2(Size.X, y + ch + 5f), new Color(1f, 1f, 1f, .35f), 2f, true);
            for (int i = 0; i < n + 1; i++)
            {
                bool loco = i == 0; float ww = loco ? cw * 1.6f : cw;
                var r = new Rect2(x, y, ww, ch);
                DrawRect(r, loco ? Ui.Gold : Body);
                DrawRect(new Rect2(x, y + ch * .62f, ww, ch * .12f), new Color(1f, 1f, 1f, .75f));
                DrawRect(new Rect2(x + ww * .12f, y + ch * .16f, ww * .76f, ch * .26f), new Color(.1f, .17f, .26f));
                x += ww + gap;
            }
        }
    }
}
