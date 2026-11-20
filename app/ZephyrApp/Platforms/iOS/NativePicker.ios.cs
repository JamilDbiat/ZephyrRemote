using PhotosUI;
using UIKit;

namespace ZephyrApp;

public static partial class NativePicker
{
    public static partial Task<List<PickedFile>> PickAsync()
    {
        TaskCompletionSource<List<PickedFile>> done = new();
        PHPickerConfiguration config = new()
        {
            SelectionLimit = 0,
            Filter = PHPickerFilter.GetAnyFilterMatchingSubfilters(new[] { PHPickerFilter.ImagesFilter, PHPickerFilter.VideosFilter }),
            PreferredAssetRepresentationMode = PHPickerConfigurationAssetRepresentationMode.Current
        };

        UIViewController? top = Platform.GetCurrentUIViewController();
        if (top == null)
            return Task.FromResult(new List<PickedFile>());

        PHPickerViewController picker = new(config) { Delegate = new PickerDelegate(done) };
        top.PresentViewController(picker, true, null);
        return done.Task;
    }
}
