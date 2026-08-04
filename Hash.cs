using System.Security.Cryptography;

namespace FurinaPixivBatchDownloader;

public class Hash
{
    public static byte[] GetSha256(string filePath)
    {
        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.SequentialScan);

        return SHA256.HashData(stream);
    }
    public static byte[] GetSha256(byte[] d)
    {
        return SHA256.HashData(d);
    }
}