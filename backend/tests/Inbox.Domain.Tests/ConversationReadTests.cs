using Inbox.Domain.Common;

namespace Inbox.Domain.Tests;

public sealed class ConversationReadTests
{
    [Fact]
    public void MarkConversationReadUpTo_MarksOnlyMessagesAtOrBeforeUpTo()
    {
        var contact = TestData.Contact();
        var first = TestData.Inbound(contact.Id, 1, TestData.Now).WithSeq(1);
        var seen = TestData.Inbound(contact.Id, 2, TestData.Now).WithSeq(2);
        var arrivedLater = TestData.Inbound(contact.Id, 3, TestData.Now.AddSeconds(1)).WithSeq(3);
        var readAt = TestData.Now.AddMinutes(1);

        var changed = contact.MarkConversationReadUpTo(seen, [first, seen, arrivedLater], readAt);

        Assert.Equal(2, changed);
        Assert.Equal(readAt, first.ReadAt);
        Assert.Equal(readAt, seen.ReadAt);
        Assert.Null(arrivedLater.ReadAt);
    }

    [Fact]
    public void MarkConversationReadUpTo_SameTimestamp_UsesSeqAsTieBreaker()
    {
        var contact = TestData.Contact();
        var upTo = TestData.Inbound(contact.Id, 1, TestData.Now).WithSeq(10);
        var sameSecondLaterSeq = TestData.Inbound(contact.Id, 2, TestData.Now).WithSeq(11);

        var changed = contact.MarkConversationReadUpTo(upTo, [upTo, sameSecondLaterSeq], TestData.Now);

        Assert.Equal(1, changed);
        Assert.False(upTo.IsUnread);
        Assert.True(sameSecondLaterSeq.IsUnread);
    }

    [Fact]
    public void MarkConversationReadUpTo_CalledTwice_IsIdempotent()
    {
        var contact = TestData.Contact();
        var message = TestData.Inbound(contact.Id).WithSeq(1);
        contact.MarkConversationReadUpTo(message, [message], TestData.Now);

        var changed = contact.MarkConversationReadUpTo(message, [message], TestData.Now.AddMinutes(1));

        Assert.Equal(0, changed);
        Assert.Equal(TestData.Now, message.ReadAt);
    }

    [Fact]
    public void MarkConversationReadUpTo_OutboundReplies_AreNotCountedNorChanged()
    {
        var contact = TestData.Contact();
        var inbound = TestData.Inbound(contact.Id, 1, TestData.Now).WithSeq(1);
        var reply = TestData.Outbound(contact.Id).WithSeq(2);
        var latest = TestData.Inbound(contact.Id, 2, TestData.Now.AddSeconds(1)).WithSeq(3);
        var readAt = TestData.Now.AddMinutes(1);

        var changed = contact.MarkConversationReadUpTo(latest, [inbound, reply, latest], readAt);

        Assert.Equal(2, changed);
        Assert.Equal(TestData.Now, reply.ReadAt);
        Assert.Equal(readAt, latest.ReadAt);
    }

    [Fact]
    public void MarkConversationReadUpTo_UpToFromAnotherContact_Throws()
    {
        var contact = TestData.Contact(1);
        var other = TestData.Contact(2);
        var foreign = TestData.Inbound(other.Id).WithSeq(1);

        Assert.Throws<DomainException>(() => contact.MarkConversationReadUpTo(foreign, [], TestData.Now));
    }

    [Fact]
    public void MarkConversationReadUpTo_ConversationContainsForeignMessage_Throws()
    {
        var contact = TestData.Contact(1);
        var mine = TestData.Inbound(contact.Id, 1).WithSeq(1);
        var foreign = TestData.Inbound(TestData.Contact(2).Id, 2).WithSeq(2);

        Assert.Throws<DomainException>(() => contact.MarkConversationReadUpTo(mine, [mine, foreign], TestData.Now));
    }
}
