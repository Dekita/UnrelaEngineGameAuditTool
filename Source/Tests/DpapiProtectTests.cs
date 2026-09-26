using System.Text;
using DekUnrealGameAudit.Gui;

namespace DekUnrealGameAudit.Tests;

public class DpapiProtectTests {
    [Fact]
    public void Protect_PassesEmptyValueThroughWithoutCallingDpapi() {
        var called = false;

        var result = DpapiProtect.Protect("", bytes => {
            called = true;
            return bytes;
        }, out var succeeded);

        Assert.True(succeeded);
        Assert.False(called);
        Assert.Equal("", result);
    }

    [Fact]
    public void Protect_FailsClosedWhenDpapiThrows() {
        var result = DpapiProtect.Protect("secret", _ => throw new InvalidOperationException(), out var succeeded);

        Assert.False(succeeded);
        Assert.Null(result);
    }

    [Fact]
    public void ProtectAndUnprotect_UseTheProtectedPayload() {
        static byte[] Reverse(byte[] bytes) => bytes.Reverse().ToArray();

        var stored = DpapiProtect.Protect("secret", Reverse, out var succeeded);
        var plaintext = DpapiProtect.Unprotect(stored, Reverse);

        Assert.True(succeeded);
        Assert.StartsWith("dpapi:", stored);
        Assert.Equal("secret", plaintext);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("secret")), stored);
    }

    [Fact]
    public void Unprotect_ReturnsNullForCorruptOrUndecryptablePayload() {
        Assert.Null(DpapiProtect.Unprotect("dpapi:not-base64", bytes => bytes));
        Assert.Null(DpapiProtect.Unprotect("dpapi:AA==", _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void Unprotect_PreservesLegacyPlaintextWithoutCallingDpapi() {
        var called = false;

        var result = DpapiProtect.Unprotect("legacy-key", bytes => {
            called = true;
            return bytes;
        });

        Assert.False(called);
        Assert.Equal("legacy-key", result);
    }
}
