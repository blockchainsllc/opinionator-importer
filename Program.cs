using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using MongoDB.Bson.Serialization;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.JsonRpc.IpcClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;

namespace VotingImporter
{
    public class BlockChainImporter
    {
        private readonly string _ipc;
        private readonly Database _db;
        private readonly List<string> _rpcUrls;
        private readonly int _batchSize;
        private static readonly HttpClient client = new HttpClient();
        
        public BlockChainImporter(CmdOptions opts)
        {
            string mongoDbUrl = opts.MongoUrl;
            if (!mongoDbUrl.StartsWith("mongodb://"))
            {
                throw new Exception("ERROR: first argument has to be a mongo url");
                
            }
            
            _rpcUrls = opts.RpcUrl.Split(',').ToList();
            if (!_rpcUrls.All(x => x.StartsWith("http://")))
            {
                throw new Exception("ERROR: second argument has to be a http RPC url");
            }

            _ipc = opts.IpcPath;
            _db = new Database(mongoDbUrl,opts.MongoDbName);
            _batchSize = opts.BatchSize;
        }


        public long GetCurrentLag()
        {
            Web3 web3 = GetWeb3Client();
            long lastBlockChain = web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().Result.AsLong();
            long lastBlockDb = _db.GetLastBlock();
            return lastBlockChain - lastBlockDb;
        }

        private long ParseHex(object value)
        {
            return Int64.Parse(value.ToString().DeHash(), NumberStyles.AllowHexSpecifier);
        }

        private (List<Database.Trace>,Database.Receipt) GetTrace(string txHash,string url)
        {
            List<Database.Trace> traces = new List<Database.Trace>();
            Database.Receipt r = new Database.Receipt();

            
            string content =
                "[{\"jsonrpc\": \"2.0\",\"method\": \"trace_transaction\",\"params\": [\"" + txHash + "\"],\"id\": 1}," +
                "{\"jsonrpc\": \"2.0\",\"method\": \"eth_getTransactionReceipt\",\"params\": [\"" + txHash + "\"],\"id\": 2}]";

            HttpResponseMessage response = client.PostAsync(url,
                new StringContent(content, Encoding.UTF8, "application/json")).Result;
            string jsonResult = response.Content.ReadAsStringAsync().Result;
            JArray obj = (JArray) JsonConvert.DeserializeObject(jsonResult);
            if (obj[0]["result"] is JArray result)
            {
                foreach (JToken jToken in result)
                {
                    try
                    {
                        JObject tobj = (JObject) jToken;
                        Database.Trace t = new Database.Trace();
                        t.CallType = tobj["type"]?.Value<string>() ?? String.Empty;
                        t.Gas = ParseHex(tobj["action"]?["gas"] ?? "0x0");
                        t.TraceValue = tobj["action"]?["value"]?.Value<string>() ?? String.Empty;
                        t.SendFrom = tobj["action"]?["from"]?.Value<string>().DeHash() ?? String.Empty;
                        t.SendTo = tobj["action"]?["to"]?.Value<string>().DeHash() ?? String.Empty;
                        t.GasUsed = ParseHex(tobj["result"]?["gasUsed"] ?? "0x0");
                        t.ParentTracePosition = 0;
                        t.Creates = tobj["result"]?["address"]?.Value<string>().DeHash() ?? String.Empty;
                        t.TracePosition = tobj["traceIndex"]?.Value<long>() ?? 0;
                        traces.Add(t);
                    }
                    catch
                    {
                        Console.WriteLine("\tIgnored unknown trace");
                        //throw;
                    }
                }
            } 
            if (obj[1]["result"] is JObject receipt)
            {
                try
                {
                    string logsJson = JsonConvert.SerializeObject(receipt["logs"]);
                    r.Logs =  BsonSerializer.Deserialize<object>(logsJson);
                    r.GasUsed = ParseHex(receipt["gasUsed"] ?? "0x0");
                    r.ContractAddress = receipt["contractAddress"]?.Value<string>().DeHash() ?? "0x0";
                    
                }
                catch
                {
                    Console.WriteLine("\tunable to get receipt");
                    //throw;
                }
            }
            

            return (traces,r);
        }

