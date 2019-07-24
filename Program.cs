using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using CommandLine;
using MongoDB.Bson;
using MongoDB.Driver;
using Sentry;

namespace VotingImporter
{
    class Program
    {
        static void Main(string[] args)
        {
            using (SentrySdk.Init(opts =>
            {
                opts.Dsn = new Dsn("https://d29df1f48689468a91153b1642b42d30@sentry.slock.it/8");
                opts.Environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "local";
                opts.Release = "voting-importer@" + (Environment.GetEnvironmentVariable("VERSION") ?? "v0.0.0");

            }))
            {
                Console.WriteLine("Blockchain Voting Importer");
                Parser.Default.ParseArguments<CmdOptions>(args)
                    .WithParsed(RunPeriodically)
                    .WithNotParsed((errs) =>
                    {
                        SentrySdk.CaptureMessage("Unable to parse cmdline: " + errs.Select(e => errs.ToString()));
                    });

            }
        }

        static void RunPeriodically(CmdOptions opts)
        {

            SentrySdk.ConfigureScope(scope =>
            {
                scope.SetTag("rpc", opts.RpcUrl);
                scope.SetTag("dbname", opts.MongoDbName);
                scope.SetTag("dburl", opts.MongoUrl);
            });

            // Migration step

            if (opts.MigrateDB)
            {
                Console.WriteLine("Start DB migration");
                MigrateDatabase(opts);
            }

            BlockChainImporter importer =new BlockChainImporter(opts);

            while (true)
            {
                long lag = importer.GetCurrentLag();
                if (lag > 10)
                {
                    importer.RunImporter();
                }
                else
                {
                    Console.WriteLine($"Waiting for more fresh blocks ({lag} blocks waiting)...");
                }
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
            // ReSharper disable once FunctionNeverReturns
        }

        private static void MigrateDatabase(CmdOptions opts)
        {
            MongoClient client = new MongoClient(opts.MongoUrl);
            var mc = client.GetDatabase(opts.MongoDbName).GetCollection<BsonDocument>("blocks");

            var fb = Builders<BsonDocument>.Filter;

            var tf = fb.Not(fb.Type("dif", BsonType.Double));
            var cur = mc.Find(tf).Project("{bn:1, dif:1}").ToCursor();
            while (cur.MoveNext())
            {
                Console.WriteLine("Next migration batch...");
                foreach (var doc in cur.Current)
                {
                    double newval = double.Parse(doc["dif"].ToString());
                    //Hard coded for testing
                    var filter = Builders<BsonDocument>.Filter.Eq("bn", doc["bn"]);
                    var update = Builders<BsonDocument>.Update.Set("dif", newval);

                    mc.UpdateOne(filter,update);
                    long bn = doc["bn"].AsInt64;
                    if (bn % 1000 == 0)
                    {
                        Console.WriteLine("Updated block " + doc["bn"].ToString() + " to " + newval);
                    }
                }
            }
        }
    }
}
