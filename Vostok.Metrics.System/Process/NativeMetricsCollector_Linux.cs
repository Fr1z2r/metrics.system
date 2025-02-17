﻿using System;
using System.IO;
using System.Linq;
using Vostok.Metrics.System.Helpers;

// ReSharper disable PossibleInvalidOperationException

namespace Vostok.Metrics.System.Process
{
    internal class NativeMetricsCollector_Linux : IDisposable
    {
        private readonly ReusableFileReader systemStatReader = new ReusableFileReader("/proc/stat");
        private readonly ReusableFileReader processStatReader = new ReusableFileReader("/proc/self/stat");
        private readonly ReusableFileReader processStatusReader = new ReusableFileReader("/proc/self/status");
        private readonly CpuUtilizationCollector cpuCollector = new CpuUtilizationCollector();

        public void Dispose()
        {
            systemStatReader.Dispose();
            processStatReader.Dispose();
            processStatusReader.Dispose();
        }

        public void Collect(CurrentProcessMetrics metrics)
        {
            var systemStat = ReadSystemStat();
            var processStat = ReadProcessStat();
            var processStatus = ReadProcessStatus();

            if (processStatus.FileDescriptorsCount.HasValue)
                metrics.HandlesCount = processStatus.FileDescriptorsCount.Value;

            if (processStatus.VirtualMemoryResident.HasValue)
                metrics.MemoryResident = processStatus.VirtualMemoryResident.Value;

            if (processStatus.VirtualMemoryData.HasValue)
                metrics.MemoryPrivate = processStatus.VirtualMemoryData.Value;

            if (systemStat.Filled && processStat.Filled)
            {
                var systemTime = systemStat.SystemTime.Value + systemStat.UserTime.Value + systemStat.IdleTime.Value;
                var processTime = processStat.SystemTime.Value + processStat.UserTime.Value;

                cpuCollector.Collect(metrics, systemTime, processTime, systemStat.CpuCount);
            }
        }

        private SystemStat ReadSystemStat()
        {
            var result = new SystemStat();

            try
            {
                if (FileParser.TrySplitLine(systemStatReader.ReadFirstLine(), 5, out var parts) && parts[0] == "cpu")
                {
                    if (ulong.TryParse(parts[1], out var utime))
                        result.UserTime = utime;

                    if (ulong.TryParse(parts[3], out var stime))
                        result.SystemTime = stime;

                    if (ulong.TryParse(parts[4], out var itime))
                        result.IdleTime = itime;
                }

                result.CpuCount = systemStatReader.ReadLines().Count(line => line.StartsWith("cpu")) - 1;
            }
            catch (Exception error)
            {
                InternalErrorLogger.Warn(error);
            }

            return result;
        }

        private ProcessStat ReadProcessStat()
        {
            var result = new ProcessStat();

            try
            {
                if (FileParser.TrySplitLine(processStatReader.ReadFirstLine(), 15, out var parts))
                {
                    if (ulong.TryParse(parts[13], out var utime))
                        result.UserTime = utime;

                    if (ulong.TryParse(parts[14], out var stime))
                        result.SystemTime = stime;
                }
            }
            catch (Exception error)
            {
                InternalErrorLogger.Warn(error);
            }

            return result;
        }

        private ProcessStatus ReadProcessStatus()
        {
            var result = new ProcessStatus();

            try
            {
                result.FileDescriptorsCount = Directory.EnumerateFiles("/proc/self/fd/").Count();
            }
            catch (DirectoryNotFoundException)
            {
                // NOTE: Ignored due to process already exited so we don't have to count it's file descriptors count.
                result.FileDescriptorsCount = 0;
            }

            try
            {
                foreach (var line in processStatusReader.ReadLines())
                {
                    if (FileParser.TryParseLong(line, "RssAnon", out var vmRss))
                        result.VirtualMemoryResident = vmRss * 1024L;

                    if (FileParser.TryParseLong(line, "VmData", out var vmData))
                        result.VirtualMemoryData = vmData * 1024L;

                    if (result.Filled)
                        break;
                }
            }
            catch (Exception error)
            {
                InternalErrorLogger.Warn(error);
            }

            return result;
        }

        private class SystemStat
        {
            public bool Filled => IdleTime.HasValue && UserTime.HasValue && SystemTime.HasValue;

            public int? CpuCount { get; set; }
            public ulong? IdleTime { get; set; }
            public ulong? UserTime { get; set; }
            public ulong? SystemTime { get; set; }
        }

        private class ProcessStat
        {
            public bool Filled => UserTime.HasValue && SystemTime.HasValue;

            public ulong? UserTime { get; set; }
            public ulong? SystemTime { get; set; }
        }

        private class ProcessStatus
        {
            public bool Filled => FileDescriptorsCount.HasValue && VirtualMemoryResident.HasValue && VirtualMemoryData.HasValue;

            public int? FileDescriptorsCount { get; set; }
            public long? VirtualMemoryResident { get; set; }
            public long? VirtualMemoryData { get; set; }
        }
    }
}