#if !UNITY_EDITOR
namespace Unity.ProcessServer.EditorStubs
{
	public static class Application
	{
		public static string productName { get; } = "DefaultApplication";
		public static string unityVersion { get; set; } = "2019.2.1f1";
		public static string projectPath { get; set; }
	}
}
#endif
