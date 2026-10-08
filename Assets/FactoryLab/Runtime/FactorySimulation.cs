using System;
using System.Collections.Generic;
using System.Linq;

namespace FactoryLab
{
    [Serializable]
    public sealed class FactoryEventRecord
    {
        public string subject, activity, reason, from, to, resource;
        public double startSeconds, distanceM;
        public double? endSeconds;
    }

    public sealed class FactoryProduct
    {
        public int Index, Station, ProcessedThrough;
        public string Id => "LAMP-" + (Index + 1).ToString("00");
        public string Activity = "storage", Reason = "controlled-stock";
        public double ActivityStart, LoadedDistance, WaitingSeconds;
        public double? ReleasedAt, FinishedAt;
        internal int From, To;
        internal double ActivityDistance;
    }

    public sealed class FactoryStationState
    {
        public readonly Queue<FactoryProduct> Input = new Queue<FactoryProduct>();
        public FactoryProduct Processing, Output;
        public double ProcessEnd;
        public int Reserved, MaxInputCount, Completed;
    }

    public enum CarrierPhase { Idle, EmptyTravel, Loading, LoadedTravel, Unloading }

    /// <summary>Deterministic event clock. No Unity time, animation or frame-rate dependency.</summary>
    public sealed class FactorySimulation
    {
        private const double Epsilon = 1e-8;
        public FactoryConfiguration Config { get; }
        public FactoryStationState[] Stations { get; }
        public FactoryProduct[] Products { get; }
        public List<FactoryEventRecord> Events { get; } = new List<FactoryEventRecord>();
        public double Time { get; private set; }
        public int FinishedCount { get; private set; }
        public bool Complete => FinishedCount == Products.Length;
        public CarrierPhase Phase { get; private set; }
        public FactoryProduct Cargo { get; private set; }
        public double PhaseStart { get; private set; }
        public double PhaseEnd { get; private set; }
        public List<FloorPoint> CarrierPath { get; private set; }
        public FloorPoint CarrierPosition { get; private set; }
        public double EmptyDistance { get; private set; }
        public double LoadedDistance => Products.Sum(p => p.LoadedDistance);
        private int issued, source, destination;

        public FactorySimulation(FactoryConfiguration config, int quantity)
        {
            config.Validate();
            if (quantity < 1 || quantity > 200) throw new ArgumentOutOfRangeException(nameof(quantity));
            Config = config;
            Stations = Enumerable.Range(0, 6).Select(_ => new FactoryStationState()).ToArray();
            Products = Enumerable.Range(0, quantity).Select(i => new FactoryProduct { Index = i }).ToArray();
            CarrierPosition = config.Ports[0];
            CarrierPath = new List<FloorPoint> { CarrierPosition };
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Complete) return;
            double target = Time + seconds;
            Settle();
            for (int guard = 0; !Complete && Time < target; guard++)
            {
                if (guard > 100000) throw new InvalidOperationException("仿真事件循环超限");
                double next = Phase == CarrierPhase.Idle ? double.PositiveInfinity : PhaseEnd;
                for (int s = 1; s <= 4; s++)
                    if (Stations[s].Processing != null) next = Math.Min(next, Stations[s].ProcessEnd);
                if (double.IsPositiveInfinity(next)) throw new InvalidOperationException("仿真死锁：订单尚未完成且无后续事件");
                if (next <= target) { Time = next; Settle(); }
                else Time = target;
            }
            AssertInvariants();
        }

        private void Settle()
        {
            for (int guard = 0; guard < 100; guard++)
            {
                bool changed = false;
                for (int s = 1; s <= 4; s++)
                {
                    var station = Stations[s];
                    if (station.Processing != null && station.ProcessEnd <= Time)
                    {
                        if (station.Output != null) throw new InvalidOperationException("输出料位冲突");
                        station.Output = station.Processing; station.Processing = null; station.Completed++;
                        station.Output.ProcessedThrough = s;
                        Change(station.Output, "delay", "waiting-carrier", s, s);
                        changed = true;
                    }
                }
                if (Phase != CarrierPhase.Idle && PhaseEnd <= Time) { FinishCarrierPhase(); changed = true; }
                for (int s = 1; s <= 4; s++)
                {
                    var station = Stations[s];
                    if (station.Processing == null && station.Output == null && station.Input.Count > 0)
                    {
                        station.Processing = station.Input.Dequeue();
                        Change(station.Processing, Config.Stations[s].Kind, "processing", s, s);
                        station.ProcessEnd = Time + Config.Stations[s].Cycle;
                        changed = true;
                    }
                    // Split delay intervals when the limiting resource changes.
                    foreach (var p in station.Input)
                        Change(p, "delay", station.Output != null ? "output-blocked" : "waiting-station", s, s);
                    if (station.Output != null && station.Output.Activity == "delay")
                        Change(station.Output, "delay", HasRoom(s + 1) ? "waiting-carrier" : "downstream-full", s, s);
                }
                if (Phase == CarrierPhase.Idle && !Complete && Dispatch()) changed = true;
                if (!changed) return;
            }
            throw new InvalidOperationException("同一时刻状态转换超限");
        }

        private bool HasRoom(int station) => station == 5 || Stations[station].Input.Count + Stations[station].Reserved < Config.Stations[station].Capacity;

        private bool Dispatch()
        {
            for (int s = 4; s >= 0; s--)
            {
                var product = s == 0 ? (issued < Products.Length ? Products[issued] : null) : Stations[s].Output;
                if (product == null || !HasRoom(s + 1)) continue;
                source = s; destination = s + 1; Cargo = product;
                if (s == 0) issued++;
                if (destination < 5) Stations[destination].Reserved++;
                Travel(CarrierPhase.EmptyTravel, Config.Ports[source]);
                return true;
            }
            return false;
        }

