namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// A <see cref="SafeHandle"/> for the search handle returned by <c>FindFirstFileEx</c>.
    /// The handle is released with <c>FindClose</c> (not <c>CloseHandle</c>), and the invalid
    /// value is <c>INVALID_HANDLE_VALUE</c> (-1), so this derives from
    /// <see cref="SafeHandleMinusOneIsInvalid"/> rather than <see cref="SafeHandleZeroOrMinusOneIsInvalid"/>.
    /// </summary>
    /// <remarks>
    /// The interop declaration returns this type directly, so the runtime marshaller assigns the raw
    /// handle into a fully constructed <see cref="SafeFindHandle"/>. That closes the window between the
    /// native call returning and a managed wrapper being built, guaranteeing <c>FindClose</c> runs even
    /// if an exception is thrown, and preventing a handle leak.
    /// </remarks>
    internal sealed class SafeFindHandle : SafeHandleMinusOneIsInvalid
    {
        // Required by the marshaller: it needs a parameterless constructor to allocate the instance
        // before populating the handle. Kept private so only interop creates one.
        private SafeFindHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.FindClose(this.handle);
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FindClose(IntPtr hFindFile);
        }
    }
}
