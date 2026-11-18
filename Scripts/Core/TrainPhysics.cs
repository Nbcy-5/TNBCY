using System;
namespace TNBCY
{
    // Pure C# physics, no Unity types. Same ingredients Open Rails uses: tractive effort curve,
    // adhesion limit, Davis resistance, gradient and curve resistance, air brake with build-up delay.
    public class TrainPhysics
    {
        public const float G = 9.81f, ServiceBrakeMax = 0.6f, RotationalMass = 1.06f, Adhesion = 0.27f;
        public readonly ConsistSpec Consist;
        public float Throttle, BrakeHandle;       // 0..1 driver inputs
        public bool Emergency;
        public float BrakeLevel, Speed, Position; // Position = chainage of the front (m), Speed in m/s
        public float MassT, LengthM, PowerW, MaxTractiveN, BrakeN, MaxSpeed;
        public float LastTractive, LastResistance, LastBrake, Accel;
        float sumA, sumB, sumC;

        public TrainPhysics(ConsistSpec c)
        {
            Consist = c; MaxSpeed = float.MaxValue; float poweredMass = 0, te = 0;
            foreach (var v in c.vehicles)
            {
                int n = Math.Max(1, v.count);
                MassT += v.massTonnes * n; LengthM += v.lengthM * n; BrakeN += v.brakeForceKn * 1000f * n;
                sumA += v.davisA * v.massTonnes * n; sumB += v.davisB * v.massTonnes * n; sumC += v.davisC * n;
                if (v.powered) { PowerW += v.maxPowerKw * 1000f * n; te += v.maxTractiveEffortKn * 1000f * n; poweredMass += v.massTonnes * n; }
                MaxSpeed = Math.Min(MaxSpeed, v.maxSpeedKmh / 3.6f);
            }
            MaxTractiveN = Math.Min(te, Adhesion * poweredMass * 1000f * G);
        }

        static float MoveToward(float a, float b, float d) => a < b ? Math.Min(a + d, b) : Math.Max(a - d, b);

        public void Step(float dt, float gradient, float curveRadius)
        {
            float target = Emergency ? 1f : BrakeHandle * ServiceBrakeMax;
            float rate = Emergency ? Consist.brakeApplyRate * 2.5f : (target > BrakeLevel ? Consist.brakeApplyRate : Consist.brakeReleaseRate);
            BrakeLevel = MoveToward(BrakeLevel, target, rate * dt);

            float th = BrakeLevel > 0.03f ? 0f : Math.Max(0f, Math.Min(1f, Throttle));   // traction cut-out under braking
            float te = Speed < 1f ? MaxTractiveN : Math.Min(MaxTractiveN, PowerW / Speed);
            if (Speed >= MaxSpeed) te = 0f;
            float traction = th * te;
            float resist = sumA + sumB * Speed + sumC * Speed * Speed;
            float grade = MassT * 1000f * G * gradient;
            float curve = curveRadius != 0f ? MassT * 700f / Math.Abs(curveRadius) : 0f;
            float brake = BrakeLevel * BrakeN;
            float massKg = MassT * 1000f * RotationalMass;

            float e = traction - resist - grade - curve;       // effort without brakes
            float a = Speed <= 0.001f ? (e > brake ? (e - brake) / massKg : 0f) : (e - brake) / massKg;
            float nv = Math.Max(0f, Speed + a * dt);
            Position += (Speed + nv) * 0.5f * dt; Speed = nv; Accel = a;
            LastTractive = traction; LastResistance = resist + grade + curve; LastBrake = brake;
        }

        // Approximate distance needed to slow to targetSpeed (m/s) with a service brake application.
        public float BrakingDistance(float targetSpeed)
        {
            if (Speed <= targetSpeed) return 0f;
            float decel = BrakeN * ServiceBrakeMax / (MassT * 1000f * RotationalMass) + 0.1f;
            return (Speed * Speed - targetSpeed * targetSpeed) / (2f * decel);
        }
    }
}
