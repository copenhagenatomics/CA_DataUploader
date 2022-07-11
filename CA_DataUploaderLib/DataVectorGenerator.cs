using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class DataVectorGenerator
    {
        // takes an IO.conf file content as input
        // returns a class definition which comply with the IO.conf file. 
        static string CreateStateMachineClass(string ioConfFile)
        {
            IOconfFile.Reload(ioConfFile);
            var cmd = new CommandHandler();
            var vectorDescription = cmd.GetExtendedVectorDescription();  //Freddy can I do this here.. what about the Lazy definition. 
            string result = $"public class {IOconfFile.GetLoopName()}DataVector : DataVector{Environment.NewLine}{{{Environment.NewLine}";
            result += $"public TestTube1_1DataVector(List<double> input, DateTime time, VectorDescription vectorDescription) : base(input, time, vectorDescription) {{ }}{Environment.NewLine}";

            foreach(var x in IOconfFile.GetInputs())
            {
                result += $"public double {x.Name} => vector[{vectorDescription.GetIndex(x.Name)}];{Environment.NewLine}";
            }

            return result + "}}";
        }
    }
}
