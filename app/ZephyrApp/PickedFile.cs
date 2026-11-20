namespace ZephyrApp;

public class PickedFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public Func<Stream> Open { get; set; } = () => Stream.Null;
    public string TempPath { get; set; } = "";
}
