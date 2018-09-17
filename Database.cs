using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Polly;

namespace VotingImporter
{
    public class Database
    {
        private readonly Policy _pollyRetryPolicy = Policy
            .Handle<MongoConnectionException>()
            .WaitAndRetry(new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3)
            }, (exception, retryCount) =>
            {
                Console.WriteLine("!!! MONGO STOPPED TALKING !!!");
                Console.WriteLine("Will retry...");
            });
        
        private readonly IMongoCollection<Block> _mc;

        public Database(string mongoUrl, string db)
        {
            MongoClient client = new MongoClient(mongoUrl);
            _mc = client.GetDatabase(db).GetCollection<Block>("blocks");
        }

        public void Insert(Block block)
        {
            _pollyRetryPolicy.Execute(() => { _mc.InsertOne(block); });

        }

        public class Transaction
        {
            [BsonElement("th")] public string TxHash { get; set; }
            [BsonElement("sender")] public string TxSender { get; set; }
            [BsonElement("gas")] public long Gas { get; set; }
            [BsonElement("gasprice")] public string GasPrice { get; set; }
            [BsonElement("nonce")] public long Nonce { get; set; }
            [BsonElement("txidx")] public int TxIndex { get; set; }
            [BsonElement("txval")] public string TxValue { get; set; }
            [BsonElement("to")] public string TxRecipient { get; set; }
            [BsonElement("traces")] public List<Trace> Traces { get; set; }
            [BsonElement("receipt")] public Receipt Receipt { get; set; }
        }

        [BsonIgnoreExtraElements]
        public class Block
        {
            [BsonElement("bn")] public long BlockNumber { get; set; }
            [BsonElement("bh")] public string BlockHash { get; set; }
            [BsonElement("author")] public string Author { get; set; }
            [BsonElement("dif")] public long Difficulty { get; set; }
            //[BsonElement("dif")] public string Difficulty { get; set; }
            [BsonElement("gaslim")] public long GasLimit { get; set; }
            [BsonElement("gasused")] public long GasUsed { get; set; }
            [BsonElement("miner")] public string Miner { get; set; }
            [BsonElement("size")] public long Size { get; set; }
            [BsonElement("ts")] public long Timestamp { get; set; }
            [BsonElement("txs")] public List<Transaction> Transactions { get; set; }

            public Block()
            {
                Transactions = new List<Transaction>();
            }
        }


        public class Trace
        {
            [BsonElement("tpos")] public long TracePosition { get; set; }

            [BsonElement("ct")] public string CallType { get; set; }
            [BsonElement("gas")] public long Gas { get; set; }
            [BsonElement("gasused")] public long GasUsed { get; set; }
            [BsonElement("to")] public string SendTo { get; set; }
            [BsonElement("from")] public string SendFrom { get; set; }
            [BsonElement("trcval")] public string TraceValue { get; set; }
            [BsonElement("parentpos")] public int ParentTracePosition { get; set; }
            [BsonElement("creates")]public string Creates { get; set; }
        }

        public long GetLastBlock()
        {
            var foo = _mc.Find(FilterDefinition<Block>.Empty).SortByDescending(x => x.BlockNumber).Limit(1).ToList(); 
            return foo.Count > 0 ? foo[0].BlockNumber : 1;
        }

        public class Receipt
        {
            [BsonElement("logs")]
            public object Logs { get; set; }
            [BsonElement("gasused")]
            public long GasUsed { get; set; }
            [BsonElement("contract")]
            public string ContractAddress { get; set; }
        }
    }
}