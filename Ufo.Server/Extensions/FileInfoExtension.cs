using Microsoft.VisualBasic.FileIO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Ufo.Extensions;

public static class FileInfoExtension
{
    public static bool DeleteToRecycleBin(this FileInfo fileInfo)
    {
        fileInfo.Refresh();

        if (fileInfo is not { Exists: true })
        {
            return false;
        }

        FileSystem.DeleteFile(fileInfo.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

        return true;
    }

    public static string GenerateFileFullPath(this FileInfo fileInfo, string destinationFolderFullPath)
    {
        var destinationImagePath = Path.Combine(destinationFolderFullPath, fileInfo.Name);
        if (!File.Exists(destinationImagePath))
        {
            return destinationImagePath;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileInfo.Name);

        const string regexPattern = "\\([0-9]+\\)$";
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        if (!regex.IsMatch(fileNameWithoutExtension))
        {
            destinationImagePath = Path.Combine(destinationFolderFullPath, $"{fileNameWithoutExtension} (0){fileInfo.Extension}");

            if (!File.Exists(destinationImagePath))
            {
                return destinationImagePath;
            }
        }

        do
        {
            const string regexPatternForIndex = "[0-9]+";
            var regexIndex = new Regex(regexPatternForIndex, RegexOptions.IgnoreCase);

            var destinationFileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationImagePath); // '101 (0)'
            var indexInBraces = regex.Match(destinationFileNameWithoutExtension); // '(0)'

            var match = regexIndex.Match(indexInBraces.Value);
            var value = match.Value; // '0'
            var index = Convert.ToInt32(value);
            index++;

            var length = indexInBraces.Length;
            var nameWithoutIndex = destinationFileNameWithoutExtension.Substring(0, destinationFileNameWithoutExtension.Length - length);

            destinationImagePath = Path.Combine(destinationFolderFullPath, $"{nameWithoutIndex}({index}){fileInfo.Extension}");
        } while (File.Exists(destinationImagePath));

        return destinationImagePath;
    }

    public static string GetFileHashSha256(this FileInfo fileInfo)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(fileInfo.FullName); // TODO LA - Use async method and Try - Throws exception
        var byteArray = sha256.ComputeHash(fileStream);
        var result = BitConverter.ToString(byteArray).Replace("-", string.Empty).ToLower();

        return result;
    }
}