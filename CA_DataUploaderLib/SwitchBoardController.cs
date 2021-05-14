using System;
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public sealed class SwitchBoardController : IDisposable
    {
        /// <summary>runs when the subsystem is about to stop running, but before all boards are closed</summary>
        /// <remarks>some boards might be closed, specially if the system is stopping due to losing connection to one of the boards</remarks>
        public event EventHandler Stopping;
        private readonly BaseSensorBox _reader;
        private static readonly object ControllerInitializationLock = new object();
        private static SwitchBoardController _instance;

        private SwitchBoardController(CommandHandler cmd) 
        {
            var heaters = IOconfFile.GetHeater().Cast<IOconfOut230Vac>();
            var ports = heaters.Concat(IOconfFile.GetValve()).Where(p => p.Map.Board != null);
            var boardsTemperatures = ports.GroupBy(p => p.BoxName).Select(b => b.Select(p => p.GetBoardTemperatureInputConf()).FirstOrDefault());
            var inputs = ports.SelectMany(p => p.GetExpandedInputConf()).Concat(boardsTemperatures);
            _reader = new BaseSensorBox(cmd, "switchboards", string.Empty, "show switchboards inputs", inputs);
            _reader.Stopping += OnStopping;
        }

        public void Dispose() => _reader.Dispose();

        public static SwitchBoardController GetOrCreate(CommandHandler cmd)
        {
            if (_instance != null) return _instance;
            lock (ControllerInitializationLock)
            {
                if (_instance != null) return _instance;
                return _instance = new SwitchBoardController(cmd);
            }
        }

        private void OnStopping(object sender, EventArgs args) => Stopping?.Invoke(this, EventArgs.Empty);
    }
}
