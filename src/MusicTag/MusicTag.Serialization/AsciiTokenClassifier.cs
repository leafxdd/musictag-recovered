namespace MusicTag.Serialization;

internal static class AsciiTokenClassifier
{
	private const char AsciiUppercaseA = 'A';

	private const char AsciiUppercaseZ = 'Z';

	private const char AsciiLowercaseA = 'a';

	private const char AsciiLowercaseZ = 'z';

	private const char AsciiDigit0 = '0';

	private const char AsciiDigit9 = '9';

	private const char FullWidthUppercaseA = 'Ａ';

	private const char FullWidthUppercaseZ = 'Ｚ';

	private const char FullWidthLowercaseA = 'ａ';

	private const char FullWidthLowercaseZ = 'ｚ';

	private const char FullWidthDigit0 = '０';

	private const char FullWidthDigit9 = '９';

	public static bool IsAsciiLetterLike(char value)
	{
		return IsAsciiLowercaseLetter(value)
			|| IsAsciiUppercaseLetter(value)
			|| IsFullWidthLowercaseLetter(value)
			|| IsFullWidthUppercaseLetter(value);
	}

	public static bool IsAsciiDigitLike(char value)
	{
		return IsAsciiDigit(value) || IsFullWidthDigit(value);
	}

	private static bool IsAsciiLowercaseLetter(char value)
	{
		return value >= AsciiLowercaseA && value <= AsciiLowercaseZ;
	}

	private static bool IsAsciiUppercaseLetter(char value)
	{
		return value >= AsciiUppercaseA && value <= AsciiUppercaseZ;
	}

	private static bool IsFullWidthLowercaseLetter(char value)
	{
		return value >= FullWidthLowercaseA && value <= FullWidthLowercaseZ;
	}

	private static bool IsFullWidthUppercaseLetter(char value)
	{
		return value >= FullWidthUppercaseA && value <= FullWidthUppercaseZ;
	}

	private static bool IsAsciiDigit(char value)
	{
		return value >= AsciiDigit0 && value <= AsciiDigit9;
	}

	private static bool IsFullWidthDigit(char value)
	{
		return value >= FullWidthDigit0 && value <= FullWidthDigit9;
	}
}
