using Foundation;
using PhotosUI;
using UniformTypeIdentifiers;

namespace ZephyrApp;

public class PickerDelegate : PHPickerViewControllerDelegate
{
    private readonly TaskCompletionSource<List<PickedFile>> done;

    public PickerDelegate(TaskCompletionSource<List<PickedFile>> done)
    {
        this.done = done;
    }

    public override async void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
    {
        picker.DismissViewController(true, null);
        List<PickedFile> files = new();
        foreach (PHPickerResult result in results)
        {
            PickedFile? file = await Copy(result.ItemProvider);
            if (file != null)
                files.Add(file);
        }
        done.TrySetResult(files);
    }

    private static Task<PickedFile?> Copy(NSItemProvider provider)
    {
        string type = provider.HasItemConformingTo(UTTypes.Movie.Identifier) ? UTTypes.Movie.Identifier : UTTypes.Image.Identifier;
        TaskCompletionSource<PickedFile?> result = new();

        provider.LoadFileRepresentation(type, (url, error) =>
        {
            try
            {
                if (url?.Path == null)
                {
                    result.TrySetResult(null);
                    return;
                }

                string dir = Path.Combine(FileSystem.CacheDirectory, "send", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string name = (provider.SuggestedName ?? "file") + Path.GetExtension(url.Path);
                string dest = Path.Combine(dir, name);
                File.Copy(url.Path, dest, true);
                result.TrySetResult(new PickedFile
                {
                    Name = name,
                    Size = new FileInfo(dest).Length,
                    TempPath = dest,
                    Open = () => File.OpenRead(dest)
                });
            }
            catch (Exception)
            {
                result.TrySetResult(null);
            }
        });

        return result.Task;
    }
}
