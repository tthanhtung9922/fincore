using FinCore.Identity.Domain.ValueObjects;
using FinCore.SharedKernel.Domain.Exceptions;
using FluentAssertions;

namespace FinCore.Identity.Domain.Tests;

public class EmailValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@nodomain")]
    [InlineData("noatsign.com")]
    public void Email_InvalidFormat_ThrowsException(string invalidEmail)
    {
        var act = () => new Email(invalidEmail);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TwoEmailsWithSameValue_AreEqual()
    {
        var email1 = new Email("User@Example.COM");
        var email2 = new Email("user@example.com");

        email1.Should().Be(email2);
        (email1 == email2).Should().BeTrue();
    }

    [Fact]
    public void TwoEmailsWithDifferentValues_AreNotEqual()
    {
        var email1 = new Email("user1@example.com");
        var email2 = new Email("user2@example.com");

        email1.Should().NotBe(email2);
    }

    [Fact]
    public void Email_StoredAsLowercase()
    {
        var email = new Email("User@Example.COM");
        email.Value.Should().Be("user@example.com");
    }
}
