using Godot;

namespace TNBCY
{
    // Controller Centre hook-up. The centre and the dispatcher live in Scripts/Controller/.
    // Opened from the home screen (CONTROLLER CENTRE button) and, during a drive, from the pause screen (CONTROLLER button, top right).
    public partial class Main
    {
        ControllerCentre ccCentre; ScheduleStore ccSchedules; Dispatcher ccDispatch; PlatformPrompt ccPrompt;
        Button ccButton; Scenario ccSeen; TrainOption ccPendingAi; float ccOrderT;

        // Called once by ControllerHost (a child node in Main.tscn) after Main._Ready has built the menu and the HUD.
        public void InstallControllerCentre()
        {
            if (ccCentre != null) return;
            ccSchedules = new ScheduleStore(trainsList);
            ccSchedules.Load(); ccSchedules.ApplyToGame();       // cancelled trains disappear from SELECT TRAIN

            // the "choose your platform" panel (above the HUD, below the Controller Centre)
            var promptLayer = new CanvasLayer { Layer = 8 }; AddChild(promptLayer);
            ccPrompt = new PlatformPrompt(); promptLayer.AddChild(ccPrompt); ccPrompt.Setup();

            // the automatic controller
            ccDispatch = new Dispatcher(ccSchedules, ccPrompt,
                (m, ok) => Toast(m, ok, 4f),
                () => { if (sc != null) RefreshSwitchTags(); },
                () => paused);
            ccDispatch.Autopilot = ccSchedules.Autopilot;
            ccPrompt.OnChoose = key => ccDispatch.Pick(key);
            ccPrompt.OnChange = () => ccDispatch.Change();

            var ctx = new ControllerContext
            {
                GetScenario = () => sc,
                SwitchesChanged = () => { if (sc != null) RefreshSwitchTags(); },
                Schedules = ccSchedules,
                Dispatch = ccDispatch
            };

            var layer = new CanvasLayer { Layer = 10 }; AddChild(layer);
            ccCentre = new ControllerCentre(); layer.AddChild(ccCentre);
            // To add a new Controller Centre module later, register it here.
            ccCentre.AddModule(new ControlRoomModule());
            ccCentre.AddModule(new TrainScheduleModule());
            ccCentre.AddModule(new PlatformModule());
            ccCentre.AddModule(new SignalModule());
            ccCentre.AddModule(new SwitchModule());
            ctx.Say = ccCentre.Say;
            ccCentre.Setup(ctx);
            ccCentre.Tick = CcTick;

            // Shortcut while the game is paused (above the pause screen, top right).
            var btnLayer = new CanvasLayer { Layer = 9 }; AddChild(btnLayer);
            ccButton = Ui.MakeButton("CONTROLLER", 22, false, new Vector2(230, 56));
            btnLayer.AddChild(ccButton); Ui.Place(ccButton, 1, 0, 1, 0, -260, 24, -30, 80);
            ccButton.Visible = false;
            ccButton.Pressed += () => ccCentre.Open();

            frontEnd.AddControllerButton(() => ccCentre.Open());

            // The controller chooses which train runs as the other service: put it first in the list just before the game picks it.
            var origStart = frontEnd.OnStart;
            frontEnd.OnStart = (r, t) => { CcPrepareAi(t); if (origStart != null) origStart(r, t); };
            var origResume = frontEnd.OnResume;
            frontEnd.OnResume = () =>
            {
                var s = Session.Load();
                if (s != null) CcPrepareAi(ccSchedules.OptionForFile(s.consist));
                if (origResume != null) origResume();
            };
        }

        void CcPrepareAi(TrainOption player)
        {
            ccSchedules.ApplyToGame();                           // normal order first
            ccPendingAi = null; ccOrderT = 0f;
            if (player == null || !ccSchedules.Autopilot) return;
            ccPendingAi = ccSchedules.PickAi(player);
            if (ccPendingAi != null && trainsList.Remove(ccPendingAi)) trainsList.Insert(0, ccPendingAi);
        }

        void CcTick(float dt)
        {
            if (ccButton != null && ccCentre != null && pauseOverlay != null)
                ccButton.Visible = pauseOverlay.Visible && !ccCentre.Visible;

            // the game has not started within 8 seconds (it failed or was cancelled): put the train list back in order
            if (ccPendingAi != null) { ccOrderT += dt; if (ccOrderT > 8f) { ccPendingAi = null; ccSchedules.ApplyToGame(); } }

            if (sc != ccSeen)
            {
                ccSeen = sc;
                if (sc != null)
                {
                    string clock = ccSchedules.ClockStartFor(curConsist);          // the game clock starts at the train's departure time
                    if (clock != null) sc.Net.clockStart = clock;
                    var player = ccSchedules.OptionForFile(curConsist);
                    TrainOption ai = ccPendingAi;
                    if (ai == null) ai = trainsList.Find(x => x != null && !string.IsNullOrEmpty(x.file) && x.file != curConsist);   // what the game itself picked
                    ccPendingAi = null;
                    ccSchedules.ApplyToGame();                                      // normal list order again
                    ccDispatch.Begin(sc, player, ai);
                }
                else ccDispatch.End();
            }

            if (sc != null && !paused)
            {
                ccDispatch.Update(dt);

                // a loop platform is a stop too: the HUD's "next stop" would not know it
                string loopName; float loopDist;
                if (hudBar != null && ccDispatch.NextLoop(out loopName, out loopDist))
                {
                    hudBar.NextName = loopName;
                    hudBar.NextDist = loopDist < 1000f ? Mathf.RoundToInt(loopDist) + " m" : (loopDist / 1000f).ToString("0.0") + " km";
                }
                // say what the controller is doing with the other train (waiting to depart, held at a signal ...)
                if (ccDispatch.AiNote != null && aiL != null && sc.Ai != null)
                    aiL.Text = aiL.Text.Replace("Opposing train: " + sc.Ai.Status, "Opposing train: " + ccDispatch.AiNote);
            }
        }
    }
}
