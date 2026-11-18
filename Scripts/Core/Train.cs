using System.Collections.Generic;
using Godot;

namespace TNBCY
{
    public class PathSeg { public TEdge Edge; public float StartOdo; }
    public class Car { public VehicleSpec Spec; public float Offset, Length; }

    // A train moving along the track graph. Forces come from TrainPhysics; this class follows the path through switches.
    public class Train
    {
        public readonly TrainPhysics Phys;
        public List<Car> Cars = new List<Car>();
        public List<PathSeg> Trail = new List<PathSeg>();
        public bool Finished; public int EndKind; public float EndSpeed;   // EndKind 1 = end of line, 2 = buffer stop

        public float Length { get { return Phys.LengthM; } }
        public float Speed { get { return Phys.Speed; } }
        public float Odo { get { return Phys.Position; } set { Phys.Position = value; } }
        public PathSeg FrontSeg { get { return Trail[Trail.Count - 1]; } }
        public float FrontOffset { get { return Odo - FrontSeg.StartOdo; } }

        public Train(ConsistSpec spec, TEdge startEdge, float startOffset)
        {
            Phys = new TrainPhysics(spec); float off = 0f;
            foreach (var v in spec.vehicles)
                for (int i = 0; i < System.Math.Max(1, v.count); i++)
                { Cars.Add(new Car { Spec = v, Offset = off, Length = v.lengthM }); off += v.lengthM; }
            Trail.Add(new PathSeg { Edge = startEdge, StartOdo = 0f });
            Odo = Mathf.Max(startOffset, Length + 2f);
        }

        PathSeg SegAt(float odo)
        {
            for (int i = Trail.Count - 1; i >= 0; i--) if (Trail[i].StartOdo <= odo) return Trail[i];
            return Trail[0];
        }
        public void PointAt(float odo, out Vector3 pos, out Vector3 dir)
        {
            var s = SegAt(odo); float off = odo - s.StartOdo;
            pos = s.Edge.PosAt(off); dir = s.Edge.DirAt(off);
        }

        public void Update(float dt)
        {
            if (Finished) return;
            var mid = SegAt(Odo - Length * .5f);
            Phys.Step(dt, mid.Edge.Gradient / 1000f, mid.Edge.CurveR);
            Advance();
            while (Trail.Count > 2 && Trail[1].StartOdo < Odo - Length - 30f) Trail.RemoveAt(0);
        }

        void Advance()
        {
            while (true)
            {
                var seg = FrontSeg;
                if (Odo - seg.StartOdo < seg.Edge.Length) return;
                float nodeOdo = seg.StartOdo + seg.Edge.Length;
                var n = seg.Edge.To; n.PassedOdo = nodeOdo;
                if (n.Outs.Count == 0)
                {
                    EndSpeed = Phys.Speed; Odo = nodeOdo; Phys.Speed = 0f;
                    Finished = true; EndKind = n.Type == "buffer" ? 2 : 1; return;
                }
                Trail.Add(new PathSeg { Edge = n.Outs[n.Sel], StartOdo = nodeOdo });
            }
        }
    }
}
