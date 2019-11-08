namespace Tests.CommandLine
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Threading;
    using Mono.Options;
    using SpoiledCat.SimpleIO;

    static class Program
	{
		private static string ReadAllTextIfFileExists(this string path)
		{
			var file = path.ToSPath();
			if (!file.IsInitialized || !file.FileExists())
			{
				return null;
			}
			return file.ReadAllText();
		}

		private static int Main(string[] args)
		{
			var retCode = 0;
			string data = null;
			string error = null;
			var sleepms = 0;
			var p = new OptionSet();
			var readInputToEof = false;
			var lines = new List<string>();
			SPath outfile = SPath.Default;
			SPath path = SPath.Default;
			var block = false;
			var exception = false;

			var arguments = new List<string>(args);

			p = p
				.Add("r=", (int v) => retCode = v)
				.Add("d=|data=", v => data = v)
				.Add("e=|error=", v => error = v)
				.Add("x|exception", v => exception = true)
				.Add("f=|file=", v => data = File.ReadAllText(v))
				.Add("ef=|errorFile=", v => error = File.ReadAllText(v))
				.Add("sleep=", (int v) => sleepms = v)
				.Add("i|input", v => readInputToEof = true)
				.Add("path=", v => path = v.ToSPath())
				.Add("o=|outfile=", v => outfile = v.ToSPath().MakeAbsolute())
				.Add("help", v => p.WriteOptionDescriptions(Console.Out))
				.Add("b|block", v => block = true)
				;

			var extra = p.Parse(arguments);

			if (sleepms > 0)
			{
				Thread.Sleep(sleepms);
			}

			if (block)
			{
				while (true)
				{
					if (readInputToEof)
					{
						Console.WriteLine(Console.ReadLine());
					}
				}
			}

			if (readInputToEof)
			{
				string line;
				while ((line = Console.ReadLine()) != null)
				{
					lines.Add(line);
				}
			}

			if (!string.IsNullOrEmpty(data))
			{
				Console.WriteLine(data);
			}
			else if (readInputToEof)
			{
				Console.WriteLine(string.Join(Environment.NewLine, lines.ToArray()));
			}

			if (!string.IsNullOrEmpty(error))
			{
				Console.Error.WriteLine(error);
			}

			if (exception)
			{
				throw new InvalidOperationException();
			}

			return retCode;
		}
	}
}
