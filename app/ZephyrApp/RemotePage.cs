using System.Text.Json;

namespace ZephyrApp;

public class RemotePage : ContentPage
{
    private const string Hook = """
        (function () {
          if (window.__zephyrApp) return;
          window.__zephyrApp = true;
          var go = document.querySelector('.big2 .go');
          if (go) go.onclick = function () { location.href = 'zephyrapp://upload'; };
          window.appUpload = function (done, total, sent, all, failed, finished, note) {
            var box = document.getElementById('uploadStatus');
            if (!box) return;
            var mb = function (b) { return b < 1048576 ? Math.round(b / 1024) + ' KB' : (b / 1048576).toFixed(1) + ' MB'; };
            var pct = Math.min(100, Math.round(sent / Math.max(all, 1) * 100));
            if (note) { box.innerHTML = '<div class="card"><small>' + note + '</small></div>'; return; }
            if (finished) {
              box.innerHTML = failed
                ? '<div class="card"><small>Sent ' + done + ' of ' + total + '. ' + failed + ' failed.</small></div>'
                : '<div class="card"><small style="color:#5BE3A0">Sent all ' + total + ' to Downloads\\Zephyr</small></div>';
              if (typeof openFolder === 'function' && document.getElementById('filePath').textContent.endsWith('Zephyr')) openFolder(document.getElementById('filePath').textContent);
              return;
            }
            box.innerHTML = '<div class="card"><div style="display:flex;justify-content:space-between;font-size:14px"><b style="font-size:14px">' + (done + failed) + ' of ' + total + ' sent</b><span style="color:var(--muted)">' + pct + '%</span></div>'
              + '<div style="height:6px;border-radius:3px;background:var(--card2)"><div style="height:6px;width:' + pct + '%;border-radius:3px;background:var(--accent)"></div></div>'
              + '<small>' + mb(sent) + ' of ' + mb(all) + (failed ? ' · ' + failed + ' failed' : '') + '</small></div>';
          };
          var settings = document.getElementById('settingsBox');
          if (settings && !document.getElementById('appChangePc')) {
            var b = document.createElement('button');
            b.id = 'appChangePc'; b.className = 'secondary'; b.textContent = 'Change PC link';
            b.onclick = function () { location.href = 'zephyrapp://pair'; };
            settings.appendChild(b);
          }
        })();
        """;

    private readonly WebView web;
    private bool uploading;

    public RemotePage()
    {
        BackgroundColor = Color.FromArgb("#0B0D12");
        NavigationPage.SetHasNavigationBar(this, false);
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, false);

        web = new WebView
        {
            Source = PcSettings.Url + "/",
            BackgroundColor = Color.FromArgb("#0B0D12"),
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 ZephyrApp/1.0"
        };
        web.Navigating += OnNavigating;
        web.Navigated += OnNavigated;
        Content = web;
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            Application.Current!.Windows[0].Page = new NavigationPage(new PairPage("Can't reach your PC. If it restarted, its remote link changed: paste the new one."));
            return;
        }
        await web.EvaluateJavaScriptAsync(Hook);
        Dispatcher.StartTimer(TimeSpan.FromSeconds(2), () =>
        {
            _ = web.EvaluateJavaScriptAsync(Hook);
            return true;
        });
    }

    private async void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("zephyrapp://"))
            return;
        e.Cancel = true;

        if (e.Url.StartsWith("zephyrapp://pair"))
        {
            Application.Current!.Windows[0].Page = new NavigationPage(new PairPage(""));
            return;
        }
        if (e.Url.StartsWith("zephyrapp://upload") && !uploading)
            await Upload();
    }

    private async Task Upload()
    {
        uploading = true;
        try
        {
            string auth = Unquote(await web.EvaluateJavaScriptAsync("auth()"));
            await Show(0, 0, 0, 0, 0, false, "Opening your photos…");
            List<PickedFile> files = await NativePicker.PickAsync();
            if (files.Count == 0)
            {
                await web.EvaluateJavaScriptAsync("document.getElementById('uploadStatus').innerHTML=''");
                return;
            }

            long total = files.Sum(f => f.Size);
            Uploader uploader = new(PcSettings.Url, auth);
            DeviceDisplay.Current.KeepScreenOn = true;
            Task sending = uploader.SendAll(files, 4);
            while (!sending.IsCompleted)
            {
                await Show(uploader.Done, files.Count, uploader.SentBytes, total, uploader.Failed, false, "");
                await Task.WhenAny(sending, Task.Delay(400));
            }
            await sending;
            await Show(uploader.Done, files.Count, total, total, uploader.Failed, true, "");
        }
        catch (Exception ex)
        {
            await Show(0, 0, 0, 0, 0, false, "Sending failed: " + ex.Message);
        }
        finally
        {
            DeviceDisplay.Current.KeepScreenOn = false;
            uploading = false;
        }
    }

    private Task Show(int done, int total, long sent, long all, int failed, bool finished, string note)
    {
        string script = $"window.appUpload && appUpload({done},{total},{sent},{all},{failed},{(finished ? "true" : "false")},{JsonSerializer.Serialize(note)})";
        return MainThread.InvokeOnMainThreadAsync(() => web.EvaluateJavaScriptAsync(script));
    }

    private static string Unquote(string? value)
    {
        string text = value ?? "";
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = JsonSerializer.Deserialize<string>(text) ?? "";
        return text.Replace("\\u0026", "&");
    }
}
