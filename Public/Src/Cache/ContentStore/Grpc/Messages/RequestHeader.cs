// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// We can't rename the Protobuff namespace so we'll have to keep these old global namespaces around.
namespace ContentStore.Grpc
{
    /// <nodoc />
    public partial class RequestHeader
    {
        /// <nodoc />
        public RequestHeader(string traceId, int sessionId)
        {
            TraceId = traceId;
            SessionId = sessionId;
        }
    }
}
