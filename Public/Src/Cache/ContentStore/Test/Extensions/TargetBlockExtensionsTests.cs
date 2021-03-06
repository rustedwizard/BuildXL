// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ContentStoreTest.Test;
using BuildXL.Utilities.Collections;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Extensions
{
    public class TargetBlockExtensionsTests : TestBase
    {
        public TargetBlockExtensionsTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task PostAllAndCompleteTest()
        {
            const int size = 100;
            var flags = new bool[size];
            var actionBlock = new ActionBlock<int>(
                i => { flags[i] = true; },
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = size / 7});
            await actionBlock.PostAllAndComplete(Enumerable.Range(0, size));
            flags.All(flag => flag).Should().BeTrue();
        }
    }
}
