// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Extension methods for FileSystemContentStoreInternal.
    /// </summary>
    public static class ContentStoreInternalExtensions
    {
        /// <summary>
        ///     Put a randomly-sized piece of content into the store.
        /// </summary>
        public static async Task<PutResult> PutRandomAsync(
            this FileSystemContentStoreInternal store, Context context, int size, HashType hashType = HashType.Vso0, PinRequest pinRequest = default(PinRequest))
        {
            var data = ThreadSafeRandom.GetBytes(size);
            using (var stream = new MemoryStream(data))
            {
                return await store.PutStreamAsync(context, stream, hashType, pinRequest);
            }
        }
    }
}
