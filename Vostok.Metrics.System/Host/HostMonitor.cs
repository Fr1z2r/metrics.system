﻿using System;
using System.Collections.Concurrent;
using JetBrains.Annotations;
using Vostok.Metrics.System.Helpers;

namespace Vostok.Metrics.System.Host
{
    /// <summary>
    /// <see cref="HostMonitor"/> provides means to observe <see cref="HostMetrics"/> periodically.
    /// </summary>
    [PublicAPI]
    public class HostMonitor
    {
        private readonly ConcurrentDictionary<TimeSpan, PeriodicObservable<HostMetrics>> observables
            = new ConcurrentDictionary<TimeSpan, PeriodicObservable<HostMetrics>>();

        public IObservable<HostMetrics> ObserveMetrics(TimeSpan period)
            => observables.GetOrAdd(period, p => new PeriodicObservable<HostMetrics>(p, new HostMetricsCollector().Collect));
    }
}