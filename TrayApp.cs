using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZephyrRemote;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly string connectUrl;
    private readonly PinStore pins;
    private readonly DeviceTracker devices;
    private readonly UpdateChecker updates;
    private readonly Control invoker = new();
    private readonly PhoneMirror phone = new();

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    public TrayApp(int port, bool firstRun, PinStore pins, DeviceTracker devices, UpdateChecker updates)
    {
        SetThreadExecutionState(0x80000000 | 0x00000001);
        this.pins = pins;
        this.devices = devices;
        this.updates = updates;
        invoker.CreateControl();
        connectUrl = $"http://localhost:{port}/connect.html";

        string iconPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "icon.png");
        Icon icon = File.Exists(iconPath)
            ? Icon.FromHandle(new Bitmap(iconPath).GetHicon())
            : SystemIcons.Application;

        ContextMenuStrip menu = new();
        menu.Items.Add("Connect my phone (QR code)", null, (s, e) => ShowConnect());
        ToolStripMenuItem phoneMenu = new("My phone");
        phoneMenu.DropDownItems.Add("Show my iPhone (AirPlay)", null, async (s, e) => await PhoneAction(() => phone.ShowIphone(Status), "On your iPhone: open Control Center, tap Screen Mirroring, and pick this PC."));
        phoneMenu.DropDownItems.Add("Show my Android (USB cable)", null, async (s, e) => await PhoneAction(() => phone.ShowAndroid(false, Status), ""));
        phoneMenu.DropDownItems.Add("Show my Android (Wi-Fi)", null, async (s, e) => await PhoneAction(() => phone.ShowAndroid(true, Status), ""));
        phoneMenu.DropDownItems.Add("Set up Android Wi-Fi (plug in with USB first)", null, async (s, e) => await PhoneAction(() => phone.SetupAndroidWifi(Status), "Wi-Fi is set up. Next time, use \"Show my Android (Wi-Fi)\" without the cable."));
        menu.Items.Add(phoneMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Approve waiting devices", null, (s, e) => ApproveDevices());
        menu.Items.Add("Unlock Zephyr Remote", null, (s, e) => Unlock());
        menu.Items.Add("Show recovery code", null, (s, e) => ShowRecovery());
        menu.Items.Add("Check for updates", null, async (s, e) => await CheckUpdates());
        menu.Items.Add("Exit Zephyr Remote", null, (s, e) => Exit());

        tray = new NotifyIcon
        {
            Icon = icon,
            Text = "Zephyr Remote",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (s, e) => ShowConnect();
        tray.BalloonTipClicked += async (s, e) => await AskToUpdate();

        if (firstRun)
            ShowConnect();
    }

    private void ShowConnect()
    {
        Process.Start("explorer.exe", "\"" + connectUrl + "\"");
    }

    private void ApproveDevices()
    {
        List<DeviceInfo> waiting = devices.All().Where(d => !d.Trusted).ToList();
        if (waiting.Count == 0)
        {
            MessageBox.Show("No devices are waiting for approval.", "Zephyr Remote");
            return;
        }

        string list = string.Join("\n", waiting.Select(d => $"• {d.Name} · IP {d.Ip} · code {d.Code}"));
        if (MessageBox.Show("Let these devices into Zephyr Remote?\n\n" + list, "Zephyr Remote", MessageBoxButtons.YesNo) == DialogResult.Yes)
            devices.ApproveAll();
    }

    private void Status(string text)
    {
        invoker.BeginInvoke(() => tray.ShowBalloonTip(8000, "Zephyr Remote", text, ToolTipIcon.Info));
    }

    private async Task PhoneAction(Func<Task<string>> action, string success)
    {
        string error;
        try
        {
            error = await action();
        }
        catch (HttpRequestException)
        {
            error = "Couldn't download it. Check your internet and try again.";
        }
        if (error != "")
            MessageBox.Show(error, "Zephyr Remote");
        else if (success != "")
            Status(success);
    }

    public void NotifyUpdate()
    {
        invoker.BeginInvoke(() => tray.ShowBalloonTip(15000, $"Zephyr Remote {updates.Latest} is out", "Click here to update now. It takes about a minute.", ToolTipIcon.Info));
    }

    private async Task CheckUpdates()
    {
        bool found;
        try
        {
            found = await updates.Check();
        }
        catch (HttpRequestException)
        {
            MessageBox.Show("Couldn't reach GitHub. Check your internet.", "Zephyr Remote");
            return;
        }
        if (found)
            await AskToUpdate();
        else
            MessageBox.Show($"You have the latest version ({updates.Current}).", "Zephyr Remote");
    }

    private async Task AskToUpdate()
    {
        if (!updates.Available)
            return;
        if (!updates.Installable)
        {
            Process.Start("explorer.exe", $"\"https://github.com/{UpdateChecker.Repo}/releases/latest\"");
            return;
        }
        if (MessageBox.Show($"Update Zephyr Remote from {updates.Current} to {updates.Latest} now?\n\nIt closes for about a minute and starts again by itself.", "Zephyr Remote", MessageBoxButtons.YesNo) == DialogResult.Yes)
            await updates.Install();
    }

    private void Unlock()
    {
        pins.UnlockFromPc();
        MessageBox.Show("Zephyr Remote is unlocked. Your phone can sign in with your PIN again.", "Zephyr Remote");
    }

    private void ShowRecovery()
    {
        string code = pins.RecoveryCode();
        MessageBox.Show(code == "" ? "No recovery code yet. Create a PIN from your phone first." : "Your recovery code:\n\n" + code + "\n\nKeep it somewhere safe. You need it to unlock Zephyr after 3 wrong PINs.", "Zephyr Remote");
    }

    private void Exit()
    {
        tray.Visible = false;
        Environment.Exit(0);
    }
}
