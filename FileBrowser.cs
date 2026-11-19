namespace ZephyrRemote;

public class FileBrowser
{
    public string UploadFolder { get; }

    public FileBrowser()
    {
        UploadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Zephyr");
        Directory.CreateDirectory(UploadFolder);
    }

    public List<FileEntry> Places()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<FileEntry> places = new()
        {
            Place("📥 From phone", UploadFolder),
            Place("🖥️ Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            Place("⬇️ Downloads", Path.Combine(home, "Downloads")),
            Place("📄 Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            Place("🖼️ Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            Place("🎬 Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))
        };
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            places.Add(Place("💽 " + drive.Name.TrimEnd('\\'), drive.RootDirectory.FullName));
        return places;
    }

    public List<FileEntry>? List(string path)
    {
        if (!Directory.Exists(path))
            return null;

        EnumerationOptions options = new()
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };
        DirectoryInfo dir = new(path);
        List<FileEntry> folders = dir.EnumerateDirectories("*", options)
            .OrderBy(d => d.Name)
            .Select(d => new FileEntry { Name = d.Name, Path = d.FullName, Folder = true, Modified = d.LastWriteTime })
            .ToList();
        List<FileEntry> files = dir.EnumerateFiles("*", options)
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new FileEntry { Name = f.Name, Path = f.FullName, Size = f.Length, Modified = f.LastWriteTime })
            .ToList();
        return folders.Concat(files).ToList();
    }

    public string NewUploadPath(string name)
    {
        string clean = Path.GetFileName(name);
        if (clean == "")
            clean = "file";
        string path = Path.Combine(UploadFolder, clean);
        int n = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(UploadFolder, $"{Path.GetFileNameWithoutExtension(clean)} ({n}){Path.GetExtension(clean)}");
            n++;
        }
        File.Create(path).Dispose();
        return path;
    }

    public string? UploadPath(string name)
    {
        string path = Path.Combine(UploadFolder, Path.GetFileName(name));
        return File.Exists(path) ? path : null;
    }

    private static FileEntry Place(string name, string path)
    {
        return new FileEntry { Name = name, Path = path, Folder = true };
    }
}
