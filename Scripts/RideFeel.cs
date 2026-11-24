using Godot;

namespace TNBCY
{
    // Procedural ride feel for the camera: wheel rumble, rail-joint clacks, jolts over points,
    // roll on curves and a dip when braking. All values are small offsets applied to the camera.
    public class RideFeel
    {
        public Vector3 Pos;      // metres in the camera's own axes (x right, y up)
        public Vector3 Rot;      // radians: x pitch, z roll
        public float Kick;       // size of a jolt that happened this frame (0 = none); used for the phone vibration

        public const float JointSpacing = 25f;     // a rail weld / joint every 25 m
        float jolt, joltT, nextJoint, rollS, pitchS, prevHead, prevSpeed, ph1, ph2, ph3;
        bool primed;
        readonly RandomNumberGenerator rng = new RandomNumberGenerator();

        public void Reset() { primed = false; jolt = 0f; rollS = 0f; pitchS = 0f; Pos = Vector3.Zero; Rot = Vector3.Zero; Kick = 0f; }

        public void Impulse(float size) { jolt = Mathf.Max(jolt, size); joltT = 0f; Kick = Mathf.Max(Kick, size); }

        // k = Settings.ShakeK (0 = off). heading = direction the train faces, in radians.
        public void Update(float dt, float speed, float odo, float heading, float k)
        {
            Kick = 0f;
            if (!primed) { prevHead = heading; prevSpeed = speed; nextJoint = odo + JointSpacing; primed = true; }
            float dh = heading - prevHead;
            while (dh > Mathf.Pi) dh -= Mathf.Tau;
            while (dh < -Mathf.Pi) dh += Mathf.Tau;
            float rate = dh / Mathf.Max(dt, .001f), accel = (speed - prevSpeed) / Mathf.Max(dt, .001f);
            prevHead = heading; prevSpeed = speed;

            int guard = 0;
            while (odo >= nextJoint && guard++ < 6) { nextJoint += JointSpacing; if (speed > 2f) Impulse(.5f + .3f * rng.Randf()); }
            if (nextJoint < odo - 2f * JointSpacing) nextJoint = odo + JointSpacing;

            float v = Mathf.Clamp(speed / 28f, 0f, 1f), mv = Mathf.Clamp(speed / 2f, 0f, 1f);
            ph1 += dt * (5f + speed * .65f); ph2 += dt * (9f + speed * 1.1f); ph3 += dt * (2.2f + speed * .22f);
            jolt *= Mathf.Exp(-8f * dt); joltT += dt;
            float s = k * (.25f + .75f * v) * mv;

            float vert = Mathf.Sin(ph1) * .0022f + Mathf.Sin(ph2 * 1.31f) * .0016f + jolt * Mathf.Sin(joltT * 36f) * .012f;
            float lat = Mathf.Sin(ph3 * 1.7f + 1f) * .0030f + Mathf.Sin(ph1 * .63f) * .0012f + jolt * Mathf.Sin(joltT * 30f) * .004f;
            Pos = new Vector3(lat * s, vert * s, 0f);

            rollS = Mathf.Lerp(rollS, Mathf.Clamp(-speed * rate * .012f, -.06f, .06f), 1f - Mathf.Exp(-4f * dt));
            pitchS = Mathf.Lerp(pitchS, Mathf.Clamp(accel * .012f, -.035f, .035f), 1f - Mathf.Exp(-3f * dt));
            float kk = Mathf.Min(k, 1.6f);
            Rot = new Vector3(pitchS * kk + jolt * Mathf.Sin(joltT * 28f) * .0035f * s, 0f, rollS * kk + jolt * Mathf.Sin(joltT * 26f + 1f) * .006f * s);
        }
    }
}
