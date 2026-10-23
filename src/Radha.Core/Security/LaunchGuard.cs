namespace Radha.Core.Security;

/// <summary>
/// Guards anything the shell is about to execute on the user's behalf.
///
/// The config can name an executable (terminal, file manager, screenshot tool). That makes the
/// config file an execution vector: a dropped or synced <c>shell.json</c> must not be able to point
/// the shell at an arbitrary binary. Everything launched therefore goes through an allowlist,
/// refuses traversal and UNC paths, and must resolve to a file that exists.
/// </summary>
public static class LaunchGuard
{
    /// <summary>Commands a profile may name without further configuration.</summary>
    public static readonly string[] DefaultAllowlist =
    {
        "wt.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "explorer.exe", "notepad.exe", "mspaint.exe",
        "control.exe", "taskmgr.exe", "ms-settings:"
    };

    public static bool IsAllowed(string? command, IReadOnlyCollection<string>? allowlist, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(command))
        {
            reason = "empty command";
            return false;
        }

        var value = command.Trim().Trim('"');

        if (value.Contains("..", StringComparison.Ordinal))
        {
            reason = "path traversal is not allowed";
            return false;
        }

        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "UNC paths are not allowed";
            return false;
        }

        // URIs (ms-settings:, mailto:) are allowlisted by scheme+prefix, not resolved as files.
        // Careful: "C:\bin\wt.exe" also contains a colon, so a drive letter is not a scheme.
        var isDrivePath = value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' &&
                          (value[2] == '\\' || value[2] == '/');
        var colon = value.IndexOf(':');
        var isUri = !isDrivePath && colon > 1 &&
                    value[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');

        if (isUri)
        {
            var allowedUris = allowlist ?? DefaultAllowlist;
            var uriAllowed = allowedUris.Any(a => a.EndsWith(':') &&
                                                  value.StartsWith(a, StringComparison.OrdinalIgnoreCase));
            if (!uriAllowed)
            {
                reason = "only allowlisted URI schemes may be opened";
                return false;
            }

            reason = "ok";
            return true;
        }

        // Path.GetFileName treats '\\' as a separator only on Windows; a config written on Windows
        // is read here on any OS, so split on both separators explicitly.
        var normalized = value.Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..];
        var allowed = allowlist ?? DefaultAllowlist;

        if (!allowed.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
        {
            reason = $"'{name}' is not in the launcher allowlist";
            return false;
        }

        // A rooted path must exist; a bare name is resolved by the shell later.
        var rooted = Path.IsPathRooted(value) || Path.IsPathRooted(normalized) || isDrivePath;
        if (rooted && !File.Exists(value))
        {
            reason = "file does not exist";
            return false;
        }

        reason = "ok";
        return true;
    }

    /// <summary>True when the config file itself is writable by other users (a tampering signal).</summary>
    public static bool IsConfigWorldWritable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                return (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
            }
            catch (Exception) { return false; }
        }

        // Windows ACL inspection needs System.Security.AccessControl; without it we cannot claim
        // to know, so we do not report a false positive.
        return false;
    }
}
