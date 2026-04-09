using RAWSimO.Core;
using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar;
using RAWSimO.Core.Control.Defaults.PathPlanning.JunctionArbitration;
using RAWSimO.Core.IO;
using RAWSimO.Core.Randomization;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.GymServer
{
    internal class GymStepResult
    {
        public Dictionary<string, byte[]> Observations { get; }
        public Dictionary<string, double> Rewards { get; }
        public bool SimulationDone { get; }
        public bool Truncated { get; }
        public Dictionary<string, object> Infos { get; }

        public GymStepResult(
            Dictionary<string, byte[]> obs,
            Dictionary<string, double> rewards,
            bool simDone, bool truncated,
            Dictionary<string, object> infos)
        {
            Observations = obs;
            Rewards = rewards;
            SimulationDone = simDone;
            Truncated = truncated;
            Infos = infos;
        }
    }

    internal class GymSimulationHost : IDisposable
    {
        private Instance _instance = null!;
        private List<BotNormal> _bots = null!;
        private Dictionary<string, BotNormal> _botById = null!;
        private AgentAStarPathManager _aStarPM = null!;
        private WaypointCandidateCache _waypointCache = null!;
        private RewardCalculator _rewardCalc = null!;
        private BotCameraRenderer _renderer = null!;

        private readonly string _instPath;
        private readonly string _settPath;
        private readonly string _confPath;
        private readonly double _stepDuration;
        private readonly int _maxSteps;
        private readonly GymCameraConfiguration _camConfig;

        private int _stepCount = 0;
        private bool _baselineMode = false;

        public int BotCount => _bots.Count;
        public GymCameraConfiguration CamConfig => _camConfig;

        public GymSimulationHost(string instPath, string settPath, string confPath,
            double stepDuration = 1.0, int maxSteps = 1000,
            GymCameraConfiguration? camConfig = null)
        {
            _instPath = instPath;
            _settPath = settPath;
            _confPath = confPath;
            _stepDuration = stepDuration;
            _maxSteps = maxSteps;
            _camConfig = camConfig ?? new GymCameraConfiguration();
        }

        public void EnableBaselineMode() => _baselineMode = true;
        public void DisableBaselineMode() => _baselineMode = false;

        /// <summary>Reset simulation. Returns initial observations.</summary>
        public Dictionary<string, byte[]> Reset(int seed = 0)
        {
            _renderer?.Dispose();
            _instance = InstanceIO.ReadInstance(_instPath, _settPath, _confPath);
            _instance.Randomizer = new RandomizerSimple(seed);
            _instance.SettingConfig.StatisticsDirectory = System.IO.Path.GetTempPath();

            _aStarPM = _instance.Controller.PathManager as AgentAStarPathManager
                ?? throw new InvalidOperationException(
                    "Control config must use AgentAStar path planner for GymServer.");

            _bots = _instance.Bots.Cast<BotNormal>().ToList();
            _botById = _bots.ToDictionary(b => "bot_" + b.GetInfoID());

            _waypointCache = new WaypointCandidateCache(_aStarPM);
            _rewardCalc = new RewardCalculator(_instance, _bots);
            _renderer = new BotCameraRenderer(_instance, _camConfig);
            _stepCount = 0;

            // Warmup
            if (_instance.SettingConfig.SimulationWarmupTime > 0)
                _instance.Controller.Update(_instance.SettingConfig.SimulationWarmupTime);
            _instance.StatReset();

            // Initial waypoint candidates
            foreach (var bot in _bots)
                _waypointCache.GetCandidates(bot);

            // Initial observations
            return _bots.ToDictionary(
                b => "bot_" + b.GetInfoID(),
                b => _renderer.RenderBot(b));
        }

        public GymStepResult Step(Dictionary<string, int[]> actions)
        {
            _stepCount++;

            // 1. Apply actions
            if (!_baselineMode)
            {
                foreach (var (botId, action) in actions)
                {
                    if (_botById.TryGetValue(botId, out var bot))
                        ApplyAction(bot, action);
                }
            }

            // 2. Advance simulation
            _instance.Controller.Update(_stepDuration);

            // 3. Detect collisions (bots sharing same waypoint)
            var collisionMap = ComputeCollisions();

            // 4. Render observations
            var observations = _bots.ToDictionary(
                b => "bot_" + b.GetInfoID(),
                b => _renderer.RenderBot(b));

            // 5. Compute rewards
            var rewards = _bots.ToDictionary(
                b => "bot_" + b.GetInfoID(),
                b => _rewardCalc.ComputeReward(b, _stepDuration,
                    collisionMap.TryGetValue(b, out var c) ? c : 0));

            // 6. Refresh candidates for next step
            foreach (var bot in _bots)
                _waypointCache.GetCandidates(bot);

            bool simDone = _instance.Controller.CurrentTime >=
                           _instance.SettingConfig.SimulationWarmupTime +
                           _instance.SettingConfig.SimulationDuration;
            bool truncated = _stepCount >= _maxSteps;

            var junctionPM = _instance.Controller.PathManager as JunctionArbitrationPathManager;
            var infos = _bots.ToDictionary(
                b => "bot_" + b.GetInfoID(),
                b =>
                {
                    var d = new Dictionary<string, object>
                    {
                        ["action_mask"] = GetActionMask(b),
                        ["collision_count"] = collisionMap.TryGetValue(b, out var c) ? c : 0
                    };
                    if (junctionPM != null &&
                        junctionPM.TryGetArbitrationStop(b, out bool stopped, out double dur, out int blockedAt))
                    {
                        d["arbitration_stopped"] = stopped;
                        d["arbitration_stop_duration"] = dur;
                        d["arbitration_blocked_at_wp"] = blockedAt;
                    }
                    return (object)d;
                });
            if (junctionPM != null)
            {
                infos["__global__"] = new Dictionary<string, object>
                {
                    ["wait_for_graph_cycle_count"] = junctionPM.LastCycleCount,
                    ["total_arbitration_stops"] = junctionPM.TotalArbitrationStops
                };
            }

            return new GymStepResult(observations, rewards, simDone, truncated, infos);
        }

        private void ApplyAction(BotNormal bot, int[] action)
        {
            int directionIdx = action[0];
            int accelMode    = action.Length > 1 ? action[1] : 1;
            int decelMode    = action.Length > 2 ? action[2] : 1;

            // Set destination: 0-3 = cardinal direction, 4 = STAY
            if (directionIdx == CardinalDirection.Stay || directionIdx >= CardinalDirection.Count)
            {
                bot.DestinationWaypoint = bot.CurrentWaypoint;
            }
            else
            {
                var dirs = _waypointCache.GetCached(bot);
                bot.DestinationWaypoint = dirs[directionIdx] ?? bot.CurrentWaypoint;
            }

            // Set physics multipliers
            bot.AccelerationMultiplier = SpeedMode.AccelFactors[accelMode];
            bot.DecelerationMultiplier = SpeedMode.DecelFactors[decelMode];
        }

        private bool[] GetActionMask(BotNormal bot)
        {
            var dirs = _waypointCache.GetCached(bot);
            return new bool[]
            {
                dirs[CardinalDirection.North] != null,
                dirs[CardinalDirection.South] != null,
                dirs[CardinalDirection.East]  != null,
                dirs[CardinalDirection.West]  != null,
                true  // STAY always valid
            };
        }

        private Dictionary<BotNormal, int> ComputeCollisions()
        {
            var result = new Dictionary<BotNormal, int>();
            var waypointGroups = _bots.GroupBy(b => b.CurrentWaypoint).Where(g => g.Count() > 1);
            foreach (var group in waypointGroups)
                foreach (var bot in group)
                    result[bot] = group.Count() - 1;
            return result;
        }

        public void Dispose()
        {
            _renderer?.Dispose();
        }
    }
}
