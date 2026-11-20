namespace ZephyrApp;

public class PairPage : ContentPage
{
    private readonly Entry linkEntry;
    private readonly Label errorLabel;

    public PairPage(string message)
    {
        Title = "Connect";
        BackgroundColor = Color.FromArgb("#0B0D12");
        NavigationPage.SetHasNavigationBar(this, false);

        linkEntry = new Entry
        {
            Placeholder = "PC link, like http://192.168.1.30:5050",
            Text = PcSettings.Url,
            Keyboard = Keyboard.Url,
            TextColor = Colors.White,
            PlaceholderColor = Color.FromArgb("#6B7385"),
            BackgroundColor = Color.FromArgb("#151922"),
            HeightRequest = 52,
            FontSize = 16
        };
        linkEntry.Completed += async (s, e) => await Connect();

        errorLabel = new Label { Text = message, TextColor = Color.FromArgb("#FF8C8C"), FontSize = 14, HorizontalTextAlignment = TextAlignment.Center };

        Button connect = new()
        {
            Text = "Connect",
            BackgroundColor = Color.FromArgb("#3D6FF0"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 52
        };
        connect.Clicked += async (s, e) => await Connect();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(28, 90, 28, 40),
                Spacing = 16,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 76, HeightRequest = 76, BackgroundColor = Color.FromArgb("#3D6FF0"),
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 22 }, StrokeThickness = 0,
                        Content = new Label { Text = "Z", FontSize = 38, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                    },
                    new Label { Text = "Connect to your PC", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center },
                    new Label
                    {
                        Text = "Install Zephyr Remote on your Windows PC (github.com/JamilDbiat/ZephyrRemote), then type the link it shows. At home use the Wi-Fi link; away from home use the remote link from Settings.",
                        FontSize = 14, TextColor = Color.FromArgb("#8A93A6"), HorizontalTextAlignment = TextAlignment.Center
                    },
                    linkEntry,
                    connect,
                    errorLabel
                }
            }
        };
    }

    private async Task Connect()
    {
        string url = PcSettings.Normalize(linkEntry.Text ?? "");
        if (url == "")
        {
            errorLabel.Text = "That doesn't look like a link.";
            return;
        }

        errorLabel.TextColor = Color.FromArgb("#8A93A6");
        errorLabel.Text = "Connecting…";
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            HttpResponseMessage res = await http.GetAsync(url + "/setup/status");
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException();
        }
        catch (Exception)
        {
            errorLabel.TextColor = Color.FromArgb("#FF8C8C");
            errorLabel.Text = "Can't reach your PC at that link. Check that Zephyr is running and the link is right.";
            return;
        }

        PcSettings.Url = url;
        Application.Current!.Windows[0].Page = new NavigationPage(new RemotePage());
    }
}
