using Android.App;
using Android.Content;
using Android.Content.PM;

namespace ZephyrApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public const int PickCode = 4711;

    public static TaskCompletionSource<Intent?>? PendingPick { get; set; }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == PickCode)
            PendingPick?.TrySetResult(resultCode == Result.Ok ? data : null);
    }
}
