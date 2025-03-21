// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.PerformanceCollector.Aggregator;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Provides high level breakdown for some internal scheduler performance.
    /// </summary>
    public sealed class SchedulerPerformanceInfo
    {
        /// <nodoc/>
        public long ExecuteProcessDurationMs;

        /// <nodoc/>
        public long ProcessOutputsObservedInputValidationDurationMs;

        /// <nodoc/>
        public long ProcessOutputsStoreContentForProcessAndCreateCacheEntryDurationMs;

        /// <nodoc/>
        public long CanceledProcessExecuteDurationMs;

        /// <nodoc/>
        public long MachineMinimumAvailablePhysicalMB;

        /// <nodoc/>
        public long ProcessPipCacheMisses;

        /// <nodoc/>
        public long ProcessPipCacheHits;

        /// <nodoc/>
        public long ProcessPipIncrementalSchedulingPruned;

        /// <nodoc/>
        public long TotalProcessPips;

        /// <nodoc/>
        public long ProcessPipsUncacheable;

        /// <nodoc/>
        public long CriticalPathTableHits;

        /// <nodoc/>
        public long CriticalPathTableMisses;

        /// <nodoc/>
        public long RunProcessFromCacheDurationMs;

        /// <nodoc/>
        public long RunProcessFromRemoteCacheDurationMs;

        /// <nodoc/>
        public long SandboxedProcessPrepDurationMs;

        /// <nodoc/>
        public FileContentStats FileContentStats;

        /// <nodoc/>
        public long OutputsProduced => FileContentStats.OutputsProduced;

        /// <nodoc/>
        public long OutputsDeployed => FileContentStats.OutputsDeployed;

        /// <nodoc/>
        public long OutputsUpToDate => FileContentStats.OutputsUpToDate;

        /// <nodoc/>
        public CounterCollection<PipExecutionStep> PipExecutionStepCounters;

        /// <nodoc/>
        public int AverageMachineCPU;

        /// <nodoc/>
        public IReadOnlyCollection<DiskStatistics> DiskStatistics;

        /// <nodoc/>
        public PipCountersByTelemetryTag ProcessPipCountersByTelemetryTag;
    }
}
