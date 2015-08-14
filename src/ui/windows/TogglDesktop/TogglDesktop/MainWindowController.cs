﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using TogglDesktop.WPF;

namespace TogglDesktop
{
public partial class MainWindowController : TogglForm
{
    private bool isResizing = false;

    private List<Icon> statusIcons = new List<Icon>();

    private LoginViewController loginViewController;
    private TimeEntryListViewController timeEntryListViewController;
    private WPF.TimeEntryEditViewController timeEntryEditViewController;
    private AboutWindowController aboutWindowController;
    private PreferencesWindow preferencesWindowController;
    private FeedbackWindowController feedbackWindowController;
    private IdleNotificationWindowController idleNotificationWindowController;

    private EditForm editForm;

    private bool isTracking = false;
    private Point defaultContentPosition =  new System.Drawing.Point(0, 0);
    private Point errorContentPosition = new System.Drawing.Point(0, 28);
    private bool remainOnTop = false;
    private bool topDisabled = false;

    private static MainWindowController instance;

    KeyboardHook startHook = new KeyboardHook();
    KeyboardHook showHook = new KeyboardHook();

    private Timer runScriptTimer;
    private bool manualMode;

    public MainWindowController()
    {
        InitializeComponent();

        instance = this;

        startHook.KeyPressed += this.hookStartKeyPressed;

        showHook.KeyPressed += this.hookShowKeyPressed;
    }

