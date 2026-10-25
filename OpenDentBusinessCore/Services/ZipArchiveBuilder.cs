// Framework compression adapter for the small subset used by inherited claim exporters.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CodeBase {
    public sealed class ZipArchiveBuilder : IDisposable {
        readonly List<Action<ZipArchive>> entries=new();
        readonly string source;
        public ZipArchiveBuilder() { }
        public ZipArchiveBuilder(string filename) { source=filename; }
        static string EntryName(string filename) {
            string name=Path.GetFileName(filename.Replace('\\','/'));
            if(string.IsNullOrEmpty(name) || name=="." || name=="..") { throw new InvalidDataException("Invalid archive entry name."); }
            return name;
        }
        public void AddFile(string filename,string directory="") {
            string name=EntryName(filename);
            entries.Add(archive => archive.CreateEntryFromFile(filename,name,CompressionLevel.Optimal));
        }
        public void AddEntry(string filename,string text) {
            string name=EntryName(filename);
            entries.Add(archive => {
                using var writer=new StreamWriter(archive.CreateEntry(name,CompressionLevel.Optimal).Open(),new UTF8Encoding(false));
                writer.Write(text);
            });
        }
        public void Save(Stream output) {
            using var archive=new ZipArchive(output,ZipArchiveMode.Create,leaveOpen:true);
            foreach(var entry in entries) { entry(archive); }
        }
        public void Save(string filename) {
            using var output=File.Create(filename);
            Save(output);
        }
        public void ExtractAll(string directory) {
            using var archive=System.IO.Compression.ZipFile.OpenRead(source);
            if(archive.Entries.Count>1000 || archive.Entries.Sum(entry => entry.Length)>256L*1024*1024) {
                throw new InvalidDataException("Archive exceeds report extraction limits.");
            }
            // The framework rejects entries escaping the destination directory.
            archive.ExtractToDirectory(directory,overwriteFiles:false);
        }
        public void Dispose() { entries.Clear(); }
    }
}
