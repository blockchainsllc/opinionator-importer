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