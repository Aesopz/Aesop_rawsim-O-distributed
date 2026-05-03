using RAWSimO.Core.Info;
using RAWSimO.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RAWSimO.Visualization.Rendering
{
    #region Base class

    public abstract class SimulationInfoObject
    {
        public SimulationInfoObject(TreeView infoHost) { _infoHost = infoHost; }

        protected readonly TreeView _infoHost;
        internal SimulationVisual2D ManagedVisual2D { get; set; }
        internal SimulationVisual3D ManagedVisual3D { get; set; }

        protected double StrokeThicknessFocused { get { return 5 * ManagedVisual2D.StrokeThicknessReference; } }

        public abstract void InfoPanelInit();
        public void InfoPanelLeave() { ManagedVisual2D.StrokeThickness = ManagedVisual2D.StrokeThicknessReference; _infoHost.Items.Clear(); }
        public abstract void InfoPanelUpdate();
    }

    #endregion

    #region Instance

    public class SimulationInfoInstance : SimulationInfoObject
    {
        private readonly IInstanceInfo _instance;
        private readonly int _infoPanelLeftColumnWidth = 110;
        private readonly int _infoPanelRightColumnWidth = 60;
        private readonly int _infoPanelSingleSmallElementWidth = 16;
        private Brush _colorStationActive = Brushes.LightGreen;
        private Brush _colorStationInactive = Brushes.MediumBlue;
        private Brush _colorStationBlocked = Brushes.Red;
        private Brush _colorStationFree = Brushes.LightGreen;
        private TreeViewItem _root;
        private TextBlock _blockPendingBundles;
        private TextBlock _blockPendingOrders;
        private TextBlock _blockStatItems;
        private TextBlock _blockStatBundles;
        private TextBlock _blockStatOrders;
        private TextBlock _blockStatOrdersLate;
        private TextBlock _blockStatRepositioningMoves;
        private TextBlock _blockStatCollisions;
        private TextBlock _blockStatStorageFillLevel;
        private TextBlock _blockStatEnergyTotal;
        private TextBlock _blockStatEnergyE1;
        private TextBlock _blockStatEnergyE2;
        private TextBlock _blockStatEnergyE3;
        private TextBlock _blockStatEnergyE4;
        private TextBlock _blockStatEnergyE5;
        private TextBlock _blockStatTurningCount;
        // KPI (Layer 1 / Layer 6)
        private TextBlock _blockKpiThroughput;
        private TextBlock _blockKpiOrderDistance;
        private TextBlock _blockKpiOrderPileOn;
        private TextBlock _blockKpiStationArrivals;
        private TextBlock _blockKpiTotalDistance;
        private TextBlock _blockKpiLoadedDistance;
        private TextBlock _blockKpiEmptyDistance;
        private TextBlock _blockKpiEnergyPerOrder;
        private TextBlock _blockKpiTripLoaded;
        private TextBlock _blockKpiTripEmpty;
        private TextBlock _blockKpiWaitTimeLoaded;
        private TextBlock _blockKpiWaitTimeEmpty;
        private TextBlock _blockKpiTurnLoaded;
        private TextBlock _blockKpiTurnEmpty;
        private TextBlock _blockKpiCompMove;
        private TextBlock _blockKpiCompTurn;
        private TextBlock _blockKpiCompLift;
        private TextBlock _blockKpiCompWait;
        private TextBlock _blockKpiCompSupport;
        private TextBlock _blockKpiEnergyEmptyLoadedRatio;
        // Per-bot dynamic blocks
        private Dictionary<IBotInfo, TextBlock> _blocksBotLoad = new Dictionary<IBotInfo, TextBlock>();
        private Dictionary<IBotInfo, TextBlock> _blocksBotEnergy = new Dictionary<IBotInfo, TextBlock>();
        private Dictionary<IBotInfo, TextBlock> _blocksBotOrders = new Dictionary<IBotInfo, TextBlock>();
        private Dictionary<IBotInfo, TextBlock> _blocksBotDistance = new Dictionary<IBotInfo, TextBlock>();
        private Dictionary<IBotInfo, TextBlock> _blocksBotState = new Dictionary<IBotInfo, TextBlock>();
        private Dictionary<IInputStationInfo, TextBlock> _blocksAssignedBundles = new Dictionary<IInputStationInfo, TextBlock>();
        private Dictionary<IOutputStationInfo, TextBlock> _blocksAssignedOrders = new Dictionary<IOutputStationInfo, TextBlock>();
        private Dictionary<IInputStationInfo, TextBlock> _blocksOpenInsertRequests = new Dictionary<IInputStationInfo, TextBlock>();
        private Dictionary<IOutputStationInfo, TextBlock> _blocksOpenExtractRequests = new Dictionary<IOutputStationInfo, TextBlock>();
        private Dictionary<IOutputStationInfo, TextBlock> _blocksOpenQueuedExtractRequests = new Dictionary<IOutputStationInfo, TextBlock>();
        private Dictionary<IOutputStationInfo, TextBlock> _blocksActiveOStations = new Dictionary<IOutputStationInfo, TextBlock>();
        private Dictionary<IInputStationInfo, TextBlock> _blocksActiveIStations = new Dictionary<IInputStationInfo, TextBlock>();
        private Dictionary<IOutputStationInfo, TextBlock> _blocksBlockedOStations = new Dictionary<IOutputStationInfo, TextBlock>();
        private Dictionary<IInputStationInfo, TextBlock> _blocksBlockedIStations = new Dictionary<IInputStationInfo, TextBlock>();
        private SimulationVisualOrderManager _orderManager;

        public SimulationInfoInstance(TreeView infoHost, IInstanceInfo instance) : base(infoHost) { _instance = instance; }

        public override void InfoPanelUpdate()
        {
            // Update all info
            _blockPendingBundles.Text = _instance.GetInfoItemManager().GetInfoPendingBundleCount().ToString();
            _blockPendingOrders.Text = _instance.GetInfoItemManager().GetInfoPendingOrderCount().ToString();
            _blockStatItems.Text = _instance.GetInfoStatItemsHandled().ToString();
            _blockStatBundles.Text = _instance.GetInfoStatBundlesHandled().ToString();
            _blockStatOrders.Text = _instance.GetInfoStatOrdersHandled().ToString();
            _blockStatOrdersLate.Text = _instance.GetInfoStatOrdersLate().ToString();
            _blockStatRepositioningMoves.Text = _instance.GetInfoStatRepositioningMoves().ToString();
            _blockStatCollisions.Text = _instance.GetInfoStatCollisions().ToString();
            _blockStatStorageFillLevel.Text =
                (_instance.GetInfoStatStorageFillLevel() * 100).ToString("F1", IOConstants.FORMATTER) + "% " +
                (_instance.GetInfoStatStorageFillAndReservedLevel() * 100).ToString("F1", IOConstants.FORMATTER) + "% " +
                (_instance.GetInfoStatStorageFillAndReservedAndBacklogLevel() * 100).ToString("F1", IOConstants.FORMATTER) + "%";
            foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
            {
                _blocksAssignedBundles[station].Text = station.GetInfoAssignedBundles().ToString();
                _blocksOpenInsertRequests[station].Text = station.GetInfoOpenRequests().ToString() + "/" + station.GetInfoOpenBundles();
            }
            foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
            {
                _blocksAssignedOrders[station].Text = station.GetInfoAssignedOrders().ToString();
                _blocksOpenExtractRequests[station].Text = station.GetInfoOpenRequests().ToString() + "/" + station.GetInfoOpenItems();
                _blocksOpenQueuedExtractRequests[station].Text = station.GetInfoOpenQueuedRequests().ToString() + "/" + station.GetInfoOpenQueuedItems();
            }
            foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
                if (station.GetInfoActive() && _blocksActiveIStations[station].Background != _colorStationActive)
                    _blocksActiveIStations[station].Background = _colorStationActive;
                else if (!station.GetInfoActive() && _blocksActiveIStations[station].Background != _colorStationInactive)
                    _blocksActiveIStations[station].Background = _colorStationInactive;
            foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                if (station.GetInfoActive() && _blocksActiveOStations[station].Background != _colorStationActive)
                    _blocksActiveOStations[station].Background = _colorStationActive;
                else if (!station.GetInfoActive() && _blocksActiveOStations[station].Background != _colorStationInactive)
                    _blocksActiveOStations[station].Background = _colorStationInactive;
            foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
                if (station.GetInfoBlocked() && _blocksBlockedIStations[station].Background != _colorStationBlocked)
                    _blocksBlockedIStations[station].Background = _colorStationBlocked;
                else if (!station.GetInfoBlocked() && _blocksBlockedIStations[station].Background != _colorStationFree)
                    _blocksBlockedIStations[station].Background = _colorStationFree;
            foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                if (station.GetInfoBlocked() && _blocksBlockedOStations[station].Background != _colorStationBlocked)
                    _blocksBlockedOStations[station].Background = _colorStationBlocked;
                else if (!station.GetInfoBlocked() && _blocksBlockedOStations[station].Background != _colorStationFree)
                    _blocksBlockedOStations[station].Background = _colorStationFree;
            _orderManager.Update(_instance.GetInfoItemManager().GetInfoOpenOrders(), _instance.GetInfoItemManager().GetInfoCompletedOrders());
            // Update per-bot stats
            foreach (var b in _instance.GetInfoBots())
            {
                if (b is RAWSimO.Core.Bots.BotNormal bn)
                {
                    if (_blocksBotLoad.ContainsKey(b))
                        _blocksBotLoad[b].Text = bn.CurrentTotalMassKg.ToString("F0", IOConstants.FORMATTER) + "kg";
                    if (_blocksBotEnergy.ContainsKey(b))
                        _blocksBotEnergy[b].Text = (bn.StatEnergyTotalJ / 1000.0).ToString("F1", IOConstants.FORMATTER) + "kJ";
                    if (_blocksBotOrders.ContainsKey(b))
                        _blocksBotOrders[b].Text = bn.StatOrdersCompleted.ToString();
                    if (_blocksBotDistance.ContainsKey(b))
                        _blocksBotDistance[b].Text = bn.StatDistanceTraveledM.ToString("F0", IOConstants.FORMATTER) + "m";
                    if (_blocksBotState.ContainsKey(b))
                        _blocksBotState[b].Text = b.GetInfoState();
                }
            }
            // Update fleet energy stats (sum over all BotNormal instances)
            if (_blockStatEnergyTotal != null)
            {
                double sumTotal = 0, sumE1 = 0, sumE2 = 0, sumE3 = 0, sumE4 = 0, sumE5 = 0;
                int sumTurning = 0;
                foreach (var b in _instance.GetInfoBots())
                {
                    if (b is RAWSimO.Core.Bots.BotNormal bn)
                    {
                        sumTotal += bn.StatEnergyTotalJ;
                        sumE1 += bn.StatEnergyE1AccelJ;
                        sumE2 += bn.StatEnergyE2DecelJ;
                        sumE3 += bn.StatEnergyE3CruiseJ;
                        sumE4 += bn.StatEnergyE4RotationJ;
                        sumE5 += bn.StatEnergyE5LiftLowerJ;
                        sumTurning += bn.StatTurningCount;
                    }
                }
                _blockStatEnergyTotal.Text = (sumTotal / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ";
                _blockStatEnergyE1.Text = (sumE1 / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ";
                _blockStatEnergyE2.Text = (sumE2 / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ";
                _blockStatEnergyE3.Text = (sumE3 / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ";
                _blockStatEnergyE4.Text = (sumE4 / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ";
                _blockStatEnergyE5.Text = (sumE5 / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ";
                if (_blockStatTurningCount != null) _blockStatTurningCount.Text = sumTurning.ToString();
            }
            // Fleet KPI (Layer 1 / 6) update
            if (_blockKpiStationArrivals != null)
            {
                double moveE = 0, turnE = 0, waitE = 0, liftE = 0, supportTotalE = 0, mechE = 0;
                double distLoaded = 0, distEmpty = 0;
                double waitTimeLoaded = 0, waitTimeEmpty = 0;
                double eLoaded = 0, eEmpty = 0;
                int orders = _instance.GetInfoStatOrdersHandled();
                int stations = 0, tripLoaded = 0, tripEmpty = 0;
                int turnLoaded = 0, turnEmpty = 0;
                double totalDist = 0;
                foreach (var b in _instance.GetInfoBots())
                {
                    if (b is RAWSimO.Core.Bots.BotNormal bn)
                    {
                        stations += bn.StatOutputStationArrivals;
                        totalDist += bn.StatDistanceTraveledM;
                        distLoaded += bn.StatLoadedDistanceM;
                        distEmpty += bn.StatEmptyDistanceM;
                        tripLoaded += bn.StatTripCountLoaded;
                        tripEmpty += bn.StatTripCountEmpty;
                        waitTimeLoaded += bn.StatWaitTimeLoadedSec;
                        waitTimeEmpty += bn.StatWaitTimeEmptySec;
                        turnLoaded += bn.StatLoadedTurningCount;
                        turnEmpty += bn.StatEmptyTurningCount;
                        moveE += bn.StatMoveEnergyEmptyJ + bn.StatMoveEnergyLoadedJ;
                        turnE += bn.StatTurnEnergyEmptyJ + bn.StatTurnEnergyLoadedJ;
                        waitE += bn.StatWaitEnergyLoadedJ + bn.StatWaitEnergyEmptyJ;
                        liftE += bn.StatEnergyE5LiftLowerJ;
                        supportTotalE += bn.StatESupportJ;
                        mechE += bn.StatEnergyTotalJ;
                        eLoaded += bn.StatMoveEnergyLoadedJ + bn.StatTurnEnergyLoadedJ + bn.StatWaitEnergyLoadedJ;
                        eEmpty  += bn.StatMoveEnergyEmptyJ  + bn.StatTurnEnergyEmptyJ  + bn.StatWaitEnergyEmptyJ;
                    }
                }
                // 5-component composition: move+turn+lift+wait+support = E_mech + E_support
                double supportE = supportTotalE - waitE;
                double comp5 = moveE + turnE + liftE + waitE + supportE;
                _blockKpiStationArrivals.Text = stations.ToString();
                _blockKpiTotalDistance.Text = totalDist.ToString("F0", IOConstants.FORMATTER) + " m";
                _blockKpiLoadedDistance.Text = distLoaded.ToString("F0", IOConstants.FORMATTER) + " m";
                _blockKpiEmptyDistance.Text = distEmpty.ToString("F0", IOConstants.FORMATTER) + " m";
                _blockKpiEnergyPerOrder.Text = orders > 0 ? (mechE / orders / 1000.0).ToString("F2", IOConstants.FORMATTER) + " kJ" : "-";
                if (_blockKpiThroughput != null)
                    _blockKpiThroughput.Text = _instance.GetInfoStatThroughput().ToString("F1", IOConstants.FORMATTER) + " ord/h";
                if (_blockKpiOrderDistance != null)
                    _blockKpiOrderDistance.Text = _instance.GetInfoStatOrderDistance().ToString("F1", IOConstants.FORMATTER) + " m";
                if (_blockKpiOrderPileOn != null)
                    _blockKpiOrderPileOn.Text = _instance.GetInfoStatOrderPileOn().ToString("F3", IOConstants.FORMATTER);
                _blockKpiTripLoaded.Text = tripLoaded.ToString();
                _blockKpiTripEmpty.Text = tripEmpty.ToString();
                _blockKpiWaitTimeLoaded.Text = waitTimeLoaded.ToString("F0", IOConstants.FORMATTER) + " s";
                _blockKpiWaitTimeEmpty.Text = waitTimeEmpty.ToString("F0", IOConstants.FORMATTER) + " s";
                _blockKpiTurnLoaded.Text = turnLoaded.ToString();
                _blockKpiTurnEmpty.Text = turnEmpty.ToString();
                if (comp5 > 0)
                {
                    _blockKpiCompMove.Text    = (moveE    / comp5 * 100).ToString("F1", IOConstants.FORMATTER) + "%";
                    _blockKpiCompTurn.Text    = (turnE    / comp5 * 100).ToString("F1", IOConstants.FORMATTER) + "%";
                    _blockKpiCompLift.Text    = (liftE    / comp5 * 100).ToString("F1", IOConstants.FORMATTER) + "%";
                    _blockKpiCompWait.Text    = (waitE    / comp5 * 100).ToString("F1", IOConstants.FORMATTER) + "%";
                    _blockKpiCompSupport.Text = (supportE / comp5 * 100).ToString("F1", IOConstants.FORMATTER) + "%";
                }
                _blockKpiEnergyEmptyLoadedRatio.Text = eLoaded > 0
                    ? (eEmpty / eLoaded).ToString("F3", IOConstants.FORMATTER)
                    : "-";
            }
        }

        public override void InfoPanelInit()
        {
            // Prepare information controls
            _infoHost.Items.Clear();
            // Init if not already done
            if (_root == null)
            {
                // Init root node
                _root = new TreeViewItem { Header = "Instance" };
                // --> Add static information
                // Add bot count info
                WrapPanel botCountPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                botCountPanel.Children.Add(new TextBlock { Text = "Bots: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botCountPanel.Children.Add(new TextBlock { Text = _instance.GetInfoBots().Count().ToString(), MinWidth = _infoPanelRightColumnWidth });
                _root.Items.Add(botCountPanel);
                // Add pod count info
                WrapPanel podCountPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                podCountPanel.Children.Add(new TextBlock { Text = "Pods: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                podCountPanel.Children.Add(new TextBlock { Text = _instance.GetInfoPods().Count().ToString(), MinWidth = _infoPanelRightColumnWidth });
                _root.Items.Add(podCountPanel);
                // Add input station count info
                WrapPanel iStationCountPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                iStationCountPanel.Children.Add(new TextBlock { Text = "Input-stations: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                iStationCountPanel.Children.Add(new TextBlock { Text = _instance.GetInfoTiers().Sum(t => t.GetInfoInputStations().Count()).ToString(), MinWidth = _infoPanelRightColumnWidth });
                _root.Items.Add(iStationCountPanel);
                // Add output station count info
                WrapPanel oStationCountPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                oStationCountPanel.Children.Add(new TextBlock { Text = "Output-stations: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                oStationCountPanel.Children.Add(new TextBlock { Text = _instance.GetInfoTiers().Sum(t => t.GetInfoOutputStations().Count()).ToString(), MinWidth = _infoPanelRightColumnWidth });
                _root.Items.Add(oStationCountPanel);
                // Add waypoint count info
                WrapPanel waypointCountPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                waypointCountPanel.Children.Add(new TextBlock { Text = "Waypoints: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                waypointCountPanel.Children.Add(new TextBlock { Text = _instance.GetInfoTiers().Sum(t => t.GetInfoWaypoints().Count()).ToString(), MinWidth = _infoPanelRightColumnWidth });
                _root.Items.Add(waypointCountPanel);
                // Add tier sizes info
                WrapPanel tierSizesPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                tierSizesPanel.Children.Add(new TextBlock { Text = "Tier sizes (LxW): ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                tierSizesPanel.Children.Add(new TextBlock { Text = string.Join(",", _instance.GetInfoTiers().Select(t => $"{t.GetInfoLength():F0}x{t.GetInfoWidth():F0}")), MinWidth = _infoPanelRightColumnWidth });
                _root.Items.Add(tierSizesPanel);
                // --> Add dynamic information
                // Add pending bundles info
                WrapPanel pendingBundlesPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                pendingBundlesPanel.Children.Add(new TextBlock { Text = "Bundles (pending): ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockPendingBundles = new TextBlock { Text = _instance.GetInfoItemManager().GetInfoPendingBundleCount().ToString(), MinWidth = _infoPanelRightColumnWidth };
                pendingBundlesPanel.Children.Add(_blockPendingBundles);
                _root.Items.Add(pendingBundlesPanel);
                // Add pending orders info
                WrapPanel pendingOrdersPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                pendingOrdersPanel.Children.Add(new TextBlock { Text = "Orders (pending): ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockPendingOrders = new TextBlock { Text = _instance.GetInfoItemManager().GetInfoPendingOrderCount().ToString(), MinWidth = _infoPanelRightColumnWidth };
                pendingOrdersPanel.Children.Add(_blockPendingOrders);
                _root.Items.Add(pendingOrdersPanel);
                // Add stats items handled
                WrapPanel itemsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                itemsPanel.Children.Add(new TextBlock { Text = "Items: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatItems = new TextBlock { Text = _instance.GetInfoStatItemsHandled().ToString(), MinWidth = _infoPanelRightColumnWidth };
                itemsPanel.Children.Add(_blockStatItems);
                _root.Items.Add(itemsPanel);
                // Add stats bundles handled
                WrapPanel bundlesPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                bundlesPanel.Children.Add(new TextBlock { Text = "Bundles: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatBundles = new TextBlock { Text = _instance.GetInfoStatBundlesHandled().ToString(), MinWidth = _infoPanelRightColumnWidth };
                bundlesPanel.Children.Add(_blockStatBundles);
                _root.Items.Add(bundlesPanel);
                // Add stats orders handled
                WrapPanel ordersPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                ordersPanel.Children.Add(new TextBlock { Text = "Orders: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatOrders = new TextBlock { Text = _instance.GetInfoStatOrdersHandled().ToString(), MinWidth = _infoPanelRightColumnWidth };
                ordersPanel.Children.Add(_blockStatOrders);
                _root.Items.Add(ordersPanel);
                // Add stats orders handled
                WrapPanel ordersLatePanel = new WrapPanel { Orientation = Orientation.Horizontal };
                ordersLatePanel.Children.Add(new TextBlock { Text = "Orders (late): ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatOrdersLate = new TextBlock { Text = _instance.GetInfoStatOrdersLate().ToString(), MinWidth = _infoPanelRightColumnWidth };
                ordersLatePanel.Children.Add(_blockStatOrdersLate);
                _root.Items.Add(ordersLatePanel);
                // Add stats orders handled
                WrapPanel repositioningsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                repositioningsPanel.Children.Add(new TextBlock { Text = "Repositionings: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatRepositioningMoves = new TextBlock { Text = _instance.GetInfoStatRepositioningMoves().ToString(), MinWidth = _infoPanelRightColumnWidth };
                repositioningsPanel.Children.Add(_blockStatRepositioningMoves);
                _root.Items.Add(repositioningsPanel);
                // Add stats collisions occurred
                WrapPanel collisionsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                collisionsPanel.Children.Add(new TextBlock { Text = "Collisions: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatCollisions = new TextBlock { Text = _instance.GetInfoStatCollisions().ToString(), MinWidth = _infoPanelRightColumnWidth };
                collisionsPanel.Children.Add(_blockStatCollisions);
                _root.Items.Add(collisionsPanel);
                // === Fleet KPI panel (Layer 1 / Layer 6) ===
                TreeViewItem kpiNode = new TreeViewItem { Header = "Fleet KPI (L1/L6)", IsExpanded = true };
                System.Func<string, TextBlock, WrapPanel> addKpiRow = (label, tb) =>
                {
                    var p = new WrapPanel { Orientation = Orientation.Horizontal };
                    p.Children.Add(new TextBlock { Text = label, TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                    p.Children.Add(tb);
                    return p;
                };
                _blockKpiStationArrivals = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Output arrivals: ", _blockKpiStationArrivals));
                _blockKpiTotalDistance = new TextBlock { Text = "0 m", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Total distance: ", _blockKpiTotalDistance));
                _blockKpiLoadedDistance = new TextBlock { Text = "0 m", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("  loaded: ", _blockKpiLoadedDistance));
                _blockKpiEmptyDistance = new TextBlock { Text = "0 m", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("  empty:  ", _blockKpiEmptyDistance));
                _blockKpiEnergyPerOrder = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Energy/order: ", _blockKpiEnergyPerOrder));
                _blockKpiThroughput = new TextBlock { Text = "0 ord/h", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Throughput: ", _blockKpiThroughput));
                _blockKpiOrderDistance = new TextBlock { Text = "0 m", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Dist/order: ", _blockKpiOrderDistance));
                _blockKpiOrderPileOn = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Order pile-on: ", _blockKpiOrderPileOn));
                _blockKpiTripLoaded = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Trips loaded: ", _blockKpiTripLoaded));
                _blockKpiTripEmpty = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Trips empty:  ", _blockKpiTripEmpty));
                _blockKpiTurnLoaded = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Turn loaded:  ", _blockKpiTurnLoaded));
                _blockKpiTurnEmpty = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Turn empty:   ", _blockKpiTurnEmpty));
                _blockKpiWaitTimeLoaded = new TextBlock { Text = "0 s", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Wait loaded: ", _blockKpiWaitTimeLoaded));
                _blockKpiWaitTimeEmpty = new TextBlock { Text = "0 s", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("Wait empty:  ", _blockKpiWaitTimeEmpty));
                _blockKpiCompMove = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("E move %:    ", _blockKpiCompMove));
                _blockKpiCompTurn = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("E turn %:    ", _blockKpiCompTurn));
                _blockKpiCompLift = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("E lift %:    ", _blockKpiCompLift));
                _blockKpiCompWait = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("E wait %:    ", _blockKpiCompWait));
                _blockKpiCompSupport = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("E support %: ", _blockKpiCompSupport));
                _blockKpiEnergyEmptyLoadedRatio = new TextBlock { Text = "-", MinWidth = _infoPanelRightColumnWidth };
                kpiNode.Items.Add(addKpiRow("E empty/loaded: ", _blockKpiEnergyEmptyLoadedRatio));
                _root.Items.Add(kpiNode);

                // Add fleet energy stats (Rizqi model)
                TreeViewItem energyNode = new TreeViewItem { Header = "Fleet Energy (Rizqi)" };
                // Total
                WrapPanel eTotalPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                eTotalPanel.Children.Add(new TextBlock { Text = "Total: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatEnergyTotal = new TextBlock { Text = "0.00 kJ", MinWidth = _infoPanelRightColumnWidth };
                eTotalPanel.Children.Add(_blockStatEnergyTotal);
                energyNode.Items.Add(eTotalPanel);
                // E1
                WrapPanel eE1Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                eE1Panel.Children.Add(new TextBlock { Text = "E1 Accel: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatEnergyE1 = new TextBlock { Text = "0.00 kJ", MinWidth = _infoPanelRightColumnWidth };
                eE1Panel.Children.Add(_blockStatEnergyE1);
                energyNode.Items.Add(eE1Panel);
                // E2
                WrapPanel eE2Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                eE2Panel.Children.Add(new TextBlock { Text = "E2 Decel: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatEnergyE2 = new TextBlock { Text = "0.00 kJ", MinWidth = _infoPanelRightColumnWidth };
                eE2Panel.Children.Add(_blockStatEnergyE2);
                energyNode.Items.Add(eE2Panel);
                // E3
                WrapPanel eE3Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                eE3Panel.Children.Add(new TextBlock { Text = "E3 Cruise: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatEnergyE3 = new TextBlock { Text = "0.00 kJ", MinWidth = _infoPanelRightColumnWidth };
                eE3Panel.Children.Add(_blockStatEnergyE3);
                energyNode.Items.Add(eE3Panel);
                // E4
                WrapPanel eE4Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                eE4Panel.Children.Add(new TextBlock { Text = "E4 Rotation: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatEnergyE4 = new TextBlock { Text = "0.00 kJ", MinWidth = _infoPanelRightColumnWidth };
                eE4Panel.Children.Add(_blockStatEnergyE4);
                energyNode.Items.Add(eE4Panel);
                // E5
                WrapPanel eE5Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                eE5Panel.Children.Add(new TextBlock { Text = "E5 Lift/Low: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatEnergyE5 = new TextBlock { Text = "0.00 kJ", MinWidth = _infoPanelRightColumnWidth };
                eE5Panel.Children.Add(_blockStatEnergyE5);
                energyNode.Items.Add(eE5Panel);
                // Turning count
                WrapPanel turningPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                turningPanel.Children.Add(new TextBlock { Text = "Turning: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatTurningCount = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                turningPanel.Children.Add(_blockStatTurningCount);
                energyNode.Items.Add(turningPanel);
                _root.Items.Add(energyNode);
                // Per-bot stats panel
                TreeViewItem botStatsNode = new TreeViewItem { Header = "Per-Bot Stats" };
                var allBots = _instance.GetInfoBots().ToList();
                // Row: Load
                StackPanel botLoadPanel = new StackPanel { Orientation = Orientation.Horizontal };
                botLoadPanel.Children.Add(new TextBlock { Text = "Load:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botLoadPanel.Children.Add(new TextBlock { MinWidth = 4 });
                foreach (var b in allBots)
                {
                    var block = new TextBlock { Text = "?", TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MinWidth = 60 };
                    _blocksBotLoad[b] = block;
                    botLoadPanel.Children.Add(block);
                }
                botStatsNode.Items.Add(botLoadPanel);
                // Row: Energy
                StackPanel botEnergyPanel = new StackPanel { Orientation = Orientation.Horizontal };
                botEnergyPanel.Children.Add(new TextBlock { Text = "Energy:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botEnergyPanel.Children.Add(new TextBlock { MinWidth = 4 });
                foreach (var b in allBots)
                {
                    var block = new TextBlock { Text = "0kJ", TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MinWidth = 60 };
                    _blocksBotEnergy[b] = block;
                    botEnergyPanel.Children.Add(block);
                }
                botStatsNode.Items.Add(botEnergyPanel);
                // Row: Orders
                StackPanel botOrdersPanel = new StackPanel { Orientation = Orientation.Horizontal };
                botOrdersPanel.Children.Add(new TextBlock { Text = "Orders:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botOrdersPanel.Children.Add(new TextBlock { MinWidth = 4 });
                foreach (var b in allBots)
                {
                    var block = new TextBlock { Text = "0", TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MinWidth = 60 };
                    _blocksBotOrders[b] = block;
                    botOrdersPanel.Children.Add(block);
                }
                botStatsNode.Items.Add(botOrdersPanel);
                // Row: Distance
                StackPanel botDistPanel = new StackPanel { Orientation = Orientation.Horizontal };
                botDistPanel.Children.Add(new TextBlock { Text = "Distance:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botDistPanel.Children.Add(new TextBlock { MinWidth = 4 });
                foreach (var b in allBots)
                {
                    var block = new TextBlock { Text = "0m", TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MinWidth = 60 };
                    _blocksBotDistance[b] = block;
                    botDistPanel.Children.Add(block);
                }
                botStatsNode.Items.Add(botDistPanel);
                // Row: State
                StackPanel botStatePanel = new StackPanel { Orientation = Orientation.Horizontal };
                botStatePanel.Children.Add(new TextBlock { Text = "State:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botStatePanel.Children.Add(new TextBlock { MinWidth = 4 });
                foreach (var b in allBots)
                {
                    var block = new TextBlock { Text = "?", TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MinWidth = 60 };
                    _blocksBotState[b] = block;
                    botStatePanel.Children.Add(block);
                }
                botStatsNode.Items.Add(botStatePanel);
                // Header row with bot IDs
                StackPanel botIdPanel = new StackPanel { Orientation = Orientation.Horizontal };
                botIdPanel.Children.Add(new TextBlock { Text = "Bot ID:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                botIdPanel.Children.Add(new TextBlock { MinWidth = 4 });
                foreach (var b in allBots)
                    botIdPanel.Children.Add(new TextBlock { Text = "B" + b.GetInfoID(), TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MinWidth = 60, FontWeight = System.Windows.FontWeights.Bold });
                botStatsNode.Items.Insert(0, botIdPanel);
                _root.Items.Add(botStatsNode);

                // Add stats storage fill level
                WrapPanel storageFillLevelPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                storageFillLevelPanel.Children.Add(new TextBlock { Text = "Storage fill level: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStatStorageFillLevel = new TextBlock
                {
                    Text =
                        (_instance.GetInfoStatStorageFillLevel() * 100).ToString("F1", IOConstants.FORMATTER) + "% " +
                        (_instance.GetInfoStatStorageFillAndReservedLevel() * 100).ToString("F1", IOConstants.FORMATTER) + "% " +
                        (_instance.GetInfoStatStorageFillAndReservedAndBacklogLevel() * 100).ToString("F1", IOConstants.FORMATTER) + "%",
                    MinWidth = _infoPanelRightColumnWidth
                };
                storageFillLevelPanel.Children.Add(_blockStatStorageFillLevel);
                _root.Items.Add(storageFillLevelPanel);

                // Add request info
                StackPanel insertRequestsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                insertRequestsPanel.Children.Add(new TextBlock { Text = "Ass. i-requests:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                insertRequestsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
                {
                    TextBlock block = new TextBlock
                    {
                        Text = station.GetInfoOpenRequests().ToString() + "/" + station.GetInfoOpenBundles(),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0),
                    };
                    _blocksOpenInsertRequests[station] = block;
                    insertRequestsPanel.Children.Add(block);
                }
                _root.Items.Add(insertRequestsPanel);
                StackPanel extractRequestsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                extractRequestsPanel.Children.Add(new TextBlock { Text = "Ass. e-requests:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                extractRequestsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                {
                    TextBlock block = new TextBlock
                    {
                        Text = station.GetInfoOpenRequests().ToString() + "/" + station.GetInfoOpenItems(),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0),
                    };
                    _blocksOpenExtractRequests[station] = block;
                    extractRequestsPanel.Children.Add(block);
                }
                _root.Items.Add(extractRequestsPanel);
                StackPanel queuedExtractRequestsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                queuedExtractRequestsPanel.Children.Add(new TextBlock { Text = "Queued e-requests:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                queuedExtractRequestsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                {
                    TextBlock block = new TextBlock
                    {
                        Text = station.GetInfoOpenQueuedRequests().ToString() + "/" + station.GetInfoOpenQueuedItems(),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0),
                    };
                    _blocksOpenQueuedExtractRequests[station] = block;
                    queuedExtractRequestsPanel.Children.Add(block);
                }
                _root.Items.Add(queuedExtractRequestsPanel);

                // Add assigned bundles and orders info
                StackPanel assignedBundlesPanel = new StackPanel { Orientation = Orientation.Horizontal };
                assignedBundlesPanel.Children.Add(new TextBlock { Text = "Ass. bundles:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                assignedBundlesPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
                {
                    TextBlock stationAssignedBlock = new TextBlock
                    {
                        Text = station.GetInfoAssignedBundles().ToString(),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0),
                    };
                    _blocksAssignedBundles[station] = stationAssignedBlock;
                    assignedBundlesPanel.Children.Add(stationAssignedBlock);
                }
                _root.Items.Add(assignedBundlesPanel);
                StackPanel assignedOrdersPanel = new StackPanel { Orientation = Orientation.Horizontal };
                assignedOrdersPanel.Children.Add(new TextBlock { Text = "Ass. orders:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                assignedOrdersPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                {
                    TextBlock stationAssignedBlock = new TextBlock
                    {
                        Text = station.GetInfoAssignedOrders().ToString(),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0),
                    };
                    _blocksAssignedOrders[station] = stationAssignedBlock;
                    assignedOrdersPanel.Children.Add(stationAssignedBlock);
                }
                _root.Items.Add(assignedOrdersPanel);

                // Add active stations info
                StackPanel activeIStationsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                activeIStationsPanel.Children.Add(new TextBlock { Text = "Input-stations:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                activeIStationsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
                {
                    TextBlock stationActiveBlock = new TextBlock
                    {
                        Text = station.GetInfoID().ToString(),
                        TextAlignment = TextAlignment.Center,
                        MinWidth = _infoPanelSingleSmallElementWidth,
                        Background = Brushes.Gray
                    };
                    _blocksActiveIStations[station] = stationActiveBlock;
                    activeIStationsPanel.Children.Add(stationActiveBlock);
                }
                _root.Items.Add(activeIStationsPanel);
                StackPanel activeOStationsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                activeOStationsPanel.Children.Add(new TextBlock { Text = "Output-stations:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                activeOStationsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                {
                    TextBlock stationActiveBlock = new TextBlock
                    {
                        Text = station.GetInfoID().ToString(),
                        TextAlignment = TextAlignment.Center,
                        MinWidth = _infoPanelSingleSmallElementWidth,
                        Background = Brushes.Gray
                    };
                    _blocksActiveOStations[station] = stationActiveBlock;
                    activeOStationsPanel.Children.Add(stationActiveBlock);
                }
                _root.Items.Add(activeOStationsPanel);

                // Add blocked stations info
                StackPanel blockedIStationsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                blockedIStationsPanel.Children.Add(new TextBlock { Text = "Input-stations:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                blockedIStationsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoInputStations()))
                {
                    TextBlock stationBlockedBlock = new TextBlock
                    {
                        Text = station.GetInfoID().ToString(),
                        TextAlignment = TextAlignment.Center,
                        MinWidth = _infoPanelSingleSmallElementWidth,
                        Background = Brushes.Gray
                    };
                    _blocksBlockedIStations[station] = stationBlockedBlock;
                    blockedIStationsPanel.Children.Add(stationBlockedBlock);
                }
                _root.Items.Add(blockedIStationsPanel);
                StackPanel blockedOStationsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                blockedOStationsPanel.Children.Add(new TextBlock { Text = "Output-stations:", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                blockedOStationsPanel.Children.Add(new TextBlock { MinWidth = 2 });
                foreach (var station in _instance.GetInfoTiers().SelectMany(t => t.GetInfoOutputStations()))
                {
                    TextBlock stationBlockedBlock = new TextBlock
                    {
                        Text = station.GetInfoID().ToString(),
                        TextAlignment = TextAlignment.Center,
                        MinWidth = _infoPanelSingleSmallElementWidth,
                        Background = Brushes.Gray
                    };
                    _blocksBlockedOStations[station] = stationBlockedBlock;
                    blockedOStationsPanel.Children.Add(stationBlockedBlock);
                }
                _root.Items.Add(blockedOStationsPanel);

                // Init order list root nodes
                TreeViewItem openOrderListItem = new TreeViewItem { };
                TreeViewItem completedOrderListItem = new TreeViewItem { IsExpanded = true };
                _root.Items.Add(openOrderListItem);
                _root.Items.Add(completedOrderListItem);
                // Add order list
                _orderManager = new SimulationVisualOrderManager(openOrderListItem, "OpenOrders", completedOrderListItem, "CompleteOrders", 40);
                _orderManager.Update(_instance.GetInfoItemManager().GetInfoOpenOrders(), _instance.GetInfoItemManager().GetInfoCompletedOrders());
            }
            // Expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion

    #region InputStation

    public class SimulationInfoInputStation : SimulationInfoObject
    {
        private readonly IInputStationInfo _iStation;
        private readonly int _infoPanelLeftColumnWidth = 105;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TreeViewItem _root;
        private TreeViewItem _treeItemContent;
        private TextBlock _blockXY;
        private TextBlock _blockCapacity;
        private TextBlock _blockCapacityReserved;
        private TextBlock _blockActivationOrderID;
        private TextBlock _blockQueueCapacity;
        private TextBlock _blockBlocked;
        private TextBlock _blockBlockedLeft;
        private TextBlock _blockOpenRequests;
        private SimulationVisualBundleManager _bundleManager;

        public SimulationInfoInputStation(TreeView infoHost, IInputStationInfo iStation) : base(infoHost) { _iStation = iStation; }

        public override void InfoPanelUpdate()
        {
            // Update all info (no need to update x/y - it's not changeable)
            _blockCapacity.Text =
                _iStation.GetInfoCapacityUsed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                _iStation.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            _blockCapacityReserved.Text =
                _iStation.GetInfoCapacityReserved().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                _iStation.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            double blockedUntil = _iStation.GetInfoBlockedLeft();
            _blockBlockedLeft.Text = double.IsNaN(blockedUntil) || double.IsPositiveInfinity(blockedUntil) || blockedUntil < 0 ? "n/a" : TimeSpan.FromSeconds(blockedUntil).ToString(IOConstants.TIMESPAN_FORMAT_HUMAN_READABLE_MINUTES);
            _blockOpenRequests.Text = _iStation.GetInfoOpenRequests().ToString() + " / " + _iStation.GetInfoOpenBundles().ToString();
            // Update content info
            _bundleManager.UpdateContentInfo(_iStation.GetInfoBundles().ToArray());
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            // Init if not already done
            if (_root == null)
            {
                // Init root node
                _root = new TreeViewItem { Header = "InputStation" + _iStation.GetInfoID() };
                // Add position
                WrapPanel xyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                xyPanel.Children.Add(new TextBlock { Text = "X/Y: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockXY = new TextBlock
                {
                    Text =
                    _iStation.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                    _iStation.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth
                };
                xyPanel.Children.Add(_blockXY);
                _root.Items.Add(xyPanel);
                // Add capacity
                WrapPanel capacityPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                capacityPanel.Children.Add(new TextBlock { Text = "Capacity: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockCapacity = new TextBlock
                {
                    Text = _iStation.GetInfoCapacityUsed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                    _iStation.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                capacityPanel.Children.Add(_blockCapacity);
                _root.Items.Add(capacityPanel);
                // Add reserved capacity
                WrapPanel capacityReservedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                capacityReservedPanel.Children.Add(new TextBlock { Text = "Capacity Reserved: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockCapacityReserved = new TextBlock
                {
                    Text = _iStation.GetInfoCapacityReserved().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                    _iStation.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                capacityReservedPanel.Children.Add(_blockCapacityReserved);
                _root.Items.Add(capacityReservedPanel);
                // Add activation ID
                WrapPanel activationPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                activationPanel.Children.Add(new TextBlock { Text = "Activation ID: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockActivationOrderID = new TextBlock
                {
                    Text = _iStation.GetInfoActivationOrderID().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                activationPanel.Children.Add(_blockActivationOrderID);
                _root.Items.Add(activationPanel);
                // Add Queue
                WrapPanel queuePanel = new WrapPanel { Orientation = Orientation.Horizontal };
                queuePanel.Children.Add(new TextBlock { Text = "Queue: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockQueueCapacity = new TextBlock
                {
                    Text = _iStation.GetInfoQueue(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                queuePanel.Children.Add(_blockQueueCapacity);
                _root.Items.Add(queuePanel);
                // Add blocked status
                WrapPanel blockedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                blockedPanel.Children.Add(new TextBlock { Text = "Blocked: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBlocked = new TextBlock
                {
                    Text = _iStation.GetInfoBlocked().ToString(),
                    MinWidth = _infoPanelRightColumnWidth
                };
                blockedPanel.Children.Add(_blockBlocked);
                _root.Items.Add(blockedPanel);
                // Add blocked time remaining
                WrapPanel blockedUntilPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                blockedUntilPanel.Children.Add(new TextBlock { Text = "Blocked until: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBlockedLeft = new TextBlock
                {
                    Text = "n/a",
                    MinWidth = _infoPanelRightColumnWidth
                };
                blockedUntilPanel.Children.Add(_blockBlockedLeft);
                _root.Items.Add(blockedUntilPanel);
                // Add open requests
                WrapPanel openRequestsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                openRequestsPanel.Children.Add(new TextBlock { Text = "Requests: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockOpenRequests = new TextBlock
                {
                    Text = _iStation.GetInfoOpenRequests().ToString() + " / " + _iStation.GetInfoOpenBundles().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                openRequestsPanel.Children.Add(_blockOpenRequests);
                _root.Items.Add(openRequestsPanel);
                // Add content
                if (_treeItemContent == null)
                {
                    _treeItemContent = new TreeViewItem { Header = "Content" };
                    _treeItemContent.IsExpanded = true;
                    _bundleManager = new SimulationVisualBundleManager(_treeItemContent);
                }
                _root.Items.Add(_treeItemContent);
            }
            // Update content info
            _bundleManager.UpdateContentInfo(_iStation.GetInfoBundles().ToArray());
            // Expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion

    #region OutputStation

    public class SimulationInfoOutputStation : SimulationInfoObject
    {
        private readonly IOutputStationInfo _oStation;
        private readonly int _infoPanelLeftColumnWidth = 80;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TreeViewItem _root;
        private TextBlock _blockXY;
        private TextBlock _blockCapacity;
        private TextBlock _blockActivationOrderID;
        private TextBlock _blockQueueCapacity;
        private TextBlock _blockBlocked;
        private TextBlock _blockBlockedLeft;
        private TextBlock _blockOpenRequests;
        private TextBlock _blockInboundPods;
        private SimulationVisualOrderManager _orderManager;

        public SimulationInfoOutputStation(TreeView infoHost, IOutputStationInfo oStation) : base(infoHost) { _oStation = oStation; }

        public override void InfoPanelUpdate()
        {
            // Update all info (no need to update x/y - it's not changeable)
            _blockCapacity.Text =
                _oStation.GetInfoCapacityUsed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                _oStation.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            _blockBlocked.Text = _oStation.GetInfoBlocked().ToString();
            double blockedUntil = _oStation.GetInfoBlockedLeft();
            _blockBlockedLeft.Text = double.IsNaN(blockedUntil) || double.IsPositiveInfinity(blockedUntil) || blockedUntil < 0 ? "n/a" : TimeSpan.FromSeconds(blockedUntil).ToString(IOConstants.TIMESPAN_FORMAT_HUMAN_READABLE_MINUTES);
            _blockOpenRequests.Text = _oStation.GetInfoOpenRequests().ToString() + " / " + _oStation.GetInfoOpenItems().ToString();
            _blockInboundPods.Text = _oStation.GetInfoInboundPods().ToString();
            // Update content info
            _orderManager.Update(_oStation.GetInfoOpenOrders(), _oStation.GetInfoCompletedOrders());
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            // Init if not already done
            if (_root == null)
            {
                // Init root node
                _root = new TreeViewItem { Header = "OutputStation" + _oStation.GetInfoID() };
                // Add position
                WrapPanel xyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                xyPanel.Children.Add(new TextBlock { Text = "X/Y: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockXY = new TextBlock
                {
                    Text =
                        _oStation.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                        _oStation.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth
                };
                xyPanel.Children.Add(_blockXY);
                _root.Items.Add(xyPanel);
                // Add capacity
                WrapPanel capacityPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                capacityPanel.Children.Add(new TextBlock { Text = "Capacity: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockCapacity = new TextBlock
                {
                    Text =
                        _oStation.GetInfoCapacityUsed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                        _oStation.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth
                };
                capacityPanel.Children.Add(_blockCapacity);
                _root.Items.Add(capacityPanel);
                // Add activation ID
                WrapPanel activationPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                activationPanel.Children.Add(new TextBlock { Text = "Activation ID: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockActivationOrderID = new TextBlock
                {
                    Text = _oStation.GetInfoActivationOrderID().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                activationPanel.Children.Add(_blockActivationOrderID);
                _root.Items.Add(activationPanel);
                // Add Queue
                WrapPanel queuePanel = new WrapPanel { Orientation = Orientation.Horizontal };
                queuePanel.Children.Add(new TextBlock { Text = "Queue: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockQueueCapacity = new TextBlock
                {
                    Text = _oStation.GetInfoQueue(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                queuePanel.Children.Add(_blockQueueCapacity);
                _root.Items.Add(queuePanel);
                // Add blocked status
                WrapPanel blockedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                blockedPanel.Children.Add(new TextBlock { Text = "Blocked: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBlocked = new TextBlock
                {
                    Text = _oStation.GetInfoBlocked().ToString(),
                    MinWidth = _infoPanelRightColumnWidth
                };
                blockedPanel.Children.Add(_blockBlocked);
                _root.Items.Add(blockedPanel);
                // Add blocked time remaining
                WrapPanel blockedUntilPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                blockedUntilPanel.Children.Add(new TextBlock { Text = "Blocked until: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBlockedLeft = new TextBlock
                {
                    Text = "n/a",
                    MinWidth = _infoPanelRightColumnWidth
                };
                blockedUntilPanel.Children.Add(_blockBlockedLeft);
                _root.Items.Add(blockedUntilPanel);
                // Add open requests
                WrapPanel openRequestsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                openRequestsPanel.Children.Add(new TextBlock { Text = "Requests: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockOpenRequests = new TextBlock
                {
                    Text = _oStation.GetInfoOpenRequests().ToString() + " / " + _oStation.GetInfoOpenItems().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                openRequestsPanel.Children.Add(_blockOpenRequests);
                _root.Items.Add(openRequestsPanel);
                // Add open requests
                WrapPanel inboundPodsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                inboundPodsPanel.Children.Add(new TextBlock { Text = "Inbound pods: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockInboundPods = new TextBlock
                {
                    Text = "0",
                    MinWidth = _infoPanelRightColumnWidth,
                };
                inboundPodsPanel.Children.Add(_blockInboundPods);
                _root.Items.Add(inboundPodsPanel);
                // Init order list root nodes
                TreeViewItem openOrderListItem = new TreeViewItem { IsExpanded = true };
                TreeViewItem completedOrderListItem = new TreeViewItem { IsExpanded = true };
                _root.Items.Add(openOrderListItem);
                _root.Items.Add(completedOrderListItem);
                // Add order list
                _orderManager = new SimulationVisualOrderManager(openOrderListItem, "OpenOrders", completedOrderListItem, "CompleteOrders", 30);
                _orderManager.Update(_oStation.GetInfoOpenOrders(), _oStation.GetInfoCompletedOrders());
            }
            // Expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion

    #region ElevatorEntrance

    public class SimulationInfoElevatorEntrance : SimulationInfoObject
    {
        private readonly IElevatorInfo _elevatorInfo;
        private readonly int _infoPanelLeftColumnWidth = 80;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TreeViewItem _root;
        private TextBlock _blockWaypoint;

        public SimulationInfoElevatorEntrance(TreeView infoHost, IElevatorInfo elevatorInfo) : base(infoHost) { _elevatorInfo = elevatorInfo; }

        public override void InfoPanelUpdate()
        {
            // Update all info (no need to update x/y - it's not changeable)
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            // Init if not already done
            if (_root == null)
            {
                // Init root node
                _root = new TreeViewItem { Header = "Elevator" + _elevatorInfo.GetInfoID() };
                // Add waypoint
                WrapPanel waypointPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                waypointPanel.Children.Add(new TextBlock { Text = "Waypoints: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockWaypoint = new TextBlock
                {
                    Text = string.Join(",", _elevatorInfo.GetInfoWaypoints().Select(wp => wp.GetInfoID())),
                    MinWidth = _infoPanelRightColumnWidth
                };
                waypointPanel.Children.Add(_blockWaypoint);
                _root.Items.Add(waypointPanel);
                // Add entrance // TODO show coordinates of selected entrance
                //WrapPanel entrancePanel = new WrapPanel { Orientation = Orientation.Horizontal };
                //entrancePanel.Children.Add(new TextBlock { Text = "Entrance: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                //_blockEntrance = new TextBlock
                //{
                //    Text =
                //        _entranceInfo.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                //        _entranceInfo.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                //    MinWidth = _infoPanelRightColumnWidth
                //};
                //entrancePanel.Children.Add(_blockEntrance);
                //_root.Items.Add(entrancePanel);
            }
            // Expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion

    #region Bot

    public class SimulationInfoBot : SimulationInfoObject
    {
        private readonly IBotInfo _bot;
        private readonly int _infoPanelLeftColumnWidth = 80;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TextBlock _blockXY;
        private TextBlock _blockOrientation;
        private TextBlock _blockTargetOrientation;
        private TextBlock _blockCurrentWaypoint;
        private TextBlock _blockDestinationWaypoint;
        private TextBlock _blockGoalWaypoint;
        private TextBlock _blockSpeed;
        private TextBlock _blockBlocked;
        private TextBlock _blockBlockedUntil;
        private TextBlock _blockQueueing;
        private TextBlock _blockState;
        private TextBlock _blockPath;
        // Energy display fields (Rizqi model)
        private TextBlock _blockEnergyTotal;
        private TextBlock _blockEnergyE1;
        private TextBlock _blockEnergyE2;
        private TextBlock _blockEnergyE3;
        private TextBlock _blockEnergyE4;
        private TextBlock _blockEnergyE5;
        private TextBlock _blockBotTurning;
        // Per-bot personal stats (Lift up/down, orders, distance, wait, loaded distance/turning)
        private TextBlock _blockBotPickup;
        private TextBlock _blockBotSetdown;
        private TextBlock _blockBotOrders;
        private TextBlock _blockBotDistance;
        private TextBlock _blockBotLoadedDistance;
        private TextBlock _blockBotLoadedTurning;
        private TextBlock _blockBotWaitTime;
        private TextBlock _blockBotESupport;
        private TextBlock _blockBotESupportLoaded;
        private TextBlock _blockBotESupportEmpty;
        private TextBlock _blockBotIdleTime;
        private TextBlock _blockBotUtilization;
        private TextBlock _blockBotESupportRatio;
        // Move/turn energy split by loaded/empty
        private TextBlock _blockBotMoveEnergyEmpty;
        private TextBlock _blockBotMoveEnergyLoaded;
        private TextBlock _blockBotTurnEnergyEmpty;
        private TextBlock _blockBotTurnEnergyLoaded;
        // Per-trip tracking (added 2026-04-20)
        private TextBlock _blockBotTripCountLoaded;
        private TextBlock _blockBotTripCountEmpty;
        private TextBlock _blockBotWaitRatioLoadedMean;
        private TextBlock _blockBotWaitRatioEmptyMean;
        // Payload display: pod frame mass + cargo (item total weight). 0 when no pod carried.
        private TextBlock _blockPayload;

        public SimulationInfoBot(TreeView infoHost, IBotInfo bot) : base(infoHost)
        {
            _bot = bot;
            // DEBUG: Log POD_FRAME_MASS to verify it was loaded from xlayo
            System.Diagnostics.Debug.WriteLine(
                $"[SimulationInfoBot] Creating info panel for Bot{bot.GetInfoID()}, POD_FRAME_MASS={RAWSimO.Core.Metrics.EnergyConsumption.POD_FRAME_MASS} kg");
        }

        public override void InfoPanelUpdate()
        {
            // Update all info
            _blockXY.Text = _bot.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" + _bot.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            _blockOrientation.Text = Transformation2D.ProjectOrientation(_bot.GetInfoOrientation()).ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "°";
            _blockTargetOrientation.Text = Transformation2D.ProjectOrientation(_bot.GetInfoTargetOrientation()).ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "°";
            IWaypointInfo currentWP = _bot.GetInfoCurrentWaypoint(); IWaypointInfo destinationWP = _bot.GetInfoDestinationWaypoint(); IWaypointInfo goalWP = _bot.GetInfoGoalWaypoint();
            _blockCurrentWaypoint.Text = currentWP == null ? "none" :
                currentWP.GetInfoID().ToString() + " (" +
                currentWP.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "," +
                currentWP.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + ")";
            _blockDestinationWaypoint.Text = destinationWP == null ? "none" :
                destinationWP.GetInfoID().ToString() + " (" +
                destinationWP.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "," +
                destinationWP.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + ")";
            _blockGoalWaypoint.Text = goalWP == null ? "none" :
                goalWP.GetInfoID().ToString() + " (" +
                goalWP.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "," +
                goalWP.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + ")";
            _blockSpeed.Text = _bot.GetInfoSpeed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + " m/s";
            _blockBlocked.Text = _bot.GetInfoBlocked().ToString();
            double blockedUntil = _bot.GetInfoBlockedLeft();
            _blockBlockedUntil.Text = double.IsNaN(blockedUntil) || double.IsPositiveInfinity(blockedUntil) || blockedUntil < 0 ? "n/a" : TimeSpan.FromSeconds(blockedUntil).ToString(IOConstants.TIMESPAN_FORMAT_HUMAN_READABLE_MINUTES);
            _blockQueueing.Text = _bot.GetInfoIsQueueing().ToString();
            _blockState.Text = _bot.GetInfoState();
            List<IWaypointInfo> path = _bot.GetInfoPath();
            if (path != null && path.Any() && _bot.GetInfoState() == "Move")
                _blockPath.Text = string.Join(Environment.NewLine, path.Select(w =>
                    "Waypoint" + w.GetInfoID() + "-(" +
                        w.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "," +
                        w.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + ")"));
            else
                _blockPath.Text = "<empty>";
            // Update energy stats if available
            var botNormal = _bot as RAWSimO.Core.Bots.BotNormal;
            if (botNormal != null && _blockEnergyTotal != null)
            {
                _blockEnergyTotal.Text = (botNormal.StatEnergyTotalJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                _blockEnergyE1.Text = (botNormal.StatEnergyE1AccelJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                _blockEnergyE2.Text = (botNormal.StatEnergyE2DecelJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                _blockEnergyE3.Text = (botNormal.StatEnergyE3CruiseJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                _blockEnergyE4.Text = (botNormal.StatEnergyE4RotationJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                _blockEnergyE5.Text = (botNormal.StatEnergyE5LiftLowerJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotTurning != null) _blockBotTurning.Text = botNormal.StatTurningCount.ToString();
                if (_blockBotPickup != null) _blockBotPickup.Text = botNormal.StatPickupCount.ToString();
                if (_blockBotSetdown != null) _blockBotSetdown.Text = botNormal.StatSetdownCount.ToString();
                if (_blockBotOrders != null) _blockBotOrders.Text = botNormal.StatOrdersCompleted.ToString();
                if (_blockBotDistance != null) _blockBotDistance.Text = botNormal.StatDistanceTraveledM.ToString("F2", IOConstants.FORMATTER) + " m";
                if (_blockBotLoadedDistance != null) _blockBotLoadedDistance.Text = botNormal.StatLoadedDistanceM.ToString("F2", IOConstants.FORMATTER) + " m";
                if (_blockBotLoadedTurning != null) _blockBotLoadedTurning.Text = botNormal.StatLoadedTurningCount.ToString();
                if (_blockBotWaitTime != null) _blockBotWaitTime.Text = botNormal.StatWaitTimeSec.ToString("F2", IOConstants.FORMATTER) + " s";
                if (_blockBotESupport != null) _blockBotESupport.Text = (botNormal.StatESupportJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotESupportLoaded != null) _blockBotESupportLoaded.Text = (botNormal.StatEWaitLoadedJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotESupportEmpty != null) _blockBotESupportEmpty.Text = (botNormal.StatEWaitEmptyJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotIdleTime != null) _blockBotIdleTime.Text = botNormal.StatTimeIdleSec.ToString("F2", IOConstants.FORMATTER) + " s";
                if (_blockBotUtilization != null)
                {
                    double simT = botNormal.Instance.Controller.CurrentTime;
                    double util = simT > 0 ? 1.0 - botNormal.StatTimeIdleSec / simT : double.NaN;
                    _blockBotUtilization.Text = double.IsNaN(util) ? "n/a" : util.ToString("P1", IOConstants.FORMATTER);
                }
                if (_blockBotESupportRatio != null)
                {
                    double ratio = botNormal.StatEnergyTotalJ > 0
                        ? botNormal.StatESupportJ / botNormal.StatEnergyTotalJ : double.NaN;
                    _blockBotESupportRatio.Text = double.IsNaN(ratio) ? "n/a" : ratio.ToString("P2", IOConstants.FORMATTER);
                }
                // Move/turn energy split by loaded/empty
                if (_blockBotMoveEnergyEmpty != null)
                    _blockBotMoveEnergyEmpty.Text = (botNormal.StatMoveEnergyEmptyJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotMoveEnergyLoaded != null)
                    _blockBotMoveEnergyLoaded.Text = (botNormal.StatMoveEnergyLoadedJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotTurnEnergyEmpty != null)
                    _blockBotTurnEnergyEmpty.Text = (botNormal.StatTurnEnergyEmptyJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                if (_blockBotTurnEnergyLoaded != null)
                    _blockBotTurnEnergyLoaded.Text = (botNormal.StatTurnEnergyLoadedJ / 1000.0).ToString("F4", IOConstants.FORMATTER) + " kJ";
                // Per-trip tracking display (added 2026-04-20)
                if (_blockBotTripCountLoaded != null)
                    _blockBotTripCountLoaded.Text = botNormal.StatTripCountLoaded.ToString();
                if (_blockBotTripCountEmpty != null)
                    _blockBotTripCountEmpty.Text = botNormal.StatTripCountEmpty.ToString();
                if (_blockBotWaitRatioLoadedMean != null && botNormal.PerTripWaitRatioLoaded.Count > 0)
                {
                    double mean = botNormal.PerTripWaitRatioLoaded.Average();
                    _blockBotWaitRatioLoadedMean.Text = mean.ToString("P1", IOConstants.FORMATTER);
                }
                if (_blockBotWaitRatioEmptyMean != null && botNormal.PerTripWaitRatioEmpty.Count > 0)
                {
                    double mean = botNormal.PerTripWaitRatioEmpty.Average();
                    _blockBotWaitRatioEmptyMean.Text = mean.ToString("P1", IOConstants.FORMATTER);
                }
            }
            // Update payload (pod frame mass + cargo); 0 when no pod carried
            // DEBUG: also show POD_FRAME_MASS value to verify it was loaded from xlayo
            if (_blockPayload != null && botNormal != null)
            {
                double podFrameKg = RAWSimO.Core.Metrics.EnergyConsumption.POD_FRAME_MASS;
                double payloadKg = RAWSimO.Core.Metrics.EnergyConsumption.GetLoadMass(botNormal.Pod);
                if (botNormal.Pod == null)
                {
                    _blockPayload.Text = "0.00 kg (no pod) [POD_FRAME=" + podFrameKg.ToString("F0", IOConstants.FORMATTER) + " kg]";
                }
                else
                {
                    double cargoKg = botNormal.Pod.GetInfoCapacityUsed();
                    _blockPayload.Text = payloadKg.ToString("F2", IOConstants.FORMATTER) + " kg (pod " +
                        podFrameKg.ToString("F0", IOConstants.FORMATTER) + " + items " +
                        cargoKg.ToString("F2", IOConstants.FORMATTER) + ")";
                }
            }
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            TreeViewItem root = new TreeViewItem { Header = "Bot" + _bot.GetInfoID() };
            _infoHost.Items.Add(root);

            // DEBUG: Add diagnostic line showing current energy config
            WrapPanel diagPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            diagPanel.Children.Add(new TextBlock
            {
                Text = $"[DIAG] RobotMass={RAWSimO.Core.Metrics.EnergyConsumption.ROBOT_MASS} kg, PodFrameMass={RAWSimO.Core.Metrics.EnergyConsumption.POD_FRAME_MASS} kg",
                Foreground = System.Windows.Media.Brushes.Red,
                FontSize = 10
            });
            root.Items.Add(diagPanel);
            // Add position
            WrapPanel xyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            xyPanel.Children.Add(new TextBlock { Text = "X/Y: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockXY = new TextBlock
            {
                Text =
                _bot.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                _bot.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                MinWidth = _infoPanelRightColumnWidth
            };
            xyPanel.Children.Add(_blockXY);
            root.Items.Add(xyPanel);
            // Add orientation
            WrapPanel orientationPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            orientationPanel.Children.Add(new TextBlock { Text = "Orientation: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockOrientation = new TextBlock
            {
                Text = Transformation2D.ProjectOrientation(_bot.GetInfoOrientation()).ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "°",
                MinWidth = _infoPanelRightColumnWidth,
            };
            orientationPanel.Children.Add(_blockOrientation);
            root.Items.Add(orientationPanel);
            // Add orientation target
            WrapPanel orientationTargetPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            orientationTargetPanel.Children.Add(new TextBlock { Text = "Target Orientation: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockTargetOrientation = new TextBlock
            {
                Text = Transformation2D.ProjectOrientation(_bot.GetInfoTargetOrientation()).ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "°",
                MinWidth = _infoPanelRightColumnWidth,
            };
            orientationTargetPanel.Children.Add(_blockTargetOrientation);
            root.Items.Add(orientationTargetPanel);
            // Add target
            WrapPanel currentWaypointPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            currentWaypointPanel.Children.Add(new TextBlock { Text = "Current Waypoint: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockCurrentWaypoint = new TextBlock
            {
                Text = (_bot.GetInfoCurrentWaypoint() == null) ? "none" : _bot.GetInfoCurrentWaypoint().GetInfoID().ToString(),
                MinWidth = _infoPanelRightColumnWidth,
            };
            currentWaypointPanel.Children.Add(_blockCurrentWaypoint);
            root.Items.Add(currentWaypointPanel);
            // Add target
            WrapPanel destinationWaypointPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            destinationWaypointPanel.Children.Add(new TextBlock { Text = "Target Waypoint: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockDestinationWaypoint = new TextBlock
            {
                Text = (_bot.GetInfoDestinationWaypoint() == null) ? "none" : _bot.GetInfoDestinationWaypoint().GetInfoID().ToString(),
                MinWidth = _infoPanelRightColumnWidth,
            };
            destinationWaypointPanel.Children.Add(_blockDestinationWaypoint);
            root.Items.Add(destinationWaypointPanel);
            // Add goal
            WrapPanel goalWaypointPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            goalWaypointPanel.Children.Add(new TextBlock { Text = "Goal Waypoint: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockGoalWaypoint = new TextBlock
            {
                Text = (_bot.GetInfoGoalWaypoint() == null) ? "none" : _bot.GetInfoGoalWaypoint().GetInfoID().ToString(),
                MinWidth = _infoPanelRightColumnWidth,
            };
            goalWaypointPanel.Children.Add(_blockGoalWaypoint);
            root.Items.Add(goalWaypointPanel);
            // Add speed
            WrapPanel speedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            speedPanel.Children.Add(new TextBlock { Text = "Speed: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockSpeed = new TextBlock
            {
                Text = _bot.GetInfoSpeed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + " m/s",
                MinWidth = _infoPanelRightColumnWidth
            };
            speedPanel.Children.Add(_blockSpeed);
            root.Items.Add(speedPanel);
            // Add blocked status
            WrapPanel blockedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            blockedPanel.Children.Add(new TextBlock { Text = "Blocked: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockBlocked = new TextBlock
            {
                Text = _bot.GetInfoBlocked().ToString(),
                MinWidth = _infoPanelRightColumnWidth
            };
            blockedPanel.Children.Add(_blockBlocked);
            root.Items.Add(blockedPanel);
            // Add blocked time remaining
            WrapPanel blockedUntilPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            blockedUntilPanel.Children.Add(new TextBlock { Text = "Blocked until: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockBlockedUntil = new TextBlock
            {
                Text = "n/a",
                MinWidth = _infoPanelRightColumnWidth
            };
            blockedUntilPanel.Children.Add(_blockBlockedUntil);
            root.Items.Add(blockedUntilPanel);
            // Add queueing status
            WrapPanel queueingPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            queueingPanel.Children.Add(new TextBlock { Text = "Queueing: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockQueueing = new TextBlock
            {
                Text = _bot.GetInfoIsQueueing().ToString(),
                MinWidth = _infoPanelRightColumnWidth
            };
            queueingPanel.Children.Add(_blockQueueing);
            root.Items.Add(queueingPanel);
            // Add state
            WrapPanel statePanel = new WrapPanel { Orientation = Orientation.Horizontal };
            statePanel.Children.Add(new TextBlock { Text = "State: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockState = new TextBlock
            {
                Text = _bot.GetInfoState(),
                MinWidth = _infoPanelRightColumnWidth
            };
            statePanel.Children.Add(_blockState);
            root.Items.Add(statePanel);
            // Add path
            WrapPanel pathPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            pathPanel.Children.Add(new TextBlock { Text = "Path: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockPath = new TextBlock
            {
                Text = "<empty>",
                MinWidth = _infoPanelRightColumnWidth
            };
            pathPanel.Children.Add(_blockPath);
            root.Items.Add(pathPanel);
            // Add payload (pod frame mass + items weight); 0 when no pod carried
            WrapPanel payloadPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            payloadPanel.Children.Add(new TextBlock { Text = "Payload: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
            _blockPayload = new TextBlock
            {
                Text = "0.00 kg (no pod)",
                MinWidth = _infoPanelRightColumnWidth
            };
            payloadPanel.Children.Add(_blockPayload);
            root.Items.Add(payloadPanel);
            // Add energy sub-panel (Rizqi model)
            var botNormal = _bot as RAWSimO.Core.Bots.BotNormal;
            if (botNormal != null)
            {
                TreeViewItem energyNode = new TreeViewItem { Header = "Energy (Rizqi) [kJ]" };
                // Total
                WrapPanel totalPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                totalPanel.Children.Add(new TextBlock { Text = "Total: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEnergyTotal = new TextBlock { Text = "0.0000", MinWidth = _infoPanelRightColumnWidth };
                totalPanel.Children.Add(_blockEnergyTotal);
                energyNode.Items.Add(totalPanel);
                // E1 Accel
                WrapPanel e1Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                e1Panel.Children.Add(new TextBlock { Text = "E1 Accel: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEnergyE1 = new TextBlock { Text = "0.0000", MinWidth = _infoPanelRightColumnWidth };
                e1Panel.Children.Add(_blockEnergyE1);
                energyNode.Items.Add(e1Panel);
                // E2 Decel
                WrapPanel e2Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                e2Panel.Children.Add(new TextBlock { Text = "E2 Decel: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEnergyE2 = new TextBlock { Text = "0.0000", MinWidth = _infoPanelRightColumnWidth };
                e2Panel.Children.Add(_blockEnergyE2);
                energyNode.Items.Add(e2Panel);
                // E3 Cruise
                WrapPanel e3Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                e3Panel.Children.Add(new TextBlock { Text = "E3 Cruise: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEnergyE3 = new TextBlock { Text = "0.0000", MinWidth = _infoPanelRightColumnWidth };
                e3Panel.Children.Add(_blockEnergyE3);
                energyNode.Items.Add(e3Panel);
                // E4 Rotation
                WrapPanel e4Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                e4Panel.Children.Add(new TextBlock { Text = "E4 Rotation: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEnergyE4 = new TextBlock { Text = "0.0000", MinWidth = _infoPanelRightColumnWidth };
                e4Panel.Children.Add(_blockEnergyE4);
                energyNode.Items.Add(e4Panel);
                // E5 Lift/Lower
                WrapPanel e5Panel = new WrapPanel { Orientation = Orientation.Horizontal };
                e5Panel.Children.Add(new TextBlock { Text = "E5 Lift/Low: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEnergyE5 = new TextBlock { Text = "0.0000", MinWidth = _infoPanelRightColumnWidth };
                e5Panel.Children.Add(_blockEnergyE5);
                energyNode.Items.Add(e5Panel);
                // Turning
                WrapPanel turnPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                turnPanel.Children.Add(new TextBlock { Text = "Turning: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotTurning = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                turnPanel.Children.Add(_blockBotTurning);
                energyNode.Items.Add(turnPanel);
                root.Items.Add(energyNode);

                // ── Per-bot Activity sub-panel ──
                TreeViewItem activityNode = new TreeViewItem { Header = "Activity (per bot)" };
                // Pickup (lift up) count
                WrapPanel pickupPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                pickupPanel.Children.Add(new TextBlock { Text = "Lift Up: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotPickup = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                pickupPanel.Children.Add(_blockBotPickup);
                activityNode.Items.Add(pickupPanel);
                // Setdown (lift down) count
                WrapPanel setdownPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                setdownPanel.Children.Add(new TextBlock { Text = "Lift Down: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotSetdown = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                setdownPanel.Children.Add(_blockBotSetdown);
                activityNode.Items.Add(setdownPanel);
                // Orders completed
                WrapPanel ordersPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                ordersPanel.Children.Add(new TextBlock { Text = "Orders: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotOrders = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                ordersPanel.Children.Add(_blockBotOrders);
                activityNode.Items.Add(ordersPanel);
                // Total distance
                WrapPanel distPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                distPanel.Children.Add(new TextBlock { Text = "Distance: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotDistance = new TextBlock { Text = "0.00 m", MinWidth = _infoPanelRightColumnWidth };
                distPanel.Children.Add(_blockBotDistance);
                activityNode.Items.Add(distPanel);
                // Loaded distance
                WrapPanel loadedDistPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                loadedDistPanel.Children.Add(new TextBlock { Text = "Loaded Dist: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotLoadedDistance = new TextBlock { Text = "0.00 m", MinWidth = _infoPanelRightColumnWidth };
                loadedDistPanel.Children.Add(_blockBotLoadedDistance);
                activityNode.Items.Add(loadedDistPanel);
                // Loaded turning
                WrapPanel loadedTurnPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                loadedTurnPanel.Children.Add(new TextBlock { Text = "Loaded Turn: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotLoadedTurning = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                loadedTurnPanel.Children.Add(_blockBotLoadedTurning);
                activityNode.Items.Add(loadedTurnPanel);
                // Wait time
                WrapPanel waitPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                waitPanel.Children.Add(new TextBlock { Text = "Wait Time: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotWaitTime = new TextBlock { Text = "0.00 s", MinWidth = _infoPanelRightColumnWidth };
                waitPanel.Children.Add(_blockBotWaitTime);
                activityNode.Items.Add(waitPanel);
                // E_support (background: P_SUPPORT × active-task time)
                WrapPanel eSupportPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                eSupportPanel.Children.Add(new TextBlock { Text = "E_support: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotESupport = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                eSupportPanel.Children.Add(_blockBotESupport);
                activityNode.Items.Add(eSupportPanel);
                // E_wait loaded (congestion-wait subset while carrying a pod)
                WrapPanel eSupportLoadedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                eSupportLoadedPanel.Children.Add(new TextBlock { Text = "E_wait Loaded: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotESupportLoaded = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                eSupportLoadedPanel.Children.Add(_blockBotESupportLoaded);
                activityNode.Items.Add(eSupportLoadedPanel);
                // E_wait empty
                WrapPanel eSupportEmptyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                eSupportEmptyPanel.Children.Add(new TextBlock { Text = "E_wait Empty: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotESupportEmpty = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                eSupportEmptyPanel.Children.Add(_blockBotESupportEmpty);
                activityNode.Items.Add(eSupportEmptyPanel);
                // Idle time
                WrapPanel idleTimePanel = new WrapPanel { Orientation = Orientation.Horizontal };
                idleTimePanel.Children.Add(new TextBlock { Text = "Idle Time: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotIdleTime = new TextBlock { Text = "0.00 s", MinWidth = _infoPanelRightColumnWidth };
                idleTimePanel.Children.Add(_blockBotIdleTime);
                activityNode.Items.Add(idleTimePanel);
                // Utilization (approx: 1 - idle/currentTime, no warmup correction)
                WrapPanel utilPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                utilPanel.Children.Add(new TextBlock { Text = "Utilization≈: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotUtilization = new TextBlock { Text = "n/a", MinWidth = _infoPanelRightColumnWidth };
                utilPanel.Children.Add(_blockBotUtilization);
                activityNode.Items.Add(utilPanel);
                // E_support / E_mech ratio
                WrapPanel eSupportRatioPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                eSupportRatioPanel.Children.Add(new TextBlock { Text = "E_sup/E_mech: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotESupportRatio = new TextBlock { Text = "n/a", MinWidth = _infoPanelRightColumnWidth };
                eSupportRatioPanel.Children.Add(_blockBotESupportRatio);
                activityNode.Items.Add(eSupportRatioPanel);
                // Move energy (empty)
                WrapPanel moveEmptyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                moveEmptyPanel.Children.Add(new TextBlock { Text = "Move E_empty: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotMoveEnergyEmpty = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                moveEmptyPanel.Children.Add(_blockBotMoveEnergyEmpty);
                activityNode.Items.Add(moveEmptyPanel);
                // Move energy (loaded)
                WrapPanel moveLoadedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                moveLoadedPanel.Children.Add(new TextBlock { Text = "Move E_loaded: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotMoveEnergyLoaded = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                moveLoadedPanel.Children.Add(_blockBotMoveEnergyLoaded);
                activityNode.Items.Add(moveLoadedPanel);
                // Turn energy (empty)
                WrapPanel turnEmptyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                turnEmptyPanel.Children.Add(new TextBlock { Text = "Turn E_empty: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotTurnEnergyEmpty = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                turnEmptyPanel.Children.Add(_blockBotTurnEnergyEmpty);
                activityNode.Items.Add(turnEmptyPanel);
                // Turn energy (loaded)
                WrapPanel turnLoadedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                turnLoadedPanel.Children.Add(new TextBlock { Text = "Turn E_loaded: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotTurnEnergyLoaded = new TextBlock { Text = "0.0000 kJ", MinWidth = _infoPanelRightColumnWidth };
                turnLoadedPanel.Children.Add(_blockBotTurnEnergyLoaded);
                activityNode.Items.Add(turnLoadedPanel);
                // Per-trip tracking (added 2026-04-20)
                WrapPanel tripCountLoadedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                tripCountLoadedPanel.Children.Add(new TextBlock { Text = "Trip Count L: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotTripCountLoaded = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                tripCountLoadedPanel.Children.Add(_blockBotTripCountLoaded);
                activityNode.Items.Add(tripCountLoadedPanel);
                WrapPanel tripCountEmptyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                tripCountEmptyPanel.Children.Add(new TextBlock { Text = "Trip Count E: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotTripCountEmpty = new TextBlock { Text = "0", MinWidth = _infoPanelRightColumnWidth };
                tripCountEmptyPanel.Children.Add(_blockBotTripCountEmpty);
                activityNode.Items.Add(tripCountEmptyPanel);
                WrapPanel waitRatioLoadedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                waitRatioLoadedPanel.Children.Add(new TextBlock { Text = "Wait Ratio L: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotWaitRatioLoadedMean = new TextBlock { Text = "n/a", MinWidth = _infoPanelRightColumnWidth };
                waitRatioLoadedPanel.Children.Add(_blockBotWaitRatioLoadedMean);
                activityNode.Items.Add(waitRatioLoadedPanel);
                WrapPanel waitRatioEmptyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                waitRatioEmptyPanel.Children.Add(new TextBlock { Text = "Wait Ratio E: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBotWaitRatioEmptyMean = new TextBlock { Text = "n/a", MinWidth = _infoPanelRightColumnWidth };
                waitRatioEmptyPanel.Children.Add(_blockBotWaitRatioEmptyMean);
                activityNode.Items.Add(waitRatioEmptyPanel);
                activityNode.IsExpanded = true;
                root.Items.Add(activityNode);
            }
            // Expand root node
            root.IsExpanded = true;
        }
    }

    #endregion

    #region Pod

    public class SimulationInfoPod : SimulationInfoObject
    {
        private readonly IPodInfo _pod;
        private readonly int _infoPanelLeftColumnWidth = 105;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TextBlock _blockXY;
        private TextBlock _blockOrientation;
        private TextBlock _blockCapacity;
        private TextBlock _blockCapacityReserved;
        private TextBlock _blockStorageTag;
        private TreeViewItem _treeItemContent;
        private TreeViewItem _root;
        private SimulationVisualContentManager _contentManager;

        private TextBlock _blockReadyForRefill;

        public SimulationInfoPod(TreeView infoHost, IPodInfo pod) : base(infoHost) { _pod = pod; }

        public override void InfoPanelUpdate()
        {
            // Update meta info
            _blockXY.Text = _pod.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" + _pod.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            _blockOrientation.Text = Transformation2D.ProjectOrientation(_pod.GetInfoOrientation()).ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "°";
            _blockCapacity.Text = _pod.GetInfoCapacityUsed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" + _pod.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            _blockCapacityReserved.Text = _pod.GetInfoCapacityReserved().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" + _pod.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER);
            _blockStorageTag.Text = _pod.InfoTagPodStorageInfo;
            _blockReadyForRefill.Text = _pod.GetInfoReadyForRefill().ToString();

            // Update content info
            if (_pod.GetInfoContentChanged())
                _contentManager.UpdateContentInfo();
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            if (_root == null)
            {
                _root = new TreeViewItem { Header = "Pod" + _pod.GetInfoID() };
                // Add position
                WrapPanel xyPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                xyPanel.Children.Add(new TextBlock { Text = "X/Y: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockXY = new TextBlock
                {
                    Text =
                    _pod.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                    _pod.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth
                };
                xyPanel.Children.Add(_blockXY);
                _root.Items.Add(xyPanel);
                // Add orientation
                WrapPanel orientationPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                orientationPanel.Children.Add(new TextBlock { Text = "Orientation: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockOrientation = new TextBlock
                {
                    Text = Transformation2D.ProjectOrientation(_pod.GetInfoOrientation()).ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "°",
                    MinWidth = _infoPanelRightColumnWidth,
                };
                orientationPanel.Children.Add(_blockOrientation);
                _root.Items.Add(orientationPanel);
                // Add capacity
                WrapPanel capacityPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                capacityPanel.Children.Add(new TextBlock { Text = "Capacity: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockCapacity = new TextBlock
                {
                    Text = _pod.GetInfoCapacityUsed().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                    _pod.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                capacityPanel.Children.Add(_blockCapacity);
                _root.Items.Add(capacityPanel);
                // Add capacity reserved
                WrapPanel capacityReservedPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                capacityReservedPanel.Children.Add(new TextBlock { Text = "Capacity reserved: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockCapacityReserved = new TextBlock
                {
                    Text = _pod.GetInfoCapacityReserved().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "/" +
                    _pod.GetInfoCapacity().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                capacityReservedPanel.Children.Add(_blockCapacityReserved);
                _root.Items.Add(capacityReservedPanel);
                // Add capacity reserved
                WrapPanel storageTagPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                storageTagPanel.Children.Add(new TextBlock { Text = "Storage tag: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStorageTag = new TextBlock
                {
                    Text = _pod.InfoTagPodStorageInfo,
                    MinWidth = _infoPanelRightColumnWidth,
                };
                storageTagPanel.Children.Add(_blockStorageTag);
                _root.Items.Add(storageTagPanel);
                // Add ReadyForRefill
                WrapPanel readyForRefillPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                readyForRefillPanel.Children.Add(new TextBlock { Text = "Ready for refill: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockReadyForRefill = new TextBlock
                {
                    Text = _pod.GetInfoReadyForRefill().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                readyForRefillPanel.Children.Add(_blockReadyForRefill);
                _root.Items.Add(readyForRefillPanel);
                // Add content
                if (_treeItemContent == null)
                {
                    _treeItemContent = new TreeViewItem { Header = "Content" };
                    _treeItemContent.IsExpanded = true;
                    _contentManager = new SimulationVisualContentManager(_treeItemContent, _pod);
                }
                _root.Items.Add(_treeItemContent);
            }
            // Update content info
            _contentManager.UpdateContentInfo();
            // Add and expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion

    #region Guard

    public class SimulationInfoGuard : SimulationInfoObject
    {
        private readonly IGuardInfo _guard;
        private readonly int _infoPanelLeftColumnWidth = 80;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TextBlock _blockConnection;
        private TextBlock _blockEntry;
        private TextBlock _blockBlock;
        private TextBlock _blockCapacity;
        private TreeViewItem _root;

        public SimulationInfoGuard(TreeView infoHost, IGuardInfo guard) : base(infoHost) { _guard = guard; }

        public override void InfoPanelUpdate()
        {
            // Update meta info
            _blockBlock.Text = _guard.GetInfoIsAccessible().ToString();
            _blockCapacity.Text = _guard.GetInfoRequests().ToString() + "/" + _guard.GetInfoCapacity().ToString();
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            if (_root == null)
            {
                _root = new TreeViewItem { Header = "Guard (S:" + _guard.GetInfoSemaphore().GetInfoID().ToString() + "-" + _guard.GetInfoSemaphore().GetInfoGuards().ToString() + "Gs)" };
                // Add connection
                WrapPanel connectionPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                connectionPanel.Children.Add(new TextBlock { Text = "Path: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockConnection = new TextBlock
                {
                    Text = _guard.GetInfoFrom().GetInfoID().ToString() + " -> " + _guard.GetInfoTo().GetInfoID().ToString(),
                    MinWidth = _infoPanelRightColumnWidth
                };
                connectionPanel.Children.Add(_blockConnection);
                _root.Items.Add(connectionPanel);
                // Add entry info
                WrapPanel entryPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                entryPanel.Children.Add(new TextBlock { Text = "Entry: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockEntry = new TextBlock
                {
                    Text = _guard.GetInfoIsEntry().ToString(),
                    MinWidth = _infoPanelRightColumnWidth
                };
                entryPanel.Children.Add(_blockEntry);
                _root.Items.Add(entryPanel);
                // Add block info
                WrapPanel blockPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                blockPanel.Children.Add(new TextBlock { Text = "Accessible: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockBlock = new TextBlock
                {
                    Text = _guard.GetInfoIsAccessible().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                blockPanel.Children.Add(_blockBlock);
                _root.Items.Add(blockPanel);
                // Add capacity
                WrapPanel capacityPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                capacityPanel.Children.Add(new TextBlock { Text = "Capacity: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockCapacity = new TextBlock
                {
                    Text = _guard.GetInfoRequests().ToString() + "/" + _guard.GetInfoCapacity().ToString(),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                capacityPanel.Children.Add(_blockCapacity);
                _root.Items.Add(capacityPanel);
            }
            // Add and expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion

    #region Waypoint

    public class SimulationInfoWaypoint : SimulationInfoObject
    {
        private readonly IWaypointInfo _waypoint;
        private readonly int _infoPanelLeftColumnWidth = 80;
        private readonly int _infoPanelRightColumnWidth = 60;
        private TextBlock _blockPaths;
        private TextBlock _blockPosition;
        private TextBlock _blockStorageInfo;
        private TreeViewItem _root;

        public SimulationInfoWaypoint(TreeView infoHost, IWaypointInfo waypoint) : base(infoHost) { _waypoint = waypoint; }

        public override void InfoPanelUpdate()
        {
            // Update meta info
            // Nothing should be subject to change - so don't do anything
        }

        public override void InfoPanelInit()
        {
            // Visually emphasize focus on element
            ManagedVisual2D.StrokeThickness = StrokeThicknessFocused;
            // Prepare information controls
            _infoHost.Items.Clear();
            if (_root == null)
            {
                _root = new TreeViewItem { Header = "Waypoint" + _waypoint.GetInfoID() };
                // Add position
                WrapPanel positionPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                positionPanel.Children.Add(new TextBlock { Text = "Position: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockPosition = new TextBlock
                {
                    Text = _waypoint.GetInfoCenterX().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + "," + _waypoint.GetInfoCenterY().ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER),
                    MinWidth = _infoPanelRightColumnWidth,
                };
                positionPanel.Children.Add(_blockPosition);
                _root.Items.Add(positionPanel);
                // Add connections
                WrapPanel pathPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                pathPanel.Children.Add(new TextBlock { Text = "Paths: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockPaths = new TextBlock
                {
                    Text = string.Join(",", _waypoint.GetInfoConnectedWaypoints().Select(wp => wp.GetInfoID())),
                    MinWidth = _infoPanelRightColumnWidth
                };
                pathPanel.Children.Add(_blockPaths);
                _root.Items.Add(pathPanel);
                // Add queue info
                WrapPanel storageInfoPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                storageInfoPanel.Children.Add(new TextBlock { Text = "Storage: ", TextAlignment = TextAlignment.Right, MinWidth = _infoPanelLeftColumnWidth });
                _blockStorageInfo = new TextBlock
                {
                    Text = _waypoint.GetInfoStorageLocation().ToString(),
                    MinWidth = _infoPanelRightColumnWidth
                };
                storageInfoPanel.Children.Add(_blockStorageInfo);
                _root.Items.Add(storageInfoPanel);
            }
            // Add and expand root node
            _infoHost.Items.Add(_root);
            _root.IsExpanded = true;
        }
    }

    #endregion
}
