using System;
using System.Threading;

namespace MusicTagWinApp.Web;

// 进程级 QQ 请求协调器。QQ 的 2001 风控按出口/会话维度生效，provider 实例级
// 限速无法覆盖组合标签、歌词和封面窗口之间的请求，因此这里统一串行化并维护冷却。
internal enum QqRequestPermit
{
	Granted,
	CoolingDown,
	Cancelled
}

internal static class QqRequestCoordinator
{
	private const int MinimumIntervalMilliseconds = 1500;

	private const int FirstCooldownSeconds = 60;

	private const int ExtendedCooldownSeconds = 120;

	private static readonly object syncRoot = new object();

	private static DateTime nextRequestUtc = DateTime.MinValue;

	private static DateTime cooldownUntilUtc = DateTime.MinValue;

	private static int cooldownCount;

	public static QqRequestPermit WaitForPermit(CancellationToken cancellationToken, out int cooldownSeconds)
	{
		cooldownSeconds = 0;
		while (true)
		{
			TimeSpan waitDuration;
			lock (syncRoot)
			{
				DateTime now = DateTime.UtcNow;
				if (cooldownUntilUtc > now)
				{
					cooldownSeconds = GetRemainingSeconds(cooldownUntilUtc - now);
					return QqRequestPermit.CoolingDown;
				}

				waitDuration = nextRequestUtc - now;
				if (waitDuration <= TimeSpan.Zero)
				{
					nextRequestUtc = now.AddMilliseconds(MinimumIntervalMilliseconds);
					return QqRequestPermit.Granted;
				}
			}

			int waitMilliseconds = (int)Math.Min(waitDuration.TotalMilliseconds, int.MaxValue);
			if (cancellationToken.WaitHandle.WaitOne(Math.Max(1, waitMilliseconds)))
			{
				return QqRequestPermit.Cancelled;
			}
		}
	}

	public static int RecordRateLimited()
	{
		lock (syncRoot)
		{
			DateTime now = DateTime.UtcNow;
			if (cooldownUntilUtc <= now)
			{
				cooldownCount = 0;
			}
			cooldownCount = Math.Min(cooldownCount + 1, 2);
			int cooldownDurationSeconds = cooldownCount == 1 ? FirstCooldownSeconds : ExtendedCooldownSeconds;
			DateTime cooldownEnd = now.AddSeconds(cooldownDurationSeconds);
			if (cooldownEnd > cooldownUntilUtc)
			{
				cooldownUntilUtc = cooldownEnd;
			}
			if (cooldownUntilUtc > nextRequestUtc)
			{
				nextRequestUtc = cooldownUntilUtc;
			}
			return GetRemainingSeconds(cooldownUntilUtc - now);
		}
	}

	public static bool IsCoolingDown(out int cooldownSeconds)
	{
		lock (syncRoot)
		{
			TimeSpan remaining = cooldownUntilUtc - DateTime.UtcNow;
			if (remaining <= TimeSpan.Zero)
			{
				cooldownSeconds = 0;
				return false;
			}
			cooldownSeconds = GetRemainingSeconds(remaining);
			return true;
		}
	}

	internal static void ResetForTests()
	{
		lock (syncRoot)
		{
			nextRequestUtc = DateTime.MinValue;
			cooldownUntilUtc = DateTime.MinValue;
			cooldownCount = 0;
		}
	}

	private static int GetRemainingSeconds(TimeSpan remaining)
	{
		return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
	}
}
