using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v24 track add-on (all generated in code; no image or model files are added).
    //  - Sleeper: heavy monoblock concrete sleeper in the style of the N26 type: full-height rail-seat blocks, narrower and lower waist,
    //    lowered end caps and cast-in steel shoulders at every rail seat.
    //  - Fastening: rubber pad, nylon insulator against the rail foot, spring clip pushed under the shoulder.
    //  - Joints: the ballast texture phase now runs on across the joint between two track pieces (it used to restart at 0 on every piece,
    //    which showed as a straight seam line in the stones at every joint).
    public partial class Main
    {
        Mesh rtSleeperN26;
        readonly Dictionary<TEdge, float> rtUvBase = new Dictionary<TEdge, float>();

        // Unit box space like the old sleeper: x across the track (scaled 2.75 m), y up (0.21 m), z along the track (0.26 m).
        // The rails stand at x = +-0.873 m / 2.75 m = +-0.3175.
        static ArrayMesh RTMakeSleeperN26()
        {
            return BuildMesh(st =>
            {
                Color body = new Color(1f, 1f, 1f), mid = new Color(.94f, .94f, .92f), cap = new Color(.9f, .9f, .88f), steel = new Color(.22f, .22f, .24f);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    AddBoxC(st, new Vector3(sd * .33f, 0f, 0f), new Vector3(.26f, 1f, 1f), body);                  // rail seat block, full height
                    AddBoxC(st, new Vector3(sd * .48f, -.1f, 0f), new Vector3(.04f, .8f, .92f), cap);              // end cap: lower and slightly narrower
                    for (int k = -1; k <= 1; k += 2)
                        AddBoxC(st, new Vector3(sd * .3175f + k * .0382f, .5f, 0f), new Vector3(.018f, .14f, .38f), steel);   // cast-in shoulders
                }
                AddBoxC(st, new Vector3(0f, -.125f, 0f), new Vector3(.4f, .75f, .8f), mid);                         // waist: narrower and lower
            });
        }

        // One rail seat (origin: rail centre on top of the sleeper): pad, nylon insulators, spring clips.
        static ArrayMesh RTMakeFit24()
        {
            return BuildMesh(st =>
            {
                Color rub = new Color(.05f, .05f, .055f), clip = new Color(.34f, .24f, .17f), nyl = new Color(.78f, .78f, .72f);
                AddBoxC(st, new Vector3(0f, .006f, 0f), new Vector3(.16f, .012f, .2f), rub);                         // rubber pad
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    AddBoxC(st, new Vector3(sd * .0795f, .02f, 0f), new Vector3(.012f, .03f, .07f), nyl);           // nylon insulator against the rail foot
                    AddBoxC(st, new Vector3(sd * .064f, .044f, 0f), new Vector3(.05f, .012f, .022f), clip);         // clip toe on the rail foot
                    AddBoxC(st, new Vector3(sd * .09f, .046f, 0f), new Vector3(.014f, .05f, .022f), clip);          // clip leg
                }
            });
        }

        void RTV24Init()
        {
            rtSleeperN26 = RTMakeSleeperN26();
            fitMesh = RTMakeFit24();
        }

        // Along-track distance (metres, 0..2) at the start of this piece, so the ballast texture (2 m long) continues across joints.
        float RTUvBase(TEdge e)
        {
            float v; if (rtUvBase.TryGetValue(e, out v)) return v;
            float sum = 0f; TEdge cur = e;
            for (int hop = 0; hop < 64; hop++)
            {
                TEdge prev = null;
                foreach (var o in sc.Graph.Edges.Values)
                    if (o != cur && o.To == cur.From && (prev == null || (!o.Turnout && prev.Turnout))) prev = o;
                if (prev == null) break;
                sum += prev.Length; cur = prev;
            }
            v = Mathf.PosMod(sum, 2f); rtUvBase[e] = v; return v;
        }
    }
}
