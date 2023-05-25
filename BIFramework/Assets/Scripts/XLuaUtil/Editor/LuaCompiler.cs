using System;
using System.Diagnostics;
using System.IO;
using BIFramework.Security.Cryptography;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace BIFramework.XLua.Editors
{
    public class LuaCompiler
    {
        private string command;
        private IEncryptor encryptor;

        public LuaCompiler(string command) : this(command, null)
        {
        }

        public LuaCompiler(string command, IEncryptor encryptor)
        {
            this.command = command;
            this.encryptor = encryptor;
        }

        public void Compile(string inputFilename, string outputFilename)
        {
            Compile(new FileInfo(inputFilename), new FileInfo(outputFilename));
        }

        public void Compile(FileInfo inputFile, FileInfo outputFile)
        {
            if (!inputFile.Exists)
            {
                UnityEngine.Debug.LogErrorFormat("Not found the file \"{0}\"", inputFile.FullName);
                return;
            }

            if (!outputFile.Directory.Exists)
                outputFile.Directory.Create();

            RunCMD(command, $" -o \"{outputFile.FullName}\" \"{inputFile.FullName}\"");

            if (outputFile.Exists)
            {
                byte[] buffer = File.ReadAllBytes(outputFile.FullName);
                if (encryptor != null) buffer = encryptor.Encrypt(buffer);
                File.WriteAllBytes(outputFile.FullName, buffer);
            }
        }

        public void Copy(FileInfo inputFile, FileInfo outputFile)
        {
            if (!inputFile.Exists)
            {
                UnityEngine.Debug.LogErrorFormat("Not found the file \"{0}\"", inputFile.FullName);
                return;
            }

            if (!outputFile.Directory.Exists)
                outputFile.Directory.Create();

            byte[] buffer = File.ReadAllBytes(inputFile.FullName);
            if (encryptor != null)
                buffer = encryptor.Encrypt(buffer);

            File.WriteAllBytes(outputFile.FullName, buffer);
        }

        public static void RunCMD(string command, string args)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = command;
                start.Arguments = args;

                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;

                start.CreateNoWindow = true;
                start.ErrorDialog = true;
                start.UseShellExecute = false;

                Process process = Process.Start(start);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();
                process.Close();

                if (!string.IsNullOrEmpty(output))
                    UnityEngine.Debug.Log(output);

                if (!string.IsNullOrEmpty(error))
                    UnityEngine.Debug.LogError(error);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }
    }

    // public class LuaCPostprocessor : AssetPostprocessor {
    //     private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
    //         var luaCompiler = new LuaCompiler(Path.GetFullPath("Assets/Scripts/XLuaUtil/Editor/luac.exe"));
    //         if (importedAssets.Length > 0) {
    //             foreach (var asset in importedAssets) {
    //                 if (asset.Contains("Assets/LuaCode") && asset.Contains(".lua.txt")) {
    //                     var fullPath = Path.GetFullPath(asset);
    //                     var outPath = fullPath.Replace("Assets\\LuaCode", "Assets\\LuaBytes");
    //                     outPath = outPath.Replace(".lua.txt", ".bytes");
    //                     luaCompiler.Compile(fullPath, outPath);
    //                 }
    //             }
    //             AssetDatabase.Refresh();
    //         }
    //     }
    // }
}
