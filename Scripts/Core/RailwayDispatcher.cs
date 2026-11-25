using System.Collections.Generic;
using Godot;

namespace TNBCY
{
    // Central route planner and interlocking reservation owner. Each train receives an
    // independent path. Track sections and points are reserved only inside a look-ahead
    // window in front of the train and are released once the train's rear has cleared them,
    // so following services can share a route at a safe distance.
    public class RailwayDispatcher
    {
        // How far ahead of a train's front sections are reserved (must exceed braking distance).
        public const float LookaheadM = 2500f;
        const float RearClearanceM = 30f;

        sealed class Plan
        {
            public string Id;
            public Train Train;
            public bool IsPlayer;
            public List<TEdge> Route = new List<TEdge>();
            public List<float> Starts = new List<float>();
            public int Reserved;   // route[Released..Reserved-1] are currently reserved
            public int Released;   // route[0..Released-1] are behind the train's rear
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
            if (string.IsNullOrEmpty(id) || train == null || train.FrontSeg == null || route == null || route.Count == 0)
            { reason = "Invalid train route or train starting position."; return false; }
            // Validate the complete directed path before taking any interlocking resources.
            // This catches malformed service data and prevents a bad route from being committed.
            if (route[0] != train.FrontSeg.Edge)
            { reason = "The planned route does not begin on the train's current track."; return false; }
            var visitedEdges = new HashSet<TEdge>();
            for (int i = 0; i < route.Count; i++)
            {
                TEdge current = route[i];
                if (current == null || current.From == null || current.To == null)
                { reason = "The planned route contains an invalid track section."; return false; }
                TEdge registered;
                if (!graph.Edges.TryGetValue(current.Id, out registered) || registered != current)
                { reason = "The planned route contains a track section outside this network: " + current.Id + "."; return false; }
                // Train.SetPlannedRoute stores one next edge per directed edge. A repeated
                // edge cannot be represented safely by that map, so reject it explicitly.
                if (!visitedEdges.Add(current))
                { reason = "The route repeats track section " + current.Id + " and cannot be represented safely."; return false; }
                if (i > 0)
                {
                    TEdge previous = route[i - 1];
                    if (previous.To != current.From || !previous.To.Outs.Contains(current))
                    { reason = "The planned route is not connected between " + previous.Id + " and " + current.Id + "."; return false; }
                }
            }

            // A route may not ask for two different settings of the same point.
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
            }

            var starts = new List<float>();
            float start = 0f;
            foreach (var edge in route) { starts.Add(start); start += edge.Length; }

            // Work out which part of the route can be reserved now (dry run). The train's own
            // section must be available; anything further ahead is reserved later as it advances.
            float horizon = train.FrontOffset + LookaheadM;
            int window = 0;
            while (window < route.Count && starts[window] <= horizon && ReserveConflict(route, window, id) == null)
                window++;
            if (window == 0)
            {
                reason = ReserveConflict(route, 0, id) ?? "The train's current track cannot be reserved.";
                return false;
            }

            // The replacement is valid: only now give up the train's previous reservations.
            if (plans.ContainsKey(id)) ReleaseTrain(id);
            var plan = new Plan { Id = id, Train = train, IsPlayer = isPlayer, Route = new List<TEdge>(route), Starts = starts };
            plans[id] = plan;
            for (int i = 0; i < window; i++) ReserveIndex(plan, i);
            plan.Reserved = window;
            train.SetPlannedRoute(route);
            UpdatePoints();
            return true;
        }

        // Returns null when route[i] (and the point at its far end) can be reserved by this train.
        string ReserveConflict(IList<TEdge> route, int i, string id)
        {
            string owner;
            if (edgeOwners.TryGetValue(route[i], out owner) && owner != id)
                return "Track section " + route[i].Id + " is already reserved by " + owner + ".";
            if (i + 1 < route.Count)
            {
                TNode node = route[i].To;
                if (node.Outs.Count > 1 && switchOwners.TryGetValue(node, out owner) && owner != id)
                    return "Switch " + node.Name + " is reserved by " + owner + ".";
            }
            return null;
        }

        void ReserveIndex(Plan plan, int i)
        {
            edgeOwners[plan.Route[i]] = plan.Id;
            if (i + 1 < plan.Route.Count)
            {
                TNode node = plan.Route[i].To;
                if (node.Outs.Count > 1)
                {
                    switchRoutes[node] = plan.Route[i + 1];
                    switchOwners[node] = plan.Id;
                }
            }
        }

        void ReleaseIndex(Plan plan, int i)
        {
            string owner;
            if (edgeOwners.TryGetValue(plan.Route[i], out owner) && owner == plan.Id)
                edgeOwners.Remove(plan.Route[i]);
            TNode node = plan.Route[i].To;
            if (switchOwners.TryGetValue(node, out owner) && owner == plan.Id)
            {
                switchOwners.Remove(node);
                switchRoutes.Remove(node);
            }
        }

        // Distance travelled along the plan by the train's front, measured from the start of route[0].
        bool TryFrontDistance(Plan plan, out float front)
        {
            front = 0f;
            if (plan.Train == null || plan.Train.FrontSeg == null) return false;
            int index = plan.Route.IndexOf(plan.Train.FrontSeg.Edge);
            if (index < 0) return false;
            front = plan.Starts[index] + plan.Train.FrontOffset;
            return true;
        }

        // Dijkstra over directed track edges. Sections currently reserved by another train cannot be used.
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
                    string pointOwner;
                    TEdge pointRoute;
                    if (node.IsSwitch && switchOwners.TryGetValue(node, out pointOwner) &&
                        pointOwner != owner && switchRoutes.TryGetValue(node, out pointRoute) && pointRoute != edge)
                        continue;

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
            foreach (var plan in plans.Values)
            {
                ReleaseClearedReservations(plan);
                ExtendReservationWindow(plan);
            }
            UpdatePoints();
        }

        // Release every section whose far end is behind the train's rear.
        void ReleaseClearedReservations(Plan plan)
        {
            float front;
            if (!TryFrontDistance(plan, out front)) return;
            float rear = front - plan.Train.Length - RearClearanceM;
            while (plan.Released < plan.Route.Count &&
                   plan.Starts[plan.Released] + plan.Route[plan.Released].Length < rear)
            {
                if (plan.Released < plan.Reserved) ReleaseIndex(plan, plan.Released);
                plan.Released++;
                if (plan.Reserved < plan.Released) plan.Reserved = plan.Released;
            }
        }

        // Reserve sections ahead of the train, in order, up to the look-ahead horizon.
        // If a section is held by another train, reservation stops there and the train's
        // movement authority ends at the start of that section.
        void ExtendReservationWindow(Plan plan)
        {
            float front;
            if (!TryFrontDistance(plan, out front)) return;
            float horizon = front + LookaheadM;
            while (plan.Reserved < plan.Route.Count && plan.Starts[plan.Reserved] <= horizon)
            {
                if (ReserveConflict(plan.Route, plan.Reserved, plan.Id) != null) break;
                ReserveIndex(plan, plan.Reserved);
                plan.Reserved++;
            }
        }

        // Distance from the train's front to the end of the track it has been cleared to use.
        // float.MaxValue means the whole remaining route is reserved (or the train is unplanned).
        public float AuthorityDistance(string trainId)
        {
            Plan plan;
            if (!plans.TryGetValue(trainId, out plan)) return float.MaxValue;
            if (plan.Reserved >= plan.Route.Count) return float.MaxValue;
            float front;
            if (!TryFrontDistance(plan, out front)) return 0f;
            return Mathf.Max(0f, plan.Starts[plan.Reserved] - front);
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
