using Inbox.Domain.Common;
using Inbox.Domain.Messages;

namespace Inbox.Domain.Tests;

public sealed class MessageTests
{
    private static readonly Guid ContactId = Guid.CreateVersion7();

    [Fact]
    public void Inbound_ValidData_IsReceivedAndUnread()
    {
        var message = Message.Inbound(ContactId, 100, 42, TestData.Text("Hola"), TestData.Now);

        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(DeliveryStatus.Received, message.Status);
        Assert.True(message.IsUnread);
        Assert.Null(message.ReadAt);
        Assert.Equal(100, message.TelegramUpdateId);
        Assert.Equal(42, message.TelegramMessageId);
        Assert.Equal(TestData.Now, message.OccurredAt);
        Assert.Equal("Hola", message.Text.Value);
        Assert.Null(message.LastQueuedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Inbound_NonPositiveUpdateId_Throws(long updateId) =>
        Assert.Throws<DomainException>(() => Message.Inbound(ContactId, updateId, 42, TestData.Text(), TestData.Now));

    [Fact]
    public void Inbound_EmptyContact_Throws() =>
        Assert.Throws<DomainException>(() => Message.Inbound(Guid.Empty, 1, 42, TestData.Text(), TestData.Now));

    [Fact]
    public void Inbound_NonUtcDate_Throws() =>
        Assert.Throws<DomainException>(() => Message.Inbound(ContactId, 1, 42, TestData.Text(), new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(-3))));

    [Fact]
    public void Outbound_ValidData_IsPendingReadAndQueued()
    {
        var message = Message.Outbound(ContactId, TestData.Text("Respuesta"), TestData.Now);

        Assert.Equal(MessageDirection.Outbound, message.Direction);
        Assert.Equal(DeliveryStatus.Pending, message.Status);
        Assert.False(message.IsUnread);
        Assert.Equal(TestData.Now, message.ReadAt);
        Assert.Equal(TestData.Now, message.LastQueuedAt);
        Assert.Null(message.TelegramUpdateId);
        Assert.Null(message.TelegramMessageId);
    }

    [Fact]
    public void Outbound_NonUtcTime_Throws() =>
        Assert.Throws<DomainException>(() => Message.Outbound(ContactId, TestData.Text(), DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(1))));

    [Fact]
    public void MarkAsRead_UnreadInbound_SetsReadAtAndReturnsTrue()
    {
        var message = TestData.Inbound(ContactId);

        Assert.True(message.MarkAsRead(TestData.Now.AddMinutes(1)));
        Assert.Equal(TestData.Now.AddMinutes(1), message.ReadAt);
    }

    [Fact]
    public void MarkAsRead_AlreadyRead_IsIdempotentAndKeepsFirstReadAt()
    {
        var message = TestData.Inbound(ContactId);
        message.MarkAsRead(TestData.Now.AddMinutes(1));

        Assert.False(message.MarkAsRead(TestData.Now.AddMinutes(2)));
        Assert.Equal(TestData.Now.AddMinutes(1), message.ReadAt);
    }

    [Fact]
    public void MarkAsRead_Outbound_IsNeverUnreadSoNothingChanges()
    {
        var message = TestData.Outbound(ContactId);

        Assert.False(message.MarkAsRead(TestData.Now.AddMinutes(1)));
        Assert.Equal(TestData.Now, message.ReadAt);
    }

