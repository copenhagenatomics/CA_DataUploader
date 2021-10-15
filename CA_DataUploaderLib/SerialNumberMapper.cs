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
        public List<MCUBoard> McuBoards { get; }
        public List<string> CalibrationUpdateMessages { get; }

        private SerialNumberMapper(IEnumerable<MCUBoard> boards, IEnumerable<string> calibrationUpdateMessages)
        {
            McuBoards = new List<MCUBoard>(boards);
            CalibrationUpdateMessages = new List<string>(calibrationUpdateMessages);
        }

        public async static Task<SerialNumberMapper> DetectDevices()
        {
            var logLevel = File.Exists("IO.conf") ? IOconfFile.GetOutputLevel() : CALogLevel.Normal;
            var boards = await Task.WhenAll(RpiVersion.GetUSBports().Select(name => AttemptToOpenDeviceConnection(name, logLevel)));
            return new SerialNumberMapper(boards.Select(b => b.board), boards.Where(b => b.calibrationUpdateMsg != default).Select(b => b.calibrationUpdateMsg));
        }

        private static async Task<(MCUBoard board, string calibrationUpdateMsg)> AttemptToOpenDeviceConnection(string name, CALogLevel logLevel)
        {
            try
            {
                var (mcu, calibrationUpdateMsg) = await MCUBoard.OpenDeviceConnection(name);
                mcu.productType = mcu.productType ?? GetStringFromDmesg(mcu.PortName);
                string logline = logLevel == CALogLevel.Debug ? mcu.ToDebugString(Environment.NewLine) : mcu.ToString();
                CALog.LogInfoAndConsoleLn(LogID.A, logline);
                return (mcu, calibrationUpdateMsg);
            }
            catch (UnauthorizedAccessException ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.Message}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex}" + Environment.NewLine);
            }

            return default;
        }

        private static string GetStringFromDmesg(string portName)
        {
            if (portName.StartsWith("COM"))
                return null;

            portName = portName[(portName.LastIndexOf('/') + 1)..];
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
