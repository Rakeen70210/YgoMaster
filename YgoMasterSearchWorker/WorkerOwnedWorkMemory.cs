using System;
using System.Runtime.InteropServices;

namespace YgoMaster
{
    /// <summary>
    /// Fresh worker-owned native buffer. DLL_SetWorkMemory is invoked only after isolated
    /// duel.dll load and only with a known-safe signature; otherwise returns no-go reason.
    /// </summary>
    public sealed class WorkerOwnedWorkMemory : IDisposable
    {
        IntPtr _ptr;
        int _size;
        bool _disposed;

        public IntPtr Pointer { get { return _ptr; } }
        public int Size { get { return _size; } }
        public bool IsWorkerOwned { get { return true; } }

        public WorkerOwnedWorkMemory(int sizeBytes)
        {
            if (sizeBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("sizeBytes");
            }
            _size = sizeBytes;
            // Worker-owned fresh local allocation (AllocateWorkMemory / WorkerOwned).
            _ptr = Marshal.AllocHGlobal(sizeBytes);
            for (int i = 0; i < sizeBytes; i++)
            {
                Marshal.WriteByte(_ptr, i, 0);
            }
        }

        /// <summary>
        /// Dynamically resolve DLL_SetWorkMemory from an already-loaded worker module handle.
        /// Does not invent required engine work size; if size/signature not established, skip invoke.
        /// </summary>
        public bool TryApplySetWorkMemoryIfSafe(IntPtr moduleHandle, out string reason)
        {
            reason = null;
            if (moduleHandle == IntPtr.Zero)
            {
                reason = "no_loaded_module_handle";
                return false;
            }
            IntPtr proc = NativeLib.GetExport(moduleHandle, "DLL_SetWorkMemory");
            if (proc == IntPtr.Zero)
            {
                reason = "DLL_SetWorkMemory_export_missing";
                return false;
            }
            // Required native work size is not established from PE alone for Master Duel duel.dll.
            // Invoking with an arbitrary 4096 buffer would be unsafe speculation.
            reason = "required_work_memory_size_not_established; refusing speculative DLL_SetWorkMemory invoke";
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_ptr != IntPtr.Zero)
            {
                // cleanup / ReleaseWorkMemory for worker-owned buffer
                Marshal.FreeHGlobal(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }
}

