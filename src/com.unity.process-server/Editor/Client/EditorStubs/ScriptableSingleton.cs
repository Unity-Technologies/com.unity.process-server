#if !UNITY_EDITOR

namespace Unity.ProcessServer.EditorStubs
{
	public class ScriptableSingleton<T>
		where T : class, new()
	{
		private static T _instance;
		public static T instance => _instance ?? (_instance = new T());
		public static T Instance => instance;

		protected void Save(bool flush)
		{ }
	}
}
#endif
