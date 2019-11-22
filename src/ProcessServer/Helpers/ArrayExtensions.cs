namespace Unity.Editor.ProcessServer.Server
{
    using System.Collections;
    using System.Collections.Generic;

    public static class ArrayExtensions
    {
        public static string Join<T>(this T[] arr, string separator = ",")
        {
            return string.Join(separator, arr);
        }

        public static string Join<T>(this IEnumerable<T> arr, string separator = ",")
        {
            return string.Join(separator, arr);
        }

        public static string Join(this IEnumerable arr, string separator = ",")
        {
            return string.Join(separator, arr);
        }
    }
}
