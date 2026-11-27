using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace TNBCY
{
    public class StopTime { public string station = "", arr = "", dep = ""; }

    // One service in the timetable, read from Data/trains.json. The game itself ignores the schedule fields
    // (days, departure, arrival, stops, finalStop, aiPartner), so a train only needs those when the controller should plan for it.
    public class ScheduleEntry
    {
        public string name = "", file = "", kind = "", origin = "", destination = "", days = "", departure = "", arrival = "", finalStop = "";
        public int trainNumber, platform = 1, coaches, aiPartner;
        public float distanceKm;
        public List<StopTime> stops = new List<StopTime>();
    }
    public class ScheduleFile { public List<ScheduleEntry> trains = new List<ScheduleEntry>(); }

    // What the controller changed for one train. Saved on the phone, so it survives restarts.
    public class TrainOverride
    {
        public bool cancelled;
        public int delayMin, shiftMin;      // delayMin = running late, shiftMin = timetable moved earlier / later
        public bool IsDefault() { return !cancelled && delayMin == 0 && shiftMin == 0; }
    }
    public class ControllerSave
    {
        public bool autopilot = true;
        public Dictionary<string, TrainOverride> trains = new Dictionary<string, TrainOverride>();
    }

    // Timetable + controller settings. It also keeps the game's train list (the one on the SELECT TRAIN screen) in step:
    // cancelled services are taken out of it, and the game clock starts at the service's departure time.
    public class ScheduleStore
    {
        const string SavePath = "user://controller.json";
        const int DefaultDepartureMin = 9 * 60;     // the route's own clock start (09:00) when a train has no timetable
        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true, WriteIndented = true };

        public List<ScheduleEntry> Entries = new List<ScheduleEntry>();
        public bool Autopilot = true;         // the automatic controller (AI train, platforms, signals) is switched on
        readonly Dictionary<string, TrainOverride> overrides = new Dictionary<string, TrainOverride>();
        readonly List<TrainOption> live;      // the list the game shows and uses; cancelled trains are removed from it
        readonly List<TrainOption> master;    // every train, before any cancellation

        public ScheduleStore(List<TrainOption> liveList) { live = liveList; master = new List<TrainOption>(liveList); }

        // ------------------------------------------------------------------ load / save
        public void Load()
        {
            Entries = new List<ScheduleEntry>(); overrides.Clear();
            try
            {
                using var f = Godot.FileAccess.Open("res://Data/trains.json", Godot.FileAccess.ModeFlags.Read);
                var sf = JsonSerializer.Deserialize<ScheduleFile>(f.GetAsText(), Opt);
                if (sf != null && sf.trains != null) Entries = sf.trains;
            }
            catch (System.Exception ex) { GD.PrintErr("Controller: timetable load failed: " + ex.Message); }
            try
            {
                if (Godot.FileAccess.FileExists(SavePath))
                {
                    using var g = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
                    var sv = JsonSerializer.Deserialize<ControllerSave>(g.GetAsText(), Opt);
                    if (sv != null)
                    {
                        Autopilot = sv.autopilot;
                        if (sv.trains != null)
                            foreach (var kv in sv.trains) if (kv.Value != null) overrides[kv.Key] = kv.Value;
                    }
                }
            }
            catch (System.Exception ex) { GD.PrintErr("Controller: saved changes could not be read: " + ex.Message); }
        }

        void Save()
        {
            try
            {
                var sv = new ControllerSave { autopilot = Autopilot };
                foreach (var kv in overrides) if (!kv.Value.IsDefault()) sv.trains[kv.Key] = kv.Value;
                using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
                if (f == null) return;
                f.StoreString(JsonSerializer.Serialize(sv, Opt));
            }
            catch (System.Exception ex) { GD.PrintErr("Controller: save failed: " + ex.Message); }
        }

        public void SetAutopilot(bool on) { Autopilot = on; Save(); }

        // ------------------------------------------------------------------ time helpers
        public static int ParseTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            int c = s.IndexOf(':'); if (c <= 0) return -1;
            int h, m;
            if (!int.TryParse(s.Substring(0, c), out h) || !int.TryParse(s.Substring(c + 1), out m)) return -1;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }
        public static string FmtTime(int minutes)
        {
            minutes = ((minutes % 1440) + 1440) % 1440;
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        // ------------------------------------------------------------------ lookups
        public static string Key(ScheduleEntry e) { return e.trainNumber > 0 ? e.trainNumber.ToString() : e.file; }
        public TrainOverride Peek(ScheduleEntry e)
        {
            TrainOverride o;
            return overrides.TryGetValue(Key(e), out o) ? o : new TrainOverride();
        }
        TrainOverride Edit(ScheduleEntry e)
        {
            TrainOverride o; string k = Key(e);
            if (!overrides.TryGetValue(k, out o)) { o = new TrainOverride(); overrides[k] = o; }
            return o;
        }
        public ScheduleEntry EntryFor(TrainOption t)
        {
            foreach (var e in Entries)
            {
                if (t.trainNumber > 0) { if (e.trainNumber == t.trainNumber) return e; }
                else if (e.trainNumber <= 0 && e.file == t.file) return e;
            }
            return null;
        }
        public ScheduleEntry EntryForFile(string file)
        {
            foreach (var e in Entries) if (e.file == file) return e;
            return null;
        }
        public TrainOption OptionForFile(string file)
        {
            foreach (var t in master) if (t.file == file) return t;
            return null;
        }

        // The train the controller sends the other way (or ahead) while the player drives 'player'.
        // 1. the train named by "aiPartner" in trains.json, 2. the first train that starts on the opposite track, 3. the first other train.
        public TrainOption PickAi(TrainOption player)
        {
            var cands = new List<TrainOption>();
            foreach (var t in live) if (t != null && !string.IsNullOrEmpty(t.file) && t.file != player.file) cands.Add(t);
            if (cands.Count == 0) return null;
            var pe = EntryFor(player);
            if (pe != null && pe.aiPartner > 0)
                foreach (var t in cands) if (t.trainNumber == pe.aiPartner) return t;
            char dir = Dir(player);
            foreach (var t in cands) if (Dir(t) != dir) return t;
            return cands[0];
        }
        static char Dir(TrainOption t) { return string.IsNullOrEmpty(t.routeStartEdge) ? '?' : char.ToLowerInvariant(t.routeStartEdge[0]); }

        // ------------------------------------------------------------------ shown times and status
        public bool HasTimetable(ScheduleEntry e) { return ParseTime(e.departure) >= 0; }
        int BaseDeparture(ScheduleEntry e) { int b = ParseTime(e.departure); return b >= 0 ? b : DefaultDepartureMin; }
        int Moved(ScheduleEntry e) { var o = Peek(e); return o.shiftMin + o.delayMin; }

        public string DepartureText(ScheduleEntry e) { return FmtTime(BaseDeparture(e) + Moved(e)); }
        public string ArrivalText(ScheduleEntry e) { int a = ParseTime(e.arrival); return a < 0 ? "--:--" : FmtTime(a + Moved(e)); }
        public string StopTimeText(string t, ScheduleEntry e) { int v = ParseTime(t); return v < 0 ? "--:--" : FmtTime(v + Moved(e)); }

        public int StatusKind(ScheduleEntry e)      // 0 on time, 1 delayed, 2 cancelled
        {
            var o = Peek(e);
            return o.cancelled ? 2 : (o.delayMin > 0 ? 1 : 0);
        }
        public string StatusText(ScheduleEntry e)
        {
            var o = Peek(e);
            if (o.cancelled) return "CANCELLED";
            return o.delayMin > 0 ? "DELAYED +" + o.delayMin + " MIN" : "ON TIME";
        }

        // The game clock for a drive of this train ("HH:MM"), or null to keep the route's own start time.
        public string ClockStartFor(string consistFile)
        {
            var e = EntryForFile(consistFile);
            if (e == null) return null;
            if (!HasTimetable(e) && Moved(e) == 0) return null;
            return DepartureText(e);
        }

        // ------------------------------------------------------------------ changes
        public void AddDelay(ScheduleEntry e, int minutes)
        {
            var o = Edit(e); o.delayMin = System.Math.Max(0, System.Math.Min(240, o.delayMin + minutes)); Commit();
        }
        public void Retime(ScheduleEntry e, int minutes)
        {
            var o = Edit(e); o.shiftMin = System.Math.Max(-300, System.Math.Min(600, o.shiftMin + minutes)); Commit();
        }
        // false = refused, because at least one service has to keep running
        public bool SetCancelled(ScheduleEntry e, bool cancel)
        {
            if (cancel && !Peek(e).cancelled && ActiveCount() <= 1) return false;
            Edit(e).cancelled = cancel; Commit(); return true;
        }
        public void Reset(ScheduleEntry e) { overrides.Remove(Key(e)); Commit(); }

        int ActiveCount()
        {
            int n = 0;
            foreach (var t in master) { var e = EntryFor(t); if (e == null || !Peek(e).cancelled) n++; }
            return n;
        }
        void Commit() { Save(); ApplyToGame(); }

        // Puts the game's train list in line with the cancellations (the list object is shared with the SELECT TRAIN screen).
        // It also puts the list back in its normal order.
        public void ApplyToGame()
        {
            live.Clear();
            foreach (var t in master)
            {
                var e = EntryFor(t);
                if (e != null && Peek(e).cancelled) continue;
                live.Add(t);
            }
        }
    }
}
