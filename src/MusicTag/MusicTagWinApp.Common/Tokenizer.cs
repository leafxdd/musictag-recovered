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

		private void InitializeFrequencyTables()
		{
			int num2 = default(int);
			while (true)
			{
				int num = 93;
				while (true)
				{
					if (num >= 0)
					{
						for (num2 = 93; num2 >= 0; num2--)
						{
							DestroyRole(serializerStub, num, num2, 0);
						}
						num--;
						continue;
					}
					for (num = 125; num >= 0; num--)
					{
						for (num2 = 190; num2 >= 0; num2--)
						{
							DestroyRole(importerStub, num, num2, 0);
						}
					}
					num = 93;
					while (true)
					{
						if (num >= 0)
						{
							for (num2 = 157; num2 >= 0; num2--)
							{
								_AttrStub[num, num2] = 0;
							}
							num--;
							continue;
						}
						for (num = 125; num >= 0; num--)
						{
							for (num2 = 190; num2 >= 0; num2--)
							{
								_ReponseStub[num, num2] = 0;
							}
						}
						while (true)
						{
							num = 93;
							while (true)
							{
								if (num >= 0)
								{
									num2 = 93;
									goto IL_109aa;
								}
								num = 93;
								goto IL_efda;
								IL_45b6:
								_RecordStub[20, 3] = 83;
								_RecordStub[39, 69] = 82;
								_RecordStub[30, 29] = 81;
								_RecordStub[28, 19] = 80;
								_RecordStub[29, 90] = 79;
								DestroyRole(_RecordStub, 17, 86, 78);
								_RecordStub[15, 9] = 77;
								_RecordStub[39, 73] = 76;
								_RecordStub[15, 37] = 75;
								goto IL_29e9;
								IL_29e9:
								_RecordStub[35, 40] = 74;
								_RecordStub[33, 77] = 73;
								_RecordStub[27, 86] = 72;
								_RecordStub[36, 79] = 71;
								_RecordStub[23, 18] = 70;
								goto IL_2a3e;
								IL_109aa:
								while (num2 >= 0)
								{
									algoStub[num, num2] = 0;
									num2--;
								}
								int num3 = 53;
								if (InvokeRole())
								{
									goto IL_5acb;
								}
								goto IL_103bc;
								IL_2a3e:
								_RecordStub[34, 87] = 69;
								_RecordStub[39, 24] = 68;
								_RecordStub[26, 8] = 67;
								DestroyRole(_RecordStub, 33, 48, 66);
								_RecordStub[39, 30] = 65;
								_RecordStub[33, 28] = 64;
								_RecordStub[16, 67] = 63;
								_RecordStub[31, 78] = 62;
								_RecordStub[32, 23] = 61;
								_RecordStub[24, 55] = 60;
								goto IL_2ae7;
								IL_103bc:
								while (true)
								{
									switch (num3)
									{
									case 375:
										serializerStub[41, 20] = 482;
										serializerStub[26, 55] = 481;
										serializerStub[21, 93] = 480;
										serializerStub[31, 76] = 479;
										DestroyRole(serializerStub, 34, 31, 478);
										goto case 360;
									case 360:
										serializerStub[20, 66] = 477;
										serializerStub[51, 33] = 476;
										serializerStub[34, 86] = 475;
										serializerStub[37, 67] = 474;
										serializerStub[53, 53] = 473;
										serializerStub[40, 88] = 472;
										serializerStub[39, 10] = 471;
										serializerStub[24, 3] = 470;
										serializerStub[27, 25] = 469;
										goto case 325;
									case 325:
										serializerStub[26, 15] = 468;
										serializerStub[21, 88] = 467;
										DestroyRole(serializerStub, 52, 62, 466);
										serializerStub[46, 81] = 465;
										serializerStub[38, 72] = 464;
										goto case 322;
									case 322:
										DestroyRole(serializerStub, 17, 30, 463);
										goto case 173;
									case 173:
										serializerStub[52, 92] = 462;
										serializerStub[34, 90] = 461;
										DestroyRole(serializerStub, 21, 7, 460);
										serializerStub[36, 13] = 459;
										DestroyRole(serializerStub, 45, 41, 458);
										serializerStub[32, 5] = 457;
										serializerStub[26, 89] = 456;
										serializerStub[23, 87] = 455;
										DestroyRole(serializerStub, 20, 39, 454);
										serializerStub[27, 23] = 453;
										serializerStub[25, 59] = 452;
										goto case 125;
									case 125:
										serializerStub[49, 20] = 451;
										serializerStub[54, 77] = 450;
										serializerStub[27, 67] = 449;
										serializerStub[47, 33] = 448;
										serializerStub[41, 17] = 447;
										serializerStub[19, 81] = 446;
										serializerStub[16, 66] = 445;
										serializerStub[45, 26] = 444;
										DestroyRole(serializerStub, 49, 81, 443);
										DestroyRole(serializerStub, 53, 55, 442);
										goto case 117;
									case 117:
										serializerStub[16, 26] = 441;
										goto case 136;
									case 136:
										serializerStub[54, 62] = 440;
										serializerStub[20, 70] = 439;
										DestroyRole(serializerStub, 42, 35, 438);
										DestroyRole(serializerStub, 20, 57, 437);
										serializerStub[34, 36] = 436;
										serializerStub[46, 63] = 435;
										DestroyRole(serializerStub, 19, 45, 434);
										serializerStub[21, 10] = 433;
										goto case 271;
									case 271:
										serializerStub[52, 93] = 432;
										DestroyRole(serializerStub, 25, 2, 431);
										serializerStub[30, 57] = 430;
										serializerStub[41, 24] = 429;
										goto case 176;
									case 176:
										DestroyRole(serializerStub, 28, 43, 428);
										serializerStub[45, 86] = 427;
										serializerStub[51, 56] = 426;
										serializerStub[37, 28] = 425;
										serializerStub[52, 69] = 424;
										goto case 84;
									case 84:
										serializerStub[43, 92] = 423;
										goto case 237;
									case 237:
										serializerStub[41, 31] = 422;
										serializerStub[37, 87] = 421;
										serializerStub[47, 36] = 420;
										DestroyRole(serializerStub, 16, 16, 419);
										serializerStub[40, 56] = 418;
										serializerStub[24, 55] = 417;
										serializerStub[17, 1] = 416;
										serializerStub[35, 57] = 415;
										serializerStub[27, 50] = 414;
										serializerStub[26, 14] = 413;
										DestroyRole(serializerStub, 50, 40, 412);
										serializerStub[39, 19] = 411;
										DestroyRole(serializerStub, 19, 89, 410);
										serializerStub[29, 91] = 409;
										DestroyRole(serializerStub, 17, 89, 408);
										serializerStub[39, 74] = 407;
										serializerStub[46, 39] = 406;
										serializerStub[40, 28] = 405;
										serializerStub[45, 68] = 404;
										serializerStub[43, 10] = 403;
										serializerStub[42, 13] = 402;
										serializerStub[44, 81] = 401;
										serializerStub[41, 47] = 400;
										serializerStub[48, 58] = 399;
										serializerStub[43, 68] = 398;
										DestroyRole(serializerStub, 16, 79, 397);
										serializerStub[19, 5] = 396;
										serializerStub[54, 59] = 395;
										serializerStub[17, 36] = 394;
										serializerStub[18, 0] = 393;
										serializerStub[41, 5] = 392;
										serializerStub[41, 72] = 391;
										serializerStub[16, 39] = 390;
										serializerStub[54, 0] = 389;
										serializerStub[51, 16] = 388;
										serializerStub[29, 36] = 387;
										serializerStub[47, 5] = 386;
										serializerStub[47, 51] = 385;
										serializerStub[44, 7] = 384;
										DestroyRole(serializerStub, 35, 30, 383);
										serializerStub[26, 9] = 382;
										serializerStub[16, 7] = 381;
										num3 = 79;
										if (PostRole())
										{
											continue;
										}
										goto case 121;
									case 121:
										DestroyRole(_AttrStub, 23, 88, 326);
										_AttrStub[18, 2] = 325;
										_AttrStub[6, 88] = 324;
										_AttrStub[16, 84] = 323;
										DestroyRole(_AttrStub, 12, 48, 322);
										_AttrStub[7, 68] = 321;
										_AttrStub[5, 50] = 320;
										_AttrStub[13, 54] = 319;
										_AttrStub[7, 98] = 318;
										_AttrStub[11, 6] = 317;
										DestroyRole(_AttrStub, 9, 80, 316);
										_AttrStub[16, 41] = 315;
										_AttrStub[7, 43] = 314;
										DestroyRole(_AttrStub, 28, 117, 313);
										goto case 33;
									case 33:
										_AttrStub[3, 51] = 312;
										goto case 350;
									case 350:
										_AttrStub[7, 3] = 311;
										_AttrStub[20, 81] = 310;
										_AttrStub[4, 2] = 309;
										_AttrStub[11, 16] = 308;
										_AttrStub[10, 4] = 307;
										_AttrStub[10, 119] = 306;
										_AttrStub[6, 142] = 305;
										_AttrStub[18, 51] = 304;
										_AttrStub[8, 144] = 303;
										_AttrStub[10, 65] = 302;
										_AttrStub[11, 64] = 301;
										_AttrStub[11, 130] = 300;
										_AttrStub[9, 92] = 299;
										_AttrStub[18, 29] = 298;
										_AttrStub[18, 78] = 297;
										_AttrStub[18, 151] = 296;
										_AttrStub[33, 127] = 295;
										_AttrStub[35, 113] = 294;
										_AttrStub[10, 155] = 293;
										goto case 314;
									case 314:
										DestroyRole(_AttrStub, 3, 76, 292);
										_AttrStub[36, 123] = 291;
										_AttrStub[13, 143] = 290;
										DestroyRole(_AttrStub, 5, 135, 289);
										_AttrStub[23, 116] = 288;
										_AttrStub[6, 101] = 287;
										_AttrStub[14, 74] = 286;
										_AttrStub[7, 153] = 285;
										_AttrStub[3, 101] = 284;
										goto case 0;
									case 0:
										_AttrStub[9, 74] = 283;
										_AttrStub[3, 156] = 282;
										_AttrStub[4, 147] = 281;
										num3 = 226;
										if (InvokeRole())
										{
										}
										continue;
									case 374:
										importerStub[71, 189] = 490;
										importerStub[23, 147] = 489;
										importerStub[51, 139] = 488;
										importerStub[47, 137] = 487;
										importerStub[77, 123] = 486;
										importerStub[86, 183] = 485;
										importerStub[63, 173] = 484;
										importerStub[79, 144] = 483;
										goto case 154;
									case 154:
										DestroyRole(importerStub, 84, 159, 482);
										importerStub[60, 91] = 481;
										goto case 105;
									case 105:
										DestroyRole(importerStub, 66, 187, 480);
										importerStub[73, 114] = 479;
										importerStub[85, 56] = 478;
										importerStub[71, 149] = 477;
										goto case 150;
									case 150:
										importerStub[84, 189] = 476;
										importerStub[104, 31] = 475;
										importerStub[83, 82] = 474;
										goto case 140;
									case 236:
										DestroyRole(importerStub, 15, 155, 471);
										importerStub[83, 153] = 470;
										importerStub[71, 1] = 469;
										importerStub[53, 190] = 468;
										importerStub[50, 135] = 467;
										importerStub[3, 147] = 466;
										importerStub[48, 136] = 465;
										importerStub[66, 166] = 464;
										importerStub[55, 159] = 463;
										DestroyRole(importerStub, 82, 150, 462);
										importerStub[58, 178] = 461;
										importerStub[64, 102] = 460;
										DestroyRole(importerStub, 16, 106, 459);
										importerStub[68, 110] = 458;
										importerStub[54, 14] = 457;
										importerStub[60, 140] = 456;
										DestroyRole(importerStub, 91, 71, 455);
										importerStub[54, 150] = 454;
										importerStub[78, 177] = 453;
										importerStub[78, 117] = 452;
										importerStub[104, 12] = 451;
										DestroyRole(importerStub, 73, 150, 450);
										DestroyRole(importerStub, 51, 142, 449);
										importerStub[81, 145] = 448;
										importerStub[66, 183] = 447;
										DestroyRole(importerStub, 51, 178, 446);
										importerStub[75, 107] = 445;
										importerStub[65, 119] = 444;
										importerStub[69, 176] = 443;
										goto case 39;
									case 39:
										importerStub[59, 122] = 442;
										DestroyRole(importerStub, 78, 160, 441);
										importerStub[85, 183] = 440;
										goto case 34;
									case 34:
										importerStub[105, 16] = 439;
										importerStub[73, 110] = 438;
										importerStub[104, 39] = 437;
										importerStub[119, 16] = 436;
										importerStub[76, 162] = 435;
										importerStub[67, 152] = 434;
										importerStub[82, 24] = 433;
										DestroyRole(importerStub, 73, 121, 432);
										goto case 135;
									case 135:
										importerStub[83, 83] = 431;
										importerStub[82, 145] = 430;
										importerStub[49, 133] = 429;
										num3 = 372;
										if (PostRole())
										{
											continue;
										}
										goto case 140;
									case 140:
										importerStub[68, 35] = 473;
										importerStub[11, 77] = 472;
										goto case 236;
									case 373:
										algoStub[35, 10] = 371;
										algoStub[41, 37] = 370;
										algoStub[52, 82] = 369;
										algoStub[91, 72] = 368;
										algoStub[37, 29] = 367;
										algoStub[56, 30] = 366;
										DestroyRole(algoStub, 37, 80, 365);
										DestroyRole(algoStub, 81, 56, 364);
										algoStub[70, 3] = 363;
										algoStub[76, 15] = 362;
										algoStub[46, 47] = 361;
										algoStub[35, 88] = 360;
										algoStub[61, 58] = 359;
										algoStub[37, 37] = 358;
										algoStub[57, 22] = 357;
										algoStub[41, 23] = 356;
										goto case 100;
									case 100:
										DestroyRole(algoStub, 90, 66, 355);
										algoStub[39, 60] = 354;
										algoStub[38, 0] = 353;
										algoStub[37, 87] = 352;
										goto case 359;
									case 359:
										algoStub[46, 2] = 351;
										algoStub[38, 56] = 350;
										goto case 213;
									case 213:
										algoStub[58, 11] = 349;
										DestroyRole(algoStub, 48, 10, 348);
										algoStub[74, 4] = 347;
										algoStub[40, 42] = 346;
										algoStub[41, 52] = 345;
										algoStub[61, 92] = 344;
										DestroyRole(algoStub, 39, 50, 343);
										algoStub[47, 88] = 342;
										algoStub[88, 36] = 341;
										algoStub[45, 73] = 340;
										DestroyRole(algoStub, 82, 3, 339);
										algoStub[61, 36] = 338;
										algoStub[60, 33] = 337;
										algoStub[38, 27] = 336;
										algoStub[35, 83] = 335;
										algoStub[65, 24] = 334;
										algoStub[73, 10] = 333;
										algoStub[41, 13] = 332;
										algoStub[50, 27] = 331;
										algoStub[59, 50] = 330;
										algoStub[42, 45] = 329;
										algoStub[55, 19] = 328;
										algoStub[36, 77] = 327;
										algoStub[69, 31] = 326;
										goto case 245;
									case 245:
										algoStub[60, 7] = 325;
										DestroyRole(algoStub, 40, 88, 324);
										algoStub[57, 56] = 323;
										algoStub[50, 50] = 322;
										goto case 13;
									case 13:
										algoStub[42, 37] = 321;
										algoStub[38, 82] = 320;
										algoStub[52, 25] = 319;
										algoStub[42, 67] = 318;
										algoStub[48, 40] = 317;
										algoStub[45, 81] = 316;
										goto case 104;
									case 104:
										algoStub[57, 14] = 315;
										DestroyRole(algoStub, 42, 13, 314);
										algoStub[78, 0] = 313;
										algoStub[35, 51] = 312;
										algoStub[41, 67] = 311;
										algoStub[64, 23] = 310;
										DestroyRole(algoStub, 36, 65, 309);
										algoStub[48, 50] = 308;
										goto case 194;
									case 194:
										algoStub[46, 69] = 307;
										algoStub[47, 89] = 306;
										algoStub[41, 48] = 305;
										algoStub[60, 56] = 304;
										algoStub[44, 82] = 303;
										algoStub[47, 35] = 302;
										algoStub[49, 3] = 301;
										DestroyRole(algoStub, 49, 69, 300);
										algoStub[45, 93] = 299;
										DestroyRole(algoStub, 60, 34, 298);
										algoStub[60, 82] = 297;
										algoStub[61, 61] = 296;
										algoStub[86, 42] = 295;
										algoStub[89, 60] = 294;
										algoStub[48, 31] = 293;
										algoStub[35, 75] = 292;
										algoStub[91, 39] = 291;
										algoStub[53, 19] = 290;
										algoStub[39, 72] = 289;
										algoStub[69, 59] = 288;
										algoStub[41, 7] = 287;
										goto case 54;
									case 54:
										algoStub[54, 13] = 286;
										algoStub[43, 28] = 285;
										algoStub[36, 6] = 284;
										algoStub[45, 75] = 283;
										algoStub[36, 61] = 282;
										algoStub[38, 21] = 281;
										InvokeRole();
										if (PostRole())
										{
											goto case 214;
										}
										goto case 268;
									case 214:
									case 358:
										algoStub[45, 14] = 280;
										algoStub[61, 43] = 279;
										algoStub[36, 63] = 278;
										algoStub[43, 30] = 277;
										algoStub[46, 51] = 276;
										algoStub[68, 87] = 275;
										algoStub[39, 26] = 274;
										algoStub[46, 76] = 273;
										algoStub[36, 15] = 272;
										algoStub[35, 40] = 271;
										algoStub[79, 60] = 270;
										algoStub[46, 7] = 269;
										algoStub[65, 72] = 268;
										algoStub[69, 88] = 267;
										num3 = 46;
										if (PostRole())
										{
											continue;
										}
										goto case 108;
									case 108:
										importerStub[0, 173] = 577;
										importerStub[11, 23] = 576;
										importerStub[61, 141] = 575;
										importerStub[60, 123] = 574;
										importerStub[81, 114] = 573;
										importerStub[82, 131] = 572;
										importerStub[67, 156] = 571;
										importerStub[71, 167] = 570;
										importerStub[20, 50] = 569;
										importerStub[77, 132] = 568;
										DestroyRole(importerStub, 84, 38, 567);
										importerStub[26, 29] = 566;
										importerStub[74, 187] = 565;
										goto case 264;
									case 264:
										importerStub[62, 116] = 564;
										goto case 183;
									case 183:
										importerStub[67, 135] = 563;
										DestroyRole(importerStub, 5, 86, 562);
										importerStub[72, 186] = 561;
										num3 = 148;
										if (InvokeRole())
										{
										}
										continue;
									case 268:
									case 328:
										eventStub[36, 9] = 524;
										eventStub[4, 73] = 523;
										eventStub[23, 10] = 522;
										eventStub[3, 63] = 521;
										goto case 159;
									case 159:
										eventStub[39, 14] = 520;
										eventStub[3, 78] = 519;
										eventStub[33, 47] = 518;
										DestroyRole(eventStub, 21, 39, 517);
										eventStub[34, 46] = 516;
										goto case 132;
									case 132:
										DestroyRole(eventStub, 36, 75, 515);
										eventStub[41, 92] = 514;
										eventStub[37, 93] = 513;
										eventStub[4, 34] = 512;
										eventStub[15, 86] = 511;
										eventStub[46, 1] = 510;
										eventStub[37, 65] = 509;
										eventStub[3, 62] = 508;
										eventStub[32, 73] = 507;
										eventStub[21, 65] = 506;
										eventStub[29, 75] = 505;
										DestroyRole(eventStub, 26, 51, 504);
										eventStub[3, 34] = 503;
										eventStub[4, 10] = 502;
										DestroyRole(eventStub, 30, 22, 501);
										eventStub[35, 73] = 500;
										eventStub[17, 82] = 499;
										DestroyRole(eventStub, 45, 8, 498);
										eventStub[27, 73] = 497;
										DestroyRole(eventStub, 18, 55, 496);
										DestroyRole(eventStub, 25, 2, 495);
										DestroyRole(eventStub, 3, 26, 494);
										eventStub[45, 46] = 493;
										eventStub[4, 22] = 492;
										eventStub[4, 40] = 491;
										DestroyRole(eventStub, 18, 10, 490);
										eventStub[32, 9] = 489;
										goto case 72;
									case 72:
										eventStub[26, 49] = 488;
										eventStub[3, 47] = 487;
										eventStub[24, 65] = 486;
										DestroyRole(eventStub, 4, 76, 485);
										eventStub[43, 67] = 484;
										eventStub[3, 9] = 483;
										eventStub[41, 37] = 482;
										goto case 319;
									case 319:
										eventStub[33, 68] = 481;
										eventStub[43, 31] = 480;
										eventStub[19, 55] = 479;
										eventStub[4, 30] = 478;
										eventStub[27, 33] = 477;
										eventStub[16, 62] = 476;
										eventStub[36, 35] = 475;
										goto case 14;
									case 14:
										eventStub[37, 15] = 474;
										DestroyRole(eventStub, 27, 70, 473);
										eventStub[22, 71] = 472;
										eventStub[33, 45] = 471;
										eventStub[31, 78] = 470;
										eventStub[43, 59] = 469;
										eventStub[32, 19] = 468;
										eventStub[17, 28] = 467;
										goto case 281;
									case 281:
										eventStub[40, 28] = 466;
										eventStub[20, 93] = 465;
										eventStub[18, 15] = 464;
										goto case 294;
									case 294:
										eventStub[4, 23] = 463;
										DestroyRole(eventStub, 3, 23, 462);
										eventStub[26, 64] = 461;
										eventStub[44, 92] = 460;
										DestroyRole(eventStub, 17, 27, 459);
										DestroyRole(eventStub, 3, 56, 458);
										eventStub[25, 38] = 457;
										eventStub[23, 31] = 456;
										eventStub[35, 43] = 455;
										eventStub[4, 54] = 454;
										eventStub[35, 19] = 453;
										eventStub[22, 47] = 452;
										eventStub[42, 0] = 451;
										DestroyRole(eventStub, 23, 28, 450);
										eventStub[46, 33] = 449;
										eventStub[36, 85] = 448;
										eventStub[31, 12] = 447;
										eventStub[3, 76] = 446;
										eventStub[4, 75] = 445;
										eventStub[36, 56] = 444;
										goto case 219;
									case 219:
										eventStub[4, 64] = 443;
										eventStub[25, 77] = 442;
										eventStub[15, 52] = 441;
										DestroyRole(eventStub, 33, 73, 440);
										eventStub[3, 55] = 439;
										eventStub[43, 82] = 438;
										eventStub[27, 82] = 437;
										eventStub[20, 3] = 436;
										eventStub[40, 51] = 435;
										eventStub[3, 17] = 434;
										eventStub[27, 71] = 433;
										eventStub[4, 52] = 432;
										eventStub[44, 48] = 431;
										DestroyRole(eventStub, 27, 2, 430);
										eventStub[17, 39] = 429;
										eventStub[31, 8] = 428;
										eventStub[44, 54] = 427;
										eventStub[43, 18] = 426;
										eventStub[43, 77] = 425;
										DestroyRole(eventStub, 4, 61, 424);
										DestroyRole(eventStub, 19, 91, 423);
										goto case 44;
									case 44:
										eventStub[31, 13] = 422;
										eventStub[44, 71] = 421;
										eventStub[20, 0] = 420;
										eventStub[23, 87] = 419;
										eventStub[21, 14] = 418;
										DestroyRole(eventStub, 29, 13, 417);
										eventStub[3, 58] = 416;
										eventStub[26, 18] = 415;
										goto case 259;
									case 259:
										eventStub[4, 47] = 414;
										eventStub[4, 18] = 413;
										eventStub[3, 53] = 412;
										eventStub[26, 92] = 411;
										eventStub[21, 7] = 410;
										eventStub[4, 37] = 409;
										eventStub[4, 63] = 408;
										goto case 235;
									case 235:
										eventStub[36, 51] = 407;
										goto case 290;
									case 290:
										eventStub[4, 32] = 406;
										goto case 292;
									case 292:
										eventStub[28, 73] = 405;
										eventStub[4, 50] = 404;
										eventStub[41, 60] = 403;
										eventStub[23, 1] = 402;
										DestroyRole(eventStub, 36, 92, 401);
										eventStub[15, 41] = 400;
										eventStub[21, 71] = 399;
										eventStub[41, 30] = 398;
										eventStub[32, 76] = 397;
										eventStub[17, 34] = 396;
										eventStub[26, 15] = 395;
										DestroyRole(eventStub, 26, 25, 394);
										eventStub[31, 77] = 393;
										DestroyRole(eventStub, 31, 3, 392);
										eventStub[46, 34] = 391;
										eventStub[27, 84] = 390;
										eventStub[23, 8] = 389;
										eventStub[16, 0] = 388;
										eventStub[28, 80] = 387;
										DestroyRole(eventStub, 26, 54, 386);
										eventStub[33, 18] = 385;
										eventStub[31, 20] = 384;
										eventStub[31, 62] = 383;
										eventStub[30, 41] = 382;
										eventStub[33, 30] = 381;
										eventStub[45, 45] = 380;
										eventStub[37, 82] = 379;
										DestroyRole(eventStub, 15, 33, 378);
										goto case 198;
									case 198:
										eventStub[20, 12] = 377;
										num3 = 300;
										if (PostRole())
										{
											continue;
										}
										goto case 229;
									case 229:
										importerStub[71, 107] = 422;
										importerStub[11, 98] = 421;
										goto case 188;
									case 188:
										importerStub[72, 153] = 420;
										importerStub[2, 137] = 419;
										importerStub[59, 147] = 418;
										importerStub[58, 152] = 417;
										importerStub[55, 144] = 416;
										importerStub[73, 125] = 415;
										importerStub[52, 154] = 414;
										goto case 348;
									case 348:
										importerStub[70, 178] = 413;
										importerStub[79, 148] = 412;
										importerStub[63, 143] = 411;
										importerStub[50, 140] = 410;
										importerStub[47, 145] = 409;
										importerStub[48, 123] = 408;
										importerStub[56, 107] = 407;
										importerStub[84, 83] = 406;
										importerStub[59, 112] = 405;
										importerStub[124, 72] = 404;
										importerStub[79, 99] = 403;
										importerStub[3, 37] = 402;
										importerStub[114, 55] = 401;
										importerStub[85, 152] = 400;
										DestroyRole(importerStub, 60, 47, 399);
										importerStub[65, 96] = 398;
										importerStub[74, 110] = 397;
										importerStub[86, 182] = 396;
										importerStub[50, 99] = 395;
										importerStub[67, 186] = 394;
										importerStub[81, 74] = 393;
										importerStub[80, 37] = 392;
										importerStub[21, 60] = 391;
										num3 = 29;
										if (PostRole())
										{
											continue;
										}
										break;
									case 67:
										break;
									case 4:
										goto IL_2a3e;
									case 244:
										goto IL_2ae7;
									case 372:
										importerStub[94, 13] = 428;
										importerStub[58, 139] = 427;
										goto case 141;
									case 141:
										DestroyRole(importerStub, 74, 189, 426);
										importerStub[66, 177] = 425;
										DestroyRole(importerStub, 85, 184, 424);
										importerStub[55, 183] = 423;
										goto case 229;
									case 371:
										algoStub[51, 21] = 583;
										algoStub[40, 2] = 582;
										DestroyRole(algoStub, 67, 35, 581);
										algoStub[38, 78] = 580;
										algoStub[49, 18] = 579;
										DestroyRole(algoStub, 35, 23, 578);
										algoStub[42, 83] = 577;
										algoStub[79, 47] = 576;
										goto case 286;
									case 286:
										algoStub[61, 82] = 575;
										DestroyRole(algoStub, 38, 7, 574);
										DestroyRole(algoStub, 35, 29, 573);
										algoStub[37, 77] = 572;
										algoStub[54, 67] = 571;
										algoStub[38, 80] = 570;
										num3 = 351;
										if (PostRole())
										{
											continue;
										}
										goto case 109;
									case 109:
										DestroyRole(_ReponseStub, 22, 185, 304);
										_ReponseStub[36, 166] = 303;
										goto case 60;
									case 60:
										_ReponseStub[19, 40] = 302;
										_ReponseStub[22, 107] = 301;
										_ReponseStub[22, 102] = 300;
										_ReponseStub[57, 162] = 299;
										_ReponseStub[22, 124] = 298;
										DestroyRole(_ReponseStub, 37, 138, 297);
										DestroyRole(_ReponseStub, 37, 25, 296);
										_ReponseStub[0, 69] = 295;
										_ReponseStub[43, 172] = 294;
										_ReponseStub[42, 167] = 293;
										_ReponseStub[35, 120] = 292;
										num3 = 312;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 339;
									case 339:
										serializerStub[38, 74] = 303;
										serializerStub[28, 26] = 302;
										DestroyRole(serializerStub, 15, 13, 301);
										serializerStub[39, 34] = 300;
										goto case 31;
									case 31:
										DestroyRole(serializerStub, 39, 46, 299);
										serializerStub[42, 66] = 298;
										goto case 270;
									case 270:
										serializerStub[33, 58] = 297;
										serializerStub[15, 56] = 296;
										serializerStub[18, 51] = 295;
										serializerStub[49, 68] = 294;
										DestroyRole(serializerStub, 30, 37, 293);
										goto case 331;
									case 331:
										serializerStub[51, 84] = 292;
										goto case 122;
									case 122:
										serializerStub[51, 9] = 291;
										serializerStub[40, 70] = 290;
										serializerStub[41, 84] = 289;
										serializerStub[28, 64] = 288;
										serializerStub[32, 88] = 287;
										serializerStub[24, 5] = 286;
										serializerStub[53, 23] = 285;
										serializerStub[42, 27] = 284;
										serializerStub[22, 38] = 283;
										serializerStub[32, 86] = 282;
										serializerStub[34, 30] = 281;
										goto case 303;
									case 303:
										serializerStub[38, 63] = 280;
										serializerStub[24, 59] = 279;
										serializerStub[22, 81] = 278;
										serializerStub[32, 11] = 277;
										goto case 251;
									case 251:
										serializerStub[51, 21] = 276;
										serializerStub[54, 41] = 275;
										serializerStub[21, 50] = 274;
										serializerStub[23, 89] = 273;
										DestroyRole(serializerStub, 19, 87, 272);
										serializerStub[26, 7] = 271;
										serializerStub[30, 75] = 270;
										serializerStub[43, 84] = 269;
										serializerStub[51, 25] = 268;
										serializerStub[16, 67] = 267;
										serializerStub[32, 9] = 266;
										goto case 258;
									case 258:
										serializerStub[48, 51] = 265;
										serializerStub[39, 7] = 264;
										serializerStub[44, 88] = 263;
										serializerStub[52, 24] = 262;
										serializerStub[23, 34] = 261;
										serializerStub[32, 75] = 260;
										serializerStub[19, 10] = 259;
										num3 = 223;
										if (InvokeRole())
										{
										}
										continue;
									case 370:
										_AttrStub[7, 101] = 495;
										_AttrStub[11, 139] = 494;
										_AttrStub[3, 135] = 493;
										_AttrStub[7, 102] = 492;
										_AttrStub[17, 13] = 491;
										_AttrStub[3, 20] = 490;
										_AttrStub[27, 106] = 489;
										_AttrStub[5, 88] = 488;
										DestroyRole(_AttrStub, 6, 33, 487);
										_AttrStub[5, 139] = 486;
										_AttrStub[6, 0] = 485;
										_AttrStub[17, 58] = 484;
										_AttrStub[5, 133] = 483;
										_AttrStub[9, 107] = 482;
										_AttrStub[23, 39] = 481;
										_AttrStub[5, 23] = 480;
										goto case 160;
									case 160:
										_AttrStub[3, 79] = 479;
										_AttrStub[32, 97] = 478;
										_AttrStub[3, 136] = 477;
										goto case 315;
									case 315:
										_AttrStub[4, 94] = 476;
										_AttrStub[21, 61] = 475;
										_AttrStub[23, 123] = 474;
										_AttrStub[26, 16] = 473;
										_AttrStub[24, 137] = 472;
										_AttrStub[22, 18] = 471;
										_AttrStub[5, 1] = 470;
										_AttrStub[20, 119] = 469;
										_AttrStub[3, 7] = 468;
										num3 = 116;
										if (PostRole())
										{
											continue;
										}
										goto case 30;
									case 30:
										_ReponseStub[37, 173] = 390;
										_ReponseStub[16, 121] = 389;
										goto case 139;
									case 139:
										DestroyRole(_ReponseStub, 35, 5, 388);
										_ReponseStub[46, 122] = 387;
										_ReponseStub[40, 138] = 386;
										_ReponseStub[50, 49] = 385;
										_ReponseStub[36, 152] = 384;
										_ReponseStub[13, 43] = 383;
										_ReponseStub[9, 88] = 382;
										_ReponseStub[36, 159] = 381;
										_ReponseStub[27, 62] = 380;
										_ReponseStub[40, 18] = 379;
										_ReponseStub[17, 129] = 378;
										DestroyRole(_ReponseStub, 43, 97, 377);
										_ReponseStub[13, 131] = 376;
										goto case 103;
									case 103:
										_ReponseStub[46, 107] = 375;
										_ReponseStub[60, 64] = 374;
										_ReponseStub[36, 179] = 373;
										_ReponseStub[37, 55] = 372;
										_ReponseStub[41, 173] = 371;
										DestroyRole(_ReponseStub, 44, 172, 370);
										_ReponseStub[23, 187] = 369;
										_ReponseStub[36, 149] = 368;
										_ReponseStub[17, 125] = 367;
										_ReponseStub[55, 180] = 366;
										_ReponseStub[51, 129] = 365;
										_ReponseStub[36, 51] = 364;
										goto case 222;
									case 222:
										_ReponseStub[37, 122] = 363;
										_ReponseStub[48, 32] = 362;
										DestroyRole(_ReponseStub, 51, 99, 361);
										_ReponseStub[54, 16] = 360;
										_ReponseStub[41, 183] = 359;
										_ReponseStub[37, 179] = 358;
										_ReponseStub[38, 179] = 357;
										DestroyRole(_ReponseStub, 35, 143, 356);
										_ReponseStub[37, 24] = 355;
										_ReponseStub[40, 177] = 354;
										_ReponseStub[47, 117] = 353;
										_ReponseStub[39, 52] = 352;
										_ReponseStub[22, 99] = 351;
										_ReponseStub[40, 142] = 350;
										_ReponseStub[36, 49] = 349;
										_ReponseStub[38, 17] = 348;
										_ReponseStub[39, 188] = 347;
										_ReponseStub[36, 186] = 346;
										DestroyRole(_ReponseStub, 35, 189, 345);
										_ReponseStub[41, 7] = 344;
										_ReponseStub[18, 91] = 343;
										_ReponseStub[43, 137] = 342;
										_ReponseStub[35, 142] = 341;
										_ReponseStub[35, 117] = 340;
										goto case 177;
									case 177:
										_ReponseStub[39, 138] = 339;
										_ReponseStub[16, 59] = 338;
										_ReponseStub[39, 174] = 337;
										_ReponseStub[55, 145] = 336;
										_ReponseStub[37, 21] = 335;
										num3 = 48;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 190;
									case 190:
										DestroyRole(_RecordStub, 32, 49, 397);
										_RecordStub[24, 61] = 396;
										_RecordStub[18, 74] = 395;
										_RecordStub[23, 77] = 394;
										goto case 280;
									case 280:
										_RecordStub[23, 50] = 393;
										_RecordStub[23, 32] = 392;
										_RecordStub[23, 36] = 391;
										_RecordStub[38, 38] = 390;
										_RecordStub[29, 86] = 389;
										_RecordStub[36, 15] = 388;
										_RecordStub[31, 50] = 387;
										_RecordStub[15, 86] = 386;
										_RecordStub[39, 13] = 385;
										_RecordStub[34, 26] = 384;
										_RecordStub[19, 34] = 383;
										_RecordStub[16, 3] = 382;
										_RecordStub[26, 93] = 381;
										_RecordStub[19, 67] = 380;
										_RecordStub[24, 72] = 379;
										DestroyRole(_RecordStub, 29, 17, 378);
										_RecordStub[23, 24] = 377;
										_RecordStub[25, 19] = 376;
										_RecordStub[18, 65] = 375;
										_RecordStub[30, 78] = 374;
										_RecordStub[27, 52] = 373;
										_RecordStub[22, 18] = 372;
										_RecordStub[16, 38] = 371;
										_RecordStub[21, 26] = 370;
										_RecordStub[34, 20] = 369;
										_RecordStub[15, 42] = 368;
										_RecordStub[16, 71] = 367;
										_RecordStub[17, 17] = 366;
										_RecordStub[24, 71] = 365;
										_RecordStub[18, 84] = 364;
										_RecordStub[15, 40] = 363;
										DestroyRole(_RecordStub, 31, 62, 362);
										_RecordStub[15, 8] = 361;
										_RecordStub[16, 69] = 360;
										_RecordStub[29, 79] = 359;
										_RecordStub[38, 91] = 358;
										_RecordStub[31, 92] = 357;
										_RecordStub[20, 77] = 356;
										_RecordStub[3, 16] = 355;
										_RecordStub[27, 87] = 354;
										_RecordStub[16, 25] = 353;
										_RecordStub[36, 33] = 352;
										_RecordStub[37, 76] = 351;
										_RecordStub[30, 12] = 350;
										_RecordStub[26, 75] = 349;
										goto case 356;
									case 356:
										_RecordStub[25, 14] = 348;
										_RecordStub[32, 26] = 347;
										_RecordStub[23, 22] = 346;
										_RecordStub[20, 90] = 345;
										_RecordStub[19, 8] = 344;
										_RecordStub[38, 41] = 343;
										_RecordStub[34, 2] = 342;
										_RecordStub[39, 4] = 341;
										_RecordStub[27, 89] = 340;
										_RecordStub[28, 41] = 339;
										DestroyRole(_RecordStub, 28, 44, 338);
										_RecordStub[24, 92] = 337;
										_RecordStub[34, 65] = 336;
										_RecordStub[39, 14] = 335;
										_RecordStub[21, 38] = 334;
										_RecordStub[19, 31] = 333;
										_RecordStub[37, 39] = 332;
										DestroyRole(_RecordStub, 33, 41, 331);
										_RecordStub[38, 4] = 330;
										_RecordStub[23, 80] = 329;
										goto case 189;
									case 189:
										_RecordStub[25, 24] = 328;
										_RecordStub[37, 17] = 327;
										_RecordStub[22, 16] = 326;
										_RecordStub[22, 46] = 325;
										_RecordStub[33, 91] = 324;
										_RecordStub[24, 89] = 323;
										DestroyRole(_RecordStub, 30, 52, 322);
										_RecordStub[29, 38] = 321;
										DestroyRole(_RecordStub, 38, 85, 320);
										_RecordStub[15, 12] = 319;
										_RecordStub[27, 58] = 318;
										_RecordStub[29, 52] = 317;
										_RecordStub[37, 38] = 316;
										_RecordStub[34, 41] = 315;
										goto case 253;
									case 253:
										_RecordStub[31, 65] = 314;
										DestroyRole(_RecordStub, 29, 53, 313);
										DestroyRole(_RecordStub, 22, 47, 312);
										goto case 143;
									case 143:
										_RecordStub[22, 19] = 311;
										DestroyRole(_RecordStub, 26, 0, 310);
										_RecordStub[37, 86] = 309;
										_RecordStub[35, 4] = 308;
										_RecordStub[36, 54] = 307;
										_RecordStub[20, 76] = 306;
										_RecordStub[30, 9] = 305;
										_RecordStub[30, 33] = 304;
										_RecordStub[23, 17] = 303;
										_RecordStub[23, 33] = 302;
										_RecordStub[38, 52] = 301;
										_RecordStub[15, 19] = 300;
										_RecordStub[28, 45] = 299;
										_RecordStub[29, 78] = 298;
										DestroyRole(_RecordStub, 23, 15, 297);
										_RecordStub[33, 5] = 296;
										_RecordStub[17, 40] = 295;
										_RecordStub[30, 83] = 294;
										_RecordStub[18, 1] = 293;
										num3 = 225;
										if (!InvokeRole())
										{
											continue;
										}
										goto IL_4105;
									case 166:
										goto IL_4105;
									case 17:
										goto IL_4191;
									case 49:
										goto IL_4257;
									case 186:
										goto IL_43dd;
									case 151:
										goto IL_43ff;
									case 130:
										goto IL_45b6;
									case 369:
										_AttrStub[23, 21] = 220;
										_AttrStub[5, 106] = 219;
										_AttrStub[14, 100] = 218;
										_AttrStub[10, 152] = 217;
										_AttrStub[14, 89] = 216;
										_AttrStub[6, 138] = 215;
										goto case 57;
									case 57:
										DestroyRole(_AttrStub, 12, 157, 214);
										_AttrStub[10, 102] = 213;
										_AttrStub[19, 94] = 212;
										_AttrStub[7, 74] = 211;
										_AttrStub[18, 128] = 210;
										_AttrStub[27, 111] = 209;
										_AttrStub[11, 57] = 208;
										DestroyRole(_AttrStub, 3, 131, 207);
										_AttrStub[30, 23] = 206;
										_AttrStub[30, 126] = 205;
										_AttrStub[4, 36] = 204;
										_AttrStub[26, 124] = 203;
										_AttrStub[4, 19] = 202;
										_AttrStub[9, 152] = 201;
										goto case 87;
									case 87:
										_ReponseStub[41, 122] = 600;
										_ReponseStub[35, 0] = 599;
										_ReponseStub[43, 15] = 598;
										_ReponseStub[35, 99] = 597;
										goto case 112;
									case 112:
										_ReponseStub[35, 6] = 596;
										_ReponseStub[35, 8] = 595;
										_ReponseStub[38, 154] = 594;
										_ReponseStub[37, 34] = 593;
										_ReponseStub[37, 115] = 592;
										_ReponseStub[36, 12] = 591;
										_ReponseStub[18, 77] = 590;
										_ReponseStub[35, 100] = 589;
										_ReponseStub[35, 42] = 588;
										_ReponseStub[120, 75] = 587;
										_ReponseStub[35, 23] = 586;
										_ReponseStub[13, 72] = 585;
										goto case 250;
									case 250:
										_ReponseStub[0, 67] = 584;
										_ReponseStub[39, 172] = 583;
										_ReponseStub[22, 182] = 582;
										_ReponseStub[15, 186] = 581;
										DestroyRole(_ReponseStub, 15, 165, 580);
										_ReponseStub[35, 44] = 579;
										goto case 282;
									case 282:
										_ReponseStub[40, 13] = 578;
										goto case 37;
									case 37:
										_ReponseStub[38, 1] = 577;
										_ReponseStub[37, 33] = 576;
										DestroyRole(_ReponseStub, 36, 24, 575);
										DestroyRole(_ReponseStub, 56, 4, 574);
										_ReponseStub[35, 29] = 573;
										_ReponseStub[9, 96] = 572;
										num3 = 341;
										if (InvokeRole())
										{
										}
										continue;
									case 368:
										algoStub[60, 68] = 384;
										algoStub[51, 46] = 383;
										algoStub[50, 0] = 382;
										algoStub[38, 30] = 381;
										algoStub[50, 85] = 380;
										DestroyRole(algoStub, 60, 54, 379);
										DestroyRole(algoStub, 73, 6, 378);
										algoStub[73, 28] = 377;
										algoStub[56, 19] = 376;
										algoStub[62, 69] = 375;
										algoStub[81, 66] = 374;
										num3 = 193;
										if (PostRole())
										{
											continue;
										}
										goto case 279;
									case 279:
										serializerStub[51, 42] = 249;
										serializerStub[45, 67] = 248;
										serializerStub[15, 74] = 247;
										serializerStub[25, 81] = 246;
										serializerStub[37, 62] = 245;
										goto case 287;
									case 287:
										serializerStub[16, 55] = 244;
										goto case 211;
									case 211:
										serializerStub[18, 38] = 243;
										serializerStub[23, 23] = 242;
										DestroyRole(serializerStub, 38, 30, 241);
										DestroyRole(serializerStub, 17, 28, 240);
										serializerStub[44, 73] = 239;
										serializerStub[23, 78] = 238;
										goto case 192;
									case 192:
										serializerStub[40, 77] = 237;
										serializerStub[38, 87] = 236;
										serializerStub[27, 19] = 235;
										goto case 257;
									case 257:
										serializerStub[38, 82] = 234;
										DestroyRole(serializerStub, 37, 22, 233);
										DestroyRole(serializerStub, 41, 30, 232);
										serializerStub[54, 9] = 231;
										serializerStub[32, 30] = 230;
										serializerStub[30, 52] = 229;
										serializerStub[40, 84] = 228;
										serializerStub[53, 57] = 227;
										serializerStub[27, 27] = 226;
										num3 = 91;
										if (InvokeRole())
										{
										}
										continue;
									case 367:
										_RecordStub[19, 70] = 484;
										_RecordStub[24, 15] = 483;
										_RecordStub[26, 92] = 482;
										_RecordStub[37, 13] = 481;
										_RecordStub[39, 2] = 480;
										_RecordStub[23, 70] = 479;
										_RecordStub[27, 25] = 478;
										_RecordStub[15, 69] = 477;
										_RecordStub[19, 61] = 476;
										_RecordStub[31, 58] = 475;
										_RecordStub[24, 57] = 474;
										_RecordStub[36, 74] = 473;
										_RecordStub[21, 6] = 472;
										_RecordStub[30, 44] = 471;
										goto case 68;
									case 68:
										_RecordStub[15, 91] = 470;
										_RecordStub[27, 16] = 469;
										_RecordStub[29, 42] = 468;
										_RecordStub[33, 86] = 467;
										_RecordStub[29, 41] = 466;
										_RecordStub[20, 68] = 465;
										goto case 215;
									case 157:
										_RecordStub[31, 28] = 461;
										_RecordStub[15, 2] = 460;
										_RecordStub[23, 76] = 459;
										_RecordStub[38, 32] = 458;
										_RecordStub[29, 82] = 457;
										_RecordStub[21, 86] = 456;
										DestroyRole(_RecordStub, 24, 62, 455);
										goto case 330;
									case 330:
										_RecordStub[31, 64] = 454;
										_RecordStub[38, 26] = 453;
										_RecordStub[32, 86] = 452;
										_RecordStub[22, 32] = 451;
										DestroyRole(_RecordStub, 19, 59, 450);
										_RecordStub[34, 18] = 449;
										_RecordStub[18, 54] = 448;
										_RecordStub[38, 63] = 447;
										_RecordStub[36, 23] = 446;
										_RecordStub[35, 35] = 445;
										DestroyRole(_RecordStub, 32, 62, 444);
										_RecordStub[28, 35] = 443;
										_RecordStub[27, 13] = 442;
										_RecordStub[31, 59] = 441;
										num3 = 123;
										if (PostRole())
										{
											continue;
										}
										goto case 181;
									case 181:
										_RecordStub[15, 87] = 510;
										_RecordStub[30, 74] = 509;
										_RecordStub[24, 69] = 508;
										goto case 43;
									case 43:
										_RecordStub[20, 4] = 507;
										_RecordStub[27, 50] = 506;
										_RecordStub[30, 75] = 505;
										goto case 313;
									case 313:
										_RecordStub[24, 13] = 504;
										_RecordStub[30, 8] = 503;
										_RecordStub[31, 6] = 502;
										_RecordStub[25, 80] = 501;
										_RecordStub[36, 8] = 500;
										_RecordStub[15, 18] = 499;
										_RecordStub[39, 23] = 498;
										_RecordStub[16, 24] = 497;
										goto case 110;
									case 110:
										DestroyRole(_RecordStub, 31, 89, 496);
										_RecordStub[15, 71] = 495;
										_RecordStub[15, 57] = 494;
										_RecordStub[30, 11] = 493;
										_RecordStub[15, 36] = 492;
										_RecordStub[16, 60] = 491;
										_RecordStub[24, 45] = 490;
										_RecordStub[37, 35] = 489;
										_RecordStub[24, 87] = 488;
										_RecordStub[20, 45] = 487;
										DestroyRole(_RecordStub, 31, 90, 486);
										_RecordStub[32, 21] = 485;
										goto case 367;
									case 215:
										_RecordStub[25, 47] = 464;
										_RecordStub[22, 0] = 463;
										_RecordStub[18, 14] = 462;
										goto case 157;
									case 366:
										DestroyRole(algoStub, 39, 24, 449);
										algoStub[35, 55] = 448;
										algoStub[44, 91] = 447;
										algoStub[37, 51] = 446;
										goto case 59;
									case 59:
										algoStub[36, 19] = 445;
										goto case 76;
									case 76:
										algoStub[69, 90] = 444;
										algoStub[55, 35] = 443;
										algoStub[35, 54] = 442;
										algoStub[49, 61] = 441;
										algoStub[36, 67] = 440;
										goto case 191;
									case 191:
										algoStub[88, 34] = 439;
										algoStub[35, 17] = 438;
										algoStub[65, 69] = 437;
										algoStub[74, 89] = 436;
										algoStub[37, 31] = 435;
										algoStub[43, 48] = 434;
										algoStub[89, 27] = 433;
										algoStub[42, 79] = 432;
										algoStub[69, 57] = 431;
										algoStub[36, 13] = 430;
										algoStub[35, 62] = 429;
										algoStub[65, 47] = 428;
										algoStub[56, 8] = 427;
										goto case 178;
									case 178:
										algoStub[38, 79] = 426;
										algoStub[37, 64] = 425;
										algoStub[64, 64] = 424;
										algoStub[38, 53] = 423;
										algoStub[38, 31] = 422;
										algoStub[56, 81] = 421;
										algoStub[36, 22] = 420;
										algoStub[43, 4] = 419;
										goto case 306;
									case 306:
										algoStub[36, 90] = 418;
										DestroyRole(algoStub, 38, 62, 417);
										DestroyRole(algoStub, 66, 85, 416);
										algoStub[39, 1] = 415;
										algoStub[59, 40] = 414;
										goto case 174;
									case 174:
										algoStub[58, 93] = 413;
										DestroyRole(algoStub, 44, 43, 412);
										goto case 207;
									case 207:
										algoStub[39, 49] = 411;
										DestroyRole(algoStub, 64, 2, 410);
										algoStub[41, 35] = 409;
										algoStub[60, 22] = 408;
										algoStub[35, 91] = 407;
										algoStub[78, 1] = 406;
										algoStub[36, 14] = 405;
										algoStub[82, 29] = 404;
										algoStub[52, 86] = 403;
										algoStub[40, 16] = 402;
										algoStub[91, 52] = 401;
										DestroyRole(algoStub, 50, 75, 400);
										algoStub[64, 30] = 399;
										algoStub[90, 78] = 398;
										algoStub[36, 52] = 397;
										DestroyRole(algoStub, 55, 87, 396);
										algoStub[57, 5] = 395;
										algoStub[57, 31] = 394;
										goto case 212;
									case 212:
										algoStub[42, 35] = 393;
										algoStub[69, 50] = 392;
										algoStub[45, 8] = 391;
										algoStub[50, 87] = 390;
										algoStub[69, 55] = 389;
										algoStub[92, 3] = 388;
										algoStub[36, 43] = 387;
										goto case 311;
									case 311:
										DestroyRole(algoStub, 64, 10, 386);
										goto case 11;
									case 11:
										algoStub[56, 25] = 385;
										num3 = 368;
										if (InvokeRole())
										{
										}
										continue;
									case 365:
										_ReponseStub[5, 75] = 60;
										_ReponseStub[36, 52] = 59;
										_ReponseStub[51, 164] = 58;
										DestroyRole(_ReponseStub, 12, 85, 57);
										_ReponseStub[39, 168] = 56;
										_ReponseStub[43, 16] = 55;
										DestroyRole(_ReponseStub, 40, 69, 54);
										_ReponseStub[26, 108] = 53;
										_ReponseStub[51, 56] = 52;
										_ReponseStub[16, 37] = 51;
										goto case 3;
									case 3:
										_ReponseStub[40, 29] = 50;
										_ReponseStub[46, 171] = 49;
										_ReponseStub[40, 128] = 48;
										_ReponseStub[72, 114] = 47;
										_ReponseStub[21, 103] = 46;
										DestroyRole(_ReponseStub, 22, 44, 45);
										_ReponseStub[40, 115] = 44;
										_ReponseStub[43, 7] = 43;
										_ReponseStub[43, 153] = 42;
										_ReponseStub[17, 20] = 41;
										_ReponseStub[16, 49] = 40;
										_ReponseStub[36, 57] = 39;
										_ReponseStub[18, 38] = 38;
										DestroyRole(_ReponseStub, 45, 184, 37);
										_ReponseStub[37, 167] = 36;
										_ReponseStub[26, 106] = 35;
										DestroyRole(_ReponseStub, 61, 121, 34);
										_ReponseStub[89, 140] = 33;
										_ReponseStub[46, 61] = 32;
										goto case 115;
									case 115:
										_ReponseStub[39, 163] = 31;
										_ReponseStub[40, 62] = 30;
										_ReponseStub[38, 165] = 29;
										_ReponseStub[47, 37] = 28;
										_ReponseStub[18, 155] = 27;
										num3 = 204;
										if (PostRole())
										{
											continue;
										}
										goto case 373;
									case 364:
										algoStub[71, 54] = 543;
										goto case 144;
									case 144:
										algoStub[60, 70] = 542;
										algoStub[80, 1] = 541;
										DestroyRole(algoStub, 39, 59, 540);
										DestroyRole(algoStub, 39, 51, 539);
										num3 = 52;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 154;
									case 363:
										goto IL_5acb;
									case 47:
										goto IL_5b03;
									case 318:
										goto IL_5c7e;
									case 361:
										eventStub[3, 31] = 589;
										eventStub[3, 41] = 588;
										eventStub[3, 5] = 587;
										eventStub[3, 10] = 586;
										eventStub[3, 75] = 585;
										DestroyRole(eventStub, 3, 65, 584);
										eventStub[3, 72] = 583;
										num3 = 299;
										if (PostRole())
										{
											continue;
										}
										goto case 302;
									case 302:
										_ReponseStub[72, 15] = 490;
										_ReponseStub[47, 106] = 489;
										_ReponseStub[54, 14] = 488;
										_ReponseStub[24, 52] = 487;
										_ReponseStub[38, 162] = 486;
										_ReponseStub[41, 43] = 485;
										_ReponseStub[37, 121] = 484;
										_ReponseStub[14, 66] = 483;
										goto case 266;
									case 266:
										_ReponseStub[37, 30] = 482;
										_ReponseStub[35, 7] = 481;
										DestroyRole(_ReponseStub, 49, 58, 480);
										DestroyRole(_ReponseStub, 43, 188, 479);
										_ReponseStub[24, 66] = 478;
										_ReponseStub[35, 171] = 477;
										goto case 133;
									case 133:
										_ReponseStub[40, 186] = 476;
										_ReponseStub[39, 164] = 475;
										_ReponseStub[78, 186] = 474;
										_ReponseStub[8, 72] = 473;
										_ReponseStub[36, 190] = 472;
										DestroyRole(_ReponseStub, 35, 53, 471);
										_ReponseStub[35, 54] = 470;
										_ReponseStub[22, 159] = 469;
										_ReponseStub[35, 9] = 468;
										_ReponseStub[41, 140] = 467;
										_ReponseStub[37, 22] = 466;
										_ReponseStub[48, 97] = 465;
										_ReponseStub[50, 97] = 464;
										goto case 137;
									case 137:
										_ReponseStub[36, 127] = 463;
										_ReponseStub[37, 23] = 462;
										_ReponseStub[40, 55] = 461;
										_ReponseStub[35, 43] = 460;
										_ReponseStub[26, 22] = 459;
										_ReponseStub[35, 15] = 458;
										_ReponseStub[72, 179] = 457;
										_ReponseStub[20, 129] = 456;
										_ReponseStub[52, 101] = 455;
										_ReponseStub[35, 12] = 454;
										goto case 165;
									case 165:
										DestroyRole(_ReponseStub, 42, 156, 453);
										goto case 7;
									case 7:
										DestroyRole(_ReponseStub, 15, 157, 452);
										_ReponseStub[50, 140] = 451;
										_ReponseStub[26, 28] = 450;
										goto case 66;
									case 66:
										_ReponseStub[54, 51] = 449;
										_ReponseStub[35, 112] = 448;
										_ReponseStub[36, 116] = 447;
										_ReponseStub[42, 11] = 446;
										goto case 197;
									case 197:
										_ReponseStub[37, 172] = 445;
										DestroyRole(_ReponseStub, 37, 29, 444);
										_ReponseStub[44, 107] = 443;
										_ReponseStub[50, 17] = 442;
										_ReponseStub[39, 107] = 441;
										_ReponseStub[19, 109] = 440;
										_ReponseStub[36, 60] = 439;
										_ReponseStub[49, 132] = 438;
										_ReponseStub[26, 16] = 437;
										_ReponseStub[43, 155] = 436;
										_ReponseStub[37, 120] = 435;
										_ReponseStub[15, 159] = 434;
										_ReponseStub[43, 6] = 433;
										_ReponseStub[45, 188] = 432;
										DestroyRole(_ReponseStub, 35, 38, 431);
										_ReponseStub[39, 143] = 430;
										_ReponseStub[48, 144] = 429;
										_ReponseStub[37, 168] = 428;
										num3 = 82;
										if (PostRole())
										{
											continue;
										}
										goto case 369;
									case 357:
										_RecordStub[20, 46] = 283;
										_RecordStub[25, 15] = 282;
										_RecordStub[25, 91] = 281;
										goto case 126;
									case 126:
										_RecordStub[21, 83] = 280;
										_RecordStub[30, 77] = 279;
										_RecordStub[35, 30] = 278;
										_RecordStub[30, 34] = 277;
										_RecordStub[20, 69] = 276;
										_RecordStub[35, 10] = 275;
										DestroyRole(_RecordStub, 29, 70, 274);
										_RecordStub[22, 50] = 273;
										_RecordStub[18, 0] = 272;
										_RecordStub[22, 64] = 271;
										_RecordStub[38, 65] = 270;
										_RecordStub[22, 70] = 269;
										_RecordStub[24, 58] = 268;
										goto case 127;
									case 127:
										_RecordStub[19, 66] = 267;
										_RecordStub[30, 59] = 266;
										_RecordStub[37, 14] = 265;
										goto case 185;
									case 185:
										_RecordStub[16, 56] = 264;
										_RecordStub[29, 85] = 263;
										_RecordStub[31, 15] = 262;
										goto case 354;
									case 354:
										_RecordStub[36, 84] = 261;
										_RecordStub[39, 15] = 260;
										_RecordStub[39, 90] = 259;
										_RecordStub[18, 12] = 258;
										_RecordStub[21, 93] = 257;
										_RecordStub[24, 66] = 256;
										DestroyRole(_RecordStub, 27, 90, 255);
										_RecordStub[25, 90] = 254;
										_RecordStub[22, 24] = 253;
										goto case 316;
									case 316:
										DestroyRole(_RecordStub, 36, 67, 252);
										DestroyRole(_RecordStub, 33, 90, 251);
										_RecordStub[15, 60] = 250;
										DestroyRole(_RecordStub, 23, 85, 249);
										goto case 16;
									case 16:
										_RecordStub[34, 1] = 248;
										_RecordStub[39, 37] = 247;
										_RecordStub[21, 18] = 246;
										_RecordStub[34, 4] = 245;
										_RecordStub[28, 33] = 244;
										_RecordStub[15, 13] = 243;
										_RecordStub[32, 22] = 242;
										_RecordStub[30, 76] = 241;
										_RecordStub[20, 21] = 240;
										_RecordStub[38, 66] = 239;
										_RecordStub[32, 55] = 238;
										_RecordStub[32, 89] = 237;
										_RecordStub[25, 26] = 236;
										_RecordStub[16, 80] = 235;
										_RecordStub[15, 43] = 234;
										_RecordStub[38, 54] = 233;
										DestroyRole(_RecordStub, 39, 68, 232);
										_RecordStub[22, 88] = 231;
										_RecordStub[21, 84] = 230;
										_RecordStub[21, 17] = 229;
										_RecordStub[20, 28] = 228;
										_RecordStub[32, 1] = 227;
										DestroyRole(_RecordStub, 33, 87, 226);
										_RecordStub[38, 71] = 225;
										_RecordStub[37, 47] = 224;
										_RecordStub[18, 77] = 223;
										DestroyRole(_RecordStub, 37, 58, 222);
										_RecordStub[34, 74] = 221;
										_RecordStub[32, 54] = 220;
										_RecordStub[27, 33] = 219;
										_RecordStub[32, 93] = 218;
										_RecordStub[23, 51] = 217;
										_RecordStub[20, 57] = 216;
										_RecordStub[22, 37] = 215;
										_RecordStub[39, 10] = 214;
										_RecordStub[39, 17] = 213;
										_RecordStub[33, 4] = 212;
										_RecordStub[32, 84] = 211;
										_RecordStub[34, 3] = 210;
										_RecordStub[28, 27] = 209;
										_RecordStub[15, 79] = 208;
										_RecordStub[34, 21] = 207;
										_RecordStub[34, 69] = 206;
										_RecordStub[21, 62] = 205;
										_RecordStub[36, 24] = 204;
										_RecordStub[16, 89] = 203;
										_RecordStub[18, 48] = 202;
										_RecordStub[38, 15] = 201;
										DestroyRole(_RecordStub, 36, 58, 200);
										_RecordStub[21, 56] = 199;
										_RecordStub[34, 48] = 198;
										_RecordStub[21, 15] = 197;
										_RecordStub[39, 3] = 196;
										_RecordStub[16, 44] = 195;
										_RecordStub[18, 79] = 194;
										_RecordStub[25, 13] = 193;
										_RecordStub[29, 47] = 192;
										_RecordStub[38, 88] = 191;
										_RecordStub[20, 71] = 190;
										_RecordStub[16, 58] = 189;
										_RecordStub[35, 57] = 188;
										_RecordStub[29, 30] = 187;
										_RecordStub[29, 23] = 186;
										_RecordStub[34, 93] = 185;
										_RecordStub[30, 85] = 184;
										_RecordStub[15, 80] = 183;
										_RecordStub[32, 78] = 182;
										_RecordStub[37, 82] = 181;
										DestroyRole(_RecordStub, 22, 40, 180);
										_RecordStub[21, 69] = 179;
										_RecordStub[26, 85] = 178;
										_RecordStub[31, 31] = 177;
										_RecordStub[28, 64] = 176;
										_RecordStub[38, 13] = 175;
										_RecordStub[25, 2] = 174;
										DestroyRole(_RecordStub, 22, 34, 173);
										_RecordStub[28, 28] = 172;
										_RecordStub[24, 91] = 171;
										goto IL_5b03;
									case 355:
										algoStub[81, 40] = 205;
										algoStub[37, 5] = 204;
										goto case 55;
									case 55:
										algoStub[74, 69] = 203;
										algoStub[36, 82] = 202;
										algoStub[46, 59] = 201;
										importerStub[52, 132] = 600;
										importerStub[73, 135] = 599;
										importerStub[49, 123] = 598;
										importerStub[77, 146] = 597;
										importerStub[81, 123] = 596;
										importerStub[82, 144] = 595;
										importerStub[51, 179] = 594;
										importerStub[83, 154] = 593;
										importerStub[71, 139] = 592;
										importerStub[64, 139] = 591;
										importerStub[85, 144] = 590;
										importerStub[52, 125] = 589;
										importerStub[88, 25] = 588;
										importerStub[81, 106] = 587;
										importerStub[81, 148] = 586;
										goto case 238;
									case 238:
										importerStub[62, 137] = 585;
										importerStub[94, 0] = 584;
										importerStub[1, 64] = 583;
										importerStub[67, 163] = 582;
										importerStub[20, 190] = 581;
										goto case 80;
									case 80:
										DestroyRole(importerStub, 57, 131, 580);
										goto case 352;
									case 352:
										importerStub[29, 169] = 579;
										importerStub[72, 143] = 578;
										goto case 108;
									case 353:
										_AttrStub[3, 42] = 594;
										_AttrStub[5, 34] = 593;
										_AttrStub[3, 8] = 592;
										DestroyRole(_AttrStub, 3, 6, 591);
										_AttrStub[3, 67] = 590;
										_AttrStub[7, 139] = 589;
										_AttrStub[23, 137] = 588;
										_AttrStub[12, 46] = 587;
										DestroyRole(_AttrStub, 4, 8, 586);
										_AttrStub[4, 41] = 585;
										_AttrStub[18, 47] = 584;
										_AttrStub[12, 114] = 583;
										_AttrStub[6, 1] = 582;
										_AttrStub[22, 60] = 581;
										_AttrStub[5, 46] = 580;
										_AttrStub[11, 79] = 579;
										_AttrStub[3, 23] = 578;
										_AttrStub[7, 114] = 577;
										_AttrStub[29, 102] = 576;
										_AttrStub[19, 14] = 575;
										_AttrStub[4, 133] = 574;
										_AttrStub[3, 29] = 573;
										_AttrStub[4, 109] = 572;
										_AttrStub[14, 127] = 571;
										_AttrStub[5, 48] = 570;
										num3 = 267;
										if (PostRole())
										{
											continue;
										}
										goto case 254;
									case 254:
										algoStub[35, 15] = 460;
										DestroyRole(algoStub, 82, 59, 459);
										algoStub[35, 43] = 458;
										algoStub[73, 0] = 457;
										num3 = 233;
										if (PostRole())
										{
											continue;
										}
										goto case 110;
									case 351:
										DestroyRole(algoStub, 52, 74, 569);
										algoStub[36, 37] = 568;
										algoStub[74, 8] = 567;
										algoStub[41, 83] = 566;
										algoStub[36, 75] = 565;
										algoStub[49, 63] = 564;
										algoStub[42, 58] = 563;
										DestroyRole(algoStub, 56, 33, 562);
										algoStub[37, 76] = 561;
										DestroyRole(algoStub, 62, 39, 560);
										algoStub[35, 21] = 559;
										algoStub[70, 19] = 558;
										algoStub[77, 88] = 557;
										algoStub[51, 14] = 556;
										algoStub[36, 17] = 555;
										algoStub[44, 51] = 554;
										algoStub[38, 72] = 553;
										DestroyRole(algoStub, 74, 90, 552);
										algoStub[35, 48] = 551;
										algoStub[35, 69] = 550;
										algoStub[66, 86] = 549;
										DestroyRole(algoStub, 57, 20, 548);
										algoStub[35, 53] = 547;
										algoStub[36, 87] = 546;
										algoStub[84, 67] = 545;
										algoStub[70, 56] = 544;
										goto case 364;
									case 349:
										serializerStub[22, 7] = 484;
										goto case 261;
									case 261:
										serializerStub[19, 42] = 483;
										goto case 375;
									case 347:
										DestroyRole(_RecordStub, 19, 60, 7);
										goto case 106;
									case 106:
										_RecordStub[30, 32] = 6;
										_RecordStub[38, 34] = 5;
										_RecordStub[23, 4] = 4;
										_RecordStub[23, 1] = 3;
										_RecordStub[27, 57] = 2;
										_RecordStub[39, 38] = 1;
										_RecordStub[32, 33] = 0;
										eventStub[3, 74] = 600;
										eventStub[3, 45] = 599;
										num3 = 329;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 7;
									case 346:
										goto IL_72ec;
									case 345:
										_RecordStub[38, 48] = 535;
										_RecordStub[23, 2] = 534;
										_RecordStub[30, 20] = 533;
										_RecordStub[38, 47] = 532;
										goto case 40;
									case 40:
										_RecordStub[39, 12] = 531;
										DestroyRole(_RecordStub, 23, 21, 530);
										num3 = 162;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 175;
									case 175:
										_RecordStub[25, 49] = 429;
										_RecordStub[36, 57] = 428;
										_RecordStub[20, 22] = 427;
										_RecordStub[15, 15] = 426;
										_RecordStub[31, 51] = 425;
										_RecordStub[24, 60] = 424;
										DestroyRole(_RecordStub, 31, 70, 423);
										_RecordStub[15, 7] = 422;
										_RecordStub[28, 40] = 421;
										DestroyRole(_RecordStub, 18, 41, 420);
										_RecordStub[15, 38] = 419;
										_RecordStub[32, 0] = 418;
										_RecordStub[19, 51] = 417;
										_RecordStub[34, 62] = 416;
										_RecordStub[16, 27] = 415;
										DestroyRole(_RecordStub, 20, 70, 414);
										_RecordStub[22, 33] = 413;
										_RecordStub[26, 73] = 412;
										_RecordStub[20, 79] = 411;
										_RecordStub[23, 6] = 410;
										_RecordStub[24, 85] = 409;
										_RecordStub[38, 51] = 408;
										_RecordStub[29, 88] = 407;
										DestroyRole(_RecordStub, 38, 55, 406);
										_RecordStub[32, 32] = 405;
										num3 = 15;
										if (PostRole())
										{
											continue;
										}
										goto case 134;
									case 134:
										_AttrStub[31, 82] = 459;
										DestroyRole(_AttrStub, 3, 43, 458);
										_AttrStub[25, 119] = 457;
										DestroyRole(_AttrStub, 16, 111, 456);
										_AttrStub[7, 77] = 455;
										_AttrStub[3, 95] = 454;
										_AttrStub[24, 82] = 453;
										_AttrStub[7, 52] = 452;
										_AttrStub[9, 151] = 451;
										_AttrStub[3, 129] = 450;
										_AttrStub[5, 87] = 449;
										_AttrStub[3, 55] = 448;
										_AttrStub[8, 153] = 447;
										_AttrStub[4, 83] = 446;
										_AttrStub[3, 114] = 445;
										_AttrStub[23, 147] = 444;
										DestroyRole(_AttrStub, 15, 31, 443);
										_AttrStub[3, 54] = 442;
										_AttrStub[11, 122] = 441;
										_AttrStub[4, 4] = 440;
										DestroyRole(_AttrStub, 34, 149, 439);
										_AttrStub[3, 17] = 438;
										_AttrStub[21, 64] = 437;
										_AttrStub[26, 144] = 436;
										_AttrStub[4, 62] = 435;
										DestroyRole(_AttrStub, 8, 15, 434);
										_AttrStub[35, 80] = 433;
										_AttrStub[7, 110] = 432;
										_AttrStub[23, 114] = 431;
										_AttrStub[3, 108] = 430;
										_AttrStub[3, 62] = 429;
										_AttrStub[21, 41] = 428;
										_AttrStub[15, 99] = 427;
										_AttrStub[5, 47] = 426;
										_AttrStub[4, 96] = 425;
										_AttrStub[20, 122] = 424;
										DestroyRole(_AttrStub, 5, 21, 423);
										_AttrStub[4, 157] = 422;
										_AttrStub[16, 14] = 421;
										_AttrStub[3, 117] = 420;
										_AttrStub[7, 129] = 419;
										_AttrStub[4, 27] = 418;
										_AttrStub[5, 30] = 417;
										DestroyRole(_AttrStub, 22, 16, 416);
										_AttrStub[5, 64] = 415;
										num3 = 28;
										if (PostRole())
										{
											continue;
										}
										goto case 356;
									case 344:
										eventStub[43, 57] = 216;
										DestroyRole(eventStub, 34, 53, 215);
										eventStub[20, 32] = 214;
										eventStub[34, 43] = 213;
										eventStub[41, 91] = 212;
										DestroyRole(eventStub, 29, 57, 211);
										DestroyRole(eventStub, 15, 43, 210);
										DestroyRole(eventStub, 22, 89, 209);
										eventStub[33, 83] = 208;
										eventStub[43, 20] = 207;
										eventStub[25, 58] = 206;
										eventStub[30, 30] = 205;
										eventStub[4, 56] = 204;
										eventStub[17, 64] = 203;
										eventStub[23, 0] = 202;
										eventStub[44, 12] = 201;
										eventStub[25, 37] = 200;
										eventStub[35, 13] = 199;
										DestroyRole(eventStub, 20, 30, 198);
										eventStub[21, 84] = 197;
										DestroyRole(eventStub, 29, 14, 196);
										eventStub[30, 5] = 195;
										eventStub[37, 2] = 194;
										eventStub[4, 78] = 193;
										eventStub[29, 78] = 192;
										eventStub[29, 84] = 191;
										eventStub[32, 86] = 190;
										DestroyRole(eventStub, 20, 68, 189);
										eventStub[30, 39] = 188;
										eventStub[15, 69] = 187;
										DestroyRole(eventStub, 4, 60, 186);
										eventStub[20, 61] = 185;
										eventStub[41, 67] = 184;
										eventStub[16, 35] = 183;
										DestroyRole(eventStub, 36, 57, 182);
										eventStub[39, 80] = 181;
										eventStub[4, 59] = 180;
										eventStub[4, 44] = 179;
										eventStub[40, 54] = 178;
										eventStub[30, 8] = 177;
										eventStub[44, 30] = 176;
										goto case 296;
									case 296:
										eventStub[31, 93] = 175;
										eventStub[31, 47] = 174;
										eventStub[16, 70] = 173;
										DestroyRole(eventStub, 21, 0, 172);
										eventStub[17, 35] = 171;
										eventStub[21, 67] = 170;
										eventStub[44, 18] = 169;
										eventStub[36, 29] = 168;
										eventStub[18, 67] = 167;
										eventStub[24, 28] = 166;
										eventStub[36, 24] = 165;
										DestroyRole(eventStub, 23, 5, 164);
										eventStub[31, 65] = 163;
										eventStub[26, 59] = 162;
										eventStub[28, 2] = 161;
										eventStub[39, 69] = 160;
										eventStub[42, 40] = 159;
										eventStub[37, 80] = 158;
										eventStub[15, 66] = 157;
										eventStub[34, 38] = 156;
										eventStub[28, 48] = 155;
										eventStub[37, 77] = 154;
										eventStub[29, 34] = 153;
										eventStub[33, 12] = 152;
										eventStub[4, 65] = 151;
										eventStub[30, 31] = 150;
										eventStub[27, 92] = 149;
										goto case 142;
									case 142:
										eventStub[4, 2] = 148;
										eventStub[4, 51] = 147;
										eventStub[23, 77] = 146;
										eventStub[4, 35] = 145;
										eventStub[3, 13] = 144;
										eventStub[26, 26] = 143;
										eventStub[44, 4] = 142;
										eventStub[39, 53] = 141;
										eventStub[20, 11] = 140;
										DestroyRole(eventStub, 40, 33, 139);
										eventStub[45, 7] = 138;
										eventStub[4, 70] = 137;
										eventStub[3, 49] = 136;
										DestroyRole(eventStub, 20, 59, 135);
										DestroyRole(eventStub, 21, 12, 134);
										goto case 184;
									case 184:
										eventStub[33, 53] = 133;
										eventStub[20, 14] = 132;
										eventStub[37, 18] = 131;
										DestroyRole(eventStub, 18, 17, 130);
										goto case 208;
									case 208:
										eventStub[36, 23] = 129;
										eventStub[18, 57] = 128;
										eventStub[26, 74] = 127;
										goto case 327;
									case 327:
										eventStub[35, 2] = 126;
										eventStub[38, 58] = 125;
										eventStub[34, 68] = 124;
										eventStub[29, 81] = 123;
										eventStub[20, 69] = 122;
										goto case 305;
									case 305:
										eventStub[39, 86] = 121;
										eventStub[4, 16] = 120;
										eventStub[16, 49] = 119;
										eventStub[15, 72] = 118;
										eventStub[26, 35] = 117;
										eventStub[32, 14] = 116;
										eventStub[40, 90] = 115;
										eventStub[33, 79] = 114;
										eventStub[35, 4] = 113;
										eventStub[23, 33] = 112;
										eventStub[19, 19] = 111;
										eventStub[31, 41] = 110;
										eventStub[44, 1] = 109;
										eventStub[22, 56] = 108;
										eventStub[31, 27] = 107;
										eventStub[32, 18] = 106;
										eventStub[27, 32] = 105;
										DestroyRole(eventStub, 37, 39, 104);
										eventStub[42, 11] = 103;
										eventStub[29, 71] = 102;
										eventStub[32, 58] = 101;
										eventStub[46, 10] = 100;
										DestroyRole(eventStub, 17, 30, 99);
										eventStub[38, 15] = 98;
										eventStub[29, 60] = 97;
										eventStub[4, 11] = 96;
										eventStub[38, 31] = 95;
										eventStub[40, 79] = 94;
										eventStub[28, 49] = 93;
										eventStub[28, 84] = 92;
										eventStub[26, 77] = 91;
										eventStub[22, 32] = 90;
										eventStub[33, 17] = 89;
										goto case 65;
									case 65:
										eventStub[23, 18] = 88;
										eventStub[32, 64] = 87;
										eventStub[4, 6] = 86;
										eventStub[33, 51] = 85;
										eventStub[44, 77] = 84;
										eventStub[29, 5] = 83;
										goto case 78;
									case 78:
										eventStub[46, 25] = 82;
										eventStub[19, 58] = 81;
										eventStub[4, 46] = 80;
										eventStub[15, 71] = 79;
										eventStub[18, 58] = 78;
										eventStub[26, 45] = 77;
										eventStub[45, 66] = 76;
										eventStub[34, 10] = 75;
										eventStub[19, 37] = 74;
										eventStub[33, 65] = 73;
										eventStub[44, 52] = 72;
										eventStub[16, 38] = 71;
										eventStub[36, 46] = 70;
										eventStub[20, 26] = 69;
										eventStub[30, 37] = 68;
										eventStub[4, 58] = 67;
										num3 = 205;
										if (PostRole())
										{
											continue;
										}
										goto case 82;
									case 82:
										DestroyRole(_ReponseStub, 37, 1, 427);
										_ReponseStub[36, 109] = 426;
										_ReponseStub[46, 53] = 425;
										_ReponseStub[38, 54] = 424;
										_ReponseStub[36, 0] = 423;
										DestroyRole(_ReponseStub, 72, 33, 422);
										_ReponseStub[42, 8] = 421;
										_ReponseStub[36, 31] = 420;
										_ReponseStub[35, 150] = 419;
										_ReponseStub[118, 93] = 418;
										_ReponseStub[37, 61] = 417;
										_ReponseStub[0, 85] = 416;
										_ReponseStub[36, 27] = 415;
										_ReponseStub[35, 134] = 414;
										_ReponseStub[36, 145] = 413;
										_ReponseStub[6, 96] = 412;
										_ReponseStub[36, 14] = 411;
										_ReponseStub[16, 36] = 410;
										_ReponseStub[15, 175] = 409;
										DestroyRole(_ReponseStub, 35, 10, 408);
										_ReponseStub[36, 189] = 407;
										_ReponseStub[35, 51] = 406;
										_ReponseStub[35, 109] = 405;
										_ReponseStub[35, 147] = 404;
										DestroyRole(_ReponseStub, 35, 180, 403);
										num3 = 273;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 170;
									case 170:
										algoStub[55, 24] = 536;
										DestroyRole(algoStub, 52, 4, 535);
										algoStub[54, 26] = 534;
										goto case 89;
									case 89:
										algoStub[36, 31] = 533;
										algoStub[37, 22] = 532;
										goto case 99;
									case 99:
										DestroyRole(algoStub, 37, 9, 531);
										algoStub[46, 0] = 530;
										algoStub[56, 46] = 529;
										algoStub[47, 93] = 528;
										algoStub[37, 25] = 527;
										algoStub[39, 8] = 526;
										algoStub[46, 73] = 525;
										algoStub[38, 48] = 524;
										algoStub[39, 83] = 523;
										algoStub[60, 92] = 522;
										goto case 25;
									case 25:
										algoStub[70, 11] = 521;
										algoStub[63, 84] = 520;
										algoStub[38, 65] = 519;
										algoStub[45, 45] = 518;
										goto case 41;
									case 41:
										algoStub[63, 49] = 517;
										algoStub[63, 50] = 516;
										goto case 90;
									case 90:
										algoStub[39, 93] = 515;
										algoStub[68, 20] = 514;
										algoStub[44, 84] = 513;
										algoStub[66, 34] = 512;
										DestroyRole(algoStub, 37, 58, 511);
										algoStub[39, 0] = 510;
										algoStub[59, 1] = 509;
										algoStub[47, 8] = 508;
										algoStub[61, 17] = 507;
										algoStub[53, 87] = 506;
										algoStub[67, 26] = 505;
										algoStub[43, 46] = 504;
										algoStub[38, 61] = 503;
										algoStub[45, 9] = 502;
										algoStub[66, 83] = 501;
										num3 = 317;
										if (PostRole())
										{
											continue;
										}
										goto case 255;
									case 255:
										_ReponseStub[31, 39] = 13;
										_ReponseStub[40, 182] = 12;
										_ReponseStub[52, 155] = 11;
										_ReponseStub[42, 166] = 10;
										_ReponseStub[35, 27] = 9;
										_ReponseStub[38, 3] = 8;
										_ReponseStub[13, 44] = 7;
										_ReponseStub[58, 157] = 6;
										_ReponseStub[47, 51] = 5;
										_ReponseStub[41, 37] = 4;
										_ReponseStub[41, 172] = 3;
										_ReponseStub[51, 165] = 2;
										_ReponseStub[15, 161] = 1;
										_ReponseStub[24, 181] = 0;
										goto case 38;
									case 38:
										algoStub[48, 49] = 599;
										algoStub[35, 65] = 598;
										algoStub[41, 27] = 597;
										algoStub[35, 0] = 596;
										algoStub[39, 19] = 595;
										algoStub[35, 42] = 594;
										algoStub[38, 66] = 593;
										algoStub[35, 8] = 592;
										algoStub[35, 6] = 591;
										algoStub[35, 66] = 590;
										algoStub[43, 14] = 589;
										algoStub[69, 80] = 588;
										algoStub[50, 48] = 587;
										algoStub[36, 71] = 586;
										algoStub[37, 10] = 585;
										algoStub[60, 52] = 584;
										num3 = 371;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 160;
									case 343:
										_RecordStub[20, 72] = 570;
										_RecordStub[15, 51] = 569;
										_RecordStub[3, 8] = 568;
										_RecordStub[32, 53] = 567;
										_RecordStub[27, 85] = 566;
										_RecordStub[25, 23] = 565;
										_RecordStub[15, 44] = 564;
										DestroyRole(_RecordStub, 32, 3, 563);
										DestroyRole(_RecordStub, 31, 68, 562);
										_RecordStub[30, 24] = 561;
										_RecordStub[29, 49] = 560;
										_RecordStub[27, 49] = 559;
										_RecordStub[23, 23] = 558;
										_RecordStub[31, 91] = 557;
										_RecordStub[31, 46] = 556;
										_RecordStub[19, 74] = 555;
										_RecordStub[27, 27] = 554;
										goto case 20;
									case 20:
										DestroyRole(_RecordStub, 3, 17, 553);
										DestroyRole(_RecordStub, 20, 38, 552);
										_RecordStub[21, 82] = 551;
										goto case 234;
									case 234:
										_RecordStub[28, 25] = 550;
										_RecordStub[32, 5] = 549;
										_RecordStub[31, 23] = 548;
										_RecordStub[25, 45] = 547;
										_RecordStub[32, 87] = 546;
										DestroyRole(_RecordStub, 18, 26, 545);
										goto case 86;
									case 86:
										_RecordStub[24, 10] = 544;
										DestroyRole(_RecordStub, 26, 82, 543);
										_RecordStub[15, 89] = 542;
										_RecordStub[28, 36] = 541;
										_RecordStub[28, 31] = 540;
										_RecordStub[16, 23] = 539;
										_RecordStub[16, 77] = 538;
										goto IL_5acb;
									case 342:
										DestroyRole(_RecordStub, 24, 43, 522);
										_RecordStub[36, 44] = 521;
										_RecordStub[20, 30] = 520;
										goto case 172;
									case 172:
										_RecordStub[39, 86] = 519;
										_RecordStub[22, 14] = 518;
										_RecordStub[29, 39] = 517;
										_RecordStub[28, 38] = 516;
										_RecordStub[23, 79] = 515;
										_RecordStub[24, 56] = 514;
										goto case 333;
									case 333:
										_RecordStub[29, 63] = 513;
										_RecordStub[31, 45] = 512;
										_RecordStub[23, 26] = 511;
										goto case 181;
									case 341:
										DestroyRole(_ReponseStub, 37, 62, 571);
										_ReponseStub[48, 47] = 570;
										_ReponseStub[51, 14] = 569;
										_ReponseStub[39, 122] = 568;
										_ReponseStub[44, 46] = 567;
										_ReponseStub[35, 21] = 566;
										_ReponseStub[36, 8] = 565;
										_ReponseStub[36, 141] = 564;
										_ReponseStub[3, 81] = 563;
										DestroyRole(_ReponseStub, 37, 155, 562);
										_ReponseStub[42, 84] = 561;
										_ReponseStub[36, 40] = 560;
										_ReponseStub[35, 103] = 559;
										goto case 118;
									case 118:
										_ReponseStub[11, 84] = 558;
										goto case 147;
									case 147:
										_ReponseStub[45, 33] = 557;
										_ReponseStub[121, 79] = 556;
										goto case 321;
									case 321:
										_ReponseStub[2, 77] = 555;
										_ReponseStub[36, 41] = 554;
										_ReponseStub[37, 47] = 553;
										_ReponseStub[39, 125] = 552;
										_ReponseStub[37, 26] = 551;
										_ReponseStub[35, 48] = 550;
										_ReponseStub[35, 28] = 549;
										_ReponseStub[35, 159] = 548;
										goto case 88;
									case 88:
										_ReponseStub[37, 40] = 547;
										_ReponseStub[35, 145] = 546;
										_ReponseStub[37, 147] = 545;
										_ReponseStub[46, 160] = 544;
										goto case 74;
									case 74:
										DestroyRole(_ReponseStub, 37, 46, 543);
										DestroyRole(_ReponseStub, 50, 99, 542);
										goto case 101;
									case 101:
										_ReponseStub[52, 13] = 541;
										_ReponseStub[10, 82] = 540;
										_ReponseStub[35, 169] = 539;
										DestroyRole(_ReponseStub, 35, 31, 538);
										goto case 69;
									case 69:
										_ReponseStub[47, 31] = 537;
										goto case 221;
									case 221:
										_ReponseStub[18, 79] = 536;
										_ReponseStub[16, 113] = 535;
										_ReponseStub[37, 104] = 534;
										goto case 42;
									case 42:
										_ReponseStub[39, 134] = 533;
										_ReponseStub[36, 53] = 532;
										_ReponseStub[38, 0] = 531;
										_ReponseStub[4, 86] = 530;
										_ReponseStub[54, 17] = 529;
										_ReponseStub[43, 157] = 528;
										_ReponseStub[35, 165] = 527;
										_ReponseStub[69, 147] = 526;
										DestroyRole(_ReponseStub, 117, 95, 525);
										_ReponseStub[35, 162] = 524;
										_ReponseStub[35, 17] = 523;
										_ReponseStub[36, 142] = 522;
										_ReponseStub[36, 4] = 521;
										_ReponseStub[37, 166] = 520;
										_ReponseStub[35, 168] = 519;
										_ReponseStub[35, 19] = 518;
										_ReponseStub[37, 48] = 517;
										_ReponseStub[42, 37] = 516;
										_ReponseStub[40, 146] = 515;
										_ReponseStub[36, 123] = 514;
										_ReponseStub[22, 41] = 513;
										DestroyRole(_ReponseStub, 20, 119, 512);
										goto case 146;
									case 146:
										_ReponseStub[2, 74] = 511;
										DestroyRole(_ReponseStub, 44, 113, 510);
										goto case 92;
									case 92:
										_ReponseStub[35, 125] = 509;
										_ReponseStub[37, 16] = 508;
										_ReponseStub[35, 20] = 507;
										_ReponseStub[35, 55] = 506;
										_ReponseStub[37, 145] = 505;
										_ReponseStub[0, 88] = 504;
										DestroyRole(_ReponseStub, 3, 94, 503);
										_ReponseStub[6, 65] = 502;
										DestroyRole(_ReponseStub, 26, 15, 501);
										_ReponseStub[41, 126] = 500;
										_ReponseStub[36, 129] = 499;
										_ReponseStub[31, 75] = 498;
										_ReponseStub[19, 61] = 497;
										_ReponseStub[35, 128] = 496;
										_ReponseStub[29, 79] = 495;
										DestroyRole(_ReponseStub, 36, 62, 494);
										_ReponseStub[37, 189] = 493;
										_ReponseStub[39, 109] = 492;
										_ReponseStub[39, 135] = 491;
										goto case 302;
									case 340:
										importerStub[74, 188] = 358;
										goto case 309;
									case 309:
										importerStub[14, 132] = 357;
										goto case 62;
									case 62:
										importerStub[62, 172] = 356;
										importerStub[25, 39] = 355;
										num3 = 324;
										if (PostRole())
										{
											continue;
										}
										goto case 99;
									case 338:
										_RecordStub[32, 29] = 524;
										_RecordStub[35, 0] = 523;
										goto case 342;
									case 337:
										_ReponseStub[15, 168] = 111;
										_ReponseStub[35, 129] = 110;
										DestroyRole(_ReponseStub, 20, 45, 109);
										_ReponseStub[38, 10] = 108;
										_ReponseStub[57, 171] = 107;
										_ReponseStub[44, 190] = 106;
										_ReponseStub[40, 56] = 105;
										_ReponseStub[36, 156] = 104;
										_ReponseStub[3, 88] = 103;
										_ReponseStub[50, 122] = 102;
										_ReponseStub[36, 7] = 101;
										_ReponseStub[39, 43] = 100;
										_ReponseStub[15, 166] = 99;
										DestroyRole(_ReponseStub, 42, 136, 98);
										_ReponseStub[22, 131] = 97;
										_ReponseStub[44, 23] = 96;
										goto case 50;
									case 50:
										_ReponseStub[54, 147] = 95;
										_ReponseStub[41, 32] = 94;
										_ReponseStub[23, 121] = 93;
										_ReponseStub[39, 108] = 92;
										_ReponseStub[2, 78] = 91;
										goto case 227;
									case 227:
										_ReponseStub[40, 155] = 90;
										_ReponseStub[55, 51] = 89;
										_ReponseStub[19, 34] = 88;
										goto case 23;
									case 23:
										DestroyRole(_ReponseStub, 48, 128, 87);
										goto case 336;
									case 336:
										_ReponseStub[48, 159] = 86;
										_ReponseStub[20, 70] = 85;
										_ReponseStub[34, 71] = 84;
										_ReponseStub[16, 31] = 83;
										_ReponseStub[42, 157] = 82;
										_ReponseStub[20, 44] = 81;
										_ReponseStub[11, 92] = 80;
										_ReponseStub[44, 180] = 79;
										_ReponseStub[84, 33] = 78;
										_ReponseStub[16, 116] = 77;
										_ReponseStub[61, 163] = 76;
										_ReponseStub[35, 164] = 75;
										goto case 145;
									case 145:
										_ReponseStub[36, 42] = 74;
										_ReponseStub[13, 40] = 73;
										_ReponseStub[43, 176] = 72;
										DestroyRole(_ReponseStub, 2, 66, 71);
										DestroyRole(_ReponseStub, 20, 133, 70);
										DestroyRole(_ReponseStub, 36, 65, 69);
										_ReponseStub[38, 33] = 68;
										DestroyRole(_ReponseStub, 12, 91, 67);
										_ReponseStub[36, 26] = 66;
										_ReponseStub[15, 174] = 65;
										_ReponseStub[77, 32] = 64;
										DestroyRole(_ReponseStub, 16, 1, 63);
										_ReponseStub[25, 86] = 62;
										_ReponseStub[17, 13] = 61;
										goto case 365;
									case 335:
										importerStub[0, 11] = 318;
										importerStub[84, 190] = 317;
										goto case 206;
									case 206:
										importerStub[76, 166] = 316;
										importerStub[14, 72] = 315;
										importerStub[67, 144] = 314;
										DestroyRole(importerStub, 84, 44, 313);
										importerStub[72, 125] = 312;
										goto case 304;
									case 304:
										importerStub[66, 127] = 311;
										importerStub[60, 25] = 310;
										importerStub[70, 146] = 309;
										importerStub[79, 135] = 308;
										importerStub[54, 135] = 307;
										importerStub[60, 104] = 306;
										importerStub[55, 132] = 305;
										importerStub[94, 2] = 304;
										importerStub[54, 133] = 303;
										importerStub[56, 190] = 302;
										importerStub[58, 174] = 301;
										importerStub[80, 144] = 300;
										importerStub[85, 113] = 299;
										_RecordStub[31, 43] = 600;
										_RecordStub[19, 56] = 599;
										_RecordStub[38, 46] = 598;
										num3 = 168;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 215;
									case 334:
										_RecordStub[25, 31] = 16;
										_RecordStub[17, 90] = 15;
										_RecordStub[18, 40] = 14;
										_RecordStub[17, 77] = 13;
										_RecordStub[17, 35] = 12;
										_RecordStub[23, 52] = 11;
										goto case 243;
									case 243:
										_RecordStub[23, 35] = 10;
										_RecordStub[16, 5] = 9;
										_RecordStub[23, 58] = 8;
										goto case 347;
									case 332:
										_ReponseStub[44, 163] = 115;
										_ReponseStub[16, 173] = 114;
										DestroyRole(_ReponseStub, 43, 49, 113);
										_ReponseStub[20, 112] = 112;
										num3 = 337;
										if (PostRole())
										{
											continue;
										}
										goto case 236;
									case 329:
										eventStub[3, 3] = 598;
										eventStub[3, 24] = 597;
										eventStub[3, 30] = 596;
										eventStub[3, 42] = 595;
										eventStub[3, 46] = 594;
										eventStub[3, 39] = 593;
										eventStub[3, 11] = 592;
										eventStub[3, 37] = 591;
										eventStub[3, 38] = 590;
										goto case 361;
									case 326:
										_AttrStub[17, 109] = 330;
										_AttrStub[7, 76] = 329;
										_AttrStub[15, 15] = 328;
										_AttrStub[4, 14] = 327;
										goto case 121;
									case 324:
										importerStub[85, 129] = 354;
										goto case 284;
									case 284:
										DestroyRole(importerStub, 64, 98, 353);
										importerStub[67, 127] = 352;
										importerStub[72, 167] = 351;
										importerStub[57, 143] = 350;
										importerStub[76, 187] = 349;
										importerStub[83, 181] = 348;
										importerStub[84, 10] = 347;
										importerStub[55, 166] = 346;
										importerStub[55, 188] = 345;
										importerStub[13, 151] = 344;
										importerStub[62, 124] = 343;
										importerStub[53, 136] = 342;
										DestroyRole(importerStub, 106, 57, 341);
										importerStub[47, 166] = 340;
										importerStub[109, 30] = 339;
										importerStub[78, 114] = 338;
										importerStub[83, 19] = 337;
										importerStub[56, 162] = 336;
										importerStub[60, 177] = 335;
										importerStub[88, 9] = 334;
										importerStub[74, 163] = 333;
										goto case 58;
									case 58:
										importerStub[52, 156] = 332;
										importerStub[71, 180] = 331;
										DestroyRole(importerStub, 60, 57, 330);
										importerStub[72, 173] = 329;
										importerStub[82, 91] = 328;
										importerStub[51, 186] = 327;
										importerStub[75, 86] = 326;
										importerStub[75, 78] = 325;
										importerStub[76, 170] = 324;
										importerStub[60, 147] = 323;
										importerStub[82, 75] = 322;
										importerStub[80, 148] = 321;
										importerStub[86, 150] = 320;
										importerStub[13, 95] = 319;
										goto case 335;
									case 317:
										algoStub[43, 88] = 500;
										goto case 24;
									case 24:
										algoStub[85, 20] = 499;
										algoStub[57, 36] = 498;
										algoStub[43, 6] = 497;
										algoStub[86, 77] = 496;
										algoStub[42, 70] = 495;
										algoStub[49, 78] = 494;
										algoStub[36, 40] = 493;
										DestroyRole(algoStub, 42, 71, 492);
										algoStub[58, 49] = 491;
										algoStub[35, 20] = 490;
										algoStub[76, 20] = 489;
										algoStub[39, 25] = 488;
										algoStub[40, 34] = 487;
										DestroyRole(algoStub, 39, 76, 486);
										algoStub[40, 1] = 485;
										algoStub[59, 0] = 484;
										algoStub[39, 70] = 483;
										algoStub[46, 14] = 482;
										algoStub[68, 77] = 481;
										algoStub[38, 55] = 480;
										algoStub[35, 78] = 479;
										algoStub[84, 44] = 478;
										goto case 18;
									case 18:
										algoStub[36, 41] = 477;
										algoStub[37, 62] = 476;
										algoStub[65, 67] = 475;
										DestroyRole(algoStub, 69, 66, 474);
										algoStub[73, 55] = 473;
										algoStub[71, 49] = 472;
										algoStub[66, 87] = 471;
										goto default;
									default:
										DestroyRole(algoStub, 38, 33, 470);
										algoStub[64, 61] = 469;
										algoStub[35, 7] = 468;
										DestroyRole(algoStub, 47, 49, 467);
										algoStub[56, 14] = 466;
										DestroyRole(algoStub, 36, 49, 465);
										algoStub[50, 81] = 464;
										algoStub[55, 76] = 463;
										algoStub[35, 19] = 462;
										algoStub[44, 47] = 461;
										goto case 254;
									case 312:
										_ReponseStub[41, 128] = 291;
										_ReponseStub[2, 88] = 290;
										_ReponseStub[20, 123] = 289;
										_ReponseStub[35, 123] = 288;
										_ReponseStub[36, 28] = 287;
										num3 = 73;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 247;
									case 247:
										_ReponseStub[36, 2] = 130;
										_ReponseStub[66, 82] = 129;
										_ReponseStub[45, 166] = 128;
										DestroyRole(_ReponseStub, 4, 88, 127);
										_ReponseStub[16, 57] = 126;
										_ReponseStub[22, 116] = 125;
										_ReponseStub[36, 108] = 124;
										_ReponseStub[13, 48] = 123;
										_ReponseStub[54, 12] = 122;
										_ReponseStub[40, 136] = 121;
										_ReponseStub[36, 128] = 120;
										_ReponseStub[23, 6] = 119;
										_ReponseStub[38, 125] = 118;
										_ReponseStub[45, 154] = 117;
										_ReponseStub[51, 127] = 116;
										goto case 332;
									case 308:
										_AttrStub[16, 49] = 357;
										_AttrStub[6, 117] = 356;
										_AttrStub[36, 55] = 355;
										DestroyRole(_AttrStub, 5, 123, 354);
										_AttrStub[4, 126] = 353;
										_AttrStub[4, 119] = 352;
										DestroyRole(_AttrStub, 9, 95, 351);
										_AttrStub[5, 24] = 350;
										_AttrStub[16, 133] = 349;
										_AttrStub[10, 134] = 348;
										_AttrStub[26, 59] = 347;
										DestroyRole(_AttrStub, 6, 41, 346);
										_AttrStub[6, 146] = 345;
										_AttrStub[19, 24] = 344;
										_AttrStub[5, 113] = 343;
										_AttrStub[10, 118] = 342;
										_AttrStub[34, 151] = 341;
										_AttrStub[9, 72] = 340;
										_AttrStub[31, 25] = 339;
										_AttrStub[18, 126] = 338;
										_AttrStub[18, 28] = 337;
										goto case 164;
									case 164:
										DestroyRole(_AttrStub, 4, 153, 336);
										_AttrStub[3, 84] = 335;
										_AttrStub[21, 18] = 334;
										num3 = 56;
										if (PostRole())
										{
											continue;
										}
										goto case 243;
									case 307:
										DestroyRole(_AttrStub, 6, 121, 597);
										_AttrStub[3, 0] = 596;
										_AttrStub[5, 82] = 595;
										num3 = 353;
										if (PostRole())
										{
											continue;
										}
										goto case 28;
									case 28:
										_AttrStub[17, 99] = 414;
										_AttrStub[17, 57] = 413;
										num3 = 153;
										if (InvokeRole())
										{
										}
										continue;
									case 301:
										DestroyRole(serializerStub, 33, 89, 217);
										serializerStub[41, 28] = 216;
										serializerStub[31, 77] = 215;
										serializerStub[46, 1] = 214;
										serializerStub[47, 19] = 213;
										DestroyRole(serializerStub, 35, 55, 212);
										serializerStub[41, 21] = 211;
										serializerStub[27, 10] = 210;
										serializerStub[32, 77] = 209;
										DestroyRole(serializerStub, 26, 37, 208);
										serializerStub[20, 33] = 207;
										serializerStub[41, 52] = 206;
										serializerStub[32, 18] = 205;
										serializerStub[38, 13] = 204;
										serializerStub[20, 18] = 203;
										serializerStub[20, 24] = 202;
										serializerStub[45, 19] = 201;
										serializerStub[18, 53] = 200;
										_AttrStub[9, 89] = 600;
										DestroyRole(_AttrStub, 11, 15, 599);
										_AttrStub[3, 66] = 598;
										goto case 307;
									case 300:
										eventStub[18, 5] = 376;
										eventStub[28, 86] = 375;
										eventStub[30, 19] = 374;
										eventStub[42, 43] = 373;
										eventStub[36, 31] = 372;
										eventStub[17, 93] = 371;
										eventStub[4, 15] = 370;
										goto case 32;
									case 32:
										eventStub[21, 20] = 369;
										eventStub[23, 21] = 368;
										eventStub[28, 72] = 367;
										eventStub[4, 20] = 366;
										eventStub[26, 55] = 365;
										eventStub[21, 5] = 364;
										eventStub[19, 16] = 363;
										eventStub[23, 64] = 362;
										eventStub[40, 59] = 361;
										eventStub[37, 26] = 360;
										eventStub[26, 56] = 359;
										goto case 71;
									case 71:
										eventStub[4, 12] = 358;
										DestroyRole(eventStub, 33, 71, 357);
										eventStub[32, 39] = 356;
										DestroyRole(eventStub, 38, 40, 355);
										eventStub[22, 74] = 354;
										goto case 96;
									case 96:
										eventStub[3, 25] = 353;
										eventStub[15, 48] = 352;
										eventStub[41, 82] = 351;
										eventStub[41, 9] = 350;
										eventStub[25, 48] = 349;
										eventStub[31, 71] = 348;
										eventStub[43, 29] = 347;
										eventStub[26, 80] = 346;
										goto case 201;
									case 201:
										eventStub[4, 5] = 345;
										eventStub[18, 71] = 344;
										eventStub[29, 0] = 343;
										DestroyRole(eventStub, 43, 43, 342);
										eventStub[23, 81] = 341;
										DestroyRole(eventStub, 4, 42, 340);
										eventStub[44, 28] = 339;
										eventStub[23, 93] = 338;
										eventStub[17, 81] = 337;
										goto case 217;
									case 217:
										eventStub[25, 25] = 336;
										num3 = 210;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 327;
									case 299:
										eventStub[37, 91] = 582;
										eventStub[0, 27] = 581;
										DestroyRole(eventStub, 3, 18, 580);
										eventStub[3, 22] = 579;
										eventStub[3, 61] = 578;
										DestroyRole(eventStub, 3, 14, 577);
										eventStub[24, 80] = 576;
										eventStub[4, 82] = 575;
										eventStub[17, 80] = 574;
										eventStub[30, 44] = 573;
										eventStub[3, 73] = 572;
										eventStub[3, 64] = 571;
										DestroyRole(eventStub, 38, 14, 570);
										eventStub[33, 70] = 569;
										eventStub[3, 1] = 568;
										eventStub[3, 16] = 567;
										eventStub[3, 35] = 566;
										eventStub[3, 40] = 565;
										eventStub[4, 74] = 564;
										eventStub[4, 24] = 563;
										eventStub[42, 59] = 562;
										eventStub[3, 7] = 561;
										eventStub[3, 71] = 560;
										eventStub[3, 12] = 559;
										goto case 167;
									case 167:
										eventStub[15, 75] = 558;
										eventStub[3, 20] = 557;
										eventStub[4, 39] = 556;
										DestroyRole(eventStub, 34, 69, 555);
										DestroyRole(eventStub, 3, 28, 554);
										eventStub[35, 24] = 553;
										eventStub[3, 82] = 552;
										eventStub[28, 47] = 551;
										eventStub[3, 67] = 550;
										DestroyRole(eventStub, 37, 16, 549);
										eventStub[26, 93] = 548;
										eventStub[4, 1] = 547;
										eventStub[26, 85] = 546;
										eventStub[31, 14] = 545;
										eventStub[4, 3] = 544;
										eventStub[4, 72] = 543;
										eventStub[24, 51] = 542;
										eventStub[27, 51] = 541;
										eventStub[27, 49] = 540;
										eventStub[22, 77] = 539;
										eventStub[27, 10] = 538;
										eventStub[29, 68] = 537;
										eventStub[20, 35] = 536;
										goto case 9;
									case 9:
										eventStub[41, 11] = 535;
										eventStub[24, 70] = 534;
										eventStub[36, 61] = 533;
										eventStub[31, 23] = 532;
										DestroyRole(eventStub, 43, 16, 531);
										goto case 98;
									case 98:
										DestroyRole(eventStub, 23, 68, 530);
										eventStub[32, 15] = 529;
										eventStub[3, 32] = 528;
										eventStub[19, 53] = 527;
										eventStub[40, 83] = 526;
										eventStub[4, 14] = 525;
										goto case 268;
									case 298:
										DestroyRole(serializerStub, 21, 51, 554);
										serializerStub[30, 40] = 553;
										serializerStub[42, 92] = 552;
										serializerStub[31, 78] = 551;
										serializerStub[25, 82] = 550;
										serializerStub[47, 0] = 549;
										serializerStub[34, 19] = 548;
										serializerStub[47, 35] = 547;
										serializerStub[21, 63] = 546;
										DestroyRole(serializerStub, 43, 75, 545);
										serializerStub[21, 87] = 544;
										serializerStub[35, 59] = 543;
										goto case 10;
									case 10:
										serializerStub[25, 34] = 542;
										serializerStub[21, 27] = 541;
										serializerStub[39, 26] = 540;
										serializerStub[34, 26] = 539;
										DestroyRole(serializerStub, 39, 52, 538);
										DestroyRole(serializerStub, 50, 57, 537);
										serializerStub[37, 79] = 536;
										serializerStub[26, 24] = 535;
										serializerStub[22, 1] = 534;
										serializerStub[18, 40] = 533;
										DestroyRole(serializerStub, 41, 33, 532);
										serializerStub[53, 26] = 531;
										serializerStub[54, 86] = 530;
										serializerStub[20, 16] = 529;
										serializerStub[46, 74] = 528;
										DestroyRole(serializerStub, 30, 19, 527);
										serializerStub[45, 35] = 526;
										serializerStub[45, 61] = 525;
										goto case 128;
									case 128:
										serializerStub[30, 9] = 524;
										serializerStub[41, 53] = 523;
										goto case 239;
									case 239:
										serializerStub[41, 13] = 522;
										serializerStub[50, 34] = 521;
										goto case 77;
									case 77:
										serializerStub[53, 86] = 520;
										serializerStub[47, 47] = 519;
										serializerStub[22, 28] = 518;
										serializerStub[50, 53] = 517;
										serializerStub[39, 70] = 516;
										DestroyRole(serializerStub, 38, 15, 515);
										serializerStub[42, 88] = 514;
										serializerStub[16, 29] = 513;
										serializerStub[27, 90] = 512;
										serializerStub[29, 12] = 511;
										serializerStub[44, 22] = 510;
										serializerStub[34, 69] = 509;
										serializerStub[24, 10] = 508;
										serializerStub[44, 11] = 507;
										DestroyRole(serializerStub, 39, 92, 506);
										DestroyRole(serializerStub, 49, 48, 505);
										serializerStub[31, 46] = 504;
										serializerStub[19, 50] = 503;
										goto case 64;
									case 64:
										serializerStub[21, 14] = 502;
										serializerStub[32, 28] = 501;
										serializerStub[18, 3] = 500;
										serializerStub[53, 9] = 499;
										serializerStub[34, 80] = 498;
										serializerStub[48, 88] = 497;
										serializerStub[46, 53] = 496;
										serializerStub[22, 53] = 495;
										serializerStub[28, 10] = 494;
										serializerStub[44, 65] = 493;
										serializerStub[20, 10] = 492;
										serializerStub[40, 76] = 491;
										serializerStub[47, 8] = 490;
										serializerStub[50, 74] = 489;
										serializerStub[23, 62] = 488;
										DestroyRole(serializerStub, 49, 65, 487);
										serializerStub[28, 87] = 486;
										serializerStub[15, 48] = 485;
										goto case 349;
									case 297:
										_ReponseStub[37, 0] = 326;
										goto case 51;
									case 51:
										_ReponseStub[26, 183] = 325;
										_ReponseStub[22, 66] = 324;
										_ReponseStub[35, 58] = 323;
										DestroyRole(_ReponseStub, 48, 117, 322);
										_ReponseStub[36, 102] = 321;
										_ReponseStub[22, 122] = 320;
										_ReponseStub[35, 11] = 319;
										_ReponseStub[46, 19] = 318;
										_ReponseStub[22, 49] = 317;
										DestroyRole(_ReponseStub, 48, 166, 316);
										_ReponseStub[41, 125] = 315;
										_ReponseStub[41, 1] = 314;
										_ReponseStub[35, 178] = 313;
										_ReponseStub[41, 12] = 312;
										_ReponseStub[26, 167] = 311;
										DestroyRole(_ReponseStub, 42, 152, 310);
										goto case 232;
									case 232:
										_ReponseStub[42, 46] = 309;
										DestroyRole(_ReponseStub, 42, 151, 308);
										_ReponseStub[20, 135] = 307;
										_ReponseStub[37, 162] = 306;
										_ReponseStub[37, 50] = 305;
										goto case 109;
									case 295:
										_ReponseStub[52, 59] = 225;
										_ReponseStub[38, 41] = 224;
										_ReponseStub[37, 127] = 223;
										DestroyRole(_ReponseStub, 22, 175, 222);
										_ReponseStub[44, 30] = 221;
										_ReponseStub[47, 178] = 220;
										_ReponseStub[43, 99] = 219;
										_ReponseStub[19, 4] = 218;
										_ReponseStub[37, 97] = 217;
										_ReponseStub[38, 181] = 216;
										_ReponseStub[45, 103] = 215;
										_ReponseStub[1, 86] = 214;
										_ReponseStub[40, 15] = 213;
										_ReponseStub[22, 136] = 212;
										_ReponseStub[75, 165] = 211;
										_ReponseStub[36, 15] = 210;
										_ReponseStub[46, 80] = 209;
										_ReponseStub[59, 55] = 208;
										DestroyRole(_ReponseStub, 37, 108, 207);
										goto case 209;
									case 209:
										_ReponseStub[21, 109] = 206;
										_ReponseStub[24, 165] = 205;
										_ReponseStub[79, 158] = 204;
										_ReponseStub[44, 139] = 203;
										_ReponseStub[36, 124] = 202;
										_ReponseStub[42, 185] = 201;
										_ReponseStub[39, 186] = 200;
										_ReponseStub[22, 128] = 199;
										_ReponseStub[40, 44] = 198;
										DestroyRole(_ReponseStub, 41, 105, 197);
										_ReponseStub[1, 70] = 196;
										_ReponseStub[1, 68] = 195;
										_ReponseStub[53, 22] = 194;
										DestroyRole(_ReponseStub, 36, 54, 193);
										_ReponseStub[47, 147] = 192;
										_ReponseStub[35, 36] = 191;
										DestroyRole(_ReponseStub, 35, 185, 190);
										_ReponseStub[45, 37] = 189;
										_ReponseStub[43, 163] = 188;
										DestroyRole(_ReponseStub, 56, 115, 187);
										_ReponseStub[38, 164] = 186;
										_ReponseStub[35, 141] = 185;
										_ReponseStub[42, 132] = 184;
										_ReponseStub[46, 120] = 183;
										_ReponseStub[69, 142] = 182;
										_ReponseStub[38, 175] = 181;
										_ReponseStub[22, 112] = 180;
										_ReponseStub[38, 142] = 179;
										DestroyRole(_ReponseStub, 40, 37, 178);
										num3 = 156;
										if (PostRole())
										{
											continue;
										}
										goto case 310;
									case 293:
										_ReponseStub[15, 156] = 160;
										DestroyRole(_ReponseStub, 36, 155, 159);
										_ReponseStub[44, 25] = 158;
										_ReponseStub[38, 12] = 157;
										_ReponseStub[38, 140] = 156;
										goto case 249;
									case 249:
										_ReponseStub[23, 4] = 155;
										_ReponseStub[45, 149] = 154;
										_ReponseStub[22, 189] = 153;
										goto case 149;
									case 149:
										_ReponseStub[38, 147] = 152;
										_ReponseStub[27, 5] = 151;
										_ReponseStub[22, 42] = 150;
										_ReponseStub[3, 68] = 149;
										_ReponseStub[39, 51] = 148;
										_ReponseStub[36, 29] = 147;
										_ReponseStub[20, 108] = 146;
										_ReponseStub[50, 57] = 145;
										_ReponseStub[55, 104] = 144;
										_ReponseStub[22, 46] = 143;
										_ReponseStub[18, 164] = 142;
										_ReponseStub[50, 159] = 141;
										_ReponseStub[85, 131] = 140;
										DestroyRole(_ReponseStub, 26, 79, 139);
										DestroyRole(_ReponseStub, 38, 100, 138);
										_ReponseStub[53, 112] = 137;
										_ReponseStub[20, 190] = 136;
										_ReponseStub[14, 69] = 135;
										_ReponseStub[23, 11] = 134;
										_ReponseStub[40, 114] = 133;
										DestroyRole(_ReponseStub, 40, 148, 132);
										_ReponseStub[53, 130] = 131;
										goto case 247;
									case 291:
										eventStub[45, 29] = 246;
										eventStub[3, 15] = 245;
										eventStub[18, 54] = 244;
										eventStub[3, 44] = 243;
										DestroyRole(eventStub, 31, 29, 242);
										eventStub[18, 45] = 241;
										eventStub[38, 28] = 240;
										eventStub[24, 12] = 239;
										eventStub[35, 82] = 238;
										eventStub[17, 43] = 237;
										eventStub[28, 9] = 236;
										eventStub[23, 25] = 235;
										eventStub[44, 37] = 234;
										eventStub[23, 75] = 233;
										goto case 22;
									case 22:
										eventStub[23, 92] = 232;
										eventStub[0, 24] = 231;
										eventStub[19, 74] = 230;
										DestroyRole(eventStub, 45, 32, 229);
										eventStub[16, 72] = 228;
										eventStub[16, 93] = 227;
										eventStub[45, 13] = 226;
										eventStub[24, 8] = 225;
										eventStub[25, 47] = 224;
										eventStub[28, 26] = 223;
										num3 = 95;
										if (PostRole())
										{
											continue;
										}
										goto case 299;
									case 289:
										DestroyRole(_ReponseStub, 59, 23, 19);
										_ReponseStub[18, 47] = 18;
										DestroyRole(_ReponseStub, 45, 134, 17);
										_ReponseStub[37, 59] = 16;
										_ReponseStub[21, 128] = 15;
										_ReponseStub[36, 106] = 14;
										goto case 255;
									case 285:
										DestroyRole(_AttrStub, 23, 107, 392);
										_AttrStub[9, 6] = 391;
										DestroyRole(_AttrStub, 12, 86, 390);
										_AttrStub[23, 112] = 389;
										_AttrStub[37, 23] = 388;
										_AttrStub[3, 138] = 387;
										_AttrStub[20, 68] = 386;
										_AttrStub[15, 116] = 385;
										DestroyRole(_AttrStub, 18, 64, 384);
										DestroyRole(_AttrStub, 12, 139, 383);
										_AttrStub[11, 155] = 382;
										_AttrStub[4, 156] = 381;
										_AttrStub[12, 84] = 380;
										_AttrStub[18, 49] = 379;
										_AttrStub[25, 125] = 378;
										DestroyRole(_AttrStub, 25, 147, 377);
										_AttrStub[15, 110] = 376;
										_AttrStub[19, 96] = 375;
										_AttrStub[30, 152] = 374;
										_AttrStub[6, 31] = 373;
										_AttrStub[27, 117] = 372;
										goto case 182;
									case 182:
										_AttrStub[3, 10] = 371;
										_AttrStub[6, 131] = 370;
										_AttrStub[13, 112] = 369;
										_AttrStub[36, 156] = 368;
										_AttrStub[4, 60] = 367;
										_AttrStub[15, 121] = 366;
										_AttrStub[4, 112] = 365;
										_AttrStub[30, 142] = 364;
										DestroyRole(_AttrStub, 23, 154, 363);
										_AttrStub[27, 101] = 362;
										_AttrStub[9, 140] = 361;
										DestroyRole(_AttrStub, 3, 89, 360);
										_AttrStub[18, 148] = 359;
										_AttrStub[4, 69] = 358;
										goto case 308;
									case 278:
										importerStub[85, 106] = 530;
										importerStub[6, 184] = 529;
										DestroyRole(importerStub, 57, 156, 528);
										importerStub[75, 104] = 527;
										importerStub[50, 137] = 526;
										importerStub[79, 133] = 525;
										importerStub[76, 108] = 524;
										importerStub[57, 142] = 523;
										importerStub[84, 130] = 522;
										goto case 93;
									case 93:
										importerStub[52, 128] = 521;
										goto case 272;
									case 272:
										importerStub[47, 44] = 520;
										importerStub[52, 152] = 519;
										importerStub[54, 104] = 518;
										goto case 131;
									case 131:
										DestroyRole(importerStub, 30, 47, 517);
										importerStub[71, 123] = 516;
										DestroyRole(importerStub, 52, 107, 515);
										importerStub[45, 84] = 514;
										importerStub[107, 118] = 513;
										importerStub[5, 161] = 512;
										DestroyRole(importerStub, 48, 126, 511);
										importerStub[67, 170] = 510;
										importerStub[43, 6] = 509;
										importerStub[70, 112] = 508;
										importerStub[86, 174] = 507;
										importerStub[84, 166] = 506;
										importerStub[79, 130] = 505;
										DestroyRole(importerStub, 57, 141, 504);
										DestroyRole(importerStub, 81, 178, 503);
										DestroyRole(importerStub, 56, 187, 502);
										importerStub[81, 162] = 501;
										importerStub[53, 104] = 500;
										DestroyRole(importerStub, 123, 35, 499);
										importerStub[70, 169] = 498;
										importerStub[69, 164] = 497;
										DestroyRole(importerStub, 109, 61, 496);
										importerStub[73, 130] = 495;
										DestroyRole(importerStub, 62, 134, 494);
										DestroyRole(importerStub, 54, 125, 493);
										importerStub[79, 105] = 492;
										importerStub[70, 165] = 491;
										goto case 374;
									case 277:
										serializerStub[26, 64] = 562;
										goto case 169;
									case 169:
										serializerStub[54, 51] = 561;
										serializerStub[54, 36] = 560;
										serializerStub[39, 4] = 559;
										serializerStub[53, 13] = 558;
										serializerStub[24, 92] = 557;
										serializerStub[27, 49] = 556;
										serializerStub[48, 6] = 555;
										goto case 298;
									case 276:
										_AttrStub[4, 91] = 223;
										_AttrStub[7, 13] = 222;
										_AttrStub[17, 116] = 221;
										num3 = 369;
										if (PostRole())
										{
											continue;
										}
										goto case 132;
									case 275:
										_ReponseStub[22, 114] = 328;
										_ReponseStub[5, 95] = 327;
										goto case 297;
									case 274:
										_AttrStub[5, 33] = 519;
										_AttrStub[9, 43] = 518;
										_AttrStub[20, 12] = 517;
										_AttrStub[20, 13] = 516;
										_AttrStub[5, 156] = 515;
										_AttrStub[22, 140] = 514;
										DestroyRole(_AttrStub, 8, 146, 513);
										DestroyRole(_AttrStub, 21, 123, 512);
										_AttrStub[4, 90] = 511;
										_AttrStub[5, 62] = 510;
										_AttrStub[17, 59] = 509;
										_AttrStub[10, 37] = 508;
										_AttrStub[18, 107] = 507;
										goto case 8;
									case 8:
										DestroyRole(_AttrStub, 14, 53, 506);
										_AttrStub[22, 51] = 505;
										DestroyRole(_AttrStub, 8, 13, 504);
										DestroyRole(_AttrStub, 5, 29, 503);
										DestroyRole(_AttrStub, 9, 7, 502);
										_AttrStub[22, 14] = 501;
										_AttrStub[8, 55] = 500;
										num3 = 187;
										if (PostRole())
										{
											continue;
										}
										goto case 359;
									case 273:
										_ReponseStub[72, 5] = 402;
										_ReponseStub[36, 107] = 401;
										DestroyRole(_ReponseStub, 49, 116, 400);
										_ReponseStub[73, 30] = 399;
										_ReponseStub[6, 90] = 398;
										goto case 129;
									case 129:
										_ReponseStub[2, 70] = 397;
										DestroyRole(_ReponseStub, 17, 141, 396);
										_ReponseStub[35, 62] = 395;
										_ReponseStub[16, 180] = 394;
										goto case 216;
									case 216:
										_ReponseStub[4, 91] = 393;
										_ReponseStub[15, 171] = 392;
										_ReponseStub[35, 177] = 391;
										goto case 30;
									case 269:
										serializerStub[50, 5] = 366;
										serializerStub[33, 22] = 365;
										serializerStub[37, 57] = 364;
										DestroyRole(serializerStub, 28, 47, 363);
										serializerStub[42, 31] = 362;
										goto case 35;
									case 35:
										serializerStub[18, 2] = 361;
										serializerStub[43, 64] = 360;
										serializerStub[23, 47] = 359;
										serializerStub[28, 79] = 358;
										num3 = 83;
										if (PostRole())
										{
											continue;
										}
										goto case 10;
									case 267:
										_AttrStub[13, 104] = 569;
										_AttrStub[3, 132] = 568;
										_AttrStub[26, 64] = 567;
										_AttrStub[7, 19] = 566;
										_AttrStub[4, 12] = 565;
										_AttrStub[11, 124] = 564;
										_AttrStub[7, 89] = 563;
										_AttrStub[15, 124] = 562;
										_AttrStub[4, 108] = 561;
										_AttrStub[19, 66] = 560;
										_AttrStub[3, 21] = 559;
										_AttrStub[24, 12] = 558;
										_AttrStub[28, 111] = 557;
										_AttrStub[12, 107] = 556;
										_AttrStub[3, 112] = 555;
										_AttrStub[8, 113] = 554;
										_AttrStub[5, 40] = 553;
										_AttrStub[26, 145] = 552;
										_AttrStub[3, 48] = 551;
										_AttrStub[3, 70] = 550;
										goto case 21;
									case 21:
										_AttrStub[22, 17] = 549;
										_AttrStub[16, 47] = 548;
										DestroyRole(_AttrStub, 3, 53, 547);
										_AttrStub[4, 24] = 546;
										DestroyRole(_AttrStub, 32, 120, 545);
										_AttrStub[24, 49] = 544;
										_AttrStub[24, 142] = 543;
										goto case 199;
									case 199:
										DestroyRole(_AttrStub, 18, 66, 542);
										DestroyRole(_AttrStub, 29, 150, 541);
										_AttrStub[5, 122] = 540;
										_AttrStub[5, 114] = 539;
										_AttrStub[3, 44] = 538;
										_AttrStub[10, 128] = 537;
										_AttrStub[15, 20] = 536;
										_AttrStub[13, 33] = 535;
										goto case 240;
									case 240:
										_AttrStub[14, 87] = 534;
										goto case 120;
									case 120:
										DestroyRole(_AttrStub, 3, 126, 533);
										_AttrStub[4, 53] = 532;
										_AttrStub[4, 40] = 531;
										_AttrStub[9, 93] = 530;
										_AttrStub[15, 137] = 529;
										goto case 102;
									case 102:
										_AttrStub[10, 123] = 528;
										_AttrStub[4, 56] = 527;
										_AttrStub[5, 71] = 526;
										_AttrStub[10, 8] = 525;
										_AttrStub[5, 16] = 524;
										num3 = 97;
										if (!InvokeRole())
										{
											continue;
										}
										goto case 232;
									case 263:
										_RecordStub[19, 41] = 52;
										_RecordStub[31, 55] = 51;
										DestroyRole(_RecordStub, 21, 39, 50);
										DestroyRole(_RecordStub, 35, 9, 49);
										_RecordStub[30, 15] = 48;
										_RecordStub[20, 52] = 47;
										_RecordStub[35, 71] = 46;
										_RecordStub[20, 7] = 45;
										_RecordStub[29, 72] = 44;
										DestroyRole(_RecordStub, 37, 77, 43);
										_RecordStub[22, 35] = 42;
										_RecordStub[20, 61] = 41;
										_RecordStub[31, 60] = 40;
										_RecordStub[20, 93] = 39;
										_RecordStub[27, 92] = 38;
										DestroyRole(_RecordStub, 28, 16, 37);
										_RecordStub[36, 26] = 36;
										_RecordStub[18, 89] = 35;
										_RecordStub[21, 63] = 34;
										_RecordStub[22, 52] = 33;
										_RecordStub[24, 65] = 32;
										_RecordStub[31, 8] = 31;
										_RecordStub[31, 49] = 30;
										_RecordStub[33, 30] = 29;
										_RecordStub[37, 15] = 28;
										_RecordStub[18, 18] = 27;
										DestroyRole(_RecordStub, 25, 50, 26);
										_RecordStub[29, 20] = 25;
										_RecordStub[35, 48] = 24;
										_RecordStub[38, 75] = 23;
										_RecordStub[26, 83] = 22;
										_RecordStub[21, 87] = 21;
										_RecordStub[27, 71] = 20;
										_RecordStub[32, 91] = 19;
										_RecordStub[25, 73] = 18;
										_RecordStub[16, 84] = 17;
										goto case 334;
									case 262:
										_ReponseStub[36, 34] = 248;
										_ReponseStub[44, 148] = 247;
										DestroyRole(_ReponseStub, 35, 3, 246);
										goto case 85;
									case 85:
										_ReponseStub[36, 114] = 245;
										DestroyRole(_ReponseStub, 42, 112, 244);
										_ReponseStub[35, 183] = 243;
										_ReponseStub[49, 73] = 242;
										_ReponseStub[39, 2] = 241;
										_ReponseStub[38, 121] = 240;
										goto case 230;
									case 230:
										_ReponseStub[44, 114] = 239;
										_ReponseStub[49, 32] = 238;
										_ReponseStub[1, 65] = 237;
										DestroyRole(_ReponseStub, 38, 25, 236);
										_ReponseStub[39, 4] = 235;
										_ReponseStub[42, 62] = 234;
										_ReponseStub[35, 40] = 233;
										_ReponseStub[24, 2] = 232;
										_ReponseStub[53, 49] = 231;
										DestroyRole(_ReponseStub, 41, 133, 230);
										_ReponseStub[43, 134] = 229;
										_ReponseStub[3, 83] = 228;
										_ReponseStub[38, 158] = 227;
										_ReponseStub[24, 17] = 226;
										goto case 295;
									case 260:
										_ReponseStub[42, 15] = 253;
										_ReponseStub[11, 83] = 252;
										_ReponseStub[0, 94] = 251;
										_ReponseStub[122, 81] = 250;
										_ReponseStub[41, 26] = 249;
										goto case 262;
									case 256:
										serializerStub[22, 70] = 324;
										num3 = 152;
										if (PostRole())
										{
											continue;
										}
										goto case 222;
									case 252:
										algoStub[36, 0] = 454;
										algoStub[70, 88] = 453;
										algoStub[42, 22] = 452;
										algoStub[46, 58] = 451;
										algoStub[36, 34] = 450;
										goto case 366;
									case 248:
										serializerStub[19, 78] = 251;
										serializerStub[15, 75] = 250;
										goto case 279;
									case 246:
										algoStub[47, 11] = 252;
										algoStub[52, 8] = 251;
										algoStub[82, 81] = 250;
										algoStub[36, 57] = 249;
										algoStub[38, 54] = 248;
										algoStub[43, 81] = 247;
										goto case 218;
									case 218:
										algoStub[37, 42] = 246;
										DestroyRole(algoStub, 40, 18, 245);
										algoStub[80, 90] = 244;
										DestroyRole(algoStub, 37, 84, 243);
										algoStub[57, 15] = 242;
										algoStub[38, 87] = 241;
										algoStub[37, 32] = 240;
										algoStub[53, 53] = 239;
										DestroyRole(algoStub, 89, 29, 238);
										algoStub[81, 53] = 237;
										algoStub[75, 3] = 236;
										DestroyRole(algoStub, 83, 73, 235);
										algoStub[66, 13] = 234;
										algoStub[48, 7] = 233;
										algoStub[46, 35] = 232;
										DestroyRole(algoStub, 35, 86, 231);
										algoStub[37, 20] = 230;
										algoStub[46, 80] = 229;
										algoStub[38, 24] = 228;
										DestroyRole(algoStub, 41, 68, 227);
										algoStub[42, 21] = 226;
										algoStub[43, 32] = 225;
										algoStub[38, 20] = 224;
										algoStub[37, 59] = 223;
										DestroyRole(algoStub, 41, 77, 222);
										algoStub[59, 57] = 221;
										algoStub[68, 59] = 220;
										algoStub[39, 43] = 219;
										algoStub[54, 39] = 218;
										algoStub[48, 28] = 217;
										algoStub[54, 28] = 216;
										algoStub[41, 44] = 215;
										algoStub[51, 64] = 214;
										algoStub[47, 72] = 213;
										DestroyRole(algoStub, 62, 67, 212);
										algoStub[42, 43] = 211;
										algoStub[61, 38] = 210;
										algoStub[76, 25] = 209;
										algoStub[48, 91] = 208;
										algoStub[36, 36] = 207;
										DestroyRole(algoStub, 80, 32, 206);
										goto case 355;
									case 242:
										DestroyRole(eventStub, 19, 21, 280);
										eventStub[46, 31] = 279;
										eventStub[20, 64] = 278;
										eventStub[26, 63] = 277;
										eventStub[22, 23] = 276;
										eventStub[25, 81] = 275;
										eventStub[4, 62] = 274;
										eventStub[37, 31] = 273;
										eventStub[40, 52] = 272;
										eventStub[29, 79] = 271;
										eventStub[41, 48] = 270;
										DestroyRole(eventStub, 31, 57, 269);
										eventStub[32, 92] = 268;
										eventStub[36, 36] = 267;
										eventStub[27, 7] = 266;
										eventStub[35, 29] = 265;
										DestroyRole(eventStub, 37, 34, 264);
										eventStub[34, 42] = 263;
										eventStub[27, 15] = 262;
										eventStub[33, 27] = 261;
										eventStub[31, 38] = 260;
										DestroyRole(eventStub, 19, 79, 259);
										eventStub[4, 31] = 258;
										eventStub[4, 66] = 257;
										DestroyRole(eventStub, 17, 32, 256);
										eventStub[26, 67] = 255;
										eventStub[16, 30] = 254;
										eventStub[26, 46] = 253;
										eventStub[24, 26] = 252;
										eventStub[35, 10] = 251;
										eventStub[18, 37] = 250;
										eventStub[3, 19] = 249;
										eventStub[33, 69] = 248;
										eventStub[31, 9] = 247;
										goto case 291;
									case 241:
										_RecordStub[30, 31] = 571;
										goto case 343;
									case 233:
										algoStub[57, 83] = 456;
										DestroyRole(algoStub, 42, 46, 455);
										goto case 252;
									case 231:
										goto IL_dd4c;
									case 226:
										_AttrStub[9, 12] = 280;
										_AttrStub[18, 133] = 279;
										_AttrStub[4, 0] = 278;
										_AttrStub[7, 155] = 277;
										_AttrStub[9, 144] = 276;
										_AttrStub[23, 49] = 275;
										_AttrStub[5, 89] = 274;
										_AttrStub[10, 11] = 273;
										_AttrStub[3, 110] = 272;
										DestroyRole(_AttrStub, 3, 40, 271);
										_AttrStub[29, 115] = 270;
										_AttrStub[9, 100] = 269;
										_AttrStub[21, 67] = 268;
										_AttrStub[23, 145] = 267;
										_AttrStub[10, 47] = 266;
										_AttrStub[4, 31] = 265;
										_AttrStub[4, 81] = 264;
										_AttrStub[22, 62] = 263;
										_AttrStub[4, 28] = 262;
										_AttrStub[27, 39] = 261;
										_AttrStub[27, 54] = 260;
										_AttrStub[32, 46] = 259;
										_AttrStub[4, 76] = 258;
										_AttrStub[26, 15] = 257;
										_AttrStub[12, 154] = 256;
										_AttrStub[9, 150] = 255;
										_AttrStub[15, 17] = 254;
										_AttrStub[5, 129] = 253;
										_AttrStub[10, 40] = 252;
										_AttrStub[13, 37] = 251;
										_AttrStub[31, 104] = 250;
										_AttrStub[3, 152] = 249;
										DestroyRole(_AttrStub, 5, 22, 248);
										DestroyRole(_AttrStub, 8, 48, 247);
										_AttrStub[4, 74] = 246;
										_AttrStub[6, 17] = 245;
										_AttrStub[30, 82] = 244;
										_AttrStub[4, 116] = 243;
										_AttrStub[16, 42] = 242;
										_AttrStub[5, 55] = 241;
										_AttrStub[4, 64] = 240;
										_AttrStub[14, 19] = 239;
										_AttrStub[35, 82] = 238;
										_AttrStub[30, 139] = 237;
										_AttrStub[26, 152] = 236;
										DestroyRole(_AttrStub, 32, 32, 235);
										_AttrStub[21, 102] = 234;
										_AttrStub[10, 131] = 233;
										_AttrStub[9, 128] = 232;
										_AttrStub[3, 87] = 231;
										_AttrStub[4, 51] = 230;
										_AttrStub[10, 15] = 229;
										_AttrStub[4, 150] = 228;
										_AttrStub[7, 4] = 227;
										_AttrStub[7, 51] = 226;
										_AttrStub[7, 157] = 225;
										_AttrStub[4, 146] = 224;
										goto case 276;
									case 225:
										_RecordStub[30, 81] = 292;
										DestroyRole(_RecordStub, 19, 40, 291);
										DestroyRole(_RecordStub, 24, 47, 290);
										_RecordStub[17, 56] = 289;
										_RecordStub[39, 80] = 288;
										_RecordStub[30, 46] = 287;
										_RecordStub[16, 61] = 286;
										_RecordStub[26, 78] = 285;
										goto case 1;
									case 1:
										_RecordStub[26, 57] = 284;
										goto case 357;
									case 224:
										_ReponseStub[39, 101] = 266;
										_ReponseStub[18, 45] = 265;
										_ReponseStub[40, 121] = 264;
										_ReponseStub[45, 41] = 263;
										_ReponseStub[22, 167] = 262;
										_ReponseStub[26, 149] = 261;
										DestroyRole(_ReponseStub, 15, 189, 260);
										num3 = 27;
										if (PostRole())
										{
											continue;
										}
										goto case 331;
									case 223:
										serializerStub[28, 91] = 258;
										serializerStub[32, 83] = 257;
										serializerStub[25, 75] = 256;
										serializerStub[53, 45] = 255;
										serializerStub[29, 85] = 254;
										serializerStub[53, 59] = 253;
										serializerStub[16, 2] = 252;
										goto case 248;
									case 220:
										importerStub[86, 76] = 368;
										importerStub[74, 132] = 367;
										importerStub[47, 97] = 366;
										DestroyRole(importerStub, 82, 137, 365);
										importerStub[94, 56] = 364;
										importerStub[92, 30] = 363;
										importerStub[19, 117] = 362;
										importerStub[48, 173] = 361;
										importerStub[2, 136] = 360;
										goto case 158;
									case 158:
										DestroyRole(importerStub, 7, 182, 359);
										goto case 340;
									case 210:
										eventStub[41, 23] = 335;
										eventStub[34, 35] = 334;
										DestroyRole(eventStub, 4, 53, 333);
										eventStub[28, 36] = 332;
										eventStub[4, 41] = 331;
										eventStub[25, 60] = 330;
										eventStub[23, 20] = 329;
										eventStub[3, 43] = 328;
										eventStub[24, 79] = 327;
										DestroyRole(eventStub, 29, 41, 326);
										eventStub[30, 83] = 325;
										eventStub[3, 50] = 324;
										eventStub[22, 18] = 323;
										eventStub[18, 3] = 322;
										eventStub[39, 30] = 321;
										DestroyRole(eventStub, 4, 28, 320);
										eventStub[21, 64] = 319;
										eventStub[4, 68] = 318;
										eventStub[17, 71] = 317;
										eventStub[27, 0] = 316;
										eventStub[39, 28] = 315;
										eventStub[30, 13] = 314;
										goto case 179;
									case 179:
										DestroyRole(eventStub, 36, 70, 313);
										eventStub[20, 82] = 312;
										DestroyRole(eventStub, 33, 38, 311);
										eventStub[44, 87] = 310;
										eventStub[34, 45] = 309;
										eventStub[4, 26] = 308;
										eventStub[24, 44] = 307;
										eventStub[38, 67] = 306;
										eventStub[38, 6] = 305;
										eventStub[30, 68] = 304;
										eventStub[15, 89] = 303;
										eventStub[24, 93] = 302;
										eventStub[40, 41] = 301;
										eventStub[38, 3] = 300;
										DestroyRole(eventStub, 28, 23, 299);
										eventStub[26, 17] = 298;
										eventStub[4, 38] = 297;
										DestroyRole(eventStub, 22, 78, 296);
										eventStub[15, 37] = 295;
										DestroyRole(eventStub, 25, 85, 294);
										eventStub[4, 9] = 293;
										goto case 138;
									case 138:
										eventStub[4, 7] = 292;
										eventStub[27, 53] = 291;
										eventStub[39, 29] = 290;
										eventStub[41, 43] = 289;
										eventStub[25, 62] = 288;
										eventStub[4, 48] = 287;
										eventStub[28, 28] = 286;
										eventStub[21, 40] = 285;
										eventStub[36, 73] = 284;
										eventStub[26, 39] = 283;
										eventStub[22, 54] = 282;
										eventStub[33, 5] = 281;
										goto case 242;
									case 205:
										eventStub[43, 2] = 66;
										eventStub[30, 18] = 65;
										eventStub[19, 35] = 64;
										eventStub[15, 68] = 63;
										eventStub[3, 36] = 62;
										eventStub[35, 40] = 61;
										eventStub[36, 32] = 60;
										eventStub[37, 14] = 59;
										eventStub[17, 11] = 58;
										num3 = 265;
										if (InvokeRole())
										{
										}
										continue;
									case 204:
										_ReponseStub[20, 33] = 26;
										_ReponseStub[29, 90] = 25;
										_ReponseStub[20, 103] = 24;
										_ReponseStub[37, 51] = 23;
										_ReponseStub[57, 0] = 22;
										_ReponseStub[40, 31] = 21;
										_ReponseStub[45, 32] = 20;
										goto case 289;
									case 203:
										serializerStub[53, 32] = 332;
										serializerStub[38, 68] = 331;
										serializerStub[45, 78] = 330;
										serializerStub[43, 7] = 329;
										serializerStub[46, 82] = 328;
										serializerStub[27, 38] = 327;
										serializerStub[16, 62] = 326;
										serializerStub[24, 17] = 325;
										goto case 256;
									case 200:
										_ReponseStub[53, 8] = 329;
										goto case 275;
									case 196:
										_AttrStub[3, 147] = 397;
										_AttrStub[15, 84] = 396;
										_AttrStub[16, 32] = 395;
										_AttrStub[16, 58] = 394;
										_AttrStub[7, 66] = 393;
										goto case 285;
									case 193:
										algoStub[40, 32] = 373;
										algoStub[76, 31] = 372;
										goto case 373;
									case 187:
										DestroyRole(_AttrStub, 33, 9, 499);
										_AttrStub[16, 64] = 498;
										_AttrStub[7, 131] = 497;
										_AttrStub[34, 4] = 496;
										goto case 370;
									case 180:
										algoStub[73, 54] = 257;
										goto case 171;
									case 171:
										algoStub[51, 61] = 256;
										algoStub[46, 57] = 255;
										algoStub[55, 21] = 254;
										DestroyRole(algoStub, 39, 66, 253);
										goto case 246;
									case 168:
										_RecordStub[3, 3] = 597;
										_RecordStub[29, 77] = 596;
										_RecordStub[19, 33] = 595;
										_RecordStub[30, 0] = 594;
										_RecordStub[29, 89] = 593;
										_RecordStub[31, 26] = 592;
										_RecordStub[31, 38] = 591;
										_RecordStub[32, 85] = 590;
										_RecordStub[15, 0] = 589;
										_RecordStub[16, 54] = 588;
										_RecordStub[15, 76] = 587;
										_RecordStub[31, 25] = 586;
										_RecordStub[23, 13] = 585;
										_RecordStub[28, 34] = 584;
										_RecordStub[18, 9] = 583;
										DestroyRole(_RecordStub, 29, 37, 582);
										DestroyRole(_RecordStub, 22, 45, 581);
										goto case 81;
									case 81:
										_RecordStub[19, 46] = 580;
										_RecordStub[16, 65] = 579;
										DestroyRole(_RecordStub, 23, 5, 578);
										_RecordStub[26, 70] = 577;
										_RecordStub[31, 53] = 576;
										_RecordStub[27, 12] = 575;
										_RecordStub[30, 67] = 574;
										_RecordStub[31, 57] = 573;
										goto case 63;
									case 63:
										_RecordStub[20, 20] = 572;
										num3 = 241;
										if (InvokeRole())
										{
										}
										continue;
									case 163:
										_RecordStub[24, 14] = 53;
										goto case 263;
									case 162:
										_RecordStub[18, 17] = 529;
										_RecordStub[30, 87] = 528;
										_RecordStub[29, 62] = 527;
										_RecordStub[29, 87] = 526;
										_RecordStub[34, 53] = 525;
										goto case 338;
									case 161:
										goto IL_efda;
									case 156:
										_ReponseStub[37, 109] = 177;
										_ReponseStub[40, 144] = 176;
										_ReponseStub[44, 117] = 175;
										DestroyRole(_ReponseStub, 35, 181, 174);
										_ReponseStub[26, 105] = 173;
										_ReponseStub[16, 48] = 172;
										_ReponseStub[44, 122] = 171;
										_ReponseStub[12, 86] = 170;
										_ReponseStub[84, 53] = 169;
										_ReponseStub[17, 44] = 168;
										_ReponseStub[59, 54] = 167;
										_ReponseStub[36, 98] = 166;
										_ReponseStub[45, 115] = 165;
										_ReponseStub[73, 9] = 164;
										_ReponseStub[44, 123] = 163;
										_ReponseStub[37, 188] = 162;
										_ReponseStub[51, 117] = 161;
										goto case 293;
									case 155:
										_ReponseStub[35, 146] = 279;
										_ReponseStub[24, 54] = 278;
										_ReponseStub[13, 110] = 277;
										_ReponseStub[23, 132] = 276;
										_ReponseStub[26, 102] = 275;
										DestroyRole(_ReponseStub, 55, 178, 274);
										_ReponseStub[17, 117] = 273;
										DestroyRole(_ReponseStub, 41, 161, 272);
										_ReponseStub[38, 150] = 271;
										_ReponseStub[10, 71] = 270;
										_ReponseStub[47, 60] = 269;
										_ReponseStub[16, 114] = 268;
										DestroyRole(_ReponseStub, 21, 47, 267);
										goto case 224;
									case 153:
										_AttrStub[8, 105] = 412;
										_AttrStub[5, 112] = 411;
										_AttrStub[20, 59] = 410;
										_AttrStub[6, 129] = 409;
										_AttrStub[18, 17] = 408;
										_AttrStub[3, 92] = 407;
										DestroyRole(_AttrStub, 28, 118, 406);
										_AttrStub[3, 109] = 405;
										goto case 45;
									case 45:
										_AttrStub[31, 51] = 404;
										_AttrStub[13, 116] = 403;
										DestroyRole(_AttrStub, 6, 15, 402);
										_AttrStub[36, 136] = 401;
										_AttrStub[12, 74] = 400;
										_AttrStub[20, 88] = 399;
										DestroyRole(_AttrStub, 36, 68, 398);
										goto case 196;
									case 152:
										serializerStub[52, 28] = 323;
										DestroyRole(serializerStub, 23, 40, 322);
										serializerStub[28, 50] = 321;
										serializerStub[42, 91] = 320;
										serializerStub[47, 76] = 319;
										serializerStub[15, 42] = 318;
										serializerStub[43, 55] = 317;
										serializerStub[29, 84] = 316;
										serializerStub[44, 90] = 315;
										serializerStub[53, 16] = 314;
										serializerStub[22, 93] = 313;
										serializerStub[34, 10] = 312;
										goto case 12;
									case 12:
										serializerStub[32, 53] = 311;
										serializerStub[43, 65] = 310;
										serializerStub[28, 7] = 309;
										serializerStub[35, 46] = 308;
										serializerStub[21, 39] = 307;
										serializerStub[44, 18] = 306;
										goto case 70;
									case 70:
										DestroyRole(serializerStub, 40, 10, 305);
										serializerStub[54, 53] = 304;
										goto case 339;
									case 148:
										importerStub[75, 161] = 560;
										importerStub[78, 130] = 559;
										importerStub[94, 30] = 558;
										importerStub[84, 72] = 557;
										importerStub[1, 67] = 556;
										importerStub[75, 172] = 555;
										importerStub[74, 185] = 554;
										importerStub[53, 160] = 553;
										importerStub[123, 14] = 552;
										importerStub[79, 97] = 551;
										DestroyRole(importerStub, 85, 110, 550);
										DestroyRole(importerStub, 78, 171, 549);
										importerStub[52, 131] = 548;
										importerStub[56, 100] = 547;
										importerStub[50, 182] = 546;
										importerStub[94, 64] = 545;
										importerStub[106, 74] = 544;
										importerStub[11, 102] = 543;
										importerStub[53, 124] = 542;
										importerStub[24, 3] = 541;
										importerStub[86, 148] = 540;
										importerStub[53, 184] = 539;
										importerStub[86, 147] = 538;
										goto case 75;
									case 75:
										DestroyRole(importerStub, 96, 161, 537);
										importerStub[82, 77] = 536;
										importerStub[59, 146] = 535;
										importerStub[84, 126] = 534;
										importerStub[79, 132] = 533;
										importerStub[85, 123] = 532;
										importerStub[71, 101] = 531;
										goto case 278;
									case 124:
										importerStub[71, 171] = 371;
										importerStub[84, 146] = 370;
										importerStub[20, 184] = 369;
										goto case 220;
									case 123:
										_RecordStub[29, 29] = 440;
										_RecordStub[15, 64] = 439;
										DestroyRole(_RecordStub, 26, 84, 438);
										_RecordStub[21, 90] = 437;
										goto case 119;
									case 119:
										_RecordStub[20, 24] = 436;
										_RecordStub[16, 18] = 435;
										_RecordStub[22, 23] = 434;
										_RecordStub[31, 14] = 433;
										_RecordStub[15, 1] = 432;
										_RecordStub[18, 63] = 431;
										_RecordStub[19, 10] = 430;
										goto case 175;
									case 116:
										DestroyRole(_AttrStub, 10, 79, 467);
										_AttrStub[15, 105] = 466;
										num3 = 2;
										if (PostRole())
										{
											continue;
										}
										goto case 164;
									case 111:
										serializerStub[39, 45] = 333;
										num3 = 203;
										if (InvokeRole())
										{
										}
										continue;
									case 107:
										serializerStub[50, 1] = 220;
										serializerStub[26, 88] = 219;
										serializerStub[36, 40] = 218;
										goto case 301;
									case 97:
										_AttrStub[5, 146] = 523;
										_AttrStub[18, 88] = 522;
										_AttrStub[24, 4] = 521;
										_AttrStub[20, 47] = 520;
										goto case 274;
									case 95:
										eventStub[43, 81] = 222;
										eventStub[32, 71] = 221;
										eventStub[18, 41] = 220;
										eventStub[26, 62] = 219;
										eventStub[41, 24] = 218;
										eventStub[40, 11] = 217;
										num3 = 344;
										if (PostRole())
										{
											continue;
										}
										goto case 81;
									case 94:
										serializerStub[40, 80] = 374;
										serializerStub[41, 92] = 373;
										serializerStub[27, 93] = 372;
										serializerStub[15, 17] = 371;
										goto case 5;
									case 5:
										serializerStub[16, 76] = 370;
										goto case 6;
									case 6:
										serializerStub[51, 12] = 369;
										DestroyRole(serializerStub, 18, 20, 368);
										serializerStub[15, 54] = 367;
										goto case 269;
									case 91:
										serializerStub[38, 64] = 225;
										serializerStub[18, 43] = 224;
										serializerStub[23, 69] = 223;
										DestroyRole(serializerStub, 28, 12, 222);
										serializerStub[50, 78] = 221;
										num3 = 107;
										if (PostRole())
										{
											continue;
										}
										goto case 124;
									case 83:
										serializerStub[25, 45] = 357;
										serializerStub[23, 91] = 356;
										serializerStub[22, 19] = 355;
										serializerStub[25, 46] = 354;
										serializerStub[22, 36] = 353;
										DestroyRole(serializerStub, 54, 85, 352);
										DestroyRole(serializerStub, 46, 20, 351);
										serializerStub[27, 37] = 350;
										DestroyRole(serializerStub, 26, 81, 349);
										serializerStub[42, 29] = 348;
										DestroyRole(serializerStub, 31, 90, 347);
										serializerStub[41, 59] = 346;
										serializerStub[24, 65] = 345;
										serializerStub[44, 84] = 344;
										serializerStub[24, 90] = 343;
										serializerStub[38, 54] = 342;
										DestroyRole(serializerStub, 28, 70, 341);
										serializerStub[27, 15] = 340;
										serializerStub[28, 80] = 339;
										serializerStub[29, 8] = 338;
										serializerStub[45, 80] = 337;
										serializerStub[53, 37] = 336;
										serializerStub[28, 65] = 335;
										DestroyRole(serializerStub, 23, 86, 334);
										goto case 111;
									case 79:
										serializerStub[32, 1] = 380;
										serializerStub[33, 76] = 379;
										serializerStub[34, 91] = 378;
										serializerStub[52, 36] = 377;
										serializerStub[26, 77] = 376;
										DestroyRole(serializerStub, 35, 48, 375);
										goto case 94;
									case 73:
										_ReponseStub[42, 188] = 286;
										_ReponseStub[42, 164] = 285;
										_ReponseStub[42, 4] = 284;
										_ReponseStub[43, 57] = 283;
										num3 = 19;
										if (InvokeRole())
										{
										}
										continue;
									case 56:
										DestroyRole(_AttrStub, 25, 129, 333);
										_AttrStub[6, 107] = 332;
										_AttrStub[12, 25] = 331;
										goto case 326;
									case 52:
										algoStub[35, 44] = 538;
										algoStub[48, 4] = 537;
										goto case 170;
									case 48:
										_ReponseStub[36, 180] = 334;
										_ReponseStub[37, 156] = 333;
										_ReponseStub[49, 13] = 332;
										_ReponseStub[41, 107] = 331;
										_ReponseStub[36, 56] = 330;
										goto case 200;
									case 46:
										DestroyRole(algoStub, 47, 18, 266);
										algoStub[37, 0] = 265;
										algoStub[37, 49] = 264;
										algoStub[67, 37] = 263;
										algoStub[36, 91] = 262;
										algoStub[75, 48] = 261;
										algoStub[75, 63] = 260;
										algoStub[83, 87] = 259;
										DestroyRole(algoStub, 37, 44, 258);
										goto case 180;
									case 29:
										importerStub[110, 12] = 390;
										importerStub[60, 162] = 389;
										importerStub[29, 115] = 388;
										importerStub[83, 130] = 387;
										importerStub[52, 136] = 386;
										importerStub[63, 114] = 385;
										importerStub[49, 127] = 384;
										importerStub[83, 109] = 383;
										importerStub[66, 128] = 382;
										importerStub[78, 136] = 381;
										importerStub[81, 180] = 380;
										importerStub[76, 104] = 379;
										importerStub[56, 156] = 378;
										importerStub[61, 23] = 377;
										importerStub[4, 30] = 376;
										importerStub[69, 154] = 375;
										importerStub[100, 37] = 374;
										importerStub[54, 177] = 373;
										importerStub[23, 119] = 372;
										goto case 124;
									case 27:
										_ReponseStub[41, 177] = 259;
										DestroyRole(_ReponseStub, 46, 36, 258);
										_ReponseStub[20, 40] = 257;
										_ReponseStub[41, 54] = 256;
										DestroyRole(_ReponseStub, 3, 87, 255);
										_ReponseStub[40, 16] = 254;
										goto case 260;
									case 19:
										_ReponseStub[39, 3] = 282;
										_ReponseStub[42, 3] = 281;
										DestroyRole(_ReponseStub, 57, 158, 280);
										goto case 155;
									case 15:
										_RecordStub[27, 18] = 404;
										DestroyRole(_RecordStub, 23, 87, 403);
										_RecordStub[35, 6] = 402;
										_RecordStub[34, 27] = 401;
										_RecordStub[39, 35] = 400;
										_RecordStub[30, 88] = 399;
										_RecordStub[32, 92] = 398;
										num3 = 190;
										if (PostRole())
										{
											continue;
										}
										goto IL_45b6;
									case 2:
										DestroyRole(_AttrStub, 3, 144, 465);
										DestroyRole(_AttrStub, 12, 80, 464);
										_AttrStub[15, 73] = 463;
										_AttrStub[3, 19] = 462;
										_AttrStub[8, 109] = 461;
										_AttrStub[3, 15] = 460;
										num3 = 134;
										if (PostRole())
										{
											continue;
										}
										goto end_IL_109de;
									case 114:
									case 283:
										goto IL_109aa;
									case 53:
										goto IL_109b6;
									case 113:
										goto end_IL_109c4;
									case 228:
										goto end_IL_109cd;
									case 61:
										goto end_IL_109d2;
									case 320:
										goto end_IL_109de;
									case 265:
										DestroyRole(eventStub, 19, 78, 57);
										eventStub[37, 11] = 56;
										goto case 26;
									case 26:
										eventStub[28, 63] = 55;
										goto case 323;
									case 323:
										eventStub[29, 61] = 54;
										eventStub[33, 3] = 53;
										eventStub[41, 52] = 52;
										eventStub[33, 63] = 51;
										eventStub[22, 41] = 50;
										eventStub[4, 19] = 49;
										goto case 288;
									case 288:
										eventStub[32, 41] = 48;
										eventStub[24, 4] = 47;
										eventStub[31, 28] = 46;
										goto case 36;
									case 36:
										eventStub[43, 30] = 45;
										eventStub[17, 3] = 44;
										eventStub[43, 70] = 43;
										DestroyRole(eventStub, 34, 19, 42);
										eventStub[20, 77] = 41;
										eventStub[18, 83] = 40;
										eventStub[17, 15] = 39;
										eventStub[23, 61] = 38;
										eventStub[40, 27] = 37;
										eventStub[16, 48] = 36;
										eventStub[39, 78] = 35;
										eventStub[41, 53] = 34;
										eventStub[40, 91] = 33;
										eventStub[40, 72] = 32;
										eventStub[18, 52] = 31;
										eventStub[35, 66] = 30;
										eventStub[39, 93] = 29;
										goto case 310;
									case 310:
										eventStub[19, 48] = 28;
										eventStub[26, 36] = 27;
										eventStub[27, 25] = 26;
										eventStub[42, 71] = 25;
										eventStub[42, 85] = 24;
										DestroyRole(eventStub, 26, 48, 23);
										eventStub[28, 15] = 22;
										goto case 195;
									case 195:
										eventStub[3, 66] = 21;
										eventStub[25, 24] = 20;
										eventStub[27, 43] = 19;
										eventStub[27, 78] = 18;
										eventStub[45, 43] = 17;
										eventStub[27, 72] = 16;
										DestroyRole(eventStub, 40, 29, 15);
										eventStub[41, 0] = 14;
										eventStub[19, 57] = 13;
										eventStub[15, 59] = 12;
										eventStub[29, 29] = 11;
										DestroyRole(eventStub, 4, 25, 10);
										eventStub[21, 42] = 9;
										DestroyRole(eventStub, 23, 35, 8);
										eventStub[33, 1] = 7;
										eventStub[4, 57] = 6;
										DestroyRole(eventStub, 17, 60, 5);
										DestroyRole(eventStub, 25, 19, 4);
										DestroyRole(eventStub, 22, 65, 3);
										eventStub[42, 29] = 2;
										goto case 362;
									case 362:
										eventStub[27, 66] = 1;
										eventStub[26, 89] = 0;
										return;
									case 376:
										return;
									}
									break;
								}
								goto IL_29e9;
								IL_109b6:
								num--;
								continue;
								IL_efda:
								while (num >= 0)
								{
									for (num2 = 93; num2 >= 0; num2--)
									{
										eventStub[num, num2] = 0;
									}
									num--;
								}
								serializerStub[20, 35] = 599;
								serializerStub[49, 26] = 598;
								serializerStub[41, 38] = 597;
								serializerStub[17, 26] = 596;
								serializerStub[32, 42] = 595;
								goto IL_dd4c;
								IL_2ae7:
								_RecordStub[30, 68] = 59;
								_RecordStub[18, 60] = 58;
								_RecordStub[15, 17] = 57;
								_RecordStub[23, 34] = 56;
								_RecordStub[20, 49] = 55;
								_RecordStub[15, 78] = 54;
								num3 = 163;
								if (InvokeRole())
								{
								}
								goto IL_103bc;
								IL_dd4c:
								serializerStub[39, 42] = 594;
								serializerStub[45, 49] = 593;
								serializerStub[51, 57] = 592;
								serializerStub[50, 47] = 591;
								serializerStub[42, 90] = 590;
								serializerStub[52, 65] = 589;
								serializerStub[53, 47] = 588;
								serializerStub[19, 82] = 587;
								serializerStub[31, 19] = 586;
								serializerStub[40, 46] = 585;
								serializerStub[24, 89] = 584;
								serializerStub[23, 85] = 583;
								serializerStub[20, 28] = 582;
								goto IL_72ec;
								IL_72ec:
								serializerStub[42, 20] = 581;
								serializerStub[34, 38] = 580;
								serializerStub[45, 9] = 579;
								serializerStub[54, 50] = 578;
								serializerStub[25, 44] = 577;
								serializerStub[35, 66] = 576;
								serializerStub[20, 55] = 575;
								serializerStub[18, 85] = 574;
								serializerStub[20, 31] = 573;
								serializerStub[49, 17] = 572;
								serializerStub[41, 16] = 571;
								serializerStub[35, 73] = 570;
								serializerStub[20, 34] = 569;
								serializerStub[29, 44] = 568;
								serializerStub[35, 38] = 567;
								serializerStub[49, 9] = 566;
								serializerStub[46, 33] = 565;
								DestroyRole(serializerStub, 49, 51, 564);
								serializerStub[40, 89] = 563;
								num3 = 277;
								if (InvokeRole())
								{
								}
								goto IL_103bc;
								IL_5acb:
								_RecordStub[19, 84] = 537;
								_RecordStub[23, 72] = 536;
								num3 = 345;
								if (InvokeRole())
								{
									goto IL_5b03;
								}
								goto IL_103bc;
								IL_5b03:
								_RecordStub[33, 74] = 170;
								DestroyRole(_RecordStub, 29, 40, 169);
								_RecordStub[15, 77] = 168;
								_RecordStub[32, 80] = 167;
								_RecordStub[30, 41] = 166;
								_RecordStub[23, 30] = 165;
								_RecordStub[24, 63] = 164;
								_RecordStub[30, 53] = 163;
								_RecordStub[39, 70] = 162;
								_RecordStub[23, 61] = 161;
								_RecordStub[37, 27] = 160;
								_RecordStub[16, 55] = 159;
								DestroyRole(_RecordStub, 22, 74, 158);
								_RecordStub[26, 50] = 157;
								_RecordStub[16, 10] = 156;
								_RecordStub[34, 63] = 155;
								DestroyRole(_RecordStub, 35, 14, 154);
								_RecordStub[17, 7] = 153;
								DestroyRole(_RecordStub, 15, 59, 152);
								goto IL_5c7e;
								IL_5c7e:
								_RecordStub[27, 23] = 151;
								goto IL_4105;
								IL_4105:
								_RecordStub[18, 70] = 150;
								_RecordStub[32, 56] = 149;
								_RecordStub[37, 87] = 148;
								_RecordStub[17, 61] = 147;
								_RecordStub[18, 83] = 146;
								_RecordStub[23, 86] = 145;
								_RecordStub[17, 31] = 144;
								goto IL_4191;
								IL_4191:
								_RecordStub[23, 83] = 143;
								_RecordStub[35, 2] = 142;
								_RecordStub[18, 64] = 141;
								_RecordStub[27, 43] = 140;
								_RecordStub[32, 42] = 139;
								_RecordStub[25, 76] = 138;
								_RecordStub[19, 85] = 137;
								_RecordStub[37, 81] = 136;
								_RecordStub[38, 83] = 135;
								_RecordStub[35, 7] = 134;
								goto IL_4257;
								IL_4257:
								_RecordStub[16, 51] = 133;
								_RecordStub[27, 22] = 132;
								_RecordStub[16, 76] = 131;
								_RecordStub[22, 4] = 130;
								DestroyRole(_RecordStub, 38, 84, 129);
								_RecordStub[17, 83] = 128;
								DestroyRole(_RecordStub, 24, 46, 127);
								_RecordStub[33, 15] = 126;
								_RecordStub[20, 48] = 125;
								_RecordStub[17, 30] = 124;
								_RecordStub[30, 93] = 123;
								DestroyRole(_RecordStub, 28, 11, 122);
								_RecordStub[28, 30] = 121;
								_RecordStub[15, 62] = 120;
								_RecordStub[17, 87] = 119;
								_RecordStub[32, 81] = 118;
								_RecordStub[23, 37] = 117;
								_RecordStub[30, 22] = 116;
								_RecordStub[32, 66] = 115;
								_RecordStub[33, 78] = 114;
								_RecordStub[21, 4] = 113;
								_RecordStub[31, 17] = 112;
								goto IL_43dd;
								IL_43dd:
								_RecordStub[39, 61] = 111;
								_RecordStub[18, 76] = 110;
								goto IL_43ff;
								IL_43ff:
								_RecordStub[15, 85] = 109;
								_RecordStub[31, 47] = 108;
								_RecordStub[19, 57] = 107;
								_RecordStub[23, 55] = 106;
								_RecordStub[27, 29] = 105;
								_RecordStub[29, 46] = 104;
								_RecordStub[33, 0] = 103;
								_RecordStub[16, 83] = 102;
								_RecordStub[39, 78] = 101;
								_RecordStub[32, 77] = 100;
								_RecordStub[36, 25] = 99;
								_RecordStub[34, 19] = 98;
								_RecordStub[38, 49] = 97;
								_RecordStub[19, 25] = 96;
								_RecordStub[23, 53] = 95;
								_RecordStub[28, 43] = 94;
								_RecordStub[31, 44] = 93;
								_RecordStub[36, 34] = 92;
								_RecordStub[16, 34] = 91;
								_RecordStub[35, 1] = 90;
								_RecordStub[19, 87] = 89;
								_RecordStub[18, 53] = 88;
								_RecordStub[29, 54] = 87;
								_RecordStub[22, 41] = 86;
								_RecordStub[38, 18] = 85;
								_RecordStub[22, 2] = 84;
								goto IL_45b6;
								continue;
								end_IL_109c4:
								break;
							}
							continue;
							end_IL_109cd:
							break;
						}
						continue;
						end_IL_109d2:
						break;
					}
					continue;
					end_IL_109de:
					break;
				}
			}
		}

		internal static bool PostRole()
		{
			return true;
		}

		internal static bool InvokeRole()
		{
			return false;
		}

		internal static void DestroyRole(object table, int row, int column, int value)
		{
			((int[,])table)[row, column] = value;
		}
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

