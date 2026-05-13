using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Channels;

namespace UnitTests
{
    [TestClass]
    public class UploadRetryTests
    {
        [TestMethod]
        public void PrepareVectorUploadBatch_WhenQueueHasMoreThanBatchSize_TakesOnlyBatchSize()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<DataVector>();
            channel.Writer.TryWrite(NewVector(1));
            channel.Writer.TryWrite(NewVector(2));
            channel.Writer.TryWrite(NewVector(3));
            var pending = new List<DataVector>();

            // Act
            ServerUploader.PrepareVectorUploadBatch(channel.Reader, pending, maxBatchSize: 2);

            // Assert
            CollectionAssert.AreEqual(new[] { 1d, 2d }, pending.Select(v => v.Data[0]).ToList());
            Assert.IsTrue(channel.Reader.TryRead(out var remaining));
            Assert.AreEqual(3d, remaining.Data[0]);
        }

        [TestMethod]
        public void PrepareVectorUploadBatch_WhenPendingBatchExists_DoesNotDequeueNewBatch()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<DataVector>();
            channel.Writer.TryWrite(NewVector(3));
            var pending = new List<DataVector> { NewVector(1), NewVector(2) };

            // Act
            ServerUploader.PrepareVectorUploadBatch(channel.Reader, pending, maxBatchSize: 2);

            // Assert
            CollectionAssert.AreEqual(new[] { 1d, 2d }, pending.Select(v => v.Data[0]).ToList());
            Assert.IsTrue(channel.Reader.TryRead(out var remaining));
            Assert.AreEqual(3d, remaining.Data[0]);
        }

        [TestMethod]
        public void PrepareVectorUploadBatch_WhenQueueIsEmpty_LeavesPendingEmpty()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<DataVector>();
            var pending = new List<DataVector>();

            // Act
            ServerUploader.PrepareVectorUploadBatch(channel.Reader, pending, maxBatchSize: 2);

            // Assert
            Assert.IsEmpty(pending);
        }

        [TestMethod]
        [DataRow(null, true)]
        [DataRow(HttpStatusCode.NotFound, true)]
        [DataRow(HttpStatusCode.RequestTimeout, true)]
        [DataRow(HttpStatusCode.TooManyRequests, true)]
        [DataRow(HttpStatusCode.BadGateway, true)]
        [DataRow(HttpStatusCode.ServiceUnavailable, true)]
        [DataRow(HttpStatusCode.GatewayTimeout, true)]
        [DataRow(HttpStatusCode.BadRequest, false)]
        [DataRow(HttpStatusCode.Unauthorized, false)]
        [DataRow(HttpStatusCode.Forbidden, false)]
        [DataRow(HttpStatusCode.InternalServerError, false)]
        public void ShouldRetryUpload_ReturnsExpectedResult(HttpStatusCode? statusCode, bool expected)
        {
            // Act
            var retry = ServerUploader.ShouldRetryUpload(statusCode);

            // Assert
            Assert.AreEqual(expected, retry);
        }

        [TestMethod]
        public void HandleFailedEventUpload_WhenStatusIsRetryable_KeepsPendingEventAndDoesNotIncrementFailures()
        {
            // Arrange
            var pendingEvent = NewEvent();
            var nonRetryableFailures = 0;
            var logs = new List<string>();

            // Act
            var result = ServerUploader.HandleFailedEventUpload(isLocalFailure: false, HttpStatusCode.BadGateway, pendingEvent, ref nonRetryableFailures, logs.Add);

            // Assert
            Assert.AreSame(pendingEvent, result);
            Assert.AreEqual(0, nonRetryableFailures);
            Assert.IsEmpty(logs);
        }

        [TestMethod]
        public void HandleFailedEventUpload_WhenNonRetryableFailureIsBelowLimit_KeepsPendingEvent()
        {
            // Arrange
            var pendingEvent = NewEvent();
            var nonRetryableFailures = 1;
            var logs = new List<string>();

            // Act
            var result = ServerUploader.HandleFailedEventUpload(isLocalFailure: false, HttpStatusCode.BadRequest, pendingEvent, ref nonRetryableFailures, logs.Add);

            // Assert
            Assert.AreSame(pendingEvent, result);
            Assert.AreEqual(2, nonRetryableFailures);
            Assert.IsEmpty(logs);
        }

        [TestMethod]
        public void HandleFailedEventUpload_WhenNonRetryableFailureReachesLimit_DropsPendingEventAndLogs()
        {
            // Arrange
            var pendingEvent = NewEvent();
            var nonRetryableFailures = 2;
            var logs = new List<string>();

            // Act
            var result = ServerUploader.HandleFailedEventUpload(isLocalFailure: false, HttpStatusCode.BadRequest, pendingEvent, ref nonRetryableFailures, logs.Add);

            // Assert
            Assert.IsNull(result);
            Assert.AreEqual(0, nonRetryableFailures);
            Assert.HasCount(1, logs);
            StringAssert.Contains(logs.Single(), "400 BadRequest");
            StringAssert.Contains(logs.Single(), pendingEvent.Data);
        }

        [TestMethod]
        public void HandleFailedEventUpload_WhenLocalFailureReachesLimit_DropsPendingEventAndLogsLocalFailure()
        {
            // Arrange
            var pendingEvent = NewEvent();
            var nonRetryableFailures = 2;
            var logs = new List<string>();

            // Act
            var result = ServerUploader.HandleFailedEventUpload(isLocalFailure: true, statusCode: null, pendingEvent, ref nonRetryableFailures, logs.Add);

            // Assert
            Assert.IsNull(result);
            Assert.AreEqual(0, nonRetryableFailures);
            Assert.HasCount(1, logs);
            StringAssert.Contains(logs.Single(), "local exception");
            StringAssert.Contains(logs.Single(), pendingEvent.Data);
        }

        private static DataVector NewVector(int minute) => new([minute], new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc));

        private static EventFiredArgs NewEvent() => new("test event", EventType.Log, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
