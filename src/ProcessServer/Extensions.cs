namespace Unity.ProcessServer.Server
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading.Tasks;
	using Editor.Tasks;
	using Interfaces;

	internal static class Extensions
	{
		public static void AddOrUpdate<Key, Val>(this Dictionary<Key, Val> list, Key key, Val value)
		{
			if (list.ContainsKey(key))
				list[key] = value;
			else
				list.Add(key, value);
		}

		public static Val Get<Key, Val>(this Dictionary<Key, Val> list, Key key, Val value = default)
		{
			if (!list.TryGetValue(key, out value))
			{
				list.Add(key, value);
			}
            return value;
		}


        public static string GetExceptionMessage(this Exception ex)
		{
			if (ex == null) return string.Empty;
			var message = GetExceptionMessageShort(ex);

			message += Environment.NewLine + "=======";

			var caller = Environment.StackTrace;
			var stack = caller.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
			message += Environment.NewLine + string.Join(Environment.NewLine, stack.Skip(1).SkipWhile(x => x.Contains(nameof(GetExceptionMessage)) || x.Contains("LogFacade")).ToArray());
			return message;
		}

		public static string GetExceptionMessageShort(this Exception ex)
		{
			if (ex == null) return string.Empty;
			var message = ex.ToString();
			var inner = ex.InnerException;
			while (inner != null)
			{
				message += Environment.NewLine + inner.ToString();
				inner = inner.InnerException;
			}
			return message;
		}

		internal static void Schedule(this SynchronizationContextTaskScheduler scheduler, ITaskManager taskManager, Func<Task> action)
		{
			taskManager.WithAsync(action, TaskAffinity.Custom).Start(scheduler);
		}


		public static string GetId(this ProcessInfo info)
		{
			StringBuilder env = new StringBuilder();
			foreach (var e in info.Environment)
			{
				env.Append(e.Key);
				env.Append(":");
				env.Append(e.Value);
				env.Append(";");
			}

			var sb = new StringBuilder();
			sb.AppendFormat("{0}-{1}-{2}-{3}-{4}{5}{6}{7}{8}{9}",
				info.WorkingDirectory?.ToUpperInvariant().GetHashCode() ?? 0,
				info.FileName?.ToUpperInvariant().GetHashCode() ?? 0,
				info.Arguments?.ToUpperInvariant().GetHashCode() ?? 0,
				env.ToString().GetHashCode(),
				info.UseShellExecute.GetHashCode(),
				info.RedirectStandardError.GetHashCode(),
				info.RedirectStandardOutput.GetHashCode(),
				info.RedirectStandardInput.GetHashCode(),
				info.CreateNoWindow.GetHashCode(),
				info.WindowStyle.GetHashCode());

			return sb.ToString();
		}

	}
}
