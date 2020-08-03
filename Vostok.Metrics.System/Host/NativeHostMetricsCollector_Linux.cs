﻿using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vostok.Metrics.System.Helpers;

// ReSharper disable PossibleInvalidOperationException

namespace Vostok.Metrics.System.Host
{
    internal class NativeHostMetricsCollector_Linux : IDisposable
    {
        private readonly Regex pidRegex = new Regex("[0-9]+$", RegexOptions.Compiled);
        private readonly ReusableFileReader systemStatReader = new ReusableFileReader("/proc/stat");
        private readonly ReusableFileReader memoryReader = new ReusableFileReader("/proc/meminfo");
        private readonly ReusableFileReader descriptorInfoReader = new ReusableFileReader("/proc/sys/fs/file-nr");
        private readonly HostCpuUtilizationCollector cpuCollector = new HostCpuUtilizationCollector();

        public void Dispose()
        {
            systemStatReader.Dispose();
            memoryReader.Dispose();
            descriptorInfoReader.Dispose();
        }

        public void Collect(HostMetrics metrics)
        {
            var systemStat = ReadSystemStat();
            var memInfo = ReadMemoryInfo();
            var perfInfo = ReadPerformanceInfo();

            if (systemStat.Filled)
            {
                var usedTime = systemStat.UserTime.Value + systemStat.NicedTime.Value +
                               systemStat.SystemTime.Value + systemStat.IdleTime.Value;

                cpuCollector.Collect(metrics, usedTime, systemStat.IdleTime.Value);
            }

            if (memInfo.Filled)
            {
                metrics.MemoryAvailable = memInfo.AvailableMemory.Value;
                metrics.MemoryCached = memInfo.CacheMemory.Value;
                metrics.MemoryKernel = memInfo.KernelMemory.Value;
                metrics.MemoryTotal = memInfo.TotalMemory.Value;
            }

            if (perfInfo.Filled)
            {
                metrics.HandleCount = perfInfo.HandleCount.Value;
                metrics.ThreadCount = perfInfo.ThreadCount.Value;
                metrics.ProcessCount = perfInfo.ProcessCount.Value;
            }
        }

        private SystemStat ReadSystemStat()
        {
            var result = new SystemStat();

            try
            {
                if (FileParser.TrySplitLine(systemStatReader.ReadFirstLine(), 7, out var parts) && parts[0] == "cpu")
                {
                    if (ulong.TryParse(parts[1], out var utime))
                        result.UserTime = utime;

                    if (ulong.TryParse(parts[2], out var ntime))
                        result.NicedTime = ntime;

                    if (ulong.TryParse(parts[3], out var stime))
                        result.SystemTime = stime;

                    if (ulong.TryParse(parts[4], out var itime))
                        result.IdleTime = itime;
                }
            }
            catch (Exception error)
            {
                InternalErrorLogger.Warn(error);
            }

            return result;
        }

        private MemoryInfo ReadMemoryInfo()
        {
            var result = new MemoryInfo();

            try
            {
                foreach (var line in memoryReader.ReadLines())
                {
                    if (FileParser.TryParseLong(line, "MemTotal", out var memTotal))
                        result.TotalMemory = memTotal * 1024;

                    if (FileParser.TryParseLong(line, "MemAvailable", out var memAvailable))
                        result.AvailableMemory = memAvailable * 1024;

                    if (FileParser.TryParseLong(line, "Cached", out var memCached))
                        result.CacheMemory = memCached * 1024;

                    if (FileParser.TryParseLong(line, "Slab", out var memKernel))
                        result.KernelMemory = memKernel * 1024;
                }
            }
            catch (Exception error)
            {
                InternalErrorLogger.Warn(error);
            }

            return result;
        }

        private PerformanceInfo ReadPerformanceInfo()
        {
            var result = new PerformanceInfo();

            try
            {
                var processDirectories = Directory.EnumerateDirectories("/proc/")
                    .Where(x => pidRegex.IsMatch(x));

                var processCount = 0;
                var threadCount = 0;

                foreach (var processDirectory in processDirectories)
                {
                    processCount++;
                    threadCount += Directory.EnumerateDirectories($"{processDirectory}/task/").Count();
                }

                result.ProcessCount = processCount;
                result.ThreadCount = threadCount;

                if (FileParser.TrySplitLine(descriptorInfoReader.ReadFirstLine(), 3, out var parts) &&
                    int.TryParse(parts[0], out var allocatedDescriptors) &&
                    int.TryParse(parts[1], out var freeDescriptors))
                    result.HandleCount = allocatedDescriptors - freeDescriptors;
            }
            catch (Exception error)
            {
                InternalErrorLogger.Warn(error);
            }

            return result;
        }

        private class PerformanceInfo
        {
            public bool Filled => ProcessCount.HasValue && ThreadCount.HasValue && HandleCount.HasValue;

            public int? ProcessCount { get; set; }
            public int? ThreadCount { get; set; }
            public int? HandleCount { get; set; }
        }

        private class SystemStat
        {
            public bool Filled => UserTime.HasValue && NicedTime.HasValue && SystemTime.HasValue && IdleTime.HasValue;

            public ulong? UserTime { get; set; }
            public ulong? NicedTime { get; set; }
            public ulong? SystemTime { get; set; }
            public ulong? IdleTime { get; set; }
        }

        private class MemoryInfo
        {
            public bool Filled => AvailableMemory.HasValue && KernelMemory.HasValue && CacheMemory.HasValue && TotalMemory.HasValue;

            public long? AvailableMemory { get; set; }
            public long? KernelMemory { get; set; }
            public long? CacheMemory { get; set; }
            public long? TotalMemory { get; set; }
        }
    }
}