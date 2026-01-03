using System;
using System.IO;
using System.Linq;

public static class FileNameHelper
{
    /// <summary>
    /// 将任意UTF-8字符串处理为合法文件名（替换禁用字符）
    /// </summary>
    /// <param name="inputStr">输入的UTF-8字符串</param>
    /// <param name="replaceChar">禁用字符的替换符（默认下划线_）</param>
    /// <returns>合法文件名</returns>
    public static string ToValidFileName(string inputStr, char replaceChar = '_')
    {
        // 1. 处理空输入，避免返回空文件名
        if (string.IsNullOrWhiteSpace(inputStr)) return "Unnamed"; // 默认文件名

        // 2. 获取当前系统禁用的所有文件名字符
        char[] invalidChars = Path.GetInvalidFileNameChars();

        // 3. 替换所有禁用字符为指定的合法字符（LINQ简洁实现）
        string validFileName = new([.. inputStr.Select(c => invalidChars.Contains(c) ? replaceChar : c)]);

        // 4. 额外处理：Windows禁止文件名以空格/句点结尾（可选，增强兼容性）
        validFileName = TrimInvalidTrailingChars(validFileName);

        // 5. 额外处理：禁止Windows特殊设备名（可选，增强兼容性）
        validFileName = ReplaceWindowsReservedNames(validFileName);

        // 6. 再次校验空值（避免替换后全为空白字符）
        return string.IsNullOrWhiteSpace(validFileName) ? "Unnamed" : validFileName;
    }

    /// <summary>
    /// 移除文件名末尾的无效字符（空格、句点）
    /// </summary>
    private static string TrimInvalidTrailingChars(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return fileName;

        // 循环移除末尾的空格或句点，直到遇到有效字符
        while (fileName.EndsWith(' ') || fileName.EndsWith('.'))
        {
            fileName = fileName.TrimEnd([' ', '.']);
            if (string.IsNullOrEmpty(fileName)) break;
        }

        return fileName;
    }

    /// <summary>
    /// 替换Windows保留的特殊设备名
    /// </summary>
    private static string ReplaceWindowsReservedNames(string fileName)
    {
        // Windows保留设备名（不区分大小写，含扩展名形式如CON.txt）
        string[] reservedNames = [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        // 分离文件名和扩展名（处理如CON.txt的情况）
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        // 若文件名（无扩展名）是保留名，则添加下划线区分
        if (reservedNames.Contains(nameWithoutExt.ToUpperInvariant()))  return $"{nameWithoutExt}_{extension}";

        return fileName;
    }
}