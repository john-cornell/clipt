namespace Clipt.Services.Sync;

public interface IGroupSyncCrypto
{
    byte[] DeriveKey(string passphrase, byte[] salt, int timeCost, int memoryCostKib, int parallelism);

    /// <summary>Returns nonce (12 bytes) + ciphertext + tag (16 bytes), concatenated.</summary>
    byte[] Encrypt(byte[] key, byte[] plaintext);

    /// <summary>Reverses <see cref="Encrypt"/>. Throws <see cref="System.Security.Cryptography.CryptographicException"/> if the payload was tampered with or the key is wrong.</summary>
    byte[] Decrypt(byte[] key, byte[] payload);
}
