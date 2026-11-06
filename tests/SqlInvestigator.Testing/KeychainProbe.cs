using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SqlInvestigator.Testing;

/// <summary>
/// Looks at the macOS keychain from the outside, as an application other than SqlInvestigator. It re-derives the
/// item naming the vault promises (service "SqlInvestigator/1 " + 16 hex digits of SHA-256 over the data home's full
/// path; account = alias) instead of calling the implementation, and it never prompts: interaction is disallowed
/// for this process, so a read the keychain would only allow after the user's approval fails with a status code.
/// </summary>
[SupportedOSPlatform("macos")]
public static class KeychainProbe
{
    public const int Success = 0;
    public const int ItemNotFound = -25300;
    public const int InteractionNotAllowed = -25308;
    public const int AuthFailed = -25293;
    public const int UserCanceled = -128;

    private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public static string ServiceFor(string home) =>
        "SqlInvestigator/1 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(home)))).ToLowerInvariant()[..16];

    /// <summary>The status of an attributes-only lookup (never needs approval): 0 when the item exists.</summary>
    public static int Lookup(string home, string alias) => CopyMatching(home, alias, returnData: false, out _);

    /// <summary>
    /// The status of reading the item's data as this (other) application, without any prompt. An item that only its
    /// creating SqlInvestigator may read answers InteractionNotAllowed (or AuthFailed); Success means any
    /// application of this user could read the secret silently.
    /// </summary>
    public static int ReadAsOtherApplication(string home, string alias, out string? value)
    {
        var status = CopyMatching(home, alias, returnData: true, out var bytes);
        value = bytes is null ? null : Encoding.UTF8.GetString(bytes);
        return status;
    }

    /// <summary>True when the status means "the keychain wants the user's approval for this application".</summary>
    public static bool NeedsApproval(int status) => status is InteractionNotAllowed or AuthFailed or UserCanceled;

    /// <summary>Delete an item the tests left behind. Used only for clean-up of disposable data homes.</summary>
    public static int Delete(string home, string alias)
    {
        Prepare();
        var query = Query(home, alias, out var owned);
        try
        {
            return SecItemDelete(query);
        }
        finally
        {
            Release(query, owned);
        }
    }

    private static int CopyMatching(string home, string alias, bool returnData, out byte[]? data)
    {
        Prepare();
        data = null;
        var query = Query(home, alias, out var owned);
        try
        {
            CFDictionarySetValue(query, Const("kSecMatchLimit"), Const("kSecMatchLimitOne"));
            CFDictionarySetValue(query, Const("kSecUseAuthenticationUI"), Const("kSecUseAuthenticationUIFail"));
            CFDictionarySetValue(query, Const(returnData ? "kSecReturnData" : "kSecReturnAttributes"), CfConst("kCFBooleanTrue"));
            var status = SecItemCopyMatching(query, out var result);
            if (result != IntPtr.Zero)
            {
                if (status == Success && returnData)
                {
                    var length = (int)CFDataGetLength(result);
                    data = new byte[length];
                    Marshal.Copy(CFDataGetBytePtr(result), data, 0, length);
                }

                CFRelease(result);
            }

            return status;
        }
        finally
        {
            Release(query, owned);
        }
    }

    private static IntPtr Query(string home, string alias, out List<IntPtr> owned)
    {
        owned = [];
        var cf = NativeLibrary.Load(CoreFoundationLib);
        var query = CFDictionaryCreateMutable(IntPtr.Zero, 0,
            NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks"));
        var service = Str(ServiceFor(home));
        var account = Str(alias);
        owned.Add(service);
        owned.Add(account);
        CFDictionarySetValue(query, Const("kSecClass"), Const("kSecClassGenericPassword"));
        CFDictionarySetValue(query, Const("kSecAttrService"), service);
        CFDictionarySetValue(query, Const("kSecAttrAccount"), account);
        return query;
    }

    private static void Release(IntPtr query, List<IntPtr> owned)
    {
        foreach (var o in owned)
        {
            CFRelease(o);
        }

        CFRelease(query);
    }

    private static void Prepare() => _ = SecKeychainSetUserInteractionAllowed(false);

    private static IntPtr Str(string value) => CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(value + "\0"), 0x08000100);

    private static IntPtr Const(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(SecurityLib), name));

    private static IntPtr CfConst(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundationLib), name));

#pragma warning disable SYSLIB1054
    [DllImport(SecurityLib)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(SecurityLib)] private static extern int SecItemDelete(IntPtr query);
    [DllImport(SecurityLib)] private static extern int SecKeychainSetUserInteractionAllowed([MarshalAs(UnmanagedType.U1)] bool state);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);
    [DllImport(CoreFoundationLib)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] cString, uint encoding);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundationLib)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundationLib)] private static extern void CFRelease(IntPtr cf);
#pragma warning restore SYSLIB1054
}
