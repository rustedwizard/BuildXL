// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Fingerprint computation results
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ProcessFingerprintComputationEventData : IExecutionLogEventData<ProcessFingerprintComputationEventData>
    {
        /// <summary>
        /// The fingerprint computation kind
        /// </summary>
        public FingerprintComputationKind Kind;

        /// <summary>
        /// The pip ID
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// The weak fingerprint for the pip
        /// </summary>
        public WeakContentFingerprint WeakFingerprint;

        /// <summary>
        /// The strong fingerprints computations
        /// </summary>
        public IReadOnlyList<ProcessStrongFingerprintComputationData> StrongFingerprintComputations;

        /// <summary>
        /// Session id of the build who creates the cache entry corresponding to this fingerprint.
        /// </summary>
        public string SessionId;

        /// <summary>
        /// Related session id of the build who creates the cache entry corresponding to this fingerprint.
        /// </summary>
        public string RelatedSessionId;

        /// <summary>
        /// Whether the serialized path set is not normalized wrt casing
        /// </summary>
        public bool PreservePathSetCasing;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<ProcessFingerprintComputationEventData> Metadata => ExecutionLogMetadata.ProcessFingerprintComputation;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.WriteCompact(PipId.Value);
            writer.WriteCompact((int)Kind);
            writer.Write(WeakFingerprint);
            writer.WriteNullableString(SessionId);
            writer.WriteNullableString(RelatedSessionId);
            writer.Write(PreservePathSetCasing);
            writer.WriteReadOnlyList(StrongFingerprintComputations, (w, v) => v.Serialize((BinaryLogger.EventWriter)w));
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            PipId = new PipId(reader.ReadUInt32Compact());
            Kind = (FingerprintComputationKind)reader.ReadInt32Compact();
            WeakFingerprint = reader.ReadWeakFingerprint();
            SessionId = reader.ReadNullableString();
            RelatedSessionId = reader.ReadNullableString();
            PreservePathSetCasing = reader.ReadBoolean();
            StrongFingerprintComputations = reader.ReadReadOnlyList(r => new ProcessStrongFingerprintComputationData((BinaryLogReader.EventReader)r));
        }
    }

    /// <summary>
    /// Describes the fingerprint computation result
    /// </summary>
    public enum FingerprintComputationKind
    {
        /// <summary>
        /// Indicates the fingerprint was computed as a part of post execution caching
        /// </summary>
        Execution,

        /// <summary>
        /// Indicates the fingerprint was computed to check whether pip could be run from cache
        /// </summary>
        CacheCheck,

        /// <summary>
        /// Indicates the fingerprint was computed but not for post execution caching (non cacheable successful pips)
        /// </summary>
        ExecutionNotCacheable,

        /// <summary>
        /// Indicates the fingerprint was computed but not for post execution caching (failed pips)
        /// </summary>
        ExecutionFailed,
    }

    /// <summary>
    /// Data associated with strong fingerprint computation
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ProcessStrongFingerprintComputationData : IFingerprintInputCollection
    {
        /// <summary>
        /// Observed path set
        /// </summary>
        public ObservedPathSet PathSet { get; private set; }

        /// <summary>
        /// The computed hash of the path set
        /// </summary>
        public ContentHash PathSetHash { get; private set; }

        /// <summary>
        /// The observed path entries
        /// </summary>
        public ReadOnlyArray<ObservedPathEntry> PathEntries => PathSet.Paths.BaseArray;

        /// <summary>
        /// Unsafe options used during pip execution.
        /// </summary>
        public UnsafeOptions UnsafeOptions => PathSet.UnsafeOptions;

        /// <summary>
        /// Observed accesses file names
        /// </summary>
        public ReadOnlyArray<StringId> ObservedAccessedFileNames => PathSet.ObservedAccessedFileNames.BaseArray;

        /// <summary>
        /// The strong fingerprint computed for the path set from the prior build
        /// </summary>
        public IReadOnlyList<StrongContentFingerprint> PriorStrongFingerprints { get; private set; }

        /// <summary>
        /// Gets whether the strong fingerprint computation succeeded (note there might still be a cache miss)
        /// Check <see cref="IsStrongFingerprintHit"/> to see if computation was a cache hit
        /// </summary>
        public bool Succeeded { get; private set; }

        // The fields below are only set for successful case

        /// <summary>
        /// Gets whether the strong fingerprint matched the expected strong fingerprint
        /// </summary>
        public bool IsStrongFingerprintHit { get; set; }

        /// <summary>
        /// The computed strong fingerprint
        /// </summary>
        public StrongContentFingerprint ComputedStrongFingerprint { get; private set; }

        /// <summary>
        /// The observed inputs
        /// </summary>
        public ReadOnlyArray<ObservedInput> ObservedInputs { get; private set; }

        /// <summary>
        /// Indicates that this instance of <see cref="ProcessStrongFingerprintComputationData"/> is the result of publishing a new augmented weak fingerprint.
        /// </summary>
        public bool IsNewlyPublishedAugmentedWeakFingerprint { get; set; }

        /// <summary>
        /// Associated augmented weak fingerprint if any.
        /// </summary>
        public WeakContentFingerprint? AugmentedWeakFingerprint { get; set; }

        /// <nodoc />
        public ProcessStrongFingerprintComputationData(
            ContentHash pathSetHash,
            List<StrongContentFingerprint> priorStrongFingerprints,
            ObservedPathSet pathSet)
        {
            PathSetHash = pathSetHash;
            PathSet = pathSet;
            PriorStrongFingerprints = priorStrongFingerprints;

            // Initial defaults
            Succeeded = false;
            IsStrongFingerprintHit = false;
            ObservedInputs = default;
            ComputedStrongFingerprint = default;
            IsNewlyPublishedAugmentedWeakFingerprint = false;
            AugmentedWeakFingerprint = default;
        }

        /// <summary>
        /// Creates a strong fingerprint computation for process execution
        /// </summary>
        public static ProcessStrongFingerprintComputationData CreateForExecution(
            ContentHash pathSetHash,
            ObservedPathSet pathSet,
            ReadOnlyArray<ObservedInput> observedInputs,
            StrongContentFingerprint strongFingerprint)
        {
            var data = default(ProcessStrongFingerprintComputationData);
            data.PathSetHash = pathSetHash;
            data.PathSet = pathSet;
            data.PriorStrongFingerprints = CollectionUtilities.EmptyArray<StrongContentFingerprint>();

            // Initial defaults
            data.Succeeded = true;
            data.IsStrongFingerprintHit = false;
            data.ObservedInputs = observedInputs;
            data.ComputedStrongFingerprint = strongFingerprint;
            data.IsNewlyPublishedAugmentedWeakFingerprint = false;
            data.AugmentedWeakFingerprint = default;

            return data;
        }

        /// <summary>
        /// Clones the current strong fingerprint computation replacing the path set and observed inputs.
        /// Internal use only for converting data to refer to different graph data structures.
        /// </summary>
        internal ProcessStrongFingerprintComputationData UnsafeOverride(
            ObservedPathSet pathSet,
            ReadOnlyArray<ObservedInput> observedInputs)
        {
            var data = this;
            // a new PathSet might have a hash than is different from the one recorded in PathSetHash
            data.PathSet = pathSet;
            data.ObservedInputs = observedInputs;
            return data;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public ProcessStrongFingerprintComputationData(BinaryLogReader.EventReader reader)
        {
            PathSetHash = reader.ReadContentHash();
            var maybePathSet = ObservedPathSet.TryDeserialize(reader.PathTable, reader, pathReader: r => r.ReadAbsolutePath(), stringReader: r => ((BinaryLogReader.EventReader)r).ReadDynamicStringId());
            if (!maybePathSet.Succeeded)
            {
                // For ease of debugging, throw the underlying failure instead of letting the Contract exception from looking at the result below bubble up.
                maybePathSet.Failure.Throw();
            }
            
            PathSet = maybePathSet.Result;
            PriorStrongFingerprints = reader.ReadReadOnlyList(r => r.ReadStrongFingerprint());
            Succeeded = reader.ReadBoolean();

            if (Succeeded)
            {
                IsStrongFingerprintHit = reader.ReadBoolean();
                ObservedInputs = reader.ReadReadOnlyArray(r => ObservedInput.Deserialize(r));
                ComputedStrongFingerprint = reader.ReadStrongFingerprint();
                IsNewlyPublishedAugmentedWeakFingerprint = reader.ReadBoolean();
                AugmentedWeakFingerprint = reader.ReadNullableStruct(r => r.ReadWeakFingerprint());
            }
            else
            {
                IsStrongFingerprintHit = false;
                ObservedInputs = default;
                ComputedStrongFingerprint = default;
                IsNewlyPublishedAugmentedWeakFingerprint = false;
                AugmentedWeakFingerprint = default;
            }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.Write(PathSetHash);
            PathSet.Serialize(
                writer.PathTable,
                writer,
                preserveCasing: false,
                pathWriter: (w, v) => w.Write(v),
                stringWriter: (w, v) => ((BinaryLogger.EventWriter)w).WriteDynamicStringId(v));
            writer.WriteReadOnlyList(PriorStrongFingerprints, (w, v) => w.Write(v));
            writer.Write(Succeeded);

            if (Succeeded)
            {
                writer.Write(IsStrongFingerprintHit);
                writer.Write(ObservedInputs, (w, v) => v.Serialize(w));
                writer.Write(ComputedStrongFingerprint);
                writer.Write(IsNewlyPublishedAugmentedWeakFingerprint);
                writer.Write(AugmentedWeakFingerprint, (w, v) => w.Write(v));
            }
        }

        /// <summary>
        /// Gets a copy with successfully computed strong fingerprint and observed inputs
        /// </summary>
        public ProcessStrongFingerprintComputationData ToSuccessfulResult(
            StrongContentFingerprint computedStrongFingerprint,
            ReadOnlyArray<ObservedInput> observedInputs)
        {
            // Copy by value
            var result = this;
            result.Succeeded = true;
            result.ComputedStrongFingerprint = computedStrongFingerprint;
            result.ObservedInputs = observedInputs;
            return result;
        }

        /// <summary>
        /// Adds a fingerprint to the list of prior strong fingerprints requiring that it is initialized to a mutable value
        /// </summary>
        internal void AddPriorStrongFingerprint(StrongContentFingerprint priorFingerprint)
        {
            var priorFingerprints = PriorStrongFingerprints as List<StrongContentFingerprint>;
            Contract.Assert(priorFingerprints != null, "Attempted to add prior strong fingerprint to immutable computation data");
            priorFingerprints.Add(priorFingerprint);
        }

        /// <inheritdoc />
        public void WriteFingerprintInputs(IFingerprinter writer)
        {
            writer.AddNested(ObservedPathSet.Labels.UnsafeOptions, UnsafeOptions.ComputeFingerprint);

            var thisRef = this;
            writer.AddNested(ObservedPathSet.Labels.Paths, w =>
            {
                foreach (var p in thisRef.PathEntries)
                {
                    w.Add(ObservedPathEntryConstants.Path, p.Path);
                    if (p.Flags != ObservedPathEntryFlags.None)
                    {
                        w.Add(ObservedPathEntryConstants.Flags, p.Flags.ToString());
                    }
                    if (p.EnumeratePatternRegex != null)
                    {
                        w.Add(ObservedPathEntryConstants.EnumeratePatternRegex, p.EnumeratePatternRegex);
                    }
                }
            });

            writer.AddCollection<StringId, ReadOnlyArray<StringId>>(
                ObservedPathSet.Labels.ObservedAccessedFileNames,
                ObservedAccessedFileNames,
                (w, v) => w.AddFileName(v));

            // Observed inputs are included directly into the strong fingerprint hash computation,
            // so they do not need to be serialized here
        }
    }
}
