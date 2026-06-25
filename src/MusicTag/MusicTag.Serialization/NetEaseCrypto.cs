using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MusicTag.Serialization;

// Managed re-implementation of the NetEase (网易云音乐) algorithms the native
// MusicTag.dll used to supply through its obfuscated exports rc2 / rc3 / rc4:
//   - rc2 -> weapi request encryption  (BuildEncryptedRequest)
//   - rc4 -> "163 key" comment encode  (EncodeMusicComment)
//   - rc3 -> "163 key" comment decode  (DecodeMusicComment)
// The constants are the public, well-known NetEase values. Verified against the
// original DLL (see artifacts/NetEaseCryptoProbe): the 163-key encode is
// byte-for-byte identical to native, and weapi is self-consistent and produces
// the same {a,b} shape (weapi is randomized per call in native too, so it cannot
// be byte-compared - only its structure and round-trip can).
internal static class NetEaseCrypto
{
	// weapi: the fixed first-layer AES-128-CBC key + shared IV, and the RSA public
	// key (exponent + modulus) used to wrap the random per-request secret key.
	private static readonly byte[] WeapiAesKey = Encoding.ASCII.GetBytes("0CoJUm6Qyw8W8jud");
	private static readonly byte[] WeapiAesIv = Encoding.ASCII.GetBytes("0102030405060708");
	private const string WeapiRsaExponent = "010001";
	private const string WeapiRsaModulus = "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7";
	private const string SecretKeyAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

	// "163 key" comment payload: AES-128-ECB with a fixed key, base64, fixed prefix.
	private static readonly byte[] CommentAesKey = Encoding.ASCII.GetBytes("#14ljk_!\\]&0U<'(");
	private const string CommentPrefix = "163 key(Don't modify):";

	// rc2: encrypt the request body, returning { "a": params, "b": encSecKey } -
	// the same JObject shape the native export handed back, so callers are unchanged.
	public static JObject BuildEncryptedRequest(string requestJson)
	{
		string secretKey = CreateRandomSecretKey();
		string firstPass = AesCbcToBase64(Encoding.UTF8.GetBytes(requestJson), WeapiAesKey, WeapiAesIv);
		string encryptedParams = AesCbcToBase64(Encoding.UTF8.GetBytes(firstPass), Encoding.ASCII.GetBytes(secretKey), WeapiAesIv);
		string encryptedSecretKey = RsaEncryptNoPadding(secretKey);
		return new JObject
		{
			{ "a", encryptedParams },
			{ "b", encryptedSecretKey }
		};
	}

	// rc4: encode plaintext into a "163 key" comment value (deterministic).
	public static string EncodeMusicComment(string plainText)
	{
		byte[] cipher = AesEcb(Encoding.UTF8.GetBytes(plainText), CommentAesKey, encrypt: true);
		return CommentPrefix + Convert.ToBase64String(cipher);
	}

	// rc3: decode a "163 key" comment value back to plaintext; returns "" for any
	// value that is not a 163-key payload or that fails to decode (callers already
	// null/whitespace-check the result).
	public static string DecodeMusicComment(string comment)
	{
		if (string.IsNullOrEmpty(comment) || !comment.StartsWith(CommentPrefix))
		{
			return "";
		}
		try
		{
			byte[] plain = AesEcb(Convert.FromBase64String(comment.Substring(CommentPrefix.Length)), CommentAesKey, encrypt: false);
			return Encoding.UTF8.GetString(plain);
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static string CreateRandomSecretKey()
	{
		byte[] randomBytes = new byte[16];
		using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
		{
			rng.GetBytes(randomBytes);
		}
		StringBuilder builder = new StringBuilder(16);
		foreach (byte value in randomBytes)
		{
			builder.Append(SecretKeyAlphabet[value % SecretKeyAlphabet.Length]);
		}
		return builder.ToString();
	}

	private static string AesCbcToBase64(byte[] data, byte[] key, byte[] iv)
	{
		using (Aes aes = Aes.Create())
		{
			aes.Key = key;
			aes.IV = iv;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;
			using (ICryptoTransform encryptor = aes.CreateEncryptor())
			{
				return Convert.ToBase64String(encryptor.TransformFinalBlock(data, 0, data.Length));
			}
		}
	}

	private static byte[] AesEcb(byte[] data, byte[] key, bool encrypt)
	{
		using (Aes aes = Aes.Create())
		{
			aes.Key = key;
			aes.Mode = CipherMode.ECB;
			aes.Padding = PaddingMode.PKCS7;
			using (ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
			{
				return transform.TransformFinalBlock(data, 0, data.Length);
			}
		}
	}

	// Textbook RSA (c = m^e mod n, no padding) over the reversed plaintext bytes,
	// big-endian hex output zero-padded to the 256-hex-digit modulus width - the
	// exact form NetEase's encSecKey uses.
	private static string RsaEncryptNoPadding(string text)
	{
		char[] reversed = text.ToCharArray();
		Array.Reverse(reversed);
		BigInteger message = BigEndianToBigInteger(Encoding.ASCII.GetBytes(new string(reversed)));
		BigInteger cipher = BigInteger.ModPow(message, HexToBigInteger(WeapiRsaExponent), HexToBigInteger(WeapiRsaModulus));
		return BigIntegerToHex(cipher).PadLeft(256, '0');
	}

	private static BigInteger BigEndianToBigInteger(byte[] bigEndian)
	{
		byte[] littleEndian = new byte[bigEndian.Length + 1];
		for (int i = 0; i < bigEndian.Length; i++)
		{
			littleEndian[i] = bigEndian[bigEndian.Length - 1 - i];
		}
		littleEndian[bigEndian.Length] = 0; // trailing zero byte forces a positive sign
		return new BigInteger(littleEndian);
	}

	private static BigInteger HexToBigInteger(string hex)
	{
		if (hex.Length % 2 == 1)
		{
			hex = "0" + hex;
		}
		byte[] bigEndian = new byte[hex.Length / 2];
		for (int i = 0; i < bigEndian.Length; i++)
		{
			bigEndian[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		}
		return BigEndianToBigInteger(bigEndian);
	}

	private static string BigIntegerToHex(BigInteger value)
	{
		byte[] littleEndian = value.ToByteArray();
		int length = littleEndian.Length;
		while (length > 1 && littleEndian[length - 1] == 0)
		{
			length--;
		}
		StringBuilder builder = new StringBuilder(length * 2);
		for (int i = length - 1; i >= 0; i--)
		{
			builder.Append(littleEndian[i].ToString("x2"));
		}
		return builder.ToString();
	}
}
