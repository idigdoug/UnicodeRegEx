namespace UnicodeRegEx.Tests
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Assembly-wide setup. The wrapper P/Invokes into the native
    /// UnicodeRegEx_&lt;arch&gt;.dll, which is built next to this test assembly
    /// (SolutionDir\out\&lt;Config&gt;). The test host may run with a different
    /// working directory, so add the assembly's own directory to the native DLL
    /// search path to guarantee the native library resolves.
    /// </summary>
    [TestClass]
    public static class AssemblyInit
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            var dir = Path.GetDirectoryName(typeof(AssemblyInit).Assembly.Location);
            if (!string.IsNullOrEmpty(dir))
            {
                if (!SetDllDirectory(dir))
                {
                    throw new InvalidOperationException(
                        $"SetDllDirectory failed for '{dir}' (error {Marshal.GetLastWin32Error()}).");
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);
    }
}
