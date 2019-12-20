namespace Unity.ProcessServer
{
	using System.Text.RegularExpressions;
	using Editor.Tasks;

	public class PortOutputProcessor : FirstResultOutputProcessor<int>
	{
		private readonly Regex regex;
		private const string portRegex = @"^Port:(?<port>\d+)";
		private static readonly Regex DefaultPortRegex = new Regex(portRegex);

		public PortOutputProcessor(Regex regex = null)
		{
			this.regex = regex ?? DefaultPortRegex;
		}

		protected override bool ProcessLine(string line, out int result)
		{
			result = 0;
			var match = regex.Match(line);
			if (!match.Success)
			{
				return false;
			}

			result = int.Parse(match.Groups["port"].Value);
			return true;
		}
	}
}
