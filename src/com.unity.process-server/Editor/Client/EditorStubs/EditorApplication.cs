#if !UNITY_EDITOR
namespace Unity.ProcessServer.EditorStubs
{
	public static class EditorApplication
	{
		public static string applicationPath { get; set; }
		public static string applicationContentsPath { get; set; }
	}
}
#endif
