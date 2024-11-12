using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class SingleNodeRunner
    {
        public static async Task Run(IIOconf ioconf, CommandHandler cmdHandler, ServerUploader uploader, CancellationToken token)
        {
            try
            {
                var alerts = new Alerts(ioconf, cmdHandler);
                var subsystemsTask = Task.Run(() => cmdHandler.RunSubsystems(token), token);
                var sendThrottle = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                DataVector? vector = null;
                var emptyCommands = new List<string>(0);

                while (!token.IsCancellationRequested)
                {
                    var events = cmdHandler.DequeueEvents();
                    var commands = events?.Where(e => e.EventType == (byte)EventType.Command);
                    var stringCommands = commands is not null && commands.Any() ? commands.Select(e => e.Data).ToList() : emptyCommands;
                    cmdHandler.MakeDecision(cmdHandler.GetNodeInputs().Concat(cmdHandler.GetGlobalInputs()).ToList(), DateTime.UtcNow, ref vector, stringCommands);
                    cmdHandler.OnNewVectorReceived(vector);
                    vector = new([.. vector.Data], vector.Timestamp, events); //we take a copy so no further changes are done to the vector shared via OnNewVectorReceived
                    await Task.WhenAny(sendThrottle.WaitForNextTickAsync(token).AsTask());//Task.WhenAny for no exceptions on cancel
                }

                CALog.LogInfoAndConsoleLn(LogID.A, "Waiting for subsystems to stop");
                await subsystemsTask; //note we can only get here if the token has been cancelled, so the subsystems have already been told to stop
            }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                uploader.SendEvent(uploader, new EventFiredArgs("User initiated uploader stop", EventType.Log, DateTime.UtcNow));
                if (ex is not OperationCanceledException)
                {
                    CALog.LoggerForUserOutput = new CALog.ConsoleLogger(); //we are about to stop the uploader, so we change inmediately to the console logger so the below already shows in the screen.
                    CALog.LogErrorAndConsoleLn(LogID.B, "Error detected while stopping uploader", ex);
                }
            }
            catch (Exception ex)
            {
                uploader.SendEvent(uploader, new EventFiredArgs($"Error detected (stopping uploader): {ex.Message}", EventType.LogError, DateTime.UtcNow));
                throw;
            }
        }
    }
}
