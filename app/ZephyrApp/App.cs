namespace ZephyrApp;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page start = PcSettings.Url == "" ? new PairPage("") : new RemotePage();
        return new Window(new NavigationPage(start) { BarBackgroundColor = Color.FromArgb("#0B0D12"), BarTextColor = Colors.White });
    }
}
