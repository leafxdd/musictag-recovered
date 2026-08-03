using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace MusicTag.Tests;

internal static class X64RuntimeCharacterization
{
	private const string ExpectedSQLiteFileVersion = "1.0.113.0";

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Runtime process is x64", delegate
		{
			Check.True(Environment.Is64BitProcess, "64-bit test process");
		});

		yield return ("SQLite x64 interop opens and executes queries", delegate
		{
			string managedAssemblyPath = typeof(SQLiteConnection).Assembly.Location;
			string nativeInteropPath = Path.Combine(AppContext.BaseDirectory, "SQLite.Interop.dll");
			Check.True(File.Exists(nativeInteropPath), "SQLite.Interop.dll exists");
			Check.Equal(ExpectedSQLiteFileVersion, FileVersionInfo.GetVersionInfo(managedAssemblyPath).FileVersion, "managed SQLite file version");
			Check.Equal(ExpectedSQLiteFileVersion, FileVersionInfo.GetVersionInfo(nativeInteropPath).FileVersion, "native SQLite file version");

			using SQLiteConnection connection = new SQLiteConnection("Data Source=:memory:;Version=3;New=True;");
			connection.Open();
			using SQLiteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT 1, sqlite_version()";
			using SQLiteDataReader reader = command.ExecuteReader();
			Check.True(reader.Read(), "SQLite query returned a row");
			Check.Equal(1L, reader.GetInt64(0), "SQLite SELECT 1");
			Check.Equal("3.32.1", reader.GetString(1), "SQLite engine version");
		});
	}
}
