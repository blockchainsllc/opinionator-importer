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
using Nethereum.Hex.HexTypes;

namespace VotingImporter
{
    public static class Extensions
    {
        public static long AsLong(this HexBigInteger bigInt)
        {
            return long.Parse(bigInt.Value.ToString());
        }
        
        public static string AsString(this HexBigInteger bigInt)
        {
            return bigInt.Value.ToString();
        }

        public static string DeHash(this string value)
        {
            var strings = (value ?? String.Empty).ToLower().Split('x');
            return strings.Length > 1 ? strings[1] : strings[0];
        }
    }
}