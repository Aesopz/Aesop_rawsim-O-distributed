using RAWSimO.Core.Geometrics;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RAWSimO.Core.Metrics
{
    /// <summary>
    /// This class implements some basic distance measures.
    /// </summary>
    public class Distances
    {
        /// <summary>
        /// Calculates the shortest path from the first to the second waypoint.
        /// </summary>
        /// <param name="from">The waypoint at which the path starts.</param>
        /// <param name="to">The waypoint at which the path ends.</param>
        /// <param name="instance">The instance containing the graph both waypoints belong to.</param>
        /// <returns>The distance of the shortest path. If both waypoints are not located on the same tier, penalty costs will be added. If there is no connection, an infinite length is returned.</returns>
        public static double CalculateShortestPath(Waypoint from, Waypoint to, Instance instance)
        {
            return instance.MetaInfoManager.ShortestPathManager.GetShortestPath(from, to, instance, false);
        }
        /// <summary>
        /// Calculates the shortest path from the first to the second waypoint avoiding pod storage locations.
        /// </summary>
        /// <param name="from">The waypoint at which the path starts.</param>
        /// <param name="to">The waypoint at which the path ends.</param>
        /// <param name="instance">The instance containing the graph both waypoints belong to.</param>
        /// <returns>The distance of the shortest path. If both waypoints are not located on the same tier, penalty costs will be added. If there is no connection, an infinite length is returned.</returns>
        public static double CalculateShortestPathPodSafe(Waypoint from, Waypoint to, Instance instance)
        {
            return instance.MetaInfoManager.ShortestPathManager.GetShortestPath(from, to, instance, true);
        }
        /// <summary>
        /// Calculates the shortest path from the first to the second waypoint.
        /// </summary>
        /// <param name="from">The waypoint at which the path starts.</param>
        /// <param name="to">The waypoint at which the path ends.</param>
        /// <param name="instance">The instance containing the graph both waypoints belong to.</param>
        /// <returns>The distance of the shortest path. If both waypoints are not located on the same tier, penalty costs will be added. If there is no connection, an infinite length is returned.</returns>
        public static double CalculateShortestTimePath(Waypoint from, Waypoint to, Instance instance)
        {
            return instance.MetaInfoManager.TimeEfficientPathManager.GetShortestPath(from, to, instance, false);
        }
        /// <summary>
        /// Calculates the shortest path from the first to the second waypoint avoiding pod storage locations.
        /// </summary>
        /// <param name="from">The waypoint at which the path starts.</param>
        /// <param name="to">The waypoint at which the path ends.</param>
        /// <param name="instance">The instance containing the graph both waypoints belong to.</param>
        /// <returns>The distance of the shortest path. If both waypoints are not located on the same tier, penalty costs will be added. If there is no connection, an infinite length is returned.</returns>
        public static double CalculateShortestTimePathPodSafe(Waypoint from, Waypoint to, Instance instance)
        {
            return instance.MetaInfoManager.TimeEfficientPathManager.GetShortestPath(from, to, instance, true);
        }
        /// <summary>
        /// Estimates the time for traveling using the euclidean metric.
        /// </summary>
        /// <param name="from">The from part of the trip.</param>
        /// <param name="to">The to part of the trip.</param>
        /// <param name="instance">The instance.</param>
        /// <returns>The time needed to for conducting the trip according to the euclidean metric.</returns>
        public static double EstimateEuclidTime(Circle from, Circle to, Instance instance)
        {
            return instance.MetaInfoManager.TimeEfficientPathManager.EstimateShortestPathEuclid(from, to, instance);
        }
        /// <summary>
        /// Estimates the time for traveling using the manhattan metric.
        /// </summary>
        /// <param name="from">The from part of the trip.</param>
        /// <param name="to">The to part of the trip.</param>
        /// <param name="instance">The instance.</param>
        /// <returns>The time needed to for conducting the trip according to the manhattan metric.</returns>
        public static double EstimateManhattanTime(Circle from, Circle to, Instance instance)
        {
            return instance.MetaInfoManager.TimeEfficientPathManager.EstimateShortestPathManhattan(from, to, instance);
        }
        /// <summary>
        /// Calculates the euclidean distance between the two circles.
        /// </summary>
        /// <param name="c1">First circle.</param>
        /// <param name="c2">Second circle.</param>
        /// <param name="wrongTierPenalty">The penalty for not being on the same tier.</param>
        /// <returns>The euclidean distance between the two.</returns>
        public static double CalculateEuclid(Circle c1, Circle c2, double wrongTierPenalty)
        {
            double distance = c1.GetDistance(c2);
            return c1.Tier == c2.Tier ? distance : distance + wrongTierPenalty;
        }
        /// <summary>
        /// Calculates the manhattan distance between the two circles.
        /// </summary>
        /// <param name="c1">First circle.</param>
        /// <param name="c2">Second circle.</param>
        /// <param name="wrongTierPenalty">The penalty for not being on the same tier.</param>
        /// <returns>The manhattan distance between the two.</returns>
        public static double CalculateManhattan(Circle c1, Circle c2, double wrongTierPenalty)
        {
            double distance = Math.Abs(c1.X - c2.X) + Math.Abs(c1.Y - c2.Y);
            return c1.Tier == c2.Tier ? distance : distance + wrongTierPenalty;
        }
        /// <summary>
        /// Simply calculates the euclidean distance for the given two points.
        /// </summary>
        /// <param name="x1">The x-value of the first point.</param>
        /// <param name="y1">The y-value of the first point.</param>
        /// <param name="x2">The x-value of the second point.</param>
        /// <param name="y2">The y-value of the second point.</param>
        /// <returns>The euclidean distance.</returns>
        public static double CalculateEuclid(double x1, double y1, double x2, double y2) { return Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2)); }
        /// <summary>
        /// Simply calculates the manhattan distance for the given two points.
        /// </summary>
        /// <param name="x1">The x-value of the first point.</param>
        /// <param name="y1">The y-value of the first point.</param>
        /// <param name="x2">The x-value of the second point.</param>
        /// <param name="y2">The y-value of the second point.</param>
        /// <returns>The manhattan distance.</returns>
        public static double CalculateManhattan(double x1, double y1, double x2, double y2) { return Math.Abs(x1 - x2) + Math.Abs(y1 - y2); }

        /// <summary>
        /// EE 線上優化框架使用：與 CalculateShortestPathPodSafe 相同語意（pod-safe shortest-path）。
        /// EE 程式碼將 distance helper 重複命名為 *1 變體；此處作為相同實作的 alias 以保留 EE manager
        /// 程式碼一字不改可以編譯。
        /// </summary>
        public static double CalculateShortestPathPodSafe1(Waypoint from, Waypoint to, Instance instance)
            => CalculateShortestPathPodSafe(from, to, instance);

        /// <summary>
        /// EE 線上優化框架使用：兩個 Circle 之間的 Manhattan 距離（不含 wrong-tier 懲罰）。
        /// 等同於 CalculateManhattan(c1, c2, 0)。EE 用此計算 robot↔pod 的曼哈頓距離當作 cost。
        /// </summary>
        public static double CalculateManhattan1(Circle c1, Circle c2)
        {
            // EE 移植 + null-tolerant：xinst 載入時 pod 可能尚未 snap 到 waypoint（pod.Waypoint == null）
            // 或 pod 已被 claim 移交給 bot（亦會將 Waypoint 設 null）。EE aesop xlayo 路徑不會發生
            // 此狀況因 xlayo + InstanceGenerator 強制 binding；xinst 路徑則需此守門。
            if (c1 == null || c2 == null) return double.MaxValue;
            return Math.Abs(c1.X - c2.X) + Math.Abs(c1.Y - c2.Y);
        }
    }
}
