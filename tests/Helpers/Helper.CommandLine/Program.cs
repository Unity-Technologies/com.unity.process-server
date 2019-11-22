namespace SpoiledCat.Tests.CommandLine
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Net;
	using System.Text;
	using System.Threading;
	using Logging;
	using Mono.Options;
	using SimpleIO;

	static class Program
	{
		private static ILogging Logger;

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
			LogHelper.LogAdapter = new ConsoleLogAdapter();
			Logger = LogHelper.GetLogger();

			var retCode = 0;
			string data = null;
			string error = null;
			var sleepms = 0;
			var p = new OptionSet();
			var readInputToEof = false;
			var lines = new List<string>();
			string version = null;
			var block = false;
			var exception = false;
            int loop = 0;

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
				.Add("v=|version=", v => version = v)
				.Add("help", v => p.WriteOptionDescriptions(Console.Out))
				.Add("b|block", v => block = true)
                .Add("l|loop=", (int v) => loop = v)
				;

			var extra = p.Parse(arguments);

            do
            {
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
            } while (--loop > 0);
            return retCode;
		}
	}
}
