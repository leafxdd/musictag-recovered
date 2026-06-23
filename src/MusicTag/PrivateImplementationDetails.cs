using System.Runtime.CompilerServices;

[CompilerGenerated]
internal static class _003CPrivateImplementationDetails_003E
{
	internal static uint ComputeStringHash(string s)
	{
		uint hash = 2166136261u;
		if (s != null)
		{
			for (int i = 0; i < s.Length; i++)
			{
				hash = (hash ^ s[i]) * 16777619u;
			}
		}
		return hash;
	}
}