    [Fact]
    public void MarkAsRead_NonUtcTime_Throws() =>
        Assert.Throws<DomainException>(() => TestData.Inbound(ContactId).MarkAsRead(DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(-5))));

    [Fact]
    public void MarkSent_Pending_BecomesSentWithTelegramData()
    {
        var message = TestData.Outbound(ContactId);
        var deliveredAt = TestData.Now.AddSeconds(2);

        message.MarkSent(777, deliveredAt);

        Assert.Equal(DeliveryStatus.Sent, message.Status);
        Assert.Equal(777, message.TelegramMessageId);
        Assert.Equal(deliveredAt, message.DeliveredAt);
        Assert.Null(message.FailureReason);
    }

    [Fact]
    public void MarkFailed_Pending_BecomesFailedWithReason()
    {
        var message = TestData.Outbound(ContactId);

        message.MarkFailed("rejected: Forbidden: bot was blocked by the user");

        Assert.Equal(DeliveryStatus.Failed, message.Status);
        Assert.Equal("rejected: Forbidden: bot was blocked by the user", message.FailureReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void MarkFailed_BlankReason_Throws(string reason) =>
        Assert.Throws<DomainException>(() => TestData.Outbound(ContactId).MarkFailed(reason));

    [Fact]
    public void RequeueForRetry_Failed_BecomesPendingAndClearsReason()
    {
        var message = TestData.Outbound(ContactId);
        message.MarkFailed("unavailable: timeout");
        var retriedAt = TestData.Now.AddMinutes(3);

        message.RequeueForRetry(retriedAt);

        Assert.Equal(DeliveryStatus.Pending, message.Status);
        Assert.Null(message.FailureReason);
        Assert.Equal(retriedAt, message.LastQueuedAt);
        Assert.Equal(TestData.Now, message.OccurredAt);
    }

    [Fact]
    public void RequeueForRetry_ThenMarkSent_CompletesTheCycle()
    {
        var message = TestData.Outbound(ContactId);
        message.MarkFailed("unavailable: timeout");
        message.RequeueForRetry(TestData.Now.AddMinutes(1));

        message.MarkSent(5, TestData.Now.AddMinutes(1));

        Assert.Equal(DeliveryStatus.Sent, message.Status);
    }

    [Fact]
    public void RecordRequeued_Pending_UpdatesLastQueuedAt()
    {
        var message = TestData.Outbound(ContactId);

        message.RecordRequeued(TestData.Now.AddMinutes(2));

        Assert.Equal(TestData.Now.AddMinutes(2), message.LastQueuedAt);
        Assert.Equal(DeliveryStatus.Pending, message.Status);
    }

    public static TheoryData<string> InvalidOutboundTransitions => new()
    {
        "Sent.MarkSent", "Sent.MarkFailed", "Sent.RequeueForRetry", "Sent.RecordRequeued",
        "Failed.MarkSent", "Failed.MarkFailed", "Failed.RecordRequeued",
        "Pending.RequeueForRetry",
    };

    [Theory]
    [MemberData(nameof(InvalidOutboundTransitions))]
    public void Transition_InvalidFromState_Throws(string transition)
    {
        var (from, operation) = (transition.Split('.')[0], transition.Split('.')[1]);
        var message = TestData.Outbound(ContactId);
        if (from == "Sent")
        {
            message.MarkSent(1, TestData.Now);
        }
        else if (from == "Failed")
        {
            message.MarkFailed("rejected: x");
        }

        var statusBefore = message.Status;
        Assert.Throws<DomainException>(() => Apply(message, operation));
        Assert.Equal(statusBefore, message.Status);
    }

    [Theory]
    [InlineData("MarkSent")]
    [InlineData("MarkFailed")]
    [InlineData("RequeueForRetry")]
    [InlineData("RecordRequeued")]
    public void Transition_OnInboundMessage_Throws(string operation)
    {
        var message = TestData.Inbound(ContactId);

        Assert.Throws<DomainException>(() => Apply(message, operation));
        Assert.Equal(DeliveryStatus.Received, message.Status);
    }

    [Fact]
    public void MarkSent_NonUtcDeliveredAt_Throws() =>
        Assert.Throws<DomainException>(() => TestData.Outbound(ContactId).MarkSent(1, DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(3))));

    [Fact]
    public void Outbound_EmptyContact_Throws() =>
        Assert.Throws<DomainException>(() => Message.Outbound(Guid.Empty, TestData.Text(), TestData.Now));

    [Fact]
    public void RequeueForRetry_NonUtcTime_Throws()
    {
        var message = TestData.Outbound(ContactId);
        message.MarkFailed("unavailable: timeout");

        Assert.Throws<DomainException>(() => message.RequeueForRetry(DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(-3))));
    }

    [Fact]
    public void RecordRequeued_NonUtcTime_Throws() =>
        Assert.Throws<DomainException>(() => TestData.Outbound(ContactId).RecordRequeued(DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(-3))));

    [Fact]
    public void MarkFailed_AlreadyFailed_KeepsTheOriginalReason()
    {
        var message = TestData.Outbound(ContactId);
        message.MarkFailed("rejected: Forbidden: bot was blocked by the user");

        Assert.Throws<DomainException>(() => message.MarkFailed("unavailable: timeout"));
        Assert.Equal("rejected: Forbidden: bot was blocked by the user", message.FailureReason);
    }

    [Fact]
    public void MarkSent_AlreadySent_KeepsTheFirstTelegramData()
    {
        var message = TestData.Outbound(ContactId);
        message.MarkSent(777, TestData.Now.AddSeconds(1));

        Assert.Throws<DomainException>(() => message.MarkSent(888, TestData.Now.AddSeconds(5)));
        Assert.Equal(777, message.TelegramMessageId);
        Assert.Equal(TestData.Now.AddSeconds(1), message.DeliveredAt);
    }

    [Fact]
    public void Outbound_ThroughFailAndRetry_IsNeverUnreadAndKeepsItsPosition()
    {
        var message = TestData.Outbound(ContactId);

        message.MarkFailed("unavailable: timeout");
        message.RequeueForRetry(TestData.Now.AddMinutes(5));
        message.MarkSent(9, TestData.Now.AddMinutes(5));

        Assert.False(message.IsUnread);
        Assert.Equal(TestData.Now, message.ReadAt);
        Assert.Equal(TestData.Now, message.OccurredAt);
        Assert.Null(message.FailureReason);
    }

    [Fact]
    public void IsAtOrBefore_OrdersByOccurredAtThenSeq()
    {
        var early = TestData.Inbound(ContactId, 1, TestData.Now).WithSeq(5);
        var sameTimeHigherSeq = TestData.Inbound(ContactId, 2, TestData.Now).WithSeq(6);
        var later = TestData.Inbound(ContactId, 3, TestData.Now.AddSeconds(1)).WithSeq(1);

        Assert.True(early.IsAtOrBefore(sameTimeHigherSeq));
        Assert.False(sameTimeHigherSeq.IsAtOrBefore(early));
        Assert.True(sameTimeHigherSeq.IsAtOrBefore(later));
        Assert.True(early.IsAtOrBefore(early));
    }

    private static void Apply(Message message, string operation)
    {
        switch (operation)
        {
            case "MarkSent": message.MarkSent(1, TestData.Now); break;
            case "MarkFailed": message.MarkFailed("reason"); break;
            case "RequeueForRetry": message.RequeueForRetry(TestData.Now); break;
            case "RecordRequeued": message.RecordRequeued(TestData.Now); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
