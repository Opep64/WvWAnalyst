namespace WvWAnalyst.Api.Auth;

public sealed record LoginRequestDto(string? Username, string? Password);

public sealed record ChangePasswordRequestDto(string? CurrentPassword, string? NewPassword, string? ConfirmPassword);

public sealed record AuthStateDto(bool Enabled, bool Authenticated, string? Username);
