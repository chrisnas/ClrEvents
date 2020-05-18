using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SampledObjectAllocationProfiler
{
    public class MethodStore : IDisposable
    {
        // JITed methods information (start address + size + signature)
        private readonly List<MethodInfo> _methods;
        
        // addresses from callstacks already matching (address -> full name)
        private readonly Dictionary<ulong, string> _cache;

        // for native methods, rely on dbghelp API
        // so the process handle is required
        private IntPtr _hProcess;
        private Process _process;
        private readonly int _pid;

        public MethodStore(int pid)
        {
            // it may be possible to open the process
            // in that case, _hProcess = IntPtr.Zero
            _pid = pid;
            _hProcess = BindToProcess(pid);

            _methods = new List<MethodInfo>(1024);
            _cache = new Dictionary<ulong, string>();
        }

        private IntPtr BindToProcess(int pid)
        {
            try
            {
                _process = Process.GetProcessById(pid);

                if (!SymInitialize(_process.Handle))
                    return IntPtr.Zero;

                return _process.Handle;
            }
            catch (Exception x)
            {
                Console.WriteLine($"Error while binding pid #{pid} to DbgHelp:");
                Console.WriteLine(x.Message);
                return IntPtr.Zero;
            }
        }

        private bool SymInitialize(IntPtr hProcess)
        {
            // read https://docs.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-symsetoptions for more details
            // maybe SYMOPT_NO_PROMPTS and SYMOPT_FAIL_CRITICAL_ERRORS could be used
            NativeDbgHelp.SymSetOptions(
                NativeDbgHelp.SYMOPT_DEFERRED_LOADS |   // performance optimization
                NativeDbgHelp.SYMOPT_UNDNAME            // C++ names are not mangled
                );

            // https://docs.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-syminitialize
            // search path for symbols:
            //   - The current working directory of the application
            //   - The _NT_SYMBOL_PATH environment variable
            //   - The _NT_ALTERNATE_SYMBOL_PATH environment variable
            //
            // passing false as last parameter means that we will need to call SymLoadModule64 
            // each time a module is loaded in the process
            return NativeDbgHelp.SymInitialize(hProcess, null, false);
        }

        public MethodInfo Add(ulong address, int size, string namespaceAndTypeName, string name, string signature)
        {
            var method = new MethodInfo(address, size, namespaceAndTypeName, name, signature);
            _methods.Add(method);
            return method;
        }

        public string GetFullName(ulong address)
        {
            if (_cache.TryGetValue(address, out var fullName))
                return fullName;
            
            // look for managed methods
            for (int i = 0; i < _methods.Count; i++)
            {
                var method = _methods[i];
                
                if ((address >= method.StartAddress) && (address < method.StartAddress + (ulong)method.Size))
                {
                    fullName = method.FullName;
                    _cache[address] = fullName;
                    return fullName;
                }
            }

            // look for native methods
            fullName = GetNativeMethodName(address);
            _cache[address] = fullName;

            return fullName;
        }

        private string GetNativeMethodName(ulong address)
        {
            var symbol = new NativeDbgHelp.SYMBOL_INFO();
            symbol.MaxNameLen = 1024;
            symbol.SizeOfStruct = (uint)Marshal.SizeOf(symbol) - 1024;   // char buffer is not counted
            // the ANSI version of SymFromAddr is called so each character is 1 byte long

            if (NativeDbgHelp.SymFromAddr(_hProcess, address, out var displacement, ref symbol))
            {
                var buffer = new StringBuilder(symbol.Name.Length);

                // remove weird "$##" at the end of some symbols
                var pos = symbol.Name.LastIndexOf("$##");
                if (pos == -1)
                    buffer.Append(symbol.Name);
                else
                    buffer.Append(symbol.Name, 0, pos);

                // add offset if any
                if (displacement != 0)
                    buffer.Append($"+0x{displacement}");

                return buffer.ToString();
            }

            // default value is the just the address in HEX
#if DEBUG
            return ($"0x{address:x}  (SymFromAddr failed with 0x{Marshal.GetLastWin32Error():x})");
#else
            return $"0x{address:x}";
#endif

        }

        const int ERROR_SUCCESS = 0;
        public void AddModule(string filename, ulong baseOfDll, int sizeOfDll)
        {
            var baseAddress = NativeDbgHelp.SymLoadModule64(_hProcess, IntPtr.Zero, filename, null, baseOfDll, (uint)sizeOfDll);
            if (baseAddress == 0)
            {
                // should work if the same module is added more than once
                if (Marshal.GetLastWin32Error() == ERROR_SUCCESS) return;

                Console.WriteLine($"SymLoadModule64 failed for {filename}");
            }
        }

        public void Dispose()
        {
            if (_hProcess == IntPtr.Zero)
                return;
            _hProcess = IntPtr.Zero;

            _process.Dispose();
        }
    }

    internal static class NativeDbgHelp
    {
        // from C:\Program Files (x86)\Windows Kits\10\Debuggers\inc\dbghelp.h
        public const uint SYMOPT_UNDNAME = 0x00000002;
        public const uint SYMOPT_DEFERRED_LOADS = 0x00000004;

        [StructLayout(LayoutKind.Sequential)]
        public struct SYMBOL_INFO
        {
            public uint SizeOfStruct;
            public uint TypeIndex;      // Type Index of symbol
            private ulong Reserved1;
            private ulong Reserved2;
            public uint Index;
            public uint Size;
            public ulong ModBase;       // Base Address of module containing this symbol
            public uint Flags;
            public ulong Value;         // Value of symbol, ValuePresent should be 1
            public ulong Address;       // Address of symbol including base address of module
            public uint Register;       // register holding value or pointer to value
            public uint Scope;          // scope of the symbol
            public uint Tag;            // pdb classification
            public uint NameLen;        // Actual length of name
            public uint MaxNameLen;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string Name;
        }

        [DllImport("dbghelp.dll", SetLastError = true)]
        public static extern bool SymInitialize(IntPtr hProcess, string userSearchPath, bool invadeProcess);

        [DllImport("dbghelp.dll", SetLastError = true)]
        public static extern uint SymSetOptions(uint symOptions);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern ulong SymLoadModule64(IntPtr hProcess, IntPtr hFile, string imageName, string moduleName, ulong baseOfDll, uint sizeOfDll);

        // use ANSI version to ensure the right size of the structure 
        // read https://docs.microsoft.com/en-us/windows/win32/api/dbghelp/ns-dbghelp-symbol_info
        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool SymFromAddr(IntPtr hProcess, ulong address, out ulong displacement, ref SYMBOL_INFO symbol);

        [DllImport("dbghelp.dll", SetLastError = true)]
        public static extern bool SymCleanup(IntPtr hProcess);
    }
}
