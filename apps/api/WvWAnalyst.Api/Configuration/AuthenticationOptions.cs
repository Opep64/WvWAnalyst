namespace WvWAnalyst.Api.Configuration;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public bool Enabled { get; set; }

    public string UsersPath { get; set; } = @"..\..\..\storage\auth-users.json";

    public string CookieName { get; set; } = "WvWAnalyst.Auth";
}
