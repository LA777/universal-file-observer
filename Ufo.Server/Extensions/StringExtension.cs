using System.Security.Cryptography;

namespace Ufo.Extensions
{
    public static class StringExtension
    {
        public static string GetHashSha256(this string text)
        {
            using var sha256 = SHA256.Create();
            var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
            var hashBytes = sha256.ComputeHash(textBytes);
            var result = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLower();

            return result;
        }
    }
}
