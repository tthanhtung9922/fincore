namespace FinCore.Identity.Application.Queries.GetUserById;

public record UserDto(Guid Id, string Email, string Role, bool IsActive, DateTimeOffset CreatedAt);
