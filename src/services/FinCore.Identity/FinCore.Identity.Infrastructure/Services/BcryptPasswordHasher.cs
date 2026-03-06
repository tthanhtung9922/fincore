using FinCore.Identity.Application.Services;

namespace FinCore.Identity.Infrastructure.Services;

public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string plaintext) => BCrypt.Net.BCrypt.HashPassword(plaintext, workFactor: 12);
    public bool Verify(string plaintext, string hash) => BCrypt.Net.BCrypt.Verify(plaintext, hash);
}
