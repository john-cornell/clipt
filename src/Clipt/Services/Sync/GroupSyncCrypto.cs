using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Clipt.Services.Sync;

public sealed class GroupSyncCrypto : IGroupSyncCrypto
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public byte[] DeriveKey(string passphrase, byte[] salt, int timeCost, int memoryCostKib, int parallelism)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        ArgumentNullException.ThrowIfNull(salt);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = timeCost,
            MemorySize = memoryCostKib,
        };
        return argon2.GetBytes(KeySizeBytes);
    }

    public byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(key, TagSizeBytes))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + ciphertext.Length, TagSizeBytes);
        return result;
    }

    public byte[] Decrypt(byte[] key, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length < NonceSizeBytes + TagSizeBytes)
            throw new ArgumentException("Payload too short to be valid.", nameof(payload));

        byte[] nonce = payload[..NonceSizeBytes];
        byte[] tag = payload[^TagSizeBytes..];
        byte[] ciphertext = payload[NonceSizeBytes..^TagSizeBytes];
        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
