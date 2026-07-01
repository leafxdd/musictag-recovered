using System;
using System.Collections.Generic;
using MusicTag.Serialization;

namespace MusicTag.Tests;

// NetEaseCrypto(MusicTag.Serialization)的 "163 key" 注释编解码 characterization。
// EncodeMusicComment/DecodeMusicComment 是 AES-128-ECB(PKCS7,固定 key "#14ljk_!\]&0U<'(")+ base64 +
// 固定前缀的 managed 重实现(注释自述:encode 对原生 MusicTag.dll 逐字节验证过)。现有 provider 测试全部以
// CommentTagWrite163Key=False 跑,根本不进 163-key 编解码路径 -> 此加密层【零覆盖】。本批锁定字节级契约:
//   - 字节 golden:Encode("hello") 的密文段由 openssl(aes-128-ecb,同 key hex 2331346c6a6b5f215c5d2630553c2728)
//     录制,build 反证 .NET 实现与之字节一致 —— 捕捉 AES key/mode/padding 漂移。
//   - round-trip 恒等:Decode(Encode(s))==s(含中文 UTF8/空串/混合),锁 encode/decode 互逆。
//   - guard 分支:非前缀/IsNullOrEmpty/坏 base64/密文长度非块倍数 -> ""(调用方已 null/whitespace 兜底)。
internal static class NetEaseCryptoCharacterization
{
	private const string Prefix = "163 key(Don't modify):";

	public static IEnumerable<(string, Action)> All()
	{
		// ===== EncodeMusicComment:确定性 + 字节 golden + 前缀 =====

		// 字节 golden:密文段 = openssl aes-128-ecb(key hex 2331346c6a6b5f215c5d2630553c2728) 对 "hello" 的输出。
		// build 通过即证 .NET Aes ECB/PKCS7 与 openssl 字节一致(捕捉 key/mode/padding 漂移)。
		yield return ("EncodeMusicComment: \"hello\" -> golden bytes", delegate
		{
			Check.Equal(Prefix + "NfoF1V6DRszdX66Ox09daA==", NetEaseCrypto.EncodeMusicComment("hello"), "hello golden ciphertext");
		});

		yield return ("EncodeMusicComment: result starts with 163-key prefix", delegate
		{
			Check.True(NetEaseCrypto.EncodeMusicComment("anything").StartsWith(Prefix), "prefix present");
		});

		yield return ("EncodeMusicComment: deterministic (no randomness)", delegate
		{
			Check.Equal(NetEaseCrypto.EncodeMusicComment("同一输入"), NetEaseCrypto.EncodeMusicComment("同一输入"), "same input -> same output");
		});

		// ===== round-trip 恒等:Decode(Encode(s)) == s =====

		yield return ("round-trip: ascii \"hello\"", delegate
		{
			Check.Equal("hello", NetEaseCrypto.DecodeMusicComment(NetEaseCrypto.EncodeMusicComment("hello")), "ascii round-trip");
		});

		yield return ("round-trip: chinese \"音乐世界\" (UTF8)", delegate
		{
			Check.Equal("音乐世界", NetEaseCrypto.DecodeMusicComment(NetEaseCrypto.EncodeMusicComment("音乐世界")), "chinese round-trip");
		});

		yield return ("round-trip: empty string", delegate
		{
			Check.Equal("", NetEaseCrypto.DecodeMusicComment(NetEaseCrypto.EncodeMusicComment("")), "empty round-trip");
		});

		yield return ("round-trip: mixed \"mixed 中英 123 !@#\"", delegate
		{
			Check.Equal("mixed 中英 123 !@#", NetEaseCrypto.DecodeMusicComment(NetEaseCrypto.EncodeMusicComment("mixed 中英 123 !@#")), "mixed round-trip");
		});

		// ===== DecodeMusicComment guard 分支 -> "" =====

		yield return ("DecodeMusicComment: no prefix -> \"\"", delegate
		{
			Check.Equal("", NetEaseCrypto.DecodeMusicComment("not a 163 key"), "no prefix guard");
		});

		yield return ("DecodeMusicComment: empty -> \"\"", delegate
		{
			Check.Equal("", NetEaseCrypto.DecodeMusicComment(""), "empty guard");
		});

		yield return ("DecodeMusicComment: null -> \"\"", delegate
		{
			Check.Equal("", NetEaseCrypto.DecodeMusicComment(null), "null guard");
		});

		yield return ("DecodeMusicComment: prefix + invalid base64 -> \"\" (FormatException caught)", delegate
		{
			Check.Equal("", NetEaseCrypto.DecodeMusicComment(Prefix + "@@@"), "bad base64 caught");
		});

		yield return ("DecodeMusicComment: prefix + valid base64 but non-block ciphertext -> \"\" (CryptographicException caught)", delegate
		{
			// "aaaa" base64 解出 3 字节,非 16 倍数 -> AES ECB 解密抛,被 catch -> ""。
			Check.Equal("", NetEaseCrypto.DecodeMusicComment(Prefix + "aaaa"), "non-block ciphertext caught");
		});

		yield return ("DecodeMusicComment: prefix case-sensitive (163 KEY... not matched) -> \"\"", delegate
		{
			Check.Equal("", NetEaseCrypto.DecodeMusicComment("163 KEY(Don't modify):x"), "prefix is case-sensitive");
		});
	}
}
