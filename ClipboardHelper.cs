namespace ZephyrRemote;

public static class ClipboardHelper
{
    public static string GetText()
    {
        string text = "";
        RunSta(() =>
        {
            if (Clipboard.ContainsText())
                text = Clipboard.GetText();
        });
        return text;
    }

    public static void SetText(string text)
    {
        RunSta(() =>
        {
            if (text == "")
                Clipboard.Clear();
            else
                Clipboard.SetText(text);
        });
    }

    private static void RunSta(Action action)
    {
        Thread thread = new(() => action());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
