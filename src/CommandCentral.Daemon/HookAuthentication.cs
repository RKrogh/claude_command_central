using System.Security.Cryptography;
using System.Text;
using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Options;

namespace CommandCentral.Daemon;

/// <summary>
/// Resolves the hook shared secret: explicit config value first, otherwise a
/// secret file that is auto-generated on first run. The install script embeds
/// the same file's content into the hook curl commands, so the value itself
/// never lives in appsettings.json or Claude Code settings.
/// </summary>
public sealed class FileHookSecretProvider(
    IOptions<CommandCentralOptions> options,
    ILogger<FileHookSecretProvider> logger) : IHookSecretProvider
{
    public static string DefaultSecretFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommandCentral", "hook-secret");

    private readonly Lazy<string?> _secret = new(
        () => Resolve(options.Value.HookAuth, logger),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public string? Secret => _secret.Value;

    private static string? Resolve(HookAuthOptions opts, ILogger logger)
    {
        if (!opts.Enabled)
        {
            logger.LogInformation("Hook authentication disabled via config (CommandCentral:HookAuth:Enabled)");
            return null;
        }

        if (!string.IsNullOrEmpty(opts.Secret))
            return opts.Secret;

        var path = string.IsNullOrEmpty(opts.SecretFilePath)
            ? DefaultSecretFilePath
            : Environment.ExpandEnvironmentVariables(opts.SecretFilePath);

        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (existing.Length > 0)
                {
                    logger.LogInformation("Hook authentication enabled (secret file: {Path})", path);
                    return existing;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var generated = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(path, generated);
            logger.LogWarning(
                "Generated a new hook secret at {Path}. Re-run scripts/install-hooks.sh (or .ps1) " +
                "so hook requests send it — until then, hooks will be rejected with 401", path);
            return generated;
        }
        catch (Exception ex)
        {
            // Fail open: a broken secret file must not brick the control plane,
            // but say so loudly.
            logger.LogError(ex,
                "Could not read or create the hook secret at {Path} — hook authentication is NOT enforced", path);
            return null;
        }
    }
}

/// <summary>
/// Validates the "Authorization: Bearer" header on hook requests against the
/// provider's secret. No secret configured → requests pass through.
/// </summary>
public static class HookAuthentication
{
    private const string BearerPrefix = "Bearer ";
    private static int _warnedRejection;

    public static async ValueTask<object?> FilterAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var secret = services.GetRequiredService<IHookSecretProvider>().Secret;
        if (secret is null)
            return await next(context);

        var header = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (IsAuthorized(header, secret))
            return await next(context);

        if (Interlocked.Exchange(ref _warnedRejection, 1) == 0)
        {
            services.GetRequiredService<ILogger<IHookSecretProvider>>().LogWarning(
                "Rejected hook request without a valid shared secret ({Path}). If this is your own " +
                "Claude Code instance, re-run scripts/install-hooks.sh to embed the secret. " +
                "Further rejections are logged at debug level",
                context.HttpContext.Request.Path);
        }
        else
        {
            services.GetRequiredService<ILogger<IHookSecretProvider>>().LogDebug(
                "Rejected unauthenticated hook request ({Path})", context.HttpContext.Request.Path);
        }

        return Results.Unauthorized();
    }

    public static bool IsAuthorized(string? authorizationHeader, string secret)
    {
        if (authorizationHeader is null ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorizationHeader[BearerPrefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
