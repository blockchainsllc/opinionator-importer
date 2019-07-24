using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.JsonRpc.IpcClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Sentry;

namespace VotingImporter
{
    [Serializable]
    public class ParseTraceException : Exception
    {
        //
        // For guidelines regarding the creation of new exception types, see
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
        // and
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
        //

        public ParseTraceException()
        {
        }

        public ParseTraceException(string message) : base(message)
        {
        }

        public ParseTraceException(string message, Exception inner) : base(message, inner)
        {
        }

        protected ParseTraceException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    public class BlockChainImporter
    {
        private readonly string _ipc;
        private readonly Database _db;
        private readonly List<string> _rpcUrls;
        private readonly int _batchSize;
        private static readonly HttpClient Client = new HttpClient();

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
            _db = new Database(mongoDbUrl, opts.MongoDbName);
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

        static T FetchValue<T>(JObject jObj, string[] path)
        {
            JObject curObj = jObj;
            for (byte i = 0; i < path.Length; i++)
            {
                JObject newObj = curObj[path[i]] as JObject;
                if (!(newObj?.HasValues ?? false))
                {
                    // not found
                    return default(T);
                }

                if (i == path.Length - 1)
                {
                    //reached end
                    return newObj.Value<T>();
                }

                curObj = newObj;
            }

            return default(T);
        }


        private (List<Database.Trace>, Database.Receipt) GetTrace(string txHash, string url)
        {
            List<Database.Trace> traces = new List<Database.Trace>();
            Database.Receipt r = new Database.Receipt();


            string content =
                "[{\"jsonrpc\": \"2.0\",\"method\": \"trace_transaction\",\"params\": [\"" + txHash +
                "\"],\"id\": 1}," +
                "{\"jsonrpc\": \"2.0\",\"method\": \"eth_getTransactionReceipt\",\"params\": [\"" + txHash +
                "\"],\"id\": 2}]";

            HttpResponseMessage response = Client.PostAsync(url,
                new StringContent(content, Encoding.UTF8, "application/json")).Result;
            string jsonResult = response.Content.ReadAsStringAsync().Result;
            JArray obj = (JArray) JsonConvert.DeserializeObject(jsonResult);

            byte idxTrace = 0;
            byte idxReciept = 1;

            if (obj[0]["id"].Value<int>() == 2)
            {
                idxTrace = 1;
                idxReciept = 0;
            }

            if (obj[idxTrace]["result"] is JArray result)
            {
                foreach (JToken jToken in result)
                {
                    try
                    {
                        JObject tobj = (JObject) jToken;
                        Database.Trace t = new Database.Trace();

                        try
                        {
                            t.CallType = tobj["type"].Value<string>();
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse type", e);
                        }

                        try
                        {
                            t.Gas = ParseHex(FetchValue<String>(tobj, new[] {"action", "gas"}) ?? "0x0");
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse action.gas", e);
                        }

                        try
                        {
                            t.TraceValue = FetchValue<String>(tobj, new[] {"action", "value"}) ?? String.Empty;
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse action.value", e);
                        }

                        try
                        {
                            t.SendFrom = FetchValue<String>(tobj, new[] {"action", "from"}).DeHash() ?? String.Empty;
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse action.from", e);
                        }

                        try
                        {
                            t.SendTo = FetchValue<String>(tobj, new[] {"action", "to"})?.DeHash() ?? String.Empty;
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse action.to", e);
                        }

                        try
                        {
                            t.GasUsed = ParseHex(FetchValue<string>(tobj, new[] {"result", "gasUsed"}) ?? "0x0");
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse result.gasUsed", e);
                        }

                        t.ParentTracePosition = 0;

                        try
                        {
                            t.Creates = FetchValue<string>(tobj, new[] {"result", "address"})?.DeHash() ?? String.Empty;
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse result.address", e);
                        }

                        try
                        {
                            t.TracePosition = tobj["traceIndex"]?.Value<long>() ?? 0;
                        }
                        catch (Exception e)
                        {
                            throw new ParseTraceException("Unable to parse traceIndex", e);
                        }

                        traces.Add(t);
                    }
                    catch (Exception ex)
                    {
                        SentrySdk.WithScope(scope =>
                        {
                            scope.SetExtra("tx-hash", txHash);
                            scope.SetExtra("trace-json", JsonConvert.SerializeObject(jToken));
                            SentrySdk.CaptureException(new Exception("Unable to parse trace", ex));
                        });
                    }
                }
            }

            if (obj[idxReciept]["result"] is JObject receipt)
            {
                try
                {
                    string logsJson = JsonConvert.SerializeObject(receipt["logs"]);
                    r.Logs = BsonSerializer.Deserialize<object>(logsJson);
                    r.GasUsed = ParseHex(receipt["gasUsed"] ?? "0x0");
                    r.ContractAddress = receipt["contractAddress"]?.Value<string>().DeHash() ?? "0x0";
                }
                catch (Exception ex)
                {
                    SentrySdk.WithScope(scope =>
                    {
                        scope.SetExtra("tx-hash", txHash);
                        SentrySdk.CaptureException(new Exception("Unable to get tx receipt", ex));
                    });
                }
            }


            return (traces, r);
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
                    SentrySdk.CaptureMessage("Parity is overloaded. Will wait... " + retryCount.TotalSeconds);
                    Console.WriteLine("Parity is overloaded. Will wait... " + retryCount.TotalSeconds);
                });


