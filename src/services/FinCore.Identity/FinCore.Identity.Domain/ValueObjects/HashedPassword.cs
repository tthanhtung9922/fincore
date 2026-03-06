using FinCore.SharedKernel.Domain;
using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Identity.Domain.ValueObjects;

public sealed class HashedPassword : ValueObject
{
    public string Value { get; }

    public HashedPassword(string hashedValue)
    {
        if (string.IsNullOrWhiteSpace(hashedValue))
            throw new DomainException("Password hash cannot be empty.");
        Value = hashedValue;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
