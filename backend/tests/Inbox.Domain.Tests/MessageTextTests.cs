using Inbox.Domain.Common;
using Inbox.Domain.Messages;

namespace Inbox.Domain.Tests;

public sealed class MessageTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void Create_BlankText_Throws(string? value) =>
        Assert.Throws<DomainException>(() => MessageText.Create(value));

    [Fact]
    public void Create_TextAtMaxLength_Succeeds()
    {
        var text = MessageText.Create(new string('a', MessageText.MaxLength));

        Assert.Equal(MessageText.MaxLength, text.Value.Length);
    }

    [Fact]
    public void Create_TextOverMaxLength_Throws() =>
        Assert.Throws<DomainException>(() => MessageText.Create(new string('a', MessageText.MaxLength + 1)));

    [Fact]
    public void Create_LengthCountsUtf16CodeUnits_EmojiCountsTwice()
    {
        var emoji = "😀"; // one code point, two UTF-16 code units
        var fits = string.Concat(Enumerable.Repeat(emoji, MessageText.MaxLength / 2));

        Assert.Equal(MessageText.MaxLength, MessageText.Create(fits).Value.Length);
        Assert.Throws<DomainException>(() => MessageText.Create(fits + "a"));
    }

    [Fact]
    public void Create_TextWithSurroundingSpaces_KeepsItLiterally()
    {
        var text = MessageText.Create("  hola  ");

        Assert.Equal("  hola  ", text.Value);
    }

    [Fact]
    public void Equals_SameValue_AreEqual() =>
        Assert.Equal(MessageText.Create("hola"), MessageText.Create("hola"));
}
