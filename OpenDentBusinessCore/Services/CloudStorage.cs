// Compatibility surface for upstream storage callers; transport is provider-neutral HTTP.
// Modified 2026-10-24; see NOTICE. Provider implementations are external services.
using System;
using System.Collections.Generic;
using System.IO;
namespace OpenDentBusiness {
    public static class CloudStorage {
        public const string LanThis="CloudStorage";
        public static bool IsCloudStorage => PrefC.AtoZfolderUsed==DataStorageType.DropboxAtoZ || PrefC.AtoZfolderUsed==DataStorageType.SftpAtoZ;
        public static char DirectorySeparatorChar => IsCloudStorage ? '/' : Path.DirectorySeparatorChar;
        public static string AtoZPath => IsCloudStorage
            ? Environment.GetEnvironmentVariable("APEX_STORAGE_ROOT")??"/"
            : throw new InvalidOperationException("Local storage does not use the optional storage service.");
        public static void Upload(string folder,string fileName,byte[] byteArray,bool hasExceptions=false) =>
            PracticeServiceClient.Call<object>("storage/upload",new {folder,fileName,data=byteArray});
        public static byte[] Download(string folder,string fileName) =>
            PracticeServiceClient.Call<byte[]>("storage/download",new {folder,fileName});
        public static void Delete(string path) => PracticeServiceClient.Call<object>("storage/delete",new {path});
        public static byte[] GetThumbnail(string folder,string fileName) =>
            PracticeServiceClient.Call<byte[]>("storage/thumbnail",new {folder,fileName});
        public static bool Move(string fromPath,string toPath,bool hasExceptions=false) =>
            PracticeServiceClient.Call<bool>("storage/move",new {fromPath,toPath});
        public static void Copy(string fromPath,string toPath) => PracticeServiceClient.Call<object>("storage/copy",new {fromPath,toPath});
        public static List<string> ListFolderContents(string folderPath) =>
            PracticeServiceClient.Call<List<string>>("storage/list",new {folderPath});
        public static bool FileExists(string path) => PracticeServiceClient.Call<bool>("storage/exists",new {path});
        public static bool FileExists(string folder,string fileName) => FileExists(PathTidy(Path.Combine(folder,fileName)));
        public static string PathTidy(string path) => IsCloudStorage ? path.Replace('\\','/') : path;
    }
}