        public void RunImporter()
        {

            long lastTimeStamp = 0;

            //get blocks
            
            Policy pollyRetryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetry(new[]
                {
                    TimeSpan.FromSeconds(1),
                }, (exception, retryCount) =>
                {
                    Console.WriteLine("Parity is overloaded. Will wait... " + retryCount.TotalSeconds);
                });
            
            
            long startBlock = _db.GetLastBlock() +1;
            Web3 web3 = GetWeb3Client();
            long maxBlock = web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().Result.AsLong();
            

            Console.WriteLine($"==> Running block #{startBlock} to #{maxBlock} <==");
            
            Stopwatch sw = new Stopwatch();
            DateTime startTime = DateTime.UtcNow;
            for (long x = startBlock; x <= maxBlock; x = x + _batchSize)
            {
                
                sw.Restart();

                long x2 = x;
                Parallel.For(x, x + _batchSize, i =>
                {
                    PolicyResult<BlockWithTransactions> capture = pollyRetryPolicy.ExecuteAndCapture(() =>
                    {
                        BlockWithTransactions rpcBlock = web3.Eth.Blocks.GetBlockWithTransactionsByNumber
                            .SendRequestAsync(new HexBigInteger(i)).Result;
                        return rpcBlock;
                    });

                    BlockWithTransactions ethBlock = capture.Result;

                    
                    //get block fromn parity
                    Database.Block newBlock = new Database.Block();
                    
                    newBlock.Author = ethBlock.Author.DeHash();
                    newBlock.BlockHash = ethBlock.BlockHash.DeHash();
                    newBlock.BlockNumber = ethBlock.Number.AsLong();
                    newBlock.Difficulty = ethBlock.Difficulty.AsLong();
                    //newBlock.Difficulty = ethBlock.Difficulty.AsString();
                    newBlock.GasLimit = ethBlock.GasLimit.AsLong();
                    newBlock.GasUsed = ethBlock.GasUsed.AsLong();
                    newBlock.Miner = ethBlock.Miner.DeHash();
                    newBlock.Size = ethBlock.Size.AsLong();
                    newBlock.Timestamp = (long) ethBlock.Timestamp.Value;
                    newBlock.Transactions = new List<Database.Transaction>();

                    object myLock = new object();
                    
                    //foreach (var ethtx in ethBlock.Transactions)
                    long x1 = x2;
                    Parallel.ForEach(ethBlock.Transactions,new ParallelOptions { MaxDegreeOfParallelism = 5 }, (ethtx) =>
                    {
                        Database.Transaction tx = new Database.Transaction
                        {
                            Gas = ethtx.Gas.AsLong(),
                            Nonce = ethtx.Nonce.AsLong(),
                            GasPrice = ethtx.GasPrice.Value.ToString(),
                            TxHash = ethtx.TransactionHash.DeHash(),
                            TxSender = ethtx.From.DeHash(),
                            TxRecipient = ethtx.To.DeHash(),
                            TxValue = ethtx.Value.Value.ToString(),
                            TxIndex = (int) ethtx.TransactionIndex.AsLong(),
                        };

                        //get traces 
                        try
                        {
                            var queryResult = GetTrace(ethtx.TransactionHash, _rpcUrls[(int) (x1 % _rpcUrls.Count)]);
                            tx.Traces = queryResult.Item1;
                            tx.Receipt = queryResult.Item2;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            //ignored
                        }

                        lock (myLock)
                        {
                            newBlock.Transactions.Add(tx);
                            lastTimeStamp = newBlock.Timestamp;
                        }
                    });

                    _db.Insert(newBlock);
                    Console.Write(".");
                });

                sw.Stop();
                double blocksPerSec = _batchSize / (sw.ElapsedMilliseconds / 1000.0);
                double avgPerSec = (x - startBlock) / (DateTime.UtcNow - startTime).TotalSeconds;

                double remainingSeconds = ((maxBlock - x) / avgPerSec);
                if (remainingSeconds < 0 || remainingSeconds > 10e6)
                {
                    remainingSeconds = 120;
                }
                TimeSpan time = TimeSpan.FromSeconds(remainingSeconds);
                DateTimeOffset lastBlockDt = DateTimeOffset.FromUnixTimeSeconds(lastTimeStamp);
                TimeSpan behindChain = DateTime.UtcNow - lastBlockDt.UtcDateTime;
                Console.WriteLine($" => {x} blocks done - {blocksPerSec,6:F1} blk/s - AVG: {avgPerSec,6:F1} blk/s - ETA: {time.ToString(@"dd\.hh\:mm\:ss")} - {behindChain.ToString(@"dd\.hh\:mm\:ss")} behind chain =====");
            }

            Console.WriteLine("==> Batch done. Wait for new blocks. <==");
        }

        private Web3 GetWeb3Client()
        {
            UnixIpcClient ipcClient = null;
            if (!String.IsNullOrEmpty(_ipc))
            {
                ipcClient = new UnixIpcClient(_ipc);
            }

            IClient web3Client = ipcClient ?? (IClient) new RpcClient(new Uri(_rpcUrls.First()));
            Web3 web3 = new Web3(web3Client);
            return web3;
        }
    }
    
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Blockchain Voting Importer");

            
            
            Parser.Default.ParseArguments<CmdOptions>(args)
                .WithParsed(RunPeriodically)
                .WithNotParsed((errs) =>
                {
                    Console.WriteLine("Unable to parse cmdline: " + errs.Select(e => errs.ToString()));
                });

        }

        static void RunPeriodically(CmdOptions opts)
        {
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
    }
}