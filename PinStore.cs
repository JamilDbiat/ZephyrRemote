using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZephyrRemote;

public class PinStore
{
    public const int MaxTries = 3;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly string file;
    private readonly string oldFile;
    private readonly object sync = new();
    private PinData data = new();

    public bool HasPin => data.Hash != "";
    public bool Locked => data.Locked;
    public int TriesLeft => Math.Max(0, MaxTries - data.WrongGuesses.Count);

    public PinStore()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote");
        Directory.CreateDirectory(folder);
        file = Path.Combine(folder, "pin.json");
        oldFile = Path.Combine(folder, "pin.txt");

        if (File.Exists(file))
        {
            data = JsonSerializer.Deserialize<PinData>(File.ReadAllText(file)) ?? new PinData();
        }
        else if (File.Exists(oldFile))
        {
            string[] parts = File.ReadAllText(oldFile).Trim().Split(':');
            if (parts.Length == 2)
            {
                data.Salt = parts[0];
                data.Hash = parts[1];
                NewRecoveryCode();
                Save();
            }
        }
    }

    public bool Check(string pin)
    {
        lock (sync)
        {
            if (!HasPin || data.Locked)
                return false;

            string guess = Hash(pin, data.Salt);
            if (guess == data.Hash)
            {
                if (data.WrongGuesses.Count > 0)
                {
                    data.WrongGuesses.Clear();
                    Save();
                }
                return true;
            }

            if (pin == "" || data.WrongGuesses.Contains(guess))
                return false;

            data.WrongGuesses.Add(guess);
            if (data.WrongGuesses.Count >= MaxTries)
                data.Locked = true;
            Save();
            return false;
        }
    }

    public string? Create(string pin)
    {
        lock (sync)
        {
            if (HasPin || !ValidPin(pin))
                return null;

            data.Salt = NewSalt();
            data.Hash = Hash(pin, data.Salt);
            string code = NewRecoveryCode();
            Save();
            return code;
        }
    }

    public bool Change(string pin)
    {
        lock (sync)
        {
            if (!ValidPin(pin))
                return false;

            data.Salt = NewSalt();
            data.Hash = Hash(pin, data.Salt);
            data.WrongGuesses.Clear();
            Save();
            return true;
        }
    }

    public bool Unlock(string code)
    {
        lock (sync)
        {
            string clean = code.Trim().ToUpper().Replace("-", "").Replace(" ", "");
            if (data.RecoveryHash == "" || Hash(clean, data.RecoverySalt) != data.RecoveryHash)
                return false;

            Reset();
            return true;
        }
    }

    public void UnlockFromPc()
    {
        lock (sync)
            Reset();
    }

    public string RecoveryCode()
    {
        if (data.RecoveryEncrypted == "")
            return "";
        byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(data.RecoveryEncrypted), null, DataProtectionScope.CurrentUser);
        return Format(Encoding.UTF8.GetString(plain));
    }

    private void Reset()
    {
        data.Locked = false;
        data.WrongGuesses.Clear();
        Save();
    }

    private string NewRecoveryCode()
    {
        char[] chars = new char[16];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        string code = new(chars);

        data.RecoverySalt = NewSalt();
        data.RecoveryHash = Hash(code, data.RecoverySalt);
        byte[] secret = ProtectedData.Protect(Encoding.UTF8.GetBytes(code), null, DataProtectionScope.CurrentUser);
        data.RecoveryEncrypted = Convert.ToBase64String(secret);
        return Format(code);
    }

    private void Save()
    {
        File.WriteAllText(file, JsonSerializer.Serialize(data));
        if (File.Exists(oldFile))
            File.Delete(oldFile);
    }

    private static bool ValidPin(string pin)
    {
        return pin.Length >= 4 && pin.Length <= 32;
    }

    private static string Format(string code)
    {
        return string.Join("-", Enumerable.Range(0, code.Length / 4).Select(i => code.Substring(i * 4, 4)));
    }

    private static string NewSalt()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    private static string Hash(string value, string salt)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + value)));
    }
}
