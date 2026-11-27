using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace TNBCY
{
    public class PlatformLabels { public Dictionary<string, string> labels = new Dictionary<string, string>(); }

    // The automatic controller. While a drive is running it
    //  - starts the AI train at its timetable time and makes it stop at, or skip, the stations of its timetable,
    //  - gives every train a platform (main line, loop line or Secunderabad yard track) and sets the switches for it,
    //  - holds a train at a red signal when no platform is free or the track ahead is occupied,
    //  - offers the player a free platform 2 km before each station, and ends the trip at the player's destination.
    public class Dispatcher
    {
        sealed class Resv { public string Owner = ""; public bool IsPlayer, Seen; public PlatformSlot Slot; }
        sealed class SigSave { public bool Held, Red, Started; public float Timer; public string Reason = ""; }

        public bool Autopilot = true;
        public readonly List<string> Log = new List<string>();
        public List<PlatformSlot> Slots = new List<PlatformSlot>();
        public Scenario Sc;
        public string AiNote;                                  // replaces the AI status text on the HUD, or null
        public string PlayerName = "Your train", AiName = "Opposing train";
        public TrainOption AiOpt; public ScheduleEntry AiEntry;

        readonly ScheduleStore store; readonly PlatformPrompt prompt;
        readonly System.Action<string, bool> toast; readonly System.Action refreshTags; readonly System.Func<bool> isPaused;
        readonly Dictionary<string, string> labels;
        readonly Dictionary<string, Resv> resv = new Dictionary<string, Resv>();
        readonly Dictionary<TSignal, SigSave> holds = new Dictionary<TSignal, SigSave>();
        ScheduleEntry playerEntry;
        string finalBase, curComplex, chosenKey, aiBase, aiKey, aiDepartText = "";
        float aiHoldUntil, planT = 10f;
        bool aiDeparted, terminalDone, announcedNone;
        List<SlotRoute> lastGroup = new List<SlotRoute>();

        public Dispatcher(ScheduleStore s, PlatformPrompt p, System.Action<string, bool> toastFn, System.Action refresh, System.Func<bool> pausedFn)
        {
            store = s; prompt = p; toast = toastFn; refreshTags = refresh; isPaused = pausedFn;
            labels = LoadLabels();
        }

        static Dictionary<string, string> LoadLabels()
        {
            try
            {
                using var f = Godot.FileAccess.Open("res://Data/platforms.json", Godot.FileAccess.ModeFlags.Read);
                var pl = JsonSerializer.Deserialize<PlatformLabels>(f.GetAsText(), new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true });
                if (pl != null && pl.labels != null) return pl.labels;
            }
            catch (System.Exception ex) { GD.PrintErr("Controller: platform names not loaded: " + ex.Message); }
            return new Dictionary<string, string>();
        }

        // ================================================================== start / stop of a drive
        public void Begin(Scenario scenario, TrainOption player, TrainOption ai)
        {
            End();
            Sc = scenario; AiOpt = ai;
            Log.Clear(); resv.Clear(); lastGroup.Clear();
            curComplex = null; chosenKey = null; aiBase = null; aiKey = null; AiNote = null;
            aiDeparted = false; terminalDone = false; announcedNone = false; planT = 10f;
            PlayerName = player != null && !string.IsNullOrEmpty(player.name) ? player.name : "Your train";
            AiName = ai != null && !string.IsNullOrEmpty(ai.name) ? ai.name : "Opposing train";
            playerEntry = player != null ? store.EntryFor(player) : null;
            AiEntry = ai != null ? store.EntryFor(ai) : null;
            finalBase = playerEntry != null && !string.IsNullOrEmpty(playerEntry.finalStop) ? RailTools.BaseName(playerEntry.finalStop) : null;
            Slots = RailTools.BuildSlots(Sc.Graph, labels);

            // a signal that is not a timed hold is never red by design: a red one here is left over from a saved session
            foreach (var s in Sc.Graph.Signals) if (!s.Held && s.Red) s.Red = false;
            Sc.Graph.UpdateAspects();

            ConfigureAiStops();
            aiHoldUntil = ComputeAiHold();
            AddLog("Controller online. " + (Sc.Ai != null ? AiName + " is the other service." : "No other train on this route."));
        }

        public void End()
        {
            if (Sc != null && holds.Count > 0) { ReleaseHolds(); Sc.Graph.UpdateAspects(); }
            holds.Clear(); resv.Clear();
            if (prompt != null) prompt.HideModel();
            Sc = null; AiNote = null;
        }

        // The AI stops only where its timetable says so: every other stop mark is marked as already served for it.
        void ConfigureAiStops()
        {
            if (AiEntry == null || AiEntry.stops == null || AiEntry.stops.Count == 0) return;
            var skipped = new List<string>();
            foreach (var st in Sc.Graph.Stations)
            {
                if (!st.Stop || AiStopsAt(RailTools.BaseName(st.Name))) continue;
                st.Served = true;
                string b = RailTools.BaseName(st.Name);
                if (!skipped.Contains(b)) skipped.Add(b);
            }
            if (skipped.Count > 0) AddLog(AiName + " runs through " + string.Join(", ", skipped));
        }

        public bool AiStopsAt(string baseName)
        {
            if (AiEntry == null || AiEntry.stops == null || AiEntry.stops.Count == 0) return true;
            foreach (var s in AiEntry.stops)
                if (string.Equals(RailTools.BaseName(s.station), baseName, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // seconds after the start of the drive at which the AI train may leave (0 = no timetable: it leaves after the usual 60 s)
        float ComputeAiHold()
        {
            aiDepartText = "";
            if (AiEntry == null || !store.HasTimetable(AiEntry)) return 0f;
            int dep = ScheduleStore.ParseTime(store.DepartureText(AiEntry));
            int clock = ScheduleStore.ParseTime(Sc.Net.clockStart);
            if (dep < 0 || clock < 0) return 0f;
            float secs = Mathf.Clamp((dep - clock) * 60f, 60f, 900f);       // never make the player wait more than 15 minutes
            aiDepartText = ScheduleStore.FmtTime(clock + Mathf.RoundToInt(secs / 60f));
            return secs;
        }

        // ================================================================== every frame
        public void Update(float dt)
        {
            if (Sc == null) return;
            LoopStops(); FinalStop();
            if (!Autopilot || Sc.Ended)
            {
                if (holds.Count > 0) { ReleaseHolds(); Sc.Graph.UpdateAspects(); }
                if (prompt != null) prompt.HideModel();
                AiNote = null;
                return;
            }
            HoldAiDeparture();
            planT += dt;
            if (planT >= 0.4f) { planT = 0f; Plan(); }
        }

        void HoldAiDeparture()
        {
            var ai = Sc.Ai; if (ai == null) return;
            if (!aiDeparted && ai.T.Speed > 0.5f) { aiDeparted = true; AddLog(AiName + " departs"); }
            if (aiDeparted || aiHoldUntil <= 60f) return;
            float left = aiHoldUntil - Sc.SimTime;
            if (left > 0f) ai.Dwell = left;
        }

        // ================================================================== loop platforms and the end of the trip (game rules, always on)
        void LoopStops()
        {
            var t = Sc.Train; if (t == null || Sc.Ended) return;
            foreach (var s in Slots)
            {
                if (!s.IsLoop || s.Done || t.FrontSeg.Edge != s.Edge) continue;
                float d = s.Offset - t.FrontOffset;
                if (d <= 80f && d >= -40f && t.Speed < 0.3f)
                {
                    s.Done = true; MarkServed(s);
                    float off = System.Math.Abs(d);
                    Say("Stopped at " + s.Name + " (loop platform)" + (off <= 8f ? "" : " - " + Mathf.RoundToInt(off) + " m from the mark"), true);
                }
                else if (d < -40f)
                {
                    s.Done = true;
                    if (!PlayerServed(s.Base)) Say("Passed " + s.Name + " without stopping", false);
                }
            }
        }

        void MarkServed(PlatformSlot s)
        {
            var names = Sc.ServedNames();
            if (!names.Contains(s.Name)) { names.Add(s.Name); Sc.SetServed(names); }
            foreach (var st in Sc.Graph.Stations)
                if (st.Name == s.Name && st.Edge.From.Id[0] == s.Edge.From.Id[0]) st.Done = true;
        }

        void FinalStop()
        {
            if (finalBase == null || Sc.Ended || !PlayerServed(finalBase)) return;
            Sc.Ended = true;
            int el = (int)Sc.SimTime;
            Sc.EndMessage = "Route complete\nTrip time " + (el / 60) + ":" + (el % 60).ToString("00");
            AddLog("Trip complete at " + finalBase);
        }

        bool PlayerServed(string baseName)
        {
            foreach (var n in Sc.ServedNames()) if (RailTools.BaseName(n) == baseName) return true;
            return false;
        }

        void Say(string msg, bool good)
        {
            if (Sc != null && Sc.OnEvent != null) Sc.OnEvent(msg, good ? "ok" : "bad");
        }

        // ================================================================== planning (about 2.5 times a second)
        void Plan()
        {
            var wants = new Dictionary<TSignal, string>();       // signals that must be red now, with the reason
            string note = null;
            PlanPlayer(wants);
            if (Sc.AiTrain != null)
            {
                note = PlanAi(wants);
                string n2 = ProtectAhead(Sc.AiTrain, Sc.Train, false, wants);
                if (note == null) note = n2;
            }
            ProtectAhead(Sc.Train, Sc.AiTrain, true, wants);
            if (Sc.AiTrain != null && AiBlockedAhead()) { Sc.Ai.Dwell = System.Math.Max(Sc.Ai.Dwell, 2f); note = "holding: track ahead is occupied"; }
            if (note == null && Sc.Ai != null && !aiDeparted && aiHoldUntil > 60f && Sc.SimTime < aiHoldUntil) note = "waiting to depart " + aiDepartText;
            AiNote = note;
            ApplySignals(wants);
            Sc.Graph.UpdateAspects();
            ReleaseReservations();
        }

        // ---------------------------------------------------------------- the player's train
        void PlanPlayer(Dictionary<TSignal, string> wants)
        {
            var t = Sc.Train;
            var routes = RailTools.Ahead(Slots, t, 7000f);
            string baseName = null; float nearest = 1e9f;
            foreach (var r in routes)
                if (!PlayerServed(r.Slot.Base) && r.Dist < nearest) { nearest = r.Dist; baseName = r.Slot.Base; }
            if (baseName == null) { curComplex = null; chosenKey = null; announcedNone = false; lastGroup.Clear(); HidePrompt(); return; }

            var group = new List<SlotRoute>();
            foreach (var r in routes) if (r.Slot.Base == baseName) group.Add(r);
            group.Sort(Prefer);
            lastGroup = group;
            if (baseName != curComplex) { curComplex = baseName; chosenKey = null; announcedNone = false; }

            float sw = SwitchDist(group);
            bool hasSwitch = sw < 1e8f;
            float entry = hasSwitch ? sw : nearest;          // distance to where the station begins (its first switch, or the stop mark)
            bool single = group.Count == 1;
            if (entry > 3500f) { HidePrompt(); return; }

            var free = new List<SlotRoute>();
            foreach (var r in group) if (TakenBy(r.Slot, true) == null) free.Add(r);
            if (chosenKey != null && FindRoute(free, chosenKey) == null)
            {
                AddLog("Your platform is no longer available");
                ReleaseOwn(true); chosenKey = null;
            }

            // nothing free: the home signal stays red until a platform is free
            if (chosenKey == null && free.Count == 0)
            {
                var sig = HomeSignal(t, entry, false);
                if (sig != null) wants[sig] = "no platform free at " + baseName;
                if (!announcedNone)
                {
                    announcedNone = true;
                    AddLog("No platform free at " + baseName + ": home signal held at red");
                    toast("No platform free at " + baseName + " - signal at red", false);
                }
                if (entry <= 2000f && !isPaused()) ShowPrompt(group, baseName, entry, null); else HidePrompt();
                return;
            }
            announcedNone = false;

            // the player did not choose in time: take the platform the switches already lead to, or the best free one
            if (chosenKey == null)
            {
                bool now = single ? entry <= 2000f : (hasSwitch ? entry <= 480f : entry <= 800f);
                if (now)
                {
                    SlotRoute pick = null;
                    foreach (var r in free) if (r.Mismatch() == 0) { pick = r; break; }
                    if (pick == null) pick = free[0];
                    Choose(pick, single ? "allotted" : "auto-selected");
                }
            }
            if (chosenKey != null)
            {
                var cur = FindRoute(group, chosenKey);
                if (cur != null) { Reserve(cur.Slot, PlayerName, true); ApplyRoute(cur); }
            }
            if (single || entry > 2000f || isPaused()) { HidePrompt(); return; }
            ShowPrompt(group, baseName, entry, chosenKey);
        }

        void Choose(SlotRoute r, string how)
        {
            ReleaseOwn(true);
            chosenKey = r.Slot.Key;
            Reserve(r.Slot, PlayerName, true);
            AddLog("You: platform " + how + " - " + r.Slot.Label + " at " + r.Slot.Base);
            toast("Platform " + how + ": " + r.Slot.Label + " (" + r.Slot.Base + ")", true);
        }

        // called by the popup buttons
        public void Pick(string key)
        {
            if (Sc == null) return;
            var r = FindRoute(lastGroup, key);
            if (r == null) return;
            if (TakenBy(r.Slot, true) != null) { toast("That platform has just been taken", false); planT = 10f; return; }
            Choose(r, "chosen");
            planT = 10f;
        }

        public void Change()
        {
            if (chosenKey == null) return;
            ReleaseOwn(true); chosenKey = null;
            AddLog("You: platform choice reopened");
            planT = 10f;
        }

        void ShowPrompt(List<SlotRoute> group, string baseName, float entry, string chosen)
        {
            if (prompt == null) return;
            var m = new PromptModel { ChooseMode = chosen == null };
            m.Title = (m.ChooseMode ? "ARRIVING AT " : "PLATFORM SET - ") + baseName.ToUpperInvariant();
            m.Sub = "Station begins in " + FormatDist(entry) + (m.ChooseMode ? " - choose your platform" : "");
            bool recommend = true, anyFree = false;
            foreach (var r in group)
            {
                if (!m.ChooseMode && r.Slot.Key != chosen) continue;
                string who; string state = OtherState(r.Slot, true, out who);
                var o = new PromptOption { Key = r.Slot.Key, Label = r.Slot.Label.ToUpperInvariant(), Free = state == null, Chosen = r.Slot.Key == chosen };
                if (state != null) o.Note = state + " (" + who + ")";
                if (o.Free) anyFree = true;
                if (o.Free && recommend && m.ChooseMode) { o.Recommended = true; recommend = false; }
                m.Options.Add(o);
            }
            if (m.ChooseMode && !anyFree) m.Warn = "NO PLATFORM FREE - SIGNAL HELD AT RED";
            m.CanChange = !m.ChooseMode && SwitchDist(group) > 150f;
            prompt.ShowModel(m);
        }

        void HidePrompt() { if (prompt != null) prompt.HideModel(); }

        // ---------------------------------------------------------------- the AI train
        string PlanAi(Dictionary<TSignal, string> wants)
        {
            var ai = Sc.AiTrain;
            // destination reached: stay at the platform for a while, then run on
            if (!terminalDone && AiEntry != null && !string.IsNullOrEmpty(AiEntry.finalStop) && Sc.Ai != null
                && Sc.Ai.Status.StartsWith("at " + RailTools.BaseName(AiEntry.finalStop), System.StringComparison.Ordinal))
            {
                terminalDone = true; Sc.Ai.Dwell = System.Math.Max(Sc.Ai.Dwell, 240f);
                AddLog(AiName + " has reached " + RailTools.BaseName(AiEntry.finalStop));
            }
            if (ai.Finished) { ReleaseOwn(false); aiKey = null; aiBase = null; return null; }

            var routes = RailTools.Ahead(Slots, ai, 7000f);
            string baseName = null; float nearest = 1e9f;
            foreach (var r in routes) if (r.Dist < nearest) { nearest = r.Dist; baseName = r.Slot.Base; }
            if (baseName == null) { aiBase = null; aiKey = null; return null; }

            var group = new List<SlotRoute>();
            foreach (var r in routes) if (r.Slot.Base == baseName) group.Add(r);
            group.Sort(Prefer);
            float sw = SwitchDist(group);
            float entry = sw < 1e8f ? sw : nearest;
            if (baseName != aiBase) { aiBase = baseName; aiKey = null; }
            if (entry > 3000f) return null;

            bool stops = AiStopsAt(baseName);
            if (aiKey != null)
            {
                var cur = FindRoute(group, aiKey);
                if (cur != null) { Reserve(cur.Slot, AiName, false); ApplyRoute(cur); return null; }
                aiKey = null;
            }
            SlotRoute pick = null;
            foreach (var r in group)                 // main line first; a train that stops needs a real stop mark, so never the loop
            {
                if (stops && r.Slot.IsLoop) continue;
                if (TakenBy(r.Slot, false) != null) continue;
                pick = r; break;
            }
            if (pick != null)
            {
                aiKey = pick.Slot.Key; Reserve(pick.Slot, AiName, false); ApplyRoute(pick);
                AddLog(AiName + (stops ? " stops at " : " runs through ") + baseName + " on " + pick.Slot.Label);
                return null;
            }
            var sig = HomeSignal(ai, entry, true);
            if (sig != null) wants[sig] = AiName + " waits for a platform at " + baseName;
            return "waiting for a platform at " + baseName;
        }

        // true when the other train is right in front of the AI train (a last safety net behind the signals)
        bool AiBlockedAhead()
        {
            var ai = Sc.AiTrain; var other = Sc.Train;
            if (ai == null || other == null || ai.Finished) return false;
            float look = 120f + ai.Speed * 4f, acc = 0f;
            var e = ai.FrontSeg.Edge; float from = ai.FrontOffset + 2f;
            for (int hop = 0; hop < 6 && e != null; hop++)
            {
                float to = System.Math.Min(e.Length, from + (look - acc));
                if (RailTools.Occupies(other, e, from, to)) return true;
                acc += to - from;
                var n = e.To;
                if (n.Outs.Count == 0) return false;
                // the other train is about to join the same track from a parallel line
                var oe = other.FrontSeg.Edge;
                if (oe != e && oe.To == n && oe.Length - other.FrontOffset < 150f + other.Speed * 5f && acc + (e.Length - to) < look + 200f && e.Length - from < 400f) return true;
                if (acc >= look - 0.1f) return false;
                e = n.Outs[n.Sel]; from = 0f;
            }
            return false;
        }

        // ---------------------------------------------------------------- signals
        // A signal is held at red while another train stands in the block beyond it.
        string ProtectAhead(Train t, Train other, bool isPlayer, Dictionary<TSignal, string> wants)
        {
            if (t == null || other == null || t.Finished) return null;
            float v = t.Speed, need = isPlayer ? v * v / 0.9f + 120f : v * v / 0.6f + 100f;     // far enough away to stop in time
            foreach (var kv in RailTools.SignalsAhead(Sc.Graph, t, 3000f))
            {
                var sg = kv.Key;
                if (!RailTools.BlockBusy(Sc.Graph, sg, other)) continue;
                if (holds.ContainsKey(sg) || kv.Value >= need)
                {
                    if (!wants.ContainsKey(sg)) wants[sg] = "track ahead is occupied";
                    return isPlayer ? null : "held at signal " + sg.Id + ": track ahead is occupied";
                }
                break;
            }
            return null;
        }

        // the signal that protects a station entry: the last signal before the entry that is still far enough away to stop at
        TSignal HomeSignal(Train t, float limit, bool isAi)
        {
            float v = t.Speed, need = isAi ? v * v / 0.6f + 100f : v * v / 0.9f + 120f;
            TSignal best = null; float bd = -1f;
            foreach (var kv in RailTools.SignalsAhead(Sc.Graph, t, 7000f))
            {
                float d = kv.Value;
                if (d > limit + 20f) break;
                if (holds.ContainsKey(kv.Key)) return kv.Key;
                if (d >= need && d > bd) { bd = d; best = kv.Key; }
            }
            return best;
        }

        void ApplySignals(Dictionary<TSignal, string> wants)
        {
            foreach (var kv in wants)
            {
                var s = kv.Key; SigSave h;
                if (holds.TryGetValue(s, out h)) { h.Reason = kv.Value; continue; }
                holds[s] = new SigSave { Held = s.Held, Red = s.Red, Started = s.Started, Timer = s.Timer, Reason = kv.Value };
                s.Held = false; s.Red = true; s.Started = false; s.Timer = 0f;
                AddLog("Signal " + s.Id + " held at red: " + kv.Value);
            }
            var release = new List<TSignal>();
            foreach (var kv in holds) if (!wants.ContainsKey(kv.Key)) release.Add(kv.Key);
            foreach (var s in release) RestoreSignal(s);
        }

        void RestoreSignal(TSignal s)
        {
            SigSave h;
            if (!holds.TryGetValue(s, out h)) return;
            s.Held = h.Held; s.Red = h.Red; s.Started = h.Started; s.Timer = h.Timer;
            holds.Remove(s);
            AddLog("Signal " + s.Id + " released");
        }

        void ReleaseHolds()
        {
            var all = new List<TSignal>(holds.Keys);
            foreach (var s in all) RestoreSignal(s);
        }

        // ---------------------------------------------------------------- switches, platforms, reservations
        static int Prefer(SlotRoute a, SlotRoute b)
        {
            if (a.Slot.IsLoop != b.Slot.IsLoop) return a.Slot.IsLoop ? 1 : -1;      // main line first
            return a.Dist.CompareTo(b.Dist);
        }

        static SlotRoute FindRoute(List<SlotRoute> list, string key)
        {
            foreach (var r in list) if (r.Slot.Key == key) return r;
            return null;
        }

        static float SwitchDist(List<SlotRoute> group)
        {
            float sw = 1e9f;
            foreach (var r in group) if (r.NodeDist.Count > 0 && r.NodeDist[0] < sw) sw = r.NodeDist[0];
            return sw;
        }

        static string FormatDist(float d) { return d < 1000f ? Mathf.RoundToInt(d) + " m" : (d / 1000f).ToString("0.0") + " km"; }

        // "OCCUPIED" / "RESERVED" when someone other than the asker has the platform, else null
        string OtherState(PlatformSlot s, bool askerIsPlayer, out string who)
        {
            who = "";
            var other = askerIsPlayer ? Sc.AiTrain : Sc.Train;
            if (other != null && RailTools.Occupies(other, s.Edge, s.Offset - 650f, s.Offset + 80f)) { who = askerIsPlayer ? AiName : PlayerName; return "OCCUPIED"; }
            Resv r;
            if (resv.TryGetValue(s.Key, out r) && r.IsPlayer != askerIsPlayer) { who = r.Owner; return "RESERVED"; }
            return null;
        }

        string TakenBy(PlatformSlot s, bool askerIsPlayer)
        {
            string who;
            return OtherState(s, askerIsPlayer, out who) == null ? null : who;
        }

        void Reserve(PlatformSlot s, string owner, bool player)
        {
            Resv r;
            if (resv.TryGetValue(s.Key, out r) && r.IsPlayer == player) return;
            resv[s.Key] = new Resv { Owner = owner, IsPlayer = player, Slot = s };
        }

        void ReleaseOwn(bool player)
        {
            var drop = new List<string>();
            foreach (var kv in resv) if (kv.Value.IsPlayer == player) drop.Add(kv.Key);
            foreach (var k in drop) resv.Remove(k);
        }

        // a reservation ends when its train has been at the platform and has cleared it
        void ReleaseReservations()
        {
            var drop = new List<string>();
            foreach (var kv in resv)
            {
                var r = kv.Value; var t = r.IsPlayer ? Sc.Train : Sc.AiTrain;
                if (t == null) { drop.Add(kv.Key); continue; }
                bool on = RailTools.Occupies(t, r.Slot.Edge, r.Slot.Offset - 700f, r.Slot.Offset + 120f);
                if (on) r.Seen = true;
                else if (r.Seen) drop.Add(kv.Key);
            }
            foreach (var k in drop) resv.Remove(k);
        }

        // true when a train is on or very close to the switch
        bool SwitchBusy(TNode n)
        {
            return TrainAtNode(Sc.Train, n, 60f) || TrainAtNode(Sc.AiTrain, n, 90f);
        }

        static bool TrainAtNode(Train t, TNode n, float approach)
        {
            if (t == null) return false;
            var seg = t.FrontSeg;
            if (seg.Edge.To == n && seg.Edge.Length - t.FrontOffset < approach) return true;
            foreach (var o in n.Outs) if (RailTools.Occupies(t, o, 0f, 12f)) return true;
            return false;
        }

        static string PosName(TNode n, int i)
        {
            return i == 0 ? "MAIN" : (n.Outs.Count == 2 ? "LOOP" : "ROUTE " + (i + 1));
        }

        // sets the switches on the way to the slot (only the ones still far enough ahead and free of trains)
        void ApplyRoute(SlotRoute r)
        {
            bool changed = false;
            for (int i = 0; i < r.Nodes.Count; i++)
            {
                var n = r.Nodes[i];
                if (n.Sel == r.Sels[i] || r.NodeDist[i] < 60f || SwitchBusy(n)) continue;
                n.Sel = r.Sels[i]; changed = true;
                AddLog("Switch " + n.Name + " set to " + PosName(n, n.Sel));
            }
            if (changed) { Sc.Graph.UpdateAspects(); if (refreshTags != null) refreshTags(); }
        }

        // ================================================================== for the screens
        public bool SwitchInUse(TNode n) { return Sc != null && SwitchBusy(n); }

        public string HoldReason(TSignal s)
        {
            SigSave h;
            return holds.TryGetValue(s, out h) ? h.Reason : "";
        }

        // FREE / OCCUPIED / RESERVED, from nobody's point of view
        public string SlotState(PlatformSlot s, out string who)
        {
            who = "";
            if (Sc == null) return "FREE";
            if (Sc.Train != null && RailTools.Occupies(Sc.Train, s.Edge, s.Offset - 650f, s.Offset + 80f)) { who = PlayerName; return "OCCUPIED"; }
            if (Sc.AiTrain != null && RailTools.Occupies(Sc.AiTrain, s.Edge, s.Offset - 650f, s.Offset + 80f)) { who = AiName; return "OCCUPIED"; }
            Resv r;
            if (resv.TryGetValue(s.Key, out r)) { who = r.Owner; return "RESERVED"; }
            return "FREE";
        }

        string SlotLabel(string key)
        {
            foreach (var s in Slots) if (s.Key == key) return s.Label;
            return key;
        }

        public string PlayerPlanText()
        {
            if (curComplex == null) return "no station within reach";
            return curComplex + (chosenKey == null ? " - platform not chosen yet" : " - " + SlotLabel(chosenKey));
        }

        public string AiPlanText()
        {
            if (aiBase == null) return "no station within reach";
            return aiBase + (aiKey == null ? " - no platform yet" : " - " + SlotLabel(aiKey)) + (AiStopsAt(aiBase) ? "  (stops)" : "  (runs through)");
        }

        // the next loop platform on the player's path, when it comes before any other stop mark (the HUD does not know about loop platforms)
        public bool NextLoop(out string name, out float dist)
        {
            name = ""; dist = 0f;
            if (Sc == null || Sc.Ended || Sc.Train == null) return false;
            var t = Sc.Train; var e = t.FrontSeg.Edge; float acc = -t.FrontOffset;
            for (int hop = 0; hop < 12 && e != null; hop++)
            {
                foreach (var s in Slots)
                    if (s.IsLoop && !s.Done && s.Edge == e && acc + s.Offset >= -40f) { name = s.Name; dist = System.Math.Max(0f, acc + s.Offset); return true; }
                foreach (var st in Sc.Graph.Stations)
                    if (st.Stop && !st.Done && st.Edge == e && acc + st.Offset >= -40f) return false;
                acc += e.Length;
                var n = e.To;
                e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return false;
        }

        void AddLog(string text)
        {
            Log.Add((Sc != null ? Sc.ClockText() : "") + "  " + text);
            if (Log.Count > 60) Log.RemoveAt(0);
        }
    }
}
