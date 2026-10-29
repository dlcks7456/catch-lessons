using System.Runtime.InteropServices;
using System.Text;

namespace CatchLessons;

internal static class MacKeychain
{
    private const string Security="/System/Library/Frameworks/Security.framework/Security";
    // Preserve access to keys saved before the app was renamed.
    private static readonly byte[] Service=Encoding.UTF8.GetBytes("CaptionGlass.OpenAI");
    private static byte[] Account=>Encoding.UTF8.GetBytes(Environment.UserName);
    public static string Read()
    {
        var account=Account;int status=SecKeychainFindGenericPassword(IntPtr.Zero,(uint)Service.Length,Service,(uint)account.Length,account,out var length,out var data,out var item);
        if(status==-25300)return "";if(status!=0)throw new IOException("macOS Keychain 읽기 실패: "+status);
        try {var bytes=new byte[length];Marshal.Copy(data,bytes,0,bytes.Length);return Encoding.UTF8.GetString(bytes);}
        finally {SecKeychainItemFreeContent(IntPtr.Zero,data);if(item!=IntPtr.Zero)CFRelease(item);}
    }
    public static void Write(string value)
    {
        var account=Account;var bytes=Encoding.UTF8.GetBytes(value.Trim());
        int found=SecKeychainFindGenericPassword(IntPtr.Zero,(uint)Service.Length,Service,(uint)account.Length,account,out _,out var old,out var item);
        if(found==0)SecKeychainItemFreeContent(IntPtr.Zero,old);
        try
        {
            int status=found==0?(bytes.Length==0?SecKeychainItemDelete(item):SecKeychainItemModifyAttributesAndData(item,IntPtr.Zero,(uint)bytes.Length,bytes)):
                bytes.Length==0?0:SecKeychainAddGenericPassword(IntPtr.Zero,(uint)Service.Length,Service,(uint)account.Length,account,(uint)bytes.Length,bytes,out item);
            if(status!=0)throw new IOException("macOS Keychain 저장 실패: "+status);
        }
        finally{if(item!=IntPtr.Zero)CFRelease(item);}
    }
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain,uint serviceLength,byte[] service,uint accountLength,byte[] account,out uint passwordLength,out IntPtr password,out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain,uint serviceLength,byte[] service,uint accountLength,byte[] account,uint passwordLength,byte[] password,out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item,IntPtr attributes,uint length,byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes,IntPtr data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
}
