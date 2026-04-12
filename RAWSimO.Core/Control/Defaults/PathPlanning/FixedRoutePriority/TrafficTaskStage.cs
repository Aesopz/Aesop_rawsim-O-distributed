namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    public enum TrafficTaskStage
    {
        Delivery,  // Loaded + heading to OutputStation
        Return,    // Loaded + not heading to OutputStation
        Pickup,    // Empty + has destination
        Idle       // No task
    }
}
