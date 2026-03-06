using FinCore.Identity.Domain.Aggregates;
using FinCore.Identity.Domain.Enums;
using FinCore.Identity.Domain.Events;
using FinCore.Identity.Domain.Exceptions;
using FinCore.Identity.Domain.ValueObjects;
using FinCore.SharedKernel.Domain.Exceptions;
using FluentAssertions;

namespace FinCore.Identity.Domain.Tests;

public class UserAggregateTests
{
    private static readonly Email TestEmail = new("test@example.com");
    private static readonly HashedPassword TestHash = new("$2a$12$fakehash");

    [Fact]
    public void Register_WithValidData_RaisesUserRegisteredEvent()
    {
        var user = User.Register(TestEmail, TestHash, UserRole.Customer);

        user.DomainEvents.Should().HaveCount(1);
        user.DomainEvents[0].Should().BeOfType<UserRegistered>();

        var evt = (UserRegistered)user.DomainEvents[0];
        evt.Email.Should().Be(TestEmail.Value);
        evt.Role.Should().Be(UserRole.Customer.ToString());
    }

    [Fact]
    public void Register_WithValidData_SetsProperties()
    {
        var user = User.Register(TestEmail, TestHash, UserRole.Admin);

        user.Email.Should().Be(TestEmail);
        user.Role.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_ActiveUser_RaisesUserDeactivatedEvent()
    {
        var user = User.Register(TestEmail, TestHash, UserRole.Customer);
        user.ClearDomainEvents();

        user.Deactivate();

        user.DomainEvents.Should().HaveCount(1);
        user.DomainEvents[0].Should().BeOfType<UserDeactivated>();
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_AlreadyDeactivated_ThrowsDomainException()
    {
        var user = User.Register(TestEmail, TestHash, UserRole.Customer);
        user.Deactivate();

        var act = () => user.Deactivate();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Login_ActiveUser_RaisesUserLoggedInEvent()
    {
        var user = User.Register(TestEmail, TestHash, UserRole.Customer);
        user.ClearDomainEvents();

        user.Login();

        user.DomainEvents.Should().HaveCount(1);
        user.DomainEvents[0].Should().BeOfType<UserLoggedIn>();
    }

    [Fact]
    public void Login_InactiveUser_ThrowsUserDeactivatedException()
    {
        var user = User.Register(TestEmail, TestHash, UserRole.Customer);
        user.Deactivate();

        var act = () => user.Login();

        act.Should().Throw<UserDeactivatedException>();
    }
}
