using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncCryptoTests
{
    // Small params — this test only checks determinism/uniqueness, not production KDF strength.
    private const int TestTimeCost = 1;
    private const int TestMemoryCostKib = 8192;
    private const int TestParallelism = 1;

    private readonly GroupSyncCrypto _crypto = new();

    [Fact]
    public void DeriveKey_SamePassphraseAndSalt_ReturnsSameKey()
    {
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8];

        byte[] key1 = _crypto.DeriveKey("correct horse battery staple", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] key2 = _crypto.DeriveKey("correct horse battery staple", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentPassphrase_ReturnsDifferentKey()
    {
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8];

        byte[] key1 = _crypto.DeriveKey("passphrase one", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] key2 = _crypto.DeriveKey("passphrase two", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        byte[] key = _crypto.DeriveKey("passphrase", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello, synced group");

        byte[] ciphertext = _crypto.Encrypt(key, plaintext);
        byte[] decrypted = _crypto.Decrypt(key, ciphertext);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, ciphertext);
    }

    [Fact]
    public void Decrypt_TamperedPayload_ThrowsCryptographicException()
    {
        byte[] key = _crypto.DeriveKey("passphrase", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] ciphertext = _crypto.Encrypt(key, System.Text.Encoding.UTF8.GetBytes("hello"));
        ciphertext[^1] ^= 0xFF; // flip a bit in the auth tag

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => _crypto.Decrypt(key, ciphertext));
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        byte[] key1 = _crypto.DeriveKey("passphrase one", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] key2 = _crypto.DeriveKey("passphrase two", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] ciphertext = _crypto.Encrypt(key1, System.Text.Encoding.UTF8.GetBytes("hello"));

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => _crypto.Decrypt(key2, ciphertext));
    }
}
