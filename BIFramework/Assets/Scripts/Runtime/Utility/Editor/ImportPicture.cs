using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class ImportPicture : AssetPostprocessor {
    // private bool ignoreProcessor = true; //是否忽略导入压缩设置
    // private bool ignoreFirst = false; //是否忽略改变设置'/
    //
    // void OnPreprocessTexture() {
    //     if (ignoreProcessor) return;
    //     var importer = (TextureImporter) assetImporter;
    //     var normalType = importer.textureType == TextureImporterType.NormalMap;
    //     if (importer != null && (importer.textureType == TextureImporterType.Default || normalType ||
    //                              importer.textureType == TextureImporterType.Sprite)) {
    //         if (importer.textureShape == TextureImporterShape.TextureCube) return;
    //         if (ignoreFirst || IsFirstImport(importer)) {
    //             if (importer.assetPath.IndexOf("TerrainData") != -1) {
    //                 // var settings = importer.GetDefaultPlatformTextureSettings();
    //                 // settings.crunchedCompression = false;
    //                 // settings.compressionQuality = 100;
    //                 // settings.textureCompression = TextureImporterCompression.Compressed;
    //                 // settings.format = TextureImporterFormat.Automatic;
    //                 // importer.SetPlatformTextureSettings(settings);
    //                 //
    //                 // settings = importer.GetPlatformTextureSettings("Android");
    //                 // settings.overridden = false;
    //                 // importer.SetPlatformTextureSettings(settings);
    //                 //
    //                 // settings = importer.GetPlatformTextureSettings("iPhone");
    //                 // settings.overridden = false;
    //                 // importer.SetPlatformTextureSettings(settings);
    //             }
    //             else
    //             {
    //                 var settings = importer.GetDefaultPlatformTextureSettings();
    //                 settings.crunchedCompression = false;
    //                 settings.compressionQuality = 100;
    //                 settings.textureCompression = TextureImporterCompression.Compressed;
    //                 settings.format = TextureImporterFormat.Automatic;
    //                 importer.SetPlatformTextureSettings(settings);
    //
    //                 settings = importer.GetPlatformTextureSettings("Android");
    //                 settings.overridden = true;
    //                 settings.format = TextureImporterFormat.ASTC_6x6;
    //                 settings.crunchedCompression = false;
    //                 settings.compressionQuality = 100;
    //                 settings.textureCompression = TextureImporterCompression.Compressed;
    //                 importer.SetPlatformTextureSettings(settings);
    //
    //                 settings = importer.GetPlatformTextureSettings("iPhone");
    //                 settings.overridden = true;
    //                 settings.format = TextureImporterFormat.ASTC_6x6;
    //                 settings.crunchedCompression = false;
    //                 settings.compressionQuality = 100;
    //                 settings.textureCompression = TextureImporterCompression.Compressed;
    //                 importer.SetPlatformTextureSettings(settings);
    //             }
    //         }
    //     }
    // }

    //被4整除
    bool IsDivisibleOf4(TextureImporter importer) {
        (int width, int height) = GetTextureImporterSize(importer);
        return (width % 4 == 0 && height % 4 == 0);
    }

    //2的整数次幂
    bool IsPowerOfTwo(TextureImporter importer) {
        (int width, int height) = GetTextureImporterSize(importer);
        return (width == height) && (width > 0) && ((width & (width - 1)) == 0);
    }

    //贴图不存在、meta文件不存在、图片尺寸发生修改需要重新导入
    bool IsFirstImport(TextureImporter importer) {
        (int width, int height) = GetTextureImporterSize(importer);
        Texture tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        bool hasMeta = File.Exists(AssetDatabase.GetAssetPathFromTextMetaFilePath(assetPath));
        return tex == null || !hasMeta || (tex.width != width && tex.height != height);
    }

    //获取导入图片的宽高
    (int, int) GetTextureImporterSize(TextureImporter importer) {
        if (importer != null) {
            object[] args = new object[2];
            MethodInfo mi =
                typeof(TextureImporter).GetMethod("GetWidthAndHeight", BindingFlags.NonPublic | BindingFlags.Instance);
            mi.Invoke(importer, args);
            return ((int) args[0], (int) args[1]);
        }

        return (0, 0);
    }
}