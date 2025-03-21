// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Test.Stores;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.Tests
{
    [Collection("Redis-based tests")]
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class BlobWithLocalCacheTests : TestWithOutput
    {
        private readonly LocalRedisFixture _fixture;
        private readonly BuildXLContext _context;

        protected string ScratchPath => Path.Combine(Path.GetTempPath(), GetType().ToString());

        public BlobWithLocalCacheTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
            _context = BuildXLContext.CreateInstanceForTesting();
        }

        [Fact]
        public async Task TestEndToEnd()
        {
            var (config, logPath) = SetupTest(nameof(TestEndToEnd));
            FullCacheRecord cacheRecord = await CreateCacheAndRunPip(config);

            // This check serves two purposes: we want to check the log file is properly setup and reflects both local and remote cache activity.
            // We also want to check that the fake build resulted in adding (a CompareExchange call) content to both caches.
#if NET
            var lines = await File.ReadAllLinesAsync(logPath);
#else
            var lines = File.ReadAllLines(logPath);
#endif
            Assert.True(lines.Any(line => line.Contains(cacheRecord.StrongFingerprint.WeakFingerprint.ToString()) && line.Contains("AzureBlobStorageMetadataStore.CompareExchangeAsync")));
            Assert.True(lines.Any(line => line.Contains(cacheRecord.StrongFingerprint.WeakFingerprint.ToString()) && line.Contains("RocksDbMemoizationDatabase.CompareExchangeAsync")));
        }

        [Fact]
        public async Task TestRemoteIsReadonly()
        {
            var (config, logPath) = SetupTest(nameof(TestRemoteIsReadonly), remoteIsReadOnly: true);
            FullCacheRecord cacheRecord = await CreateCacheAndRunPip(config);

            // We want to check that the fake build resulted in adding (a CompareExchange call) content to the local cache, but not to the remote cache.
#if NET
            var lines = await File.ReadAllLinesAsync(logPath);
#else
            var lines = File.ReadAllLines(logPath);
#endif
            Assert.False(lines.Any(line => line.Contains(cacheRecord.StrongFingerprint.WeakFingerprint.ToString()) && line.Contains("AzureBlobStorageMetadataStore.CompareExchangeAsync")));
            Assert.True(lines.Any(line => line.Contains(cacheRecord.StrongFingerprint.WeakFingerprint.ToString()) && line.Contains("RocksDbMemoizationDatabase.CompareExchangeAsync")));
        }

        private async Task<FullCacheRecord> CreateCacheAndRunPip(string config)
        {
            ICache? cache = null;
            FullCacheRecord? cacheRecord = null;
            try
            {
                var configuration = new ConfigurationImpl();
                cache = await CacheFactory.InitializeCacheAsync(config, default(Guid), configuration, _context).SuccessAsync();
                var session = await cache.CreateSessionAsync().SuccessAsync();
                cacheRecord = await FakeBuild.DoPipAsync(session, "TestPip");

                await session.CloseAsync().SuccessAsync();
            }
            finally
            {
                if (cache != null)
                {
                    await cache.ShutdownAsync().SuccessAsync();
                }
            }

            return cacheRecord;
        }

        /// <summary>
        /// Each test will get clean caches, for both remote and local. The universe is based on the test name.
        /// </summary>
        private (string configuration, string logPath) SetupTest(string testName, bool remoteIsReadOnly = false)
        {
            // Start emulator to get a blob storage account
            var shards = Enumerable.Range(0, 10).Select(shard => (BlobCacheStorageAccountName)new BlobCacheStorageShardingAccountName("0123456789", shard, "testing")).ToList();

            // Force it to use a non-sharding account
            shards.Add(new BlobCacheStorageNonShardingAccountName("devstoreaccount1"));

            using var process = AzuriteStorageProcess.CreateAndStart(
                _fixture,
                TestGlobal.Logger,
                accounts: shards.Select(account => account.AccountName).ToList());

            IReadOnlyList<BuildCacheConfiguration> configuration = [BuildCacheConfigurationSecretGenerator.GenerateConfigurationFrom("default", process, shards)];

            Directory.CreateDirectory(Path.Combine(ScratchPath, testName));

            var buildCacheConfigPath = Path.Combine(ScratchPath, testName, "BuildCacheConfiguration.json");
            File.WriteAllText(buildCacheConfigPath, System.Text.Json.JsonSerializer.Serialize(configuration));
            var buildCacheConfigurationPathEnvVarName = $"BlobCacheFactoryHostedPoolConfigurationFile{testName}";
            Environment.SetEnvironmentVariable(buildCacheConfigurationPathEnvVarName, buildCacheConfigPath);

            var logDir = Path.Combine(ScratchPath, testName, "logs");
            var baseLogPath = Path.Combine(logDir, "blobWithLocal");
            var rootDir = Path.Combine(ScratchPath, testName, "root");

            var config = $@"{{
  ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
  ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.BlobWithLocalCacheFactory"",
  ""LocalCache"": {{
    ""MaxCacheSizeInMB"": 40480,
    ""CacheLogPath"": ""{baseLogPath.Replace("\\", "\\\\")}.local.log"",
    ""CacheRootPath"": ""{rootDir.Replace("\\", "\\\\")}"",
    ""CacheId"": ""TestLocal""
  }},
  ""RemoteCache"": {{
    ""CacheLogPath"": ""{baseLogPath.Replace("\\", "\\\\")}"",
    ""CacheId"": ""TestBlob"",
    ""Universe"" : ""{testName.ToLowerInvariant()}"",
    ""RetentionPolicyInDays"": 6,
    ""HostedPoolBuildCacheConfigurationFileEnvironmentVariableName"" : ""{buildCacheConfigurationPathEnvVarName}"",
    ""ConnectionStringFileDataProtectionEncrypted"": false
    {(remoteIsReadOnly ? @",""IsReadOnly"" : true" : string.Empty)}
  }}
}}
";
            return (config, baseLogPath);

        }
    }
}
