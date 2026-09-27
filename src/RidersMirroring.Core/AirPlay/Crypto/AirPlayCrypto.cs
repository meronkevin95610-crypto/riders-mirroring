using System.Security.Cryptography;
using System.Text;

namespace Riders.Mirroring.Core.AirPlay.Crypto;

/// <summary>
/// AirPlay uses RSA-1024 for the initial handshake and AES-128-CTR for
/// payload streaming. This file bundles the small set of crypto operations
/// we need without pulling in a full RSA/OAEP library.
/// </summary>
/// <remarks>
/// The iPhone sends us its RSA public key (1024-bit, X.509 SubjectPublicKeyInfo,
/// ASN.1 DER) embedded in the SETUP request body. We generate our own RSA
/// keypair, advertise the public key in the SETUP response, and from then on
/// the client encrypts its symmetric AES key + IV with our public key.
///
/// The AirPlay streaming key (used to decrypt H.264 NAL units) is a separate
/// AES-128 key + 16-byte IV negotiated through the RSA layer.
/// </remarks>
public static class AirPlayCrypto
{
    /// <summary>Generate a fresh RSA-1024 keypair to advertise to the client.</summary>
    public static RSAKeyPair GenerateServerKeyPair()
    {
        using var rsa = RSA.Create(1024);
        var publicBytes = rsa.ExportSubjectPublicKeyInfo();
        var privateBytes = rsa.ExportPkcs8PrivateKey();
        return new RSAKeyPair(
            PublicKeyDer: publicBytes,
            PrivateKeyPkcs8: privateBytes,
            PublicKeyModulus: rsa.ExportParameters(false).Modulus!,
            PublicKeyExponent: rsa.ExportParameters(false).Exponent!);
    }

    /// <summary>Import the client's public key (X.509 SPKI DER).</summary>
    public static RSA ImportClientPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        }
        catch
        {
            // Some iOS versions send PKCS#1 RSAPublicKey instead of X.509 SPKI.
            // PKCS#1 has the form: SEQUENCE { INTEGER modulus, INTEGER exponent }.
            rsa.ImportRSAPublicKey(ExtractPkcs1Key(subjectPublicKeyInfo), out _);
        }
        return rsa;
    }

    /// <summary>RSA-decrypt the streaming key the client sent us.</summary>
    public static byte[] RsaDecrypt(RSA privateKey, ReadOnlySpan<byte> ciphertext)
    {
        return privateKey.Decrypt(ciphertext, RSAEncryptionPadding.Pkcs1);
    }

    /// <summary>AES-128-CTR decrypt (the AirPlay streaming cipher).</summary>
    public static byte[] AesCtrDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Mode = CipherMode.ECB; // we'll wrap with manual CTR
        aes.Padding = PaddingMode.None;

        // .NET's AES doesn't have a CTR mode; we emulate it with ECB on each
        // 16-byte counter block.  For our typical payload (< 64 KB) this is
        // fast enough.
        using var decryptor = aes.CreateEncryptor(key.ToArray(), new byte[16]);
        var counter = iv.ToArray();
        var output = new byte[data.Length];
        for (int offset = 0; offset < data.Length; offset += 16)
        {
            var block = counter.AsSpan();
            var keystream = new byte[16];
            decryptor.TransformBlock(block.ToArray(), 0, 16, keystream, 0);
            var chunk = Math.Min(16, data.Length - offset);
            for (int i = 0; i < chunk; i++)
            {
                output[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            }
            IncrementCounter(counter);
        }
        return output;
    }

    /// <summary>Wrap RSA + AES key material exchanged with the client.</summary>
    public sealed record AirPlaySessionKeys(byte[] AesKey, byte[] AesIv, byte[] SharedSecret);

    private static void IncrementCounter(byte[] counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0) break;
        }
    }

    private static ReadOnlySpan<byte> ExtractPkcs1Key(ReadOnlySpan<byte> data)
    {
        // Find the SEQUENCE tag. PKCS#1 RSAPublicKey is always DER-encoded.
        if (data.Length < 2 || data[0] != 0x30) throw new CryptographicException("Not DER");
        // Skip length + modulus INTEGER + exponent INTEGER. Use a permissive copy.
        return data; // ImportRSAPublicKey accepts the full SEQUENCE.
    }
}

/// <summary>Our own RSA keypair, freshly generated when we start the receiver.</summary>
public sealed record RSAKeyPair(byte[] PublicKeyDer, byte[] PrivateKeyPkcs8, byte[] PublicKeyModulus, byte[] PublicKeyExponent);