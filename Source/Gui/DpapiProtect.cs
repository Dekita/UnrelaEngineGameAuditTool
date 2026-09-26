using System.Security.Cryptography;
using System.Text;

namespace DekUnrealGameAudit.Gui;

/// <summary>Wraps Windows DPAPI (current-user scope) for persisting a secret (the AES key) to disk without
/// storing it as plain text - a copy of gui-settings.json/profiles.json is only decryptable by the same
/// Windows user account on the same machine that wrote it.</summary>
public static class DpapiProtect {
    private const string Prefix = "dpapi:";

    /// <summary>Null/empty passes through unchanged - nothing to protect. Fails closed: if DPAPI itself fails,
    /// returns null (via <paramref name="succeeded"/> = false) rather than the plaintext value, so a broken
    /// user profile can't turn into a plaintext key on disk. The caller decides how to surface that (e.g. by
    /// leaving the on-disk value untouched, or omitting the key with a warning) - see
    /// <see cref="AppSettings.AesKeyForStorage"/>.</summary>
    public static string? Protect(string? plaintext, out bool succeeded) {
        return Protect(plaintext,
            bytes => ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser), out succeeded);
    }

    internal static string? Protect(string? plaintext, Func<byte[], byte[]> protect, out bool succeeded) {
        succeeded = true;
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        try {
            var protectedBytes = protect(Encoding.UTF8.GetBytes(plaintext));
            return Prefix + Convert.ToBase64String(protectedBytes);
        } catch {
            succeeded = false;
            return null;
        }
    }

    /// <summary>A value without the "dpapi:" prefix is legacy plaintext from before encryption was added -
    /// returned as-is rather than rejected, so existing saved settings/profiles keep working. A value that
    /// looks protected but can't actually be decrypted (different user or machine) is dropped rather than
    /// surfaced as garbage - the user just needs to re-enter the key, same as if it were never saved.</summary>
    public static string? Unprotect(string? stored) {
        return Unprotect(stored,
            bytes => ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
    }

    internal static string? Unprotect(string? stored, Func<byte[], byte[]> unprotect) {
        if (string.IsNullOrEmpty(stored))
            return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        try {
            var protectedBytes = Convert.FromBase64String(stored[Prefix.Length..]);
            return Encoding.UTF8.GetString(unprotect(protectedBytes));
        } catch {
            return null;
        }
    }
}
