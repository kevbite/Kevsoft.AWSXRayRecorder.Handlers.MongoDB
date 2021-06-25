using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.XRay.Recorder.Core.Internal.Emitters;
using Amazon.XRay.Recorder.Core.Internal.Entities;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
namespace Kevsoft.AWSXRayRecorder.Handlers.MongoDB.Tests
{
    public class MongoDbXRayTests : ISegmentEmitter
    {
        private List<Entity> _emittedSegments = new();
        
        [Fact]
        public async Task ShouldEmitMongoDBSubSegment()
        {
            Amazon.XRay.Recorder.Core.AWSXRayRecorder.InitializeInstance();
            Amazon.XRay.Recorder.Core.AWSXRayRecorder.Instance.Emitter = this;
            var settings = XRayMongoClientSettingsConfigurator.Configure(new MongoClientSettings(), new MongoXRayOptions());

            var mongoClient = new MongoClient(settings);

            var database = mongoClient.GetDatabase("test");
            var collection = database.GetCollection<BsonDocument>("test");

            Amazon.XRay.Recorder.Core.AWSXRayRecorder.Instance.BeginSegment("StartTest");
            await collection.Find(x => true)
                .ToListAsync();
            Amazon.XRay.Recorder.Core.AWSXRayRecorder.Instance.EndSegment();
            
            _emittedSegments.Should().HaveCount(1);
            _emittedSegments[0].Subsegments.Should().HaveCount(1);

            var subsegment = _emittedSegments[0].Subsegments[0];
            subsegment.Name.Should().Be("test@localhost:27017");
            subsegment.Namespace.Should().Be("remote");
            subsegment.Annotations["endpoint"].Should().Be("localhost:27017");
            subsegment.Annotations["database"].Should().Be("test");
            subsegment.Annotations["command"].As<string>().Should().Contain("find");
            subsegment.Annotations["duration"].Should().NotBeNull();
            subsegment.Annotations["command_name"].Should().Be("find");

        }

        void IDisposable.Dispose() { }

        void ISegmentEmitter.Send(Entity segment)
        {
            _emittedSegments.Add(segment);
        }

        void ISegmentEmitter.SetDaemonAddress(string daemonAddress) { }
    }
}
