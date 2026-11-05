using Inbox.Domain.Common;
using Inbox.Domain.Contacts;

namespace Inbox.Domain.Tests;

public sealed class ContactTests
{
    [Fact]
    public void FromTelegram_ValidData_SetsProfileAndTimestamps()
    {
        var contact = Contact.FromTelegram(123, " Ana ", " Pérez ", "@anap", TestData.Now);

        Assert.NotEqual(Guid.Empty, contact.Id);
        Assert.Equal(123, contact.TelegramChatId);
        Assert.Equal("Ana", contact.FirstName);
        Assert.Equal("Pérez", contact.LastName);
        Assert.Equal("anap", contact.Username);
        Assert.Equal(TestData.Now, contact.CreatedAt);
        Assert.Equal(TestData.Now, contact.UpdatedAt);
    }

    [Fact]
    public void FromTelegram_ZeroChatId_Throws() =>
        Assert.Throws<DomainException>(() => Contact.FromTelegram(0, "Ana", null, null, TestData.Now));

    [Fact]
    public void FromTelegram_NonUtcTime_Throws() =>
        Assert.Throws<DomainException>(() => Contact.FromTelegram(1, "Ana", null, null, new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(-3))));

    [Fact]
    public void FromTelegram_NullFirstName_StoresEmptyString()
    {
        var contact = Contact.FromTelegram(1, null, null, "anap", TestData.Now);

        Assert.Equal(string.Empty, contact.FirstName);
    }

    [Theory]
    [InlineData("Ana", "Pérez", "anap", "Ana Pérez")]
    [InlineData("Ana", null, "anap", "Ana")]
    [InlineData("", "Pérez", null, "Pérez")]
    [InlineData("  ", "  ", "anap", "@anap")]
    [InlineData(null, null, "anap", "@anap")]
    [InlineData(null, null, null, "555")]
    [InlineData("", "", "  ", "555")]
    public void DisplayName_Fallbacks_NameThenUsernameThenChatId(string? first, string? last, string? username, string expected)
    {
        var contact = Contact.FromTelegram(555, first, last, username, TestData.Now);

        Assert.Equal(expected, contact.DisplayName);
    }

    [Fact]
    public void UpdateProfile_ChangedData_UpdatesFieldsAndUpdatedAt()
    {
        var contact = TestData.Contact();
        var later = TestData.Now.AddMinutes(5);

        var changed = contact.UpdateProfile("Ana María", null, "anamaria", later);

        Assert.True(changed);
        Assert.Equal("Ana María", contact.FirstName);
        Assert.Null(contact.LastName);
        Assert.Equal("anamaria", contact.Username);
        Assert.Equal(later, contact.UpdatedAt);
        Assert.Equal(TestData.Now, contact.CreatedAt);
    }

    [Fact]
    public void UpdateProfile_SameData_ReturnsFalseAndKeepsUpdatedAt()
    {
        var contact = TestData.Contact();

        var changed = contact.UpdateProfile("Ana", "Pérez", "anap", TestData.Now.AddMinutes(5));

        Assert.False(changed);
        Assert.Equal(TestData.Now, contact.UpdatedAt);
    }

    [Fact]
    public void UpdateProfile_NonUtcTime_Throws() =>
        Assert.Throws<DomainException>(() => TestData.Contact().UpdateProfile("X", null, null, DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(2))));
}
