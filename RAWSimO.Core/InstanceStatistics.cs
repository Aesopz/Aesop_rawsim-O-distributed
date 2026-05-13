using RAWSimO.Core.Bots;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.IO;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Statistics;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RAWSimO.Core.Metrics;
using static RAWSimO.Core.Statistics.StationTripDatapoint;

namespace RAWSimO.Core
{
    /// THIS PARTIAL CLASS CONTAINS ALL CORE ELEMENTS OF THE PERFORMANCE INDICATORS
    /// <summary>
    /// The core element of each simulation instance.
    /// </summary>
    public partial class Instance
    {
        #region Data fields

        /// <summary>
        /// Flag used by OrderManager to trigger a one-time distance table read.
        /// Set to true externally; reset to false after ReadDistance() completes.
        /// </summary>
        public bool ifreaddistance;

        /// <summary>
        /// Number of datapoints stored before a flush is done.
        /// </summary>
        public const int STAT_MAX_DATA_POINTS = 10000;

        /// <summary>
        /// The time the recording of the statistics started.
        /// </summary>
        internal double StatTimeStart = 0.0;
        /// <summary>
        /// The current time regarding all statistical measurements.
        /// </summary>
        internal double StatTime { get { return Controller.CurrentTime - StatTimeStart; } }

        /// <summary>
        /// Indicates whether the stat reset after the warmup period was done.
        /// </summary>
        public bool StatWarmupResetDone { get; private set; }
        /// <summary>
        /// Indicates whether the stats have been written.
        /// </summary>
        public bool StatResultsWritten { get; private set; }

