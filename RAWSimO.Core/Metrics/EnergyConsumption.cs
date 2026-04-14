using System;

namespace RAWSimO.Core.Metrics
{
    /// <summary>
    /// Rizqi et al. AGV energy consumption model.
    /// Physical constants are configurable via Configure() — call once at instance load.
    /// Bot-specific kinematics (a, d, vMax, TurnSpeed, PodTransferTime) are passed per-call
    /// and read live from Physics / Bot properties, so they automatically reflect the xinst values.
    /// </summary>
    public static class EnergyConsumption
    {
        #region Configurable Physical Parameters (defaults match Rizqi et al.)

        /// <summary>AGV empty chassis mass [kg]. From xinst EnergyParameters.RobotMass.</summary>
        public static double ROBOT_MASS = 300.0;

        /// <summary>Gravitational acceleration [m/s²].</summary>
        public static double GRAVITY = 9.8;

        /// <summary>Rolling friction coefficient (straight-line movement).</summary>
        public static double FRICTION = 0.01;

        /// <summary>Drivetrain inertia equivalent coefficient (rotational → translational).</summary>
        public static double INERTIA = 0.15;

        /// <summary>AGV body width [m]. From xinst EnergyParameters.RobotWidth.</summary>
        public static double ROBOT_WIDTH = 0.6;

        /// <summary>AGV body length [m]. From xinst EnergyParameters.RobotLength.</summary>
        public static double ROBOT_LENGTH = 0.75;

        /// <summary>AGV turning radius (= width/2) [m]. Auto-computed from ROBOT_WIDTH.</summary>
        public static double ROBOT_RADIUS = 0.3;

        /// <summary>Pod lift/lower height [m]. From xinst EnergyParameters.LiftHeight.</summary>
        public static double LIFT_HEIGHT = 0.2;

        /// <summary>
        /// Pod shelf/frame structural mass [kg] (excludes cargo).
        /// Added to mLoad for E5/E6 even when pod carries no items.
        /// From xinst EnergyParameters.PodFrameMass.
        /// </summary>
        public static double POD_FRAME_MASS = 40.0;


        #endregion

        #region Configuration

        /// <summary>
        /// Applies energy parameters loaded from the xinst EnergyParameters block.
        /// Call once after DTOInstance.Submit() completes.
        /// Bot-specific kinematics (a, d, vMax, TurnSpeed, PodTransferTime) are NOT set here —
        /// they are already read per-call from Physics and Bot properties.
        /// </summary>
        public static void Configure(
            double robotMass,
            double robotWidth,
            double robotLength,
            double rollingFriction,
            double inertiaCoeff,
            double liftHeight,
            double podFrameMass)
        {
            ROBOT_MASS          = robotMass;
            ROBOT_WIDTH         = robotWidth;
            ROBOT_LENGTH        = robotLength;
            ROBOT_RADIUS        = robotWidth / 2.0;
            FRICTION            = rollingFriction;
            INERTIA             = inertiaCoeff;
            LIFT_HEIGHT         = liftHeight;
            POD_FRAME_MASS      = podFrameMass;
        }

        #endregion

        #region E1 + E2 + E3 — Segment Analytical Integration

        /// <summary>
        /// Computes drive energy for one waypoint-to-waypoint segment using closed-form
        /// integration over the accel–cruise–decel velocity profile (v=0 at both ends).
        /// Kinematics parameters (accel, decel, vMax) are read from Physics per-call,
        /// so they automatically use whatever values are in the xinst Bot definitions.
        /// </summary>
        /// <param name="mTotal">Dynamic total mass = ROBOT_MASS + pod load [kg].</param>
        /// <param name="accel">Effective acceleration [m/s²] from Physics.Acceleration.</param>
        /// <param name="decel">Effective deceleration [m/s²] from Physics.Deceleration.</param>
        /// <param name="vMax">Maximum speed [m/s] from Physics.MaxSpeed.</param>
        /// <param name="distance">Waypoint-to-waypoint Euclidean distance [m].</param>
        /// <param name="e1J">Out: acceleration phase energy [J].</param>
        /// <param name="e2J">Out: deceleration phase energy [J].</param>
        /// <param name="e3J">Out: cruise phase energy [J].</param>
        /// <returns>e1J + e2J + e3J [J].</returns>
        public static double ComputeSegmentEnergy(
            double mTotal, double accel, double decel, double vMax, double distance,
            out double e1J, out double e2J, out double e3J)
        {
            e1J = e2J = e3J = 0.0;

            if (distance <= 0.0 || mTotal <= 0.0 || accel <= 0.0 || decel <= 0.0 || vMax <= 0.0)
                return 0.0;

            // --- Kinematic phase decomposition (v=0 → vPeak → v=0) ---
            double dAccel = (vMax * vMax) / (2.0 * accel);
            double dDecel = (vMax * vMax) / (2.0 * decel);

            double vPeak, dCruise;
            if (dAccel + dDecel <= distance)
            {
                // Three-phase: accel → cruise → decel
                vPeak  = vMax;
                dCruise = distance - dAccel - dDecel;
            }
            else
            {
                // Two-phase: accel → decel only (short segment, vPeak < vMax)
                vPeak  = Math.Sqrt(2.0 * accel * decel * distance / (accel + decel));
                dCruise = 0.0;
            }

            // E1 — Acceleration energy
            // ∫₀^{t_a} m(g·μ_r + a·η)·(a·t) dt = m(g·μ_r + a·η)·vPeak²/(2a)
            if (vPeak > 0.0)
                e1J = mTotal * (GRAVITY * FRICTION + accel * INERTIA) * (vPeak * vPeak) / (2.0 * accel);

            // E2 — Deceleration energy
            // Only positive when d·η > g·μ_r (braking inertia exceeds friction assist)
            if (vPeak > 0.0)
            {
                double netCoeff = decel * INERTIA - GRAVITY * FRICTION;
                if (netCoeff > 0.0)
                    e2J = mTotal * netCoeff * (vPeak * vPeak) / (2.0 * decel);
            }

            // E3 — Cruise energy = friction force × cruise distance
            // m·g·μ_r·d_cruise
            if (dCruise > 0.0)
                e3J = mTotal * GRAVITY * FRICTION * dCruise;

            return e1J + e2J + e3J;
        }

