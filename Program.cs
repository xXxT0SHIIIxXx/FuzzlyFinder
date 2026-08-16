using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ContentAlignment = System.Drawing.ContentAlignment;

static class Program
{
    [STAThread]
    static void Main()
    {
        using Mutex mutex = new Mutex
        (
            true,
            "FuzzlyFinder_SingleInstance",
            out bool createdNew
        );

        if (!createdNew)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}

class TrayApplicationContext : ApplicationContext
{

    HotkeyWindow hiddenWindow;
    private NotifyIcon trayIcon;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    Icon fuzzlyIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "fuzzlyFinder.ico"));
    public TrayApplicationContext()
    {

        trayIcon = new NotifyIcon()
        {
            Icon = fuzzlyIcon,
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
            Text = "Fuzzly Finder"
        };
        hiddenWindow = new HotkeyWindow();
        RegisterHotKey(hiddenWindow.Handle, 1, MOD_CONTROL | MOD_SHIFT, (uint)Keys.F);
        RegisterHotKey(hiddenWindow.Handle, 2, MOD_CONTROL | MOD_SHIFT, (uint)Keys.X);
        hiddenWindow.onShortCutted += ShortCutWindowOpen;
        hiddenWindow.onExited += Exit;
        trayIcon.DoubleClick += OpenWindow;
        trayIcon.ContextMenuStrip.Items.Add("Open", null, OpenWindow);
        trayIcon.ContextMenuStrip.Items.Add("Exit", null, Exit);
        CreateWindow();
    }

    TextBox searchBox;
    ListBox listBox;
    Label statusLabel;
    Form searchWindow;
    void OpenWindow(object sender, EventArgs e)
    {
        if(searchWindow == null)
        {
            CreateWindow();
            FocusSearchWindow();
        }
        else
        {
            FocusSearchWindow();
            return;
        }
    }

    void ShortCutWindowOpen(object sender, EventArgs e)
    {
        if (searchWindow == null)
        {
            CreateWindow();
            FocusSearchWindow();
        }
        else
        {
            searchWindow.Close();
            return;
        }
    }

    void CreateWindow()
    {
        //INIT
        searchWindow = new Form();
        searchWindow.Text = "Fuzzly Finder";
        searchWindow.Icon = fuzzlyIcon;
        searchWindow.Size = new Size(500, 800);
        Panel mainPanel = new Panel();
        searchBox = new TextBox();
        listBox = new ListBox();
        statusLabel = new Label();
        statusLabel.Text = "Idle";
        searchBox.KeyDown += onSubmit;
        searchWindow.FormClosing += searchFormClosing;
        //Styling

        TableLayoutPanel searchRow = new TableLayoutPanel();

        searchRow.Dock = DockStyle.Top;
        searchRow.Height = 30;
        searchRow.ColumnCount = 2;
        searchRow.RowCount = 1;

        searchRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 60F)
        );

        searchRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 30F)
        );

        searchRow.Controls.Add(searchBox, 0, 0);
        searchRow.Controls.Add(statusLabel, 1, 0);
        searchBox.Dock = DockStyle.Fill;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        searchWindow.Controls.Add(mainPanel);

        mainPanel.Controls.Add(listBox);
        mainPanel.Controls.Add(searchRow);

        mainPanel.Dock = DockStyle.Fill;
        mainPanel.Padding = new Padding(10);

        listBox.Dock = DockStyle.Fill;
        listBox.MouseClick += ItemClick;
        listBox.MouseDoubleClick += ItemDoubleClick;
        ApplyTheme(searchWindow);
        searchWindow.Show();
    }

    bool IsDarkMode()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        );

        object? value = key?.GetValue("AppsUseLightTheme");

        return value is int intValue && intValue == 0;
    }

    void ApplyTheme(Form form)
    {
        bool dark = IsDarkMode();

        Color background = dark
            ? Color.FromArgb(30, 30, 30)
            : SystemColors.Window;

        Color foreground = dark
            ? Color.White
            : SystemColors.WindowText;

        form.BackColor = background;
        form.ForeColor = foreground;

        ApplyThemeToControls(form.Controls, dark);
    }

    void ApplyThemeToControls(Control.ControlCollection controls, bool dark)
    {
        foreach (Control control in controls)
        {
            control.BackColor = dark
                ? Color.FromArgb(30, 30, 30)
                : SystemColors.Window;

            control.ForeColor = dark
                ? Color.White
                : SystemColors.WindowText;

            ApplyThemeToControls(control.Controls, dark);
        }
    }

    void ItemClick(object sender, MouseEventArgs e)
    {

        int index = listBox.IndexFromPoint(e.Location);

        if (index != ListBox.NoMatches)
        {
            string path = listBox.Items[index].ToString();

            Clipboard.SetText(path);
            statusLabel.Text = "Copied Path to clipboard";
        }
    }

    void ItemDoubleClick(object sender, MouseEventArgs e)
    {
        int index = listBox.IndexFromPoint(e.Location);
        if (index != ListBox.NoMatches)
        {
            string path = listBox.Items[index].ToString();
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
    }

    void FocusSearchWindow()
    {
        searchWindow.Show();
        searchWindow.WindowState = FormWindowState.Normal;
        searchWindow.TopMost = true;
        searchWindow.BringToFront();
        searchWindow.Activate();
        searchBox.Focus();
    }

    private void searchFormClosing(object? sender, FormClosingEventArgs e)
    {
        searchWindow = null;
    }

    private void onSubmit(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !running)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            SearchTask();
        }
    }
    bool running = false;

    private async void SearchTask()
    {
        string query = searchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
            return;
        running = true;
        listBox.Items.Clear();
        IProgress<string> progress = new Progress<string>(result =>
        {
            listBox.Items.Add(result);
        });
        IProgress<string> prog2 = new Progress<string>(result =>
        {
            statusLabel.Text = result;
        });


        try
        {
            List<string> results = await Task.Run(() =>
            {
                return SearchSubmitted(query, progress, prog2);
            });
        }
        finally
        {
            prog2.Report("Finished || Idle");
            running = false;
        }
    }

    List<string> Results;
    private List<string> SearchSubmitted(string searchQuery, IProgress<string> progress, IProgress<string> prog2)
    {
        Results = new List<string>();
        DriveInfo[] drives = DriveInfo.GetDrives();

        foreach (DriveInfo drive in drives)
        {
            if (!drive.IsReady)
                continue;
            prog2.Report("Running | " + drive.Name);
            SearchDirectory(drive.Name, searchQuery, progress, prog2);
        }
        return Results;
    }


    void SearchDirectory(string directory, string searchMatch, IProgress<string> progress, IProgress<string> prog2)
    {
        try
        {
            DirectoryInfo dir = new DirectoryInfo(directory);

            foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
            {
                string name = entry.Name;

                if (FuzzyMatch(name, searchMatch))
                {
                    progress.Report(entry.FullName);
                }

                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    SearchDirectory(
                        entry.FullName,
                        searchMatch,
                        progress,
                        prog2
                    );
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Can't access this folder.
        }
        catch (IOException)
        {
            // Drive/device/filesystem problem.
        }
    }

    bool FuzzyMatch(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        text = text.ToLowerInvariant();
        query = query.ToLowerInvariant();

        int queryIndex = 0;

        foreach (char c in text)
        {
            if (c == query[queryIndex])
            {
                queryIndex++;

                if (queryIndex == query.Length)
                    return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(
    IntPtr hWnd,
    int id,
    uint fsModifiers,
    uint vk
    );

    void Exit(object sender, EventArgs e)
    {
        trayIcon.Visible = false;
        Application.Exit();
    }


    class HotkeyWindow : Form
    {
        public const int WM_HOTKEY = 0x0312;
        public EventHandler? onShortCutted;
        public EventHandler? onExited;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();

                if(hotkeyId == 1)
                {
                    onShortCutted?.Invoke(this, EventArgs.Empty);
                }
                else if(hotkeyId == 2)
                {
                    onExited?.Invoke(this, EventArgs.Empty);
                }
            }

            base.WndProc(ref m);
        }
    }
}