namespace ZephyrApp;

public static class PcSettings
{
    public static string Url
    {
        get => Preferences.Default.Get("pcUrl", "");
        set => Preferences.Default.Set("pcUrl", value);
    }

    public static string Normalize(string text)
    {
        string url = text.Trim().TrimEnd('/');
        if (url != "" && !url.StartsWith("http://") && !url.StartsWith("https://"))
            url = (url.Contains("trycloudflare.com") ? "https://" : "http://") + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return uri.GetLeftPart(UriPartial.Authority);
        return "";
    }
}
