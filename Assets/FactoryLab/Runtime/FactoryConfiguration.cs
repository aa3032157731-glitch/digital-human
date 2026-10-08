using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FactoryLab
{
    [Serializable]
    public struct FloorPoint
    {
        public double x, z;
        public FloorPoint(double x, double z) { this.x = x; this.z = z; }
        public double Distance(FloorPoint other) => Math.Abs(x - other.x) + Math.Abs(z - other.z);
    }

    public sealed class StationDefinition
    {
        public string Id, Name, Kind;
        public double Cycle;
        public int Capacity;
    }

    public sealed class FactoryConfiguration
    {
        public static readonly string[] Route = { "material-storage", "assembly", "fastening", "inspection", "packaging", "finished-goods-storage" };
        public StationDefinition[] Stations;
        public FloorPoint[] Centers, Ports;
        public double Speed, LoadSeconds, UnloadSeconds, AisleZ;
        public int OrderQuantity;
        public string LayoutId;

        public static FactoryConfiguration Read(string caseJson, string layoutJson, string layoutId = "baseline")
        {
            var data = JObject.Parse(caseJson);
            var layouts = JObject.Parse(layoutJson);
            var layout = ((JArray)layouts["layouts"]).Single(x => (string)x["id"] == layoutId);
            var result = new FactoryConfiguration {
                LayoutId = layoutId, OrderQuantity = (int)data["order"]["quantity"],
                Speed = (double)data["carrier"]["speed"], LoadSeconds = (double)data["carrier"]["loadSeconds"],
                UnloadSeconds = (double)data["carrier"]["unloadSeconds"], AisleZ = (double)layouts["walkway"]["z"],
                Stations = new StationDefinition[6], Centers = new FloorPoint[6], Ports = new FloorPoint[6]
            };
            var occupied = new HashSet<string>();
            for (int i = 0; i < Route.Length; i++)
            {
                var station = ((JArray)data["stations"]).Single(x => (string)x["id"] == Route[i]);
                string slot = (string)layout["positions"][Route[i]];
                if (!occupied.Add(slot)) throw new ArgumentException("工位位置重复: " + slot);
                var point = layouts["legalPositions"][slot];
                string fixedId = (string)point["fixedStationId"];
                if (fixedId != null && fixedId != Route[i]) throw new ArgumentException("固定区域不可移动");
                if ((bool?)station["fixed"] == true && fixedId != Route[i]) throw new ArgumentException("固定工位不可移动");
                result.Centers[i] = new FloorPoint((double)point["x"], (double)point["z"]);
                // Handoffs are on the aisle-facing side; bodies are not navigation nodes.
                var port = point["port"];
                if (port == null) throw new ArgumentException("布局缺少交接点: " + slot);
                result.Ports[i] = new FloorPoint((double)port["x"], (double)port["z"]);
                result.Stations[i] = new StationDefinition { Id = Route[i], Name = (string)station["name"], Kind = (string)station["kind"],
                    Cycle = (double)station["cycleTime"], Capacity = (int)station["bufferCapacity"] };
            }
            result.Validate();
            return result;
        }

        public void Validate()
        {
            if (OrderQuantity < 1 || OrderQuantity > 200 || !Finite(Speed) || Speed <= 0 ||
                !Finite(LoadSeconds) || LoadSeconds < 0 || !Finite(UnloadSeconds) || UnloadSeconds < 0)
                throw new ArgumentException("订单或搬运参数无效");
            if (Stations == null || Stations.Length != 6 || Centers.Length != 6 || Ports.Length != 6)
                throw new ArgumentException("需要完整的六个工位");
            foreach (var s in Stations)
                if (!Finite(s.Cycle) || s.Cycle < 0 || s.Capacity < 1) throw new ArgumentException("工时或容量无效");
            foreach (var p in Centers.Concat(Ports))
                if (!Finite(p.x) || !Finite(p.z) || p.x < 0 || p.x > 12 || p.z < 0 || p.z > 8)
                    throw new ArgumentException("工位或交接点超出车间");
        }

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        public List<FloorPoint> Path(FloorPoint from, FloorPoint to)
        {
            var path = new List<FloorPoint> { from };
            if (from.Distance(to) < 1e-9) return path;
            foreach (var p in new[] { new FloorPoint(from.x, AisleZ), new FloorPoint(to.x, AisleZ), to })
                if (path[path.Count - 1].Distance(p) > 1e-9) path.Add(p);
            return path;
        }

        public double Distance(FloorPoint from, FloorPoint to)
        {
            var path = Path(from, to); double length = 0;
            for (int i = 1; i < path.Count; i++) length += path[i - 1].Distance(path[i]);
            return length;
        }

        public double ProductDistance() => Enumerable.Range(0, 5).Sum(i => Distance(Ports[i], Ports[i + 1]));
        public double SinglePieceSeconds() => Stations.Sum(s => s.Cycle) + 5 * (LoadSeconds + UnloadSeconds) + ProductDistance() / Speed;
    }
}
