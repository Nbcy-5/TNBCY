using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TNBCY
{
    // One kind of vehicle (loco or wagon). Loaded from JSON, so new trains need no code changes.
    [Serializable]
    public class VehicleSpec
    {
        public string name = "Wagon";
        public int count = 1;               // how many identical vehicles
        public float massTonnes = 30f;
        public float lengthM = 15f;
        public float brakeForceKn = 45f;    // brake force at full application
        public float davisA = 25f;          // rolling resistance, N per tonne
        public float davisB = 0.5f;         // N per tonne per (m/s)
        public float davisC = 6f;           // air drag, N per (m/s)^2 for the vehicle
        public bool powered;
        public float maxPowerKw;
        public float maxTractiveEffortKn;
        public float maxSpeedKmh = 120f;
        public int colorRgb = 0x2f7d84;     // used by the 3D view
        public string model = "";           // id of a 3D locomotive model in Data/locos.json (empty = the built-in box loco / coach)
        public float bogieCentreM;          // distance from the middle of the vehicle to each bogie centre (0 = 13% in from the ends)
    }

    // One locomotive model from Data/locos.json: the 3D file, its physical data and where the axles are.
    [Serializable]
    public class LocoSpec
    {
        public string id = "";
        public string name = "";
        public string model = "";           // file name (without .glb) in Assets/Locos
        public bool electric;
        public float massTonnes = 120f;
        public float lengthM = 21f;
        public float brakeForceKn = 150f;
        public float maxPowerKw = 3000f;
        public float maxTractiveEffortKn = 250f;
        public float maxSpeedKmh = 110f;
        public float bogieCentreM = 6f;
        public float wheelR = .55f;         // wheel radius in metres
        public float zc;                    // the model is shifted by this much along its own Z so both bogies sit symmetrically
        public bool frames = true;          // true = the model has no bogie frames, so simple ones are added
        public float roofY = 4.2f;          // roof height above the rail (pantograph base), electric locos only
        public float pantZ = -3.6f;         // pantograph position along the loco (+Z = front)
        public List<float> axles = new List<float>();   // axle positions along the loco, front = +Z
    }

    public class LocoList { public List<LocoSpec> locos = new List<LocoSpec>(); }

    [Serializable]
    public class ConsistSpec
    {
        public string name = "Train";
        public bool randomElectricLoco;         // true = a different electric (WAP) loco from Data/locos.json is coupled on each start
        public float brakeApplyRate = 0.25f;    // brake level per second when applying
        public float brakeReleaseRate = 0.12f;  // slower release, like a real air brake
        public List<VehicleSpec> vehicles = new List<VehicleSpec>();
    }

    // ---- Track network (nodes joined by edges; a node with 2+ outgoing edges is a switch) ----
    public class NodeSpec { public string id = ""; public float x; public float z; public string type = "plain"; }

    public class EdgeSpec
    {
        public string id = "";
        public string turnout = "";          // "L" for a loop line that leaves the main line on the left
        [JsonPropertyName("from")] public string fromNode = "";
        [JsonPropertyName("to")] public string toNode = "";
        public List<List<float>> via = new List<List<float>>();   // extra [x,z] points the track bends through
        public float gradientPermil;
        public float curveRadiusM;
        public float speedLimitKmh;
        // v5.29 (limits): filled in by LimitBuilder, never needed in the route file.
        public string baseId = "";           // the route-file edge this piece was cut from ("" = itself)
        public float baseStart;              // where this piece starts along that route-file edge
        public bool virt;                    // hidden reverse-direction twin of a track piece (same rails, opposite direction; never drawn)
        public bool crossover;               // crossover track between the Down and Up mains
        public string twinOf = "";           // for a virtual edge: the real piece it shares its rails with
        public string dir = "";              // "D" or "U": direction of travel on this edge (empty = from the edge name / geometry)
    }

    public class NetSignalSpec { public string id = ""; public string edge = ""; public float offsetM; public bool held; public float holdSeconds = 20f; public string dir = ""; public bool guard; }
    public class NetStationSpec { public string name = ""; public string edge = ""; public float offsetM; public bool stop = true; public string side = "R"; public float dwellS = 15f; }
    public class StartSpec { public string edge = ""; public float offsetM; }
    public class AiSpec { public bool enabled = true; public string edge = ""; public float offsetM; public float departAfterS = 20f; }

    // A limit = a named stretch of track where two-way running is allowed: a main station, a passing loop or a halt.
    // Everything between the Down and Up mains inside it is generated at start-up (see LimitBuilder): the reverse-direction track,
    // two single crossovers at each end (one for each running direction) and the signals. Positions are metres along the route-file edges.
    public class LimitSpec
    {
        public string id = "";               // short code, used in generated names, e.g. "SC"
        public string name = "";
        public string type = "station";      // station | loop | halt
        public string downEdge = "";         // route-file edge of the Down main that carries the limit
        public float downFromM = 0f, downToM = -1f;     // limit start / end along it (-1 = end of the edge)
        public string upEdge = "";           // same for the Up main
        public float upFromM = 0f, upToM = -1f;
        public List<string> extraEdges = new List<string>();   // loop / platform lines inside the limit that also become two-way
        public float crossoverLengthM = 100f;   // length of one crossover along the track
        public float crossoverGapM = 30f;       // gap between the two crossovers at one end
        public float endMarginM = 120f;         // distance from the limit end to the first crossover (keeps clear of loop points)
        public float crossoverLimitKmh = 25f;
        public float signalSetbackM = 40f;
    }

    public class NetworkSpec
    {
        [JsonIgnore] public bool limitsDone;     // set once the limits have been expanded into edges
        public List<LimitSpec> limits = new List<LimitSpec>();
        public string name = "";
        public float defaultLimitKmh = 100f;
        public float timeScale = 1f;         // game seconds per real second (20 km in ~3 min needs about 5)
        public float targetTimeS = 0f;       // real seconds for the time bonus (0 = no bonus)
        public float routeLengthM = 0f;
        public string clockStart = "09:00";
        public AiSpec ai = new AiSpec();
        public StartSpec start = new StartSpec();
        public List<NodeSpec> nodes = new List<NodeSpec>();
        public List<EdgeSpec> edges = new List<EdgeSpec>();
        public List<NetSignalSpec> signals = new List<NetSignalSpec>();
        public List<NetStationSpec> stations = new List<NetStationSpec>();
    }
}
