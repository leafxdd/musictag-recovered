using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MusicTag.States;
using MusicTagWinApp.Instances;

namespace MusicTagWinApp.Common;

internal class Tokenizer
{
	private class EncodingDetector : EncodingNameTables
	{
		private int[,] serializerStub;

		private int[,] importerStub;

		private int[,] _AttrStub;

		private int[,] _ReponseStub;

		private int[,] algoStub;

		private int[,] _RecordStub;

		private int[,] eventStub;

		public bool logEncodingScores;

		public EncodingDetector()
		{
			logEncodingScores = true;
			serializerStub = new int[94, 94];
			importerStub = new int[126, 191];
			_AttrStub = new int[94, 158];
			_ReponseStub = new int[126, 191];
			algoStub = new int[94, 94];
			_RecordStub = new int[94, 94];
			eventStub = new int[94, 94];
			InitializeFrequencyTables();
		}

		public int DetectFileEncodingIndex(string filePath)
		{
			var (bytes, bytesRead) = ReadFileSampleBytes(filePath);
			if (bytesRead <= 0)
			{
				return 23;
			}
			return DetectEncodingIndex(bytes);
		}

		public int DetectEncodingIndex(byte[] bytes)
		{
			int result = 23;
			int[] encodingScores = new int[24];
			encodingScores[0] = ScoreGb2312Encoding(bytes);
			encodingScores[1] = ScoreGbkEncoding(bytes);
			encodingScores[2] = ScoreGb18030Encoding(bytes);
			encodingScores[3] = ScoreHzGb2312Encoding(bytes);
			encodingScores[4] = ScoreBig5Encoding(bytes);
			encodingScores[5] = ScoreEucTwEncoding(bytes);
			encodingScores[12] = ScoreIso2022CnEncoding(bytes);
			encodingScores[6] = ScoreUtf8Encoding(bytes);
			encodingScores[9] = ScoreUtf16Bom(bytes);
			encodingScores[15] = ScoreEucKrEncoding(bytes);
			encodingScores[16] = ScoreCp949Encoding(bytes);
			encodingScores[18] = 0;
			encodingScores[17] = ScoreIso2022KrEncoding(bytes);
			encodingScores[22] = ScoreAsciiEncoding(bytes);
			encodingScores[19] = ScoreShiftJisEncoding(bytes);
			encodingScores[20] = ScoreEucJpEncoding(bytes);
			encodingScores[21] = ScoreIso2022JpEncoding(bytes);
			encodingScores[10] = 0;
			encodingScores[11] = 0;
			encodingScores[14] = 0;
			encodingScores[13] = 0;
			encodingScores[23] = 0;
			int bestScore = default(int);
			for (int i = 0; i < 24; i++)
			{
				if (logEncodingScores)
				{
					Console.WriteLine("Encoding " + EncodingNameTables.GetDisplayEncodingNames()[i] + " score " + encodingScores[i]);
				}
				if (encodingScores[i] > bestScore)
				{
					result = i;
					bestScore = encodingScores[i];
				}
			}
			if (bestScore <= 50)
			{
				result = 23;
			}
			return result;
		}

		private int ScoreGb2312Encoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (161 <= bytes[offset] && bytes[offset] <= 247 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = (sbyte)bytes[offset + 1] + 256 - 161;
					if (serializerStub[row, column] != 0)
					{
						frequencyScore += serializerStub[row, column];
					}
					else if (15 <= row && row < 55)
					{
						frequencyScore += 200L;
					}
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreGbkEncoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (161 <= bytes[offset] && bytes[offset] <= 247 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = (sbyte)bytes[offset + 1] + 256 - 161;
					if (serializerStub[row, column] != 0)
					{
						frequencyScore += serializerStub[row, column];
					}
					else if (15 <= row && row < 55)
					{
						frequencyScore += 200L;
					}
					offset++;
					continue;
				}
				bool isGbkPair = 129 <= bytes[offset] && bytes[offset] <= 254 && ((128 <= bytes[offset + 1] && bytes[offset + 1] <= 254) || (64 <= bytes[offset + 1] && bytes[offset + 1] <= 126));
				if (isGbkPair)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 129;
					int column = ((64 > bytes[offset + 1] || bytes[offset + 1] > 126) ? ((sbyte)bytes[offset + 1] + 256 - 64) : ((sbyte)bytes[offset + 1] - 64));
					if (importerStub[row, column] != 0)
					{
						frequencyScore += importerStub[row, column];
					}
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore) - 1;
		}

