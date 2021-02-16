using Skyzi000.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SkyziBackup
{
    internal class DirectoryBackup
    {
        public BackupResults Results { get; private set; }
        public OpensslCompatibleAesCrypter AesCrypter { get; set; }
        public bool CopyAttributes { get; set; }


        private string _originPath;
        private string _destPath;

        public DirectoryBackup(string originPath, string destPath, string password = "")
        {
            _originPath = originPath;
            _destPath = destPath;
            if (password != "")
            {
                AesCrypter = new OpensslCompatibleAesCrypter(password);
            }
        }

        public class BackupResults
        {
            /// <summary>
            /// 成功したかどうか。一つでも失敗したファイルがあるとfalse。
            /// </summary>
            public bool isSuccess;

            /// <summary>
            /// 全体的なメッセージ。
            /// </summary>
            public string message;

            /// <summary>
            /// 新しくバックアップされたファイルのパス。
            /// </summary>
            public List<string> backedupFileList;

            /// <summary>
            /// バックアップ対象だが失敗したファイルのパス。
            /// </summary>
            public List<string> failedFileList;

            public BackupResults(bool isSuccess, string message = "")
            {
                this.isSuccess = isSuccess;
                this.message = message;
                backedupFileList = new List<string>();
                failedFileList = new List<string>();
            }
        }

        public BackupResults StartBackup()
        {
            if (!Directory.Exists(_originPath)) return new BackupResults(false, "バックアップ元のディレクトリが見つかりません。");
            Results = new BackupResults(false);
            foreach (string originDirPath in Directory.EnumerateDirectories(_originPath, "*", SearchOption.AllDirectories))
            {
                string destDirPath = originDirPath.Replace(_originPath, _destPath);
                Directory.CreateDirectory(destDirPath);
                if (CopyAttributes)
                {
                    Directory.SetCreationTime(destDirPath, Directory.GetCreationTime(originDirPath));
                    Directory.SetLastWriteTime(destDirPath, Directory.GetLastWriteTime(originDirPath));
                }
            }
            //try
            //{
            foreach (string originFilePath in System.IO.Directory.EnumerateFiles(_originPath, "*", System.IO.SearchOption.AllDirectories))
            {
                string destFilePath = originFilePath.Replace(_originPath, _destPath);
                Debug.WriteLine($"Backup {originFilePath} > {destFilePath}");
                if (AesCrypter != null)
                {
                    try
                    {
                        if (AesCrypter.EncryptFile(originFilePath, destFilePath))
                        {
                            if (CopyAttributes)
                            {
                                var attributes = File.GetAttributes(originFilePath);
                                //if((attributes & FileAttributes.Compressed) == FileAttributes.Compressed)
                                //{
                                //    attributes = RemoveAttribute(attributes, FileAttributes.Compressed);
                                //}
                                File.SetAttributes(destFilePath, attributes);
                                
                                File.SetCreationTime(destFilePath, File.GetCreationTime(originFilePath));
                                File.SetLastWriteTime(destFilePath, File.GetLastWriteTime(originFilePath));
                            }
                            Debug.WriteLine("Success!");
                            Results.backedupFileList.Add(originFilePath);
                        }
                        else
                        {
                            Debug.WriteLine($"Failed {AesCrypter.Error}");
                            Results.failedFileList.Add(originFilePath);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine($"Failed {e}");
                        Results.failedFileList.Add(originFilePath);
                    }
                }
                else
                {
                    throw new NotImplementedException("非暗号化バックアップは未実装です。");
                }
            }
            //}
            //catch (Exception)
            //{
            //    throw;
            //}

            Results.isSuccess = Results.failedFileList.Count == 0;
            return Results;
        }
        private static FileAttributes RemoveAttribute(FileAttributes attributes, FileAttributes attributesToRemove)
        {
            return attributes & ~attributesToRemove;
        }
    }
}