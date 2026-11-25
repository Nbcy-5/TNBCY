using System.Collections.Generic;
using Godot;

namespace TNBCY
{
    // Central route planner and interlocking reservation owner. Each train receives an
    // independent path; occupied sections and conflicting point settings are protected.
    public class RailwayDispatcher
    {
        sealed class Plan
        {
            public string Id;
            public Train Train;
            public bool IsPlayer;
            public List<TEdge> Route = new List<TEdge>();
            public List<float> Starts = new List<float>();
        }

        readonly TrackGraph graph;
        readonly Dictionary<string, Plan> plans = new Dictionary<string, Plan>();
        readonly Dictionary<TEdge, string> edgeOwners = new Dictionary<TEdge, string>();
        readonly Dictionary<string, string> platformOwners = new Dictionary<string, string>();
        readonly Dictionary<TNode, TEdge> switchRoutes = new Dictionary<TNode, TEdge>();
        readonly Dictionary<TNode, string> switchOwners = new Dictionary<TNode, string>();

        public RailwayDispatcher(TrackGraph trackGraph) { graph = trackGraph; }
        public int TrainCount { get { return plans.Count; } }

        public bool TryPlanToFarthestStop(string trainId, Train train, bool isPlayer, out string reason)
        {
            reason = "";
            if (train == null || train.FrontSeg == null) { reason = "Train has no starting track."; return false; }
            TEdge start = train.FrontSeg.Edge;
            List<TEdge> best = null;
            float bestDistance = -1f;

            foreach (var station in graph.Stations)
            {
                if (!station.Stop || station.Edge == null || station.Edge.Id.Length == 0) continue;
                if (station.Edge.Id[0] != start.Id[0]) continue;
                List<TEdge> candidate;
                if (!BuildRoute(start, station.Edge, trainId, out candidate)) continue;
                float distance = 0f;
                foreach (var edge in candidate) distance += edge.Length;
                if (distance > bestDistance) { bestDistance = distance; best = candidate; }
            }

            if (best == null) { reason = "No reachable stopping station was found for " + trainId + "."; return false; }
            return CommitPlan(trainId, train, isPlayer, best, out reason);
        }

        public bool TryPlanRoute(string trainId, Train train, string destinationEdgeId,
            IList<string> viaEdgeIds, bool isPlayer, out string reason)
        {
            reason = "";
            TEdge destination;
            if (train == null || !graph.Edges.TryGetValue(destinationEdgeId, out destination))
            { reason = "The train or destination track is missing."; return false; }

            var route = new List<TEdge>();
            TEdge current = train.FrontSeg.Edge;
            route.Add(current);
            if (viaEdgeIds != null)
            {
                foreach (string id in viaEdgeIds)
                {
                    TEdge via;
                    if (!graph.Edges.TryGetValue(id, out via)) { reason = "Unknown via track: " + id; return false; }
                    List<TEdge> segment;
                    if (!BuildRoute(current, via, trainId, out segment))
                    { reason = "No route to via track " + id; return false; }
                    for (int i = 1; i < segment.Count; i++) route.Add(segment[i]);
                    current = via;
                }
            }

            List<TEdge> finalSegment;
            if (!BuildRoute(current, destination, trainId, out finalSegment))
            { reason = "No route to destination track " + destinationEdgeId; return false; }
            for (int i = 1; i < finalSegment.Count; i++) route.Add(finalSegment[i]);
            return CommitPlan(trainId, train, isPlayer, route, out reason);
        }

        bool CommitPlan(string id, Train train, bool isPlayer, List<TEdge> route, out string reason)
        {
            reason = "";
            if (string.IsNullOrEmpty(id) || route == null || route.Count == 0)
            { reason = "Invalid train route."; return false; }
            if (plans.ContainsKey(id)) ReleaseTrain(id);

            var requestedSwitches = new Dictionary<TNode, TEdge>();
            for (int i = 0; i + 1 < route.Count; i++)
            {
                TNode node = route[i].To;
                TEdge next = route[i + 1];
                if (node.Outs.Count <= 1) continue;

                TEdge existing;
                if (requestedSwitches.TryGetValue(node, out existing) && existing != next)
                { reason = "Route requires conflicting settings at " + node.Name; return false; }
                requestedSwitches[node] = next;

                string owner;
                if (switchOwners.TryGetValue(node, out owner) && owner != id)
                { reason = "Switch " + node.Name + " is reserved by " + owner + "."; return false; }
            }

            foreach (var edge in route)
            {
                string owner;
                if (edgeOwners.TryGetValue(edge, out owner) && owner != id)
                { reason = "Track section " + edge.Id + " is already reserved by " + owner + "."; return false; }
            }

            var plan = new Plan { Id = id, Train = train, IsPlayer = isPlayer, Route = new List<TEdge>(route) };
            float start = 0f;
            foreach (var edge in route) { plan.Starts.Add(start); start += edge.Length; }
            plans[id] = plan;
            foreach (var edge in route) edgeOwners[edge] = id;
            foreach (var pair in requestedSwitches)
            {
                switchRoutes[pair.Key] = pair.Value;
                switchOwners[pair.Key] = id;
            }
            train.SetPlannedRoute(route);
            UpdatePoints();
            return true;
        }

