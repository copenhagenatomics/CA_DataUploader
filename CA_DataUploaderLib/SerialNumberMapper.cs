using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public sealed class SerialNumberMapper : IDisposable
    {
        public readonly List<MCUBoard> McuBoards = new List<MCUBoard>();
        private readonly CALogLevel _logLevel = CALogLevel.Normal;

        public SerialNumberMapper()
        {
            if(File.Exists("IO.conf"))
                _logLevel = IOconfFile.GetOutputLevel();

            Parallel.ForEach(RpiVersion.GetUSBports(), name =>
            {
                try
                {
                    var mcu = MCUBoard.OpenDeviceConnection(name);
                    McuBoards.Add(mcu);
                    mcu.productType = mcu.productType ?? GetStringFromDmesg(mcu.PortName);
                    string logline = _logLevel == CALogLevel.Debug ? mcu.ToDebugString(Environment.NewLine) : mcu.ToString();
                    CALog.LogInfoAndConsoleLn(LogID.A, logline);
                }
                catch (UnauthorizedAccessException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.Message}" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.ToString()}" + Environment.NewLine);
                }
            });
        }

        private string GetStringFromDmesg(string portName)
        {
            if (portName.StartsWith("COM"))
                return null;

            portName = portName.Substring(portName.LastIndexOf('/') + 1);
            var result = DULutil.ExecuteShellCommand($"dmesg | grep {portName}").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return result.FirstOrDefault(x => x.EndsWith(portName))?.StringBetween(": ", " to ttyUSB");
        }

        /// <summary>
        /// Return a list of MCU boards where productType contains the input string. 
        /// </summary>
        /// <param name="productType">type of boards to look for. (Case sensitive)</param>
        /// <returns></returns>
        public List<MCUBoard> ByProductType(string productType) => 
            McuBoards.Where(x => x.productType != null && x.productType.Contains(productType)).ToList();

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
        }
    }
}
