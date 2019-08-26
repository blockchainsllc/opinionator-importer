// This file is part of the opinionator project

// Copyright (C) 2019  Markus Keil <markus.keil@slock.it>, Slock.it GmbH
 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// [Permissions of this strong copyleft license are conditioned on making available complete source code of licensed works and modifications, which include larger works using a licensed work, under the same license. Copyright and license notices must be preserved. Contributors provide an express grant of patent rights.]
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
                opts.Release = Environment.GetEnvironmentVariable("VERSION") ?? "voting-importer@v0.0.0";

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
                if (lag > 50)
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