    void setGlobalShortCutKeys()
    {
        try
        {
            startHook.Clear();
            string startKey = Properties.Settings.Default.StartKey;
            if (!string.IsNullOrEmpty(startKey))
            {
                startHook.RegisterHotKey(
                    Properties.Settings.Default.StartModifiers,
                    (Keys)Enum.Parse(typeof(Keys), startKey));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not register start shortcut: ", e);
        }

        try
        {
            showHook.Clear();
            string showKey = Properties.Settings.Default.ShowKey;
            if (!string.IsNullOrEmpty(showKey))
            {
                showHook.RegisterHotKey(
                    Properties.Settings.Default.ShowModifiers,
                    (Keys)Enum.Parse(typeof(Keys), showKey));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not register show hotkey: ", e);
        }
    }

    void hookStartKeyPressed(object sender, KeyPressedEventArgs e)
    {
        if (isTracking)
        {
            Toggl.Stop();
        }
        else
        {
            if (this.manualMode)
            {
                var guid = Toggl.Start("", "0", 0, 0, "", "");
                Toggl.Edit(guid, false, Toggl.Duration);
            }
            else
            {
                Toggl.ContinueLatest();
            }
        }
    }

    void hookShowKeyPressed(object sender, KeyPressedEventArgs e)
    {
        if (Visible)
        {
            Hide();
            if (editForm.Visible)
            {
                editForm.ClosePopup();
            }
            feedbackWindowController.Close();
            aboutWindowController.Close();
            preferencesWindowController.Hide();
        }
        else
        {
            show();
        }
    }

    public void toggleMenu()
    {
        // this method is called from external code (magic?) to open the cogwheel-menu
        Point pt = new Point(Width - 80, 0);
        pt = PointToScreen(pt);
        trayIconMenu.Show(pt);
    }

    public static void DisableTop()
    {
        instance.topDisabled = true;
        instance.setWindowPos();
    }

    public static void EnableTop()
    {
        instance.topDisabled = false;
        instance.setWindowPos();
    }

    public void RemoveTrayIcon()
    {
        trayIcon.Visible = false;
    }

    private const int kTogglTray = 0;
    private const int kTogglTrayInactive = 1;
    private const int kToggl = 2;
    private const int kTogglInactive = 3;
    private const int kTogglOfflineActive = 4;
    private const int kTogglOfflineInactive = 5;

    private void loadStatusIcons()
    {
        if (statusIcons.Count > 0)
        {
            throw new InvalidOperationException("Status images already loaded");
        }
        statusIcons.Add(Properties.Resources.toggltray);
        statusIcons.Add(Properties.Resources.toggltray_inactive);
        statusIcons.Add(Properties.Resources.toggl);
        statusIcons.Add(Properties.Resources.toggl_inactive);
        statusIcons.Add(Properties.Resources.toggl_offline_active);
        statusIcons.Add(Properties.Resources.toggl_offline_inactive);
    }

    private void MainWindowController_Load(object sender, EventArgs e)
    {
        troubleBox.BackColor = Color.FromArgb(239, 226, 121);
        contentPanel.Location = defaultContentPosition;

        loadStatusIcons();

        Toggl.OnApp += OnApp;
        Toggl.OnError += OnError;
        Toggl.OnLogin += OnLogin;
        Toggl.OnTimeEntryList += OnTimeEntryList;
        Toggl.OnTimeEntryEditor += OnTimeEntryEditor;
        Toggl.OnOnlineState += OnOnlineState;
        Toggl.OnReminder += OnReminder;
        Toggl.OnURL += OnURL;
        Toggl.OnRunningTimerState += OnRunningTimerState;
        Toggl.OnStoppedTimerState += OnStoppedTimerState;
        Toggl.OnSettings += OnSettings;
        Toggl.OnIdleNotification += OnIdleNotification;

        loginViewController = new LoginViewController();
        timeEntryListViewController = new TimeEntryListViewController();
        timeEntryEditViewController = new WPF.TimeEntryEditViewController();

        aboutWindowController = new AboutWindowController();
        preferencesWindowController = new PreferencesWindow();
        feedbackWindowController = new FeedbackWindowController();
        idleNotificationWindowController = new IdleNotificationWindowController();

        initEditForm();
        timeEntryListViewController.SetEditPopup(timeEntryEditViewController);
        editForm.Owner = aboutWindowController.Owner = feedbackWindowController.Owner = this;

        var windowInteropHelper = new WindowInteropHelper(this.preferencesWindowController);
        windowInteropHelper.Owner = this.Handle;
        ElementHost.EnableModelessKeyboardInterop(this.preferencesWindowController);

        if (!Toggl.StartUI(TogglDesktop.Program.Version()))
        {
            try
            {
                DisableTop();
                MessageBox.Show("Missing callback. See the log file for details");
            } finally {
                EnableTop();
            }
            TogglDesktop.Program.Shutdown(1);
        }

        Utils.LoadWindowLocation(this, editForm);

        setCorrectMinimumSize();

        aboutWindowController.initAndCheck();

        runScriptTimer = new Timer();
        runScriptTimer.Interval = 1000;
        runScriptTimer.Tick += runScriptTimer_Tick;
        runScriptTimer.Start();
    }

    private void setCorrectMinimumSize()
    {
        Size minSize;
        if (contentPanel.Controls.Contains(loginViewController))
        {
            minSize = new Size(loginViewController.MinimumSize.Width, loginViewController.MinimumSize.Height + 40);
        }
        else
        {
            minSize = new Size(230, 86);
        }
        if(minSize != MinimumSize)
        {
            MinimumSize = minSize;
            updateResizeHandleBackground();
        }
    }

    void runScriptTimer_Tick(object sender, EventArgs e)
    {
        runScriptTimer.Stop();

        if (null == Toggl.ScriptPath)
        {
            return;
        }

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            if (!File.Exists(Toggl.ScriptPath))
            {
                Console.WriteLine("Script file does not exist: " + Toggl.ScriptPath);
                TogglDesktop.Program.Shutdown(0);
            }

            string script = File.ReadAllText(Toggl.ScriptPath);

            Int64 err = 0;
            string result = Toggl.RunScript(script, ref err);
            if (0 != err)
            {
                Console.WriteLine("Failed to run script, err = {0}", err);
            }
            Console.WriteLine(result);

            if (0 == err)
            {
                TogglDesktop.Program.Shutdown(0);
            }
        }, null);
    }