            long startBlock = _db.GetLastBlock() + 1;
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
                    BlockWithTransactions ethBlock = GetBlockWithTransactions(pollyRetryPolicy, web3, i);

                    //get block fromn parity
                    Database.Block newBlock = new Database.Block
                    {
                        Author = ethBlock.Author.DeHash(),
                        BlockHash = ethBlock.BlockHash.DeHash(),
                        BlockNumber = ethBlock.Number.AsLong(),
                        Difficulty = double.Parse(ethBlock.Difficulty.AsString()),
                        GasLimit = ethBlock.GasLimit.AsLong(),
                        GasUsed = ethBlock.GasUsed.AsLong(),
                        Miner = ethBlock.Miner.DeHash(),
                        Size = ethBlock.Size.AsLong(),
                        Timestamp = (long) ethBlock.Timestamp.Value,
                        Transactions = new List<Database.Transaction>()
                    };

                    //newBlock.Difficulty = ethBlock.Difficulty.AsLong();
                    lastTimeStamp = newBlock.Timestamp;

                    ParseBlockTransactions(x2, ethBlock, newBlock);

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
                Console.WriteLine(
                    $" => {x} blocks done - {blocksPerSec,6:F1} blk/s - AVG: {avgPerSec,6:F1} blk/s - ETA: {time.ToString(@"dd\.hh\:mm\:ss")} - {behindChain.ToString(@"dd\.hh\:mm\:ss")} behind chain =====");
            }

            Console.WriteLine("==> Batch done. Wait for new blocks. <==");
        }

        private void ParseBlockTransactions(long x2, BlockWithTransactions ethBlock, Database.Block newBlock)
        {
            object myLock = new object();

            //foreach (var ethtx in ethBlock.Transactions)
            long x1 = x2;
            Parallel.ForEach(ethBlock.Transactions, new ParallelOptions {MaxDegreeOfParallelism = 5}, (ethtx) =>
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
                    SentrySdk.CaptureException(e);
                    //ignored
                }

                lock (myLock)
                {
                    newBlock.Transactions.Add(tx);
                }
            });
        }

        private static BlockWithTransactions GetBlockWithTransactions(Policy pollyRetryPolicy, Web3 web3,
            long blockNumber)
        {
            PolicyResult<BlockWithTransactions> capture = pollyRetryPolicy.ExecuteAndCapture(() =>
            {
                BlockWithTransactions rpcBlock = web3.Eth.Blocks.GetBlockWithTransactionsByNumber
                    .SendRequestAsync(new HexBigInteger(blockNumber)).Result;
                return rpcBlock;
            });

            BlockWithTransactions ethBlock = capture.Result;
            return ethBlock;
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
}
