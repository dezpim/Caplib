using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Caplib
{
    internal enum MouseClickType
    {
        Left,
        Right,
        Middle
    }

    internal sealed class MouseClickEventArgs : EventArgs
    {
        public Point Location { get; private set; }
        public MouseClickType Button { get; private set; }
        public DateTime Timestamp { get; private set; }

        public MouseClickEventArgs(Point location, MouseClickType button, DateTime timestamp)
        {
            Location = location;
            Button = button;
            Timestamp = timestamp;
        }
    }

    internal sealed class ClickHook : IDisposable
    {
        private readonly NativeMethods.LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        public event EventHandler<MouseClickEventArgs> MouseClicked;

        public ClickHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero)
            {
                return;
            }

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, moduleHandle, 0);
                if (_hookId == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "전역 마우스 훅 등록에 실패했습니다.");
                }
            }
        }

        public void Uninstall()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                MouseClickType? clickType = null;

                if (msg == NativeMethods.WM_LBUTTONDOWN)
                {
                    clickType = MouseClickType.Left;
                }
                else if (msg == NativeMethods.WM_RBUTTONDOWN)
                {
                    clickType = MouseClickType.Right;
                }
                else if (msg == NativeMethods.WM_MBUTTONDOWN)
                {
                    clickType = MouseClickType.Middle;
                }

                if (clickType.HasValue)
                {
                    NativeMethods.MSLLHOOKSTRUCT hookStruct =
                        (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));

                    EventHandler<MouseClickEventArgs> handler = MouseClicked;
                    if (handler != null)
                    {
                        handler(this, new MouseClickEventArgs(
                            new Point(hookStruct.pt.x, hookStruct.pt.y),
                            clickType.Value,
                            DateTime.Now));
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Uninstall();
            _disposed = true;
        }
    }
}
