using System.Threading.Tasks;
using Kevsoft.AWSXRayRecorder.Handlers.MongoDB;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AspNetCoreWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ExampleController : ControllerBase
    {

        [HttpGet]
        public async Task<string> Get()
        {
            var settings = XRayMongoClientSettingsConfigurator.Configure(new MongoClientSettings { }, new MongoXRayOptions());

            var mongoClient = new MongoClient(settings);

            var database = mongoClient.GetDatabase("test");
            var collection = database.GetCollection<BsonDocument>("test");

            await collection.Find(x => true).ToListAsync();

            return "something";
        }
    }
}
