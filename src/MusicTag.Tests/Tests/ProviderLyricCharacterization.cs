using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// 联网歌词搜索 characterization：SearchLyrics 端到端驱动各 provider 真实解析链
//（搜索 JSON 取候选 → 逐首 LoadLyrics/LoadSongDetails 二次请求歌词端点 → 解析 LRC/翻译 → 排序）。
// 注入按 HTTP 调用区分：
//  - NetEase/QQ：搜索走 PostString、歌词走 GetResponseString（两个 override 分别喂）。
//  - Kugou/Kuwo：搜索与歌词同走 GetResponseString，故 Stub 按 URL 关键字分流（Kugou 歌词含 get_krc、
//    Kuwo 详情含 songinfoandlrc）。
// QQ 歌词是 base64-in-jsonp：fixture 用 Convert.ToBase64String(UTF8) 动态编码（自解释，不硬编码 base64）。
// QQ 仅在原文+译文都非空时才走 AlignAndSplitTranslatedLyric 复杂对齐 / Kuwo 仅在多行双语时走重排——
// 此处均用单语规避（对齐/重排留待边界外）。
internal static class ProviderLyricCharacterization
{
	private sealed class StubNetEase : NetEaseMusicTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		public string LastLyricUrl { get; private set; }
		public StubNetEase(string searchResponse, string lyricResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
		}
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false) => searchResponse;
		protected override string GetResponseString(string url)
		{
			LastLyricUrl = url;
			return lyricResponse;
		}
	}

	private sealed class StubQq : QqMusicTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		private readonly Queue<string> qrcResponses;
		public string QrcRequestUrl { get; private set; }
		public string QrcRequestBody { get; private set; }
		public int QrcRequestCount { get; private set; }
		public int LegacyRequestCount { get; private set; }
		public int LyricRetryWaitCount { get; private set; }
		public bool CancelOnLyricRetryWait { get; set; }
		public StubQq(string searchResponse, string lyricResponse = null, string qrcResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
			qrcResponses = new Queue<string>();
			if (qrcResponse != null)
			{
				qrcResponses.Enqueue(qrcResponse);
			}
		}
		public StubQq(string searchResponse, string lyricResponse, IEnumerable<string> qrcResponses) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
			this.qrcResponses = new Queue<string>(qrcResponses ?? Array.Empty<string>());
		}
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			if (body?.Contains("GetPlayLyricInfo") == true)
			{
				QrcRequestCount++;
				QrcRequestUrl = url;
				QrcRequestBody = body;
				return qrcResponses.Count > 0 ? qrcResponses.Dequeue() : searchResponse;
			}
			return searchResponse;
		}
		protected override string GetResponseString(string url)
		{
			LegacyRequestCount++;
			return lyricResponse;
		}
		protected override bool WaitForLyricRetryDelay(int waitMilliseconds)
		{
			LyricRetryWaitCount++;
			if (CancelOnLyricRetryWait)
			{
				cancellationSource.Cancel();
				return true;
			}
			return false;
		}
	}

	private sealed class StubKugou : KugouTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		public StubKugou(string searchResponse, string lyricResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
		}
		// 搜索（/api/v3/search/song）与歌词（/krc/get_krc）同走 GET，按 URL 关键字分流。
		protected override string GetResponseString(string url) => url.Contains("get_krc") ? lyricResponse : searchResponse;
	}

	private sealed class StubKuwo : KuwoTagProvider
	{
		private readonly string searchResponse;
		private readonly string detailResponse;
		public StubKuwo(string searchResponse, string detailResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.detailResponse = detailResponse;
		}
		// 搜索（search.kuwo.cn/r.s）与详情/歌词（…/songinfoandlrc）同走 GET，按 URL 关键字分流。
		protected override string GetResponseString(string url) => url.Contains("songinfoandlrc") ? detailResponse : searchResponse;
	}

	// QQ 歌词响应：callbackName({"lyric":"<base64>","trans":"<base64>"})。空串保持为空（触发"无歌词"分支）。
	private static string QqJsonpLyric(string lyric, string translation = "")
	{
		string encodedLyric = string.IsNullOrEmpty(lyric) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(lyric));
		string encodedTrans = string.IsNullOrEmpty(translation) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(translation));
		return "MusicJsonCallback34475857153687595({\"lyric\":\"" + encodedLyric + "\",\"trans\":\"" + encodedTrans + "\"})";
	}

	private static string QqQrcResponse(string lyric, string translation = "")
	{
		return "{\"req_0\":{\"code\":0,\"data\":{\"qrc_t\":1,\"lyric\":\"" + EncryptQrcFixture(lyric) + "\",\"trans\":\"" + EncryptQrcFixture(translation) + "\"}}}";
	}

	private static string EncryptQrcFixture(string lyric)
	{
		if (string.IsNullOrEmpty(lyric))
		{
			return "";
		}

		byte[] compressed;
		using (MemoryStream output = new MemoryStream())
		{
			using (ZLibStream compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
			{
				byte[] source = Encoding.UTF8.GetBytes(lyric);
				compressor.Write(source, 0, source.Length);
			}
			compressed = output.ToArray();
		}

		int paddedLength = (compressed.Length + 7) / 8 * 8;
		byte[] padded = new byte[paddedLength];
		Buffer.BlockCopy(compressed, 0, padded, 0, compressed.Length);
		byte[] encrypted = new byte[paddedLength];
		byte[][][] schedule = new byte[3][][];
		for (int keyIndex = 0; keyIndex < schedule.Length; keyIndex++)
		{
			schedule[keyIndex] = new byte[16][];
			for (int round = 0; round < schedule[keyIndex].Length; round++)
			{
				schedule[keyIndex][round] = new byte[6];
			}
		}

		byte[] key = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL");
		QqDesHelper.TripleDESKeySetup(key, schedule, QqDesHelper.ENCRYPT);
		for (int offset = 0; offset < padded.Length; offset += 8)
		{
			byte[] block = new byte[8];
			Buffer.BlockCopy(padded, offset, block, 0, block.Length);
			byte[] encryptedBlock = new byte[8];
			QqDesHelper.TripleDESCrypt(block, encryptedBlock, schedule);
			Buffer.BlockCopy(encryptedBlock, 0, encrypted, offset, encryptedBlock.Length);
		}

		return Convert.ToHexString(encrypted);
	}

	private const string NetEaseSearchOneSong =
		"{\"result\":{\"songs\":[{\"id\":111,\"name\":\"SongA\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\"}}]}}";

	private const string QqSearchOneSong =
		"{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[" +
		"{\"id\":555,\"mid\":\"M555\",\"name\":\"NameA\",\"title\":\"TitleA\",\"singer\":[{\"name\":\"SingerA\"}],\"album\":{\"id\":99,\"mid\":\"ALB99\",\"name\":\"AlbumA\"}}" +
		"]}}}}}";

	private const string KugouSearchOneSong =
		"{\"data\":{\"info\":[{\"audio_id\":\"A123\",\"songname\":\"SongA\",\"singername\":\"ArtistA\",\"album_name\":\"AlbumA\",\"hash\":\"H1\",\"duration\":240}]}}";

	private const string KuwoSearchOneSong =
		"{\"abslist\":[{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_12345\"}]}";

	public static IEnumerable<(string, Action)> All()
	{
		// ---- NetEase：lrc.lyric / tlyric.lyric 直取 ----
		yield return ("NetEase.SearchLyrics requests latest YRC fields on the v1 lyric endpoint", delegate
		{
			StubNetEase provider = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"[00:01.00]Line1\"}}");
			provider.SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal("https://music.163.com/api/song/lyric/v1?id=111&cp=false&lv=0&kv=0&tv=0&rv=0&yv=0&ytv=0&yrv=0", provider.LastLyricUrl, "latest lyric endpoint and flags");
		});

		yield return ("NetEase.SearchLyrics maps lrc + tlyric + fields", delegate
		{
			string lyricJson = "{\"lrc\":{\"lyric\":\"[00:01.00]Line1\\n[00:02.00]Line2\"},\"tlyric\":{\"lyric\":\"[00:01.00]Trans1\"}}";
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, lyricJson).SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 2);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal("[00:01.00]Line1\n[00:02.00]Line2", lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("[00:01.00]Trans1", lyrics[0].TranslatedLyric, "[0].TranslatedLyric");
			Check.Equal("111", lyrics[0].TrackId, "[0].TrackId");
			Check.Equal("SongA", lyrics[0].Title, "[0].Title");
			Check.Equal("ArtistA", lyrics[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal(SearchSource.Music163, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(0, lyrics[0].ResultOrder, "[0].ResultOrder");
			Check.Equal(2, lyrics[0].SourceOrder, "[0].SourceOrder");
		}
		);

		yield return ("NetEase.SearchLyrics empty lrc -> 0 lyrics (null result skipped)", delegate
		{
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"\"}}").SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("NetEase.SearchLyrics nolyric response -> 0 lyrics", delegate
		{
			string response = "{\"nolyric\":true,\"lrc\":{\"lyric\":\"[00:00.00]纯音乐，请欣赏。\"}}";
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, response).SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("NetEase.SearchLyrics existing TrackId skipped", delegate
		{
			List<LyricSearchResult> existing = new List<LyricSearchResult> { new LyricSearchResult { TrackId = "111" } };
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"[00:01.00]X\"}}").SearchLyrics("q", 10, 0L, existing, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("NetEase.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubNetEase provider = new StubNetEase("<html>not json</html>", "{\"lrc\":{\"lyric\":\"x\"}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);

		// ---- QQ：base64-in-jsonp 歌词，纯原文（规避 AlignAndSplitTranslatedLyric） ----
		yield return ("QqQrcDecoder decrypts a real QQ golden vector independently verified by LDDC", delegate
		{
			// Captured from GetPlayLyricInfo for song ID 678268984 on 2026-07-23.
			// The expected plaintext digest was independently verified with LDDC
			// commit 84631e8. Do not regenerate this vector with EncryptQrcFixture.
			const string encryptedLyrics =
				"94093D7895A5393FED0950A5746DE8CE401D36C157525BCA8A042CBBDA5E1E6A591A815E6E564D902ACB222DACDB1ACB" +
				"67005933FD6E60060C13AA8E403B8B99DC6835179691F006ABCD626FAE03B95FF52D38781D4209A9FB65E1D23C41A50F" +
				"10A6D24421E1FDEB69D99CEC66FEA73BD1523C34015E5B52CFB97CD236165EA4E70419252F9B825697571723DAE9FEE0" +
				"C414BBFF4A927A2E51E68ED11245C6B53F578EE52A92907EB40F91A89AF0C4DAD56A1F4B2F21C90450F4498BE1E6436A" +
				"4CEE32EDB9474E3195FCBEB4F3E22960454DFAD1BEFEEFE5248967A6DC312C5F1D7E5D9768B520E21D7CD695CF37D071" +
				"639FCE63E3ACBC2B45FDF101521C3E6AE86992759D0CD14D4EF6DD5EDF72E1842FEE2BC143B4F96B1CBFD0941320D7E4" +
				"0238021926B2BF985F1C9BB6481264257B44F9821DD685B92F5628F5137FFD75A8872A3BD2E4B8EEF125847BA304DD70" +
				"0AB297BAF9237299C22E130162FA0880E4C658DB3667DEB9870E7EFB0D1C49D39132DD48AB2D7415E17434610C3E73FE" +
				"229028F5BB87FDDCDC75447CDE83358429CADCE0E48AAC0661FF72BDF4579040301D103F8581E9D3F8FA5773F78032C3" +
				"6EA88E9A3586B8066866B302FE60BD6698899C66B939BFD15DF90723E8BCD460DDFD5F5898D8B5B384B827B75BE1AA45" +
				"4915E82F43D32B19FC90957A781184A46DAB186651FB2585BF1C60CA0DAC1D450D2E7295AD4110D2B4244171FC3E008F" +
				"8547DA0719E8110F457FAB789EBEE021297C540FCC1620CF204B5E7D35BDD1ACC92F9C6E035A26A83C7C98686F194E9D" +
				"03109A38CB67AB46162A36F0001E3190A75624D00434278313A0F46FC185657D30901F30E5107495501B32DB3F1A18A3" +
				"180E3B5B5C3F8141342D1E51CB56DBC202ABC09DAB6A8E1F17C9B5D321EC1F1466754A8004D633037149B74A8E5C57F4" +
				"BE790A5ED4B33BF5610CC3A9E9DC3DFB60429CFE63AC48DC18FEB97CD9A86A73AD3022A65C0ADA11CF6A18C19B15C527" +
				"2889755609A8269464E56B31398AD49C47FED455DA5EB0B8C246E41018C09017050268D12FB651FAB562BD4CCC0C58A2" +
				"7D352DB9F0B0FF4E9B6EE80D0E33EF7D2A1123CA76679DC94F11E0A3C28EC0A0A8E28F7ADB9C0F67749403A2D04AC300" +
				"7E64A8994CE0DAA5F0E36D7175E5C939A08B1CA36EE7E2BB2C57E944755ECACFC5492FE2E31F2DF9ED43358422B5D61B" +
				"2797057B613063434EF74161E2C7308DB7EF47A57038B71A408C4D047E7A6840A66E9BA64F64F8466099988C10E7C032" +
				"01F7D9E031E3AAD642EBAF6677DE3AFF0BE1FCB4430E3FBD03FF0358162B7EAFD020EF9E628E1AA89116D5D13292DA55" +
				"36DE3295D0AFF108230D265BC5A830AB0D572F1844A9253C94E8A198B019CD6131E18047754AB2B826C7C5C1F23BCBD4" +
				"041E382CD9DCDAC49F82498B96052BF8B12A0C2369EE055E73D3B5656B82C60F23852DF15F3877E4CB09539B9618CE42" +
				"0A2E629BBC96EAC6DD777541ABE9F3090DB334133BF5B2C8250FA10C4D893C1D1289B8BA35A87F4413924B267CBE01F4" +
				"904DF593529E72E50A7C34AC28D66066BF1C7127388619FDE005866E943BB395DB7017E3230B1B205917E5B82EE6FB8D" +
				"2D0DAD2381A8292D694E973D3079ECA1A4A97397129DC852004FDB2D44DDD438EC91FBA9657FFFC428A761EB4B12AD29" +
				"1650F71543EC5D79F617AC79CF07275A02250158AF7D69435BE95C20D9018CBC4933B3447DC59DA97A850D76B09628AA" +
				"9737E792BF2B055ED9D540B68FD6BA3642A514D510A0ADFDD7AF6F8AC03408D1C9816EEFC3B0970DFB13F40D8C3723C7" +
				"34B8EE8DE3749F777DAD0628A9F4E8882FD7CB611E23398EF6B0B4B54B87EA795FB5DED39D9700326810A7C84FDDDB44" +
				"ED6D26D26D0D112754E789BADB3176D4B52B9EC48C53C6A3DC012DC4A84FA2F2F4D583C3AE167C4B996CD7752A541BA8" +
				"A8B44A786D06D6748F47971BE2A2E4A04905105A2F6FA51C2D2EBE023C7C45970683BB23BB621C934AE15CFF1A231E38" +
				"244D45F3782B6FE3FCDABCD4E06C0D65F12374DFF6F4BF1D7DBFF7F3D014F23F934613CD9E845621F8F0F7C123F32221" +
				"121289FAEDA76CEC7AA8CCB3B28C88C57E53A866CB6F77C207F1EFF4376E3AD8096C6B2029A9FA855A279095A6BF4088" +
				"0DEE748ED8AE41F465CB538E5C8CE84ACA8D25424C1DE42541FB61BD53B54A0CC4DAB96841C9E3135E0DB8FA287D4970" +
				"953D5474873D1D496F03614D51995458931D43AF9D6D424AE437C49FE40E47E221074A04D376E73CF39D003FCD22B7F2" +
				"D6A6D5E825B536F648A56E307FFF616A9AD3D67D9320DA6668B8C23F12E530578C988C7EA35D823FAF0E86EEAC548E90" +
				"EB7E49B035CF5A2C08E26AABDC08B12462B70F8CDCE48FFCE8D2B14A2DE481BAF723B276B2E8A623F792BF72E24EE610" +
				"B8E2EF9685F2FA7F10D829F08B34F889685BC38E155E689B508BA6EEE566B0C71578633C1C5902FB8A4E05208EDDAB52" +
				"44AC96EAC2FD38FCD01E628E9D3FE938BC4B111EDA660FFB695EB2CED27B683D8B717B3EEABA0C6F145458E84DE773AF" +
				"0BA0B60E7FE0228718E8BA43A8B247CE40242E4831B0EA2045FC2A0DF9ADEC63A5A2206D804DBC47910150420AA77005" +
				"4735D70EC14E30950D59580CDFF07525E602C924E35CC610191CEDB1D9870C404B5A29EEF2E879A03AAE489FE85EF0CE" +
				"2992B2FC64CD3D5392412F2E7ABA93663A465E73E5FACD50BFD65314649ECE1E2E92F9782773EF9108E26AE5D1BC40F9" +
				"78DA2510538C95598F8530D3669118D16AB9AFEAE2E286C1DC8CC3CA2F39AC9464F95CDB98E0E1CE72491F48FC62338F" +
				"97BFF98A474C6F95C80EA02489850A9ECB97FAD719A7F2D107EDC549FCF6A76B029D50509A7402E2CF86641A92EF599E" +
				"9EF0DAEF6F439A5ED6D4C4879F700890FC7A5EB4BF0B0D9998AE371037BDA10767307DBFE44028FA5188B932B2AD7653" +
				"C123D67F34FD17EB7F48D597AE1FC6CFFB490D3F982C623280C4AA66AAE0F37CC478A2A6BC7F96EF9DE5817197FD39E7" +
				"45705A068DB1F4B7DAE1B7C97F90F5DD6CE8C8D2FC1506BC38EEB9AC3E9A51AAF14F1D043CE00505D9669226EA6B0026" +
				"69D86CD4E8EB1BB4EC9BB4F249164444B22576EA779A17D80F45CF65465E9B62910FFD96C938A9B9B17B80E440E0DD54" +
				"2C0623139AAD29A0F9F37DAECC9EA5F64DC93836169DB291E8D726A7C11AC97691FB01952DAF3990C1CC58899F0ACBA0" +
				"70C03F6868C442B943871E4ECA50675AF5EF9E8B926688DD3488EA2ED7847AF868FBEF4C15C883911FF2C5EEC648FCE9" +
				"607568ED4A7CC205F777BF711ACBB122158E9779DE6ACC475FDE477F9603E502F6F218A2EDEB45E6A1F2F95D4C9C2082" +
				"D0A8C728B3431DD47FB6FF700E74776DB64793B759D5E613AD326C0FA8C1562FF2EACEC66AC6DAC057A970940941B595" +
				"5794C14AEF2DFC3BB5F34E4467247FFCC8CBE5045FBE49DBCCA13682F23852214BF1A6E761A9BF2EC60719F5F2E82FF8" +
				"9C19E8DAA8C75F8A799532FF35737DD9C22364BB449475554218B6610A9F13BDB7EF485CA3714CE5F2B9FBB3C6037BBB" +
				"22E59F7CC18E791B834184828FE2A8791C3E8BE6D143229AA05DFA9AD7626E69C92703D8C2B2163795B083FC37357B25" +
				"0C22F3B66782B2D7579A2EE49A0217F3EAEC0BF0787AE22910B540D2A128D641D7719EBC39FEB0FD72DD76497D1A6D75" +
				"F6B20B5C8A976DA2BF05B2EFDCA841FBE827E1A569FA3BD2834D7ABB38522685D70A84309011A1D5C63D04C4B39F1963" +
				"C3D052C4467BF7AFE68F304E63E44C248D7C86CB7D9F6D994E8B85DCA93E32D3AA04446669CA9E80AD3BAD264E52D5C9" +
				"F7202886ADDE5ED83AE85F5E33B1076A51ACFA1DD383759C95A230FBF1CA098390FC118F6EECEDACE237674155032F00" +
				"EE1A9C6A9E83C83F80F2F8D0061C972408CD3E3D50144EE4502D7486E31DA42337DE228474FB3001E1E60D3AF23C7339" +
				"9798583BE5D8B5D3E98642179DD629BE0362B4BF7E8FA90B7B11221BBBAEA8B40D0130D37B9368F649E45C79E81A9D4A" +
				"DB3025B3B1D1C457F147B37284627753B6F31DCA8C2BF8EE2C760D869316DF57903A142C7089045375974DBBB7647BBA" +
				"B42047313EC0BF915262BB93444D7944B0D5FF6A49F4D83DE3D59CD413A473D8AF70FE8314582777E9EA1C4FC7E338CB" +
				"7D04829352448109B526875359364B924ABF176548F62EF91103716269C183BB3ED5D2577EEACF7873E7EC691ED4306C" +
				"D7DD7E3DDCCF06A77FAF19EBE6872674FBAADCAA735B1E737813687F6916E84AEE4EDB7804EED1530E2D96DA6DD67629" +
				"C106DF215F78BC02258EA6214833047330BCD146B89803DCE9B310B3FBD58C660FD10E6A0ED0FFD86CCADFB994866564" +
				"F0EF0D175E51AD96A8BACE5A3FE7A815EB342FB14BE6FC2D0921AA9EA6D4B3AF53E9F68B4F2BA72662C98B5E5360F9EC" +
				"EB63C9FE297BA2135668C5717D2664B51B9E5534327FE870CAB57A5B68F1C4091E5957FC6A6CA14158A9618228826CC6" +
				"38EB9C0D22129E79E3B24509CFE6FC3220132E2B3DB7555518D4141DC339406A115890C95C0513903281A443987AD45C" +
				"DEA0D0FA7C66A614815AA1D550B9321FE756F130CC2701FC8BF2B8F8F04205BF1A1E31955C31771AB353C23817FE0D52";
			string decryptedLyrics = QqQrcDecoder.DecryptLyrics(encryptedLyrics);
			Check.Equal(6500, decryptedLyrics.Length, "decrypted QRC character length");
			Check.Equal(7434, Encoding.UTF8.GetByteCount(decryptedLyrics), "decrypted QRC UTF-8 length");
			Check.Equal(
				"BDD4FD1997C13BF7B71D63E4B68F4F69CCEFB3A6362CFD72B45D19884EC02CC4",
				Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(decryptedLyrics))),
				"decrypted QRC SHA-256");
			Check.True(decryptedLyrics.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", StringComparison.Ordinal), "decrypted QRC XML prefix");
		});

		yield return ("QQ.SearchLyrics decodes base64 jsonp lyric + fields", delegate
		{
			string lyricText = "[00:01.00]Line1\n[00:02.00]Line2";
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric(lyricText)).SearchLyrics("q", 10, 3);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal(lyricText, lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("555", lyrics[0].TrackId, "[0].TrackId");
			Check.Equal("TitleA", lyrics[0].Title, "[0].Title");
			Check.Equal("NameA", lyrics[0].OriginalTitle, "[0].OriginalTitle");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal("SingerA", lyrics[0].Artist, "[0].Artist");
			Check.Equal(SearchSource.QQ, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(3, lyrics[0].SourceOrder, "[0].SourceOrder");
		}
		);

		yield return ("QQ.SearchLyrics prefers QRC and preserves real 3-digit milliseconds", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string qrc = "<?xml version=\"1.0\"?><QrcInfos><LyricInfo LyricContent=\"[ti:Title] [ar:Singer] [12347,800]Hello(12347,400) world(12747,400) [13201,500]Next(13201,500)\" /></QrcInfos>";
				StubQq provider = new StubQq(QqSearchOneSong, QqJsonpLyric("[00:01.00]fallback"), QqQrcResponse(qrc));
				List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
				Check.Equal(1, lyrics.Count, "count");
				Check.Equal("[ti:Title]\n[ar:Singer]\n[00:12.347]Hello world\n[00:13.201]Next", lyrics[0].Lyric, "QRC line lyric");
				Check.True(provider.QrcRequestUrl.Contains("musicu.fcg"), "QRC endpoint");
				Check.True(provider.QrcRequestBody.Contains("\"songID\":555"), "numeric song id");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		}
		);

		yield return ("QQ.SearchLyrics QRC follows ReformatTimetag 2-digit rounding", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = true;
				string qrc = "[12347,800]Hello(12347,800)";
				List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("fallback"), QqQrcResponse(qrc)).SearchLyrics("q", 10, 0);
				Check.Equal("[00:12.35]Hello", lyrics[0].Lyric, "rounded QRC line lyric");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		}
		);

		yield return ("QQ.SearchLyrics aligns 2-digit translation to precise QRC line timestamps", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string qrc = "<?xml version=\"1.0\"?><QrcInfos><LyricInfo LyricContent=\"[ti:Title] [12347,800]Hello(12347,800) [13201,500]Next(13201,500) [14509,500]Last(14509,500)\" /></QrcInfos>";
				string translation = "[kana:fixture]\n[00:12.34]你好\n[00:13.20]//\n[00:14.50]最后";
				List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("fallback"), QqQrcResponse(qrc, translation)).SearchLyrics("q", 10, 0);
				Check.Equal("[ti:Title]\n[00:12.347]Hello\n[00:13.201]Next\n[00:14.509]Last", lyrics[0].Lyric, "precise QRC original");
				Check.Equal("[00:12.347]你好\n[00:14.509]最后", lyrics[0].TranslatedLyric, "translation aligned to QRC timestamps");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		}
		);

		yield return ("QQ.SearchLyrics invalid QRC falls back to base64 LRC", delegate
		{
			string fallback = "[00:01.23]fallback";
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric(fallback), "{\"req_0\":{\"code\":0,\"data\":{\"lyric\":\"not-hex\"}}}").SearchLyrics("q", 10, 0);
			Check.Equal(fallback, lyrics[0].Lyric, "fallback lyric");
		}
		);

		yield return ("QQ.SearchLyrics retries QRC once after rate limit", delegate
		{
			string qrc = "[1007,500]Recovered(1007,500)";
			List<SourceSearchStatus> statuses = new List<SourceSearchStatus>();
			StubQq provider = new StubQq(
				QqSearchOneSong,
				QqJsonpLyric("[00:01.00]fallback"),
				new[] { "{\"req_0\":{\"code\":2001}}", QqQrcResponse(qrc) });
			provider.StatusReporter = statuses.Add;
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal(2, provider.QrcRequestCount, "QRC request count");
			Check.Equal(0, provider.LegacyRequestCount, "legacy request count");
			Check.Equal(1, provider.LyricRetryWaitCount, "retry wait count");
			Check.True(lyrics[0].Lyric.EndsWith("Recovered", StringComparison.Ordinal), "retried QRC lyric");
			Check.Equal(1, statuses.Count, "status count");
			Check.Equal(SourceSearchPhase.Retrying, statuses[0].Phase, "retry status phase");
			Check.Equal("2001", statuses[0].ErrorCode, "retry status code");
			Check.Equal(1, statuses[0].RetryAttempt, "retry attempt");
			Check.Equal(1, statuses[0].RetryTotal, "retry total");
			Check.Equal(1, statuses[0].RetrySecondsLeft, "retry seconds");
		}
		);

		yield return ("QQ.SearchLyrics exhausted QRC retry still uses legacy lyric", delegate
		{
			string fallback = "[00:01.23]fallback";
			StubQq provider = new StubQq(
				QqSearchOneSong,
				QqJsonpLyric(fallback),
				new[] { "{\"req_0\":{\"code\":2001}}", "{\"req_0\":{\"code\":2001}}" });
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal(fallback, lyrics[0].Lyric, "fallback lyric");
			Check.Equal(2, provider.QrcRequestCount, "QRC request count");
			Check.Equal(1, provider.LegacyRequestCount, "legacy request count");
			Check.Equal(1, provider.LyricRetryWaitCount, "retry wait count");
		}
		);

		yield return ("QQ.SearchLyrics stops QRC retry and reports exhausted rate limit", delegate
		{
			StubQq provider = new StubQq(
				QqSearchOneSong,
				QqJsonpLyric(""),
				new[] { "{\"req_0\":{\"code\":2001}}", "{\"req_0\":{\"code\":2001}}" });
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.Equal(2, provider.QrcRequestCount, "QRC request count");
			Check.Equal(1, provider.LegacyRequestCount, "legacy request count");
			Check.Equal(1, provider.LyricRetryWaitCount, "retry wait count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.RateLimited, provider.LastTransportResult.Error, "Error");
			Check.Equal("2001", provider.LastTransportResult.ErrorCode, "ErrorCode");
		}
		);

		yield return ("QQ.SearchLyrics cancellation during QRC retry stops further requests", delegate
		{
			StubQq provider = new StubQq(
				QqSearchOneSong,
				QqJsonpLyric("[00:01.00]fallback"),
				new[] { "{\"req_0\":{\"code\":2001}}", QqQrcResponse("[1000,500]Unused(1000,500)") })
			{
				CancelOnLyricRetryWait = true
			};
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.Equal(1, provider.QrcRequestCount, "QRC request count");
			Check.Equal(0, provider.LegacyRequestCount, "legacy request count");
			Check.Equal(1, provider.LyricRetryWaitCount, "retry wait count");
		}
		);

		yield return ("QQ.SearchLyrics empty jsonp lyric -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("")).SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("QQ.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubQq provider = new StubQq("<html>not json</html>", QqJsonpLyric("x"));
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);

		// ---- Kugou：data.lrc 直取（无 landata 翻译时 TranslatedLyric 空） ----
		yield return ("Kugou.SearchLyrics maps data.lrc + fields", delegate
		{
			List<LyricSearchResult> lyrics = new StubKugou(KugouSearchOneSong, "{\"data\":{\"lrc\":\"[00:01.00]Hello\"}}").SearchLyrics("q", 10, 1);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal("[00:01.00]Hello", lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("A123", lyrics[0].TrackId, "[0].TrackId");
			Check.Equal("SongA", lyrics[0].Title, "[0].Title");
			Check.Equal("ArtistA", lyrics[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal(SearchSource.Kugou, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(1, lyrics[0].SourceOrder, "[0].SourceOrder");
		}
		);

		yield return ("Kugou.SearchLyrics empty lrc -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubKugou(KugouSearchOneSong, "{\"data\":{}}").SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("Kugou.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubKugou provider = new StubKugou("<html>not json</html>", "{\"data\":{\"lrc\":\"x\"}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);

		// ---- Kuwo：详情 lrclist（time 秒*1000，FormatTimestamp 厘秒）逐行拼装；单语规避双语重排 ----
		yield return ("Kuwo.SearchLyrics builds lyric from detail lrclist + fields", delegate
		{
			string detail = "{\"data\":{\"lrclist\":[{\"time\":\"1.5\",\"lineLyric\":\"Hello\"},{\"time\":\"3.0\",\"lineLyric\":\"World\"}]}}";
			List<LyricSearchResult> lyrics = new StubKuwo(KuwoSearchOneSong, detail).SearchLyrics("q", 10, 3);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal("[00:01.50]Hello\n[00:03.00]World\n", lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("SongA", lyrics[0].Title, "[0].Title");
			Check.Equal("ArtistA", lyrics[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal(SearchSource.Kuwo, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(3, lyrics[0].SourceOrder, "[0].SourceOrder");
		}
		);

		yield return ("Kuwo.SearchLyrics no lrclist -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubKuwo(KuwoSearchOneSong, "{\"data\":{}}").SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("Kuwo.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubKuwo provider = new StubKuwo("<html>not json</html>", "{\"data\":{\"lrclist\":[]}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);
	}
}
