namespace ZephyrRemote;

public class PinData
{
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public string RecoverySalt { get; set; } = "";
    public string RecoveryHash { get; set; } = "";
    public string RecoveryEncrypted { get; set; } = "";
    public List<string> WrongGuesses { get; set; } = new();
    public bool Locked { get; set; }
}
