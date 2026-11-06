using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SqlInvestigator.Connections;

/// <summary>
/// Generic-password items in the user's default (login) keychain, through Security.framework's SecItem API. The
/// secret travels only as CFData between this process and the keychain: nothing shells out to `security`, so a
/// secret never appears on a command line. An item added here gets the keychain's default access list, which trusts
/// only the executable that added it; any other application needs the user's approval in a keychain prompt.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeychain
{
    public const int Success = 0;
    public const int ItemNotFound = -25300;      // errSecItemNotFound
    public const int DuplicateItem = -25299;     // errSecDuplicateItem
    public const int InteractionNotAllowed = -25308; // errSecInteractionNotAllowed
    public const int AuthFailed = -25293;        // errSecAuthFailed
    public const int UserCanceled = -128;        // errSecUserCanceled
    public const int NoDefaultKeychain = -25307; // errSecNoDefaultKeychain
    public const int Timeout = int.MinValue;     // ours: the call did not return within the time box

    private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    private static readonly Lazy<Symbols> Sym = new(() => new Symbols());

    /// <summary>
    /// Forbid (or allow) keychain prompts for the whole process. The MCP server runs unattended under Claude Code, so
    /// it forbids them: a read that would need the user's approval fails at once with errSecInteractionNotAllowed
    /// instead of waiting on a dialog nobody is looking at. The local console allows them.
    /// </summary>
    public static void SetInteractionAllowed(bool allowed)
    {
        // Deprecated, but still the switch that covers file-based keychain access prompts.
        _ = SecKeychainSetUserInteractionAllowed(allowed);
    }

    /// <summary>The item's data for (service, account), or the status. With <paramref name="allowUi"/> false the call never prompts.</summary>
    public static int Read(string service, string account, bool allowUi, TimeSpan timeBox, out byte[]? data)
    {
        byte[]? found = null;
        var work = Task.Run(() =>
        {
            using var query = Query(service, account);
            query.Set(Sym.Value.ReturnData, Sym.Value.True);
            query.Set(Sym.Value.MatchLimit, Sym.Value.MatchLimitOne);
            if (!allowUi)
            {
                query.Set(Sym.Value.UseAuthenticationUI, Sym.Value.UseAuthenticationUIFail);
            }

            var status = SecItemCopyMatching(query.Handle, out var result);
            if (status == Success && result != IntPtr.Zero)
            {
                try
                {
                    found = CopyBytes(result);
                }
                finally
                {
                    CFRelease(result);
                }
            }

            return status;
        });

        // A keychain call that blocks (a prompt the process could not suppress, a hung securityd) must not hang the
        // server: after the time box the read is abandoned and reported as unavailable.
        if (!work.Wait(timeBox))
        {
            data = null;
            return Timeout;
        }

        data = found;
        return work.Result;
    }

    /// <summary>True when an item exists for (service, account). Reads attributes only, which never needs approval.</summary>
    public static int Exists(string service, string account)
    {
        using var query = Query(service, account);
        query.Set(Sym.Value.ReturnAttributes, Sym.Value.True);
        query.Set(Sym.Value.MatchLimit, Sym.Value.MatchLimitOne);
        query.Set(Sym.Value.UseAuthenticationUI, Sym.Value.UseAuthenticationUIFail);
        var status = SecItemCopyMatching(query.Handle, out var result);
        if (result != IntPtr.Zero)
        {
            CFRelease(result);
        }

        return status;
    }

    /// <summary>Add the item, or replace the data of the existing one.</summary>
    public static int Write(string service, string account, string label, byte[] secret)
    {
        using var data = CfData.From(secret);
        using (var add = Query(service, account))
        {
            add.Set(Sym.Value.Label, label);
            add.Set(Sym.Value.Description, "SQL Investigator credential");
            add.Set(Sym.Value.ValueData, data.Handle);
            var status = SecItemAdd(add.Handle, IntPtr.Zero);
            if (status != DuplicateItem)
            {
                return status;
            }
        }

        using var match = Query(service, account);
        using var change = new CfDictionary();
        change.Set(Sym.Value.ValueData, data.Handle);
        change.Set(Sym.Value.Label, label);
        return SecItemUpdate(match.Handle, change.Handle);
    }

    public static int Delete(string service, string account)
    {
        using var query = Query(service, account);
        return SecItemDelete(query.Handle);
    }

    /// <summary>The keychain's own description of a status code.</summary>
    public static string Describe(int status)
    {
        if (status == Timeout)
        {
            return "the keychain did not answer in time";
        }

        var message = SecCopyErrorMessageString(status, IntPtr.Zero);
        if (message == IntPtr.Zero)
        {
            return $"OSStatus {status}";
        }

        try
        {
            return $"{CopyString(message)} (OSStatus {status})";
        }
        finally
        {
            CFRelease(message);
        }
    }

    private static CfDictionary Query(string service, string account)
    {
        var query = new CfDictionary();
        query.Set(Sym.Value.Class, Sym.Value.ClassGenericPassword);
        query.Set(Sym.Value.Service, service);
        query.Set(Sym.Value.Account, account);
        return query;
    }

    private static byte[] CopyBytes(IntPtr cfData)
    {
        var length = (int)CFDataGetLength(cfData);
        var bytes = new byte[length];
        Marshal.Copy(CFDataGetBytePtr(cfData), bytes, 0, length);
        return bytes;
    }

    private static string CopyString(IntPtr cfString)
    {
        var length = CFStringGetLength(cfString);
        var max = CFStringGetMaximumSizeForEncoding(length, Utf8) + 1;
        var buffer = new byte[max];
        if (!CFStringGetCString(cfString, buffer, max, Utf8))
        {
            return string.Empty;
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    private sealed class CfDictionary : IDisposable
    {
        private readonly List<IntPtr> owned = [];

        public CfDictionary()
        {
            Handle = CFDictionaryCreateMutable(IntPtr.Zero, 0, Sym.Value.KeyCallBacks, Sym.Value.ValueCallBacks);
        }

        public IntPtr Handle { get; }

        public void Set(IntPtr key, IntPtr value) => CFDictionarySetValue(Handle, key, value);

        public void Set(IntPtr key, string value)
        {
            var s = CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(value + "\0"), Utf8);
            owned.Add(s);
            CFDictionarySetValue(Handle, key, s);
        }

        public void Dispose()
        {
            foreach (var o in owned)
            {
                CFRelease(o);
            }

            CFRelease(Handle);
        }
    }

    private sealed class CfData : IDisposable
    {
        private CfData(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }

        public static CfData From(byte[] bytes) => new(CFDataCreate(IntPtr.Zero, bytes, bytes.Length));

        public void Dispose() => CFRelease(Handle);
    }

    /// <summary>The framework's exported constants (CFStringRef keys and values, and the dictionary callbacks).</summary>
    private sealed class Symbols
    {
        public Symbols()
        {
            var security = NativeLibrary.Load(SecurityLib);
            var cf = NativeLibrary.Load(CoreFoundationLib);
            IntPtr Read(IntPtr lib, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, name));
            Class = Read(security, "kSecClass");
            ClassGenericPassword = Read(security, "kSecClassGenericPassword");
            Service = Read(security, "kSecAttrService");
            Account = Read(security, "kSecAttrAccount");
            Label = Read(security, "kSecAttrLabel");
            Description = Read(security, "kSecAttrDescription");
            ValueData = Read(security, "kSecValueData");
            ReturnData = Read(security, "kSecReturnData");
            ReturnAttributes = Read(security, "kSecReturnAttributes");
            MatchLimit = Read(security, "kSecMatchLimit");
            MatchLimitOne = Read(security, "kSecMatchLimitOne");
            UseAuthenticationUI = Read(security, "kSecUseAuthenticationUI");
            UseAuthenticationUIFail = Read(security, "kSecUseAuthenticationUIFail");
            True = Read(cf, "kCFBooleanTrue");
            KeyCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks");
            ValueCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks");
        }

        public IntPtr Class { get; }
        public IntPtr ClassGenericPassword { get; }
        public IntPtr Service { get; }
        public IntPtr Account { get; }
        public IntPtr Label { get; }
        public IntPtr Description { get; }
        public IntPtr ValueData { get; }
        public IntPtr ReturnData { get; }
        public IntPtr ReturnAttributes { get; }
        public IntPtr MatchLimit { get; }
        public IntPtr MatchLimitOne { get; }
        public IntPtr UseAuthenticationUI { get; }
        public IntPtr UseAuthenticationUIFail { get; }
        public IntPtr True { get; }
        public IntPtr KeyCallBacks { get; }
        public IntPtr ValueCallBacks { get; }
    }

#pragma warning disable SYSLIB1054 // DllImport keeps the bindings free of unsafe code and source generation
    [DllImport(SecurityLib)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
    [DllImport(SecurityLib)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(SecurityLib)] private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);
    [DllImport(SecurityLib)] private static extern int SecItemDelete(IntPtr query);
    [DllImport(SecurityLib)] private static extern int SecKeychainSetUserInteractionAllowed([MarshalAs(UnmanagedType.U1)] bool state);
    [DllImport(SecurityLib)] private static extern IntPtr SecCopyErrorMessageString(int status, IntPtr reserved);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);
    [DllImport(CoreFoundationLib)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] cString, uint encoding);
    [DllImport(CoreFoundationLib)] private static extern nint CFStringGetLength(IntPtr s);
    [DllImport(CoreFoundationLib)] private static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);
    [DllImport(CoreFoundationLib)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool CFStringGetCString(IntPtr s, byte[] buffer, nint size, uint encoding);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundationLib)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundationLib)] private static extern void CFRelease(IntPtr cf);
#pragma warning restore SYSLIB1054
}
