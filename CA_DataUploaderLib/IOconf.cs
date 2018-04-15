using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class IOconf
    {
        public List<List<string>> Table { get; private set; }

        public IOconf()
        {
            var lines = File.ReadAllLines("IO.conf").ToList();
            lines = lines.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
            Table = lines.Select(x => x.Split(",".ToCharArray()).ToList()).ToList();

            var groups = Table.GroupBy(x => x[0] + x[1]);
            foreach (var g in groups.Where(x => x.Count() > 1))
                Console.WriteLine($"WARNING: Name: {g.First()[1]} occure {g.Count()} times in this group: {g.First()[0]}");

            var indexs = Table.Where(x => x.Count() > 2).GroupBy(x => x[2]);
            foreach (var g in indexs.Where(x => x.Count() > 1))
                Console.WriteLine($"WARNING: vector index: {g.Key}, occure {g.Count()} times in IO.conf file");
        }

        public IEnumerable<List<string>> GetTypes(IOTypes type)
        {
            var list = Table.Where(x => x.First() == type.ToString()).ToList();
            Console.WriteLine($"read {list.Count} {type} rows from IO.config");
            return list;
        }

        internal static string GetLoopName()
        {
            return new IOconf().Table.Single(x => x.First() == "LoopName")[1];
        }

        public static IEnumerable<List<string>> GetInTypeK()
        {
            return new IOconf().GetTypes(IOTypes.InTypeK);
        }

        public static IEnumerable<List<string>> GetOut230Vac()
        {
            return new IOconf().GetTypes(IOTypes.Out230Vac);
        }

        public static IEnumerable<List<string>> GetPressure()
        {
            return new IOconf().GetTypes(IOTypes.Pressure);
        }
    }
}
