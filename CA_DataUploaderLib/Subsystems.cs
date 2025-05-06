using CA_DataUploaderLib.IOconf;
using System;

namespace CA_DataUploaderLib
{
    public class Subsystems
    {
        private static Alerts? _alerts;
        private static ServerUploader? _uploader;

        public static Alerts Alerts
        {
            get => _alerts ?? throw new InvalidOperationException("This property can not be used before calling AddSubsystemsTo");
            private set => _alerts = value;
        }
        /// <remarks>
        /// This is only exposed to be able to report issues where the cluster is down and thus there are no new vectors being posted
        /// and to send single host events (in 3.x these are not coming into the vector).
        /// . 
        /// This is mainly to be used to report issues where the 
        /// check <see cref="ServerUploader.IsEnabled"/> 
        /// </remarks>
        public static ServerUploader Uploader
        {
            get => _uploader ?? throw new InvalidOperationException("This property can not be used before calling AddSubsystemsTo");
            private set => _uploader = value;
        }

        public static void RegisterIOConfAndThirdPartyBoardsProtocols(IIOconfLoader loader)
        {
            Redundancy.RegisterSystemExtensions(loader);
        }

        public static void AddSubsystemsTo(IIOconf ioconf, CommandHandler cmdHandler)
        {
            _alerts = new Alerts(ioconf, cmdHandler);
            Uploader = new ServerUploader(ioconf, cmdHandler);
            _ = new Redundancy(ioconf, cmdHandler);
            _ = new GenericSensorBox(ioconf, cmdHandler);
            _ = new ThermocoupleBox(ioconf, cmdHandler);
            _ = new HeatingController(ioconf, cmdHandler);
            _ = new CurrentBox(ioconf, cmdHandler);
        }
    }
}
