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