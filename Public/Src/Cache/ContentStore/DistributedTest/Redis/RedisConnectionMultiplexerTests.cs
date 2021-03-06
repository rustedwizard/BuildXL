// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif
using Xunit;

namespace ContentStoreTest.Distributed.Redis
{
    public class RedisConnectionMultiplexerTests
    {
        [Fact]
        public void CreateWithFailingProvider()
        {
            var context = new Context(TestGlobal.Logger);
            var errorMessage = "error";
            var failingProvider = CreateFailingProvider(errorMessage);
            Func<Task<IConnectionMultiplexer>> createFunc =
                () => RedisConnectionMultiplexer.CreateAsync(context, failingProvider, logSeverity: BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Unknown, usePreventThreadTheft: false);
            createFunc.Should().Throw<ArgumentException>().Where(e => e.Message.Contains(
                $"Failed to get connection string from provider {failingProvider.GetType().Name}. {errorMessage}."));
        }

        private IConnectionStringProvider CreateFailingProvider(string errorMessage)
        {
            var mockProvider = new TestConnectionStringProvider(errorMessage);
            return mockProvider;
        }

        private class TestConnectionStringProvider : IConnectionStringProvider
        {
            private readonly string _errorMessage;

            public TestConnectionStringProvider(string errorMessage)
            {
                _errorMessage = errorMessage;
            }

            public Task<ConnectionStringResult> GetConnectionString()
            {
                return Task.FromResult(ConnectionStringResult.CreateFailure(_errorMessage));
            }
        }
    }
}