        // Dijkstra over directed track edges. Reserved sections cannot be used by another train.
        bool BuildRoute(TEdge from, TEdge to, string owner, out List<TEdge> route)
        {
            route = null;
            if (from == null || to == null) return false;
            if (from == to) { route = new List<TEdge> { from }; return true; }

            TNode source = from.To, target = to.From;
            var distance = new Dictionary<TNode, float>();
            var previous = new Dictionary<TNode, TEdge>();
            var open = new List<TNode>();
            var closed = new HashSet<TNode>();
            distance[source] = 0f;
            open.Add(source);

            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                    if (distance[open[i]] < distance[open[bestIndex]]) bestIndex = i;
                TNode node = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (!closed.Add(node)) continue;
                if (node == target) break;

                foreach (var edge in node.Outs)
                {
                    string reservedBy;
                    if (edgeOwners.TryGetValue(edge, out reservedBy) && reservedBy != owner) continue;
                    TNode next = edge.To;
                    if (closed.Contains(next)) continue;
                    float candidate = distance[node] + Mathf.Max(1f, edge.Length);
                    float known;
                    if (distance.TryGetValue(next, out known) && candidate >= known) continue;
                    distance[next] = candidate;
                    previous[next] = edge;
                    if (!open.Contains(next)) open.Add(next);
                }
            }

            if (source != target && !previous.ContainsKey(target)) return false;
            var middle = new List<TEdge>();
            TNode cursor = target;
            while (cursor != source)
            {
                TEdge edge;
                if (!previous.TryGetValue(cursor, out edge)) return false;
                middle.Add(edge);
                cursor = edge.From;
            }
            middle.Reverse();
            route = new List<TEdge> { from };
            route.AddRange(middle);
            if (route[route.Count - 1] != to) route.Add(to);
            return true;
        }

        public void Update()
        {
            ReleaseClearedReservations();
            UpdatePoints();
        }

        void ReleaseClearedReservations()
        {
            foreach (var plan in plans.Values)
            {
                float rearOdo = plan.Train.Odo - plan.Train.Length - 30f;
                for (int i = 0; i < plan.Route.Count; i++)
                {
                    float endOdo = plan.Starts[i] + plan.Route[i].Length;
                    if (endOdo >= rearOdo) continue;

                    string edgeOwner;
                    if (edgeOwners.TryGetValue(plan.Route[i], out edgeOwner) && edgeOwner == plan.Id)
                        edgeOwners.Remove(plan.Route[i]);

                    TNode node = plan.Route[i].To;
                    string switchOwner;
                    if (node.IsSwitch && switchOwners.TryGetValue(node, out switchOwner) && switchOwner == plan.Id)
                    {
                        switchOwners.Remove(node);
                        switchRoutes.Remove(node);
                    }
                }
            }
        }

        void UpdatePoints()
        {
            foreach (var pair in switchRoutes)
            {
                int index = pair.Key.Outs.IndexOf(pair.Value);
                if (index >= 0) pair.Key.Sel = index;
            }
            graph.UpdateAspects();
        }

        public bool TryReservePlatform(string platformId, string trainId)
        {
            if (string.IsNullOrEmpty(platformId) || string.IsNullOrEmpty(trainId)) return false;
            string owner;
            if (platformOwners.TryGetValue(platformId, out owner) && owner != trainId) return false;
            platformOwners[platformId] = trainId;
            return true;
        }

        public bool IsPlatformAvailable(string platformId, string trainId)
        {
            string owner;
            return !platformOwners.TryGetValue(platformId, out owner) || owner == trainId;
        }

        public void ReleasePlatform(string platformId, string trainId)
        {
            string owner;
            if (platformOwners.TryGetValue(platformId, out owner) && owner == trainId)
                platformOwners.Remove(platformId);
        }

        public bool IsEdgeReservedByOther(TEdge edge, string trainId)
        {
            string owner;
            return edgeOwners.TryGetValue(edge, out owner) && owner != trainId;
        }

        public void ReleaseTrain(string trainId)
        {
            Plan plan;
            if (!plans.TryGetValue(trainId, out plan)) return;
            foreach (var edge in plan.Route)
            {
                string owner;
                if (edgeOwners.TryGetValue(edge, out owner) && owner == trainId) edgeOwners.Remove(edge);
            }
            var removeSwitches = new List<TNode>();
            foreach (var pair in switchOwners) if (pair.Value == trainId) removeSwitches.Add(pair.Key);
            foreach (var node in removeSwitches) { switchOwners.Remove(node); switchRoutes.Remove(node); }
            var removePlatforms = new List<string>();
            foreach (var pair in platformOwners) if (pair.Value == trainId) removePlatforms.Add(pair.Key);
            foreach (var key in removePlatforms) platformOwners.Remove(key);
            plans.Remove(trainId);
        }

        public List<string> PlannedEdgeIds(string trainId)
        {
            var result = new List<string>();
            Plan plan;
            if (plans.TryGetValue(trainId, out plan))
                foreach (var edge in plan.Route) result.Add(edge.Id);
            return result;
        }
    }
}