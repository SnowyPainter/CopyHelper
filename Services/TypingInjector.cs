using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CopyHelper.Services
{
    public sealed class TypingInjector
    {
        private const int InputKeyboard = 1;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint KeyeventfUnicode = 0x0004;

        public Task TypeTextAsync(string text, int delayMs, CancellationToken cancellationToken, nint targetWindow)
        {
            return Task.Run(async () =>
            {
                SetForegroundWindow(targetWindow);
                await Task.Delay(50);
                foreach (char ch in text)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SendChar(ch);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                }
            }, cancellationToken);
        }

        private static void SendChar(char ch)
        {
            if (ch == '\r' || ch == '\n')
            {
                SendVirtualKey(0x0D);
                return;
            }

            if (ch == '\t')
            {
                SendVirtualKey(0x09);
                return;
            }

            INPUT[] inputs = new INPUT[2];
            inputs[0] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KeyeventfUnicode,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };
            inputs[1] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KeyeventfUnicode | KeyeventfKeyup,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                int err = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"SendInput failed. err={err}");
            }

        }

        private static void SendVirtualKey(ushort key)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = key,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };
            inputs[1] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = key,
                        dwFlags = KeyeventfKeyup,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
