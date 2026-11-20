using Android.Content;
using Android.Provider;

namespace ZephyrApp;

public static partial class NativePicker
{
    public static async partial Task<List<PickedFile>> PickAsync()
    {
        List<PickedFile> files = new();
        Android.App.Activity? activity = Platform.CurrentActivity;
        if (activity?.ContentResolver == null)
            return files;

        Intent intent = new(Intent.ActionGetContent);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { "image/*", "video/*" });
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        intent.AddCategory(Intent.CategoryOpenable);

        MainActivity.PendingPick = new TaskCompletionSource<Intent?>();
        activity.StartActivityForResult(intent, MainActivity.PickCode);
        Intent? data = await MainActivity.PendingPick.Task;

        List<Android.Net.Uri> uris = new();
        if (data?.ClipData != null)
        {
            for (int i = 0; i < data.ClipData.ItemCount; i++)
            {
                Android.Net.Uri? uri = data.ClipData.GetItemAt(i)?.Uri;
                if (uri != null)
                    uris.Add(uri);
            }
        }
        else if (data?.Data != null)
        {
            uris.Add(data.Data);
        }

        ContentResolver resolver = activity.ContentResolver;
        foreach (Android.Net.Uri uri in uris)
        {
            string name = "file";
            long size = 0;
            using (Android.Database.ICursor? cursor = resolver.Query(uri, null, null, null, null))
            {
                if (cursor != null && cursor.MoveToFirst())
                {
                    int nameColumn = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                    int sizeColumn = cursor.GetColumnIndex(IOpenableColumns.Size);
                    if (nameColumn >= 0)
                        name = cursor.GetString(nameColumn) ?? name;
                    if (sizeColumn >= 0)
                        size = cursor.GetLong(sizeColumn);
                }
            }

            Android.Net.Uri captured = uri;
            files.Add(new PickedFile { Name = name, Size = size, Open = () => resolver.OpenInputStream(captured) ?? Stream.Null });
        }
        return files;
    }
}
