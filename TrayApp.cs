using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZephyrRemote;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly string connectUrl;
    private readonly PinStore pins;
    private readonly DeviceTracker devices;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    public TrayApp(int port, bool firstRun, PinStore pins, DeviceTracker devices)
    {
        SetThreadExecutionState(0x80000000 | 0x00000001);
        this.pins = pins;
        this.devices = devices;
        connectUrl = $"http://localhost:{port}/connect.html";

        string iconPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "icon.png");
        Icon icon = File.Exists(iconPath)
            ? Icon.FromHandle(new Bitmap(iconPath).GetHicon())
            : SystemIcons.Application;

        ContextMenuStrip menu = new();
        menu.Items.Add("Connect my phone (QR code)", null, (s, e) => ShowConnect());
        menu.Items.Add("Approve waiting devices", null, (s, e) => ApproveDevices());
        menu.Items.Add("Unlock Zephyr Remote", null, (s, e) => Unlock());
        menu.Items.Add("Show recovery code", null, (s, e) => ShowRecovery());
        menu.Items.Add("Exit Zephyr Remote", null, (s, e) => Exit());

        tray = new NotifyIcon
        {
            Icon = icon,
            Text = "Zephyr Remote",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (s, e) => ShowConnect();

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
