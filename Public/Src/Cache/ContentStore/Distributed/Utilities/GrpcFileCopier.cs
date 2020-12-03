// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier which operates over Grpc. <seealso cref="GrpcCopyClient"/>
    /// </summary>
    public class GrpcFileCopier : IRemoteFileCopier, IContentCommunicationManager, IDisposable
    {
        private static readonly Tracer Tracer = new Tracer(nameof(GrpcFileCopier));

        private readonly Context _context;
        private readonly GrpcFileCopierConfiguration _configuration;

        private const string GrpcUriSchemePrefix = "grpc://";

        private readonly GrpcCopyClientCache _clientCache;

        private readonly IReadOnlyDictionary<AbsolutePath, AbsolutePath> _junctionsByDirectory;

        /// <summary>
        /// The resolved DNS host name or local machine name
        /// </summary>
        private readonly string _localMachineName;

        /// <summary>
        /// Extract the host name from an AbsolutePath's segments.
        /// </summary>
        public static string GetHostName(bool isLocal, IReadOnlyList<string> segments)
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return isLocal ? "localhost" : segments.First();
            }
            else
            {
                // Linux always uses the first segment as the host name.
                return segments.First();
            }
        }

        /// <summary>
        /// Constructor for <see cref="GrpcFileCopier"/>.
        /// </summary>
        public GrpcFileCopier(Context context, GrpcFileCopierConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _clientCache = new GrpcCopyClientCache(context, _configuration.GrpcCopyClientCacheConfiguration);

            _junctionsByDirectory = configuration.JunctionsByDirectory?.ToDictionary(kvp => new AbsolutePath(kvp.Key), kvp => new AbsolutePath(kvp.Value)) ?? new Dictionary<AbsolutePath, AbsolutePath>();

            try
            {
                _localMachineName = System.Net.Dns.GetHostName();
            }
            catch (Exception e)
            {
                Tracer.Warning(context, $"Failed to get machine name from `Dns.GetHostName`. Falling back to `Environment.MachineName`. {e}");
                _localMachineName = Environment.MachineName;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _clientCache.Dispose();
        }

        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(OperationContext context, ContentLocation sourceLocation)
        {
            // Extract host and port from machine location
            (string host, int port) = ExtractHostInfo(sourceLocation.Machine);

            return _clientCache.UseAsync(context, host, port, (nestedContext, client) => client.CheckFileExistsAsync(nestedContext, sourceLocation.Hash));
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyToAsync(
            OperationContext context,
            ContentLocation sourceLocation,
            Stream destinationStream,
            CopyOptions options)
        {
            // Extract host and port from machine location
            (string host, int port) = ExtractHostInfo(sourceLocation.Machine);

            // Contact hard-coded port on source
            try
            {
                // ResourcePoolV2 may throw TimeoutException if the connection fails.
                // Wrapping this error and converting it to an "error code".

                return await _clientCache.UseWithInvalidationAsync(context, host, _configuration.GrpcPort, async (nestedContext, clientWrapper) =>
                {
                    var result = await clientWrapper.Value.CopyToAsync(nestedContext, sourceLocation.Hash, destinationStream, options);
                    InvalidateResourceIfNeeded(nestedContext, options, result, clientWrapper);
                    return result;
                });
            }
            catch (ResultPropagationException e)
            {
                if (e.Result.Exception != null)
                {
                    return GrpcCopyClient.CreateResultFromException(e.Result.Exception);
                }

                return new CopyFileResult(CopyResultCode.Unknown, e.Result);
            }
            catch (Exception e)
            {
                return new CopyFileResult(CopyResultCode.Unknown, e);
            }
        }

        private void InvalidateResourceIfNeeded(Context context, CopyOptions options, CopyFileResult result, IResourceWrapperAdapter<GrpcCopyClient> clientWrapper)
        {
            if (!result)
            {
                switch (_configuration.GrpcCopyClientInvalidationPolicy)
                {
                    case GrpcFileCopierConfiguration.ClientInvalidationPolicy.Disabled:
                        break;
                    case GrpcFileCopierConfiguration.ClientInvalidationPolicy.OnEveryError:
                        clientWrapper.Invalidate(context);
                        break;
                    case GrpcFileCopierConfiguration.ClientInvalidationPolicy.OnConnectivityErrors:
                        if ((result.Code == CopyResultCode.CopyBandwidthTimeoutError && options.TotalBytesCopied == 0) ||
                            result.Code == CopyResultCode.ConnectionTimeoutError)
                        {
                            if (options?.BandwidthConfiguration?.InvalidateOnTimeoutError ?? true)
                            {
                                clientWrapper.Invalidate(context);
                            }
                        }

                        break;
                }
            }
        }

        /// <inheritdoc />
        public Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream, MachineLocation targetMachine, CopyOptions options)
        {
            (string host, int port) = ExtractHostInfo(targetMachine);

            return _clientCache.UseAsync(context, host, port, (nestedContext, client) => client.PushFileAsync(nestedContext, hash, stream, options));
        }

        /// <inheritdoc />
        public Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            (string host, int port) = ExtractHostInfo(targetMachine);

            return _clientCache.UseAsync(context, host, port, (nestedContext, client) => client.RequestCopyFileAsync(nestedContext, hash));
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            (string host, int port) = ExtractHostInfo(targetMachine);

            using (var client = new GrpcContentClient(
                new ServiceClientContentSessionTracer(nameof(ServiceClientContentSessionTracer)),
                new PassThroughFileSystem(),
                new ServiceClientRpcConfiguration(port) { GrpcHost = host },
                scenario: string.Empty))
            {
                return await client.DeleteContentAsync(context, hash, deleteLocalOnly: true);
            }
        }

        private (string host, int port) ExtractHostInfo(MachineLocation machineLocation)
        {
            var path = machineLocation.Path;
            if (path.StartsWith(GrpcUriSchemePrefix))
            {
                // This is a uri format machine location
                var uri = new Uri(path);
                return (uri.Host, uri.Port);
            }

            var sourcePath = new AbsolutePath(path);

            // TODO: Keep the segments in the AbsolutePath object?
            // TODO: Indexable structure?
            var segments = sourcePath.GetSegments();
            Contract.Assert(segments.Count >= 4);

            string host = GetHostName(sourcePath.IsLocal, segments);

            return (host, _configuration.GrpcPort);
        }

        /// <inheritdoc />
        public MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot)
        {
            if (_configuration.UseUniversalLocations)
            {
                return new MachineLocation($"{GrpcUriSchemePrefix}{_localMachineName}:{_configuration.GrpcPort}/");
            }

            if (!cacheRoot.IsLocal)
            {
                throw new ArgumentException($"Local cache root must be a local path. Found {cacheRoot}.");
            }

            if (!cacheRoot.GetFileName().Equals(Constants.SharedDirectoryName))
            {
                cacheRoot = cacheRoot / Constants.SharedDirectoryName;
            }

            var cacheRootString = cacheRoot.Path.ToUpperInvariant();

            // Determine if cacheRoot needs to be accessed through its directory junction
            var directories = _junctionsByDirectory.Keys;
            var directoryToReplace = directories.SingleOrDefault(directory =>
                                        cacheRootString.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase));

            if (directoryToReplace != null)
            {
                // Replace directory with its junction
                var junction = _junctionsByDirectory[directoryToReplace];
                cacheRootString = cacheRootString.Replace(directoryToReplace.Path.ToUpperInvariant(), junction.Path);
            }

            string networkPathRoot = null;
            if (OperatingSystemHelper.IsWindowsOS)
            {
                // Only unify paths along casing if on Windows
                networkPathRoot = Path.Combine(@"\\" + _localMachineName, cacheRootString.Replace(":", "$"));
            }
            else
            {
                // Path.Combine ignores the first parameter if the second is a rooted path. To get the machine name before the rooted network path, the combination must be done manually.
                networkPathRoot = Path.Combine(Path.DirectorySeparatorChar + _localMachineName, cacheRootString.TrimStart(Path.DirectorySeparatorChar));
            }

            return new MachineLocation(networkPathRoot.ToUpperInvariant());
        }
    }
}
