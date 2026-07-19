using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.NotepadSearch
{
    public class NotepadSearch : IPlugin
    {
        private PluginInitContext _context;
        private string IconPath { get; set; }
        
        public void Init(PluginInitContext context)
        {
            _context = context;

            string notepadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            
            if (File.Exists(notepadPath))
            {
                string iconDirectory = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images");
                if (!Directory.Exists(iconDirectory))
                {
                    Directory.CreateDirectory(iconDirectory);
                }
                
                string iconPath = Path.Combine(iconDirectory, "notepad.png");
                
                if (!File.Exists(iconPath))
                {
                    try
                    {
                        using (Icon icon = Icon.ExtractAssociatedIcon(notepadPath))
                        {
                            using (Bitmap bitmap = icon.ToBitmap())
                            {
                                bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                        _context.API.LogInfo("NotepadSearch", $"Extracted Notepad icon to {iconPath}", "");
                    }
                    catch (Exception ex)
                    {
                        _context.API.LogException("NotepadSearch", "Error extracting Notepad icon", ex, "");
                        iconPath = "Images/icon.png"; // Fallback to default icon
                    }
                }
                
                IconPath = iconPath;
            }
        }

        public List<Result> Query(Query query)
        {
            NotepadReader reader = new NotepadReader();
            string jsonContent = reader.GetAllNotepadContent();
            List<Result> results = new List<Result>();
            
            _context.API.LogInfo("NotepadSearch", $"Raw content length: {jsonContent?.Length ?? 0}", "");
            
            try
            {
                var notepadWindows = System.Text.Json.JsonSerializer.Deserialize<List<NotepadWindow>>(jsonContent);
                
                _context.API.LogInfo("NotepadSearch", $"Deserialized {notepadWindows?.Count ?? 0} Notepad windows", "");
                
                if (notepadWindows.Count > 1 && 
                    notepadWindows[0].contentLength > 0 && 
                    notepadWindows[0].contentLength > notepadWindows.Sum(w => w.contentLength) * 0.8)
                {
                    _context.API.LogInfo("NotepadSearch", "Skipping first result as it appears to contain the full collection", "");
                    notepadWindows.RemoveAt(0);
                }
                
                if (string.IsNullOrEmpty(query.Search))
                {
                    foreach (var window in notepadWindows)
                    {
                        if (string.IsNullOrEmpty(window.title))
                            continue;
                            
                        results.Add(new Result
                        {
                            Title = window.title,
                            SubTitle = $"Notepad window (PID: {window.processId}), {window.contentLength} characters",
                            IcoPath = IconPath,
                            CopyText = window.content,
                            Action = _ =>
                            {
                                try
                                {
                                    FocusNotepadWindow(window.title, window.processId);
                                }
                                catch (Exception ex)
                                {
                                    _context.API.LogException("NotepadSearch", "Error focusing window", ex, "");
                                }
                                return true;
                            }
                        });
                    }
                }
                else
                {
                    _context.API.LogInfo("NotepadSearch", $"Searching for '{query.Search}' in {notepadWindows.Count} windows", "");
                    
                    foreach (var window in notepadWindows)
                    {
                        if ((string.IsNullOrEmpty(window.content) && string.IsNullOrEmpty(window.title)) || 
                            window.title == null)
                            continue;
                            
                        _context.API.LogInfo("NotepadSearch", $"Checking window '{window.title}' (PID: {window.processId})", "");
                            
                        bool foundInContent = !string.IsNullOrEmpty(window.content) && 
                                            window.content.Contains(query.Search, StringComparison.OrdinalIgnoreCase);
                        bool foundInTitle = window.title.Contains(query.Search, StringComparison.OrdinalIgnoreCase);
                        
                        if (foundInContent || foundInTitle)
                        {
                            string matchType = foundInContent && foundInTitle ? "title and content" : 
                                            foundInContent ? "content" : "title";
                            
                            _context.API.LogInfo("NotepadSearch", $"Match found in {matchType} of window '{window.title}'", "");
                            
                            string subtitle;
                            if (foundInContent && !string.IsNullOrEmpty(window.content))
                            {
                                int index = window.content.IndexOf(query.Search, StringComparison.OrdinalIgnoreCase);
                                int startIndex = Math.Max(0, index - 30);
                                int length = Math.Min(window.content.Length - startIndex, 60 + query.Search.Length);
                                string context = window.content.Substring(startIndex, length);
                                
                                context = context.Replace("\r\n", " ").Replace("\n", " ");
                                
                                if (startIndex > 0) context = "..." + context;
                                if (startIndex + length < window.content.Length) context += "...";
                                
                                subtitle = $"Found in {matchType}: {context}";
                            }
                            else
                            {
                                subtitle = $"Found in {matchType}";
                            }
                            
                            results.Add(new Result
                            {
                                Title = window.title,
                                SubTitle = subtitle,
                                IcoPath = IconPath,
                                CopyText = window.content,
                                Action = _ =>
                                {
                                    try
                                    {
                                        FocusNotepadWindow(window.title, window.processId);
                                    }
                                    catch (Exception ex)
                                    {
                                        _context.API.LogException("NotepadSearch", "Error focusing window", ex, "");
                                    }
                                    return true;
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _context.API.LogException("NotepadSearch", "Error parsing Notepad content", ex, "");
                
                results.Add(new Result
                {
                    Title = "Error parsing Notepad content",
                    SubTitle = ex.Message
                });
            }
            
            _context.API.LogInfo("NotepadSearch", $"Returning {results.Count} results", "");
            
            return results;
        }

        private class NotepadWindow
        {
            public string content { get; set; }
            public int contentLength { get; set; }
            public int processId { get; set; }
            public string title { get; set; }
        }

        private void FocusNotepadWindow(string title, int processId)
        {
            _context.API.LogInfo("NotepadSearch", $"Attempting to focus window with title '{title}' and PID {processId}", "");
            
            EnumWindows((hWnd, lParam) =>
            {
                int pid;
                GetWindowThreadProcessId(hWnd, out pid);
                
                if (pid == processId)
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string windowTitle = sb.ToString();
                    
                    _context.API.LogInfo("NotepadSearch", $"Found window: '{windowTitle}'", "");
                    
                    if (windowTitle.Contains(title))
                    {
                        _context.API.LogInfo("NotepadSearch", $"Found matching window, focusing: '{windowTitle}'", "");
                        
                        if (IsIconic(hWnd))
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                        }
                        
                        SetForegroundWindow(hWnd);
                        return false; 
                    }
                }
                
                return true; 
            }, 0);
        }

        // API Win32
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, int lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

        private const int SW_RESTORE = 9;
    }
}