using Godot;

namespace TNBCY
{
    // Controller Centre hook-up. The centre itself lives in Scripts/Controller/.
    // Opened from the home screen (CONTROLLER CENTRE button) and, during a drive, from the pause screen (CONTROLLER button, top right).
    public partial class Main
    {
        ControllerCentre ccCentre; ScheduleStore ccSchedules; Button ccButton; Scenario ccSeen;

        // Called once by ControllerHost (a child node in Main.tscn) after Main._Ready has built the menu and the HUD.
        public void InstallControllerCentre()
        {
            if (ccCentre != null) return;
            ccSchedules = new ScheduleStore(trainsList);
            ccSchedules.Load(); ccSchedules.ApplyToGame();       // cancelled trains disappear from SELECT TRAIN

            var ctx = new ControllerContext
            {
                GetScenario = () => sc,
                SwitchesChanged = () => { if (sc != null) RefreshSwitchTags(); },
                Schedules = ccSchedules
            };

            var layer = new CanvasLayer { Layer = 10 }; AddChild(layer);
            ccCentre = new ControllerCentre(); layer.AddChild(ccCentre);
            // To add a new Controller Centre module later, register it here.
            ccCentre.AddModule(new TrainScheduleModule());
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
        }

        void CcTick()
        {
            if (ccButton != null && ccCentre != null && pauseOverlay != null)
                ccButton.Visible = pauseOverlay.Visible && !ccCentre.Visible;

            if (sc != ccSeen)
            {
                ccSeen = sc;       // a new drive has just started (or a saved one was resumed): the game clock follows the train's timetable
                if (sc != null)
                {
                    string clock = ccSchedules.ClockStartFor(curConsist);
                    if (clock != null) sc.Net.clockStart = clock;
                }
            }
        }
    }
}
