using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Linq;

public class NotepadReader
{
    #region Win32 API
    [DllImport("user32.dll")]
    static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, [Out] StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Constants
    const uint WM_GETTEXT = 0x000D;
    const uint WM_GETTEXTLENGTH = 0x000E;
    #endregion

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // Class to store Notepad window information
    public class NotepadWindow
    {
        public IntPtr WindowHandle { get; set; }
        public uint ProcessId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int ContentLength => Content?.Length ?? 0;
    }

    public List<NotepadWindow> ScanAllNotepadWindows()
    {
        List<NotepadWindow> notepadWindows = new List<NotepadWindow>();

        // Find all Notepad windows
        EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
        {
            if (IsWindowVisible(hWnd))
            {
                StringBuilder className = new StringBuilder(256);
                GetClassName(hWnd, className, className.Capacity);

                if (className.ToString() == "Notepad")
                {
                    StringBuilder windowTitle = new StringBuilder(256);
                    GetWindowText(hWnd, windowTitle, windowTitle.Capacity);

                    uint processId = 0;
                    GetWindowThreadProcessId(hWnd, out processId);

                    notepadWindows.Add(new NotepadWindow
                    {
                        WindowHandle = hWnd,
                        ProcessId = processId,
                        Title = windowTitle.ToString()
                    });
                }
            }
            return true;
        }, IntPtr.Zero);

        // Read each window's content
        var distinctWindows = new List<NotepadWindow>();
        var seenEditControls = new HashSet<IntPtr>();

        foreach (var notepad in notepadWindows)
        {
            IntPtr editControl;
            notepad.Content = GetNotepadContent(notepad.WindowHandle, out editControl);

            // Skip windows with no text control, or whose control we've already read.
            if (editControl == IntPtr.Zero || !seenEditControls.Add(editControl))
            {
                continue;
            }

            distinctWindows.Add(notepad);
        }

        return distinctWindows;
    }

    // Get content from a Notepad window
    private string GetNotepadContent(IntPtr notepadWindow, out IntPtr editControl)
    {
        editControl = FindWindowEx(notepadWindow, IntPtr.Zero, "Edit", null);

        if (editControl == IntPtr.Zero)
        {
            editControl = FindWindowEx(notepadWindow, IntPtr.Zero, "RichEdit20W", null);
        }

        if (editControl == IntPtr.Zero)
        {
            editControl = FindDirectChildByClassName(notepadWindow, "NotepadTextBox");
        }

        if (editControl != IntPtr.Zero)
        {
            if (GetClassName(editControl) == "NotepadTextBox")
            {
                IntPtr richEdit = FindDirectChildByClassName(editControl, "RichEditD2DPT");
                if (richEdit != IntPtr.Zero)
                {
                    editControl = richEdit;
                }
            }

            IntPtr length = SendMessage(editControl, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);

            if (length.ToInt32() > 0)
            {
                int maxLength = Math.Min(length.ToInt32(), 10 * 1024 * 1024);

                StringBuilder sb = new StringBuilder(maxLength + 1);
                SendMessage(editControl, WM_GETTEXT, new IntPtr(sb.Capacity), sb);
                return sb.ToString();
            }
        }

        return string.Empty;
    }

    // Helper method to find a direct child window by class name
    private IntPtr FindDirectChildByClassName(IntPtr parentWindow, string className)
    {
        IntPtr childWindow = IntPtr.Zero;
        List<IntPtr> foundWindows = new List<IntPtr>();

        EnumChildWindows(parentWindow, delegate (IntPtr hWnd, IntPtr lParam)
        {
            StringBuilder sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, sbClass.Capacity);

            if (sbClass.ToString() == className)
            {
                foundWindows.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        if (foundWindows.Count > 0)
        {
            childWindow = foundWindows[0];
        }

        return childWindow;
    }

    // Helper for getting className as string
    private string GetClassName(IntPtr hWnd)
    {
        StringBuilder className = new StringBuilder(256);
        GetClassName(hWnd, className, className.Capacity);
        return className.ToString();
    }
}
