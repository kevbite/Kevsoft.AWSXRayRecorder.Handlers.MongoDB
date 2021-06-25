using System;
using Amazon.XRay.Recorder.Core.Internal.Entities;

namespace Kevsoft.AWSXRayRecorder.Handlers.MongoDB
{
    struct CachedQuery
    {
        public DateTime CachedAt { get; init; }
        public Entity Entity { get; init; }
    }
}
