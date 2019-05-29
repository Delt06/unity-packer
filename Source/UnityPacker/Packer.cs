﻿using System;
using System.IO;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using YamlDotNet.RepresentationModel;

namespace UnityPacker {
    public static class Packer
    {
        static YamlMappingNode ReadMeta(string file)
        {
            using (var read = new StreamReader(file))
            {
                var metaYaml = new YamlStream();
                metaYaml.Load(read);
                return (YamlMappingNode) metaYaml.Documents[0].RootNode;
            }
        }

        /// <summary>
        /// Creates a Package object from the given directory
        /// </summary>
        /// <param name="folderToPack">Directory to pack</param>
        /// <param name="packageName">Name of the package</param>
        /// <param name="respectMeta">Whether metadata files should be kept or not</param>
        /// <param name="omitExts">Extensions to omit</param>
        /// <param name="omitDirs">Directories to omit</param>
        /// <returns></returns>
        public static Package PackDirectory(string folderToPack, bool respectMeta, string ignoreRegex)
        {
            var files = Directory.GetFiles(folderToPack, "*.*", SearchOption.AllDirectories);

            // Create a package object from the given directory
            var pack = new Package();

            foreach (var file in files)
            {
                var altName = file;
                if (file.StartsWith("."))
                    altName = file.Replace("." + Path.DirectorySeparatorChar, "");

                string directory = Path.GetDirectoryName(file);
                string extension = Path.GetExtension(file).ToLower();

                bool skip = Regex.IsMatch(file, ignoreRegex) || extension == ".meta";

                if (skip)
                    continue;

                var meta = new YamlMappingNode {
                    {"guid", RandomUtils.RandomHash()},
                    {"fileFormatVersion", "2"}
                };

                if (respectMeta && File.Exists(file + ".meta")) {
                    var metaFile = file + ".meta";
                    meta = ReadMeta(metaFile);
                }

                var f = new OnDiskFile(file, file, meta);
                pack.PushFile(f);
            }

            return pack;
        }

        public static Package ReadPackage(string pathToPack)
        {
            Stream inStream = File.Open(pathToPack, FileMode.Open, FileAccess.Read);
            Stream gziStream = new GZipInputStream(inStream);
            TarArchive ar = TarArchive.CreateInputTarArchive(gziStream);

            var name = Path.GetFileNameWithoutExtension(pathToPack);

            var tmpPath = Path.Combine(Path.GetTempPath(), "packUnity" + name);
            if (Directory.Exists(tmpPath))
            {
                Directory.Delete(tmpPath, true);
            }

            ar.ExtractContents(tmpPath);
            ar.Close();
            gziStream.Close();
            inStream.Close();

            var dirs = Directory.GetDirectories(tmpPath);

            var pack = new Package();
            foreach (var dir in dirs)
            {
                var assetPath = File.ReadAllText(Path.Combine(dir, "pathname"));
                var meta = ReadMeta(Path.Combine(dir, "asset.meta"));
                var diskPath = Path.Combine(dir, "asset");
                var guid = Path.GetFileName(dir);
                if (((YamlScalarNode) meta["guid"]).Value != guid)
                {
                    using (var stderr = new StreamWriter(Console.OpenStandardError()))
                    {
                        stderr.WriteLine("Erroneous File In Package! {0}, {1}", assetPath, guid);
                    }
                    continue;
                }
                var file = new OnDiskFile(assetPath, diskPath, meta);
                pack.PushFile(file);
            }

            return pack;
        }

        internal static void CreateTarGZ(string tgzPath, string sourceDirectory)
        {
            Stream outStream = File.Create(tgzPath);
            Stream gzoStream = new GZipOutputStream(outStream);
            TarArchive tarArchive = TarArchive.CreateOutputTarArchive(gzoStream);

            tarArchive.RootPath = sourceDirectory.Replace('\\', '/');
            if (tarArchive.RootPath.EndsWith("/"))
                tarArchive.RootPath = tarArchive.RootPath.Remove(tarArchive.RootPath.Length - 1);

            tarArchive.AddDirectoryFilesToTar(sourceDirectory, true);

            tarArchive.Close();
        }

        private static void AddDirectoryFilesToTar(this TarArchive tarArchive, string sourceDirectory, bool recurse)
        {
            var filenames = Directory.GetFiles(sourceDirectory);
            foreach (var filename in filenames)
            {
                var tarEntry = TarEntry.CreateEntryFromFile(filename);
                tarEntry.Name = filename.Remove(0, tarArchive.RootPath.Length + 1);
                tarArchive.WriteEntry(tarEntry, true);
            }

            if (!recurse) return;

            var directories = Directory.GetDirectories(sourceDirectory);
            foreach (var directory in directories)
            {
                AddDirectoryFilesToTar(tarArchive, directory, true);
            }
        }
    }
}