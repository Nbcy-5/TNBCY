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
    }

    [Serializable]
    public class ConsistSpec
    {
        public string name = "Train";
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
    }

    public class NetSignalSpec { public string id = ""; public string edge = ""; public float offsetM; public bool held; public float holdSeconds = 20f; }
    public class NetStationSpec { public string name = ""; public string edge = ""; public float offsetM; public bool stop = true; public string side = "R"; public float dwellS = 15f; }
    public class StartSpec { public string edge = ""; public float offsetM; }
    public class AiSpec { public bool enabled = true; public string edge = ""; public float offsetM; public float departAfterS = 20f; }

    public class NetworkSpec
    {
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
