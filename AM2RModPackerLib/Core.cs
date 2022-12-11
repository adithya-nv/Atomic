﻿using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using AM2RModPackerLib.XML;

namespace AM2RModPackerLib;

public enum ProfileOperatingSystems
{
    Windows,
    Linux,
    Mac,
    Android
}

/// <summary>
/// An enum, that has possible return codes for <see cref="Core.CheckIfZipIsAM2R11"/>.
/// </summary>
public enum IsZipAM2R11ReturnCodes
{
    Successful,
    MissingOrInvalidAM2RExe,
    MissingOrInvalidD3DX943Dll,
    MissingOrInvalidDataWin,
    GameIsInASubfolder
}

public static class Core
{
    public const string Version = "2.0.3";
    private static readonly string[] DATAFILES_BLACKLIST = { "data.win", "AM2R.exe", "D3DX9_43.dll", "game.unx", "game.ios" };
    private static readonly string localPath = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
    
    // TODO: go over thhis and clean
    public static void CreateModPack(ModCreationInfo modInfo, string output)
    {
        ProfileOperatingSystems profileOS = Enum.Parse<ProfileOperatingSystems>(modInfo.Profile.OperatingSystem);
        string modZipPath = profileOS switch
        {
            ProfileOperatingSystems.Windows => modInfo.WindowsModPath,
            ProfileOperatingSystems.Linux => modInfo.LinuxModPath,
            ProfileOperatingSystems.Mac => modInfo.MacModPath,
            _ => throw new NotSupportedException("The current Operating system is not supported!")
        };
        
        // Cleanup in case of previous errors
        if (Directory.Exists(Path.GetTempPath() + "/AM2RModPacker"))
            Directory.Delete(Path.GetTempPath() + "/AM2RModPacker", true);

        // Create temp work folders
        string tempPath = Directory.CreateDirectory(Path.GetTempPath() + "/AM2RModPacker").FullName;
        string tempOriginalPath = Directory.CreateDirectory(tempPath + "/original").FullName;
        string tempModPath = Directory.CreateDirectory(tempPath + "/mod").FullName;
        string tempProfilePath = Directory.CreateDirectory(tempPath + "/profile").FullName;

        // Extract 1.1 and modded AM2R to their own directories in temp work
        ZipFile.ExtractToDirectory(modInfo.AM2R11Path, tempOriginalPath);
        ZipFile.ExtractToDirectory(modZipPath, tempModPath);

        if (Directory.Exists(tempModPath + "/AM2R"))
            tempModPath += "/AM2R";
        
        switch (profileOS)
        {
            // Create AM2R.exe and data.win patches
            case ProfileOperatingSystems.Windows:
            {
                if (modInfo.Profile.UsesYYC)
                    CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/AM2R.exe", tempProfilePath + "/AM2R.xdelta");
                else
                {
                    CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/data.win", tempProfilePath + "/data.xdelta");
                    CreatePatch(tempOriginalPath + "/AM2R.exe", tempModPath + "/AM2R.exe", tempProfilePath + "/AM2R.xdelta");
                }
                break;
            }
            case ProfileOperatingSystems.Linux:
            {
                string runnerName = File.Exists(tempModPath + "/" + "AM2R") ? "AM2R" : "runner";
                CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/assets/game.unx", tempProfilePath + "/game.xdelta");
                CreatePatch(tempOriginalPath + "/AM2R.exe", tempModPath + "/" + runnerName, tempProfilePath + "/AM2R.xdelta");
                break;
            }
            case ProfileOperatingSystems.Mac:
            {
                CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/AM2R.app/Contents/Resources/game.ios", tempProfilePath + "/game.xdelta");
                CreatePatch(tempOriginalPath + "/AM2R.exe", tempModPath + "/AM2R.app/Contents/MacOS/Mac_Runner", tempProfilePath + "/AM2R.xdelta");

                // Copy plist over for custom title name
                File.Copy(tempModPath + "/AM2R.app/Contents/Info.plist", tempProfilePath + "/Info.plist");
                break;
            }
        }
        
        // Create game.droid patch and wrapper if Android is supported
        if (modInfo.Profile.SupportsAndroid)
        {
            string tempAndroid = Directory.CreateDirectory(tempPath + "/android").FullName;

            // Extract APK 
            // - java -jar apktool.jar d "%~dp0AM2RWrapper_old.apk"

            // Process startInfo
            string filename = OS.IsWindows ? "cmd.exe" : "java";
            string javaArgs = OS.IsWindows ? "/C java -jar" : "-jar";
            var procStartInfo = new ProcessStartInfo
            {
                FileName = filename,
                WorkingDirectory = tempAndroid,
                Arguments = $"{javaArgs} \"" + localPath + "/utilities/android/apktool.jar\" d -f -o \"" + tempAndroid + "\" \"" + modInfo.ApkModPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Run process
            using (var proc = new Process { StartInfo = procStartInfo })
            {
                proc.Start();
                proc.WaitForExit();
            }

            // Create game.droid patch
            CreatePatch(tempOriginalPath + "/data.win", tempAndroid + "/assets/game.droid", tempProfilePath + "/droid.xdelta");

            // Delete excess files in APK
            // Create whitelist
            string[] whitelist = { "splash.png", "portrait_splash.png" };

            // Get directory
            var androidAssets = new DirectoryInfo(tempAndroid + "/assets");


            // Delete files
            foreach (var file in androidAssets.GetFiles())
            {
                if (file.Name.EndsWith(".ini") && file.Name != "modifiers.ini")
                {
                    if (File.Exists(tempProfilePath + "/AM2R.ini"))
                        // This shouldn't be a problem... normally...
                        File.Delete(tempProfilePath + "/AM2R.ini");
                    File.Copy(file.FullName, tempProfilePath + "/AM2R.ini");
                }

                if (!whitelist.Contains(file.Name))
                    File.Delete(file.FullName);
            }

            foreach (var dir in androidAssets.GetDirectories())
                Directory.Delete(dir.FullName, true);

            // Create wrapper

            // Process startInfo
            // - java -jar apktool.jar b "%~dp0AM2RWrapper_old" -o "%~dp0AM2RWrapper.apk"
            var procStartInfo2 = new ProcessStartInfo
            {
                FileName = filename,
                WorkingDirectory = tempAndroid,
                Arguments = $"{javaArgs} \"" + localPath + "/utilities/android/apktool.jar\" b -f \"" + tempAndroid + "\" -o \"" + tempProfilePath + "/AM2RWrapper.apk\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Run process
            using (var proc = new Process { StartInfo = procStartInfo2 })
            {
                proc.Start();
                proc.WaitForExit();
            }

            string tempAndroidProfilePath = tempProfilePath + "/android";
            Directory.CreateDirectory(tempAndroidProfilePath);

            File.Move(tempProfilePath + "/AM2RWrapper.apk", tempAndroidProfilePath + "/AM2RWrapper.apk");
            if (File.Exists(tempProfilePath + "/AM2R.ini"))
                File.Move(tempProfilePath + "/AM2R.ini", tempAndroidProfilePath + "/AM2R.ini");
        }

        // Copy datafiles (exclude .ogg if custom music is not selected)
        var dirInfo = new DirectoryInfo(tempModPath);
        if (profileOS == ProfileOperatingSystems.Linux)
            dirInfo = new DirectoryInfo(tempModPath + "/assets");
        else if (profileOS == ProfileOperatingSystems.Mac)
            dirInfo = new DirectoryInfo(tempModPath + "/AM2R.app/Contents/Resources");

        Directory.CreateDirectory(tempProfilePath + "/files_to_copy");

        if (modInfo.Profile.UsesCustomMusic)
        {
            // Copy files, excluding the blacklist
            CopyFilesRecursive(dirInfo, DATAFILES_BLACKLIST, tempProfilePath + "/files_to_copy");
        }
        else
        {
            // Get list of 1.1's music files
            string[] musFiles = Directory.GetFiles(tempOriginalPath, "*.ogg").Select(file => Path.GetFileName(file)).ToArray();

            if (profileOS == ProfileOperatingSystems.Linux || profileOS == ProfileOperatingSystems.Mac)
                musFiles = Directory.GetFiles(tempOriginalPath, "*.ogg").Select(file => Path.GetFileName(file).ToLower()).ToArray();


            // Combine musFiles with the known datafiles for a blacklist
            string[] blacklist = musFiles.Concat(DATAFILES_BLACKLIST).ToArray();

            // Copy files, excluding the blacklist
            CopyFilesRecursive(dirInfo, blacklist, tempProfilePath + "/files_to_copy");
        }

        // Export profile as XML
        string xmlOutput = Serializer.Serialize<ModProfileXML>(modInfo.Profile);
        File.WriteAllText(tempProfilePath + "/profile.xml", xmlOutput);

        // Compress temp folder to .zip
        if (File.Exists(output))
            File.Delete(output);

        ZipFile.CreateFromDirectory(tempProfilePath, output);

        // Delete temp folder
        Directory.Delete(tempPath, true);
    }

    
    public static void CreatePatch(string original, string modified, string output)
    {
        // Specify process start info
        var parameters = new ProcessStartInfo
        {
            FileName = OS.IsWindows ? localPath + "/utilities/xdelta/xdelta3.exe" : "xdelta3",
            WorkingDirectory = localPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "-f -e -s \"" + original + "\" \"" + modified + "\" \"" + output + "\""
        };

        // Launch process and wait for exit.
        try
        {
            using var proc = new Process { StartInfo = parameters };
            proc.Start();
            proc.WaitForExit();
        }
        catch (Win32Exception)
        {
            throw new Exception("Xdelta3 could not be found! For Windows, make sure that the utilities folder exists, for other OS make sure it is installed and in PATH.");
        }
    }
    
    // Taken from AM2RLauncher
    /// <summary>
    /// Checks if a Zip file is a valid AM2R_1.1 zip.
    /// </summary>
    /// <param name="zipPath">Full Path to the Zip file to validate.</param>
    /// <returns><see cref="IsZipAM2R11ReturnCodes"/> detailing the result</returns>
    public static IsZipAM2R11ReturnCodes CheckIfZipIsAM2R11(string zipPath)
    {
        const string d3dHash = "86e39e9161c3d930d93822f1563c280d";
        const string dataWinHash = "f2b84fe5ba64cb64e284be1066ca08ee";
        const string am2rHash = "15253f7a66d6ea3feef004ebbee9b438";
        string tmpPath = Path.GetTempPath() + "/" + Path.GetFileNameWithoutExtension(zipPath);

        // Clean up in case folder exists already
        if (Directory.Exists(tmpPath))
            Directory.Delete(tmpPath, true);
        Directory.CreateDirectory(tmpPath);

        // Open archive
        ZipArchive am2rZip = ZipFile.OpenRead(zipPath);


        // Check if exe exists anywhere
        ZipArchiveEntry am2rExe = am2rZip.Entries.FirstOrDefault(x => x.FullName.Contains("AM2R.exe"));
        if (am2rExe == null)
            return IsZipAM2R11ReturnCodes.MissingOrInvalidAM2RExe;

        // Check if it's not in a subfolder. if it'd be in a subfolder, fullname would be "folder/AM2R.exe"
        if (am2rExe.FullName != "AM2R.exe")
            return IsZipAM2R11ReturnCodes.GameIsInASubfolder;

        // Check validity
        am2rExe.ExtractToFile($"{tmpPath}/{am2rExe.FullName}");
        if (CalculateMD5($"{tmpPath}/{am2rExe.FullName}") != am2rHash)
            return IsZipAM2R11ReturnCodes.MissingOrInvalidAM2RExe;


        // Check if data.win exists / is valid
        ZipArchiveEntry dataWin = am2rZip.Entries.FirstOrDefault(x => x.FullName == "data.win");
        if (dataWin == null)
            return IsZipAM2R11ReturnCodes.MissingOrInvalidDataWin;

        dataWin.ExtractToFile($"{tmpPath}/{dataWin.FullName}");
        if (CalculateMD5($"{tmpPath}/{dataWin.FullName}") != dataWinHash)
            return IsZipAM2R11ReturnCodes.MissingOrInvalidDataWin;


        // Check if d3d.dll exists / is valid
        ZipArchiveEntry d3dx = am2rZip.Entries.FirstOrDefault(x => x.FullName == "D3DX9_43.dll");
        if (d3dx == null)
            return IsZipAM2R11ReturnCodes.MissingOrInvalidD3DX943Dll;

        d3dx.ExtractToFile($"{tmpPath}/{d3dx.FullName}");
        if (CalculateMD5($"{tmpPath}/{d3dx.FullName}") != d3dHash)
            return IsZipAM2R11ReturnCodes.MissingOrInvalidD3DX943Dll;


        // Clean up
        Directory.Delete(tmpPath, true);

        // If we didn't exit before, everything is fine
        return IsZipAM2R11ReturnCodes.Successful;
    }
    
    public static string CalculateMD5(string filename)
    {
        using var stream = File.OpenRead(filename);
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
    
    public static void CopyFilesRecursive(DirectoryInfo source, string[] blacklist, string destination)
    {
        foreach (var file in source.GetFiles())
        {
            if (!blacklist.Contains(file.Name))
                file.CopyTo(destination + "/" + file.Name);
        }

        foreach (var dir in source.GetDirectories())
        {
            // Folders need to be lowercase, because GM only reads from lowercase names on *nix systems. Windows is case-insensitive so doesnt matter for them
            string newDir = Directory.CreateDirectory(destination + "/" + dir.Name.ToLower()).FullName;
            CopyFilesRecursive(dir, blacklist, newDir);
        }
    }
}