		private int ScoreGb18030Encoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (161 <= bytes[offset] && bytes[offset] <= 247 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = (sbyte)bytes[offset + 1] + 256 - 161;
					if (serializerStub[row, column] != 0)
					{
						frequencyScore += serializerStub[row, column];
					}
					else if (15 <= row && row < 55)
					{
						frequencyScore += 200L;
					}
					offset++;
					continue;
				}
				bool isGbkPair = 129 <= bytes[offset] && bytes[offset] <= 254 && ((128 <= bytes[offset + 1] && bytes[offset + 1] <= 254) || (64 <= bytes[offset + 1] && bytes[offset + 1] <= 126));
				if (isGbkPair)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 129;
					int column = ((64 > bytes[offset + 1] || bytes[offset + 1] > 126) ? ((sbyte)bytes[offset + 1] + 256 - 64) : ((sbyte)bytes[offset + 1] - 64));
					if (importerStub[row, column] != 0)
					{
						frequencyScore += importerStub[row, column];
					}
					offset++;
					continue;
				}
				if (129 <= bytes[offset] && bytes[offset] <= 254 && offset + 3 < bytes.Length && 48 <= bytes[offset + 1] && bytes[offset + 1] <= 57 && 129 <= bytes[offset + 2] && bytes[offset + 2] <= 254 && 48 <= bytes[offset + 3] && bytes[offset + 3] <= 57)
				{
					matchedPairs++;
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore) - 1;
		}

		private int ScoreHzGb2312Encoding(byte[] bytes)
		{
			int hzSequenceCount = 0;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length; offset++)
			{
				if (bytes[offset] != 126 || offset + 1 >= bytes.Length)
				{
					continue;
				}
				if (bytes[offset + 1] == 123)
				{
					hzSequenceCount++;
					offset += 2;
					while (offset < bytes.Length - 1 && bytes[offset] != 10 && bytes[offset] != 13)
					{
						if (bytes[offset] == 126 && bytes[offset + 1] == 125)
						{
							offset++;
							break;
						}
						if (33 <= bytes[offset] && bytes[offset] <= 119 && 33 <= bytes[offset + 1] && bytes[offset + 1] <= 119)
						{
							int row = (sbyte)bytes[offset] - 33;
							int column = (sbyte)bytes[offset + 1] - 33;
							maxFrequencyScore += 500L;
							if (serializerStub[row, column] != 0)
							{
								frequencyScore += serializerStub[row, column];
							}
							else if (15 <= row && row < 55)
							{
								frequencyScore += 200L;
							}
							offset += 2;
							continue;
						}
						if (161 <= bytes[offset] && bytes[offset] <= 247 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 247)
						{
							int row = (sbyte)bytes[offset] + 256 - 161;
							int column = (sbyte)bytes[offset + 1] + 256 - 161;
							maxFrequencyScore += 500L;
							if (serializerStub[row, column] != 0)
							{
								frequencyScore += serializerStub[row, column];
							}
							else if (15 <= row && row < 55)
							{
								frequencyScore += 200L;
							}
						}
						offset += 2;
					}
				}
				else if (bytes[offset + 1] == 125 || bytes[offset + 1] == 126)
				{
					offset++;
				}
			}
			float escapeSequenceScore = hzSequenceCount > 4 ? 50f : (hzSequenceCount > 1 ? 41f : (hzSequenceCount <= 0 ? 0f : 39f));
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(escapeSequenceScore + frequencyRatioScore);
		}

		private int ScoreBig5Encoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (161 <= bytes[offset] && bytes[offset] <= 249 && ((64 <= bytes[offset + 1] && bytes[offset + 1] <= 126) || (161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)))
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = ((64 > bytes[offset + 1] || bytes[offset + 1] > 126) ? ((sbyte)bytes[offset + 1] + 256 - 97) : ((sbyte)bytes[offset + 1] - 64));
					if (_AttrStub[row, column] != 0)
					{
						frequencyScore += _AttrStub[row, column];
					}
					else if (3 <= row && row <= 37)
					{
						frequencyScore += 200L;
					}
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreEucTwEncoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (offset + 3 < bytes.Length && bytes[offset] == 142 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 176 && 161 <= bytes[offset + 2] && bytes[offset + 2] <= 254 && 161 <= bytes[offset + 3] && bytes[offset + 3] <= 254)
				{
					matchedPairs++;
					offset += 3;
					continue;
				}
				if (161 <= bytes[offset] && bytes[offset] <= 254 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = (sbyte)bytes[offset + 1] + 256 - 161;
					if (algoStub[row, column] != 0)
					{
						frequencyScore += algoStub[row, column];
					}
					else if (35 <= row && row <= 92)
					{
						frequencyScore += 150L;
					}
					offset++;
				}
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreIso2022CnEncoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			int offset = 0;
			while (offset < bytes.Length - 1)
			{
				if (bytes[offset] == 27 && offset + 3 < bytes.Length && bytes[offset + 1] == 36 && bytes[offset + 2] == 41 && bytes[offset + 3] == 65)
				{
					offset += 4;
					while (offset < bytes.Length - 1 && bytes[offset] != 27)
					{
						totalBytes++;
						if (33 <= bytes[offset] && bytes[offset] <= 119 && 33 <= bytes[offset + 1] && bytes[offset + 1] <= 119)
						{
							matchedPairs++;
							int row = (sbyte)bytes[offset] - 33;
							int column = (sbyte)bytes[offset + 1] - 33;
							maxFrequencyScore += 500L;
							if (serializerStub[row, column] != 0)
							{
								frequencyScore += serializerStub[row, column];
							}
							else if (15 <= row && row < 55)
							{
								frequencyScore += 200L;
							}
							offset += 2;
						}
						else
						{
							offset++;
						}
					}
					continue;
				}
				if (bytes[offset] == 27 && offset + 3 < bytes.Length && bytes[offset + 1] == 36 && bytes[offset + 2] == 41 && bytes[offset + 3] == 71)
				{
					offset += 4;
					while (offset < bytes.Length - 1 && bytes[offset] != 27)
					{
						totalBytes++;
						if (33 <= bytes[offset] && bytes[offset] <= 126 && 33 <= bytes[offset + 1] && bytes[offset + 1] <= 126)
						{
							matchedPairs++;
							int row = (sbyte)bytes[offset] - 33;
							int column = (sbyte)bytes[offset + 1] - 33;
							maxFrequencyScore += 500L;
							if (algoStub[row, column] != 0)
							{
								frequencyScore += algoStub[row, column];
							}
							else if (35 <= row && row <= 92)
							{
								frequencyScore += 150L;
							}
							offset += 2;
						}
						else
						{
							offset++;
						}
					}
					continue;
				}
				if (bytes[offset] == 27 && offset + 2 < bytes.Length && bytes[offset + 1] == 40 && bytes[offset + 2] == 66)
				{
					offset += 3;
					continue;
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreUtf8Encoding(byte[] bytes)
		{
			int utf8ByteCount = 0;
			int asciiByteCount = 0;
			int totalLength = bytes.Length;
			for (int i = 0; i < totalLength; i++)
			{
				if ((bytes[i] & 0x7F) == bytes[i])
				{
					asciiByteCount++;
				}
				else if (-64 <= (sbyte)bytes[i] && (sbyte)bytes[i] <= -33 && i + 1 < totalLength && sbyte.MinValue <= (sbyte)bytes[i + 1] && (sbyte)bytes[i + 1] <= -65)
				{
					utf8ByteCount += 2;
					i++;
				}
				else if (-32 <= (sbyte)bytes[i] && (sbyte)bytes[i] <= -17 && i + 2 < totalLength && sbyte.MinValue <= (sbyte)bytes[i + 1] && (sbyte)bytes[i + 1] <= -65 && sbyte.MinValue <= (sbyte)bytes[i + 2] && (sbyte)bytes[i + 2] <= -65)
				{
					utf8ByteCount += 3;
					i += 2;
				}
			}
			if (asciiByteCount == totalLength)
			{
				return 0;
			}
			int utf8Score = (int)(100f * ((float)utf8ByteCount / (float)(totalLength - asciiByteCount)));
			if (utf8Score > 98)
			{
				return utf8Score;
			}
			if (utf8Score > 95 && utf8ByteCount > 30)
			{
				return utf8Score;
			}
			return 0;
		}

		private int ScoreUtf16Bom(byte[] bytes)
		{
			if (bytes.Length > 1 && ((bytes[0] == 254 && bytes[1] == byte.MaxValue) || (bytes[0] == byte.MaxValue && bytes[1] == 254)))
			{
				return 100;
			}
			return 0;
		}

		private int ScoreAsciiEncoding(byte[] bytes)
		{
			int score = 75;
			for (int i = 0; i < bytes.Length; i++)
			{
				if ((sbyte)bytes[i] < 0 || bytes[i] == 27)
				{
					score -= 5;
					if (score <= 0)
					{
						return 0;
					}
				}
			}
			return score;
		}

		private int ScoreEucKrEncoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (161 <= bytes[offset] && bytes[offset] <= 254 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = (sbyte)bytes[offset + 1] + 256 - 161;
					if (_RecordStub[row, column] != 0)
					{
						frequencyScore += _RecordStub[row, column];
					}
					else if (15 <= row && row < 55)
					{
						frequencyScore += 200L;
					}
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreCp949Encoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				bool hasLeadByte = 129 <= bytes[offset] && bytes[offset] <= 254;
				bool hasLetterTrail = (65 <= bytes[offset + 1] && bytes[offset + 1] <= 90) || (97 <= bytes[offset + 1] && bytes[offset + 1] <= 122);
				bool hasHighTrail = 129 <= bytes[offset + 1] && bytes[offset + 1] <= 254;
				if (hasLeadByte && (hasLetterTrail || hasHighTrail))
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					if (161 <= bytes[offset] && bytes[offset] <= 254 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
					{
						int row = (sbyte)bytes[offset] + 256 - 161;
						int column = (sbyte)bytes[offset + 1] + 256 - 161;
						if (_RecordStub[row, column] != 0)
						{
							frequencyScore += _RecordStub[row, column];
						}
					}
				}
				offset++;
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreIso2022KrEncoding(byte[] bytes)
		{
			for (int i = 0; i < bytes.Length; i++)
			{
				if (i + 3 < bytes.Length && bytes[i] == 27 && bytes[i + 1] == 36 && bytes[i + 2] == 41 && bytes[i + 3] == 67)
				{
					return 100;
				}
			}
			return 0;
		}

		private int ScoreEucJpEncoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				if (161 <= bytes[offset] && bytes[offset] <= 254 && 161 <= bytes[offset + 1] && bytes[offset + 1] <= 254)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256 - 161;
					int column = (sbyte)bytes[offset + 1] + 256 - 161;
					if (eventStub[row, column] != 0)
					{
						frequencyScore += eventStub[row, column];
					}
					else if (15 <= row && row < 55)
					{
						frequencyScore += 200L;
					}
					offset++;
				}
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore);
		}

		private int ScoreIso2022JpEncoding(byte[] bytes)
		{
			for (int i = 0; i < bytes.Length; i++)
			{
				if (i + 2 < bytes.Length && bytes[i] == 27 && bytes[i + 1] == 36 && bytes[i + 2] == 66)
				{
					return 100;
				}
			}
			return 0;
		}

		private int ScoreShiftJisEncoding(byte[] bytes)
		{
			int totalBytes = 1;
			int matchedPairs = 1;
			long frequencyScore = 0L;
			long maxFrequencyScore = 1L;
			for (int offset = 0; offset < bytes.Length - 1; offset++)
			{
				if ((sbyte)bytes[offset] >= 0)
				{
					continue;
				}
				totalBytes++;
				bool isLeadByte = (129 <= bytes[offset] && bytes[offset] <= 159) || (224 <= bytes[offset] && bytes[offset] <= 239);
				bool isTrailByte = (64 <= bytes[offset + 1] && bytes[offset + 1] <= 126) || (128 <= bytes[offset + 1] && bytes[offset + 1] <= 252);
				if (isLeadByte && isTrailByte)
				{
					matchedPairs++;
					maxFrequencyScore += 500L;
					int row = (sbyte)bytes[offset] + 256;
					int column = (sbyte)bytes[offset + 1] + 256;
					int lowTrailByteAdjustment = column < 159 ? 1 : 0;
					row = ((row >= 160) ? ((row - 176 << 1) - lowTrailByteAdjustment) : ((row - 112 << 1) - lowTrailByteAdjustment));
					row -= 32;
					column = 32;
					if (row < eventStub.GetLength(0) && column < eventStub.GetLength(1) && eventStub[row, column] != 0)
					{
						frequencyScore += eventStub[row, column];
					}
					offset++;
				}
			}
			float pairRatioScore = 50f * ((float)matchedPairs / (float)totalBytes);
			float frequencyRatioScore = 50f * ((float)frequencyScore / (float)maxFrequencyScore);
			return (int)(pairRatioScore + frequencyRatioScore) - 1;
		}

		// --- Character-frequency tables for encoding detection -----------------------
		//
		// Each Score*Encoding method scores a byte buffer against one encoding by looking
		// up candidate double-byte characters in one of these tables; a non-zero cell is a
		// frequency-rank weight for that character. The tables are pure static data.
		//
		// The original decompiled InitializeFrequencyTables filled them through an
		// obfuscated goto/switch state machine, but its predicates were compile-time
		// constants (PostRole() always returned true, InvokeRole() always false) and its
		// DestroyRole(table, r, c, v) helper was just `table[r, c] = v`. That machine
		// therefore reduced to a fixed list of cell assignments into freshly allocated
		// (zero-filled) tables. The data below is that exact final state, captured from
		// the original build by reflection (see artifacts/Dump-TokenizerTables.ps1) and
		// stored as flat { row, column, value } triples.

		private void InitializeFrequencyTables()
		{
			LoadFrequencyTable(serializerStub, SerializerStubFrequencies);
			LoadFrequencyTable(importerStub, ImporterStubFrequencies);
			LoadFrequencyTable(_AttrStub, AttrStubFrequencies);
			LoadFrequencyTable(_ReponseStub, ReponseStubFrequencies);
			LoadFrequencyTable(algoStub, AlgoStubFrequencies);
			LoadFrequencyTable(_RecordStub, RecordStubFrequencies);
			LoadFrequencyTable(eventStub, EventStubFrequencies);
		}

		// Replays packed { row, column, value } triples into a zero-initialized table.
		private static void LoadFrequencyTable(int[,] table, int[] packedCells)
		{
			for (int i = 0; i < packedCells.Length; i += 3)
			{
				table[packedCells[i], packedCells[i + 1]] = packedCells[i + 2];
			}
		}

		// serializerStub: 94x94 table, 400 non-zero cells.
		private static readonly int[] SerializerStubFrequencies =
		{
			15, 13, 301,  15, 17, 371,  15, 42, 318,  15, 48, 485,  15, 54, 367,  15, 56, 296,  15, 74, 247,  15, 75, 250,
			16, 2, 252,  16, 7, 381,  16, 16, 419,  16, 26, 441,  16, 29, 513,  16, 39, 390,  16, 55, 244,  16, 62, 326,
			16, 66, 445,  16, 67, 267,  16, 76, 370,  16, 79, 397,  17, 1, 416,  17, 26, 596,  17, 28, 240,  17, 30, 463,
			17, 36, 394,  17, 89, 408,  18, 0, 393,  18, 2, 361,  18, 3, 500,  18, 20, 368,  18, 38, 243,  18, 40, 533,
			18, 43, 224,  18, 51, 295,  18, 53, 200,  18, 85, 574,  19, 5, 396,  19, 10, 259,  19, 42, 483,  19, 45, 434,
			19, 50, 503,  19, 78, 251,  19, 81, 446,  19, 82, 587,  19, 87, 272,  19, 89, 410,  20, 10, 492,  20, 16, 529,
			20, 18, 203,  20, 24, 202,  20, 28, 582,  20, 31, 573,  20, 33, 207,  20, 34, 569,  20, 35, 599,  20, 39, 454,
			20, 55, 575,  20, 57, 437,  20, 66, 477,  20, 70, 439,  21, 7, 460,  21, 10, 433,  21, 14, 502,  21, 27, 541,
			21, 39, 307,  21, 50, 274,  21, 51, 554,  21, 63, 546,  21, 87, 544,  21, 88, 467,  21, 93, 480,  22, 1, 534,
			22, 7, 484,  22, 19, 355,  22, 28, 518,  22, 36, 353,  22, 38, 283,  22, 53, 495,  22, 70, 324,  22, 81, 278,
			22, 93, 313,  23, 23, 242,  23, 34, 261,  23, 40, 322,  23, 47, 359,  23, 62, 488,  23, 69, 223,  23, 78, 238,
			23, 85, 583,  23, 86, 334,  23, 87, 455,  23, 89, 273,  23, 91, 356,  24, 3, 470,  24, 5, 286,  24, 10, 508,
			24, 17, 325,  24, 55, 417,  24, 59, 279,  24, 65, 345,  24, 89, 584,  24, 90, 343,  24, 92, 557,  25, 2, 431,
			25, 34, 542,  25, 44, 577,  25, 45, 357,  25, 46, 354,  25, 59, 452,  25, 75, 256,  25, 81, 246,  25, 82, 550,
			26, 7, 271,  26, 9, 382,  26, 14, 413,  26, 15, 468,  26, 24, 535,  26, 37, 208,  26, 55, 481,  26, 64, 562,
			26, 77, 376,  26, 81, 349,  26, 88, 219,  26, 89, 456,  27, 10, 210,  27, 15, 340,  27, 19, 235,  27, 23, 453,
			27, 25, 469,  27, 27, 226,  27, 37, 350,  27, 38, 327,  27, 49, 556,  27, 50, 414,  27, 67, 449,  27, 90, 512,
			27, 93, 372,  28, 7, 309,  28, 10, 494,  28, 12, 222,  28, 26, 302,  28, 43, 428,  28, 47, 363,  28, 50, 321,
			28, 64, 288,  28, 65, 335,  28, 70, 341,  28, 79, 358,  28, 80, 339,  28, 87, 486,  28, 91, 258,  29, 8, 338,
			29, 12, 511,  29, 36, 387,  29, 44, 568,  29, 84, 316,  29, 85, 254,  29, 91, 409,  30, 9, 524,  30, 19, 527,
			30, 37, 293,  30, 40, 553,  30, 52, 229,  30, 57, 430,  30, 75, 270,  31, 19, 586,  31, 46, 504,  31, 76, 479,
			31, 77, 215,  31, 78, 551,  31, 90, 347,  32, 1, 380,  32, 5, 457,  32, 9, 266,  32, 11, 277,  32, 18, 205,
			32, 28, 501,  32, 30, 230,  32, 42, 595,  32, 53, 311,  32, 75, 260,  32, 77, 209,  32, 83, 257,  32, 86, 282,
			32, 88, 287,  33, 22, 365,  33, 58, 297,  33, 76, 379,  33, 89, 217,  34, 10, 312,  34, 19, 548,  34, 26, 539,
			34, 30, 281,  34, 31, 478,  34, 36, 436,  34, 38, 580,  34, 69, 509,  34, 80, 498,  34, 86, 475,  34, 90, 461,
			34, 91, 378,  35, 30, 383,  35, 38, 567,  35, 46, 308,  35, 48, 375,  35, 55, 212,  35, 57, 415,  35, 59, 543,
			35, 66, 576,  35, 73, 570,  36, 13, 459,  36, 40, 218,  37, 22, 233,  37, 28, 425,  37, 57, 364,  37, 62, 245,
			37, 67, 474,  37, 79, 536,  37, 87, 421,  38, 13, 204,  38, 15, 515,  38, 30, 241,  38, 54, 342,  38, 63, 280,
			38, 64, 225,  38, 68, 331,  38, 72, 464,  38, 74, 303,  38, 82, 234,  38, 87, 236,  39, 4, 559,  39, 7, 264,
			39, 10, 471,  39, 19, 411,  39, 26, 540,  39, 34, 300,  39, 42, 594,  39, 45, 333,  39, 46, 299,  39, 52, 538,
			39, 70, 516,  39, 74, 407,  39, 92, 506,  40, 10, 305,  40, 28, 405,  40, 46, 585,  40, 56, 418,  40, 70, 290,
			40, 76, 491,  40, 77, 237,  40, 80, 374,  40, 84, 228,  40, 88, 472,  40, 89, 563,  41, 5, 392,  41, 13, 522,
			41, 16, 571,  41, 17, 447,  41, 20, 482,  41, 21, 211,  41, 24, 429,  41, 28, 216,  41, 30, 232,  41, 31, 422,
			41, 33, 532,  41, 38, 597,  41, 47, 400,  41, 52, 206,  41, 53, 523,  41, 59, 346,  41, 72, 391,  41, 84, 289,
			41, 92, 373,  42, 13, 402,  42, 20, 581,  42, 27, 284,  42, 29, 348,  42, 31, 362,  42, 35, 438,  42, 66, 298,
			42, 88, 514,  42, 90, 590,  42, 91, 320,  42, 92, 552,  43, 7, 329,  43, 10, 403,  43, 55, 317,  43, 64, 360,
			43, 65, 310,  43, 68, 398,  43, 75, 545,  43, 84, 269,  43, 92, 423,  44, 7, 384,  44, 11, 507,  44, 18, 306,
			44, 22, 510,  44, 65, 493,  44, 73, 239,  44, 81, 401,  44, 84, 344,  44, 88, 263,  44, 90, 315,  45, 9, 579,
			45, 19, 201,  45, 26, 444,  45, 35, 526,  45, 41, 458,  45, 49, 593,  45, 61, 525,  45, 67, 248,  45, 68, 404,
			45, 78, 330,  45, 80, 337,  45, 86, 427,  46, 1, 214,  46, 20, 351,  46, 33, 565,  46, 39, 406,  46, 53, 496,
			46, 63, 435,  46, 74, 528,  46, 81, 465,  46, 82, 328,  47, 0, 549,  47, 5, 386,  47, 8, 490,  47, 19, 213,
			47, 33, 448,  47, 35, 547,  47, 36, 420,  47, 47, 519,  47, 51, 385,  47, 76, 319,  48, 6, 555,  48, 51, 265,
			48, 58, 399,  48, 88, 497,  49, 9, 566,  49, 17, 572,  49, 20, 451,  49, 26, 598,  49, 48, 505,  49, 51, 564,
			49, 65, 487,  49, 68, 294,  49, 81, 443,  50, 1, 220,  50, 5, 366,  50, 34, 521,  50, 40, 412,  50, 47, 591,
			50, 53, 517,  50, 57, 537,  50, 74, 489,  50, 78, 221,  51, 9, 291,  51, 12, 369,  51, 16, 388,  51, 21, 276,
			51, 25, 268,  51, 33, 476,  51, 42, 249,  51, 56, 426,  51, 57, 592,  51, 84, 292,  52, 24, 262,  52, 28, 323,
			52, 36, 377,  52, 62, 466,  52, 65, 589,  52, 69, 424,  52, 92, 462,  52, 93, 432,  53, 9, 499,  53, 13, 558,
			53, 16, 314,  53, 23, 285,  53, 26, 531,  53, 32, 332,  53, 37, 336,  53, 45, 255,  53, 47, 588,  53, 53, 473,
			53, 55, 442,  53, 57, 227,  53, 59, 253,  53, 86, 520,  54, 0, 389,  54, 9, 231,  54, 36, 560,  54, 41, 275,
			54, 50, 578,  54, 51, 561,  54, 53, 304,  54, 59, 395,  54, 62, 440,  54, 77, 450,  54, 85, 352,  54, 86, 530,
		};

		// importerStub: 126x191 table, 302 non-zero cells.
		private static readonly int[] ImporterStubFrequencies =
		{
			0, 11, 318,  0, 173, 577,  1, 64, 583,  1, 67, 556,  2, 136, 360,  2, 137, 419,  3, 37, 402,  3, 147, 466,
			4, 30, 376,  5, 86, 562,  5, 161, 512,  6, 184, 529,  7, 182, 359,  11, 23, 576,  11, 77, 472,  11, 98, 421,
			11, 102, 543,  13, 95, 319,  13, 151, 344,  14, 72, 315,  14, 132, 357,  15, 155, 471,  16, 106, 459,  19, 117, 362,
			20, 50, 569,  20, 184, 369,  20, 190, 581,  21, 60, 391,  23, 119, 372,  23, 147, 489,  24, 3, 541,  25, 39, 355,
			26, 29, 566,  29, 115, 388,  29, 169, 579,  30, 47, 517,  43, 6, 509,  45, 84, 514,  47, 44, 520,  47, 97, 366,
			47, 137, 487,  47, 145, 409,  47, 166, 340,  48, 123, 408,  48, 126, 511,  48, 136, 465,  48, 173, 361,  49, 123, 598,
			49, 127, 384,  49, 133, 429,  50, 99, 395,  50, 135, 467,  50, 137, 526,  50, 140, 410,  50, 182, 546,  51, 139, 488,
			51, 142, 449,  51, 178, 446,  51, 179, 594,  51, 186, 327,  52, 107, 515,  52, 125, 589,  52, 128, 521,  52, 131, 548,
			52, 132, 600,  52, 136, 386,  52, 152, 519,  52, 154, 414,  52, 156, 332,  53, 104, 500,  53, 124, 542,  53, 136, 342,
			53, 160, 553,  53, 184, 539,  53, 190, 468,  54, 14, 457,  54, 104, 518,  54, 125, 493,  54, 133, 303,  54, 135, 307,
			54, 150, 454,  54, 177, 373,  55, 132, 305,  55, 144, 416,  55, 159, 463,  55, 166, 346,  55, 183, 423,  55, 188, 345,
			56, 100, 547,  56, 107, 407,  56, 156, 378,  56, 162, 336,  56, 187, 502,  56, 190, 302,  57, 131, 580,  57, 141, 504,
			57, 142, 523,  57, 143, 350,  57, 156, 528,  58, 139, 427,  58, 152, 417,  58, 174, 301,  58, 178, 461,  59, 112, 405,
			59, 122, 442,  59, 146, 535,  59, 147, 418,  60, 25, 310,  60, 47, 399,  60, 57, 330,  60, 91, 481,  60, 104, 306,
			60, 123, 574,  60, 140, 456,  60, 147, 323,  60, 162, 389,  60, 177, 335,  61, 23, 377,  61, 141, 575,  62, 116, 564,
			62, 124, 343,  62, 134, 494,  62, 137, 585,  62, 172, 356,  63, 114, 385,  63, 143, 411,  63, 173, 484,  64, 98, 353,
			64, 102, 460,  64, 139, 591,  65, 96, 398,  65, 119, 444,  66, 127, 311,  66, 128, 382,  66, 166, 464,  66, 177, 425,
			66, 183, 447,  66, 187, 480,  67, 127, 352,  67, 135, 563,  67, 144, 314,  67, 152, 434,  67, 156, 571,  67, 163, 582,
			67, 170, 510,  67, 186, 394,  68, 35, 473,  68, 110, 458,  69, 154, 375,  69, 164, 497,  69, 176, 443,  70, 112, 508,
			70, 146, 309,  70, 165, 491,  70, 169, 498,  70, 178, 413,  71, 1, 469,  71, 101, 531,  71, 107, 422,  71, 123, 516,
			71, 139, 592,  71, 149, 477,  71, 167, 570,  71, 171, 371,  71, 180, 331,  71, 189, 490,  72, 125, 312,  72, 143, 578,
			72, 153, 420,  72, 167, 351,  72, 173, 329,  72, 186, 561,  73, 110, 438,  73, 114, 479,  73, 121, 432,  73, 125, 415,
			73, 130, 495,  73, 135, 599,  73, 150, 450,  74, 110, 397,  74, 132, 367,  74, 163, 333,  74, 185, 554,  74, 187, 565,
			74, 188, 358,  74, 189, 426,  75, 78, 325,  75, 86, 326,  75, 104, 527,  75, 107, 445,  75, 161, 560,  75, 172, 555,
			76, 104, 379,  76, 108, 524,  76, 162, 435,  76, 166, 316,  76, 170, 324,  76, 187, 349,  77, 123, 486,  77, 132, 568,
			77, 146, 597,  78, 114, 338,  78, 117, 452,  78, 130, 559,  78, 136, 381,  78, 160, 441,  78, 171, 549,  78, 177, 453,
			79, 97, 551,  79, 99, 403,  79, 105, 492,  79, 130, 505,  79, 132, 533,  79, 133, 525,  79, 135, 308,  79, 144, 483,
			79, 148, 412,  80, 37, 392,  80, 144, 300,  80, 148, 321,  81, 74, 393,  81, 106, 587,  81, 114, 573,  81, 123, 596,
			81, 145, 448,  81, 148, 586,  81, 162, 501,  81, 178, 503,  81, 180, 380,  82, 24, 433,  82, 75, 322,  82, 77, 536,
			82, 91, 328,  82, 131, 572,  82, 137, 365,  82, 144, 595,  82, 145, 430,  82, 150, 462,  83, 19, 337,  83, 82, 474,
			83, 83, 431,  83, 109, 383,  83, 130, 387,  83, 153, 470,  83, 154, 593,  83, 181, 348,  84, 10, 347,  84, 38, 567,
			84, 44, 313,  84, 72, 557,  84, 83, 406,  84, 126, 534,  84, 130, 522,  84, 146, 370,  84, 159, 482,  84, 166, 506,
			84, 189, 476,  84, 190, 317,  85, 56, 478,  85, 106, 530,  85, 110, 550,  85, 113, 299,  85, 123, 532,  85, 129, 354,
			85, 144, 590,  85, 152, 400,  85, 183, 440,  85, 184, 424,  86, 76, 368,  86, 147, 538,  86, 148, 540,  86, 150, 320,
			86, 174, 507,  86, 182, 396,  86, 183, 485,  88, 9, 334,  88, 25, 588,  91, 71, 455,  92, 30, 363,  94, 0, 584,
			94, 2, 304,  94, 13, 428,  94, 30, 558,  94, 56, 364,  94, 64, 545,  96, 161, 537,  100, 37, 374,  104, 12, 451,
			104, 31, 475,  104, 39, 437,  105, 16, 439,  106, 57, 341,  106, 74, 544,  107, 118, 513,  109, 30, 339,  109, 61, 496,
			110, 12, 390,  114, 55, 401,  119, 16, 436,  123, 14, 552,  123, 35, 499,  124, 72, 404,
		};

		// _AttrStub: 94x158 table, 400 non-zero cells.
		private static readonly int[] AttrStubFrequencies =
		{
			3, 0, 596,  3, 6, 591,  3, 7, 468,  3, 8, 592,  3, 10, 371,  3, 15, 460,  3, 17, 438,  3, 19, 462,
			3, 20, 490,  3, 21, 559,  3, 23, 578,  3, 29, 573,  3, 40, 271,  3, 42, 594,  3, 43, 458,  3, 44, 538,
			3, 48, 551,  3, 51, 312,  3, 53, 547,  3, 54, 442,  3, 55, 448,  3, 62, 429,  3, 66, 598,  3, 67, 590,
			3, 70, 550,  3, 76, 292,  3, 79, 479,  3, 84, 335,  3, 87, 231,  3, 89, 360,  3, 92, 407,  3, 95, 454,
			3, 101, 284,  3, 108, 430,  3, 109, 405,  3, 110, 272,  3, 112, 555,  3, 114, 445,  3, 117, 420,  3, 126, 533,
			3, 129, 450,  3, 131, 207,  3, 132, 568,  3, 135, 493,  3, 136, 477,  3, 138, 387,  3, 144, 465,  3, 147, 397,
			3, 152, 249,  3, 156, 282,  4, 0, 278,  4, 2, 309,  4, 4, 440,  4, 8, 586,  4, 12, 565,  4, 14, 327,
			4, 19, 202,  4, 24, 546,  4, 27, 418,  4, 28, 262,  4, 31, 265,  4, 36, 204,  4, 40, 531,  4, 41, 585,
			4, 51, 230,  4, 53, 532,  4, 56, 527,  4, 60, 367,  4, 62, 435,  4, 64, 240,  4, 69, 358,  4, 74, 246,
			4, 76, 258,  4, 81, 264,  4, 83, 446,  4, 90, 511,  4, 91, 223,  4, 94, 476,  4, 96, 425,  4, 108, 561,
			4, 109, 572,  4, 112, 365,  4, 116, 243,  4, 119, 352,  4, 126, 353,  4, 133, 574,  4, 146, 224,  4, 147, 281,
			4, 150, 228,  4, 153, 336,  4, 156, 381,  4, 157, 422,  5, 1, 470,  5, 16, 524,  5, 21, 423,  5, 22, 248,
			5, 23, 480,  5, 24, 350,  5, 29, 503,  5, 30, 417,  5, 33, 519,  5, 34, 593,  5, 40, 553,  5, 46, 580,
			5, 47, 426,  5, 48, 570,  5, 50, 320,  5, 55, 241,  5, 62, 510,  5, 64, 415,  5, 71, 526,  5, 82, 595,
			5, 87, 449,  5, 88, 488,  5, 89, 274,  5, 106, 219,  5, 112, 411,  5, 113, 343,  5, 114, 539,  5, 122, 540,
			5, 123, 354,  5, 129, 253,  5, 133, 483,  5, 135, 289,  5, 139, 486,  5, 146, 523,  5, 156, 515,  6, 0, 485,
			6, 1, 582,  6, 15, 402,  6, 17, 245,  6, 31, 373,  6, 33, 487,  6, 41, 346,  6, 88, 324,  6, 101, 287,
			6, 107, 332,  6, 117, 356,  6, 121, 597,  6, 129, 409,  6, 131, 370,  6, 138, 215,  6, 142, 305,  6, 146, 345,
			7, 3, 311,  7, 4, 227,  7, 13, 222,  7, 19, 566,  7, 43, 314,  7, 51, 226,  7, 52, 452,  7, 66, 393,
			7, 68, 321,  7, 74, 211,  7, 76, 329,  7, 77, 455,  7, 89, 563,  7, 98, 318,  7, 101, 495,  7, 102, 492,
			7, 110, 432,  7, 114, 577,  7, 129, 419,  7, 131, 497,  7, 139, 589,  7, 153, 285,  7, 155, 277,  7, 157, 225,
			8, 13, 504,  8, 15, 434,  8, 48, 247,  8, 55, 500,  8, 105, 412,  8, 109, 461,  8, 113, 554,  8, 144, 303,
			8, 146, 513,  8, 153, 447,  9, 6, 391,  9, 7, 502,  9, 12, 280,  9, 43, 518,  9, 72, 340,  9, 74, 283,
			9, 80, 316,  9, 89, 600,  9, 92, 299,  9, 93, 530,  9, 95, 351,  9, 100, 269,  9, 107, 482,  9, 128, 232,
			9, 140, 361,  9, 144, 276,  9, 150, 255,  9, 151, 451,  9, 152, 201,  10, 4, 307,  10, 8, 525,  10, 11, 273,
			10, 15, 229,  10, 37, 508,  10, 40, 252,  10, 47, 266,  10, 65, 302,  10, 79, 467,  10, 102, 213,  10, 118, 342,
			10, 119, 306,  10, 123, 528,  10, 128, 537,  10, 131, 233,  10, 134, 348,  10, 152, 217,  10, 155, 293,  11, 6, 317,
			11, 15, 599,  11, 16, 308,  11, 57, 208,  11, 64, 301,  11, 79, 579,  11, 122, 441,  11, 124, 564,  11, 130, 300,
			11, 139, 494,  11, 155, 382,  12, 25, 331,  12, 46, 587,  12, 48, 322,  12, 74, 400,  12, 80, 464,  12, 84, 380,
			12, 86, 390,  12, 107, 556,  12, 114, 583,  12, 139, 383,  12, 154, 256,  12, 157, 214,  13, 33, 535,  13, 37, 251,
			13, 54, 319,  13, 104, 569,  13, 112, 369,  13, 116, 403,  13, 143, 290,  14, 19, 239,  14, 53, 506,  14, 74, 286,
			14, 87, 534,  14, 89, 216,  14, 100, 218,  14, 127, 571,  15, 15, 328,  15, 17, 254,  15, 20, 536,  15, 31, 443,
			15, 73, 463,  15, 84, 396,  15, 99, 427,  15, 105, 466,  15, 110, 376,  15, 116, 385,  15, 121, 366,  15, 124, 562,
			15, 137, 529,  16, 14, 421,  16, 32, 395,  16, 41, 315,  16, 42, 242,  16, 47, 548,  16, 49, 357,  16, 58, 394,
			16, 64, 498,  16, 84, 323,  16, 111, 456,  16, 133, 349,  17, 13, 491,  17, 57, 413,  17, 58, 484,  17, 59, 509,
			17, 99, 414,  17, 109, 330,  17, 116, 221,  18, 2, 325,  18, 17, 408,  18, 28, 337,  18, 29, 298,  18, 47, 584,
			18, 49, 379,  18, 51, 304,  18, 64, 384,  18, 66, 542,  18, 78, 297,  18, 88, 522,  18, 107, 507,  18, 126, 338,
			18, 128, 210,  18, 133, 279,  18, 148, 359,  18, 151, 296,  19, 14, 575,  19, 24, 344,  19, 66, 560,  19, 94, 212,
			19, 96, 375,  20, 12, 517,  20, 13, 516,  20, 47, 520,  20, 59, 410,  20, 68, 386,  20, 81, 310,  20, 88, 399,
			20, 119, 469,  20, 122, 424,  21, 18, 334,  21, 41, 428,  21, 61, 475,  21, 64, 437,  21, 67, 268,  21, 102, 234,
			21, 123, 512,  22, 14, 501,  22, 16, 416,  22, 17, 549,  22, 18, 471,  22, 51, 505,  22, 60, 581,  22, 62, 263,
			22, 140, 514,  23, 21, 220,  23, 39, 481,  23, 49, 275,  23, 88, 326,  23, 107, 392,  23, 112, 389,  23, 114, 431,
			23, 116, 288,  23, 123, 474,  23, 137, 588,  23, 145, 267,  23, 147, 444,  23, 154, 363,  24, 4, 521,  24, 12, 558,
			24, 49, 544,  24, 82, 453,  24, 137, 472,  24, 142, 543,  25, 119, 457,  25, 125, 378,  25, 129, 333,  25, 147, 377,
			26, 15, 257,  26, 16, 473,  26, 59, 347,  26, 64, 567,  26, 124, 203,  26, 144, 436,  26, 145, 552,  26, 152, 236,
			27, 39, 261,  27, 54, 260,  27, 101, 362,  27, 106, 489,  27, 111, 209,  27, 117, 372,  28, 111, 557,  28, 117, 313,
			28, 118, 406,  29, 102, 576,  29, 115, 270,  29, 150, 541,  30, 23, 206,  30, 82, 244,  30, 126, 205,  30, 139, 237,
			30, 142, 364,  30, 152, 374,  31, 25, 339,  31, 51, 404,  31, 82, 459,  31, 104, 250,  32, 32, 235,  32, 46, 259,
			32, 97, 478,  32, 120, 545,  33, 9, 499,  33, 127, 295,  34, 4, 496,  34, 149, 439,  34, 151, 341,  35, 80, 433,
			35, 82, 238,  35, 113, 294,  36, 55, 355,  36, 68, 398,  36, 123, 291,  36, 136, 401,  36, 156, 368,  37, 23, 388,
		};

		// _ReponseStub: 126x191 table, 600 non-zero cells.
		private static readonly int[] ReponseStubFrequencies =
		{
			0, 67, 584,  0, 69, 295,  0, 85, 416,  0, 88, 504,  0, 94, 251,  1, 65, 237,  1, 68, 195,  1, 70, 196,
			1, 86, 214,  2, 66, 71,  2, 70, 397,  2, 74, 511,  2, 77, 555,  2, 78, 91,  2, 88, 290,  3, 68, 149,
			3, 81, 563,  3, 83, 228,  3, 87, 255,  3, 88, 103,  3, 94, 503,  4, 86, 530,  4, 88, 127,  4, 91, 393,
			5, 75, 60,  5, 95, 327,  6, 65, 502,  6, 90, 398,  6, 96, 412,  8, 72, 473,  9, 88, 382,  9, 96, 572,
			10, 71, 270,  10, 82, 540,  11, 83, 252,  11, 84, 558,  11, 92, 80,  12, 85, 57,  12, 86, 170,  12, 91, 67,
			13, 40, 73,  13, 43, 383,  13, 44, 7,  13, 48, 123,  13, 72, 585,  13, 110, 277,  13, 131, 376,  14, 66, 483,
			14, 69, 135,  15, 156, 160,  15, 157, 452,  15, 159, 434,  15, 161, 1,  15, 165, 580,  15, 166, 99,  15, 168, 111,
			15, 171, 392,  15, 174, 65,  15, 175, 409,  15, 186, 581,  15, 189, 260,  16, 1, 63,  16, 31, 83,  16, 36, 410,
			16, 37, 51,  16, 48, 172,  16, 49, 40,  16, 57, 126,  16, 59, 338,  16, 113, 535,  16, 114, 268,  16, 116, 77,
			16, 121, 389,  16, 173, 114,  16, 180, 394,  17, 13, 61,  17, 20, 41,  17, 44, 168,  17, 117, 273,  17, 125, 367,
			17, 129, 378,  17, 141, 396,  18, 38, 38,  18, 45, 265,  18, 47, 18,  18, 77, 590,  18, 79, 536,  18, 91, 343,
			18, 155, 27,  18, 164, 142,  19, 4, 218,  19, 34, 88,  19, 40, 302,  19, 61, 497,  19, 109, 440,  20, 33, 26,
			20, 40, 257,  20, 44, 81,  20, 45, 109,  20, 70, 85,  20, 103, 24,  20, 108, 146,  20, 112, 112,  20, 119, 512,
			20, 123, 289,  20, 129, 456,  20, 133, 70,  20, 135, 307,  20, 190, 136,  21, 47, 267,  21, 103, 46,  21, 109, 206,
			21, 128, 15,  22, 41, 513,  22, 42, 150,  22, 44, 45,  22, 46, 143,  22, 49, 317,  22, 66, 324,  22, 99, 351,
			22, 102, 300,  22, 107, 301,  22, 112, 180,  22, 114, 328,  22, 116, 125,  22, 122, 320,  22, 124, 298,  22, 128, 199,
			22, 131, 97,  22, 136, 212,  22, 159, 469,  22, 167, 262,  22, 175, 222,  22, 182, 582,  22, 185, 304,  22, 189, 153,
			23, 4, 155,  23, 6, 119,  23, 11, 134,  23, 121, 93,  23, 132, 276,  23, 187, 369,  24, 2, 232,  24, 17, 226,
			24, 52, 487,  24, 54, 278,  24, 66, 478,  24, 165, 205,  25, 86, 62,  26, 15, 501,  26, 16, 437,  26, 22, 459,
			26, 28, 450,  26, 79, 139,  26, 102, 275,  26, 105, 173,  26, 106, 35,  26, 108, 53,  26, 149, 261,  26, 167, 311,
			26, 183, 325,  27, 5, 151,  27, 62, 380,  29, 79, 495,  29, 90, 25,  31, 39, 13,  31, 75, 498,  34, 71, 84,
			35, 0, 599,  35, 3, 246,  35, 5, 388,  35, 6, 596,  35, 7, 481,  35, 8, 595,  35, 9, 468,  35, 10, 408,
			35, 11, 319,  35, 12, 454,  35, 15, 458,  35, 17, 523,  35, 19, 518,  35, 20, 507,  35, 21, 566,  35, 23, 586,
			35, 27, 9,  35, 28, 549,  35, 29, 573,  35, 31, 538,  35, 36, 191,  35, 38, 431,  35, 40, 233,  35, 42, 588,
			35, 43, 460,  35, 44, 579,  35, 48, 550,  35, 51, 406,  35, 53, 471,  35, 54, 470,  35, 55, 506,  35, 58, 323,
			35, 62, 395,  35, 99, 597,  35, 100, 589,  35, 103, 559,  35, 109, 405,  35, 112, 448,  35, 117, 340,  35, 120, 292,
			35, 123, 288,  35, 125, 509,  35, 128, 496,  35, 129, 110,  35, 134, 414,  35, 141, 185,  35, 142, 341,  35, 143, 356,
			35, 145, 546,  35, 146, 279,  35, 147, 404,  35, 150, 419,  35, 159, 548,  35, 162, 524,  35, 164, 75,  35, 165, 527,
			35, 168, 519,  35, 169, 539,  35, 171, 477,  35, 177, 391,  35, 178, 313,  35, 180, 403,  35, 181, 174,  35, 183, 243,
			35, 185, 190,  35, 189, 345,  36, 0, 423,  36, 2, 130,  36, 4, 521,  36, 7, 101,  36, 8, 565,  36, 12, 591,
			36, 14, 411,  36, 15, 210,  36, 24, 575,  36, 26, 66,  36, 27, 415,  36, 28, 287,  36, 29, 147,  36, 31, 420,
			36, 34, 248,  36, 40, 560,  36, 41, 554,  36, 42, 74,  36, 49, 349,  36, 51, 364,  36, 52, 59,  36, 53, 532,
			36, 54, 193,  36, 56, 330,  36, 57, 39,  36, 60, 439,  36, 62, 494,  36, 65, 69,  36, 98, 166,  36, 102, 321,
			36, 106, 14,  36, 107, 401,  36, 108, 124,  36, 109, 426,  36, 114, 245,  36, 116, 447,  36, 123, 514,  36, 124, 202,
			36, 127, 463,  36, 128, 120,  36, 129, 499,  36, 141, 564,  36, 142, 522,  36, 145, 413,  36, 149, 368,  36, 152, 384,
			36, 155, 159,  36, 156, 104,  36, 159, 381,  36, 166, 303,  36, 179, 373,  36, 180, 334,  36, 186, 346,  36, 189, 407,
			36, 190, 472,  37, 0, 326,  37, 1, 427,  37, 16, 508,  37, 21, 335,  37, 22, 466,  37, 23, 462,  37, 24, 355,
			37, 25, 296,  37, 26, 551,  37, 29, 444,  37, 30, 482,  37, 33, 576,  37, 34, 593,  37, 40, 547,  37, 46, 543,
			37, 47, 553,  37, 48, 517,  37, 50, 305,  37, 51, 23,  37, 55, 372,  37, 59, 16,  37, 61, 417,  37, 62, 571,
			37, 97, 217,  37, 104, 534,  37, 108, 207,  37, 109, 177,  37, 115, 592,  37, 120, 435,  37, 121, 484,  37, 122, 363,
			37, 127, 223,  37, 138, 297,  37, 145, 505,  37, 147, 545,  37, 155, 562,  37, 156, 333,  37, 162, 306,  37, 166, 520,
			37, 167, 36,  37, 168, 428,  37, 172, 445,  37, 173, 390,  37, 179, 358,  37, 188, 162,  37, 189, 493,  38, 0, 531,
			38, 1, 577,  38, 3, 8,  38, 10, 108,  38, 12, 157,  38, 17, 348,  38, 25, 236,  38, 33, 68,  38, 41, 224,
			38, 54, 424,  38, 100, 138,  38, 121, 240,  38, 125, 118,  38, 140, 156,  38, 142, 179,  38, 147, 152,  38, 150, 271,
			38, 154, 594,  38, 158, 227,  38, 162, 486,  38, 164, 186,  38, 165, 29,  38, 175, 181,  38, 179, 357,  38, 181, 216,
			39, 2, 241,  39, 3, 282,  39, 4, 235,  39, 43, 100,  39, 51, 148,  39, 52, 352,  39, 101, 266,  39, 107, 441,
			39, 108, 92,  39, 109, 492,  39, 122, 568,  39, 125, 552,  39, 134, 533,  39, 135, 491,  39, 138, 339,  39, 143, 430,
			39, 163, 31,  39, 164, 475,  39, 168, 56,  39, 172, 583,  39, 174, 337,  39, 186, 200,  39, 188, 347,  40, 13, 578,
			40, 15, 213,  40, 16, 254,  40, 18, 379,  40, 29, 50,  40, 31, 21,  40, 37, 178,  40, 44, 198,  40, 55, 461,
			40, 56, 105,  40, 62, 30,  40, 69, 54,  40, 114, 133,  40, 115, 44,  40, 121, 264,  40, 128, 48,  40, 136, 121,
			40, 138, 386,  40, 142, 350,  40, 144, 176,  40, 146, 515,  40, 148, 132,  40, 155, 90,  40, 177, 354,  40, 182, 12,
			40, 186, 476,  41, 1, 314,  41, 7, 344,  41, 12, 312,  41, 26, 249,  41, 32, 94,  41, 37, 4,  41, 43, 485,
			41, 54, 256,  41, 105, 197,  41, 107, 331,  41, 122, 600,  41, 125, 315,  41, 126, 500,  41, 128, 291,  41, 133, 230,
			41, 140, 467,  41, 161, 272,  41, 172, 3,  41, 173, 371,  41, 177, 259,  41, 183, 359,  42, 3, 281,  42, 4, 284,
			42, 8, 421,  42, 11, 446,  42, 15, 253,  42, 37, 516,  42, 46, 309,  42, 62, 234,  42, 84, 561,  42, 112, 244,
			42, 132, 184,  42, 136, 98,  42, 151, 308,  42, 152, 310,  42, 156, 453,  42, 157, 82,  42, 164, 285,  42, 166, 10,
			42, 167, 293,  42, 185, 201,  42, 188, 286,  43, 6, 433,  43, 7, 43,  43, 15, 598,  43, 16, 55,  43, 49, 113,
			43, 57, 283,  43, 97, 377,  43, 99, 219,  43, 134, 229,  43, 137, 342,  43, 153, 42,  43, 155, 436,  43, 157, 528,
			43, 163, 188,  43, 172, 294,  43, 176, 72,  43, 188, 479,  44, 23, 96,  44, 25, 158,  44, 30, 221,  44, 46, 567,
			44, 107, 443,  44, 113, 510,  44, 114, 239,  44, 117, 175,  44, 122, 171,  44, 123, 163,  44, 139, 203,  44, 148, 247,
			44, 163, 115,  44, 172, 370,  44, 180, 79,  44, 190, 106,  45, 32, 20,  45, 33, 557,  45, 37, 189,  45, 41, 263,
			45, 103, 215,  45, 115, 165,  45, 134, 17,  45, 149, 154,  45, 154, 117,  45, 166, 128,  45, 184, 37,  45, 188, 432,
			46, 19, 318,  46, 36, 258,  46, 53, 425,  46, 61, 32,  46, 80, 209,  46, 107, 375,  46, 120, 183,  46, 122, 387,
			46, 160, 544,  46, 171, 49,  47, 31, 537,  47, 37, 28,  47, 51, 5,  47, 60, 269,  47, 106, 489,  47, 117, 353,
			47, 147, 192,  47, 178, 220,  48, 32, 362,  48, 47, 570,  48, 97, 465,  48, 117, 322,  48, 128, 87,  48, 144, 429,
			48, 159, 86,  48, 166, 316,  49, 13, 332,  49, 32, 238,  49, 58, 480,  49, 73, 242,  49, 116, 400,  49, 132, 438,
			50, 17, 442,  50, 49, 385,  50, 57, 145,  50, 97, 464,  50, 99, 542,  50, 122, 102,  50, 140, 451,  50, 159, 141,
			51, 14, 569,  51, 56, 52,  51, 99, 361,  51, 117, 161,  51, 127, 116,  51, 129, 365,  51, 164, 58,  51, 165, 2,
			52, 13, 541,  52, 59, 225,  52, 101, 455,  52, 155, 11,  53, 8, 329,  53, 22, 194,  53, 49, 231,  53, 112, 137,
			53, 130, 131,  54, 12, 122,  54, 14, 488,  54, 16, 360,  54, 17, 529,  54, 51, 449,  54, 147, 95,  55, 51, 89,
			55, 104, 144,  55, 145, 336,  55, 178, 274,  55, 180, 366,  56, 4, 574,  56, 115, 187,  57, 0, 22,  57, 158, 280,
			57, 162, 299,  57, 171, 107,  58, 157, 6,  59, 23, 19,  59, 54, 167,  59, 55, 208,  60, 64, 374,  61, 121, 34,
			61, 163, 76,  66, 82, 129,  69, 142, 182,  69, 147, 526,  72, 5, 402,  72, 15, 490,  72, 33, 422,  72, 114, 47,
			72, 179, 457,  73, 9, 164,  73, 30, 399,  75, 165, 211,  77, 32, 64,  78, 186, 474,  79, 158, 204,  84, 33, 78,
			84, 53, 169,  85, 131, 140,  89, 140, 33,  117, 95, 525,  118, 93, 418,  120, 75, 587,  121, 79, 556,  122, 81, 250,
		};

		// algoStub: 94x94 table, 399 non-zero cells.
		private static readonly int[] AlgoStubFrequencies =
		{
			35, 0, 596,  35, 6, 591,  35, 7, 468,  35, 8, 592,  35, 10, 371,  35, 15, 460,  35, 17, 438,  35, 19, 462,
			35, 20, 490,  35, 21, 559,  35, 23, 578,  35, 29, 573,  35, 40, 271,  35, 42, 594,  35, 43, 458,  35, 44, 538,
			35, 48, 551,  35, 51, 312,  35, 53, 547,  35, 54, 442,  35, 55, 448,  35, 62, 429,  35, 65, 598,  35, 66, 590,
			35, 69, 550,  35, 75, 292,  35, 78, 479,  35, 83, 335,  35, 86, 231,  35, 88, 360,  35, 91, 407,  36, 0, 454,
			36, 6, 284,  36, 13, 430,  36, 14, 405,  36, 15, 272,  36, 17, 555,  36, 19, 445,  36, 22, 420,  36, 31, 533,
			36, 34, 450,  36, 36, 207,  36, 37, 568,  36, 40, 493,  36, 41, 477,  36, 43, 387,  36, 49, 465,  36, 52, 397,
			36, 57, 249,  36, 61, 282,  36, 63, 278,  36, 65, 309,  36, 67, 440,  36, 71, 586,  36, 75, 565,  36, 77, 327,
			36, 82, 202,  36, 87, 546,  36, 90, 418,  36, 91, 262,  37, 0, 265,  37, 5, 204,  37, 9, 531,  37, 10, 585,
			37, 20, 230,  37, 22, 532,  37, 25, 527,  37, 29, 367,  37, 31, 435,  37, 32, 240,  37, 37, 358,  37, 42, 246,
			37, 44, 258,  37, 49, 264,  37, 51, 446,  37, 58, 511,  37, 59, 223,  37, 62, 476,  37, 64, 425,  37, 76, 561,
			37, 77, 572,  37, 80, 365,  37, 84, 243,  37, 87, 352,  38, 0, 353,  38, 7, 574,  38, 20, 224,  38, 21, 281,
			38, 24, 228,  38, 27, 336,  38, 30, 381,  38, 31, 422,  38, 33, 470,  38, 48, 524,  38, 53, 423,  38, 54, 248,
			38, 55, 480,  38, 56, 350,  38, 61, 503,  38, 62, 417,  38, 65, 519,  38, 66, 593,  38, 72, 553,  38, 78, 580,
			38, 79, 426,  38, 80, 570,  38, 82, 320,  38, 87, 241,  39, 0, 510,  39, 1, 415,  39, 8, 526,  39, 19, 595,
			39, 24, 449,  39, 25, 488,  39, 26, 274,  39, 43, 219,  39, 49, 411,  39, 50, 343,  39, 51, 539,  39, 59, 540,
			39, 60, 354,  39, 66, 253,  39, 70, 483,  39, 72, 289,  39, 76, 486,  39, 83, 523,  39, 93, 515,  40, 1, 485,
			40, 2, 582,  40, 16, 402,  40, 18, 245,  40, 32, 373,  40, 34, 487,  40, 42, 346,  40, 88, 324,  41, 7, 287,
			41, 13, 332,  41, 23, 356,  41, 27, 597,  41, 35, 409,  41, 37, 370,  41, 44, 215,  41, 48, 305,  41, 52, 345,
			41, 67, 311,  41, 68, 227,  41, 77, 222,  41, 83, 566,  42, 13, 314,  42, 21, 226,  42, 22, 452,  42, 35, 393,
			42, 37, 321,  42, 43, 211,  42, 45, 329,  42, 46, 455,  42, 58, 563,  42, 67, 318,  42, 70, 495,  42, 71, 492,
			42, 79, 432,  42, 83, 577,  43, 4, 419,  43, 6, 497,  43, 14, 589,  43, 28, 285,  43, 30, 277,  43, 32, 225,
			43, 46, 504,  43, 48, 434,  43, 81, 247,  43, 88, 500,  44, 43, 412,  44, 47, 461,  44, 51, 554,  44, 82, 303,
			44, 84, 513,  44, 91, 447,  45, 8, 391,  45, 9, 502,  45, 14, 280,  45, 45, 518,  45, 73, 340,  45, 75, 283,
			45, 81, 316,  45, 93, 299,  46, 0, 530,  46, 2, 351,  46, 7, 269,  46, 14, 482,  46, 35, 232,  46, 47, 361,
			46, 51, 276,  46, 57, 255,  46, 58, 451,  46, 59, 201,  46, 69, 307,  46, 73, 525,  46, 76, 273,  46, 80, 229,
			47, 8, 508,  47, 11, 252,  47, 18, 266,  47, 35, 302,  47, 49, 467,  47, 72, 213,  47, 88, 342,  47, 89, 306,
			47, 93, 528,  48, 4, 537,  48, 7, 233,  48, 10, 348,  48, 28, 217,  48, 31, 293,  48, 40, 317,  48, 49, 599,
			48, 50, 308,  48, 91, 208,  49, 3, 301,  49, 18, 579,  49, 61, 441,  49, 63, 564,  49, 69, 300,  49, 78, 494,
			50, 0, 382,  50, 27, 331,  50, 48, 587,  50, 50, 322,  50, 75, 400,  50, 81, 464,  50, 85, 380,  50, 87, 390,
			51, 14, 556,  51, 21, 583,  51, 46, 383,  51, 61, 256,  51, 64, 214,  52, 4, 535,  52, 8, 251,  52, 25, 319,
			52, 74, 569,  52, 82, 369,  52, 86, 403,  53, 19, 290,  53, 53, 239,  53, 87, 506,  54, 13, 286,  54, 26, 534,
			54, 28, 216,  54, 39, 218,  54, 67, 571,  55, 19, 328,  55, 21, 254,  55, 24, 536,  55, 35, 443,  55, 76, 463,
			55, 87, 396,  56, 8, 427,  56, 14, 466,  56, 19, 376,  56, 25, 385,  56, 30, 366,  56, 33, 562,  56, 46, 529,
			56, 81, 421,  57, 5, 395,  57, 14, 315,  57, 15, 242,  57, 20, 548,  57, 22, 357,  57, 31, 394,  57, 36, 498,
			57, 56, 323,  57, 83, 456,  58, 11, 349,  58, 49, 491,  58, 93, 413,  59, 0, 484,  59, 1, 509,  59, 40, 414,
			59, 50, 330,  59, 57, 221,  60, 7, 325,  60, 22, 408,  60, 33, 337,  60, 34, 298,  60, 52, 584,  60, 54, 379,
			60, 56, 304,  60, 68, 384,  60, 70, 542,  60, 82, 297,  60, 92, 522,  61, 17, 507,  61, 36, 338,  61, 38, 210,
			61, 43, 279,  61, 58, 359,  61, 61, 296,  61, 82, 575,  61, 92, 344,  62, 39, 560,  62, 67, 212,  62, 69, 375,
			63, 49, 517,  63, 50, 516,  63, 84, 520,  64, 2, 410,  64, 10, 386,  64, 23, 310,  64, 30, 399,  64, 61, 469,
			64, 64, 424,  65, 24, 334,  65, 47, 428,  65, 67, 475,  65, 69, 437,  65, 72, 268,  66, 13, 234,  66, 34, 512,
			66, 83, 501,  66, 85, 416,  66, 86, 549,  66, 87, 471,  67, 26, 505,  67, 35, 581,  67, 37, 263,  68, 20, 514,
			68, 59, 220,  68, 77, 481,  68, 87, 275,  69, 31, 326,  69, 50, 392,  69, 55, 389,  69, 57, 431,  69, 59, 288,
			69, 66, 474,  69, 80, 588,  69, 88, 267,  69, 90, 444,  70, 3, 363,  70, 11, 521,  70, 19, 558,  70, 56, 544,
			70, 88, 453,  71, 49, 472,  71, 54, 543,  73, 0, 457,  73, 6, 378,  73, 10, 333,  73, 28, 377,  73, 54, 257,
			73, 55, 473,  74, 4, 347,  74, 8, 567,  74, 69, 203,  74, 89, 436,  74, 90, 552,  75, 3, 236,  75, 48, 261,
			75, 63, 260,  76, 15, 362,  76, 20, 489,  76, 25, 209,  76, 31, 372,  77, 88, 557,  78, 0, 313,  78, 1, 406,
			79, 47, 576,  79, 60, 270,  80, 1, 541,  80, 32, 206,  80, 90, 244,  81, 40, 205,  81, 53, 237,  81, 56, 364,
			81, 66, 374,  82, 3, 339,  82, 29, 404,  82, 59, 459,  82, 81, 250,  83, 73, 235,  83, 87, 259,  84, 44, 478,
			84, 67, 545,  85, 20, 499,  86, 42, 295,  86, 77, 496,  88, 34, 439,  88, 36, 341,  89, 27, 433,  89, 29, 238,
			89, 60, 294,  90, 66, 355,  90, 78, 398,  91, 39, 291,  91, 52, 401,  91, 72, 368,  92, 3, 388,
		};

		// _RecordStub: 94x94 table, 600 non-zero cells.
		private static readonly int[] RecordStubFrequencies =
		{
			3, 3, 597,  3, 8, 568,  3, 16, 355,  3, 17, 553,  15, 0, 589,  15, 1, 432,  15, 2, 460,  15, 7, 422,
			15, 8, 361,  15, 9, 77,  15, 12, 319,  15, 13, 243,  15, 15, 426,  15, 17, 57,  15, 18, 499,  15, 19, 300,
			15, 36, 492,  15, 37, 75,  15, 38, 419,  15, 40, 363,  15, 42, 368,  15, 43, 234,  15, 44, 564,  15, 51, 569,
			15, 57, 494,  15, 59, 152,  15, 60, 250,  15, 62, 120,  15, 64, 439,  15, 69, 477,  15, 71, 495,  15, 76, 587,
			15, 77, 168,  15, 78, 54,  15, 79, 208,  15, 80, 183,  15, 85, 109,  15, 86, 386,  15, 87, 510,  15, 89, 542,
			15, 91, 470,  16, 3, 382,  16, 5, 9,  16, 10, 156,  16, 18, 435,  16, 23, 539,  16, 24, 497,  16, 25, 353,
			16, 27, 415,  16, 34, 91,  16, 38, 371,  16, 44, 195,  16, 51, 133,  16, 54, 588,  16, 55, 159,  16, 56, 264,
			16, 58, 189,  16, 60, 491,  16, 61, 286,  16, 65, 579,  16, 67, 63,  16, 69, 360,  16, 71, 367,  16, 76, 131,
			16, 77, 538,  16, 80, 235,  16, 83, 102,  16, 84, 17,  16, 89, 203,  17, 7, 153,  17, 17, 366,  17, 30, 124,
			17, 31, 144,  17, 35, 12,  17, 40, 295,  17, 56, 289,  17, 61, 147,  17, 77, 13,  17, 83, 128,  17, 86, 78,
			17, 87, 119,  17, 90, 15,  18, 0, 272,  18, 1, 293,  18, 9, 583,  18, 12, 258,  18, 14, 462,  18, 17, 529,
			18, 18, 27,  18, 26, 545,  18, 40, 14,  18, 41, 420,  18, 48, 202,  18, 53, 88,  18, 54, 448,  18, 60, 58,
			18, 63, 431,  18, 64, 141,  18, 65, 375,  18, 70, 150,  18, 74, 395,  18, 76, 110,  18, 77, 223,  18, 79, 194,
			18, 83, 146,  18, 84, 364,  18, 89, 35,  19, 8, 344,  19, 10, 430,  19, 25, 96,  19, 31, 333,  19, 33, 595,
			19, 34, 383,  19, 40, 291,  19, 41, 52,  19, 46, 580,  19, 51, 417,  19, 56, 599,  19, 57, 107,  19, 59, 450,
			19, 60, 7,  19, 61, 476,  19, 66, 267,  19, 67, 380,  19, 70, 484,  19, 74, 555,  19, 84, 537,  19, 85, 137,
			19, 87, 89,  20, 3, 83,  20, 4, 507,  20, 7, 45,  20, 20, 572,  20, 21, 240,  20, 22, 427,  20, 24, 436,
			20, 28, 228,  20, 30, 520,  20, 38, 552,  20, 45, 487,  20, 46, 283,  20, 48, 125,  20, 49, 55,  20, 52, 47,
			20, 57, 216,  20, 61, 41,  20, 68, 465,  20, 69, 276,  20, 70, 414,  20, 71, 190,  20, 72, 570,  20, 76, 306,
			20, 77, 356,  20, 79, 411,  20, 90, 345,  20, 93, 39,  21, 4, 113,  21, 6, 472,  21, 15, 197,  21, 17, 229,
			21, 18, 246,  21, 26, 370,  21, 38, 334,  21, 39, 50,  21, 56, 199,  21, 62, 205,  21, 63, 34,  21, 69, 179,
			21, 82, 551,  21, 83, 280,  21, 84, 230,  21, 86, 456,  21, 87, 21,  21, 90, 437,  21, 93, 257,  22, 0, 463,
			22, 2, 84,  22, 4, 130,  22, 14, 518,  22, 16, 326,  22, 18, 372,  22, 19, 311,  22, 23, 434,  22, 24, 253,
			22, 32, 451,  22, 33, 413,  22, 34, 173,  22, 35, 42,  22, 37, 215,  22, 40, 180,  22, 41, 86,  22, 45, 581,
			22, 46, 325,  22, 47, 312,  22, 50, 273,  22, 52, 33,  22, 64, 271,  22, 70, 269,  22, 74, 158,  22, 88, 231,
			23, 1, 3,  23, 2, 534,  23, 4, 4,  23, 5, 578,  23, 6, 410,  23, 13, 585,  23, 15, 297,  23, 17, 303,
			23, 18, 70,  23, 21, 530,  23, 22, 346,  23, 23, 558,  23, 24, 377,  23, 26, 511,  23, 30, 165,  23, 32, 392,
			23, 33, 302,  23, 34, 56,  23, 35, 10,  23, 36, 391,  23, 37, 117,  23, 50, 393,  23, 51, 217,  23, 52, 11,
			23, 53, 95,  23, 55, 106,  23, 58, 8,  23, 61, 161,  23, 70, 479,  23, 72, 536,  23, 76, 459,  23, 77, 394,
			23, 79, 515,  23, 80, 329,  23, 83, 143,  23, 85, 249,  23, 86, 145,  23, 87, 403,  24, 10, 544,  24, 13, 504,
			24, 14, 53,  24, 15, 483,  24, 43, 522,  24, 45, 490,  24, 46, 127,  24, 47, 290,  24, 55, 60,  24, 56, 514,
			24, 57, 474,  24, 58, 268,  24, 60, 424,  24, 61, 396,  24, 62, 455,  24, 63, 164,  24, 65, 32,  24, 66, 256,
			24, 69, 508,  24, 71, 365,  24, 72, 379,  24, 85, 409,  24, 87, 488,  24, 89, 323,  24, 91, 171,  24, 92, 337,
			25, 2, 174,  25, 13, 193,  25, 14, 348,  25, 15, 282,  25, 19, 376,  25, 23, 565,  25, 24, 328,  25, 26, 236,
			25, 31, 16,  25, 45, 547,  25, 47, 464,  25, 49, 429,  25, 50, 26,  25, 73, 18,  25, 76, 138,  25, 80, 501,
			25, 90, 254,  25, 91, 281,  26, 0, 310,  26, 8, 67,  26, 50, 157,  26, 57, 284,  26, 70, 577,  26, 73, 412,
			26, 75, 349,  26, 78, 285,  26, 82, 543,  26, 83, 22,  26, 84, 438,  26, 85, 178,  26, 92, 482,  26, 93, 381,
			27, 12, 575,  27, 13, 442,  27, 16, 469,  27, 18, 404,  27, 22, 132,  27, 23, 151,  27, 25, 478,  27, 27, 554,
			27, 29, 105,  27, 33, 219,  27, 43, 140,  27, 49, 559,  27, 50, 506,  27, 52, 373,  27, 57, 2,  27, 58, 318,
			27, 71, 20,  27, 85, 566,  27, 86, 72,  27, 87, 354,  27, 89, 340,  27, 90, 255,  27, 92, 38,  28, 11, 122,
			28, 16, 37,  28, 19, 80,  28, 25, 550,  28, 27, 209,  28, 28, 172,  28, 30, 121,  28, 31, 540,  28, 33, 244,
			28, 34, 584,  28, 35, 443,  28, 36, 541,  28, 38, 516,  28, 40, 421,  28, 41, 339,  28, 43, 94,  28, 44, 338,
			28, 45, 299,  28, 64, 176,  29, 17, 378,  29, 20, 25,  29, 23, 186,  29, 29, 440,  29, 30, 187,  29, 37, 582,
			29, 38, 321,  29, 39, 517,  29, 40, 169,  29, 41, 466,  29, 42, 468,  29, 46, 104,  29, 47, 192,  29, 49, 560,
			29, 52, 317,  29, 53, 313,  29, 54, 87,  29, 62, 527,  29, 63, 513,  29, 70, 274,  29, 72, 44,  29, 77, 596,
			29, 78, 298,  29, 79, 359,  29, 82, 457,  29, 85, 263,  29, 86, 389,  29, 87, 526,  29, 88, 407,  29, 89, 593,
			29, 90, 79,  30, 0, 594,  30, 8, 503,  30, 9, 305,  30, 11, 493,  30, 12, 350,  30, 15, 48,  30, 20, 533,
			30, 22, 116,  30, 24, 561,  30, 29, 81,  30, 31, 571,  30, 32, 6,  30, 33, 304,  30, 34, 277,  30, 41, 166,
			30, 44, 471,  30, 46, 287,  30, 52, 322,  30, 53, 163,  30, 59, 266,  30, 67, 574,  30, 68, 59,  30, 74, 509,
			30, 75, 505,  30, 76, 241,  30, 77, 279,  30, 78, 374,  30, 81, 292,  30, 83, 294,  30, 85, 184,  30, 87, 528,
			30, 88, 399,  30, 93, 123,  31, 6, 502,  31, 8, 31,  31, 14, 433,  31, 15, 262,  31, 17, 112,  31, 23, 548,
			31, 25, 586,  31, 26, 592,  31, 28, 461,  31, 31, 177,  31, 38, 591,  31, 43, 600,  31, 44, 93,  31, 45, 512,
			31, 46, 556,  31, 47, 108,  31, 49, 30,  31, 50, 387,  31, 51, 425,  31, 53, 576,  31, 55, 51,  31, 57, 573,
			31, 58, 475,  31, 59, 441,  31, 60, 40,  31, 62, 362,  31, 64, 454,  31, 65, 314,  31, 68, 562,  31, 70, 423,
			31, 78, 62,  31, 89, 496,  31, 90, 486,  31, 91, 557,  31, 92, 357,  32, 0, 418,  32, 1, 227,  32, 3, 563,
			32, 5, 549,  32, 21, 485,  32, 22, 242,  32, 23, 61,  32, 26, 347,  32, 29, 524,  32, 32, 405,  32, 42, 139,
			32, 49, 397,  32, 53, 567,  32, 54, 220,  32, 55, 238,  32, 56, 149,  32, 62, 444,  32, 66, 115,  32, 77, 100,
			32, 78, 182,  32, 80, 167,  32, 81, 118,  32, 84, 211,  32, 85, 590,  32, 86, 452,  32, 87, 546,  32, 89, 237,
			32, 91, 19,  32, 92, 398,  32, 93, 218,  33, 0, 103,  33, 4, 212,  33, 5, 296,  33, 15, 126,  33, 28, 64,
			33, 30, 29,  33, 41, 331,  33, 48, 66,  33, 74, 170,  33, 77, 73,  33, 78, 114,  33, 86, 467,  33, 87, 226,
			33, 90, 251,  33, 91, 324,  34, 1, 248,  34, 2, 342,  34, 3, 210,  34, 4, 245,  34, 18, 449,  34, 19, 98,
			34, 20, 369,  34, 21, 207,  34, 26, 384,  34, 27, 401,  34, 41, 315,  34, 48, 198,  34, 53, 525,  34, 62, 416,
			34, 63, 155,  34, 65, 336,  34, 69, 206,  34, 74, 221,  34, 87, 69,  34, 93, 185,  35, 0, 523,  35, 1, 90,
			35, 2, 142,  35, 4, 308,  35, 6, 402,  35, 7, 134,  35, 9, 49,  35, 10, 275,  35, 14, 154,  35, 30, 278,
			35, 35, 445,  35, 40, 74,  35, 48, 24,  35, 57, 188,  35, 71, 46,  36, 8, 500,  36, 15, 388,  36, 23, 446,
			36, 24, 204,  36, 25, 99,  36, 26, 36,  36, 33, 352,  36, 34, 92,  36, 44, 521,  36, 54, 307,  36, 57, 428,
			36, 58, 200,  36, 67, 252,  36, 74, 473,  36, 79, 71,  36, 84, 261,  37, 13, 481,  37, 14, 265,  37, 15, 28,
			37, 17, 327,  37, 27, 160,  37, 35, 489,  37, 38, 316,  37, 39, 332,  37, 47, 224,  37, 58, 222,  37, 76, 351,
			37, 77, 43,  37, 81, 136,  37, 82, 181,  37, 86, 309,  37, 87, 148,  38, 4, 330,  38, 13, 175,  38, 15, 201,
			38, 18, 85,  38, 26, 453,  38, 32, 458,  38, 34, 5,  38, 38, 390,  38, 41, 343,  38, 46, 598,  38, 47, 532,
			38, 48, 535,  38, 49, 97,  38, 51, 408,  38, 52, 301,  38, 54, 233,  38, 55, 406,  38, 63, 447,  38, 65, 270,
			38, 66, 239,  38, 71, 225,  38, 75, 23,  38, 83, 135,  38, 84, 129,  38, 85, 320,  38, 88, 191,  38, 91, 358,
			39, 2, 480,  39, 3, 196,  39, 4, 341,  39, 10, 214,  39, 12, 531,  39, 13, 385,  39, 14, 335,  39, 15, 260,
			39, 17, 213,  39, 23, 498,  39, 24, 68,  39, 30, 65,  39, 35, 400,  39, 37, 247,  39, 38, 1,  39, 61, 111,
			39, 68, 232,  39, 69, 82,  39, 70, 162,  39, 73, 76,  39, 78, 101,  39, 80, 288,  39, 86, 519,  39, 90, 259,
		};

		// eventStub: 94x94 table, 600 non-zero cells.
		private static readonly int[] EventStubFrequencies =
		{
			0, 24, 231,  0, 27, 581,  3, 1, 568,  3, 3, 598,  3, 5, 587,  3, 7, 561,  3, 9, 483,  3, 10, 586,
			3, 11, 592,  3, 12, 559,  3, 13, 144,  3, 14, 577,  3, 15, 245,  3, 16, 567,  3, 17, 434,  3, 18, 580,
			3, 19, 249,  3, 20, 557,  3, 22, 579,  3, 23, 462,  3, 24, 597,  3, 25, 353,  3, 26, 494,  3, 28, 554,
			3, 30, 596,  3, 31, 589,  3, 32, 528,  3, 34, 503,  3, 35, 566,  3, 36, 62,  3, 37, 591,  3, 38, 590,
			3, 39, 593,  3, 40, 565,  3, 41, 588,  3, 42, 595,  3, 43, 328,  3, 44, 243,  3, 45, 599,  3, 46, 594,
			3, 47, 487,  3, 49, 136,  3, 50, 324,  3, 53, 412,  3, 55, 439,  3, 56, 458,  3, 58, 416,  3, 61, 578,
			3, 62, 508,  3, 63, 521,  3, 64, 571,  3, 65, 584,  3, 66, 21,  3, 67, 550,  3, 71, 560,  3, 72, 583,
			3, 73, 572,  3, 74, 600,  3, 75, 585,  3, 76, 446,  3, 78, 519,  3, 82, 552,  4, 1, 547,  4, 2, 148,
			4, 3, 544,  4, 5, 345,  4, 6, 86,  4, 7, 292,  4, 9, 293,  4, 10, 502,  4, 11, 96,  4, 12, 358,
			4, 14, 525,  4, 15, 370,  4, 16, 120,  4, 18, 413,  4, 19, 49,  4, 20, 366,  4, 22, 492,  4, 23, 463,
			4, 24, 563,  4, 25, 10,  4, 26, 308,  4, 28, 320,  4, 30, 478,  4, 31, 258,  4, 32, 406,  4, 34, 512,
			4, 35, 145,  4, 37, 409,  4, 38, 297,  4, 39, 556,  4, 40, 491,  4, 41, 331,  4, 42, 340,  4, 44, 179,
			4, 46, 80,  4, 47, 414,  4, 48, 287,  4, 50, 404,  4, 51, 147,  4, 52, 432,  4, 53, 333,  4, 54, 454,
			4, 56, 204,  4, 57, 6,  4, 58, 67,  4, 59, 180,  4, 60, 186,  4, 61, 424,  4, 62, 274,  4, 63, 408,
			4, 64, 443,  4, 65, 151,  4, 66, 257,  4, 68, 318,  4, 70, 137,  4, 72, 543,  4, 73, 523,  4, 74, 564,
			4, 75, 445,  4, 76, 485,  4, 78, 193,  4, 82, 575,  15, 33, 378,  15, 37, 295,  15, 41, 400,  15, 43, 210,
			15, 48, 352,  15, 52, 441,  15, 59, 12,  15, 66, 157,  15, 68, 63,  15, 69, 187,  15, 71, 79,  15, 72, 118,
			15, 75, 558,  15, 86, 511,  15, 89, 303,  16, 0, 388,  16, 30, 254,  16, 35, 183,  16, 38, 71,  16, 48, 36,
			16, 49, 119,  16, 62, 476,  16, 70, 173,  16, 72, 228,  16, 93, 227,  17, 3, 44,  17, 11, 58,  17, 15, 39,
			17, 27, 459,  17, 28, 467,  17, 30, 99,  17, 32, 256,  17, 34, 396,  17, 35, 171,  17, 39, 429,  17, 43, 237,
			17, 60, 5,  17, 64, 203,  17, 71, 317,  17, 80, 574,  17, 81, 337,  17, 82, 499,  17, 93, 371,  18, 3, 322,
			18, 5, 376,  18, 10, 490,  18, 15, 464,  18, 17, 130,  18, 37, 250,  18, 41, 220,  18, 45, 241,  18, 52, 31,
			18, 54, 244,  18, 55, 496,  18, 57, 128,  18, 58, 78,  18, 67, 167,  18, 71, 344,  18, 83, 40,  19, 16, 363,
			19, 19, 111,  19, 21, 280,  19, 35, 64,  19, 37, 74,  19, 48, 28,  19, 53, 527,  19, 55, 479,  19, 57, 13,
			19, 58, 81,  19, 74, 230,  19, 78, 57,  19, 79, 259,  19, 91, 423,  20, 0, 420,  20, 3, 436,  20, 11, 140,
			20, 12, 377,  20, 14, 132,  20, 26, 69,  20, 30, 198,  20, 32, 214,  20, 35, 536,  20, 59, 135,  20, 61, 185,
			20, 64, 278,  20, 68, 189,  20, 69, 122,  20, 77, 41,  20, 82, 312,  20, 93, 465,  21, 0, 172,  21, 5, 364,
			21, 7, 410,  21, 12, 134,  21, 14, 418,  21, 20, 369,  21, 39, 517,  21, 40, 285,  21, 42, 9,  21, 64, 319,
			21, 65, 506,  21, 67, 170,  21, 71, 399,  21, 84, 197,  22, 18, 323,  22, 23, 276,  22, 32, 90,  22, 41, 50,
			22, 47, 452,  22, 54, 282,  22, 56, 108,  22, 65, 3,  22, 71, 472,  22, 74, 354,  22, 77, 539,  22, 78, 296,
			22, 89, 209,  23, 0, 202,  23, 1, 402,  23, 5, 164,  23, 8, 389,  23, 10, 522,  23, 18, 88,  23, 20, 329,
			23, 21, 368,  23, 25, 235,  23, 28, 450,  23, 31, 456,  23, 33, 112,  23, 35, 8,  23, 61, 38,  23, 64, 362,
			23, 68, 530,  23, 75, 233,  23, 77, 146,  23, 81, 341,  23, 87, 419,  23, 92, 232,  23, 93, 338,  24, 4, 47,
			24, 8, 225,  24, 12, 239,  24, 26, 252,  24, 28, 166,  24, 44, 307,  24, 51, 542,  24, 65, 486,  24, 70, 534,
			24, 79, 327,  24, 80, 576,  24, 93, 302,  25, 2, 495,  25, 19, 4,  25, 24, 20,  25, 25, 336,  25, 37, 200,
			25, 38, 457,  25, 47, 224,  25, 48, 349,  25, 58, 206,  25, 60, 330,  25, 62, 288,  25, 77, 442,  25, 81, 275,
			25, 85, 294,  26, 15, 395,  26, 17, 298,  26, 18, 415,  26, 25, 394,  26, 26, 143,  26, 35, 117,  26, 36, 27,
			26, 39, 283,  26, 45, 77,  26, 46, 253,  26, 48, 23,  26, 49, 488,  26, 51, 504,  26, 54, 386,  26, 55, 365,
			26, 56, 359,  26, 59, 162,  26, 62, 219,  26, 63, 277,  26, 64, 461,  26, 67, 255,  26, 74, 127,  26, 77, 91,
			26, 80, 346,  26, 85, 546,  26, 92, 411,  26, 93, 548,  27, 0, 316,  27, 2, 430,  27, 7, 266,  27, 10, 538,
			27, 15, 262,  27, 25, 26,  27, 32, 105,  27, 33, 477,  27, 43, 19,  27, 49, 540,  27, 51, 541,  27, 53, 291,
			27, 66, 1,  27, 70, 473,  27, 71, 433,  27, 72, 16,  27, 73, 497,  27, 78, 18,  27, 82, 437,  27, 84, 390,
			27, 92, 149,  28, 2, 161,  28, 9, 236,  28, 15, 22,  28, 23, 299,  28, 26, 223,  28, 28, 286,  28, 36, 332,
			28, 47, 551,  28, 48, 155,  28, 49, 93,  28, 63, 55,  28, 72, 367,  28, 73, 405,  28, 80, 387,  28, 84, 92,
			28, 86, 375,  29, 0, 343,  29, 5, 83,  29, 13, 417,  29, 14, 196,  29, 29, 11,  29, 34, 153,  29, 41, 326,
			29, 57, 211,  29, 60, 97,  29, 61, 54,  29, 68, 537,  29, 71, 102,  29, 75, 505,  29, 78, 192,  29, 79, 271,
			29, 81, 123,  29, 84, 191,  30, 5, 195,  30, 8, 177,  30, 13, 314,  30, 18, 65,  30, 19, 374,  30, 22, 501,
			30, 30, 205,  30, 31, 150,  30, 37, 68,  30, 39, 188,  30, 41, 382,  30, 44, 573,  30, 68, 304,  30, 83, 325,
			31, 3, 392,  31, 8, 428,  31, 9, 247,  31, 12, 447,  31, 13, 422,  31, 14, 545,  31, 20, 384,  31, 23, 532,
			31, 27, 107,  31, 28, 46,  31, 29, 242,  31, 38, 260,  31, 41, 110,  31, 47, 174,  31, 57, 269,  31, 62, 383,
			31, 65, 163,  31, 71, 348,  31, 77, 393,  31, 78, 470,  31, 93, 175,  32, 9, 489,  32, 14, 116,  32, 15, 529,
			32, 18, 106,  32, 19, 468,  32, 39, 356,  32, 41, 48,  32, 58, 101,  32, 64, 87,  32, 71, 221,  32, 73, 507,
			32, 76, 397,  32, 86, 190,  32, 92, 268,  33, 1, 7,  33, 3, 53,  33, 5, 281,  33, 12, 152,  33, 17, 89,
			33, 18, 385,  33, 27, 261,  33, 30, 381,  33, 38, 311,  33, 45, 471,  33, 47, 518,  33, 51, 85,  33, 53, 133,
			33, 63, 51,  33, 65, 73,  33, 68, 481,  33, 69, 248,  33, 70, 569,  33, 71, 357,  33, 73, 440,  33, 79, 114,
			33, 83, 208,  34, 10, 75,  34, 19, 42,  34, 35, 334,  34, 38, 156,  34, 42, 263,  34, 43, 213,  34, 45, 309,
			34, 46, 516,  34, 53, 215,  34, 68, 124,  34, 69, 555,  35, 2, 126,  35, 4, 113,  35, 10, 251,  35, 13, 199,
			35, 19, 453,  35, 24, 553,  35, 29, 265,  35, 40, 61,  35, 43, 455,  35, 66, 30,  35, 73, 500,  35, 82, 238,
			36, 9, 524,  36, 23, 129,  36, 24, 165,  36, 29, 168,  36, 31, 372,  36, 32, 60,  36, 35, 475,  36, 36, 267,
			36, 46, 70,  36, 51, 407,  36, 56, 444,  36, 57, 182,  36, 61, 533,  36, 70, 313,  36, 73, 284,  36, 75, 515,
			36, 85, 448,  36, 92, 401,  37, 2, 194,  37, 11, 56,  37, 14, 59,  37, 15, 474,  37, 16, 549,  37, 18, 131,
			37, 26, 360,  37, 31, 273,  37, 34, 264,  37, 39, 104,  37, 65, 509,  37, 77, 154,  37, 80, 158,  37, 82, 379,
			37, 91, 582,  37, 93, 513,  38, 3, 300,  38, 6, 305,  38, 14, 570,  38, 15, 98,  38, 28, 240,  38, 31, 95,
			38, 40, 355,  38, 58, 125,  38, 67, 306,  39, 14, 520,  39, 28, 315,  39, 29, 290,  39, 30, 321,  39, 53, 141,
			39, 69, 160,  39, 78, 35,  39, 80, 181,  39, 86, 121,  39, 93, 29,  40, 11, 217,  40, 27, 37,  40, 28, 466,
			40, 29, 15,  40, 33, 139,  40, 41, 301,  40, 51, 435,  40, 52, 272,  40, 54, 178,  40, 59, 361,  40, 72, 32,
			40, 79, 94,  40, 83, 526,  40, 90, 115,  40, 91, 33,  41, 0, 14,  41, 9, 350,  41, 11, 535,  41, 23, 335,
			41, 24, 218,  41, 30, 398,  41, 37, 482,  41, 43, 289,  41, 48, 270,  41, 52, 52,  41, 53, 34,  41, 60, 403,
			41, 67, 184,  41, 82, 351,  41, 91, 212,  41, 92, 514,  42, 0, 451,  42, 11, 103,  42, 29, 2,  42, 40, 159,
			42, 43, 373,  42, 59, 562,  42, 71, 25,  42, 85, 24,  43, 2, 66,  43, 16, 531,  43, 18, 426,  43, 20, 207,
			43, 29, 347,  43, 30, 45,  43, 31, 480,  43, 43, 342,  43, 57, 216,  43, 59, 469,  43, 67, 484,  43, 70, 43,
			43, 77, 425,  43, 81, 222,  43, 82, 438,  44, 1, 109,  44, 4, 142,  44, 12, 201,  44, 18, 169,  44, 28, 339,
			44, 30, 176,  44, 37, 234,  44, 48, 431,  44, 52, 72,  44, 54, 427,  44, 71, 421,  44, 77, 84,  44, 87, 310,
			44, 92, 460,  45, 7, 138,  45, 8, 498,  45, 13, 226,  45, 29, 246,  45, 32, 229,  45, 43, 17,  45, 45, 380,
			45, 46, 493,  45, 66, 76,  46, 1, 510,  46, 10, 100,  46, 25, 82,  46, 31, 279,  46, 33, 449,  46, 34, 391,
		};
	}

		public class EncodingNameTables
		{
			private static readonly string[] dotNetEncodingNames =
			{
				"GB2312", "GBK", "GB18030", "HZ-GB-2312", "BIG5", "ASCII", "UTF-8", "UTF-8",
				"UTF-8", "UTF-16", "UTF-16", "UTF-16", "x-cp50227", "x-cp50227", "x-cp50227", "EUC-KR",
				"ASCII", "ISO-2022-KR", "Johab", "SHIFT_JIS", "EUC-JP", "ISO-2022-JP", "ASCII", "ISO8859-1"
			};

			private static readonly string[] mlangEncodingNames =
			{
				"GB2312", "GBK", "GB18030", "ASCII", "BIG5", "EUC-TW", "UTF-8", "UTF-8",
				"UTF-8", "Unicode", "Unicode", "Unicode", "ISO2022CN", "ISO2022CN_CNS", "ISO2022CN_GB", "EUC-KR",
				"MS949", "ISO2022KR", "Johab", "SJIS", "EUC_JP", "ISO2022JP", "ASCII", "ISO8859_1"
			};

			private static readonly string[] displayEncodingNames =
			{
				"GB-2312", "GBK", "GB18030", "HZ", "Big5", "CNS11643", "UTF-8", "UTF-8 (Trad)",
				"UTF-8 (Simp)", "Unicode", "Unicode (Trad)", "Unicode (Simp)", "ISO2022 CN", "ISO2022CN-CNS",
				"ISO2022CN-GB", "EUC-KR", "CP949", "ISO 2022 KR", "Johab", "Shift-JIS", "EUC-JP",
				"ISO 2022 JP", "ASCII", "OTHER"
			};

			private static readonly string[] ianaEncodingNames =
			{
				"GB2312", "GBK", "GB18030", "HZ-GB-2312", "BIG5", "EUC-TW", "UTF-8", "UTF-8",
				"UTF-8", "UTF-16", "UTF-16", "UTF-16", "ISO-2022-CN", "ISO-2022-CN-EXT", "ISO-2022-CN-EXT",
				"EUC-KR", "x-windows-949", "ISO-2022-KR", "x-Johab", "Shift_JIS", "EUC-JP", "ISO-2022-JP",
				"ASCII", "ISO8859-1"
			};

			public static string[] GetDotNetEncodingNames()
			{
				return dotNetEncodingNames;
			}

			public static string[] GetMlangEncodingNames()
			{
				return mlangEncodingNames;
			}

			public static string[] GetDisplayEncodingNames()
			{
				return displayEncodingNames;
			}

			public static string[] GetIanaEncodingNames()
			{
				return ianaEncodingNames;
			}

		}

	public static string DetectFileEncoding(string filePath)
	{
		var (sampleBytes, bytesRead) = ReadFileSampleBytes(filePath);
		string encodingName;
		if (bytesRead > 0
			&& (encodingName = ConfigDescriptorState.ReadAndFreeNativeString(ResolveToken(sampleBytes, bytesRead))) != null
			&& !string.IsNullOrWhiteSpace(encodingName)
			&& encodingName != "ISO8859-1")
		{
			Console.WriteLine("GetCSharpEncode " + encodingName);
			return encodingName;
		}
		return Encoding.Default.HeaderName;
	}

	private static (byte[], int) ReadFileSampleBytes(string filePath)
	{
		byte[] sampleBytes = new byte[2000];
		int bytesRead = 0;
		try
		{
			using FileStream fileStream = new FileStream(filePath, FileMode.Open);
			bytesRead = fileStream.Read(sampleBytes, 0, sampleBytes.Length);
		}
		catch (Exception ex)
		{
			Console.WriteLine("GetFileBytes error:" + ex.GetMessageChain());
		}
		return (sampleBytes, bytesRead);
	}

	[DllImport("MusicTag.dll", EntryPoint = "de")]
	private static extern IntPtr ResolveToken(byte[] res, int offset_cont);
}

