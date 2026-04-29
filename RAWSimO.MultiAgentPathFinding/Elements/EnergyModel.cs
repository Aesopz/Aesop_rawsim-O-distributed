using System;

namespace RAWSimO.MultiAgentPathFinding.Elements
{
    /// <summary>
    /// Lightweight energy model for use inside ESpaceTimeAStar.
    /// Mirrors EnergyConsumption (Core) but lives in MultiAgentPathFinding to avoid cross-project dependency.
    /// </summary>
    public static class EnergyModel
    {
        public static double GRAVITY = 9.8;
        public static double FRICTION = 0.02;
        public static double INERTIA = 0.15;
        public static double ROBOT_MASS = 300.0;
        public static double ROBOT_WIDTH = 0.6;
        public static double ROBOT_LENGTH = 0.75;
        public static double ROBOT_RADIUS = 0.3;

        /// <summary>
        /// Fixed support power draw [W] — background electronics drain while a task is active.
        /// E_wait = P_SUPPORT × waitDuration [J] (congestion/CBS hold).
        /// Independent of mass (control system / motor standby draw).
        /// </summary>
        public static double P_SUPPORT = 90;

        /// <summary>
        /// Synchronizes physical constants with EnergyConsumption (Core).
        /// Must be called after EnergyConsumption.Configure() so that
        /// low-level A* search uses the same friction/inertia/dimensions as BotNormal accounting.
        /// </summary>
        public static void Configure(
            double robotMass, double robotWidth, double robotLength,
            double rollingFriction, double inertiaCoeff)
        {
            ROBOT_MASS   = robotMass;
            ROBOT_WIDTH  = robotWidth;
            ROBOT_LENGTH = robotLength;
            ROBOT_RADIUS = robotWidth / 2.0;
            FRICTION     = rollingFriction;
            INERTIA      = inertiaCoeff;
        }

        public static double ComputeWaitEnergy(double mTotal, double waitDuration)
            => P_SUPPORT * waitDuration;

        /// <summary>
        /// Real transition cost (E1+E2+E3) — mirrors EnergyConsumption.ComputeSegmentEnergy.
        /// Full accel-cruise-decel analytical integration.
        /// </summary>
        public static double ComputeMoveEnergy(double mTotal, double accel, double decel, double vMax, double distance)
        {
            if (distance <= 0.0 || mTotal <= 0.0 || accel <= 0.0 || decel <= 0.0 || vMax <= 0.0)
                return 0.0;

            double dAccel = (vMax * vMax) / (2.0 * accel);
            double dDecel = (vMax * vMax) / (2.0 * decel);

            double vPeak, dCruise;
            if (dAccel + dDecel <= distance)
            {
                vPeak = vMax;
                dCruise = distance - dAccel - dDecel;
            }
            else
            {
                vPeak = Math.Sqrt(2.0 * accel * decel * distance / (accel + decel));
                dCruise = 0.0;
            }

            double e1 = 0.0, e2 = 0.0, e3 = 0.0;

            if (vPeak > 0.0)
                e1 = mTotal * (GRAVITY * FRICTION + accel * INERTIA) * (vPeak * vPeak) / (2.0 * accel);

            if (vPeak > 0.0)
            {
                double netCoeff = decel * INERTIA - GRAVITY * FRICTION;
                if (netCoeff > 0.0)
                    e2 = mTotal * netCoeff * (vPeak * vPeak) / (2.0 * decel);
            }

            if (dCruise > 0.0)
                e3 = mTotal * GRAVITY * FRICTION * dCruise;

            return e1 + e2 + e3;
        }

        /// <summary>
        /// Heuristic-only lower bound: conservative per-meter cruise energy (no turn/stop-go).
        /// Admissible for A* because real cost >= this.
        /// </summary>
        public static double ComputeMoveEnergyLowerBound(double mTotal, double distance)
        {
            return mTotal * GRAVITY * FRICTION * distance;
        }

        /// <summary>
        /// Turn energy (E4) — mirrors EnergyConsumption.E4_Rotation.
        /// Uses mTotal for moment of inertia (payload shifts rotational mass).
        /// </summary>
        public static double ComputeTurnEnergy(double mTotal, double thetaRad, double turnSpeed)
        {
            if (thetaRad <= 0.0 || turnSpeed <= 0.0)
                return 0.0;

            double omega = 2.0 * Math.PI / turnSpeed;
            double momentOfInertia = (1.0 / 12.0) * mTotal
                * (ROBOT_LENGTH * ROBOT_LENGTH + ROBOT_WIDTH * ROBOT_WIDTH);
            double eKinetic = 0.5 * momentOfInertia * omega * omega;
            double eFriction = mTotal * GRAVITY * FRICTION * ROBOT_RADIUS * thetaRad;

            return eKinetic + eFriction;
        }
    }
}
