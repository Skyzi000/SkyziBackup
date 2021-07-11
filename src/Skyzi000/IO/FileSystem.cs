using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;

namespace Skyzi000.IO
{
    public static class FileSystem
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string SymLoopMessage = "シンボリックリンク(リパースポイント)がループしている可能性があります。";

        public static IEnumerable<string> EnumerateAllFilesIgnoreReparsePoints(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Enumerable.Empty<string>() : Directory.EnumerateFiles(path)
             .Union(Directory.EnumerateDirectories(path)
             .SelectMany(s =>
             {
                 try
                 {
                     return EnumerateAllFilesIgnoreReparsePoints(s);
                 }
                 catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                 {
                     Logger.Error(e, "'{0}'の列挙に失敗", s);
                     return Enumerable.Empty<string>();
                 }
             }));

        public static IEnumerable<string> EnumerateAllFilesIgnoreReparsePoints(string path, IEnumerable<Regex>? ignoreDirectoryRegices, int matchingStartIndex = -1)
        {
            if (ignoreDirectoryRegices is null)
                return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Enumerable.Empty<string>() : Directory.EnumerateFiles(path)
                 .Union(Directory.EnumerateDirectories(path)
                 .SelectMany(s =>
                 {
                     try
                     {
                         return EnumerateAllFilesIgnoreReparsePoints(s);
                     }
                     catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                     {
                         Logger.Error(e, "'{0}'の列挙に失敗", s);
                         return Enumerable.Empty<string>();
                     }
                 }));
            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Enumerable.Empty<string>() : Directory.EnumerateFiles(path)
                .Union(Directory.EnumerateDirectories(path)
                .Where(d =>
                {
                    var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                    return ignoreDirectoryRegices.All(r => !r.IsMatch(matchingPath));
                })
                .SelectMany(s =>
                {
                    try
                    {
                        return EnumerateAllFilesIgnoreReparsePoints(s, ignoreDirectoryRegices, matchingStartIndex);
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));
        }

        public static IEnumerable<string> EnumerateAllDirectoriesIgnoreReparsePoints(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Enumerable.Empty<string>() : Enumerable.Empty<string>()
                .Append(path)
                .Union(Directory.EnumerateDirectories(path)
                .SelectMany(s =>
                {
                    try
                    {
                        return EnumerateAllDirectoriesIgnoreReparsePoints(s);
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));

        public static IEnumerable<string> EnumerateAllDirectoriesIgnoreReparsePoints(string path, IEnumerable<Regex>? ignoreDirectoryRegices = null, int matchingStartIndex = -1)
        {
            if (ignoreDirectoryRegices is null)
                return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Enumerable.Empty<string>() : Enumerable.Empty<string>()
                    .Append(path)
                    .Union(Directory.EnumerateDirectories(path)
                    .SelectMany(s =>
                    {
                        try
                        {
                            return EnumerateAllDirectoriesIgnoreReparsePoints(s);
                        }
                        catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                        {
                            Logger.Error(e, "'{0}'の列挙に失敗", s);
                            return Enumerable.Empty<string>();
                        }
                    }));
            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? Enumerable.Empty<string>() : Enumerable.Empty<string>()
                .Append(path)
                .Union(Directory.EnumerateDirectories(path)
                .Where(d =>
                {
                    var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                    return ignoreDirectoryRegices.All(r => !r.IsMatch(matchingPath));
                })
                .SelectMany(s =>
                {
                    try
                    {
                        return EnumerateAllDirectoriesIgnoreReparsePoints(s, ignoreDirectoryRegices, matchingStartIndex);
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));
        }

        public static IEnumerable<string> EnumerateAllFiles(string path) => Directory.EnumerateFiles(path)
             .Union(Directory.EnumerateDirectories(path)
             .SelectMany(s =>
             {
                 try
                 {
                     return EnumerateAllFiles(s);
                 }
                 catch (IOException e)
                 {
                     Logger.Error(e, SymLoopMessage);
                     return Enumerable.Empty<string>();
                 }
                 catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                 {
                     Logger.Error(e, "'{0}'の列挙に失敗", s);
                     return Enumerable.Empty<string>();
                 }
             }));

        public static IEnumerable<string> EnumerateAllFiles(string path, IEnumerable<Regex>? ignoreDirectoryRegices, int matchingStartIndex = -1)
        {
            if (ignoreDirectoryRegices is null)
                return Directory.EnumerateFiles(path)
                 .Union(Directory.EnumerateDirectories(path)
                 .SelectMany(s =>
                 {
                     try
                     {
                         return EnumerateAllFiles(s);
                     }
                     catch (IOException e)
                     {
                         Logger.Error(e, SymLoopMessage);
                         return Enumerable.Empty<string>();
                     }
                     catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                     {
                         Logger.Error(e, "'{0}'の列挙に失敗", s);
                         return Enumerable.Empty<string>();
                     }
                 }));
            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return Directory.EnumerateFiles(path)
                .Union(Directory.EnumerateDirectories(path)
                .Where(d =>
                {
                    var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                    return ignoreDirectoryRegices.All(r => !r.IsMatch(matchingPath));
                })
                .SelectMany(s =>
                {
                    try
                    {
                        return EnumerateAllFiles(s, ignoreDirectoryRegices, matchingStartIndex);
                    }
                    catch (IOException e)
                    {
                        Logger.Error(e, SymLoopMessage);
                        return Enumerable.Empty<string>();
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));
        }

        public static IEnumerable<string> EnumerateAllDirectories(string path) => Enumerable.Empty<string>()
                .Append(path)
                .Union(Directory.EnumerateDirectories(path)
                .SelectMany(s =>
                {
                    try
                    {
                        return EnumerateAllDirectories(s);
                    }
                    catch (IOException e)
                    {
                        Logger.Error(e, SymLoopMessage);
                        return Enumerable.Empty<string>();
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));

        public static IEnumerable<string> EnumerateAllDirectories(string path, IEnumerable<Regex>? ignoreDirectoryRegices = null, int matchingStartIndex = -1)
        {
            if (ignoreDirectoryRegices is null)
                return Enumerable.Empty<string>()
                    .Append(path)
                    .Union(Directory.EnumerateDirectories(path)
                    .SelectMany(s =>
                    {
                        try
                        {
                            return EnumerateAllDirectories(s);
                        }
                        catch (IOException e)
                        {
                            Logger.Error(e, SymLoopMessage);
                            return Enumerable.Empty<string>();
                        }
                        catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                        {
                            Logger.Error(e, "'{0}'の列挙に失敗", s);
                            return Enumerable.Empty<string>();
                        }
                    }));
            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return Enumerable.Empty<string>()
                .Append(path)
                .Union(Directory.EnumerateDirectories(path)
                .Where(d =>
                {
                    var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                    return ignoreDirectoryRegices.All(r => !r.IsMatch(matchingPath));
                })
                .SelectMany(s =>
                {
                    try
                    {
                        return EnumerateAllDirectories(s, ignoreDirectoryRegices, matchingStartIndex);
                    }
                    catch (IOException e)
                    {
                        Logger.Error(e, SymLoopMessage);
                        return Enumerable.Empty<string>();
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));
        }

    }
}
