using System;
using System.Collections.Generic;

namespace Kevsoft.AWSXRayRecorder.Handlers.MongoDB
{
    public sealed class MongoXRayOptions
    {
        /// <summary>
        /// Mongo commands which will be ignored
        /// </summary>
        public HashSet<string> FilteredCommands { get; init; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "buildInfo",
                "getLastError",
                "isMaster",
                "ping",
                "saslStart",
                "saslContinue"
            };

        /// <summary>
        /// Gets or sets a value indicating whether to track the Mongo command text in MongoDB dependencies.
        /// </summary>
        public bool EnableMongoCommandTextInstrumentation { get; init; } = true;
        
        /// <summary>
        /// The maximum length of time a query may run for before XRay tracing is discarded
        /// This is to prevent memory leaks if the MongoDB driver reports that a query has been
        /// started, but not whether it has succeeded or failed. If you have queries that run for
        /// longer than the default time (4 hours), then you will need to increase this value
        /// to obtain tracing for them.
        /// </summary>
        public TimeSpan MaxQueryTime { get; init; } = new(4, 0, 0);
        
    }
}