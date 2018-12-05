using CommandLine;

namespace VotingImporter
{
    public class CmdOptions
    {
        [Option('r',"rpc",Required = true)]
        public string RpcUrl { get; set; }
        
        [Option('i',"ipc",Required = false, Default = "")]
        public string IpcPath { get; set; }
        
        [Option('d',"dburl",Required = true)]
        public string MongoUrl { get; set; }
        
        [Option('b',"batch",Required = false, Default = 5)]
        public int BatchSize { get; set; }
        
        [Option('n',"dbname",Required = true)]
        public string MongoDbName { get; set; }
        
        [Option('m',"migrate",Required = false)]
        public bool MigrateDB { get; set; }
    }
}