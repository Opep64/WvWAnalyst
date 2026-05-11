using System.Text;
using Microsoft.Extensions.Configuration;
using WvWAnalyst.Api.Configuration;

namespace WvWAnalyst.Api.Auth;

public static class AuthUserCli
{
    public static bool TryRun(string[] args, IConfiguration configuration, string contentRootPath)
    {
        if (args.Length == 0 || !string.Equals(args[0], "users", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var options = configuration.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>() ?? new AuthenticationOptions();
        var usersPath = AuthUserStore.ResolveUsersPath(contentRootPath, options.UsersPath);
        var store = new AuthUserStore(usersPath);
        var passwordHashService = new PasswordHashService();
        var command = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "help";

        switch (command)
        {
            case "add":
                AddOrResetUser(store, passwordHashService, args, resetOnly: false);
                return true;
            case "reset-password":
                AddOrResetUser(store, passwordHashService, args, resetOnly: true);
                return true;
            case "remove":
                RemoveUser(store, args);
                return true;
            case "list":
                ListUsers(store);
                return true;
            case "help":
            default:
                PrintHelp(usersPath);
                return true;
        }
    }

    private static void AddOrResetUser(AuthUserStore store, PasswordHashService passwordHashService, string[] args, bool resetOnly)
    {
        string username = args.Length > 2 ? AuthUserStore.NormalizeUsername(args[2]) : string.Empty;
        if (username.Length == 0)
        {
            Console.Error.WriteLine("A username is required.");
            PrintHelp(store.UsersPath);
            return;
        }

        if (resetOnly && store.FindUser(username) is null)
        {
            Console.Error.WriteLine($"User '{username}' does not exist.");
            return;
        }

        string password = ReadPassword("Password: ");
        string confirmation = ReadPassword("Confirm password: ");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords did not match.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("Password cannot be empty.");
            return;
        }

        store.UpsertUser(username, passwordHashService.HashPassword(password), out bool created);
        Console.WriteLine(created ? $"Added user '{username}'." : $"Updated password for '{username}'.");
        Console.WriteLine($"User store: {store.UsersPath}");
    }

    private static void RemoveUser(AuthUserStore store, string[] args)
    {
        string username = args.Length > 2 ? AuthUserStore.NormalizeUsername(args[2]) : string.Empty;
        if (username.Length == 0)
        {
            Console.Error.WriteLine("A username is required.");
            PrintHelp(store.UsersPath);
            return;
        }

        Console.WriteLine(store.RemoveUser(username)
            ? $"Removed user '{username}'."
            : $"User '{username}' was not found.");
    }

    private static void ListUsers(AuthUserStore store)
    {
        var users = store.GetUsers();
        if (users.Count == 0)
        {
            Console.WriteLine("No users configured.");
            Console.WriteLine($"User store: {store.UsersPath}");
            return;
        }

        foreach (var user in users)
        {
            Console.WriteLine($"{user.Username}\tupdated {user.UpdatedAtUtc}");
        }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static void PrintHelp(string usersPath)
    {
        Console.WriteLine("WvWAnalyst user commands:");
        Console.WriteLine("  users list");
        Console.WriteLine("  users add <username>");
        Console.WriteLine("  users reset-password <username>");
        Console.WriteLine("  users remove <username>");
        Console.WriteLine($"User store: {usersPath}");
    }
}
