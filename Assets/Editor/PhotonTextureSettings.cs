using UnityEditor;
using UnityEngine;

public sealed class PhotonTextureSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Photon/Textures/")) return;
        var texture = (TextureImporter)assetImporter;
        bool sky = System.IO.Path.GetFileName(assetPath).StartsWith("sky-");
        texture.wrapMode = sky ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        texture.maxTextureSize = sky ? 4096 : 2048;
        texture.anisoLevel = 4;
    }
}
