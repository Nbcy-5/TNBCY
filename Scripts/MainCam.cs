using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Cameras. Switching views fades through black (the camera never flies through the train).
    // Chase and Free orbit around any coach: drag to look around, pinch to zoom, buttons to change coach.
    public partial class Main
    {
        int activeMode, fadePhase, wantFocus = -1, focusCar;
        float fadeA, idleT, chYaw, chPitch = .24f, chDist = 40f, frYaw = 1.35f, frPitch = .2f, frDist = 28f;
        Vector3 frPan, sidePos; bool sideInit, camPrimed;
        readonly RideFeel ride = new RideFeel();
        TEdge lastEdge; float hapticT;

        // Train running feel: updates the shake and sends a short phone vibration on joints and points.
        void RideUpdate(float dt, Train T, float heading)
        {
            ride.Update(dt, T.Speed, T.Odo, heading, Settings.ShakeK);
            var e = T.FrontSeg.Edge;
            if (lastEdge != null && e != lastEdge) ride.Impulse((lastEdge.To.IsSwitch || e.Turnout || lastEdge.Turnout) ? 1.7f : .7f);   // jolt over points
            lastEdge = e;
            hapticT -= dt;
            if (ride.Kick > 0f && hapticT <= 0f && Settings.HapticK > 0f && T.Speed > 1.5f)
            {
                Input.VibrateHandheld(Mathf.RoundToInt(10f + 22f * ride.Kick), Mathf.Clamp((.25f + .35f * ride.Kick) * Settings.HapticK, .1f, 1f));
                hapticT = .16f;
            }
        }
        ColorRect fadeRect;
        readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();

        void OnCamChanged(int mode)
        {
            sideInit = false; camPrimed = false; fingers.Clear();
            if (mode == 1) Toast("Chase: drag to look around, pinch to zoom, < COACH > to change coach", true);
            else if (mode == 5) Toast("Free: orbit the train - drag to look, pinch to zoom, 2 fingers to slide along it", true);
        }

        void Orbit(Train T, float yaw, float pitch, float dist, Vector3 pan, out Vector3 pos, out Vector3 target)
        {
            var c = T.Cars[Mathf.Clamp(focusCar, 0, T.Cars.Count - 1)];
            T.PointAt(T.Odo - c.Offset - c.Length * .5f, out var p, out var d);
            float head = Mathf.Atan2(d.X, d.Z);
            target = p + Vector3.Up * 2.2f + new Basis(Vector3.Up, head) * pan;
            float a = head + Mathf.Pi + yaw;
            pos = target + new Vector3(Mathf.Sin(a) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(a) * Mathf.Cos(pitch)) * dist;
            pos.Y = Mathf.Max(pos.Y, .6f);
        }

        void UpdateCamera(float dt)
        {
            var T = sc.Train;
            if (fadePhase == 0 && (touch.CamMode != activeMode || wantFocus >= 0)) fadePhase = 1;
            if (fadePhase == 1)
            {
                fadeA += dt / .12f;
                if (fadeA >= 1f)
                {
                    fadeA = 1f;
                    if (wantFocus >= 0) { focusCar = wantFocus; wantFocus = -1; Toast("Coach " + (focusCar + 1) + " of " + T.Cars.Count, true); }
                    activeMode = touch.CamMode; OnCamChanged(activeMode); fadePhase = 2;
                }
            }
            else if (fadePhase == 2) { fadeA -= dt / .25f; if (fadeA <= 0f) { fadeA = 0f; fadePhase = 0; } }
            fadeRect.Color = new Color(0f, 0f, 0f, fadeA); fadeRect.Visible = fadeA > .001f;

            int mode = activeMode; var up = Vector3.Up;
            T.PointAt(T.Odo, out var p, out var d);
            cam.Fov = mode == 0 ? 72f : 62f; cabRig.Visible = mode == 0; touch.ShowCoachButtons = mode == 1 || mode == 5;
            float nn = mode == 0 ? .12f : .5f;                    // a larger near plane in outside views gives the depth buffer 4x more precision (less flicker)
            if (!Mathf.IsEqualApprox(cam.Near, nn)) cam.Near = nn;
            RideUpdate(dt, T, Mathf.Atan2(d.X, d.Z));
            Vector3 pos, look;

            if (mode == 0)
            {
                T.PointAt(T.Odo - 2.8f, out var cp, out var cd);
                var b = Basis.LookingAt(cd, up) * new Basis(Vector3.Back, ride.Rot.Z) * new Basis(Vector3.Right, ride.Rot.X);
                pos = cp + up * 3.3f + b * new Vector3(ride.Pos.X, ride.Pos.Y, 0f);
                cam.GlobalTransform = new Transform3D(b, pos);
                UpdateCab(true);
                return;
            }
            UpdateCab(false);

            if (mode == 1)
            {
                if (fingers.Count == 0) { idleT += dt; if (idleT > 4f) { chYaw = Mathf.LerpAngle(chYaw, 0f, 1f - Mathf.Exp(-1.5f * dt)); chPitch = Mathf.Lerp(chPitch, .24f, 1f - Mathf.Exp(-1.5f * dt)); } }
                Orbit(T, chYaw, chPitch, chDist, Vector3.Zero, out pos, out look);
            }
            else if (mode == 5) Orbit(T, frYaw, frPitch, frDist, frPan, out pos, out look);
            else if (mode == 2) { pos = p + d * .5f + up * 1.7f; look = p + d * 120f + up * 1.5f; }
            else if (mode == 3)
            {
                if (!sideInit || (sidePos - p).Dot(d) < -90f) { sidePos = p + d * 190f - Right(d) * 12f + up * 2.6f; sideInit = true; }
                T.PointAt(T.Odo - T.Length * .5f, out var mp, out _);
                pos = sidePos; look = mp + up * 1.8f;
            }
            else { pos = p - d * 30f + up * 85f; look = p + d * 8f; }
            if (mode == 2)       // front view: half the cab shake
            {
                var bn = Basis.LookingAt(look - pos, up) * new Basis(Vector3.Back, ride.Rot.Z * .5f) * new Basis(Vector3.Right, ride.Rot.X * .5f);
                cam.GlobalTransform = new Transform3D(bn, pos + bn * new Vector3(ride.Pos.X * .6f, ride.Pos.Y * .6f, 0f));
                return;
            }
            cam.GlobalTransform = new Transform3D(Basis.LookingAt(look - pos, up), pos);
        }

        public override void _Input(InputEvent ev)
        {
            if (sc == null || touch == null || (activeMode != 1 && activeMode != 5)) { fingers.Clear(); return; }
            bool chase = activeMode == 1;
            if (ev is InputEventScreenTouch t)
            {
                if (t.Pressed) { if (!touch.Blocks(t.Position)) { fingers[t.Index] = t.Position; idleT = 0f; } } else fingers.Remove(t.Index);
            }
            else if (ev is InputEventScreenDrag d && fingers.ContainsKey(d.Index))
            {
                idleT = 0f;
                if (fingers.Count == 1) Look(chase, d.Relative);
                else
                {
                    Vector2 other = Vector2.Zero; foreach (var kv in fingers) if (kv.Key != d.Index) { other = kv.Value; break; }
                    float oldDist = (fingers[d.Index] - other).Length(), newDist = (d.Position - other).Length();
                    if (oldDist > 8f && newDist > 8f) Zoom(chase, oldDist / newDist);
                    if (!chase) { float k = frDist * .003f * Settings.CamSens; frPan.Z = Mathf.Clamp(frPan.Z + d.Relative.Y * k, -320f, 320f); frPan.X = Mathf.Clamp(frPan.X + d.Relative.X * k * .5f, -20f, 20f); }
                }
                fingers[d.Index] = d.Position;
            }
            else if (ev is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Right) != 0) Look(chase, m.Relative);
            else if (ev is InputEventMouseButton mb && mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)) Zoom(chase, mb.ButtonIndex == MouseButton.WheelUp ? .9f : 1.1f);
        }

        void Look(bool chase, Vector2 rel)
        {
            float sn = Settings.CamSens;                       // camera sensitivity from Settings
            if (chase) { chYaw -= rel.X * .005f * sn; chPitch = Mathf.Clamp(chPitch + rel.Y * .004f * sn, -.05f, 1.45f); }
            else { frYaw -= rel.X * .005f * sn; frPitch = Mathf.Clamp(frPitch + rel.Y * .004f * sn, -.05f, 1.45f); }
        }

        void Zoom(bool chase, float f)
        {
            f = Mathf.Pow(f, Settings.CamSens);
            if (chase) chDist = Mathf.Clamp(chDist * f, 12f, 140f); else frDist = Mathf.Clamp(frDist * f, 7f, 100f);
        }
    }
}
