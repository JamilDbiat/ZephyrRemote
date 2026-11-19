namespace ZephyrRemote;

public class FileEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Folder { get; set; }
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}
