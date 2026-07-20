using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;

namespace Skyzi000.IO
{
    public static class FileSystem
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string SymLoopMessage = "シンボリックリンク(リパースポイント)がループしている可能性があります。";

        public static IEnumerable<string> EnumerateAllFilesIgnoringReparsePoints(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
            ? Enumerable.Empty<string>()
            : Directory.EnumerateFiles(path)
                .Union(Directory.EnumerateDirectories(path)
                    .SelectMany(s =>
                    {
                        try
                        {
                            return EnumerateAllFilesIgnoringReparsePoints(s);
                        }
                        catch (Exception e) when (e is UnauthorizedAccessException
                                                      or DirectoryNotFoundException
                                                      or FileNotFoundException
                                                      or PathTooLongException)
                        {
                            Logger.Error(e, "'{0}'の列挙に失敗", s);
                            return Enumerable.Empty<string>();
                        }
                    }));

        /// <param name="onEnumerationError">
        /// 配下の列挙に失敗してその部分を空列挙として飛ばす際に呼ばれる。
        /// 「列挙が全域を網羅できたか」を呼び出し元が判定する必要がある場合に指定する。
        /// </param>
        public static IEnumerable<string> EnumerateAllFilesIgnoringReparsePoints(string path,
            IEnumerable<Regex>? ignoreDirectoryRegexes,
            int matchingStartIndex = -1,
            Action<Exception>? onEnumerationError = null)
        {
            if (ignoreDirectoryRegexes is null)
            {
                return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                    ? Enumerable.Empty<string>()
                    : Directory.EnumerateFiles(path)
                        .Union(Directory.EnumerateDirectories(path)
                            .SelectMany(s =>
                            {
                                try
                                {
                                    return EnumerateAllFilesIgnoringReparsePoints(s, null, -1, onEnumerationError);
                                }
                                catch (Exception e) when (e is UnauthorizedAccessException
                                                              or DirectoryNotFoundException
                                                              or FileNotFoundException
                                                              or PathTooLongException)
                                {
                                    Logger.Error(e, "'{0}'の列挙に失敗", s);
                                    onEnumerationError?.Invoke(e);
                                    return Enumerable.Empty<string>();
                                }
                            }));
            }

            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                ? Enumerable.Empty<string>()
                : Directory.EnumerateFiles(path)
                    .Union(Directory.EnumerateDirectories(path)
                        .Where(d =>
                        {
                            var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                            return ignoreDirectoryRegexes.All(r => !r.IsMatch(matchingPath));
                        })
                        .SelectMany(s =>
                        {
                            try
                            {
                                return EnumerateAllFilesIgnoringReparsePoints(s, ignoreDirectoryRegexes, matchingStartIndex, onEnumerationError);
                            }
                            catch (Exception e) when (e is UnauthorizedAccessException
                                                          or DirectoryNotFoundException
                                                          or FileNotFoundException
                                                          or PathTooLongException)
                            {
                                Logger.Error(e, "'{0}'の列挙に失敗", s);
                                onEnumerationError?.Invoke(e);
                                return Enumerable.Empty<string>();
                            }
                        }));
        }

        public static IEnumerable<string> EnumerateAllDirectoriesIgnoringReparsePoints(string path) =>
            File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                ? Enumerable.Empty<string>()
                : Enumerable.Empty<string>()
                    .Append(path)
                    .Union(Directory.EnumerateDirectories(path)
                        .SelectMany(s =>
                        {
                            try
                            {
                                return EnumerateAllDirectoriesIgnoringReparsePoints(s);
                            }
                            catch (Exception e) when (e is UnauthorizedAccessException
                                                          or DirectoryNotFoundException
                                                          or FileNotFoundException
                                                          or PathTooLongException)
                            {
                                Logger.Error(e, "'{0}'の列挙に失敗", s);
                                return Enumerable.Empty<string>();
                            }
                        }));

        /// <param name="onEnumerationError">
        /// 配下の列挙に失敗してその部分を空列挙として飛ばす際に呼ばれる。
        /// 「列挙が全域を網羅できたか」を呼び出し元が判定する必要がある場合に指定する。
        /// </param>
        public static IEnumerable<string> EnumerateAllDirectoriesIgnoringReparsePoints(string path,
            IEnumerable<Regex>? ignoreDirectoryRegexes = null,
            int matchingStartIndex = -1,
            Action<Exception>? onEnumerationError = null)
        {
            if (ignoreDirectoryRegexes is null)
            {
                return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                    ? Enumerable.Empty<string>()
                    : Enumerable.Empty<string>()
                        .Append(path)
                        .Union(Directory.EnumerateDirectories(path)
                            .SelectMany(s =>
                            {
                                try
                                {
                                    return EnumerateAllDirectoriesIgnoringReparsePoints(s, null, -1, onEnumerationError);
                                }
                                catch (Exception e) when (e is UnauthorizedAccessException
                                                              or DirectoryNotFoundException
                                                              or FileNotFoundException
                                                              or PathTooLongException)
                                {
                                    Logger.Error(e, "'{0}'の列挙に失敗", s);
                                    onEnumerationError?.Invoke(e);
                                    return Enumerable.Empty<string>();
                                }
                            }));
            }

            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                ? Enumerable.Empty<string>()
                : Enumerable.Empty<string>()
                    .Append(path)
                    .Union(Directory.EnumerateDirectories(path)
                        .Where(d =>
                        {
                            var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                            return ignoreDirectoryRegexes.All(r => !r.IsMatch(matchingPath));
                        })
                        .SelectMany(s =>
                        {
                            try
                            {
                                return EnumerateAllDirectoriesIgnoringReparsePoints(s, ignoreDirectoryRegexes, matchingStartIndex, onEnumerationError);
                            }
                            catch (Exception e) when (e is UnauthorizedAccessException
                                                          or DirectoryNotFoundException
                                                          or FileNotFoundException
                                                          or PathTooLongException)
                            {
                                Logger.Error(e, "'{0}'の列挙に失敗", s);
                                onEnumerationError?.Invoke(e);
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
                    catch (Exception e) when (e is UnauthorizedAccessException
                                                  or DirectoryNotFoundException
                                                  or FileNotFoundException
                                                  or PathTooLongException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));

        /// <param name="onEnumerationError">
        /// 配下の列挙に失敗してその部分を空列挙として飛ばす際に呼ばれる。
        /// 「列挙が全域を網羅できたか」を呼び出し元が判定する必要がある場合に指定する。
        /// </param>
        public static IEnumerable<string> EnumerateAllFiles(string path,
            IEnumerable<Regex>? ignoreDirectoryRegexes,
            int matchingStartIndex = -1,
            Action<Exception>? onEnumerationError = null)
        {
            if (ignoreDirectoryRegexes is null)
            {
                return Directory.EnumerateFiles(path)
                    .Union(Directory.EnumerateDirectories(path)
                        .SelectMany(s =>
                        {
                            try
                            {
                                return EnumerateAllFiles(s, null, -1, onEnumerationError);
                            }
                            catch (IOException e)
                            {
                                Logger.Error(e, SymLoopMessage);
                                onEnumerationError?.Invoke(e);
                                return Enumerable.Empty<string>();
                            }
                            catch (Exception e) when (e is UnauthorizedAccessException
                                                          or DirectoryNotFoundException
                                                          or FileNotFoundException
                                                          or PathTooLongException)
                            {
                                Logger.Error(e, "'{0}'の列挙に失敗", s);
                                onEnumerationError?.Invoke(e);
                                return Enumerable.Empty<string>();
                            }
                        }));
            }

            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return Directory.EnumerateFiles(path)
                .Union(Directory.EnumerateDirectories(path)
                    .Where(d =>
                    {
                        var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                        return ignoreDirectoryRegexes.All(r => !r.IsMatch(matchingPath));
                    })
                    .SelectMany(s =>
                    {
                        try
                        {
                            return EnumerateAllFiles(s, ignoreDirectoryRegexes, matchingStartIndex, onEnumerationError);
                        }
                        catch (IOException e)
                        {
                            Logger.Error(e, SymLoopMessage);
                            onEnumerationError?.Invoke(e);
                            return Enumerable.Empty<string>();
                        }
                        catch (Exception e) when (e is UnauthorizedAccessException
                                                      or DirectoryNotFoundException
                                                      or FileNotFoundException
                                                      or PathTooLongException)
                        {
                            Logger.Error(e, "'{0}'の列挙に失敗", s);
                            onEnumerationError?.Invoke(e);
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
                    catch (Exception e) when (e is UnauthorizedAccessException
                                                  or DirectoryNotFoundException
                                                  or FileNotFoundException
                                                  or PathTooLongException)
                    {
                        Logger.Error(e, "'{0}'の列挙に失敗", s);
                        return Enumerable.Empty<string>();
                    }
                }));

        /// <param name="onEnumerationError">
        /// 配下の列挙に失敗してその部分を空列挙として飛ばす際に呼ばれる。
        /// 「列挙が全域を網羅できたか」を呼び出し元が判定する必要がある場合に指定する。
        /// </param>
        public static IEnumerable<string> EnumerateAllDirectories(string path,
            IEnumerable<Regex>? ignoreDirectoryRegexes,
            int matchingStartIndex = -1,
            Action<Exception>? onEnumerationError = null)
        {
            if (ignoreDirectoryRegexes is null)
            {
                return Enumerable.Empty<string>()
                    .Append(path)
                    .Union(Directory.EnumerateDirectories(path)
                        .SelectMany(s =>
                        {
                            try
                            {
                                return EnumerateAllDirectories(s, null, -1, onEnumerationError);
                            }
                            catch (IOException e)
                            {
                                Logger.Error(e, SymLoopMessage);
                                onEnumerationError?.Invoke(e);
                                return Enumerable.Empty<string>();
                            }
                            catch (Exception e) when (e is UnauthorizedAccessException
                                                          or DirectoryNotFoundException
                                                          or FileNotFoundException
                                                          or PathTooLongException)
                            {
                                Logger.Error(e, "'{0}'の列挙に失敗", s);
                                onEnumerationError?.Invoke(e);
                                return Enumerable.Empty<string>();
                            }
                        }));
            }

            if (matchingStartIndex == -1)
                matchingStartIndex = path.Length - 1;
            return Enumerable.Empty<string>()
                .Append(path)
                .Union(Directory.EnumerateDirectories(path)
                    .Where(d =>
                    {
                        var matchingPath = (d + Path.DirectorySeparatorChar)[matchingStartIndex..];
                        return ignoreDirectoryRegexes.All(r => !r.IsMatch(matchingPath));
                    })
                    .SelectMany(s =>
                    {
                        try
                        {
                            return EnumerateAllDirectories(s, ignoreDirectoryRegexes, matchingStartIndex, onEnumerationError);
                        }
                        catch (IOException e)
                        {
                            Logger.Error(e, SymLoopMessage);
                            onEnumerationError?.Invoke(e);
                            return Enumerable.Empty<string>();
                        }
                        catch (Exception e) when (e is UnauthorizedAccessException
                                                      or DirectoryNotFoundException
                                                      or FileNotFoundException
                                                      or PathTooLongException)
                        {
                            Logger.Error(e, "'{0}'の列挙に失敗", s);
                            onEnumerationError?.Invoke(e);
                            return Enumerable.Empty<string>();
                        }
                    }));
        }
    }
}
