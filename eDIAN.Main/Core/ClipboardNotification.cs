namespace eDIAN.Main.Core
{
    using System;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using System.Windows.Interop;

    public static class ClipboardNotificationForWinforms
    {
        public static event EventHandler OnUpdateClipboard;

        private static NotificationForm _form = new NotificationForm();

        private class NotificationForm : Form
        {
            public const int WM_CLIPBOARDUPDATE = 0x031D;
            public static IntPtr HWND_MESSAGE = new IntPtr(-3);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

            public NotificationForm()
            {
                NotificationForm.SetParent(Handle, NotificationForm.HWND_MESSAGE);
                NotificationForm.AddClipboardFormatListener(Handle);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NotificationForm.WM_CLIPBOARDUPDATE)
                {
                    OnUpdateClipboard?.Invoke(null, EventArgs.Empty);
                }

                base.WndProc(ref m);
            }
        }
    }

    public static class ClipboardNotification
    {
        public static event EventHandler OnUpdateClipboard;

        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public static IntPtr HWND_MESSAGE = new IntPtr(-3);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        // 가비지 컬렉터(GC)로부터 보호하기 위해 static 참조로 유지
        private static HwndSource _hwndSource;

        static ClipboardNotification()
        {
            // 1. 메시지만 수신할 빈 WPF HwndSource 생성 (가로/세로 0)
            HwndSourceParameters parameters = new HwndSourceParameters("HookWindow")
            {
                WindowStyle = 0,
                Width = 0,
                Height = 0
            };

            _hwndSource = new HwndSource(parameters);

            // 2. 해당 프로세스를 메시지 전용(HWND_MESSAGE)으로 전환 (WinForms 시절과 동일 기법)
            SetParent(_hwndSource.Handle, HWND_MESSAGE);

            // 3. WPF HwndSource에 WndProc 메시지 후킹 핸들러 연결
            _hwndSource.AddHook(WndProc);

            // 4. 생성한 WPF 핸들을 클립보드 리스너로 등록
            AddClipboardFormatListener(_hwndSource.Handle);
        }

        // WinForms의 WndProc 오버라이드를 대체하는 핸들러
        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnUpdateClipboard?.Invoke(null, EventArgs.Empty);
                handled = true; // 메시지 처리 완료 명시
            }

            return IntPtr.Zero;
        }
    }
}