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
using System.Collections.Generic;
using MongoDB.Bson;
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
            
            [BsonElement("dif")] public double Difficulty { get; set; }
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