        private void Travel(CarrierPhase phase, FloorPoint target)
        {
            CarrierPath = Config.Path(CarrierPosition, target);
            Phase = phase; PhaseStart = Time;
            PhaseEnd = Time + Config.Distance(CarrierPosition, target) / Config.Speed;
        }

        private void FinishCarrierPhase()
        {
            switch (Phase)
            {
                case CarrierPhase.EmptyTravel:
                    double empty = Config.Distance(CarrierPosition, Config.Ports[source]);
                    if (empty > Epsilon)
                        Events.Add(new FactoryEventRecord { subject = "carrier-01", activity = "empty-travel", resource = "carrier-01",
                            reason = "pickup", startSeconds = PhaseStart, endSeconds = Time, distanceM = empty,
                            from = Config.Stations[Array.FindIndex(Config.Ports, p => p.Distance(CarrierPosition) < Epsilon)].Id, to = Config.Stations[source].Id });
                    EmptyDistance += empty; CarrierPosition = Config.Ports[source];
                    if (!Cargo.ReleasedAt.HasValue) Cargo.ReleasedAt = Time;
                    Change(Cargo, "transport", "loaded-transfer", source, destination);
                    Cargo.ActivityDistance = Config.Distance(Config.Ports[source], Config.Ports[destination]);
                    Phase = CarrierPhase.Loading; PhaseStart = Time; PhaseEnd = Time + Config.LoadSeconds;
                    break;
                case CarrierPhase.Loading:
                    if (source > 0) Stations[source].Output = null;
                    Travel(CarrierPhase.LoadedTravel, Config.Ports[destination]);
                    break;
                case CarrierPhase.LoadedTravel:
                    CarrierPosition = Config.Ports[destination];
                    Phase = CarrierPhase.Unloading; PhaseStart = Time; PhaseEnd = Time + Config.UnloadSeconds;
                    break;
                case CarrierPhase.Unloading:
                    Cargo.LoadedDistance += Cargo.ActivityDistance;
                    Cargo.Station = destination;
                    if (destination == 5)
                    {
                        Change(Cargo, "storage", "finished-stock", 5, 5);
                        Cargo.FinishedAt = Time; FinishedCount++;
                        Events.Add(new FactoryEventRecord { subject = Cargo.Id, activity = "storage", reason = "finished-stock",
                            from = Config.Stations[5].Id, to = Config.Stations[5].Id, resource = "finished-goods-storage",
                            startSeconds = Time, endSeconds = null });
                    }
                    else
                    {
                        var station = Stations[destination]; station.Reserved--; station.Input.Enqueue(Cargo);
                        station.MaxInputCount = Math.Max(station.MaxInputCount, station.Input.Count);
                        Change(Cargo, "delay", "waiting-station", destination, destination);
                    }
                    Cargo = null; Phase = CarrierPhase.Idle;
                    break;
            }
        }

        private void Change(FactoryProduct product, string activity, string reason, int from, int to)
        {
            if (product.Activity == activity && product.Reason == reason && product.From == from && product.To == to) return;
            double elapsed = Time - product.ActivityStart;
            if (elapsed > Epsilon || product.Activity == "operation" || product.Activity == "inspection" || product.Activity == "transport")
                Events.Add(new FactoryEventRecord { subject = product.Id, activity = product.Activity, reason = product.Reason,
                    from = Config.Stations[product.From].Id, to = Config.Stations[product.To].Id,
                    resource = product.Activity == "transport" ? "carrier-01" : Config.Stations[product.From].Id,
                    startSeconds = product.ActivityStart, endSeconds = Time, distanceM = product.ActivityDistance });
            if (product.Activity == "delay") product.WaitingSeconds += elapsed;
            product.Activity = activity; product.Reason = reason; product.From = from; product.To = to;
            product.ActivityStart = Time; product.ActivityDistance = 0;
        }

        public FloorPoint CurrentCarrierPoint()
        {
            if (Phase != CarrierPhase.EmptyTravel && Phase != CarrierPhase.LoadedTravel) return CarrierPosition;
            double remaining = Math.Max(0, Time - PhaseStart) * Config.Speed;
            for (int i = 1; i < CarrierPath.Count; i++)
            {
                var a = CarrierPath[i - 1]; var b = CarrierPath[i]; double leg = a.Distance(b);
                if (remaining <= leg) { double t = leg > 0 ? remaining / leg : 0; return new FloorPoint(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t); }
                remaining -= leg;
            }
            return CarrierPath[CarrierPath.Count - 1];
        }

        public void AssertInvariants()
        {
            var owners = new HashSet<int>();
            Action<FactoryProduct> own = p => { if (p != null && !owners.Add(p.Index)) throw new InvalidOperationException("产品重复占位: " + p.Id); };
            for (int s = 1; s <= 4; s++)
            {
                var state = Stations[s];
                if (state.Reserved < 0 || state.Input.Count + state.Reserved > Config.Stations[s].Capacity)
                    throw new InvalidOperationException("输入缓冲容量越界");
                own(state.Processing); own(state.Output); foreach (var p in state.Input) own(p);
            }
            // During empty travel/loading the product still belongs to its source output slot.
            if (Cargo != null && (source == 0 || Phase == CarrierPhase.LoadedTravel || Phase == CarrierPhase.Unloading)) own(Cargo);
            for (int i = issued; i < Products.Length; i++) own(Products[i]);
            foreach (var p in Products.Where(p => p.FinishedAt.HasValue)) own(p);
            if (owners.Count != Products.Length || FinishedCount != Products.Count(p => p.FinishedAt.HasValue))
                throw new InvalidOperationException("产品数量不守恒");
        }
    }
}
