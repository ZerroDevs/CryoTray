using System;
using System.Collections.Generic;

namespace CryoTray.Services
{
    [Flags]
    public enum TelemetryDemand
    {
        None = 0,
        Dashboard = 1,
        Overlay = 2,
        Settings = 4,
        SystemInfo = 8,
        StressTest = 16,
        Csv = 32,
        Tray = 64,
        Alerts = 128,
        Memory = 256
    }

    /// <summary>
    /// Reference-counted demand from windows and services. Baseline demand is owned by App,
    /// while short-lived windows lease their own flags so closing one cannot lower another.
    /// </summary>
    public sealed class TelemetryDemandTracker
    {
        private static TelemetryDemandTracker? _instance;

        public static TelemetryDemandTracker Instance => _instance ??= new TelemetryDemandTracker();

        private readonly object _lock = new();
        private readonly Dictionary<long, Lease> _leases = new();
        private long _nextLeaseId;
        private TelemetryDemand _baseline;

        public TelemetryDemand Current
        {
            get
            {
                lock (_lock)
                {
                    TelemetryDemand current = _baseline;
                    foreach (Lease lease in _leases.Values)
                    {
                        current |= lease.Demand;
                    }

                    return current;
                }
            }
        }

        public TelemetryDemand Baseline
        {
            get
            {
                lock (_lock)
                {
                    return _baseline;
                }
            }
        }

        public IDisposable Acquire(string owner, TelemetryDemand demand)
        {
            if (demand == TelemetryDemand.None)
            {
                return new LeaseHandle(this, 0);
            }

            long id;
            lock (_lock)
            {
                id = ++_nextLeaseId;
                _leases[id] = new Lease(owner ?? "", demand);
            }

            return new LeaseHandle(this, id);
        }

        public void SetBaseline(TelemetryDemand demand)
        {
            lock (_lock)
            {
                _baseline = demand;
            }
        }

        private void Release(long id)
        {
            if (id == 0)
            {
                return;
            }

            lock (_lock)
            {
                _leases.Remove(id);
            }
        }

        private sealed class Lease
        {
            public Lease(string owner, TelemetryDemand demand)
            {
                Owner = owner;
                Demand = demand;
            }

            public string Owner { get; }
            public TelemetryDemand Demand { get; }
        }

        private sealed class LeaseHandle : IDisposable
        {
            private readonly TelemetryDemandTracker _tracker;
            private readonly long _id;
            private bool _disposed;

            public LeaseHandle(TelemetryDemandTracker tracker, long id)
            {
                _tracker = tracker;
                _id = id;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _tracker.Release(_id);
            }
        }
    }
}
