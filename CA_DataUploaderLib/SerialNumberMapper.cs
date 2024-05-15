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
        private readonly IIOconf? ioconf;

        private static List<Func<IIOconf?, string, Board?>> CustomDetections { get; } = new();
        public List<Board> McuBoards { get; } = new();

        public SerialNumberMapper(IIOconf? ioconf)
        {
            this.ioconf = ioconf;
        }

        public async Task DetectDevices()
        {
            var boards = await Task.WhenAll(MCUBoard.GetUSBports().Select(name => AttemptToOpenDeviceConnection(ioconf, name)));
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
        public static void RegisterCustomDetection(Func<IIOconf?, string, Board?> detectionFunction) => CustomDetections.Add(detectionFunction);
        private static async Task<Board?> AttemptToOpenDeviceConnection(IIOconf? ioconf, string name)
        {
            try
            {
                foreach (var detection in CustomDetections)
                    if (detection(ioconf, name) is Board board)
                    {
                        LogToLocalLogAndConsole(board.ToString());
                        return board;
                    }
                var mcu = await MCUBoard.OpenDeviceConnection(ioconf, name);
                if (mcu == null)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}");
                    return default;
                }

                LogToLocalLogAndConsole(ioconf?.GetOutputLevel() == CALogLevel.Debug ? mcu.ToDebugString(Environment.NewLine) : mcu.ToString());
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

            void LogToLocalLogAndConsole(string line)
            {
                Console.WriteLine(line);
                CALog.LogData(LogID.A, line);
            }
        }
    }
}
