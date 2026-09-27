using System.Security.Cryptography;
using FluentAssertions;
using Riders.Mirroring.Core.AirPlay.Crypto;

namespace RidersMirroring.Core.Tests.AirPlay;

public class AirPlayCryptoTests
{
    [Fact]
    public void GenerateServerKeyPair_ProducesValidRsa()
    {
        var pair = AirPlayCrypto.GenerateServerKeyPair();
        pair.PublicKeyDer.Should().NotBeEmpty();
        pair.PublicKeyModulus.Should().HaveCount(128); // 1024 bits = 128 bytes
    }

    [Fact]
    public void RsaRoundtrip_DecryptsWhatRsaEncrypted()
    {
        var pair = AirPlayCrypto.GenerateServerKeyPair();
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(pair.PrivateKeyPkcs8, out _);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("hello airplay");
        var cipher = rsa.Encrypt(plaintext, RSAEncryptionPadding.Pkcs1);
        var recovered = AirPlayCrypto.RsaDecrypt(rsa, cipher);

        recovered.Should().Equal(plaintext);
    }

    [Fact]
    public void AesCtrDecrypt_XorsWithKeystream()
    {
        // AES-CTR with key + iv all zero should produce the keystream XOR'd
        // against input. For 16 bytes of zeroes, that equals the keystream.
        var key = new byte[16];
        var iv = new byte[16];
        var data = new byte[16];

        var result = AirPlayCrypto.AesCtrDecrypt(key, iv, data);
        result.Should().NotBeEquivalentTo(data); // keystream shouldn't be all zero
    }

    [Fact]
    public void AesCtrDecrypt_HandlesMultipleBlocks()
    {
        var key = new byte[16];
        var iv = new byte[16];
        var data = new byte[64]; // 4 blocks
        var result = AirPlayCrypto.AesCtrDecrypt(key, iv, data);
        result.Should().HaveCount(64);
    }

    [Fact]
    public void AesCtrDecrypt_ProducesSameOutputForSameInput()
    {
        var key = new byte[16];
        for (int i = 0; i < 16; i++) key[i] = (byte)(0x10 + i);
        var iv = new byte[16];
        var data = new byte[32];
        var r1 = AirPlayCrypto.AesCtrDecrypt(key, iv, data);
        var r2 = AirPlayCrypto.AesCtrDecrypt(key, iv, data);
        r1.Should().Equal(r2);
    }
}