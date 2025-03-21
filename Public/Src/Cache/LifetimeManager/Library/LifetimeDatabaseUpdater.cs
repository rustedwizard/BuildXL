﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public class LifetimeDatabaseUpdater
    {
        private static readonly Tracer Tracer = new(nameof(LifetimeDatabaseUpdater));

        private readonly Dictionary<BlobNamespaceId, IBlobCacheTopology> _topologies;
        private readonly Dictionary<BlobNamespaceId, RocksDbLifetimeDatabase.IAccessor> _accessors;
        private readonly IClock _clock;

        private readonly ActionBlockSlim<(LifetimeDatabaseCreator.ProcessFingerprintRequest, TaskSourceSlim<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>>)> _fingerprintCreatedActionBlock;

        public LifetimeDatabaseUpdater(
            Dictionary<BlobNamespaceId, IBlobCacheTopology> topologies,
            Dictionary<BlobNamespaceId, RocksDbLifetimeDatabase.IAccessor> accessors,
            IClock clock,
            int fingerprintsDegreeOfParallelism)
        {
            _topologies = topologies;
            _accessors = accessors;
            _clock = clock;

            _fingerprintCreatedActionBlock = ActionBlockSlim.CreateWithAsyncAction<(LifetimeDatabaseCreator.ProcessFingerprintRequest, TaskSourceSlim<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>>)>(
                new ActionBlockSlimConfiguration(DegreeOfParallelism: fingerprintsDegreeOfParallelism),
                async tpl =>
                {
                    var (request, tcs) = tpl;
                    try
                    {
                        var result = await LifetimeDatabaseCreator.DownloadAndProcessContentHashListAsync(request, clock);

                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetResult(new Result<LifetimeDatabaseCreator.ProcessContentHashListResult>(ex));
                    }
                });
        }

        public void ContentCreated(OperationContext context, AbsoluteBlobPath blobPath, long length)
        {
            var blobName = blobPath.Path.Path;

            if (!_accessors.TryGetValue(blobPath.NamespaceId, out var db))
            {
                Tracer.Diagnostic(context, $"Ignoring creation of {nameof(ContentHash)} because it's namespace isn't tracked. Namespace=[{blobPath.NamespaceId}] Path=[{blobPath}]");
                return;
            }

            if (!BlobUtilities.TryExtractContentHashFromBlobName(blobName, out var hashString))
            {
                Tracer.Error(context, $"Failed to extract {nameof(ContentHash)} from a blob name that's expected to be one. Ignoring blob. Path=[{blobPath}]");
                return;
            }

            if (!ContentHash.TryParse(hashString, out var hash))
            {
                Tracer.Error(context, $"Failed to parse {nameof(ContentHash)} from a blob name that's supposed to be composed of it. Ignoring blob. Path=[{blobPath}]");
                return;
            }

            db.AddContent(hash, length);
        }

        internal record FingerprintCreationEvent(
            OperationContext Context,
            AbsoluteBlobPath BlobPath,
            long BlobLength,
            DateTime EventTimestampUtc)
        {
            public string BlobName => BlobPath.Path.Path;
        }

        internal async Task<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>> ContentHashListCreatedAsync(
            FingerprintCreationEvent fingerprintCreationEvent)
        {
            var (context, blobPath, blobLength, eventTimestampUtc) = fingerprintCreationEvent;
            var blobName = blobPath.Path.Path;
            var namespaceId = blobPath.NamespaceId;

            if (!_accessors.TryGetValue(namespaceId, out var db) ||
                !_topologies.TryGetValue(namespaceId, out var topology))
            {
                Tracer.Diagnostic(context, $"Ignoring creation of {nameof(ContentHashList)} because it's namespace isn't being tracked. Namespace=[{namespaceId}] Path=[{blobPath}]");
                return LifetimeDatabaseCreator.ProcessContentHashListResult.Success;
            }

            StrongFingerprint strongFingerprint;
            try
            {
                strongFingerprint = BlobCacheTopologyExtensions.ExtractStrongFingerprintFromPath(blobName);
            }
            catch (Exception e)
            {
                Tracer.Error(context, e, $"Failed to parse {nameof(StrongFingerprint)} from a blob name that's expected to be one. Ignoring blob. Path=[{blobPath}]");
                return LifetimeDatabaseCreator.ProcessContentHashListResult.ContentHashListDoesNotExist;
            }

            // TODO: this should be moved inside of the async worker, so we avoid hanging the hot path on this
            var oldContentHashList = db.GetContentHashList(strongFingerprint, out _);
            if (oldContentHashList is not null)
            {
                // The CHL was updated. This can happen for various reasons, such as a non-deterministic fingerprint being replaced by a deterministic one,
                // or the build engine failing to match a target to a selector.
                // In any case, we need to make sure that we reflect the fact that the old CHL no longer truly exists.
                db.DeleteContentHashList(blobName, oldContentHashList.Hashes);
            }

            var containerClient = await topology.GetContainerClientAsync(context, strongFingerprint);
            var tcs = TaskSourceSlim.Create<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>>();
            var request = new LifetimeDatabaseCreator.ProcessFingerprintRequest(context, containerClient, fingerprintCreationEvent, db, topology);
            _fingerprintCreatedActionBlock.Post((request, tcs));
            return await tcs.Task;
        }
    }
}
