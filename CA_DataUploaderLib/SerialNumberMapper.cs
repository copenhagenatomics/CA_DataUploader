#nullable enable
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public sealed class SerialNumberMapper
    {
        private static List<Func<string, Board?>> CustomDetections { get; } = new();
        public List<Board> McuBoards { get; } = new();

        public SerialNumberMapper()
        {
        }

        public async Task DetectDevices()
        {
            var logLevel = IOconfFileLoader.FileExists() ? IOconfFile.GetOutputLevel() : CALogLevel.Normal;
            var boards = await Task.WhenAll(MCUBoard.GetUSBports().Select(name => AttemptToOpenDeviceConnection(name, logLevel)));
            McuBoards.AddRange(boards.OfType<Board>());
        }

        /// <summary>Registers custom board detection, typically a board that does not support a terminal like interface i.e. not tty on linux</summary>
        /// <remarks>
        /// The custom detections must be registered before <see cref="DetectDevices"/> is called.
        /// 
        /// The subsystems that use this custom board detection must implement all communication with the relevant board,
        /// so it is not possible to base the subsystem on or use BaseSensorBox with these custom boards. As such, 
        /// the subsystem must also implement or register an <see cref="ISubsystemWithVectorData"/> to add any relevant board data to the vector.
        /// 
        /// In linux only boards listed under /dev/USB* are considered. It is possible to use udev rules that target the type of device and
        /// add a specific suffix for easy detection, for example, /dev/USBAUDIO1-1.3.
        /// 
        /// An alternative for boards with a terminal like interface, supported by <see cref="BaseSensorBox"/>, is to use <see cref="MCUBoard.AddCustomProtocol(MCUBoard.CustomProtocolDetectionDelegate)"/>.
        /// </remarks>
        public static void RegisterCustomDetection(Func<string, Board?> detectionFunction) => CustomDetections.Add(detectionFunction);
        private static async Task<Board?> AttemptToOpenDeviceConnection(string name, CALogLevel logLevel)
        {
            try
            {
                foreach (var detection in CustomDetections)
                    if (detection(name) is Board board)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, board.ToString());
                        return board;
                    }
                var mcu = await MCUBoard.OpenDeviceConnection(name);
                if (mcu == null)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}");
                    return default;
                }

                string logline = logLevel == CALogLevel.Debug ? mcu.ToDebugString(Environment.NewLine) : mcu.ToString();
                Console.WriteLine(logline);      // Always write this in the console
                CALog.LogData(LogID.A, logline); // + local log, but don't send it to the event log.
                return mcu;
            }
            catch (UnauthorizedAccessException ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex}");
            }

            return default;
        }
    }
}