        #endregion

        #region E4 — Rotation Energy

        /// <summary>
        /// Computes in-place rotation energy (Rizqi E4). Fixed from original:
        ///   - Moment of inertia: (1/12)·m·(L²+W²)  [was incorrectly 1/6]
        ///   - Kinetic energy:    (1/2)·I·ω²         [was incorrectly I·ω²]
        /// TurnSpeed is passed per-call from bot.TurnSpeed (xinst value).
        /// </summary>
        /// <param name="thetaRad">Actual rotation angle [radians].</param>
        /// <param name="turnSpeed">Seconds for a full 360° rotation [s] (from xinst Bot.TurnSpeed).</param>
        /// <returns>Rotation energy [J].</returns>
        /// <param name="mTotal">Total dynamic mass [kg] = ROBOT_MASS + pod load.
        /// Use GetTotalMass(Pod) so loaded robots correctly reflect increased rotational inertia.</param>
        public static double E4_Rotation(double thetaRad, double turnSpeed, double mTotal)
        {
            if (thetaRad <= 0.0 || turnSpeed <= 0.0 || mTotal <= 0.0)
                return 0.0;

            // ω = 2π / T  where T = turnSpeed [s/rev]
            double omega = 2.0 * Math.PI / turnSpeed;

            // Moment of inertia for uniform rectangular body about vertical centroid axis
            // I = (1/12)·mTotal·(L² + W²)
            // mTotal includes pod mass when loaded — pod sits on robot, shifts rotational inertia.
            double momentOfInertia = (1.0 / 12.0) * mTotal
                * (ROBOT_LENGTH * ROBOT_LENGTH + ROBOT_WIDTH * ROBOT_WIDTH);

            // Kinetic energy to spin up: (1/2)·I·ω²
            double eKinetic = 0.5 * momentOfInertia * omega * omega;

            // Friction energy while rotating: mTotal·g·μ_r·r·θ
            double eFriction = mTotal * GRAVITY * FRICTION * ROBOT_RADIUS * thetaRad;

            return eKinetic + eFriction;
        }

        #endregion

        #region E5 — Pod Lift / Lower Energy

        /// <summary>
        /// E5a — Energy to lift a pod (Phase pickup).
        /// mLoad = POD_FRAME_MASS + cargo. PodTransferTime read from bot.PodTransferTime (xinst).
        /// </summary>
        public static double E5a_LiftPod(double mLoad, double podTransferTime)
        {
            if (mLoad <= 0.0 || podTransferTime <= 0.0)
                return 0.0;

            // E_LU = m_L · (g + 2h/t²) · h
            return mLoad * (GRAVITY + 2.0 * LIFT_HEIGHT / (podTransferTime * podTransferTime)) * LIFT_HEIGHT;
        }

        /// <summary>
        /// E5b — Energy to lower a pod (Phase setdown).
        /// mLoad = POD_FRAME_MASS + cargo. PodTransferTime read from bot.PodTransferTime (xinst).
        /// </summary>
        public static double E5b_LowerPod(double mLoad, double podTransferTime)
        {
            if (mLoad <= 0.0 || podTransferTime <= 0.0)
                return 0.0;

            // E_LD = m_L · (g − 2h/t²) · h  (gravity assists lowering; clamp to ≥ 0)
            double result = mLoad * (GRAVITY - 2.0 * LIFT_HEIGHT / (podTransferTime * podTransferTime)) * LIFT_HEIGHT;
            return Math.Max(0.0, result);
        }

        #endregion


        #region Mass Helpers

        /// <summary>
        /// Total dynamic mass for drive energy (E1/E2/E3):
        /// ROBOT_MASS + POD_FRAME_MASS + cargo load.
        /// </summary>
        public static double GetTotalMass(Elements.Pod pod)
        {
            if (pod == null)
                return ROBOT_MASS;
            return ROBOT_MASS + POD_FRAME_MASS + pod.CapacityInUse;
        }

        /// <summary>
        /// Pod effective load mass for lift/station energy (E5/E6):
        /// POD_FRAME_MASS + cargo load (0 when no pod carried).
        /// </summary>
        public static double GetLoadMass(Elements.Pod pod)
        {
            if (pod == null)
                return 0.0;
            return POD_FRAME_MASS + pod.CapacityInUse;
        }

        #endregion
    }
}
