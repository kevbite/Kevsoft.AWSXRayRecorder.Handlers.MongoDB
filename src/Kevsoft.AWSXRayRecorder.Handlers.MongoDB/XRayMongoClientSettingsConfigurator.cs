using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using Amazon.Runtime.Internal.Util;
using Amazon.XRay.Recorder.Core.Exceptions;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace Kevsoft.AWSXRayRecorder.Handlers.MongoDB
{
    /// <summary>
    /// XRay Mongo Client Settings Configurator
    /// </summary>
    public static class XRayMongoClientSettingsConfigurator
    {
        private static readonly ConcurrentDictionary<int, CachedQuery> _queryCache =
            new();
        private static long _nextPruneTimeTicks;
        private static readonly Logger _logger = Logger.GetLogger(typeof(XRayMongoClientSettingsConfigurator));

        private static MongoXRayOptions _settings = new();

        /// <summary>
        /// Configures the mongo client settings to trace with XRay
        /// </summary>
        /// <param name="mongoDbSettings"></param>
        /// <returns></returns>
        public static MongoClientSettings ConfigureXRay(this MongoClientSettings mongoDbSettings)
        {
            return ConfigureXRay(mongoDbSettings, new MongoXRayOptions());
        }
        /// <summary>
        /// Configures the mongo client settings to trace with XRay
        /// </summary>
        /// <param name="mongoDbSettings"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static MongoClientSettings ConfigureXRay(this MongoClientSettings mongoDbSettings, MongoXRayOptions settings)
        {
            _settings = settings;
            _nextPruneTimeTicks = DateTime.UtcNow.Add(_settings.MaxQueryTime).Ticks;
            var clone = mongoDbSettings.Clone();
            clone.ClusterConfigurator += clusterConfigurator =>
            {
                clusterConfigurator.Subscribe<CommandStartedEvent>(OnCommandStarted);
                clusterConfigurator.Subscribe<CommandSucceededEvent>(OnCommandSucceeded);
                clusterConfigurator.Subscribe<CommandFailedEvent>(OnCommandFailed);
            };
            return clone;
        }

        private static string FormatEndPoint(EndPoint endPoint)
        {
            if (endPoint is DnsEndPoint dnsEndPoint)
            {
                return $"{dnsEndPoint.Host}:{dnsEndPoint.Port}";
            }

            return endPoint.ToString();
        }

        private static void OnCommandStarted(CommandStartedEvent evt)
        {
            Prune(DateTime.UtcNow);

            if (!CanTraceCommand(evt.CommandName))
                return;

            var instance = GetAwsXRayRecorderInstance();

            try
            {
                var subsegmentName =
                    $"{evt.DatabaseNamespace.DatabaseName}@{FormatEndPoint(evt.ConnectionId.ServerId.EndPoint)}";

                instance.BeginSubsegment(subsegmentName);
                instance.SetNamespace("remote");
                instance.AddAnnotation("database", evt.DatabaseNamespace.DatabaseName);
                instance.AddAnnotation("command_name", evt.CommandName);
                instance.AddAnnotation("endpoint", FormatEndPoint(evt.ConnectionId.ServerId.EndPoint));
                if (_settings.EnableMongoCommandTextInstrumentation)
                {
                    instance.AddAnnotation("command", evt.Command.ToString());
                }
                
                var query = new CachedQuery { CachedAt = DateTime.UtcNow, Entity = instance.TraceContext.GetEntity() };
                _queryCache.TryAdd(evt.RequestId, query);
            }
            catch (EntityNotAvailableException ex)
            {
                instance.TraceContext.HandleEntityMissing(instance, ex,
                    "Failed to get entity since it is not available in trace context while processing mongodb command.");
            }
        }

        private static bool CanTraceCommand(string commandName)
        {
            var instance = GetAwsXRayRecorderInstance();

            if (instance.IsTracingDisabled())
            {
                _logger.DebugFormat("Tracing is disabled. Not starting a subsegment on MongoDB command.",
                    Array.Empty<object>());
                return false;
            }

            if (_settings.FilteredCommands.Contains(commandName))
            {
                _logger.DebugFormat("Command is filtered. Not starting a subsegment on MongoDB command.",
                    Array.Empty<object>());
                return false;
            }

            return true;
        }

        private static Amazon.XRay.Recorder.Core.AWSXRayRecorder GetAwsXRayRecorderInstance()
        {
            return Amazon.XRay.Recorder.Core.AWSXRayRecorder.Instance;
        }

        private static void OnCommandSucceeded(CommandSucceededEvent evt)
        {
            if (!CanTraceCommand(evt.CommandName) || !_queryCache.TryRemove(evt.RequestId, out CachedQuery query))
                return;

            var instance = GetAwsXRayRecorderInstance();
            instance.TraceContext.SetEntity(query.Entity);
            
            instance.AddAnnotation("duration", evt.Duration.ToString());
            instance.EndSubsegment();
        }

        private static void OnCommandFailed(CommandFailedEvent evt)
        {
            if (!CanTraceCommand(evt.CommandName) || !_queryCache.TryRemove(evt.RequestId, out CachedQuery query))
                return;

            var instance = GetAwsXRayRecorderInstance();
            instance.TraceContext.SetEntity(query.Entity);

            instance.MarkFault();
            instance.AddException(evt.Failure);
            instance.AddAnnotation("duration", evt.Duration.ToString());
            instance.EndSubsegment();
            
        }

        private static void Prune(DateTime now)
        {
            var currentPruneTime = _nextPruneTimeTicks;

            if (now.Ticks < currentPruneTime)
            {
                return;
            }

            var nextPruneTime = now.Add(_settings.MaxQueryTime).Ticks;

            if (Interlocked.CompareExchange(ref _nextPruneTimeTicks, nextPruneTime, currentPruneTime) != currentPruneTime)
            {
                return;
            }

            var expiryTime = now.Subtract(_settings.MaxQueryTime);

            foreach (var cacheEntry in _queryCache)
            {
                if (cacheEntry.Value.CachedAt < expiryTime)
                {
                    _queryCache.TryRemove(cacheEntry.Key, out _);
                }
            }
        }
    }
}