        /// <summary>
        /// The number of times bundle generation was paused.
        /// </summary>
        public int StatBundleGenerationStops { get; private set; }
        /// <summary>
        /// The number of times order generation was paused.
        /// </summary>
        public int StatOrderGenerationStops { get; private set; }
        /// <summary>
        /// The number of handled items overall.
        /// </summary>
        public int StatOverallItemsHandled { get; private set; }
        /// <summary>
        /// The number of handled bundles overall.
        /// </summary>
        public int StatOverallBundlesHandled { get; private set; }
        /// <summary>
        /// The number of handled order lines overall.
        /// </summary>
        public int StatOverallLinesHandled { get; private set; }
        /// <summary>
        /// The number of handled orders overall.
        /// </summary>
        public int StatOverallOrdersHandled { get; private set; }
        /// <summary>
        /// The number of handled orders overall that were not completed in time.
        /// </summary>
        public int StatOverallOrdersLate { get; private set; }
        /// <summary>
        /// The absolute number of items ordered.
        /// </summary>
        public int StatOverallItemsOrdered { get; private set; }
        /// <summary>
        /// The absolute number of bundles placed.
        /// </summary>
        public int StatOverallBundlesPlaced { get; private set; }
        /// <summary>
        /// The absolute number of orders placed.
        /// </summary>
        public int StatOverallOrdersPlaced { get; private set; }
        /// <summary>
        /// The absolute number of bundles placed.
        /// </summary>
        public int StatOverallBundlesRejected { get; private set; }
        /// <summary>
        /// The absolute number of orders placed.
        /// </summary>
        public int StatOverallOrdersRejected { get; private set; }
        /// <summary>
        /// The total number of repositioning moves that were executed.
        /// </summary>
        public int StatRepositioningMoves { get; private set; }
        /// <summary>
        /// The total distance traveled by the bots so far.
        /// </summary>
        public double StatOverallDistanceTraveled { get { return Bots.Sum(b => b.StatDistanceTraveled); } }
        /// <summary>Total energy consumed by all bots [J] (Rizqi model).</summary>
        public double StatOverallEnergyTotalJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyTotalJ); } }
        /// <summary>Fleet acceleration energy E1 [J].</summary>
        public double StatOverallEnergyE1J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE1AccelJ); } }
        /// <summary>Fleet deceleration energy E2 [J].</summary>
        public double StatOverallEnergyE2J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE2DecelJ); } }
        /// <summary>Fleet cruise energy E3 [J].</summary>
        public double StatOverallEnergyE3J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE3CruiseJ); } }
        /// <summary>Fleet rotation energy E4 [J].</summary>
        public double StatOverallEnergyE4J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE4RotationJ); } }
        /// <summary>Fleet pod lift/lower energy E5 [J].</summary>
        public double StatOverallEnergyE5J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE5LiftLowerJ); } }
        /// <summary>Fleet support energy [J] = P_SUPPORT × Σ task-active time (standby/rest excluded).</summary>
        public double StatOverallEnergySupportJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatESupportJ); } }
        /// <summary>Fleet total energy including support [J] = E_mech + P_SUPPORT×t_active.</summary>
        public double StatOverallEnergyTotalWithSupportJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyTotalWithSupportJ); } }
        /// <summary>Total turning events across all bots.</summary>
        public int StatOverallTurningCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurningCount); } }
        /// <summary>Total orders completed across all bots.</summary>
        public int StatOverallOrdersCompletedByBots { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatOrdersCompleted); } }
        /// <summary>Total distance traveled by bots (Rizqi tracker) [m].</summary>
        public double StatOverallDistanceTraveledRizqi { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatDistanceTraveledM); } }
        /// <summary>Total distance traveled while carrying a pod [m].</summary>
        public double StatOverallLoadedDistanceM { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatLoadedDistanceM); } }
        /// <summary>Total turning events while carrying a pod.</summary>
        public int StatOverallLoadedTurningCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatLoadedTurningCount); } }
        /// <summary>Total turning events while empty.</summary>
        public int StatOverallEmptyTurningCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEmptyTurningCount); } }
        /// <summary>Total wait time across all bots (stationary, not rotating) [s].</summary>
        public double StatOverallWaitTimeSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitTimeSec); } }
        /// <summary>Fleet E_support (background P_SUPPORT × active-task time; includes moving) [J].</summary>
        public double StatOverallESupportJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatESupportJ); } }
        /// <summary>Fleet E_wait (P_SUPPORT × congestion-wait subset; strict subset of E_support) [J].</summary>
        public double StatOverallEWaitJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEWaitJ); } }
        /// <summary>Fleet E_wait while loaded [J].</summary>
        public double StatOverallEWaitLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEWaitLoadedJ); } }
        /// <summary>Fleet E_wait while empty [J].</summary>
        public double StatOverallEWaitEmptyJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEWaitEmptyJ); } }
        /// <summary>Fleet idle time (no task assigned) [s].</summary>
        public double StatOverallTimeIdleSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTimeIdleSec); } }
        // ── Pref calibration: event-level 8-accumulator aggregation ───────────────
        public double StatOverallMoveEnergyEmptyJ  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveEnergyEmptyJ); } }
        public double StatOverallMoveTimeEmptySec  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveTimeEmptySec); } }
        public double StatOverallTurnEnergyEmptyJ  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnEnergyEmptyJ); } }
        public double StatOverallTurnTimeEmptySec  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnTimeEmptySec); } }
        public double StatOverallMoveEnergyLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveEnergyLoadedJ); } }
        public double StatOverallMoveTimeLoadedSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveTimeLoadedSec); } }
        public double StatOverallTurnEnergyLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnEnergyLoadedJ); } }
        public double StatOverallTurnTimeLoadedSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnTimeLoadedSec); } }
        /// <summary>Total number of completed loaded trips (pickup → setdown) across all bots.</summary>
        public int StatOverallTripCountLoaded { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTripCountLoaded); } }
        /// <summary>Total number of completed empty trips (setdown → next pickup) across all bots.</summary>
        public int StatOverallTripCountEmpty { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTripCountEmpty); } }
        /// <summary>Fleet distance traveled while empty [m].</summary>
        public double StatOverallEmptyDistanceM { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEmptyDistanceM); } }
        /// <summary>System throughput [orders/hour] = orders / simulation hours.</summary>
        public double StatThroughputOrdersPerHour => StatTime > 0 ? StatOverallOrdersHandled / (StatTime / 3600.0) : 0.0;
        /// <summary>Average distance per completed order [m/order] = total_distance_m / orders (consistent with CSV kpi_report).</summary>
        public double StatOrderDistanceM => StatOverallOrdersHandled > 0 ? StatOverallDistanceTraveled / StatOverallOrdersHandled : 0.0;
        /// <summary>System-wide output-side order pile-on [orders/output_station_arrival].</summary>
        public double StatSystemOrderPileOn { get { int arrivals = StatOverallOutputStationArrivals; return arrivals > 0 ? (double)StatOverallOrdersHandled / arrivals : 0.0; } }
        /// <summary>Fleet wait time while loaded [s].</summary>
        public double StatOverallWaitTimeLoadedSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitTimeLoadedSec); } }
        /// <summary>Fleet wait time while empty [s].</summary>
        public double StatOverallWaitTimeEmptySec  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitTimeEmptySec); } }
        /// <summary>Fleet wait energy while loaded [J] = P_SUPPORT × congestion-wait time.</summary>
        public double StatOverallWaitEnergyLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitEnergyLoadedJ); } }
        /// <summary>Fleet wait energy while empty [J].</summary>
        public double StatOverallWaitEnergyEmptyJ  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitEnergyEmptyJ); } }
        /// <summary>Fleet input-station processing arrivals.</summary>
        public int StatOverallInputStationArrivals { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatInputStationArrivals); } }
        /// <summary>Fleet output-station processing arrivals.</summary>
        public int StatOverallOutputStationArrivals { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatOutputStationArrivals); } }
        /// <summary>Fleet station processing arrivals.</summary>
        public int StatOverallStationArrivals { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatStationArrivals); } }
        /// <summary>
        /// The estimated distance by the bots.
        /// </summary>
        public double StatOverallDistanceEstimated { get { return Bots.Sum(b => b.StatDistanceEstimated); } }
        /// <summary>
        /// The total number of assigned tasks so far.
        /// </summary>
        public int StatOverallAssignedTasks { get { return Bots.Sum(b => b.StatAssignedTasks); } }
        /// <summary>
        /// The maximum Memory Usage in megabyte.
        /// </summary>
        public double StatMaxMemoryUsed { get; set; }
        /// <summary>
        /// The turnover times for all completed orders.
        /// </summary>
        internal List<double> _statOrderTurnoverTimes = new List<double>();
        /// <summary>
        /// The throughput times for all completed orders.
        /// </summary>
        internal List<double> _statOrderThroughputTimes = new List<double>();
        /// <summary>
        /// The lateness for all completed orders.
        /// </summary>
        internal List<double> _statOrderLatenessTimes = new List<double>();
        /// <summary>
        /// The turnover times for all completed bundles.
        /// </summary>
        internal List<double> _statBundleTurnoverTimes = new List<double>();
        /// <summary>
        /// The throughput times for all completed bundles.
        /// </summary>
        internal List<double> _statBundleThroughputTimes = new List<double>();
        /// <summary>
        /// The average order throughput time.
        /// </summary>
        public double StatOrderThroughputTimeAvg { get { return _statOrderThroughputTimes.Any() ? _statOrderThroughputTimes.Average(t => t) : 0; } }
        /// <summary>
        /// Stores all times at which an item was handled.
        /// </summary>
        internal List<ItemHandledDatapoint> _statItemHandlingTimestamps = new List<ItemHandledDatapoint>();
        /// <summary>
        /// Stores all times at which an bundle was handled.
        /// </summary>
        internal List<BundleHandledDatapoint> _statBundleHandlingTimestamps = new List<BundleHandledDatapoint>();
        /// <summary>
        /// Stores all times at which an order was handled.
        /// </summary>
        internal List<OrderHandledDatapoint> _statOrderHandlingTimestamps = new List<OrderHandledDatapoint>();
        /// <summary>
        /// Stores all times at which an incoming bundle was placed.
        /// </summary>
        internal List<BundlePlacedDatapoint> _statBundlePlacementTimestamps = new List<BundlePlacedDatapoint>();
        /// <summary>
        /// Stores all times at which a new order was placed.
        /// </summary>
        internal List<OrderPlacedDatapoint> _statOrderPlacementTimestamps = new List<OrderPlacedDatapoint>();
        /// <summary>
        /// Stores all times at which a collision happened.
        /// </summary>
        internal List<CollisionDatapoint> _statCollisionTimestamps = new List<CollisionDatapoint>();
        /// <summary>
        /// Stores all completed trips to stations queueing areas.
        /// </summary>
        internal List<StationTripDatapoint> _statStationTripTimestamps = new List<StationTripDatapoint>();
        /// <summary>
        /// Per-segment edge traversal records (Stage A of congestion-aware cost estimator).
        /// </summary>
        internal List<TraversalDatapoint> _statTraversalRecords = new List<TraversalDatapoint>();
        /// <summary>
        /// Per-segment congestion-feature snapshots taken at ReadyTime (Stage A.2).
        /// </summary>
        internal List<CongestionFeatureDatapoint> _statCongestionFeatures = new List<CongestionFeatureDatapoint>();
        /// <summary>
        /// The number of collisions that happened in this instance. This value may count a collision of two robots twice, hence it should only be used as an indicator.
        /// </summary>
        public int StatOverallCollisions { get; private set; }
        /// <summary>
        /// The number of bots that reached their targeted output station queueing area.
        /// </summary>
        private int _oStationTripCount = 0;
        /// <summary>
        /// The average time it took a bot for their last trip towards an output station queueing area.
        /// </summary>
        private double _oStationTripTimeAvg = 0;
        /// <summary>
        /// The number of bots that reached their targeted input station queueing area.
        /// </summary>
        private int _iStationTripCount = 0;
        /// <summary>
        /// The average time it took a bot for their last trip towards an input station queueing area.
        /// </summary>
        private double _iStationTripTimeAvg = 0;
        /// <summary>
        /// Registers another trip completed trip to a station queueing area.
        /// </summary>
        /// <param name="tripType">The type of the trip.</param>
        /// <param name="tripTime">The time for completing the trip.</param>
        protected void StatAddTrip(StationTripType tripType, double tripTime)
        {
            switch (tripType)
            {
                case StationTripType.O: _oStationTripCount++; _oStationTripTimeAvg = _oStationTripTimeAvg + (tripTime - _oStationTripTimeAvg) / (_oStationTripCount); break;
                case StationTripType.I: _iStationTripCount++; _iStationTripTimeAvg = _iStationTripTimeAvg + (tripTime - _iStationTripTimeAvg) / (_iStationTripCount); break;
                default: throw new ArgumentException("Unknown trip type: " + tripType);
            }
        }
        /// <summary>
        /// The number of bots that reached their targeted output station queueing area.
        /// </summary>
        public double OStationTripCount { get { return _oStationTripCount; } }
        /// <summary>
        /// The average time it took a bot for their last trip towards an output station queueing area.
        /// </summary>
        public double OStationTripTimeAvg { get { return _oStationTripTimeAvg; } }
        /// <summary>
        /// The number of bots that reached their targeted input station queueing area.
        /// </summary>
        public double IStationTripCount { get { return _iStationTripCount; } }
        /// <summary>
        /// The number of bots that reached their targeted output station queueing area.
        /// </summary>
        public double IStationTripTimeAvg { get { return _iStationTripTimeAvg; } }
        /// <summary>
        /// The number of times a move could not be executed due to a failed reservation.
        /// </summary>
        public int StatOverallFailedReservations { get; internal set; }
        /// <summary>
        /// The number of times the runtime limit was reached by the path planning method.
        /// </summary>
        public int StatOverallPathPlanningTimeouts { get; internal set; }
        /// <summary>
        /// The overall storage capacity.
        /// </summary>
        private double _storageCapacity = double.NaN;
        /// <summary>
        /// The current overall storage usage.
        /// </summary>
        private double _storageUsage = double.NaN;
        /// <summary>
        /// The current overall storage reserved for bundles.
        /// </summary>
        private double _storageReserved = double.NaN;
        /// <summary>
        /// The current storage capacity required by the backlog bundles.
        /// </summary>
        private double _storageBacklog = double.NaN;
        /// <summary>
        /// The overall storage capacity.
        /// </summary>
        internal double StorageCapacity
        {
            get { if (double.IsNaN(_storageCapacity)) InitStorageTracking(); return _storageCapacity; }
            set { if (double.IsNaN(_storageCapacity)) InitStorageTracking(); _storageCapacity = value; }
        }
        /// <summary>
        /// The current overall storage usage.
        /// </summary>
        internal double StorageUsage
        {
            get { if (double.IsNaN(_storageUsage)) InitStorageTracking(); return _storageUsage; }
            set { if (double.IsNaN(_storageUsage)) InitStorageTracking(); _storageUsage = value; }
        }
        /// <summary>
        /// The current overall storage reserved for bundles.
        /// </summary>
        internal double StorageReserved
        {
            get { if (double.IsNaN(_storageReserved)) InitStorageTracking(); return _storageReserved; }
            set { if (double.IsNaN(_storageReserved)) InitStorageTracking(); _storageReserved = value; }
        }
        /// <summary>
        /// The current storage capacity required by the backlog bundles.
        /// </summary>
        internal double StorageBacklog
        {
            get { if (double.IsNaN(_storageBacklog)) InitStorageTracking(); return _storageBacklog; }
            set { if (double.IsNaN(_storageBacklog)) InitStorageTracking(); _storageBacklog = value; }
        }
        /// <summary>
        /// Initializes storage tracking.
        /// </summary>
        private void InitStorageTracking()
        {
            _storageCapacity = Pods.Sum(p => p.Capacity);
            _storageUsage = Pods.Sum(p => p.CapacityInUse);
            _storageReserved = Pods.Sum(p => p.CapacityReserved);
            _storageBacklog = 0;
        }
        /// <summary>
        /// The overall storage fill level.
        /// </summary>
        public double StatStorageFillLevel { get { return StorageUsage / StorageCapacity; } }
        /// <summary>
        /// The overall storage fill level including the reservations present for the pods.
        /// </summary>
        public double StatStorageFillAndReservedLevel { get { return (StorageUsage + StorageReserved) / StorageCapacity; } }
        /// <summary>
        /// The overall storage fill level including the reservations present for the pods and the capacity consumed by the backlog bundles.
        /// </summary>
        public double StatStorageFillAndReservedAndBacklogLevel { get { return (StorageUsage + StorageReserved + StorageBacklog) / StorageCapacity; } }
        /// <summary>
        /// The maximal number of items handled by a pod.
        /// </summary>
        public int StatMaxItemsHandledByPod { get; private set; }
        /// <summary>
        /// The maximal number of bundles handled by a pod.
        /// </summary>
        public int StatMaxBundlesHandledByPod { get; private set; }
        /// <summary>
        /// Contains custom info written by the different controllers.
        /// </summary>
        public CustomControllerDatapoint StatCustomControllerInfo { get; private set; } = new CustomControllerDatapoint();

        #endregion

        #region Stat I/O and reset

        /// <summary>
        /// Resets the statistics
        /// </summary>
        public void StatReset()
        {
            // Indicate reset for controlling processes
            StatWarmupResetDone = true;
            // Reset basics
            StatTimeStart = Controller.CurrentTime;
            StatMaxItemsHandledByPod = 0;
            StatMaxBundlesHandledByPod = 0;
            StatBundleGenerationStops = 0;
            StatOrderGenerationStops = 0;
            StatOverallItemsHandled = 0;
            StatOverallBundlesHandled = 0;
            StatOverallLinesHandled = 0;
            StatOverallOrdersHandled = 0;
            StatOverallOrdersLate = 0;
            _statBundleHandlingTimestamps.Clear();
            _statItemHandlingTimestamps.Clear();
            _statOrderHandlingTimestamps.Clear();
            _statBundlePlacementTimestamps.Clear();
            _statOrderPlacementTimestamps.Clear();
            StatOverallCollisions = 0;
            StatOverallFailedReservations = 0;
            StatOverallPathPlanningTimeouts = 0;
            _statCollisionTimestamps.Clear();
            _oStationTripCount = 0;
            _oStationTripTimeAvg = 0;
            _iStationTripCount = 0;
            _iStationTripTimeAvg = 0;
            _statStationTripTimestamps.Clear();
            StatOverallItemsOrdered = 0;
            StatOverallOrdersPlaced = 0;
            StatOverallBundlesPlaced = 0;
            StatOverallOrdersRejected = 0;
            StatOverallBundlesRejected = 0;
            StatRepositioningMoves = 0;
            _statOrderTurnoverTimes.Clear();
            _statOrderThroughputTimes.Clear();
            _statOrderLatenessTimes.Clear();
            _statBundleThroughputTimes.Clear();
            _statBundleTurnoverTimes.Clear();

            // Reset custom controller info
            StatCustomControllerInfo = new CustomControllerDatapoint();
            Controller.MethodManager?.StatReset();
            Controller.StationManager?.StatReset();
            Controller.OrderManager?.StatReset();
            Controller.BundleManager?.StatReset();
            Controller.StorageManager?.StatReset();
            Controller.PodStorageManager?.StatReset();
            Controller.RepositioningManager?.StatReset();
            Controller.BotManager?.StatReset();
            Controller.PathManager?.StatReset();

            // Reset observer
            Observer.Reset();

            // Reset frequency tracker
            FrequencyTracker.Reset();

            // Reset all objects
            foreach (var b in Bots)
            {
                b.ResetStatistics();
                if (b is Bots.BotNormal bn)
                    bn.ResetEnergyStatistics();
            }
            foreach (var ls in InputStations)
                ls.ResetStatistics();
            foreach (var ws in OutputStations)
                ws.ResetStatistics();
            foreach (var b in Pods)
                b.ResetStatistics();
            foreach (var wp in Waypoints)
                wp.ResetStatistics();
            ItemManager.ResetStatistics();

            // Init statistics directory
            StatInitDirectory();

            // Clean up potential previous data
            if (Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                // Delete all stat files one by one (sparing other files in the directory)
                foreach (var statFileName in IOConstants.StatFileNames.Values)
                {
                    string statFilePath = Path.Combine(SettingConfig.StatisticsDirectory, statFileName);
                    if (File.Exists(statFilePath))
                        File.Delete(statFilePath);
                }
            }
        }

        /// <summary>
        /// Finalizes some potentially incomplete statistics.
        /// </summary>
        public void StatFinish()
        {
            // Finalize potentially incomplete trips
            foreach (var bot in Bots)
                bot.LogIncompleteTrip();
            // Submit custom statistics
            Controller.MethodManager?.StatFinish();
            Controller.StationManager?.StatFinish();
            Controller.OrderManager?.StatFinish();
            Controller.BundleManager?.StatFinish();
            Controller.StorageManager?.StatFinish();
            Controller.PodStorageManager?.StatFinish();
            Controller.RepositioningManager?.StatFinish();
            Controller.BotManager?.StatFinish();
            Controller.PathManager?.StatFinish();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushBundlesHandled()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write bundle-handling progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundleProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundleProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + BundleHandledDatapoint.GetHeader());
                        foreach (var d in _statBundleHandlingTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statBundleHandlingTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushItemsHandled()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write item-handling progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ItemProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ItemProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + ItemHandledDatapoint.GetHeader());
                        foreach (var d in _statItemHandlingTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statItemHandlingTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushOrdersHandled()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write order-handling progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + OrderHandledDatapoint.GetHeader());
                        foreach (var d in _statOrderHandlingTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statOrderHandlingTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics.
        /// </summary>
        public void StatFlushBundlesPlaced()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write bundle-placement progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundlePlacementProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundlePlacementProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + BundlePlacedDatapoint.GetHeader());
                        foreach (var d in _statBundlePlacementTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statBundlePlacementTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics.
        /// </summary>
        public void StatFlushOrdersPlaced()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write order-placement progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderPlacementProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderPlacementProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + OrderPlacedDatapoint.GetHeader());
                        foreach (var d in _statOrderPlacementTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statOrderPlacementTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics.
        /// </summary>
        private void StatFlushPathFinding()
        {
            if (Controller.PathManager == null || !Controller.PathManager.Log)
                return;

            // Init statistics directory
            StatInitDirectory();
            // Write path finding data
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.PathFinding]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.PathFinding]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + PathFindingDatapoint.GetHeader());
                        foreach (var d in Controller.PathManager.StatDataPoints)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            Controller.PathManager.StatDataPoints.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushCollisions()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write collision progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.CollisionProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.CollisionProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + CollisionDatapoint.GetHeader());
                        foreach (var d in _statCollisionTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statCollisionTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushTripsCompleted()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write station trip progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.TripsCompletedProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.TripsCompletedProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + StationTripDatapoint.GetHeader());
                        foreach (var d in _statStationTripTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statStationTripTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the per-segment edge traversal log.
        /// </summary>
        public void StatFlushTraversalLog()
        {
            // Init statistics directory
            StatInitDirectory();
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.TraversalLog]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.TraversalLog]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + TraversalDatapoint.GetHeader());
                        foreach (var d in _statTraversalRecords)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            _statTraversalRecords.Clear();
        }

        /// <summary>
        /// Flushes the per-segment congestion-feature snapshots.
        /// </summary>
        public void StatFlushCongestionFeatures()
        {
            StatInitDirectory();
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.CongestionFeatureLog]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.CongestionFeatureLog]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + CongestionFeatureDatapoint.GetHeader());
                        foreach (var d in _statCongestionFeatures)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            _statCongestionFeatures.Clear();
        }

        /// <summary>
        /// Flushes statistics about the trips of the robots.
        /// </summary>
        public void StatFlushTripStatistics()
        {
            // --> Flush whole trip statistics
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    // Get station waypoints
                    List<Waypoint> outputstationWPs = Waypoints.Where(wp => wp.OutputStation != null).ToList();
                    List<Waypoint> inputstationWPs = Waypoints.Where(wp => wp.InputStation != null).ToList();
                    // Create files and flush data
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.HeatTrips]), false))
                    {
                        // Write header
                        sw.WriteLine(TimeIndependentTripDataPoint.GetCSVHeader());
                        // Write datapoints per waypoint
                        foreach (var waypoint in Waypoints)
                        {
                            TimeIndependentTripDataPoint datapoint = new TimeIndependentTripDataPoint()
                            {
                                Tier = waypoint.Tier.ID,
                                X = waypoint.X,
                                Y = waypoint.Y,
                                Overall = Waypoints.Sum(wp => wp.StatContainsTripDataIn(waypoint) ? wp.StatGetTripDataIn(waypoint).Count : 0),
                                ToOStation = outputstationWPs.Sum(o => o.StatContainsTripDataIn(waypoint) ? o.StatGetTripDataIn(waypoint).Count : 0),
                                FromOStation = outputstationWPs.Sum(o => o.StatContainsTripDataOut(waypoint) ? o.StatGetTripDataOut(waypoint).Count : 0),
                                ToIStation = inputstationWPs.Sum(i => i.StatContainsTripDataIn(waypoint) ? i.StatGetTripDataIn(waypoint).Count : 0),
                                FromIStation = inputstationWPs.Sum(i => i.StatContainsTripDataOut(waypoint) ? i.StatGetTripDataOut(waypoint).Count : 0),
                            };
                            sw.WriteLine(datapoint.ToCSV());
                        }
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
        }

        /// <summary>
        /// Flushes all data of all connections that have been used.
        /// </summary>
        public void StatFlushConnectionStatistics()
        {
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ConnectionStatistics]), false))
                    {
                        sw.WriteLine(IOConstants.COMMENT_LINE + ConnectionStatisticsDataPoint.GetStringTupleRepresentationDescription());
                        foreach (var from in Waypoints)
                            foreach (var to in Waypoints)
                                if (from.StatContainsTripDataOut(to))
                                    sw.WriteLine(from.StatGetTripDataOut(to).GetStringTupleRepresentation());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
        }

        /// <summary>
        /// Initializes the statistics directory.
        /// </summary>
        internal void StatInitDirectory()
        {
            // Create a default statistics directory name if none is given
            if (string.IsNullOrWhiteSpace(SettingConfig.StatisticsDirectory))
                SettingConfig.StatisticsDirectory = GetMetaInfoBasedInstanceName() + "-" + ControllerConfig.GetMetaInfoBasedConfigName() + "-" + SettingConfig.Seed;
            // Create the new and empty directory
            if (!Directory.Exists(SettingConfig.StatisticsDirectory))
                Directory.CreateDirectory(SettingConfig.StatisticsDirectory);
        }

        /// <summary>
        /// Calculates the p-th percentile of a sorted list of values.
        /// </summary>
        /// <param name="sortedValues">List of values already sorted in ascending order.</param>
        /// <param name="p">Percentile (0.0 to 1.0), e.g., 0.50 for median, 0.95 for p95.</param>
        /// <returns>The percentile value, or NaN if list is empty.</returns>
        private static double Percentile(List<double> sortedValues, double p)
        {
            if (sortedValues.Count == 0) return double.NaN;
            double idx = p * (sortedValues.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return sortedValues[lo] + (sortedValues[hi] - sortedValues[lo]) * (idx - lo);
        }

        /// <summary>
        /// Writes and flushes all statistics to the directory specified in the configuration.
        /// </summary>
        public void WriteStatistics()
        {
            // Indicate stats written for controlling processes
            StatResultsWritten = true;

            // Finalize statistics
            StatFinish();

            // Init statistics directory
            StatInitDirectory();

            // Write footprint
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.Footprint])))
                // Write stat line
                sw.WriteLine(new FootprintDatapoint(this).GetFootprint());

            // Write 6-Layer KPI report (always, regardless of log level)
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "kpi_report.csv")))
                WriteKpiReport(sw);

            // Write further statistics
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    // Write readable statistics
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ReadableStatistics])))
                        PrintStatistics(sw.WriteLine, detailedAll: true);
                    // Write station statistics
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.StationStatistics])))
                        WriteStationStatistics(sw.Write);
                    // Write item descriptions statistics
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ItemDescriptionStatistics])))
                        WriteItemDescriptionStatistics(sw.Write);
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }

            // Write instance name
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.InstanceName])))
                sw.Write((string.IsNullOrWhiteSpace(this.Name) ? this.GetMetaInfoBasedInstanceName() : this.Name));

            // Write setting name
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.SettingName])))
                sw.Write(SettingConfig.Name);

            // Write controller name
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ControllerName])))
                sw.Write(ControllerConfig.Name);

            // Flush the data
            StatFlushBundlesHandled();
            StatFlushItemsHandled();
            StatFlushOrdersHandled();
            StatFlushBundlesPlaced();
            StatFlushOrdersPlaced();
            StatFlushCollisions();
            StatFlushTripsCompleted();
            StatFlushTraversalLog();
            StatFlushCongestionFeatures();
            StatFlushPathFinding();

            // Flush observer data
            Observer.FlushData();

            // Flush controller dependant performance information
            WriteIndividiualStatistics();

            // Flush trip statistics data
            StatFlushTripStatistics();
            StatFlushConnectionStatistics();
        }

        /// <summary>
        /// Writes basic statistics about the stations in a CSV manner.
        /// </summary>
        /// <param name="writer">The write action to use.</param>
        public void WriteStationStatistics(Action<string> writer)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                "Ident" + IOConstants.DELIMITER_VALUE +
                "X" + IOConstants.DELIMITER_VALUE +
                "Y" + IOConstants.DELIMITER_VALUE +
                "Transfers" + IOConstants.DELIMITER_VALUE +
                "InjectedTransfers" + IOConstants.DELIMITER_VALUE +
                "PodsHandled" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeAvg" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeVar" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeMin" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeMax" + IOConstants.DELIMITER_VALUE +
                "PileOn" + IOConstants.DELIMITER_VALUE +
                "IdleTime" + IOConstants.DELIMITER_VALUE +
                "UpTime");
            foreach (var station in InputStations)
            {
                sb.AppendLine(
                    station.GetIdentfierString() + IOConstants.DELIMITER_VALUE +
                    station.X.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.Y.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatNumBundlesStored.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatNumInjectedBundlesStored.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodsHandled.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatBundlePileOn.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatIdleTime.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatActiveTime.ToString(IOConstants.FORMATTER));
            }
            foreach (var station in OutputStations)
            {
                sb.AppendLine(
                    station.GetIdentfierString() + IOConstants.DELIMITER_VALUE +
                    station.X.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.Y.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatNumItemsPicked.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatNumInjectedItemsPicked.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodsHandled.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatItemPileOn.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatIdleTime.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatActiveTime.ToString(IOConstants.FORMATTER));
            }
            writer(sb.ToString());
        }

        /// <summary>
        /// Writes basic information about the SKUs and how they were ordered during simulation.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        public void WriteItemDescriptionStatistics(Action<string> writer)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(IOConstants.COMMENT_LINE + ItemDescriptionFrequencyDatapoint.GetHeader());
            // Output all item descriptions
            foreach (var itemDescription in ItemDescriptions.OrderBy(i => i.ID))
                sb.AppendLine(new ItemDescriptionFrequencyDatapoint(itemDescription, FrequencyTracker).GetLine());
            // Write it
            writer(sb.ToString());
        }

        /// <summary>
        /// Prints a statistics overview in readable format to a specified action.
        /// </summary>
        /// <param name="writer">The action to print the statistics to.</param>
        /// <param name="detailedAll">Indicates whether to print detailed or short statistics.</param>
        /// <param name="detailedBots">Indicates whether to print detailed statistics about the bots.</param>
        /// <param name="detailedPods">Indicates whether to print detailed statistics about the pods.</param>
        /// <param name="detailedStations">Indicates whether to print detailed statistics about the stations.</param>
        public void PrintStatistics(Action<string> writer, bool detailedAll = false, bool detailedBots = false, bool detailedPods = false, bool detailedStations = false)
        {
            StringBuilder sb = new StringBuilder();
            if (detailedAll || detailedBots)
            {
                sb.AppendLine(">>> Bots");
                foreach (var bot in Bots)
                {
                    sb.AppendLine(bot.ToString() + ":");
                    sb.AppendLine("DistanceTraveled: " + bot.StatDistanceTraveled);
                    sb.AppendLine("DistanceEstimated: " + bot.StatDistanceEstimated);
                    sb.AppendLine("NumberOfPickups: " + bot.StatNumberOfPickups);
                    sb.AppendLine("NumberOfSetdowns: " + bot.StatNumberOfSetdowns);
                    sb.AppendLine("NumCollisions: " + bot.StatNumCollisions);
                    sb.AppendLine("TotalTimeMoving: " + bot.StatTotalTimeMoving.ToString(IOConstants.FORMATTER));
                    //sb.AppendLine("TaskStartTime: " + bot.StatTaskStartTime);
                    foreach (var taskType in Enum.GetValues(typeof(BotTaskType)).Cast<BotTaskType>())
                        if (bot.StatTotalTaskTimes.ContainsKey(taskType))
                            sb.AppendLine(taskType + ": " + bot.StatTotalTaskTimes[taskType].ToString(IOConstants.FORMATTER));
                    foreach (var stateType in Enum.GetValues(typeof(BotStateType)).Cast<BotStateType>())
                        if (bot.StatTotalStateTimes.ContainsKey(stateType))
                            sb.AppendLine(stateType + ": " + bot.StatTotalStateTimes[stateType].ToString(IOConstants.FORMATTER));
                }
            }
            if (detailedAll || detailedPods)
            {
                sb.AppendLine(">>> Pods");
                foreach (var pod in Pods)
                {
                    sb.AppendLine(pod.ToString() + ":");
                    string itemDescriptions = "";
                    foreach (var description in pod.ItemDescriptionsContained)
                        itemDescriptions += description.ToString() + " ";
                    sb.AppendLine(itemDescriptions);
                    sb.AppendLine("StatItemsHandledAtOutputStations: " + pod.StatItemsHandled);
                    sb.AppendLine("StatBundlesHandledAtInputStations: " + pod.StatBundlesHandled);
                }
            }
            if (detailedAll || detailedStations)
            {
                sb.AppendLine(">>> InputStations");
                foreach (var iStation in InputStations)
                {
                    sb.AppendLine(iStation.ToString() + ":");
                    sb.AppendLine("IdleTime: " + iStation.StatIdleTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("UpTime: " + iStation.StatActiveTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("DownTime: " + iStation.StatDownTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("NumBundlesPut: " + iStation.StatNumBundlesStored);
                    sb.AppendLine("NumInjectedBundlesPut: " + iStation.StatNumInjectedBundlesStored);
                    sb.AppendLine("BundlePileOn: " + iStation.StatBundlePileOn.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodsHandled: " + iStation.StatPodsHandled.ToString());
                    sb.AppendLine("PodHandlingTimeAvg: " + iStation.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeVar: " + iStation.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMin: " + iStation.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMax: " + iStation.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER));
                }
                sb.AppendLine(">>> OutputStations");
                foreach (var oStation in OutputStations)
                {
                    sb.AppendLine(oStation.ToString() + ":");
                    sb.AppendLine("IdleTime: " + oStation.StatIdleTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("UpTime: " + oStation.StatActiveTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("DownTime: " + oStation.StatDownTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("NumItemsPicked: " + oStation.StatNumItemsPicked);
                    sb.AppendLine("NumInjectedItemsPicked: " + oStation.StatNumInjectedItemsPicked);
                    sb.AppendLine("NumOrdersFinished: " + oStation.StatNumOrdersFinished);
                    sb.AppendLine("ItemPileOn: " + oStation.StatItemPileOn.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("OrderPileOn: " + oStation.StatOrderPileOn.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodsHandled: " + oStation.StatPodsHandled.ToString());
                    sb.AppendLine("PodHandlingTimeAvg: " + oStation.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeVar: " + oStation.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMin: " + oStation.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMax: " + oStation.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER));
                }
            }
            sb.AppendLine(">>> Timings");
            sb.AppendLine("StatTimingPathPlanningAverage: " + Observer.TimingPathPlanningAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPathPlanningOverall: " + Observer.TimingPathPlanningOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPathPlanningCount: " + Observer.TimingPathPlanningDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingTaskAllocationAverage: " + Observer.TimingTaskAllocationAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingTaskAllocationOverall: " + Observer.TimingTaskAllocationOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingTaskAllocationCount: " + Observer.TimingTaskAllocationDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingItemStorageAverage: " + Observer.TimingItemStorageAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingItemStorageOverall: " + Observer.TimingItemStorageOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingItemStorageCount: " + Observer.TimingItemStorageDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPodStorageAverage: " + Observer.TimingPodStorageAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPodStorageOverall: " + Observer.TimingPodStorageOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPodStorageCount: " + Observer.TimingPodStorageDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingReplenishmentBatchingAverage: " + Observer.TimingReplenishmentBatchingAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingReplenishmentBatchingOverall: " + Observer.TimingReplenishmentBatchingOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingReplenishmentBatchingCount: " + Observer.TimingReplenishmentBatchingDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingOrderBatchingAverage: " + Observer.TimingOrderBatchingAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingOrderBatchingOverall: " + Observer.TimingOrderBatchingOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingOrderBatchingCount: " + Observer.TimingOrderBatchingDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine(">>> Overall");
            sb.AppendLine("StatOverallBundlesPlaced: " + StatOverallBundlesPlaced);
            sb.AppendLine("StatOverallItemsOrdered: " + StatOverallItemsOrdered);
            sb.AppendLine("StatOverallOrdersPlaced: " + StatOverallOrdersPlaced);
            sb.AppendLine("StatOverallBundlesRejected: " + StatOverallBundlesRejected);
            sb.AppendLine("StatOverallOrdersRejected: " + StatOverallOrdersRejected);
            sb.AppendLine("StatOverallBundlesHandled: " + StatOverallBundlesHandled);
            sb.AppendLine("StatOverallItemsHandled: " + StatOverallItemsHandled);
            sb.AppendLine("StatOverallLinesHandled: " + StatOverallLinesHandled);
            sb.AppendLine("StatOverallOrdersHandled: " + StatOverallOrdersHandled);
            sb.AppendLine("StatThroughputOrdersPerHour: " + StatThroughputOrdersPerHour.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOrderDistanceM: " + StatOrderDistanceM.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatInputStationArrivals: " + StatOverallInputStationArrivals);
            sb.AppendLine("StatOutputStationArrivals: " + StatOverallOutputStationArrivals);
            sb.AppendLine("StatStationArrivalsTotal: " + StatOverallStationArrivals);
            sb.AppendLine("StatSystemOrderPileOn: " + StatSystemOrderPileOn.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOverallCollisions: " + StatOverallCollisions);
            sb.AppendLine("StatOverallDistanceTraveled: " + StatOverallDistanceTraveled.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOverallDistanceEstimated: " + StatOverallDistanceEstimated.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOverallAssignedTasks: " + StatOverallAssignedTasks);
            sb.AppendLine("StatMaxMemoryUsed: " + StatMaxMemoryUsed);
            sb.AppendLine("StatRealTimeUsed: " + ((SettingConfig.StartTime != default(DateTime) && SettingConfig.StopTime != default(DateTime)) ? (SettingConfig.StopTime - SettingConfig.StartTime).TotalSeconds.ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatAverageTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : _statOrderTurnoverTimes.Average().ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatMedianTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : StatisticsHelper.GetMedian(_statOrderTurnoverTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatLowerQuartileTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : StatisticsHelper.GetLowerQuartile(_statOrderTurnoverTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatUpperQuartileTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : StatisticsHelper.GetUpperQuartile(_statOrderTurnoverTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatAverageThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : _statOrderThroughputTimes.Average().ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatMedianThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : StatisticsHelper.GetMedian(_statOrderThroughputTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatLowerQuartileThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : StatisticsHelper.GetLowerQuartile(_statOrderThroughputTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatUpperQuartileThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : StatisticsHelper.GetUpperQuartile(_statOrderThroughputTimes).ToString(IOConstants.FORMATTER)));
            // KPI Summary (5 core metrics)
            sb.AppendLine(">>> KPI Summary");
            sb.AppendLine("KPI_TP: " + StatThroughputOrdersPerHour.ToString(IOConstants.FORMATTER));
            sb.AppendLine("KPI_PO: " + StatSystemOrderPileOn.ToString(IOConstants.FORMATTER));
            sb.AppendLine("KPI_RD: " + StatOverallDistanceTraveled.ToString(IOConstants.FORMATTER));
            sb.AppendLine("KPI_OD: " + (StatOverallOrdersHandled > 0 ? (StatOverallDistanceTraveled / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("KPI_EOR: " + (StatOverallOrdersHandled > 0 ? (StatOverallEnergyTotalJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            // Energy statistics (Rizqi model)
            sb.AppendLine(">>> Energy (Rizqi model)");
            sb.AppendLine("StatEnergyTotalKJ: " + (StatOverallEnergyTotalJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE1AccelKJ: " + (StatOverallEnergyE1J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE2DecelKJ: " + (StatOverallEnergyE2J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE3CruiseKJ: " + (StatOverallEnergyE3J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE4RotationKJ: " + (StatOverallEnergyE4J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE5LiftLowerKJ: " + (StatOverallEnergyE5J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEnergyTotalJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatEnergyPerMeterJoule: " + (StatOverallDistanceTraveledRizqi > 0 ? (StatOverallEnergyTotalJ / StatOverallDistanceTraveledRizqi).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatEnergySupportKJ: " + (StatOverallEnergySupportJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyTotalWithSupportKJ: " + (StatOverallEnergyTotalWithSupportJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyPerOrderWithSupportKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEnergyTotalWithSupportJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatDistanceTraveledRizqiM: " + StatOverallDistanceTraveledRizqi.ToString(IOConstants.FORMATTER));
            sb.AppendLine(">>> Motion Behavior");
            sb.AppendLine("StatTurningCount: " + StatOverallTurningCount);
            sb.AppendLine("StatLoadedDistanceM: " + StatOverallLoadedDistanceM.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatLoadedTurningCount: " + StatOverallLoadedTurningCount);
            sb.AppendLine("StatEmptyTurningCount: " + StatOverallEmptyTurningCount);
            sb.AppendLine("StatWaitTimeSec: " + StatOverallWaitTimeSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatPathPlanningTimeouts: " + StatOverallPathPlanningTimeouts);
            // ── Pref calibration output (event-level, move/turn split) ───────────
            sb.AppendLine("StatMoveEnergyEmptyKJ: "  + (StatOverallMoveEnergyEmptyJ  / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatMoveTimeEmptySec: "   + StatOverallMoveTimeEmptySec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnEnergyEmptyKJ: "  + (StatOverallTurnEnergyEmptyJ  / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnTimeEmptySec: "   + StatOverallTurnTimeEmptySec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatMoveEnergyLoadedKJ: " + (StatOverallMoveEnergyLoadedJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatMoveTimeLoadedSec: "  + StatOverallMoveTimeLoadedSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnEnergyLoadedKJ: " + (StatOverallTurnEnergyLoadedJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnTimeLoadedSec: "  + StatOverallTurnTimeLoadedSec.ToString(IOConstants.FORMATTER));
            // Pref = (moveE + turnE) / (moveT + turnT)  — power during active motion only
            double prefEmpty  = (StatOverallMoveTimeEmptySec  + StatOverallTurnTimeEmptySec)  > 0
                ? (StatOverallMoveEnergyEmptyJ  + StatOverallTurnEnergyEmptyJ)  / (StatOverallMoveTimeEmptySec  + StatOverallTurnTimeEmptySec)  : 0.0;
            double prefLoaded = (StatOverallMoveTimeLoadedSec + StatOverallTurnTimeLoadedSec) > 0
                ? (StatOverallMoveEnergyLoadedJ + StatOverallTurnEnergyLoadedJ) / (StatOverallMoveTimeLoadedSec + StatOverallTurnTimeLoadedSec) : 0.0;
            sb.AppendLine("StatPrefEmptyW: "  + prefEmpty.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatPrefLoadedW: " + prefLoaded.ToString(IOConstants.FORMATTER));
            // ── Per-trip wait ratio statistics ───────────────
            var totalPickupCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.StatPickupCount);
            var totalSetdownCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.StatSetdownCount);
            var totalWaitRatiosEmptyCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.PerTripWaitRatioEmpty.Count);
            var totalWaitRatiosLoadedCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.PerTripWaitRatioLoaded.Count);
            sb.AppendLine("StatPickupCount: " + totalPickupCount);
            sb.AppendLine("StatSetdownCount: " + totalSetdownCount);
            sb.AppendLine("PerTripWaitRatioEmptyCount: " + totalWaitRatiosEmptyCount);
            sb.AppendLine("PerTripWaitRatioLoadedCount: " + totalWaitRatiosLoadedCount);
            sb.AppendLine("StatTripCountLoaded: " + StatOverallTripCountLoaded);
            sb.AppendLine("StatTripCountEmpty: " + StatOverallTripCountEmpty);
            // ── Per-robot diagnostics ───────────────
            sb.AppendLine(">>> Per-Robot Trip Counts");
            foreach (var bot in Bots.OfType<Bots.BotNormal>().OrderBy(b => b.ID))
            {
                sb.AppendLine($"  Bot{bot.ID}: pickupCount={bot.StatPickupCount}, tripCountEmpty={bot.StatTripCountEmpty}, setdownCount={bot.StatSetdownCount}, tripCountLoaded={bot.StatTripCountLoaded}, ordersCompleted={bot.StatOrdersCompleted}");
            }
            // Aggregate all per-trip wait ratios from all bots
            var allWaitRatiosLoaded = Bots.OfType<Bots.BotNormal>()
                .SelectMany(b => b.PerTripWaitRatioLoaded).ToList();
            if (allWaitRatiosLoaded.Count > 0)
            {
                allWaitRatiosLoaded.Sort();
                double meanLoaded = allWaitRatiosLoaded.Average();
                double medianLoaded = Percentile(allWaitRatiosLoaded, 0.50);
                double p95Loaded = Percentile(allWaitRatiosLoaded, 0.95);
                sb.AppendLine("StatTripWaitRatioLoadedMean: " + meanLoaded.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioLoadedMedian: " + medianLoaded.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioLoadedP95: " + p95Loaded.ToString(IOConstants.FORMATTER));
            }
            var allWaitRatiosEmpty = Bots.OfType<Bots.BotNormal>()
                .SelectMany(b => b.PerTripWaitRatioEmpty).ToList();
            if (allWaitRatiosEmpty.Count > 0)
            {
                allWaitRatiosEmpty.Sort();
                double meanEmpty = allWaitRatiosEmpty.Average();
                double medianEmpty = Percentile(allWaitRatiosEmpty, 0.50);
                double p95Empty = Percentile(allWaitRatiosEmpty, 0.95);
                sb.AppendLine("StatTripWaitRatioEmptyMean: " + meanEmpty.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioEmptyMedian: " + medianEmpty.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioEmptyP95: " + p95Empty.ToString(IOConstants.FORMATTER));
            }
            sb.AppendLine(">>> Support & Utilization");
            int botCount = Bots.OfType<Bots.BotNormal>().Count();
            double utilization = (StatTime > 0 && botCount > 0)
                ? 1.0 - (StatOverallTimeIdleSec / (StatTime * botCount))
                : double.NaN;
            // E_support = background support energy (P_SUPPORT × active-task time; includes moving)
            sb.AppendLine("StatESupportKJ: " + (StatOverallESupportJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatESupportPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallESupportJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            // E_wait = congestion-wait subset of E_support (stationary with active task)
            sb.AppendLine("StatEWaitKJ: " + (StatOverallEWaitJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEWaitLoadedKJ: " + (StatOverallEWaitLoadedJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEWaitEmptyKJ: " + (StatOverallEWaitEmptyJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEWaitPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEWaitJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatTimeIdleSec: " + StatOverallTimeIdleSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatRobotUtilization: " + (double.IsNaN(utilization) ? "NaN" : utilization.ToString(IOConstants.FORMATTER)));
            double eWaitToMechRatio = StatOverallEnergyTotalJ > 0
                ? StatOverallEWaitJ / StatOverallEnergyTotalJ : double.NaN;
            sb.AppendLine("StatEWaitToMechRatio: " + (double.IsNaN(eWaitToMechRatio) ? "NaN" : eWaitToMechRatio.ToString(IOConstants.FORMATTER)));
            double eSupportToMechRatio = StatOverallEnergyTotalJ > 0
                ? StatOverallESupportJ / StatOverallEnergyTotalJ : double.NaN;
            sb.AppendLine("StatESupportToMechRatio: " + (double.IsNaN(eSupportToMechRatio) ? "NaN" : eSupportToMechRatio.ToString(IOConstants.FORMATTER)));
            // Write output
            writer(sb.ToString());
        }

        /// <summary>
        /// Writes a CSV KPI report covering the 6-layer metrics framework.
        /// Columns: layer,metric,empty,loaded,total,unit
        /// </summary>
        private void WriteKpiReport(StreamWriter sw)
        {
            var bots = Bots.OfType<Bots.BotNormal>().ToList();
            string fmt(double v) => v.ToString(IOConstants.FORMATTER);
            string row(string layer, string metric, object empty, object loaded, object total, string unit)
                => $"{layer},{metric},{empty},{loaded},{total},{unit}";

            double totalEnergyJ   = StatOverallEnergyTotalWithSupportJ;
            double totalMechJ     = StatOverallEnergyTotalJ;
            double kpiDistM       = StatOverallDistanceTraveled;
            double motionDistM    = StatOverallDistanceTraveledRizqi;
            double totalDistLoad  = StatOverallLoadedDistanceM;
            double totalDistEmpty = StatOverallEmptyDistanceM;
            int    totalOrders    = StatOverallOrdersHandled;
            double durationSec    = StatTime;
            int    tripsLoaded    = StatOverallTripCountLoaded;
            int    tripsEmpty     = StatOverallTripCountEmpty;

            double moveE_L = StatOverallMoveEnergyLoadedJ, moveE_E = StatOverallMoveEnergyEmptyJ;
            double turnE_L = StatOverallTurnEnergyLoadedJ, turnE_E = StatOverallTurnEnergyEmptyJ;
            double waitE_L = StatOverallWaitEnergyLoadedJ,  waitE_E = StatOverallWaitEnergyEmptyJ;

            double moveT_L = StatOverallMoveTimeLoadedSec, moveT_E = StatOverallMoveTimeEmptySec;
            double turnT_L = StatOverallTurnTimeLoadedSec, turnT_E = StatOverallTurnTimeEmptySec;
            double waitT_L = StatOverallWaitTimeLoadedSec, waitT_E = StatOverallWaitTimeEmptySec;

            int tnC_L = StatOverallLoadedTurningCount,   tnC_E = StatOverallEmptyTurningCount;

            sw.WriteLine("layer,metric,empty,loaded,total,unit");

            // Layer 1
            sw.WriteLine(row("L1", "orders_completed", "", "", totalOrders, "orders"));
            double opH = durationSec > 0 ? totalOrders / (durationSec / 3600.0) : 0.0;
            sw.WriteLine(row("L1", "orders_per_hour", "", "", fmt(opH), "orders/h"));
            sw.WriteLine(row("L1", "total_energy_with_support_kJ", "", "", fmt(totalEnergyJ / 1000.0), "kJ"));
            sw.WriteLine(row("L1", "total_energy_mech_kJ", "", "", fmt(totalMechJ / 1000.0), "kJ"));
            sw.WriteLine(row("L1", "energy_total_with_support_per_order_kJ", "", "",
                fmt(totalOrders > 0 ? totalEnergyJ / 1000.0 / totalOrders : 0.0), "kJ/order"));
            sw.WriteLine(row("L1", "energy_mech_per_order_kJ", "", "",
                fmt(totalOrders > 0 ? totalMechJ / 1000.0 / totalOrders : 0.0), "kJ/order"));
            sw.WriteLine(row("L1", "total_distance_m", "", "", fmt(kpiDistM), "m"));
            sw.WriteLine(row("L1", "order_distance_m", "", "", fmt(totalOrders > 0 ? kpiDistM / totalOrders : 0.0), "m/order"));
            sw.WriteLine(row("L1", "system_order_pile_on", "", "", fmt(StatSystemOrderPileOn), "orders/output_station_arrival"));
            sw.WriteLine(row("L1", "input_station_arrivals", "", "", StatOverallInputStationArrivals, "count"));
            sw.WriteLine(row("L1", "output_station_arrivals", "", "", StatOverallOutputStationArrivals, "count"));
            sw.WriteLine(row("L1", "station_arrivals_total", "", "", StatOverallStationArrivals, "count"));

            // Layer 2: trip split
            sw.WriteLine(row("L2", "trip_count", tripsEmpty, tripsLoaded, tripsEmpty + tripsLoaded, "trips"));
            sw.WriteLine(row("L2", "distance_m", fmt(totalDistEmpty), fmt(totalDistLoad), fmt(motionDistM), "m"));
            double timeLoad = moveT_L + turnT_L + waitT_L;
            double timeEmpty = moveT_E + turnT_E + waitT_E;
            sw.WriteLine(row("L2", "total_time_sec", fmt(timeEmpty), fmt(timeLoad), fmt(timeEmpty + timeLoad), "s"));
            double energyL_mech = moveE_L + turnE_L, energyE_mech = moveE_E + turnE_E;
            // E1-E4 only (E5 cannot be cleanly split by load state; see L3 for full phase breakdown)
            sw.WriteLine(row("L2", "total_move_turn_energy_kJ", fmt(energyE_mech / 1000.0), fmt(energyL_mech / 1000.0),
                fmt((energyE_mech + energyL_mech) / 1000.0), "kJ"));
            sw.WriteLine(row("L2", "move_turn_pct_of_mech",
                fmt(totalMechJ > 0 ? 100.0 * energyE_mech / totalMechJ : 0.0),
                fmt(totalMechJ > 0 ? 100.0 * energyL_mech / totalMechJ : 0.0),
                "", "%"));
            sw.WriteLine(row("L2", "avg_distance_per_trip_m",
                fmt(tripsEmpty  > 0 ? totalDistEmpty / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? totalDistLoad  / tripsLoaded : 0.0), "", "m/trip"));
            sw.WriteLine(row("L2", "avg_time_per_trip_sec",
                fmt(tripsEmpty  > 0 ? timeEmpty / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? timeLoad  / tripsLoaded : 0.0), "", "s/trip"));
            sw.WriteLine(row("L2", "avg_energy_per_trip_kJ",
                fmt(tripsEmpty  > 0 ? energyE_mech / 1000.0 / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? energyL_mech / 1000.0 / tripsLoaded : 0.0), "", "kJ/trip"));

            // Layer 3: energy phase breakdown (E1–E5), denominator = totalMechJ
            double e1J = StatOverallEnergyE1J;
            double e2J = StatOverallEnergyE2J;
            double e3J = StatOverallEnergyE3J;
            double e4J = StatOverallEnergyE4J;
            double e5J = StatOverallEnergyE5J;
            string mechPct(double v) => fmt(totalMechJ > 0 ? 100.0 * v / totalMechJ : 0.0);
            sw.WriteLine(row("L3", "e1_accel_kJ",  "", "", fmt(e1J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e2_decel_kJ",  "", "", fmt(e2J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e3_cruise_kJ", "", "", fmt(e3J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e4_rotation_kJ", "", "", fmt(e4J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e5_lift_kJ",   "", "", fmt(e5J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e1_pct_of_mech", "", "", mechPct(e1J), "%"));
            sw.WriteLine(row("L3", "e2_pct_of_mech", "", "", mechPct(e2J), "%"));
            sw.WriteLine(row("L3", "e3_pct_of_mech", "", "", mechPct(e3J), "%"));
            sw.WriteLine(row("L3", "e4_pct_of_mech", "", "", mechPct(e4J), "%"));
            sw.WriteLine(row("L3", "e5_pct_of_mech", "", "", mechPct(e5J), "%"));

            // Layer 4: turning
            sw.WriteLine(row("L4", "turn_count", tnC_E, tnC_L, tnC_E + tnC_L, "events"));
            sw.WriteLine(row("L4", "turn_energy_kJ", fmt(turnE_E / 1000.0), fmt(turnE_L / 1000.0),
                fmt((turnE_E + turnE_L) / 1000.0), "kJ"));
            sw.WriteLine(row("L4", "turn_pct_of_total",
                fmt(totalEnergyJ > 0 ? 100.0 * turnE_E / totalEnergyJ : 0.0),
                fmt(totalEnergyJ > 0 ? 100.0 * turnE_L / totalEnergyJ : 0.0), "", "%"));
            sw.WriteLine(row("L4", "turn_pct_of_state",
                fmt(energyE_mech > 0 ? 100.0 * turnE_E / energyE_mech : 0.0),
                fmt(energyL_mech > 0 ? 100.0 * turnE_L / energyL_mech : 0.0), "", "%"));
            sw.WriteLine(row("L4", "turn_per_trip",
                fmt(tripsEmpty  > 0 ? (double)tnC_E / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? (double)tnC_L / tripsLoaded : 0.0), "", "per trip"));
            sw.WriteLine(row("L4", "turn_per_m",
                fmt(totalDistEmpty > 0 ? tnC_E / totalDistEmpty : 0.0),
                fmt(totalDistLoad  > 0 ? tnC_L / totalDistLoad  : 0.0), "", "per m"));

            // Layer 5: wait
            sw.WriteLine(row("L5", "wait_time_sec", fmt(waitT_E), fmt(waitT_L), fmt(waitT_E + waitT_L), "s"));
            sw.WriteLine(row("L5", "wait_energy_kJ", fmt(waitE_E / 1000.0), fmt(waitE_L / 1000.0),
                fmt((waitE_E + waitE_L) / 1000.0), "kJ"));
            sw.WriteLine(row("L5", "wait_pct_of_total",
                fmt(totalEnergyJ > 0 ? 100.0 * waitE_E / totalEnergyJ : 0.0),
                fmt(totalEnergyJ > 0 ? 100.0 * waitE_L / totalEnergyJ : 0.0), "", "%"));
            // wait_ratio stats (combined + split)
            var wrAll = bots.SelectMany(b => b.PerTripWaitRatioLoaded.Concat(b.PerTripWaitRatioEmpty)).ToList();
            var wrL = bots.SelectMany(b => b.PerTripWaitRatioLoaded).ToList();
            var wrE = bots.SelectMany(b => b.PerTripWaitRatioEmpty).ToList();
            double meanR(List<double> xs) => xs.Count > 0 ? xs.Average() : 0.0;
            double medR (List<double> xs) { if (xs.Count == 0) return 0.0; xs.Sort(); return Percentile(xs, 0.50); }
            double p95R (List<double> xs) { if (xs.Count == 0) return 0.0; xs.Sort(); return Percentile(xs, 0.95); }
            sw.WriteLine(row("L5", "wait_ratio_mean", fmt(meanR(wrE)), fmt(meanR(wrL)), fmt(meanR(wrAll)), "ratio"));
            sw.WriteLine(row("L5", "wait_ratio_median", fmt(medR(wrE)), fmt(medR(wrL)), fmt(medR(wrAll)), "ratio"));
            sw.WriteLine(row("L5", "wait_ratio_p95", fmt(p95R(wrE)), fmt(p95R(wrL)), fmt(p95R(wrAll)), "ratio"));

            // Layer 6: derived insight metrics
            // 5-component energy composition: non-overlapping sum = E_mech + E_support.
            //   move    = E1+E2+E3 (mechanical drive)
            //   turn    = E4       (mechanical rotation)
            //   lift    = E5       (mechanical lift/lower)
            //   wait    = P_SUPPORT × WaitTimeSec  (routing-induced congestion cost)
            //   support = E_support_total − wait  (P_SUPPORT during motion & station service, no standby)
            // denominator = move+turn+lift+wait+support = StatOverallEnergyTotalWithSupportJ
            double totMove = moveE_L + moveE_E;
            double totTurn = turnE_L + turnE_E;
            double totLift = StatOverallEnergyE5J;
            double totWait = waitE_L + waitE_E;
            double totSupport = StatOverallEnergySupportJ - totWait;
            double comp5Total = totMove + totTurn + totLift + totWait + totSupport; // == StatOverallEnergyTotalWithSupportJ
            string compPct(double v) => fmt(comp5Total > 0 ? 100.0 * v / comp5Total : 0.0);
            sw.WriteLine(row("L6", "composition_move_pct",    "", "", compPct(totMove),    "%"));
            sw.WriteLine(row("L6", "composition_turn_pct",    "", "", compPct(totTurn),    "%"));
            sw.WriteLine(row("L6", "composition_lift_pct",    "", "", compPct(totLift),    "%"));
            sw.WriteLine(row("L6", "composition_wait_pct",    "", "", compPct(totWait),    "%"));
            sw.WriteLine(row("L6", "composition_support_pct", "", "", compPct(totSupport), "%"));
            sw.WriteLine(row("L6", "energy_empty_to_loaded_ratio", "", "",
                fmt(energyL_mech > 0 ? energyE_mech / energyL_mech : 0.0), "ratio"));
            double travT = moveT_L + moveT_E + turnT_L + turnT_E;
            sw.WriteLine(row("L6", "effective_motion_ratio", "", "",
                fmt((travT + waitT_L + waitT_E) > 0 ? travT / (travT + waitT_L + waitT_E) : 0.0), "ratio"));
            sw.WriteLine(row("L6", "wait_energy_per_order_kJ", "", "",
                fmt(totalOrders > 0 ? (waitE_L + waitE_E) / 1000.0 / totalOrders : 0.0), "kJ/order"));
            sw.WriteLine(row("L6", "energy_per_m_empty_J",
                fmt(totalDistEmpty > 0 ? energyE_mech / totalDistEmpty : 0.0), "", "", "J/m"));
            sw.WriteLine(row("L6", "energy_per_m_loaded_J", "",
                fmt(totalDistLoad > 0 ? energyL_mech / totalDistLoad : 0.0), "", "J/m"));
        }

        /// <summary>
        /// Flushes individual statistics, if the corresponding controllers are present.
        /// </summary>
        private void WriteIndividiualStatistics()
        {
            // Nothing to see here currently (experimental methods using this have been removed)
            // You may use this for dumping additional statistics for custom controllers (just check whether your controller was running)
            // e.g.: if(ControllerConfig.OrderBatchingConfig.GetMethodType() == Configurations.OrderBatchingMethodType.SimpleSavings) DumpStatisticsOfSimpleSavings
        }

        #endregion
    }
}