    void OnRunningTimerState(Toggl.TogglTimeEntryView te)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnRunningTimerState(te);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnRunningTimerState(te)));
            return;
        }

        isTracking = true;
        enableMenuItems();
        updateStatusIcons(true);

        string newText = "Toggl Desktop";
        if (te.Description.Length > 0) {
            runningToolStripMenuItem.Text = te.Description.Replace("&", "&&");
            newText = te.Description + " - Toggl Desktop";
        }
        else
        {
            runningToolStripMenuItem.Text = "Timer is tracking";
        }
        if (newText.Length > 63)
        {
            newText = newText.Substring(0, 60) + "...";
        }
        Text = newText;
        if (trayIcon != null)
        {
            trayIcon.Text = Text;
        }
        updateResizeHandleBackground();
    }

    void OnStoppedTimerState()
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnStoppedTimerState();
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnStoppedTimerState()));
            return;
        }

        isTracking = false;
        enableMenuItems();
        updateStatusIcons(true);

        runningToolStripMenuItem.Text = "Timer is not tracking";
        Text = "Toggl Desktop";
        trayIcon.Text = Text;
        updateResizeHandleBackground();
    }

    void OnSettings(bool open, Toggl.TogglSettingsView settings)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnSettings(open, settings);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnSettings(open, settings)));
            return;
        }

        remainOnTop = settings.OnTop;
        setWindowPos();
        timerIdleDetection.Enabled = settings.UseIdleDetection;
        setGlobalShortCutKeys();
    }

    private void updateStatusIcons(bool is_online)
    {
        if (0 == statusIcons.Count)
        {
            return;
        }

        Icon tray = null;
        Icon form = null;

        if (is_online)
        {
            if (TogglDesktop.Program.IsLoggedIn && isTracking)
            {
                tray = statusIcons[kTogglTray];
                form = statusIcons[kToggl];
            }
            else
            {
                tray = statusIcons[kTogglTrayInactive];
                form = statusIcons[kTogglInactive];
            }
        }
        else
        {
            if (TogglDesktop.Program.IsLoggedIn && isTracking)
            {
                tray = statusIcons[kTogglOfflineActive];
                form = statusIcons[kToggl];
            }
            else
            {
                tray = statusIcons[kTogglOfflineInactive];
                form = statusIcons[kTogglInactive];
            }
        }

        if (Icon != form)
        {
            Icon = form;
        }

        if (null != trayIcon)
        {
            if (trayIcon.Icon != tray)
            {
                trayIcon.Icon = tray;
            }
        }
    }

    void OnOnlineState(Int64 state)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnOnlineState(state);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnOnlineState(state)));
            return;
        }

        // FIXME: render online state on bottom of the window
        updateStatusIcons(0 == state);
    }

    void OnURL(string url)
    {
        Process.Start(url);
    }

    void OnApp(bool open)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnApp(open);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnApp(open)));
            return;
        }

        if (open) {
            show();
        }
    }

    void OnError(string errmsg, bool user_error)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnError(errmsg, user_error);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnError(errmsg, user_error)));
            return;
        }

        errorLabel.Text = errmsg;
        errorToolTip.SetToolTip(errorLabel, errmsg);
        troubleBox.Visible = true;
        contentPanel.Location = errorContentPosition;
    }

    void OnIdleNotification(
        string guid,
        string since,
        string duration,
        UInt64 started,
        string description)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnIdleNotification(guid, since, duration, started, description);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnIdleNotification(guid, since, duration, started, description)));
            return;
        }

        idleNotificationWindowController.ShowWindow();
    }

    void OnLogin(bool open, UInt64 user_id)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate {
                OnLogin(open, user_id);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnLogin(open, user_id)));
            return;
        }

        if (open) {
            if (editForm.Visible)
            {
                editForm.Hide();
                editForm.GUID = null;
                timeEntryListViewController.DisableHighlight();
            }
            contentPanel.Controls.Remove(timeEntryListViewController);
            contentPanel.Controls.Add(loginViewController);
            setCorrectMinimumSize();
            loginViewController.SetAcceptButton(this);
            resizeHandle.BackColor = Color.FromArgb(69, 69, 69);
        }
        enableMenuItems();
        updateStatusIcons(true);

        if (open || 0 == user_id)
        {
            runningToolStripMenuItem.Text = "Timer is not tracking";
        }

        currentUserEmailMenuItem.Text = Toggl.UserEmail();
    }

    private void enableMenuItems()
    {
        bool isLoggedIn = TogglDesktop.Program.IsLoggedIn;

        newToolStripMenuItem.Enabled = isLoggedIn;
        continueToolStripMenuItem.Enabled = isLoggedIn && !isTracking;
        stopToolStripMenuItem.Enabled = isLoggedIn && isTracking;
        syncToolStripMenuItem.Enabled = isLoggedIn;
        logoutToolStripMenuItem.Enabled = isLoggedIn;
        clearCacheToolStripMenuItem.Enabled = isLoggedIn;
        sendFeedbackToolStripMenuItem.Enabled = isLoggedIn;
        openInBrowserToolStripMenuItem.Enabled = isLoggedIn;
    }

    void OnTimeEntryList(bool open, List<Toggl.TogglTimeEntryView> list)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnTimeEntryList(open, list);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnTimeEntryList(open, list)));
            return;
        }

        if (open)
        {
            troubleBox.Visible = false;
            contentPanel.Location = defaultContentPosition;
            contentPanel.Controls.Remove(loginViewController);
            setCorrectMinimumSize();
            contentPanel.Controls.Add(timeEntryListViewController);
            timeEntryListViewController.SetAcceptButton(this);
            if (editForm.Visible)
            {
                editForm.Hide();
                editForm.GUID = null;
                timeEntryListViewController.DisableHighlight();
            }
        }
    }

    private void initEditForm()
    {
        editForm = new EditForm
        {
            ControlBox = false,
            StartPosition = FormStartPosition.Manual
        };

        var editViewHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = this.timeEntryEditViewController
        };

        editForm.Controls.Add(editViewHost);

        editForm.SetViewController(this.timeEntryEditViewController);

        editForm.VisibleChanged += (sender, args) => this.updateEntriesListWidth();
        editForm.Resize += (sender, args) => this.updateEntriesListWidth();
    }

    public void PopupInput(Toggl.TogglTimeEntryView te)
    {
        if (te.GUID == editForm.GUID) {
            editForm.ClosePopup();
            return;
        }
        editForm.reset();
        setEditFormLocation();
        editForm.GUID = te.GUID;
        editForm.Show();
        timeEntryListViewController.HighlightEntry(te.GUID);
    }

    void OnTimeEntryEditor(
        bool open,
        Toggl.TogglTimeEntryView te,
        string focused_field_name)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnTimeEntryEditor(open, te, focused_field_name);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnTimeEntryEditor(open, te, focused_field_name)));
            return;
        }

        if (open)
        {
            contentPanel.Controls.Remove(loginViewController);
            MinimumSize = new Size(230, 86);
            timeEntryEditViewController.FocusField(focused_field_name);
            PopupInput(te);
        }
        timeEntryListViewController.HighlightEntry(te.GUID);
    }

    private void MainWindowController_FormClosing(object sender, FormClosingEventArgs e)
    {
        Utils.SaveWindowLocation(this, editForm);

        if (CloseReason.WindowsShutDown == e.CloseReason)
        {
            return;
        }

        if (!TogglDesktop.Program.ShuttingDown)
        {
            Hide();
            e.Cancel = true;
        }

        if (editForm.Visible)
        {
            editForm.ClosePopup();
        }
    }

    private void buttonDismissError_Click(object sender, EventArgs e)
    {
        troubleBox.Visible = false;
        contentPanel.Location = defaultContentPosition;
    }

    private void sendFeedbackToolStripMenuItem_Click(object sender, EventArgs e)
    {
        feedbackWindowController.Show();
        feedbackWindowController.TopMost = true;
    }

    private void quitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (Visible)
        {
            Utils.SaveWindowLocation(this, editForm);
        }

        TogglDesktop.Program.Shutdown(0);
    }

    private void toggleVisibility()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
            show();
            return;
        }
        if (Visible)
        {
            Hide();
            return;
        }
        show();
    }

    private void newToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (this.manualMode)
        {
            var guid = Toggl.Start("", "0", 0, 0, "", "");
            Toggl.Edit(guid, false, Toggl.Duration);
        }
        else
        {
            Toggl.Start("", "", 0, 0, "", "");   
        }
    }

    private void continueToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Toggl.ContinueLatest();
    }

    private void stopToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Toggl.Stop();
    }

    private void showToolStripMenuItem_Click(object sender, EventArgs e)
    {
        show();
    }

    private void syncToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Toggl.Sync();
    }

    private void openInBrowserToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Toggl.OpenInBrowser();
    }

    private void preferencesToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Toggl.EditPreferences();
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
    {
        aboutWindowController.ShowUpdates();
    }

    private void logoutToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Toggl.Logout();
    }

    private void show()
    {
        Show();
        TopMost = true;
        setWindowPos();
    }

    private void setWindowPos()
    {
        var onTop = this.remainOnTop && !this.topDisabled;

        var hwndInsertAfter = onTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST;

        Win32.SetWindowPos(this.Handle, hwndInsertAfter, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
        if (this.editForm != null)
        {
            this.editForm.SetWindowPos(onTop);
        }
    }

    void OnReminder(string title, string informative_text)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                OnReminder(title, informative_text);
            });
            return;
        }

        if (!this.timeEntryEditViewController.Dispatcher.CheckAccess())
        {
            this.timeEntryEditViewController.Dispatcher.BeginInvoke(new Action(() => OnReminder(title, informative_text)));
            return;
        }

        trayIcon.ShowBalloonTip(6000 * 100, title, informative_text, ToolTipIcon.None);
    }

    private void clearCacheToolStripMenuItem_Click(object sender, EventArgs e)
    {
        DialogResult dr;
        try
        {
            DisableTop();
            dr = MessageBox.Show(
                "This will remove your Toggl user data from this PC and log you out of the Toggl Desktop app. " +
                "Any unsynced data will be lost." +
                Environment.NewLine + "Do you want to continue?",
                "Clear Cache",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }
        finally
        {
            EnableTop();
        }
        if (DialogResult.Yes == dr)
        {
            Toggl.ClearCache();
        }
    }

    private void trayIcon_BalloonTipClicked(object sender, EventArgs e)
    {
        show();
    }

    private void timerIdleDetection_Tick(object sender, EventArgs e)
    {
        Win32.LASTINPUTINFO lastInputInfo = new Win32.LASTINPUTINFO();
        lastInputInfo.cbSize = Marshal.SizeOf(lastInputInfo);
        lastInputInfo.dwTime = 0;
        if (!Win32.GetLastInputInfo(out lastInputInfo)) {
            return;
        }
        int idle_seconds = unchecked(Environment.TickCount - (int)lastInputInfo.dwTime) / 1000;
        if (idle_seconds < 1) {
            return;
        }
        Toggl.SetIdleSeconds((ulong)idle_seconds);
    }

    private void trayIcon_MouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            toggleVisibility();
        }
    }

    private void MainWindowController_Activated(object sender, EventArgs e)
    {
        Toggl.SetWake();
    }

    private void MainWindowController_LocationChanged(object sender, EventArgs e)
    {
        recalculatePopupPosition();
        this.updateMaxmimumSize();

        if (this.WindowState != FormWindowState.Maximized)
        {
            if (this.FormBorderStyle != FormBorderStyle.Sizable)
                this.FormBorderStyle = FormBorderStyle.Sizable;
        }
    }

    private void setEditFormLocation()
    {
        this.calculateEditFormPosition();
    }

    private Screen getCurrentScreen()
    {
        if (Screen.AllScreens.Length > 1)
        {
            foreach (var s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(this.DesktopBounds))
                {
                    return s;
                }
            }
        }

        return Screen.PrimaryScreen;
    }

    private void calculateEditFormPosition()
    {
        var editPopupLocation = this.Location;

        if (this.WindowState == FormWindowState.Maximized)
        {
            var timerHeight = this.timeEntryListViewController.TimerHeight;
            var headerHeight = timerHeight + 40;

            editPopupLocation.Y += headerHeight;
            editPopupLocation.X += this.Width;

            this.editForm.SetPlacement(true, editPopupLocation, this.Height - headerHeight, true);
        }
        else
        {
            var s = this.getCurrentScreen();
            bool left = s.WorkingArea.Right - this.Right < this.editForm.Width;

            if (!left)
            {
                editPopupLocation.X += this.Width;
            }

            this.editForm.SetPlacement(left, editPopupLocation, this.Height);
        }

    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (this.editForm.Visible)
            {
                this.editForm.ClosePopup();
                return true;
            }
        }

        foreach (var item in this.trayIconMenu.Items)
        {
            var asMenuItem = item as ToolStripMenuItem;
            if (asMenuItem != null)
            {
                if (keyData == asMenuItem.ShortcutKeys)
                {
                    asMenuItem.PerformClick();
                    return true;
                }
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void MainWindowController_SizeChanged(object sender, EventArgs e)
    {
        if (this.WindowState == FormWindowState.Maximized && this.FormBorderStyle != FormBorderStyle.None)
        {
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
        }

        recalculatePopupPosition();
        resizeHandle.Location = new Point(Width-16, Height-56);
        updateResizeHandleBackground();
    }

    private void updateMaxmimumSize()
    {
        var screenSize = this.getCurrentScreen().WorkingArea.Size;
        this.MaximumSize = screenSize;
    }

    private void updateEntriesListWidth(bool? overrideMaximised = null)
    {
        if (this.timeEntryListViewController == null)
            return;

        var maximised = overrideMaximised ?? (this.WindowState == FormWindowState.Maximized);

        if (!maximised || this.editForm == null || !this.editForm.Visible)
        {
            this.timeEntryListViewController.DisableListWidth();
            return;
        }

        this.timeEntryListViewController.SetListWidth(this.Width - this.editForm.Width);
    }

    protected override void WndProc(ref Message message)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MAXIMIZE = 0xF030;

        switch (message.Msg)
        {
        case WM_SYSCOMMAND:
        {
            var command = message.WParam.ToInt32() & 0xfff0;
            if (command == SC_MAXIMIZE)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.updateEntriesListWidth(true);
            }
        }
        break;
        }
        base.WndProc(ref message);
    }

    private void updateResizeHandleBackground() {
        if (contentPanel.Controls.Contains(loginViewController))
        {
            resizeHandle.BackColor = Color.FromArgb(69, 69, 69);
        }
        else if (Height <= MinimumSize.Height)
        {
            String c = "#4dd965";
            if(isTracking) {
                c = "#ff3d32";
            }
            resizeHandle.BackColor = ColorTranslator.FromHtml(c);
        }
        else
        {
            resizeHandle.BackColor = System.Drawing.Color.Transparent;
        }
    }
    private void recalculatePopupPosition()
    {
        if (editForm != null && editForm.Visible)
        {
            setEditFormLocation();
        }
    }


    private void resizeHandle_MouseDown(object sender, MouseEventArgs e)
    {
        isResizing = true;
    }

    private void resizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (isResizing)
        {
            isResizing = (e.Button == MouseButtons.Left);
            Win32.ReleaseCapture();
            int buttonEvent = (isResizing) ? Win32.wmNcLButtonDown : Win32.wmNcLButtonUp;
            Win32.SendMessage(Handle, buttonEvent, Win32.HtBottomRight, 0);
        }
    }

    private void useManualModeToolStripMenuItem_Click(object sender, EventArgs e)
    {
        this.manualMode = !this.manualMode;

        this.useManualModeToolStripMenuItem.Text =
            this.manualMode ? "Use timer" : "Use manual mode";

        this.timeEntryListViewController.SetManualMode(this.manualMode);
    }
}
}
