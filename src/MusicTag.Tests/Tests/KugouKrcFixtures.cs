namespace MusicTag.Tests;

internal static class KugouKrcFixtures
{
	// Captured from lyrics.kugou.com/download?fmt=krc for lyric id 216374858 on 2026-08-02.
	// The matching plaintext starts with [22144,4242] and was independently decoded during protocol review.
	internal const string RealKrcContentBase64 =
		"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";

	// Captured from the same lyric id with fmt=lrc; contenttype was 1 and charset was utf8.
	internal const string RealLrcContentBase64 =
		"W29mZnNldDowXQ0KWzAwOjIyLjE0XemDveaYr+WLh+aVoueahA0KWzAwOjI4LjM4XeS9oOmineWktOeahOS8pOWPo+S9oOeahOS4" +
		"jeWQjOS9oOeKr+eahOmUmQ0KWzAwOjM2Ljg4XemDveS4jeW/hemakOiXjw0KWzAwOjQzLjE1XeS9oOegtOaXp+eahOeOqeWBtuS9" +
		"oOeahOmdouWFt+S9oOeahOiHquaIkQ0KWzAwOjUxLjYzXeS7luS7rOivtOimgeW4puedgOWFiempr+acjeavj+S4gOWktOaAquWF" +
		"vQ0KWzAwOjU4LjYwXeS7luS7rOivtOimgee8neWlveS9oOeahOS8pOayoeacieS6uueIseWwj+S4kQ0KWzAxOjA1Ljc0XeS4uuS9" +
		"leWtpOeLrOS4jeWPr+WFieiNow0KWzAxOjA5LjAyXeS6uuWPquacieS4jeWujOe+juWAvOW+l+atjOmigg0KWzAxOjEzLjI4Xeiw" +
		"geivtOaxoeazpea7oei6q+eahOS4jeeul+iLsembhA0KWzAxOjIwLjc0XeeIseS9oOWtpOi6q+i1sOaal+W3tw0KWzAxOjIyLjY5" +
		"XeeIseS9oOS4jei3queahOaooeagtw0KWzAxOjI0LjU0XeeIseS9oOWvueWzmei/h+e7neacmw0KWzAxOjI2LjI5XeS4jeiCr+WT" +
		"reS4gOWcug0KWzAxOjI4LjE2XeeIseS9oOegtOeDgueahOiho+ijsw0KWzAxOjI5Ljk3XeWNtOaVouWgteWRvei/kOeahOaeqg0K" +
		"WzAxOjMxLjg0XeeIseS9oOWSjOaIkemCo+S5iOWDjw0KWzAxOjMzLjYyXee8uuWPo+mDveS4gOagtw0KWzAxOjM1LjQ3XeWOu+WQ" +
		"l+mFjeWQl+i/meiktOikm+eahOaKq+mjjg0KWzAxOjM5LjE5XeaImOWQl+aImOWViuS7peacgOWNkeW+rueahOaipg0KWzAxOjQy" +
		"Ljg4XeiHtOmCo+m7keWknOS4reeahOWRnOWSveS4juaAkuWQvA0KWzAxOjUwLjE3XeiwgeivtOermeWcqOWFiemHjOeahOaJjeeu" +
		"l+iLsembhA0KWzAyOjA4LjY5XeS7luS7rOivtOimgeaIkuS6huS9oOeahOeLgg0KWzAyOjExLjUzXeWwseWDj+aTpuaOieS6huax" +
		"oeWeog0KWzAyOjE2LjEwXeS7luS7rOivtOimgemhuuWPsOmYtuiAjOS4iuiAjOS7o+S7t+aYr+S9juWktA0KWzAyOjIzLjMyXemC" +
		"o+WwseiuqeaIkeS4jeWPr+S5mOmjjg0KWzAyOjI2LjUxXeS9oOS4gOagt+mqhOWCsuedgOmCo+enjeWtpOWLhw0KWzAyOjMwLjgw" +
		"XeiwgeivtOWvueW8iOW5s+WHoeeahOS4jeeul+iLsembhA0KWzAyOjM4LjMxXeeIseS9oOWtpOi6q+i1sOaal+W3tw0KWzAyOjQw" +
		"LjIyXeeIseS9oOS4jei3queahOaooeagtw0KWzAyOjQyLjAxXeeIseS9oOWvueWzmei/h+e7neacmw0KWzAyOjQzLjg0XeS4jeiC" +
		"r+WTreS4gOWcug0KWzAyOjQ1LjcxXeeIseS9oOegtOeDgueahOiho+ijsw0KWzAyOjQ3LjUwXeWNtOaVouWgteWRvei/kOeahOae" +
		"qg0KWzAyOjQ5LjM4XeeIseS9oOWSjOaIkemCo+S5iOWDjw0KWzAyOjUxLjE5Xee8uuWPo+mDveS4gOagtw0KWzAyOjUzLjA0XeWO" +
		"u+WQl+mFjeWQl+i/meiktOikm+eahOaKq+mjjg0KWzAyOjU2LjczXeaImOWQl+aImOWViuS7peacgOWNkeW+rueahOaipg0KWzAz" +
		"OjAwLjQyXeiHtOmCo+m7keWknOS4reeahOWRnOWSveS4juaAkuWQvA0KWzAzOjA3LjczXeiwgeivtOermeWcqOWFiemHjOeahOaJ" +
		"jeeul+iLsembhA0KWzAzOjExLjk4XeS9oOeahOaWkemps+S4juS8l+S4jeWQjA0KWzAzOjEyLjE4XeS9oOeahOayiem7mOmch+iA" +
		"s+assuiBiw0KWzAzOjEyLjQzXXlvdSBhcmUgdGhlIGhlcm8NClswMzoyNi4zMl3niLHkvaDlraTouqvotbDmmpflt7cNClswMzoy" +
		"OC4yMl3niLHkvaDkuI3ot6rnmoTmqKHmoLcNClswMzozMC4wMV3niLHkvaDlr7nls5nov4fnu53mnJsNClswMzozMS44NV3kuI3o" +
		"gq/lk63kuIDlnLogeW91IGFyZSB0aGUgaGVybw0KWzAzOjMzLjcyXeeIseS9oOadpeiHquS6juibruiNkg0KWzAzOjM1LjYxXeS4" +
		"gOeUn+S4jeWAn+iwgeeahOWFiQ0KWzAzOjM3LjM1XeS9oOWwhumAoOS9oOeahOWfjumCpg0KWzAzOjM5LjIwXeWcqOW6n+Win+S5" +
		"i+S4ig0KWzAzOjQxLjAxXeWOu+WQl+WOu+WViuS7peacgOWNkeW+rueahOaipg0KWzAzOjQ0Ljc0XeaImOWQl+aImOWViuS7peac" +
		"gOWtpOmrmOeahOaipg0KWzAzOjQ4LjQzXeiHtOmCo+m7keWknOS4reeahOWRnOWSveS4juaAkuWQvA0KWzAzOjU1LjY2Xeiwgeiv" +
		"tOermeWcqOWFiemHjOeahOaJjeeul+iLsembhA0K";

	internal const string LanguageMetadataBase64 =
		"eyJjb250ZW50IjpbeyJseXJpY0NvbnRlbnQiOltbInJvbWFuIl0sWyJ3b3JsZCJdXSwidHlwZSI6MH0seyJseXJpY0NvbnRlbnQiOltbIuS9oOWlvSJdLFsi5LiW55WMIl1dLCJ0eXBlIjoxfV19";

	// contenttype=2 behavior is documented by the independently reviewed LDDC implementation; no live sample was available.
	internal const string Type2ContentBase64 = "WzAwOjAxLjIzXVR5cGUyCg==";
}
