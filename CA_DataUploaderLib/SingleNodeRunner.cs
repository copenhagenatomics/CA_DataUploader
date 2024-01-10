using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class SingleNodeRunner
    {
        public static async Task Run(CommandHandler cmdHandler, ServerUploader uploader, CancellationToken token)
        {
            Queue<EventFiredArgs> receivedEventsInThisCycleQueue = new();
            List<EventFiredArgs> receivedEventsInThisCycle = new();

            try
            {
                var alerts = new Alerts(cmdHandler);
                var subsystemsTask = Task.Run(() => cmdHandler.RunSubsystems(token), token);
                var sendThrottle = new TimeThrottle(100);
                DataVector? vector = null;
                var emptyCommands = new List<string>(0);

                while (!token.IsCancellationRequested)
                {
                    var events = cmdHandler.DequeueEvents();
                    var commands = events?.Where(e => e.EventType == (byte)EventType.Command);
                    var stringCommands = commands is not null && commands.Any() ? commands.Select(e => e.Data).ToList() : emptyCommands;
                    cmdHandler.MakeDecision(cmdHandler.GetNodeInputs().Concat(cmdHandler.GetGlobalInputs()).ToList(), DateTime.UtcNow, ref vector, stringCommands);
                    vector = new(vector.Data.ToArray(), vector.Timestamp, events is not null ? new List<EventFiredArgs>(events) : null);//note we create copies to avoid changes in the next cycle affecting data of the previous cycle (specially via OnNewVectorReceived)
                    cmdHandler.OnNewVectorReceived(vector);
                    await Task.WhenAny(sendThrottle.WaitAsync(token));//whenany for no exceptions on cancel
                }

                CALog.LogInfoAndConsoleLn(LogID.A, "waiting for subsystems to stop");
                await subsystemsTask; //note we can only get here if the token has been cancelled, so the subsystems have already been told to stop
            }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                uploader.SendEvent(uploader, new EventFiredArgs("user initiated uploader stop", EventType.Log, DateTime.UtcNow));
                if (ex is not OperationCanceledException)
                {
                    CALog.LoggerForUserOutput = new CALog.ConsoleLogger(); //we are about to stop the uploader, so we change inmediately to the console logger so the below already shows in the screen.
                    CALog.LogErrorAndConsoleLn(LogID.B, "error detected while stopping uploader", ex);
                }
            }
            catch (Exception ex)
            {
                uploader.SendEvent(uploader, new EventFiredArgs($"error detected (stopping uploader): {ex.Message}", EventType.LogError, DateTime.UtcNow));
                throw;
            }
        }
    }
}
