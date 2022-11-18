#nullable enable
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
        private static List<Func<string, Board?>> CustomDetections { get; } = new();
        public List<Board> McuBoards { get; }

        private SerialNumberMapper(IEnumerable<Board> boards)
        {
            McuBoards = new List<Board>(boards);
        }

        public async static Task<SerialNumberMapper> DetectDevices()
        {
            var logLevel = File.Exists("IO.conf") ? IOconfFile.GetOutputLevel() : CALogLevel.Normal;
            var boards = await Task.WhenAll(RpiVersion.GetUSBports().Select(name => AttemptToOpenDeviceConnection(name, logLevel)));
            return new SerialNumberMapper(boards.OfType<Board>());
        }

        public static Task<SerialNumberMapper> SkipDetection() => Task.FromResult(new SerialNumberMapper(Enumerable.Empty<Board>()));
        /// <remarks>
        /// Custom detection is typically used for 
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
                CALog.LogInfoAndConsoleLn(LogID.A, logline);
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

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
        }
    }
}
