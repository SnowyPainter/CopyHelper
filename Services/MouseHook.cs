using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopyHelper.Services
{
    public sealed class MouseHook : IDisposable
    {
        private const int WhMouseLl = 14;
        private const int WmLbuttonDown = 0x0201;

        private readonly LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event EventHandler? LeftButtonDown;
        public bool IsInstalled => _hookId != IntPtr.Zero;

        public MouseHook()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelMouseProc proc)
        {
            using Process currentProcess = Process.GetCurrentProcess();
            using ProcessModule currentModule = currentProcess.MainModule!;
            return SetWindowsHookEx(WhMouseLl, proc, GetModuleHandle(currentModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WmLbuttonDown)
            {
                LeftButtonDown?.Invoke(this, EventArgs.Empty);
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
